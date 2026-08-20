using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// CRUD del catálogo de procedencias. No lleva soft delete: es un catálogo
/// chico y la FK del paciente es ON DELETE SET NULL, así que borrar un
/// referidor no rompe pacientes, solo los deja "sin procedencia". Aun así la
/// vía normal es desactivarlo.
/// </summary>
public class ReferidorRepository
{
    private readonly ConexionFactory _factory;

    public ReferidorRepository(ConexionFactory factory) => _factory = factory;

    private const string ColumnasBase = "id, nombre, tipo, notas, activo";

    public async Task<List<Referidor>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Referidor}
            ORDER BY activo DESC, nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<List<Referidor>> ObtenerActivosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Referidor}
            WHERE activo = 1
            ORDER BY nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Referidor?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT {ColumnasBase} FROM {DbNames.Referidor} WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// Busca por nombre y tipo exactos (la comparación la hace MySQL con la
    /// collation utf8mb4_unicode_ci: no distingue mayúsculas ni acentos, que
    /// es justo lo que hace falta para no duplicar "Dr. Pérez" y "dr. perez").
    /// </summary>
    public async Task<Referidor?> BuscarPorNombreAsync(string nombre, TipoReferidor tipo,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Referidor}
            WHERE nombre = @nombre AND tipo = @tipo
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(tipo));
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<long> InsertarAsync(ReferidorDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Referidor} (nombre, tipo, notas, activo)
            VALUES (@nombre, @tipo, @notas, @activo);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ReferidorDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Referidor}
            SET nombre = @nombre, tipo = @tipo, notas = @notas, activo = @activo
            WHERE id = @id;
            """;
        AgregarParametros(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Cuántos pacientes vinieron por esta procedencia (vivos, no borrados).</summary>
    public async Task<int> ContarPacientesAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Cliente}
            WHERE referidor_id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<List<Referidor>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Referidor>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static Referidor Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Nombre = reader.GetString("nombre"),
        Tipo = EnumMap.TipoReferidorDeDb(reader.GetString("tipo")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        Activo = reader.GetBoolean("activo")
    };

    private static void AgregarParametros(MySqlCommand cmd, ReferidorDatos datos)
    {
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(datos.Tipo));
        cmd.Parameters.AddWithValue("@notas", (object?)datos.Notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", datos.Activo);
    }
}
