using RadioWindows.Core.Models;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class NowPlayingParserTests
{
    [Fact]
    public void Parses_artist_and_title()
    {
        var track = NowPlayingParser.Parse("StreamTitle='Portishead - Roads';");
        Assert.NotNull(track);
        Assert.Equal("Portishead", track!.Artist);
        Assert.Equal("Roads", track.Title);
        Assert.Equal("Portishead — Roads", track.TrayLine);
    }

    [Fact]
    public void Splits_on_en_dash_and_em_dash()
    {
        var track = NowPlayingParser.Parse("StreamTitle='Daft Punk – Around the World';");
        Assert.Equal("Daft Punk", track!.Artist);
        Assert.Equal("Around the World", track.Title);

        var emDash = NowPlayingParser.Parse("StreamTitle='Кино — Звезда по имени Солнце';");
        Assert.Equal("Кино", emDash!.Artist);
        Assert.Equal("Звезда по имени Солнце", emDash.Title);
    }

    [Fact]
    public void Keeps_title_without_artist()
    {
        var track = NowPlayingParser.Parse("StreamTitle='Evening Jazz';");
        Assert.Null(track!.Artist);
        Assert.Equal("Evening Jazz", track.DisplayTitle);
    }

    [Theory]
    [InlineData("StreamTitle='';")]
    [InlineData("StreamTitle='n/a';")]
    [InlineData("StreamTitle='Radio Paradise';")]
    [InlineData("StreamTitle='https://example.com/stream';")]
    public void Drops_empty_junk_station_and_url_titles(string raw)
    {
        Assert.Null(NowPlayingParser.Parse(raw, "Radio Paradise"));
    }

    [Fact]
    public void Collapses_whitespace_and_limits_length()
    {
        var parsed = NowPlayingParser.Parse("StreamTitle='  A   -   B  ';");
        Assert.Equal("A - B", parsed!.Raw);
        Assert.Null(NowPlayingParser.Parse($"StreamTitle='{new string('x', 300)}';"));
    }

    [Fact]
    public void Recognises_artwork_urls()
    {
        Assert.True(NowPlayingParser.LooksLikeImageUrl("https://cdn.example.com/a.jpg"));
        Assert.False(NowPlayingParser.LooksLikeImageUrl("https://cdn.example.com/a.mp3"));
        Assert.False(NowPlayingParser.LooksLikeImageUrl(null));
    }
}
