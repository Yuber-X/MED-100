using System.Diagnostics;
using System.Windows;
using MED100.ViewModels;
using Microsoft.Win32;

namespace MED100.Views;

/// <summary>
/// Qué hacer con un documento del expediente: abrirlo, guardar una copia,
/// reclasificarlo, re-ubicarlo o eliminarlo.
///
/// Re-ubicar y eliminar SOLO aparecen para un Admin. La regla real vive en
/// ExpedienteService: acá se ocultan para no ofrecer lo que va a ser rechazado.
/// </summary>
public partial class DocumentoAccionesWindow : Window
{
    private readonly ExpedientePacienteViewModel _vm;
    private readonly DocumentoFila _fila;

    public DocumentoAccionesWindow(ExpedientePacienteViewModel vm, DocumentoFila fila)
    {
        InitializeComponent();
        _vm = vm;
        _fila = fila;

        IconoArchivo.Text = fila.Icono;
        TextoNombre.Text = fila.Nombre;
        TextoDetalle.Text = $"{fila.Familia} · {fila.TamanoTexto} · subido el {fila.FechaTexto} " +
                            $"por {fila.SubidoPorTexto}";

        ComboTipos.ItemsSource = vm.TiposDocumento;
        ComboTipos.SelectedItem = vm.TiposDocumento.FirstOrDefault(
            t => t.Valor == fila.Documento.Tipo);

        PanelAdmin.Visibility = vm.PuedeAdministrar ? Visibility.Visible : Visibility.Collapsed;
        if (vm.PuedeAdministrar)
        {
            ComboDestinos.ItemsSource = vm.Destinos;
            _ = vm.CargarDestinosAsync();
        }
    }

    /// <summary>
    /// Abre el archivo con la app que Windows tenga asociada: un .docx abre
    /// Word, una imagen el visor de fotos. UseShellExecute es lo que hace esa
    /// resolución; la extensión ya pasó por la lista blanca del servicio, así
    /// que no se puede disparar un ejecutable disfrazado.
    /// </summary>
    private async void Abrir_Click(object sender, RoutedEventArgs e)
    {
        var ruta = await _vm.RutaParaAbrirAsync(_fila);
        if (ruta is null)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Windows no pudo abrir el archivo.\n\n{ex.Message}",
                "Abrir documento", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Guardar_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new SaveFileDialog
        {
            Title = "¿Dónde guardo la copia?",
            FileName = _fila.Nombre,
            Filter = "Todos los archivos|*.*"
        };
        if (dialogo.ShowDialog(this) != true)
            return;

        await _vm.GuardarCopiaAsync(_fila, dialogo.FileName);
        Close();
    }

    private async void CambiarTipo_Click(object sender, RoutedEventArgs e)
    {
        if (ComboTipos.SelectedItem is not OpcionTipoDocumento tipo)
            return;
        if (tipo.Valor == _fila.Documento.Tipo)
        {
            Close();
            return;
        }

        if (await _vm.CambiarTipoAsync(_fila, tipo))
            Close();
    }

    private async void Reubicar_Click(object sender, RoutedEventArgs e)
    {
        if (ComboDestinos.SelectedItem is not DestinoDocumento destino)
        {
            MessageBox.Show(this, "Elegí a qué paciente hay que mover el documento.",
                "Re-ubicar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (await _vm.ReubicarAsync(_fila, destino))
            Close();
    }

    private void Eliminar_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.EliminarCommand.CanExecute(_fila))
            _vm.EliminarCommand.Execute(_fila);
        Close();
    }

    private void Cerrar_Click(object sender, RoutedEventArgs e) => Close();
}
