using System.Windows;
using MED100.ViewModels;

namespace MED100.Views;

/// <summary>
/// Los textos de consentimiento informado: se escriben acá y desde acá se
/// imprimen para que el paciente los firme (pedido de la clínica 2026-09-21).
///
/// El ViewModel llega armado desde App —ahí vive el contenedor de servicios—,
/// igual que el resto de las ventanas del proyecto. Code-behind de solo UI.
/// </summary>
public partial class ConsentimientosWindow : Window
{
    public ConsentimientosWindow(ConsentimientosViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void BotonCerrar_Click(object sender, RoutedEventArgs e) => Close();
}
