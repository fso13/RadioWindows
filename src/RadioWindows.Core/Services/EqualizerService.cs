using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>Ten-band equalizer state, applied live to the audio engine.</summary>
public sealed class EqualizerService : ObservableObject, IDisposable
{
    private readonly Preferences _preferences;
    private readonly Timer _persistTimer;
    private bool _enabled;
    private string _presetId;
    private double[] _gains;
    private double[] _customGains;
    private bool _pendingPersist;

    public EqualizerService(Preferences preferences)
    {
        _preferences = preferences;
        _enabled = preferences.GetBoolLike("eq_enabled") ?? false;
        _presetId = preferences.GetString("eq_preset") ?? EqualizerCatalog.FlatId;

        var stored = preferences.GetDoubleArray("eq_gains");
        _gains = stored is not null
            ? EqualizerCatalog.ClampGains(stored)
            : EqualizerCatalog.ClampGains(EqualizerCatalog.ById(_presetId).Gains);
        _customGains = preferences.GetDoubleArray("eq_custom_gains") ?? (double[])_gains.Clone();

        if (_presetId != EqualizerCatalog.CustomId)
        {
            _gains = EqualizerCatalog.ClampGains(Preset.Gains);
        }

        _persistTimer = new Timer(_ => PersistNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool Enabled => _enabled;

    public string PresetId => _presetId;

    public double[] Gains => _gains;

    public EqualizerPreset Preset => EqualizerCatalog.ById(_presetId);

    /// <summary>libVLC provides a native 10-band equalizer on every codec.</summary>
    public bool IsSupported => true;

    public bool IsCustom => _presetId == EqualizerCatalog.CustomId;

    public void SetEnabled(bool value)
    {
        if (_enabled == value || !IsSupported)
        {
            return;
        }

        _enabled = value;
        Raise(nameof(Enabled));
        PersistNow();
    }

    public void ApplyPreset(string id)
    {
        _presetId = id;
        _gains = id == EqualizerCatalog.CustomId
            ? EqualizerCatalog.ClampGains(_customGains)
            : EqualizerCatalog.ClampGains(EqualizerCatalog.ById(id).Gains);
        Raise(nameof(PresetId));
        Raise(nameof(Preset));
        Raise(nameof(Gains));
        Raise(nameof(IsCustom));
        PersistNow();
    }

    public void SetBand(int index, double value)
    {
        if (index < 0 || index >= EqualizerCatalog.BandCount)
        {
            return;
        }

        var next = (double[])_gains.Clone();
        next[index] = Math.Clamp(value, EqualizerCatalog.MinGain, EqualizerCatalog.MaxGain);
        _gains = next;
        _presetId = EqualizerCatalog.CustomId;
        _customGains = (double[])next.Clone();
        Raise(nameof(Gains));
        Raise(nameof(PresetId));
        Raise(nameof(Preset));
        Raise(nameof(IsCustom));
        SchedulePersist();
    }

    public void ResetFlat() => ApplyPreset(EqualizerCatalog.FlatId);

    public string PresetLabelKey => EqualizerCatalog.ById(_presetId).LabelKey;

    private void SchedulePersist()
    {
        _pendingPersist = true;
        _persistTimer.Change(TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
    }

    private void PersistNow()
    {
        _pendingPersist = false;
        _persistTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _preferences.Set("eq_enabled", _enabled);
        _preferences.Set("eq_preset", _presetId);
        _preferences.Set("eq_gains", GainsJson.Serialize(_gains));
        _preferences.Set("eq_custom_gains", GainsJson.Serialize(_customGains));
    }

    public void Dispose()
    {
        if (_pendingPersist)
        {
            PersistNow();
        }

        _persistTimer.Dispose();
    }

    private static class GainsJson
    {
        public static string Serialize(IEnumerable<double> gains) =>
            "[" + string.Join(",", gains.Select(value => value.ToString(
                System.Globalization.CultureInfo.InvariantCulture))) + "]";
    }
}
