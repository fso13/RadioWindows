using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioWindows.Core.Services;

/// <summary>
/// Opt-in local diagnostics: a small event journal plus crash detection through
/// a session marker. Nothing is uploaded and stream URLs are reduced to their
/// scheme and host before they reach the file. All operations are best effort
/// and never throw. Property changes are raised on the caller's (UI) thread.
/// </summary>
public sealed class DiagnosticsService : ObservableObject, IDisposable
{
    /// <summary>Preferences key holding the opt-in flag.</summary>
    public const string EnabledKey = "diagnostics_enabled";

    /// <summary>Number of lines returned (and kept in memory) by default.</summary>
    public const int MaxLines = 500;

    /// <summary>The journal is trimmed to roughly half of this size when it grows past it.</summary>
    public const int RotationLimitBytes = 512 * 1024;

    private const string LogFileName = "diagnostics.log";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Regex UrlPattern = new(
        @"\bhttps?://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly AppPaths _paths;
    private readonly Preferences _preferences;
    private readonly object _gate = new();
    private volatile bool _enabled;
    private bool _started;
    private bool _sessionActive;
    private bool _hooked;
    private bool _disposed;

    public DiagnosticsService(AppPaths paths, Preferences preferences)
    {
        _paths = paths;
        _preferences = preferences;
        _enabled = preferences.GetBoolLike(EnabledKey) ?? false;
    }

    public bool IsEnabled => _enabled;

    public string LogPath => Path.Combine(_paths.LogDirectory, LogFileName);

    public bool HasLog => File.Exists(LogPath);

    /// <summary>Start time of the previous session when it did not exit cleanly.</summary>
    public DateTimeOffset? LastCrashDate { get; private set; }

    // MARK: - Lifecycle

    public void Start()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        EnsureHooks();
        if (!_enabled)
        {
            NotifyState();
            return;
        }

        BeginSession();
        Append("app", Loc.Format("Diagnostics.Log.Started", VersionText));
        NotifyState();
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }

        EnsureHooks();
        if (enabled)
        {
            _enabled = true;
            try
            {
                _preferences.Set(EnabledKey, true);
            }
            catch (Exception)
            {
                // Storage errors must not break the journal toggle.
            }

            BeginSession();
            Append("app", Loc.T("Diagnostics.Log.Enabled"));
            NotifyState();
            return;
        }

        Append("app", Loc.T("Diagnostics.Log.Disabled"));
        _enabled = false;
        try
        {
            _preferences.Set(EnabledKey, false);
        }
        catch (Exception)
        {
            // Best effort.
        }

        lock (_gate)
        {
            _sessionActive = false;
        }

        TryDeleteMarker();
        NotifyState();
    }

    public void MarkCleanExit()
    {
        lock (_gate)
        {
            if (!_sessionActive)
            {
                return;
            }

            _sessionActive = false;
        }

        if (_enabled)
        {
            Append("app", Loc.T("Diagnostics.Log.Exit"));
        }

        TryDeleteMarker();
    }

    // MARK: - Journal

    public void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Append("app", message);
    }

    public IReadOnlyList<string> ReadLines(int max = MaxLines)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(LogPath))
                {
                    return Array.Empty<string>();
                }

                var lines = File.ReadAllLines(LogPath, Utf8);
                if (max <= 0 || lines.Length <= max)
                {
                    return lines;
                }

                return lines[^max..];
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    File.Delete(LogPath);
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
        }

        if (_enabled)
        {
            Append("app", Loc.T("Diagnostics.Log.Cleared"));
        }

        NotifyState();
    }

    // MARK: - Redaction

    /// <summary>
    /// Replaces full URLs with <c>scheme://host</c>, dropping path, query and
    /// fragment because stream tokens often live there.
    /// </summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        try
        {
            return UrlPattern.Replace(text, match => RedactMatch(match.Value));
        }
        catch (Exception)
        {
            return text;
        }
    }

    private static string RedactMatch(string value)
    {
        var trailing = string.Empty;
        while (value.Length > 0 && value[^1] is '.' or ',' or ';' or ')' or ']' or '}')
        {
            trailing = value[^1] + trailing;
            value = value[..^1];
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return value + trailing;
        }

        return $"{uri.Scheme.ToLowerInvariant()}://{uri.Host}{trailing}";
    }

    // MARK: - File storage

    private void Append(string category, string message)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            var line = Line(DateTimeOffset.Now, category, message);
            lock (_gate)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (File.Exists(LogPath)
                    && new FileInfo(LogPath).Length + Utf8.GetByteCount(line) + Environment.NewLine.Length > RotationLimitBytes)
                {
                    Rotate();
                }

                File.AppendAllText(LogPath, line + Environment.NewLine, Utf8);
            }
        }
        catch (Exception)
        {
            // Diagnostics must never take the app down.
        }
    }

    /// <summary>Keeps the newest lines that fit into half of the limit. Caller holds the gate.</summary>
    private void Rotate()
    {
        try
        {
            if (!File.Exists(LogPath))
            {
                return;
            }

            var lines = File.ReadAllLines(LogPath, Utf8);
            var kept = new List<string>();
            var bytes = 0;
            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var size = Utf8.GetByteCount(lines[index]) + Environment.NewLine.Length;
                if (bytes + size > RotationLimitBytes / 2)
                {
                    break;
                }

                kept.Insert(0, lines[index]);
                bytes += size;
            }

            File.WriteAllLines(LogPath, kept, Utf8);
        }
        catch (Exception)
        {
            // A failed trim leaves the previous journal in place.
        }
    }

    // MARK: - Session marker

    private void BeginSession()
    {
        lock (_gate)
        {
            if (_sessionActive)
            {
                return;
            }

            _sessionActive = true;
        }

        var previous = ReadMarker();
        if (previous is { } crashedAt)
        {
            LastCrashDate = crashedAt;
            Append("crash", Loc.Format("Diagnostics.Log.Crash", Timestamp(crashedAt)));
        }

        WriteMarker(DateTimeOffset.Now);
    }

    private DateTimeOffset? ReadMarker()
    {
        try
        {
            var path = _paths.CrashMarkerFile;
            if (!File.Exists(path))
            {
                return null;
            }

            var text = File.ReadAllText(path).Trim();
            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WriteMarker(DateTimeOffset now)
    {
        try
        {
            var path = _paths.CrashMarkerFile;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, Timestamp(now), Utf8);
        }
        catch (Exception)
        {
            // Crash detection is best effort.
        }
    }

    private void TryDeleteMarker()
    {
        try
        {
            if (File.Exists(_paths.CrashMarkerFile))
            {
                File.Delete(_paths.CrashMarkerFile);
            }
        }
        catch (Exception)
        {
            // Best effort.
        }
    }

    // MARK: - Exceptions

    private void EnsureHooks()
    {
        lock (_gate)
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void Unhook()
    {
        lock (_gate)
        {
            if (!_hooked)
            {
                return;
            }

            _hooked = false;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Append("crash", Loc.Format("Diagnostics.Log.Unhandled", Describe(e.ExceptionObject)));

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        Append("error", Loc.Format("Diagnostics.Log.Unobserved", e.Exception.GetBaseException().Message));

    private static string Describe(object? exception) => exception switch
    {
        Exception error => $"{error.GetType().Name}: {error.Message}",
        null => "unknown",
        _ => exception.ToString() ?? "unknown",
    };

    // MARK: - Formatting

    /// <summary>Refreshes the settings page when the toggle or crash state changes.</summary>
    private void NotifyState()
    {
        try
        {
            Raise(nameof(IsEnabled));
            Raise(nameof(HasLog));
            Raise(nameof(LastCrashDate));
        }
        catch (Exception)
        {
            // A faulty view-model handler must not break diagnostics.
        }
    }

    private static string Line(DateTimeOffset date, string category, string message) =>
        $"{Timestamp(date)} [{category}] {Redact(message)}";

    private static string Timestamp(DateTimeOffset date) =>
        date.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string VersionText
    {
        get
        {
            var version = typeof(DiagnosticsService).Assembly.GetName().Version;
            return version is null
                ? "1.0.0"
                : $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unhook();
        if (_started)
        {
            MarkCleanExit();
        }
    }
}
