using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// libVLC backend: HLS, AAC, MP3, ICY streams and a native 10-band equalizer.
/// Volume and equalizer changes apply without restarting the stream.
/// </summary>
public sealed class VlcAudioEngine : IAudioEngine
{
    private const int NetworkCachingMs = 1_000;

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly StreamProbe _probe;
    private readonly object _gate = new();
    private Media? _media;
    private Equalizer? _equalizer;
    private bool _stopping;

    public VlcAudioEngine()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC(
            "--no-video",
            "--no-osd",
            "--no-snapshot-preview",
            "--quiet",
            $"--network-caching={NetworkCachingMs}",
            $"--live-caching={NetworkCachingMs}",
            "--file-caching=1000");
        _mediaPlayer = new MediaPlayer(_libVlc)
        {
            EnableHardwareDecoding = false,
        };

        _mediaPlayer.Playing += (_, _) => Raise(PlaybackState.Playing);
        _mediaPlayer.Paused += (_, _) => Raise(PlaybackState.Paused);
        _mediaPlayer.Buffering += (_, _) => { };
        _mediaPlayer.Stopped += (_, _) =>
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    return;
                }
            }

            Raise(PlaybackState.Idle);
        };
        _mediaPlayer.EndReached += (_, _) => Raise(PlaybackState.Idle);
        _mediaPlayer.EncounteredError += (_, _) => Raise(PlaybackState.Error);
        _probe = new StreamProbe();
    }

    public string Name => "libVLC 3";

    public event Action<PlaybackState>? StateChanged;

    public void Play(Uri url, double volume)
    {
        lock (_gate)
        {
            _stopping = false;
            _mediaPlayer.Stop();
            _media?.Dispose();
            _media = new Media(_libVlc, url);
            _media.AddOption($":network-caching={NetworkCachingMs}");
            _media.AddOption(":http-reconnect=true");
            _mediaPlayer.Volume = ToPercent(volume);
            _mediaPlayer.Play(_media);
        }
    }

    public void Pause()
    {
        _mediaPlayer.SetPause(true);
        Raise(PlaybackState.Paused);
    }

    public void Resume()
    {
        _mediaPlayer.SetPause(false);
        Raise(PlaybackState.Playing);
    }

    public void Stop(bool emitIdle = true)
    {
        lock (_gate)
        {
            _stopping = true;
        }

        _mediaPlayer.Stop();
        if (emitIdle)
        {
            Raise(PlaybackState.Idle);
        }
    }

    public void SetVolume(double volume) => _mediaPlayer.Volume = ToPercent(volume);

    public void SetEqualizer(bool enabled, IReadOnlyList<double> gains)
    {
        if (!enabled)
        {
            _mediaPlayer.SetEqualizer(null!);
            return;
        }

        var equalizer = _equalizer ??= new Equalizer();
        equalizer.SetPreamp(0);
        var bands = (int)Math.Min((uint)EqualizerCatalog.BandCount, equalizer.BandCount);
        for (uint band = 0; band < bands; band++)
        {
            var index = (int)band;
            var gain = index < gains.Count ? (float)gains[index] : 0f;
            equalizer.SetAmp((float)gain, band);
        }

        _mediaPlayer.SetEqualizer(equalizer);
    }

    public async Task<StreamInfo?> FetchStreamInfoAsync(CancellationToken cancellationToken)
    {
        StreamInfo? parsed = null;
        try
        {
            var media = _mediaPlayer.Media;
            if (media is not null)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                await media.Parse(MediaParseOptions.ParseNetwork, 6000, timeout.Token).ConfigureAwait(false);
                foreach (var track in media.Tracks)
                {
                    if (track.TrackType != TrackType.Audio)
                    {
                        continue;
                    }

                    var data = track.Data.Audio;
                    parsed = new StreamInfo(
                        StreamInfo.CodecNameFromFourCc((uint)track.Codec),
                        (int)data.Rate,
                        (int)data.Channels,
                        null,
                        NetworkCachingMs);
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or VLCException
                                           or InvalidOperationException or ObjectDisposedException)
        {
            // Fall through to the HTTP probe.
        }

        var probe = await _probe.ProbeAsync(_mediaPlayer.Media?.Mrl ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        if (probe is null)
        {
            return parsed;
        }

        var server = probe.Server ?? HostOf(_mediaPlayer.Media?.Mrl);
        if (parsed is null)
        {
            return new StreamInfo(
                probe.Codec,
                0,
                0,
                probe.BitrateKbps,
                NetworkCachingMs,
                server);
        }

        return parsed with
        {
            Codec = parsed.Codec is "—" or "AUDIO" ? probe.Codec : parsed.Codec,
            BitrateKbps = parsed.BitrateKbps ?? probe.BitrateKbps,
            Server = server,
        };
    }

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static int ToPercent(double volume) => (int)Math.Round(Math.Clamp(volume, 0, 1) * 100);

    private void Raise(PlaybackState state) => StateChanged?.Invoke(state);

    public void Dispose()
    {
        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Dispose();
            _media?.Dispose();
            _libVlc.Dispose();
        }
        catch (Exception exception) when (exception is ObjectDisposedException or VLCException
                                           or DllNotFoundException)
        {
            // Shutdown is best effort.
        }
    }
}

/// <summary>Creates the engine that works on this machine, falling back to silence.</summary>
public static class AudioEngineFactory
{
    public static IAudioEngine Create(Action<string>? warn = null)
    {
        try
        {
            return new VlcAudioEngine();
        }
        catch (Exception exception) when (exception is DllNotFoundException or VLCException
                                           or InvalidOperationException or TypeInitializationException
                                           or ArgumentException)
        {
            warn?.Invoke($"libVLC is unavailable ({exception.GetType().Name}); audio is disabled.");
            return new NullAudioEngine();
        }
    }
}
