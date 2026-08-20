using MED100.Common;
using MED100.Data;
using MED100.Models;

namespace MED100.Services;

/// <summary>Atajos de rango para el reporte por fechas.</summary>
public enum RangoReporte
{
    Hoy,
    Ayer,
    EstaSemana,
    EsteMes,
    MesPasado,
    Personalizado
}

/// <summary>
/// Analítica: Panel (permiso 'panel') y Reportes por fecha (permiso 'reportes').
/// Los cálculos de rango y variación son estáticos y puros: se testean sin BD.
/// </summary>
public class AnaliticaService
{
    private readonly AnaliticaRepository _analitica;
    private readonly AjustesLocales _ajustes;

    public AnaliticaService(AnaliticaRepository analitica, AjustesLocales ajustes)
    {
        _analitica = analitica;
        _ajustes = ajustes;
    }

    /// <summary>
    /// Datos del panel. Los umbrales de "por caducar" y "stock bajo" salen de
    /// los ajustes del equipo (Configuración), no están hardcodeados.
    /// </summary>
    public Task<DashboardDatos> ObtenerDashboardAsync(CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("panel"))
            throw new InvalidOperationException("No tienes permiso para ver el panel.");

        return _analitica.ObtenerDashboardAsync(
            FechaNegocio.Hoy, _ajustes.AvisoCaducidadDias, _ajustes.AvisoStockBajoUmbral, ct);
    }

    public Task<ReporteVentas> ObtenerReporteAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        if (!SesionActual.TienePermiso("reportes"))
            throw new InvalidOperationException("No tienes permiso para ver los reportes.");
        if (hasta < desde)
            throw new ArgumentException("La fecha final no puede ser anterior a la inicial.");

        return _analitica.ObtenerReporteAsync(desde, hasta, ct);
    }

    /// <summary>
    /// Rango de días de negocio de cada atajo. La semana empieza el LUNES
    /// (convención dominicana en comercio).
    /// </summary>
    public static (DateOnly Desde, DateOnly Hasta) CalcularRango(RangoReporte rango, DateOnly hoy) => rango switch
    {
        RangoReporte.Hoy => (hoy, hoy),
        RangoReporte.Ayer => (hoy.AddDays(-1), hoy.AddDays(-1)),
        RangoReporte.EstaSemana => (InicioSemana(hoy), hoy),
        RangoReporte.EsteMes => (new DateOnly(hoy.Year, hoy.Month, 1), hoy),
        RangoReporte.MesPasado => MesPasado(hoy),
        _ => (hoy, hoy)
    };

    private static DateOnly InicioSemana(DateOnly hoy)
    {
        var diasDesdeLunes = ((int)hoy.DayOfWeek + 6) % 7;   // domingo = 6
        return hoy.AddDays(-diasDesdeLunes);
    }

    private static (DateOnly, DateOnly) MesPasado(DateOnly hoy)
    {
        var inicioEsteMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var inicio = inicioEsteMes.AddMonths(-1);
        return (inicio, inicioEsteMes.AddDays(-1));
    }

    /// <summary>
    /// Variación porcentual contra el período anterior (KPI del panel).
    /// Devuelve el texto listo para mostrar y si la tendencia es positiva.
    /// </summary>
    public static (string Texto, bool Positivo) CalcularVariacion(decimal actual, decimal anterior)
    {
        if (anterior == 0m)
            return actual > 0m
                ? ("Sin ventas el mes anterior", true)
                : ("Sin datos del mes anterior", true);

        var porcentaje = (actual - anterior) / anterior * 100m;
        var signo = porcentaje >= 0m ? "+" : "";
        return ($"{signo}{porcentaje:0.#}% vs. mes anterior", porcentaje >= 0m);
    }
}
