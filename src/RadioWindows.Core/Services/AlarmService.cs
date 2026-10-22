using System.Text;
using System.Text.Json;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>Desktop notification surface used by the wake-up radio.</summary>
public interface INotificationSink
{
    void Show(string title, string body);
}

/// <summary>
/// Wake-up radio: a weekday schedule, station selection, a volume fade-in and a
/// desktop notification. The alarm works while the app is running; a ten-second
/// timer polls the local clock, so a scheduled minute is still caught after the
/// machine wakes from sleep. Settings are persisted under <see cref="SettingsKey"/>.
/// </summary>
public sealed class AlarmService : ObservableObject, IDisposable
{
    /// <summary>Preferences key holding the serialised <see cref="AlarmSettings"/>.</summary>
    public const string SettingsKey = "alarm_settings";

    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(10);
    private const string LastStationKey = "last_station_id";

    private readonly Preferences _preferences;
    private readonly StationLibrary _library;
    private readonly IPlaybackTarget _player;
    private readonly IPlaybackFade _fade;
    private readonly IUiDispatcher _dispatcher;
    private readonly INotificationSink _notifications;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeSpan _checkInterval;
    private readonly Timer _timer;
    private readonly object _gate = new();

    private readonly AlarmSettings _settings;
    private DateTimeOffset? _nextFireDate;
    private DateTimeOffset? _lastFiredMinute;
    private CancellationTokenSource? _fadeCancellation;
    private bool _running;
    private bool _disposed;

    public AlarmService(
        Preferences preferences,
        StationLibrary library,
        IPlaybackTarget player,
        IPlaybackFade fade,
        IUiDispatcher dispatcher,
        INotificationSink notifications,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? checkInterval = null)
    {
        _preferences = preferences;
        _library = library;
        _player = player;
        _fade = fade;
        _dispatcher = dispatcher;
        _notifications = notifications;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _checkInterval = checkInterval is { TotalMilliseconds: > 0 } interval ? interval : DefaultCheckInterval;

        _settings = LoadSettings(preferences);
        _nextFireDate = _settings.NextFireDate(_clock());
        _timer = new Timer(OnTimer, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The live settings object; mutate through the service so changes persist.</summary>
    public AlarmSettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public DateTimeOffset? NextFireDate
    {
        get
        {
            lock (_gate)
            {
                return _nextFireDate;
            }
        }
    }

    /// <summary>Kept for parity with macOS; Windows notifications need no permission prompt.</summary>
    public bool NotificationDenied { get; private set; }

    // MARK: - Lifecycle

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        if (!_running)
        {
            _running = true;
            try
            {
                _timer.Change(_checkInterval, _checkInterval);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }

        // Catch up when the app launched inside the scheduled minute (the macOS
        // timer behaves the same: a missed fire date fires immediately).
        _dispatcher.Post(CheckDue);
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }

        CancelFade(restore: true);
    }

    // MARK: - Settings

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_settings.Enabled == enabled)
            {
                return;
            }

            _settings.Enabled = enabled;
        }

        if (!enabled)
        {
            CancelFade(restore: true);
        }

        AfterChange();
    }

    public void SetTime(int hour, int minute)
    {
        var clampedHour = Math.Clamp(hour, 0, 23);
        var clampedMinute = Math.Clamp(minute, 0, 59);

        lock (_gate)
        {
            if (_settings.Hour == clampedHour && _settings.Minute == clampedMinute)
            {
                return;
            }

            _settings.Hour = clampedHour;
            _settings.Minute = clampedMinute;
        }

        AfterChange();
    }

    public void ToggleWeekday(int weekday)
    {
        if (weekday is < 1 or > 7)
        {
            return;
        }

        lock (_gate)
        {
            if (!_settings.Weekdays.Remove(weekday))
            {
                _settings.Weekdays.Add(weekday);
            }
        }

        AfterChange();
    }

    public void SetStation(string? stationId)
    {
        var value = string.IsNullOrWhiteSpace(stationId) ? null : stationId.Trim();

        lock (_gate)
        {
            if (string.Equals(_settings.StationId, value, StringComparison.Ordinal))
            {
                return;
            }

            _settings.StationId = value;
        }

        AfterChange();
    }

    public void SetFadeInSeconds(int seconds)
    {
        var clamped = Math.Clamp(seconds, 0, 120);

        lock (_gate)
        {
            if (_settings.FadeInSeconds == clamped)
            {
                return;
            }

            _settings.FadeInSeconds = clamped;
        }

        AfterChange();
    }

    /// <summary>Fires the alarm immediately; used by tests and the settings preview.</summary>
    public void FireNow()
    {
        if (_disposed)
        {
            return;
        }

        AlarmSettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        FireCore(settings, _clock());
    }

    // MARK: - Scheduling

    private void OnTimer(object? state)
    {
        if (_disposed)
        {
            return;
        }

        _dispatcher.Post(CheckDue);
    }

    private void CheckDue()
    {
        if (_disposed)
        {
            return;
        }

        AlarmSettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        if (!settings.Enabled)
        {
            RefreshNextFireDate();
            return;
        }

        RefreshNextFireDate();

        var now = _clock();
        if (settings.Weekdays.Count == 0 || !settings.Weekdays.Contains(WeekdayOf(now)))
        {
            return;
        }

        if (now.Hour != settings.Hour || now.Minute != settings.Minute)
        {
            return;
        }

        var minute = MinuteOf(now);
        lock (_gate)
        {
            if (_lastFiredMinute == minute)
            {
                return;
            }
        }

        FireCore(settings, now);
    }

    private void FireCore(AlarmSettings settings, DateTimeOffset now)
    {
        lock (_gate)
        {
            _lastFiredMinute = MinuteOf(now);
        }

        var station = ResolveStation(settings);
        RefreshNextFireDate();
        if (station is null)
        {
            return;
        }

        try
        {
            _player.Play(station);
        }
        catch (Exception)
        {
            // An audio backend failure must not stop the scheduler.
        }

        StartFade(settings.FadeInSeconds);

        try
        {
            _notifications.Show(
                Loc.T("Alarm.NotificationTitle"),
                Loc.Format("Alarm.NotificationBody", station.Name));
        }
        catch (Exception)
        {
            // Notification errors are non-fatal.
        }
    }

    private RadioStation? ResolveStation(AlarmSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.StationId)
            && _library.ById(settings.StationId) is { } configured)
        {
            return configured;
        }

        // The alarm ports against IPlaybackTarget, but the real player exposes
        // the current station; fall back to the persisted last station, then the
        // first library entry.
        if (_player is RadioPlayerService radio && radio.Station is { } current)
        {
            return current;
        }

        var lastId = _preferences.GetString(LastStationKey);
        if (!string.IsNullOrWhiteSpace(lastId) && _library.ById(lastId) is { } last)
        {
            return last;
        }

        return _library.Stations.FirstOrDefault();
    }

    private void RefreshNextFireDate()
    {
        AlarmSettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        var next = settings.NextFireDate(_clock());
        var changed = false;
        lock (_gate)
        {
            if (_nextFireDate != next)
            {
                _nextFireDate = next;
                changed = true;
            }
        }

        if (changed)
        {
            Notify(nameof(NextFireDate));
        }
    }

    // MARK: - Fade-in

    private void StartFade(int seconds)
    {
        CancelFade(restore: false);

        if (seconds <= 0)
        {
            SafeSetFadeFactor(1);
            return;
        }

        SafeSetFadeFactor(0);

        var cancellation = new CancellationTokenSource();
        _fadeCancellation = cancellation;
        var token = cancellation.Token;
        var steps = seconds * 10; // one step every 100 ms

        _ = Task.Run(async () =>
        {
            try
            {
                for (var step = 1; step <= steps; step++)
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    _fade.SetFadeFactor((double)step / steps);
                }
            }
            catch (OperationCanceledException)
            {
                // The alarm was disabled or the app is shutting down.
            }
            catch (ObjectDisposedException)
            {
                // Cancellation raced with disposal.
            }
        });
    }

    private void CancelFade(bool restore)
    {
        var cancellation = _fadeCancellation;
        _fadeCancellation = null;
        if (cancellation is not null)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already cancelled.
            }

            cancellation.Dispose();
        }

        if (restore)
        {
            SafeSetFadeFactor(1);
        }
    }

    private void SafeSetFadeFactor(double factor)
    {
        try
        {
            _fade.SetFadeFactor(factor);
        }
        catch (Exception)
        {
            // The fade target is optional during shutdown.
        }
    }

    // MARK: - Helpers

    private static int WeekdayOf(DateTimeOffset value) => (int)value.DayOfWeek + 1;

    private static DateTimeOffset MinuteOf(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);

    private void AfterChange()
    {
        Persist();
        RefreshNextFireDate();
        Notify(nameof(Settings));
    }

    private void Notify(params string[] propertyNames) => _dispatcher.Post(() =>
    {
        foreach (var name in propertyNames)
        {
            Raise(name);
        }
    });

    // MARK: - Persistence

    private void Persist()
    {
        AlarmSettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        try
        {
            _preferences.Set(SettingsKey, Serialize(settings));
        }
        catch (Exception)
        {
            // Preference storage is best effort.
        }
    }

    private static AlarmSettings LoadSettings(Preferences preferences)
    {
        try
        {
            var raw = preferences.GetString(SettingsKey);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parsed = Deserialize(raw);
                if (parsed is not null)
                {
                    return parsed.Normalized();
                }
            }
        }
        catch (Exception)
        {
            // A corrupted entry falls back to the defaults.
        }

        return new AlarmSettings().Normalized();
    }

    private static string Serialize(AlarmSettings settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("enabled", settings.Enabled);
            writer.WriteNumber("hour", settings.Hour);
            writer.WriteNumber("minute", settings.Minute);
            writer.WriteStartArray("weekdays");
            foreach (var weekday in settings.Weekdays.Order())
            {
                writer.WriteNumberValue(weekday);
            }

            writer.WriteEndArray();
            if (settings.StationId is null)
            {
                writer.WriteNull("stationID");
            }
            else
            {
                writer.WriteString("stationID", settings.StationId);
            }

            writer.WriteNumber("fadeInSeconds", settings.FadeInSeconds);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AlarmSettings? Deserialize(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var settings = new AlarmSettings
        {
            Enabled = ReadBool(root, "enabled") ?? false,
            Hour = ReadInt(root, "hour") ?? 7,
            Minute = ReadInt(root, "minute") ?? 30,
            StationId = ReadString(root, "stationID"),
            FadeInSeconds = ReadInt(root, "fadeInSeconds") ?? 30,
        };

        var weekdays = Find(root, "weekdays");
        if (weekdays is { ValueKind: JsonValueKind.Array })
        {
            foreach (var item in weekdays.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var day))
                {
                    settings.Weekdays.Add(day);
                }
                else if (item.ValueKind == JsonValueKind.String
                         && int.TryParse(item.GetString(), out var parsedDay))
                {
                    settings.Weekdays.Add(parsedDay);
                }
            }
        }

        return settings;
    }

    private static JsonElement? Find(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.Value.TryGetDouble(out var number) => number != 0,
            JsonValueKind.String => value.Value.GetString()?.ToLowerInvariant() switch
            {
                "1" or "true" or "yes" => true,
                "0" or "false" or "no" => false,
                _ => null,
            },
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.Number when value.Value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.Value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string? ReadString(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.Value.GetString())
                ? null
                : value.Value.GetString()!.Trim(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _running = false;
        try
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Already stopped.
        }

        _timer.Dispose();
        CancelFade(restore: true);
    }
}
