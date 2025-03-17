using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.RealTime;
using TransitTracker.Framework;

namespace TransitTracker;

public class TransitRoute(string routeId, Feature routeFeature)
{
    public string RouteId { get; set; } = routeId;

    public Feature RouteFeature { get; set; } = routeFeature;

    public ObservableCollection<TransitVehicle> Vehicles { get; set; } = [];

    public TrainStatistics TrainStatistics { get; } = new();

    public ImageSource? Icon { get; set; }
}

public partial class TransitVehicle : ObservableObject
{
    public TransitVehicle(DynamicEntity vehicleEntity)
    {
        _vehicleEntity = vehicleEntity;
    }

    public DynamicEntity VehicleEntity
    {
        get => _vehicleEntity;
        set => SetProperty(ref _vehicleEntity, value);
    }
    private DynamicEntity _vehicleEntity;

    public bool IsOnSchedule => VehicleEntity.Attributes.GetIntAttribute("Delay") <= 0;

    public int LastUpdated => VehicleEntity.Attributes.GetIntAttribute("LastUpdated");

    public void ForceUpdate()
    {
        OnPropertyChanged(nameof(VehicleEntity));
        OnPropertyChanged(nameof(IsOnSchedule));
        OnPropertyChanged(nameof(LastUpdated));
    }
}