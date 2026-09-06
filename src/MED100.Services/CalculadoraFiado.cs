using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Semáforo de los fiados (012). Lógica pura: entra un saldo y una fecha de
/// compromiso, sale un color. Sin base de datos, para poder probarla entera.
///
/// Los umbrales son los mismos que FAControl usa para las cuotas, porque Yuber
/// pidió que se pareciera a eso:
///   Pagado (saldo 0) · Al día (falta más de 7 días o sin fecha)
///   · Por vencer (≤ 7 días) · Vencido (1–15 días de atraso) · En mora (&gt; 15)
///
/// <b>Sin fecha de compromiso NO es mora.</b> Es la decisión de fondo de este
/// archivo: la clínica puede fiar sin fijar día, y tratar eso como atrasado
/// pintaría de rojo a alguien que nunca prometió una fecha. Aparece como "al
/// día" y con saldo, que es exactamente lo que pasa.
/// </summary>
public static class CalculadoraFiado
{
    /// <summary>Después de este atraso deja de ser un descuido y hay que llamar.</summary>
    public const int DiasParaMora = 15;

    /// <summary>Ventana en la que ya conviene recordarle al paciente.</summary>
    public const int DiasParaAvisar = 7;

    public static SemaforoFiado Calcular(decimal saldo, DateOnly? fechaCompromiso, DateOnly hoy)
    {
        // El saldo manda sobre la fecha: una deuda saldada tarde sigue saldada.
        if (saldo <= 0m)
            return SemaforoFiado.Pagado;

        if (fechaCompromiso is not { } fecha)
            return SemaforoFiado.AlDia;

        var dias = fecha.DayNumber - hoy.DayNumber;

        return dias switch
        {
            > DiasParaAvisar => SemaforoFiado.AlDia,
            // Incluye el día mismo del compromiso: hasta que no termina, no
            // está atrasado.
            >= 0 => SemaforoFiado.PorVencer,
            >= -DiasParaMora => SemaforoFiado.Vencido,
            _ => SemaforoFiado.EnMora
        };
    }

    /// <summary>
    /// Días de atraso. 0 si todavía no venció o no hay fecha; nunca negativo,
    /// para poder escribir "atrasado N días" sin restar al revés.
    /// </summary>
    public static int DiasDeAtraso(DateOnly? fechaCompromiso, DateOnly hoy) =>
        fechaCompromiso is { } fecha ? Math.Max(0, hoy.DayNumber - fecha.DayNumber) : 0;

    /// <summary>Días que faltan. Negativo si ya se pasó; null si no hay fecha.</summary>
    public static int? DiasRestantes(DateOnly? fechaCompromiso, DateOnly hoy) =>
        fechaCompromiso is { } fecha ? fecha.DayNumber - hoy.DayNumber : null;

    /// <summary>
    /// Cómo se lee la fecha en pantalla y en el correo del aviso. Vive acá y no
    /// en la vista para que el aviso automático y la lista digan exactamente lo
    /// mismo — si se separan, terminan contradiciéndose.
    /// </summary>
    public static string DescribirVencimiento(DateOnly? fechaCompromiso, DateOnly hoy)
    {
        if (fechaCompromiso is not { } fecha)
            return "sin fecha acordada";

        var dias = fecha.DayNumber - hoy.DayNumber;
        return dias switch
        {
            0 => "vence HOY",
            1 => "vence mañana",
            > 1 => $"vence en {dias} días",
            -1 => "venció ayer",
            _ => $"atrasado {-dias} días"
        };
    }
}
