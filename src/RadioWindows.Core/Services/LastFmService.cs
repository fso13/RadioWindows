using System.ComponentModel;
using System.Diagnostics;

namespace RadioWindows.Core.Services;

/// <summary>
/// Opt-in Last.fm scrobbling.
///
/// Nothing is sent until the feature is enabled <em>and</em> an account is
/// connected. While a track plays, now-playing is sent once per track change;
/// a second timer scrobbles the same track after the threshold (240 s, matching
/// the macOS app). Disabling stops all sending but keeps the session, so
/// reconnecting is a single click; <see cref="Disconnect"/> forgets the session.
/// </summary>
public sealed class LastFmService : ObservableObject, IDisposable
{
    public const string SessionStoreName = "lastfm.session";

    public const string UsernameStoreName = "lastfm.username";

    public const string EnabledPreferenceKey = "lastfm_enabled";

    public static readonly TimeSpan DefaultScrobbleThreshold = TimeSpan.FromSeconds(240);

    private static readonly TimeSpan SessionPollInterval = TimeSpan.FromSeconds(2);

    private const int SessionPollAttempts = 45;

    private readonly Preferences _preferences;
    private readonly RadioPlayerService _player;
    private readonly LastFmClient? _client;
    private readonly Func<string, Task> _openUrl;
    private readonly TimeSpan _scrobbleThreshold;
    private readonly object _gate = new();

    private LastFmSession? _session;
    private bool _enabled;
    private string _statusKey = "LastFm.Status.Disabled";
    private Track? _activeTrack;
    private DateTimeOffset _activeStartedAt;
    private string? _lastNowPlayingKey;
    private bool _scrobbled;
    private long _generation;
    private Timer? _scrobbleTimer;
    private bool _disposed;

    public LastFmService(
        Preferences preferences,
        RadioPlayerService player,
        LastFmClient? client = null,
        Func<string, Task>? openUrl = null,
        TimeSpan? scrobbleThreshold = null)
    {
        _preferences = preferences;
        _player = player;
        _openUrl = openUrl ?? OpenInBrowserAsync;
        _scrobbleThreshold = scrobbleThreshold is { } threshold && threshold > TimeSpan.Zero
            ? threshold
            : DefaultScrobbleThreshold;
        _client = client ?? (LastFmConfig.IsConfigured
            ? new LastFmClient(LastFmConfig.ApiKey, LastFmConfig.ApiSecret)
            : null);

        _enabled = preferences.GetBoolLike(EnabledPreferenceKey) ?? false;
        var storedKey = NullIfEmpty(SecureStore.Load(SessionStoreName));
        if (storedKey is not null)
        {
            var storedUsername = NullIfEmpty(SecureStore.Load(UsernameStoreName)) ?? string.Empty;
            _session = new LastFmSession(storedKey, storedUsername);
        }

        _statusKey = IdleStatusKey();
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    public bool IsEnabled => _enabled;

    public bool IsConnected => _session is not null;

    public string? Username => _session is { Username.Length: > 0 } session ? session.Username : null;

    public bool KeysConfigured => _client is not null;

    /// <summary>Localisation key describing the current state; see the service summary.</summary>
    public string StatusKey => _statusKey;

    public TimeSpan ScrobbleThreshold => _scrobbleThreshold;

    // MARK: - Opt-in

    public Task<bool> SetEnabledAsync(bool enabled)
    {
        if (_disposed)
        {
            return Task.FromResult(false);
        }

        if (_enabled == enabled)
        {
            SetStatusKey(IdleStatusKey());
            return Task.FromResult(true);
        }

        _enabled = enabled;
        _preferences.Set(EnabledPreferenceKey, enabled);
        Raise(nameof(IsEnabled));

        if (enabled)
        {
            if (_session is null)
            {
                SetStatusKey("LastFm.Status.NeedsConnection");
            }
            else
            {
                SetStatusKey("LastFm.Status.Connected");
                HandleNowPlaying();
            }
        }
        else
        {
            // Stop sending immediately but keep the stored session for a one-click resume.
            FinalizeTrack(scrobbleEligible: false);
            SetStatusKey(IdleStatusKey());
        }

        return Task.FromResult(true);
    }

    // MARK: - Authorization

    /// <summary>
    /// Desktop auth: gets a token, opens the Last.fm page and waits until the
    /// account is authorized. Returns an error key from the <c>LastFm.*</c> family.
    /// </summary>
    public async Task<(bool ok, string? errorKey)> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = _client;
        if (client is null)
        {
            return (false, "LastFm.KeysMissing");
        }

        try
        {
            var token = await client.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(token))
            {
                return (false, cancellationToken.IsCancellationRequested
                    ? "LastFm.Error.Cancelled"
                    : "LastFm.Error.Token");
            }

            SetStatusKey("LastFm.Waiting");
            try
            {
                await _openUrl(client.BuildAuthUrl(token)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Browser launch is best effort; the URL is still advertised by the UI.
            }

            for (var attempt = 0; attempt < SessionPollAttempts; attempt++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return (false, "LastFm.Error.Cancelled");
                }

                var session = await client.GetSessionAsync(token, cancellationToken).ConfigureAwait(false);
                if (session is not null)
                {
                    ApplySession(session);
                    return (true, null);
                }

                if (attempt + 1 < SessionPollAttempts)
                {
                    await Task.Delay(SessionPollInterval, cancellationToken).ConfigureAwait(false);
                }
            }

            SetStatusKey(IdleStatusKey());
            return (false, "LastFm.Error.Timeout");
        }
        catch (OperationCanceledException)
        {
            SetStatusKey(IdleStatusKey());
            return (false, "LastFm.Error.Cancelled");
        }
        catch (Exception)
        {
            SetStatusKey(IdleStatusKey());
            return (false, "LastFm.Error.Unknown");
        }
    }

    /// <summary>Forgets the account and turns scrobbling off (the Swift disconnect flow).</summary>
    public void Disconnect()
    {
        if (_disposed)
        {
            return;
        }

        FinalizeTrack(scrobbleEligible: false);
        _enabled = false;
        _preferences.Set(EnabledPreferenceKey, false);

        SecureStore.Delete(SessionStoreName);
        SecureStore.Delete(UsernameStoreName);
        _session = null;

        Raise(nameof(IsEnabled));
        Raise(nameof(IsConnected));
        Raise(nameof(Username));
        SetStatusKey(IdleStatusKey());
    }

    // MARK: - Playback

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        switch (args.PropertyName)
        {
            case nameof(RadioPlayerService.Status):
                if (_player.Status == PlaybackState.Playing)
                {
                    HandleNowPlaying();
                }
                else if (_player.Status is PlaybackState.Idle or PlaybackState.Error
                         or PlaybackState.Paused or PlaybackState.Reconnecting)
                {
                    // The same treatment as the macOS app: a stopped, failed, paused or
                    // reconnecting stream finalizes the current track.
                    FinalizeTrack(scrobbleEligible: true);
                }

                break;

            case nameof(RadioPlayerService.NowPlaying):
                if (_player.Status == PlaybackState.Playing)
                {
                    HandleNowPlaying();
                }

                break;
        }
    }

    private void HandleNowPlaying()
    {
        if (_disposed || !_enabled || _session is null || _client is null
            || _player.Status != PlaybackState.Playing)
        {
            return;
        }

        var track = MakeTrack();
        if (track is null)
        {
            return;
        }

        Track? previous = null;
        DateTimeOffset previousStartedAt = default;
        var scrobblePrevious = false;
        var sendNowPlaying = false;

        lock (_gate)
        {
            if (_activeTrack is null || !string.Equals(_activeTrack.Key, track.Key, StringComparison.Ordinal))
            {
                if (_activeTrack is not null)
                {
                    previous = _activeTrack;
                    previousStartedAt = _activeStartedAt;
                    scrobblePrevious = !_scrobbled && Elapsed(previousStartedAt) >= _scrobbleThreshold;
                }

                _generation++;
                _activeTrack = track;
                _activeStartedAt = DateTimeOffset.UtcNow;
                _scrobbled = false;
                _lastNowPlayingKey = null;
                ScheduleScrobbleLocked(_generation);
            }

            if (!string.Equals(_lastNowPlayingKey, track.Key, StringComparison.Ordinal))
            {
                _lastNowPlayingKey = track.Key;
                sendNowPlaying = true;
            }
        }

        if (previous is not null && scrobblePrevious)
        {
            _ = SendScrobbleAsync(previous, previousStartedAt);
        }

        if (sendNowPlaying)
        {
            _ = SendNowPlayingAsync(track);
        }
    }

    private Track? MakeTrack()
    {
        var nowPlaying = _player.NowPlaying;
        var artist = nowPlaying?.DisplayArtist;
        var title = nowPlaying?.Title?.Trim();
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var album = nowPlaying!.Album?.Trim();
        return new Track(
            artist,
            title,
            string.IsNullOrEmpty(album) ? null : album,
            $"{artist.ToLowerInvariant()}|{title.ToLowerInvariant()}");
    }

    /// <summary>Ends the active track and optionally scrobbles it when it lasted long enough.</summary>
    private void FinalizeTrack(bool scrobbleEligible)
    {
        Track? track = null;
        DateTimeOffset startedAt = default;
        var scrobble = false;

        lock (_gate)
        {
            if (_activeTrack is not null)
            {
                track = _activeTrack;
                startedAt = _activeStartedAt;
                scrobble = scrobbleEligible
                           && !_scrobbled
                           && _enabled
                           && _session is not null
                           && Elapsed(startedAt) >= _scrobbleThreshold;
                if (scrobble)
                {
                    _scrobbled = true;
                }
            }

            CancelScrobbleLocked();
            _generation++;
            _activeTrack = null;
            _activeStartedAt = default;
            _lastNowPlayingKey = null;
            _scrobbled = false;
        }

        if (track is not null && scrobble)
        {
            _ = SendScrobbleAsync(track, startedAt);
        }
    }

    private void ScheduleScrobbleLocked(long generation)
    {
        CancelScrobbleLocked();
        _scrobbleTimer = new Timer(OnScrobbleTimer, generation, _scrobbleThreshold, Timeout.InfiniteTimeSpan);
    }

    private void CancelScrobbleLocked()
    {
        _scrobbleTimer?.Dispose();
        _scrobbleTimer = null;
    }

    /// <summary>Fires once per track, and only while that same track is still active.</summary>
    private void OnScrobbleTimer(object? state)
    {
        var generation = state is long value ? value : -1;
        Track? track = null;
        DateTimeOffset startedAt = default;

        lock (_gate)
        {
            if (_disposed
                || generation != _generation
                || _activeTrack is null
                || _scrobbled
                || !_enabled
                || _session is null)
            {
                return;
            }

            track = _activeTrack;
            startedAt = _activeStartedAt;
            _scrobbled = true;
        }

        _ = SendScrobbleAsync(track, startedAt);
    }

    private async Task SendNowPlayingAsync(Track track)
    {
        var client = _client;
        var session = _session;
        if (client is null || session is null || !_enabled)
        {
            return;
        }

        var ok = false;
        try
        {
            ok = await client
                .UpdateNowPlayingAsync(session, track.Artist, track.Title, track.Album, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sending must never surface as a crash.
        }

        if (_enabled && _activeTrack is not null && string.Equals(_activeTrack.Key, track.Key, StringComparison.Ordinal))
        {
            SetStatusKey(ok ? "LastFm.Status.NowPlaying" : "LastFm.Status.SendFailed");
        }
    }

    private async Task SendScrobbleAsync(Track track, DateTimeOffset startedAt)
    {
        var client = _client;
        var session = _session;
        if (client is null || session is null)
        {
            return;
        }

        var ok = false;
        try
        {
            ok = await client
                .ScrobbleAsync(session, track.Artist, track.Title, track.Album, startedAt, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sending must never surface as a crash.
        }

        if (_enabled)
        {
            SetStatusKey(ok ? "LastFm.Status.Scrobbled" : "LastFm.Status.SendFailed");
        }
    }

    // MARK: - Session and status

    private void ApplySession(LastFmSession session)
    {
        SecureStore.Save(SessionStoreName, session.SessionKey);
        if (!string.IsNullOrWhiteSpace(session.Username))
        {
            SecureStore.Save(UsernameStoreName, session.Username);
        }
        else
        {
            SecureStore.Delete(UsernameStoreName);
        }

        _session = session;
        _enabled = true;
        _preferences.Set(EnabledPreferenceKey, true);

        Raise(nameof(IsConnected));
        Raise(nameof(Username));
        Raise(nameof(IsEnabled));
        SetStatusKey("LastFm.Status.Connected");
        HandleNowPlaying();
    }

    private string IdleStatusKey()
    {
        if (_client is null)
        {
            return "LastFm.KeysMissing";
        }

        if (!_enabled)
        {
            return "LastFm.Status.Disabled";
        }

        return _session is null ? "LastFm.Status.NeedsConnection" : "LastFm.Status.Connected";
    }

    private void SetStatusKey(string key)
    {
        lock (_gate)
        {
            if (string.Equals(_statusKey, key, StringComparison.Ordinal))
            {
                return;
            }

            _statusKey = key;
        }

        Raise(nameof(StatusKey));
    }

    private static TimeSpan Elapsed(DateTimeOffset startedAt) => DateTimeOffset.UtcNow - startedAt;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Task OpenInBrowserAsync(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
            // Opening a browser is best effort.
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
        lock (_gate)
        {
            CancelScrobbleLocked();
            _activeTrack = null;
        }
    }

    private sealed record Track(string Artist, string Title, string? Album, string Key);
}
