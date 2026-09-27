using System.Windows;
using System.Windows.Documents;
using MED100.Printing;

namespace MED100.Views;

/// <summary>
/// Vista previa de un documento que pagina (consentimiento informado).
///
/// Recibe una FÁBRICA del documento y no el documento: se arma uno para
/// mostrar y otro para imprimir, porque un FlowDocument no puede estar colgado
/// del visor y del paginador a la vez. Code-behind de solo UI.
/// </summary>
public partial class VistaPreviaDocumentoWindow : Window
{
    private readonly Func<FlowDocument> _fabrica;
    private readonly string _descripcion;

    public VistaPreviaDocumentoWindow(Func<FlowDocument> fabrica, string titulo, string descripcion)
    {
        InitializeComponent();
        Title = titulo;
        _fabrica = fabrica;
        _descripcion = descripcion;
        Visor.Document = fabrica();
    }

    private void BotonImprimir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ImpresoraDocumentos.Imprimir(_fabrica, _descripcion))
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
