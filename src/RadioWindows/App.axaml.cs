using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using RadioWindows.Core;
using RadioWindows.Services;
using RadioWindows.ViewModels;
using RadioWindows.Views;

namespace RadioWindows;

public sealed class App : Application
{
    public AppServices? Services { get; private set; }

    public ShellViewModel? Shell { get; private set; }

    private TrayController? _tray;

    private GlobalHotKeyService? _hotKeys;

    private MainWindow? _window;

    public override void Initialize()
    {
        Loc.Initialize();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var firstRun = !System.IO.File.Exists(new AppPaths().SettingsFile);
            var services = new AppServices();
            Services = services;
            var shell = new ShellViewModel(services);
            Shell = shell;
            var window = new MainWindow { DataContext = shell };
            _window = window;
            services.Clipboard.Attach(window);

            void ShowWindow()
            {
                window.Opacity = 1;
                window.Show();
                window.Activate();
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }
            }

            shell.HideRequested += window.HidePanel;

            void HandleUrl(string url)
            {
                var command = RadioWindows.Core.Data.RadioUrlCommand.Parse(url);
                switch (command.Action)
                {
                    case RadioWindows.Core.Data.RadioUrlAction.Play:
                        if (command.StationId is { } id && services.Library.ById(id) is { } station)
                        {
                            services.Player.Play(station);
                            return;
                        }

                        ShowWindow();
                        return;
                    case RadioWindows.Core.Data.RadioUrlAction.Pause:
                        services.Player.Pause();
                        return;
                    case RadioWindows.Core.Data.RadioUrlAction.Settings:
                        ShowWindow();
                        shell.ShowSettings();
                        return;
                    case RadioWindows.Core.Data.RadioUrlAction.Open:
                        ShowWindow();
                        return;
                }
            }
            shell.QuitRequested += () =>
            {
                window.PrepareForExit();
                desktop.Shutdown();
            };

            services.AlarmShowWindow = ShowWindow;
            _tray = new TrayController(services, shell, ShowWindow, () =>
            {
                ShowWindow();
                shell.ShowSettings();
            }, () =>
            {
                window.PrepareForExit();
                desktop.Shutdown();
            });

            _hotKeys = new GlobalHotKeyService(services.Preferences);
            _hotKeys.PlayPausePressed += () => shell.Main.TogglePlayPauseCommand.Execute(null);
            _hotKeys.NextPressed += () => shell.Main.PlayNextStation();
            var args = Environment.GetCommandLineArgs();
            var showOnStart = firstRun
                              || args.Contains("--show")
                              || args.Contains("-showWindow")
                              || !OperatingSystem.IsWindows()
                              || Environment.GetEnvironmentVariable("RADIOWINDOWS_SHOW") == "1";

            window.Opened += (_, _) =>
            {
                _hotKeys?.Attach(window);
                _hotKeys?.RegisterAll();
                if (!showOnStart)
                {
                    // A hidden window still owns the handle global hotkeys need.
                    window.Opacity = 0;
                    window.Hide();
                }
            };

            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                _hotKeys?.Dispose();
                _tray?.Dispose();
                services.Dispose();
            };
            if (Program.ShowSignal is { } signal)
            {
                var listener = new Thread(() =>
                {
                    while (true)
                    {
                        signal.WaitOne();
                        var forwarded = ReadForwardedUrl();
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (forwarded is not null)
                            {
                                HandleUrl(forwarded);
                            }
                            else
                            {
                                ShowWindow();
                            }
                        });
                    }
                })
                {
                    IsBackground = true,
                    Name = "RadioWindows.InstanceListener",
                };
                listener.Start();
            }

            services.Start();

            window.Opacity = showOnStart ? 1 : 0;
            window.Show();

            if (Program.StartupUrl is { } startupUrl)
            {
                HandleUrl(startupUrl);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static string? ReadForwardedUrl()
    {
        try
        {
            var path = Program.ForwardedUrlFile;
            if (!File.Exists(path))
            {
                return null;
            }

            var url = File.ReadAllText(path).Trim();
            File.Delete(path);
            return url.Length == 0 ? null : url;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
