using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.RealTime;
using ProtoBuf;
using TransitRealtime;

namespace TransitTracker;

// Custom DynamicEntityDataSource that polls WMATA's GTFS-RT feeds for train positions and updates
// - derive from DynamicEntityDataSource and implement the required methods
// - provides a mashup of vehicle position and trip update GTFS-RT data
public partial class TrainSource : DynamicEntityDataSource
{
    #region Constants
    private const string ApiKey = "b0610eae713f40be9668828325f8f52d";
    private const string TripUpdatesUrl = "https://api.wmata.com/gtfs/rail-gtfsrt-tripupdates.pb";
    private const string VehiclePositionsUrl = "https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb";

    public const string TrainIdField = "TrainId";
    public const string TimestampField = "Timestamp";
    public const string RouteIdField = "RouteId";
    public const string TripIdField = "TripId";
    public const string OriginField = "Origin";
    public const string DestinationField = "Destination";
    public const string StatusField = "Status";
    public const string StopIdField = "StopId";
    public const string StopNameField = "StopName";
    public const string StopSequenceField = "StopSequence";
    public const string NumberOfStopsField = "NumberOfStops";
    public const string ArriveOrDepartTimeField = "ArriveOrDepartTime";
    public const string BearingField = "Bearing";
    public const string RouteColorField = "RouteColor";
    public const string DelayField = "Delay";
    #endregion

    #region Fields
    private readonly StaticGtfsData _staticGtfsData;
    private readonly Dictionary<string, Train> _trains = [];

    private readonly Timer _vehiclePositionUpdateTimer;
    private readonly SemaphoreSlim _vehiclePositionUpdateSemaphore = new(1, 1);
    private readonly int _vehiclePositionUpdateInterval = 10_000;

    private readonly Timer _tripUpdateTimer;
    private readonly SemaphoreSlim _tripUpdateSemaphore = new(1, 1);
    private readonly int _tripUpdateInterval = 25_000;
    #endregion

    public TrainSource(StaticGtfsData staticGtfsData)
    {
        _staticGtfsData = staticGtfsData;

        // create polling timers
        _vehiclePositionUpdateTimer = new Timer(VehiclePositionUpdateTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        _tripUpdateTimer = new Timer(TripUpdateTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
    }

    protected override Task<DynamicEntityDataSourceInfo> OnLoadAsync()
    {
        // set the schema and track ID field (tells the API how to handle the data)
        var info = new DynamicEntityDataSourceInfo(TrainIdField, GetSchema()) { SpatialReference = SpatialReferences.Wgs84 };
        return Task.FromResult(info);
    }

    protected override Task OnConnectAsync(CancellationToken cancellationToken)
    {
        // start the polling timers
        _vehiclePositionUpdateTimer.Change(100, _vehiclePositionUpdateInterval);
        _tripUpdateTimer.Change(3000, _tripUpdateInterval);
        return Task.CompletedTask;
    }

    protected override Task OnDisconnectAsync()
    {
        // stop the polling timers
        _vehiclePositionUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _tripUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    private async void VehiclePositionUpdateTimerCallback(object? o)
    {
        // only run the method if the previous run is complete
        if (!_vehiclePositionUpdateSemaphore.Wait(0))
            return;

        try
        {
            // poll the latest GTFS data
            var feed = await MakeGtfsRequestAsync(VehiclePositionsUrl);

            // emit a new observation for each vehicle position entry
            var vehiclePositions = feed.Entities.Select(fe => fe.Vehicle).Where(v => v is not null);
            foreach (VehiclePosition vehiclePosition in vehiclePositions)
            {
                // parse the protobuf data and create or update the cached train object
                Train? train = CreateOrUpdateVehiclePositionData(vehiclePosition);

                // update timestamp, location, and bearing
                train.Timestamp = (long)vehiclePosition.Timestamp;
                train.Bearing = vehiclePosition.Position.Bearing;
                train.Location = new MapPoint(vehiclePosition.Position.Longitude, vehiclePosition.Position.Latitude, SpatialReferences.Wgs84);

                // emit the observation
                AddObservation(train.Location, CreateAttributes(train));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error generating observations: {ex}");
        }
        finally
        {
            _vehiclePositionUpdateSemaphore.Release();
        }
    }

    private async void TripUpdateTimerCallback(object? o)
    {
        // only run the method if the previous run is complete
        if (!_tripUpdateSemaphore.Wait(0))
            return;

        try
        {
            // poll the latest GTFS trip updates
            var feed = await MakeGtfsRequestAsync(TripUpdatesUrl);

            // emit a new train observation for each trip update
            var tripUpdates = feed.Entities.Where(ent => ent.TripUpdate is not null).Select(ent => ent.TripUpdate);
            foreach (TripUpdate tripUpdate in tripUpdates)
            {
                // find the cached train object for the trip update
                if (GetTrainFromTripUpdate(tripUpdate) is not Train train)
                    continue;

                // calculate and update the train's schedule status and delay
                // - delay is only updated when a trip update is received (not in the vehicle position update)
                if (!string.IsNullOrEmpty(train.StopId))
                    train.Delay = CalculateDelay(train.ArriveOrDepartTime, GetStopUpdateTime(tripUpdate, train.StopId));

                // update the timestamp and emit the observation
                train.Timestamp = (long)tripUpdate.Timestamp;
                AddObservation(train.Location, CreateAttributes(train));
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error generating observations on trip update: {ex}");
        }
        finally
        {
            _tripUpdateSemaphore.Release();
        }
    }

    #region Methods

    private Train CreateOrUpdateVehiclePositionData(VehiclePosition vehiclePosition)
    {
        // get the train from the cache or create a new one
        if (!_trains.TryGetValue(vehiclePosition.Vehicle.Id, out Train? train))
        {
            train = new Train { TrainId = vehiclePosition.Vehicle.Id };
            _trains[train.TrainId] = train;
        }

        // update trip information
        if (train.TripId != vehiclePosition.Trip.TripId)
        {
            train.RouteId = vehiclePosition.Trip.RouteId;
            train.RouteColor = _staticGtfsData.GetRoute(train.RouteId)?.Color ?? 0;

            train.TripId = vehiclePosition.Trip.TripId;
            var (origin, destination, numberOfStops) = _staticGtfsData.GetTripInfo(train.TripId);
            train.Origin = origin;
            train.Destination = destination;
            train.NumberOfStops = numberOfStops;
        }

        // update stop information
        if (train.StopSequence != vehiclePosition.CurrentStopSequence)
        {
            train.StopSequence = (int)vehiclePosition.CurrentStopSequence;

            var stop = _staticGtfsData.GetStopBySequence(train.TripId, train.StopSequence);
            if (stop is null)
            {
                train.StopId = string.Empty;
                train.StopName = string.Empty;
                train.ArriveOrDepartTime = 0;
            }
            else
            {
                train.StopId = stop.Id;

                // set the stop time at the current stop
                var stopTimes = _staticGtfsData.GetStopTimes(train.TripId);
                if (stopTimes.Count >= train.StopSequence)
                    train.ArriveOrDepartTime = stopTimes[train.StopSequence - 1].ArrivalTime?.TotalSeconds ?? 0;
            }
        }

        var status = vehiclePosition.CurrentStatus switch
        {
            VehiclePosition.VehicleStopStatus.StoppedAt => "Stopped",
            VehiclePosition.VehicleStopStatus.IncomingAt => "Incoming",
            _ => "In Transit",
        };
        if (train.StopSequence != vehiclePosition.CurrentStopSequence || train.Status != status)
        {
            train.Status = status;

            if (vehiclePosition.CurrentStatus == VehiclePosition.VehicleStopStatus.StoppedAt)
                train.StopName = _staticGtfsData.GetStationName(train.StopId);
            else
                train.StopName = _staticGtfsData.GetNextStationName(train.TripId, train.StopSequence);
        }

        return train;
    }

    private Train? GetTrainFromTripUpdate(TripUpdate tripUpdate)
    {
        if (tripUpdate.Trip.schedule_relationship is not TripDescriptor.ScheduleRelationship.Scheduled)
            return null;

        var vehicle = tripUpdate.Vehicle;
        if (vehicle is null)
            return null;

        if (!_trains.TryGetValue(vehicle.Id, out Train? train))
            return null;

        if (train.Location is null)
            return null;

        return train;
    }

    public int CalculateDelay(long scheduledTime, long actualTimeStamp)
    {
        var scheduled = TimeSpan.FromSeconds(scheduledTime);
        var actual = DateTimeOffset.FromUnixTimeSeconds(actualTimeStamp).ToOffset(TimeSpan.FromHours(-4)).DateTime.TimeOfDay;

        // Adjust for midnight crossover
        if (scheduled < TimeSpan.FromHours(4) && actual > TimeSpan.FromHours(20))
        {
            scheduled = scheduled.Add(TimeSpan.FromDays(1));
        }
        else if (actual < TimeSpan.FromHours(4) && scheduled > TimeSpan.FromHours(20))
        {
            actual = actual.Add(TimeSpan.FromDays(1));
        }

        return (int)Math.Floor((actual - scheduled).TotalMinutes);
    }

    private static long GetStopUpdateTime(TripUpdate tripUpdate, string stopId)
    {
        var stopTimeUpdate = tripUpdate.StopTimeUpdates.FirstOrDefault(stu => stu.StopId == stopId);
        return stopTimeUpdate?.Arrival?.Time ?? stopTimeUpdate?.Departure?.Time ?? 0L;
    }

    private static async Task<FeedMessage> MakeGtfsRequestAsync(string url)
    {
        // make REST API call to get GTFS-RT protobuf file
        var client = new HttpClient();
        client.DefaultRequestHeaders.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        client.DefaultRequestHeaders.Add("api_key", ApiKey);
        var response = await client.GetAsync(url);
        var stream = await response.Content.ReadAsStreamAsync();
        return Serializer.Deserialize<FeedMessage>(stream);
    }

    private static List<KeyValuePair<string, object?>> CreateAttributes(Train train)
    {
        return
        [
            new KeyValuePair<string, object?>(TrainIdField, train.TrainId),
            new KeyValuePair<string, object?>(TimestampField, train.Timestamp),
            new KeyValuePair<string, object?>(RouteIdField, train.RouteId),
            new KeyValuePair<string, object?>(TripIdField, train.TripId),
            new KeyValuePair<string, object?>(OriginField, train.Origin),
            new KeyValuePair<string, object?>(DestinationField, train.Destination),
            new KeyValuePair<string, object?>(StatusField, train.Status),
            new KeyValuePair<string, object?>(StopIdField, train.StopId),
            new KeyValuePair<string, object?>(StopNameField, train.StopName),
            new KeyValuePair<string, object?>(StopSequenceField, train.StopSequence),
            new KeyValuePair<string, object?>(NumberOfStopsField, train.NumberOfStops),
            new KeyValuePair<string, object?>(ArriveOrDepartTimeField, train.ArriveOrDepartTime),
            new KeyValuePair<string, object?>(BearingField, train.Bearing),
            new KeyValuePair<string, object?>(RouteColorField, train.RouteColor),
            new KeyValuePair<string, object?>(DelayField, train.Delay),
        ];
    }

    private static List<Field> GetSchema()
    {
        return
        [
            new(FieldType.Text, TrainIdField, TrainIdField.ToUpper(), 256),
            new(FieldType.Int64, TimestampField, TimestampField.ToUpper(), 8),
            new(FieldType.Text, RouteIdField, RouteIdField.ToUpper(), 64),
            new(FieldType.Text, TripIdField, TripIdField.ToUpper(), 64),
            new(FieldType.Text, OriginField, OriginField.ToUpper(), 64),
            new(FieldType.Text, DestinationField, DestinationField.ToUpper(), 64),
            new(FieldType.Text, StatusField, StatusField.ToUpper(), 64),
            new(FieldType.Text, StopIdField, StopIdField.ToUpper(), 64),
            new(FieldType.Text, StopNameField, StopNameField.ToUpper(), 64),
            new(FieldType.Int32, StopSequenceField, StopSequenceField.ToUpper(), 4),
            new(FieldType.Int32, NumberOfStopsField, NumberOfStopsField.ToUpper(), 4),
            new(FieldType.Int64, ArriveOrDepartTimeField, ArriveOrDepartTimeField.ToUpper(), 8),
            new(FieldType.Float64, BearingField, BearingField.ToUpper(), 8),
            new(FieldType.Int32, RouteColorField, RouteColorField.ToUpper(), 4),
            new(FieldType.Int32, DelayField, DelayField.ToUpper(), 4),
        ];
    }

    #endregion
}

#region Train Model

internal class Train
{
    public string TrainId { get; set; } = string.Empty;
    public MapPoint? Location { get; set; }
    public long Timestamp { get; set; }
    public string RouteId { get; set; } = string.Empty;
    public string TripId { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public int StopSequence { get; set; }
    public int NumberOfStops { get; set; }
    public long ArriveOrDepartTime { get; set; }
    public int Delay { get; set; }
    public double Bearing { get; set; }
    public int RouteColor { get; set; }
}

#endregion