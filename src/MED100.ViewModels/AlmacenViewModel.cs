using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Almacén: vista de solo lectura del stock con totales (spec §7 módulo 6).
/// Los totales vienen calculados de SQL, no se suman en la UI.
/// </summary>
public partial class AlmacenViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly ProductoService _servicio;
    private readonly IDialogService _dialogos;
    private readonly AjustesLocales _ajustes;
    private IReadOnlyList<Producto> _todos = [];

    public AlmacenViewModel(ProductoService servicio, IDialogService dialogos, AjustesLocales ajustes)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _ajustes = ajustes;
    }

    public ObservableCollection<ProductoFila> Filas { get; } = [];

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private int _totalProductos;
    [ObservableProperty] private long _totalUnidades;
    [ObservableProperty] private decimal _valorInventario;
    [ObservableProperty] private int _productosStockBajo;

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();

    public async Task RefrescarAsync()
    {
        try
        {
            _todos = await _servicio.ObtenerTodosAsync();
            var totales = await _servicio.ObtenerTotalesAsync();
            TotalProductos = totales.TotalProductos;
            TotalUnidades = totales.TotalUnidades;
            ValorInventario = totales.ValorInventario;
            ProductosStockBajo = _todos.Count(p => p.Cantidad <= _ajustes.AvisoStockBajoUmbral);
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando el almacén");
            _dialogos.MostrarError("Almacén", $"No se pudo cargar el almacén.\n\n{ex.Message}");
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        var hoy = FechaNegocio.Hoy;
        Filas.Clear();
        foreach (var producto in _todos.Where(p => string.IsNullOrEmpty(filtro) ||
                     p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                     (p.Codigo?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false)))
            Filas.Add(new ProductoFila(producto, hoy));
    }
}
