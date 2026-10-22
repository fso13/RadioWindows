using System.Text.Json.Serialization;

namespace RadioWindows.Core.Models;

public sealed class TrackHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string StationId { get; set; } = string.Empty;

    public string StationName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Artist { get; set; }

    public DateTimeOffset PlayedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string Line => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} — {Title}";

    [JsonIgnore]
    public string SearchText => $"{StationName} {Line}".ToLowerInvariant();

    public static TrackHistoryEntry Create(RadioStation station, NowPlaying track) => new()
    {
        StationId = station.Id,
        StationName = station.Name,
        Title = track.DisplayTitle,
        Artist = track.DisplayArtist,
        PlayedAt = DateTimeOffset.Now,
    };
}
