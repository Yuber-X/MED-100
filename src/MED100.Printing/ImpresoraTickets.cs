using System.Windows;
using System.Windows.Controls;

namespace MED100.Printing;

/// <summary>
/// Envío del ticket a la impresora (PrintVisual, patrón PrestControl).
/// La impresión NUNCA bloquea la venta (spec §9.6): quien llama ya persistió
/// la factura y maneja el reintento si esto falla.
/// </summary>
public static class ImpresoraTickets
{
    /// <summary>Abre el diálogo de impresión del sistema. True si se envió a imprimir.</summary>
    public static bool Imprimir(FrameworkElement visual, string descripcion, int copias = 1)
    {
        var dialogo = new PrintDialog();
        if (dialogo.ShowDialog() != true)
            return false;

        for (var i = 0; i < Math.Max(1, copias); i++)
            dialogo.PrintVisual(visual, descripcion);
        return true;
    }

    /// <summary>
    /// Imprime SIN preguntar (flujo por defecto al cobrar, pedido Yuber
    /// 2026-07-12): usa la impresora configurada o la predeterminada.
    /// </summary>
    public static void ImprimirDirecto(FrameworkElement visual, string descripcion,
        int copias = 1, string? nombreImpresora = null)
    {
        var dialogo = new PrintDialog();
        if (!string.IsNullOrWhiteSpace(nombreImpresora))
            dialogo.PrintQueue = new System.Printing.PrintQueue(
                new System.Printing.PrintServer(), nombreImpresora);

        for (var i = 0; i < Math.Max(1, copias); i++)
            dialogo.PrintVisual(visual, descripcion);
    }
}
