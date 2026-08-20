using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Reporte de ventas por rango de fechas (permiso 'reportes'): totales,
/// desglose por método de pago, tendencia diaria, top de productos y
/// desempeño por cajero. Las anuladas se informan sin sumar.
/// </summary>
public partial class ReportesViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly AnaliticaService _analitica;
    private readonly IDialogService _dialogos;

    public ReportesViewModel(AnaliticaService analitica, IDialogService dialogos)
    {
        _analitica = analitica;
        _dialogos = dialogos;

        Atajos =
        [
            new Opcion<RangoReporte>(RangoReporte.Hoy, "Hoy"),
            new Opcion<RangoReporte>(RangoReporte.Ayer, "Ayer"),
            new Opcion<RangoReporte>(RangoReporte.EstaSemana, "Esta semana"),
            new Opcion<RangoReporte>(RangoReporte.EsteMes, "Este mes"),
            new Opcion<RangoReporte>(RangoReporte.MesPasado, "Mes pasado"),
            new Opcion<RangoReporte>(RangoReporte.Personalizado, "Personalizado")
        ];
        _atajoSeleccionado = Atajos[3];   // Este mes

        var (desde, hasta) = AnaliticaService.CalcularRango(RangoReporte.EsteMes, FechaNegocio.Hoy);
        _desde = desde.ToDateTime(TimeOnly.MinValue);
        _hasta = hasta.ToDateTime(TimeOnly.MinValue);
    }

    public IReadOnlyList<Opcion<RangoReporte>> Atajos { get; }
    public ObservableCollection<ProductoTopFila> TopProductos { get; } = [];
    public ObservableCollection<VendedorFila> PorCajero { get; } = [];

    [ObservableProperty] private Opcion<RangoReporte> _atajoSeleccionado;
    [ObservableProperty] private DateTime _desde;
    [ObservableProperty] private DateTime _hasta;

    [ObservableProperty] private string _periodoTexto = string.Empty;
    [ObservableProperty] private decimal _totalVendido;
    [ObservableProperty] private int _totalFacturas;
    [ObservableProperty] private decimal _ticketPromedio;
    [ObservableProperty] private decimal _totalItbis;
    [ObservableProperty] private decimal _efectivo;
    [ObservableProperty] private decimal _tarjeta;
    [ObservableProperty] private decimal _transferencia;
    [ObservableProperty] private decimal _mixto;
    [ObservableProperty] private int _facturasAnuladas;
    [ObservableProperty] private decimal _montoAnulado;
    [ObservableProperty] private bool _sinDatos;
    [ObservableProperty] private bool _ocupado;

    [ObservableProperty] private ISeries[] _series = [];
    [ObservableProperty] private Axis[] _xAxes = [];
    [ObservableProperty] private Axis[] _yAxes = [];

    partial void OnAtajoSeleccionadoChanged(Opcion<RangoReporte> value)
    {
        if (value.Valor == RangoReporte.Personalizado)
            return;   // el usuario elige las fechas a mano

        var (desde, hasta) = AnaliticaService.CalcularRango(value.Valor, FechaNegocio.Hoy);
        Desde = desde.ToDateTime(TimeOnly.MinValue);
        Hasta = hasta.ToDateTime(TimeOnly.MinValue);
        _ = GenerarAsync();
    }

    public Task RefrescarAsync() => GenerarAsync();

    [RelayCommand]
    private async Task GenerarAsync()
    {
        var desde = DateOnly.FromDateTime(Desde);
        var hasta = DateOnly.FromDateTime(Hasta);

        try
        {
            Ocupado = true;
            var reporte = await _analitica.ObtenerReporteAsync(desde, hasta);

            PeriodoTexto = desde == hasta
                ? $"Ventas del {desde.ToString("dd 'de' MMMM yyyy", CulturaRd)}"
                : $"Ventas del {desde.ToString("dd/MM/yyyy", CulturaRd)} al {hasta.ToString("dd/MM/yyyy", CulturaRd)}";

            TotalVendido = reporte.TotalVendido;
            TotalFacturas = reporte.TotalFacturas;
            TicketPromedio = reporte.TicketPromedio;
            TotalItbis = reporte.TotalItbis;
            Efectivo = reporte.PorMetodo.Efectivo;
            Tarjeta = reporte.PorMetodo.Tarjeta;
            Transferencia = reporte.PorMetodo.Transferencia;
            Mixto = reporte.PorMetodo.Mixto;
            FacturasAnuladas = reporte.FacturasAnuladas;
            MontoAnulado = reporte.MontoAnulado;
            SinDatos = reporte.TotalFacturas == 0;

            (Series, XAxes, YAxes) = GraficoVentas.Construir(reporte.VentasPorDia, desde, hasta);

            TopProductos.Clear();
            for (var i = 0; i < reporte.TopProductos.Count; i++)
                TopProductos.Add(new ProductoTopFila(i + 1, reporte.TopProductos[i]));

            PorCajero.Clear();
            for (var i = 0; i < reporte.PorCajero.Count; i++)
                PorCajero.Add(new VendedorFila(i + 1, reporte.PorCajero[i]));
        }
        catch (ArgumentException ex)
        {
            _dialogos.MostrarError("Reportes", ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error generando el reporte {Desde}–{Hasta}", desde, hasta);
            _dialogos.MostrarError("Reportes", $"No se pudo generar el reporte.\n\n{ex.Message}");
        }
        finally
        {
            Ocupado = false;
        }
    }
}
