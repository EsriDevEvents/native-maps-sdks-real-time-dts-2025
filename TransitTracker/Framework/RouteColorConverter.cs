using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace TransitTracker.Framework;

public class RouteColorConverter : MarkupExtension, IValueConverter
{
    private static RouteColorConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new RouteColorConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int routeColor)
            return DependencyProperty.UnsetValue;
        var color = Color.FromArgb(255, (byte)((routeColor >> 16) & 0xFF), (byte)((routeColor >> 8) & 0xFF), (byte)((routeColor) & 0xFF));
        return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
