using CommunityToolkit.Mvvm.ComponentModel;

namespace TransitTracker;

public partial class ObservationStatistics(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private int _totalObservations = 1;

    [ObservableProperty]
    private int _totalEntities = 0;
}