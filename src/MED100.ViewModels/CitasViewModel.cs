using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila de la agenda del día.</summary>
public record CitaFila(Cita Cita)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public long Id => Cita.Id;
    public DateTime InicioLocal => FechaNegocio.AUtcLocal(Cita.FechaHoraUtc);
    public string HoraTexto => InicioLocal.ToString("h:mm tt", CulturaRd);
    public string RangoTexto =>
        $"{HoraTexto} – {InicioLocal.AddMinutes(Cita.DuracionMinutos).ToString("h:mm tt", CulturaRd)}";
    public string Paciente => Cita.PacienteNombre;
    public string Medico => Cita.MedicoNombre;
    public string MotivoTexto => Cita.ProcedimientoNombre ?? "Consulta general";
    public string TelefonoTexto =>
        string.IsNullOrWhiteSpace(Cita.PacienteTelefono) ? "—" : Cita.PacienteTelefono;
    public EstadoCita Estado => Cita.Estado;
    public string EstadoTexto => AgendaMedico.EtiquetaEstado(Cita.Estado);

    /// <summary>Verde para las que siguen en pie, apagado para las que no.</summary>
    public bool OcupaAgenda => Cita.OcupaAgenda;

    /// <summary>
    /// Si el recordatorio salió o no. Es lo primero que se mira cuando un
    /// paciente dice "nadie me avisó".
    /// </summary>
    public string RecordatorioTexto => Cita.RecordatorioEnviadoAtUtc is { } enviado
        ? $"Avisado {FechaNegocio.AUtcLocal(enviado).ToString("dd/MM h:mm tt", CulturaRd)}"
        : string.IsNullOrWhiteSpace(Cita.PacienteEmail) ? "Sin correo" : "Sin avisar";

    public bool RecordatorioEnviado => Cita.RecordatorioEnviadoAtUtc is not null;
    public bool YaSeCobro => Cita.FacturaId is not null;
}

/// <summary>Opción de médico o procedimiento para los combos.</summary>
public record OpcionCatalogo(long? Id, string Nombre);

/// <summary>Estado al que se puede pasar una cita, listo para un botón.</summary>
public record OpcionEstado(EstadoCita Valor, string Etiqueta);

/// <summary>
/// Agenda del día: lista a la izquierda, formulario a la derecha.
///
/// La pantalla trabaja SIEMPRE en hora local de RD. La conversión a UTC la
/// hace el repositorio; acá no hay ni una cuenta de zonas horarias.
/// </summary>
public partial class CitasViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly CitaService _citas;
    private readonly MedicoService _medicos;
    private readonly ProcedimientoService _procedimientos;
    private readonly ClienteService _pacientes;
    private readonly RecordatorioCitasService _recordatorios;
    private readonly IDialogService _dialogos;

    /// <summary>El shell lleva la cita a la pantalla de cobro con todo cargado.</summary>
    public event Action<Cita>? CobroSolicitado;

    private IReadOnlyList<Cliente> _todosLosPacientes = [];

    /// <summary>
    /// Mientras se rehace el combo de médicos, el cambio de selección NO debe
    /// disparar el cálculo de huecos: quedarían dos corriendo a la vez sobre
    /// la misma colección y la lista saldría duplicada o a medias.
    /// </summary>
    private bool _rearmandoMedicos;

    /// <summary>
    /// Mientras Nueva/Editar llenan el formulario, los handlers de propiedad NO
    /// disparan nada. Sin esto, asignar DiaForm lanzaba una recarga del combo
    /// de médicos por su cuenta y el método lanzaba otra a continuación: las
    /// dos limpiaban y rellenaban la MISMA colección, y con la interleaving
    /// justa la lista terminaba duplicada. Se suprime todo y al final se hace
    /// UNA carga explícita.
    /// </summary>
    private bool _cargandoFormulario;

    public CitasViewModel(CitaService citas, MedicoService medicos,
        ProcedimientoService procedimientos, ClienteService pacientes,
        RecordatorioCitasService recordatorios, IDialogService dialogos)
    {
        _citas = citas;
        _medicos = medicos;
        _procedimientos = procedimientos;
        _pacientes = pacientes;
        _recordatorios = recordatorios;
        _dialogos = dialogos;
    }

    // ---------- Lista ----------
    public ObservableCollection<CitaFila> Citas { get; } = [];

    /// <summary>Filtro de médico de la lista. El primero es "Todos".</summary>
    public ObservableCollection<OpcionCatalogo> MedicosFiltro { get; } = [];

    [ObservableProperty] private DateTime _dia = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
    [ObservableProperty] private OpcionCatalogo? _medicoFiltro;
    [ObservableProperty] private CitaFila? _seleccionada;
    [ObservableProperty] private string _resumen = string.Empty;
    [ObservableProperty] private bool _ocupado;

    public string DiaTexto => Dia.ToString("dddd d 'de' MMMM 'de' yyyy", CulturaRd);

    public bool HaySeleccion => Seleccionada is not null;

    /// <summary>Estados a los que puede pasar la cita elegida (vacío si ya terminó).</summary>
    public ObservableCollection<OpcionEstado> EstadosDisponibles { get; } = [];

    /// <summary>False cuando la cita ya está en un estado final: no hay a dónde pasarla.</summary>
    public bool PuedeCambiarEstado => EstadosDisponibles.Count > 0;

    partial void OnDiaChanged(DateTime value)
    {
        OnPropertyChanged(nameof(DiaTexto));
        _ = RefrescarAsync();
    }

    partial void OnMedicoFiltroChanged(OpcionCatalogo? value) => _ = RefrescarAsync();

    partial void OnSeleccionadaChanged(CitaFila? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        EstadosDisponibles.Clear();
        if (value is not null)
        {
            foreach (var estado in AgendaMedico.SiguientesEstados(value.Estado))
                EstadosDisponibles.Add(new OpcionEstado(estado, AgendaMedico.EtiquetaEstado(estado)));
        }
        OnPropertyChanged(nameof(PuedeCambiarEstado));
    }

    // ---------- Formulario ----------
    public ObservableCollection<OpcionCatalogo> MedicosForm { get; } = [];
    public ObservableCollection<OpcionCatalogo> Procedimientos { get; } = [];
    public ObservableCollection<Cliente> PacientesSugeridos { get; } = [];
    /// <summary>Huecos libres del médico ese día. Es lo que se ofrece en vez de adivinar.</summary>
    public ObservableCollection<HuecoAgenda> Huecos { get; } = [];

    [ObservableProperty] private bool _editando;
    [ObservableProperty] private long? _editandoId;
    [ObservableProperty] private string _busquedaPaciente = string.Empty;
    [ObservableProperty] private Cliente? _pacienteSeleccionado;
    [ObservableProperty] private OpcionCatalogo? _medicoForm;
    [ObservableProperty] private OpcionCatalogo? _procedimientoForm;
    [ObservableProperty] private DateTime _diaForm = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
    [ObservableProperty] private HuecoAgenda? _huecoSeleccionado;
    [ObservableProperty] private string _duracionTexto = "30";
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private string _mensajeHuecos = string.Empty;
    [ObservableProperty] private string _mensajePacientes = string.Empty;
    [ObservableProperty] private string _mensajeMedicos = string.Empty;

    partial void OnBusquedaPacienteChanged(string value) => FiltrarPacientes();

    partial void OnMedicoFormChanged(OpcionCatalogo? value)
    {
        if (!_rearmandoMedicos && !_cargandoFormulario)
            _ = RecalcularHuecosAsync();
    }

    // Al cambiar el día cambia QUIÉN atiende, no solo qué horas quedan libres.
    partial void OnDiaFormChanged(DateTime value)
    {
        if (!_cargandoFormulario)
            _ = ActualizarMedicosDelDiaAsync();
    }

    partial void OnDuracionTextoChanged(string value)
    {
        if (!_cargandoFormulario)
            _ = RecalcularHuecosAsync();
    }

    partial void OnProcedimientoFormChanged(OpcionCatalogo? value)
    {
        if (!_cargandoFormulario)
            _ = AplicarDuracionSugeridaAsync();
    }

    // =========================================================

    public async Task RefrescarAsync()
    {
        try
        {
            Ocupado = true;
            await CargarCatalogosAsync();

            var previa = Seleccionada?.Id;
            var dia = DateOnly.FromDateTime(Dia);
            var citas = await _citas.ObtenerPorDiaAsync(dia, MedicoFiltro?.Id);

            Citas.Clear();
            foreach (var cita in citas)
                Citas.Add(new CitaFila(cita));

            var enPie = citas.Count(c => c.OcupaAgenda);
            var sinAvisar = citas.Count(c => c.OcupaAgenda && c.RecordatorioEnviadoAtUtc is null &&
                                             !string.IsNullOrWhiteSpace(c.PacienteEmail));
            Resumen = citas.Count == 0
                ? "No hay citas ese día"
                : $"{enPie} cita(s) en pie de {citas.Count}" +
                  (sinAvisar > 0 ? $" · {sinAvisar} sin recordatorio enviado" : string.Empty);

            Seleccionada = Citas.FirstOrDefault(c => c.Id == previa);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la agenda");
            _dialogos.MostrarError("Citas", $"No se pudo cargar la agenda.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task CargarCatalogosAsync()
    {
        // El filtro de arriba lista TODOS los médicos activos; el del
        // formulario, solo los que atienden el día elegido. Son dos preguntas
        // distintas: "mostrame la agenda de X" vs. "¿con quién puedo agendar?".
        if (MedicosFiltro.Count == 0)
        {
            MedicosFiltro.Add(new OpcionCatalogo(null, "Todos los médicos"));
            foreach (var m in await _medicos.ObtenerActivosAsync())
                MedicosFiltro.Add(new OpcionCatalogo(m.Id, m.Nombre));
            MedicoFiltro ??= MedicosFiltro[0];
        }

        if (Procedimientos.Count == 0)
        {
            Procedimientos.Add(new OpcionCatalogo(null, "Consulta general"));
            foreach (var p in await _procedimientos.ObtenerActivosAsync())
                Procedimientos.Add(new OpcionCatalogo(p.Id, p.Nombre));
        }

        _todosLosPacientes = await _pacientes.ObtenerTodosAsync();
        FiltrarPacientes();
    }

    /// <summary>Cuántos pacientes se ofrecen de una en el combo.</summary>
    private const int TopePacientes = 50;

    private void FiltrarPacientes()
    {
        var filtro = BusquedaPaciente.Trim();
        PacientesSugeridos.Clear();

        // Con el combo vacío se listan los pacientes ya registrados (pedido de
        // Yuber 2026-08-14). Antes había que escribir dos letras para que
        // apareciera algo, y con la caja en blanco parecía roto. Se limita el
        // volcado porque un combo de mil filas no se navega.
        var visibles = filtro.Length == 0
            ? _todosLosPacientes
            : _todosLosPacientes
                .Where(p => p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                            (p.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
                            (p.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false));

        foreach (var p in visibles.Take(TopePacientes))
            PacientesSugeridos.Add(p);

        MensajePacientes = _todosLosPacientes.Count == 0
            ? "Todavía no hay pacientes registrados."
            : PacientesSugeridos.Count == 0
                ? "Ningún paciente coincide con esa búsqueda."
                : _todosLosPacientes.Count > PacientesSugeridos.Count
                    ? $"Mostrando {PacientesSugeridos.Count} de {_todosLosPacientes.Count}. " +
                      "Escribí nombre, cédula o teléfono para acotar."
                    : $"{PacientesSugeridos.Count} paciente(s)";
    }

    /// <summary>
    /// Rehace el combo de médicos con los que atienden el día elegido y vuelve
    /// a calcular los huecos. Va todo junto porque los huecos dependen del
    /// médico, y el médico del día.
    /// </summary>
    private async Task ActualizarMedicosDelDiaAsync()
    {
        try
        {
            _rearmandoMedicos = true;
            var previo = MedicoForm?.Id;

            var delDia = await _medicos.ObtenerQueAtiendenAsync(DateOnly.FromDateTime(DiaForm));
            MedicosForm.Clear();
            foreach (var m in delDia)
                MedicosForm.Add(new OpcionCatalogo(m.Id, m.Nombre));

            MedicoForm = MedicosForm.FirstOrDefault(m => m.Id == previo) ?? MedicosForm.FirstOrDefault();

            MensajeMedicos = delDia.Count == 0
                ? "Ningún médico atiende ese día. Probá otra fecha, o cargale el día " +
                  "en Médicos si ese día sí viene."
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los médicos del día");
            MensajeMedicos = "No se pudo cargar la lista de médicos.";
        }
        finally
        {
            _rearmandoMedicos = false;
        }

        await RecalcularHuecosAsync();
    }

    private async Task AplicarDuracionSugeridaAsync()
    {
        // Al elegir el procedimiento se trae su duración del tarifario: es un
        // dato que ya está cargado y que la recepción no tiene por qué recordar.
        var sugerida = await _citas.DuracionSugeridaAsync(ProcedimientoForm?.Id);
        DuracionTexto = sugerida.ToString(CulturaRd);
    }

    private async Task RecalcularHuecosAsync()
    {
        Huecos.Clear();
        MensajeHuecos = string.Empty;

        if (MedicoForm?.Id is not { } medicoId)
            return;
        if (!int.TryParse(DuracionTexto, NumberStyles.Integer, CulturaRd, out var duracion) || duracion <= 0)
            return;

        try
        {
            var previo = HuecoSeleccionado?.InicioLocal;
            var huecos = await _citas.HuecosLibresAsync(medicoId, DateOnly.FromDateTime(DiaForm),
                duracion, EditandoId);

            foreach (var h in huecos)
                Huecos.Add(h);

            HuecoSeleccionado = Huecos.FirstOrDefault(h => h.InicioLocal == previo);
            MensajeHuecos = huecos.Count == 0
                ? "Ese día no le quedan huecos libres. Probá otro día u otro médico."
                : $"{huecos.Count} hueco(s) disponible(s)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error calculando huecos de la agenda");
            MensajeHuecos = "No se pudieron calcular los huecos.";
        }
    }

    // ---------- Comandos ----------

    [RelayCommand]
    private void DiaAnterior() => Dia = Dia.AddDays(-1);

    [RelayCommand]
    private void DiaSiguiente() => Dia = Dia.AddDays(1);

    [RelayCommand]
    private void Hoy() => Dia = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);

    [RelayCommand]
    private async Task NuevaAsync()
    {
        try
        {
            _cargandoFormulario = true;
            EditandoId = null;
            BusquedaPaciente = string.Empty;
            PacienteSeleccionado = null;
            FiltrarPacientes();
            ProcedimientoForm = Procedimientos.FirstOrDefault();
            DiaForm = Dia;
            DuracionTexto = "30";
            Notas = string.Empty;
            MensajeError = string.Empty;
            Editando = true;
        }
        finally
        {
            _cargandoFormulario = false;
        }

        await ActualizarMedicosDelDiaAsync();
        // Si el filtro de arriba ya apuntaba a un médico y ese día atiende, se
        // arranca con él: es a quien la recepción venía mirando.
        if (MedicoFiltro?.Id is { } filtrado &&
            MedicosForm.FirstOrDefault(m => m.Id == filtrado) is { } coincide)
        {
            MedicoForm = coincide;
        }
    }

    [RelayCommand]
    private async Task EditarAsync()
    {
        if (Seleccionada is null)
            return;
        var cita = await _citas.ObtenerPorIdAsync(Seleccionada.Id);
        if (cita is null)
            return;

        var local = FechaNegocio.AUtcLocal(cita.FechaHoraUtc);
        try
        {
            _cargandoFormulario = true;
            EditandoId = cita.Id;
            PacienteSeleccionado = _todosLosPacientes.FirstOrDefault(p => p.Id == cita.ClienteId);
            BusquedaPaciente = cita.PacienteNombre;
            FiltrarPacientes();
            PacienteSeleccionado ??= PacientesSugeridos.FirstOrDefault(p => p.Id == cita.ClienteId);
            ProcedimientoForm = Procedimientos.FirstOrDefault(p => p.Id == cita.ProcedimientoId)
                                ?? Procedimientos.FirstOrDefault();
            DiaForm = local.Date;
            // La duración va TAL CUAL la tenía la cita, no la sugerida del
            // tarifario: si se la habían alargado a mano, reprogramarla no
            // puede encogerla por su cuenta.
            DuracionTexto = cita.DuracionMinutos.ToString(CulturaRd);
            Notas = cita.Notas ?? string.Empty;
            MensajeError = string.Empty;
            Editando = true;
        }
        finally
        {
            _cargandoFormulario = false;
        }

        await ActualizarMedicosDelDiaAsync();
        // El médico de la cita puede no estar en la lista del día si le
        // cambiaron el horario después de agendarla: se agrega igual, porque
        // la cita existe y hay que poder reprogramarla sin perderlo.
        if (MedicosForm.All(m => m.Id != cita.MedicoId))
            MedicosForm.Add(new OpcionCatalogo(cita.MedicoId, cita.MedicoNombre));
        MedicoForm = MedicosForm.FirstOrDefault(m => m.Id == cita.MedicoId);
        // La hora actual de la cita no aparece entre los huecos "libres" salvo
        // que se la excluya del choque — por eso se pasa EditandoId arriba.
        HuecoSeleccionado = Huecos.FirstOrDefault(h => h.InicioLocal == local);
    }

    [RelayCommand]
    private void Cancelar()
    {
        Editando = false;
        MensajeError = string.Empty;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        try
        {
            MensajeError = string.Empty;

            if (PacienteSeleccionado is null)
            {
                MensajeError = "Elegí el paciente.";
                return;
            }
            if (MedicoForm?.Id is not { } medicoId)
            {
                MensajeError = "Elegí el médico.";
                return;
            }
            if (HuecoSeleccionado is null)
            {
                MensajeError = "Elegí la hora de la lista de huecos disponibles.";
                return;
            }
            if (!int.TryParse(DuracionTexto, NumberStyles.Integer, CulturaRd, out var duracion))
            {
                MensajeError = "La duración va en minutos (ej. 30).";
                return;
            }

            var datos = new CitaDatos(
                PacienteSeleccionado.Id, medicoId, ProcedimientoForm?.Id,
                HuecoSeleccionado.InicioLocal, duracion, Notas);

            if (EditandoId is { } id)
                await _citas.ActualizarAsync(id, datos);
            else
                await _citas.CrearAsync(datos);

            Editando = false;
            // La agenda salta al día de la cita: si se agendó para el jueves,
            // mostrar el lunes haría creer que no se guardó.
            if (DateOnly.FromDateTime(DiaForm) != DateOnly.FromDateTime(Dia))
                Dia = DiaForm.Date;
            else
                await RefrescarAsync();
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando la cita");
            _dialogos.MostrarError("Citas", $"No se pudo guardar la cita.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CambiarEstadoAsync(OpcionEstado? opcion)
    {
        if (Seleccionada is null || opcion is null)
            return;

        try
        {
            await _citas.CambiarEstadoAsync(Seleccionada.Id, opcion.Valor);
            await RefrescarAsync();
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.MostrarError("Cambiar estado", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cambiando el estado de la cita");
            _dialogos.MostrarError("Citas", $"No se pudo cambiar el estado.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (Seleccionada is null)
            return;
        if (!_dialogos.Confirmar("Eliminar cita",
                $"¿Eliminar la cita de {Seleccionada.Paciente} a las {Seleccionada.HoraTexto}?\n\n" +
                "Si el paciente simplemente no vino, es mejor marcarla como «No asistió»: " +
                "así queda el registro."))
            return;

        try
        {
            await _citas.EliminarAsync(Seleccionada.Id);
            await RefrescarAsync();
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.MostrarError("Eliminar cita", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando la cita");
            _dialogos.MostrarError("Citas", $"No se pudo eliminar la cita.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Lleva la cita a la caja con el paciente, el médico y el procedimiento ya
    /// puestos. Es el camino corto de la agenda al cobro.
    /// </summary>
    [RelayCommand]
    private async Task CobrarAsync()
    {
        if (Seleccionada is null)
            return;
        if (Seleccionada.YaSeCobro)
        {
            _dialogos.MostrarError("Cobrar cita", "Esa cita ya se cobró.");
            return;
        }

        var cita = await _citas.ObtenerPorIdAsync(Seleccionada.Id);
        if (cita is not null)
            CobroSolicitado?.Invoke(cita);
    }

    /// <summary>Manda los recordatorios pendientes a pedido, sin esperar al automático.</summary>
    [RelayCommand]
    private async Task EnviarRecordatoriosAsync()
    {
        try
        {
            Ocupado = true;
            var r = await _recordatorios.EnviarAsync();
            _dialogos.Informar("Recordatorios de cita", r.Detalle);
            await RefrescarAsync();
        }
        catch (InvalidOperationException ex)
        {
            _dialogos.MostrarError("Recordatorios de cita", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error enviando recordatorios de cita");
            _dialogos.MostrarError("Recordatorios de cita",
                $"No se pudieron enviar los recordatorios.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }
}
