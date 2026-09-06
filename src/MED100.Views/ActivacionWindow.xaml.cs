using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using MED100.Common;
using MED100.ViewModels;

namespace MED100.Views;

/// <summary>
/// La ventana del código. Code-behind de solo UI.
///
/// <see cref="Continuar"/> es lo que mira el arranque: false significa que el
/// usuario cerró sin activar y con el demo vencido, o sea que la app no abre.
/// Cerrar con la X cuenta como no activar — si no, el aspa sería la forma de
/// saltarse el bloqueo.
/// </summary>
public partial class ActivacionWindow : Window
{
    private readonly ActivacionViewModel _vm;

    /// <summary>True si se puede seguir usando MED-100 al cerrar esta ventana.</summary>
    public bool Continuar { get; private set; }

    public ActivacionWindow(ActivacionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.Resuelto += puedeSeguir =>
        {
            Continuar = puedeSeguir;
            Close();
        };

        Loaded += (_, _) => CajaCodigo.Focus();
    }

    private void CajaCodigo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.ActivarCommand.CanExecute(null))
            _vm.ActivarCommand.Execute(null);
    }

    private void BotonWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // UseShellExecute: sin esto .NET intenta ejecutar la URL como si
            // fuera un .exe y tira excepción.
            Process.Start(new ProcessStartInfo(Soporte.UrlWhatsApp) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo abrir WhatsApp");
            MessageBox.Show(
                "No se pudo abrir WhatsApp desde acá.\n\nEscribí al " + Soporte.Telefono + ".",
                AppInfo.Nombre, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
