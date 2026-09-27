using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Un renglón editable del formulario. Es un envoltorio observable de
/// <see cref="IndicacionMedicamento"/>, que es un modelo plano y no notifica
/// cambios a la interfaz.
/// </summary>
public partial class RenglonMedicamento : ObservableObject
{
    [ObservableProperty] private string _medicamento = string.Empty;
    [ObservableProperty] private string _dosis = string.Empty;
    [ObservableProperty] private string _frecuencia = string.Empty;
    [ObservableProperty] private string _duracion = string.Empty;
    [ObservableProperty] private string _instrucciones = string.Empty;

    /// <summary>
    /// El medicamento elegido del listado de los más usados (2026-09-21). Al
    /// elegirlo se completan dosis, frecuencia y duración con las de la última
    /// vez, pero SOLO las que están en blanco: si ya se escribió algo, manda lo
    /// que se escribió. Autocompletar arriba de la dosis de otro paciente es
    /// justo el error que no se puede cometer acá.
    /// </summary>
    [ObservableProperty] private MedicamentoFrecuente? _sugerencia;

    public RenglonMedicamento() { }

    /// <summary>Carga un renglón ya guardado para poder corregirlo.</summary>
    public RenglonMedicamento(IndicacionMedicamento m)
    {
        _medicamento = m.Medicamento;
        _dosis = m.Dosis ?? string.Empty;
        _frecuencia = m.Frecuencia ?? string.Empty;
        _duracion = m.Duracion ?? string.Empty;
        _instrucciones = m.Instrucciones ?? string.Empty;
    }

    partial void OnSugerenciaChanged(MedicamentoFrecuente? value)
    {
        if (value is null)
            return;

        Medicamento = value.Medicamento;
        if (string.IsNullOrWhiteSpace(Dosis)) Dosis = value.Dosis ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Frecuencia)) Frecuencia = value.Frecuencia ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Duracion)) Duracion = value.Duracion ?? string.Empty;
    }

    public IndicacionMedicamento AModelo() => new()
    {
        Medicamento = Medicamento.Trim(),
        Dosis = Dosis,
        Frecuencia = Frecuencia,
        Duracion = Duracion,
        Instrucciones = Instrucciones
    };

    public bool EstaVacio => string.IsNullOrWhiteSpace(Medicamento);
}

/// <summary>
/// Medicamentos indicados (013). Pedido de Yuber del 2026-09-06: <i>"cuando el
/// médico le indique los medicamentos también puedan ser colocados acá para
/// mejor organización e historial de los procesos durante el día trabajado"</i>.
///
/// La pantalla está organizada POR DÍA y no por paciente, porque eso fue lo que
/// se pidió: poder releer la jornada. El historial de un paciente sale de su
/// ficha.
///
/// ⚠ Contenido clínico: hasta CONSULTAR exige el permiso `indicaciones`
/// (IndicacionService). Ver CLAUDE.md §1.1.
/// </summary>
public partial class IndicacionesViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");

    private readonly IndicacionService _indicaciones;
    private readonly ClienteService _clientes;
    private readonly MedicoService _medicos;
    private readonly IDialogService _dialogos;

    public IndicacionesViewModel(IndicacionService indicaciones, ClienteService clientes,
        MedicoService medicos, IDialogService dialogos)
    {
        _indicaciones = indicaciones;
        _clientes = clientes;
        _medicos = medicos;
        _dialogos = dialogos;

        _dia = DateTime.Today;
        LimpiarFormulario();
    }

    public ObservableCollection<Indicacion> DelDia { get; } = [];
    public ObservableCollection<Opcion<long?>> Pacientes { get; } = [];
    public ObservableCollection<Opcion<long?>> Medicos { get; } = [];
    public ObservableCollection<RenglonMedicamento> Renglones { get; } = [];

    /// <summary>
    /// Los medicamentos que más se indican, para elegirlos del desplegable en
    /// vez de tipearlos (pedido de la clínica 2026-09-21).
    /// </summary>
    public ObservableCollection<MedicamentoFrecuente> MasUsados { get; } = [];

    /// <summary>
    /// Pide imprimir la receta de lo seleccionado (pedido de la clínica
    /// 2026-09-21). El ViewModel no toca ventanas ni impresoras: arma los datos
    /// y App los lleva a la vista previa, igual que el turno y el cierre.
    /// </summary>
    public event Action<RecetaImpresa>? RecetaSolicitada;

    [ObservableProperty] private DateTime _dia;
    [ObservableProperty] private Opcion<long?>? _pacienteSeleccionado;
    [ObservableProperty] private Opcion<long?>? _medicoSeleccionado;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private Indicacion? _seleccionada;

    /// <summary>
    /// Id de la indicación que se está corrigiendo, o null si el formulario es
    /// para una nueva. Es lo que hace que el mismo formulario sirva para las dos
    /// cosas: corregir en otra pantalla obligaría a duplicar los renglones.
    /// </summary>
    [ObservableProperty] private long? _corrigiendoId;

    public bool PuedeRegistrar => SesionActual.TienePermiso("indicaciones");
    public bool EsAdmin => SesionActual.EsAdmin;
    public bool HaySeleccion => Seleccionada is not null;

    /// <summary>
    /// True cuando el día que se está mirando NO es hoy. La pantalla se pone
    /// solo-lectura: una indicación se anota el día que ocurre, y dejar cargar
    /// hacia atrás invitaría a "acomodar" el historial.
    /// </summary>
    public bool EsDiaPasado => DateOnly.FromDateTime(Dia) != FechaNegocio.Hoy;

    public bool PuedeCargar => PuedeRegistrar && !EsDiaPasado;

    public bool EnCorreccion => CorrigiendoId is not null;

    /// <summary>
    /// Solo se corrige lo del día que se está mirando y siendo hoy: para atrás
    /// la pantalla entera es de lectura (ver <see cref="EsDiaPasado"/>).
    /// </summary>
    public bool PuedeCorregir => HaySeleccion && PuedeCargar;

    public string TituloFormulario =>
        EnCorreccion ? "Corregir los medicamentos" : "Anotar lo indicado";

    public string TextoBotonGuardar =>
        EnCorreccion ? "Guardar los cambios" : "Guardar";

    /// <summary>
    /// Corrigiendo se tocan los medicamentos, no a quién ni quién lo indicó:
    /// cambiarle el paciente a algo ya guardado no es corregir, es otra cosa.
    /// </summary>
    public bool PuedeCambiarCabecera => !EnCorreccion;

    partial void OnCorrigiendoIdChanged(long? value)
    {
        OnPropertyChanged(nameof(EnCorreccion));
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(TextoBotonGuardar));
        OnPropertyChanged(nameof(PuedeCambiarCabecera));
    }

    partial void OnSeleccionadaChanged(Indicacion? value)
    {
        OnPropertyChanged(nameof(HaySeleccion));
        OnPropertyChanged(nameof(PuedeCorregir));
    }

    partial void OnDiaChanged(DateTime value)
    {
        OnPropertyChanged(nameof(EsDiaPasado));
        OnPropertyChanged(nameof(PuedeCargar));
        OnPropertyChanged(nameof(PuedeCorregir));
        // Cambiar de día deja a medias cualquier corrección: lo que está en el
        // formulario es de una indicación que ya no se ve en la lista.
        LimpiarFormulario();
        _ = CargarDelDiaAsync();
    }

    public async Task RefrescarAsync()
    {
        OnPropertyChanged(nameof(PuedeRegistrar));   // cambia con quién entró
        OnPropertyChanged(nameof(PuedeCargar));
        OnPropertyChanged(nameof(EsAdmin));

        if (!PuedeRegistrar)
        {
            // Sin permiso no se pide nada a la base: el servicio tiraría igual,
            // y una pantalla vacía dice lo mismo sin un error en la cara.
            DelDia.Clear();
            return;
        }

        try
        {
            Ocupado = true;

            var pacientePrevio = PacienteSeleccionado?.Valor;
            Pacientes.Clear();
            foreach (var c in await _clientes.ObtenerTodosAsync())
                Pacientes.Add(new Opcion<long?>(c.Id, c.Nombre));
            PacienteSeleccionado = Pacientes.FirstOrDefault(p => p.Valor == pacientePrevio);

            var medicoPrevio = MedicoSeleccionado?.Valor;
            Medicos.Clear();
            Medicos.Add(new Opcion<long?>(null, "Sin médico"));
            foreach (var m in await _medicos.ObtenerActivosAsync())
                Medicos.Add(new Opcion<long?>(m.Id, m.Nombre));
            MedicoSeleccionado = Medicos.FirstOrDefault(m => m.Valor == medicoPrevio) ?? Medicos[0];

            await CargarMasUsadosAsync();
            await CargarDelDiaAsync();
        }
        catch (UnauthorizedAccessException)
        {
            DelDia.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la pantalla de indicaciones");
            _dialogos.MostrarError("Medicamentos indicados",
                $"No se pudo cargar la pantalla.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Los más usados son una ayuda para escribir, no un dato de la pantalla:
    /// si la consulta falla, la pantalla sigue sirviendo con el campo libre y
    /// queda el aviso en el log. No se le corta la jornada a nadie por esto.
    /// </summary>
    private async Task CargarMasUsadosAsync()
    {
        try
        {
            var lista = await _indicaciones.ObtenerMasUsadosAsync();
            MasUsados.Clear();
            foreach (var m in lista)
                MasUsados.Add(m);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo cargar el listado de medicamentos más usados");
        }
    }

    private async Task CargarDelDiaAsync()
    {
        if (!PuedeRegistrar)
            return;

        try
        {
            var lista = await _indicaciones.ObtenerDelDiaAsync(DateOnly.FromDateTime(Dia));
            DelDia.Clear();
            foreach (var i in lista)
                DelDia.Add(i);
            Seleccionada = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando las indicaciones del día");
            _dialogos.MostrarError("Medicamentos indicados",
                $"No se pudo cargar el día.\n\n{ex.Message}");
        }
    }

    /// <summary>
    /// Deja tres renglones en blanco. Tres y no uno porque una indicación casi
    /// nunca es de un solo medicamento, y tener que apretar "Agregar" antes de
    /// escribir el segundo es fricción en el mostrador.
    /// </summary>
    private void LimpiarFormulario()
    {
        CorrigiendoId = null;
        Renglones.Clear();
        for (var i = 0; i < 3; i++)
            Renglones.Add(new RenglonMedicamento());
        Notas = string.Empty;
        PacienteSeleccionado = null;
    }

    [RelayCommand]
    private void AgregarRenglon() => Renglones.Add(new RenglonMedicamento());

    [RelayCommand]
    private void QuitarRenglon(RenglonMedicamento? renglon)
    {
        if (renglon is null)
            return;
        Renglones.Remove(renglon);
        // Nunca queda el formulario sin ninguna fila: sin caja donde escribir,
        // la pantalla parece rota.
        if (Renglones.Count == 0)
            Renglones.Add(new RenglonMedicamento());
    }

    /// <summary>
    /// Trae lo seleccionado al formulario para quitarle un medicamento,
    /// arreglarle la dosis o agregarle el que faltó (pedido de la clínica
    /// 2026-09-21). El paciente y el médico quedan a la vista pero bloqueados.
    /// </summary>
    [RelayCommand]
    private void Corregir()
    {
        if (Seleccionada is not { } indicacion || !PuedeCargar)
            return;

        CorrigiendoId = indicacion.Id;
        PacienteSeleccionado = Pacientes.FirstOrDefault(p => p.Valor == indicacion.ClienteId);
        MedicoSeleccionado = Medicos.FirstOrDefault(m => m.Valor == indicacion.MedicoId)
                             ?? Medicos.FirstOrDefault();
        Notas = indicacion.Notas ?? string.Empty;

        Renglones.Clear();
        foreach (var m in indicacion.Medicamentos)
            Renglones.Add(new RenglonMedicamento(m));
        if (Renglones.Count == 0)
            Renglones.Add(new RenglonMedicamento());
    }

    [RelayCommand]
    private void CancelarCorreccion() => LimpiarFormulario();

    /// <summary>
    /// Arma la receta de lo seleccionado y la manda a la vista previa.
    ///
    /// Se leen el paciente y el médico de nuevo en vez de usar lo que trae la
    /// lista: la receta necesita la cédula, la edad y el exequátur, que no
    /// están en la grilla. Si el médico no está cargado igual se imprime, con
    /// la línea de la firma y el exequátur en blanco — es un papel que se
    /// completa a mano.
    /// </summary>
    [RelayCommand]
    private async Task ImprimirRecetaAsync()
    {
        if (Seleccionada is not { } indicacion)
            return;

        try
        {
            Ocupado = true;

            var paciente = await _clientes.ObtenerPorIdAsync(indicacion.ClienteId);
            var medico = indicacion.MedicoId is { } medicoId
                ? await _medicos.ObtenerPorIdAsync(medicoId)
                : null;

            RecetaSolicitada?.Invoke(new RecetaImpresa(
                paciente?.Nombre ?? indicacion.ClienteNombre,
                paciente?.Cedula,
                EdadDe(paciente?.FechaNacimiento),
                indicacion.FechaUtc,
                medico?.Nombre ?? indicacion.MedicoNombre,
                medico?.Especialidad,
                medico?.Exequatur,
                indicacion.Medicamentos,
                indicacion.Notas));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error preparando la receta de la indicación {Id}", indicacion.Id);
            _dialogos.MostrarError("Receta", $"No se pudo preparar la receta.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Años cumplidos a la fecha de negocio. Null si no hay fecha de nacimiento.</summary>
    private static int? EdadDe(DateOnly? nacimiento)
    {
        if (nacimiento is not { } fecha)
            return null;

        var hoy = FechaNegocio.Hoy;
        var edad = hoy.Year - fecha.Year;
        if (fecha.AddYears(edad) > hoy)
            edad--;
        return edad < 0 ? null : edad;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (CorrigiendoId is { } enCorreccion)
        {
            await GuardarCorreccionAsync(enCorreccion);
            return;
        }

        if (PacienteSeleccionado?.Valor is not { } clienteId)
        {
            _dialogos.MostrarError("Medicamentos indicados", "Elegí el paciente.");
            return;
        }

        var medicamentos = Renglones.Where(r => !r.EstaVacio).Select(r => r.AModelo()).ToList();
        if (medicamentos.Count == 0)
        {
            _dialogos.MostrarError("Medicamentos indicados", "Escribí al menos un medicamento.");
            return;
        }

        var indicacion = new Indicacion
        {
            ClienteId = clienteId,
            ClienteNombre = PacienteSeleccionado.Etiqueta,
            MedicoId = MedicoSeleccionado?.Valor,
            MedicoNombre = MedicoSeleccionado?.Valor is null ? null : MedicoSeleccionado.Etiqueta,
            Notas = Notas,
            Medicamentos = medicamentos
        };

        try
        {
            Ocupado = true;
            await _indicaciones.CrearAsync(indicacion);
            LimpiarFormulario();
            await CargarMasUsadosAsync();
            await CargarDelDiaAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Medicamentos indicados", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error guardando una indicación");
            _dialogos.MostrarError("Medicamentos indicados",
                $"No se pudo guardar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async Task GuardarCorreccionAsync(long id)
    {
        // Se manda la indicación tal como se leyó del día: el servicio necesita
        // los medicamentos de ANTES para escribirlos en la auditoría.
        var original = DelDia.FirstOrDefault(i => i.Id == id);
        if (original is null)
        {
            _dialogos.MostrarError("Corregir",
                "La indicación ya no está en la lista del día. Volvé a cargar la pantalla.");
            LimpiarFormulario();
            return;
        }

        var medicamentos = Renglones.Where(r => !r.EstaVacio).Select(r => r.AModelo()).ToList();
        if (medicamentos.Count == 0)
        {
            _dialogos.MostrarError("Corregir",
                "Tiene que quedar al menos un medicamento. Si no queda ninguno, " +
                "lo que corresponde es dar de baja la indicación completa.");
            return;
        }

        try
        {
            Ocupado = true;
            await _indicaciones.ActualizarMedicamentosAsync(original, medicamentos);
            LimpiarFormulario();
            await CargarMasUsadosAsync();
            await CargarDelDiaAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Corregir", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error corrigiendo los medicamentos de la indicación {Id}", id);
            _dialogos.MostrarError("Corregir", $"No se pudo guardar.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task EliminarAsync()
    {
        if (Seleccionada is not { } indicacion)
            return;

        var motivo = _dialogos.PedirTexto("Dar de baja",
            $"¿Por qué se da de baja lo indicado a {indicacion.ClienteNombre}? " +
            "Queda registrado quién lo hizo y por qué.");
        if (string.IsNullOrWhiteSpace(motivo))
            return;

        try
        {
            Ocupado = true;
            await _indicaciones.EliminarAsync(indicacion.Id, motivo);
            await CargarDelDiaAsync();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
        {
            _dialogos.MostrarError("Dar de baja", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private void IrAHoy() => Dia = DateTime.Today;

    public string DiaTexto => Dia.ToString("dddd d 'de' MMMM", CulturaDo);
}
