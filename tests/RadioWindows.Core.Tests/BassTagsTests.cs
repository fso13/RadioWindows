using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class BassTagsTests
{
    [Fact]
    public void Parses_icy_and_http_header_series()
    {
        var tags = BassTags.ParseTagLines(
        [
            "icy-name:Radio Paradise",
            "icy-br:128",
            "Server: Icecast 2.4.0",
            "Content-Type: audio/aacp",
        ]);

        Assert.Equal("Radio Paradise", tags["icy-name"]);
        Assert.Equal("128", tags["icy-br"]);
        Assert.Equal(128, BassTags.BitrateFromTags(tags));
        Assert.Equal("Icecast 2.4.0", BassTags.ServerFromTags(tags, tags, "https://example.com/stream"));
    }

    [Fact]
    public void Falls_back_to_the_url_host_for_the_server()
    {
        var empty = BassTags.ParseTagLines([]);
        Assert.Equal("stream.example.com",
            BassTags.ServerFromTags(empty, empty, "https://stream.example.com/live"));
    }

    [Theory]
    [InlineData(BassNativeTypes.Mp3, null, null, "MP3")]
    [InlineData(BassNativeTypes.Aac, null, null, "AAC")]
    [InlineData(BassNativeTypes.Opus, null, null, "Opus")]
    [InlineData(BassNativeTypes.Flac, null, null, "FLAC")]
    [InlineData(0, "application/vnd.apple.mpegurl", null, "HLS")]
    [InlineData(0, "audio/mpeg", "https://example.com/live.mp3", "MP3")]
    public void Maps_channel_types_to_codec_names(uint ctype, string? contentType, string? url, string expected)
    {
        Assert.Equal(expected, BassTags.CodecFromChannelType(ctype, contentType, url));
    }

    private static class BassNativeTypes
    {
        public const uint Mp3 = 0x10005;
        public const uint Aac = 0x10b00;
        public const uint Opus = 0x11200;
        public const uint Flac = 0x10900;
    }
}
