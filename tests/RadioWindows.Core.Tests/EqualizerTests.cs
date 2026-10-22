using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class EqualizerTests
{
    [Fact]
    public void There_are_ten_bands_and_frequencies()
    {
        Assert.Equal(10, EqualizerCatalog.BandCount);
        Assert.Equal(new[] { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }, EqualizerCatalog.Frequencies);
    }

    [Fact]
    public void Gains_are_clamped_and_padded()
    {
        var gains = EqualizerCatalog.ClampGains([20, -20, 1]);
        Assert.Equal(10, gains.Length);
        Assert.Equal(12, gains[0]);
        Assert.Equal(-12, gains[1]);
        Assert.Equal(1, gains[2]);
        Assert.Equal(0, gains[9]);
    }

    [Fact]
    public void Unknown_preset_falls_back_to_flat()
    {
        Assert.Equal(EqualizerCatalog.FlatId, EqualizerCatalog.ById("missing").Id);
    }

    [Fact]
    public void Frequency_labels_match_the_mac_app()
    {
        Assert.Equal("32", EqualizerCatalog.LabelKeyForFrequency(32));
        Assert.Equal("1k", EqualizerCatalog.LabelKeyForFrequency(1000));
        Assert.Equal("16k", EqualizerCatalog.LabelKeyForFrequency(16000));
    }

    [Fact]
    public void Service_persists_enabled_preset_and_custom_gains()
    {
        using var fixture = new TestPreferences();
        var service = new EqualizerService(fixture.Preferences);
        service.SetEnabled(true);
        service.SetBand(0, 6);
        Assert.True(service.IsCustom);
        Assert.Equal(6, service.Gains[0]);
        service.Dispose();

        var reloaded = new EqualizerService(fixture.Preferences);
        Assert.True(reloaded.Enabled);
        Assert.True(reloaded.IsCustom);
        Assert.Equal(6, reloaded.Gains[0]);
        reloaded.Dispose();
    }
}
