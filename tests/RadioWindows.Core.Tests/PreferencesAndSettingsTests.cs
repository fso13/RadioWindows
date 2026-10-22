using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class PreferencesAndSettingsTests
{
    [Fact]
    public void Values_round_trip_through_the_file()
    {
        using var fixture = new TestPreferences();
        fixture.Preferences.Set("volume", 0.4);
        fixture.Preferences.Set("flag", true);
        fixture.Preferences.Set("name", "Radio");
        fixture.Preferences.Flush();

        var reloaded = new Preferences(fixture.Paths.SettingsFile);
        Assert.Equal(0.4, reloaded.GetDoubleLike("volume"));
        Assert.True(reloaded.GetBoolLike("flag"));
        Assert.Equal("Radio", reloaded.GetString("name"));
    }

    [Fact]
    public void Accepts_numbers_stored_as_strings_like_flutter()
    {
        using var fixture = new TestPreferences();
        fixture.Preferences.Set("volume", "0.65");
        fixture.Preferences.Set("resume_last_station", "true");
        Assert.Equal(0.65, fixture.Preferences.GetDoubleLike("volume"));
        Assert.True(fixture.Preferences.GetBoolLike("resume_last_station"));
    }

    [Fact]
    public void Flutter_keys_are_migrated_once()
    {
        using var fixture = new TestPreferences();
        fixture.Preferences.Set("flutter.volume", 0.25);
        fixture.Preferences.Set("flutter.resume_last_station", true);

        fixture.Preferences.MigrateFlutterKeysIfNeeded();
        Assert.Equal(0.25, fixture.Preferences.GetDoubleLike("volume"));
        Assert.True(fixture.Preferences.GetBoolLike("resume_last_station"));

        fixture.Preferences.Set("flutter.volume", 0.9);
        fixture.Preferences.MigrateFlutterKeysIfNeeded();
        Assert.Equal(0.25, fixture.Preferences.GetDoubleLike("volume"));
    }

    [Fact]
    public void Settings_clamp_volume_and_persist()
    {
        using var fixture = new TestPreferences();
        var settings = new AppSettings(fixture.Preferences);
        Assert.Equal(0.8, settings.Volume);

        settings.SetVolume(3);
        settings.SetResumeLastStation(true);
        settings.SetLastStationId("rp-marusya");
        fixture.Preferences.Flush();

        var reloaded = new AppSettings(fixture.Preferences);
        Assert.Equal(1, reloaded.Volume);
        Assert.True(reloaded.ResumeLastStation);
        Assert.Equal("rp-marusya", reloaded.LastStationId);
    }

    [Fact]
    public void Theme_preference_round_trips()
    {
        using var fixture = new TestPreferences();
        var theme = new ThemeService(fixture.Preferences);
        Assert.Equal(ThemePreference.System, theme.Preference);
        theme.SetPreference(ThemePreference.Dark);
        fixture.Preferences.Flush();
        Assert.Equal(ThemePreference.Dark, new ThemeService(fixture.Preferences).Preference);
    }

    [Fact]
    public void Hotkeys_parse_and_display()
    {
        var combo = HotKeyCombo.Parse("Ctrl+Alt+Right", HotKeyCombo.DefaultNext);
        Assert.Equal(HotKeyCombo.DefaultNext, combo);
        Assert.Equal("Ctrl+Alt+→", combo.Display);
        Assert.Equal("Ctrl+Alt+Right", combo.ToStorage());
        Assert.Equal(HotKeyCombo.DefaultPlayPause, HotKeyCombo.Parse("garbage", HotKeyCombo.DefaultPlayPause));
    }

    [Fact]
    public void Sleep_timer_formats_mm_ss()
    {
        Assert.Equal("05:00", SleepTimerService.Format(TimeSpan.FromMinutes(5)));
        Assert.Equal("00:59", SleepTimerService.Format(TimeSpan.FromSeconds(58.2)));
        Assert.Equal("00:00", SleepTimerService.Format(TimeSpan.FromSeconds(-1)));
    }
}
