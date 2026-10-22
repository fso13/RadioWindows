using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace RadioWindows.Controls;

/// <summary>Downloads artwork once, keeps an in-memory LRU and a disk cache.</summary>
public sealed class ImageCache
{
    private readonly HttpClient _http;
    private readonly string? _diskDirectory;
    private readonly ConcurrentDictionary<string, Bitmap?> _memory = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _inflight = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private const int MemoryLimit = 120;

    public ImageCache(string? diskDirectory = null)
    {
        _diskDirectory = diskDirectory;
        if (!string.IsNullOrEmpty(diskDirectory))
        {
            Directory.CreateDirectory(diskDirectory);
        }

        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioWindows/1.0 (+https://github.com/fso13/RadioWindows)");
    }

    public async Task<Bitmap?> GetAsync(string url, CancellationToken cancellationToken)
    {
        if (_memory.TryGetValue(url, out var cached))
        {
            return cached;
        }

        var task = _inflight.GetOrAdd(url, _ => LoadAsync(url, cancellationToken));
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            _inflight.TryRemove(url, out _);
        }
    }

    private async Task<Bitmap?> LoadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            var path = DiskPath(url);
            if (path is not null && File.Exists(path))
            {
                var bitmap = await ReadAsync(path).ConfigureAwait(false);
                if (bitmap is not null)
                {
                    Store(url, bitmap);
                    return bitmap;
                }
            }

            var bytes = await _http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
            if (path is not null)
            {
                try
                {
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Cache is best effort.
                }
            }

            using var stream = new MemoryStream(bytes);
            var loaded = new Bitmap(stream);
            Store(url, loaded);
            return loaded;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                           or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<Bitmap?> ReadAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Store(string url, Bitmap bitmap)
    {
        _memory[url] = bitmap;
        _order.Enqueue(url);
        while (_order.Count > MemoryLimit && _order.TryDequeue(out var oldest))
        {
            _memory.TryRemove(oldest, out _);
        }
    }

    private string? DiskPath(string url)
    {
        if (string.IsNullOrEmpty(_diskDirectory))
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        return Path.Combine(_diskDirectory, hash[..32] + ".img");
    }
}
