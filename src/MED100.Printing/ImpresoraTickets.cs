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
    ///
    /// <para><b>PrintQueue y PrintServer se liberan.</b> Los dos son
    /// IDisposable y envuelven handles del spooler de Windows. Sin liberarlos,
    /// cada ticket dejaba uno colgando: en una clínica que imprime todo el día
    /// eso se acumula hasta que el spooler empieza a fallar, y el síntoma
    /// ("de repente dejó de imprimir") aparece horas después y lejos de la
    /// causa.</para>
    ///
    /// <para>Si la impresora configurada ya no existe —se desconectó, la
    /// borraron, le cambiaron el nombre— el constructor de PrintQueue tira. Se
    /// deja propagar a propósito: quien llama lo atrapa y cae a la vista previa,
    /// que es el plan B correcto. Tragarlo acá haría que el ticket se fuera en
    /// silencio a otra impresora.</para>
    /// </summary>
    public static void ImprimirDirecto(FrameworkElement visual, string descripcion,
        int copias = 1, string? nombreImpresora = null)
    {
        var dialogo = new PrintDialog();

        if (string.IsNullOrWhiteSpace(nombreImpresora))
        {
            ImprimirCopias(dialogo, visual, descripcion, copias);
            return;
        }

        using var servidor = new System.Printing.PrintServer();
        using var cola = new System.Printing.PrintQueue(servidor, nombreImpresora);
        dialogo.PrintQueue = cola;
        ImprimirCopias(dialogo, visual, descripcion, copias);
    }

    private static void ImprimirCopias(PrintDialog dialogo, FrameworkElement visual,
        string descripcion, int copias)
    {
        for (var i = 0; i < Math.Max(1, copias); i++)
            dialogo.PrintVisual(visual, descripcion);
    }
}
