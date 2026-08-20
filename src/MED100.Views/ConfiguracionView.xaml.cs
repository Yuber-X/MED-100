using System.Windows.Navigation;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MED100.Common;
using MED100.ViewModels;

namespace MED100.Views;

/// <summary>
/// Code-behind de SOLO UI: los diálogos de archivo/carpeta (elegir dónde
/// guardar el respaldo o el Excel). La lógica vive en el ViewModel.
/// </summary>
public partial class ConfiguracionView : UserControl
{
    public ConfiguracionView() => InitializeComponent();

    private ConfiguracionViewModel? Vm => DataContext as ConfiguracionViewModel;

    private async void BotonRespaldar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialogo = new SaveFileDialog
        {
            Title = "Guardar respaldo de la base de datos",
            Filter = "Respaldo SQL (*.sql)|*.sql",
            FileName = $"MED100_Respaldo_{FechaNegocio.Hoy:yyyy-MM-dd}.sql"
        };
        if (dialogo.ShowDialog() == true)
            await Vm.RespaldarAsync(dialogo.FileName);
    }

    private async void BotonRestaurar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elegir el archivo de respaldo a restaurar",
            Filter = "Respaldo SQL (*.sql)|*.sql"
        };
        if (dialogo.ShowDialog() == true)
            await Vm.RestaurarAsync(dialogo.FileName);
    }

    private async void BotonExportar_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialogo = new SaveFileDialog
        {
            Title = "Exportar todos los datos a Excel",
            Filter = "Libro de Excel (*.xlsx)|*.xlsx",
            FileName = $"MED100_Export_{FechaNegocio.Hoy:yyyy-MM-dd}.xlsx"
        };
        if (dialogo.ShowDialog() == true)
            await Vm.ExportarAhoraAsync(dialogo.FileName);
    }

    private void BotonCarpeta_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialogo = new OpenFolderDialog
        {
            Title = "Carpeta donde guardar las exportaciones automáticas"
        };
        if (dialogo.ShowDialog() == true)
            Vm.EstablecerCarpetaExport(dialogo.FolderName);
    }

    /// <summary>
    /// Adónde van los archivos del expediente. Los que ya están NO se mueven:
    /// moverlos sería una operación larga y riesgosa que nadie pidió; lo que
    /// cambia es dónde se guardan los nuevos.
    /// </summary>
    private void BotonCarpetaExpedientes_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;

        var dialogo = new OpenFolderDialog
        {
            Title = "Carpeta donde guardar los expedientes de los pacientes"
        };
        if (dialogo.ShowDialog() == true)
            Vm.EstablecerCarpetaExpedientes(dialogo.FolderName);
    }

    /// <summary>
    /// La PasswordBox no se puede bindear (WPF no expone Password como
    /// DependencyProperty, a propósito): la View se la pasa al ViewModel.
    /// </summary>
    private void CampoGmailPassword_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ConfiguracionViewModel vm)
            vm.EstablecerAppPassword(CampoGmailPassword.Password);
    }

    /// <summary>Abre el link de las contraseñas de aplicación en el navegador.</summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se pudo abrir {Uri}", e.Uri);
        }
    }
}
