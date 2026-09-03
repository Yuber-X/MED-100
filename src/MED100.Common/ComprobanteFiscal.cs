namespace MED100.Common;

/// <summary>
/// Nomenclatura de comprobantes fiscales de la DGII (República Dominicana).
///
/// El NCF empieza con tres caracteres que dicen QUÉ clase de comprobante es.
/// Eso no es un detalle de impresión sino una regla del país, igual que
/// <see cref="FechaNegocio"/>: por eso vive acá y no dentro del ticket.
///
/// La clínica lo pidió el 2026-08-27 en el encabezado de la factura
/// (<i>"Factura de consumo"</i>). Se deduce del NCF en vez de escribirse fijo:
/// si un día emiten un crédito fiscal y el papel sigue diciendo "de consumo",
/// al contador del paciente no le sirve, y el error recién se ve en la DGII.
/// </summary>
public static class ComprobanteFiscal
{
    /// <summary>Cuando no hay NCF: es un recibo interno, no un comprobante fiscal.</summary>
    public const string SinNcf = "Factura";

    public static string Tipo(string? ncf)
    {
        var codigo = (ncf ?? string.Empty).Trim().ToUpperInvariant();
        if (codigo.Length < 3)
            return SinNcf;

        return codigo[..3] switch
        {
            "B01" => "Factura de crédito fiscal",
            "B02" => "Factura de consumo",
            "B03" => "Nota de débito",
            "B04" => "Nota de crédito",
            "B11" => "Comprobante de compras",
            "B14" => "Factura de régimen especial",
            "B15" => "Comprobante gubernamental",
            // Un prefijo que no se conoce NO se traduce a la fuerza: decir el
            // tipo equivocado es peor que no decirlo.
            _ => SinNcf
        };
    }
}
