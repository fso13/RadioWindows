using System.Net;
using System.Text;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class UpdateServiceTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v9.9.9",
          "html_url": "https://github.com/fso13/RadioWindows/releases/tag/v9.9.9",
          "body": "Release notes",
          "published_at": "2026-10-20T10:00:00Z",
          "assets": [
            {
              "name": "RadioWindows-Setup.exe",
              "browser_download_url": "https://example.com/RadioWindows-Setup.exe"
            },
            {
              "name": "RadioWindows-portable.zip",
              "browser_download_url": "https://example.com/RadioWindows-portable.zip"
            }
          ]
        }
        """;

    [Fact]
    public async Task Parses_a_newer_release()
    {
        var handler = new FakeHandler(ReleaseJson);
        using var http = new HttpClient(handler);
        var service = new UpdateService("fso13/RadioWindows", http);

        var info = await service.CheckAsync(CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal("v9.9.9", info.Tag);
        Assert.Equal("Release notes", info.Notes);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), info.PublishedAt);
        Assert.Equal("https://github.com/fso13/RadioWindows/releases/tag/v9.9.9", info.PageUrl);
        Assert.EndsWith("RadioWindows-Setup.exe", info.InstallerUrl);
        Assert.EndsWith("RadioWindows-portable.zip", info.PortableUrl);
        Assert.Same(info, service.Latest);
        Assert.False(service.IsChecking);

        Assert.Equal(
            "https://api.github.com/repos/fso13/RadioWindows/releases/latest",
            handler.RequestUri?.ToString());
        Assert.Contains("RadioWindows/", handler.UserAgent);
    }

    [Fact]
    public async Task Returns_null_when_the_release_is_not_newer()
    {
        var handler = new FakeHandler("""
            {
              "tag_name": "v0.0.1",
              "html_url": "https://github.com/fso13/RadioWindows/releases/tag/v0.0.1",
              "body": "",
              "published_at": "2026-10-20T10:00:00Z",
              "assets": []
            }
            """);
        using var http = new HttpClient(handler);
        var service = new UpdateService("fso13/RadioWindows", http);

        Assert.Null(await service.CheckAsync(CancellationToken.None));
        Assert.Null(service.Latest);
        Assert.False(service.IsChecking);
    }

    [Fact]
    public async Task Returns_null_on_a_failed_request()
    {
        var handler = new FakeHandler("{}", HttpStatusCode.Forbidden);
        using var http = new HttpClient(handler);
        var service = new UpdateService("fso13/RadioWindows", http);

        Assert.Null(await service.CheckAsync(CancellationToken.None));
        Assert.Null(service.Latest);
        Assert.False(service.IsChecking);
    }

    [Fact]
    public async Task Returns_null_on_malformed_json()
    {
        var handler = new FakeHandler("not json");
        using var http = new HttpClient(handler);
        var service = new UpdateService("fso13/RadioWindows", http);

        Assert.Null(await service.CheckAsync(CancellationToken.None));
        Assert.False(service.IsChecking);
    }

    [Fact]
    public async Task Returns_null_when_cancelled()
    {
        var handler = new FakeHandler(ReleaseJson);
        using var http = new HttpClient(handler);
        var service = new UpdateService("fso13/RadioWindows", http);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Null(await service.CheckAsync(cancellation.Token));
        Assert.False(service.IsChecking);
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("0.9.9", "1.0.0", false)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("1.0.1", "1.0", true)]
    [InlineData("1.0.0-beta.2", "1.0.0", false)]
    public void Compares_versions_semantically(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerVersion(candidate, current));
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3")]
    [InlineData("1.2.3+build.5", "1.2.3")]
    [InlineData(" 1.2.3 ", "1.2.3")]
    public void Normalizes_version_tags(string tag, string expected)
    {
        Assert.Equal(expected, UpdateService.NormalizeVersion(tag));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public FakeHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public Uri? RequestUri { get; private set; }

        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
