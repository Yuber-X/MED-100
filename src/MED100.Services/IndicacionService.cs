using MED100.Common;
using MED100.Data;
using MED100.Models;
using Serilog;

namespace MED100.Services;

/// <summary>
/// Medicamentos indicados (013). Pedido de Yuber del 2026-09-06.
///
/// ⚠ CONTENIDO CLÍNICO. La Ley 172-13 clasifica los datos de salud como
/// SENSIBLES. Por eso acá:
///  * TODO pasa por el permiso <c>indicaciones</c>, incluida la CONSULTA. Es la
///    diferencia con los fiados, donde mirar la lista es libre: quién debe
///    plata es administrativo; qué le mandaron a tomar a alguien, no.
///  * Toda alta y toda baja quedan en auditoría con usuario y hora. La ley
///    exige poder decir quién escribió y quién miró qué.
///  * No se borra nada de verdad (soft delete).
///
/// Lo que NO hace y no debe hacer: guardar diagnóstico, evolución ni motivo.
/// Se registra QUÉ se indicó, nunca POR QUÉ (CLAUDE.md §1.1).
/// </summary>
public class IndicacionService
{
    private readonly IndicacionRepository _indicaciones;
    private readonly AuditoriaService _auditoria;

    public IndicacionService(IndicacionRepository indicaciones, AuditoriaService auditoria)
    {
        _indicaciones = indicaciones;
        _auditoria = auditoria;
    }

    private static void ExigirPermiso()
    {
        if (!SesionActual.TienePermiso("indicaciones"))
            throw new UnauthorizedAccessException(
                "No tienes permiso para ver ni registrar los medicamentos indicados.");
    }

    public Task<IReadOnlyList<Indicacion>> ObtenerDelDiaAsync(DateOnly dia,
        CancellationToken ct = default)
    {
        ExigirPermiso();
        return _indicaciones.ObtenerDelDiaAsync(dia, ct);
    }

    public Task<IReadOnlyList<Indicacion>> ObtenerDeClienteAsync(long clienteId,
        CancellationToken ct = default)
    {
        ExigirPermiso();
        return _indicaciones.ObtenerDeClienteAsync(clienteId, ct);
    }

    public async Task<long> CrearAsync(Indicacion indicacion, CancellationToken ct = default)
    {
        ExigirPermiso();

        if (indicacion.ClienteId <= 0)
            throw new ArgumentException("Elegí a qué paciente se le indicó.");

        // Se limpian los renglones vacíos ANTES de contar: la pantalla arranca
        // con filas en blanco para escribir, y si no se filtran, "guardar" con
        // todo vacío pasaría la validación con tres medicamentos fantasma.
        indicacion.Medicamentos = indicacion.Medicamentos
            .Where(m => !string.IsNullOrWhiteSpace(m.Medicamento))
            .ToList();

        if (indicacion.Medicamentos.Count == 0)
            throw new ArgumentException("Escribí al menos un medicamento.");

        indicacion.UsuarioId = SesionActual.Id;
        indicacion.FechaUtc = DateTime.UtcNow;

        var id = await _indicaciones.CrearAsync(indicacion, ct);

        // La auditoría NOMBRA los medicamentos. Es a propósito: si alguien tiene
        // que responder por lo que se indicó, "se registró una indicación" no
        // sirve de nada.
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Indicacion, id,
            $"Medicamentos indicados a {indicacion.ClienteNombre}" +
            (indicacion.MedicoNombre is { } medico ? $" por {medico}" : "") +
            $": {string.Join("; ", indicacion.Medicamentos.Select(m => m.Resumen))}", ct);

        Log.Information("Indicación {Id} registrada para el cliente {ClienteId} ({Cantidad} medicamentos)",
            id, indicacion.ClienteId, indicacion.Medicamentos.Count);
        return id;
    }

    /// <summary>
    /// Da de baja una indicación cargada por error. Solo el Admin: corregir lo
    /// que consta que se le indicó a un paciente no es una operación de
    /// mostrador.
    /// </summary>
    public async Task EliminarAsync(long id, string motivo, CancellationToken ct = default)
    {
        if (!SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Solo un administrador puede dar de baja una indicación.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new ArgumentException("Escribí por qué se da de baja.");

        await _indicaciones.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Indicacion, id,
            $"Indicación dada de baja: {motivo.Trim()}", ct);
    }
}
