using System.Windows;
using MED100.Common;

namespace MED100.App;

/// <summary>
/// Implementación WPF de IDialogService. Los ViewModels dependen solo de la
/// interfaz (testeable); el MessageBox vive únicamente aquí, en la capa de UI.
/// </summary>
public class DialogService : IDialogService
{
    public bool Confirmar(string titulo, string mensaje) =>
        MessageBox.Show(Propietaria(), mensaje, titulo,
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Informar(string titulo, string mensaje) =>
        MessageBox.Show(Propietaria(), mensaje, titulo,
            MessageBoxButton.OK, MessageBoxImage.Information);

    public void MostrarError(string titulo, string mensaje) =>
        MessageBox.Show(Propietaria(), mensaje, titulo,
            MessageBoxButton.OK, MessageBoxImage.Error);

    public string? PedirTexto(string titulo, string mensaje, string textoInicial = "")
    {
        var ventana = new MED100.Views.PedirTextoWindow(titulo, mensaje, textoInicial)
        {
            Owner = Propietaria()
        };
        return ventana.ShowDialog() == true ? ventana.Resultado : null;
    }

    private static Window Propietaria() => Application.Current.MainWindow;
}
