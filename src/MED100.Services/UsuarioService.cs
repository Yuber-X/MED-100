using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Administración de usuarios. Regla de Yuber (2026-07-12):
/// **SOLO el Admin** (permiso 'usuarios') puede crear empleados, cambiarles la
/// contraseña y ajustar sus permisos. Nadie más, ni siquiera el Supervisor.
///
/// Permisos por defecto: al asignar un rol, los triggers de la BD copian los
/// permisos de ese rol al usuario (patrón POS-400). Desde la pantalla, el Admin
/// puede además marcar/desmarcar permisos individuales (overrides) — por ejemplo
/// quitarle a Servicio la capacidad de editar pacientes, o darle a un Cajero
/// acceso al cuadre de todos.
/// </summary>
public class UsuarioService
{
    private const int CostBcrypt = 12;
    public const int MinLargoPassword = 8;

    private readonly UsuarioRepository _usuarios;
    private readonly AuditoriaService _auditoria;

    public UsuarioService(UsuarioRepository usuarios, AuditoriaService auditoria)
    {
        _usuarios = usuarios;
        _auditoria = auditoria;
    }

    /// <summary>Todo lo de esta pantalla exige ser Admin.</summary>
    private static void ExigirAdmin()
    {
        if (!SesionActual.TienePermiso("usuarios"))
            throw new InvalidOperationException(
                "Solo el Administrador puede gestionar usuarios.");
    }

    public Task<List<Usuario>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        ExigirAdmin();
        return _usuarios.ObtenerTodosAsync(ct);
    }

    public Task<List<Rol>> ObtenerRolesAsync(CancellationToken ct = default) =>
        _usuarios.ObtenerRolesAsync(ct);

    public Task<List<Permiso>> ObtenerCatalogoPermisosAsync(CancellationToken ct = default) =>
        _usuarios.ObtenerCatalogoPermisosAsync(ct);

    public Task<List<string>> ObtenerPermisosAsync(long usuarioId, CancellationToken ct = default) =>
        _usuarios.ObtenerPermisosAsync(usuarioId, ct);

    /// <summary>Permisos que el rol otorga por defecto (para el botón "restablecer").</summary>
    public Task<List<string>> ObtenerPermisosDeRolAsync(int rolId, CancellationToken ct = default) =>
        _usuarios.ObtenerPermisosDeRolAsync(rolId, ct);

    /// <summary>
    /// Crea un empleado. El trigger le asigna automáticamente los permisos de
    /// su rol; después el Admin puede afinarlos desde la misma pantalla.
    /// </summary>
    public async Task<long> CrearAsync(string username, string nombre, string? apellido,
        int rolId, string password, CancellationToken ct = default)
    {
        ExigirAdmin();
        ValidarDatos(username, nombre);
        ValidarPassword(password);

        if (await _usuarios.ExisteUsernameAsync(username.Trim(), null, ct))
            throw new ArgumentException($"Ya existe un usuario con el nombre \"{username.Trim()}\".");

        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: CostBcrypt);
        var id = await _usuarios.CrearAsync(username.Trim(), hash, nombre.Trim(),
            string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim(), rolId, ct);

        var rol = (await _usuarios.ObtenerRolesAsync(ct)).First(r => r.Id == rolId);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Usuario, id,
            $"Usuario creado: {username.Trim()} ({nombre.Trim()}) con rol {rol.Nombre}", ct);
        return id;
    }

    /// <summary>
    /// Actualiza datos y rol. OJO: si el rol cambia, los triggers resincronizan
    /// los permisos con los del rol nuevo (se pierden los overrides anteriores).
    /// </summary>
    public async Task ActualizarAsync(long id, string username, string nombre, string? apellido,
        int rolId, bool activo, CancellationToken ct = default)
    {
        ExigirAdmin();
        ValidarDatos(username, nombre);

        var anterior = await _usuarios.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");

        if (await _usuarios.ExisteUsernameAsync(username.Trim(), id, ct))
            throw new ArgumentException($"Ya existe otro usuario con el nombre \"{username.Trim()}\".");

        // Un Admin no puede desactivarse a sí mismo y quedar fuera del sistema
        if (id == SesionActual.Id && !activo)
            throw new ArgumentException("No puedes desactivar tu propia cuenta.");

        await _usuarios.ActualizarAsync(id, username.Trim(), nombre.Trim(),
            string.IsNullOrWhiteSpace(apellido) ? null : apellido.Trim(), rolId, activo, ct);

        var detalle = $"Usuario modificado: {username.Trim()}";
        if (anterior.RolId != rolId)
        {
            var roles = await _usuarios.ObtenerRolesAsync(ct);
            var rolNuevo = roles.First(r => r.Id == rolId).Nombre;
            detalle += $" · rol {anterior.RolNombre ?? "(ninguno)"} → {rolNuevo} " +
                       "(permisos restablecidos a los del rol)";
        }
        if (anterior.Activo != activo)
            detalle += activo ? " · REACTIVADO" : " · DESACTIVADO";

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, id, detalle, ct);
    }

    /// <summary>
    /// Cambia la contraseña de CUALQUIER usuario. Solo el Admin (regla Yuber):
    /// no se pide la contraseña anterior, porque el empleado la olvidó.
    /// </summary>
    public async Task CambiarPasswordAsync(long usuarioId, string passwordNueva, CancellationToken ct = default)
    {
        ExigirAdmin();
        ValidarPassword(passwordNueva);

        var usuario = await _usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");

        var hash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: CostBcrypt);
        await _usuarios.CambiarPasswordAsync(usuarioId, hash, ct);

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, usuarioId,
            $"Contraseña restablecida por el Administrador para {usuario.Username}", ct);
    }

    /// <summary>
    /// Guarda los permisos efectivos del usuario (las casillas de la pantalla).
    /// Así el Admin limita quién puede editar/eliminar productos o clientes.
    /// </summary>
    public async Task GuardarPermisosAsync(long usuarioId, IReadOnlyList<string> codigos,
        CancellationToken ct = default)
    {
        ExigirAdmin();

        var usuario = await _usuarios.ObtenerPorIdAsync(usuarioId, ct)
            ?? throw new InvalidOperationException("El usuario no existe.");

        // Un Admin no puede quitarse a sí mismo el acceso a Usuarios/Configuración:
        // se quedaría sin forma de volver a entrar a administrar
        if (usuarioId == SesionActual.Id &&
            (!codigos.Contains("usuarios") || !codigos.Contains("configuracion")))
            throw new ArgumentException(
                "No puedes quitarte a ti mismo los permisos de Usuarios o Configuración: " +
                "quedarías sin poder administrar el sistema.");

        var antes = await _usuarios.ObtenerPermisosAsync(usuarioId, ct);
        await _usuarios.GuardarPermisosAsync(usuarioId, codigos, ct);

        var agregados = codigos.Except(antes).ToList();
        var quitados = antes.Except(codigos).ToList();
        if (agregados.Count == 0 && quitados.Count == 0)
            return;

        var detalle = $"Permisos de {usuario.Username}:";
        if (agregados.Count > 0) detalle += $" +[{string.Join(", ", agregados)}]";
        if (quitados.Count > 0) detalle += $" -[{string.Join(", ", quitados)}]";

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.UsuarioPermiso, usuarioId,
            detalle, ct);
    }

    private static void ValidarDatos(string username, string nombre)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El nombre de usuario es obligatorio.");
        if (username.Trim().Length < 3)
            throw new ArgumentException("El nombre de usuario debe tener al menos 3 caracteres.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre del empleado es obligatorio.");
    }

    private static void ValidarPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLargoPassword)
            throw new ArgumentException(
                $"La contraseña debe tener al menos {MinLargoPassword} caracteres.");
    }
}
