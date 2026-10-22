using System.Globalization;
using RadioWindows.Core.Services;
using Xunit;

namespace RadioWindows.Core.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Disabled_journal_writes_nothing()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);

        diagnostics.Start();
        diagnostics.Log("https://example.com/stream?token=secret");

        Assert.False(diagnostics.IsEnabled);
        Assert.False(diagnostics.HasLog);
        Assert.Empty(diagnostics.ReadLines());
    }

    [Fact]
    public void Enabled_journal_writes_lines_and_redacts_urls()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);

        diagnostics.SetEnabled(true);
        diagnostics.Start();
        diagnostics.Log("playing https://user:secret@example.com:8000/stream?token=abc");

        Assert.True(diagnostics.IsEnabled);
        Assert.True(diagnostics.HasLog);
        Assert.EndsWith("diagnostics.log", diagnostics.LogPath);

        var lines = diagnostics.ReadLines();
        Assert.NotEmpty(lines);
        var logged = Assert.Single(lines, line => line.Contains("playing", StringComparison.Ordinal));
        Assert.Contains("https://example.com", logged);
        Assert.DoesNotContain("secret", logged);
        Assert.DoesNotContain("token", logged);
        Assert.Contains("[app]", logged);
    }

    [Fact]
    public void Previous_session_marker_is_reported_as_a_crash()
    {
        using var preferences = new TestPreferences();
        var crashedAt = DateTimeOffset.Now.AddMinutes(-5);
        File.WriteAllText(
            preferences.Paths.CrashMarkerFile,
            crashedAt.ToString("O", CultureInfo.InvariantCulture));

        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);
        diagnostics.SetEnabled(true);
        diagnostics.Start();

        Assert.Equal(crashedAt, diagnostics.LastCrashDate);
        Assert.Contains(diagnostics.ReadLines(), line => line.Contains("[crash]", StringComparison.Ordinal));
        Assert.True(File.Exists(preferences.Paths.CrashMarkerFile));
    }

    [Fact]
    public void Clean_exit_removes_the_session_marker_and_logs_once()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);

        diagnostics.SetEnabled(true);
        diagnostics.Start();
        var before = diagnostics.ReadLines().Count;

        diagnostics.MarkCleanExit();
        diagnostics.MarkCleanExit();

        Assert.False(File.Exists(preferences.Paths.CrashMarkerFile));
        var after = diagnostics.ReadLines();
        Assert.Equal(before + 1, after.Count);
        Assert.Contains("[app]", after[^1]);
    }

    [Fact]
    public void Clear_removes_the_file_when_disabled()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);

        diagnostics.SetEnabled(true);
        diagnostics.Start();
        diagnostics.Log("something");
        diagnostics.SetEnabled(false);

        diagnostics.Clear();

        Assert.False(diagnostics.HasLog);
        Assert.Empty(diagnostics.ReadLines());
    }

    [Fact]
    public void Clear_keeps_a_single_marker_line_when_enabled()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);

        diagnostics.SetEnabled(true);
        diagnostics.Start();
        diagnostics.Log("something");
        diagnostics.Clear();

        Assert.True(diagnostics.HasLog);
        var line = Assert.Single(diagnostics.ReadLines());
        Assert.Contains("[app]", line);
    }

    [Fact]
    public void Journal_rotates_around_the_limit_and_keeps_the_newest_lines()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);
        diagnostics.SetEnabled(true);
        diagnostics.Start();

        var payload = new string('x', 1000);
        for (var index = 0; index < 800; index++)
        {
            diagnostics.Log($"{payload} {index}");
        }

        var size = new FileInfo(diagnostics.LogPath).Length;
        Assert.True(size <= DiagnosticsService.RotationLimitBytes, $"journal grew to {size} bytes");

        var lines = diagnostics.ReadLines(10);
        Assert.Equal(10, lines.Count);
        Assert.Contains("799", lines[^1]);
    }

    [Fact]
    public void Read_lines_respects_the_maximum()
    {
        using var preferences = new TestPreferences();
        using var diagnostics = new DiagnosticsService(preferences.Paths, preferences.Preferences);
        diagnostics.SetEnabled(true);
        diagnostics.Start();

        for (var index = 0; index < 20; index++)
        {
            diagnostics.Log($"line {index}");
        }

        var lines = diagnostics.ReadLines(5);
        Assert.Equal(5, lines.Count);
        Assert.Contains("line 19", lines[^1]);
    }

    [Theory]
    [InlineData("see https://example.com/a/b?token=1 now", "see https://example.com now")]
    [InlineData("http://radio.example.org:8000/stream", "http://radio.example.org")]
    [InlineData("https://user:pass@example.com/path", "https://example.com")]
    [InlineData("no url here", "no url here")]
    public void Redact_keeps_only_scheme_and_host(string input, string expected)
    {
        Assert.Equal(expected, DiagnosticsService.Redact(input));
    }
}
