using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadioWindows.Core.Models;

/// <summary>A single radio stream in the library.</summary>
public sealed class RadioStation : IEquatable<RadioStation>
{
    private string _genre = GenreCatalog.Other;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Normalised genre; unknown values pass through unchanged.</summary>
    public string Genre
    {
        get => _genre;
        set => _genre = GenreCatalog.Normalize(value);
    }

    public string? Comment { get; set; }

    public string? Group { get; set; }

    public string? LogoUrl { get; set; }

    public bool IsFavorite { get; set; }

    public bool BuiltIn { get; set; }

    /// <summary>Stable identifier from an external catalog such as Radio Browser.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Per-station loudness correction in dB (-12…+12).</summary>
    public double VolumeOffsetDb { get; set; }

    /// <summary>Optional equalizer preset applied when this station starts.</summary>
    public string? EqPresetId { get; set; }

    /// <summary>Start this station automatically when the app launches.</summary>
    public bool Autoplay { get; set; }

    [JsonIgnore]
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    [JsonIgnore]
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);

    [JsonIgnore]
    public Uri? StreamUri => TryParseStream(Url);

    public static Uri? TryParseStream(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)
               && !string.IsNullOrEmpty(uri.Host)
            ? uri
            : null;
    }

    public bool Equals(RadioStation? other) =>
        other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RadioStation);

    public override int GetHashCode() => Id.GetHashCode(StringComparison.Ordinal);

    public RadioStation Clone() => (RadioStation)MemberwiseClone();
}

/// <summary>
/// Tolerant reader/writer for the station shape shared with the macOS app
/// (keys: id, name, url, genre, comment, group, logoUrl, isFavorite, builtIn,
/// externalId, volumeOffset, eqPreset, autoplay).
/// </summary>
public static class RadioStationJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new RadioStationJsonConverter() },
    };

    public static RadioStation Read(JsonElement element)
    {
        var station = new RadioStation
        {
            Id = String(element, "id") ?? string.Empty,
            Name = String(element, "name") ?? string.Empty,
            Url = String(element, "url") ?? string.Empty,
            Genre = GenreCatalog.Normalize(String(element, "genre")),
            Comment = EmptyToNil(String(element, "comment")),
            Group = EmptyToNil(String(element, "group")),
            LogoUrl = EmptyToNil(String(element, "logoUrl") ?? String(element, "logoURL")),
            IsFavorite = Bool(element, "isFavorite") ?? false,
            BuiltIn = Bool(element, "builtIn") ?? false,
            ExternalId = EmptyToNil(String(element, "externalId") ?? String(element, "externalID")),
            VolumeOffsetDb = Math.Clamp(Double(element, "volumeOffset") ?? 0, -12, 12),
            EqPresetId = EmptyToNil(String(element, "eqPreset") ?? String(element, "eqPresetId")),
            Autoplay = Bool(element, "autoplay") ?? false,
        };
        return station;
    }

    public static void Write(Utf8JsonWriter writer, RadioStation station)
    {
        writer.WriteStartObject();
        writer.WriteString("id", station.Id);
        writer.WriteString("name", station.Name);
        writer.WriteString("url", station.Url);
        writer.WriteString("genre", station.Genre);
        WriteOptional(writer, "comment", station.Comment);
        WriteOptional(writer, "group", station.Group);
        WriteOptional(writer, "logoUrl", station.LogoUrl);
        writer.WriteBoolean("isFavorite", station.IsFavorite);
        writer.WriteBoolean("builtIn", station.BuiltIn);
        WriteOptional(writer, "externalId", station.ExternalId);
        writer.WriteNumber("volumeOffset", Math.Round(station.VolumeOffsetDb, 2));
        WriteOptional(writer, "eqPreset", station.EqPresetId);
        writer.WriteBoolean("autoplay", station.Autoplay);
        writer.WriteEndObject();
    }

    public static string Serialize(IEnumerable<RadioStation> stations) =>
        JsonSerializer.Serialize(stations.ToList(), Options);

    public static List<RadioStation> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<RadioStation>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal static string? EmptyToNil(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static void WriteOptional(Utf8JsonWriter writer, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteString(key, value);
        }
    }

    private static JsonElement? Find(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        return null;
    }

    private static string? String(JsonElement element, string key)
    {
        var value = Find(element, key);
        return value?.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static double? Double(JsonElement element, string key)
    {
        var value = Find(element, key);
        return value?.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.Value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool? Bool(JsonElement element, string key)
    {
        var value = Find(element, key);
        return value?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.Value.GetDouble() != 0,
            JsonValueKind.String => value.Value.GetString()?.ToLowerInvariant() switch
            {
                "1" or "true" or "yes" => true,
                "0" or "false" or "no" => false,
                _ => null,
            },
            _ => null,
        };
    }
}

public sealed class RadioStationJsonConverter : JsonConverter<RadioStation>
{
    public override RadioStation? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return RadioStationJson.Read(document.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, RadioStation value, JsonSerializerOptions options) =>
        RadioStationJson.Write(writer, value);
}
