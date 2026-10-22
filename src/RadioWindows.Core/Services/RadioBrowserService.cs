using System.Globalization;
using System.Text;
using System.Text.Json;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>One station returned by the public Radio Browser directory.</summary>
/// <param name="Id">Radio Browser <c>stationuuid</c>; reused as <see cref="RadioStation.ExternalId"/>.</param>
/// <param name="Name">Display name as published by the directory.</param>
/// <param name="Url">Playable stream URL (prefers <c>url_resolved</c> over <c>url</c>).</param>
/// <param name="LogoUrl">Favicon, when the directory has one.</param>
/// <param name="Country">Country name as published by the directory.</param>
/// <param name="Genre">First tag; not normalised yet, <see cref="ToStation"/> does that.</param>
/// <param name="Codec">Stream codec such as <c>mp3</c> or <c>aac</c>.</param>
/// <param name="Bitrate">Nominal bitrate in kbps, zero when unknown.</param>
/// <param name="Votes">Directory popularity vote count.</param>
public sealed record RadioBrowserStation(
    string Id,
    string Name,
    string Url,
    string? LogoUrl,
    string Country,
    string Genre,
    string Codec,
    int Bitrate,
    int Votes)
{
    /// <summary>Human-readable "country · codec · bitrate" summary.</summary>
    public string Details
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(Country))
            {
                parts.Add(Country.Trim());
            }

            if (!string.IsNullOrWhiteSpace(Codec))
            {
                parts.Add(Codec.Trim().ToUpperInvariant());
            }

            if (Bitrate > 0)
            {
                parts.Add(string.Format(CultureInfo.InvariantCulture, "{0} kbps", Bitrate));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Materialises a library station: custom (not built in) and linked to the directory.</summary>
    public RadioStation ToStation()
    {
        var name = Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            name = RadioStation.TryParseStream(Url)?.Host ?? "Radio Browser";
        }

        var details = Details;
        return new RadioStation
        {
            Id = "rb-" + Id,
            Name = name,
            Url = Url?.Trim() ?? string.Empty,
            Genre = GenreCatalog.Normalize(Genre),
            Comment = details.Length == 0 ? "Radio Browser" : "Radio Browser · " + details,
            LogoUrl = string.IsNullOrWhiteSpace(LogoUrl) ? null : LogoUrl.Trim(),
            BuiltIn = false,
            ExternalId = Id,
        };
    }
}

/// <summary>
/// Search client for the public Radio Browser directory.
/// Every lookup fails soft (empty result) so an offline UI still works; only the
/// caller's own cancellation token is propagated as an exception.
/// </summary>
public sealed class RadioBrowserService
{
    public const string BaseUrl = "https://all.api.radio-browser.info/";

    private const string UserAgentText = "RadioWindows/1.0 (Windows)";
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;
    private const int MaxGenres = 100;

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _countriesGate = new(1, 1);
    private readonly SemaphoreSlim _genresGate = new(1, 1);
    private IReadOnlyList<string>? _countries;
    private IReadOnlyList<string>? _genres;

    public RadioBrowserService(HttpClient? http = null)
    {
        if (http is null)
        {
            _http = new HttpClient(new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                ConnectTimeout = TimeSpan.FromSeconds(10),
            })
            {
                BaseAddress = new Uri(BaseUrl),
                Timeout = TimeSpan.FromSeconds(20),
            };
        }
        else
        {
            _http = http;
            _http.BaseAddress ??= new Uri(BaseUrl);
        }

        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgentText);
        }

        if (!_http.DefaultRequestHeaders.Contains("Accept"))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        }

        if (!_http.DefaultRequestHeaders.Contains("Accept-Encoding"))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "identity");
        }
    }

    /// <summary>
    /// Searches by name, tag and country (all filters are combined). Returns an empty
    /// list when every filter is blank, on transport errors and on non-success statuses.
    /// </summary>
    public async Task<IReadOnlyList<RadioBrowserStation>> SearchAsync(
        string? query,
        string? genre,
        string? country,
        int limit = 50,
        CancellationToken ct = default)
    {
        var name = Clean(query);
        var tag = Clean(genre);
        var area = Clean(country);
        if (name.Length == 0 && tag.Length == 0 && area.Length == 0)
        {
            return [];
        }

        var effectiveLimit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);
        var relative = new StringBuilder("json/stations/search?limit=")
            .Append(effectiveLimit.ToString(CultureInfo.InvariantCulture))
            .Append("&hidebroken=true&order=votes&reverse=true");

        if (name.Length > 0)
        {
            relative.Append("&name=").Append(Uri.EscapeDataString(name));
        }

        if (tag.Length > 0)
        {
            relative.Append("&tag=").Append(Uri.EscapeDataString(tag));
        }

        if (area.Length > 0)
        {
            relative.Append("&country=").Append(Uri.EscapeDataString(area));
        }

        var payload = await GetAsync(relative.ToString(), ct).ConfigureAwait(false);
        return ParseStations(payload);
    }

    /// <summary>Country names with at least one station, most popular first; cached in memory.</summary>
    public async Task<IReadOnlyList<string>> CountriesAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _countries) is { } cached)
        {
            return cached;
        }

        await _countriesGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_countries is { } already)
            {
                return already;
            }

            var payload = await GetAsync(
                "json/countries?hidebroken=true&order=stationcount&reverse=true",
                ct).ConfigureAwait(false);

            var names = ParseDirectory(payload, int.MaxValue);
            if (names.Count > 0)
            {
                Volatile.Write(ref _countries, names);
            }

            return names;
        }
        finally
        {
            _countriesGate.Release();
        }
    }

    /// <summary>Popular tags used as genres, most common first; cached in memory.</summary>
    public async Task<IReadOnlyList<string>> GenresAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _genres) is { } cached)
        {
            return cached;
        }

        await _genresGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_genres is { } already)
            {
                return already;
            }

            var payload = await GetAsync(
                "json/tags?hidebroken=true&order=stationcount&reverse=true&limit=300",
                ct).ConfigureAwait(false);

            var names = ParseDirectory(payload, MaxGenres);
            if (names.Count > 0)
            {
                Volatile.Write(ref _genres, names);
            }

            return names;
        }
        finally
        {
            _genresGate.Release();
        }
    }

    /// <summary>True when the directory entry is already in the library (same external id or stream URL).</summary>
    public bool IsDuplicateOf(RadioStation station, RadioBrowserStation candidate)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.IsNullOrWhiteSpace(station.ExternalId)
            && string.Equals(station.ExternalId.Trim(), candidate.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var existing = UrlKey(station.Url);
        return existing.Length > 0 && string.Equals(existing, UrlKey(candidate.Url), StringComparison.Ordinal);
    }

    /// <summary>GETs a directory path; null on any transport, status or payload failure.</summary>
    private async Task<string?> GetAsync(string relative, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relative);
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                          or OperationCanceledException
                                          or IOException
                                          or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Decodes a search response, dropping rows without a playable stream URL.</summary>
    private static IReadOnlyList<RadioBrowserStation> ParseStations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json, TolerantJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var stations = new List<RadioBrowserStation>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var url = Clean(Text(element, "url_resolved"));
                if (url.Length == 0)
                {
                    url = Clean(Text(element, "url"));
                }

                if (RadioStation.TryParseStream(url) is not { } stream)
                {
                    continue;
                }

                var id = Clean(Text(element, "stationuuid"));
                if (id.Length == 0)
                {
                    id = url;
                }

                if (!seen.Add(id))
                {
                    continue;
                }

                var name = Clean(Text(element, "name"));
                stations.Add(new RadioBrowserStation(
                    Id: id,
                    Name: name.Length == 0 ? stream.Host : name,
                    Url: url,
                    LogoUrl: NullIfEmpty(Text(element, "favicon")),
                    Country: Clean(Text(element, "country")),
                    Genre: FirstTag(Text(element, "tags")),
                    Codec: Clean(Text(element, "codec")),
                    Bitrate: Integer(element, "bitrate"),
                    Votes: Integer(element, "votes")));
            }

            return stations;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Decodes a countries/tags response into a popularity-ordered, de-duplicated list.</summary>
    private static IReadOnlyList<string> ParseDirectory(string? json, int max)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json, TolerantJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var entries = new List<(string Name, int Count)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = Clean(Text(element, "name"));
                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                var count = Integer(element, "stationcount");
                if (count <= 0)
                {
                    continue;
                }

                entries.Add((name, count));
            }

            return entries
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .Select(entry => entry.Name)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string FirstTag(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return string.Empty;
        }

        foreach (var tag in tags.Split(','))
        {
            var trimmed = tag.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    /// <summary>Scheme/host are case-insensitive and a trailing slash is ignored.</summary>
    private static string UrlKey(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.ToLowerInvariant();
        }

        return string.Concat(
            uri.Scheme.ToLowerInvariant(),
            "://",
            uri.Authority.ToLowerInvariant(),
            uri.AbsolutePath.TrimEnd('/'),
            uri.Query);
    }

    private static readonly JsonDocumentOptions TolerantJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static JsonElement? Find(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? Text(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static int Integer(JsonElement element, string name)
    {
        var value = Find(element, name);
        if (value is not { } number)
        {
            return 0;
        }

        return number.ValueKind switch
        {
            JsonValueKind.Number when number.TryGetInt32(out var parsed) => parsed,
            JsonValueKind.Number => (int)Math.Round(number.GetDouble(), MidpointRounding.AwayFromZero),
            JsonValueKind.String when int.TryParse(
                number.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
