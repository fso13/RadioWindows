using Avalonia;

namespace RadioWindows;

internal static class Program
{
    private const string InstanceMutexName = @"Local\RadioWindows.Instance";
    private const string ShowEventName = @"Local\RadioWindows.Show";

    /// <summary>Signalled by a second process to bring the existing window forward.</summary>
    public static EventWaitHandle? ShowSignal { get; private set; }

    /// <summary>Deep link passed on the command line (radiowindows://…).</summary>
    public static string? StartupUrl { get; private set; }

    /// <summary>Deep link forwarded by a second instance.</summary>
    public static string ForwardedUrlFile => Path.Combine(Path.GetTempPath(), "radiowindows-url.txt");

    [STAThread]
    public static void Main(string[] args)
    {
        StartupUrl = FindUrl(args);
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            if (StartupUrl is { } forwarded)
            {
                try
                {
                    File.WriteAllText(ForwardedUrlFile, forwarded);
                }
                catch (IOException)
                {
                    // The first instance simply opens its window.
                }
            }

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                    existing.Set();
                }
                catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException
                                                   or UnauthorizedAccessException)
                {
                    // The first instance is starting up; the second click is a no-op.
                }
            }

            return;
        }

        ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static string? FindUrl(IReadOnlyList<string> args)
    {
        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index];
            if (arg is "--url" && index + 1 < args.Count)
            {
                return args[index + 1];
            }

            if (arg.StartsWith("radiowindows://", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("radiomac://", StringComparison.OrdinalIgnoreCase))
            {
                return arg;
            }
        }

        return null;
    }

    /// <summary>Entry point shared by the desktop app and the headless UI tests.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
