using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>CRUD de productos + consultas de Almacén y Caducidad.</summary>
public class ProductoRepository
{
    private readonly ConexionFactory _factory;

    public ProductoRepository(ConexionFactory factory) => _factory = factory;

    private const string ColumnasBase =
        "id, codigo, nombre, precio, cantidad, tipo, descripcion, fecha_caducidad, created_at, updated_at";

    public async Task<List<Producto>> ObtenerTodosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Producto}
            WHERE deleted_at IS NULL
            ORDER BY nombre;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    /// <summary>Solo productos con fecha de caducidad, los más próximos primero.</summary>
    public async Task<List<Producto>> ObtenerConCaducidadAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Producto}
            WHERE deleted_at IS NULL AND fecha_caducidad IS NOT NULL
            ORDER BY fecha_caducidad;
            """;
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Producto?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasBase} FROM {DbNames.Producto}
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
            SELECT COUNT(*) FROM {DbNames.Producto}
            WHERE codigo = @codigo AND deleted_at IS NULL
              AND (@exceptoId IS NULL OR id <> @exceptoId);
            """;
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@exceptoId", (object?)exceptoId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    public async Task<long> InsertarAsync(ProductoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Producto} (codigo, nombre, precio, cantidad, tipo, descripcion, fecha_caducidad)
            VALUES (@codigo, @nombre, @precio, @cantidad, @tipo, @descripcion, @fechaCaducidad);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ProductoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Producto}
            SET codigo = @codigo, nombre = @nombre, precio = @precio, cantidad = @cantidad,
                tipo = @tipo, descripcion = @descripcion, fecha_caducidad = @fechaCaducidad,
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
            UPDATE {DbNames.Producto} SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Totales del Almacén calculados en SQL (dinero: DECIMAL, nunca double).</summary>
    public async Task<AlmacenTotales> ObtenerTotalesAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*)                                   AS total_productos,
                   COALESCE(SUM(cantidad), 0)                 AS total_unidades,
                   COALESCE(SUM(cantidad * precio), 0.00)     AS valor_inventario
            FROM {DbNames.Producto}
            WHERE deleted_at IS NULL;
            """;
        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new AlmacenTotales(
            Convert.ToInt32(reader["total_productos"]),
            Convert.ToInt64(reader["total_unidades"]),
            reader.GetDecimal("valor_inventario"));
    }

    private static async Task<List<Producto>> LeerListaAsync(MySqlCommand cmd, CancellationToken ct)
    {
        var lista = new List<Producto>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static void AgregarParametros(MySqlCommand cmd, ProductoDatos datos)
    {
        cmd.Parameters.AddWithValue("@codigo", (object?)datos.Codigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@nombre", datos.Nombre);
        cmd.Parameters.AddWithValue("@precio", datos.Precio);
        cmd.Parameters.AddWithValue("@cantidad", datos.Cantidad);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(datos.Tipo));
        cmd.Parameters.AddWithValue("@descripcion", (object?)datos.Descripcion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fechaCaducidad",
            datos.FechaCaducidad is { } f ? f.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
    }

    private static Producto Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        Codigo = reader.IsDBNull(reader.GetOrdinal("codigo")) ? null : reader.GetString("codigo"),
        Nombre = reader.GetString("nombre"),
        Precio = reader.GetDecimal("precio"),
        Cantidad = reader.GetInt32("cantidad"),
        Tipo = EnumMap.TipoProductoDeDb(reader.GetString("tipo")),
        Descripcion = reader.IsDBNull(reader.GetOrdinal("descripcion")) ? null : reader.GetString("descripcion"),
        FechaCaducidad = reader.IsDBNull(reader.GetOrdinal("fecha_caducidad"))
            ? null : DateOnly.FromDateTime(reader.GetDateTime("fecha_caducidad")),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
