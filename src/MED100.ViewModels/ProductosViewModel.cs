using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>Fila de la tabla de productos (también usada por Almacén y Caducidad).</summary>
public record ProductoFila(Producto Producto, DateOnly Hoy)
{
    public long Id => Producto.Id;
    public string CodigoTexto => string.IsNullOrWhiteSpace(Producto.Codigo) ? "—" : Producto.Codigo;
    public string Nombre => Producto.Nombre;
    public decimal Precio => Producto.Precio;
    public int Cantidad => Producto.Cantidad;
    /// <summary>Insumo, medicamento, material… Es lo que agrupa el almacén.</summary>
    public string TipoTexto => ProductoService.EtiquetaTipo(Producto.Tipo);
    public decimal ValorStock => Producto.Cantidad * Producto.Precio;
    public DateOnly? FechaCaducidad => Producto.FechaCaducidad;
    public string CaducidadTexto => Producto.FechaCaducidad?.ToString("dd/MM/yyyy") ?? "—";

    public SemaforoCaducidad? Semaforo => Producto.FechaCaducidad is { } f
        ? CalculadoraCaducidad.Calcular(f, Hoy) : null;

    public string SemaforoTexto
    {
        get
        {
            if (Producto.FechaCaducidad is not { } f) return "—";
            var meses = CalculadoraCaducidad.MesesCompletosRestantes(f, Hoy);
            if (CalculadoraCaducidad.DiasRestantes(f, Hoy) < 0) return "Caducado";
            return meses switch
            {
                0 => $"{CalculadoraCaducidad.DiasRestantes(f, Hoy)} días",
                1 => "1 mes",
                _ => $"{meses} meses"
            };
        }
    }
}

/// <summary>Criterio del filtro rápido de productos.</summary>
public enum FiltroProducto
{
    Todos,
    StockBajo,
    ConCaducidad,
    SinStock
}

/// <summary>Lista de productos con búsqueda por nombre o código + filtros.</summary>
public partial class ProductosViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly ProductoService _servicio;
    private readonly IDialogService _dialogos;
    private readonly AjustesLocales _ajustes;
    private IReadOnlyList<Producto> _todos = [];

    public event Action? NuevoSolicitado;
    public event Action<long>? EdicionSolicitada;

    public ProductosViewModel(ProductoService servicio, IDialogService dialogos, AjustesLocales ajustes)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _ajustes = ajustes;

        Filtros =
        [
            new Opcion<FiltroProducto>(FiltroProducto.Todos, "Todos"),
            new Opcion<FiltroProducto>(FiltroProducto.StockBajo, "Stock bajo"),
            new Opcion<FiltroProducto>(FiltroProducto.ConCaducidad, "Con caducidad"),
            new Opcion<FiltroProducto>(FiltroProducto.SinStock, "Sin stock")
        ];
        _filtroSeleccionado = Filtros[0];
    }

    public ObservableCollection<ProductoFila> Filas { get; } = [];
    public IReadOnlyList<Opcion<FiltroProducto>> Filtros { get; }

    [ObservableProperty] private string _textoBusqueda = string.Empty;
    [ObservableProperty] private Opcion<FiltroProducto> _filtroSeleccionado;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    partial void OnTextoBusquedaChanged(string value) => AplicarFiltro();
    partial void OnFiltroSeleccionadoChanged(Opcion<FiltroProducto> value) => AplicarFiltro();

    public async Task RefrescarAsync()
    {
        try
        {
            _todos = await _servicio.ObtenerTodosAsync();
            AplicarFiltro();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando la lista de productos");
            _dialogos.MostrarError("Productos", $"No se pudo cargar la lista de productos.\n\n{ex.Message}");
        }
    }

    private void AplicarFiltro()
    {
        var filtro = TextoBusqueda.Trim();
        var umbralStock = _ajustes.AvisoStockBajoUmbral;
        var visibles = _todos
            .Where(p => FiltroSeleccionado.Valor switch
            {
                FiltroProducto.StockBajo => p.Cantidad <= umbralStock,
                FiltroProducto.ConCaducidad => p.FechaCaducidad is not null,
                FiltroProducto.SinStock => p.Cantidad == 0,
                _ => true
            })
            .Where(p => string.IsNullOrEmpty(filtro) ||
                p.Nombre.Contains(filtro, StringComparison.OrdinalIgnoreCase) ||
                (p.Codigo?.Contains(filtro, StringComparison.OrdinalIgnoreCase) ?? false));

        var hoy = FechaNegocio.Hoy;
        Filas.Clear();
        foreach (var producto in visibles)
            Filas.Add(new ProductoFila(producto, hoy));

        ContadorTexto = _todos.Count == 0
            ? "Sin productos registrados"
            : $"Mostrando {Filas.Count} de {_todos.Count} productos";
    }

    [RelayCommand]
    private void Nuevo() => NuevoSolicitado?.Invoke();

    [RelayCommand]
    private void Editar(ProductoFila? fila)
    {
        if (fila is not null)
            EdicionSolicitada?.Invoke(fila.Id);
    }

    [RelayCommand]
    private async Task EliminarAsync(ProductoFila? fila)
    {
        if (fila is null)
            return;
        if (!_dialogos.Confirmar("Eliminar producto",
                $"¿Eliminar {fila.Nombre}?\n\nLas ventas pasadas que lo incluyen se conservan."))
            return;

        try
        {
            await _servicio.EliminarAsync(fila.Id);
            await RefrescarAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error eliminando producto {Id}", fila.Id);
            _dialogos.MostrarError("Eliminar producto", ex.Message);
        }
    }
}
