using System.Reflection;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class TrackHistoryTests
{
    [Fact]
    public void Entries_are_persisted_and_restored_in_mac_keys()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        var now = DateTimeOffset.Now;

        using (var history = new TrackHistoryService(preferences.Preferences, player, () => now))
        {
            history.Add(station, new NowPlaying { Artist = "Artist", Title = "Title" });
            Assert.Single(history.Entries);
            Assert.False(history.IsEmpty);
        }

        var json = preferences.Preferences.GetString(TrackHistoryService.HistoryKey);
        Assert.NotNull(json);
        Assert.Contains("\"stationID\":", json);
        Assert.Contains("\"stationName\":", json);
        Assert.Contains("\"playedAt\":", json);

        using var restored = new TrackHistoryService(preferences.Preferences, player);
        var entry = Assert.Single(restored.Entries);
        Assert.Equal(station.Id, entry.StationId);
        Assert.Equal(station.Name, entry.StationName);
        Assert.Equal("Artist", entry.Artist);
        Assert.Equal("Title", entry.Title);
        Assert.Equal("Artist — Title", entry.Line);
    }

    [Fact]
    public void Duplicates_of_the_same_title_and_station_are_ignored_for_twenty_seconds()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        var now = DateTimeOffset.Now;
        using var history = new TrackHistoryService(preferences.Preferences, player, () => now);

        history.Add(station, new NowPlaying { Title = "Same" });
        history.Add(station, new NowPlaying { Title = "Same" });
        Assert.Single(history.Entries);

        now = now.AddSeconds(21);
        history.Add(station, new NowPlaying { Title = "Same" });
        Assert.Equal(2, history.Entries.Count);

        history.Add(station, new NowPlaying { Title = "Other" });
        Assert.Equal(3, history.Entries.Count);
        Assert.Equal("Other", history.Entries[0].Title);
    }

    [Fact]
    public void Keeps_only_the_newest_hundred_entries()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        var now = DateTimeOffset.Now;
        using var history = new TrackHistoryService(preferences.Preferences, player, () => now);

        for (var index = 0; index < 105; index++)
        {
            history.Add(station, new NowPlaying { Title = $"Track {index}" });
        }

        Assert.Equal(TrackHistoryService.MaxEntries, history.Entries.Count);
        Assert.Equal("Track 104", history.Entries[0].Title);
        Assert.Equal("Track 5", history.Entries[^1].Title);
    }

    [Fact]
    public void Search_filters_by_station_and_track()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        using var history = new TrackHistoryService(preferences.Preferences, player);

        history.Add(station, new NowPlaying { Artist = "Queen", Title = "Alpha" });
        history.Add(station, new NowPlaying { Artist = "Queen", Title = "Beta" });

        history.SearchText = "alpha";
        var filtered = Assert.Single(history.Filtered);
        Assert.Equal("Alpha", filtered.Title);

        history.SearchText = "test fm";
        Assert.Equal(2, history.Filtered.Count);

        history.SearchText = "missing";
        Assert.Empty(history.Filtered);

        history.SearchText = string.Empty;
        Assert.Equal(2, history.Filtered.Count);
    }

    [Fact]
    public void Copy_text_contains_date_station_and_line()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        using var history = new TrackHistoryService(preferences.Preferences, player);

        history.Add(station, new NowPlaying { Artist = "Artist", Title = "Song" });
        var text = history.CopyText();

        Assert.Contains(station.Name, text);
        Assert.Contains("Artist — Song", text);
        Assert.Contains("·", text);
    }

    [Fact]
    public void Clear_removes_entries_and_the_stored_history()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        using var history = new TrackHistoryService(preferences.Preferences, player);

        history.Add(station, new NowPlaying { Title = "Song" });
        history.Clear();

        Assert.Empty(history.Entries);
        Assert.True(history.IsEmpty);
        Assert.Null(preferences.Preferences.GetString(TrackHistoryService.HistoryKey));
    }

    [Fact]
    public void Records_when_the_player_reports_now_playing_while_playing()
    {
        using var preferences = new TestPreferences();
        var library = CreateLibrary(preferences, out var station);
        using var equalizer = new EqualizerService(preferences.Preferences);
        using var player = CreatePlayer(preferences, library, equalizer);
        using var history = new TrackHistoryService(preferences.Preferences, player);

        PublishNowPlaying(player, station, new NowPlaying { Artist = "A", Title = "First" }, PlaybackState.Playing);
        Assert.Single(history.Entries);

        PublishNowPlaying(player, station, new NowPlaying { Artist = "A", Title = "Second" }, PlaybackState.Paused);
        Assert.Single(history.Entries);
    }

    private static StationLibrary CreateLibrary(TestPreferences preferences, out RadioStation station)
    {
        var library = new StationLibrary(preferences.Preferences);
        station = library.Add("Test FM", "https://example.com/test.mp3", "Разное");
        return library;
    }

    private static RadioPlayerService CreatePlayer(
        TestPreferences preferences,
        StationLibrary library,
        EqualizerService equalizer) =>
        new(
            new AppSettings(preferences.Preferences),
            library,
            equalizer,
            new NoopNowPlayingLookup(),
            new NullAudioEngine(),
            new ImmediateDispatcher());

    /// <summary>
    /// The player exposes read-only state; tests publish a metadata change the
    /// same way the metadata pipeline does (fields plus a property notification).
    /// </summary>
    private static void PublishNowPlaying(
        RadioPlayerService player,
        RadioStation station,
        NowPlaying track,
        PlaybackState status)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(RadioPlayerService);
        type.GetField("_station", flags)!.SetValue(player, station);
        type.GetField("_nowPlaying", flags)!.SetValue(player, track);
        type.GetField("_status", flags)!.SetValue(player, status);

        var raise = typeof(ObservableObject).GetMethod("Raise", flags)!;
        raise.Invoke(player, [nameof(RadioPlayerService.NowPlaying)]);
    }
}
