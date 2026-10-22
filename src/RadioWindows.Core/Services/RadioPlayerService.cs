using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// Glue between the audio engine, the station library, track metadata and the
/// reconnect policy. All property changes are raised on the UI dispatcher.
/// </summary>
public sealed class RadioPlayerService : ObservableObject, IPlaybackFade, IPlaybackTarget, IDisposable
{
    private readonly AppSettings _settings;
    private readonly StationLibrary _library;
    private readonly EqualizerService _equalizer;
    private readonly INowPlayingLookup _lookup;
    private readonly IAudioEngine _engine;
    private readonly IIcyMetadataReader _icy;
    private readonly IUiDispatcher _dispatcher;
    private readonly ReconnectPolicy _reconnectPolicy = new();
    private readonly Timer _pollTimer;

    private RadioStation? _station;
    private PlaybackState _status = PlaybackState.Idle;
    private NowPlaying? _nowPlaying;
    private StreamInfo? _streamInfo;
    private string? _errorMessage;
    private double _volume;
    private int _reconnectAttempts;
    private double _transientVolume = 1;
    private bool _wantsPlayback;
    private bool _icyLive;
    private int _generation;
    private CancellationTokenSource? _icyCancellation;
    private string? _icyStationId;
    private CancellationTokenSource? _reconnectCancellation;
    private CancellationTokenSource? _stableCancellation;
    private bool _disposed;

    public RadioPlayerService(
        AppSettings settings,
        StationLibrary library,
        EqualizerService equalizer,
        INowPlayingLookup lookup,
        IAudioEngine engine,
        IUiDispatcher dispatcher,
        IIcyMetadataReader? icy = null)
    {
        _settings = settings;
        _library = library;
        _equalizer = equalizer;
        _lookup = lookup;
        _engine = engine;
        _dispatcher = dispatcher;
        _icy = icy ?? new IcyMetadataReader();
        _volume = settings.Volume;

        _engine.StateChanged += OnEngineState;
        _equalizer.PropertyChanged += (_, _) =>
        {
            _engine.SetEqualizer(_equalizer.Enabled, _equalizer.Gains);
        };
        _engine.SetEqualizer(_equalizer.Enabled, _equalizer.Gains);
        _engine.MetadataChanged += title => _dispatcher.Post(() =>
        {
            if (_station is null)
            {
                return;
            }

            ApplyNowPlaying(NowPlayingParser.Parse(title, _station.Name), fromIcy: true);
        });
        _icy.TitleChanged += title => _dispatcher.Post(() =>
        {
            if (_icyStationId is null || _icyStationId != _station?.Id)
            {
                return;
            }

            ApplyNowPlaying(NowPlayingParser.Parse(title, _station.Name), fromIcy: true);
        });

        _pollTimer = new Timer(_ => PollNowPlaying(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public RadioStation? Station => _station;

    public PlaybackState Status => _status;

    public NowPlaying? NowPlaying => _nowPlaying;

    public StreamInfo? StreamInfo => _streamInfo;

    public string? ErrorMessage => _errorMessage;

    public double Volume => _volume;

    public string ActiveEngineName => _engine.Name;

    public int ReconnectAttempts => _reconnectAttempts;

    public bool IsPlaying => _status == PlaybackState.Playing;

    public bool IsActive => _status is PlaybackState.Playing or PlaybackState.Paused
        or PlaybackState.Loading or PlaybackState.Reconnecting;

    public string? TrackTitle => _nowPlaying?.TrayLine;

    public int MaxReconnectAttempts => _reconnectPolicy.MaxAttempts;

    // MARK: - Lifecycle

    public void Start()
    {
        _engine.SetVolume(EffectiveVolume(_station));
        if (_settings.ResumeLastStation
            && _settings.LastStationId is { } id
            && _library.ById(id) is { } station)
        {
            Play(station);
            return;
        }

        var autoplay = _library.Stations.FirstOrDefault(item => item.Autoplay && item.StreamUri is not null);
        if (autoplay is not null)
        {
            Play(autoplay);
        }
    }

    // MARK: - Transport

    public void Play(RadioStation station, bool persist = true)
    {
        CancelReconnect();
        _reconnectAttempts = 0;
        _wantsPlayback = true;

        _station = station;
        _nowPlaying = null;
        _streamInfo = null;
        _icyLive = false;
        _generation++;
        _errorMessage = null;
        SetStatus(PlaybackState.Loading);
        if (persist)
        {
            _settings.SetLastStationId(station.Id);
        }

        if (station.EqPresetId is { } presetId
            && EqualizerCatalog.Presets.Any(preset => preset.Id == presetId))
        {
            _equalizer.ApplyPreset(presetId);
        }

        BeginPlayback(station);
    }

    private void BeginPlayback(RadioStation station)
    {
        _errorMessage = null;
        SetStatus(PlaybackState.Loading);
        StartPolling();
        StartIcy(station);

        if (station.StreamUri is null)
        {
            _wantsPlayback = false;
            _errorMessage = Loc.T("Player.InvalidUrl");
            SetStatus(PlaybackState.Error);
            StopPolling();
            return;
        }

        _engine.Stop(emitIdle: false);
        _engine.SetVolume(EffectiveVolume(station));
        _engine.Play(station.StreamUri, EffectiveVolume(station));
    }

    /// <summary>Manual retry from the error state.</summary>
    public void RetryNow()
    {
        if (_station is not { } station)
        {
            return;
        }

        CancelReconnect();
        _reconnectAttempts = 0;
        _wantsPlayback = true;
        BeginPlayback(station);
    }

    public void TogglePlayPause()
    {
        switch (_status)
        {
            case PlaybackState.Playing:
                Pause();
                break;
            case PlaybackState.Paused:
                ResumeActive();
                break;
            default:
                if (_station is not null)
                {
                    Play(_station);
                }

                break;
        }
    }

    public void Pause()
    {
        if (_status != PlaybackState.Playing)
        {
            return;
        }

        _wantsPlayback = false;
        CancelReconnect();
        _engine.Pause();
    }

    public void ResumeOrPlay()
    {
        if (_status == PlaybackState.Paused)
        {
            ResumeActive();
        }
        else if (_station is not null)
        {
            Play(_station);
        }
    }

    private void ResumeActive()
    {
        _wantsPlayback = true;
        _engine.Resume();
    }

    public void Stop()
    {
        _wantsPlayback = false;
        _reconnectAttempts = 0;
        CancelReconnect();
        StopPolling();
        _generation++;
        StopIcy();
        _engine.Stop();
        SetStatus(PlaybackState.Idle);
        _nowPlaying = null;
        _streamInfo = null;
        _icyLive = false;
        _errorMessage = null;
    }

    public void SetVolume(double value)
    {
        var clamped = Math.Clamp(value, 0, 1);
        _volume = clamped;
        Raise(nameof(Volume));
        _engine.SetVolume(EffectiveVolume(_station));
        _settings.SetVolume(clamped);
    }

    /// <summary>Sleep-timer fade. Not persisted: the user's volume returns after it ends.</summary>
    public void SetFadeFactor(double factor)
    {
        _transientVolume = Math.Clamp(factor, 0, 1);
        _engine.SetVolume(EffectiveVolume(_station));
    }

    public string? ConsumeError()
    {
        var message = _errorMessage;
        _errorMessage = null;
        Raise(nameof(ErrorMessage));
        return message;
    }

    public void RefreshStreamInfo() => _ = RefreshStreamInfoAsync(force: true);

    // MARK: - Engine events

    private void OnEngineState(PlaybackState state) => _dispatcher.Post(() => HandleEngineState(state));

    private void HandleEngineState(PlaybackState state)
    {
        if (state == PlaybackState.Error)
        {
            if (_wantsPlayback && _station is not null)
            {
                _errorMessage ??= Loc.T("Player.OpenFailed");
                ScheduleReconnect();
                return;
            }

            SetStatus(PlaybackState.Error);
            _errorMessage ??= Loc.T("Player.OpenFailed");
            StopPolling();
            return;
        }

        if (state == PlaybackState.Idle)
        {
            if (_wantsPlayback && _station is not null)
            {
                ScheduleReconnect();
                return;
            }

            if (_status == PlaybackState.Error)
            {
                return;
            }
        }

        if (state == PlaybackState.Playing)
        {
            CancelReconnect();
            ScheduleStableReset();
            if (_errorMessage?.StartsWith("Переподключение", StringComparison.Ordinal) == true)
            {
                _errorMessage = null;
                Raise(nameof(ErrorMessage));
            }
        }

        if (state == _status)
        {
            return;
        }

        SetStatus(state);
        if (state == PlaybackState.Playing)
        {
            _ = RefreshStreamInfoAsync();
        }
        else if (state is PlaybackState.Idle or PlaybackState.Error)
        {
            _streamInfo = null;
            Raise(nameof(StreamInfo));
        }
    }

    private void ScheduleReconnect()
    {
        if (!_wantsPlayback || _station is null || _reconnectCancellation is not null)
        {
            return;
        }

        if (_reconnectPolicy.DelayFor(_reconnectAttempts) is not { } delay)
        {
            _wantsPlayback = false;
            _errorMessage = Loc.T("Player.ReconnectFailed");
            SetStatus(PlaybackState.Error);
            StopPolling();
            return;
        }

        _reconnectAttempts++;
        SetStatus(PlaybackState.Reconnecting);
        _errorMessage = Loc.Format("Player.Reconnecting", _reconnectAttempts, _reconnectPolicy.MaxAttempts);
        Raise(nameof(ErrorMessage));

        var stationId = _station.Id;
        var cancellation = new CancellationTokenSource();
        _reconnectCancellation = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _dispatcher.Post(() =>
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                _reconnectCancellation = null;
                if (!_wantsPlayback || _station?.Id != stationId)
                {
                    return;
                }

                BeginPlayback(_station);
            });
        });
    }

    private void ScheduleStableReset()
    {
        _stableCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _stableCancellation = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _dispatcher.Post(() =>
            {
                if (cancellation.IsCancellationRequested || _status != PlaybackState.Playing)
                {
                    return;
                }

                _reconnectAttempts = 0;
                Raise(nameof(ReconnectAttempts));
                if (_errorMessage?.StartsWith("Переподключение", StringComparison.Ordinal) == true)
                {
                    _errorMessage = null;
                    Raise(nameof(ErrorMessage));
                }
            });
        });
    }

    private void CancelReconnect()
    {
        _reconnectCancellation?.Cancel();
        _reconnectCancellation?.Dispose();
        _reconnectCancellation = null;
        _stableCancellation?.Cancel();
        _stableCancellation?.Dispose();
        _stableCancellation = null;
    }

    private void SetStatus(PlaybackState state)
    {
        if (_status == state)
        {
            return;
        }

        _status = state;
        Raise(nameof(Status));
        Raise(nameof(IsPlaying));
        Raise(nameof(IsActive));
    }

    private async Task RefreshStreamInfoAsync(bool force = false)
    {
        var station = _station;
        if (station is null)
        {
            _streamInfo = null;
            Raise(nameof(StreamInfo));
            return;
        }

        if (!force && _streamInfo is not null)
        {
            return;
        }

        var info = await _engine.FetchStreamInfoAsync(CancellationToken.None).ConfigureAwait(false);
        _dispatcher.Post(() =>
        {
            if (_station?.Id != station.Id)
            {
                return;
            }

            _streamInfo = info;
            Raise(nameof(StreamInfo));
        });
    }

    // MARK: - Track metadata

    private void StartPolling()
    {
        _pollTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(20));
    }

    private void StopPolling() => _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);

    private void PollNowPlaying()
    {
        var station = _station;
        if (station is null || _status is not (PlaybackState.Playing or PlaybackState.Loading))
        {
            return;
        }

        var generation = _generation;
        var allowIcy = !_icyLive && !_engine.ProvidesMetadata;
        _ = Task.Run(async () =>
        {
            var track = await _lookup
                .ResolveAsync(station.Url, station.Name, allowIcy, CancellationToken.None)
                .ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                if (generation != _generation)
                {
                    return;
                }

                ApplyNowPlaying(track, fromIcy: false);
            });
        });
    }

    private void StartIcy(RadioStation station)
    {
        StopIcy();
        if (station.StreamUri is null || _engine.ProvidesMetadata)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _icyCancellation = cancellation;
        _icyStationId = station.Id;
        _ = Task.Run(() => _icy.RunAsync(station.StreamUri, cancellation.Token), cancellation.Token);
    }

    private void StopIcy()
    {
        _icyStationId = null;
        _icyCancellation?.Cancel();
        _icyCancellation?.Dispose();
        _icyCancellation = null;
    }

    private void ApplyNowPlaying(NowPlaying? track, bool fromIcy, string? artworkUrl = null)
    {
        if (track is null)
        {
            return;
        }

        if (fromIcy)
        {
            _icyLive = true;
        }

        var art = artworkUrl ?? track.ArtworkUrl;
        if (_nowPlaying?.Raw == track.Raw)
        {
            if (art is not null && _nowPlaying is { HasArtwork: false })
            {
                _nowPlaying.ArtworkUrl = art;
                Raise(nameof(NowPlaying));
            }

            return;
        }

        track.ArtworkUrl = art;
        _nowPlaying = track;
        Raise(nameof(NowPlaying));
        _ = FillArtworkAsync(track);
    }

    private async Task FillArtworkAsync(NowPlaying track)
    {
        if (track.HasArtwork || _station?.Genre == GenreCatalog.News)
        {
            return;
        }

        var generation = _generation;
        var art = await _lookup.ArtworkForAsync(track, CancellationToken.None).ConfigureAwait(false);
        _dispatcher.Post(() =>
        {
            if (generation != _generation || _nowPlaying?.Raw != track.Raw || art is null)
            {
                return;
            }

            _nowPlaying.ArtworkUrl = art;
            Raise(nameof(NowPlaying));
        });
    }

    // MARK: - Helpers

    private double EffectiveVolume(RadioStation? station)
    {
        var offset = station?.VolumeOffsetDb ?? 0;
        var multiplier = Math.Pow(10, offset / 20);
        return Math.Clamp(_volume * multiplier * _transientVolume, 0, 1);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Dispose();
        CancelReconnect();
        StopIcy();
        _engine.Dispose();
    }
}

/// <summary>Backoff for unexpected stream failures: 1, 2, 5, 10 seconds.</summary>
public sealed class ReconnectPolicy
{
    private readonly TimeSpan[] _delays;

    public ReconnectPolicy(TimeSpan[]? delays = null)
    {
        _delays = delays ?? [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];
    }

    public int MaxAttempts => _delays.Length;

    public double? DelayFor(int attempt) =>
        attempt >= 0 && attempt < _delays.Length ? _delays[attempt].TotalSeconds : null;
}
