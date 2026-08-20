using System.Globalization;
using System.Windows.Data;

namespace MED100.Common.Converters;

/// <summary>
/// True si el valor es igual al parámetro. Uso: marcar el ítem activo del
/// sidebar comparando la página actual con la página del botón.
/// </summary>
public class IgualdadConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
