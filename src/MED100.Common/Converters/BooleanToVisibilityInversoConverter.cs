using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MED100.Common.Converters;

/// <summary>
/// Muestra el elemento cuando el bool es FALSE. Es el complemento del
/// BooleanToVisibilityConverter de WPF y evita tener que duplicar una
/// propiedad negada en el ViewModel solo para poder ocultar algo.
/// </summary>
public class BooleanToVisibilityInversoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}
