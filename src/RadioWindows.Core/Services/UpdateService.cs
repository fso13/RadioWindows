using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace RadioWindows.Core.Services;

/// <summary>A newer GitHub release, ready to be shown in the settings page.</summary>
public sealed record UpdateInfo(
    string Version,
    string Tag,
    string PageUrl,
    string? InstallerUrl,
    string? PortableUrl,
    string Notes,
    DateTimeOffset PublishedAt);

/// <summary>
/// GitHub release check used by the Windows build (the macOS build uses
/// Sparkle). The service only reads the latest stable release; downloading and
/// installing stay with the browser or the installer.
/// </summary>
public sealed class UpdateService : ObservableObject, IDisposable
{
    private const string DefaultRepository = "fso13/RadioWindows";
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly string _repository;
    private readonly HttpClient _http;
    private bool _isChecking;
    private UpdateInfo? _latest;

    public UpdateService(string repository = DefaultRepository, HttpClient? http = null)
    {
        _repository = string.IsNullOrWhiteSpace(repository) ? DefaultRepository : repository.Trim();
        _http = http ?? SharedClient;
    }

    public bool IsChecking => _isChecking;

    public UpdateInfo? Latest => _latest;

    /// <summary>Version of the running assembly, e.g. <c>1.0.0</c>.</summary>
    public string CurrentVersion
    {
        get
        {
            var version = typeof(UpdateService).Assembly.GetName().Version;
            return version is null
                ? "0.0.0"
                : $"{version.Major}.{Math.Max(0, version.Minor)}.{Math.Max(0, version.Build)}";
        }
    }

    // MARK: - Checking

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        if (_isChecking)
        {
            return null;
        }

        _isChecking = true;
        Raise(nameof(IsChecking));

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{_repository}/releases/latest");
            request.Headers.UserAgent.ParseAdd($"RadioWindows/{CurrentVersion}");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(true);
            using var document = await JsonDocument
                .ParseAsync(stream, default, cancellationToken)
                .ConfigureAwait(true);

            var info = Parse(document.RootElement);
            if (info is null || !IsNewerVersion(info.Version, CurrentVersion))
            {
                return null;
            }

            _latest = info;
            Raise(nameof(Latest));
            return info;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                              or OperationCanceledException
                                              or JsonException
                                              or IOException)
        {
            // Offline, rate-limited, cancelled or malformed: no update prompt.
            return null;
        }
        finally
        {
            _isChecking = false;
            Raise(nameof(IsChecking));
        }
    }

    // MARK: - Launching

    /// <summary>Opens the installer, portable archive or release page in the shell.</summary>
    public void OpenDownload(UpdateInfo info)
    {
        var target = info.InstallerUrl ?? info.PortableUrl ?? info.PageUrl;
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or PlatformNotSupportedException
                                              or FileNotFoundException)
        {
            // Opening a link is best effort.
        }
    }

    // MARK: - Parsing

    private UpdateInfo? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var tag = ReadString(root, "tag_name");
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var version = NormalizeVersion(tag);
        if (version.Length == 0)
        {
            return null;
        }

        var assets = new List<(string Name, string Url)>();
        var assetsElement = Find(root, "assets");
        if (assetsElement is { ValueKind: JsonValueKind.Array })
        {
            foreach (var asset in assetsElement.Value.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadString(asset, "name") ?? string.Empty;
                var url = ReadString(asset, "browser_download_url");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    assets.Add((name, url));
                }
            }
        }

        return new UpdateInfo(
            version,
            tag.Trim(),
            ReadString(root, "html_url") ?? $"https://github.com/{_repository}/releases",
            PickAsset(assets, name => name.Contains("setup", StringComparison.Ordinal)
                                      || name.Contains("install", StringComparison.Ordinal))
            ?? PickAsset(assets, name => IsInstaller(name)),
            PickAsset(assets, name => name.Contains("portable", StringComparison.Ordinal))
            ?? PickAsset(assets, name => name.EndsWith(".zip", StringComparison.Ordinal)),
            ReadString(root, "body") ?? string.Empty,
            ReadDate(root, "published_at") ?? DateTimeOffset.MinValue);
    }

    private static bool IsInstaller(string name) =>
        name.EndsWith(".exe", StringComparison.Ordinal) || name.EndsWith(".msi", StringComparison.Ordinal);

    private static string? PickAsset(
        IReadOnlyList<(string Name, string Url)> assets,
        Func<string, bool> predicate)
    {
        foreach (var asset in assets)
        {
            if (predicate(asset.Name.ToLowerInvariant()))
            {
                return asset.Url;
            }
        }

        return null;
    }

    // MARK: - Versions

    /// <summary>Strips a leading <c>v</c>, pre-release and build metadata.</summary>
    public static string NormalizeVersion(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            text = text[..suffix];
        }

        return text.Trim();
    }

    /// <summary>Semantic-ish comparison: numeric parts, missing parts count as zero.</summary>
    public static bool IsNewerVersion(string candidate, string current)
    {
        var left = VersionParts(NormalizeVersion(candidate));
        var right = VersionParts(NormalizeVersion(current));
        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
        {
            var candidatePart = index < left.Count ? left[index] : 0;
            var currentPart = index < right.Count ? right[index] : 0;
            if (candidatePart != currentPart)
            {
                return candidatePart > currentPart;
            }
        }

        return false;
    }

    private static List<int> VersionParts(string value) =>
        value.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0)
            .ToList();

    // MARK: - JSON helpers

    private static JsonElement? Find(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        var value = Find(element, name);
        return value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
    {
        var value = ReadString(element, name);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>The shared HTTP client lives for the process; an injected client stays caller-owned.</summary>
    public void Dispose()
    {
    }
}
