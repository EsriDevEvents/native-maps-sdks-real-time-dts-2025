using CommunityToolkit.Mvvm.ComponentModel;

namespace TransitTracker;

public partial class TrainStatistics : ObservableObject
{
    [ObservableProperty]
    private int _totalTrains;

    [ObservableProperty]
    private int _onScheduleTrains;

    [ObservableProperty]
    private int _delayedTrains;

    [ObservableProperty]
    private double _averageDelay;

    private readonly Dictionary<string, TrainInfo> trains;
    private readonly List<double> delayTimes;

    public TrainStatistics()
    {
        TotalTrains = 0;
        OnScheduleTrains = 0;
        DelayedTrains = 0;
        AverageDelay = 0.0;
        trains = [];
        delayTimes = [];
    }

    public void AddOrUpdateTrain(string trainId, int delay = 0)
    {
        if (trains.ContainsKey(trainId))
        {
            UpdateTrain(trainId, delay);
        }
        else
        {
            AddTrain(trainId);
        }
    }

    private void AddTrain(string trainId)
    {
        TotalTrains++;
        OnScheduleTrains++;
        trains[trainId] = new TrainInfo { IsOnSchedule = true, Delay = 0.0 };
    }

    private void UpdateTrain(string trainId, int delay)
    {
        var train = trains[trainId];

        var isOnSchedule = (delay <= 0);
        if (train.IsOnSchedule && !isOnSchedule)
        {
            OnScheduleTrains--;
            DelayedTrains++;
            delayTimes.Add(delay);
        }
        else if (!train.IsOnSchedule && isOnSchedule)
        {
            OnScheduleTrains++;
            DelayedTrains--;
            delayTimes.Remove(train.Delay);
        }
        else if (!train.IsOnSchedule && !isOnSchedule)
        {
            delayTimes.Remove(train.Delay);
            delayTimes.Add(delay);
        }
        train.IsOnSchedule = isOnSchedule;
        train.Delay = delay;
        AverageDelay = CalculateAverageDelay();
    }

    private double CalculateAverageDelay()
    {
        if (delayTimes.Count == 0)
            return 0.0;

        double totalDelay = 0.0;
        foreach (var delay in delayTimes)
            totalDelay += delay;
        return totalDelay / delayTimes.Count;
    }

    private class TrainInfo
    {
        public bool IsOnSchedule { get; set; }
        public double Delay { get; set; }
    }
}