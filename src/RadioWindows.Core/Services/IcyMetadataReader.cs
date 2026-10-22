using System.Net.Http.Headers;
using System.Text;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// Side-channel ICY reader: opens the stream with <c>Icy-MetaData: 1</c> and
/// emits <c>StreamTitle</c> updates. libVLC plays the audio, this reader only
/// watches the metadata blocks, exactly like the macOS app.
/// </summary>
public sealed class IcyMetadataReader : IIcyMetadataReader
{
    private readonly HttpClient _http;

    public IcyMetadataReader(HttpClient? http = null)
    {
        _http = http ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(8),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioWindows/1.0");
    }

    public event Action<string>? TitleChanged;

    public async Task RunAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ReadOnceAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                // Retry a few seconds later unless the station changed.
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ReadOnceAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var metaint = ReadHeaderInt(response, "icy-metaint");
        if (metaint is null or <= 0)
        {
            return;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var audio = new byte[metaint.Value];
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await ReadExactlyAsync(stream, audio, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var lengthByte = new byte[1];
            if (!await ReadExactlyAsync(stream, lengthByte, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var length = lengthByte[0] * 16;
            if (length == 0)
            {
                continue;
            }

            var block = new byte[length];
            if (!await ReadExactlyAsync(stream, block, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            var title = NowPlayingParser.UnwrapIcyBlock(Encoding.UTF8.GetString(block));
            if (!string.IsNullOrWhiteSpace(title))
            {
                TitleChanged?.Invoke(title);
            }
        }
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static int? ReadHeaderInt(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return int.TryParse(values.FirstOrDefault(), out var value) ? value : null;
    }
}
