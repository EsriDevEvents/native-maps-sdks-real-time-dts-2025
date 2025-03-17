using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace TransitTracker.Framework;

public class UnixDateTimeConverter : MarkupExtension, IValueConverter
{
    private static UnixDateTimeConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new UnixDateTimeConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long unixTime)
            return DependencyProperty.UnsetValue;
        return DateTimeOffset.FromUnixTimeSeconds(unixTime).ToLocalTime().ToString("MM/dd/yyyy hh:mm:ss tt");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
