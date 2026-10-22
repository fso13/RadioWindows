using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Services;

namespace RadioWindows.Services;

/// <summary>Composition root: one instance per process, shared by every view.</summary>
public sealed class AppServices : IDisposable
{
    private readonly NotificationSink _notifications = new();

    public AppServices(
        AppPaths? paths = null,
        IAudioEngine? engine = null,
        INowPlayingLookup? lookup = null,
        ILogoResolver? logos = null,
        bool loadLibrary = true,
        IIcyMetadataReader? icy = null)
    {
        Paths = paths ?? new AppPaths();
        Dispatcher = new AvaloniaDispatcher();
        ObservableObject.UiDispatcher = Dispatcher;
        Preferences = new Preferences(Paths);
        Preferences.MigrateFlutterKeysIfNeeded();
        Settings = new AppSettings(Preferences);
        Theme = new ThemeService(Preferences);
        Library = new StationLibrary(Preferences, logos ?? new LogoResolver(), Dispatcher);
        Equalizer = new EqualizerService(Preferences);
        Player = new RadioPlayerService(
            Settings,
            Library,
            Equalizer,
            lookup ?? new NowPlayingLookup(),
            engine ?? AudioEngineFactory.Create(),
            Dispatcher,
            icy);
        Sleep = new SleepTimerService(Player, Dispatcher);
        Toasts = new ToastService();
        _notifications.Toasts = Toasts;
        Clipboard = new ClipboardService();
        Images = new ImageCache(Path.Combine(Paths.CacheDirectory, "images"));
        Diagnostics = new DiagnosticsService(Paths, Preferences);
        History = new TrackHistoryService(Preferences, Player);
        Alarm = new AlarmService(Preferences, Library, Player, Player, Dispatcher, _notifications);
        HotKeys = new GlobalHotKeyService(Preferences);
        Updates = new UpdateService();
        Browser = new RadioBrowserService();
        LastFm = new LastFmService(
            Preferences,
            Player,
            LastFmConfig.IsConfigured ? new LastFmClient(LastFmConfig.ApiKey, LastFmConfig.ApiSecret) : null,
            url =>
            {
                ShellOpen.OpenUrl(url);
                return Task.CompletedTask;
            });

        if (loadLibrary)
        {
            Library.Load();
        }
    }

    public AppPaths Paths { get; }

    public IUiDispatcher Dispatcher { get; }

    public Preferences Preferences { get; }

    public AppSettings Settings { get; }

    public ThemeService Theme { get; }

    public StationLibrary Library { get; }

    public EqualizerService Equalizer { get; }

    public RadioPlayerService Player { get; }

    public SleepTimerService Sleep { get; }

    public TrackHistoryService History { get; }

    public AlarmService Alarm { get; }

    public DiagnosticsService Diagnostics { get; }

    public UpdateService Updates { get; }

    public LastFmService LastFm { get; }

    public GlobalHotKeyService HotKeys { get; }

    public RadioBrowserService Browser { get; }

    public ToastService Toasts { get; }

    public ClipboardService Clipboard { get; }

    public ImageCache Images { get; }

    /// <summary>Called once the main window exists so alarm notifications can surface.</summary>
    public Action? AlarmShowWindow
    {
        get => _notifications.ShowWindow;
        set => _notifications.ShowWindow = value;
    }

    public void Start()
    {
        RemoteImage.Configure(Images);
        Player.Start();
        Alarm.Start();
        Diagnostics.Start();
        Diagnostics.Log("app started");
    }

    public void Dispose()
    {
        Diagnostics.Log("app stopping");
        Diagnostics.MarkCleanExit();
        HotKeys.Dispose();
        LastFm.Dispose();
        History.Dispose();
        Alarm.Dispose();
        Sleep.Dispose();
        Player.Dispose();
        Equalizer.Dispose();
        Updates.Dispose();
        Diagnostics.Dispose();
        Preferences.Dispose();
    }

    private sealed class NotificationSink : INotificationSink
    {
        public Action? ShowWindow { get; set; }

        public ToastService? Toasts { get; set; }

        public void Show(string title, string body)
        {
            Toasts?.Show($"{title}: {body}", ToastKind.Info);
            ShowWindow?.Invoke();
        }
    }
}
