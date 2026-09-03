using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>CRUD de pacientes (tabla <c>cliente</c>). Soft delete siempre; lecturas filtran deleted_at.</summary>
public class ClienteRepository
{
    private readonly ConexionFactory _factory;

    public ClienteRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>
    /// LEFT JOIN, no INNER: la mayoría de los pacientes llega por su cuenta y
    /// tiene referidor_id NULL. Con INNER desaparecerían de la lista.
    /// </summary>
    private const string SelectBase = $"""
        SELECT c.id, c.cedula, c.nombre, c.telefono, c.email, c.fecha_nacimiento,
               c.sexo, c.direccion, c.referidor_id, r.nombre AS referidor_nombre,
               c.ultima_visita_previa, c.notas, c.created_at, c.updated_at
        FROM {DbNames.Cliente} c
        LEFT JOIN {DbNames.Referidor} r ON r.id = c.referidor_id
        """;

    public async Task<List<Cliente>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE c.deleted_at IS NULL
            ORDER BY c.nombre;
            """;
        var lista = new List<Cliente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    public async Task<Cliente?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SelectBase}
            WHERE c.id = @id AND c.deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<bool> ExisteCedulaAsync(string cedula, long? exceptoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Cliente}
            WHERE cedula = @cedula AND deleted_at IS NULL
              AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@cedula", cedula);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<long> InsertarAsync(ClienteDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Cliente}
              (cedula, nombre, telefono, email, fecha_nacimiento, sexo,
               direccion, referidor_id, ultima_visita_previa, notas)
            VALUES
              (@cedula, @nombre, @telefono, @email, @nacimiento, @sexo,
               @direccion, @referidor, @visitaPrevia, @notas);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ClienteDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cliente}
            SET cedula = @cedula, nombre = @nombre, telefono = @telefono,
                email = @email, fecha_nacimiento = @nacimiento, sexo = @sexo,
                direccion = @direccion, referidor_id = @referidor,
                ultima_visita_previa = @visitaPrevia, notas = @notas,
                updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        AgregarParametros(cmd, datos);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Cliente} SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AgregarParametros(MySqlCommand cmd, ClienteDatos datos)
    {
        cmd.Parameters.AddWithValue("@cedula", (object?)datos.Cedula ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@telefono", (object?)datos.Telefono ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)datos.Email ?? DBNull.Value);
        // DATE puro: la fecha de nacimiento no tiene hora ni zona horaria, así
        // que NO pasa por la conversión UTC ↔ local del resto del sistema.
        cmd.Parameters.AddWithValue("@nacimiento",
            datos.FechaNacimiento is { } n ? n.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@sexo",
            datos.Sexo is { } s ? EnumMap.ADb(s) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@direccion", (object?)datos.Direccion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@referidor", (object?)datos.ReferidorId ?? DBNull.Value);
        // Igual que la fecha de nacimiento: DATE puro, sin hora ni zona.
        cmd.Parameters.AddWithValue("@visitaPrevia",
            datos.UltimaVisitaPrevia is { } v ? v.ToDateTime(TimeOnly.MinValue) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@notas", (object?)datos.Notas ?? DBNull.Value);
    }

    private static Cliente Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Cedula = reader.IsDBNull(reader.GetOrdinal("cedula")) ? null : reader.GetString("cedula"),
        Nombre = reader.GetString("nombre"),
        Telefono = reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
        Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
        FechaNacimiento = reader.IsDBNull(reader.GetOrdinal("fecha_nacimiento"))
            ? null : DateOnly.FromDateTime(reader.GetDateTime("fecha_nacimiento")),
        Sexo = reader.IsDBNull(reader.GetOrdinal("sexo"))
            ? null : EnumMap.SexoDeDb(reader.GetString("sexo")),
        Direccion = reader.IsDBNull(reader.GetOrdinal("direccion")) ? null : reader.GetString("direccion"),
        ReferidorId = reader.IsDBNull(reader.GetOrdinal("referidor_id"))
            ? null : reader.GetInt64("referidor_id"),
        ReferidorNombre = reader.IsDBNull(reader.GetOrdinal("referidor_nombre"))
            ? null : reader.GetString("referidor_nombre"),
        UltimaVisitaPrevia = reader.IsDBNull(reader.GetOrdinal("ultima_visita_previa"))
            ? null : DateOnly.FromDateTime(reader.GetDateTime("ultima_visita_previa")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
