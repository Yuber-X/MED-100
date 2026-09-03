using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Opción de un ComboBox: el valor real y cómo se lee en pantalla.</summary>
public record OpcionSexo(SexoPaciente? Valor, string Etiqueta);

/// <summary>Ídem para el tipo de procedencia.</summary>
public record OpcionTipoReferidor(TipoReferidor Valor, string Etiqueta);

/// <summary>
/// Formulario de paciente (nuevo y edición). Errores de validación inline.
///
/// La procedencia se captura en dos controles: el TIPO se elige de una lista
/// cerrada y el NOMBRE se escribe libre con autocompletado de los que ya
/// existen. Al guardar, <see cref="ReferidorService.ResolverAsync"/> reutiliza
/// el referidor si el nombre ya está y lo crea si es nuevo. Se hace así, y no
/// con una pantalla aparte de catálogo, porque la recepcionista se entera de
/// quién mandó al paciente mientras lo registra: si hay que salir a otra
/// pantalla, el dato termina en las notas y el reporte queda vacío.
/// </summary>
public partial class ClienteFormViewModel : ObservableObject
{
    private readonly ClienteService _servicio;
    private readonly ReferidorService _referidores;
    private readonly MedicoService _medicos;
    private readonly ArsService _ars;
    private readonly IDialogService _dialogos;
    private long? _clienteId; // null = nuevo
    private IReadOnlyList<Referidor> _referidoresActivos = [];

    // Catálogos vivos de la clínica. Se cachean por formulario: la lista de
    // médicos y de ARS no cambia mientras se registra a una persona.
    private IReadOnlyList<string> _nombresDeMedicos = [];
    private IReadOnlyList<string> _nombresDeArs = [];

    public event Action<long>? Guardado;
    public event Action? Cancelado;

    public ClienteFormViewModel(ClienteService servicio, ReferidorService referidores,
        MedicoService medicos, ArsService ars, IDialogService dialogos)
    {
        _servicio = servicio;
        _referidores = referidores;
        _medicos = medicos;
        _ars = ars;
        _dialogos = dialogos;
    }

    [ObservableProperty] private string _titulo = "Nuevo paciente";
    [ObservableProperty] private string _cedula = string.Empty;
    [ObservableProperty] private string _nombre = string.Empty;
    [ObservableProperty] private string _telefono = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private DateTime? _fechaNacimiento;
    [ObservableProperty] private string _direccion = string.Empty;
    /// <summary>
    /// Última consulta ANTES de usar MED-100, para los pacientes que se cargan
    /// pasando los archivos viejos (pedido de la clínica 2026-08-27). Sin esto
    /// figuran como "nunca vino" y quedan fuera del aviso de los que dejaron de
    /// venir — que son justamente a los que hay que llamar.
    /// </summary>
    [ObservableProperty] private DateTime? _ultimaVisitaPrevia;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private string _mensajeError = string.Empty;
    [ObservableProperty] private bool _ocupado;

    // ---------- Sexo ----------
    public IReadOnlyList<OpcionSexo> OpcionesSexo { get; } =
    [
        new(null, "Sin especificar"),
        new(SexoPaciente.Femenino, "Femenino"),
        new(SexoPaciente.Masculino, "Masculino"),
        new(SexoPaciente.Otro, "Otro")
    ];

    [ObservableProperty] private OpcionSexo? _sexoSeleccionado;

    // ---------- Procedencia ----------
    public IReadOnlyList<OpcionTipoReferidor> TiposReferidor { get; } =
        Enum.GetValues<TipoReferidor>()
            .Select(t => new OpcionTipoReferidor(t, ReferidorService.Etiqueta(t)))
            .ToList();

    [ObservableProperty] private OpcionTipoReferidor? _tipoReferidorSeleccionado;
    [ObservableProperty] private string _referidorNombre = string.Empty;

    /// <summary>Texto de ayuda bajo el combo: cambia según el tipo elegido.</summary>
    [ObservableProperty] private string _ayudaReferidor =
        "Escribí el nombre. Si ya está registrado se reutiliza; si es nuevo se agrega solo. " +
        "Dejalo vacío si el paciente vino por su cuenta.";

    /// <summary>Nombres ya registrados del tipo elegido, para el autocompletado.</summary>
    public ObservableCollection<string> SugerenciasReferidor { get; } = [];

    partial void OnTipoReferidorSeleccionadoChanged(OpcionTipoReferidor? value) =>
        ActualizarSugerencias();

    /// <summary>Edad en vivo mientras se teclea la fecha. Es la comprobación del dedazo.</summary>
    public string EdadTexto =>
        FechaNacimiento is { } f ? EdadPaciente.Texto(DateOnly.FromDateTime(f)) : string.Empty;

    partial void OnFechaNacimientoChanged(DateTime? value) => OnPropertyChanged(nameof(EdadTexto));

    /// <summary>Último día que el calendario de "última visita" deja elegir: hoy.</summary>
    public DateTime UltimoDiaVisitable => FechaNegocio.Hoy.ToDateTime(TimeOnly.MinValue);

    public async Task PrepararNuevoAsync()
    {
        _clienteId = null;
        Titulo = "Nuevo paciente";
        Cedula = Nombre = Telefono = Email = Direccion = Notas = string.Empty;
        FechaNacimiento = null;
        UltimaVisitaPrevia = null;
        SexoSeleccionado = OpcionesSexo[0];
        ReferidorNombre = string.Empty;
        TipoReferidorSeleccionado = TiposReferidor.First(t => t.Valor == TipoReferidor.Medico);
        MensajeError = string.Empty;
        await CargarReferidoresAsync();
    }

    public async Task PrepararEdicionAsync(long clienteId)
    {
        var cliente = await _servicio.ObtenerPorIdAsync(clienteId)
            ?? throw new InvalidOperationException("El paciente no existe o fue eliminado.");

        _clienteId = clienteId;
        Titulo = $"Editar paciente — {cliente.Nombre}";
        Cedula = cliente.Cedula ?? string.Empty;
        Nombre = cliente.Nombre;
        Telefono = cliente.Telefono ?? string.Empty;
        Email = cliente.Email ?? string.Empty;
        FechaNacimiento = cliente.FechaNacimiento?.ToDateTime(TimeOnly.MinValue);
        SexoSeleccionado = OpcionesSexo.FirstOrDefault(o => o.Valor == cliente.Sexo) ?? OpcionesSexo[0];
        Direccion = cliente.Direccion ?? string.Empty;
        UltimaVisitaPrevia = cliente.UltimaVisitaPrevia?.ToDateTime(TimeOnly.MinValue);
        Notas = cliente.Notas ?? string.Empty;
        MensajeError = string.Empty;

        await CargarReferidoresAsync();

        var referidor = cliente.ReferidorId is { } id
            ? _referidoresActivos.FirstOrDefault(r => r.Id == id)
            : null;
        TipoReferidorSeleccionado = TiposReferidor.First(t =>
            t.Valor == (referidor?.Tipo ?? TipoReferidor.Medico));
        ReferidorNombre = referidor?.Nombre ?? cliente.ReferidorNombre ?? string.Empty;
    }

    private async Task CargarReferidoresAsync()
    {
        try
        {
            _referidoresActivos = await _referidores.ObtenerActivosAsync();
        }
        catch (Exception ex)
        {
            // Que falle el catálogo no puede impedir registrar al paciente:
            // se sigue sin autocompletado y el referidor se crea al guardar.
            Log.Warning(ex, "No se pudo cargar el catálogo de procedencias");
            _referidoresActivos = [];
        }

        // Médicos y ARS de la clínica: quien refiere casi siempre es alguien
        // que YA está cargado en el sistema, y hacerlo teclear el nombre otra
        // vez termina creando "Dr. Peña", "Dr Peña" y "doctor peña" como tres
        // procedencias distintas, que después no suman en ningún reporte.
        try
        {
            _nombresDeMedicos = (await _medicos.ObtenerActivosAsync())
                .Select(m => m.Nombre).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo cargar la lista de médicos para la procedencia");
            _nombresDeMedicos = [];
        }

        try
        {
            _nombresDeArs = (await _ars.ObtenerActivasAsync()).Select(a => a.Nombre).ToList();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo cargar la lista de ARS para la procedencia");
            _nombresDeArs = [];
        }

        ActualizarSugerencias();
    }

    /// <summary>
    /// Llena el segundo combo según el TIPO elegido:
    ///  · Médico → los médicos activos de la clínica;
    ///  · ARS    → las aseguradoras del catálogo;
    ///  · resto  → solo lo que ya se usó antes con ese tipo.
    ///
    /// En los tres casos se agregan las procedencias YA registradas de ese
    /// tipo, y el combo sigue siendo editable: si el que refirió no está en
    /// ninguna lista (un médico de otra clínica, una campaña nueva), se
    /// escribe y se crea al guardar.
    /// </summary>
    private void ActualizarSugerencias()
    {
        var tipo = TipoReferidorSeleccionado?.Valor;

        var delCatalogo = tipo switch
        {
            TipoReferidor.Medico => _nombresDeMedicos,
            TipoReferidor.Ars => _nombresDeArs,
            _ => (IReadOnlyList<string>)[]
        };

        var yaUsados = _referidoresActivos
            .Where(r => tipo is null || r.Tipo == tipo)
            .Select(r => r.Nombre);

        SugerenciasReferidor.Clear();
        foreach (var nombre in delCatalogo.Concat(yaUsados)
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            SugerenciasReferidor.Add(nombre);

        AyudaReferidor = tipo switch
        {
            TipoReferidor.Medico =>
                "Elegí al médico de la lista. Si es de otra clínica y no está, escribí su nombre.",
            TipoReferidor.Ars =>
                "Elegí la ARS de la lista. Si no está, escribí el nombre y se agrega.",
            _ =>
                "Escribí el nombre. Si ya está registrado se reutiliza; si es nuevo se agrega solo. " +
                "Dejalo vacío si el paciente vino por su cuenta."
        };
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        try
        {
            Ocupado = true;
            MensajeError = string.Empty;

            // Una "última visita" en el futuro no es un dato retroactivo: es un
            // dedazo, y dejaría al paciente marcado como activo para siempre.
            if (UltimaVisitaPrevia is { } visita && DateOnly.FromDateTime(visita) > FechaNegocio.Hoy)
            {
                MensajeError = "La última visita no puede ser una fecha futura.";
                return;
            }

            // El referidor se resuelve ANTES de armar el paciente: si el nombre
            // ya existe se reutiliza y si es nuevo se crea, pero el paciente
            // siempre termina con un id, nunca con texto suelto.
            var referidorId = await _referidores.ResolverAsync(
                ReferidorNombre,
                TipoReferidorSeleccionado?.Valor ?? TipoReferidor.Otro);

            var datos = new ClienteDatos(
                Cedula, Nombre, Telefono, Direccion, Notas,
                Email,
                FechaNacimiento is { } f ? DateOnly.FromDateTime(f) : null,
                SexoSeleccionado?.Valor,
                referidorId,
                UltimaVisitaPrevia is { } v ? DateOnly.FromDateTime(v) : null);

            long id;
            if (_clienteId is null)
            {
                id = await _servicio.CrearAsync(datos);
                _dialogos.Informar("Paciente creado", $"{datos.Nombre} se registró correctamente.");
            }
            else
            {
                id = _clienteId.Value;
                await _servicio.ActualizarAsync(id, datos);
            }

            Guardado?.Invoke(id);
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
            Log.Error(ex, "Error guardando el paciente");
            _dialogos.MostrarError("Guardar paciente", $"No se pudo guardar el paciente.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void Cancelar() => Cancelado?.Invoke();
}
