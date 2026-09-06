using MySqlConnector;
using MED100.Common;
using MED100.Models;

namespace MED100.Data;

/// <summary>
/// Cuadre de caja: totales del día por cajero. Los montos se calculan en SQL
/// (nunca sumando en la UI) y el día se evalúa por DÍA DE NEGOCIO (UTC-4):
/// una venta de las 11pm pertenece a ese día, no al siguiente en UTC.
/// Las facturas anuladas NO suman al vendido, pero se informan aparte.
/// </summary>
public class CuadreRepository
{
    private readonly ConexionFactory _factory;

    public CuadreRepository(ConexionFactory factory) => _factory = factory;

    /// <summary>Calcula el cuadre en vivo (no lo persiste).</summary>
    public async Task<CuadreResumen> CalcularAsync(long usuarioId, DateOnly fecha, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);

        string nombreCajero;
        using (var cmd = conexion.CreateCommand())
        {
            cmd.CommandText =
                $"SELECT TRIM(CONCAT(nombre, ' ', COALESCE(apellido, ''))) FROM {DbNames.Usuario} WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", usuarioId);
            nombreCajero = (await cmd.ExecuteScalarAsync(ct))?.ToString() ?? "—";
        }

        using var totalesCmd = conexion.CreateCommand();
        totalesCmd.CommandText = $"""
            SELECT
              COALESCE(SUM(estado = 'emitida'), 0)                                      AS facturas,
              COALESCE(SUM(CASE WHEN estado = 'emitida' THEN total END), 0.00)          AS vendido,
              -- Lo que ENTRA a la caja es abonado_inicial, no total:
              --  * lo que cubre la ARS se cobra por otra via y en otro momento
              --    (CLAUDE.md 1.3.3), por eso nunca fue `total` sino paciente_paga;
              --  * y desde la 012 el paciente puede quedar debiendo, asi que
              --    tampoco es paciente_paga sino lo que efectivamente entrego.
              -- Sin ARS y sin fiar, abonado_inicial == paciente_paga == total,
              -- asi que para el dia a dia de una clinica no cambia nada.
              -- Los abonos de deudas viejas se suman aparte (ver mas abajo):
              -- entran a la caja del dia en que se cobran, no a la del dia en
              -- que se facturo.
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'efectivo'
                                THEN abonado_inicial END), 0.00)                        AS efectivo,
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'tarjeta'
                                THEN abonado_inicial END), 0.00)                                AS tarjeta,
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'transferencia'
                                THEN abonado_inicial END), 0.00)                                AS transferencia,
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'mixto'
                                THEN abonado_inicial END), 0.00)                                AS mixto,
              -- Lo que quedo fiado HOY. No entra a la caja: se informa para que
              -- el cajero entienda por que vendio mas de lo que tiene en mano.
              COALESCE(SUM(CASE WHEN estado = 'emitida'
                                THEN paciente_paga - abonado_inicial END), 0.00)        AS fiado,
              COALESCE(SUM(estado = 'anulada'), 0)                                      AS anuladas,
              COALESCE(SUM(CASE WHEN estado = 'anulada' THEN total END), 0.00)          AS monto_anulado
            FROM {DbNames.Factura}
            WHERE usuario_id = @usuarioId
              AND DATE(DATE_SUB(fecha_emision, INTERVAL 4 HOUR)) = @fecha;
            """;
        totalesCmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        totalesCmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));

        using var reader = await totalesCmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);

        var facturas = Convert.ToInt32(reader["facturas"]);
        var vendido = reader.GetDecimal("vendido");
        var efectivo = reader.GetDecimal("efectivo");
        var tarjeta = reader.GetDecimal("tarjeta");
        var transferencia = reader.GetDecimal("transferencia");
        var mixto = reader.GetDecimal("mixto");
        var fiado = reader.GetDecimal("fiado");
        var anuladas = Convert.ToInt32(reader["anuladas"]);
        var montoAnulado = reader.GetDecimal("monto_anulado");
        await reader.CloseAsync();

        // Los abonos de deudas viejas cobrados HOY por este cajero. Se suman a
        // los totales por método de pago porque es plata que está en la caja,
        // pero NO a `vendido`: no se facturó nada nuevo, se cobró algo viejo.
        var abonos = await SumarAbonosDelDiaAsync(conexion, usuarioId, fecha, ct);
        efectivo += abonos.Efectivo;
        tarjeta += abonos.Tarjeta;
        transferencia += abonos.Transferencia;
        mixto += abonos.Mixto;

        var resumen = new CuadreResumen(
            usuarioId, nombreCajero, fecha,
            facturas, vendido, efectivo, tarjeta, transferencia, mixto,
            anuladas, montoAnulado,
            TiempoActivoSegundos: 0,
            YaCerrado: false,
            TotalFiado: fiado,
            TotalAbonosRecibidos: abonos.Total);

        var tiempo = await CalcularTiempoActivoAsync(conexion, usuarioId, fecha, ct);
        var cerrado = await EstaCerradoAsync(conexion, usuarioId, fecha, ct);
        return resumen with { TiempoActivoSegundos = tiempo, YaCerrado = cerrado };
    }

    /// <summary>
    /// Abonos de deudas viejas cobrados en un día, por método de pago.
    /// </summary>
    private record AbonosDelDia(decimal Efectivo, decimal Tarjeta,
        decimal Transferencia, decimal Mixto)
    {
        public decimal Total => Efectivo + Tarjeta + Transferencia + Mixto;
    }

    /// <summary>
    /// Lo que este cajero cobró hoy de deudas de OTROS días (012).
    ///
    /// Se excluyen los abonos de facturas ANULADAS: si la factura se anuló, esa
    /// plata se devolvió y no puede seguir contando como caja del día. El abono
    /// no se borra —queda el registro de que entró y salió—, simplemente no se
    /// suma.
    /// </summary>
    private static async Task<AbonosDelDia> SumarAbonosDelDiaAsync(
        MySqlConnection conexion, long usuarioId, DateOnly fecha, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT
              COALESCE(SUM(CASE WHEN a.metodo_pago = 'efectivo'      THEN a.monto END), 0.00) AS efectivo,
              COALESCE(SUM(CASE WHEN a.metodo_pago = 'tarjeta'       THEN a.monto END), 0.00) AS tarjeta,
              COALESCE(SUM(CASE WHEN a.metodo_pago = 'transferencia' THEN a.monto END), 0.00) AS transferencia,
              COALESCE(SUM(CASE WHEN a.metodo_pago = 'mixto'         THEN a.monto END), 0.00) AS mixto
            FROM {DbNames.FacturaAbono} a
            JOIN {DbNames.Factura} f ON f.id = a.factura_id
            WHERE a.usuario_id = @usuarioId
              AND f.estado = 'emitida'
              AND DATE(DATE_SUB(a.fecha_utc, INTERVAL 4 HOUR)) = @fecha;
            """;
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new AbonosDelDia(0m, 0m, 0m, 0m);

        return new AbonosDelDia(
            reader.GetDecimal("efectivo"),
            reader.GetDecimal("tarjeta"),
            reader.GetDecimal("transferencia"),
            reader.GetDecimal("mixto"));
    }

    /// <summary>
    /// Suma la duración de las sesiones del día. Una sesión aún abierta
    /// (logout_at NULL) cuenta hasta ahora — es el turno en curso.
    /// </summary>
    private static async Task<int> CalcularTiempoActivoAsync(
        MySqlConnection conexion, long usuarioId, DateOnly fecha, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT COALESCE(SUM(TIMESTAMPDIFF(SECOND, login_at,
                       COALESCE(logout_at, UTC_TIMESTAMP()))), 0)
            FROM {DbNames.Sesion}
            WHERE usuario_id = @usuarioId
              AND DATE(DATE_SUB(login_at, INTERVAL 4 HOUR)) = @fecha;
            """;
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<bool> EstaCerradoAsync(
        MySqlConnection conexion, long usuarioId, DateOnly fecha, CancellationToken ct)
    {
        using var cmd = conexion.CreateCommand();
        cmd.CommandText =
            $"SELECT COUNT(*) FROM {DbNames.CuadreCaja} WHERE usuario_id = @usuarioId AND fecha = @fecha;";
        cmd.Parameters.AddWithValue("@usuarioId", usuarioId);
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    /// <summary>
    /// Persiste el cierre. Es inmutable (spec §9.8): la UNIQUE (usuario, fecha)
    /// impide cerrar dos veces el mismo turno.
    /// </summary>
    public async Task CerrarAsync(CuadreResumen cuadre, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO {DbNames.CuadreCaja}
                (usuario_id, fecha, total_facturas, total_vendido, tiempo_activo_segundos)
            VALUES (@usuarioId, @fecha, @facturas, @vendido, @tiempo);
            """;
        cmd.Parameters.AddWithValue("@usuarioId", cuadre.UsuarioId);
        cmd.Parameters.AddWithValue("@fecha", cuadre.Fecha.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@facturas", cuadre.TotalFacturas);
        cmd.Parameters.AddWithValue("@vendido", cuadre.TotalVendido);
        cmd.Parameters.AddWithValue("@tiempo", cuadre.TiempoActivoSegundos);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Cuadre GENERAL del día: un desglose por cajero con ventas + los totales
    /// del negocio. Los totales se suman de los parciales ya calculados en SQL.
    /// </summary>
    public async Task<CuadreGeneral> CalcularGeneralAsync(DateOnly fecha, CancellationToken ct = default)
    {
        var ids = await ObtenerCajerosConActividadAsync(fecha, ct);

        var porCajero = new List<CuadreResumen>();
        foreach (var id in ids)
            porCajero.Add(await CalcularAsync(id, fecha, ct));

        return new CuadreGeneral(
            fecha,
            porCajero,
            porCajero.Sum(c => c.TotalFacturas),
            porCajero.Sum(c => c.TotalVendido),
            porCajero.Sum(c => c.TotalEfectivo),
            porCajero.Sum(c => c.TotalTarjeta),
            porCajero.Sum(c => c.TotalTransferencia),
            porCajero.Sum(c => c.TotalMixto),
            porCajero.Sum(c => c.FacturasAnuladas),
            porCajero.Sum(c => c.MontoAnulado));
    }

    /// <summary>Ids de los cajeros que emitieron o anularon facturas ese día.</summary>
    public async Task<List<long>> ObtenerCajerosConActividadAsync(
        DateOnly fecha, CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT DISTINCT usuario_id
            FROM {DbNames.Factura}
            WHERE DATE(DATE_SUB(fecha_emision, INTERVAL 4 HOUR)) = @fecha
            ORDER BY usuario_id;
            """;
        cmd.Parameters.AddWithValue("@fecha", fecha.ToDateTime(TimeOnly.MinValue));

        var ids = new List<long>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetInt64("usuario_id"));
        return ids;
    }

    /// <summary>Cajeros con actividad en el día (para el selector del Supervisor/Admin).</summary>
    public async Task<List<(long Id, string Nombre)>> ObtenerCajerosAsync(CancellationToken ct = default)
    {
        using var conexion = await _factory.AbrirAsync(ct);
        using var cmd = conexion.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, TRIM(CONCAT(nombre, ' ', COALESCE(apellido, ''))) AS nombre_completo
            FROM {DbNames.Usuario}
            WHERE activo = 1
            ORDER BY nombre;
            """;
        var lista = new List<(long, string)>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lista.Add((reader.GetInt64("id"), reader.GetString("nombre_completo")));
        return lista;
    }
}
