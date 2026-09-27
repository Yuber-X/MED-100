using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Catálogo de textos de consentimiento informado (015).
///
/// Soft delete como todo el resto: una plantilla que se dio de baja tiene
/// papeles firmados dando vueltas por ahí, y hay que poder saber qué decía el
/// texto que el paciente firmó.
/// </summary>
public class ConsentimientoRepository
{
    private readonly ConexionFactory _factory;

    public ConsentimientoRepository(ConexionFactory factory) => _factory = factory;

    private static readonly string SqlSelect = $"""
        SELECT c.id, c.procedimiento_id, c.titulo, c.cuerpo, c.activo,
               c.created_at, c.updated_at,
               p.nombre AS procedimiento_nombre
        FROM {DbNames.Consentimiento} c
        LEFT JOIN {DbNames.Procedimiento} p ON p.id = c.procedimiento_id
        WHERE c.deleted_at IS NULL
        """;

    /// <summary>
    /// Todas las plantillas vigentes. Los generales van primero: son los que se
    /// firman cuando el procedimiento no tiene uno propio.
    /// </summary>
    public async Task<List<Consentimiento>> ObtenerTodosAsync(bool soloActivos = false,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelect}
              AND (@soloActivos = 0 OR c.activo = 1)
            ORDER BY (c.procedimiento_id IS NOT NULL), p.nombre, c.titulo;
            """;
        cmd.Parameters.AddWithValue("@soloActivos", soloActivos ? 1 : 0);
        return await LeerListaAsync(cmd, ct);
    }

    /// <summary>
    /// Los que corresponden a un procedimiento: el suyo Y los generales.
    ///
    /// Los generales van siempre porque la clínica que tiene un solo papel para
    /// todo es el caso normal; si no vinieran, el botón aparecería vacío
    /// justamente en esa clínica. Eso sí, van DESPUÉS: primero el del
    /// procedimiento, que es el que corresponde firmar cuando existe.
    /// </summary>
    public async Task<List<Consentimiento>> ObtenerParaProcedimientoAsync(long? procedimientoId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            {SqlSelect}
              AND c.activo = 1
              AND (c.procedimiento_id IS NULL OR c.procedimiento_id = @procedimientoId)
            ORDER BY (c.procedimiento_id IS NULL), c.titulo;
            """;
        cmd.Parameters.AddWithValue("@procedimientoId", (object?)procedimientoId ?? DBNull.Value);
        return await LeerListaAsync(cmd, ct);
    }

    public async Task<Consentimiento?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"{SqlSelect} AND c.id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    public async Task<long> InsertarAsync(ConsentimientoDatos datos, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Consentimiento}
              (procedimiento_id, titulo, cuerpo, activo)
            VALUES
              (@procedimientoId, @titulo, @cuerpo, @activo);
            SELECT LAST_INSERT_ID();
            """;
        AgregarParametros(cmd, datos);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarAsync(long id, ConsentimientoDatos datos,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.Consentimiento}
            SET procedimiento_id = @procedimientoId,
                titulo           = @titulo,
                cuerpo           = @cuerpo,
                activo           = @activo,
                updated_at       = UTC_TIMESTAMP()
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
            UPDATE {DbNames.Consentimiento}
            SET deleted_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AgregarParametros(MySqlCommand cmd, ConsentimientoDatos datos)
    {
        cmd.Parameters.AddWithValue("@procedimientoId",
            (object?)datos.ProcedimientoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@titulo", datos.Titulo.Trim());
        cmd.Parameters.AddWithValue("@cuerpo", datos.Cuerpo.Trim());
        cmd.Parameters.AddWithValue("@activo", datos.Activo ? 1 : 0);
    }

    private static async Task<List<Consentimiento>> LeerListaAsync(MySqlCommand cmd,
        CancellationToken ct)
    {
        var lista = new List<Consentimiento>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    private static Consentimiento Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        ProcedimientoId = reader.IsDBNull(reader.GetOrdinal("procedimiento_id"))
            ? null : reader.GetInt64("procedimiento_id"),
        ProcedimientoNombre = reader.IsDBNull(reader.GetOrdinal("procedimiento_nombre"))
            ? null : reader.GetString("procedimiento_nombre"),
        Titulo = reader.GetString("titulo"),
        Cuerpo = reader.GetString("cuerpo"),
        Activo = reader.GetBoolean("activo"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        UpdatedAtUtc = reader.IsDBNull(reader.GetOrdinal("updated_at"))
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime("updated_at"), DateTimeKind.Utc)
    };
}
