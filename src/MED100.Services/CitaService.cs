using System.Globalization;
using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Reglas de la agenda contra la base: valida con <see cref="AgendaMedico"/>
/// (que es cálculo puro) y deja constancia en auditoría.
///
/// Todo lo que entra y sale de acá está en HORA LOCAL de RD. La traducción a
/// UTC la hace el repositorio.
/// </summary>
public class CitaService
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly CitaRepository _citas;
    private readonly MedicoRepository _medicos;
    private readonly ProcedimientoRepository _procedimientos;
    private readonly AuditoriaService _auditoria;

    public CitaService(CitaRepository citas, MedicoRepository medicos,
        ProcedimientoRepository procedimientos, AuditoriaService auditoria)
    {
        _citas = citas;
        _medicos = medicos;
        _procedimientos = procedimientos;
        _auditoria = auditoria;
    }

    public Task<List<Cita>> ObtenerPorDiaAsync(DateOnly dia, long? medicoId = null,
        CancellationToken ct = default) =>
        _citas.ObtenerPorDiaAsync(dia, medicoId, ct);

    public Task<List<Cita>> ObtenerDelPacienteAsync(long clienteId, CancellationToken ct = default) =>
        _citas.ObtenerDelPacienteAsync(clienteId, ct);

    public Task<Cita?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _citas.ObtenerPorIdAsync(id, ct);

    /// <summary>
    /// Huecos libres de un médico en un día para una duración dada. Es lo que
    /// la pantalla ofrece en vez de hacer adivinar la hora.
    /// </summary>
    public async Task<IReadOnlyList<HuecoAgenda>> HuecosLibresAsync(long medicoId, DateOnly dia,
        int duracionMinutos, long? exceptoCitaId = null, CancellationToken ct = default)
    {
        var horarios = await _medicos.ObtenerHorariosAsync(medicoId, ct);
        var citas = await _citas.ObtenerDelMedicoEnDiaAsync(medicoId, dia, ct);
        return AgendaMedico.HuecosLibres(dia, duracionMinutos, horarios, citas, exceptoCitaId);
    }

    /// <summary>
    /// Duración sugerida: la del procedimiento elegido. Es el dato que ya está
    /// cargado en el tarifario y evita que la recepción tenga que acordarse de
    /// cuánto dura cada cosa.
    /// </summary>
    public async Task<int> DuracionSugeridaAsync(long? procedimientoId, CancellationToken ct = default)
    {
        const int consultaGeneral = 30;
        if (procedimientoId is not { } id)
            return consultaGeneral;
        var procedimiento = await _procedimientos.ObtenerPorIdAsync(id, ct);
        return procedimiento?.DuracionMinutos ?? consultaGeneral;
    }

    public async Task<long> CrearAsync(CitaDatos datos, CancellationToken ct = default)
    {
        await ValidarAsync(datos, exceptoCitaId: null, ct);

        var id = await _citas.InsertarAsync(datos, ct);
        var creada = await _citas.ObtenerPorIdAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Cita, id,
            $"Cita creada: {Describir(creada)}", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, CitaDatos datos, CancellationToken ct = default)
    {
        var anterior = await _citas.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("La cita no existe o fue eliminada.");

        if (anterior.FacturaId is not null)
            throw new InvalidOperationException(
                "Esa cita ya se cobró y no se puede mover. Si hay que corregirla, hay que anular la factura.");

        await ValidarAsync(datos, exceptoCitaId: id, ct);
        await _citas.ActualizarAsync(id, datos, ct);

        var detalle = $"Cita modificada: {Describir(anterior)}";
        var nuevaLocal = datos.FechaHoraLocal;
        var anteriorLocal = FechaNegocio.AUtcLocal(anterior.FechaHoraUtc);
        if (anteriorLocal != nuevaLocal)
            detalle += $" · movida a {Formatear(nuevaLocal)}";
        if (anterior.MedicoId != datos.MedicoId)
            detalle += " · cambió de médico";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Cita, id, detalle, ct);
    }

    /// <summary>
    /// Cambia el estado (confirmada, atendida, cancelada, no asistió). Solo se
    /// aceptan las transiciones que tienen sentido: lo que ya ocurrió no vuelve
    /// atrás.
    /// </summary>
    public async Task CambiarEstadoAsync(long id, EstadoCita nuevo, CancellationToken ct = default)
    {
        ValidarPermiso();
        var cita = await _citas.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("La cita no existe o fue eliminada.");

        if (cita.Estado == nuevo)
            return;

        if (!AgendaMedico.SiguientesEstados(cita.Estado).Contains(nuevo))
            throw new InvalidOperationException(
                $"Una cita {AgendaMedico.EtiquetaEstado(cita.Estado).ToLower(CulturaRd)} " +
                $"no puede pasar a {AgendaMedico.EtiquetaEstado(nuevo).ToLower(CulturaRd)}.");

        await _citas.CambiarEstadoAsync(id, nuevo, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Cita, id,
            $"Cita {AgendaMedico.EtiquetaEstado(cita.Estado).ToLower(CulturaRd)} → " +
            $"{AgendaMedico.EtiquetaEstado(nuevo).ToLower(CulturaRd)}: {Describir(cita)}", ct);
    }

    /// <summary>
    /// Deshace un estado final puesto por error y devuelve la cita a
    /// «Programada».
    ///
    /// Pedido del cliente (2026-08-28): <i>"si uno elige algo por error o se
    /// arrepiente. No puede cancelar o darle para atrás"</i>. Marcar «No
    /// asistió» de un clic dejaba la cita muerta sin vuelta.
    ///
    /// Dos protecciones, y las dos importan:
    ///  - <b>Si ya se cobró, no se toca.</b> Es la misma regla que en
    ///    <see cref="ActualizarAsync"/> y <see cref="EliminarAsync"/>: una cita
    ///    unida a una factura se corrige anulando la factura, no reabriendo la
    ///    agenda.
    ///  - <b>Se revalida el hueco.</b> Una cita cancelada LIBERA su lugar, y
    ///    puede haber otro paciente ahí desde entonces. Reactivarla a ciegas
    ///    crearía dos citas encima y el choque aparecería recién el día de la
    ///    consulta, con los dos pacientes en la sala.
    ///
    /// No se revalida el horario del médico ni que la fecha sea futura: la cita
    /// ya existía con esos datos y lo que se corrige es la marca, no la cita.
    /// </summary>
    public async Task RevertirEstadoAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var cita = await _citas.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("La cita no existe o fue eliminada.");

        if (AgendaMedico.EstadoAlDeshacer(cita.Estado) is not { } destino)
            throw new InvalidOperationException(
                $"Una cita {AgendaMedico.EtiquetaEstado(cita.Estado).ToLower(CulturaRd)} " +
                "no tiene nada que deshacer.");

        if (cita.FacturaId is not null)
            throw new InvalidOperationException(
                "Esa cita ya se cobró y no se puede reabrir.\n\n" +
                "Si hay que corregirla, primero se anula la factura.");

        var local = FechaNegocio.AUtcLocal(cita.FechaHoraUtc);
        var delDia = await _citas.ObtenerDelMedicoEnDiaAsync(
            cita.MedicoId, DateOnly.FromDateTime(local), ct);

        if (AgendaMedico.ChoqueDeAgenda(local, cita.DuracionMinutos, delDia, cita.Id) is { } choque)
            throw new InvalidOperationException(
                "No se puede reabrir: ese lugar ya lo tomó otro paciente.\n\n" +
                AgendaMedico.DescribirChoque(choque) + "\n\n" +
                "Agendala de nuevo en un hueco libre.");

        await _citas.CambiarEstadoAsync(id, destino, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Cita, id,
            $"Se deshizo «{AgendaMedico.EtiquetaEstado(cita.Estado)}»: la cita vuelve a " +
            $"{AgendaMedico.EtiquetaEstado(destino).ToLower(CulturaRd)}. {Describir(cita)}", ct);
    }

    /// <summary>
    /// Elimina una cita agendada por error. Una cita ya cobrada no se toca: eso
    /// se arregla anulando la factura, no borrando la agenda.
    /// </summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var cita = await _citas.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("La cita no existe o ya fue eliminada.");

        if (cita.FacturaId is not null)
            throw new InvalidOperationException(
                "Esa cita ya se cobró y no se puede eliminar.\n\n" +
                "Si el paciente no vino, marcala como «No asistió»; el historial se conserva.");

        if (!await _citas.EliminarAsync(id, ct))
            throw new InvalidOperationException("No se pudo eliminar la cita.");

        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Cita, id,
            $"Cita eliminada: {Describir(cita)}", ct);
    }

    private async Task ValidarAsync(CitaDatos datos, long? exceptoCitaId, CancellationToken ct)
    {
        ValidarPermiso();

        var medico = await _medicos.ObtenerPorIdAsync(datos.MedicoId, ct)
            ?? throw new ArgumentException("El médico no existe o fue eliminado.");
        if (!medico.Activo)
            throw new ArgumentException(
                $"{medico.Nombre} está inactivo y no se le pueden agendar citas.");

        if (datos.ProcedimientoId is { } procedimientoId)
        {
            var procedimiento = await _procedimientos.ObtenerPorIdAsync(procedimientoId, ct)
                ?? throw new ArgumentException("El procedimiento no existe o fue eliminado.");
            if (!procedimiento.Activo)
                throw new ArgumentException(
                    $"«{procedimiento.Nombre}» está inactivo y no se puede agendar.");
        }

        var horarios = await _medicos.ObtenerHorariosAsync(datos.MedicoId, ct);
        var dia = DateOnly.FromDateTime(datos.FechaHoraLocal);
        var citas = await _citas.ObtenerDelMedicoEnDiaAsync(datos.MedicoId, dia, ct);

        AgendaMedico.Validar(datos.FechaHoraLocal, datos.DuracionMinutos,
            horarios, citas, exceptoCitaId);
    }

    private static void ValidarPermiso()
    {
        if (!SesionActual.TienePermiso("citas"))
            throw new InvalidOperationException("No tienes permiso para gestionar la agenda.");
    }

    private static string Describir(Cita? cita) => cita is null
        ? "(cita no encontrada)"
        : $"{cita.PacienteNombre} con {cita.MedicoNombre} el " +
          $"{Formatear(FechaNegocio.AUtcLocal(cita.FechaHoraUtc))}";

    private static string Formatear(DateTime local) =>
        local.ToString("dd/MM/yyyy h:mm tt", CulturaRd);
}
