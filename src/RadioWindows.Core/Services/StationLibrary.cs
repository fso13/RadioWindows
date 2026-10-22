using System.Collections.ObjectModel;
using RadioWindows.Core.Data;
using RadioWindows.Core.Models;

namespace RadioWindows.Core.Services;

/// <summary>The user's station library: built-in catalog plus their own streams.</summary>
public sealed class StationLibrary : ObservableObject
{
    private readonly Preferences _preferences;
    private readonly ILogoResolver? _logoResolver;
    private readonly IUiDispatcher _dispatcher;
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);
    private bool _suppressChanged;
    private bool _loaded;

    public StationLibrary(
        Preferences preferences,
        ILogoResolver? logoResolver = null,
        IUiDispatcher? dispatcher = null)
    {
        _preferences = preferences;
        _logoResolver = logoResolver;
        _dispatcher = dispatcher ?? new ImmediateDispatcher();
    }

    public ObservableCollection<RadioStation> Stations { get; } = [];

    /// <summary>Raised whenever the persisted library changes.</summary>
    public event Action? Changed;

    public IReadOnlyList<RadioStation> Favorites => Stations.Where(station => station.IsFavorite).ToList();

    /// <summary>User-defined groups, alphabetically. Stations without a group are not listed.</summary>
    public IReadOnlyList<string> Groups =>
        Stations.Where(station => !string.IsNullOrWhiteSpace(station.Group))
            .Select(station => station.Group!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Genres in catalog order first, then custom genres alphabetically.</summary>
    public IReadOnlyList<string> Genres
    {
        get
        {
            var names = Stations.Select(station => GenreCatalog.Normalize(station.Genre))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ordered = GenreCatalog.Presets.Where(names.Contains).ToList();
            var custom = names
                .Where(name => !GenreCatalog.Presets.Contains(name, StringComparer.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return ordered.Concat(custom).ToList();
        }
    }

    public RadioStation? ById(string id) =>
        Stations.FirstOrDefault(station => string.Equals(station.Id, id, StringComparison.Ordinal));

    // MARK: - Loading

    public void Load()
    {
        var raw = _preferences.GetString("stations_v1");
        var loaded = RadioStationJson.Deserialize(raw)
            .Where(station => !string.IsNullOrWhiteSpace(station.Id) && !string.IsNullOrWhiteSpace(station.Url))
            .ToList();

        IReadOnlyList<RadioStation> next;
        if (loaded.Count == 0)
        {
            next = DefaultStations.Catalog.Select(station => station.Clone()).ToList();
        }
        else
        {
            next = RefreshBuiltInLogos(MergeMissingBuiltIns(loaded));
        }

        ReplaceAll(next);
        Persist();
        _loaded = true;
        _ = ResolveMissingLogosAsync();
    }

    public bool IsLoaded => _loaded;

    /// <summary>Adds newly shipped built-in stations without wiping user edits.</summary>
    private List<RadioStation> MergeMissingBuiltIns(List<RadioStation> existing)
    {
        var ids = existing.Select(station => station.Id).ToHashSet(StringComparer.Ordinal);
        var merged = new List<RadioStation>(existing);
        foreach (var station in DefaultStations.Catalog)
        {
            if (!ids.Contains(station.Id))
            {
                merged.Add(station.Clone());
            }
        }

        return merged;
    }

    private static List<RadioStation> RefreshBuiltInLogos(List<RadioStation> input)
    {
        var catalog = DefaultStations.Catalog.ToDictionary(station => station.Id, StringComparer.Ordinal);
        foreach (var station in input)
        {
            if (catalog.TryGetValue(station.Id, out var shipped)
                && shipped.HasLogo
                && !station.HasLogo)
            {
                station.LogoUrl = shipped.LogoUrl;
            }
        }

        return input;
    }

    // MARK: - Mutations

    public RadioStation Add(
        string name,
        string url,
        string genre,
        string? comment = null,
        string? group = null,
        string? logoUrl = null,
        string? externalId = null,
        double volumeOffsetDb = 0,
        string? eqPresetId = null,
        bool autoplay = false)
    {
        var trimmedUrl = url.Trim();
        var trimmedExternalId = RadioStationJson.EmptyToNil(externalId);
        var existing = Stations.FirstOrDefault(station => SameStream(station, trimmedUrl, trimmedExternalId));
        if (existing is not null)
        {
            return existing;
        }

        var station = new RadioStation
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim(),
            Url = trimmedUrl,
            Genre = GenreCatalog.Normalize(genre),
            Comment = RadioStationJson.EmptyToNil(comment),
            Group = NormalizeGroup(group),
            LogoUrl = RadioStationJson.EmptyToNil(logoUrl),
            ExternalId = trimmedExternalId,
            VolumeOffsetDb = Math.Clamp(volumeOffsetDb, -12, 12),
            EqPresetId = RadioStationJson.EmptyToNil(eqPresetId),
            Autoplay = autoplay,
        };

        Stations.Add(station);
        Persist();

        if (!station.HasLogo)
        {
            ResolveLogo(station);
        }

        return station;
    }

    public bool IsDuplicate(string url, string? externalId = null) =>
        Stations.Any(station => SameStream(station, url.Trim(), RadioStationJson.EmptyToNil(externalId)));

    private static bool SameStream(RadioStation station, string url, string? externalId)
    {
        if (!string.IsNullOrEmpty(externalId)
            && !string.IsNullOrEmpty(station.ExternalId)
            && string.Equals(station.ExternalId, externalId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(NormalizedUrl(station.Url), NormalizedUrl(url), StringComparison.Ordinal);
    }

    public static string NormalizedUrl(string raw)
    {
        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed.ToLowerInvariant();
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
        };

        if (builder.Path.Length > 1 && builder.Path.EndsWith('/'))
        {
            builder.Path = builder.Path[..^1];
        }

        return builder.Uri.ToString();
    }

    public StationImportResult Import(IEnumerable<RadioStation> imported, StationImportMode mode)
    {
        var result = new StationImportResult();
        var favoriteUrls = Stations.Where(station => station.IsFavorite)
            .Select(station => NormalizedUrl(station.Url))
            .ToHashSet(StringComparer.Ordinal);

        if (mode == StationImportMode.Replace)
        {
            foreach (var station in Stations.Where(station => !station.BuiltIn).ToList())
            {
                Stations.Remove(station);
            }
        }

        foreach (var source in imported)
        {
            var url = source.Url.Trim();
            if (RadioStation.TryParseStream(url) is null)
            {
                result.Skipped++;
                continue;
            }

            var index = IndexOfStream(url, source.ExternalId);
            if (index >= 0)
            {
                var shouldFavorite = source.IsFavorite || favoriteUrls.Contains(NormalizedUrl(url));
                if (shouldFavorite && !Stations[index].IsFavorite)
                {
                    Stations[index].IsFavorite = true;
                    result.FavoritesUpdated++;
                }

                result.Skipped++;
                continue;
            }

            var added = Add(
                source.Name,
                url,
                source.Genre,
                source.Comment,
                source.Group,
                source.LogoUrl,
                source.ExternalId,
                source.VolumeOffsetDb,
                source.EqPresetId,
                source.Autoplay);

            if (source.IsFavorite || favoriteUrls.Contains(NormalizedUrl(url)))
            {
                var addedIndex = IndexOfId(added.Id);
                if (addedIndex >= 0 && !Stations[addedIndex].IsFavorite)
                {
                    Stations[addedIndex].IsFavorite = true;
                    result.FavoritesUpdated++;
                }
            }

            result.Added++;
        }

        Persist();
        return result;
    }

    private int IndexOfId(string id)
    {
        for (var index = 0; index < Stations.Count; index++)
        {
            if (string.Equals(Stations[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private int IndexOfStream(string url, string? externalId)
    {
        for (var index = 0; index < Stations.Count; index++)
        {
            if (SameStream(Stations[index], url, RadioStationJson.EmptyToNil(externalId)))
            {
                return index;
            }
        }

        return -1;
    }

    public void Update(RadioStation updated)
    {
        var index = IndexOfId(updated.Id);
        if (index < 0)
        {
            return;
        }

        var next = updated.Clone();
        next.Genre = GenreCatalog.Normalize(updated.Genre);
        next.Comment = RadioStationJson.EmptyToNil(updated.Comment);
        next.Group = NormalizeGroup(updated.Group);
        next.LogoUrl = RadioStationJson.EmptyToNil(updated.LogoUrl);
        next.VolumeOffsetDb = Math.Clamp(updated.VolumeOffsetDb, -12, 12);
        next.EqPresetId = RadioStationJson.EmptyToNil(updated.EqPresetId);

        if (_suppressChanged)
        {
            Stations[index] = next;
            return;
        }

        Stations[index] = next;
        Persist();

        if (!next.HasLogo)
        {
            ResolveLogo(next);
        }
    }

    public void Remove(string id)
    {
        var index = IndexOfId(id);
        if (index < 0)
        {
            return;
        }

        Stations.RemoveAt(index);
        Persist();
    }

    /// <summary>Trims a group and reuses the existing spelling when only the case differs.</summary>
    private string? NormalizeGroup(string? value)
    {
        var trimmed = RadioStationJson.EmptyToNil(value);
        if (trimmed is null)
        {
            return null;
        }

        return Stations.Select(station => station.Group)
                   .FirstOrDefault(group => !string.IsNullOrEmpty(group)
                                            && string.Equals(group, trimmed, StringComparison.CurrentCultureIgnoreCase))
               ?? trimmed;
    }

    /// <summary>Moves a station before another one, preserving the library order.</summary>
    public void Move(string id, string targetId)
    {
        if (string.Equals(id, targetId, StringComparison.Ordinal))
        {
            return;
        }

        var from = IndexOfId(id);
        var target = IndexOfId(targetId);
        if (from < 0 || target < 0)
        {
            return;
        }

        var station = Stations[from];
        Stations.RemoveAt(from);
        var insertion = from < target ? target - 1 : target;
        Stations.Insert(Math.Clamp(insertion, 0, Stations.Count), station);
        Persist();
    }

    public void MoveToEnd(string id)
    {
        var from = IndexOfId(id);
        if (from < 0 || from == Stations.Count - 1)
        {
            return;
        }

        var station = Stations[from];
        Stations.RemoveAt(from);
        Stations.Add(station);
        Persist();
    }

    /// <summary>Keeps autoplay on a single station.</summary>
    public void SetAutoplayStation(string id)
    {
        var changed = false;
        foreach (var station in Stations)
        {
            var shouldAutoplay = string.Equals(station.Id, id, StringComparison.Ordinal);
            if (station.Autoplay != shouldAutoplay)
            {
                station.Autoplay = shouldAutoplay;
                changed = true;
            }
        }

        if (changed)
        {
            Persist();
        }
    }

    public void ToggleFavorite(string id)
    {
        var index = IndexOfId(id);
        if (index < 0)
        {
            return;
        }

        Stations[index].IsFavorite = !Stations[index].IsFavorite;
        Persist();
        Raise(nameof(Favorites));
    }

    /// <summary>Re-seeds built-in stations; custom stations and favourites are preserved.</summary>
    public void RestoreDefaults()
    {
        var favoriteIds = Stations.Where(station => station.IsFavorite)
            .Select(station => station.Id)
            .ToHashSet(StringComparer.Ordinal);
        var custom = Stations.Where(station => !station.BuiltIn)
            .Select(station => station.Clone())
            .ToList();

        var restored = DefaultStations.Catalog.Select(station =>
        {
            var copy = station.Clone();
            copy.IsFavorite = favoriteIds.Contains(station.Id);
            return copy;
        }).ToList();

        ReplaceAll(restored.Concat(custom).ToList());
        Persist();
    }

    // MARK: - Logo resolution

    public async Task ResolveMissingLogosAsync()
    {
        if (_logoResolver is null)
        {
            return;
        }

        foreach (var station in Stations.Where(station => !station.HasLogo).ToList())
        {
            var logo = await SafeResolve(station, null);
            if (logo is not null)
            {
                _dispatcher.Post(() => ApplyResolvedLogo(station.Id, logo));
            }
        }
    }

    public async Task ResolveFromHomepageAsync(string id, string homepage)
    {
        if (_logoResolver is null)
        {
            return;
        }

        var station = ById(id);
        if (station is null || station.HasLogo)
        {
            return;
        }

        var logo = await SafeResolve(station, homepage);
        if (logo is not null)
        {
            _dispatcher.Post(() => ApplyResolvedLogo(id, logo));
        }
    }

    public void ApplyResolvedLogo(string id, string logoUrl)
    {
        var index = IndexOfId(id);
        if (index < 0 || Stations[index].HasLogo)
        {
            return;
        }

        Stations[index].LogoUrl = logoUrl;
        Persist();
    }

    private void ResolveLogo(RadioStation station)
    {
        if (_logoResolver is null || station.HasLogo || !_resolving.Add(station.Id))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var logo = await SafeResolve(station, null);
                if (logo is not null)
                {
                    _dispatcher.Post(() => ApplyResolvedLogo(station.Id, logo));
                }
            }
            finally
            {
                _resolving.Remove(station.Id);
            }
        });
    }

    private async Task<string?> SafeResolve(RadioStation station, string? homepage)
    {
        try
        {
            return await _logoResolver!.ResolveAsync(station.Url, homepage, station.Name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // MARK: - Persistence

    private void ReplaceAll(IReadOnlyList<RadioStation> items)
    {
        _suppressChanged = true;
        try
        {
            Stations.Clear();
            foreach (var station in items)
            {
                Stations.Add(station);
            }
        }
        finally
        {
            _suppressChanged = false;
        }

        Raise(nameof(Stations));
        Raise(nameof(Genres));
        Raise(nameof(Groups));
        Raise(nameof(Favorites));
        Changed?.Invoke();
    }

    private void Persist()
    {
        _preferences.Set("stations_v1", RadioStationJson.Serialize(Stations));
        Raise(nameof(Genres));
        Raise(nameof(Groups));
        Raise(nameof(Favorites));
        if (!_suppressChanged)
        {
            Changed?.Invoke();
        }
    }
}
