using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// Reads ICY response headers to recover bitrate/codec/server before the engine
/// reports track properties. Never throws: diagnostics must not break playback.
/// </summary>
public sealed class StreamProbe
{
    private readonly HttpClient _http;

    public StreamProbe(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(6),
        })
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioWindows/1.0");
    }

    public async Task<ProbeResult?> ProbeAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var headers = response.Headers;
            var content = response.Content.Headers;

            int? bitrate = ParseInt(Header(response, "icy-br"));
            var codec = StreamInfo.CodecNameFromToken(content.ContentType?.MediaType ?? string.Empty);
            var name = Header(response, "icy-name");
            var server = Header(response, "Server") ?? Header(response, "icy-server");
            var metaint = ParseInt(Header(response, "icy-metaint"));

            if (bitrate is null && content.ContentType?.MediaType?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) == true)
            {
                codec = "HLS";
            }

            return new ProbeResult(codec, bitrate, name, server, metaint);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                           or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Header(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault()?.Trim();
        }

        if (response.Content.Headers.TryGetValues(name, out var contentValues))
        {
            return contentValues.FirstOrDefault()?.Trim();
        }

        return null;
    }

    private static int? ParseInt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var digits = new string(raw.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : null;
    }
}

public sealed record ProbeResult(string Codec, int? BitrateKbps, string? StationName, string? Server, int? MetadataInterval);
