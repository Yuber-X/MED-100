using System.Windows;
using System.Windows.Media;
using MED100.Services;
using MED100.ViewModels;

namespace MED100.App;

public partial class MainWindow : Window
{
    private readonly AuthService _auth;

    /// <summary>
    /// Cambio rápido de usuario (pedido Yuber 2026-07-12): el turno cambia y el
    /// nuevo cajero entra sin cerrar la aplicación. La App lo maneja: cierra la
    /// sesión actual y pide credenciales de nuevo.
    /// </summary>
    public event Action? CambiarUsuarioSolicitado;

    public MainWindow(MainViewModel vm, AuthService auth)
    {
        InitializeComponent();
        DataContext = vm;
        _auth = auth;
    }

    /// <summary>Tamaño de texto Pequeño/Mediano/Grande (Configuración → Apariencia).</summary>
    public void AplicarEscala(double factor) =>
        Raiz.LayoutTransform = factor == 1.0 ? null : new ScaleTransform(factor, factor);

    private void BotonCambiarUsuario_Click(object sender, RoutedEventArgs e) =>
        CambiarUsuarioSolicitado?.Invoke();

    private async void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        await _auth.LogoutAsync();
        Application.Current.Shutdown();
    }
}
