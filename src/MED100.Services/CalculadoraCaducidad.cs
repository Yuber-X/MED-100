using MED100.Models;

namespace MED100.Services;

/// <summary>
/// Semáforo de caducidad. Regla heredada del POS-400 (FormCaducidad, por MESES
/// completos restantes, TIMESTAMPDIFF(MONTH)) mapeada de 6 colores a los 4 de
/// la spec — decisión corregible con el cliente (BLOCKERS #3):
///   Verde ≥ 7 meses · Amarillo 4–6 · Naranja 2–3 · Rojo ≤ 1 (incluye caducado)
/// </summary>
public static class CalculadoraCaducidad
{
    public static SemaforoCaducidad Calcular(DateOnly fechaCaducidad, DateOnly hoy)
    {
        var meses = MesesCompletosRestantes(fechaCaducidad, hoy);
        return meses switch
        {
            >= 7 => SemaforoCaducidad.Verde,
            >= 4 => SemaforoCaducidad.Amarillo,
            >= 2 => SemaforoCaducidad.Naranja,
            _ => SemaforoCaducidad.Rojo
        };
    }

    /// <summary>
    /// Meses COMPLETOS entre hoy y la fecha (semántica TIMESTAMPDIFF de MySQL).
    /// Negativo si ya caducó.
    /// </summary>
    public static int MesesCompletosRestantes(DateOnly fechaCaducidad, DateOnly hoy)
    {
        var meses = (fechaCaducidad.Year - hoy.Year) * 12 + fechaCaducidad.Month - hoy.Month;
        if (fechaCaducidad >= hoy && fechaCaducidad.Day < hoy.Day) meses--;
        if (fechaCaducidad < hoy && fechaCaducidad.Day > hoy.Day) meses++;
        return meses;
    }

    public static int DiasRestantes(DateOnly fechaCaducidad, DateOnly hoy) =>
        fechaCaducidad.DayNumber - hoy.DayNumber;
}
