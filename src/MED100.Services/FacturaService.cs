using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Consulta y anulación de comprobantes.
///
/// Permisos (spec §6):
///  · 'comprobantes'        → ve SOLO sus propias facturas
///  · 'comprobantes_todos'  → ve las de todos los cajeros
///  · 'facturas_anular'     → puede anular (permiso especial)
///
/// Una factura JAMÁS se borra (spec §9.3): se anula, se devuelve el stock y
/// queda en el historial con su motivo. Todo en una transacción y auditado.
/// </summary>
public class FacturaService
{
    private readonly FacturaRepository _facturas;
    private readonly AuditoriaService _auditoria;

    public FacturaService(FacturaRepository facturas, AuditoriaService auditoria)
    {
        _facturas = facturas;
        _auditoria = auditoria;
    }

    /// <summary>True si el usuario puede ver los comprobantes de todos los cajeros.</summary>
    public static bool PuedeVerTodos => SesionActual.TienePermiso("comprobantes_todos");

    public static bool PuedeAnular => SesionActual.TienePermiso("facturas_anular");

    /// <summary>
    /// Busca comprobantes. Si el usuario no tiene 'comprobantes_todos', el
    /// filtro se fuerza a su propio id: un Cajero no puede ver las ventas
    /// de otro ni manipulando la UI.
    /// </summary>
    public Task<List<FacturaResumen>> BuscarAsync(FiltroComprobantes filtro, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("comprobantes"))
            throw new InvalidOperationException("No tienes permiso para consultar comprobantes.");

        var efectivo = PuedeVerTodos
            ? filtro
            : filtro with { UsuarioId = SesionActual.Id };

        return _facturas.BuscarAsync(efectivo, ct);
    }

    /// <summary>Factura completa (detalle + reimpresión). Valida el mismo alcance.</summary>
    public async Task<FacturaCompleta?> ObtenerCompletaAsync(long facturaId, CancellationToken ct = default)
    {
        var factura = await _facturas.ObtenerCompletaAsync(facturaId, ct);
        if (factura is null)
            return null;

        if (!PuedeVerTodos && factura.Resumen.UsuarioId != SesionActual.Id)
            throw new InvalidOperationException("Solo puedes consultar tus propios comprobantes.");

        return factura;
    }

    /// <summary>
    /// Anula una factura: estado='anulada' + motivo, devuelve el stock al
    /// inventario y audita — todo en UNA transacción. Anular dos veces es
    /// imposible: el UPDATE exige estado='emitida'.
    /// </summary>
    public async Task AnularAsync(long facturaId, string motivo, CancellationToken ct = default)
    {
        if (!PuedeAnular)
            throw new InvalidOperationException("No tienes permiso para anular facturas.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Indica el motivo de la anulación (queda en el historial).");

        var factura = await _facturas.ObtenerCompletaAsync(facturaId, ct)
            ?? throw new InvalidOperationException("La factura no existe.");
        if (factura.Resumen.Estado == EstadoFactura.Anulada)
            throw new InvalidOperationException(
                $"La factura {factura.Resumen.NumeroFactura} ya estaba anulada.");

        using var conexion = await _facturas.AbrirConexionAsync(ct);
        using var transaccion = await conexion.BeginTransactionAsync(ct);
        try
        {
            var anulada = await FacturaRepository.MarcarAnuladaAsync(
                conexion, transaccion, facturaId, motivo.Trim(), DateTime.UtcNow, ct);
            if (!anulada)
                throw new InvalidOperationException(
                    "La factura ya fue anulada por otro usuario. Actualiza la lista.");

            await FacturaRepository.DevolverStockAsync(conexion, transaccion, facturaId, ct);

            await _auditoria.RegistrarEnTransaccionAsync(AccionAuditoria.Anular, DbNames.Factura, facturaId,
                $"Factura {factura.Resumen.NumeroFactura} ANULADA (total {factura.Totales.Total:0.00}). " +
                $"Motivo: {motivo.Trim()}. Stock devuelto: {factura.Lineas.Count} producto(s).",
                conexion, transaccion, ct);

            await transaccion.CommitAsync(ct);
        }
        catch
        {
            await transaccion.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Convierte una factura leída de BD al formato que consume el ticket,
    /// para poder REIMPRIMIRLA idéntica a la original.
    /// </summary>
    public static VentaResultado AVentaResultado(FacturaCompleta factura) => new(
        factura.Resumen.Id,
        factura.Resumen.NumeroFactura,
        factura.Resumen.FechaEmisionUtc,
        // El descuento NO se guarda en la cabecera: se reconstruye de las
        // líneas, que es de donde salió. Así la reimpresión de una factura
        // rebajada dice lo mismo que el papel original sin agregar una columna
        // que podría quedar desincronizada con su propio detalle.
        factura.Totales with { Descuento = factura.Lineas.Sum(DescuentoDe) },
        factura.EfectivoRecibido,
        factura.Cambio,
        // Se conserva si era procedimiento o insumo y si iba exento: la
        // reimpresión tiene que salir idéntica al papel original.
        [.. factura.Lineas.Select(l =>
            new VentaLinea(l.ProductoId, l.NombreProducto, l.Cantidad, l.PrecioUnitario,
                l.Exento, l.ProcedimientoId, l.PrecioCatalogo))],
        factura.Resumen.NombreCliente,
        factura.Resumen.MetodoPago,
        factura.Honorario,
        factura.Ars,
        factura.Ncf,
        // El paciente viaja hasta acá para que la copia en PDF sepa a qué
        // expediente va cuando se archiva una factura vieja a mano.
        factura.Resumen.ClienteId);

    private static decimal DescuentoDe(FacturaLinea linea) =>
        linea.PrecioCatalogo is { } lista && lista > linea.PrecioUnitario
            ? (lista - linea.PrecioUnitario) * linea.Cantidad
            : 0m;
}
