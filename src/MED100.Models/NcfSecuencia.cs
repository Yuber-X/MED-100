using System.Text.RegularExpressions;

namespace MED100.Models;

/// <summary>
/// Secuencia de comprobantes fiscales autorizada por la DGII (011_ncf_secuencia.sql).
/// Prefijo = serie + tipo (ej. "B02" tradicional, "E32" e-CF). La parte numérica
/// se rellena con ceros a la izquierda hasta <see cref="Largo"/>.
///
/// Portado de FAControl (012_ncf.sql) por pedido de Yuber (2026-09-06):
/// <i>"agreguemos también las opciones de NCF como en el PrestControl en cobro y
/// en configuración"</i>. Se le quitó la dimensión de "modo": FAControl lleva una
/// secuencia por estancia porque es una suite de tres rubros; la clínica es un
/// solo negocio con un solo libro de ventas.
///
/// <b>Un NCF consumido NUNCA se reusa</b>, aunque la factura se anule (regla DGII).
/// Por eso anular escribe estado='anulada' y jamás libera el comprobante.
/// </summary>
public class NcfSecuencia
{
    public int Id { get; set; }
    public string Prefijo { get; set; } = "B02";

    /// <summary>Dígitos de la secuencia: 8 para NCF tradicional, 10 para e-CF.</summary>
    public int Largo { get; set; } = 8;

    /// <summary>Próximo número a asignar.</summary>
    public long Proxima { get; set; } = 1;

    /// <summary>Último número autorizado (inclusive). NULL = sin tope conocido.</summary>
    public long? FinRango { get; set; }

    /// <summary>Vencimiento de la autorización. NULL = sin vencimiento conocido.</summary>
    public DateOnly? Vencimiento { get; set; }

    public bool Activo { get; set; } = true;

    /// <summary>NCF ya formateado para un número dado, ej. B02 + 00000012.</summary>
    public string Formatear(long numero) =>
        $"{Prefijo}{numero.ToString().PadLeft(Largo, '0')}";

    /// <summary>Cuántos números quedan disponibles (null = sin tope conocido).</summary>
    public long? Restantes => FinRango is { } fin ? Math.Max(0, fin - Proxima + 1) : null;

    public bool EstaVencida(DateOnly hoy) => Vencimiento is { } v && hoy > v;

    public bool EstaAgotada => FinRango is { } fin && Proxima > fin;

    /// <summary>
    /// Forma de un comprobante de la DGII: una letra de serie, dos dígitos de
    /// tipo y la secuencia (8 en el NCF tradicional, 10 en el e-CF). Se aceptan
    /// de 6 a 12 dígitos para no pelear con rangos raros de autorización.
    /// Los formatos anteriores a 2018 (serie A de 19 dígitos) quedan fuera a
    /// propósito: no se emiten más y admitirlos volvería ambiguo el prefijo.
    /// </summary>
    private static readonly Regex FormaNcf = new(@"^([A-Z]\d{2})(\d{6,12})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parte un NCF digitado a mano en prefijo + número + largo, para poder
    /// adoptarlo como secuencia predeterminada. Null si el texto no tiene forma
    /// de comprobante: en ese caso no se toca nada, porque adivinar la
    /// numeración de un libro de ventas es peor que no hacer nada.
    /// </summary>
    public static (string Prefijo, long Numero, int Largo)? Descomponer(string? ncf)
    {
        if (string.IsNullOrWhiteSpace(ncf))
            return null;
        var m = FormaNcf.Match(ncf.Trim().ToUpperInvariant());
        if (!m.Success)
            return null;
        var digitos = m.Groups[2].Value;
        return long.TryParse(digitos, out var numero)
            ? (m.Groups[1].Value, numero, digitos.Length)
            : null;
    }
}
