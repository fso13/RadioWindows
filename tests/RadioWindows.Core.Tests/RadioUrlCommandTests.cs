using RadioWindows.Core.Data;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class RadioUrlCommandTests
{
    [Theory]
    [InlineData("radiowindows://open", RadioUrlAction.Open, null)]
    [InlineData("radiowindows://pause", RadioUrlAction.Pause, null)]
    [InlineData("radiowindows://settings", RadioUrlAction.Settings, null)]
    [InlineData("radiowindows://play/rp-marusya", RadioUrlAction.Play, "rp-marusya")]
    [InlineData("radiomac://play/rp-marusya", RadioUrlAction.Play, "rp-marusya")]
    [InlineData("radiowindows://play/", RadioUrlAction.Open, null)]
    [InlineData("https://example.com", RadioUrlAction.Unknown, null)]
    [InlineData("garbage", RadioUrlAction.Unknown, null)]
    public void Parses_deep_links(string raw, RadioUrlAction action, string? stationId)
    {
        var command = RadioUrlCommand.Parse(raw);
        Assert.Equal(action, command.Action);
        Assert.Equal(stationId, command.StationId);
    }

    [Fact]
    public void Decodes_station_ids()
    {
        Assert.Equal("a b", RadioUrlCommand.Parse("radiowindows://play/a%20b").StationId);
    }
}
