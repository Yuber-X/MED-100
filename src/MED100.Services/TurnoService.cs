using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Sala de espera.
///
/// La regla de fondo (CLAUDE.md §1.3.4): <b>el turno y la factura son dos
/// papeles distintos</b>. El paciente se lleva el turno al entrar, antes de que
/// exista factura y a veces antes de estar registrado. Por eso acá nada exige
/// paciente, ni médico, ni cita: se da el número y listo.
/// </summary>
public class TurnoService
{
    private readonly TurnoRepository _turnos;
    private readonly AuditoriaService _auditoria;
    private readonly ConfiguracionNegocioService _negocio;

    public TurnoService(TurnoRepository turnos, AuditoriaService auditoria,
        ConfiguracionNegocioService negocio)
    {
        _turnos = turnos;
        _auditoria = auditoria;
        _negocio = negocio;
    }

    public Task<List<Turno>> ObtenerDelDiaAsync(DateOnly? dia = null, CancellationToken ct = default) =>
        _turnos.ObtenerPorDiaAsync(dia ?? FechaNegocio.Hoy, ct);

    public Task<ResumenSala> ResumenAsync(DateOnly? dia = null, CancellationToken ct = default) =>
        _turnos.ResumenAsync(dia ?? FechaNegocio.Hoy, ct);

    public Task<Turno?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _turnos.ObtenerPorIdAsync(id, ct);

    public Task<Turno?> SiguienteEnEsperaAsync(DateOnly? dia = null, CancellationToken ct = default) =>
        _turnos.SiguienteEnEsperaAsync(dia ?? FechaNegocio.Hoy, ct);

    /// <summary>
    /// Da el siguiente turno del día de negocio. Siempre es HOY: dar un turno
    /// para mañana no tiene sentido — eso es una cita.
    /// </summary>
    public async Task<Turno> DarAsync(TurnoDatos datos, CancellationToken ct = default)
    {
        ValidarPermiso();
        var turno = await _turnos.DarSiguienteAsync(FechaNegocio.Hoy, datos, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Turno, turno.Id,
            $"Turno {Etiqueta(turno)} entregado" +
            (turno.PacienteNombre is { } p ? $" a {p}" : " (sin paciente asignado)"), ct);
        return turno;
    }

    /// <summary>
    /// Llama a un turno puntual. Sella <c>llamado_at</c> la primera vez, que es
    /// de donde después sale cuánto esperó la gente.
    /// </summary>
    public async Task<Turno> LlamarAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var turno = await _turnos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El turno no existe.");

        if (turno.EsFinal)
            throw new InvalidOperationException(
                $"El turno {Etiqueta(turno)} ya está {EtiquetaEstado(turno.Estado).ToLowerInvariant()}. " +
                "Si volvió, dale un turno nuevo.");

        await _turnos.CambiarEstadoAsync(id, EstadoTurno.Llamado, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Turno, id,
            $"Turno {Etiqueta(turno)} llamado", ct);

        return await _turnos.ObtenerPorIdAsync(id, ct)!
            ?? throw new InvalidOperationException("El turno desapareció al llamarlo.");
    }

    /// <summary>
    /// Llama al que sigue: el de menor número que todavía espera. Devuelve null
    /// si la sala está vacía.
    /// </summary>
    public async Task<Turno?> LlamarSiguienteAsync(CancellationToken ct = default)
    {
        ValidarPermiso();
        var siguiente = await _turnos.SiguienteEnEsperaAsync(FechaNegocio.Hoy, ct);
        return siguiente is null ? null : await LlamarAsync(siguiente.Id, ct);
    }

    /// <summary>Cierra el turno: atendido o ausente (se llamó y no contestó).</summary>
    public async Task CerrarAsync(long id, EstadoTurno estadoFinal, CancellationToken ct = default)
    {
        ValidarPermiso();
        if (estadoFinal is not (EstadoTurno.Atendido or EstadoTurno.Ausente))
            throw new ArgumentException("Un turno solo se cierra como atendido o ausente.");

        var turno = await _turnos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El turno no existe.");
        if (turno.EsFinal)
            return;

        await _turnos.CambiarEstadoAsync(id, estadoFinal, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Turno, id,
            $"Turno {Etiqueta(turno)} → {EtiquetaEstado(estadoFinal).ToLowerInvariant()}", ct);
    }

    /// <summary>
    /// Le pone paciente a un turno que se dio en blanco. Es el caso normal: el
    /// número se entrega en la puerta y el registro se hace después.
    /// </summary>
    public async Task AsignarPacienteAsync(long id, long clienteId, CancellationToken ct = default)
    {
        ValidarPermiso();
        var turno = await _turnos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El turno no existe.");

        await _turnos.AsignarPacienteAsync(id, clienteId, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Turno, id,
            $"Turno {Etiqueta(turno)} asignado a un paciente", ct);
    }

    /// <summary>
    /// Cómo se lee el turno en el papel y en la pantalla.
    ///
    /// Manda el código del MÉDICO si el turno tiene uno asignado ("YO-15"):
    /// es el pedido de Yuber del 2026-08-14, y sirve para que en la sala se
    /// vea a simple vista a quién va cada número cuando hay dos o tres médicos
    /// atendiendo a la vez. Sin médico se cae al prefijo general del negocio
    /// ("A-15") y, si tampoco hay, al número pelado ("15").
    ///
    /// El número NO se reinicia por médico: sigue siendo uno por día para toda
    /// la clínica. Numerar por médico haría que dos personas sentadas en la
    /// misma sala tuvieran ambas el "3", que es exactamente la discusión que
    /// el turno viene a evitar.
    /// </summary>
    public string Etiqueta(Turno turno) => Etiqueta(turno.Numero, turno.MedicoCodigoTurno);

    public string Etiqueta(int numero, string? codigoMedico = null)
    {
        var prefijo = string.IsNullOrWhiteSpace(codigoMedico)
            ? _negocio.Actual.TurnoPrefijo
            : codigoMedico;
        return string.IsNullOrWhiteSpace(prefijo) ? numero.ToString() : $"{prefijo.Trim()}-{numero}";
    }

    public static string EtiquetaEstado(EstadoTurno estado) => estado switch
    {
        EstadoTurno.Esperando => "Esperando",
        EstadoTurno.Llamado => "Llamado",
        EstadoTurno.Atendido => "Atendido",
        EstadoTurno.Ausente => "Ausente",
        _ => estado.ToString()
    };

    private static void ValidarPermiso()
    {
        if (!SesionActual.TienePermiso("turnos"))
            throw new InvalidOperationException("No tienes permiso para manejar los turnos de la sala.");
    }
}
