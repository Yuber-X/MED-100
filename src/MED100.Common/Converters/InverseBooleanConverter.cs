using System.Globalization;
using System.Windows.Data;

namespace MED100.Common.Converters;

/// <summary>Invierte un bool en bindings (ej. IsEnabled = !Ocupado).</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}
