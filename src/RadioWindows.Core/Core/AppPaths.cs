namespace RadioWindows.Core;

/// <summary>File-system locations owned by the app (roaming profile by default).</summary>
public sealed class AppPaths
{
    public const string DataDirectoryVariable = "RADIOWINDOWS_DATA_DIR";

    public AppPaths(string? root = null)
    {
        var overridden = root ?? Environment.GetEnvironmentVariable(DataDirectoryVariable);
        Root = string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                    Environment.SpecialFolderOption.Create),
                "RadioWindows")
            : overridden;
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string CacheDirectory => Path.Combine(Root, "cache");

    public string LogDirectory => Path.Combine(Root, "logs");

    public string LogFile => Path.Combine(LogDirectory, "radio-debug.log");

    public string CrashMarkerFile => Path.Combine(Root, "session.open");
}
