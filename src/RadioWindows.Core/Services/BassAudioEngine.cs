using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// BASS 2.4 backend: MP3/OGG/AAC/HLS streams, ICY metadata through
/// <c>BASS_SYNC_META</c> and a 10-band peaking equalizer through BASS_FX.
/// Native libraries sit next to the executable; add-ons are loaded when present.
/// </summary>
public sealed class BassAudioEngine : IAudioEngine
{
    private const int NetBufferMilliseconds = 5_000;
    private const int NetTimeoutMilliseconds = 15_000;

    private readonly object _gate = new();
    private readonly List<int> _plugins = [];
    private readonly BassNative.SyncProc _metaSync;
    private readonly BassNative.SyncProc _endSync;
    private readonly Timer _monitor;

    private bool _fxAvailable;
    private string _fxLoadError = string.Empty;
    private string _equalizerDiagnostics = "not-applied";
    private int _stream;
    private int _eqFx;
    private int _generation;
    private double _volume = 0.8;
    private bool _eqEnabled;
    private double[] _gains = new double[EqualizerCatalog.BandCount];
    private int _stalledTicks;
    private bool _ended;
    private bool _disposed;

    /// <param name="noSound">Initialise BASS without an output device; used by CI probes.</param>
    public BassAudioEngine(bool noSound = false)
    {
        if (!BassNative.Init(noSound ? 0 : -1, 44_100, 0, IntPtr.Zero, IntPtr.Zero))
        {
            throw new InvalidOperationException(
                $"BASS_Init failed: {BassNative.ErrorName(BassNative.ErrorGetCode())}");
        }

        BassNative.SetConfig(BassNative.ConfigNetBuffer, NetBufferMilliseconds);
        BassNative.SetConfig(BassNative.ConfigNetTimeout, NetTimeoutMilliseconds);
        BassNative.SetConfig(BassNative.ConfigNetPrebuf, 500);
        BassNative.SetConfig(BassNative.ConfigNetPlaylist, 1);

        LoadPlugins();

        try
        {
            _ = BassNative.FxGetVersion();
            _fxAvailable = true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                           or BadImageFormatException)
        {
            _fxAvailable = false;
            _fxLoadError = $"{exception.GetType().Name}: {exception.Message}";
        }

        _metaSync = (handle, _, _, _) => EmitMetadata(handle);
        _endSync = (handle, _, _, _) => OnStreamEnded(handle);
        _monitor = new Timer(_ => Monitor(), null, 2_000, 2_000);
    }

    public string Name => "BASS 2.4";

    public bool ProvidesMetadata => true;

    /// <summary>True when BASS_FX loaded and a peaking EQ is attached (diagnostics).</summary>
    public bool EqualizerAttached => _fxAvailable && _eqFx != 0 && CurrentStream() != 0;

    /// <summary>Human-readable equalizer state for tools and the diagnostics panel.</summary>
    public string EqualizerDiagnostics => _equalizerDiagnostics;

    public event Action<PlaybackState>? StateChanged;

    public event Action<string>? MetadataChanged;

    public void Play(Uri url, double volume)
    {
        var generation = Interlocked.Increment(ref _generation);
        _volume = Math.Clamp(volume, 0, 1);
        _ended = false;
        _stalledTicks = 0;
        FreeStream();
        Raise(PlaybackState.Loading);

        var uri = url.ToString();
        CurrentUrl = uri;
        _ = Task.Run(() =>
        {
            var flags = BassNative.StreamBlock | BassNative.StreamStatus;
            var handle = BassNative.StreamCreateUrl(uri, flags, null, IntPtr.Zero);
            if (generation != Volatile.Read(ref _generation))
            {
                if (handle != 0)
                {
                    BassNative.StreamFree(handle);
                }

                return;
            }

            if (handle == 0)
            {
                Raise(PlaybackState.Error);
                return;
            }

            lock (_gate)
            {
                if (_disposed || generation != _generation)
                {
                    BassNative.StreamFree(handle);
                    return;
                }

                _stream = handle;
                _eqFx = 0;
            }

            BassNative.ChannelSetAttribute(handle, BassNative.AttribVolume, (float)_volume);
            BassNative.ChannelSetSync(handle, BassNative.SyncMeta, 0, _metaSync, IntPtr.Zero);
            BassNative.ChannelSetSync(handle, BassNative.SyncOggChange, 0, _metaSync, IntPtr.Zero);
            BassNative.ChannelSetSync(handle, BassNative.SyncEnd, 0, _endSync, IntPtr.Zero);

            ApplyEqualizer(handle);

            if (!BassNative.ChannelPlay(handle, false))
            {
                Raise(PlaybackState.Error);
                return;
            }

            Raise(PlaybackState.Playing);
            EmitMetadata(handle);
        });
    }

    public void Pause()
    {
        var stream = CurrentStream();
        if (stream == 0)
        {
            return;
        }

        BassNative.ChannelPause(stream);
        Raise(PlaybackState.Paused);
    }

    public void Resume()
    {
        var stream = CurrentStream();
        if (stream == 0)
        {
            return;
        }

        BassNative.ChannelPlay(stream, false);
        Raise(PlaybackState.Playing);
    }

    public void Stop(bool emitIdle = true)
    {
        Interlocked.Increment(ref _generation);
        _ended = false;
        FreeStream();
        if (emitIdle)
        {
            Raise(PlaybackState.Idle);
        }
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        var stream = CurrentStream();
        if (stream != 0)
        {
            BassNative.ChannelSetAttribute(stream, BassNative.AttribVolume, (float)_volume);
        }
    }

    public void SetEqualizer(bool enabled, IReadOnlyList<double> gains)
    {
        _eqEnabled = enabled;
        _gains = EqualizerCatalog.ClampGains(gains);
        _equalizerDiagnostics = enabled ? "pending" : "disabled";

        var stream = CurrentStream();
        if (stream != 0)
        {
            ApplyEqualizer(stream);
        }
    }

    public Task<StreamInfo?> FetchStreamInfoAsync(CancellationToken cancellationToken)
    {
        var stream = CurrentStream();
        if (stream == 0)
        {
            return Task.FromResult<StreamInfo?>(null);
        }

        try
        {
            var info = default(BassNative.ChannelInfo);
            if (!BassNative.ChannelGetInfo(stream, ref info))
            {
                return Task.FromResult<StreamInfo?>(null);
            }

            var icy = BassTags.ReadTagList(BassNative.ChannelGetTags(stream, BassNative.TagIcy));
            var http = BassTags.ReadTagList(BassNative.ChannelGetTags(stream, BassNative.TagHttp));
            int? bitrate = null;
            if (BassNative.ChannelGetAttribute(stream, BassNative.AttribBitrate, out var bitrateValue)
                && bitrateValue > 0)
            {
                bitrate = (int)Math.Round(bitrateValue);
            }

            bitrate ??= BassTags.BitrateFromTags(icy);
            var contentType = http.TryGetValue("Content-Type", out var type) ? type : null;
            var codec = BassTags.CodecFromChannelType(info.Ctype, contentType, CurrentUrl);
            var server = BassTags.ServerFromTags(http, icy, CurrentUrl);

            var result = new StreamInfo(
                codec,
                (int)info.Freq,
                (int)info.Chans,
                bitrate,
                NetBufferMilliseconds,
                server);
            return Task.FromResult<StreamInfo?>(result);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                           or BadImageFormatException)
        {
            return Task.FromResult<StreamInfo?>(null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitor.Dispose();
        FreeStream();
        foreach (var plugin in _plugins)
        {
            BassNative.PluginFree(plugin);
        }

        _plugins.Clear();
        try
        {
            BassNative.Free();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Shutdown is best effort.
        }
    }

    private string? CurrentUrl { get; set; }

    private void LoadPlugins()
    {
        var baseDirectory = AppContext.BaseDirectory;
        string[] candidates = OperatingSystem.IsWindows()
            ? ["basshls.dll", "bass_aac.dll", "bassopus.dll", "bassflac.dll"]
            : ["libbasshls.dylib", "libbass_aac.dylib", "libbassopus.dylib", "libbassflac.dylib"];

        foreach (var name in candidates)
        {
            var path = Path.Combine(baseDirectory, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var plugin = BassNative.PluginLoad(path);
            if (plugin != 0)
            {
                _plugins.Add(plugin);
            }
        }
    }

    private int CurrentStream()
    {
        lock (_gate)
        {
            return _stream;
        }
    }

    private void FreeStream()
    {
        int stream;
        lock (_gate)
        {
            stream = _stream;
            _stream = 0;
            _eqFx = 0;
        }

        if (stream == 0)
        {
            return;
        }

        BassNative.ChannelStop(stream);
        BassNative.StreamFree(stream);
    }

    private void ApplyEqualizer(int stream)
    {
        if (!_fxAvailable || stream == 0)
        {
            _equalizerDiagnostics = _fxAvailable ? "no-stream" : $"fx-unavailable: {_fxLoadError}";
            return;
        }

        try
        {
            if (!_eqEnabled)
            {
                if (_eqFx != 0)
                {
                    BassNative.ChannelRemoveFx(stream, _eqFx);
                    _eqFx = 0;
                }

                _equalizerDiagnostics = "disabled";
                return;
            }

            var fx = _eqFx;
            if (fx == 0)
            {
                fx = BassNative.ChannelSetFx(stream, BassNative.BfxPeakEq, 0);
                if (fx == 0)
                {
                    _equalizerDiagnostics =
                        $"fx-set-failed: {BassNative.ErrorName(BassNative.ErrorGetCode())}";
                    return;
                }

                _eqFx = fx;
            }

            var info = default(BassNative.ChannelInfo);
            BassNative.ChannelGetInfo(stream, ref info);
            var maxCenter = info.Freq > 0 ? info.Freq * 0.49f : 20_000f;

            for (var band = 0; band < EqualizerCatalog.BandCount; band++)
            {
                var parameters = new BassNative.PeakEqParameters
                {
                    Band = band,
                    Bandwidth = 1.0f,
                    Q = 0f,
                    Center = Math.Clamp(EqualizerCatalog.Frequencies[band], 20, (int)maxCenter),
                    Gain = (float)_gains[band],
                    Channel = BassNative.BfxChannelAll,
                };
                BassNative.FxSetParameters(fx, ref parameters);
            }

            _equalizerDiagnostics = "attached";
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                           or BadImageFormatException)
        {
            _fxAvailable = false;
            _fxLoadError = $"{exception.GetType().Name}: {exception.Message}";
            _equalizerDiagnostics = $"fx-unavailable: {_fxLoadError}";
        }
    }

    private void EmitMetadata(int handle)
    {
        if (handle == 0 || handle != CurrentStream())
        {
            return;
        }

        var meta = BassTags.ReadMeta(BassNative.ChannelGetTags(handle, BassNative.TagMeta));
        var title = NowPlayingParser.UnwrapIcyBlock(meta);
        if (!string.IsNullOrWhiteSpace(title))
        {
            MetadataChanged?.Invoke(title);
        }
    }

    private void OnStreamEnded(int handle)
    {
        if (handle == 0 || handle != CurrentStream())
        {
            return;
        }

        _ended = true;
        Raise(PlaybackState.Idle);
    }

    private void Monitor()
    {
        if (_disposed)
        {
            return;
        }

        var stream = CurrentStream();
        if (stream == 0)
        {
            return;
        }

        var active = BassNative.ChannelIsActive(stream);
        if (active == BassNative.ChannelActiveStalled)
        {
            if (++_stalledTicks >= 5 && !_ended)
            {
                _ended = true;
                Raise(PlaybackState.Idle);
            }

            return;
        }

        _stalledTicks = 0;
        if (active == BassNative.ChannelActiveStopped && !_ended)
        {
            _ended = true;
            Raise(PlaybackState.Idle);
        }
    }

    private void Raise(PlaybackState state) => StateChanged?.Invoke(state);
}
