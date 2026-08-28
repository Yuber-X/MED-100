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
/// <param name="MesesInactividad">
/// Desde cuántos meses sin venir se considera que el paciente dejó de venir.
/// Viaja por parámetro y no se lee de los ajustes acá adentro: la fila es un
/// record de presentación y no debe saber de configuración.
/// </param>
public record ExpedienteFila(ResumenExpediente Resumen, int MesesInactividad = 6)
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

    /// <summary>
    /// Hace más de <see cref="MesesInactividad"/> que no viene (pedido 2026-08-25).
    /// La regla vive en <see cref="CalculadoraInactividad"/>: es de negocio —a
    /// quién llama la clínica— y tiene sus propias pruebas.
    /// </summary>
    public bool DejoDeVenir =>
        CalculadoraInactividad.DejoDeVenir(Resumen.UltimaVisitaUtc, DateTime.UtcNow, MesesInactividad);

    /// <summary>Cuánto hace que no viene, en palabras. Vacío si no dejó de venir.</summary>
    public string InactividadTexto =>
        CalculadoraInactividad.Describir(Resumen.UltimaVisitaUtc, DateTime.UtcNow, MesesInactividad);
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
    private readonly AjustesLocales _ajustes;
    private IReadOnlyList<ResumenExpediente> _todos = [];

    /// <summary>El shell abre el expediente de ese paciente.</summary>
    public event Action<long>? ExpedienteSolicitado;

    public ExpedientesViewModel(ExpedienteService expedientes, IDialogService dialogos,
        AjustesLocales ajustes)
    {
        _expedientes = expedientes;
        _dialogos = dialogos;
        _ajustes = ajustes;
    }

    public ObservableCollection<ExpedienteFila> Filas { get; } = [];

    [ObservableProperty] private string _busqueda = string.Empty;
    [ObservableProperty] private bool _soloConDocumentos;
    /// <summary>
    /// "Los que dejaron de venir" (pedido 2026-08-25). Es el filtro que
    /// convierte la alerta en algo accionable: la lista de a quién llamar.
    /// </summary>
    [ObservableProperty] private bool _soloInactivos;
    /// <summary>Cuántos pacientes dejaron de venir, con el corte configurado.</summary>
    [ObservableProperty] private int _inactivos;
    [ObservableProperty] private string _inactivosTexto = string.Empty;
    /// <summary>Manda si la franja de aviso se muestra: hay inactivos Y el aviso está prendido.</summary>
    public bool HayInactivos => Inactivos > 0 && _ajustes.AvisoPacienteInactivoActivo;
    [ObservableProperty] private string _resumenTexto = string.Empty;
    [ObservableProperty] private ExpedienteFila? _seleccionada;
    [ObservableProperty] private bool _ocupado;

    partial void OnBusquedaChanged(string value) => AplicarFiltro();
    partial void OnSoloConDocumentosChanged(bool value) => AplicarFiltro();
    partial void OnSoloInactivosChanged(bool value) => AplicarFiltro();

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
        var meses = Math.Max(1, _ajustes.AvisoPacienteInactivoMeses);
        var filtro = Busqueda.Trim();

        // Se arman TODAS las filas primero: el conteo de inactivos es sobre la
        // cartera entera, no sobre lo que quedó visible. Si contara lo filtrado,
        // buscar un nombre haría "desaparecer" a los demás inactivos.
        var todasLasFilas = _todos.Select(r => new ExpedienteFila(r, meses)).ToList();

        var visibles = todasLasFilas.Where(f =>
            (!SoloConDocumentos || f.Resumen.Documentos > 0) &&
            (!SoloInactivos || f.DejoDeVenir) &&
            (filtro.Length == 0 ||
             f.Resumen.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
             (f.Resumen.Cedula?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false) ||
             (f.Resumen.Telefono?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false)));

        Filas.Clear();
        foreach (var f in visibles)
            Filas.Add(f);

        Inactivos = todasLasFilas.Count(f => f.DejoDeVenir);
        InactivosTexto = Inactivos == 0
            ? string.Empty
            : $"{Inactivos} paciente(s) llevan más de {meses} meses sin venir. " +
              "Marcá \"Los que dejaron de venir\" para verlos.";
        OnPropertyChanged(nameof(HayInactivos));

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
