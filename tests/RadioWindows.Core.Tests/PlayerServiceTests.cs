using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class PlayerServiceTests
{
    [Fact]
    public async Task Plays_a_station_and_resolves_the_track()
    {
        using var fixture = new TestPreferences();
        var library = new StationLibrary(fixture.Preferences);
        library.Load();
        var equalizer = new EqualizerService(fixture.Preferences);
        using var player = new RadioPlayerService(
            new AppSettings(fixture.Preferences),
            library,
            equalizer,
            new FixedLookup(),
            new NullAudioEngine(),
            new ImmediateDispatcher(),
            new NullIcyMetadataReader());

        var station = library.Stations.First(item => item.Id == "soma-groove");
        player.Play(station);

        var deadline = DateTime.UtcNow.AddSeconds(4);
        while ((player.NowPlaying is null || player.Status != PlaybackState.Playing)
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(PlaybackState.Playing, player.Status);
        Assert.NotNull(player.NowPlaying);
        Assert.Contains("Kerala", player.NowPlaying!.TrayLine);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Reconnects_after_an_engine_error()
    {
        using var fixture = new TestPreferences();
        var library = new StationLibrary(fixture.Preferences);
        library.Load();
        var equalizer = new EqualizerService(fixture.Preferences);
        var engine = new NullAudioEngine { FailOnPlay = true };
        using var player = new RadioPlayerService(
            new AppSettings(fixture.Preferences),
            library,
            equalizer,
            new FixedLookup(),
            engine,
            new ImmediateDispatcher(),
            new NullIcyMetadataReader());

        player.Play(library.Stations[0]);
        await Task.Delay(1500);
        Assert.True(player.Status is PlaybackState.Reconnecting or PlaybackState.Error);
        Assert.Contains("Переподключение", player.ErrorMessage);
    }

    private sealed class FixedLookup : INowPlayingLookup
    {
        public Task<NowPlaying?> ResolveAsync(
            string streamUrl,
            string stationName,
            bool allowIcy,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NowPlaying?>(new NowPlaying
            {
                Raw = "Bonobo — Kerala",
                Artist = "Bonobo",
                Title = "Kerala",
            });

        public Task<string?> ArtworkForAsync(NowPlaying track, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
