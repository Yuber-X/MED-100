using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MED100.Common;
using MED100.Models;
using MED100.Services;
using Serilog;

namespace MED100.ViewModels;

/// <summary>
/// Caducidad: productos con fecha, los más próximos primero, con semáforo
/// (regla mensual del POS-400 mapeada a 4 colores). Solo lectura.
/// </summary>
public partial class CaducidadViewModel : ObservableObject, IPaginaAsincrona
{
    private readonly ProductoService _servicio;
    private readonly IDialogService _dialogos;

    public CaducidadViewModel(ProductoService servicio, IDialogService dialogos)
    {
        _servicio = servicio;
        _dialogos = dialogos;
    }

    public ObservableCollection<ProductoFila> Filas { get; } = [];

    [ObservableProperty] private int _totalRojos;
    [ObservableProperty] private int _totalNaranjas;
    [ObservableProperty] private string _contadorTexto = string.Empty;

    public async Task RefrescarAsync()
    {
        try
        {
            var productos = await _servicio.ObtenerConCaducidadAsync();
            var hoy = FechaNegocio.Hoy;

            Filas.Clear();
            foreach (var producto in productos)
                Filas.Add(new ProductoFila(producto, hoy));

            TotalRojos = Filas.Count(f => f.Semaforo == SemaforoCaducidad.Rojo);
            TotalNaranjas = Filas.Count(f => f.Semaforo == SemaforoCaducidad.Naranja);
            ContadorTexto = Filas.Count == 0
                ? "Ningún producto tiene fecha de caducidad registrada"
                : $"{Filas.Count} productos con fecha de caducidad";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cargando caducidad");
            _dialogos.MostrarError("Caducidad", $"No se pudo cargar la información.\n\n{ex.Message}");
        }
    }
}
