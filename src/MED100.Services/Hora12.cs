using System.Globalization;

namespace MED100.Services;

/// <summary>
/// Horas en formato de 12 con AM/PM, que es como se dicen en República
/// Dominicana (pedido de Yuber, 2026-08-14: "en RD no usamos la hora de 24hrs").
///
/// La pantalla captura la hora en dos pedazos —el texto "8:30" y un combo con
/// AM o PM— y esto los une en el <see cref="TimeOnly"/> que entiende el resto
/// del sistema. La base y la lógica siguen trabajando en 24 horas: el formato
/// de 12 es de la interfaz para afuera, no del dominio.
///
/// La trampa que resuelve: <b>las 12</b>. Las 12 AM son las 00:xx (medianoche) y
/// las 12 PM son las 12:xx (mediodía) — no es "sumar 12 si es PM". Un médico
/// cargado con entrada "12:00 PM" tiene que atender al mediodía, no a medianoche.
/// </summary>
public static class Hora12
{
    /// <summary>Las dos opciones del combo, en el orden en que se leen.</summary>
    public static readonly IReadOnlyList<string> Meridianos = ["AM", "PM"];

    private static readonly CultureInfo CulturaRd = CultureInfo.GetCultureInfo("es-DO");

    /// <summary>
    /// Une el texto de la hora con el meridiano. Acepta "8", "8:30" y "08:30".
    /// Devuelve false si no se entiende: el llamador decide qué decirle al usuario.
    /// </summary>
    public static bool TryParsear(string? hora, string? meridiano, out TimeOnly resultado)
    {
        resultado = default;

        if (string.IsNullOrWhiteSpace(hora))
            return false;

        var limpio = hora.Trim().Replace('.', ':');
        var partes = limpio.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length is 0 or > 2)
            return false;

        if (!int.TryParse(partes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h))
            return false;

        var m = 0;
        if (partes.Length == 2 &&
            !int.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m))
            return false;

        if (h is < 1 or > 12 || m is < 0 or > 59)
            return false;

        if (!EsPm(meridiano, out var pm))
            return false;

        // Las 12 son el caso especial: 12 AM = 00, 12 PM = 12.
        var hora24 = h == 12 ? (pm ? 12 : 0) : (pm ? h + 12 : h);
        resultado = new TimeOnly(hora24, m);
        return true;
    }

    /// <summary>Parte un <see cref="TimeOnly"/> en lo que muestran los dos controles.</summary>
    public static (string Hora, string Meridiano) Formatear(TimeOnly hora)
    {
        var h = hora.Hour % 12;
        if (h == 0)
            h = 12;
        return ($"{h}:{hora.Minute:00}", hora.Hour < 12 ? "AM" : "PM");
    }

    /// <summary>Cómo se lee de corrido: "8:00 am".</summary>
    public static string Texto(TimeOnly hora) =>
        hora.ToString("h:mm tt", CulturaRd).ToLower(CulturaRd);

    /// <summary>
    /// Acepta AM/PM escrito de las formas en que la gente lo escribe: "am",
    /// "a.m.", "A. M.". Cualquier otra cosa se rechaza en vez de asumir AM —
    /// asumir dejaría a un médico entrando a medianoche sin que nadie lo note.
    /// </summary>
    private static bool EsPm(string? meridiano, out bool pm)
    {
        pm = false;
        if (string.IsNullOrWhiteSpace(meridiano))
            return false;

        var limpio = new string(meridiano.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        switch (limpio)
        {
            case "AM":
                pm = false;
                return true;
            case "PM":
                pm = true;
                return true;
            default:
                return false;
        }
    }
}
