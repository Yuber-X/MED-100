using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MED100.Common.Converters;

namespace MED100.Verificacion;

/// <summary>
/// Verifica que NINGUNA pantalla se dibuje fuera de su contenedor.
///
/// POR QUÉ EXISTE
/// --------------
/// La clínica reportó el 2026-09-25: <i>"en cita el mes no se ve bien"</i>. El
/// campo de la fecha salía partido por la mitad con la ventana sin maximizar.
/// La causa no era la fecha: las barras de botones se armaban con columnas
/// <c>Auto</c> de un Grid, que se dibujan a su tamaño AUNQUE NO QUEPAN. Lo que
/// sobra no encoge ni avisa: se sale del panel y lo tapa la tarjeta de al lado.
/// Compila perfecto, se ve perfecto en el monitor del programador, y en la
/// clínica falta medio control.
///
/// CÓMO LO DETECTA
/// ---------------
/// Arma cada UserControl a los anchos reales del shell y recorre el árbol
/// visual comparando el borde derecho de cada elemento con el de su contenedor.
/// Lo que quede afuera, lo nombra.
///
/// Los anchos salen del shell: ventana − sidebar (240) − márgenes (64),
/// divididos por la escala del tamaño de texto (Configuración → Apariencia),
/// porque el shell escala TODO con un LayoutTransform y con el texto en Grande
/// queda un 20% menos de ancho útil.
///
/// USO
/// ---
///     dotnet run --project scripts/verificar_desborde
///
/// Devuelve 0 si está todo bien, 1 si alguna pantalla se desborda.
///
/// LO QUE NO CUBRE
/// ---------------
/// Mide sin datos: no ve lo que crece con el contenido (un nombre largo en una
/// celda, una lista con muchos ítems). Para eso hay que abrir la pantalla.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Ancho de contenido mínimo que el shell garantiza: 1024 de ventana
    /// (MainWindow.MinWidth) − 240 del sidebar − 64 de márgenes. Si una
    /// pantalla no entra acá, en la clínica se ve cortada.
    /// </summary>
    private const double AnchoMinimoDeContenido = 720;

    [STAThread]
    private static int Main()
    {
        var app = new Application();
        foreach (var tema in new[] { "Colores", "Tipografia", "Controles" })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/MED100.Views;component/Themes/{tema}.xaml")
            });
        }

        // Los converters viven en App.xaml, que no se puede cargar sin arrancar
        // la aplicación entera. Se registran acá con las MISMAS claves.
        app.Resources["Money"] = new MoneyConverter();
        app.Resources["Fecha"] = new DateConverter();
        app.Resources["InversorBool"] = new InverseBooleanConverter();
        app.Resources["InversorBoolVisibilidad"] = new BooleanToVisibilityInversoConverter();
        app.Resources["Igualdad"] = new IgualdadConverter();
        app.Resources["BoolAVisibilidad"] = new BooleanToVisibilityConverter();

        var escenarios = new (string Nombre, double Ancho, double Alto)[]
        {
            ("mínimo garantizado", AnchoMinimoDeContenido, 600),
            ("1366 maximizada, texto normal", 1366 - 240 - 64, 624),
            ("1366 maximizada, texto grande", (1366 - 240 - 64) / 1.25, 499),
            ("1920 maximizada, texto grande", (1920 - 240 - 64) / 1.25, 749)
        };

        var tipos = typeof(MED100.Views.CitasView).Assembly
            .GetExportedTypes()
            .Where(t => t.IsSubclassOf(typeof(UserControl)) && !t.IsAbstract)
            .OrderBy(t => t.Name)
            .ToList();

        var conProblema = new HashSet<string>();

        foreach (var (nombre, ancho, alto) in escenarios)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {nombre}  ({ancho:F0} x {alto:F0}) ===");

            foreach (var tipo in tipos)
            {
                UserControl vista;
                try
                {
                    vista = (UserControl)Activator.CreateInstance(tipo)!;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {tipo.Name}: no se pudo crear ({ex.GetBaseException().Message})");
                    continue;
                }

                vista.Measure(new Size(ancho, alto));
                vista.Arrange(new Rect(0, 0, ancho, alto));
                vista.UpdateLayout();

                var desbordes = new List<string>();
                Recorrer(vista, vista, ancho, false, desbordes);
                if (desbordes.Count == 0)
                    continue;

                conProblema.Add(tipo.Name);
                Console.WriteLine($"  {tipo.Name}:");
                foreach (var caso in desbordes.Take(5))
                    Console.WriteLine($"      {caso}");
                if (desbordes.Count > 5)
                    Console.WriteLine($"      ... y {desbordes.Count - 5} mas");
            }
        }

        Console.WriteLine();
        if (conProblema.Count == 0)
        {
            Console.WriteLine($"OK - las {tipos.Count} pantallas entran en su contenedor.");
            return 0;
        }

        Console.WriteLine($"PANTALLAS CON DESBORDE: {string.Join(", ", conProblema.OrderBy(n => n))}");
        Console.WriteLine("Arreglo habitual: WrapPanel en vez de StackPanel/Grid para las barras de");
        Console.WriteLine("botones, y MaxWidth en vez de Width en los buscadores.");
        return 1;
    }

    private static void Recorrer(FrameworkElement raiz, DependencyObject nodo,
        double bordeDerechoPadre, bool dentroDeScrollHorizontal, List<string> desbordes)
    {
        var hijos = VisualTreeHelper.GetChildrenCount(nodo);
        for (var i = 0; i < hijos; i++)
        {
            var hijo = VisualTreeHelper.GetChild(nodo, i);
            var limite = bordeDerechoPadre;
            var enScroll = dentroDeScrollHorizontal;

            if (hijo is FrameworkElement fe && fe.Visibility == Visibility.Visible && fe.ActualWidth > 0)
            {
                // Lo que se pasa dentro de algo que scrollea a lo ancho no se
                // pierde: se alcanza con la barra. Un ScrollViewer vertical —lo
                // normal en este proyecto— NO cuenta: ahí el ancho de más se
                // recorta igual.
                var scrollea = fe is DataGrid
                    || (fe is ScrollViewer sv
                        && sv.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled);

                var izquierda = fe.TransformToAncestor(raiz).Transform(new Point(0, 0)).X;
                var derecha = izquierda + fe.ActualWidth;

                if (!enScroll && !scrollea && derecha > bordeDerechoPadre + 0.5)
                {
                    desbordes.Add(
                        $"{Describir(fe)} llega a {derecha:F0} y su contenedor termina en " +
                        $"{bordeDerechoPadre:F0} (se sale {derecha - bordeDerechoPadre:F0})");
                    continue;   // lo de adentro se explica por esto
                }

                enScroll = enScroll || scrollea;
                limite = Math.Min(bordeDerechoPadre, derecha);
            }

            Recorrer(raiz, hijo, limite, enScroll, desbordes);
        }
    }

    private static string Describir(FrameworkElement fe)
    {
        var nombre = string.IsNullOrEmpty(fe.Name) ? "" : $" <{fe.Name}>";
        var texto = fe switch
        {
            TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text) => $" \"{Recortar(tb.Text)}\"",
            ContentControl cc when cc.Content is string s && s.Length > 0 => $" \"{Recortar(s)}\"",
            _ => ""
        };
        return $"{fe.GetType().Name}{nombre}{texto}";
    }

    private static string Recortar(string texto) =>
        texto.Length <= 40 ? texto : texto[..40] + "...";
}
