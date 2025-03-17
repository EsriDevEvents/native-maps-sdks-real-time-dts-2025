using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace TransitTracker.Framework;

public class NullToVisibilityConverter : MarkupExtension, IValueConverter
{
    private static NullToVisibilityConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new NullToVisibilityConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = false;
        if (parameter != null)
            invert = string.Equals(parameter.ToString(), "invert", StringComparison.OrdinalIgnoreCase);

        var isVisible = value is not null;
        if (invert)
            isVisible = !isVisible;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
{
    private static BoolToVisibilityConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new BoolToVisibilityConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var invert = false;
        if (parameter != null)
            invert = string.Equals(parameter.ToString(), "invert", StringComparison.OrdinalIgnoreCase);

        var isVisible = value is bool bVal && bVal == true;
        if (invert)
            isVisible = !isVisible;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
