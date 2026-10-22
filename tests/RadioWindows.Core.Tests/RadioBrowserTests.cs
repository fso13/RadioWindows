using System.Net;
using System.Text;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class RadioBrowserTests
{
    private const string SearchPayload = """
    [
      {
        "stationuuid": "96062",
        "name": " Radio Paradise ",
        "url": "http://stream.example.com/old",
        "url_resolved": "https://stream.example.com/main",
        "homepage": "https://example.com",
        "favicon": "https://example.com/favicon.png",
        "tags": "eclectic, rock, chill",
        "country": "United States",
        "codec": "mp3",
        "bitrate": 320,
        "votes": 1234
      },
      {
        "stationuuid": "96063",
        "name": "",
        "url": "http://broken.example.com/stream",
        "url_resolved": "",
        "favicon": "",
        "tags": "",
        "country": "Germany",
        "codec": "aac",
        "bitrate": 0,
        "votes": 7
      },
      {
        "stationuuid": "96064",
        "name": "Dead Stream",
        "url": "",
        "url_resolved": "",
        "favicon": "",
        "tags": "jazz",
        "country": "France",
        "codec": "mp3",
        "bitrate": 128,
        "votes": 3
      }
    ]
    """;

    private const string CountriesPayload = """
    [
      {"name": "Germany", "iso_3166_1": "DE", "stationcount": 1200},
      {"name": "France", "iso_3166_1": "FR", "stationcount": 800},
      {"name": "", "stationcount": 5},
      {"name": "Nowhere", "stationcount": 0},
      {"name": "germany", "stationcount": 4}
    ]
    """;

    private const string GenresPayload = """
    [
      {"name": "jazz", "stationcount": 500},
      {"name": "rock", "stationcount": 900},
      {"name": "Jazz", "stationcount": 10},
      {"name": "", "stationcount": 1},
      {"name": "silent", "stationcount": 0}
    ]
    """;

    [Fact]
    public async Task Search_maps_directory_fields_and_resolves_the_stream_url()
    {
        using var http = new HttpClient(new StubHandler(_ => StubHandler.Json(SearchPayload)));
        var service = new RadioBrowserService(http);

        var results = await service.SearchAsync("radio", "rock", "United States", 5);

        Assert.Equal(2, results.Count);
        var first = results[0];
        Assert.Equal("96062", first.Id);
        Assert.Equal("Radio Paradise", first.Name);
        Assert.Equal("https://stream.example.com/main", first.Url);
        Assert.Equal("https://example.com/favicon.png", first.LogoUrl);
        Assert.Equal("United States", first.Country);
        Assert.Equal("eclectic", first.Genre);
        Assert.Equal("mp3", first.Codec);
        Assert.Equal(320, first.Bitrate);
        Assert.Equal(1234, first.Votes);
        Assert.Equal("United States · MP3 · 320 kbps", first.Details);

        var fallback = results[1];
        Assert.Equal("broken.example.com", fallback.Name);
        Assert.Equal("http://broken.example.com/stream", fallback.Url);
        Assert.Null(fallback.LogoUrl);
        Assert.Equal(string.Empty, fallback.Genre);
    }

    [Fact]
    public async Task Search_encodes_parameters_and_sets_directory_options()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        await service.SearchAsync("jazz & blues", "lo-fi/chill", "Côte d'Ivoire", 25);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("all.api.radio-browser.info", request.Uri.Host);
        Assert.Equal("/json/stations/search", request.Uri.AbsolutePath);
        Assert.Contains("RadioWindows", request.UserAgent);

        var query = Query(request.Uri);
        Assert.Equal("25", query["limit"]);
        Assert.Equal("true", query["hidebroken"]);
        Assert.Equal("votes", query["order"]);
        Assert.Equal("true", query["reverse"]);
        Assert.Equal("jazz & blues", query["name"]);
        Assert.Equal("lo-fi/chill", query["tag"]);
        Assert.Equal("Côte d'Ivoire", query["country"]);
    }

    [Fact]
    public async Task Search_omits_blank_filters()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        await service.SearchAsync("  only name  ", null, "   ");

        var query = Query(Assert.Single(handler.Requests).Uri);
        Assert.Equal("only name", query["name"]);
        Assert.False(query.ContainsKey("tag"));
        Assert.False(query.ContainsKey("country"));
    }

    [Fact]
    public async Task Limit_is_clamped_to_the_directory_range()
    {
        var handler = new StubHandler(_ => StubHandler.Json("[]"));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        await service.SearchAsync("x", null, null, 0);
        await service.SearchAsync("x", null, null, 10_000);

        Assert.Equal("50", Query(handler.Requests[0].Uri)["limit"]);
        Assert.Equal("500", Query(handler.Requests[1].Uri)["limit"]);
    }

    [Fact]
    public async Task Empty_query_returns_nothing_without_a_request()
    {
        var handler = new StubHandler(_ => StubHandler.Json(SearchPayload));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        Assert.Empty(await service.SearchAsync("   ", null, string.Empty));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Server_errors_yield_an_empty_list(HttpStatusCode status)
    {
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(status)));
        var service = new RadioBrowserService(http);

        Assert.Empty(await service.SearchAsync("jazz", null, null));
    }

    [Fact]
    public async Task Transport_errors_yield_an_empty_list()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));
        var service = new RadioBrowserService(http);

        Assert.Empty(await service.SearchAsync("jazz", null, null));
        Assert.Empty(await service.CountriesAsync());
        Assert.Empty(await service.GenresAsync());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"error\": \"nope\"}")]
    [InlineData("")]
    public async Task Malformed_payloads_yield_an_empty_list(string body)
    {
        using var http = new HttpClient(new StubHandler(_ => StubHandler.Json(body)));
        var service = new RadioBrowserService(http);

        Assert.Empty(await service.SearchAsync("jazz", null, null));
    }

    [Fact]
    public void To_station_maps_to_a_custom_library_station()
    {
        var candidate = new RadioBrowserStation(
            "abc",
            "  Jazz FM ",
            "https://jazz.example/live",
            "https://jazz.example/logo.png",
            "France",
            "jazz",
            "mp3",
            192,
            42);

        var station = candidate.ToStation();

        Assert.Equal("rb-abc", station.Id);
        Assert.Equal("Jazz FM", station.Name);
        Assert.Equal("https://jazz.example/live", station.Url);
        Assert.False(station.BuiltIn);
        Assert.Equal("abc", station.ExternalId);
        Assert.Equal("Jazz", station.Genre);
        Assert.Equal("https://jazz.example/logo.png", station.LogoUrl);
        Assert.Contains("France", station.Comment);
        Assert.Contains("MP3", station.Comment);
        Assert.Contains("192 kbps", station.Comment);
    }

    [Fact]
    public void To_station_falls_back_to_the_host_and_drops_a_blank_logo()
    {
        var station = new RadioBrowserStation(
            "x",
            "",
            "https://host.example/live",
            "  ",
            "",
            "",
            "aac",
            0,
            0).ToStation();

        Assert.Equal("host.example", station.Name);
        Assert.Null(station.LogoUrl);
        Assert.Equal(GenreCatalog.Other, station.Genre);
        Assert.Equal("Radio Browser · AAC", station.Comment);
    }

    [Fact]
    public async Task Countries_are_popularity_ordered_and_cached()
    {
        var handler = new StubHandler(_ => StubHandler.Json(CountriesPayload));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        var first = await service.CountriesAsync();
        var second = await service.CountriesAsync();

        Assert.Equal(new[] { "Germany", "France" }, first);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
        Assert.Equal("/json/countries", Assert.Single(handler.Requests).Uri.AbsolutePath);
        Assert.Equal("true", Query(handler.Requests[0].Uri)["hidebroken"]);
    }

    [Fact]
    public async Task Genres_are_deduplicated_and_ordered_by_popularity()
    {
        var handler = new StubHandler(_ => StubHandler.Json(GenresPayload));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        var genres = await service.GenresAsync();

        Assert.Equal(new[] { "rock", "jazz" }, genres);
        Assert.Equal("/json/tags", Assert.Single(handler.Requests).Uri.AbsolutePath);
    }

    [Fact]
    public async Task A_failed_directory_lookup_is_not_cached()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : StubHandler.Json(CountriesPayload));
        using var http = new HttpClient(handler);
        var service = new RadioBrowserService(http);

        Assert.Empty(await service.CountriesAsync());
        Assert.Equal(new[] { "Germany", "France" }, await service.CountriesAsync());
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var http = new HttpClient(new StubHandler(_ => throw new OperationCanceledException(cts.Token)));
        var service = new RadioBrowserService(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SearchAsync("jazz", null, null, ct: cts.Token));
    }

    [Fact]
    public async Task Timeouts_are_treated_as_ordinary_failures()
    {
        using var http = new HttpClient(new StubHandler(_ => throw new TaskCanceledException("timed out")));
        var service = new RadioBrowserService(http);

        Assert.Empty(await service.SearchAsync("jazz", null, null));
    }

    [Fact]
    public void Duplicate_detection_uses_the_external_id_and_the_stream_url()
    {
        using var http = new HttpClient(new StubHandler(_ => StubHandler.Json("[]")));
        var service = new RadioBrowserService(http);
        var candidate = new RadioBrowserStation(
            "96062",
            "A",
            "https://a.example/live",
            null,
            "",
            "",
            "",
            0,
            0);

        Assert.True(service.IsDuplicateOf(new RadioStation { ExternalId = "96062" }, candidate));
        Assert.True(service.IsDuplicateOf(new RadioStation { Url = "https://A.example/live/" }, candidate));
        Assert.False(service.IsDuplicateOf(new RadioStation { Url = "https://b.example/live" }, candidate));
        Assert.False(service.IsDuplicateOf(new RadioStation(), candidate));
    }

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => parts[0],
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty,
                StringComparer.Ordinal);

    private sealed record RecordedRequest(Uri Uri, string UserAgent);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<RecordedRequest> Requests { get; } = [];

        public int Calls => Requests.Count;

        public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Headers.TryGetValues("User-Agent", out var agents)
                    ? string.Join(' ', agents)
                    : string.Empty));

            return Task.FromResult(_responder(request));
        }
    }
}
