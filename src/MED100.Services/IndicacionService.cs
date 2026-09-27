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

    /// <summary>
    /// Los medicamentos que más se indican, para elegirlos en vez de tipearlos
    /// (pedido de la clínica 2026-09-21). Pasa por el mismo permiso que todo lo
    /// demás: la lista sale del historial de pacientes reales.
    /// </summary>
    public Task<IReadOnlyList<MedicamentoFrecuente>> ObtenerMasUsadosAsync(int tope = 30,
        CancellationToken ct = default)
    {
        ExigirPermiso();
        return _indicaciones.ObtenerMasUsadosAsync(tope, ct);
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
    /// Corrige los medicamentos de una indicación ya guardada: quitar uno,
    /// arreglar una dosis o agregar el que faltó (pedido de la clínica
    /// 2026-09-21: <i>"la opción de poder quitar o modificar medicamentos una
    /// vez agregado, es decir si deseo quitar solo uno de la lista"</i>).
    ///
    /// Lo de HOY lo corrige quien lo carga: es el renglón que se acaba de
    /// tipear mal, y obligar a llamar al Admin para eso llevaría a no
    /// corregirlo. Lo de días anteriores ya es reescribir historial y queda
    /// para el Admin, igual que dar de baja.
    ///
    /// La auditoría guarda el ANTES y el DESPUÉS con los nombres. Sin eso, una
    /// corrección legítima sería indistinguible de borrar lo que molestaba.
    /// </summary>
    public async Task ActualizarMedicamentosAsync(Indicacion indicacion,
        IReadOnlyList<IndicacionMedicamento> nuevos, CancellationToken ct = default)
    {
        ExigirPermiso();

        var limpios = nuevos
            .Where(m => !string.IsNullOrWhiteSpace(m.Medicamento))
            .ToList();

        if (limpios.Count == 0)
            throw new ArgumentException(
                "Tiene que quedar al menos un medicamento. Si no queda ninguno, " +
                "lo que corresponde es dar de baja la indicación completa.");

        var dia = DateOnly.FromDateTime(FechaNegocio.AUtcLocal(indicacion.FechaUtc));
        if (dia != FechaNegocio.Hoy && !SesionActual.EsAdmin)
            throw new UnauthorizedAccessException(
                "Esta indicación no es de hoy: solo un administrador puede corregirla.");

        var antes = Resumir(indicacion.Medicamentos);
        await _indicaciones.ReemplazarMedicamentosAsync(indicacion.Id, limpios, ct);
        var ahora = Resumir(limpios);

        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Indicacion, indicacion.Id,
            $"Medicamentos corregidos a {indicacion.ClienteNombre}. " +
            $"Antes: {antes}. Ahora: {ahora}", ct);

        Log.Information("Indicación {Id} corregida ({Antes} -> {Ahora})",
            indicacion.Id, antes, ahora);
    }

    private static string Resumir(IEnumerable<IndicacionMedicamento> medicamentos)
    {
        var texto = string.Join("; ", medicamentos.Select(m => m.Resumen));
        return string.IsNullOrWhiteSpace(texto) ? "(nada)" : texto;
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
