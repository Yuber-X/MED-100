using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MED100.ViewModels;
using Microsoft.Win32;

namespace MED100.Views;

/// <summary>
/// Expediente del paciente: los papeles que entregó, con sus dos modos de ver.
///
/// Code-behind SOLO de UI: abrir los diálogos de archivo de Windows y pasarle
/// las rutas al ViewModel, que es quien decide qué se puede guardar. La lista
/// blanca de extensiones y los permisos viven en ExpedienteService.
/// </summary>
public partial class ExpedientePacienteView : UserControl
{
    public ExpedientePacienteView() => InitializeComponent();

    private ExpedientePacienteViewModel? Vm => DataContext as ExpedientePacienteViewModel;

    private async void SubirDocumentos_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var dialogo = new OpenFileDialog
        {
            Title = "Elegí los documentos del paciente",
            Multiselect = true,     // la cédula suele venir en dos fotos
            Filter = ExpedientePacienteViewModel.FiltroArchivos
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await vm.AgregarArchivosAsync(dialogo.FileNames);
    }

    private async void ExportarExpediente_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm)
            return;

        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo el ZIP del expediente?",
            FileName = $"Expediente {vm.Nombre}.zip",
            Filter = "Archivo comprimido (*.zip)|*.zip"
        };
        if (dialogo.ShowDialog(Window.GetWindow(this)) != true)
            return;

        await vm.ExportarZipAsync(dialogo.FileName);
    }

    private void Documento_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is DocumentoFila fila)
            AbrirAcciones(fila);
    }

    private void AccionesDocumento_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is DocumentoFila fila)
            AbrirAcciones(fila);
    }

    private void AbrirAcciones(DocumentoFila fila)
    {
        if (Vm is not { } vm)
            return;

        var ventana = new DocumentoAccionesWindow(vm, fila) { Owner = Window.GetWindow(this) };
        ventana.ShowDialog();
    }
}
