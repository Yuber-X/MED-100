using MySqlConnector;
using MED100.Common;

namespace MED100.Data;

/// <summary>Registro de logins/logouts en la tabla sesion.</summary>
public class SesionRepository
{
    private readonly ConexionFactory _factory;

    public SesionRepository(ConexionFactory factory) => _factory = factory;

    public async Task<long> RegistrarLoginAsync(long usuarioId, DateTime loginAtUtc, string? ipLocal, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Sesion} (usuario_id, login_at, ip_local)
            VALUES (@usuarioId, @loginAt, @ipLocal);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@loginAt", loginAtUtc);
        cmd.Parameters.AddWithValue("@ipLocal", (object?)ipLocal ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task RegistrarLogoutAsync(long sesionId, DateTime logoutAtUtc, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.Sesion} SET logout_at = @logoutAt WHERE id = @id AND logout_at IS NULL;";
        cmd.Parameters.AddWithValue("@logoutAt", logoutAtUtc);
        cmd.Parameters.AddWithValue("@id", sesionId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
