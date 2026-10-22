using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>Engine-neutral playback state reported by the audio engine.</summary>
public enum PlaybackState
{
    Idle,
    Loading,
    Playing,
    Paused,
    Reconnecting,
    Error,
}

/// <summary>Audio backend (BASS on Windows, a silent engine in tests).</summary>
public interface IAudioEngine : IDisposable
{
    string Name { get; }

    event Action<PlaybackState>? StateChanged;

    /// <summary>Raised when the engine reads a new ICY title itself.</summary>
    event Action<string>? MetadataChanged;

    /// <summary>True when <see cref="MetadataChanged"/> is fed by the engine, so no side-channel is needed.</summary>
    bool ProvidesMetadata { get; }

    void Play(Uri url, double volume);

    void Pause();

    void Resume();

    /// <summary>Stops playback. With <paramref name="emitIdle"/> false the engine stays silent
    /// about the transition, so an intentional restart is not treated as a failure.</summary>
    void Stop(bool emitIdle = true);

    void SetVolume(double volume);

    void SetEqualizer(bool enabled, IReadOnlyList<double> gains);

    Task<StreamInfo?> FetchStreamInfoAsync(CancellationToken cancellationToken);
}

/// <summary>Finds station artwork (Radio Browser → stream homepage → favicon).</summary>
public interface ILogoResolver
{
    Task<string?> ResolveAsync(string streamUrl, string? homepage, string stationName, CancellationToken cancellationToken = default);
}

/// <summary>Track metadata from ICY, Icecast, SomaFM, Radio Paradise and iTunes artwork.</summary>
public interface INowPlayingLookup
{
    Task<NowPlaying?> ResolveAsync(string streamUrl, string stationName, bool allowIcy, CancellationToken cancellationToken = default);

    Task<string?> ArtworkForAsync(NowPlaying track, CancellationToken cancellationToken = default);
}

/// <summary>Reads ICY metadata blocks from the stream side-channel.</summary>
public interface IIcyMetadataReader
{
    event Action<string>? TitleChanged;

    Task RunAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>Sleep-timer fade target.</summary>
public interface IPlaybackFade
{
    void SetFadeFactor(double factor);

    void Pause();
}

/// <summary>The alarm clock's view of the player.</summary>
public interface IPlaybackTarget
{
    void Play(RadioStation station, bool persist = true);

    void Pause();

    void Stop();
}
