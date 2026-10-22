using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>Placeholder used before the network lookups are composed (and in tests).</summary>
public sealed class NoopNowPlayingLookup : INowPlayingLookup
{
    private readonly NowPlaying? _fixed;

    public NoopNowPlayingLookup(NowPlaying? fixedTrack = null) => _fixed = fixedTrack;

    public Task<NowPlaying?> ResolveAsync(
        string streamUrl,
        string stationName,
        bool allowIcy,
        CancellationToken cancellationToken = default) => Task.FromResult(_fixed);

    public Task<string?> ArtworkForAsync(NowPlaying track, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}
