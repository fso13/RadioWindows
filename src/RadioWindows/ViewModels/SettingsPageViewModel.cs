using System.Collections.ObjectModel;
using System.Reflection;
using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

public sealed class ThemeOptionViewModel : ObservableObject
{
    private readonly SettingsPageViewModel _owner;

    public ThemeOptionViewModel(ThemePreference preference, string label, string detail, SettingsPageViewModel owner)
    {
        Preference = preference;
        Label = label;
        Detail = detail;
        _owner = owner;
        Command = new RelayCommand(() => owner.SetTheme(preference));
    }

    public ThemePreference Preference { get; }

    public string Label { get; }

    public string Detail { get; }

    public RelayCommand Command { get; }

    public bool IsSelected => _owner.Theme == Preference;

    public void Refresh() => Raise(nameof(IsSelected));
}

public sealed class HotKeyKeyOption
{
    public HotKeyKeyOption(HotKeyKey key, string label)
    {
        Key = key;
        Label = label;
    }

    public HotKeyKey Key { get; }

    public string Label { get; }
}

public sealed class HotKeyModifierOption
{
    public HotKeyModifierOption(HotKeyModifiers modifiers, string label)
    {
        Modifiers = modifiers;
        Label = label;
    }

    public HotKeyModifiers Modifiers { get; }

    public string Label { get; }
}

public sealed class InfoRowViewModel
{
    public InfoRowViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }

    public string Value { get; }
}

public sealed class AlarmWeekdayViewModel : ObservableObject
{
    private readonly SettingsPageViewModel _owner;

    public AlarmWeekdayViewModel(int weekday, SettingsPageViewModel owner)
    {
        Weekday = weekday;
        _owner = owner;
        Command = new RelayCommand(() => owner.ToggleWeekday(weekday));
    }

    public int Weekday { get; }

    public string Label => Loc.T(AlarmSettings.ShortLabelKey(Weekday));

    public string FullLabel => Loc.T(AlarmSettings.FullLabelKey(Weekday));

    public RelayCommand Command { get; }

    public bool IsSelected => _owner.AlarmSettings.Weekdays.Contains(Weekday);

    public bool IsEnabled => _owner.AlarmSettings.Enabled;

    public void Refresh()
    {
        Raise(nameof(IsSelected));
        Raise(nameof(IsEnabled));
    }
}

public sealed class AlarmStationOption
{
    public AlarmStationOption(string? id, string name)
    {
        Id = id;
        Name = name;
    }

    public string? Id { get; }

    public string Name { get; }
}

/// <summary>Settings page: appearance, behaviour, sound, library, help and about.</summary>
public sealed class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly HelpViewModel _help = new();
    private bool _isHelpVisible;
    private string _alarmNextText = string.Empty;
    private string _lastFmStatus = string.Empty;
    private string _updateStatus = string.Empty;
    private bool _isCheckingUpdates;
    private bool _isAutostartOn;
    private bool _isLastFmEnabled;
    private bool _isDiagnosticsEnabled;
    private string _selectedHour = "07";
    private string _selectedMinute = "30";
    private AlarmStationOption? _selectedAlarmStation;

    public SettingsPageViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;

        ThemeOptions =
        [
            new ThemeOptionViewModel(ThemePreference.System, Loc.T("Settings.ThemeSystem"), Loc.T("Settings.ThemeSystemDetail"), this),
            new ThemeOptionViewModel(ThemePreference.Light, Loc.T("Settings.ThemeLight"), Loc.T("Settings.ThemeLightDetail"), this),
            new ThemeOptionViewModel(ThemePreference.Dark, Loc.T("Settings.ThemeDark"), Loc.T("Settings.ThemeDarkDetail"), this),
        ];

        HotKeyKeys =
        [
            new HotKeyKeyOption(HotKeyKey.P, "P"),
            new HotKeyKeyOption(HotKeyKey.N, "N"),
            new HotKeyKeyOption(HotKeyKey.Space, "Space"),
            new HotKeyKeyOption(HotKeyKey.Left, "←"),
            new HotKeyKeyOption(HotKeyKey.Right, "→"),
            new HotKeyKeyOption(HotKeyKey.F8, "F8"),
            new HotKeyKeyOption(HotKeyKey.F9, "F9"),
        ];
        HotKeyModifiersList =
        [
            new HotKeyModifierOption(HotKeyModifiers.Control | HotKeyModifiers.Alt, "Ctrl + Alt"),
            new HotKeyModifierOption(HotKeyModifiers.Control | HotKeyModifiers.Shift, "Ctrl + Shift"),
            new HotKeyModifierOption(HotKeyModifiers.Alt | HotKeyModifiers.Shift, "Alt + Shift"),
            new HotKeyModifierOption(HotKeyModifiers.None, Loc.T("Settings.HotkeyNone")),
        ];

        foreach (var weekday in AlarmSettings.WeekdayOrder)
        {
            Weekdays.Add(new AlarmWeekdayViewModel(weekday, this));
        }

        Hours = Enumerable.Range(0, 24).Select(hour => hour.ToString("00")).ToList();
        Minutes = Enumerable.Range(0, 60).Select(minute => minute.ToString("00")).ToList();
        _selectedHour = AlarmSettings.Hour.ToString("00");
        _selectedMinute = AlarmSettings.Minute.ToString("00");
        RebuildAlarmStations();
        _selectedAlarmStation = AlarmStations.FirstOrDefault(option => option.Id == AlarmSettings.StationId) ?? AlarmStations[0];
        _isAutostartOn = LaunchAtLogin.IsEnabled;
        _isLastFmEnabled = services.LastFm.IsEnabled;
        _isDiagnosticsEnabled = services.Diagnostics.IsEnabled;
        _lastFmStatus = ComputeLastFmStatus();
        _updateStatus = string.Empty;

        BackCommand = new RelayCommand(GoBack);
        CloseCommand = new RelayCommand(() => _shell.RequestHide());
        QuitCommand = new RelayCommand(() => _shell.ShowModal(new ConfirmDialogViewModel(
            Loc.T("Settings.QuitTitle"),
            Loc.T("Settings.QuitMessage"),
            Loc.T("Settings.QuitConfirm"),
            isDestructive: true,
            () => _shell.RequestQuit(),
            _shell)));
        OpenEqualizerCommand = new RelayCommand(() => _shell.ShowModal(new EqualizerViewModel(_services, _shell)));
        OpenHistoryCommand = new RelayCommand(() => _shell.ShowModal(new HistoryViewModel(_services, _shell)));
        OpenDiagnosticsCommand = new RelayCommand(() => _shell.ShowModal(new DiagnosticsViewModel(_services, _shell)));
        OpenLastFmCommand = new RelayCommand(() => _shell.ShowModal(new LastFmSetupViewModel(_services, _shell)));
        OpenHelpCommand = new RelayCommand(() =>
        {
            IsHelpVisible = true;
            _help.Expand("basics");
        });
        RevealDiagnosticsCommand = new RelayCommand(() => ShellOpen.Reveal(_services.Diagnostics.HasLog
            ? _services.Diagnostics.LogPath
            : _services.Paths.LogDirectory));
        CopyDiagnosticsCommand = new RelayCommand(async () => await CopyDiagnosticsAsync());
        RefreshStreamCommand = new RelayCommand(() => _services.Player.RefreshStreamInfo());
        CheckUpdatesCommand = new RelayCommand(async () => await CheckUpdatesAsync());
        OpenUpdateCommand = new RelayCommand(OpenUpdate);
        RestoreLibraryCommand = new RelayCommand(() => _shell.ShowModal(new ConfirmDialogViewModel(
            Loc.T("Settings.LibraryRestore"),
            Loc.T("Settings.LibraryRestoreMessage"),
            Loc.T("Settings.LibraryRestoreConfirm"),
            isDestructive: true,
            () =>
            {
                _services.Library.RestoreDefaults();
                _services.Toasts.ShowSuccess(Loc.T("Toast.Restored"));
            },
            _shell)));
        SleepStartCommand = new RelayCommand(parameter =>
        {
            if (parameter is int minutes)
            {
                _services.Sleep.Start(minutes);
            }
            else if (parameter is string text && int.TryParse(text, out var parsed))
            {
                _services.Sleep.Start(parsed);
            }
        });
        SleepCancelCommand = new RelayCommand(_services.Sleep.Cancel);

        _services.Theme.PropertyChanged += OnThemeChanged;
        _services.Sleep.PropertyChanged += OnSleepChanged;
        _services.History.PropertyChanged += OnHistoryChanged;
        _services.Alarm.PropertyChanged += OnAlarmChanged;
        _services.Player.PropertyChanged += OnPlayerChanged;
        _services.LastFm.PropertyChanged += OnLastFmChanged;
        _services.Diagnostics.PropertyChanged += OnDiagnosticsChanged;
        _services.Library.Changed += OnLibraryChanged;

        RaiseAlarm();
        RaiseSleep();
        RaiseStream();
    }

    public AppServices Services => _services;

    public void Dispose()
    {
        _services.Theme.PropertyChanged -= OnThemeChanged;
        _services.Sleep.PropertyChanged -= OnSleepChanged;
        _services.History.PropertyChanged -= OnHistoryChanged;
        _services.Alarm.PropertyChanged -= OnAlarmChanged;
        _services.Player.PropertyChanged -= OnPlayerChanged;
        _services.LastFm.PropertyChanged -= OnLastFmChanged;
        _services.Diagnostics.PropertyChanged -= OnDiagnosticsChanged;
        _services.Library.Changed -= OnLibraryChanged;
    }

    private void OnThemeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Raise(nameof(Theme));
        Raise(nameof(ThemeDetail));
        foreach (var option in ThemeOptions)
        {
            option.Refresh();
        }
    }

    private void OnSleepChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseSleep();

    private void OnHistoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        Raise(nameof(HistoryCountText));

    private void OnAlarmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseAlarm();

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseStream();

    private void OnLastFmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _isLastFmEnabled = _services.LastFm.IsEnabled;
        Raise(nameof(IsLastFmEnabled));
        Raise(nameof(LastFmStatus));
        Raise(nameof(LastFmConnected));
    }

    private void OnDiagnosticsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _isDiagnosticsEnabled = _services.Diagnostics.IsEnabled;
        Raise(nameof(IsDiagnosticsEnabled));
        Raise(nameof(DiagnosticsSubtitle));
        Raise(nameof(HasCrash));
        Raise(nameof(CrashText));
    }

    private void OnLibraryChanged()
    {
        RebuildAlarmStations();
        Raise(nameof(LibraryCountText));
        Raise(nameof(StationCountText));
    }

    public string Title => Loc.T("Settings.Title");

    public string Subtitle => Loc.T("Settings.Subtitle");

    // MARK: - Appearance

    public IReadOnlyList<ThemeOptionViewModel> ThemeOptions { get; }

    public ThemePreference Theme => _services.Theme.Preference;

    public string ThemeDetail => Theme switch
    {
        ThemePreference.Light => Loc.T("Settings.ThemeLightDetail"),
        ThemePreference.Dark => Loc.T("Settings.ThemeDarkDetail"),
        _ => Loc.T("Settings.ThemeSystemDetail"),
    };

    public void SetTheme(ThemePreference preference)
    {
        _services.Theme.SetPreference(preference);
        Raise(nameof(ThemeDetail));
        foreach (var option in ThemeOptions)
        {
            option.Refresh();
        }
    }

    // MARK: - Sleep

    public IReadOnlyList<int> SleepOptions => _services.Sleep.Options;

    public bool SleepActive => _services.Sleep.IsActive;

    public string SleepRemaining => _services.Sleep.RemainingText;

    public string SleepRowTitle => SleepActive
        ? Loc.Format("Settings.SleepRemaining", SleepRemaining)
        : Loc.T("Settings.SleepOff");

    public string SleepOptionLabel(int minutes) => Loc.Format("Settings.SleepStart", minutes);

    private void RaiseSleep()
    {
        Raise(nameof(SleepActive));
        Raise(nameof(SleepRemaining));
        Raise(nameof(SleepRowTitle));
    }

    // MARK: - History

    public string HistoryCountText => Loc.Format("Settings.HistoryCount", _services.History.Entries.Count);

    // MARK: - Hot keys

    public IReadOnlyList<HotKeyKeyOption> HotKeyKeys { get; }

    public IReadOnlyList<HotKeyModifierOption> HotKeyModifiersList { get; }

    public HotKeyKeyOption SelectedPlayPauseKey
    {
        get => HotKeyKeys.First(option => option.Key == _services.HotKeys.PlayPause.Key);
        set
        {
            if (value is not null && value.Key != _services.HotKeys.PlayPause.Key)
            {
                _services.HotKeys.SetPlayPause(_services.HotKeys.PlayPause with { Key = value.Key });
                RaiseHotKeys();
            }
        }
    }

    public HotKeyModifierOption SelectedPlayPauseModifiers
    {
        get => HotKeyModifiersList.First(option => option.Modifiers == _services.HotKeys.PlayPause.Modifiers);
        set
        {
            if (value is not null && value.Modifiers != _services.HotKeys.PlayPause.Modifiers)
            {
                _services.HotKeys.SetPlayPause(_services.HotKeys.PlayPause with { Modifiers = value.Modifiers });
                RaiseHotKeys();
            }
        }
    }

    public HotKeyKeyOption SelectedNextKey
    {
        get => HotKeyKeys.First(option => option.Key == _services.HotKeys.NextStation.Key);
        set
        {
            if (value is not null && value.Key != _services.HotKeys.NextStation.Key)
            {
                _services.HotKeys.SetNext(_services.HotKeys.NextStation with { Key = value.Key });
                RaiseHotKeys();
            }
        }
    }

    public HotKeyModifierOption SelectedNextModifiers
    {
        get => HotKeyModifiersList.First(option => option.Modifiers == _services.HotKeys.NextStation.Modifiers);
        set
        {
            if (value is not null && value.Modifiers != _services.HotKeys.NextStation.Modifiers)
            {
                _services.HotKeys.SetNext(_services.HotKeys.NextStation with { Modifiers = value.Modifiers });
                RaiseHotKeys();
            }
        }
    }

    public string HotKeyError => _services.HotKeys.ErrorText ?? string.Empty;

    public bool HasHotKeyError => HotKeyError.Length > 0;

    private void RaiseHotKeys()
    {
        Raise(nameof(SelectedPlayPauseKey));
        Raise(nameof(SelectedPlayPauseModifiers));
        Raise(nameof(SelectedNextKey));
        Raise(nameof(SelectedNextModifiers));
        Raise(nameof(HotKeyError));
        Raise(nameof(HasHotKeyError));
    }

    // MARK: - Behaviour

    public bool ResumeLastStation
    {
        get => _services.Settings.ResumeLastStation;
        set
        {
            _services.Settings.SetResumeLastStation(value);
            Raise();
        }
    }

    public bool IsAutostartOn
    {
        get => _isAutostartOn;
        set
        {
            if (_isAutostartOn == value)
            {
                return;
            }

            var result = LaunchAtLogin.Set(value);
            if (!result.Ok)
            {
                _services.Toasts.ShowError(result.Error ?? Loc.T("Toast.AutoStartFailed"));
                Raise();
                return;
            }

            _isAutostartOn = result.Enabled;
            _services.Settings.SetAutoStartEnabled(result.Enabled);
            Raise();
        }
    }

    public bool AutostartSupported => LaunchAtLogin.IsSupported;

    public string AutostartSubtitle => AutostartSupported
        ? Loc.T("Settings.AutostartSubtitle")
        : Loc.T("Settings.AutostartUnsupported");

    // MARK: - Alarm

    public AlarmSettings AlarmSettings => _services.Alarm.Settings;

    public AlarmWeekdayViewModel[] WeekdayItems => Weekdays.ToArray();

    public ObservableCollection<AlarmWeekdayViewModel> Weekdays { get; } = [];

    public ObservableCollection<AlarmStationOption> AlarmStations { get; } = [];

    public IReadOnlyList<string> Hours { get; }

    public IReadOnlyList<string> Minutes { get; }

    public string SelectedHour
    {
        get => _selectedHour;
        set
        {
            if (string.IsNullOrEmpty(value) || _selectedHour == value)
            {
                return;
            }

            _selectedHour = value;
            ApplyAlarmTime();
            Raise();
        }
    }

    public string SelectedMinute
    {
        get => _selectedMinute;
        set
        {
            if (string.IsNullOrEmpty(value) || _selectedMinute == value)
            {
                return;
            }

            _selectedMinute = value;
            ApplyAlarmTime();
            Raise();
        }
    }

    public AlarmStationOption? SelectedAlarmStation
    {
        get => _selectedAlarmStation;
        set
        {
            if (value is null || ReferenceEquals(_selectedAlarmStation, value))
            {
                return;
            }

            _selectedAlarmStation = value;
            _services.Alarm.SetStation(value.Id);
            Raise();
        }
    }

    public bool AlarmEnabled
    {
        get => AlarmSettings.Enabled;
        set
        {
            if (AlarmSettings.Enabled == value)
            {
                return;
            }

            _services.Alarm.SetEnabled(value);
            RaiseAlarm();
        }
    }

    public string AlarmSubtitle
    {
        get
        {
            if (!AlarmSettings.Enabled)
            {
                return Loc.T("Settings.AlarmOffSubtitle");
            }

            return AlarmSettings.Weekdays.Count == 0
                ? Loc.T("Settings.AlarmNoDays")
                : Loc.Format("Settings.AlarmFadeSubtitle", AlarmSettings.FadeInSeconds);
        }
    }

    public string AlarmNextText => _alarmNextText;

    public bool AlarmHasNext => _services.Alarm.NextFireDate is not null;

    public string AlarmNote => Loc.T("Settings.AlarmNote");

    public string AlarmTimeLabel => Loc.T("Settings.AlarmTime");

    public string AlarmDaysLabel => Loc.T("Settings.AlarmDays");

    public string AlarmStationLabel => Loc.T("Settings.AlarmStation");

    public string AlarmNextLabel => Loc.T("Settings.AlarmNext");

    private void ApplyAlarmTime()
    {
        if (int.TryParse(_selectedHour, out var hour) && int.TryParse(_selectedMinute, out var minute))
        {
            _services.Alarm.SetTime(hour, minute);
            RaiseAlarm();
        }
    }

    internal void ToggleWeekday(int weekday)
    {
        _services.Alarm.ToggleWeekday(weekday);
        foreach (var item in Weekdays)
        {
            item.Refresh();
        }

        RaiseAlarm();
    }

    private void RebuildAlarmStations()
    {
        AlarmStations.Clear();
        AlarmStations.Add(new AlarmStationOption(null, Loc.T("Settings.AlarmLastPlayed")));
        foreach (var station in _services.Library.Stations)
        {
            AlarmStations.Add(new AlarmStationOption(station.Id, station.Name));
        }

        Raise(nameof(AlarmStations));
    }

    private void RaiseAlarm()
    {
        _selectedHour = AlarmSettings.Hour.ToString("00");
        _selectedMinute = AlarmSettings.Minute.ToString("00");
        _alarmNextText = FormatNextFire();
        foreach (var item in Weekdays)
        {
            item.Refresh();
        }

        Raise(nameof(AlarmEnabled));
        Raise(nameof(AlarmSubtitle));
        Raise(nameof(AlarmNextText));
        Raise(nameof(AlarmHasNext));
        Raise(nameof(SelectedHour));
        Raise(nameof(SelectedMinute));
        Raise(nameof(WeekdayItems));
        Raise(nameof(AlarmStations));
    }

    private string FormatNextFire()
    {
        if (!AlarmSettings.Enabled)
        {
            return Loc.T("Settings.AlarmNextOff");
        }

        if (AlarmSettings.Weekdays.Count == 0)
        {
            return Loc.T("Settings.AlarmPickDays");
        }

        if (_services.Alarm.NextFireDate is not { } next)
        {
            return Loc.T("Settings.AlarmNoNext");
        }

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        return next.LocalDateTime.ToString("dddd, d MMMM, HH:mm", culture);
    }

    // MARK: - Sound

    public string EqualizerSubtitle => _services.Equalizer.Enabled
        ? Loc.Format("Eq.On", Loc.T(_services.Equalizer.PresetLabelKey))
        : Loc.T("Eq.Off");

    // MARK: - Stream

    public ObservableCollection<InfoRowViewModel> StreamRows { get; } = [];

    public string PlayerStatusText => _services.Player.Status switch
    {
        PlaybackState.Idle => Loc.T("Status.Idle"),
        PlaybackState.Loading => Loc.T("Status.Loading"),
        PlaybackState.Playing => Loc.T("Status.Playing"),
        PlaybackState.Paused => Loc.T("Status.Paused"),
        PlaybackState.Reconnecting => Loc.Format("Status.Reconnecting", _services.Player.ReconnectAttempts, _services.Player.MaxReconnectAttempts),
        _ => Loc.T("Status.Error"),
    };

    public string StreamStationText => _services.Player.Station?.Name ?? Loc.T("Stream.NoStation");

    public string StreamEngineText => _services.Player.ActiveEngineName;

    private void RaiseStream()
    {
        StreamRows.Clear();
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamStation"), StreamStationText));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamStatus"), PlayerStatusText));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamEngine"), StreamEngineText));
        var info = _services.Player.StreamInfo;
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamCodec"), info?.Codec ?? "—"));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamBitrate"), FormatBitrate(info)));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamFrequency"), FormatFrequency(info)));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamChannels"), FormatChannels(info)));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamBuffer"), FormatBuffer(info)));
        StreamRows.Add(new InfoRowViewModel(Loc.T("Settings.StreamServer"), FormatServer(info)));
        Raise(nameof(StreamRows));
    }

    private static string FormatBitrate(StreamInfo? info) =>
        info?.BitrateKbps is { } bitrate and > 0 ? Loc.Format("Unit.Kbps", bitrate) : "—";

    private static string FormatFrequency(StreamInfo? info) =>
        info is { Frequency: > 0 } ? Loc.Format("Unit.Hz", info.Frequency) : "—";

    private static string FormatChannels(StreamInfo? info) => info?.Channels switch
    {
        null or 0 => "—",
        1 => Loc.T("Channels.Mono"),
        2 => Loc.T("Channels.Stereo"),
        var channels => Loc.Format("Channels.Many", channels),
    };

    private static string FormatBuffer(StreamInfo? info) =>
        info is { BufferMilliseconds: > 0 } ? Loc.Format("Unit.Ms", info.BufferMilliseconds) : "—";

    private string FormatServer(StreamInfo? info)
    {
        if (!string.IsNullOrEmpty(info?.Server))
        {
            return info.Server!;
        }

        return _services.Player.Station is { } station
               && Uri.TryCreate(station.Url, UriKind.Absolute, out var uri)
            ? uri.Host
            : "—";
    }

    // MARK: - Last.fm

    public bool LastFmKeysConfigured => _services.LastFm.KeysConfigured;

    public bool IsLastFmEnabled
    {
        get => _isLastFmEnabled;
        set
        {
            if (_isLastFmEnabled == value)
            {
                return;
            }

            _isLastFmEnabled = value;
            Raise();
            _ = ApplyLastFmAsync(value);
        }
    }

    public bool LastFmConnected => _services.LastFm.IsConnected;

    public string LastFmStatus => _lastFmStatus;

    public string LastFmNote => Loc.T("Settings.LastFmNote");

    private async Task ApplyLastFmAsync(bool enabled)
    {
        await _services.LastFm.SetEnabledAsync(enabled);
        _lastFmStatus = ComputeLastFmStatus();
        Raise(nameof(LastFmStatus));
        Raise(nameof(LastFmConnected));
        if (enabled && !_services.LastFm.IsConnected)
        {
            _shell.ShowModal(new LastFmSetupViewModel(_services, _shell));
        }
    }

    private string ComputeLastFmStatus()
    {
        if (_services.LastFm.IsConnected && _services.LastFm.Username is { } username)
        {
            return Loc.Format("Settings.LastFmConnected", username);
        }

        return _services.LastFm.IsEnabled
            ? Loc.T("Settings.LastFmNeedsConnect")
            : Loc.T("Settings.LastFmOff");
    }

    // MARK: - Updates

    public string VersionText => AppInfo.Version;

    public string BuildText => AppInfo.Build;

    public string UpdatesSubtitle => Loc.T("Settings.CheckUpdatesSubtitle");

    public string UpdatesTitle => $"RadioWindows {VersionText}";

    public string UpdateStatus => _updateStatus;

    public bool UpdateAvailable => _services.Updates.Latest is { } latest
                                   && LatestIsNewer(latest.Version);

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set => Set(ref _isCheckingUpdates, value);
    }

    public RelayCommand CheckUpdatesCommand { get; }

    public RelayCommand OpenUpdateCommand { get; }

    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        _updateStatus = Loc.T("Updates.Checking");
        Raise(nameof(UpdateStatus));
        var info = await _services.Updates.CheckAsync(CancellationToken.None);
        IsCheckingUpdates = false;
        _updateStatus = info is null || !LatestIsNewer(info.Version)
            ? Loc.T("Updates.UpToDate")
            : Loc.Format("Updates.Available", info.Version);
        Raise(nameof(UpdateStatus));
        Raise(nameof(UpdateAvailable));
        if (info is not null && LatestIsNewer(info.Version))
        {
            _services.Toasts.ShowSuccess(_updateStatus);
        }
        else
        {
            _services.Toasts.Show(_updateStatus);
        }
    }

    private static bool LatestIsNewer(string version) =>
        Version.TryParse(version.TrimStart('v'), out var latest)
        && Version.TryParse(AppInfo.Version, out var current)
        && latest > current;

    public void OpenUpdate()
    {
        if (_services.Updates.Latest is { } info)
        {
            _services.Updates.OpenDownload(info);
        }
    }

    // MARK: - Diagnostics

    public bool IsDiagnosticsEnabled
    {
        get => _isDiagnosticsEnabled;
        set
        {
            if (_isDiagnosticsEnabled == value)
            {
                return;
            }

            _isDiagnosticsEnabled = value;
            _services.Diagnostics.SetEnabled(value);
            Raise();
            Raise(nameof(DiagnosticsSubtitle));
        }
    }

    public string DiagnosticsSubtitle => _isDiagnosticsEnabled
        ? Loc.T("Settings.DiagnosticsOn")
        : Loc.T("Settings.DiagnosticsOff");

    public bool HasCrash => _services.Diagnostics.LastCrashDate is not null;

    public string CrashText => _services.Diagnostics.LastCrashDate is { } crash
        ? Loc.Format("Settings.DiagnosticsCrash", crash.LocalDateTime.ToString("d MMMM, HH:mm"))
        : string.Empty;

    // MARK: - Library / About

    public string LibraryCountText => Loc.Format("Settings.LibraryCount", _services.Library.Stations.Count);

    public string StationCountText => _services.Library.Stations.Count.ToString();

    public string AboutLine => Loc.T("Settings.AboutLine");

    public RelayCommand RestoreLibraryCommand { get; }

    // MARK: - Navigation

    public HelpViewModel Help => _help;

    public bool IsHelpVisible
    {
        get => _isHelpVisible;
        private set
        {
            if (Set(ref _isHelpVisible, value))
            {
                Raise(nameof(IsSettingsContentVisible));
            }
        }
    }

    public bool IsSettingsContentVisible => !_isHelpVisible;

    public void GoBack()
    {
        if (_isHelpVisible)
        {
            IsHelpVisible = false;
            return;
        }

        _shell.ShowMain();
    }

    // MARK: - Commands

    public RelayCommand BackCommand { get; }

    public RelayCommand CloseCommand { get; }

    public RelayCommand QuitCommand { get; }

    public RelayCommand OpenEqualizerCommand { get; }

    public RelayCommand OpenHistoryCommand { get; }

    public RelayCommand OpenDiagnosticsCommand { get; }

    public RelayCommand OpenLastFmCommand { get; }

    public RelayCommand OpenHelpCommand { get; }

    public RelayCommand RevealDiagnosticsCommand { get; }

    public RelayCommand CopyDiagnosticsCommand { get; }

    public RelayCommand RefreshStreamCommand { get; }

    public RelayCommand SleepStartCommand { get; }

    public RelayCommand SleepCancelCommand { get; }

    private async Task CopyDiagnosticsAsync()
    {
        var info = _services.Player.StreamInfo;
        var station = _services.Player.Station;
        var lines = new List<string>
        {
            $"RadioWindows {VersionText} ({BuildText})",
            $"Windows {Environment.OSVersion.VersionString}",
            $"Дата: {DateTimeOffset.Now:O}",
            string.Empty,
            $"Станция: {station?.Name ?? "—"}",
            $"URL: {station?.Url ?? "—"}",
            $"Сервер: {FormatServer(info)}",
            $"Статус: {PlayerStatusText}",
            $"Кодек: {info?.Codec ?? "—"}",
            $"Битрейт: {FormatBitrate(info)}",
            $"Частота: {FormatFrequency(info)}",
            $"Каналы: {FormatChannels(info)}",
            $"Буфер: {FormatBuffer(info)}",
            $"Эквалайзер: {EqualizerSubtitle}",
            $"Ошибка: {_services.Player.ErrorMessage ?? "—"}",
        };

        var copied = await _services.Clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
        if (copied)
        {
            _services.Toasts.ShowSuccess(Loc.T("Toast.Copied"));
        }
        else
        {
            _services.Toasts.ShowError(Loc.T("Toast.CopyFailed"));
        }
    }
}

/// <summary>Version/build strings from the entry assembly.</summary>
public static class AppInfo
{
    public static string Version
    {
        get
        {
            var informational = typeof(AppInfo).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                var plus = informational.IndexOf('+');
                return plus > 0 ? informational[..plus] : informational;
            }

            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        }
    }

    public static string Build => typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "1";
}
