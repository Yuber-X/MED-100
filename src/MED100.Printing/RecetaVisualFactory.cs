using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MED100.Models;

namespace MED100.Printing;

/// <summary>
/// La receta impresa en hoja CARTA (pedido de la clínica 2026-09-21: <i>"receta
/// timbrada con logo, firma del doctor, exequátur, que se pueda imprimir"</i>;
/// Yuber confirmó el tamaño el 2026-09-24: carta con logo y la firma en blanco).
///
/// POR QUÉ NO ES UN TICKET: todo lo demás que imprime MED-100 sale de la
/// térmica de 80mm, pero una receta se entrega al paciente y la lee un
/// farmacéutico. Una tira de papel térmico de 8cm —que además se borra con el
/// calor en unos meses— no sirve para eso.
///
/// QUÉ LA HACE "TIMBRADA": el membrete de la clínica con su logo arriba, y
/// abajo el nombre del médico con su exequátur —el número que lo habilita a
/// ejercer en RD— sobre una línea EN BLANCO para que firme a mano. La firma no
/// se digitaliza a propósito: guardar la firma del médico en la base para
/// estamparla sola convierte cualquier PC de la recepción en una fábrica de
/// recetas firmadas.
///
/// LÍMITE CONOCIDO: la hoja mide una página (1056 DIU) y la firma queda pegada
/// al pie. Con más de unos diez medicamentos el visual crece y la impresora
/// parte la hoja en dos; una receta normal trae dos o tres.
/// </summary>
public static class RecetaVisualFactory
{
    private const double AnchoCarta = 816;    // 8.5in @96dpi
    private const double AltoCarta = 1056;    // 11in @96dpi
    private const double Margen = 64;         // ~1.7cm, dentro del área imprimible

    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly SolidColorBrush Tinta = Brushes.Black;
    private static readonly SolidColorBrush Suave = Congelar(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush Regla = Congelar(Color.FromRgb(0x33, 0x33, 0x33));

    public static FrameworkElement Crear(RecetaImpresa receta, ConfiguracionNegocio negocio)
    {
        ArgumentNullException.ThrowIfNull(receta);

        // Grid y no StackPanel: la fila del medio se estira y empuja la firma al
        // pie de la hoja, con dos medicamentos o con ocho. En una receta la
        // firma va abajo, no flotando a media página.
        var pagina = new Grid { Width = AnchoCarta, MinHeight = AltoCarta, Background = Brushes.White };
        pagina.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pagina.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pagina.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Agregar(pagina, Membrete(receta, negocio), 0);
        Agregar(pagina, Cuerpo(receta), 1);
        Agregar(pagina, Firma(receta), 2);

        pagina.Measure(new Size(AnchoCarta, double.PositiveInfinity));
        pagina.Arrange(new Rect(0, 0, AnchoCarta, Math.Max(AltoCarta, pagina.DesiredSize.Height)));
        pagina.UpdateLayout();
        return pagina;
    }

    // ---------------- Membrete ----------------

    private static FrameworkElement Membrete(RecetaImpresa receta, ConfiguracionNegocio negocio)
    {
        var raiz = new StackPanel { Margin = new Thickness(Margen, 44, Margen, 0) };

        var fila = new Grid();
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (CargarLogo(negocio.LogoRuta) is { } logo)
        {
            var imagen = new Image
            {
                Source = logo,
                Width = 92,
                MaxHeight = 92,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 20, 0)
            };
            Grid.SetColumn(imagen, 0);
            fila.Children.Add(imagen);
        }

        var datos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        datos.Children.Add(Linea(negocio.NombreNegocio, 21, FontWeights.Bold));

        if (!string.IsNullOrWhiteSpace(negocio.Direccion))
            datos.Children.Add(Linea(negocio.Direccion!, 11, FontWeights.Normal, Suave));

        var contacto = Unir("  ·  ",
            negocio.Telefono,
            negocio.Email,
            string.IsNullOrWhiteSpace(negocio.Rnc) ? null : $"RNC {negocio.Rnc}");
        if (contacto is not null)
            datos.Children.Add(Linea(contacto, 11, FontWeights.Normal, Suave));

        Grid.SetColumn(datos, 1);
        fila.Children.Add(datos);
        raiz.Children.Add(fila);

        raiz.Children.Add(Raya(2, margenSuperior: 16));
        raiz.Children.Add(Linea("RECETA MÉDICA", 15, FontWeights.SemiBold,
            alineacion: TextAlignment.Center, margenSuperior: 14));

        // Paciente y fecha en dos columnas: es lo primero que busca el que lee
        // la receta en la farmacia.
        var identificacion = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        identificacion.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        identificacion.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var izquierda = new StackPanel();
        izquierda.Children.Add(Dato("Paciente", receta.PacienteNombre, 13));
        var segunda = Unir("      ",
            string.IsNullOrWhiteSpace(receta.PacienteCedula) ? null : $"Cédula: {receta.PacienteCedula}",
            receta.PacienteEdad is { } edad ? $"Edad: {edad} años" : null);
        if (segunda is not null)
            izquierda.Children.Add(Linea(segunda, 11, FontWeights.Normal, Suave, margenSuperior: 4));
        Grid.SetColumn(izquierda, 0);
        identificacion.Children.Add(izquierda);

        var derecha = new StackPanel();
        derecha.Children.Add(Dato("Fecha", Local(receta.FechaUtc).ToString("dd/MM/yyyy", CulturaDo), 13,
            TextAlignment.Right));
        Grid.SetColumn(derecha, 1);
        identificacion.Children.Add(derecha);

        raiz.Children.Add(identificacion);
        raiz.Children.Add(Raya(1, margenSuperior: 14));
        return raiz;
    }

    // ---------------- Medicamentos ----------------

    private static FrameworkElement Cuerpo(RecetaImpresa receta)
    {
        var raiz = new StackPanel { Margin = new Thickness(Margen, 18, Margen, 0) };

        // "Rp." es como se encabeza una prescripción desde siempre; el
        // farmacéutico sabe que lo que sigue es lo que hay que despachar.
        raiz.Children.Add(Linea("Rp.", 14, FontWeights.Bold, margenSuperior: 0));

        var numero = 1;
        foreach (var m in receta.Medicamentos)
        {
            var item = new StackPanel { Margin = new Thickness(6, 14, 0, 0) };

            var titulo = new TextBlock
            {
                FontFamily = Fuente,
                FontSize = 13.5,
                Foreground = Tinta,
                TextWrapping = TextWrapping.Wrap
            };
            titulo.Inlines.Add(new System.Windows.Documents.Run($"{numero}.  ") { FontWeight = FontWeights.Bold });
            titulo.Inlines.Add(new System.Windows.Documents.Run(m.Medicamento) { FontWeight = FontWeights.Bold });
            if (!string.IsNullOrWhiteSpace(m.Dosis))
                titulo.Inlines.Add(new System.Windows.Documents.Run($"   {m.Dosis}"));
            item.Children.Add(titulo);

            var pauta = Unir("  ·  ", m.Frecuencia, m.Duracion);
            if (pauta is not null)
                item.Children.Add(Linea(pauta, 12, FontWeights.Normal, Tinta,
                    margenSuperior: 3, margenIzquierdo: 24));

            if (!string.IsNullOrWhiteSpace(m.Instrucciones))
            {
                var indicacion = Linea(m.Instrucciones!, 11.5, FontWeights.Normal, Suave,
                    margenSuperior: 2, margenIzquierdo: 24);
                indicacion.FontStyle = FontStyles.Italic;
                item.Children.Add(indicacion);
            }

            raiz.Children.Add(item);
            numero++;
        }

        if (receta.Medicamentos.Count == 0)
            raiz.Children.Add(Linea("—", 13, FontWeights.Normal, Suave, margenSuperior: 10));

        if (!string.IsNullOrWhiteSpace(receta.Notas))
        {
            raiz.Children.Add(Raya(1, margenSuperior: 22));
            raiz.Children.Add(Linea("Indicaciones generales", 11, FontWeights.SemiBold, Suave,
                margenSuperior: 10));
            raiz.Children.Add(Linea(receta.Notas!, 12, FontWeights.Normal, Tinta, margenSuperior: 4));
        }

        return raiz;
    }

    // ---------------- Firma ----------------

    private static FrameworkElement Firma(RecetaImpresa receta)
    {
        var raiz = new StackPanel
        {
            Margin = new Thickness(Margen, 32, Margen, 48),
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = 320
        };

        // La línea va EN BLANCO: la firma se pone a mano (ver el resumen de la
        // clase). Debajo, quién firma y su exequátur.
        raiz.Children.Add(new Border
        {
            BorderBrush = Regla,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 46, 0, 0)
        });

        raiz.Children.Add(Linea(receta.MedicoNombre ?? "Médico tratante", 13, FontWeights.SemiBold,
            alineacion: TextAlignment.Center, margenSuperior: 6));

        if (!string.IsNullOrWhiteSpace(receta.MedicoEspecialidad))
            raiz.Children.Add(Linea(receta.MedicoEspecialidad!, 11, FontWeights.Normal, Suave,
                alineacion: TextAlignment.Center, margenSuperior: 2));

        raiz.Children.Add(Linea(
            string.IsNullOrWhiteSpace(receta.MedicoExequatur)
                ? "Exequátur: ______________"
                : $"Exequátur: {receta.MedicoExequatur}",
            11, FontWeights.Normal, Suave,
            alineacion: TextAlignment.Center, margenSuperior: 2));

        raiz.Children.Add(Linea("Firma y sello", 10, FontWeights.Normal, Suave,
            alineacion: TextAlignment.Center, margenSuperior: 6));

        return raiz;
    }

    // ---------------- Piezas ----------------

    private static TextBlock Linea(string texto, double tamano, FontWeight peso,
        Brush? color = null, TextAlignment alineacion = TextAlignment.Left,
        double margenSuperior = 0, double margenIzquierdo = 0) => new()
    {
        Text = texto,
        FontFamily = Fuente,
        FontSize = tamano,
        FontWeight = peso,
        Foreground = color ?? Tinta,
        TextAlignment = alineacion,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(margenIzquierdo, margenSuperior, 0, 0)
    };

    /// <summary>Etiqueta chica arriba del dato, como en los formularios de papel.</summary>
    private static FrameworkElement Dato(string etiqueta, string valor, double tamano,
        TextAlignment alineacion = TextAlignment.Left)
    {
        var panel = new StackPanel();
        panel.Children.Add(Linea(etiqueta.ToUpperInvariant(), 9, FontWeights.SemiBold, Suave, alineacion));
        panel.Children.Add(Linea(valor, tamano, FontWeights.SemiBold, Tinta, alineacion, margenSuperior: 1));
        return panel;
    }

    private static Border Raya(double grosor, double margenSuperior) => new()
    {
        BorderBrush = Regla,
        BorderThickness = new Thickness(0, grosor, 0, 0),
        Margin = new Thickness(0, margenSuperior, 0, 0)
    };

    private static string? Unir(string separador, params string?[] partes)
    {
        var limpias = partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return limpias.Length == 0 ? null : string.Join(separador, limpias);
    }

    private static DateTime Local(DateTime utc) =>
        MED100.Common.FechaNegocio.AUtcLocal(utc);

    /// <summary>
    /// El logo del negocio, o null si no hay o no se puede leer.
    ///
    /// Nunca tira: una receta sin logo se entrega igual, y una excepción acá
    /// dejaría al paciente esperando por un archivo que alguien movió.
    /// <c>OnLoad</c> es obligatorio: sin eso WPF deja el archivo tomado y la
    /// próxima vez que se quiera cambiar el logo desde Configuración, Windows
    /// no deja.
    /// </summary>
    private static BitmapImage? CargarLogo(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta))
            return null;

        try
        {
            var imagen = new BitmapImage();
            imagen.BeginInit();
            imagen.CacheOption = BitmapCacheOption.OnLoad;
            imagen.UriSource = new Uri(ruta, UriKind.Absolute);
            imagen.EndInit();
            imagen.Freeze();
            return imagen;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Agregar(Grid pagina, FrameworkElement hijo, int fila)
    {
        Grid.SetRow(hijo, fila);
        pagina.Children.Add(hijo);
    }

    private static SolidColorBrush Congelar(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
