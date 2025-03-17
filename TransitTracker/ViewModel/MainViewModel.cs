using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using TransitTracker.Framework;

namespace TransitTracker.ViewModel;

public partial class MainViewModel : ObservableRecipient
{
    #region Fields

    private static readonly Envelope _extent = new(
        -8649465.94716581, 4679652.96714959,
        -8526006.8393619, 4746398.04730608,
        SpatialReferences.WebMercator);

    private const string _webmap = "https://arcgis.com/home/item.html?id=c1bddcc5242746d9a71982d338b1a150";

    private readonly StaticGtfsData _staticGtfsData = new(recreateStaticTransitData: false);

    #endregion

    public MainViewModel()
    {
        _ = InitializeAsync();
    }

    #region Properties
    public Map Map { get; } = new(new Uri(_webmap)) { InitialViewpoint = new Viewpoint(_extent) };

    private DynamicEntityLayer? _trainLayer;

    [ObservableProperty]
    private ObservableCollection<TransitRoute> _transitRoutes = [];

    [ObservableProperty]
    private bool _isPopupOpen;

    [ObservableProperty]
    private bool _showTrainList;

    [ObservableProperty]
    private bool _showStatistics;

    [ObservableProperty]
    private bool _showObservationStatistics;

    [ObservableProperty]
    private ObservableCollection<ObservationStatistics> _observationStatistics = [];

    public TransitVehicle? SelectedVehicle
    {
        get => _selectedVehicle;
        set
        {
            ClearSelectedTrain();
            SetProperty(ref _selectedVehicle, value);
            _ = SelectTrainAsync(_selectedVehicle);
        }
    }
    private TransitVehicle? _selectedVehicle;

    public MapViewController MapViewController { get; } = new();

    public TrainStatistics TrainStatistics { get; } = new();

    #endregion

    private async Task InitializeAsync()
    {
        const double minScale = 1_000_000d;

        try
        {
            await _staticGtfsData.InitializeAsync();

            var trainDictionaryStyle = await DictionarySymbolStyle.CreateFromFileAsync(@"Content\Transit.stylx");
            var routeLayer = await _staticGtfsData.InitializeRailRoutesAsync(TransitRoutes, trainDictionaryStyle);
            routeLayer.MinScale = minScale;
            Map.OperationalLayers.Add(routeLayer);

            var stopLayer = await _staticGtfsData.InitializeRailStopsAsync();
            stopLayer.MinScale = minScale;
            Map.OperationalLayers.Add(stopLayer);

            _trainLayer = await InitializeTrainLayerAsync(trainDictionaryStyle);
            _trainLayer.MinScale = minScale;
            Map.OperationalLayers.Add(_trainLayer);

            foreach (var layer in Map.OperationalLayers)
            {
                if (layer is DynamicEntityLayer deLayer)
                {
                    deLayer.DataSource.DynamicEntityObservationReceived += (s, e) =>
                    {
                        try
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                var stats = ObservationStatistics.FirstOrDefault(stat => stat.Name == deLayer.Name);
                                if (stats is null)
                                    ObservationStatistics.Add(new ObservationStatistics(deLayer.Name));
                                else
                                    stats.TotalObservations++;
                            });
                        }
                        catch(Exception) { }
                    };

                    deLayer.DataSource.DynamicEntityReceived += (s, e) =>
                    {
                        try
                        {
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                var stats = ObservationStatistics.FirstOrDefault(stat => stat.Name == deLayer.Name);
                                if (stats != null)
                                    stats.TotalEntities++;
                            });
                        }
                        catch(Exception) { }
                    };
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Initialization Error");
        }
    }

    private async Task<DynamicEntityLayer> InitializeTrainLayerAsync(DictionarySymbolStyle trainDictionaryStyle)
    {
        // create, load, and connect custom dynamic entity data source
        var trainSource = new TrainSource(_staticGtfsData);

        // load and connect can also be done automatically when associated layer is added to the map
        await trainSource.ConnectAsync();

        // subscribe to received events (for statistics gathering)
        trainSource.DynamicEntityReceived += TrainSource_DynamicEntityReceived;
        trainSource.DynamicEntityObservationReceived += TrainSource_DynamicEntityObservationReceived;

        // create the dynamic entity layer (with advanced symbology)
        // - the dictionary renderer script:
        //   - selects the symbol color based on the the color field from the train entity
        //   - rotates the symbol based on the bearing of the train entity
        return new DynamicEntityLayer(trainSource) { Name = "WMATA Trains", Renderer = new DictionaryRenderer(trainDictionaryStyle) };
    }

    // Event handler for when a brand new dynamic entity is received
    private void TrainSource_DynamicEntityReceived(object? sender, DynamicEntityEventArgs e)
    {
        try
        {
            // received a new train entity - DynamicEntityEventArgs contains the entity
            var trainEntity = e.DynamicEntity;

            // get the train and route IDs from the attributes of the entity
            var trainId = trainEntity.Attributes.GetStringAttribute("TrainId");
            var routeId = trainEntity.Attributes.GetStringAttribute("RouteId");
            if (string.IsNullOrEmpty(trainId) || string.IsNullOrEmpty(routeId))
                return;

            // cache the train in the appropriate route
            var train = new TransitVehicle(trainEntity);
            var route = TransitRoutes.FirstOrDefault(tr => routeId.Equals(tr.RouteFeature.Attributes["Id"]));
            Application.Current?.Dispatcher.Invoke(() => route?.Vehicles.Add(train));
        }
        catch (Exception)
        {
        }
    }

    // Event handler for when a dynamic entity observation is received
    // - this is used to gather statistics for the UI
    private void TrainSource_DynamicEntityObservationReceived(object? sender, DynamicEntityObservationEventArgs e)
    {
        try
        {
            // received a new observation for a train entity - observation available in the event args
            var train = e.Observation;
            var trainId = train.Attributes.GetStringAttribute("TrainId");
            if (string.IsNullOrEmpty(trainId))
                return;

            // find the route and train in the cache and update the statistics
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var trainEntity = train.GetDynamicEntity();
                var routeId = train.Attributes.GetStringAttribute("RouteId");
                var route = TransitRoutes.FirstOrDefault(tr => routeId.Equals(tr.RouteId));
                var routeTrains = route?.Vehicles.ToList();
                var vehicle = routeTrains?.FirstOrDefault(v => v.VehicleEntity == trainEntity);
                var delay = train.Attributes.GetIntAttribute("Delay");

                TrainStatistics.AddOrUpdateTrain(trainId, delay);
                route?.TrainStatistics.AddOrUpdateTrain(trainId, delay);
                vehicle?.ForceUpdate();
            });
        }
        catch (Exception)
        {
        }
    }

    private void ClearSelectedTrain()
    {
        IsPopupOpen = false;
        MapViewController.DismissCallout();
        _trainLayer?.ClearSelection();
    }

    // On selection of the train via Identify or list selection change:
    // - select the train dynamic entity in the layer
    // - zoom to the train on the map
    // - show a callout for the train
    private async Task SelectTrainAsync(TransitVehicle? transitVehicle)
    {
        if (transitVehicle?.VehicleEntity is not DynamicEntity train
            || train.Geometry is not MapPoint point)
            return;

        // select the train
        _trainLayer?.SelectDynamicEntity(train);

        // zoom to the train
        MapViewController.SetViewpoint(new Viewpoint(point, 25_000d));

        // show a callout for the train
        var route = TransitRoutes.FirstOrDefault(tr => tr.RouteId == train.Attributes.GetStringAttribute("RouteId"));
        // use TextExpression and DetailTextExpression to make the callout text dynamic
        var calloutDef = new CalloutDefinition(train,
            "'Train: ' + $feature.TrainId",
            "IIf($feature.Delay <= 0, 'On Schedule', 'Delay: ' + $feature.Delay + ' min')")
        {
            Icon = await RuntimeImageFromRouteAsync(route),
            OnButtonClick = (obj) => OpenPopup()
        };
        MapViewController.ShowCalloutForGeoElement(train, calloutDef);
    }

    [RelayCommand]
    private async Task GeoViewTappedAsync(GeoViewInputEventArgs eventArgs)
    {
        if (_trainLayer is null)
            return;

        try
        {
            // clear selection in the UI
            SelectedVehicle = null;

            // identify the map layer at the tapped location
            var result = await MapViewController.IdentifyLayerAsync(_trainLayer, eventArgs.Position, 10d);

            // result contains identified observation (not dynamic entity)
            if (result?.GeoElements?.FirstOrDefault() is DynamicEntityObservation observation)
            {
                // get the dynamic entity from the observation
                var vehicle = observation.GetDynamicEntity();
                if (vehicle is null)
                    return;

                // select the train in the UI
                var route = TransitRoutes.FirstOrDefault(tr => tr.RouteId == vehicle.Attributes.GetStringAttribute("RouteId"));
                if (route is not null)
                {
                    SelectedVehicle = route.Vehicles.FirstOrDefault(v => v.VehicleEntity == vehicle);
                }
            }
        }
        catch (Exception)
        {
            // ignore
        }
    }

    [RelayCommand]
    private void OpenPopup()
    {
        if (SelectedVehicle?.VehicleEntity is null)
            return;

        // create and open the popup
        IsPopupOpen = !IsPopupOpen;
    }

    #region Helper Methods

    private async Task<RuntimeImage?> RuntimeImageFromRouteAsync(TransitRoute? route)
    {
        if (route?.Icon is null)
            return null;
        return await route.Icon.ToRuntimeImageAsync();
    }

    #endregion
}