using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MED100.Printing;

/// <summary>
/// Impresión de documentos que PAGINAN (FlowDocument), a diferencia de
/// <see cref="ImpresoraTickets"/>, que manda un visual de una sola pieza.
///
/// Se usa para el consentimiento informado: es un texto legal de largo
/// impredecible y tiene que poder seguir en la hoja 2 en vez de cortarse.
/// </summary>
public static class ImpresoraDocumentos
{
    /// <summary>
    /// Abre el diálogo del sistema e imprime. True si se envió a imprimir.
    ///
    /// Recibe una FÁBRICA y no el documento: el FlowDocument que se está
    /// mostrando en la vista previa ya tiene padre, y pasárselo al paginador
    /// tira "already has a logical parent". Se arma uno nuevo para imprimir.
    /// </summary>
    public static bool Imprimir(Func<FlowDocument> fabrica, string descripcion)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
            return false;

        var documento = fabrica();
        var paginador = ((IDocumentPaginatorSource)documento).DocumentPaginator;

        // El tamaño de página sale de la impresora elegida, no de una constante:
        // si el usuario manda la hoja a una impresora configurada en A4, el
        // texto tiene que reacomodarse a A4 y no salir cortado a lo ancho.
        var ancho = dialogo.PrintableAreaWidth > 0 ? dialogo.PrintableAreaWidth : documento.PageWidth;
        var alto = dialogo.PrintableAreaHeight > 0 ? dialogo.PrintableAreaHeight : documento.PageHeight;

        documento.PageWidth = ancho;
        documento.PageHeight = alto;
        // La columna sigue al ancho de la hoja: si se queda con el de carta en
        // una impresora A4, el texto se imprime angosto y las tablas quedan
        // descalzadas del margen.
        documento.ColumnWidth = Math.Max(1,
            ancho - documento.PagePadding.Left - documento.PagePadding.Right);
        paginador.PageSize = new Size(ancho, alto);

        dialogo.PrintDocument(paginador, descripcion);
        return true;
    }
}
