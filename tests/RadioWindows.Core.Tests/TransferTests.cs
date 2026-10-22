using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class TransferTests
{
    [Fact]
    public void File_filter_and_extensions_match_the_dialog_contract()
    {
        Assert.Equal("JSON|*.json|M3U|*.m3u|OPML|*.opml", StationTransfer.FileFilter);
        Assert.Equal("json", StationTransfer.ExtensionFor(StationExportFormat.Json));
        Assert.Equal("m3u", StationTransfer.ExtensionFor(StationExportFormat.M3u));
        Assert.Equal("opml", StationTransfer.ExtensionFor(StationExportFormat.Opml));
    }

    [Theory]
    [InlineData("stations.json", StationExportFormat.Json)]
    [InlineData("STATIONS.JSON", StationExportFormat.Json)]
    [InlineData("playlist.m3u", StationExportFormat.M3u)]
    [InlineData("playlist.m3u8", StationExportFormat.M3u)]
    [InlineData("streams.opml", StationExportFormat.Opml)]
    [InlineData("streams.xml", StationExportFormat.Opml)]
    [InlineData("backup.txt", StationExportFormat.Json)]
    public void Detect_uses_the_file_extension(string fileName, StationExportFormat expected)
    {
        Assert.Equal(expected, StationTransfer.Detect(fileName));
    }

    [Fact]
    public void Json_export_round_trips_favorites_groups_and_comments()
    {
        var original = new List<RadioStation>
        {
            new()
            {
                Id = "a",
                Name = "Jazz FM",
                Url = "https://example.com/jazz",
                Genre = "jazz",
                Comment = "night show",
                Group = "Jazz",
                LogoUrl = "https://example.com/logo.png",
                IsFavorite = true,
            },
            new()
            {
                Id = "b",
                Name = "Rock FM",
                Url = "https://example.com/rock",
                Genre = "Rock",
                Group = "Rock",
            },
        };

        var json = StationTransfer.Export(original, StationExportFormat.Json);
        Assert.StartsWith("[", json);
        Assert.Contains("\"isFavorite\":true", json);

        var parsed = StationTransfer.ParseJson(json);
        Assert.Equal(2, parsed.Count);
        Assert.Equal("Jazz FM", parsed[0].Name);
        Assert.True(parsed[0].IsFavorite);
        Assert.Equal("night show", parsed[0].Comment);
        Assert.Equal("Jazz", parsed[0].Group);
        Assert.Equal("Jazz", parsed[0].Genre);
        Assert.Equal("https://example.com/logo.png", parsed[0].LogoUrl);
        Assert.Equal("Rock", parsed[1].Group);
        Assert.False(parsed[1].IsFavorite);
    }

    [Fact]
    public void Json_import_tolerates_bom_wrapper_comments_and_trailing_commas()
    {
        const string payload = "\uFEFF{\n  // exported by another player\n  \"version\": 2,\n"
                               + "  \"stations\": [\n"
                               + "    {\"name\": \"Ambient One\", \"url\": \"https://a.example/stream\","
                               + " \"genre\": \"ambient\", \"unknownKey\": true,},\n"
                               + "  ],\n}";

        var parsed = StationTransfer.ParseJson(payload);

        var station = Assert.Single(parsed);
        Assert.Equal("Ambient One", station.Name);
        Assert.Equal("Ambient", station.Genre);
        Assert.Equal("https://a.example/stream", station.Url);
        Assert.False(string.IsNullOrWhiteSpace(station.Id));
    }

    [Fact]
    public void Json_import_skips_invalid_urls_and_duplicate_streams()
    {
        const string payload = """
        [
          {"name": "One", "url": "https://example.com/stream"},
          {"name": "Duplicate", "url": "https://EXAMPLE.com/stream/"},
          {"name": "Broken", "url": "not a url"},
          {"name": "No host", "url": "file:///tmp/a.mp3"},
          {"name": "", "url": "https://host.example/live"},
          "not a station"
        ]
        """;

        var parsed = StationTransfer.ParseJson(payload);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("One", parsed[0].Name);
        Assert.Equal("host.example", parsed[1].Name);
        Assert.Equal(GenreCatalog.Other, parsed[1].Genre);
    }

    [Fact]
    public void M3u_import_reads_extinf_attributes_and_skips_noise()
    {
        const string playlist = "\uFEFF#EXTM3U\r\n"
                                + "# just a comment\r\n"
                                + "\r\n"
                                + "#EXTINF:-1 tvg-name=\"Jazz FM\" group-title=\"Jazz\" "
                                + "tvg-logo=\"https://example.com/logo.png\",Jazz FM\r\n"
                                + "https://example.com/jazz\r\n"
                                + "#EXTVLCOPT:http-user-agent=test\r\n"
                                + "#EXTINF:-1,Bare Stream\r\n"
                                + "https://example.com/bare\r\n"
                                + "#EXTINF:-1,Broken\r\n"
                                + "not-a-url\r\n";

        var parsed = StationTransfer.ParseM3u(playlist);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Jazz FM", parsed[0].Name);
        Assert.Equal("Jazz", parsed[0].Group);
        Assert.Equal("Jazz", parsed[0].Genre);
        Assert.Equal("https://example.com/logo.png", parsed[0].LogoUrl);
        Assert.False(string.IsNullOrWhiteSpace(parsed[0].Comment));
        Assert.Equal("Bare Stream", parsed[1].Name);
        Assert.Null(parsed[1].Group);
        Assert.Equal(GenreCatalog.Other, parsed[1].Genre);
    }

    [Fact]
    public void M3u_import_accepts_bare_url_lists()
    {
        var parsed = StationTransfer.ParseM3u("https://one.example/live\nhttps://two.example/live\n");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("one.example", parsed[0].Name);
        Assert.Equal("two.example", parsed[1].Name);
    }

    [Fact]
    public void M3u_export_round_trips_name_group_and_logo()
    {
        var stations = new[]
        {
            new RadioStation
            {
                Name = "Deep House",
                Url = "https://example.com/deep",
                Genre = "Electronic",
                Group = "Electronic",
                LogoUrl = "https://example.com/l.png",
            },
        };

        var text = StationTransfer.Export(stations, StationExportFormat.M3u);

        Assert.StartsWith("#EXTM3U\n", text);
        Assert.Contains("group-title=\"Electronic\"", text);
        Assert.Contains("tvg-logo=\"https://example.com/l.png\"", text);
        Assert.Contains(",Deep House\n", text);
        Assert.Contains("https://example.com/deep\n", text);

        var parsed = StationTransfer.ParseM3u(text);
        var station = Assert.Single(parsed);
        Assert.Equal("Deep House", station.Name);
        Assert.Equal("Electronic", station.Group);
        Assert.Equal("Electronic", station.Genre);
        Assert.Equal("https://example.com/deep", station.Url);
        Assert.Equal("https://example.com/l.png", station.LogoUrl);
    }

    [Fact]
    public void Opml_import_reads_nested_groups_categories_and_descriptions()
    {
        const string payload = """
        <?xml version="1.0" encoding="UTF-8"?>
        <opml version="2.0">
          <head><title>Other player</title></head>
          <body>
            <outline text="Jazz">
              <outline type="link" text="Jazz FM" xmlUrl="https://example.com/jazz"
                       category="Jazz" description="Night show" tvg-logo="https://example.com/j.png"/>
              <outline type="link" title="No Category" url="https://example.com/plain"/>
              <outline type="link" text="Broken" xmlUrl="nonsense"/>
            </outline>
            <outline type="link" text="Top Level" xmlUrl="https://example.com/top" description="Ungrouped"/>
          </body>
        </opml>
        """;

        var parsed = StationTransfer.ParseOpml(payload);

        Assert.Equal(3, parsed.Count);
        Assert.Equal("Jazz FM", parsed[0].Name);
        Assert.Equal("Jazz", parsed[0].Group);
        Assert.Equal("Jazz", parsed[0].Genre);
        Assert.Equal("Night show", parsed[0].Comment);
        Assert.Equal("https://example.com/j.png", parsed[0].LogoUrl);

        Assert.Equal("No Category", parsed[1].Name);
        Assert.Equal("Jazz", parsed[1].Group);
        Assert.Equal("Jazz", parsed[1].Genre);

        Assert.Equal("Top Level", parsed[2].Name);
        Assert.Null(parsed[2].Group);
        Assert.Equal("Ungrouped", parsed[2].Comment);
        Assert.Equal(GenreCatalog.Other, parsed[2].Genre);
    }

    [Fact]
    public void Opml_export_round_trips_groups_and_comments()
    {
        var stations = new[]
        {
            new RadioStation
            {
                Name = "Jazz FM",
                Url = "https://example.com/jazz",
                Genre = "Jazz",
                Group = "Jazz",
                Comment = "Night show",
            },
            new RadioStation
            {
                Name = "Rock & Roll",
                Url = "https://example.com/rock",
                Genre = "Rock",
            },
        };

        var xml = StationTransfer.Export(stations, StationExportFormat.Opml);

        Assert.Contains("<opml version=\"2.0\">", xml);
        Assert.Contains("xmlUrl=\"https://example.com/jazz\"", xml);
        Assert.Contains("Rock &amp; Roll", xml);
        Assert.Contains("description=\"Night show\"", xml);

        var parsed = StationTransfer.ParseOpml(xml);
        Assert.Equal(2, parsed.Count);

        var jazz = Assert.Single(parsed, station => station.Name == "Jazz FM");
        Assert.Equal("Jazz", jazz.Group);
        Assert.Equal("Night show", jazz.Comment);

        var rock = Assert.Single(parsed, station => station.Name == "Rock & Roll");
        Assert.Null(rock.Group);
        Assert.Equal("Rock", rock.Genre);
    }

    [Fact]
    public void Import_dedupes_repeated_urls_within_one_file()
    {
        const string playlist = "#EXTM3U\n"
                                + "#EXTINF:-1,First\nhttps://example.com/live\n"
                                + "#EXTINF:-1,Second\nhttps://example.com/live\n";

        var parsed = StationTransfer.ParseM3u(playlist);

        var station = Assert.Single(parsed);
        Assert.Equal("First", station.Name);
    }

    [Fact]
    public void Parse_sniffs_content_when_the_extension_is_unknown()
    {
        Assert.Single(StationTransfer.Parse(
            "stations.dat",
            """[{"name": "A", "url": "https://a.example/live"}]"""));

        Assert.Single(StationTransfer.Parse(
            "playlist",
            "#EXTM3U\n#EXTINF:-1,B\nhttps://b.example/live\n"));

        Assert.Single(StationTransfer.Parse(
            "feed.txt",
            "<opml><body><outline text=\"C\" xmlUrl=\"https://c.example/live\"/></body></opml>"));
    }

    [Fact]
    public void Parse_falls_back_to_sniffing_when_the_extension_lies()
    {
        var parsed = StationTransfer.Parse(
            "playlist.json",
            "#EXTM3U\n#EXTINF:-1,B\nhttps://b.example/live\n");

        var station = Assert.Single(parsed);
        Assert.Equal("B", station.Name);
    }

    [Fact]
    public void Corrupt_input_returns_empty_instead_of_throwing()
    {
        Assert.Empty(StationTransfer.ParseJson("{not json"));
        Assert.Empty(StationTransfer.ParseJson(string.Empty));
        Assert.Empty(StationTransfer.ParseM3u(string.Empty));
        Assert.Empty(StationTransfer.ParseOpml("<opml><body>"));
        Assert.Empty(StationTransfer.ParseOpml(string.Empty));
        Assert.Empty(StationTransfer.Parse("x.json", "garbage"));
        Assert.Empty(StationTransfer.Parse("x.m3u", "#EXTM3U\n"));
        Assert.Empty(StationTransfer.Parse("x.opml", "<opml><body/></opml>"));
    }

    [Fact]
    public void Empty_exports_are_still_well_formed()
    {
        Assert.Equal("[]", StationTransfer.Export([], StationExportFormat.Json));
        Assert.Equal("#EXTM3U\n", StationTransfer.Export([], StationExportFormat.M3u));
        Assert.Contains("<body>", StationTransfer.Export([], StationExportFormat.Opml));
    }
}
