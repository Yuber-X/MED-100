using MED100.Common;

namespace MED100.Services;

/// <summary>
/// Edad del paciente a partir de la fecha de nacimiento.
///
/// Por qué existe en vez de un `(hoy - nacimiento).Days / 365`: en una clínica
/// la edad de los chiquitos se dice en meses y la de los recién nacidos en
/// días. "0 años" no le sirve a nadie en la recepción.
///
/// La fecha de nacimiento es un DATE puro (sin hora ni zona), así que se
/// compara contra la fecha de negocio de RD, no contra UTC. Un paciente
/// cumple años el día que lo dice el calendario dominicano.
/// </summary>
public static class EdadPaciente
{
    /// <summary>
    /// Edad en años cumplidos. null si no hay fecha o si la fecha es futura
    /// (dato mal cargado: mejor no mostrar nada que mostrar una edad negativa).
    /// </summary>
    public static int? EnAnios(DateOnly? nacimiento, DateOnly? hoy = null)
    {
        if (nacimiento is not { } n)
            return null;
        var referencia = hoy ?? FechaNegocio.Hoy;
        if (n > referencia)
            return null;

        var anios = referencia.Year - n.Year;
        // Todavía no llegó el cumpleaños de este año. AddYears manda el 29 de
        // febrero al 28 en los años que no son bisiestos, que es lo que se usa.
        if (referencia < n.AddYears(anios))
            anios--;
        return anios;
    }

    /// <summary>
    /// Edad lista para mostrar: "34 años", "7 meses", "12 días".
    /// Devuelve "—" si no hay fecha o si es futura.
    /// </summary>
    public static string Texto(DateOnly? nacimiento, DateOnly? hoy = null)
    {
        if (nacimiento is not { } n)
            return "—";
        var referencia = hoy ?? FechaNegocio.Hoy;
        if (n > referencia)
            return "—";

        var anios = EnAnios(n, referencia)!.Value;
        if (anios >= 1)
            return anios == 1 ? "1 año" : $"{anios} años";

        var meses = MesesCumplidos(n, referencia);
        if (meses >= 1)
            return meses == 1 ? "1 mes" : $"{meses} meses";

        var dias = referencia.DayNumber - n.DayNumber;
        return dias switch
        {
            0 => "Recién nacido",
            1 => "1 día",
            _ => $"{dias} días"
        };
    }

    private static int MesesCumplidos(DateOnly nacimiento, DateOnly hoy)
    {
        var meses = ((hoy.Year - nacimiento.Year) * 12) + hoy.Month - nacimiento.Month;
        // El día del mes todavía no llegó: el mes no está cumplido.
        if (hoy.Day < nacimiento.Day)
            meses--;
        return meses;
    }
}
