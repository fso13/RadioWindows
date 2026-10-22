using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>File shapes understood by <see cref="StationTransfer"/>.</summary>
public enum StationExportFormat
{
    Json,
    M3u,
    Opml,
}

/// <summary>
/// Imports and exports the station library as JSON, M3U or OPML — the port of the
/// macOS <c>StationTransfer</c> helper. Parsing is deliberately forgiving: a broken
/// file yields an empty list instead of an exception, invalid stream URLs are
/// dropped and repeated URLs inside one file are collapsed to the first entry.
/// </summary>
public static class StationTransfer
{
    private const string M3uImportComment = "Импорт M3U";

    private static readonly JsonDocumentOptions TolerantJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly char[] TrimChars = [' ', '\t', '\r', '\n', '\uFEFF'];

    /// <summary>Same shape as the shared options, but tolerant while reading imports.</summary>
    private static readonly JsonSerializerOptions ImportOptions = new(RadioStationJson.Options)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Filter string for a Windows open/save dialog.</summary>
    public static string FileFilter => "JSON|*.json|M3U|*.m3u|OPML|*.opml";

    /// <summary>Extension without the leading dot, as used by the macOS app.</summary>
    public static string ExtensionFor(StationExportFormat format) => format switch
    {
        StationExportFormat.M3u => "m3u",
        StationExportFormat.Opml => "opml",
        _ => "json",
    };

    /// <summary>
    /// Guesses the export format from a file name. Unknown extensions resolve to
    /// JSON, which is the least destructive choice for a save dialog.
    /// </summary>
    public static StationExportFormat Detect(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty)
            .TrimStart('.')
            .ToLowerInvariant();

        return extension switch
        {
            "m3u" or "m3u8" => StationExportFormat.M3u,
            "opml" or "xml" => StationExportFormat.Opml,
            _ => StationExportFormat.Json,
        };
    }

    /// <summary>
    /// Parses an imported file. The extension wins; when it is unknown (or the
    /// content does not match it) the payload itself is sniffed.
    /// </summary>
    public static IReadOnlyList<RadioStation> Parse(string fileName, string content)
    {
        var text = Clean(content);
        if (text.Length == 0)
        {
            return [];
        }

        var declared = FormatFromExtension(fileName);
        if (declared is { } known)
        {
            var parsed = ParseCore(text, known);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        var sniffed = Sniff(text);
        if (sniffed is { } sniffedFormat && sniffedFormat != declared)
        {
            var parsed = ParseCore(text, sniffedFormat);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        // Last resort: try every reader so a file with a misleading name still works.
        foreach (var candidate in new[]
                 {
                     StationExportFormat.Json, StationExportFormat.M3u, StationExportFormat.Opml,
                 })
        {
            if (candidate == declared || candidate == sniffed)
            {
                continue;
            }

            var parsed = ParseCore(text, candidate);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        return [];
    }

    /// <summary>Parses a JSON array of station objects; also accepts a wrapper object.</summary>
    public static IReadOnlyList<RadioStation> ParseJson(string content)
    {
        var text = Clean(content);
        if (text.Length == 0)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(text, TolerantJson);
            var root = document.RootElement;
            var wrapper = FindProperty(root, "stations");
            if (wrapper is { } stationsArray && stationsArray.ValueKind == JsonValueKind.Array)
            {
                root = stationsArray;
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var stations = new List<RadioStation>();
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                try
                {
                    var station = element.Deserialize<RadioStation>(ImportOptions);
                    if (station is not null)
                    {
                        stations.Add(station);
                    }
                }
                catch (Exception exception) when (exception is JsonException or NotSupportedException)
                {
                    // Skip a malformed row, keep the rest of the file.
                }
            }

            return Normalize(stations);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Parses an #EXTM3U playlist, including bare URL lists.</summary>
    public static IReadOnlyList<RadioStation> ParseM3u(string content)
    {
        var text = Clean(content);
        if (text.Length == 0)
        {
            return [];
        }

        var stations = new List<RadioStation>();
        string? pendingName = null;
        string? pendingGroup = null;
        string? pendingLogo = null;

        foreach (var rawLine in SplitLines(text))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                if (!line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var comma = line.IndexOf(',');
                var attributes = comma >= 0 ? line[..comma] : line;
                var title = comma >= 0 ? line[(comma + 1)..].Trim() : string.Empty;

                pendingName = NullIfEmpty(title) ?? NullIfEmpty(Attribute(attributes, "tvg-name"));
                pendingGroup = NullIfEmpty(Attribute(attributes, "group-title"))
                               ?? NullIfEmpty(Attribute(attributes, "group"));
                pendingLogo = NullIfEmpty(Attribute(attributes, "tvg-logo"))
                              ?? NullIfEmpty(Attribute(attributes, "logo"));
                continue;
            }

            if (RadioStation.TryParseStream(line) is not { } uri)
            {
                continue;
            }

            stations.Add(new RadioStation
            {
                Id = Guid.NewGuid().ToString(),
                Name = pendingName ?? uri.Host,
                Url = line,
                Genre = pendingGroup ?? GenreCatalog.Other,
                Comment = LocOr("Transfer.ImportM3u", M3uImportComment),
                Group = pendingGroup,
                LogoUrl = pendingLogo,
            });

            pendingName = null;
            pendingGroup = null;
            pendingLogo = null;
        }

        return Normalize(stations);
    }

    /// <summary>Parses an OPML subscription list; nested outlines become groups.</summary>
    public static IReadOnlyList<RadioStation> ParseOpml(string content)
    {
        var text = Clean(content);
        if (text.Length == 0)
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (XmlException)
        {
            return [];
        }

        var stations = new List<RadioStation>();
        var outlines = document.Root is null
            ? Enumerable.Empty<XElement>()
            : document.Root.DescendantsAndSelf();
        foreach (var outline in outlines)
        {
            if (!IsOutline(outline))
            {
                continue;
            }

            var stream = FirstAttribute(outline, "xmlUrl", "url", "href");
            if (string.IsNullOrWhiteSpace(stream))
            {
                continue;
            }

            var url = stream.Trim();
            if (RadioStation.TryParseStream(url) is not { } uri)
            {
                continue;
            }

            var name = FirstAttribute(outline, "text", "title");
            var group = EnclosingGroup(outline);

            stations.Add(new RadioStation
            {
                Id = Guid.NewGuid().ToString(),
                Name = string.IsNullOrWhiteSpace(name) ? uri.Host : name.Trim(),
                Url = url,
                Genre = NullIfEmpty(FirstAttribute(outline, "category")) ?? group ?? GenreCatalog.Other,
                Comment = NullIfEmpty(FirstAttribute(outline, "description")),
                Group = group,
                LogoUrl = NullIfEmpty(FirstAttribute(outline, "tvg-logo"))
                          ?? NullIfEmpty(FirstAttribute(outline, "logo"))
                          ?? NullIfEmpty(FirstAttribute(outline, "image")),
            });
        }

        return Normalize(stations);
    }

    /// <summary>Serialises the stations in the requested format.</summary>
    public static string Export(IEnumerable<RadioStation> stations, StationExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(stations);
        var list = stations as IReadOnlyList<RadioStation> ?? stations.ToList();

        return format switch
        {
            StationExportFormat.M3u => ExportM3u(list),
            StationExportFormat.Opml => ExportOpml(list),
            _ => ExportJson(list),
        };
    }

    // MARK: - JSON

    private static string ExportJson(IReadOnlyList<RadioStation> stations) =>
        JsonSerializer.Serialize(stations, RadioStationJson.Options);

    // MARK: - M3U

    private static string ExportM3u(IReadOnlyList<RadioStation> stations)
    {
        var builder = new StringBuilder("#EXTM3U\n");
        foreach (var station in stations)
        {
            var attributes = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(station.Group))
            {
                attributes.Add($"group-title=\"{EscapeAttribute(station.Group)}\"");
            }

            if (!string.IsNullOrWhiteSpace(station.LogoUrl))
            {
                attributes.Add($"tvg-logo=\"{EscapeAttribute(station.LogoUrl)}\"");
            }

            attributes.Add($"tvg-name=\"{EscapeAttribute(station.Name)}\"");

            builder.Append("#EXTINF:-1 ")
                .Append(string.Join(' ', attributes))
                .Append(',')
                .Append(SingleLine(station.Name))
                .Append('\n');
            builder.Append(station.Url.Trim()).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>Reads a <c>name="value"</c> or <c>name=value</c> EXTINF attribute.</summary>
    private static string? Attribute(string line, string name)
    {
        var search = 0;
        while (true)
        {
            var index = line.IndexOf(name, search, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            search = index + name.Length;
            if (index > 0 && !char.IsWhiteSpace(line[index - 1]))
            {
                continue;
            }

            var after = index + name.Length;
            if (after >= line.Length || line[after] != '=')
            {
                continue;
            }

            var start = after + 1;
            if (start >= line.Length)
            {
                return null;
            }

            if (line[start] is '"' or '\'')
            {
                var quote = line[start];
                var end = line.IndexOf(quote, start + 1);
                return end > start ? line[(start + 1)..end] : null;
            }

            var stop = line.IndexOfAny([' ', ','], start);
            return (stop < 0 ? line[start..] : line[start..stop]).Trim();
        }
    }

    private static string EscapeAttribute(string value) =>
        SingleLine(value).Replace('"', '\'');

    // MARK: - OPML

    private static string ExportOpml(IReadOnlyList<RadioStation> stations)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<opml version=\"2.0\">\n");
        builder.Append("  <head><title>RadioWindows</title></head>\n");
        builder.Append("  <body>\n");

        var groups = stations
            .Where(station => !string.IsNullOrWhiteSpace(station.Group))
            .GroupBy(station => station.Group!.Trim())
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            builder.Append("    <outline text=\"")
                .Append(EscapeXml(group.Key))
                .Append("\" title=\"")
                .Append(EscapeXml(group.Key))
                .Append("\">\n");
            foreach (var station in group)
            {
                AppendStationOutline(builder, station, "      ");
            }

            builder.Append("    </outline>\n");
        }

        // Ungrouped stations sit directly under <body>, so a round trip keeps Group = null.
        foreach (var station in stations.Where(station => string.IsNullOrWhiteSpace(station.Group)))
        {
            AppendStationOutline(builder, station, "    ");
        }

        builder.Append("  </body>\n");
        builder.Append("</opml>\n");
        return builder.ToString();
    }

    private static void AppendStationOutline(StringBuilder builder, RadioStation station, string indent)
    {
        var name = EscapeXml(SingleLine(station.Name));
        builder.Append(indent)
            .Append("<outline type=\"link\" text=\"")
            .Append(name)
            .Append("\" title=\"")
            .Append(name)
            .Append("\" xmlUrl=\"")
            .Append(EscapeXml(station.Url.Trim()))
            .Append("\" category=\"")
            .Append(EscapeXml(station.Genre))
            .Append('"');

        if (!string.IsNullOrWhiteSpace(station.Comment))
        {
            builder.Append(" description=\"")
                .Append(EscapeXml(SingleLine(station.Comment)))
                .Append('"');
        }

        if (!string.IsNullOrWhiteSpace(station.LogoUrl))
        {
            builder.Append(" tvg-logo=\"")
                .Append(EscapeXml(station.LogoUrl.Trim()))
                .Append('"');
        }

        builder.Append("/>\n");
    }

    private static bool IsOutline(XElement element) =>
        string.Equals(element.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase);

    /// <summary>Innermost enclosing outline without a stream URL, read as a group name.</summary>
    private static string? EnclosingGroup(XElement outline)
    {
        for (var parent = outline.Parent; parent is not null; parent = parent.Parent)
        {
            if (!IsOutline(parent))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(FirstAttribute(parent, "xmlUrl", "url", "href")))
            {
                continue;
            }

            var label = FirstAttribute(parent, "text", "title");
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label.Trim();
            }
        }

        return null;
    }

    private static string? FirstAttribute(XElement element, params string[] names)
    {
        foreach (var attribute in element.Attributes())
        {
            foreach (var name in names)
            {
                if (string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value;
                }
            }
        }

        return null;
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    // MARK: - Shared helpers

    private static IReadOnlyList<RadioStation> ParseCore(string text, StationExportFormat format) => format switch
    {
        StationExportFormat.Json => ParseJson(text),
        StationExportFormat.M3u => ParseM3u(text),
        StationExportFormat.Opml => ParseOpml(text),
        _ => [],
    };

    private static StationExportFormat? FormatFromExtension(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty)
            .TrimStart('.')
            .ToLowerInvariant();

        return extension switch
        {
            "json" => StationExportFormat.Json,
            "m3u" or "m3u8" => StationExportFormat.M3u,
            "opml" or "xml" => StationExportFormat.Opml,
            _ => null,
        };
    }

    private static StationExportFormat? Sniff(string text)
    {
        var head = text.TrimStart();
        if (head.StartsWith('{') || head.StartsWith('['))
        {
            return StationExportFormat.Json;
        }

        if (head.StartsWith('<'))
        {
            return StationExportFormat.Opml;
        }

        if (head.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase)
            || head.Contains("#EXTINF", StringComparison.OrdinalIgnoreCase)
            || head.Contains("://", StringComparison.Ordinal))
        {
            return StationExportFormat.M3u;
        }

        return null;
    }

    /// <summary>Validates URLs, fills defaults and collapses duplicate streams.</summary>
    private static IReadOnlyList<RadioStation> Normalize(IEnumerable<RadioStation> input)
    {
        var stations = new List<RadioStation>();
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in input)
        {
            var url = source.Url?.Trim() ?? string.Empty;
            if (RadioStation.TryParseStream(url) is not { } uri)
            {
                continue;
            }

            var station = source.Clone();
            station.Url = url;
            if (string.IsNullOrWhiteSpace(station.Id))
            {
                station.Id = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrWhiteSpace(station.Name))
            {
                station.Name = uri.Host;
            }
            else
            {
                station.Name = station.Name.Trim();
            }

            station.Genre = GenreCatalog.Normalize(station.Genre);
            station.Comment = NullIfEmpty(station.Comment);
            station.Group = NullIfEmpty(station.Group);
            station.LogoUrl = NullIfEmpty(station.LogoUrl);

            if (!seenUrls.Add(UrlKey(station.Url)))
            {
                continue;
            }

            stations.Add(station);
        }

        return stations;
    }

    /// <summary>Comparison key: scheme and host are case-insensitive, a trailing slash is ignored.</summary>
    private static string UrlKey(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.ToLowerInvariant();
        }

        return string.Concat(
            uri.Scheme.ToLowerInvariant(),
            "://",
            uri.Authority.ToLowerInvariant(),
            uri.AbsolutePath.TrimEnd('/'),
            uri.Query);
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.None);

    private static string Clean(string? content) =>
        string.IsNullOrEmpty(content) ? string.Empty : content.Trim(TrimChars);

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>Localised text with a literal fallback while the key is absent.</summary>
    private static string LocOr(string key, string fallback) => Loc.Has(key) ? Loc.T(key) : fallback;
}
