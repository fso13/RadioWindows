using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RadioWindows;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;
using RadioWindows.ViewModels;
using RadioWindows.Views;

internal static class Program
{
    private static void Main(string[] args)
    {
        var output = args.FirstOrDefault() ?? "shots";
        Directory.CreateDirectory(output);

        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .SetupWithoutStarting();

        Loc.Initialize(Environment.GetEnvironmentVariable("RADIOWINDOWS_LANG") ?? "ru");

        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            var name = theme == ThemePreference.Dark ? "dark" : "light";
            using var services = CreateServices(theme);
            var shell = new ShellViewModel(services);
            var window = new MainWindow { DataContext = shell };
            window.Show();
            Pump(200);

            Capture(window, Path.Combine(output, $"main-{name}.png"));

            var playing = services.Library.Stations.First(station => station.Id == "soma-groove");
            services.Player.Play(playing);
            Pump(900);
            Capture(window, Path.Combine(output, $"main-playing-{name}.png"));

            services.History.Add(playing, new NowPlaying
            {
                Raw = "Bonobo — Kerala",
                Artist = "Bonobo",
                Title = "Kerala",
            });
            services.History.Add(playing, new NowPlaying
            {
                Raw = "Tycho — Awake",
                Artist = "Tycho",
                Title = "Awake",
            });
            services.History.Add(playing, new NowPlaying
            {
                Raw = "Emancipator — Soon It Will Be Cold Enough",
                Artist = "Emancipator",
                Title = "Soon It Will Be Cold Enough",
            });

            shell.ShowSettings();
            Pump(150);
            Capture(window, Path.Combine(output, $"settings-{name}.png"));

            var settings = (SettingsPageViewModel)shell.Page!;
            var settingsScroll = window.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (settingsScroll is not null)
            {
                settingsScroll.Offset = new Vector(0, settingsScroll.Extent.Height);
                Pump(120);
                Capture(window, Path.Combine(output, $"settings-bottom-{name}.png"));
                settingsScroll.Offset = new Vector(0, 0);
                Pump(60);
            }

            settings.OpenHelpCommand.Execute(null);
            Pump(120);
            Capture(window, Path.Combine(output, $"help-{name}.png"));
            settings.GoBack();

            shell.ShowMain();
            shell.ShowModal(new EqualizerViewModel(services, shell));
            Pump(120);
            Capture(window, Path.Combine(output, $"equalizer-{name}.png"));

            shell.ShowModal(new StationEditorViewModel(services, shell, null));
            Pump(120);
            Capture(window, Path.Combine(output, $"editor-{name}.png"));

            var browser = new BrowserViewModel(services, shell);
            browser.LoadResults(DemoBrowserStations());
            shell.ShowModal(browser);
            Pump(120);
            Capture(window, Path.Combine(output, $"browser-{name}.png"));

            shell.ShowModal(new TransferViewModel(services, shell));
            Pump(120);
            Capture(window, Path.Combine(output, $"transfer-{name}.png"));

            shell.ShowModal(new HistoryViewModel(services, shell));
            Pump(120);
            Capture(window, Path.Combine(output, $"history-{name}.png"));

            services.Diagnostics.SetEnabled(true);
            services.Diagnostics.Log("stream started: https://ice1.somafm.com/groovesalad-128-mp3");
            services.Diagnostics.Log("now playing: Bonobo — Kerala");
            Pump(200);
            shell.ShowModal(new DiagnosticsViewModel(services, shell));
            Pump(120);
            Capture(window, Path.Combine(output, $"diagnostics-{name}.png"));

            shell.ShowModal(new LastFmSetupViewModel(services, shell));
            Pump(120);
            Capture(window, Path.Combine(output, $"lastfm-{name}.png"));

            shell.ShowModal(new ConfirmDialogViewModel(
                "Удалить станцию?",
                "«SomaFM Groove Salad» будет удалена из библиотеки.",
                "Удалить",
                isDestructive: true,
                () => { },
                shell));
            Pump(100);
            Capture(window, Path.Combine(output, $"confirm-{name}.png"));

            shell.CloseModal();

            var miniModel = new MiniPlayerViewModel(services, shell);
            var mini = new MiniPlayerWindow { DataContext = miniModel, AutoHideOnDeactivate = false };
            mini.Show();
            Pump(300);
            Capture(mini, Path.Combine(output, $"mini-player-{name}.png"));
            mini.Close();

            window.Close();
            Pump(60);
        }

        Console.WriteLine($"screenshots written to {Path.GetFullPath(output)}");
    }

    private static AppServices CreateServices(ThemePreference theme)
    {
        var root = Path.Combine(Path.GetTempPath(), "radiowindows-shots", Guid.NewGuid().ToString("N"));
        var services = new AppServices(
            new AppPaths(root),
            new NullAudioEngine(),
            new DemoNowPlayingLookup(),
            new NullLogoResolver(),
            icy: new NullIcyMetadataReader());
        services.Theme.SetPreference(theme);
        return services;
    }

    private static IReadOnlyList<RadioBrowserStation> DemoBrowserStations() =>
    [
        new("rb-1", "Radio Paradise — Mix", "https://stream.radioparadise.com/aac-128",
            "https://radioparadise.com/apple-touch-icon.png", "United States", "Eclectic", "AAC", 128, 4821),
        new("rb-2", "FIP", "https://icecast.radiofrance.fr/fip-midfi.mp3",
            "https://www.radiofrance.fr/favicon.ico", "France", "Jazz", "MP3", 128, 2310),
        new("rb-3", "KEXP", "https://kexp-mp3-128.streamguys1.com/kexp128.mp3",
            "https://www.kexp.org/static/assets/img/favicon-32x32.png", "United States", "Indie", "MP3", 128, 1874),
        new("rb-4", "Radio Swiss Jazz", "https://stream.srg-ssr.ch/m/rsj/mp3_128",
            "https://www.radioswissjazz.ch/favicon.ico", "Switzerland", "Jazz", "MP3", 128, 942),
    ];

    private static void Pump(int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string path)
    {
        var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.WriteLine($"frame is null: {path}");
            return;
        }

        frame.Save(path);
        Console.WriteLine($"saved {path} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
    }

    private sealed class DemoNowPlayingLookup : INowPlayingLookup
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
