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

    /// <summary>
    /// Ancho y alto mínimos de la ventana con el texto en tamaño normal.
    ///
    /// Debajo de esto las pantallas dejan de entrar: los grupos de botones se
    /// salen de su panel y los tapa la tarjeta de al lado. Así llegó el reporte
    /// de la clínica del 2026-09-25 ("en Citas el mes no se ve bien": el campo
    /// de la fecha aparecía cortado por la mitad, con la ventana sin
    /// maximizar). 1024 = 240 del sidebar + 64 de márgenes + 720 de contenido,
    /// que es el ancho con el que se verificó que ninguna pantalla se desborda.
    /// </summary>
    private const double AnchoMinimoBase = 1024;
    private const double AltoMinimoBase = 700;

    /// <summary>Tamaño de texto Pequeño/Mediano/Grande (Configuración → Apariencia).</summary>
    public void AplicarEscala(double factor)
    {
        Raiz.LayoutTransform = factor == 1.0 ? null : new ScaleTransform(factor, factor);

        // El texto grande agranda TODO con un LayoutTransform: la misma ventana
        // queda con un 20% menos de ancho útil, así que el mínimo acompaña. Se
        // topea al área de trabajo para no exigir una ventana más grande que la
        // pantalla —en un monitor de 1366×768 con texto grande, el alto pedido
        // se pasaría—.
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(AnchoMinimoBase * factor, area.Width);
        MinHeight = Math.Min(AltoMinimoBase * factor, area.Height);
    }

    private void BotonCambiarUsuario_Click(object sender, RoutedEventArgs e) =>
        CambiarUsuarioSolicitado?.Invoke();

    private async void BotonSalir_Click(object sender, RoutedEventArgs e)
    {
        await _auth.LogoutAsync();
        Application.Current.Shutdown();
    }
}
