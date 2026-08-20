using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Cuadre de caja. Permisos (spec §6):
///  · 'cuadre'        → solo su propio turno
///  · 'cuadre_todos'  → el de cualquier cajero
/// Una vez cerrado es INMUTABLE (spec §9.8).
/// </summary>
public class CuadreService
{
    private readonly CuadreRepository _cuadres;
    private readonly AuditoriaService _auditoria;

    public CuadreService(CuadreRepository cuadres, AuditoriaService auditoria)
    {
        _cuadres = cuadres;
        _auditoria = auditoria;
    }

    public static bool PuedeVerTodos => SesionActual.TienePermiso("cuadre_todos");

    public Task<List<(long Id, string Nombre)>> ObtenerCajerosAsync(CancellationToken ct = default) =>
        _cuadres.ObtenerCajerosAsync(ct);

    /// <summary>
    /// Cuadre de un cajero en un día. Sin 'cuadre_todos' solo se permite el
    /// propio: un Cajero no puede espiar la caja de otro.
    /// </summary>
    public Task<CuadreResumen> CalcularAsync(long usuarioId, DateOnly fecha, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("cuadre"))
            throw new InvalidOperationException("No tienes permiso para ver el cuadre de caja.");
        if (!PuedeVerTodos && usuarioId != SesionActual.Id)
            throw new InvalidOperationException("Solo puedes ver el cuadre de tu propio turno.");

        return _cuadres.CalcularAsync(usuarioId, fecha, ct);
    }

    /// <summary>
    /// Cuadre GENERAL del día (pedido Yuber 2026-07-12): desglose de todos los
    /// cajeros en una sola vista. Exige 'cuadre_todos' — un Cajero solo ve el suyo.
    /// </summary>
    public Task<CuadreGeneral> CalcularGeneralAsync(DateOnly fecha, CancellationToken ct = default)
    {
        if (!PuedeVerTodos)
            throw new InvalidOperationException(
                "Solo un Supervisor o Administrador puede ver el cuadre general.");

        return _cuadres.CalcularGeneralAsync(fecha, ct);
    }

    /// <summary>
    /// Cierra de una vez los turnos pendientes del día (botón del cuadre general
    /// y cierre automático). Los ya cerrados se saltan: nunca se duplica.
    /// Devuelve cuántos se cerraron.
    /// </summary>
    public async Task<int> CerrarPendientesDelDiaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        if (!PuedeVerTodos)
            throw new InvalidOperationException(
                "Solo un Supervisor o Administrador puede cerrar la caja de todos.");

        var general = await _cuadres.CalcularGeneralAsync(fecha, ct);
        var cerrados = 0;
        foreach (var cuadre in general.PorCajero.Where(c => !c.YaCerrado))
        {
            await _cuadres.CerrarAsync(cuadre, ct);
            cerrados++;
        }

        if (cerrados > 0)
            await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.CuadreCaja, null,
                $"Cierre general del {fecha:dd/MM/yyyy}: {cerrados} cajero(s), " +
                $"total {general.TotalVendido:0.00}", ct);

        return cerrados;
    }

    /// <summary>Cierra el turno: lo persiste (ya no cambia) y lo audita.</summary>
    public async Task CerrarAsync(CuadreResumen cuadre, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("cuadre"))
            throw new InvalidOperationException("No tienes permiso para cerrar el cuadre.");
        if (!PuedeVerTodos && cuadre.UsuarioId != SesionActual.Id)
            throw new InvalidOperationException("Solo puedes cerrar tu propio turno.");
        if (cuadre.YaCerrado)
            throw new InvalidOperationException(
                $"El cuadre de {cuadre.NombreCajero} del {cuadre.Fecha:dd/MM/yyyy} ya fue cerrado.");

        await _cuadres.CerrarAsync(cuadre, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.CuadreCaja, cuadre.UsuarioId,
            $"Cuadre cerrado — {cuadre.NombreCajero}, {cuadre.Fecha:dd/MM/yyyy}: " +
            $"{cuadre.TotalFacturas} facturas, total {cuadre.TotalVendido:0.00} " +
            $"(efectivo {cuadre.TotalEfectivo:0.00}), tiempo activo {cuadre.TiempoActivoTexto}", ct);
    }
}
