using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Escritura de facturas. Los métodos transaccionales reciben la conexión y
/// transacción del VentaService: la emisión es UN solo commit (spec §9.2/§9.4).
/// Las facturas JAMÁS se borran (solo estado anulada, Fase 4).
/// </summary>
public class FacturaRepository
{
    private readonly ConexionFactory _factory;

    public FacturaRepository(ConexionFactory factory) => _factory = factory;

    public async Task<MySqlConnection> AbrirConexionAsync(CancellationToken ct = default) =>
        await _factory.AbrirAsync(ct);

    // ------------------------------------------------------------------
    // Lectura (Buscar comprobante)
    // ------------------------------------------------------------------

    // Columnas y FROM separados: el detalle añade columnas extra al SELECT y
    // pegarlas después del FROM haría que MySQL las lea como tablas
    private const string ColumnasResumen = """
        f.id, f.numero_factura, f.fecha_emision, f.total, f.metodo_pago, f.estado,
        f.anulada_motivo, f.usuario_id, f.cliente_id,
        c.nombre AS cliente_nombre,
        TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS cajero_nombre
        """;

    private const string FromResumen = """
        FROM factura f
        LEFT JOIN cliente c ON c.id = f.cliente_id
        JOIN usuario u ON u.id = f.usuario_id
        """;

    /// <summary>
    /// Búsqueda de comprobantes. El rango de fechas se evalúa por DÍA DE NEGOCIO
    /// (RD = UTC-4): una venta de las 10pm del lunes pertenece al lunes, no al martes.
    /// </summary>
    public async Task<List<FacturaResumen>> BuscarAsync(FiltroComprobantes filtro, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {ColumnasResumen}
            {FromResumen}
            WHERE (@texto IS NULL
                   OR f.numero_factura LIKE CONCAT('%', @texto, '%')
                   OR c.nombre LIKE CONCAT('%', @texto, '%'))
              AND (@desde IS NULL OR DATE(DATE_SUB(f.fecha_emision, INTERVAL 4 HOUR)) >= @desde)
              AND (@hasta IS NULL OR DATE(DATE_SUB(f.fecha_emision, INTERVAL 4 HOUR)) <= @hasta)
              AND (@usuarioId IS NULL OR f.usuario_id = @usuarioId)
            ORDER BY f.fecha_emision DESC
            LIMIT @limite;
            """;
        cmd.Parameters.AddWithValue("@texto",
            string.IsNullOrWhiteSpace(filtro.Texto) ? DBNull.Value : filtro.Texto.Trim());
        cmd.Parameters.AddWithValue("@desde",
            filtro.Desde is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@hasta",
            filtro.Hasta is { } h ? h.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@usuarioId", (object?)filtro.UsuarioId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@limite", filtro.Limite);

        var lista = new List<FacturaResumen>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(MapearResumen(reader));
        return lista;
    }

    /// <summary>Factura completa con sus líneas (detalle + reimpresión del ticket).</summary>
    public async Task<FacturaCompleta?> ObtenerCompletaAsync(long facturaId, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);

        FacturaResumen resumen;
        VentaTotales totales;
        decimal? efectivo, cambio;
        HonorarioMedico? honorario;
        RepartoArs? reparto;
        string? ncf;

        using (var cmd = conexion.CreateCommand())
        {
            // El honorario y el reparto se leen de la FACTURA, no del catálogo:
            // es lo que se pagó ese día, aunque hoy el porcentaje sea otro.
            cmd.CommandText = $"""
                SELECT {ColumnasResumen},
                       f.subtotal, f.itbis_tasa, f.itbis, f.efectivo_recibido, f.cambio,
                       f.medico_id, med.nombre AS medico_nombre,
                       f.honorario_porcentaje, f.honorario_monto,
                       f.ars_id, a.nombre AS ars_nombre, f.ars_autorizacion,
                       f.ars_cubierto, f.paciente_paga, f.ncf
                {FromResumen}
                LEFT JOIN {DbNames.Medico} med ON med.id = f.medico_id
                LEFT JOIN {DbNames.Ars}    a   ON a.id   = f.ars_id
                WHERE f.id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", facturaId);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            resumen = MapearResumen(reader);
            totales = new VentaTotales(
                reader.GetDecimal("subtotal"),
                reader.GetDecimal("itbis_tasa"),
                reader.GetDecimal("itbis"),
                reader.GetDecimal("total"));
            efectivo = reader.IsDBNull(reader.GetOrdinal("efectivo_recibido"))
                ? null : reader.GetDecimal("efectivo_recibido");
            cambio = reader.IsDBNull(reader.GetOrdinal("cambio"))
                ? null : reader.GetDecimal("cambio");

            honorario = reader.IsDBNull(reader.GetOrdinal("medico_id"))
                ? null
                : new HonorarioMedico(
                    reader.GetInt64("medico_id"),
                    reader.IsDBNull(reader.GetOrdinal("medico_nombre"))
                        ? null : reader.GetString("medico_nombre"),
                    reader.GetDecimal("honorario_porcentaje"),
                    // La base no se persiste: se reconstruye del monto y el
                    // porcentaje solo para mostrarla, y con 0% no hay base.
                    reader.GetDecimal("honorario_porcentaje") == 0m
                        ? 0m
                        : reader.GetDecimal("honorario_monto") * 100m / reader.GetDecimal("honorario_porcentaje"),
                    reader.GetDecimal("honorario_monto"));

            reparto = reader.IsDBNull(reader.GetOrdinal("ars_id"))
                ? null
                : new RepartoArs(
                    reader.GetInt64("ars_id"),
                    reader.IsDBNull(reader.GetOrdinal("ars_nombre")) ? null : reader.GetString("ars_nombre"),
                    reader.IsDBNull(reader.GetOrdinal("ars_autorizacion"))
                        ? null : reader.GetString("ars_autorizacion"),
                    reader.GetDecimal("ars_cubierto"),
                    reader.GetDecimal("paciente_paga"));

            ncf = reader.IsDBNull(reader.GetOrdinal("ncf")) ? null : reader.GetString("ncf");
        }

        var lineas = new List<FacturaLinea>();
        using (var cmd = conexion.CreateCommand())
        {
            // La descripción se lee de la FACTURA, no del catálogo: es el nombre
            // que tenía la cosa el día que se cobró. Y el JOIN a producto se fue,
            // porque ahora una línea puede ser un procedimiento, que no vive
            // en esa tabla.
            cmd.CommandText = $"""
                SELECT d.producto_id, d.procedimiento_id, d.descripcion,
                       d.cantidad, d.precio_unitario, d.precio_catalogo,
                       d.subtotal, d.exento_itbis
                FROM {DbNames.Detalle} d
                WHERE d.factura_id = @id
                ORDER BY d.id;
                """;
            cmd.Parameters.AddWithValue("@id", facturaId);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                lineas.Add(new FacturaLinea(
                    reader.IsDBNull(reader.GetOrdinal("producto_id"))
                        ? null : reader.GetInt64("producto_id"),
                    reader.GetString("descripcion"),
                    reader.GetInt32("cantidad"),
                    reader.GetDecimal("precio_unitario"),
                    reader.GetDecimal("subtotal"),
                    reader.IsDBNull(reader.GetOrdinal("procedimiento_id"))
                        ? null : reader.GetInt64("procedimiento_id"),
                    reader.GetBoolean("exento_itbis"),
                    // NULL = se cobró el precio de lista. Las facturas anteriores
                    // a la migración 010 lo tienen todas en NULL, y está bien:
                    // en esa época no se podía rebajar.
                    reader.IsDBNull(reader.GetOrdinal("precio_catalogo"))
                        ? null : reader.GetDecimal("precio_catalogo")));
        }

        return new FacturaCompleta(resumen, totales, efectivo, cambio, lineas,
            honorario, reparto, ncf);
    }

    // ------------------------------------------------------------------
    // Anulación (nunca se borra una factura — spec §9.3)
    // ------------------------------------------------------------------

    /// <summary>
    /// Marca la factura como anulada. Devuelve false si ya lo estaba (el
    /// WHERE exige estado='emitida'), lo que evita anular dos veces y
    /// devolver el stock por duplicado.
    /// </summary>
    public static async Task<bool> MarcarAnuladaAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long facturaId, string motivo, DateTime ahoraUtc, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Factura}
            SET estado = 'anulada', anulada_at = @ahora, anulada_motivo = @motivo
            WHERE id = @id AND estado = 'emitida';
            """;
        cmd.Parameters.AddWithValue("@ahora", ahoraUtc);
        cmd.Parameters.AddWithValue("@motivo", motivo);
        cmd.Parameters.AddWithValue("@id", facturaId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>Devuelve al inventario lo vendido en la factura (al anular).</summary>
    public static async Task DevolverStockAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long facturaId, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Producto} p
            JOIN {DbNames.Detalle} d ON d.producto_id = p.id
            SET p.cantidad = p.cantidad + d.cantidad, p.updated_at = UTC_TIMESTAMP()
            WHERE d.factura_id = @facturaId;
            """;
        cmd.Parameters.AddWithValue("@facturaId", facturaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static FacturaResumen MapearResumen(MySqlDataReader reader) => new(
        reader.GetInt64("id"),
        reader.GetString("numero_factura"),
        DateTime.SpecifyKind(reader.GetDateTime("fecha_emision"), DateTimeKind.Utc),
        reader.IsDBNull(reader.GetOrdinal("cliente_nombre")) ? null : reader.GetString("cliente_nombre"),
        reader.GetString("cajero_nombre"),
        reader.GetInt64("usuario_id"),
        reader.GetDecimal("total"),
        EnumMap.MetodoPagoDeDb(reader.GetString("metodo_pago")),
        EnumMap.EstadoFacturaDeDb(reader.GetString("estado")),
        reader.IsDBNull(reader.GetOrdinal("anulada_motivo")) ? null : reader.GetString("anulada_motivo"),
        reader.IsDBNull(reader.GetOrdinal("cliente_id")) ? null : reader.GetInt64("cliente_id"));

    /// <summary>
    /// Descuenta stock validando disponibilidad EN LA MISMA sentencia
    /// (cantidad >= @cantidad): si otro cajero vendió el mismo producto a la
    /// vez, afecta 0 filas y la venta completa se revierte (spec §9.5).
    /// </summary>
    public static async Task<bool> DescontarStockAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long productoId, int cantidad, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Producto}
            SET cantidad = cantidad - @cantidad, updated_at = UTC_TIMESTAMP()
            WHERE id = @id AND deleted_at IS NULL AND cantidad >= @cantidad;
            """;
        cmd.Parameters.AddWithValue("@cantidad", cantidad);
        cmd.Parameters.AddWithValue("@id", productoId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    /// <summary>
    /// Inserta la factura con TODO copiado: el porcentaje del honorario y el
    /// reparto con la ARS quedan congelados acá. Un comprobante ya entregado no
    /// puede cambiar porque alguien editó un porcentaje seis meses después.
    /// </summary>
    public static async Task<long> InsertarFacturaAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        string numeroFactura, long? clienteId, long usuarioId, DateTime fechaEmisionUtc,
        VentaTotales totales, MetodoPagoFactura metodoPago,
        decimal? efectivoRecibido, decimal? cambio,
        HonorarioMedico? honorario, RepartoArs? ars, string? ncf,
        CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Factura}
                (numero_factura, cliente_id, usuario_id, medico_id, fecha_emision,
                 subtotal, itbis_tasa, itbis, total,
                 honorario_porcentaje, honorario_monto,
                 ars_id, ars_autorizacion, ars_cubierto, paciente_paga,
                 metodo_pago, efectivo_recibido, cambio, ncf, estado)
            VALUES
                (@numero, @clienteId, @usuarioId, @medicoId, @fecha,
                 @subtotal, @itbisTasa, @itbis, @total,
                 @honorarioPct, @honorarioMonto,
                 @arsId, @arsAutorizacion, @arsCubierto, @pacientePaga,
                 @metodoPago, @efectivo, @cambio, @ncf, 'emitida');
            SELECT LAST_INSERT_ID();
            """;
        cmd.Parameters.AddWithValue("@numero", numeroFactura);
        cmd.Parameters.AddWithValue("@clienteId", (object?)clienteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@medicoId", (object?)honorario?.MedicoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fecha", fechaEmisionUtc);
        cmd.Parameters.AddWithValue("@subtotal", totales.Subtotal);
        cmd.Parameters.AddWithValue("@itbisTasa", totales.ItbisTasa);
        cmd.Parameters.AddWithValue("@itbis", totales.Itbis);
        cmd.Parameters.AddWithValue("@total", totales.Total);
        cmd.Parameters.AddWithValue("@honorarioPct", honorario?.Porcentaje ?? 0m);
        cmd.Parameters.AddWithValue("@honorarioMonto", honorario?.Monto ?? 0m);
        cmd.Parameters.AddWithValue("@arsId", (object?)ars?.ArsId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@arsAutorizacion", (object?)ars?.Autorizacion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@arsCubierto", ars?.Cubierto ?? 0m);
        // Sin ARS, el paciente paga todo. Guardarlo igual (en vez de 0) es lo
        // que permite que el cuadre sume SIEMPRE esta columna.
        cmd.Parameters.AddWithValue("@pacientePaga", ars?.PacientePaga ?? totales.Total);
        cmd.Parameters.AddWithValue("@metodoPago", EnumMap.ADb(metodoPago));
        cmd.Parameters.AddWithValue("@efectivo", (object?)efectivoRecibido ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cambio", (object?)cambio ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ncf",
            string.IsNullOrWhiteSpace(ncf) ? DBNull.Value : ncf.Trim());
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Une la cita con la factura que la cobró. Se marca atendida de paso: si
    /// se cobró, el paciente vino.
    /// </summary>
    public static async Task EnlazarCitaAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long citaId, long facturaId, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            UPDATE {DbNames.Cita}
            SET factura_id = @facturaId, estado = 'atendida', updated_at = UTC_TIMESTAMP()
            WHERE id = @citaId AND factura_id IS NULL;
            """;
        cmd.Parameters.AddWithValue("@facturaId", facturaId);
        cmd.Parameters.AddWithValue("@citaId", citaId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task InsertarDetalleAsync(
        MySqlConnection conexion, MySqlTransaction transaccion,
        long facturaId, VentaLinea linea, CancellationToken ct = default)
    {
        using var cmd = conexion.CreateCommand();
        cmd.Transaction = transaccion;
        cmd.CommandText = $"""
            INSERT INTO {DbNames.Detalle}
                (factura_id, procedimiento_id, producto_id, descripcion, cantidad,
                 precio_unitario, precio_catalogo, exento_itbis, subtotal)
            VALUES (@facturaId, @procedimientoId, @productoId, @descripcion, @cantidad,
                    @precio, @precioCatalogo, @exento, @subtotal);
            """;
        cmd.Parameters.AddWithValue("@facturaId", facturaId);
        cmd.Parameters.AddWithValue("@procedimientoId", (object?)linea.ProcedimientoId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@productoId", (object?)linea.ProductoId ?? DBNull.Value);
        // El nombre se guarda tal como estaba al facturar: si mañana se
        // renombra el insumo, el comprobante viejo no puede cambiar.
        cmd.Parameters.AddWithValue("@descripcion", linea.NombreProducto);
        cmd.Parameters.AddWithValue("@exento", linea.Exento);
        cmd.Parameters.AddWithValue("@cantidad", linea.Cantidad);
        cmd.Parameters.AddWithValue("@precio", linea.PrecioUnitario);
        // Solo se guarda si de verdad hubo rebaja: escribir el mismo número dos
        // veces en cada línea de cada factura no documenta nada.
        cmd.Parameters.AddWithValue("@precioCatalogo",
            linea.PrecioCatalogo is { } lista && lista != linea.PrecioUnitario
                ? lista : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@subtotal", linea.Subtotal);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
