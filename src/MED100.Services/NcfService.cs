using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Comprobante fiscal (NCF) — pedido de Yuber del 2026-09-06, portado de
/// FAControl. Hay DOS caminos y los dos se soportan a la vez:
///
///  * <b>REGISTRAR</b>: pegar el NCF que generó el Facturador Gratuito de la
///    DGII. Es lo que la clínica hace hoy.
///  * <b>ASIGNAR</b>: tomar el siguiente de la secuencia local autorizada.
///    Reserva atómica dentro de la transacción de la factura.
///
/// Cuál manda: si el cajero escribió algo, manda lo escrito. Si dejó la caja
/// vacía y hay secuencia configurada, la app asigna. Si no hay ninguna de las
/// dos, la factura sale sin NCF — que es un recibo interno válido, no un error.
///
/// Un NCF puesto en una factura NO se cambia desde acá: el comprobante emitido
/// es irreversible para la DGII y una corrección es asunto del contador.
/// </summary>
public class NcfService
{
    private readonly NcfRepository _ncf;
    private readonly AuditoriaService _auditoria;

    public NcfService(NcfRepository ncf, AuditoriaService auditoria)
    {
        _ncf = ncf;
        _auditoria = auditoria;
    }

    public Task<NcfSecuencia?> ObtenerSecuenciaAsync(CancellationToken ct = default) =>
        _ncf.ObtenerActivaAsync(ct);

    /// <summary>
    /// El próximo comprobante que entregaría la secuencia, ya formateado
    /// (ej. "B0200000046"), para mostrarlo como marcador en la caja de NCF de
    /// la pantalla de Cobrar.
    ///
    /// Null cuando NO hay que mostrar nada: no hay secuencia, está apagada,
    /// venció o se agotó. Un marcador con un número que la app no va a poder
    /// entregar sería peor que ninguno.
    /// </summary>
    public async Task<string?> ProximoNcfAsync(CancellationToken ct = default)
    {
        try
        {
            var secuencia = await ObtenerSecuenciaAsync(ct);
            if (secuencia is null || !secuencia.Activo)
                return null;
            if (secuencia.EstaVencida(FechaNegocio.Hoy) || secuencia.EstaAgotada)
                return null;
            return secuencia.Formatear(secuencia.Proxima);
        }
        catch (Exception ex)
        {
            // Un marcador es una ayuda visual: si falla, la pantalla sigue.
            Log.Warning(ex, "No se pudo calcular el próximo NCF para el marcador");
            return null;
        }
    }

    /// <summary>
    /// Adopta como predeterminado el comprobante que se acaba de pegar a mano.
    /// Se llama DESPUÉS de que la factura commiteó.
    ///
    /// NUNCA propaga: el cobro ya está guardado y es válido. Que no se haya
    /// podido mover la secuencia es una comodidad — hacerlo estallar acá le
    /// mostraría un error al cajero por una operación que salió bien.
    /// </summary>
    public async Task AdoptarComoPredeterminadaAsync(string? ncfUsado, CancellationToken ct = default)
    {
        try
        {
            if (await _ncf.AdoptarComoPredeterminadaAsync(ncfUsado, ct))
                Log.Information("Secuencia NCF adoptada desde el comprobante {Ncf}", ncfUsado);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo adoptar {Ncf} como secuencia predeterminada", ncfUsado);
        }
    }

    /// <summary>Guarda la configuración de la secuencia (solo Admin) + auditoría.</summary>
    public async Task GuardarSecuenciaAsync(NcfSecuencia secuencia, CancellationToken ct = default)
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Solo un administrador puede configurar la secuencia de comprobantes.");

        var prefijo = secuencia.Prefijo?.Trim().ToUpperInvariant() ?? string.Empty;
        if (prefijo.Length < 3)
            throw new ArgumentException(
                "El prefijo del comprobante debe tener al menos 3 caracteres (ej. B02 o E32).");
        secuencia.Prefijo = prefijo;

        if (secuencia.Largo is < 6 or > 12)
            throw new ArgumentException(
                "El largo de la secuencia debe estar entre 6 y 12 dígitos (8 tradicional, 10 e-CF).");
        if (secuencia.Proxima < 1)
            throw new ArgumentException("La próxima secuencia debe ser 1 o mayor.");
        if (secuencia.FinRango is { } fin && fin < secuencia.Proxima)
            throw new ArgumentException("El fin del rango no puede ser menor que la próxima secuencia.");

        await _ncf.GuardarAsync(secuencia, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.NcfSecuencia, null,
            $"Secuencia NCF {secuencia.Prefijo}: próxima {secuencia.Proxima}" +
            (secuencia.FinRango is { } f ? $", fin {f}" : "") +
            (secuencia.Vencimiento is { } v ? $", vence {v:dd/MM/yyyy}" : "") +
            (secuencia.Activo ? "" : " — DESACTIVADA"), ct);
        Log.Information("Secuencia NCF {Prefijo} guardada (próxima {Proxima})",
            secuencia.Prefijo, secuencia.Proxima);
    }

    /// <summary>Apaga la numeración local (solo Admin) + auditoría.</summary>
    public async Task DesactivarAsync(CancellationToken ct = default)
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Solo un administrador puede desactivar la secuencia de comprobantes.");
        await _ncf.DesactivarTodasAsync(ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.NcfSecuencia, null,
            "Numeración local de comprobantes desactivada: el NCF vuelve a escribirse a mano", ct);
    }
}
