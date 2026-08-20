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
              -- Lo que ENTRA a la caja es paciente_paga, no total: lo que
              -- cubre la ARS se cobra por otra via y en otro momento
              -- (CLAUDE.md 1.3.3). Sin ARS, paciente_paga == total, asi que
              -- para una clinica sin seguros no cambia nada.
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'efectivo'
                                THEN paciente_paga END), 0.00)                          AS efectivo,
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'tarjeta'
                                THEN paciente_paga END), 0.00)                                  AS tarjeta,
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'transferencia'
                                THEN paciente_paga END), 0.00)                                  AS transferencia,
              COALESCE(SUM(CASE WHEN estado = 'emitida' AND metodo_pago = 'mixto'
                                THEN paciente_paga END), 0.00)                                  AS mixto,
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

        var resumen = new CuadreResumen(
            usuarioId, nombreCajero, fecha,
            Convert.ToInt32(reader["facturas"]),
            reader.GetDecimal("vendido"),
            reader.GetDecimal("efectivo"),
            reader.GetDecimal("tarjeta"),
            reader.GetDecimal("transferencia"),
            reader.GetDecimal("mixto"),
            Convert.ToInt32(reader["anuladas"]),
            reader.GetDecimal("monto_anulado"),
            TiempoActivoSegundos: 0,
            YaCerrado: false);
        await reader.CloseAsync();

        var tiempo = await CalcularTiempoActivoAsync(conexion, usuarioId, fecha, ct);
        var cerrado = await EstaCerradoAsync(conexion, usuarioId, fecha, ct);
        return resumen with { TiempoActivoSegundos = tiempo, YaCerrado = cerrado };
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
