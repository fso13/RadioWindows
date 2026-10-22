using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// Resolves the current track from station APIs (SomaFM, Radio Paradise), an
/// Icecast status page or in-band ICY metadata, and finds missing artwork via
/// the iTunes Search API. Port of the macOS NowPlayingLookup: every provider is
/// best-effort, so a failure yields <c>null</c> instead of an exception.
/// </summary>
public sealed class NowPlayingLookup : INowPlayingLookup
{
    private const string UserAgent = "RadioWindows/1.0";
    private const int MaxMetadataInterval = 256_000;
    private const int MaxMetadataBlocks = 4;
    private const int CacheCapacity = 64;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public NowPlayingLookup(HttpClient? http = null)
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

    /// <summary>
    /// Tries SomaFM, Radio Paradise and the Icecast status page in that order;
    /// when they all miss and <paramref name="allowIcy"/> is set, the stream is
    /// probed for an in-band <c>StreamTitle</c> block.
    /// </summary>
    public async Task<NowPlaying?> ResolveAsync(
        string streamUrl,
        string stationName,
        bool allowIcy,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(streamUrl, UriKind.Absolute, out var stream)
            || stream.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(stream.Host))
        {
            return null;
        }

        var key = $"track\u001f{streamUrl}\u001f{stationName}\u001f{allowIcy}";
        if (TryGetCached(key, out var cached))
        {
            return cached as NowPlaying;
        }

        NowPlaying? track = null;
        try
        {
            track = await FromSomaFmAsync(stream, cancellationToken).ConfigureAwait(false)
                ?? await FromRadioParadiseAsync(stream, cancellationToken).ConfigureAwait(false)
                ?? await FromIcecastAsync(stream, stationName, cancellationToken).ConfigureAwait(false);

            if (track is null && allowIcy && !LooksLikePlaylist(stream))
            {
                track = await FromIcyAsync(stream, stationName, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Lookups are decorative: a broken API must never break playback.
            track = null;
        }

        Store(key, track);
        return track;
    }

    /// <summary>Queries iTunes for cover art when the track carries none.</summary>
    public async Task<string?> ArtworkForAsync(NowPlaying track, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (track.HasArtwork)
        {
            return track.ArtworkUrl;
        }

        var song = track.DisplayTitle;
        if (song.Length < 3)
        {
            return null;
        }

        var query = track.DisplayArtist is { } artist ? $"{artist} {song}" : song;
        query = query.Trim();
        if (query.Length == 0)
        {
            return null;
        }

        var key = $"art\u001f{query}";
        if (TryGetCached(key, out var cached))
        {
            return cached as string;
        }

        string? artwork;
        try
        {
            artwork = await ITunesArtworkAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            artwork = null;
        }

        Store(key, artwork);
        return artwork;
    }

    // SomaFM

    private async Task<NowPlaying?> FromSomaFmAsync(Uri stream, CancellationToken cancellationToken)
    {
        if (!stream.Host.Contains("somafm.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // ice1.somafm.com/groovesalad-128-mp3 → https://somafm.com/songs/groovesalad.json
        var channel = SomaChannel(stream);
        if (channel is null)
        {
            return null;
        }

        using var json = await GetJsonAsync(
            new Uri($"https://somafm.com/songs/{channel}.json"),
            cancellationToken).ConfigureAwait(false);
        if (json is null || !json.RootElement.TryGetProperty("songs", out var songs)
            || songs.ValueKind != JsonValueKind.Array || songs.GetArrayLength() == 0)
        {
            return null;
        }

        var song = songs[0];
        var title = Text(song, "title");
        if (title is null)
        {
            return null;
        }

        return Build(
            Text(song, "artist"),
            title,
            Text(song, "album"),
            SanitizeArtwork(Text(song, "albumArt")));
    }

    private static string? SomaChannel(Uri stream)
    {
        var segments = stream.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var dash = segments[0].IndexOf('-');
        if (dash == 0)
        {
            return null;
        }

        var channel = dash > 0 ? segments[0][..dash] : segments[0];
        return channel.Length == 0 ? null : channel;
    }

    // Radio Paradise

    private async Task<NowPlaying?> FromRadioParadiseAsync(Uri stream, CancellationToken cancellationToken)
    {
        if (!stream.Host.Contains("radioparadise.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = stream.AbsolutePath.ToLowerInvariant();
        var channel = path.Contains("mellow", StringComparison.Ordinal) ? 1
            : path.Contains("rock", StringComparison.Ordinal) ? 2
            : path.Contains("global", StringComparison.Ordinal) || path.Contains("world", StringComparison.Ordinal) ? 3
            : 0;

        using var json = await GetJsonAsync(
            new Uri($"https://api.radioparadise.com/api/nowplaying_list?chan={channel}"),
            cancellationToken).ConfigureAwait(false);
        if (json is null || !json.RootElement.TryGetProperty("song", out var songs)
            || songs.ValueKind != JsonValueKind.Object
            || !songs.TryGetProperty("0", out var song))
        {
            return null;
        }

        var title = Text(song, "title");
        if (title is null)
        {
            return null;
        }

        var cover = Text(song, "cover");
        var artwork = SanitizeArtwork(cover is null
            ? null
            : $"https://img.radioparadise.com/{cover.TrimStart('/')}");

        return Build(Text(song, "artist"), title, Text(song, "album"), artwork);
    }

    // Icecast status page

    private async Task<NowPlaying?> FromIcecastAsync(Uri stream, string stationName, CancellationToken cancellationToken)
    {
        var authority = stream.IsDefaultPort ? stream.Host : $"{stream.Host}:{stream.Port}";
        if (!Uri.TryCreate($"{stream.Scheme}://{authority}/status-json.xsl", UriKind.Absolute, out var statusUrl))
        {
            return null;
        }

        using var json = await GetJsonAsync(statusUrl, cancellationToken).ConfigureAwait(false);
        if (json is null || !json.RootElement.TryGetProperty("icestats", out var stats)
            || stats.ValueKind != JsonValueKind.Object
            || !stats.TryGetProperty("source", out var source))
        {
            return null;
        }

        var sources = source.ValueKind switch
        {
            JsonValueKind.Object => new[] { source },
            JsonValueKind.Array => source.EnumerateArray()
                .Where(candidate => candidate.ValueKind == JsonValueKind.Object)
                .ToArray(),
            _ => Array.Empty<JsonElement>(),
        };
        if (sources.Length == 0)
        {
            return null;
        }

        // Match the mount point; a lone source is used even without a match.
        var mount = stream.AbsolutePath;
        JsonElement chosen = default;
        var found = false;
        if (!string.IsNullOrEmpty(mount))
        {
            foreach (var sourceEntry in sources)
            {
                var listenUrl = Text(sourceEntry, "listenurl");
                if (listenUrl is not null && listenUrl.Contains(mount, StringComparison.Ordinal))
                {
                    chosen = sourceEntry;
                    found = true;
                    break;
                }
            }
        }

        if (!found && sources.Length == 1)
        {
            chosen = sources[0];
            found = true;
        }

        if (!found)
        {
            return null;
        }

        var title = Text(chosen, "title") ?? Text(chosen, "yp_currently_playing");
        return title is null ? null : NowPlayingParser.Parse(title, stationName);
    }

    // ICY sidecar probe

    private async Task<NowPlaying?> FromIcyAsync(Uri stream, string stationName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, stream);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");

        using var timeout = CreateTimeout(cancellationToken, RequestTimeout);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var interval = HeaderInt(response, "icy-metaint");
        if (interval is null or <= 0 or > MaxMetadataInterval)
        {
            return null;
        }

        var metaint = interval.Value;
        var needed = metaint * MaxMetadataBlocks + 1 + 255 * 16;
        var buffer = new byte[needed];
        var received = 0;

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        try
        {
            // A live stream never ends: stop at the deadline and parse what arrived.
            while (received < needed)
            {
                var read = await body
                    .ReadAsync(buffer.AsMemory(received, needed - received), timeout.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                received += read;
            }
        }
        catch (OperationCanceledException)
        {
            // Partial audio is enough to reach the first metadata block.
        }

        return ParseIcyBlocks(buffer, received, metaint, stationName);
    }

    private static NowPlaying? ParseIcyBlocks(byte[] buffer, int count, int metaint, string? stationName)
    {
        var offset = 0;
        for (var blockIndex = 0; blockIndex < MaxMetadataBlocks; blockIndex++)
        {
            var metaAt = offset + metaint;
            if (count <= metaAt)
            {
                break;
            }

            var length = buffer[metaAt] * 16;
            offset = metaAt + 1;
            if (length == 0)
            {
                continue;
            }

            if (count < offset + length)
            {
                break;
            }

            var block = Encoding.UTF8.GetString(buffer, offset, length);
            var parsed = NowPlayingParser.Parse(block, stationName);
            if (parsed is not null)
            {
                return parsed;
            }

            offset += length;
        }

        return null;
    }

    // iTunes artwork search

    private async Task<string?> ITunesArtworkAsync(string query, CancellationToken cancellationToken)
    {
        var url = new Uri(
            $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query)}&media=music&entity=song&limit=1");
        using var json = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        if (json is null || !json.RootElement.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return null;
        }

        var raw = Text(results[0], "artworkUrl100");
        if (raw is null)
        {
            return null;
        }

        return SanitizeArtwork(raw.Replace("100x100bb", "600x600bb", StringComparison.OrdinalIgnoreCase));
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

    private static int? HeaderInt(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values)
            && int.TryParse(values.FirstOrDefault(), out var fromHeaders))
        {
            return fromHeaders;
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues)
            && int.TryParse(contentValues.FirstOrDefault(), out var fromContent))
        {
            return fromContent;
        }

        return null;
    }

    // Helpers

    private static NowPlaying Build(string? artist, string title, string? album, string? artwork) => new()
    {
        Raw = string.Join(" — ", new[] { artist, title }.Where(part => !string.IsNullOrWhiteSpace(part))),
        Artist = artist,
        Title = title,
        Album = album,
        ArtworkUrl = artwork,
    };

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

    private static string? SanitizeArtwork(string? url) =>
        NowPlayingParser.LooksLikeImageUrl(url) ? url!.Trim() : null;

    private static bool LooksLikePlaylist(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".m3u8", StringComparison.Ordinal)
               || path.EndsWith(".m3u", StringComparison.Ordinal);
    }

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

    private readonly record struct CacheEntry(object? Value, DateTime Expires);
}
