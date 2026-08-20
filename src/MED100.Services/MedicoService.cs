using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Reglas de negocio de los médicos y sus horarios.
///
/// Lo que gobierna este servicio y no se puede saltear:
///  · el porcentaje de honorario va entre 0 y 100;
///  · un médico que ya facturó NO se borra, se desactiva;
///  · dos tramos de horario del mismo día no se pueden pisar.
/// </summary>
public class MedicoService
{
    private readonly MedicoRepository _medicos;
    private readonly AuditoriaService _auditoria;

    public MedicoService(MedicoRepository medicos, AuditoriaService auditoria)
    {
        _medicos = medicos;
        _auditoria = auditoria;
    }

    // ---------- Lectura ----------

    public Task<List<Medico>> ObtenerTodosAsync(CancellationToken ct = default) =>
        _medicos.ObtenerTodosAsync(ct);

    public Task<List<Medico>> ObtenerActivosAsync(CancellationToken ct = default) =>
        _medicos.ObtenerActivosAsync(ct);

    public Task<Medico?> ObtenerPorIdAsync(long id, CancellationToken ct = default) =>
        _medicos.ObtenerPorIdAsync(id, ct);

    public Task<List<MedicoHorario>> ObtenerHorariosAsync(long medicoId, CancellationToken ct = default) =>
        _medicos.ObtenerHorariosAsync(medicoId, ct);

    /// <summary>
    /// Quién está atendiendo AHORA. Es la consulta de la recepción, así que
    /// devuelve el estado de todos los médicos, no solo de los disponibles:
    /// saber que alguien "entra a las 2" es tan útil como saber que está.
    /// </summary>
    public async Task<IReadOnlyList<MedicoDisponibilidad>> ObtenerDisponibilidadAsync(
        DateTime? momentoLocal = null, CancellationToken ct = default)
    {
        var medicos = await _medicos.ObtenerTodosAsync(ct);
        var horarios = await _medicos.ObtenerTodosLosHorariosAsync(ct);
        return DisponibilidadMedicos.Evaluar(medicos, horarios,
            momentoLocal ?? FechaNegocio.AhoraLocal());
    }

    /// <summary>
    /// Los médicos activos que atienden ese día de la semana.
    ///
    /// Es lo que alimenta el combo de Nueva cita: ofrecer un médico que ese
    /// día no viene hace que la pantalla después no encuentre ningún hueco y
    /// el usuario no entienda por qué. Mejor que no aparezca.
    /// </summary>
    public async Task<List<Medico>> ObtenerQueAtiendenAsync(DateOnly dia,
        CancellationToken ct = default)
    {
        var medicos = await _medicos.ObtenerActivosAsync(ct);
        var horarios = await _medicos.ObtenerTodosLosHorariosAsync(ct);
        var diaSemana = DisponibilidadMedicos.DiaSemanaDe(dia.ToDateTime(TimeOnly.MinValue));

        var atienden = horarios.Where(h => h.DiaSemana == diaSemana)
                               .Select(h => h.MedicoId)
                               .ToHashSet();
        return medicos.Where(m => atienden.Contains(m.Id)).ToList();
    }

    // ---------- Mutaciones ----------

    public async Task<long> CrearAsync(MedicoDatos datos, CancellationToken ct = default)
    {
        var limpios = await ValidarAsync(datos, exceptoId: null, ct);
        var id = await _medicos.InsertarAsync(limpios, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Crear, DbNames.Medico, id,
            $"Médico creado: {limpios.Nombre} (honorario {limpios.PorcentajeHonorario:0.##}%)", ct);
        return id;
    }

    public async Task ActualizarAsync(long id, MedicoDatos datos, CancellationToken ct = default)
    {
        var anterior = await _medicos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El médico no existe o fue eliminado.");
        var limpios = await ValidarAsync(datos, exceptoId: id, ct);
        await _medicos.ActualizarAsync(id, limpios, ct);

        var detalle = $"Médico modificado: {limpios.Nombre}";
        // El cambio de porcentaje se deja anotado con nombre y apellido: es
        // plata, y el día que un médico reclame hay que poder decir cuándo cambió.
        if (anterior.PorcentajeHonorario != limpios.PorcentajeHonorario)
            detalle += $" · honorario {anterior.PorcentajeHonorario:0.##}% → {limpios.PorcentajeHonorario:0.##}%";
        if (anterior.Activo != limpios.Activo)
            detalle += limpios.Activo ? " · reactivado" : " · desactivado";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Medico, id, detalle, ct);
    }

    /// <summary>
    /// Elimina (soft delete) un médico que nunca facturó. Si ya facturó, se
    /// niega y sugiere desactivarlo: sus facturas tienen que seguir sabiendo
    /// a quién nombran.
    /// </summary>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        ValidarPermiso();
        var medico = await _medicos.ObtenerPorIdAsync(id, ct)
            ?? throw new InvalidOperationException("El médico no existe o ya fue eliminado.");

        if (await _medicos.TieneFacturasAsync(id, ct))
            throw new InvalidOperationException(
                $"{medico.Nombre} ya tiene facturas y no se puede eliminar.\n\n" +
                "Desactivalo: deja de aparecer para cobrar y para agendar, pero " +
                "sus facturas siguen completas.");

        await _medicos.EliminarAsync(id, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Eliminar, DbNames.Medico, id,
            $"Médico eliminado: {medico.Nombre}", ct);
    }

    // ---------- Horarios ----------

    public async Task<long> AgregarHorarioAsync(long medicoId, HorarioDatos datos,
        CancellationToken ct = default)
    {
        ValidarPermiso();
        var medico = await _medicos.ObtenerPorIdAsync(medicoId, ct)
            ?? throw new InvalidOperationException("El médico no existe o fue eliminado.");

        // Se releen los tramos acá y no se confía en los que trajo la pantalla:
        // entre que se abrió el formulario y se guardó, otro usuario pudo cargar uno.
        var existentes = await _medicos.ObtenerHorariosAsync(medicoId, ct);
        DisponibilidadMedicos.ValidarTramo(datos, existentes);

        var id = await _medicos.InsertarHorarioAsync(medicoId, datos, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Medico, medicoId,
            $"Horario agregado a {medico.Nombre}: " +
            $"{DisponibilidadMedicos.NombreDia(datos.DiaSemana)} de {datos.HoraInicio:HH:mm} a {datos.HoraFin:HH:mm}",
            ct);
        return id;
    }

    public async Task QuitarHorarioAsync(long medicoId, long horarioId, CancellationToken ct = default)
    {
        ValidarPermiso();
        var medico = await _medicos.ObtenerPorIdAsync(medicoId, ct)
            ?? throw new InvalidOperationException("El médico no existe o fue eliminado.");
        var tramo = (await _medicos.ObtenerHorariosAsync(medicoId, ct))
            .FirstOrDefault(h => h.Id == horarioId)
            ?? throw new InvalidOperationException("Ese horario ya no existe.");

        await _medicos.EliminarHorarioAsync(horarioId, ct);
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Medico, medicoId,
            $"Horario quitado a {medico.Nombre}: " +
            $"{DisponibilidadMedicos.NombreDia(tramo.DiaSemana)} de {tramo.HoraInicio:HH:mm} a {tramo.HoraFin:HH:mm}",
            ct);
    }

    /// <summary>
    /// Pone al médico exactamente los días fijos indicados, con el mismo tramo
    /// horario para los que todavía no tenían ninguno.
    ///
    /// Es el pedido de Yuber (2026-08-14): marcar «Ingrid trabaja lunes, martes
    /// y viernes» de un tirón, en vez de cargar tres tramos a mano.
    ///
    /// Dos decisiones que importan:
    ///  · un día que YA tenía tramos se deja intacto — si tenía mañana y tarde,
    ///    reescribirlo le borraría el corte del almuerzo;
    ///  · un día que se destilda pierde TODOS sus tramos, que es justamente lo
    ///    que significa "ese día ya no viene".
    /// </summary>
    public async Task SincronizarDiasFijosAsync(long medicoId, IReadOnlyList<int> dias,
        TimeOnly horaInicio, TimeOnly horaFin, CancellationToken ct = default)
    {
        ValidarPermiso();
        var medico = await _medicos.ObtenerPorIdAsync(medicoId, ct)
            ?? throw new InvalidOperationException("El médico no existe o fue eliminado.");

        foreach (var dia in dias)
        {
            if (dia is < 1 or > 7)
                throw new ArgumentException("El día de la semana va de 1 (domingo) a 7 (sábado).");
        }
        if (horaFin <= horaInicio)
            throw new ArgumentException("La hora de salida tiene que ser posterior a la de entrada.");

        var existentes = await _medicos.ObtenerHorariosAsync(medicoId, ct);
        var conTramos = existentes.Select(h => h.DiaSemana).Distinct().ToHashSet();
        var pedidos = dias.Distinct().ToHashSet();

        var agregados = new List<int>();
        var quitados = new List<int>();

        foreach (var dia in pedidos.Where(d => !conTramos.Contains(d)).OrderBy(d => d))
        {
            await _medicos.InsertarHorarioAsync(medicoId, new HorarioDatos(dia, horaInicio, horaFin), ct);
            agregados.Add(dia);
        }

        foreach (var horario in existentes.Where(h => !pedidos.Contains(h.DiaSemana)))
        {
            await _medicos.EliminarHorarioAsync(horario.Id, ct);
            if (!quitados.Contains(horario.DiaSemana))
                quitados.Add(horario.DiaSemana);
        }

        if (agregados.Count == 0 && quitados.Count == 0)
            return;

        var detalle = $"Días de atención de {medico.Nombre}:";
        if (agregados.Count > 0)
            detalle += $" + {string.Join(", ", agregados.Select(DisponibilidadMedicos.NombreDia))}" +
                       $" ({horaInicio:HH}:{horaInicio:mm}–{horaFin:HH}:{horaFin:mm})";
        if (quitados.Count > 0)
            detalle += $" − {string.Join(", ", quitados.Select(DisponibilidadMedicos.NombreDia))}";
        await _auditoria.RegistrarAsync(AccionAuditoria.Modificar, DbNames.Medico, medicoId, detalle, ct);
    }

    // ---------- Validación ----------

    private async Task<MedicoDatos> ValidarAsync(MedicoDatos datos, long? exceptoId, CancellationToken ct)
    {
        ValidarPermiso();

        if (string.IsNullOrWhiteSpace(datos.Nombre))
            throw new ArgumentException("El nombre del médico es obligatorio.");
        if (datos.PorcentajeHonorario is < 0m or > 100m)
            throw new ArgumentException("El porcentaje de honorario va de 0 a 100.");

        var cedula = Limpiar(datos.Cedula);
        if (cedula is not null && await _medicos.ExisteCedulaAsync(cedula, exceptoId, ct))
            throw new ArgumentException($"Ya hay un médico registrado con la cédula {cedula}.");

        var email = Limpiar(datos.Email);
        if (email is not null && !email.Contains('@'))
            throw new ArgumentException("El correo del médico no parece válido.");

        return datos with
        {
            Nombre = datos.Nombre.Trim(),
            Cedula = cedula,
            Exequatur = Limpiar(datos.Exequatur),
            Especialidad = Limpiar(datos.Especialidad),
            Telefono = Limpiar(datos.Telefono),
            Email = email,
            CodigoTurno = await ResolverCodigoTurnoAsync(datos.CodigoTurno, datos.Nombre, exceptoId, ct)
        };
    }

    /// <summary>
    /// Deja listo el código de turno: si lo escribieron, se respeta (y se
    /// rechaza si está tomado); si lo dejaron vacío, se calcula del nombre.
    ///
    /// El índice único de la base es lo que de verdad impide el duplicado; acá
    /// se busca la primera variante libre (YO, YO2, YO3…) para que dos médicos
    /// con las mismas iniciales no bloqueen el alta con un error de MySQL.
    /// </summary>
    private async Task<string?> ResolverCodigoTurnoAsync(string? propuesto, string nombre,
        long? exceptoId, CancellationToken ct)
    {
        if (CodigoTurnoMedico.Normalizar(propuesto) is { } escrito)
        {
            if (await _medicos.ExisteCodigoTurnoAsync(escrito, exceptoId, ct))
                throw new ArgumentException(
                    $"El código de turno «{escrito}» ya lo tiene otro médico. " +
                    "Poné otro o dejalo vacío para que se calcule solo.");
            return escrito;
        }

        if (CodigoTurnoMedico.Proponer(nombre) is not { } baseCodigo)
            return null;

        for (var intento = 1; intento <= 99; intento++)
        {
            var candidato = CodigoTurnoMedico.Variante(baseCodigo, intento);
            if (!await _medicos.ExisteCodigoTurnoAsync(candidato, exceptoId, ct))
                return candidato;
        }

        // 99 médicos con las mismas dos iniciales. El turno cae al prefijo
        // general del negocio en vez de reventar el alta por un adorno.
        return null;
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static void ValidarPermiso()
    {
        if (!SesionActual.TienePermiso("medicos"))
            throw new InvalidOperationException("No tienes permiso para gestionar médicos.");
    }
}
