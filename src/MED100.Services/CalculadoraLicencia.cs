using MED100.Models;

namespace MED100.Services;

/// <summary>Lo que quedó guardado, venga de la base o del ancla.</summary>
/// <param name="InicioUtc">Primera vez que se abrió la app en este equipo.</param>
/// <param name="Activada">Si ya se escribió la llave del producto.</param>
/// <param name="UltimaAperturaUtc">El arranque anterior, para detectar el reloj movido.</param>
public record LicenciaGuardada(DateTime InicioUtc, bool Activada, DateTime UltimaAperturaUtc);

/// <summary>En qué situación está la licencia y cuántos días de demo quedan.</summary>
public record ResultadoLicencia(EstadoLicencia Estado, int DiasRestantes)
{
    public bool PermiteEntrar => Estado is EstadoLicencia.Completa or EstadoLicencia.Demo;

    /// <summary>Texto corto para la pastilla del menú. Vacío si está activada.</summary>
    public string Etiqueta => Estado switch
    {
        EstadoLicencia.Demo when DiasRestantes == 1 => "DEMO · último día",
        EstadoLicencia.Demo => $"DEMO · {DiasRestantes} días",
        EstadoLicencia.Vencida => "DEMO VENCIDO",
        EstadoLicencia.RelojAtrasado => "FECHA ALTERADA",
        _ => string.Empty
    };
}

/// <summary>
/// La regla del demo, sin base de datos ni reloj propio: se le pasa lo
/// guardado y qué hora es, y dice qué hacer. Pura a propósito — es la única
/// forma de probar "el día 15" sin esperar dos semanas.
/// </summary>
public static class CalculadoraLicencia
{
    /// <summary>Días de prueba. Decisión de Yuber, 2026-08-18.</summary>
    public const int DiasDemo = 15;

    /// <summary>
    /// Cuánto se le perdona al reloj antes de gritar. Un día completo cubre el
    /// caso legítimo (la PC arranca con la hora de fábrica y Windows la
    /// corrige a los minutos) sin regalar un demo entero.
    /// </summary>
    public static readonly TimeSpan ToleranciaReloj = TimeSpan.FromHours(24);

    public static ResultadoLicencia Evaluar(LicenciaGuardada guardada, DateTime ahoraUtc)
    {
        // Activada gana sobre todo lo demás: ni el reloj ni la fecha de
        // instalación importan si el cliente ya pagó. Una PC con la hora mal
        // puesta no puede dejar sin trabajar a una clínica que compró.
        if (guardada.Activada)
            return new ResultadoLicencia(EstadoLicencia.Completa, 0);

        if (ahoraUtc < guardada.UltimaAperturaUtc - ToleranciaReloj)
            return new ResultadoLicencia(EstadoLicencia.RelojAtrasado, 0);

        // Se cuenta por días enteros desde el primer arranque: si instaló hoy
        // a las 3 de la tarde, el día 15 se le acaba a las 3 de la tarde. Es
        // más fácil de explicar por teléfono que "a la medianoche del 15".
        var transcurridos = (ahoraUtc - guardada.InicioUtc).TotalDays;

        // Un inicio en el futuro (reloj adelantado al instalar y corregido
        // después) daría días negativos: se toma como recién instalada.
        if (transcurridos < 0)
            transcurridos = 0;

        var restantes = DiasDemo - (int)Math.Floor(transcurridos);
        return restantes <= 0
            ? new ResultadoLicencia(EstadoLicencia.Vencida, 0)
            : new ResultadoLicencia(EstadoLicencia.Demo, restantes);
    }

    /// <summary>
    /// De las dos copias de la fecha de instalación (la base y el ancla) manda
    /// la MÁS VIEJA: borrar una de las dos no reinicia el demo.
    /// </summary>
    public static DateTime InicioReal(DateTime enLaBase, DateTime? enElAncla) =>
        enElAncla is { } ancla && ancla < enLaBase ? ancla : enLaBase;
}
