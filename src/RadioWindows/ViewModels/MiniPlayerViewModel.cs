using System.Collections.ObjectModel;
using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>Tray popover: artwork, transport, volume and quick station pick.</summary>
public sealed class MiniPlayerViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;

    public MiniPlayerViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;

        TogglePlayPauseCommand = new RelayCommand(() =>
        {
            if (_services.Player.Station is null)
            {
                shell.Main.TogglePlayPauseCommand.Execute(null);
                return;
            }

            _services.Player.TogglePlayPause();
        });
        StopCommand = new RelayCommand(() => _services.Player.Stop());
        OpenWindowCommand = new RelayCommand(() => OpenWindowRequested?.Invoke());
        SelectStationCommand = new RelayCommand(parameter =>
        {
            if (parameter is RadioStation station)
            {
                _services.Player.Play(station);
            }
            else if (parameter is int index && index >= 0 && index < QuickStations.Count)
            {
                _services.Player.Play(QuickStations[index]);
            }
        });

        _services.Player.PropertyChanged += OnPlayerChanged;
        _services.Library.Changed += RebuildQuickStations;
        _services.Sleep.PropertyChanged += OnSleepChanged;
        RebuildQuickStations();
        Refresh();
    }

    public string NowLabel => Loc.T("Mini.Now");

    public string StatusText => _services.Player.Status switch
    {
        PlaybackState.Playing => Loc.T("Player.Live"),
        PlaybackState.Loading => Loc.T("Status.Loading").ToUpperInvariant(),
        PlaybackState.Paused => Loc.T("Status.Paused").ToUpperInvariant(),
        PlaybackState.Reconnecting => Loc.T("Player.Reconnect"),
        PlaybackState.Error => Loc.T("Status.Error").ToUpperInvariant(),
        _ => Loc.T("Player.OffAir"),
    };

    public bool IsError => _services.Player.Status == PlaybackState.Error;

    public bool IsPlaying => _services.Player.IsPlaying;

    public bool IsLoading => _services.Player.Status is PlaybackState.Loading or PlaybackState.Reconnecting;

    public IconKind PlayPauseGlyph => IsPlaying ? IconKind.Pause : IconKind.Play;

    public string StationName => _services.Player.Station?.Name ?? Loc.T("Player.NoStation");

    public string TrackLine =>
        _services.Player.NowPlaying?.TrayLine
        ?? (_services.Player.IsActive ? Loc.T("Player.NoTrack") : Loc.T("Mini.SelectStation"));

    public string ArtworkUrl =>
        _services.Player.NowPlaying?.ArtworkUrl ?? _services.Player.Station?.LogoUrl ?? string.Empty;

    public double Volume
    {
        get => _services.Player.Volume;
        set => _services.Player.SetVolume(value);
    }

    public ObservableCollection<RadioStation> QuickStations { get; } = [];

    public int SelectedQuickIndex
    {
        get
        {
            for (var index = 0; index < QuickStations.Count; index++)
            {
                if (QuickStations[index].Id == _services.Player.Station?.Id)
                {
                    return index;
                }
            }

            return -1;
        }
        set
        {
            if (value >= 0 && value < QuickStations.Count
                && QuickStations[value].Id != _services.Player.Station?.Id)
            {
                _services.Player.Play(QuickStations[value]);
            }

            Raise();
        }
    }

    public string QuickPickLabel => Loc.T("Mini.QuickPick");

    public string OpenWindowLabel => Loc.T("Mini.OpenWindow");

    public string StopLabel => Loc.T("Player.Stop");

    public RelayCommand TogglePlayPauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand OpenWindowCommand { get; }

    public RelayCommand SelectStationCommand { get; }

    public event Action? OpenWindowRequested;

    private void RebuildQuickStations()
    {
        QuickStations.Clear();
        foreach (var station in _services.Library.Stations.Take(25))
        {
            QuickStations.Add(station);
        }

        Raise(nameof(SelectedQuickIndex));
    }

    private void Refresh()
    {
        Raise(nameof(StatusText));
        Raise(nameof(IsError));
        Raise(nameof(IsPlaying));
        Raise(nameof(IsLoading));
        Raise(nameof(PlayPauseGlyph));
        Raise(nameof(StationName));
        Raise(nameof(TrackLine));
        Raise(nameof(ArtworkUrl));
        Raise(nameof(Volume));
        Raise(nameof(SelectedQuickIndex));
    }

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    private void OnSleepChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Refresh();

    public void Dispose()
    {
        _services.Player.PropertyChanged -= OnPlayerChanged;
        _services.Library.Changed -= RebuildQuickStations;
        _services.Sleep.PropertyChanged -= OnSleepChanged;
    }
}
