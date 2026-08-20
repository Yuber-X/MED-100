using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>Acceso a usuario + sus permisos efectivos (multiusuario con roles).</summary>
public class UsuarioRepository
{
    private readonly ConexionFactory _factory;

    public UsuarioRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>True si ya existe al menos un usuario (decide wizard inicial vs login).</summary>
    public async Task<bool> ExisteAlgunUsuarioAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {DbNames.Usuario};";
        var total = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        return total > 0;
    }

    public async Task<Usuario?> ObtenerPorUsernameAsync(string username, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT u.id, u.username, u.password_hash, u.nombre, u.apellido,
                   u.rol_id, r.nombre AS rol_nombre, u.activo, u.created_at, u.last_login_at
            FROM {DbNames.Usuario} u
            LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
            WHERE u.username = @username AND u.activo = 1;
            """;
        cmd.Parameters.AddWithValue("@username", username);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    private static Usuario Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Username = reader.GetString("username"),
        PasswordHash = reader.GetString("password_hash"),
        Nombre = reader.GetString("nombre"),
        Apellido = reader.IsDBNull(reader.GetOrdinal("apellido")) ? null : reader.GetString("apellido"),
        RolId = reader.IsDBNull(reader.GetOrdinal("rol_id")) ? null : reader.GetInt32("rol_id"),
        RolNombre = reader.IsDBNull(reader.GetOrdinal("rol_nombre")) ? null : reader.GetString("rol_nombre"),
        Activo = reader.GetBoolean("activo"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        LastLoginAtUtc = reader.IsDBNull(reader.GetOrdinal("last_login_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("last_login_at"), DateTimeKind.Utc)
    };

    /// <summary>
    /// Permisos efectivos del usuario (tabla usuario_permiso, que los triggers
    /// sincronizan con el rol y admite overrides individuales).
    /// </summary>
    public async Task<List<string>> ObtenerPermisosAsync(long usuarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.codigo
            FROM {DbNames.UsuarioPermiso} up
            JOIN {DbNames.Permiso} p ON p.id = up.permiso_id
            WHERE up.usuario_id = @usuarioId;
            """;
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);

        var permisos = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            permisos.Add(reader.GetString("codigo"));
        return permisos;
    }

    /// <summary>
    /// Crea un usuario. El trigger trg_usuario_after_insert asigna los permisos
    /// del rol automáticamente.
    /// </summary>
    public async Task<long> CrearAsync(string username, string passwordHash, string nombre,
        string? apellido, int rolId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Usuario} (username, password_hash, nombre, apellido, rol_id)
            VALUES (@username, @passwordHash, @nombre, @apellido, @rolId);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@apellido", (object?)apellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rolId", rolId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<int> ObtenerRolIdPorNombreAsync(string nombreRol, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id FROM {DbNames.Rol} WHERE nombre = @nombre;";
        cmd.Parameters.AddWithValue("@nombre", nombreRol);
        var id = await cmd.ExecuteScalarAsync(ct)
            ?? throw new InvalidOperationException(
                $"No existe el rol '{nombreRol}'. ¿Se ejecutó 002_seed_data.sql?");
        return Convert.ToInt32(id);
    }

    // ------------------------------------------------------------------
    // Admin de Usuarios (solo Admin)
    // ------------------------------------------------------------------

    public async Task<List<Usuario>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT u.id, u.username, u.password_hash, u.nombre, u.apellido,
                   u.rol_id, r.nombre AS rol_nombre, u.activo, u.created_at, u.last_login_at
            FROM {DbNames.Usuario} u
            LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
            ORDER BY u.activo DESC, u.nombre;
            """;

        var lista = new List<Usuario>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    public async Task<Usuario?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT u.id, u.username, u.password_hash, u.nombre, u.apellido,
                   u.rol_id, r.nombre AS rol_nombre, u.activo, u.created_at, u.last_login_at
            FROM {DbNames.Usuario} u
            LEFT JOIN {DbNames.Rol} r ON r.id = u.rol_id
            WHERE u.id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<List<Rol>> ObtenerRolesAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id, nombre, descripcion FROM {DbNames.Rol} ORDER BY id;";

        var lista = new List<Rol>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new Rol
            {
                Id = reader.GetInt32("id"),
                Nombre = reader.GetString("nombre"),
                Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion"))
                    ? null : reader.GetString("descripcion")
            });
        return lista;
    }

    /// <summary>Catálogo completo de permisos (para las casillas de la pantalla).</summary>
    public async Task<List<Permiso>> ObtenerCatalogoPermisosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT id, codigo, nombre, descripcion FROM {DbNames.Permiso} ORDER BY id;";

        var lista = new List<Permiso>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new Permiso
            {
                Id = reader.GetInt32("id"),
                Codigo = reader.GetString("codigo"),
                Nombre = reader.GetString("nombre"),
                Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion"))
                    ? null : reader.GetString("descripcion")
            });
        return lista;
    }

    /// <summary>Permisos que un rol otorga por defecto (los que asignan los triggers).</summary>
    public async Task<List<string>> ObtenerPermisosDeRolAsync(int rolId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.codigo
            FROM {DbNames.RolPermiso} rp
            JOIN {DbNames.Permiso} p ON p.id = rp.permiso_id
            WHERE rp.rol_id = @rolId;
            """;
        cmd.Parameters.AddWithValue("@rolId", rolId);

        var lista = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(reader.GetString("codigo"));
        return lista;
    }

    public async Task<bool> ExisteUsernameAsync(string username, long? exceptoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Usuario}
            WHERE username = @username AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>
    /// Actualiza datos y rol. Si el rol cambia, el trigger
    /// trg_usuario_after_update RESINCRONIZA los permisos con los del rol nuevo
    /// (los overrides individuales previos se pierden — es lo esperado).
    /// </summary>
    public async Task ActualizarAsync(long id, string username, string nombre, string? apellido,
        int rolId, bool activo, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Usuario}
            SET username = @username, nombre = @nombre, apellido = @apellido,
                rol_id = @rolId, activo = @activo, updated_at = UTC_TIMESTAMP()
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@username", username);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@apellido", (object?)apellido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rolId", rolId);
        cmd.Parameters.AddWithValue("@activo", activo);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reemplaza los permisos efectivos del usuario (overrides desde la pantalla
    /// de Admin de Usuarios). Se hace en transacción: borrar + insertar.
    /// </summary>
    public async Task GuardarPermisosAsync(long usuarioId, IReadOnlyList<string> codigos,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            using (var borrar = conexion.CreateCommand())
            {
                borrar.Transaction = transaccion;
                borrar.CommandText =
                    $"DELETE FROM {DbNames.UsuarioPermiso} WHERE usuario_id = @usuarioId;";
                borrar.Parameters.AddWithValue("@usuarioId", usuarioId);
                await borrar.ExecuteNonQueryAsync(ct);
            }

            foreach (var codigo in codigos.Distinct())
            {
                using var insertar = conexion.CreateCommand();
                insertar.Transaction = transaccion;
                insertar.CommandText = $"""
                    INSERT INTO {DbNames.UsuarioPermiso} (usuario_id, permiso_id)
                    SELECT @usuarioId, id FROM {DbNames.Permiso} WHERE codigo = @codigo;
                    """;
                insertar.Parameters.AddWithValue("@usuarioId", usuarioId);
                insertar.Parameters.AddWithValue("@codigo", codigo);
                await insertar.ExecuteNonQueryAsync(ct);
            }

            await transaccion.CommitAsync(ct);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task ActualizarUltimoLoginAsync(long usuarioId, DateTime loginAtUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Usuario} SET last_login_at = @loginAt WHERE id = @id;";
        cmd.Parameters.AddWithValue("@loginAt", loginAtUtc);
        cmd.Parameters.AddWithValue("@id", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CambiarPasswordAsync(long usuarioId, string nuevoHash, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Usuario} SET password_hash = @hash, updated_at = UTC_TIMESTAMP() WHERE id = @id;";
        cmd.Parameters.AddWithValue("@hash", nuevoHash);
        cmd.Parameters.AddWithValue("@id", usuarioId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
