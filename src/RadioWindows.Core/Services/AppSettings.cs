namespace RadioWindows.Core.Services;

/// <summary>Small persisted preferences: volume, resume-on-launch, last station, autostart.</summary>
public sealed class AppSettings : ObservableObject
{
    private readonly Preferences _preferences;
    private double _volume;
    private bool _resumeLastStation;
    private string? _lastStationId;
    private bool _autoStartEnabled;

    public AppSettings(Preferences preferences)
    {
        _preferences = preferences;
        _volume = Math.Clamp(preferences.GetDoubleLike("volume") ?? 0.8, 0, 1);
        _resumeLastStation = preferences.GetBoolLike("resume_last_station") ?? false;
        _lastStationId = preferences.GetString("last_station_id");
        _autoStartEnabled = preferences.GetBoolLike("auto_start") ?? false;
    }

    public double Volume => _volume;

    public bool ResumeLastStation => _resumeLastStation;

    public string? LastStationId => _lastStationId;

    public bool AutoStartEnabled => _autoStartEnabled;

    public void SetVolume(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        if (Set(ref _volume, clamped, nameof(Volume)))
        {
            _preferences.Set("volume", clamped);
        }
    }

    public void SetResumeLastStation(bool value)
    {
        if (Set(ref _resumeLastStation, value, nameof(ResumeLastStation)))
        {
            _preferences.Set("resume_last_station", value);
        }
    }

    public void SetLastStationId(string? id)
    {
        if (Set(ref _lastStationId, id, nameof(LastStationId)))
        {
            if (id is null)
            {
                _preferences.Remove("last_station_id");
            }
            else
            {
                _preferences.Set("last_station_id", id);
            }
        }
    }

    public void SetAutoStartEnabled(bool value)
    {
        if (Set(ref _autoStartEnabled, value, nameof(AutoStartEnabled)))
        {
            _preferences.Set("auto_start", value);
        }
    }
}
