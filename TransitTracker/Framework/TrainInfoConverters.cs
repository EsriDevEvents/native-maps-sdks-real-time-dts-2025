using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Esri.ArcGISRuntime.RealTime;

namespace TransitTracker.Framework;

public class TrainIconConverter : MarkupExtension, IValueConverter
{
    private static TrainIconConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new TrainIconConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TransitVehicle train)
            return DependencyProperty.UnsetValue;

        var route = App.MainViewModel.TransitRoutes.FirstOrDefault(rte => rte.RouteId.Equals(train.VehicleEntity.Attributes["RouteId"]));
        return route?.Icon ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public class TrainStopSequenceConverter : MarkupExtension, IValueConverter
{
    private static TrainStopSequenceConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new TrainStopSequenceConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DynamicEntity train)
            return DependencyProperty.UnsetValue;

        var stopSequence = train.Attributes.GetIntAttribute("StopSequence");
        var numberOfStops = train.Attributes.GetIntAttribute("NumberOfStops");
        return $"Stop {stopSequence} of {numberOfStops}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public class StationNameConverter : MarkupExtension, IValueConverter
{
    private static StationNameConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new StationNameConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string stationName)
            return DependencyProperty.UnsetValue;

        return stationName.Replace("Metrorail Station", "", StringComparison.InvariantCultureIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public class TrainDelayConverter : MarkupExtension, IValueConverter
{
    private static TrainDelayConverter? _converter;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return _converter ??= new TrainDelayConverter();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int delay)
            return DependencyProperty.UnsetValue;

        return (delay <= 0) ? "On Schedule" : $"Delayed: {delay} min";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}