using System.Net.Http.Headers;
using System.Text.Json;

namespace RadioWindows.Core.Services;

/// <summary>
/// Finds a station logo from the stream URL, the ICY homepage and the station
/// name. Port of the macOS LogoResolver:
/// 1. ask Radio Browser for a favicon matching the stream URL, then the name,
/// 2. try <c>&lt;homepage&gt;/favicon.ico</c> for the ICY <c>icy-url</c>,
/// 3. fall back to a favicon on the stream's brand domain (CDNs are skipped).
/// Every candidate is validated to answer with an image content type.
/// </summary>
public sealed class LogoResolver : ILogoResolver
{
    private const string UserAgent = "RadioWindows/1.0";
    private const string RadioBrowserBase = "https://de1.api.radio-browser.info";
    private const int CacheCapacity = 128;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ImageProbeTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly string[] CdnHosts =
    [
        "hostingradio.ru",
        "cdnvideo.ru",
        "streamguys1.com",
        "getradio.me",
        "mediacdn.ru",
        "101.ru",
        "amgradio.ru",
        "volna.top",
        "radio-holding.ru",
        "vdfm.ru",
    ];

    private readonly HttpClient _http;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public LogoResolver(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(6),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<string?> ResolveAsync(
        string streamUrl,
        string? homepage,
        string stationName,
        CancellationToken cancellationToken = default)
    {
        var key = $"logo\u001f{streamUrl}\u001f{homepage}\u001f{stationName}";
        if (TryGetCached(key, out var cached))
        {
            return cached as string;
        }

        string? logo;
        try
        {
            logo = await ResolveCoreAsync(streamUrl, homepage, stationName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A logo is decoration only; the caller falls back to its placeholder.
            logo = null;
        }

        Store(key, logo);
        return logo;
    }

    private async Task<string?> ResolveCoreAsync(
        string streamUrl,
        string? homepage,
        string? stationName,
        CancellationToken cancellationToken)
    {
        var stream = TryUri(streamUrl);

        // 1. Radio Browser catalog: exact stream URL first, then the name.
        if (stream is not null)
        {
            var byUrl = await SearchStationsAsync(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["url"] = streamUrl.Trim() },
                cancellationToken).ConfigureAwait(false);
            if (await FirstImageAsync(byUrl, cancellationToken).ConfigureAwait(false) is { } fromUrl)
            {
                return fromUrl;
            }
        }

        var name = stationName?.Trim();
        if (name is { Length: >= 3 })
        {
            var byName = await SearchStationsAsync(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = name },
                cancellationToken).ConfigureAwait(false);
            if (await FirstImageAsync(byName, cancellationToken).ConfigureAwait(false) is { } fromName)
            {
                return fromName;
            }
        }

        // 2. The station homepage reported by ICY metadata.
        if (TryUri(homepage) is { } home)
        {
            var host = home.Host.ToLowerInvariant();
            var direct = $"{home.Scheme}://{Authority(home)}/favicon.ico";
            if (await IsImageAsync(direct, cancellationToken).ConfigureAwait(false))
            {
                return direct;
            }

            var service = FaviconService(host);
            if (await IsImageAsync(service, cancellationToken).ConfigureAwait(false))
            {
                return service;
            }
        }

        // 3. The stream's brand domain (stream hosts are often CDNs).
        if (BrandHost(stream?.Host) is { } brand)
        {
            var direct = $"https://{brand}/favicon.ico";
            if (await IsImageAsync(direct, cancellationToken).ConfigureAwait(false))
            {
                return direct;
            }

            var service = FaviconService(brand);
            if (await IsImageAsync(service, cancellationToken).ConfigureAwait(false))
            {
                return service;
            }
        }

        return null;
    }

    // Radio Browser

    private async Task<List<JsonElement>> SearchStationsAsync(
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var query = parameters
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")
            .Concat(new[] { "hidebroken=true", "order=clickcount", "reverse=true", "limit=5" });

        using var json = await GetJsonAsync(
            new Uri($"{RadioBrowserBase}/json/stations/search?{string.Join("&", query)}"),
            cancellationToken).ConfigureAwait(false);

        var stations = new List<JsonElement>();
        if (json is null || json.RootElement.ValueKind != JsonValueKind.Array)
        {
            return stations;
        }

        // Cloned elements survive the JsonDocument being disposed.
        foreach (var station in json.RootElement.EnumerateArray())
        {
            if (station.ValueKind == JsonValueKind.Object)
            {
                stations.Add(station.Clone());
            }
        }

        return stations;
    }

    private async Task<string?> FirstImageAsync(IEnumerable<JsonElement> stations, CancellationToken cancellationToken)
    {
        foreach (var station in stations)
        {
            var favicon = Text(station, "favicon");
            if (favicon is null || string.Equals(favicon, "null", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await IsImageAsync(favicon, cancellationToken).ConfigureAwait(false))
            {
                return favicon;
            }
        }

        return null;
    }

    // Image probing

    private async Task<bool> IsImageAsync(string url, CancellationToken cancellationToken)
    {
        var uri = TryUri(url);
        if (uri is null)
        {
            return false;
        }

        var head = await ProbeAsync(uri, HttpMethod.Head, cancellationToken).ConfigureAwait(false);
        if (head != ProbeOutcome.Unknown)
        {
            return head == ProbeOutcome.Image;
        }

        // Some servers reject HEAD outright; one ranged GET is the fallback.
        var get = await ProbeAsync(uri, HttpMethod.Get, cancellationToken).ConfigureAwait(false);
        return get == ProbeOutcome.Image;
    }

    private async Task<ProbeOutcome> ProbeAsync(Uri uri, HttpMethod method, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CreateTimeout(cancellationToken, ImageProbeTimeout);
            using var request = new HttpRequestMessage(method, uri);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;
            if (status is 400 or 403 or 405 or 501)
            {
                return ProbeOutcome.Unknown;
            }

            if (status is < 200 or >= 400)
            {
                return ProbeOutcome.NotImage;
            }

            var type = response.Content.Headers.ContentType?.MediaType;
            if (type is null)
            {
                return ProbeOutcome.Unknown;
            }

            return type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? ProbeOutcome.Image
                : ProbeOutcome.NotImage;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                                            or IOException or JsonException or InvalidOperationException)
        {
            return ProbeOutcome.Unknown;
        }
    }

    // HTTP plumbing

    private async Task<JsonDocument?> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CreateTimeout(cancellationToken, RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
                                            or IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    // Host helpers

    private static Uri? TryUri(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
               && uri.Scheme is "http" or "https"
               && !string.IsNullOrEmpty(uri.Host)
            ? uri
            : null;
    }

    private static string Authority(Uri uri) =>
        uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";

    /// <summary>Drops CDN hosts; <c>ice1.somafm.com</c> → <c>somafm.com</c>.</summary>
    private static string? BrandHost(string? rawHost)
    {
        if (string.IsNullOrEmpty(rawHost))
        {
            return null;
        }

        var host = rawHost.ToLowerInvariant();
        foreach (var cdn in CdnHosts)
        {
            if (host == cdn || host.EndsWith($".{cdn}", StringComparison.Ordinal))
            {
                return null;
            }
        }

        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? string.Join('.', parts[^2..]) : host;
    }

    private static string FaviconService(string host) =>
        $"https://www.google.com/s2/favicons?sz=128&domain={Uri.EscapeDataString(host)}";

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // Cache

    private bool TryGetCached(string key, out object? value)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expires > DateTime.UtcNow)
                {
                    value = entry.Value;
                    return true;
                }

                _cache.Remove(key);
            }
        }

        value = null;
        return false;
    }

    private void Store(string key, object? value)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_cache.Count >= CacheCapacity)
            {
                foreach (var stale in _cache.Where(pair => pair.Value.Expires <= now).Select(pair => pair.Key).ToList())
                {
                    _cache.Remove(stale);
                }

                while (_cache.Count >= CacheCapacity)
                {
                    _cache.Remove(_cache.Keys.First());
                }
            }

            _cache[key] = new CacheEntry(value, now + CacheTtl);
        }
    }

    private enum ProbeOutcome
    {
        Unknown,
        Image,
        NotImage,
    }

    private readonly record struct CacheEntry(object? Value, DateTime Expires);
}
