using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila del ranking de cajeros del mes.</summary>
public record VendedorFila(int Puesto, VendedorRanking Ranking)
{
    public string Nombre => Ranking.Nombre;
    public string FacturasTexto => $"{Ranking.Facturas} factura(s)";
    public decimal Total => Ranking.Total;
}

/// <summary>Fila del ranking de productos del mes.</summary>
public record ProductoTopFila(int Puesto, ProductoRanking Ranking)
{
    public string Nombre => Ranking.Nombre;
    public string UnidadesTexto => $"{Ranking.Unidades} unidad(es)";
    public decimal Total => Ranking.Total;
}

/// <summary>
/// Panel de control: KPIs del día y del mes, tendencia diaria y rankings.
/// Requiere permiso 'panel' (el Cajero no lo tiene: su pantalla inicial es Vender).
/// </summary>
public partial class PanelViewModel : ObservableObject, IPaginaAsincrona
{
    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    private readonly AnaliticaService _analitica;
    private readonly IDialogService _dialogos;

    public PanelViewModel(AnaliticaService analitica, IDialogService dialogos)
    {
        _analitica = analitica;
        _dialogos = dialogos;
    }

    // KPIs
    [ObservableProperty] private decimal _ventasHoy;
    [ObservableProperty] private string _facturasHoyTexto = string.Empty;
    [ObservableProperty] private decimal _ventasMes;
    [ObservableProperty] private string _variacionTexto = string.Empty;
    [ObservableProperty] private bool _variacionPositiva = true;
    [ObservableProperty] private decimal _ticketPromedio;
    [ObservableProperty] private int _productosPorCaducar;
    [ObservableProperty] private int _productosStockBajo;
    [ObservableProperty] private string _alertasInventarioTexto = string.Empty;
    [ObservableProperty] private string _tituloGrafico = string.Empty;

    // Gráfico de tendencia
    [ObservableProperty] private ISeries[] _series = [];
    [ObservableProperty] private Axis[] _xAxes = [];
    [ObservableProperty] private Axis[] _yAxes = [];

    public ObservableCollection<VendedorFila> TopVendedores { get; } = [];
    public ObservableCollection<ProductoTopFila> TopProductos { get; } = [];

    [ObservableProperty] private bool _sinVentas;

    public async Task RefrescarAsync()
    {
        try
        {
            var hoy = FechaNegocio.Hoy;
            var datos = await _analitica.ObtenerDashboardAsync();

            VentasHoy = datos.VentasHoy;
            FacturasHoyTexto = datos.FacturasHoy == 1
                ? "1 factura emitida hoy"
                : $"{datos.FacturasHoy} facturas emitidas hoy";
            VentasMes = datos.VentasMes;
            (VariacionTexto, VariacionPositiva) =
                AnaliticaService.CalcularVariacion(datos.VentasMes, datos.VentasMesAnterior);
            TicketPromedio = datos.TicketPromedioMes;

            ProductosPorCaducar = datos.ProductosPorCaducar;
            ProductosStockBajo = datos.ProductosStockBajo;
            AlertasInventarioTexto = datos.ProductosPorCaducar == 0 && datos.ProductosStockBajo == 0
                ? "Inventario sin alertas"
                : $"{datos.ProductosPorCaducar} por caducar · {datos.ProductosStockBajo} con stock bajo";

            TituloGrafico = $"Ventas diarias de {hoy.ToString("MMMM yyyy", CulturaRd)}";
            var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
            (Series, XAxes, YAxes) = GraficoVentas.Construir(datos.VentasPorDia, inicioMes, hoy);

            TopVendedores.Clear();
            for (var i = 0; i < datos.TopVendedores.Count; i++)
                TopVendedores.Add(new VendedorFila(i + 1, datos.TopVendedores[i]));

            TopProductos.Clear();
            for (var i = 0; i < datos.TopProductos.Count; i++)
                TopProductos.Add(new ProductoTopFila(i + 1, datos.TopProductos[i]));

            SinVentas = datos.VentasMes == 0m;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el panel");
            _dialogos.MostrarError("Panel", $"No se pudo cargar el panel.\n\n{ex.Message}");
        }
    }
}
