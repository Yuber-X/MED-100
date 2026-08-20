namespace MED100.Common;

/// <summary>
/// Contacto de soporte del desarrollador (pedido del cliente 2026-07-29:
/// "un btn de ayuda con mi numero para contactar cualquier inconveniente").
///
/// Vive en Common porque lo usan el launcher (Views), el shell (App) y la
/// ventana de ayuda. Un solo lugar para cambiarlo el día que cambie el número.
/// </summary>
public static class Soporte
{
    public const string Desarrollador = "Yuber Santana";

    /// <summary>Número como se lee en RD.</summary>
    public const string Telefono = "849-438-0242";

    /// <summary>El mismo número en formato internacional (para wa.me y tel:).</summary>
    public const string TelefonoInternacional = "18494380242";

    public static string UrlWhatsApp => $"https://wa.me/{TelefonoInternacional}";
    public static string UrlLlamada => $"tel:+{TelefonoInternacional}";

    /// <summary>Qué conviene contar al escribir, para no perder viajes de ida y vuelta.</summary>
    public static readonly IReadOnlyList<string> QueContar =
    [
        "En qué pantalla estabas y qué botón apretaste.",
        "Qué esperabas que pasara y qué pasó en su lugar.",
        "Una foto o captura de la pantalla, si se puede."
    ];
}
