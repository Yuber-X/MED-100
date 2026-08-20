using System.Globalization;
using System.Text;

namespace MED100.Services;

/// <summary>
/// El código de dos letras que identifica al médico en la sala de espera
/// (pedido de Yuber, 2026-08-14).
///
/// La regla que dio: <b>primera letra del nombre + última letra del apellido</b>.
/// "Yuber Santana Lizardo" → <c>YO</c>, y en la sala los turnos de ese médico
/// se ven como YO-1, YO-2, YO-3. Sirve para que la recepción distinga de un
/// vistazo a quién le toca cada número sin leer el nombre completo.
///
/// Es cálculo puro: no toca la base. Quién resuelve los choques es
/// <see cref="MedicoService"/>, que es el que sabe qué códigos están tomados.
/// </summary>
public static class CodigoTurnoMedico
{
    /// <summary>Cuánto entra en la columna (y en el papelito de 80mm).</summary>
    public const int LargoMaximo = 8;

    /// <summary>
    /// Propone el código a partir del nombre completo. Devuelve null si del
    /// nombre no sale ni una letra (un nombre que es puro número o símbolo).
    /// </summary>
    public static string? Proponer(string? nombreCompleto)
    {
        if (string.IsNullOrWhiteSpace(nombreCompleto))
            return null;

        var palabras = SinAcentos(nombreCompleto)
            .Split([' ', '.', ',', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new string(p.Where(char.IsLetter).ToArray()))
            .Where(p => p.Length > 0)
            .ToList();

        if (palabras.Count == 0)
            return null;

        var primera = palabras[0][0];
        // Con una sola palabra se usa esa misma para las dos letras: es mejor
        // "ID" para Ingrid que dejar al médico sin código.
        var ultima = palabras[^1][^1];

        return $"{char.ToUpperInvariant(primera)}{char.ToUpperInvariant(ultima)}";
    }

    /// <summary>
    /// Deja el código como se guarda: sin espacios, sin acentos, en mayúsculas
    /// y recortado. Devuelve null si queda vacío — un código en blanco y un
    /// código ausente son la misma cosa, y así el índice único deja convivir
    /// a varios médicos sin código.
    /// </summary>
    public static string? Normalizar(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        var limpio = new string(SinAcentos(codigo)
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray())
            .ToUpperInvariant();

        if (limpio.Length == 0)
            return null;

        return limpio.Length > LargoMaximo ? limpio[..LargoMaximo] : limpio;
    }

    /// <summary>
    /// Variante número <paramref name="intento"/> del código, para cuando el
    /// propuesto ya lo tiene otro médico: YO → YO2 → YO3…
    /// El intento 1 es el código pelado.
    /// </summary>
    public static string Variante(string baseCodigo, int intento)
    {
        if (intento <= 1)
            return baseCodigo;

        var sufijo = intento.ToString(CultureInfo.InvariantCulture);
        var raiz = baseCodigo.Length + sufijo.Length > LargoMaximo
            ? baseCodigo[..(LargoMaximo - sufijo.Length)]
            : baseCodigo;
        return raiz + sufijo;
    }

    /// <summary>
    /// Quita tildes y la virgulilla de la ñ. El código termina impreso en la
    /// tira térmica de 80mm, y esas impresoras no siempre tienen el juego de
    /// caracteres completo: una "Ñ" puede salir como basura.
    /// </summary>
    private static string SinAcentos(string texto)
    {
        var descompuesto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
