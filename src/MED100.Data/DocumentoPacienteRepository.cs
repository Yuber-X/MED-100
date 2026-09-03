using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Fichas del expediente digital del paciente.
///
/// Acá SOLO se toca la base. Quien mueve los archivos en el disco es
/// <c>ExpedienteService</c>: mezclando las dos cosas, un fallo de disco dejaría
/// filas apuntando a archivos que no existen y al revés.
/// </summary>
public class DocumentoPacienteRepository
{
    private readonly ConexionFactory _factory;

    public DocumentoPacienteRepository(ConexionFactory factory) => _factory = factory;

    private const string Columnas = """
        d.id, d.cliente_id, d.nombre, d.ruta_relativa, d.extension, d.tamano_bytes,
        d.tipo, d.notas, d.created_at,
        TRIM(CONCAT(COALESCE(u.nombre, ''), ' ', COALESCE(u.apellido, ''))) AS subido_por
        """;

    public async Task<List<DocumentoPaciente>> ObtenerDeAsync(long clienteId,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columnas}
            FROM {DbNames.DocumentoPaciente} d
            LEFT JOIN {DbNames.Usuario} u ON u.id = d.created_by
            WHERE d.cliente_id = @cliente AND d.deleted_at IS NULL
            ORDER BY d.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("@cliente", clienteId);

        var lista = new List<DocumentoPaciente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(Mapear(reader));
        return lista;
    }

    public async Task<DocumentoPaciente?> ObtenerPorIdAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {Columnas}
            FROM {DbNames.DocumentoPaciente} d
            LEFT JOIN {DbNames.Usuario} u ON u.id = d.created_by
            WHERE d.id = @id AND d.deleted_at IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Mapear(reader) : null;
    }

    /// <summary>
    /// El tablero del almacén: un renglón por paciente con cuántos papeles
    /// tiene, cuántas citas y facturas, y cuándo vino por última vez.
    ///
    /// Se calcula en SQL con subconsultas y no trayendo todo a memoria: con
    /// mil pacientes, contar en C# significa mil viajes a la base.
    /// </summary>
    public async Task<List<ResumenExpediente>> ObtenerResumenAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, c.nombre, c.cedula, c.telefono,
                   (SELECT COUNT(*) FROM {DbNames.DocumentoPaciente} d
                     WHERE d.cliente_id = c.id AND d.deleted_at IS NULL) AS documentos,
                   (SELECT MAX(d.created_at) FROM {DbNames.DocumentoPaciente} d
                     WHERE d.cliente_id = c.id AND d.deleted_at IS NULL) AS ultimo_documento,
                   (SELECT COUNT(*) FROM {DbNames.Cita} ci
                     WHERE ci.cliente_id = c.id) AS citas,
                   (SELECT COUNT(*) FROM {DbNames.Factura} f
                     WHERE f.cliente_id = c.id AND f.estado = 'emitida') AS facturas,
                   GREATEST(
                     COALESCE((SELECT MAX(ci.fecha_hora) FROM {DbNames.Cita} ci
                                WHERE ci.cliente_id = c.id AND ci.estado = 'atendida'), '1000-01-01'),
                     COALESCE((SELECT MAX(f.fecha_emision) FROM {DbNames.Factura} f
                                WHERE f.cliente_id = c.id AND f.estado = 'emitida'), '1000-01-01'),
                     -- La que cargó la recepción al pasar el paciente al sistema.
                     -- Entra como una candidata más: si hay actividad real
                     -- posterior, GREATEST la elige sola y no hay que borrar nada.
                     COALESCE(c.ultima_visita_previa, '1000-01-01')
                   ) AS ultima_visita
            FROM {DbNames.Cliente} c
            WHERE c.deleted_at IS NULL
            ORDER BY c.nombre;
            """;

        var lista = new List<ResumenExpediente>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // GREATEST devuelve el centinela cuando el paciente nunca vino:
            // se traduce a null en vez de mostrar el año 1000 en pantalla.
            var visita = reader.IsDBNull(reader.GetOrdinal("ultima_visita"))
                ? (DateTime?)null
                : reader.GetDateTime("ultima_visita");
            if (visita is { Year: <= 1000 })
                visita = null;

            lista.Add(new ResumenExpediente(
                reader.GetInt64("id"),
                reader.GetString("nombre"),
                reader.IsDBNull(reader.GetOrdinal("cedula")) ? null : reader.GetString("cedula"),
                reader.IsDBNull(reader.GetOrdinal("telefono")) ? null : reader.GetString("telefono"),
                reader.GetInt32("documentos"),
                reader.IsDBNull(reader.GetOrdinal("ultimo_documento"))
                    ? null
                    : DateTime.SpecifyKind(reader.GetDateTime("ultimo_documento"), DateTimeKind.Utc),
                reader.GetInt32("citas"),
                reader.GetInt32("facturas"),
                visita is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null));
        }
        return lista;
    }

    /// <summary>
    /// Inserta la ficha y devuelve su id. La ruta se arma DESPUÉS con ese id
    /// (el archivo en disco lo lleva delante para no pisarse con otro del mismo
    /// nombre), así que entra provisoria y se corrige con
    /// <see cref="ActualizarRutaAsync"/>.
    /// </summary>
    public async Task<long> CrearAsync(long clienteId, string nombre, string rutaRelativa,
        string extension, long tamanoBytes, TipoDocumentoPaciente tipo, string? notas,
        long? usuarioId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.DocumentoPaciente}
              (cliente_id, nombre, ruta_relativa, extension, tamano_bytes, tipo, notas, created_by)
            VALUES (@cliente, @nombre, @ruta, @ext, @tamano, @tipo, @notas, @usuario);
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@cliente", clienteId);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        cmd.Parameters.AddWithValue("@ruta", rutaRelativa);
        cmd.Parameters.AddWithValue("@ext", extension);
        cmd.Parameters.AddWithValue("@tamano", tamanoBytes);
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(tipo));
        cmd.Parameters.AddWithValue("@notas", (object?)notas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuario", (object?)usuarioId ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ActualizarRutaAsync(long id, string rutaRelativa, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            $"UPDATE {DbNames.DocumentoPaciente} SET ruta_relativa = @ruta WHERE id = @id;";
        cmd.Parameters.AddWithValue("@ruta", rutaRelativa);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Cambia para qué sirve el papel sin volver a subirlo.</summary>
    public async Task CambiarTipoAsync(long id, TipoDocumentoPaciente tipo,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"UPDATE {DbNames.DocumentoPaciente} SET tipo = @tipo WHERE id = @id;";
        cmd.Parameters.AddWithValue("@tipo", EnumMap.ADb(tipo));
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Re-ubicar: el papel era de otro paciente y hubo confusión en el
    /// mostrador. Es de las cosas que solo hace un Admin.
    /// </summary>
    public async Task MoverAsync(long id, long clienteDestinoId, string rutaRelativa,
        CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            UPDATE {DbNames.DocumentoPaciente}
            SET cliente_id = @cliente, ruta_relativa = @ruta
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@cliente", clienteDestinoId);
        cmd.Parameters.AddWithValue("@ruta", rutaRelativa);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Soft delete: la ficha queda con su rastro de quién la subió y quién la
    /// quitó. El ARCHIVO tampoco se borra del disco — deshacer un borrado tiene
    /// que ser posible; borrar el archivo lo haría irreversible.
    /// </summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            $"UPDATE {DbNames.DocumentoPaciente} SET deleted_at = UTC_TIMESTAMP() WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static DocumentoPaciente Mapear(MySqlDataReader reader) => new()
    {
        Id = reader.GetInt64("id"),
        ClienteId = reader.GetInt64("cliente_id"),
        Nombre = reader.GetString("nombre"),
        RutaRelativa = reader.GetString("ruta_relativa"),
        Extension = reader.GetString("extension"),
        TamanoBytes = reader.GetInt64("tamano_bytes"),
        Tipo = EnumMap.TipoDocumentoDeDb(reader.GetString("tipo")),
        Notas = reader.IsDBNull(reader.GetOrdinal("notas")) ? null : reader.GetString("notas"),
        CreatedAtUtc = DateTime.SpecifyKind(reader.GetDateTime("created_at"), DateTimeKind.Utc),
        SubidoPor = reader.IsDBNull(reader.GetOrdinal("subido_por"))
            ? null : reader.GetString("subido_por")
    };
}
