using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MED100.Common;
using MED100.Models;

namespace MED100.Printing;

/// <summary>
/// Construye el visual del ticket 80mm (302px @96dpi) desde VentaResultado.
/// El mismo visual va a pantalla (vista previa) y a impresora — patrón
/// PrestControl. Los datos del negocio vienen de ConfiguracionNegocio
/// (NUNCA hardcodeados, spec §12) y encabezado/pie de AjustesLocales.
/// </summary>
public static class TicketVisualFactory
{
    private const double Ancho = 302;
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private static readonly FontFamily Mono = new("Consolas");

    public static FrameworkElement Crear(
        VentaResultado venta, ConfiguracionNegocio negocio,
        string nombreCajero, string? encabezadoExtra, string? pie)
    {
        var panel = new StackPanel { Width = Ancho, Background = Brushes.White };
        var margen = new Thickness(12, 2, 12, 2);

        // --- Encabezado del negocio ---
        panel.Children.Add(Texto(negocio.NombreNegocio, 15, FontWeights.Bold, TextAlignment.Center, new Thickness(12, 14, 12, 2)));
        if (!string.IsNullOrWhiteSpace(negocio.Rnc))
            panel.Children.Add(Texto($"RNC: {negocio.Rnc}", 11, FontWeights.Normal, TextAlignment.Center, margen));
        if (!string.IsNullOrWhiteSpace(negocio.Direccion))
            panel.Children.Add(Texto(negocio.Direccion, 11, FontWeights.Normal, TextAlignment.Center, margen));
        if (!string.IsNullOrWhiteSpace(negocio.Telefono))
            panel.Children.Add(Texto($"Tel: {negocio.Telefono}", 11, FontWeights.Normal, TextAlignment.Center, margen));
        if (!string.IsNullOrWhiteSpace(encabezadoExtra))
            panel.Children.Add(Texto(encabezadoExtra, 11, FontWeights.Normal, TextAlignment.Center, margen));

        panel.Children.Add(Separador());

        // --- Datos de la venta (código de compra = número de factura) ---
        var fechaLocal = TimeZoneInfo.ConvertTimeFromUtc(venta.FechaEmisionUtc, ZonaRd());
        panel.Children.Add(Fila("Factura no.:", venta.NumeroFactura, FontWeights.Bold));
        // Qué clase de comprobante es. Se DEDUCE del NCF y no se escribe fijo:
        // "Factura de consumo" es B02, pero si un día emiten un crédito fiscal
        // (B01) el papel tiene que decirlo o le sirve de nada al contador del
        // paciente. Sin NCF no es un comprobante fiscal y no se afirma nada.
        panel.Children.Add(Texto(ComprobanteFiscal.Tipo(venta.Ncf), 11, FontWeights.Normal,
            TextAlignment.Center, margen));
        // El NCF va arriba y en negrita: es lo que el paciente le lleva al
        // contador, y buscarlo perdido entre las líneas es un fastidio.
        if (!string.IsNullOrWhiteSpace(venta.Ncf))
            panel.Children.Add(Fila("NCF:", venta.Ncf, FontWeights.Bold));
        panel.Children.Add(Fila("Fecha:", fechaLocal.ToString("dd/MM/yyyy hh:mm tt", CulturaDo), FontWeights.Normal));
        panel.Children.Add(Fila("Paciente:", venta.NombreCliente ?? "Consumidor final", FontWeights.Normal));
        // El médico va en el ticket para que sepa a quién le toca el paciente
        // (pedido 2026-08-10). El PORCENTAJE del honorario NO se imprime: es
        // un arreglo entre la clínica y el médico, no del paciente.
        if (venta.Honorario?.MedicoNombre is { } medico)
            panel.Children.Add(Fila("Médico:", medico, FontWeights.Normal));

        panel.Children.Add(Separador());

        // --- Líneas ---
        foreach (var linea in venta.Lineas)
        {
            panel.Children.Add(Texto(linea.NombreProducto, 11, FontWeights.Normal, TextAlignment.Left, margen));
            panel.Children.Add(Fila($"  {linea.Cantidad} x {Moneda(linea.PrecioUnitario, negocio)}",
                Moneda(linea.Subtotal, negocio), FontWeights.Normal));
        }

        panel.Children.Add(Separador());

        // --- Totales ---
        // Con rebaja se imprimen los tres renglones: lo que valía, lo que se
        // rebajó y lo que queda. Poner solo el final deja al paciente sin ver
        // el descuento que le hicieron, que es justo lo que se quiere mostrar.
        if (venta.Totales.HuboRebaja)
        {
            panel.Children.Add(Fila("Subtotal:", Moneda(venta.Totales.SubtotalSinRebaja, negocio), FontWeights.Normal));
            panel.Children.Add(Fila("Descuento:", "-" + Moneda(venta.Totales.Descuento, negocio), FontWeights.Normal));
            panel.Children.Add(Fila("Subtotal con descuento:", Moneda(venta.Totales.Subtotal, negocio), FontWeights.Normal));
        }
        else
        {
            panel.Children.Add(Fila("Subtotal:", Moneda(venta.Totales.Subtotal, negocio), FontWeights.Normal));
        }
        // Solo se imprime el ITBIS si de verdad hubo: en una clínica casi todo
        // es servicio de salud exento y una línea de "ITBIS 0.00" confunde.
        if (venta.Totales.Itbis > 0m)
            panel.Children.Add(Fila($"ITBIS ({venta.Totales.ItbisTasa:0.##}%):", Moneda(venta.Totales.Itbis, negocio), FontWeights.Normal));
        panel.Children.Add(Fila("TOTAL:", Moneda(venta.Totales.Total, negocio), FontWeights.Bold, 13));

        // --- Seguro ---
        // Se imprime el reparto completo: el paciente tiene que poder ver
        // cuánto cubrió su ARS y cuánto puso él. Es la pregunta número uno
        // en el mostrador.
        if (venta.Ars is { ArsId: not null } ars)
        {
            panel.Children.Add(Separador());
            panel.Children.Add(Fila("Seguro:", ars.ArsNombre ?? "—", FontWeights.Normal));
            if (!string.IsNullOrWhiteSpace(ars.Autorizacion))
                panel.Children.Add(Fila("Autorización:", ars.Autorizacion, FontWeights.Normal));
            panel.Children.Add(Fila("Cubre el seguro:", Moneda(ars.Cubierto, negocio), FontWeights.Normal));
            panel.Children.Add(Fila("PAGA EL PACIENTE:", Moneda(ars.PacientePaga, negocio), FontWeights.Bold, 13));
        }

        if (venta.EfectivoRecibido is { } efectivo)
        {
            panel.Children.Add(Fila("Efectivo:", Moneda(efectivo, negocio), FontWeights.Normal));
            if (venta.Cambio is { } cambio)
                panel.Children.Add(Fila("Cambio:", Moneda(cambio, negocio), FontWeights.Normal));
        }
        panel.Children.Add(Fila("Método de pago:", NombreMetodo(venta.MetodoPago), FontWeights.Normal));

        // Fiado (012). Va en el papel que se lleva el paciente y no solo en el
        // sistema: si el único registro de la deuda queda del lado de la
        // clínica, discutirla después es la palabra de uno contra la del otro.
        if (venta.QuedoFiado)
        {
            panel.Children.Add(Separador());
            panel.Children.Add(Fila("Abonó hoy:", Moneda(venta.AbonadoInicial, negocio), FontWeights.Normal));
            panel.Children.Add(Fila("QUEDA DEBIENDO:", Moneda(venta.SaldoPendiente, negocio),
                FontWeights.Bold, 13));
            if (venta.FechaCompromiso is { } compromiso)
                panel.Children.Add(Fila("Se compromete a pagar:",
                    compromiso.ToString("dd/MM/yyyy"), FontWeights.Bold));
        }
        // El cajero va al final, junto al método de pago, como lo pidió la
        // clínica el 2026-08-27: es dato de quién cobró, no de quién atendió.
        panel.Children.Add(Fila("Cajero:", nombreCajero, FontWeights.Normal));

        panel.Children.Add(Separador());
        panel.Children.Add(Texto(
            string.IsNullOrWhiteSpace(pie) ? "Gracias por preferir nuestros servicios" : pie,
            11, FontWeights.Normal, TextAlignment.Center, new Thickness(12, 4, 12, 16)));

        // Medir/organizar para poder imprimir sin mostrarse en pantalla
        panel.Measure(new Size(Ancho, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, Ancho, panel.DesiredSize.Height));
        return panel;
    }

    private static string Moneda(decimal valor, ConfiguracionNegocio negocio)
    {
        var texto = negocio.FormatoMiles == "punto"
            ? valor.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))
            : valor.ToString("N2", CulturaDo);
        return $"{negocio.MonedaSimbolo} {texto}";
    }

    private static string NombreMetodo(MetodoPagoFactura metodo) => metodo switch
    {
        MetodoPagoFactura.Efectivo => "Efectivo",
        MetodoPagoFactura.Tarjeta => "Tarjeta",
        MetodoPagoFactura.Transferencia => "Transferencia",
        _ => "Mixto"
    };

    private static TimeZoneInfo ZonaRd()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time"); }
    }

    private static TextBlock Texto(string texto, double tamano, FontWeight peso,
        TextAlignment alineacion, Thickness margen) => new()
    {
        Text = texto,
        FontFamily = Mono,
        FontSize = tamano,
        FontWeight = peso,
        Foreground = Brushes.Black,
        TextAlignment = alineacion,
        TextWrapping = TextWrapping.Wrap,
        Margin = margen
    };

    private static Grid Fila(string izquierda, string derecha, FontWeight peso, double tamano = 11)
    {
        var grid = new Grid { Margin = new Thickness(12, 1, 12, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var izq = Texto(izquierda, tamano, peso, TextAlignment.Left, new Thickness(0));
        var der = Texto(derecha, tamano, peso, TextAlignment.Right, new Thickness(0));
        Grid.SetColumn(der, 1);
        grid.Children.Add(izq);
        grid.Children.Add(der);
        return grid;
    }

    private static TextBlock Separador() =>
        Texto(new string('-', 38), 11, FontWeights.Normal, TextAlignment.Center, new Thickness(12, 4, 12, 4));
}
