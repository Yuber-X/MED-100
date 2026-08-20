using System.Windows;
using MED100.Printing;

namespace MED100.Views;

/// <summary>
/// Vista previa del ticket tras cobrar. La venta YA está persistida: si la
/// impresión falla, se avisa y se puede reintentar sin tocar la factura
/// (spec §9.6). Code-behind de solo UI.
/// </summary>
public partial class TicketWindow : Window
{
    private readonly FrameworkElement _visual;
    private readonly string _descripcion;
    private readonly int _copias;

    public TicketWindow(FrameworkElement visualTicket, string descripcion, int copias)
    {
        InitializeComponent();
        _visual = visualTicket;
        _descripcion = descripcion;
        _copias = copias;
        ContenedorTicket.Content = visualTicket;
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ImpresoraTickets.Imprimir(_visual, _descripcion, _copias);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error imprimiendo el ticket {Descripcion}", _descripcion);
            MessageBox.Show(this,
                "No se pudo imprimir el ticket. La venta YA quedó registrada: " +
                "revisa la impresora y vuelve a intentar.\n\n" + ex.Message,
                "Impresión", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
