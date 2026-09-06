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

    [ObservableProperty] private DateTime _dia;
    [ObservableProperty] private Opcion<long?>? _pacienteSeleccionado;
    [ObservableProperty] private Opcion<long?>? _medicoSeleccionado;
    [ObservableProperty] private string _notas = string.Empty;
    [ObservableProperty] private bool _ocupado;
    [ObservableProperty] private Indicacion? _seleccionada;

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

    partial void OnSeleccionadaChanged(Indicacion? value) =>
        OnPropertyChanged(nameof(HaySeleccion));

    partial void OnDiaChanged(DateTime value)
    {
        OnPropertyChanged(nameof(EsDiaPasado));
        OnPropertyChanged(nameof(PuedeCargar));
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

    [RelayCommand]
    private async Task GuardarAsync()
    {
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
