using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Las tres cuentas propias de la clínica: ITBIS por exención de línea,
/// reparto del honorario del médico y reparto entre seguro y paciente.
///
/// Todo es cálculo puro y sin base de datos, para poder probarlo con números
/// exactos. Las tres mueven plata y las tres se guardan en la factura al
/// emitirla, así que un error acá no se descubre hasta que alguien reclama.
/// </summary>
public static class CalculosClinica
{
    /// <summary>
    /// Totales con ITBIS <b>solo sobre la parte gravada</b>.
    ///
    /// Es LA diferencia con el POS-500, que le cobra 18% a todo. En RD los
    /// servicios de salud están exentos: la consulta no paga ITBIS y la gasa sí.
    /// El impuesto se calcula sobre la SUMA de lo gravado, no línea por línea,
    /// para no acumular redondeos (misma regla que docs/ITBIS.md).
    /// </summary>
    public static VentaTotales CalcularTotales(
        IReadOnlyList<VentaLinea> lineas, decimal itbisTasa, ModoRedondeo redondeo)
    {
        var subtotal = lineas.Sum(l => l.Subtotal);
        var baseGravada = lineas.Where(l => !l.Exento).Sum(l => l.Subtotal);
        // La rebaja NO se descuenta acá: ya está adentro del precio de cada
        // línea. Se suma solo para poder IMPRIMIRLA. Hacerlo al revés —cobrar
        // el de lista y restar un descuento global al final— obligaría a
        // repartir esa resta entre lo exento y lo gravado para saber sobre qué
        // se calcula el ITBIS, y esa repartición es una decisión que nadie tomó.
        var descuento = lineas.Sum(l => l.Descuento);

        var itbis = Math.Round(baseGravada * itbisTasa / 100m, 2, MidpointRounding.AwayFromZero);
        var total = subtotal + itbis;

        total = redondeo switch
        {
            ModoRedondeo.Peso => Math.Round(total, 0, MidpointRounding.AwayFromZero),
            ModoRedondeo.Arriba => Math.Ceiling(total),
            _ => total   // centavo: subtotal e itbis ya están a 2 decimales
        };

        return new VentaTotales(subtotal, itbisTasa, itbis, total, baseGravada, descuento);
    }

    /// <summary>
    /// Honorario del médico sobre los PROCEDIMIENTOS de la factura.
    ///
    /// La base son solo los procedimientos a propósito: al médico no le toca
    /// porcentaje de la gasa ni del suero, que son mercancía que la clínica
    /// compró. Si la factura es de puros insumos, el honorario es cero aunque
    /// haya médico asignado.
    ///
    /// El porcentaje entra por parámetro (ya leído del médico) y de acá sale
    /// para copiarse a la factura: nunca se vuelve a consultar el catálogo para
    /// mostrar una factura vieja (CLAUDE.md §1.3.2).
    /// </summary>
    public static HonorarioMedico CalcularHonorario(
        IReadOnlyList<VentaLinea> lineas, long? medicoId, string? medicoNombre, decimal porcentaje)
    {
        var baseHonorario = lineas.Where(l => l.EsProcedimiento).Sum(l => l.Subtotal);
        var monto = Math.Round(baseHonorario * porcentaje / 100m, 2, MidpointRounding.AwayFromZero);
        return new HonorarioMedico(medicoId, medicoNombre, porcentaje, baseHonorario, monto);
    }

    /// <summary>
    /// Reparto entre el seguro y el paciente.
    ///
    /// <c>Cubierto + PacientePaga = Total</c>, siempre. Lo que cubre la ARS NO
    /// es un descuento: es plata que entra por otra vía y en otro momento, así
    /// que no puede sumarse a la caja del día (CLAUDE.md §1.3.3).
    ///
    /// El monto cubierto se recorta al total: si alguien teclea 5000 en una
    /// factura de 1500, el paciente pagaría −3500 y el cuadre saldría negativo.
    /// </summary>
    public static RepartoArs CalcularReparto(decimal total, long? arsId, string? arsNombre,
        string? autorizacion, decimal cubierto)
    {
        if (arsId is null)
            return new RepartoArs(null, null, null, 0m, total);

        if (cubierto < 0m)
            throw new ArgumentException("Lo que cubre el seguro no puede ser negativo.");

        var cubiertoReal = Math.Min(Math.Round(cubierto, 2, MidpointRounding.AwayFromZero), total);
        return new RepartoArs(arsId, arsNombre, autorizacion, cubiertoReal, total - cubiertoReal);
    }
}
