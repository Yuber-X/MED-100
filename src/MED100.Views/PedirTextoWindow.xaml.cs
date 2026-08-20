using System.Windows;
using System.Windows.Input;

namespace MED100.Views;

/// <summary>
/// Entrada de texto simple (ej: motivo de anulación). WPF no trae un input box;
/// esta ventana lo resuelve con el estilo del sistema. Code-behind de solo UI.
/// </summary>
public partial class PedirTextoWindow : Window
{
    public string? Resultado { get; private set; }

    public PedirTextoWindow(string titulo, string mensaje, string textoInicial)
    {
        InitializeComponent();
        Title = titulo;
        Mensaje.Text = mensaje;
        CajaTexto.Text = textoInicial;
        Loaded += (_, _) =>
        {
            CajaTexto.Focus();
            CajaTexto.SelectAll();
        };
    }

    private void BotonAceptar_Click(object sender, RoutedEventArgs e) => Aceptar();

    private void CajaTexto_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Aceptar();
    }

    private void Aceptar()
    {
        if (string.IsNullOrWhiteSpace(CajaTexto.Text))
        {
            Aviso.Visibility = Visibility.Visible;
            return;
        }
        Resultado = CajaTexto.Text.Trim();
        DialogResult = true;
    }

    private void BotonCancelar_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
