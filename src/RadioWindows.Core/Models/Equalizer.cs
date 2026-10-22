namespace RadioWindows.Core.Models;

/// <summary>A 10-band equalizer preset. Gains are in dB, ordered low → high,
/// following the fixed band centres of the libVLC equalizer:
/// 60, 170, 310, 600, 1k, 3k, 6k, 12k, 14k, 16k Hz.</summary>
public sealed record EqualizerPreset(string Id, string LabelKey, IReadOnlyList<double> Gains);

public static class EqualizerCatalog
{
    public const int BandCount = 10;
    public const double MinGain = -12;
    public const double MaxGain = 12;

    public static readonly IReadOnlyList<int> Frequencies = [60, 170, 310, 600, 1_000, 3_000, 6_000, 12_000, 14_000, 16_000];

    public const string CustomId = "custom";
    public const string FlatId = "flat";

    public static readonly EqualizerPreset Custom =
        new(CustomId, "Eq.Custom", Enumerable.Repeat(0.0, BandCount).ToArray());

    public static readonly IReadOnlyList<EqualizerPreset> Presets =
    [
        new(FlatId, "Eq.Flat", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        Custom,
        new("bass", "Eq.Bass", [8, 6, 4, 2, 0, -1, -2, -2, -2, -2]),
        new("treble", "Eq.Treble", [-3, -2, -1, 0, 1, 2, 4, 6, 7, 8]),
        new("voice", "Eq.Voice", [-4, -3, -1, 2, 5, 5, 4, 1, -2, -4]),
        new("rock", "Eq.Rock", [5, 4, 2, 0, -2, -1, 1, 3, 4, 5]),
        new("pop", "Eq.Pop", [-1, 2, 4, 3, 0, -1, -1, 1, 3, 4]),
        new("jazz", "Eq.Jazz", [3, 2, 0, 1, -2, -2, 0, 1, 3, 4]),
        new("electronic", "Eq.Electronic", [6, 5, 1, 0, -2, 2, 1, 2, 4, 6]),
        new("classical", "Eq.Classical", [5, 3, 1, 0, 0, 0, -1, -1, 2, 4]),
        new("spoken", "Eq.Spoken", [-6, -4, 0, 3, 4, 4, 2, 0, -2, -5]),
    ];

    public static EqualizerPreset ById(string? id) =>
        Presets.FirstOrDefault(preset => preset.Id == id) ?? Presets[0];

    public static string LabelKeyForFrequency(int hz) => hz >= 1_000 ? $"{hz / 1_000}k" : $"{hz}";

    /// <summary>Pads/truncates to <see cref="BandCount"/> bands and clamps every gain to ±12 dB.</summary>
    public static double[] ClampGains(IEnumerable<double>? raw)
    {
        var input = raw?.ToArray() ?? [];
        var result = new double[BandCount];
        for (var index = 0; index < BandCount; index++)
        {
            var value = index < input.Length ? input[index] : 0;
            result[index] = Math.Clamp(value, MinGain, MaxGain);
        }

        return result;
    }

    public static bool SameGains(IReadOnlyList<double> left, IReadOnlyList<double> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => Math.Abs(pair.First - pair.Second) <= 0.05);
}
