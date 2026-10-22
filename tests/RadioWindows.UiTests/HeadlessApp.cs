using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using RadioWindows;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;
using RadioWindows.ViewModels;
using RadioWindows.Views;
using Xunit;

namespace RadioWindows.UiTests;

/// <summary>Boots the headless Avalonia platform once per test process.</summary>
public static class HeadlessApp
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            Loc.Initialize("ru");
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia()
                .WithInterFont()
                .SetupWithoutStarting();
            _initialized = true;
        }
    }

    public static (AppServices Services, ShellViewModel Shell, MainWindow Window) CreateShell()
    {
        Ensure();
        var root = Path.Combine(Path.GetTempPath(), "radiowindows-uitests", Guid.NewGuid().ToString("N"));
        var services = new AppServices(
            new AppPaths(root),
            new NullAudioEngine(),
            new FixedLookup(),
            new NullLogoResolver(),
            icy: new NullIcyMetadataReader());
        var shell = new ShellViewModel(services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump(120);
        return (services, shell, window);
    }

    public static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(8);
        }

        Dispatcher.UIThread.RunJobs();
    }

    public static bool WaitUntil(Func<bool> condition, int timeoutMs = 4000)
    {
        var until = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < until)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    public static void Capture(Window window, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);

        // The headless compositor needs a couple of frames; retry instead of flaking.
        for (var attempt = 0; attempt < 12; attempt++)
        {
            Pump(80);
            var frame = window.CaptureRenderedFrame();
            if (frame is not null)
            {
                frame.Save(Path.Combine(directory, name + ".png"));
                return;
            }
        }

        Assert.Fail($"No frame rendered for {name}");
    }

    private sealed class FixedLookup : INowPlayingLookup
    {
        public Task<NowPlaying?> ResolveAsync(
            string streamUrl,
            string stationName,
            bool allowIcy,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<NowPlaying?>(new NowPlaying
            {
                Raw = "Bonobo — Kerala",
                Artist = "Bonobo",
                Title = "Kerala",
            });

        public Task<string?> ArtworkForAsync(NowPlaying track, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }
}
