using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Deudas de pacientes (012). Pedido de Yuber del 2026-09-06.
///
/// El permiso <c>fiados</c> gobierna DEJAR una deuda y COBRARLA. Va aparte de
/// <c>vender</c> a propósito: decidir a quién se le fía es una decisión de
/// crédito, y en una clínica chica no todo el que cobra en el mostrador la toma.
///
/// Consultar la lista NO exige el permiso: saber quién debe es información que
/// cualquiera en recepción necesita para atender al paciente que llega.
/// </summary>
public class FiadoService
{
    private readonly FiadoRepository _fiados;
    private readonly AuditoriaService _auditoria;

    public FiadoService(FiadoRepository fiados, AuditoriaService auditoria)
    {
        _fiados = fiados;
        _auditoria = auditoria;
    }

    public Task<IReadOnlyList<FiadoResumen>> ObtenerPendientesAsync(CancellationToken ct = default) =>
        _fiados.ObtenerPendientesAsync(ct);

    public Task<IReadOnlyList<FiadoResumen>> ObtenerPendientesDeClienteAsync(
        long clienteId, CancellationToken ct = default) =>
        _fiados.ObtenerPendientesDeClienteAsync(clienteId, ct);

    public Task<IReadOnlyList<FacturaAbono>> ObtenerAbonosAsync(long facturaId,
        CancellationToken ct = default) =>
        _fiados.ObtenerAbonosAsync(facturaId, ct);

    /// <summary>
    /// Cobra parte o todo de una deuda. Devuelve el saldo que queda.
    ///
    /// La validación de fondo (que el abono no supere el saldo, que la factura
    /// no esté anulada) vive en el repositorio y no acá: necesita el bloqueo de
    /// fila para ser cierta con dos cajeros a la vez, y una comprobación previa
    /// sin bloqueo daría una falsa sensación de seguridad.
    /// </summary>
    public async Task<decimal> RegistrarAbonoAsync(long facturaId, decimal monto,
        MetodoPagoFactura metodo, string? notas = null, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("fiados"))
            throw new UnauthorizedAccessException("No tienes permiso para cobrar deudas.");
        if (monto <= 0m)
            throw new ArgumentException("El abono tiene que ser mayor que cero.");

        var saldo = await _fiados.RegistrarAbonoAsync(facturaId, SesionActual.Id, monto,
            metodo, notas, ct);

        var cerrada = saldo <= 0m;
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Factura, facturaId,
            $"Abono de {monto:0.00} ({metodo}) a la deuda. " +
            (cerrada ? "Queda SALDADA." : $"Saldo restante {saldo:0.00}."), ct);

        Log.Information("Abono de {Monto} a la factura {FacturaId}; saldo {Saldo}",
            monto, facturaId, saldo);
        return saldo;
    }

    /// <summary>Mueve la fecha acordada de pago (el clásico "pasá el viernes").</summary>
    public async Task ActualizarCompromisoAsync(long facturaId, DateOnly nueva,
        CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("fiados"))
            throw new UnauthorizedAccessException("No tienes permiso para administrar deudas.");
        if (nueva < FechaNegocio.Hoy)
            throw new ArgumentException("La nueva fecha de pago no puede ser anterior a hoy.");

        await _fiados.ActualizarCompromisoAsync(facturaId, nueva, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Factura, facturaId,
            $"Fecha de pago de la deuda movida al {nueva:dd/MM/yyyy}", ct);
    }
}
