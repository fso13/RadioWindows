using RadioWindows.Core.Data;
using RadioWindows.Core.Models;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Catalog_has_47_built_in_stations()
    {
        Assert.Equal(47, DefaultStations.Catalog.Count);
    }

    [Fact]
    public void Catalog_ids_are_unique_and_urls_are_valid()
    {
        Assert.Equal(
            DefaultStations.Catalog.Count,
            DefaultStations.Catalog.Select(station => station.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var station in DefaultStations.Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(station.Name));
            Assert.NotNull(station.StreamUri);
            Assert.Equal(Uri.UriSchemeHttps, station.StreamUri!.Scheme);
            Assert.True(station.BuiltIn);
        }
    }

    [Fact]
    public void Catalog_genres_are_known_or_normalised()
    {
        foreach (var station in DefaultStations.Catalog)
        {
            Assert.Contains(station.Genre, GenreCatalog.Presets);
        }
    }

    [Theory]
    [InlineData("jazz", "Jazz")]
    [InlineData("  Electronic ", "Electronic")]
    [InlineData("", "Other")]
    [InlineData(null, "Other")]
    [InlineData("Lo-fi", "Lo-fi")]
    public void Genre_normalisation_keeps_canonical_casing(string? raw, string expected)
    {
        Assert.Equal(expected, GenreCatalog.Normalize(raw));
    }
}
