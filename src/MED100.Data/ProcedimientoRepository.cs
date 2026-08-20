using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>CRUD del tarifario de procedimientos.</summary>
public class ProcedimientoRepository
{
    private readonly ConexionFactory _factory;

    public ProcedimientoRepository(ConexionFactory factory) => _factory = factory;

    private const string ColumnasBase =
        "id, codigo, nombre, precio, duracion_minutos, exento_itbis, descripcion, " +
        "activo, created_at, updated_at";

    /// <summary>Todos los vigentes, activos e inactivos (la UI los distingue).</summary>
    public async Task<List<Procedimiento>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Procedimiento}
            WHERE deleted_at IS NULL
            ORDER BY activo DESC, nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    /// <summary>Solo los que se pueden cobrar y agendar hoy.</summary>
    public async Task<List<Procedimiento>> ObtenerActivosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Procedimiento}
            WHERE deleted_at IS NULL AND activo = 1
            ORDER BY nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Procedimiento?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Procedimiento}
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<bool> ExisteCodigoAsync(string codigo, long? exceptoId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Procedimiento}
            WHERE codigo = @codigo AND deleted_at IS NULL
              AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<long> InsertarAsync(ProcedimientoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Procedimiento}
              (codigo, nombre, precio, duracion_minutos, exento_itbis, descripcion, activo)
            VALUES
              (@codigo, @nombre, @precio, @duracion, @exento, @descripcion, @activo);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ProcedimientoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Procedimiento}
            SET codigo = @codigo, nombre = @nombre, precio = @precio,
                duracion_minutos = @duracion, exento_itbis = @exento,
                descripcion = @descripcion, activo = @activo,
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
            UPDATE {DbNames.Procedimiento} SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// ¿Ya se facturó alguna vez? Un procedimiento facturado NO se borra: se
    /// desactiva. Las facturas viejas lo referencian y tienen que seguir
    /// pudiendo reimprimirse.
    /// </summary>
    public async Task<bool> FueFacturadoAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {DbNames.Detalle} WHERE procedimiento_id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>¿Hay citas que lo usan? Igual que arriba: se desactiva, no se borra.</summary>
    public async Task<bool> TieneCitasAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {DbNames.Cita} WHERE procedimiento_id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<List<Procedimiento>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Procedimiento>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static Procedimiento Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Codigo = reader.IsDBNull(reader.GetOrdinal("codigo")) ? null : reader.GetString("codigo"),
        Nombre = reader.GetString("nombre"),
        Precio = reader.GetDecimal("precio"),
        DuracionMinutos = reader.GetInt32("duracion_minutos"),
        ExentoItbis = reader.GetBoolean("exento_itbis"),
        Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion"),
        Activo = reader.GetBoolean("activo"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };

    private static void AgregarParametros(MySqlCommand cmd, ProcedimientoDatos datos)
    {
        cmd.Parameters.AddWithValue("@codigo", (object?)datos.Codigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@precio", datos.Precio);
        cmd.Parameters.AddWithValue("@duracion", datos.DuracionMinutos);
        cmd.Parameters.AddWithValue("@exento", datos.ExentoItbis);
        cmd.Parameters.AddWithValue("@descripcion", (object?)datos.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@activo", datos.Activo);
    }
}
