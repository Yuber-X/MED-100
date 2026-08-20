using System.Windows;
using MED100.Printing;

namespace MED100.Views;

/// <summary>
/// Vista previa genérica de un documento imprimible (cierre de caja).
/// A diferencia del ticket de venta, el cierre SIEMPRE se previsualiza
/// antes de imprimir (pedido Yuber 2026-07-12). Code-behind de solo UI.
/// </summary>
public partial class VistaPreviaWindow : Window
{
    private readonly FrameworkElement _visual;
    private readonly string _descripcion;

    public VistaPreviaWindow(FrameworkElement visual, string titulo, string descripcion)
    {
        InitializeComponent();
        Title = titulo;
        _visual = visual;
        _descripcion = descripcion;
        Contenedor.Content = visual;
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Diálogo del sistema: aquí el usuario elige impresora y papel
            if (ImpresoraTickets.Imprimir(_visual, _descripcion))
                Close();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error imprimiendo {Descripcion}", _descripcion);
            MessageBox.Show(this,
                "No se pudo imprimir.\n\n" + ex.Message,
                "Impresión", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
