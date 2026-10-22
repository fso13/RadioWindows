namespace RadioWindows.Core.Models;

public enum StationImportMode
{
    /// <summary>Keep the existing library and append new streams.</summary>
    Merge,

    /// <summary>Drop custom stations and import from scratch.</summary>
    Replace,
}

public sealed class StationImportResult
{
    public int Added { get; set; }

    public int Skipped { get; set; }

    public int FavoritesUpdated { get; set; }
}
