using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RadioWindows.Core.Services;

/// <summary>A Last.fm authenticated session (obtained once, then kept in the secure store).</summary>
public sealed record LastFmSession(string SessionKey, string Username);

/// <summary>
/// Minimal Last.fm 2.0 client: desktop auth, now-playing and scrobbling.
///
/// The app ships no developer secret, so keys come from configuration. Every
/// call signs the parameters (sorted <c>key + value</c>, plus the secret) with
/// MD5 as the Last.fm protocol requires, and uses <c>api_sig</c>. Network and
/// parse failures are reported as <c>null</c>/<c>false</c>, never exceptions.
/// </summary>
public sealed class LastFmClient
{
    private static readonly HttpClient SharedHttp = new();

    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly HttpClient _http;

    public LastFmClient(string apiKey, string apiSecret, HttpClient? http = null)
    {
        _apiKey = apiKey ?? string.Empty;
        _apiSecret = apiSecret ?? string.Empty;
        _http = http ?? SharedHttp;
    }

    /// <summary>Returns a request token, or <c>null</c> when Last.fm is unreachable.</summary>
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getToken",
            ["api_key"] = _apiKey,
        };

        using var document = await PostAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = token.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Exchanges an authorized request token for a session key.</summary>
    public async Task<LastFmSession?> GetSessionAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "auth.getSession",
            ["api_key"] = _apiKey,
            ["token"] = token,
        };

        using var document = await PostAsync(parameters, cancellationToken).ConfigureAwait(false);
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("session", out var session)
            || session.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var key = session.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var username = session.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        return new LastFmSession(key, username?.Trim() ?? string.Empty);
    }

    /// <summary>Browser page where the user grants access to this application.</summary>
    public string BuildAuthUrl(string token) =>
        "https://www.last.fm/api/auth/?api_key=" + Uri.EscapeDataString(_apiKey)
        + "&token=" + Uri.EscapeDataString(token);

    public async Task<bool> UpdateNowPlayingAsync(
        LastFmSession session,
        string artist,
        string title,
        string? album,
        CancellationToken cancellationToken)
    {
        if (session is null || string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var parameters = TrackParameters("track.updateNowPlaying", session, artist, title, album);
        using var document = await PostAsync(parameters, cancellationToken).ConfigureAwait(false);
        return document is not null;
    }


    public async Task<bool> ScrobbleAsync(
        LastFmSession session,
        string artist,
        string title,
        string? album,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        if (session is null || string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var parameters = TrackParameters("track.scrobble", session, artist, title, album);
        parameters["timestamp"] = startedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        using var document = await PostAsync(parameters, cancellationToken).ConfigureAwait(false);
        return document is not null;
    }

    /// <summary>
    /// Last.fm <c>api_sig</c>: MD5 over sorted parameter names and values
    /// (format/api_sig excluded) followed by the application secret, lowercase hex.
    /// MD5 is a protocol requirement here, not password hashing.
    /// </summary>
    public static string Signature(IReadOnlyDictionary<string, string> parameters, string secret)
    {
        var baseString = new StringBuilder();
        foreach (var pair in parameters
                     .Where(pair => pair.Key is not ("format" or "api_sig"))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            baseString.Append(pair.Key).Append(pair.Value);
        }

        baseString.Append(secret);
        var digest = MD5.HashData(Encoding.UTF8.GetBytes(baseString.ToString()));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private Dictionary<string, string> TrackParameters(
        string method,
        LastFmSession session,
        string artist,
        string title,
        string? album)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = method,
            ["api_key"] = _apiKey,
            ["sk"] = session.SessionKey,
            ["artist"] = artist,
            ["track"] = title,
        };
        if (!string.IsNullOrWhiteSpace(album))
        {
            parameters["album"] = album.Trim();
        }

        return parameters;
    }

    private async Task<JsonDocument?> PostAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var signed = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in parameters)
            {
                if (pair.Key is not ("format" or "api_sig"))
                {
                    signed[pair.Key] = pair.Value;
                }
            }

            signed["api_key"] = _apiKey;
            signed["api_sig"] = Signature(signed, _apiSecret);

            var body = new List<KeyValuePair<string, string>>(signed.Count + 1);
            foreach (var pair in signed)
            {
                body.Add(new KeyValuePair<string, string>(pair.Key, pair.Value));
            }

            body.Add(new KeyValuePair<string, string>("format", "json"));

            using var content = new FormUrlEncodedContent(body);
            using var response = await _http
                .PostAsync(LastFmConfig.ApiRoot, content, cancellationToken)
                .ConfigureAwait(false);

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Number)
            {
                // {"error":6,"message":"..."} is an API-level failure, not a payload.
                document.Dispose();
                return null;
            }

            return document;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or OperationCanceledException
                                              or JsonException
                                              or IOException
                                              or NotSupportedException
                                              or InvalidOperationException)
        {
            return null;
        }
    }
}
