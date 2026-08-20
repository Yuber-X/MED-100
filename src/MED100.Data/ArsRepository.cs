using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Catálogo de aseguradoras. Sin soft delete: es un catálogo chico y las
/// facturas viejas apuntan por FK, así que lo que se hace es desactivarlas.
/// </summary>
public class ArsRepository
{
    private readonly ConexionFactory _factory;

    public ArsRepository(ConexionFactory factory) => _factory = factory;

    private const string ColumnasBase = "id, nombre, rnc, telefono, notas, activo";

    public async Task<List<Ars>> ObtenerTodasAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {ColumnasBase} FROM {DbNames.Ars} ORDER BY activo DESC, nombre;";
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<List<Ars>> ObtenerActivasAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {ColumnasBase} FROM {DbNames.Ars} WHERE activo = 1 ORDER BY nombre;";
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Ars?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {ColumnasBase} FROM {DbNames.Ars} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<bool> ExisteNombreAsync(string nombre, long? exceptoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Ars}
            WHERE nombre = @nombre AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<long> InsertarAsync(ArsDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Ars} (nombre, rnc, telefono, notas, activo)
            VALUES (@nombre, @rnc, @telefono, @notas, @activo);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ArsDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Ars}
            SET nombre = @nombre, rnc = @rnc, telefono = @telefono,
                notas = @notas, activo = @activo
            WHERE id = @id;
            """;
        AgregarParametros(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<Ars>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Ars>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static Ars Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Nombre = reader.GetString("nombre"),
        Rnc = reader.IsDBNull(reader.GetOrdinal("rnc")) ? null : reader.GetString("rnc"),
        Telefono = reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        Activo = reader.GetBoolean("activo")
    };

    private static void AgregarParametros(MySqlCommand cmd, ArsDatos datos)
    {
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@rnc", (object?)datos.Rnc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@telefono", (object?)datos.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notas", (object?)datos.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", datos.Activo);
    }
}
