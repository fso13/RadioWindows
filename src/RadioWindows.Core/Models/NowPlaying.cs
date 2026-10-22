using System.Text.RegularExpressions;

namespace RadioWindows.Core.Models;

/// <summary>Track metadata shown in the player bar, tray tooltip and mini player.</summary>
public sealed class NowPlaying
{
    public string Raw { get; init; } = string.Empty;

    public string? Artist { get; set; }

    public string? Title { get; set; }

    public string? Album { get; set; }

    public string? ArtworkUrl { get; set; }

    public bool HasArtwork => !string.IsNullOrWhiteSpace(ArtworkUrl);

    public string DisplayTitle
    {
        get
        {
            var song = Title?.Trim();
            return string.IsNullOrEmpty(song) ? Raw : song;
        }
    }

    public string? DisplayArtist
    {
        get
        {
            var value = Artist?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }

    /// <summary><c>Artist — Title</c> for the tray, title alone when there is no artist.</summary>
    public string TrayLine => DisplayArtist is { } who ? $"{who} — {DisplayTitle}" : DisplayTitle;
}

/// <summary>Parses ICY/Shoutcast metadata into artist/title pairs.</summary>
public static class NowPlayingParser
{
    private static readonly Regex StreamTitle = new(
        "StreamTitle='(.*?)';",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex DashSplit = new(@"\s+[-–—]\s+", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> Junk = new(StringComparer.OrdinalIgnoreCase)
    {
        "-", "—", "–", "n/a", "na", "null", "unknown", "undefined",
        "advert", "advertisement", "реклама",
    };

    public static string? UnwrapIcyBlock(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = StreamTitle.Match(raw);
        if (match.Success)
        {
            var value = match.Groups[1].Value.Trim();
            return value.Length == 0 ? null : value;
        }

        var trimmed = raw.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    public static NowPlaying? Parse(string? raw, string? stationName = null)
    {
        var unwrapped = UnwrapIcyBlock(raw);
        if (unwrapped is null)
        {
            return null;
        }

        var cleaned = Whitespace.Replace(unwrapped, " ").Trim();
        if (cleaned.Length == 0 || cleaned.Length > 240)
        {
            return null;
        }

        if (Junk.Contains(cleaned))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(stationName)
            && string.Equals(cleaned, stationName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = DashSplit.Split(cleaned);
        if (parts.Length >= 2)
        {
            var artist = parts[0].Trim();
            var title = string.Join(" — ", parts.Skip(1)).Trim();
            if (artist.Length == 0 || title.Length == 0)
            {
                return new NowPlaying { Raw = cleaned, Title = cleaned };
            }

            return new NowPlaying { Raw = cleaned, Artist = artist, Title = title };
        }

        return new NowPlaying { Raw = cleaned, Title = cleaned };
    }

    public static bool LooksLikeImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        var path = parsed.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".jpg", StringComparison.Ordinal)
               || path.EndsWith(".jpeg", StringComparison.Ordinal)
               || path.EndsWith(".png", StringComparison.Ordinal)
               || path.EndsWith(".webp", StringComparison.Ordinal)
               || path.EndsWith(".gif", StringComparison.Ordinal);
    }
}
