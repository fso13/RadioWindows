using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class StationLibraryTests
{
    private static StationLibrary CreateLibrary(TestPreferences fixture)
    {
        var library = new StationLibrary(fixture.Preferences);
        library.Load();
        return library;
    }

    [Fact]
    public void Seeds_the_built_in_catalog_when_empty()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        Assert.Equal(47, library.Stations.Count);
        Assert.NotEmpty(library.Genres);
    }

    [Fact]
    public void Adds_toggles_and_removes_custom_stations()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        var added = library.Add("Test FM", "https://example.com/stream.mp3", "jazz", group: "  ", volumeOffsetDb: 30);

        Assert.Equal("Jazz", added.Genre);
        Assert.Null(added.Group);
        Assert.Equal(12, added.VolumeOffsetDb);
        Assert.Equal(48, library.Stations.Count);

        library.ToggleFavorite(added.Id);
        Assert.True(library.ById(added.Id)!.IsFavorite);

        library.Remove(added.Id);
        Assert.Null(library.ById(added.Id));
        Assert.Equal(47, library.Stations.Count);
    }

    [Fact]
    public void Duplicate_urls_are_not_added_twice()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        var first = library.Add("One", "https://Example.com/stream/", "Pop");
        var second = library.Add("Two", "https://example.com/stream", "Rock");
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(48, library.Stations.Count);
    }

    [Fact]
    public void Import_skips_duplicates_and_keeps_favorites()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        var imported = new List<RadioStation>
        {
            new() { Id = "a", Name = "Imported", Url = "https://import.example/one", Genre = "Rock", IsFavorite = true },
            new() { Id = "b", Name = "Duplicate", Url = "https://import.example/one", Genre = "Rock" },
            new() { Id = "c", Name = "Broken", Url = "not a url", Genre = "Rock" },
        };

        var result = library.Import(imported, StationImportMode.Merge);
        Assert.Equal(1, result.Added);
        Assert.Equal(2, result.Skipped);
        Assert.Single(library.Stations, station => station.IsFavorite);
    }

    [Fact]
    public void Reordering_moves_station_before_target_and_to_end()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        var first = library.Stations[0];
        var second = library.Stations[1];
        library.Move(second.Id, first.Id);
        Assert.Equal(second.Id, library.Stations[0].Id);

        library.MoveToEnd(second.Id);
        Assert.Equal(second.Id, library.Stations[^1].Id);
    }

    [Fact]
    public void Restore_defaults_keeps_custom_stations_and_favorites()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        library.ToggleFavorite("rp-marusya");
        library.Add("Custom", "https://custom.example/stream", "Other");
        library.Remove("rp-vesti");

        library.RestoreDefaults();
        Assert.Equal(48, library.Stations.Count);
        Assert.True(library.ById("rp-marusya")!.IsFavorite);
        Assert.NotNull(library.ById("rp-vesti"));
        Assert.Contains(library.Stations, station => station.Name == "Custom");
    }

    [Fact]
    public void Library_persists_between_instances()
    {
        using var fixture = new TestPreferences();
        var library = CreateLibrary(fixture);
        library.Add("Persisted", "https://persist.example/stream", "Rock");
        library.ToggleFavorite("rp-marusya");
        fixture.Preferences.Flush();

        var reloaded = CreateLibrary(fixture);
        Assert.Contains(reloaded.Stations, station => station.Name == "Persisted");
        Assert.True(reloaded.ById("rp-marusya")!.IsFavorite);
    }
}
