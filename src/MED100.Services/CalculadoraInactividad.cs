namespace MED100.Services;

/// <summary>
/// Cuándo un paciente "dejó de venir".
///
/// Pedido del cliente (2026-08-25): <i>"quiero que de si se puede una especie
/// de alerta cuando un paciente dure más de 6 meses sin ser atendido"</i>.
///
/// Vive acá y no en el ViewModel porque es una REGLA DE NEGOCIO —a quién llama
/// la clínica— y no una decisión de presentación. Es pura y sin dependencias,
/// igual que <see cref="CalculadoraCaducidad"/>: se le pasa la fecha y el
/// corte, y contesta. Así se puede probar sin levantar WPF ni la base.
/// </summary>
public static class CalculadoraInactividad
{
    /// <summary>
    /// True si hace más de <paramref name="mesesCorte"/> que no viene.
    ///
    /// El que NUNCA vino (<paramref name="ultimaVisitaUtc"/> nulo) NO cuenta
    /// como inactivo: es un paciente registrado que todavía no tuvo su primera
    /// cita, no uno que se perdió. Mezclarlos llenaría la lista de gente a la
    /// que no hay por qué llamar, y la lista dejaría de servir.
    /// </summary>
    public static bool DejoDeVenir(DateTime? ultimaVisitaUtc, DateTime ahoraUtc, int mesesCorte)
    {
        if (ultimaVisitaUtc is not { } visita)
            return false;
        if (mesesCorte <= 0)
            return false;   // un corte de cero marcaría a todo el mundo
        return visita < ahoraUtc.AddMonths(-mesesCorte);
    }

    /// <summary>
    /// Meses COMPLETOS que lleva sin venir. Cero si nunca vino o si vino hoy.
    /// Se cuenta por calendario y no dividiendo días entre 30: con la división,
    /// "6 meses" caía a veces en 5 y el texto contradecía a la marca.
    /// </summary>
    public static int MesesSinVenir(DateTime? ultimaVisitaUtc, DateTime ahoraUtc)
    {
        if (ultimaVisitaUtc is not { } visita || visita >= ahoraUtc)
            return 0;
        var meses = (ahoraUtc.Year - visita.Year) * 12 + ahoraUtc.Month - visita.Month;
        if (ahoraUtc.Day < visita.Day)
            meses--;
        return Math.Max(0, meses);
    }

    /// <summary>Cuánto hace que no viene, para mostrar. Vacío si no dejó de venir.</summary>
    public static string Describir(DateTime? ultimaVisitaUtc, DateTime ahoraUtc, int mesesCorte)
    {
        if (!DejoDeVenir(ultimaVisitaUtc, ahoraUtc, mesesCorte))
            return string.Empty;

        var meses = MesesSinVenir(ultimaVisitaUtc, ahoraUtc);
        if (meses < 12)
            return $"{meses} meses sin venir";

        var años = meses / 12;
        var resto = meses % 12;
        return resto == 0
            ? $"{años} año{(años == 1 ? "" : "s")} sin venir"
            : $"{años} año{(años == 1 ? "" : "s")} y {resto} mes{(resto == 1 ? "" : "es")} sin venir";
    }
}
