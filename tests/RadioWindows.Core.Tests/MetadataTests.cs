using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

/// <summary>
/// Provider-order tests for <see cref="NowPlayingLookup"/>. Every request goes
/// through a fake handler, so the suite never touches the network.
/// </summary>
public sealed class MetadataTests
{
    [Fact]
    public async Task Resolves_somafm_channel_from_json()
    {
        using var handler = new FakeHandler().Json(
            "https://somafm.com/songs/groovesalad.json",
            """
            {"songs":[{"title":"Café Del Mar","artist":"Energy 52","album":"The Album","albumArt":"https://somafm.com/img/gs400.jpg"}]}
            """);
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var track = await lookup.ResolveAsync(
            "https://ice1.somafm.com/groovesalad-128-mp3",
            "Groove Salad",
            allowIcy: true);

        Assert.NotNull(track);
        Assert.Equal("Energy 52", track!.Artist);
        Assert.Equal("Café Del Mar", track.Title);
        Assert.Equal("The Album", track.Album);
        Assert.Equal("Energy 52 — Café Del Mar", track.Raw);
        Assert.Equal("https://somafm.com/img/gs400.jpg", track.ArtworkUrl);
        Assert.Contains(handler.Calls, call => call.Url == "https://somafm.com/songs/groovesalad.json");
    }

    [Fact]
    public async Task Picks_radio_paradise_channel_from_mount()
    {
        using var handler = new FakeHandler().Json(
            "https://api.radioparadise.com/api/nowplaying_list?chan=1",
            """
            {"song":{"0":{"title":"Teardrop","artist":"Massive Attack","album":"Mezzanine","cover":"covers/l/1234.jpg"}}}
            """);
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var track = await lookup.ResolveAsync(
            "https://stream.radioparadise.com/mellow-320",
            "Radio Paradise",
            allowIcy: true);

        Assert.NotNull(track);
        Assert.Equal("Massive Attack", track!.Artist);
        Assert.Equal("Teardrop", track.Title);
        Assert.Equal("https://img.radioparadise.com/covers/l/1234.jpg", track.ArtworkUrl);
        Assert.Contains(handler.Calls, call => call.Url.Contains("chan=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Uses_icecast_status_source_matching_the_mount()
    {
        using var handler = new FakeHandler().Json(
            "http://icecast.example.com:8000/status-json.xsl",
            """
            {"icestats":{"source":[
              {"listenurl":"http://icecast.example.com:8000/other","title":"Other - Song"},
              {"listenurl":"http://icecast.example.com:8000/live","title":"Portishead - Roads"}
            ]}}
            """);
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var track = await lookup.ResolveAsync("http://icecast.example.com:8000/live", "Test FM", allowIcy: false);

        Assert.NotNull(track);
        Assert.Equal("Portishead", track!.Artist);
        Assert.Equal("Roads", track.Title);
        Assert.Contains(handler.Calls, call => call.Url == "http://icecast.example.com:8000/status-json.xsl");
    }

    [Fact]
    public async Task Reads_icy_metadata_block_from_the_stream()
    {
        var block = Encoding.UTF8.GetBytes("StreamTitle='Daft Punk - Around the World';");
        using var handler = new FakeHandler().Bytes(
            "http://stream.example.com/live",
            BuildIcyPayload(metaInt: 16, block),
            "audio/mpeg",
            metaInt: 16);
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var track = await lookup.ResolveAsync("http://stream.example.com/live", "Test FM", allowIcy: true);

        Assert.NotNull(track);
        Assert.Equal("Daft Punk", track!.Artist);
        Assert.Equal("Around the World", track.Title);
        Assert.Contains(handler.Calls, call => call.Method == "GET" && call.Url == "http://stream.example.com/live");
    }

    [Fact]
    public async Task Does_not_probe_playlists_with_icy()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var track = await lookup.ResolveAsync("http://stream.example.com/live.m3u", "Test FM", allowIcy: true);

        Assert.Null(track);
        Assert.DoesNotContain(handler.Calls, call => call.Url.EndsWith(".m3u", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Returns_null_when_providers_fail()
    {
        using var handler = new FakeHandler().Throws();
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var track = await lookup.ResolveAsync("http://icecast.example.com/live", "Test FM", allowIcy: true);

        Assert.Null(track);
    }

    [Fact]
    public async Task Caches_a_resolved_track_briefly()
    {
        using var handler = new FakeHandler().Json(
            "https://somafm.com/songs/indiepop.json",
            """{"songs":[{"title":"Song","artist":"Band"}]}""");
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        var first = await lookup.ResolveAsync("https://ice2.somafm.com/indiepop-128-mp3", "Indie Pop", allowIcy: true);
        var afterFirst = handler.Calls.Count;
        var second = await lookup.ResolveAsync("https://ice2.somafm.com/indiepop-128-mp3", "Indie Pop", allowIcy: true);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(afterFirst, handler.Calls.Count);
    }

    [Fact]
    public async Task Upscales_itunes_artwork_to_600px()
    {
        using var handler = new FakeHandler().Json(
            "https://itunes.apple.com/search",
            """
            {"resultCount":1,"results":[{"artworkUrl100":"https://is1-ssl.mzstatic.com/image/thumb/Music/100x100bb.jpg"}]}
            """);
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);
        var track = new NowPlaying { Artist = "Massive Attack", Title = "Teardrop" };

        var artwork = await lookup.ArtworkForAsync(track);

        Assert.Equal("https://is1-ssl.mzstatic.com/image/thumb/Music/600x600bb.jpg", artwork);
        var query = Uri.UnescapeDataString(handler.Calls[0].Url);
        Assert.Contains("itunes.apple.com/search", query, StringComparison.Ordinal);
        Assert.Contains("term=Massive Attack Teardrop", query, StringComparison.Ordinal);
        Assert.Contains("media=music", query, StringComparison.Ordinal);
        Assert.Contains("limit=1", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_artwork_the_track_already_has()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);
        var track = new NowPlaying { Title = "Teardrop", ArtworkUrl = "https://example.com/cover.jpg" };

        var artwork = await lookup.ArtworkForAsync(track);

        Assert.Equal("https://example.com/cover.jpg", artwork);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Rejects_itunes_urls_that_are_not_images()
    {
        using var handler = new FakeHandler().Json(
            "https://itunes.apple.com/search",
            """{"resultCount":1,"results":[{"artworkUrl100":"https://example.com/cover?id=42"}]}""");
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        Assert.Null(await lookup.ArtworkForAsync(new NowPlaying { Title = "Teardrop" }));
    }

    [Fact]
    public async Task Skips_itunes_for_very_short_titles()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var lookup = new NowPlayingLookup(client);

        Assert.Null(await lookup.ArtworkForAsync(new NowPlaying { Title = "FM" }));
        Assert.Empty(handler.Calls);
    }

    private static byte[] BuildIcyPayload(int metaInt, byte[] block)
    {
        var padding = (block.Length + 15) / 16;
        var payload = new byte[metaInt + 1 + (padding * 16)];
        payload[metaInt] = (byte)padding;
        block.CopyTo(payload, metaInt + 1);
        return payload;
    }

    /// <summary>Routes requests to canned responses; anything unknown answers 404.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<Route> _routes = [];

        public List<(string Method, string Url)> Calls { get; } = [];

        public FakeHandler Json(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            On(url, _ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        public FakeHandler Bytes(string url, byte[] body, string contentType, int? metaInt = null) =>
            On(url, _ => Build(body, contentType, metaInt));

        public FakeHandler Throws() => On(string.Empty, _ => throw new HttpRequestException("offline"));

        private FakeHandler On(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _routes.Add(new Route(request => Matches(request.RequestUri!.ToString(), url), respond));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls.Add((request.Method.Method, request.RequestUri!.ToString()));
            foreach (var route in _routes)
            {
                if (route.Match(request))
                {
                    return Task.FromResult(route.Respond(request));
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Build(byte[] body, string contentType, int? metaInt)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            if (metaInt is { } value)
            {
                response.Headers.TryAddWithoutValidation("icy-metaint", value.ToString());
            }

            return response;
        }

        // Exact URL, or a key that ends at the query string (so tests can ignore it).
        private static bool Matches(string actual, string key) =>
            key.Length == 0 || actual.Equals(key, StringComparison.OrdinalIgnoreCase)
            || actual.StartsWith(key, StringComparison.OrdinalIgnoreCase);

        private sealed record Route(
            Func<HttpRequestMessage, bool> Match,
            Func<HttpRequestMessage, HttpResponseMessage> Respond);
    }
}
