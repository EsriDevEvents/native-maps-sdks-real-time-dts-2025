using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Mapping.Labeling;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.UI;
using GTFS;
using GTFS.Entities;
using TransitTracker.Framework;

namespace TransitTracker;

public class StaticGtfsData(bool recreateStaticTransitData)
{
    private const string _tempPath = @"c:\temp\test";

    private Geodatabase? _staticTransitGdb;
    private GTFSFeed? _staticRailFeed;

    public async Task InitializeAsync()
    {
        _staticTransitGdb = await InitializeStaticTransitGeodatabaseAsync();
        _staticRailFeed = await InitializeStaticRailFeedAsync();
    }

    private async Task<Geodatabase> InitializeStaticTransitGeodatabaseAsync()
    {
        const string _staticTransitData = @$"{_tempPath}\static_transit_data.geodatabase";

        if (File.Exists(_staticTransitData) && !recreateStaticTransitData)
            return await Geodatabase.OpenAsync(_staticTransitData);

        if (File.Exists(_staticTransitData))
            File.Delete(_staticTransitData);

        return await Geodatabase.CreateAsync(_staticTransitData);
    }

    private async Task<GTFSFeed> InitializeStaticRailFeedAsync()
    {
        var staticRailUrl = "https://api.wmata.com/gtfs/rail-gtfs-static.zip";
        var staticZipPath = @$"{_tempPath}\rail-gtfs-static.zip";

        if (!File.Exists(staticZipPath) || recreateStaticTransitData)
        {
            // redownload the GTFS static zip file from WMATA
            using FileStream file = File.OpenWrite(staticZipPath);
            var client = new HttpClient();
            client.DefaultRequestHeaders.CacheControl = CacheControlHeaderValue.Parse("no-cache");
            // demo API Key for WMATA GTFS data: from https://developer.wmata.com/products
            client.DefaultRequestHeaders.Add("api_key", "e13626d03d8e4c03ac07f95541b3091b");
            var stream = await client.GetStreamAsync(staticRailUrl);
            await stream.CopyToAsync(file);
        }

        var reader = new GTFSReader<GTFSFeed>();
        return reader.Read(staticZipPath);
    }

    public async Task<FeatureLayer> InitializeRailRoutesAsync(IList<TransitRoute> transitRoutes, DictionarySymbolStyle symbolStyle)
    {
        _ = _staticTransitGdb ?? throw new InvalidOperationException("Static transit geodatabase not initialized");
        _ = _staticRailFeed ?? throw new InvalidOperationException("Static rail feed not initialized");

        var routesTable = _staticTransitGdb.GetGeodatabaseFeatureTable("Routes");
        if (routesTable is null)
        {
            var tableDesc = new TableDescription("Routes", SpatialReferences.Wgs84, GeometryType.Polyline);
            tableDesc.FieldDescriptions.Add(new("ID", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Color", FieldType.Int32));
            tableDesc.FieldDescriptions.Add(new("Description", FieldType.Text) { Length = 256 });
            tableDesc.FieldDescriptions.Add(new("LongName", FieldType.Text) { Length = 256 });
            tableDesc.FieldDescriptions.Add(new("ShortName", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Tag", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("TextColor", FieldType.Int32));
            tableDesc.FieldDescriptions.Add(new("Type", FieldType.Int32));
            tableDesc.FieldDescriptions.Add(new("Url", FieldType.Text) { Length = 256 });
            routesTable = await _staticTransitGdb.CreateTableAsync(tableDesc);

            var tripShapes = _staticRailFeed.Trips.GroupBy(t => t.ShapeId).Select(ts => ts.First()).ToList();
            foreach (var trip in tripShapes)
            {
                var polyline = new PolylineBuilder(SpatialReferences.Wgs84);
                foreach (var shape in _staticRailFeed.Shapes.Get(trip.ShapeId))
                {
                    var point = new MapPoint(shape.Longitude, shape.Latitude, SpatialReferences.Wgs84);
                    polyline.AddPoint(point);
                }

                var route = _staticRailFeed.Routes.Get(trip.RouteId);
                if (route.ShortName == "SHUTTLE")
                    continue;

                var feature = routesTable.CreateFeature(
                    [
                        new KeyValuePair<string, object?>("ID", trip.RouteId),
                        new KeyValuePair<string, object?>("Color", route.Color),
                        new KeyValuePair<string, object?>("Description", route.Description),
                        new KeyValuePair<string, object?>("LongName", route.LongName),
                        new KeyValuePair<string, object?>("ShortName", route.ShortName),
                        new KeyValuePair<string, object?>("Tag", route.Tag),
                        new KeyValuePair<string, object?>("TextColor", route.TextColor),
                        new KeyValuePair<string, object?>("Type", (int)route.Type),
                        new KeyValuePair<string, object?>("Url", route.Url)
                    ],
                    polyline.ToGeometry());
                await routesTable.AddFeatureAsync(feature);
            }
        }

        // reload symbol style to reset configurations
        var basicStyle = await DictionarySymbolStyle.CreateFromFileAsync(symbolStyle.StyleLocation);
        basicStyle.Configurations.First(c => c.Name == "include_arrow").Value = "OFF";

        var renderer = new UniqueValueRenderer
        {
            DefaultSymbol = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.Black, 1)
        };
        renderer.FieldNames.Add("ID");
        var query = new QueryParameters { ReturnGeometry = false, WhereClause = "1=1" };
        var routeFeatures = (await routesTable.QueryFeaturesAsync(query))
            .GroupBy(f => f.Attributes.GetStringAttribute("ID"))
            .Select(g => g.First());
        foreach (var route in routeFeatures)
        {
            var routeId = route.Attributes.GetStringAttribute("ID");
            var transitRoute = new TransitRoute(routeId, route);

            var sym = await basicStyle.GetSymbolAsync(
                new Dictionary<string, object?> { { "RouteColor", route.Attributes["Color"] } });
            transitRoute.Icon = await (await sym.CreateSwatchAsync(256)).ToImageSourceAsync();

            transitRoutes.Add(transitRoute);
            
            var color = Color.FromArgb(route.Attributes.GetIntAttribute("Color"));
            var routeColor = Color.FromArgb(175, color.R, color.G, color.B);

            renderer.UniqueValues.Add(
                new UniqueValue(
                    route.Attributes.GetStringAttribute("LongName"),
                    route.Attributes.GetStringAttribute("ShortName"),
                    new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, routeColor, 4),
                    route.Attributes.GetStringAttribute("Id")));
        }
        return new FeatureLayer(routesTable) { Renderer = renderer };
    }

    public async Task<FeatureLayer> InitializeRailStopsAsync()
    {
        _ = _staticTransitGdb ?? throw new InvalidOperationException("Static transit geodatabase not initialized");
        _ = _staticRailFeed ?? throw new InvalidOperationException("Static rail feed not initialized");

        var stopsTable = _staticTransitGdb.GetGeodatabaseFeatureTable("Stops");
        if (stopsTable is null)
        {
            var tableDesc = new TableDescription("Stops", SpatialReferences.Wgs84, GeometryType.Point);
            tableDesc.FieldDescriptions.Add(new("Id", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Name", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Code", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Description", FieldType.Text) { Length = 256 });
            tableDesc.FieldDescriptions.Add(new("LevelId", FieldType.Text) { Length = 256 });
            tableDesc.FieldDescriptions.Add(new("LocationType", FieldType.Int32));
            tableDesc.FieldDescriptions.Add(new("ParentStation", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("PlatformCode", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Tag", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Timezone", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Url", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("WheelchairBoarding", FieldType.Text) { Length = 64 });
            tableDesc.FieldDescriptions.Add(new("Zone", FieldType.Text) { Length = 64 });
            stopsTable = await _staticTransitGdb.CreateTableAsync(tableDesc);

            foreach (var gtfsStop in _staticRailFeed.Stops)
            {
                var point = new MapPoint(gtfsStop.Longitude, gtfsStop.Latitude, SpatialReferences.Wgs84);
                var stop = stopsTable.CreateFeature([
                        new KeyValuePair<string, object?>("Id", gtfsStop.Id),
                        new KeyValuePair<string, object?>("Name", gtfsStop.Name),
                        new KeyValuePair<string, object?>("Code", gtfsStop.Code),
                        new KeyValuePair<string, object?>("Description", gtfsStop.Description),
                        new KeyValuePair<string, object?>("LevelId", gtfsStop.LevelId),
                        new KeyValuePair<string, object?>("LocationType", (int)gtfsStop.LocationType!),
                        new KeyValuePair<string, object?>("ParentStation", gtfsStop.ParentStation),
                        new KeyValuePair<string, object?>("PlatformCode", gtfsStop.PlatformCode),
                        new KeyValuePair<string, object?>("Tag", gtfsStop.Tag),
                        new KeyValuePair<string, object?>("Timezone", gtfsStop.Timezone),
                        new KeyValuePair<string, object?>("Url", gtfsStop.Url),
                        new KeyValuePair<string, object?>("WheelchairBoarding", gtfsStop.WheelchairBoarding),
                        new KeyValuePair<string, object?>("Zone", gtfsStop.Zone)
                    ], point);
                await stopsTable.AddFeatureAsync(stop);
            }
        }

        var layer = new FeatureLayer(stopsTable)
        {
            DefinitionExpression = "ParentStation = ''",
            Renderer = new SimpleRenderer(new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, Color.White, 12d) { Outline = new SimpleLineSymbol(SimpleLineSymbolStyle.Solid, Color.Gray, 2d) })
        };
        layer.LabelDefinitions.Add(new LabelDefinition(
            new ArcadeLabelExpression("Replace($feature.Name, ' METRORAIL STATION', '')"),
            new TextSymbol()
            {
                Color = Color.Black,
                HaloColor = Color.White,
                HaloWidth = 1,
                FontFamily = "Arial",
                Size = 10,
            })
        { LabelOverlapStrategy = LabelOverlapStrategy.Exclude, MinScale = 75_000d });
        layer.LabelsEnabled = true;
        return layer;
    }

    public Route? GetRoute(string routeId)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        return _staticRailFeed.Routes.Get(routeId);
    }

    public Stop GetStop(string stopId)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        return _staticRailFeed.Stops.Get(stopId);
    }

    public Stop? GetStopBySequence(string tripId, int stopSequence)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        var stopTime = _staticRailFeed.StopTimes.GetForTrip(tripId).FirstOrDefault(st => st.StopSequence == stopSequence);
        if (stopTime is null)
            return null;

        return _staticRailFeed.Stops.Get(stopTime.StopId);
    }

    public string GetStationName(string stopId)
    {
        try
        {
            if (_staticRailFeed is null)
                throw new InvalidOperationException("Static rail feed not initialized");

            var stop = _staticRailFeed.Stops.Get(stopId);
            if (!stop.IsTypeStation())
                stop = GetStop(stop.ParentStation) ?? stop;

            return stop?.Name ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public string GetNextStationName(string tripId, int stopSequence)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        var stop = GetStopBySequence(tripId, stopSequence + 1);
        if (stop is null)
            return string.Empty;

        if (!stop.IsTypeStation())
            stop = GetStop(stop.ParentStation) ?? stop;

        return stop?.Name ?? string.Empty;
    }

    public IList<StopTime> GetStopTimes(string tripId)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        return _staticRailFeed.StopTimes.GetForTrip(tripId).ToList();
    }

    public Stop? GetNextStation(string tripId, int currentStopSequenece)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        var nextStopTime = _staticRailFeed.StopTimes.GetForTrip(tripId)
            .FirstOrDefault(st => st.StopSequence == currentStopSequenece + 1);
        if (nextStopTime is null)
            return null;

        var nextStop = _staticRailFeed.Stops.Get(nextStopTime.StopId);
        if (!nextStop.IsTypeStation())
            return GetStop(nextStop.ParentStation) ?? nextStop;

        return nextStop;
    }

    public (Stop? nextStop, Stop? nextStation, TimeOfDay? arrivalTime) GetNextStopInfo(string tripId, int currentStopSequenece)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        Stop? nextStop = null;
        Stop? nextStation = null;
        TimeOfDay? scheduledTime;

        var nextStopTime = _staticRailFeed.StopTimes.FirstOrDefault(st => st.TripId == tripId && st.StopSequence == currentStopSequenece + 1);
        if (nextStopTime is null)
            return (null, null, null);

        nextStation = nextStop = _staticRailFeed.Stops.Get(nextStopTime.StopId);
        if (!nextStation.IsTypeStation())
            nextStation = GetStop(nextStation.ParentStation) ?? nextStop;

        scheduledTime = nextStopTime.ArrivalTime;
        return (nextStop, nextStation, scheduledTime);
    }

    public List<Stop> GetNextStations(string tripId, int currentStopSequenece)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        List<Stop> nextStations = [];

        var stopTimes = _staticRailFeed.StopTimes
            .Where(st => st.TripId == tripId && st.StopSequence > currentStopSequenece)
            .Skip(currentStopSequenece)
            .ToList();
        foreach (var stopTime in stopTimes)
        {
            var stop = _staticRailFeed.Stops.Get(stopTime.StopId);
            if (stop.IsTypeStation() && !nextStations.Contains(stop))
            {
                nextStations.Add(stop);
                if (nextStations.Count == 3)
                    break;
            }
        }

        return nextStations;
    }

    public int GetStopTimeUpdate(string stopId)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        var stopTime = _staticRailFeed.StopTimes.FirstOrDefault(st => st.StopId == stopId);
        if (stopTime is null || !stopTime.ArrivalTime.HasValue)
            return 0;

        return stopTime.ArrivalTime.Value.TotalSeconds;
    }

    public (string origin, string destination, int numberOfStops) GetTripInfo(string tripId)
    {
        if (_staticRailFeed is null)
            throw new InvalidOperationException("Static rail feed not initialized");

        var trip = _staticRailFeed.Trips.FirstOrDefault(t => t.Id == tripId);
        if (trip is null)
            return (string.Empty, string.Empty, 0);

        var stopTime = _staticRailFeed.StopTimes.FirstOrDefault(st => st.TripId == tripId && st.StopSequence == 1);
        if (stopTime is null)
            return (string.Empty, string.Empty, 0);

        var originStationId = _staticRailFeed.Stops.Get(stopTime.StopId).ParentStation;
        var origin = _staticRailFeed.Stops.Get(originStationId).Name;

        var destStationId = _staticRailFeed.Stops.Get(_staticRailFeed.StopTimes.Last(st => st.TripId == tripId).StopId).ParentStation;
        var destination = _staticRailFeed.Stops.Get(destStationId).Name;

        var numberOfStops = _staticRailFeed.StopTimes.Count(st => st.TripId == tripId);
        return (origin, destination, numberOfStops);
    }
}
