using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila de la lista de médicos, con su estado de atención ahora mismo.</summary>
public record MedicoFila(MedicoDisponibilidad Disponibilidad)
{
    public long Id => Disponibilidad.Medico.Id;
    public string Nombre => Disponibilidad.Medico.Nombre;
    public string Especialidad =>
        string.IsNullOrWhiteSpace(Disponibilidad.Medico.Especialidad) ? "—" : Disponibilidad.Medico.Especialidad!;
    public string HonorarioTexto => $"{Disponibilidad.Medico.PorcentajeHonorario:0.##}%";
    public bool AtiendeAhora => Disponibilidad.AtiendeAhora;
    public bool Activo => Disponibilidad.Medico.Activo;
    public string EstadoTexto => Disponibilidad.Detalle;

    /// <summary>Los días fijos en una línea: "Lun, Mar, Vie".</summary>
    public string DiasTexto => DisponibilidadMedicos.ResumirDias(Disponibilidad.DiasQueAtiende);

    /// <summary>El prefijo con que salen sus turnos en la sala.</summary>
    public string CodigoTurnoTexto => Disponibilidad.Medico.CodigoTurno ?? "—";
}

/// <summary>
/// Un día de la semana con su casilla, para marcar de un tirón los días en
/// que el médico siempre atiende (pedido de Yuber 2026-08-14).
/// Es ObservableObject y no un record porque la casilla se ata en dos vías.
/// </summary>
public partial class DiaSeleccionable : ObservableObject
{
    public DiaSeleccionable(int numero)
    {
        Numero = numero;
        Nombre = DisponibilidadMedicos.NombreDia(numero);
        Corto = DisponibilidadMedicos.NombreDiaCorto(numero);
    }

    /// <summary>1 = domingo … 7 = sábado (convención DAYOFWEEK de MySQL).</summary>
    public int Numero { get; }
    public string Nombre { get; }
    public string Corto { get; }

    [ObservableProperty] private bool _marcado;
}

/// <summary>Un tramo de horario tal como se muestra en la ficha del médico.</summary>
public record HorarioFila(MedicoHorario Horario)
{
    public long Id => Horario.Id;
    public string DiaTexto => DisponibilidadMedicos.NombreDia(Horario.DiaSemana);
    public string RangoTexto =>
        $"{Horario.HoraInicio:h:mm tt} a {Horario.HoraFin:h:mm tt}".ToLower(CultureInfo.GetCultureInfo("es-DO"));
}

/// <summary>
/// Médicos: la lista con quién atiende AHORA a la izquierda y la ficha del
/// seleccionado —con sus horarios— a la derecha.
///
/// Van juntos a propósito: la pregunta de la recepción no es "¿qué médicos
/// hay?" sino "¿quién está y hasta qué hora?", y eso se contesta viendo la
/// lista y el horario en la misma pantalla.
/// </summary>
public partial class MedicosViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly MedicoService _servicio;
    private readonly IDialogService _dialogos;

    public MedicosViewModel(MedicoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _diasSemana = [.. Enumerable.Range(1, 7)
            .Select(d => new OpcionDia(d, DisponibilidadMedicos.NombreDia(d)))];
        _diaSeleccionado = _diasSemana[1];   // lunes: es lo que más se carga
    }

    public ObservableCollection<MedicoFila> Medicos { get; } = [];
    public ObservableCollection<HorarioFila> Horarios { get; } = [];

    [ObservableProperty] private MedicoFila? _medicoSeleccionado;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private string _busqueda = string.Empty;

    /// <summary>Cuántos están atendiendo en este momento: el dato que se mira de reojo.</summary>
    [ObservableProperty] private string _resumenAhora = string.Empty;

    // ---------- Formulario del médico ----------
    [ObservableProperty] private bool _editando;
    [ObservableProperty] private long? _editandoId;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _cedula = string.Empty;
    [ObservableProperty] private string _exequatur = string.Empty;
    [ObservableProperty] private string _especialidad = string.Empty;
    [ObservableProperty] private string _telefono = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _honorarioTexto = "0";
    [ObservableProperty] private bool _activo = true;
    [ObservableProperty] private string _mensajeError = string.Empty;

    /// <summary>
    /// Prefijo del turno. Se deja editable, pero vacío significa "calculalo
    /// vos": el servicio lo saca del nombre y resuelve los choques.
    /// </summary>
    [ObservableProperty] private string _codigoTurno = string.Empty;

    // ---------- Días fijos de atención ----------

    /// <summary>Las siete casillas, ordenadas de lunes a domingo.</summary>
    public IReadOnlyList<DiaSeleccionable> DiasFijos => _diasFijos;
    private readonly List<DiaSeleccionable> _diasFijos =
        [.. new[] { 2, 3, 4, 5, 6, 7, 1 }.Select(d => new DiaSeleccionable(d))];

    [ObservableProperty] private string _horaFijaInicioTexto = "8:00";
    [ObservableProperty] private string _horaFijaInicioMeridiano = "AM";
    [ObservableProperty] private string _horaFijaFinTexto = "5:00";
    [ObservableProperty] private string _horaFijaFinMeridiano = "PM";

    // ---------- Alta de tramo horario ----------
    public IReadOnlyList<OpcionDia> DiasSemana => _diasSemana;
    private readonly List<OpcionDia> _diasSemana;

    [ObservableProperty] private OpcionDia _diaSeleccionado;
    [ObservableProperty] private string _horaInicioTexto = "8:00";
    [ObservableProperty] private string _horaInicioMeridiano = "AM";
    [ObservableProperty] private string _horaFinTexto = "12:00";
    [ObservableProperty] private string _horaFinMeridiano = "PM";
    [ObservableProperty] private string _mensajeHorario = string.Empty;

    /// <summary>AM/PM para los cuatro combos de hora. En RD nadie dice "17:00".</summary>
    public IReadOnlyList<string> Meridianos => Hora12.Meridianos;

    public bool HayMedicoSeleccionado => MedicoSeleccionado is not null;

    partial void OnMedicoSeleccionadoChanged(MedicoFila? value)
    {
        OnPropertyChanged(nameof(HayMedicoSeleccionado));
        MensajeHorario = string.Empty;
        _ = CargarHorariosAsync();
    }

    partial void OnBusquedaChanged(string value) => _ = RefrescarAsync();

    // ---------- Carga ----------

    public async Task RefrescarAsync()
    {
        try
        {
            Ocupado = true;
            var seleccionado = MedicoSeleccionado?.Id;
            var disponibilidad = await _servicio.ObtenerDisponibilidadAsync();

            var filtro = Busqueda.Trim();
            var filas = disponibilidad
                .Where(d => filtro.Length == 0 ||
                            d.Medico.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                            (d.Medico.Especialidad ?? string.Empty)
                                .Contains(filtro, StringComparison.OrdinalIgnoreCase))
                .Select(d => new MedicoFila(d))
                .ToList();

            Medicos.Clear();
            foreach (var f in filas)
                Medicos.Add(f);

            var atendiendo = disponibilidad.Count(d => d.AtiendeAhora);
            ResumenAhora = atendiendo switch
            {
                0 => "Ahora mismo no hay ningún médico atendiendo",
                1 => "1 médico atendiendo ahora",
                _ => $"{atendiendo} médicos atendiendo ahora"
            };

            // Conserva la selección si el médico sigue en la lista
            MedicoSeleccionado = Medicos.FirstOrDefault(m => m.Id == seleccionado);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los médicos");
            _dialogos.MostrarError("Médicos", $"No se pudieron cargar los médicos.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task CargarHorariosAsync()
    {
        Horarios.Clear();
        if (MedicoSeleccionado is null)
            return;

        try
        {
            var tramos = await _servicio.ObtenerHorariosAsync(MedicoSeleccionado.Id);
            foreach (var t in tramos)
                Horarios.Add(new HorarioFila(t));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando los horarios del médico {Id}", MedicoSeleccionado.Id);
            _dialogos.MostrarError("Horarios", $"No se pudieron cargar los horarios.\n\n{ex.Message}");
        }
    }

    // ---------- Alta / edición del médico ----------

    [RelayCommand]
    private void Nuevo()
    {
        EditandoId = null;
        Nombre = Cedula = Exequatur = Especialidad = Telefono = Email = string.Empty;
        CodigoTurno = string.Empty;
        HonorarioTexto = "0";
        Activo = true;
        MensajeError = string.Empty;
        // Lunes a viernes marcados: es lo que trabaja casi todo el mundo, y
        // destildar dos casillas cuesta menos que tildar cinco.
        foreach (var dia in _diasFijos)
            dia.Marcado = dia.Numero is >= 2 and <= 6;
        HoraFijaInicioTexto = "8:00";
        HoraFijaInicioMeridiano = "AM";
        HoraFijaFinTexto = "5:00";
        HoraFijaFinMeridiano = "PM";
        Editando = true;
    }

    [RelayCommand]
    private async Task EditarAsync()
    {
        if (MedicoSeleccionado is null)
            return;
        var medico = await _servicio.ObtenerPorIdAsync(MedicoSeleccionado.Id);
        if (medico is null)
            return;

        EditandoId = medico.Id;
        Nombre = medico.Nombre;
        Cedula = medico.Cedula ?? string.Empty;
        Exequatur = medico.Exequatur ?? string.Empty;
        Especialidad = medico.Especialidad ?? string.Empty;
        Telefono = medico.Telefono ?? string.Empty;
        Email = medico.Email ?? string.Empty;
        CodigoTurno = medico.CodigoTurno ?? string.Empty;
        HonorarioTexto = medico.PorcentajeHonorario.ToString("0.##", CulturaRd);
        Activo = medico.Activo;
        MensajeError = string.Empty;

        // Las casillas salen de los tramos que YA tiene cargados, y el rango
        // por defecto del primero: así reabrir el formulario y guardar sin
        // tocar nada no le cambia el horario a nadie.
        var tramos = await _servicio.ObtenerHorariosAsync(medico.Id);
        var conTramos = tramos.Select(t => t.DiaSemana).ToHashSet();
        foreach (var dia in _diasFijos)
            dia.Marcado = conTramos.Contains(dia.Numero);

        var primero = tramos.OrderBy(t => t.DiaSemana).ThenBy(t => t.HoraInicio).FirstOrDefault();
        if (primero is not null)
        {
            (HoraFijaInicioTexto, HoraFijaInicioMeridiano) = Hora12.Formatear(primero.HoraInicio);
            var salida = tramos.Where(t => t.DiaSemana == primero.DiaSemana).Max(t => t.HoraFin);
            (HoraFijaFinTexto, HoraFijaFinMeridiano) = Hora12.Formatear(salida);
        }

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

            if (!decimal.TryParse(HonorarioTexto, NumberStyles.Number, CulturaRd, out var honorario))
            {
                MensajeError = "El honorario tiene que ser un número (ej. 40 para 40%).";
                return;
            }

            if (!Hora12.TryParsear(HoraFijaInicioTexto, HoraFijaInicioMeridiano, out var entrada) ||
                !Hora12.TryParsear(HoraFijaFinTexto, HoraFijaFinMeridiano, out var salida))
            {
                MensajeError = "El horario va como 8:00 AM y 5:00 PM.";
                return;
            }

            var datos = new MedicoDatos(Nombre, Cedula, Exequatur, Especialidad,
                Telefono, Email, honorario, Activo, CodigoTurno);

            long id;
            if (EditandoId is { } existente)
            {
                await _servicio.ActualizarAsync(existente, datos);
                id = existente;
            }
            else
            {
                id = await _servicio.CrearAsync(datos);
            }

            // Los días van DESPUÉS de guardar al médico: si el alta falla, no
            // quedan tramos colgando de un médico que nunca llegó a existir.
            var marcados = _diasFijos.Where(d => d.Marcado).Select(d => d.Numero).ToList();
            await _servicio.SincronizarDiasFijosAsync(id, marcados, entrada, salida);

            Editando = false;
            await RefrescarAsync();
            MedicoSeleccionado = Medicos.FirstOrDefault(m => m.Id == id);
        }
        catch (ArgumentException ex)
        {
            MensajeError = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando el médico");
            _dialogos.MostrarError("Médicos", $"No se pudo guardar.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (MedicoSeleccionado is null)
            return;
        if (!_dialogos.Confirmar("Eliminar médico",
                $"¿Eliminar a {MedicoSeleccionado.Nombre}?"))
            return;

        try
        {
            await _servicio.EliminarAsync(MedicoSeleccionado.Id);
            await RefrescarAsync();
        }
        catch (InvalidOperationException ex)
        {
            // Regla de negocio (ya tiene facturas): mensaje claro, sin stack
            _dialogos.MostrarError("Eliminar médico", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando el médico");
            _dialogos.MostrarError("Médicos", $"No se pudo eliminar.\n\n{ex.Message}");
        }
    }

    // ---------- Horarios ----------

    [RelayCommand]
    private async Task AgregarHorarioAsync()
    {
        if (MedicoSeleccionado is null)
            return;

        try
        {
            MensajeHorario = string.Empty;

            if (!Hora12.TryParsear(HoraInicioTexto, HoraInicioMeridiano, out var inicio) ||
                !Hora12.TryParsear(HoraFinTexto, HoraFinMeridiano, out var fin))
            {
                MensajeHorario = "Las horas van como 8:00 AM y 12:00 PM.";
                return;
            }

            await _servicio.AgregarHorarioAsync(MedicoSeleccionado.Id,
                new HorarioDatos(DiaSeleccionado.Numero, inicio, fin));

            await CargarHorariosAsync();
            await RefrescarAsync();   // el estado "atiende ahora" pudo cambiar
        }
        catch (ArgumentException ex)
        {
            MensajeHorario = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error agregando el horario");
            _dialogos.MostrarError("Horarios", $"No se pudo agregar el horario.\n\n{ex.Message}");
        }
    }

    [RelayCommand]
    private async Task QuitarHorarioAsync(HorarioFila? fila)
    {
        if (fila is null || MedicoSeleccionado is null)
            return;
        if (!_dialogos.Confirmar("Quitar horario",
                $"¿Quitar el horario de {fila.DiaTexto}, {fila.RangoTexto}?"))
            return;

        try
        {
            await _servicio.QuitarHorarioAsync(MedicoSeleccionado.Id, fila.Id);
            await CargarHorariosAsync();
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error quitando el horario");
            _dialogos.MostrarError("Horarios", $"No se pudo quitar el horario.\n\n{ex.Message}");
        }
    }
}

/// <summary>Opción del combo de días (1 = domingo, como MySQL DAYOFWEEK).</summary>
public record OpcionDia(int Numero, string Nombre)
{
    public override string ToString() => Nombre;
}
