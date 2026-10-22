namespace RadioWindows.Core.Models;

/// <summary>The fixed genre vocabulary plus normalisation helpers.</summary>
public static class GenreCatalog
{
    public const string Jazz = "Jazz";
    public const string Electronic = "Electronic";
    public const string Rock = "Rock";
    public const string Classical = "Classical";
    public const string News = "News";
    public const string Ambient = "Ambient";
    public const string Pop = "Pop";
    public const string Chill = "Chill";
    public const string Folk = "Folk";
    public const string World = "World";
    public const string Shanson = "Shanson";
    public const string Retro = "Retro";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> Presets =
    [
        Jazz, Electronic, Rock, Classical, News, Ambient, Pop,
        Chill, Folk, World, Shanson, Retro, Other,
    ];

    /// <summary>Keeps known genres in their canonical casing, passes custom ones through.</summary>
    public static string Normalize(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Other;
        }

        foreach (var preset in Presets)
        {
            if (string.Equals(preset, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return trimmed;
    }
}
