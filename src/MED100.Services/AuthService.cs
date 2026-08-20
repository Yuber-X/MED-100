using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>Resultado de un intento de login.</summary>
public enum ResultadoLogin
{
    Exitoso,
    CredencialesInvalidas,
    BloqueadoTemporalmente
}

/// <summary>
/// Autenticación multiusuario con BCrypt (cost 12) y rate-limiting:
/// 5 intentos fallidos → bloqueo temporal de 5 minutos.
/// Al login se cargan el rol y los permisos efectivos en SesionActual;
/// la UI y los services validan contra esos permisos.
/// </summary>
public class AuthService
{
    public const int MinLargoPassword = 8;
    private const int CostBcrypt = 12;
    private const int MaxIntentosFallidos = 5;
    private const string RolAdmin = "Admin";
    private static readonly TimeSpan DuracionBloqueo = TimeSpan.FromMinutes(5);

    private readonly UsuarioRepository _usuarios;
    private readonly SesionRepository _sesiones;
    private readonly AuditoriaService _auditoria;

    private int _intentosFallidos;
    private DateTime? _bloqueadoHastaUtc;

    public AuthService(UsuarioRepository usuarios, SesionRepository sesiones, AuditoriaService auditoria)
    {
        _usuarios = usuarios;
        _sesiones = sesiones;
        _auditoria = auditoria;
    }

    /// <summary>True si aún no existe ningún usuario (primer arranque → wizard).</summary>
    public async Task<bool> RequiereCuentaInicialAsync(CancellationToken ct = default) =>
        !await _usuarios.ExisteAlgunUsuarioAsync(ct);

    /// <summary>
    /// Crea la PRIMERA cuenta del sistema desde el wizard: siempre con rol Admin
    /// (alguien tiene que poder configurar y crear a los demás empleados).
    /// </summary>
    public async Task<long> CrearCuentaInicialAsync(string username, string nombre, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("El nombre de usuario es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new ArgumentException("El nombre es obligatorio.");
        ValidarPassword(password);

        if (await _usuarios.ExisteAlgunUsuarioAsync(ct))
            throw new InvalidOperationException(
                "Ya existe una cuenta inicial. Los demás usuarios se crean desde Admin de Usuarios.");

        var rolAdminId = await _usuarios.ObtenerRolIdPorNombreAsync(RolAdmin, ct);
        var hash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: CostBcrypt);
        return await _usuarios.CrearAsync(username.Trim(), hash, nombre.Trim(),
            apellido: null, rolAdminId, ct);
    }

    /// <summary>
    /// Valida credenciales y, si son correctas: registra la sesión en BD,
    /// actualiza last_login_at, carga rol + permisos en SesionActual y audita.
    /// </summary>
    public async Task<ResultadoLogin> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (_bloqueadoHastaUtc is { } bloqueo && DateTime.UtcNow < bloqueo)
            return ResultadoLogin.BloqueadoTemporalmente;

        var usuario = await _usuarios.ObtenerPorUsernameAsync(username.Trim(), ct);
        if (usuario is null || !BCrypt.Net.BCrypt.Verify(password, usuario.PasswordHash))
        {
            RegistrarIntentoFallido();
            return ResultadoLogin.CredencialesInvalidas;
        }

        _intentosFallidos = 0;
        _bloqueadoHastaUtc = null;

        var permisos = await _usuarios.ObtenerPermisosAsync(usuario.Id, ct);
        var ahoraUtc = DateTime.UtcNow;
        var sesionId = await _sesiones.RegistrarLoginAsync(usuario.Id, ahoraUtc, ipLocal: null, ct);
        await _usuarios.ActualizarUltimoLoginAsync(usuario.Id, ahoraUtc, ct);

        SesionActual.Iniciar(usuario.Id, usuario.Username, usuario.NombreCompleto,
            usuario.RolNombre ?? string.Empty, permisos, ahoraUtc, sesionId);
        await _auditoria.RegistrarAsync(AccionAuditoria.Login, DbNames.Usuario, usuario.Id,
            $"Login de {usuario.Username} ({usuario.RolNombre})", ct);

        return ResultadoLogin.Exitoso;
    }

    /// <summary>Cierra la sesión: registra logout en BD, audita y limpia SesionActual.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        if (!SesionActual.HaySesionActiva)
            return;

        await _auditoria.RegistrarAsync(AccionAuditoria.Logout, DbNames.Usuario, SesionActual.Id,
            $"Logout de {SesionActual.Username}", ct);
        await _sesiones.RegistrarLogoutAsync(SesionActual.SesionId, DateTime.UtcNow, ct);
        SesionActual.Cerrar();
    }

    /// <summary>Cambio de la PROPIA contraseña: exige la contraseña actual.</summary>
    public async Task CambiarPasswordAsync(string passwordActual, string passwordNueva, CancellationToken ct = default)
    {
        if (!SesionActual.HaySesionActiva)
            throw new InvalidOperationException("No hay sesión activa.");
        ValidarPassword(passwordNueva);

        var usuario = await _usuarios.ObtenerPorUsernameAsync(SesionActual.Username, ct)
            ?? throw new InvalidOperationException("Usuario no encontrado.");

        if (!BCrypt.Net.BCrypt.Verify(passwordActual, usuario.PasswordHash))
            throw new InvalidOperationException("La contraseña actual no es correcta.");

        var nuevoHash = BCrypt.Net.BCrypt.HashPassword(passwordNueva, workFactor: CostBcrypt);
        await _usuarios.CambiarPasswordAsync(usuario.Id, nuevoHash, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Usuario, usuario.Id,
            "Cambio de contraseña", ct);
    }

    private static void ValidarPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLargoPassword)
            throw new ArgumentException($"La contraseña debe tener al menos {MinLargoPassword} caracteres.");
    }

    private void RegistrarIntentoFallido()
    {
        _intentosFallidos++;
        if (_intentosFallidos >= MaxIntentosFallidos)
        {
            _bloqueadoHastaUtc = DateTime.UtcNow.Add(DuracionBloqueo);
            _intentosFallidos = 0;
        }
    }
}
