using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila del almacén: un paciente y el estado de su expediente.</summary>
public record ExpedienteFila(ResumenExpediente Resumen)
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    public long ClienteId => Resumen.ClienteId;
    public string Nombre => Resumen.Nombre;
    public string CedulaTexto => string.IsNullOrWhiteSpace(Resumen.Cedula) ? "—" : Resumen.Cedula!;
    public string TelefonoTexto => string.IsNullOrWhiteSpace(Resumen.Telefono) ? "—" : Resumen.Telefono!;

    public int Documentos => Resumen.Documentos;
    public string DocumentosTexto => Resumen.Documentos == 0 ? "—" : Resumen.Documentos.ToString();

    /// <summary>Sin papeles: la lista lo marca en gris para que salte a la vista.</summary>
    public bool TieneDocumentos => Resumen.Documentos > 0;

    public string CitasTexto => Resumen.Citas == 0 ? "—" : Resumen.Citas.ToString();
    public string FacturasTexto => Resumen.Facturas == 0 ? "—" : Resumen.Facturas.ToString();

    public string UltimaVisitaTexto => Resumen.UltimaVisitaUtc is { } v
        ? FechaNegocio.AUtcLocal(v).ToString("dd/MM/yyyy", CulturaRd)
        : "Nunca vino";

    public string UltimoDocumentoTexto => Resumen.UltimoDocumentoUtc is { } d
        ? FechaNegocio.AUtcLocal(d).ToString("dd/MM/yyyy", CulturaRd)
        : "—";
}

/// <summary>
/// Almacén de expedientes (pedido de Yuber 2026-08-14, copiando el almacén de
/// contratos de FAControl): la lista de pacientes con cuántos papeles tiene
/// cada uno, y desde ahí se entra a su expediente.
///
/// Contesta la pregunta con la que nació el módulo: <b>"volvió este paciente —
/// ¿qué tenemos ya de él?"</b>. Por eso el filtro por defecto es "solo los que
/// tienen documentos" apagado: hay que ver también a los que NO tienen nada,
/// que son a los que hay que pedirles los papeles.
/// </summary>
public partial class ExpedientesViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly ExpedienteService _expedientes;
    private readonly IDialogService _dialogos;
    private IReadOnlyList<ResumenExpediente> _todos = [];

    /// <summary>El shell abre el expediente de ese paciente.</summary>
    public event Action<long>? ExpedienteSolicitado;

    public ExpedientesViewModel(ExpedienteService expedientes, IDialogService dialogos)
    {
        _expedientes = expedientes;
        _dialogos = dialogos;
    }

    public ObservableCollection<ExpedienteFila> Filas { get; } = [];

    [ObservableProperty] private string _busqueda = string.Empty;
    [ObservableProperty] private bool _soloConDocumentos;
    [ObservableProperty] private string _resumenTexto = string.Empty;
    [ObservableProperty] private ExpedienteFila? _seleccionada;
    [ObservableProperty] private bool _ocupado;

    partial void OnBusquedaChanged(string value) => AplicarFiltro();
    partial void OnSoloConDocumentosChanged(bool value) => AplicarFiltro();

    public async Task RefrescarAsync()
    {
        try
        {
            Ocupado = true;
            _todos = await _expedientes.ObtenerResumenAsync();
            AplicarFiltro();
        }
        catch (UnauthorizedAccessException ex)
        {
            _dialogos.MostrarError("Expedientes", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el almacén de expedientes");
            _dialogos.MostrarError("Expedientes",
                $"No se pudo cargar el almacén de expedientes.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void AplicarFiltro()
    {
        var filtro = Busqueda.Trim();
        var visibles = _todos.Where(r =>
            (!SoloConDocumentos || r.Documentos > 0) &&
            (filtro.Length == 0 ||
             r.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
             (r.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (r.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false)));

        Filas.Clear();
        foreach (var r in visibles)
            Filas.Add(new ExpedienteFila(r));

        var conPapeles = _todos.Count(r => r.Documentos > 0);
        var papeles = _todos.Sum(r => r.Documentos);
        ResumenTexto = _todos.Count == 0
            ? "Todavía no hay pacientes registrados."
            : $"Mostrando {Filas.Count} de {_todos.Count} pacientes · " +
              $"{conPapeles} con expediente · {papeles} documento(s) guardados";
    }

    [RelayCommand]
    private void AbrirExpediente(ExpedienteFila? fila)
    {
        var destino = fila ?? Seleccionada;
        if (destino is not null)
            ExpedienteSolicitado?.Invoke(destino.ClienteId);
    }
}
