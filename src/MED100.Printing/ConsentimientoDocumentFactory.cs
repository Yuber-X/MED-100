using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MED100.Models;

namespace MED100.Printing;

/// <summary>
/// El consentimiento informado impreso, en hoja carta (pedido de la clínica
/// 2026-09-21; Yuber decidió el 2026-09-24 que se imprime y se firma en papel).
///
/// POR QUÉ ES UN FlowDocument Y LA RECETA NO: la receta entra siempre en una
/// hoja y la firma va clavada al pie, así que se arma como un visual de tamaño
/// fijo. Un consentimiento es un texto legal de largo impredecible —la clínica
/// escribe lo que su abogado le diga— y un visual de tamaño fijo se CORTARÍA al
/// imprimir, sin avisar, justo en el documento donde eso es inaceptable. El
/// FlowDocument pagina solo: si el texto no cabe, sigue en la hoja 2.
///
/// La firma va EN BLANCO, en papel: el paciente firma de puño y letra y el
/// papel firmado se escanea a su expediente. El sistema nunca estampa una firma.
/// </summary>
public static class ConsentimientoDocumentFactory
{
    public const double AnchoCarta = 816;    // 8.5in @96dpi
    public const double AltoCarta = 1056;    // 11in @96dpi

    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Fuente = new("Segoe UI");
    private static readonly SolidColorBrush Suave = Congelar(Color.FromRgb(0x55, 0x55, 0x55));
    private static readonly SolidColorBrush Regla = Congelar(Color.FromRgb(0x33, 0x33, 0x33));

    /// <summary>
    /// Arma el documento. Se llama UNA VEZ POR USO (una para la vista previa y
    /// otra para imprimir): un FlowDocument no puede estar colgado de dos
    /// lugares a la vez, y reusar la misma instancia tira "already has a
    /// logical parent" en medio de la impresión.
    /// </summary>
    public static FlowDocument Crear(ConsentimientoImpreso consentimiento,
        ConfiguracionNegocio negocio)
    {
        ArgumentNullException.ThrowIfNull(consentimiento);

        var documento = new FlowDocument
        {
            PageWidth = AnchoCarta,
            PageHeight = AltoCarta,
            PagePadding = new Thickness(64, 56, 64, 56),
            // Una sola columna, del ancho útil de la hoja. OJO: infinito NO sirve
            // —tentador para "que no divida en columnas"— porque deja el ancho
            // disponible sin definir y las tablas de ancho proporcional (las
            // dos firmas) colapsan a un carácter de ancho. Pasó el 2026-09-25.
            ColumnWidth = AnchoCarta - 64 * 2,
            FontFamily = Fuente,
            FontSize = 12,
            Foreground = Brushes.Black,
            Background = Brushes.White,
            TextAlignment = TextAlignment.Justify
        };

        documento.Blocks.Add(Membrete(negocio));
        documento.Blocks.Add(Titulo("CONSENTIMIENTO INFORMADO", 15, FontWeights.Bold, 18));

        var subtitulo = consentimiento.ProcedimientoNombre is { } procedimiento
            ? $"{consentimiento.Titulo} — {procedimiento}"
            : consentimiento.Titulo;
        documento.Blocks.Add(Titulo(subtitulo, 12.5, FontWeights.SemiBold, 4));

        documento.Blocks.Add(Identificacion(consentimiento));

        foreach (var parrafo in Parrafos(consentimiento.Cuerpo))
            documento.Blocks.Add(parrafo);

        documento.Blocks.Add(Firmas(consentimiento));
        return documento;
    }

    // ---------------- Bloques ----------------

    private static Block Membrete(ConfiguracionNegocio negocio)
    {
        var fila = new Grid();
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (CargarLogo(negocio.LogoRuta) is { } logo)
        {
            var imagen = new Image
            {
                Source = logo,
                Width = 76,
                MaxHeight = 76,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 18, 0)
            };
            Grid.SetColumn(imagen, 0);
            fila.Children.Add(imagen);
        }

        var datos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        datos.Children.Add(new TextBlock
        {
            Text = negocio.NombreNegocio,
            FontFamily = Fuente,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap
        });

        var contacto = Unir("  ·  ",
            negocio.Direccion,
            negocio.Telefono,
            string.IsNullOrWhiteSpace(negocio.Rnc) ? null : $"RNC {negocio.Rnc}");
        if (contacto is not null)
            datos.Children.Add(new TextBlock
            {
                Text = contacto,
                FontFamily = Fuente,
                FontSize = 10.5,
                Foreground = Suave,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });

        Grid.SetColumn(datos, 1);
        fila.Children.Add(datos);

        var contenedor = new StackPanel();
        contenedor.Children.Add(fila);
        contenedor.Children.Add(new Border
        {
            BorderBrush = Regla,
            BorderThickness = new Thickness(0, 2, 0, 0),
            Margin = new Thickness(0, 12, 0, 0)
        });

        return new BlockUIContainer(contenedor) { Margin = new Thickness(0) };
    }

    private static Block Titulo(string texto, double tamano, FontWeight peso, double margenSuperior) =>
        new Paragraph(new Run(texto))
        {
            FontSize = tamano,
            FontWeight = peso,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, margenSuperior, 0, 0),
            KeepWithNext = true
        };

    /// <summary>Quién firma y cuándo. Va arriba del texto: es lo que se verifica primero.</summary>
    private static Block Identificacion(ConsentimientoImpreso consentimiento)
    {
        var partes = new List<string> { $"Paciente: {consentimiento.PacienteNombre}" };
        if (!string.IsNullOrWhiteSpace(consentimiento.PacienteCedula))
            partes.Add($"Cédula: {consentimiento.PacienteCedula}");
        if (!string.IsNullOrWhiteSpace(consentimiento.MedicoNombre))
            partes.Add($"Médico: {consentimiento.MedicoNombre}");
        partes.Add($"Fecha: {Local(consentimiento.FechaUtc).ToString("dd/MM/yyyy", CulturaDo)}");

        return new Paragraph(new Run(string.Join("     ", partes)))
        {
            FontSize = 11,
            TextAlignment = TextAlignment.Left,
            Margin = new Thickness(0, 18, 0, 16),
            Padding = new Thickness(0, 0, 0, 10),
            BorderBrush = Regla,
            BorderThickness = new Thickness(0, 0, 0, 1),
            KeepWithNext = true
        };
    }

    /// <summary>
    /// El texto de la clínica, respetando sus saltos de línea. Una línea en
    /// blanco separa párrafos; los saltos sueltos se respetan dentro del mismo
    /// párrafo, porque muchas plantillas vienen con listas escritas a mano.
    /// </summary>
    private static IEnumerable<Block> Parrafos(string cuerpo)
    {
        var bloques = cuerpo.Replace("\r\n", "\n").Split("\n\n",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var bloque in bloques)
        {
            var parrafo = new Paragraph { Margin = new Thickness(0, 0, 0, 10), LineHeight = 19 };
            var lineas = bloque.Split('\n');
            for (var i = 0; i < lineas.Length; i++)
            {
                if (i > 0)
                    parrafo.Inlines.Add(new LineBreak());
                parrafo.Inlines.Add(new Run(lineas[i].Trim()));
            }
            yield return parrafo;
        }
    }

    /// <summary>
    /// Las dos firmas en blanco, lado a lado. Tabla y no dos párrafos porque un
    /// FlowDocument apila todo: sin tabla, la firma del médico quedaría debajo
    /// de la del paciente y la hoja se vería como un formulario a medio hacer.
    /// </summary>
    private static Block Firmas(ConsentimientoImpreso consentimiento)
    {
        // Anchos EN PÍXELES, no proporcionales. Las columnas "estrella" de una
        // Table dentro de un FlowDocument NO reparten el ancho como en un Grid:
        // colapsan al mínimo y la etiqueta sale una letra por renglón. Probado
        // el 2026-09-25 (píxeles y auto sí funcionan). Los 640 de ancho total
        // entran también en A4, que es más angosta que carta.
        const double AnchoFirma = 290;
        const double Separacion = 60;

        var tabla = new Table { Margin = new Thickness(0, 40, 0, 0), CellSpacing = 0 };
        tabla.Columns.Add(new TableColumn { Width = new GridLength(AnchoFirma) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(Separacion) });
        tabla.Columns.Add(new TableColumn { Width = new GridLength(AnchoFirma) });

        var grupo = new TableRowGroup();
        var fila = new TableRow();
        fila.Cells.Add(Firma("Firma del paciente o su tutor", consentimiento.PacienteNombre));
        fila.Cells.Add(new TableCell());
        fila.Cells.Add(Firma("Firma y sello del médico", consentimiento.MedicoNombre));
        grupo.Rows.Add(fila);
        tabla.RowGroups.Add(grupo);
        return tabla;
    }

    private static TableCell Firma(string etiqueta, string? nombre)
    {
        var celda = new TableCell();

        celda.Blocks.Add(new Paragraph
        {
            Margin = new Thickness(0, 34, 0, 0),
            BorderBrush = Regla,
            BorderThickness = new Thickness(0, 0, 0, 1),
            KeepWithNext = true
        });

        celda.Blocks.Add(new Paragraph(new Run(etiqueta))
        {
            FontSize = 10,
            Foreground = Suave,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            KeepWithNext = true
        });

        if (!string.IsNullOrWhiteSpace(nombre))
            celda.Blocks.Add(new Paragraph(new Run(nombre))
            {
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });

        return celda;
    }

    // ---------------- Auxiliares ----------------

    private static string? Unir(string separador, params string?[] partes)
    {
        var limpias = partes.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).ToArray();
        return limpias.Length == 0 ? null : string.Join(separador, limpias);
    }

    private static DateTime Local(DateTime utc) => MED100.Common.FechaNegocio.AUtcLocal(utc);

    /// <summary>El logo, o null si no hay o no se puede leer. Nunca tira.</summary>
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

    private static SolidColorBrush Congelar(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
