using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class AlarmServiceTests
{
    [Fact]
    public void Settings_are_persisted_and_restored()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var player = new FakePlaybackTarget();
        var fade = new FakeFade();
        var notifications = new FakeNotifications();
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));

        using (var alarm = CreateAlarm(preferences, library, player, fade, notifications, now))
        {
            alarm.SetEnabled(true);
            alarm.SetTime(6, 15);
            alarm.ToggleWeekday(1);
            alarm.SetStation(station.Id);
            alarm.SetFadeInSeconds(45);

            Assert.True(alarm.Settings.Enabled);
            Assert.Equal(6, alarm.Settings.Hour);
            Assert.Equal(15, alarm.Settings.Minute);
            Assert.Contains(1, alarm.Settings.Weekdays);
            Assert.Equal(station.Id, alarm.Settings.StationId);
            Assert.Equal(45, alarm.Settings.FadeInSeconds);
        }

        var json = preferences.Preferences.GetString(AlarmService.SettingsKey);
        Assert.NotNull(json);
        Assert.Contains("\"stationID\":", json);

        using var restored = CreateAlarm(preferences, library, player, fade, notifications, now);
        Assert.True(restored.Settings.Enabled);
        Assert.Equal(6, restored.Settings.Hour);
        Assert.Equal(15, restored.Settings.Minute);
        Assert.Contains(1, restored.Settings.Weekdays);
        Assert.Equal(station.Id, restored.Settings.StationId);
        Assert.Equal(45, restored.Settings.FadeInSeconds);
    }

    [Fact]
    public void Values_are_clamped()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out _);
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, new FakePlaybackTarget(), new FakeFade(),
            new FakeNotifications(), now);

        alarm.SetTime(99, -5);
        alarm.SetFadeInSeconds(999);
        alarm.ToggleWeekday(9);

        Assert.Equal(23, alarm.Settings.Hour);
        Assert.Equal(0, alarm.Settings.Minute);
        Assert.Equal(120, alarm.Settings.FadeInSeconds);
        Assert.DoesNotContain(9, alarm.Settings.Weekdays);
    }

    [Fact]
    public void Fire_now_plays_the_selected_station_and_notifies()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var player = new FakePlaybackTarget();
        var fade = new FakeFade();
        var notifications = new FakeNotifications();
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, player, fade, notifications, now);

        alarm.SetStation(station.Id);
        alarm.SetFadeInSeconds(0);
        alarm.FireNow();

        Assert.Same(station, Assert.Single(player.Played));
        var message = Assert.Single(notifications.Snapshot());
        Assert.Equal(Loc.T("Alarm.NotificationTitle"), message.Title);
        Assert.Equal(Loc.Format("Alarm.NotificationBody", station.Name), message.Body);
        Assert.Equal(1, fade.Snapshot()[^1]);
        Assert.False(alarm.NotificationDenied);
    }

    [Fact]
    public void Fade_in_starts_at_zero_and_stop_restores_full_volume()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var fade = new FakeFade();
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, new FakePlaybackTarget(), fade,
            new FakeNotifications(), now);

        alarm.SetStation(station.Id);
        alarm.SetFadeInSeconds(5);
        alarm.FireNow();

        Assert.Equal(0, fade.Snapshot()[0]);

        alarm.Stop();
        Assert.Contains(1, fade.Snapshot());
    }

    [Fact]
    public void Fires_once_when_the_scheduled_minute_matches()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var player = new FakePlaybackTarget();
        var notifications = new FakeNotifications();
        // Friday, 23 Oct 2026, 07:30:20 local time.
        var now = new DateTimeOffset(2026, 10, 23, 7, 30, 20, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, player, new FakeFade(),
            notifications, now, TimeSpan.FromMilliseconds(20));

        alarm.SetTime(7, 30);
        alarm.SetStation(station.Id);
        alarm.SetEnabled(true);
        alarm.Start();

        Assert.Single(notifications.Snapshot());
        Thread.Sleep(150);
        Assert.Single(notifications.Snapshot());
        alarm.Stop();
    }

    [Fact]
    public void Does_not_fire_on_an_unselected_weekday()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var notifications = new FakeNotifications();
        var now = new DateTimeOffset(2026, 10, 23, 7, 30, 10, TimeSpan.FromHours(3)); // Friday
        using var alarm = CreateAlarm(preferences, library, new FakePlaybackTarget(), new FakeFade(),
            notifications, now, TimeSpan.FromMilliseconds(20));

        // Keep Monday only; Friday is weekday 6.
        foreach (var weekday in new[] { 3, 4, 5, 6 })
        {
            alarm.ToggleWeekday(weekday);
        }

        alarm.SetTime(7, 30);
        alarm.SetStation(station.Id);
        alarm.SetEnabled(true);
        alarm.Start();

        Assert.Empty(notifications.Snapshot());
        alarm.Stop();
    }

    [Fact]
    public void Disabled_alarm_never_fires_and_has_no_next_date()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var notifications = new FakeNotifications();
        var now = new DateTimeOffset(2026, 10, 23, 7, 30, 10, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, new FakePlaybackTarget(), new FakeFade(),
            notifications, now, TimeSpan.FromMilliseconds(20));

        alarm.SetTime(7, 30);
        alarm.SetStation(station.Id);
        alarm.Start();

        Assert.Empty(notifications.Snapshot());
        Assert.Null(alarm.NextFireDate);
        alarm.Stop();
    }

    [Fact]
    public void Falls_back_to_the_last_station_when_no_station_is_selected()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var player = new FakePlaybackTarget();
        preferences.Preferences.Set("last_station_id", station.Id);
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, player, new FakeFade(),
            new FakeNotifications(), now);

        alarm.FireNow();

        Assert.Same(station, Assert.Single(player.Played));
    }

    [Fact]
    public void Falls_back_to_the_library_when_the_selected_station_is_missing()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        var player = new FakePlaybackTarget();
        var now = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, player, new FakeFade(),
            new FakeNotifications(), now);

        alarm.SetStation("missing");
        alarm.FireNow();

        Assert.Same(station, Assert.Single(player.Played));
    }

    [Fact]
    public void Next_fire_date_is_recomputed_after_changes()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out _);
        var now = new DateTimeOffset(2026, 10, 23, 6, 0, 0, TimeSpan.FromHours(3));
        using var alarm = CreateAlarm(preferences, library, new FakePlaybackTarget(), new FakeFade(),
            new FakeNotifications(), now);

        alarm.SetTime(7, 30);
        alarm.SetEnabled(true);

        Assert.Equal(new DateTimeOffset(2026, 10, 23, 7, 30, 0, TimeSpan.FromHours(3)), alarm.NextFireDate);

        alarm.SetEnabled(false);
        Assert.Null(alarm.NextFireDate);
    }

    private static StationLibrary CreateLibrary(TestPreferences preferences, out RadioStation station)
    {
        var library = new StationLibrary(preferences.Preferences);
        station = library.Add("Test FM", "https://example.com/test.mp3", "Разное");
        return library;
    }

    private static AlarmService CreateAlarm(
        TestPreferences preferences,
        StationLibrary library,
        FakePlaybackTarget player,
        FakeFade fade,
        FakeNotifications notifications,
        DateTimeOffset now,
        TimeSpan? interval = null) =>
        new(
            preferences.Preferences,
            library,
            player,
            fade,
            new ImmediateDispatcher(),
            notifications,
            () => now,
            interval);

    private sealed class FakePlaybackTarget : IPlaybackTarget
    {
        private readonly object _gate = new();

        public List<RadioStation> Played { get; } = [];

        public void Play(RadioStation station, bool persist = true)
        {
            lock (_gate)
            {
                Played.Add(station);
            }
        }

        public void Pause()
        {
        }

        public void Stop()
        {
        }
    }

    private sealed class FakeFade : IPlaybackFade
    {
        private readonly object _gate = new();
        private readonly List<double> _factors = [];

        public void SetFadeFactor(double factor)
        {
            lock (_gate)
            {
                _factors.Add(factor);
            }
        }

        public void Pause()
        {
        }

        public List<double> Snapshot()
        {
            lock (_gate)
            {
                return [.. _factors];
            }
        }
    }

    private sealed class FakeNotifications : INotificationSink
    {
        private readonly object _gate = new();
        private readonly List<(string Title, string Body)> _messages = [];

        public void Show(string title, string body)
        {
            lock (_gate)
            {
                _messages.Add((title, body));
            }
        }

        public List<(string Title, string Body)> Snapshot()
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }
}
