using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Consultas de analítica (Panel y Reportes). Reglas transversales:
///  · Todo se agrupa por DÍA DE NEGOCIO (RD = UTC-4): una venta de las 11pm
///    pertenece a ese día, no al siguiente en UTC.
///  · Las facturas ANULADAS nunca suman a las ventas (se informan aparte).
///  · Los totales se calculan en SQL con DECIMAL — jamás sumando en la UI.
/// </summary>
public class AnaliticaRepository
{
    private readonly ConexionFactory _factory;

    public AnaliticaRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>Expresión del día de negocio de una factura.</summary>
    private const string DiaNegocio = "DATE(DATE_SUB(f.fecha_emision, INTERVAL 4 HOUR))";

    // ------------------------------------------------------------------
    // Panel
    // ------------------------------------------------------------------

    public async Task<DashboardDatos> ObtenerDashboardAsync(
        DateOnly hoy, int diasCaducidad, int umbralStockBajo, CancellationToken ct = default)
    {
        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var inicioMesAnterior = inicioMes.AddMonths(-1);
        var finMesAnterior = inicioMes.AddDays(-1);

        using var conexion = await _factory.AbrirAsync(ct);

        var (ventasHoy, facturasHoy) = await TotalYConteoAsync(conexion, hoy, hoy, ct);
        var (ventasMes, facturasMes) = await TotalYConteoAsync(conexion, inicioMes, hoy, ct);
        var (ventasMesAnterior, _) = await TotalYConteoAsync(conexion, inicioMesAnterior, finMesAnterior, ct);

        var ticketPromedio = facturasMes > 0
            ? Math.Round(ventasMes / facturasMes, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var porCaducar = await ContarPorCaducarAsync(conexion, hoy, diasCaducidad, ct);
        var stockBajo = await ContarStockBajoAsync(conexion, umbralStockBajo, ct);
        var ventasPorDia = await VentasPorDiaAsync(conexion, inicioMes, hoy, ct);
        var topVendedores = await TopVendedoresAsync(conexion, inicioMes, hoy, limite: 5, ct);
        var topProductos = await TopProductosAsync(conexion, inicioMes, hoy, limite: 5, ct);

        return new DashboardDatos(ventasHoy, facturasHoy, ventasMes, ventasMesAnterior,
            ticketPromedio, porCaducar, stockBajo, ventasPorDia, topVendedores, topProductos);
    }

    // ------------------------------------------------------------------
    // Reportes por fecha
    // ------------------------------------------------------------------

    public async Task<ReporteVentas> ObtenerReporteAsync(
        DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);

        decimal total, itbis, montoAnulado;
        int facturas, anuladas;
        TotalesPorMetodo porMetodo;

        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT
                  COALESCE(SUM(CASE WHEN f.estado='emitida' THEN f.total END), 0.00) AS total,
                  COALESCE(SUM(f.estado='emitida'), 0)                               AS facturas,
                  COALESCE(SUM(CASE WHEN f.estado='emitida' THEN f.itbis END), 0.00) AS itbis,
                  -- Por metodo de pago va paciente_paga: es lo que entro de
                  -- verdad. El "total" de arriba sigue siendo lo FACTURADO, y
                  -- los dos numeros ahora pueden diferir a proposito.
                  COALESCE(SUM(CASE WHEN f.estado='emitida' AND f.metodo_pago='efectivo'
                                    THEN f.paciente_paga END), 0.00)                          AS efectivo,
                  COALESCE(SUM(CASE WHEN f.estado='emitida' AND f.metodo_pago='tarjeta'
                                    THEN f.paciente_paga END), 0.00)                          AS tarjeta,
                  COALESCE(SUM(CASE WHEN f.estado='emitida' AND f.metodo_pago='transferencia'
                                    THEN f.paciente_paga END), 0.00)                          AS transferencia,
                  COALESCE(SUM(CASE WHEN f.estado='emitida' AND f.metodo_pago='mixto'
                                    THEN f.paciente_paga END), 0.00)                          AS mixto,
                  COALESCE(SUM(f.estado='anulada'), 0)                                AS anuladas,
                  COALESCE(SUM(CASE WHEN f.estado='anulada' THEN f.total END), 0.00)  AS monto_anulado
                FROM {DbNames.Factura} f
                WHERE {DiaNegocio} BETWEEN @desde AND @hasta;
                """;
            AgregarRango(cmd, desde, hasta);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            total = reader.GetDecimal("total");
            facturas = Convert.ToInt32(reader["facturas"]);
            itbis = reader.GetDecimal("itbis");
            porMetodo = new TotalesPorMetodo(
                reader.GetDecimal("efectivo"), reader.GetDecimal("tarjeta"),
                reader.GetDecimal("transferencia"), reader.GetDecimal("mixto"));
            anuladas = Convert.ToInt32(reader["anuladas"]);
            montoAnulado = reader.GetDecimal("monto_anulado");
        }

        var ticketPromedio = facturas > 0
            ? Math.Round(total / facturas, 2, MidpointRounding.AwayFromZero)
            : 0m;

        var ventasPorDia = await VentasPorDiaAsync(conexion, desde, hasta, ct);
        var topProductos = await TopProductosAsync(conexion, desde, hasta, limite: 10, ct);
        var porCajero = await TopVendedoresAsync(conexion, desde, hasta, limite: 20, ct);

        return new ReporteVentas(desde, hasta, total, facturas, itbis, ticketPromedio,
            porMetodo, anuladas, montoAnulado, ventasPorDia, topProductos, porCajero);
    }

    // ------------------------------------------------------------------
    // Consultas compartidas
    // ------------------------------------------------------------------

    private async Task<(decimal Total, int Facturas)> TotalYConteoAsync(
        MySqlConnection conexion, DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COALESCE(SUM(f.total), 0.00) AS total, COUNT(*) AS facturas
            FROM {DbNames.Factura} f
            WHERE f.estado = 'emitida'
              AND {DiaNegocio} BETWEEN @desde AND @hasta;
            """;
        AgregarRango(cmd, desde, hasta);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetDecimal("total"), Convert.ToInt32(reader["facturas"]));
    }

    private async Task<List<VentaDiaria>> VentasPorDiaAsync(
        MySqlConnection conexion, DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT {DiaNegocio} AS dia,
                   COALESCE(SUM(f.total), 0.00) AS monto,
                   COUNT(*) AS facturas
            FROM {DbNames.Factura} f
            WHERE f.estado = 'emitida'
              AND {DiaNegocio} BETWEEN @desde AND @hasta
            GROUP BY dia
            ORDER BY dia;
            """;
        AgregarRango(cmd, desde, hasta);

        var lista = new List<VentaDiaria>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new VentaDiaria(
                DateOnly.FromDateTime(reader.GetDateTime("dia")),
                reader.GetDecimal("monto"),
                reader.GetInt32("facturas")));
        return lista;
    }

    private async Task<List<VendedorRanking>> TopVendedoresAsync(
        MySqlConnection conexion, DateOnly desde, DateOnly hasta, int limite, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT TRIM(CONCAT(u.nombre, ' ', COALESCE(u.apellido, ''))) AS nombre,
                   COUNT(*) AS facturas,
                   COALESCE(SUM(f.total), 0.00) AS total
            FROM {DbNames.Factura} f
            JOIN {DbNames.Usuario} u ON u.id = f.usuario_id
            WHERE f.estado = 'emitida'
              AND {DiaNegocio} BETWEEN @desde AND @hasta
            GROUP BY f.usuario_id, nombre
            ORDER BY total DESC
            LIMIT @limite;
            """;
        AgregarRango(cmd, desde, hasta);
        cmd.Parameters.AddWithValue("@limite", limite);

        var lista = new List<VendedorRanking>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new VendedorRanking(
                reader.GetString("nombre"),
                reader.GetInt32("facturas"),
                reader.GetDecimal("total")));
        return lista;
    }

    private async Task<List<ProductoRanking>> TopProductosAsync(
        MySqlConnection conexion, DateOnly desde, DateOnly hasta, int limite, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.nombre,
                   COALESCE(SUM(d.cantidad), 0) AS unidades,
                   COALESCE(SUM(d.subtotal), 0.00) AS total
            FROM {DbNames.Detalle} d
            JOIN {DbNames.Factura} f ON f.id = d.factura_id
            JOIN {DbNames.Producto} p ON p.id = d.producto_id
            WHERE f.estado = 'emitida'
              AND {DiaNegocio} BETWEEN @desde AND @hasta
            GROUP BY d.producto_id, p.nombre
            ORDER BY unidades DESC
            LIMIT @limite;
            """;
        AgregarRango(cmd, desde, hasta);
        cmd.Parameters.AddWithValue("@limite", limite);

        var lista = new List<ProductoRanking>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add(new ProductoRanking(
                reader.GetString("nombre"),
                Convert.ToInt32(reader["unidades"]),
                reader.GetDecimal("total")));
        return lista;
    }

    private static async Task<int> ContarPorCaducarAsync(
        MySqlConnection conexion, DateOnly hoy, int dias, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Producto}
            WHERE deleted_at IS NULL
              AND fecha_caducidad IS NOT NULL
              AND fecha_caducidad <= DATE_ADD(@hoy, INTERVAL @dias DAY);
            """;
        cmd.Parameters.AddWithValue("@hoy", hoy.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@dias", dias);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<int> ContarStockBajoAsync(
        MySqlConnection conexion, int umbral, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*) FROM {DbNames.Producto}
            WHERE deleted_at IS NULL AND cantidad <= @umbral;
            """;
        cmd.Parameters.AddWithValue("@umbral", umbral);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static void AgregarRango(MySqlCommand cmd, DateOnly desde, DateOnly hasta)
    {
        cmd.Parameters.AddWithValue("@desde", desde.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@hasta", hasta.ToDateTime(TimeOnly.MinValue));
    }
}
