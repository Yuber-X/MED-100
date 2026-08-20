using System.Globalization;
using System.Windows.Data;

namespace MED100.Common.Converters;

/// <summary>
/// Formateo de fechas unificado en español dominicano.
/// Default: DD/MM/YYYY (inputs). Con parameter="compacta": DD-MMM-YY (tablas).
/// Acepta DateOnly y DateTime (los DateTime UTC se convierten a hora local RD).
/// </summary>
public class DateConverter : IValueConverter
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");
    private static readonly TimeZoneInfo ZonaRd = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fecha = value switch
        {
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTime dt when dt.Kind == DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(dt, ZonaRd),
            DateTime dt => dt,
            _ => (DateTime?)null
        };

        if (fecha is null)
            return string.Empty;

        var compacta = string.Equals(parameter as string, "compacta", StringComparison.OrdinalIgnoreCase);
        return compacta
            ? fecha.Value.ToString("dd-MMM-yy", CulturaDo).Replace(".", "")
            : fecha.Value.ToString("dd/MM/yyyy", CulturaDo);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string texto &&
            DateTime.TryParseExact(texto, "dd/MM/yyyy", CulturaDo, DateTimeStyles.None, out var fecha))
            return DateOnly.FromDateTime(fecha);
        return null;
    }
}
