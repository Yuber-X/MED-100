namespace MED100.Common;

/// <summary>
/// Fecha de negocio en República Dominicana (America/Santo_Domingo, UTC-4 sin DST).
/// La BD guarda UTC; los vencimientos y el semáforo se evalúan con esta fecha local.
/// </summary>
public static class FechaNegocio
{
    private static readonly TimeZoneInfo ZonaRd = ObtenerZona();

    public static DateOnly Hoy => DateOnly.FromDateTime(AhoraLocal());

    public static DateTime AhoraLocal() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZonaRd);

    public static DateTime AUtcLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaRd);

    /// <summary>
    /// Camino inverso: una hora que el usuario escribió en pantalla ("la cita
    /// es a las 3:00") pasada a UTC para guardarla.
    ///
    /// RD no tiene horario de verano, así que la conversión es siempre exacta:
    /// no hay horas que no existan ni horas que ocurran dos veces, que es lo
    /// que hace ambigua esta cuenta en otros países.
    /// </summary>
    public static DateTime ALocalUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ZonaRd);

    private static TimeZoneInfo ObtenerZona()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");
        }
        catch (TimeZoneNotFoundException)
        {
            // Id equivalente en Windows sin datos ICU
            return TimeZoneInfo.FindSystemTimeZoneById("SA Western Standard Time");
        }
    }
}
