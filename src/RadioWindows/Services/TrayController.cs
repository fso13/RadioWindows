using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.ViewModels;
using RadioWindows.Views;

namespace RadioWindows.Services;

/// <summary>Tray icon: tooltip, native menu and the mini-player popover.</summary>
public sealed class TrayController : IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly TrayIcon _tray;
    private readonly NativeMenu _menu = new();
    private readonly NativeMenuItem _playItem;
    private readonly NativeMenuItem _stationItem;
    private readonly NativeMenuItem _trackItem;
    private MiniPlayerWindow? _mini;
    private MiniPlayerViewModel? _miniModel;
    private readonly Action _showWindow;
    private readonly Action _showSettings;
    private readonly Action _quit;

    public TrayController(
        AppServices services,
        ShellViewModel shell,
        Action showWindow,
        Action showSettings,
        Action quit)
    {
        _services = services;
        _shell = shell;
        _showWindow = showWindow;
        _showSettings = showSettings;
        _quit = quit;

        _stationItem = new NativeMenuItem(string.Empty) { IsEnabled = false };
        _trackItem = new NativeMenuItem(string.Empty) { IsEnabled = false };
        _playItem = new NativeMenuItem(Loc.T("Player.Play"));
        _playItem.Click += (_, _) => TogglePlayPause();

        var show = new NativeMenuItem(Loc.T("Tray.Show"));
        show.Click += (_, _) => _showWindow();

        var stop = new NativeMenuItem(Loc.T("Player.Stop"));
        stop.Click += (_, _) => _services.Player.Stop();

        var sleep = new NativeMenuItem(Loc.T("Player.Sleep"));
        var sleepMenu = new NativeMenu();
        foreach (var minutes in _services.Sleep.Options)
        {
            var option = new NativeMenuItem(Loc.Format("Player.SleepOptions", minutes));
            option.Click += (_, _) => _services.Sleep.Start(minutes);
            sleepMenu.Add(option);
        }

        var cancelSleep = new NativeMenuItem(Loc.T("Player.SleepCancel"));
        cancelSleep.Click += (_, _) => _services.Sleep.Cancel();
        sleepMenu.Add(new NativeMenuItemSeparator());
        sleepMenu.Add(cancelSleep);
        sleep.Menu = sleepMenu;

        var updatesItem = new NativeMenuItem(Loc.T("Tray.CheckUpdates"));
        updatesItem.Click += async (_, _) =>
        {
            var info = await _services.Updates.CheckAsync(CancellationToken.None);
            _services.Toasts.Show(info is null
                ? Loc.T("Updates.UpToDate")
                : Loc.Format("Updates.Available", info.Version));
        };

        var history = new NativeMenuItem(Loc.T("History.Title"));
        history.Click += (_, _) =>
        {
            _showWindow();
            shell.ShowModal(new HistoryViewModel(services, shell));
        };

        var settings = new NativeMenuItem(Loc.T("Header.Settings").TrimEnd('…'));
        settings.Click += (_, _) => _showSettings();

        var quitItem = new NativeMenuItem(Loc.T("Tray.Quit"));
        quitItem.Click += (_, _) => _quit();

        _menu.Add(_stationItem);
        _menu.Add(_trackItem);
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(_playItem);
        _menu.Add(stop);
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(show);
        _menu.Add(sleep);
        _menu.Add(updatesItem);
        _menu.Add(history);
        _menu.Add(settings);
        _menu.Add(new NativeMenuItemSeparator());
        _menu.Add(quitItem);

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://RadioWindows/Assets/app.ico"))),
            ToolTipText = Loc.T("Tray.Tooltip"),
            Menu = _menu,
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => ToggleMiniPlayer();

        _services.Player.PropertyChanged += (_, _) => Refresh();
        _services.Sleep.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public void ShowMiniPlayer()
    {
        EnsureMini();
        if (_mini is null)
        {
            return;
        }

        if (_mini.IsVisible)
        {
            _mini.Hide();
            return;
        }

        PositionMiniPlayer(_mini);
        _mini.Show();
        _mini.Activate();
    }

    public void ToggleMiniPlayer()
    {
        EnsureMini();
        if (_mini is not { } window)
        {
            return;
        }

        if (window.IsVisible)
        {
            window.Hide();
        }
        else
        {
            PositionMiniPlayer(window);
            window.Show();
            window.Activate();
        }
    }

    private void EnsureMini()
    {
        if (_mini is not null || Application.Current?.ApplicationLifetime is null)
        {
            return;
        }

        _miniModel = new MiniPlayerViewModel(_services, _shell);
        _mini = new MiniPlayerWindow { DataContext = _miniModel };
        _miniModel.OpenWindowRequested += () =>
        {
            _mini?.Hide();
            _showWindow();
        };
    }

    private static void PositionMiniPlayer(Window window)
    {
        var screen = window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        var width = (int)(320 * scaling);
        var height = (int)(430 * scaling);
        window.Position = new PixelPoint(
            Math.Max(area.X + 8, area.Right - width - (int)(12 * scaling)),
            Math.Max(area.Y + 8, area.Bottom - height - (int)(12 * scaling)));
    }

    private void TogglePlayPause()
    {
        if (_services.Player.Station is null)
        {
            _shell.Main.TogglePlayPauseCommand.Execute(null);
            return;
        }

        _services.Player.TogglePlayPause();
    }

    private void Refresh()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
            return;
        }

        var player = _services.Player;
        _playItem.Header = player.IsPlaying ? Loc.T("Player.Pause") : Loc.T("Player.Play");
        _stationItem.Header = player.Station?.Name ?? Loc.T("Tray.Tooltip");
        _stationItem.IsVisible = !string.IsNullOrEmpty(player.Station?.Name);
        _trackItem.Header = player.NowPlaying?.TrayLine ?? string.Empty;
        _trackItem.IsVisible = !string.IsNullOrEmpty(player.NowPlaying?.TrayLine);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(player.Station?.Name))
        {
            parts.Add(player.Station!.Name);
        }

        if (!string.IsNullOrEmpty(player.NowPlaying?.TrayLine))
        {
            parts.Add(player.NowPlaying!.TrayLine);
        }

        var body = parts.Count > 0 ? string.Join(" · ", parts) : Loc.T("Tray.Tooltip");
        if (_services.Sleep.IsActive)
        {
            body += " · " + Loc.Format("Player.SleepActive", _services.Sleep.RemainingText);
        }

        if (body.Length > 90)
        {
            body = body[..89] + "…";
        }

        _tray.ToolTipText = body;
    }

    public void Dispose()
    {
        _mini?.Close();
        _miniModel?.Dispose();
        _tray.Dispose();
    }
}
