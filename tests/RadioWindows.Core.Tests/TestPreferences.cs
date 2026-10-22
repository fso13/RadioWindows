using RadioWindows.Core;

namespace RadioWindows.Core.Tests;

/// <summary>Preferences backed by a throwaway directory.</summary>
public sealed class TestPreferences : IDisposable
{
    private readonly string _directory;

    public TestPreferences()
    {
        _directory = Path.Combine(Path.GetTempPath(), "radiowindows-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Paths = new AppPaths(_directory);
        Preferences = new Preferences(Paths);
    }

    public AppPaths Paths { get; }

    public Preferences Preferences { get; }

    public void Dispose()
    {
        Preferences.Dispose();
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }
}
