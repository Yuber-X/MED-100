using System.Globalization;
using System.Windows.Data;

namespace MED100.Common.Converters;

/// <summary>
/// Formateo de moneda unificado para todo XAML: RD$ 15,000.00.
/// Negativos: -RD$ 500.00 (el color rojo lo aplica el estilo, no el converter).
/// </summary>
public class MoneyConverter : IValueConverter
{
    private static readonly CultureInfo CulturaDo = CultureInfo.GetCultureInfo("es-DO");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal monto)
            return string.Empty;

        return monto < 0
            ? $"-RD$ {Math.Abs(monto).ToString("N2", CulturaDo)}"
            : $"RD$ {monto.ToString("N2", CulturaDo)}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string texto)
            return null;

        texto = texto.Replace("RD$", "", StringComparison.OrdinalIgnoreCase).Trim();
        return decimal.TryParse(texto, NumberStyles.Number, CulturaDo, out var monto) ? monto : null;
    }
}
