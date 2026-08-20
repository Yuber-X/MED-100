using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila del tarifario.</summary>
public record ProcedimientoFila(Procedimiento Procedimiento)
{
    public long Id => Procedimiento.Id;
    public string CodigoTexto =>
        string.IsNullOrWhiteSpace(Procedimiento.Codigo) ? "—" : Procedimiento.Codigo!;
    public string Nombre => Procedimiento.Nombre;
    public decimal Precio => Procedimiento.Precio;
    public bool Activo => Procedimiento.Activo;
    public bool ExentoItbis => Procedimiento.ExentoItbis;

    public string DuracionTexto
    {
        get
        {
            var m = Procedimiento.DuracionMinutos;
            if (m < 60) return $"{m} min";
            var horas = m / 60;
            var resto = m % 60;
            return resto == 0 ? $"{horas} h" : $"{horas} h {resto} min";
        }
    }

    public string ItbisTexto => Procedimiento.ExentoItbis ? "Exento" : "Gravado";
    public string EstadoTexto => Procedimiento.Activo ? "Activo" : "Inactivo";
}

/// <summary>
/// Tarifario de procedimientos ("costo de procedimientos"): lista a la
/// izquierda, formulario a la derecha.
/// </summary>
public partial class ProcedimientosViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly ProcedimientoService _servicio;
    private readonly ClienteService _pacientes;
    private readonly MedicoService _medicos;
    private readonly CitaService _citas;
    private readonly IDialogService _dialogos;

    private IReadOnlyList<Cliente> _todosLosPacientes = [];

    public ProcedimientosViewModel(ProcedimientoService servicio, ClienteService pacientes,
        MedicoService medicos, CitaService citas, IDialogService dialogos)
    {
        _servicio = servicio;
        _pacientes = pacientes;
        _medicos = medicos;
        _citas = citas;
        _dialogos = dialogos;
    }

    public ObservableCollection<ProcedimientoFila> Procedimientos { get; } = [];

    // ---------- Agendar desde el tarifario ----------
    //
    // Pedido de Yuber (2026-08-14): "dos comboboxes para el médico y el
    // paciente, si es que el procedimiento es lo que el paciente viene a
    // checarse". Van en la FICHA del procedimiento y no en el alta: el
    // tarifario es un catálogo de precios, y meterle un paciente al
    // procedimiento en sí lo convertiría en otra cosa. Acá lo que se crea es
    // una CITA, que es donde vive la relación paciente–médico–procedimiento.

    public ObservableCollection<Cliente> PacientesSugeridos { get; } = [];
    public ObservableCollection<OpcionCatalogo> MedicosAgenda { get; } = [];
    public ObservableCollection<HuecoAgenda> HuecosAgenda { get; } = [];

    [ObservableProperty] private bool _agendando;
    [ObservableProperty] private string _busquedaPaciente = string.Empty;
    [ObservableProperty] private Cliente? _pacienteAgenda;
    [ObservableProperty] private OpcionCatalogo? _medicoAgenda;
    [ObservableProperty] private DateTime _diaAgenda = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
    [ObservableProperty] private HuecoAgenda? _huecoAgenda;
    [ObservableProperty] private string _mensajeAgenda = string.Empty;
    [ObservableProperty] private string _mensajePacientes = string.Empty;

    private bool _rearmandoMedicos;

    /// <summary>
    /// Mientras se abre el panel de agendar, los handlers no disparan nada:
    /// asignar DiaAgenda lanzaría una recarga del combo de médicos y el método
    /// lanzaría otra, las dos sobre la misma colección. Se suprime y al final
    /// se hace UNA carga explícita.
    /// </summary>
    private bool _abriendoAgenda;

    partial void OnBusquedaPacienteChanged(string value) => FiltrarPacientes();

    partial void OnDiaAgendaChanged(DateTime value)
    {
        if (!_abriendoAgenda)
            _ = ActualizarMedicosYHuecosAsync();
    }

    partial void OnMedicoAgendaChanged(OpcionCatalogo? value)
    {
        if (!_rearmandoMedicos && !_abriendoAgenda)
            _ = RecalcularHuecosAsync();
    }

    [ObservableProperty] private ProcedimientoFila? _seleccionado;
    [ObservableProperty] private string _busqueda = string.Empty;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _resumen = string.Empty;

    // ---------- Formulario ----------
    [ObservableProperty] private bool _editando;
    [ObservableProperty] private long? _editandoId;
    [ObservableProperty] private string _codigo = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _precioTexto = string.Empty;
    [ObservableProperty] private string _duracionTexto = "30";
    [ObservableProperty] private bool _exentoItbis = true;
    [ObservableProperty] private string _descripcion = string.Empty;
    [ObservableProperty] private bool _activo = true;
    [ObservableProperty] private string _mensajeError = string.Empty;

    public bool HaySeleccion => Seleccionado is not null;

    partial void OnSeleccionadoChanged(ProcedimientoFila? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        // Cambiar de procedimiento cierra el panel de agendar: dejarlo abierto
        // con el paciente del anterior es la forma de agendar la cita
        // equivocada sin darse cuenta.
        Agendando = false;
        MensajeAgenda = string.Empty;
    }

    partial void OnBusquedaChanged(string value) => _ = RefrescarAsync();

    public async Task RefrescarAsync()
    {
        try
        {
            Ocupado = true;
            var previo = Seleccionado?.Id;
            var todos = await _servicio.ObtenerTodosAsync();

            var filtro = Busqueda.Trim();
            var filas = todos
                .Where(p => filtro.Length == 0 ||
                            p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                            (p.Codigo ?? string.Empty).Contains(filtro, StringComparison.OrdinalIgnoreCase))
                .Select(p => new ProcedimientoFila(p))
                .ToList();

            Procedimientos.Clear();
            foreach (var f in filas)
                Procedimientos.Add(f);

            var activos = todos.Count(p => p.Activo);
            Resumen = $"{activos} activos de {todos.Count} en el tarifario";
            Seleccionado = Procedimientos.FirstOrDefault(p => p.Id == previo);

            _todosLosPacientes = await _pacientes.ObtenerTodosAsync();
            FiltrarPacientes();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el tarifario");
            _dialogos.MostrarError("Procedimientos", $"No se pudo cargar el tarifario.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Nuevo()
    {
        EditandoId = null;
        Codigo = Nombre = Descripcion = string.Empty;
        PrecioTexto = string.Empty;
        DuracionTexto = "30";
        // Siempre exento: los servicios de salud no llevan ITBIS en RD y la
        // llave general del impuesto vive en Configuración (pedido de Yuber
        // 2026-08-14). La columna sigue existiendo en la base y la respeta el
        // cálculo, pero ya no se decide procedimiento por procedimiento.
        ExentoItbis = true;
        Activo = true;
        MensajeError = string.Empty;
        Editando = true;
    }

    [RelayCommand]
    private async Task EditarAsync()
    {
        if (Seleccionado is null)
            return;
        var p = await _servicio.ObtenerPorIdAsync(Seleccionado.Id);
        if (p is null)
            return;

        EditandoId = p.Id;
        Codigo = p.Codigo ?? string.Empty;
        Nombre = p.Nombre;
        PrecioTexto = p.Precio.ToString("0.##", CulturaRd);
        DuracionTexto = p.DuracionMinutos.ToString(CulturaRd);
        ExentoItbis = p.ExentoItbis;
        Descripcion = p.Descripcion ?? string.Empty;
        Activo = p.Activo;
        MensajeError = string.Empty;
        Editando = true;
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

            if (!decimal.TryParse(PrecioTexto, NumberStyles.Number, CulturaRd, out var precio))
            {
                MensajeError = "El precio tiene que ser un número (ej. 1500).";
                return;
            }
            if (!int.TryParse(DuracionTexto, NumberStyles.Integer, CulturaRd, out var duracion))
            {
                MensajeError = "La duración va en minutos (ej. 30).";
                return;
            }

            var datos = new ProcedimientoDatos(Codigo, Nombre, precio, duracion,
                ExentoItbis, Descripcion, Activo);

            if (EditandoId is { } id)
                await _servicio.ActualizarAsync(id, datos);
            else
                await _servicio.CrearAsync(datos);

            Editando = false;
            await RefrescarAsync();
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el procedimiento");
            _dialogos.MostrarError("Procedimientos", $"No se pudo guardar.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (Seleccionado is null)
            return;
        if (!_dialogos.Confirmar("Eliminar procedimiento",
                $"¿Eliminar «{Seleccionado.Nombre}» del tarifario?"))
            return;

        try
        {
            await _servicio.EliminarAsync(Seleccionado.Id);
            await RefrescarAsync();
        }
        catch (InvalidOperationException ex)
        {
            // Regla de negocio (ya facturado / con citas): mensaje claro, sin stack
            _dialogos.MostrarError("Eliminar procedimiento", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando el procedimiento");
            _dialogos.MostrarError("Procedimientos", $"No se pudo eliminar.\n\n{ex.Message}");
        }
    }

    // =========================================================
    // Agendar este procedimiento a un paciente
    // =========================================================

    /// <summary>Cuántos pacientes se ofrecen de una en el combo.</summary>
    private const int TopePacientes = 50;

    private void FiltrarPacientes()
    {
        var filtro = BusquedaPaciente.Trim();
        PacientesSugeridos.Clear();

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
                    ? $"Mostrando {PacientesSugeridos.Count} de {_todosLosPacientes.Count}."
                    : string.Empty;
    }

    [RelayCommand]
    private async Task AbrirAgendaAsync()
    {
        if (Seleccionado is null)
            return;

        try
        {
            _abriendoAgenda = true;
            BusquedaPaciente = string.Empty;
            PacienteAgenda = null;
            FiltrarPacientes();
            DiaAgenda = FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);
            MensajeAgenda = string.Empty;
            Agendando = true;
        }
        finally
        {
            _abriendoAgenda = false;
        }

        await ActualizarMedicosYHuecosAsync();
    }

    [RelayCommand]
    private void CerrarAgenda()
    {
        Agendando = false;
        MensajeAgenda = string.Empty;
    }

    private async Task ActualizarMedicosYHuecosAsync()
    {
        try
        {
            _rearmandoMedicos = true;
            var previo = MedicoAgenda?.Id;

            var delDia = await _medicos.ObtenerQueAtiendenAsync(DateOnly.FromDateTime(DiaAgenda));
            MedicosAgenda.Clear();
            foreach (var m in delDia)
                MedicosAgenda.Add(new OpcionCatalogo(m.Id, m.Nombre));

            MedicoAgenda = MedicosAgenda.FirstOrDefault(m => m.Id == previo)
                           ?? MedicosAgenda.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los médicos del día en el tarifario");
        }
        finally
        {
            _rearmandoMedicos = false;
        }

        await RecalcularHuecosAsync();
    }

    private async Task RecalcularHuecosAsync()
    {
        HuecosAgenda.Clear();
        if (Seleccionado is null || MedicoAgenda?.Id is not { } medicoId)
        {
            MensajeAgenda = MedicosAgenda.Count == 0
                ? "Ningún médico atiende ese día."
                : string.Empty;
            return;
        }

        try
        {
            var duracion = await _citas.DuracionSugeridaAsync(Seleccionado.Id);
            var huecos = await _citas.HuecosLibresAsync(medicoId,
                DateOnly.FromDateTime(DiaAgenda), duracion);

            foreach (var h in huecos)
                HuecosAgenda.Add(h);

            HuecoAgenda = HuecosAgenda.FirstOrDefault();
            MensajeAgenda = huecos.Count == 0
                ? "Ese día no le quedan huecos libres. Probá otra fecha u otro médico."
                : string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error calculando huecos desde el tarifario");
            MensajeAgenda = "No se pudieron calcular los horarios libres.";
        }
    }

    [RelayCommand]
    private async Task AgendarAsync()
    {
        if (Seleccionado is null)
            return;

        try
        {
            MensajeAgenda = string.Empty;

            if (PacienteAgenda is null)
            {
                MensajeAgenda = "Elegí el paciente.";
                return;
            }
            if (MedicoAgenda?.Id is not { } medicoId)
            {
                MensajeAgenda = "Elegí el médico.";
                return;
            }
            if (HuecoAgenda is null)
            {
                MensajeAgenda = "Elegí la hora de la lista.";
                return;
            }

            var duracion = await _citas.DuracionSugeridaAsync(Seleccionado.Id);
            await _citas.CrearAsync(new CitaDatos(
                PacienteAgenda.Id, medicoId, Seleccionado.Id,
                HuecoAgenda.InicioLocal, duracion));

            _dialogos.Informar("Cita creada",
                $"{PacienteAgenda.Nombre} queda agendado para «{Seleccionado.Nombre}» " +
                $"el {HuecoAgenda.InicioLocal:dd/MM/yyyy} a las {HuecoAgenda.HoraTexto}.\n\n" +
                "La cita ya aparece en la agenda del médico.");

            Agendando = false;
        }
        catch (ArgumentException ex)
        {
            MensajeAgenda = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            MensajeAgenda = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error agendando desde el tarifario");
            _dialogos.MostrarError("Agendar", $"No se pudo crear la cita.\n\n{ex.Message}");
        }
    }
}
