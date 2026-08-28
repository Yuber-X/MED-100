using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace MED100.Printing;

/// <summary>
/// Convierte a PDF el mismo visual que va a la impresora térmica.
///
/// Pedido de Yuber (2026-08-15): que cada factura quede guardada como PDF en el
/// expediente del paciente. El PDF es el formato correcto para eso — se abre en
/// cualquier PC, en el teléfono y se manda por correo sin que nadie tenga que
/// tener MED-100 instalado.
///
/// CÓMO: el ticket se rasteriza a una imagen y esa imagen se mete en una página
/// del ancho del papel. Es el mismo camino que ya usa FAControl y no es
/// casualidad: el visual es un árbol de WPF, y traducirlo a primitivas
/// vectoriales de PDF significaría reescribir el ticket dos veces y que las dos
/// versiones se desincronicen. Rasterizando, <b>el PDF es exactamente lo que se
/// imprimió</b>, que es justamente lo que hay que poder demostrar de un
/// comprobante.
///
/// Se rasteriza al DOBLE de resolución (192 DPI) para que el texto chico del
/// ticket se lea al ampliarlo, sin que el archivo se vaya de tamaño.
/// </summary>
public static class ExportadorPdf
{
    /// <summary>96 DPI de WPF × 2. Nítido al ampliar, y el archivo pesa poco.</summary>
    private const double Escala = 2.0;

    // El tamaño de la página se DERIVA del visual (DIU de WPF → puntos de PDF)
    // en vez de fijarse a 80mm. Los visuales de MED-100 no son todos del ancho
    // del papel térmico: CierreVisualFactory arma 794 DIU (210mm) cuando el
    // usuario elige Carta, y con el ancho fijo ese cierre saldría en una tira
    // de 8cm con el contenido de una hoja entera encogido adentro. Es el mismo
    // defecto que estaba en FAControl y que allá sí llegó a manos del cliente
    // (facturas de venta y fichas de vehículo guardadas en 80mm).

    /// <summary>
    /// Guarda el visual como PDF en <paramref name="rutaDestino"/>.
    ///
    /// El visual tiene que venir ya medido y organizado (las factories de
    /// ticket lo hacen): sin <c>Arrange</c>, ActualWidth vale 0 y saldría un
    /// PDF en blanco.
    /// </summary>
    public static void Guardar(FrameworkElement visual, string rutaDestino, string titulo)
    {
        ArgumentNullException.ThrowIfNull(visual);

        // Si llega sin organizar, se organiza acá antes que devolver una hoja
        // en blanco. Es barato y evita un fallo silencioso.
        if (visual.ActualWidth <= 0 || visual.ActualHeight <= 0)
        {
            visual.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            visual.Arrange(new Rect(visual.DesiredSize));
            visual.UpdateLayout();
        }

        var ancho = visual.ActualWidth;
        var alto = visual.ActualHeight;
        if (ancho <= 0 || alto <= 0)
            throw new InvalidOperationException(
                "El documento no tiene tamaño: no se puede exportar a PDF.");

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(ancho * Escala), (int)Math.Ceiling(alto * Escala),
            96 * Escala, 96 * Escala, PixelFormats.Pbgra32);

        // Fondo blanco explícito: el ticket es transparente donde no dibuja, y
        // en PDF eso sale negro en algunos visores.
        var fondo = new DrawingVisual();
        using (var dc = fondo.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ancho, alto));
        bitmap.Render(fondo);
        bitmap.Render(visual);

        var rutaPng = Path.Combine(Path.GetTempPath(), $"med100-doc-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var archivo = File.Create(rutaPng))
                encoder.Save(archivo);

            using var pdf = new PdfDocument();
            pdf.Info.Title = titulo;
            pdf.Info.Creator = "MED-100";

            // DIU (96 por pulgada) → puntos (72 por pulgada). Un ticket de 302
            // DIU da 80mm, que es lo que salía antes; un cierre en Carta da su
            // tamaño real en vez de encogerse.
            var pagina = pdf.AddPage();
            pagina.Width = XUnit.FromPoint(ancho * 72.0 / 96.0);
            pagina.Height = XUnit.FromPoint(alto * 72.0 / 96.0);

            using (var grafico = XGraphics.FromPdfPage(pagina))
            using (var imagen = XImage.FromFile(rutaPng))
                grafico.DrawImage(imagen, 0, 0, pagina.Width.Point, pagina.Height.Point);

            pdf.Save(rutaDestino);
        }
        finally
        {
            // El PNG es un paso intermedio: si queda, ocupa espacio para siempre.
            try { if (File.Exists(rutaPng)) File.Delete(rutaPng); }
            catch (IOException) { /* lo limpia Windows */ }
        }
    }

    /// <summary>
    /// Genera el PDF en un archivo temporal y devuelve su ruta. Lo usa el
    /// archivado automático: el expediente guarda ARCHIVOS, no visuales de WPF,
    /// así que hace falta un intermediario en disco.
    ///
    /// Quien llama es responsable de borrarlo — el expediente lo COPIA, no lo
    /// mueve.
    /// </summary>
    public static string GuardarTemporal(FrameworkElement visual, string nombreSugerido,
        string titulo)
    {
        var limpio = string.Concat(nombreSugerido
            .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var ruta = Path.Combine(Path.GetTempPath(), $"{limpio}.pdf");
        Guardar(visual, ruta, titulo);
        return ruta;
    }
}
