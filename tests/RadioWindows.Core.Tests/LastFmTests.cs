using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;
using Xunit.Sdk;

namespace RadioWindows.Core.Tests;

/// <summary>
/// Last.fm protocol and timing tests. HTTP is a fake handler, so nothing ever
/// leaves the machine. The player is driven through its private metadata/state
/// setters because <see cref="RadioPlayerService"/> has no test seam.
/// </summary>
public sealed class LastFmTests : IDisposable
{
    private const string ApiKey = "test-api-key";
    private const string ApiSecret = "test-api-secret";
    private const string SessionKey = "test-session-key";

    private readonly string _root;
    private readonly TestPreferences _context;
    private readonly NullAudioEngine _engine;
    private readonly EqualizerService _equalizer;
    private readonly RadioPlayerService _player;
    private readonly StubHandler _handler;
    private readonly LastFmClient _client;
    private readonly HttpClient _http;

    public LastFmTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "radiowindows-lastfm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SecureStore.UseRoot(_root);

        _context = new TestPreferences();
        _engine = new NullAudioEngine { StartDelay = TimeSpan.FromMilliseconds(1) };
        _equalizer = new EqualizerService(_context.Preferences);
        _player = new RadioPlayerService(
            new AppSettings(_context.Preferences),
            new StationLibrary(_context.Preferences),
            _equalizer,
            new NoopNowPlayingLookup(),
            _engine,
            new ImmediateDispatcher());

        _handler = new StubHandler { Respond = DefaultResponse };
        _http = new HttpClient(_handler, disposeHandler: false);
        _client = new LastFmClient(ApiKey, ApiSecret, _http);
    }

    // MARK: - Signature and protocol

    [Fact]
    public void Signature_is_md5_of_sorted_pairs_plus_secret()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.scrobble",
            ["api_key"] = ApiKey,
            ["sk"] = SessionKey,
            ["artist"] = "Björk",
            ["track"] = "Jóga",
            ["timestamp"] = "1700000000",
        };

        var expected = IndependentSignature(parameters, ApiSecret);
        var actual = LastFmClient.Signature(parameters, ApiSecret);

        Assert.Equal(expected, actual);
        Assert.Matches("^[0-9a-f]{32}$", actual);
    }

    [Fact]
    public void Signature_ignores_format_and_api_sig()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getToken",
            ["api_key"] = ApiKey,
        };

        var expected = LastFmClient.Signature(parameters, ApiSecret);
        parameters["format"] = "json";
        parameters["api_sig"] = "deadbeef";

        Assert.Equal(expected, LastFmClient.Signature(parameters, ApiSecret));
    }

    [Fact]
    public async Task Now_playing_uses_the_expected_method_parameters_and_signature()
    {
        var ok = await _client.UpdateNowPlayingAsync(
            new LastFmSession(SessionKey, "tester"), "Artist", "Title", "Album", CancellationToken.None);

        Assert.True(ok);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(LastFmConfig.ApiRoot, request.Uri.ToString());

        Assert.Equal("track.updateNowPlaying", request.Form["method"]);
        Assert.Equal(ApiKey, request.Form["api_key"]);
        Assert.Equal(SessionKey, request.Form["sk"]);
        Assert.Equal("Artist", request.Form["artist"]);
        Assert.Equal("Title", request.Form["track"]);
        Assert.Equal("Album", request.Form["album"]);
        Assert.Equal("json", request.Form["format"]);

        var unsigned = new Dictionary<string, string>(request.Form, StringComparer.Ordinal);
        var signature = unsigned["api_sig"];
        unsigned.Remove("api_sig");
        unsigned.Remove("format");
        Assert.Equal(IndependentSignature(unsigned, ApiSecret), signature);
    }

    [Fact]
    public async Task Scrobble_sends_a_unix_timestamp_and_omits_an_empty_album()
    {
        var startedAt = DateTimeOffset.FromUnixTimeSeconds(1_700_000_123);

        var ok = await _client.ScrobbleAsync(
            new LastFmSession(SessionKey, "tester"), "Artist", "Title", null, startedAt, CancellationToken.None);

        Assert.True(ok);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("track.scrobble", request.Form["method"]);
        Assert.Equal("1700000123", request.Form["timestamp"]);
        Assert.False(request.Form.ContainsKey("album"));
    }

    [Fact]
    public async Task Session_is_parsed_from_the_api_response()
    {
        var session = await _client.GetSessionAsync("request-token", CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal(SessionKey, session!.SessionKey);
        Assert.Equal("tester", session.Username);
        Assert.Equal("auth.getSession", _handler.Requests[0].Form["method"]);
        Assert.Equal("request-token", _handler.Requests[0].Form["token"]);
    }

    [Fact]
    public async Task Api_errors_are_reported_as_null_not_exceptions()
    {
        _handler.Respond = _ => "{\"error\":4,\"message\":\"Invalid method\"}";

        Assert.Null(await _client.GetTokenAsync(CancellationToken.None));
        Assert.Null(await _client.GetSessionAsync("request-token", CancellationToken.None));
        Assert.False(await _client.UpdateNowPlayingAsync(
            new LastFmSession(SessionKey, "tester"), "Artist", "Title", null, CancellationToken.None));
    }

    [Fact]
    public async Task Http_failures_are_reported_as_null_not_exceptions()
    {
        _handler.StatusCode = HttpStatusCode.InternalServerError;
        _handler.Respond = _ => "oops";

        Assert.Null(await _client.GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public void Auth_url_contains_the_key_and_token()
    {
        var url = _client.BuildAuthUrl("token with space");

        Assert.StartsWith("https://www.last.fm/api/auth/", url);
        Assert.Contains("api_key=test-api-key", url);
        Assert.Contains("token=token%20with%20space", url);
    }

    // MARK: - Service flow

    [Fact]
    public async Task Connect_opens_the_browser_and_persists_the_session()
    {
        var opened = new List<string>();
        using var service = CreateService(openUrl: url =>
        {
            opened.Add(url);
            return Task.CompletedTask;
        });

        Assert.True(service.KeysConfigured);
        Assert.False(service.IsConnected);
        Assert.Equal("LastFm.Status.Disabled", service.StatusKey);

        var (ok, errorKey) = await service.ConnectAsync(CancellationToken.None);

        Assert.True(ok);
        Assert.Null(errorKey);
        Assert.True(service.IsConnected);
        Assert.True(service.IsEnabled);
        Assert.Equal("tester", service.Username);
        Assert.Equal("LastFm.Status.Connected", service.StatusKey);
        Assert.Equal(SessionKey, SecureStore.Load(LastFmService.SessionStoreName));
        Assert.Equal("tester", SecureStore.Load(LastFmService.UsernameStoreName));
        Assert.Contains("token=token-1", Assert.Single(opened));
    }

    [Fact]
    public async Task Enabling_without_a_session_asks_for_a_connection()
    {
        using var service = CreateService();

        Assert.True(await service.SetEnabledAsync(true));

        Assert.True(service.IsEnabled);
        Assert.False(service.IsConnected);
        Assert.Equal("LastFm.Status.NeedsConnection", service.StatusKey);
        Assert.True(_context.Preferences.GetBoolLike(LastFmService.EnabledPreferenceKey));
    }

    [Fact]
    public async Task Disconnect_forgets_the_stored_session()
    {
        using var service = CreateService();
        await service.ConnectAsync(CancellationToken.None);

        service.Disconnect();

        Assert.False(service.IsEnabled);
        Assert.False(service.IsConnected);
        Assert.Null(service.Username);
        Assert.Null(SecureStore.Load(LastFmService.SessionStoreName));
        Assert.Null(SecureStore.Load(LastFmService.UsernameStoreName));
        Assert.Equal("LastFm.Status.Disabled", service.StatusKey);
    }

    // MARK: - Now-playing / scrobble timing

    [Fact]
    public async Task Now_playing_is_sent_once_per_track()
    {
        using var service = await ConnectedServiceAsync(TimeSpan.FromSeconds(30));
        await SetPlayingAsync();
        SetNowPlaying("Artist A", "Song A");

        await WaitUntilAsync(() => Count("track.updateNowPlaying") == 1);

        // Metadata polls for the same track must not resend now-playing.
        SetNowPlaying("Artist A", "Song A");
        await Task.Delay(100);
        Assert.Equal(1, Count("track.updateNowPlaying"));

        SetNowPlaying("Artist B", "Song B");
        await WaitUntilAsync(() => Count("track.updateNowPlaying") == 2);
    }

    [Fact]
    public async Task Scrobble_fires_after_the_threshold_for_the_same_track_only()
    {
        using var service = await ConnectedServiceAsync(TimeSpan.FromMilliseconds(200));
        await SetPlayingAsync();
        SetNowPlaying("Artist A", "Song A");

        await WaitUntilAsync(() => Count("track.scrobble") == 1);
        Assert.Equal("Song A", Scrobbles()[0].Form["track"]);

        // Changing the track must not scrobble the finished track twice.
        SetNowPlaying("Artist B", "Song B");
        await WaitUntilAsync(() => Count("track.scrobble") == 2);

        var forms = Scrobbles().Select(request => request.Form).ToList();
        Assert.Equal(2, forms.Count);
        Assert.Equal("Song A", forms[0]["track"]);
        Assert.Equal("Song B", forms[1]["track"]);
        Assert.Equal(2, Count("track.updateNowPlaying"));
    }

    [Fact]
    public async Task Track_changed_before_the_threshold_is_not_scrobbled()
    {
        using var service = await ConnectedServiceAsync(TimeSpan.FromSeconds(10));
        await SetPlayingAsync();
        SetNowPlaying("Artist A", "Song A");

        await WaitUntilAsync(() => Count("track.updateNowPlaying") == 1);
        SetNowPlaying("Artist B", "Song B");
        await WaitUntilAsync(() => Count("track.updateNowPlaying") == 2);
        await Task.Delay(150);

        Assert.Equal(0, Count("track.scrobble"));
    }

    [Fact]
    public async Task Disabling_stops_sending_but_keeps_the_session()
    {
        using var service = await ConnectedServiceAsync(TimeSpan.FromSeconds(10));
        await SetPlayingAsync();
        SetNowPlaying("Artist A", "Song A");
        await WaitUntilAsync(() => Count("track.updateNowPlaying") == 1);

        Assert.True(await service.SetEnabledAsync(false));
        SetNowPlaying("Artist B", "Song B");
        await Task.Delay(150);

        Assert.Equal(1, Count("track.updateNowPlaying"));
        Assert.False(service.IsEnabled);
        Assert.True(service.IsConnected);
        Assert.Equal(SessionKey, SecureStore.Load(LastFmService.SessionStoreName));
        Assert.Equal("LastFm.Status.Disabled", service.StatusKey);
    }

    // MARK: - Secure store and config

    [Fact]
    public void Secure_store_round_trips_and_deletes()
    {
        Assert.Null(SecureStore.Load("lastfm.session"));

        SecureStore.Save("lastfm.session", "super-secret");

        Assert.Equal("super-secret", SecureStore.Load("lastfm.session"));
        var path = Path.Combine(_root, "credentials", "lastfm.session.bin");
        Assert.True(File.Exists(path));

        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
            Assert.False(mode.HasFlag(UnixFileMode.OtherRead));
        }

        SecureStore.Delete("lastfm.session");
        Assert.Null(SecureStore.Load("lastfm.session"));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Config_reads_the_environment_variables()
    {
        var previousKey = Environment.GetEnvironmentVariable(LastFmConfig.ApiKeyVariable);
        var previousSecret = Environment.GetEnvironmentVariable(LastFmConfig.ApiSecretVariable);
        try
        {
            Environment.SetEnvironmentVariable(LastFmConfig.ApiKeyVariable, "  env-key  ");
            Environment.SetEnvironmentVariable(LastFmConfig.ApiSecretVariable, "env-secret");

            Assert.Equal("env-key", LastFmConfig.ApiKey);
            Assert.Equal("env-secret", LastFmConfig.ApiSecret);
            Assert.True(LastFmConfig.IsConfigured);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LastFmConfig.ApiKeyVariable, previousKey);
            Environment.SetEnvironmentVariable(LastFmConfig.ApiSecretVariable, previousSecret);
        }
    }

    // MARK: - Helpers

    private LastFmService CreateService(
        TimeSpan? threshold = null,
        Func<string, Task>? openUrl = null) =>
        new(_context.Preferences, _player, _client, openUrl ?? (_ => Task.CompletedTask), threshold);

    private async Task<LastFmService> ConnectedServiceAsync(TimeSpan threshold)
    {
        var service = CreateService(threshold);
        var (ok, errorKey) = await service.ConnectAsync(CancellationToken.None);
        Assert.True(ok, errorKey);
        Assert.True(service.IsConnected);
        return service;
    }

    private async Task SetPlayingAsync()
    {
        SetStatus(PlaybackState.Playing);
        await Task.Yield();

        if (_player.Status != PlaybackState.Playing)
        {
            throw new XunitException("The player did not report the Playing state.");
        }
    }

    private void SetNowPlaying(string artist, string title, string? album = null)
    {
        var track = new NowPlaying { Raw = $"{artist} - {title}", Artist = artist, Title = title, Album = album };
        typeof(RadioPlayerService)
            .GetField("_nowPlaying", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_player, track);
        typeof(ObservableObject)
            .GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_player, ["NowPlaying"]);
    }

    private void SetStatus(PlaybackState state)
    {
        typeof(RadioPlayerService)
            .GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_player, [state]);
    }

    private static string DefaultResponse(IReadOnlyDictionary<string, string> form)
    {
        var method = form.TryGetValue("method", out var value) ? value : string.Empty;
        return method switch
        {
            "auth.getToken" => "{\"token\":\"token-1\"}",
            "auth.getSession" => "{\"session\":{\"name\":\"tester\",\"key\":\"test-session-key\"}}",
            "track.updateNowPlaying" => "{}",
            "track.scrobble" => "{\"scrobbles\":{\"@attr\":{\"accepted\":1,\"ignored\":0}}}",
            _ => "{\"error\":3,\"message\":\"Invalid method\"}",
        };
    }

    private int Count(string method) =>
        _handler.Requests.Count(request => request.Form.TryGetValue("method", out var value) && value == method);

    private IReadOnlyList<RecordedRequest> Scrobbles() =>
        _handler.Requests.Where(request =>
            request.Form.TryGetValue("method", out var value) && value == "track.scrobble").ToList();

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new XunitException("Timed out waiting for the Last.fm request.");
            }

            await Task.Delay(15);
        }
    }

    private static string IndependentSignature(IReadOnlyDictionary<string, string> parameters, string secret)
    {
        var baseString = string.Concat(
            parameters
                .Where(pair => pair.Key is not ("format" or "api_sig"))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Key + pair.Value))
            + secret;

        var digest = MD5.HashData(Encoding.UTF8.GetBytes(baseString));
        return string.Concat(digest.Select(value => value.ToString("x2")));
    }

    public void Dispose()
    {
        _http.Dispose();
        _player.Dispose();
        _equalizer.Dispose();
        _context.Dispose();
        SecureStore.UseRoot(null);

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Form);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly List<RecordedRequest> _requests = [];

        public Func<IReadOnlyDictionary<string, string>, string>? Respond { get; set; }

        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return _requests.ToList();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var form = ParseForm(body);

            lock (_gate)
            {
                _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, form));
            }

            var payload = Respond?.Invoke(form) ?? "{}";
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
        }

        private static Dictionary<string, string> ParseForm(string body)
        {
            var form = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                var key = separator < 0 ? pair : pair[..separator];
                var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
                form[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                    Uri.UnescapeDataString(value.Replace('+', ' '));
            }

            return form;
        }
    }
}
