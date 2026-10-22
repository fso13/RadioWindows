using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

/// <summary>
/// Source-order tests for <see cref="LogoResolver"/>. Every request is served by
/// a fake handler, so the suite never touches the network.
/// </summary>
public sealed class LogoResolverTests
{
    private const string SearchUrl = "https://de1.api.radio-browser.info/json/stations/search";

    [Fact]
    public async Task Prefers_radio_browser_favicon_matching_the_stream_url()
    {
        using var handler = new FakeHandler()
            .Json($"{SearchUrl}?url=", """[{"favicon":"https://cdn.example.org/logo.png"}]""")
            .Image("https://cdn.example.org/logo.png");
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync("http://icecast.example.com:8000/live", null, "Example FM");

        Assert.Equal("https://cdn.example.org/logo.png", logo);
        Assert.Contains(handler.Calls, call => call.Url.StartsWith($"{SearchUrl}?url=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Falls_back_to_a_name_search_when_the_url_is_unknown()
    {
        using var handler = new FakeHandler()
            .Json($"{SearchUrl}?url=", "[]")
            .Json($"{SearchUrl}?name=", """[{"favicon":"https://cdn.example.org/logo.png"}]""")
            .Image("https://cdn.example.org/logo.png");
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync("http://icecast.example.com:8000/live", null, "Example FM");

        Assert.Equal("https://cdn.example.org/logo.png", logo);
        Assert.Contains(handler.Calls, call => call.Url.StartsWith($"{SearchUrl}?name=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skips_favicons_that_are_not_images()
    {
        using var handler = new FakeHandler()
            .Json($"{SearchUrl}?url=", """[{"favicon":"https://cdn.example.org/logo.png"}]""")
            .Json($"{SearchUrl}?name=", "[]")
            .Bytes("https://cdn.example.org/logo.png", [1, 2, 3], "text/html")
            .Image("https://example.com/favicon.ico");
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync(
            "http://icecast.example.com:8000/live",
            "https://example.com/radio",
            "Example FM");

        Assert.Equal("https://example.com/favicon.ico", logo);
    }

    [Fact]
    public async Task Falls_back_to_the_brand_domain_favicon()
    {
        using var handler = new FakeHandler()
            .Json($"{SearchUrl}?name=", "[]")
            .Image("https://somafm.com/favicon.ico");
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync(
            "http://ice1.somafm.com/groovesalad-128-mp3",
            null,
            "Groove Salad");

        Assert.Equal("https://somafm.com/favicon.ico", logo);
        Assert.DoesNotContain(handler.Calls, call => call.Url.StartsWith("https://ice1.somafm.com/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skips_cdn_hosts()
    {
        using var handler = new FakeHandler();
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync("http://stream.hostingradio.ru:8080/live", null, "");

        Assert.Null(logo);
        Assert.DoesNotContain(handler.Calls, call => call.Url.Contains("favicon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Retries_with_get_when_head_is_rejected()
    {
        const string favicon = "https://cdn.example.org/logo.png";
        using var handler = new FakeHandler()
            .Json($"{SearchUrl}?url=", $$"""[{"favicon":"{{favicon}}"}]""")
            .HeadRejected(favicon)
            .Image(favicon);
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync("http://icecast.example.com:8000/live", null, "Example FM");

        Assert.Equal(favicon, logo);
        Assert.Contains(handler.Calls, call => call.Method == "GET" && call.Url == favicon);
    }

    [Fact]
    public async Task Caches_a_resolved_logo()
    {
        using var handler = new FakeHandler()
            .Json($"{SearchUrl}?url=", """[{"favicon":"https://cdn.example.org/logo.png"}]""")
            .Image("https://cdn.example.org/logo.png");
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var first = await resolver.ResolveAsync("http://icecast.example.com:8000/live", null, "Example FM");
        var afterFirst = handler.Calls.Count;
        var second = await resolver.ResolveAsync("http://icecast.example.com:8000/live", null, "Example FM");

        Assert.Equal(first, second);
        Assert.Equal(afterFirst, handler.Calls.Count);
    }

    [Fact]
    public async Task Returns_null_when_every_source_fails()
    {
        using var handler = new FakeHandler().Throws();
        using var client = new HttpClient(handler);
        var resolver = new LogoResolver(client);

        var logo = await resolver.ResolveAsync(
            "http://icecast.example.com:8000/live",
            "https://example.com",
            "Example FM");

        Assert.Null(logo);
    }

    /// <summary>Routes requests to canned responses; anything unknown answers 404.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly List<Route> _routes = [];

        public List<(string Method, string Url)> Calls { get; } = [];

        public FakeHandler Json(string url, string body) =>
            On(url, _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        public FakeHandler Bytes(string url, byte[] body, string contentType) =>
            On(url, _ => Build(body, contentType));

        public FakeHandler Image(string url) => Bytes(url, [1, 2, 3], "image/png");

        public FakeHandler HeadRejected(string url) => On(url, request =>
            request.Method == HttpMethod.Head
                ? new HttpResponseMessage(HttpStatusCode.MethodNotAllowed)
                : Build([1, 2, 3], "image/png"));

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

        private static HttpResponseMessage Build(byte[] body, string contentType)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
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
