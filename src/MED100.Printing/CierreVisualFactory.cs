using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MED100.Models;

namespace MED100.Printing;

/// <summary>
/// Visual imprimible del cierre de caja (pedido Yuber 2026-07-12).
/// Dos tamaños: ticket 80mm (para la impresora térmica de la caja) y Carta
/// (para archivar o entregar al dueño). El mismo visual va a pantalla
/// (vista previa) y a la impresora — patrón del ticket de venta.
/// </summary>
public static class CierreVisualFactory
{
    private const double Ancho80mm = 302;      // 80mm @96dpi
    private const double AnchoCarta = 794;     // 210mm @96dpi (A4/Carta)
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Mono = new("Consolas");

    /// <summary>Cierre GENERAL: desglose de cada cajero + totales del negocio.</summary>
    public static FrameworkElement Crear(CuadreGeneral cierre, ConfiguracionNegocio negocio,
        string generadoPor, TamanoImpresion tamano)
    {
        var ancho = tamano == TamanoImpresion.Carta ? AnchoCarta : Ancho80mm;
        var grande = tamano == TamanoImpresion.Carta;
        var panel = new StackPanel { Width = ancho, Background = Brushes.White };

        Encabezado(panel, negocio, "CIERRE DE CAJA", cierre.Fecha, generadoPor, ancho, grande);

        // --- Desglose por cajero ---
        foreach (var cajero in cierre.PorCajero)
        {
            panel.Children.Add(Titulo(cajero.NombreCajero, ancho, grande));
            panel.Children.Add(Fila("Facturas:", cajero.TotalFacturas.ToString(CulturaDo), ancho, grande));
            panel.Children.Add(Fila("Efectivo:", Moneda(cajero.TotalEfectivo, negocio), ancho, grande));
            panel.Children.Add(Fila("Tarjeta:", Moneda(cajero.TotalTarjeta, negocio), ancho, grande));
            if (cajero.TotalTransferencia > 0m)
                panel.Children.Add(Fila("Transferencia:", Moneda(cajero.TotalTransferencia, negocio), ancho, grande));
            if (cajero.TotalMixto > 0m)
                panel.Children.Add(Fila("Mixto:", Moneda(cajero.TotalMixto, negocio), ancho, grande));
            panel.Children.Add(Fila("Total del turno:", Moneda(cajero.TotalVendido, negocio), ancho, grande, negrita: true));
            panel.Children.Add(Fila("Tiempo activo:", cajero.TiempoActivoTexto, ancho, grande));
            if (cajero.FacturasAnuladas > 0)
                panel.Children.Add(Fila($"Anuladas ({cajero.FacturasAnuladas}):",
                    Moneda(cajero.MontoAnulado, negocio), ancho, grande));
            panel.Children.Add(Separador(ancho, grande));
        }

        if (cierre.PorCajero.Count == 0)
            panel.Children.Add(Texto("Sin ventas registradas en este día.", ancho, grande,
                TextAlignment.Center, FontWeights.Normal));

        // --- Totales del negocio ---
        panel.Children.Add(Titulo("TOTAL DEL DÍA", ancho, grande));
        panel.Children.Add(Fila("Facturas emitidas:", cierre.TotalFacturas.ToString(CulturaDo), ancho, grande));
        panel.Children.Add(Fila("Efectivo:", Moneda(cierre.TotalEfectivo, negocio), ancho, grande));
        panel.Children.Add(Fila("Tarjeta:", Moneda(cierre.TotalTarjeta, negocio), ancho, grande));
        panel.Children.Add(Fila("Transferencia:", Moneda(cierre.TotalTransferencia, negocio), ancho, grande));
        panel.Children.Add(Fila("Mixto:", Moneda(cierre.TotalMixto, negocio), ancho, grande));
        panel.Children.Add(Separador(ancho, grande));
        panel.Children.Add(Fila("TOTAL VENDIDO:", Moneda(cierre.TotalVendido, negocio), ancho, grande,
            negrita: true, escala: 1.2));

        if (cierre.FacturasAnuladas > 0)
            panel.Children.Add(Fila($"Anuladas del día ({cierre.FacturasAnuladas}):",
                Moneda(cierre.MontoAnulado, negocio), ancho, grande));

        panel.Children.Add(Separador(ancho, grande));

        // Espacio para firmas: el cierre suele archivarse en papel
        panel.Children.Add(Texto("Firma del responsable: ______________________", ancho, grande,
            TextAlignment.Left, FontWeights.Normal, margenSuperior: 24));

        panel.Measure(new Size(ancho, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, ancho, panel.DesiredSize.Height));
        return panel;
    }

    private static void Encabezado(StackPanel panel, ConfiguracionNegocio negocio, string titulo,
        DateOnly fecha, string generadoPor, double ancho, bool grande)
    {
        panel.Children.Add(Texto(negocio.NombreNegocio, ancho, grande, TextAlignment.Center,
            FontWeights.Bold, escala: 1.3, margenSuperior: 16));
        if (!string.IsNullOrWhiteSpace(negocio.Rnc))
            panel.Children.Add(Texto($"RNC: {negocio.Rnc}", ancho, grande, TextAlignment.Center, FontWeights.Normal));

        panel.Children.Add(Texto(titulo, ancho, grande, TextAlignment.Center, FontWeights.Bold,
            escala: 1.15, margenSuperior: 10));
        panel.Children.Add(Texto($"Día de negocio: {fecha:dd/MM/yyyy}", ancho, grande,
            TextAlignment.Center, FontWeights.Normal));
        panel.Children.Add(Texto(
            $"Generado por {generadoPor} · {DateTime.Now.ToString("dd/MM/yyyy hh:mm tt", CulturaDo)}",
            ancho, grande, TextAlignment.Center, FontWeights.Normal));
        panel.Children.Add(Separador(ancho, grande));
    }

    private static string Moneda(decimal valor, ConfiguracionNegocio negocio)
    {
        var texto = negocio.FormatoMiles == "punto"
            ? valor.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))
            : valor.ToString("N2", CulturaDo);
        return $"{negocio.MonedaSimbolo} {texto}";
    }

    private static double TamanoBase(bool grande) => grande ? 12 : 11;

    private static TextBlock Texto(string texto, double ancho, bool grande, TextAlignment alineacion,
        FontWeight peso, double escala = 1.0, double margenSuperior = 2) => new()
    {
        Text = texto,
        FontFamily = Mono,
        FontSize = TamanoBase(grande) * escala,
        FontWeight = peso,
        Foreground = Brushes.Black,
        TextAlignment = alineacion,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(Margen(grande), margenSuperior, Margen(grande), 2)
    };

    private static TextBlock Titulo(string texto, double ancho, bool grande) =>
        Texto(texto, ancho, grande, TextAlignment.Left, FontWeights.Bold, escala: 1.1, margenSuperior: 10);

    private static Grid Fila(string izquierda, string derecha, double ancho, bool grande,
        bool negrita = false, double escala = 1.0)
    {
        var grid = new Grid { Margin = new Thickness(Margen(grande), 1, Margen(grande), 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var peso = negrita ? FontWeights.Bold : FontWeights.Normal;
        var izq = Texto(izquierda, ancho, grande, TextAlignment.Left, peso, escala, 0);
        var der = Texto(derecha, ancho, grande, TextAlignment.Right, peso, escala, 0);
        izq.Margin = der.Margin = new Thickness(0);
        Grid.SetColumn(der, 1);
        grid.Children.Add(izq);
        grid.Children.Add(der);
        return grid;
    }

    private static TextBlock Separador(double ancho, bool grande)
    {
        var guiones = grande ? 96 : 38;
        return Texto(new string('-', guiones), ancho, grande, TextAlignment.Center,
            FontWeights.Normal, margenSuperior: 6);
    }

    private static double Margen(bool grande) => grande ? 40 : 12;
}
