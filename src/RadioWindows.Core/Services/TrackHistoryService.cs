using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>
/// Local-only history of the last hundred tracks. Entries are persisted under
/// <see cref="HistoryKey"/> as a JSON array (keys match the macOS app), newest
/// first, and can be searched and copied as text.
/// </summary>
public sealed class TrackHistoryService : ObservableObject, IDisposable
{
    /// <summary>Preferences key holding the serialised history.</summary>
    public const string HistoryKey = "track_history";

    public const int MaxEntries = 100;

    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(20);

    private readonly Preferences _preferences;
    private readonly RadioPlayerService _player;
    private readonly Func<DateTimeOffset> _clock;
    private string _searchText = string.Empty;
    private bool _disposed;

    public TrackHistoryService(
        Preferences preferences,
        RadioPlayerService player,
        Func<DateTimeOffset>? clock = null)
    {
        _preferences = preferences;
        _player = player;
        _clock = clock ?? (() => DateTimeOffset.Now);

        Entries = new ObservableCollection<TrackHistoryEntry>(Load(preferences));
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    /// <summary>Newest first, at most <see cref="MaxEntries"/> items.</summary>
    public ObservableCollection<TrackHistoryEntry> Entries { get; }

    /// <summary>Case-insensitive substring matched against station and track.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_searchText, next, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = next;
            Raise(nameof(SearchText));
            Raise(nameof(Filtered));
        }
    }

    public IReadOnlyList<TrackHistoryEntry> Filtered
    {
        get
        {
            var query = SearchText.Trim();
            if (query.Length == 0)
            {
                return Entries.ToList();
            }

            var needle = query.ToLowerInvariant();
            return Entries.Where(entry => entry.SearchText.Contains(needle, StringComparison.Ordinal)).ToList();
        }
    }

    public bool IsEmpty => Entries.Count == 0;

    // MARK: - Recording

    public void Add(RadioStation station, NowPlaying track)
    {
        var title = track.DisplayTitle.Trim();
        if (title.Length == 0 || string.IsNullOrWhiteSpace(station.Id))
        {
            return;
        }

        var now = _clock();
        var duplicate = Entries.FirstOrDefault(entry =>
            string.Equals(entry.StationId, station.Id, StringComparison.Ordinal)
            && string.Equals(entry.Title, title, StringComparison.Ordinal)
            && now - entry.PlayedAt < DuplicateWindow);
        if (duplicate is not null)
        {
            return;
        }

        Entries.Insert(0, new TrackHistoryEntry
        {
            Id = Guid.NewGuid().ToString(),
            StationId = station.Id,
            StationName = station.Name,
            Title = title,
            Artist = track.DisplayArtist,
            PlayedAt = now,
        });

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        Persist();
        Raise(nameof(Filtered));
        Raise(nameof(IsEmpty));
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(RadioPlayerService.NowPlaying)))
        {
            return;
        }

        if (_player.Status != PlaybackState.Playing || _player.NowPlaying is not { } track)
        {
            return;
        }

        if (_player.Station is { } station)
        {
            Add(station, track);
        }
    }

    // MARK: - Commands

    public void Clear()
    {
        Entries.Clear();
        _preferences.Remove(HistoryKey);
        Raise(nameof(Filtered));
        Raise(nameof(IsEmpty));
    }

    /// <summary>Plain text for the clipboard: one line per visible entry.</summary>
    public string CopyText() => string.Join(
        Environment.NewLine,
        Filtered.Select(entry =>
            $"{entry.PlayedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)} · {entry.StationName} · {entry.Line}"));

    // MARK: - Persistence

    private void Persist()
    {
        try
        {
            _preferences.Set(HistoryKey, Serialize(Entries));
        }
        catch (Exception)
        {
            // History is a convenience; never block playback on storage errors.
        }
    }

    private static List<TrackHistoryEntry> Load(Preferences preferences)
    {
        var result = new List<TrackHistoryEntry>();
        try
        {
            var raw = preferences.GetString(HistoryKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var title = ReadString(element, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                result.Add(new TrackHistoryEntry
                {
                    Id = ReadString(element, "id") ?? Guid.NewGuid().ToString(),
                    StationId = ReadString(element, "stationID") ?? string.Empty,
                    StationName = ReadString(element, "stationName") ?? string.Empty,
                    Title = title.Trim(),
                    Artist = ReadString(element, "artist"),
                    PlayedAt = ReadDate(element, "playedAt") ?? DateTimeOffset.Now,
                });

                if (result.Count >= MaxEntries)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore a corrupted history file.
        }

        return result;
    }

    private static string Serialize(IEnumerable<TrackHistoryEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", entry.Id);
                writer.WriteString("stationID", entry.StationId);
                writer.WriteString("stationName", entry.StationName);
                writer.WriteString("title", entry.Title);
                if (entry.Artist is null)
                {
                    writer.WriteNull("artist");
                }
                else
                {
                    writer.WriteString("artist", entry.Artist);
                }

                writer.WriteString("playedAt", entry.PlayedAt.ToString("O", CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

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
            JsonValueKind.String => string.IsNullOrWhiteSpace(value.Value.GetString())
                ? null
                : value.Value.GetString(),
            JsonValueKind.Number => value.Value.GetRawText(),
            _ => null,
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
    {
        var value = Find(element, name);
        if (value is not { ValueKind: JsonValueKind.String })
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value.Value.GetString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.PropertyChanged -= OnPlayerPropertyChanged;
    }
}
