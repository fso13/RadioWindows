using System.Collections.ObjectModel;
using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>The main page: filters, station table, keyboard selection and transport.</summary>
public sealed class MainPageViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;

    private string _searchText = string.Empty;
    private StationFilter _filter = StationFilter.All;
    private List<RadioStation> _visible = [];
    private int _selectedIndex;
    private bool _keyboardNavigated;
    private NowPlaying? _nowPlaying;

    public MainPageViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;

        AllItem = new NavItemViewModel(Loc.T("Nav.All"), StationFilter.All, this) { IsSelected = true };
        FavoritesItem = new NavItemViewModel(Loc.T("Nav.Favorites"), StationFilter.Favorites, this);
        PrimaryItems.Add(AllItem);
        PrimaryItems.Add(FavoritesItem);

        PlaySelectedCommand = new RelayCommand(PlaySelected);
        AddStationCommand = new RelayCommand(NewStation);
        OpenBrowserCommand = new RelayCommand(() => _shell.ShowModal(new BrowserViewModel(_services, _shell)));
        OpenTransferCommand = new RelayCommand(() => _shell.ShowModal(new TransferViewModel(_services, _shell)));
        OpenEqualizerCommand = new RelayCommand(() => _shell.ShowModal(new EqualizerViewModel(_services, _shell)));
        TogglePlayPauseCommand = new RelayCommand(TogglePlayPause);
        StopCommand = new RelayCommand(() => _services.Player.Stop());
        PreviousCommand = new RelayCommand(() => Move(-1), () => CanPrevious);
        NextCommand = new RelayCommand(() => Move(1), () => CanNext);
        RetryCommand = new RelayCommand(() => _services.Player.RetryNow());
        EmptyActionCommand = new RelayCommand(RunEmptyAction);
        SleepCommand = new RelayCommand(parameter =>
        {
            var minutes = parameter switch
            {
                int value => value,
                string text when int.TryParse(text, out var parsed) => parsed,
                _ => 0,
            };
            if (minutes > 0)
            {
                _services.Sleep.Start(minutes);
            }
        });
        SleepCancelCommand = new RelayCommand(_services.Sleep.Cancel);
        CloseCommand = new RelayCommand(() => _shell.RequestHide());

        _services.Library.Changed += OnLibraryChanged;
        _services.Player.PropertyChanged += (_, _) => OnPlayerChanged();
        _services.Sleep.PropertyChanged += (_, _) => RaiseSleep();
        _services.Equalizer.PropertyChanged += (_, _) =>
        {
            Raise(nameof(EqualizerEnabled));
            Raise(nameof(EqualizerSubtitle));
        };

        RebuildNav();
        Recompute(resetSelection: true);
        OnPlayerChanged();
    }

    public AppServices Services => _services;

    public RadioPlayerService Player => _services.Player;

    public StationLibrary Library => _services.Library;

    public ToastService Toasts => _services.Toasts;

    public NavItemViewModel AllItem { get; }

    public NavItemViewModel FavoritesItem { get; }

    public ObservableCollection<NavItemViewModel> PrimaryItems { get; } = [];

    public ObservableCollection<NavItemViewModel> GroupItems { get; } = [];

    public ObservableCollection<NavItemViewModel> GenreItems { get; } = [];

    public ObservableCollection<StationRowViewModel> Rows { get; } = [];

    public bool HasGroups => GroupItems.Count > 0;

    public string GroupsLabel => Loc.T("Nav.Groups");

    public string GenresLabel => Loc.T("Nav.Genres");

    public string SearchPlaceholder => Loc.T("Search.Placeholder");

    // MARK: - Search & filter

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value))
            {
                Recompute(resetSelection: true);
            }
        }
    }

    public StationFilter Filter => _filter;

    public bool IsSearchFocused
    {
        get => _isSearchFocused;
        set => Set(ref _isSearchFocused, value);
    }

    private bool _isSearchFocused;

    public bool HasQuery => !string.IsNullOrWhiteSpace(_searchText);

    public void SetFilter(StationFilter filter)
    {
        if (_filter == filter)
        {
            return;
        }

        _filter = filter;
        Raise(nameof(Filter));
        Recompute(resetSelection: true);
    }

    public void ClearSearch() => SearchText = string.Empty;

    public void Recompute(bool resetSelection)
    {
        var query = _searchText.Trim().ToLowerInvariant();
        _visible = _services.Library.Stations.Where(station =>
        {
            if (!_filter.Matches(station))
            {
                return false;
            }

            if (query.Length == 0)
            {
                return true;
            }

            return station.Name.ToLowerInvariant().Contains(query)
                   || station.Genre.ToLowerInvariant().Contains(query)
                   || station.Url.ToLowerInvariant().Contains(query)
                   || (station.Comment?.ToLowerInvariant().Contains(query) ?? false)
                   || (station.Group?.ToLowerInvariant().Contains(query) ?? false);
        }).ToList();

        if (resetSelection)
        {
            _selectedIndex = 0;
            _keyboardNavigated = false;
        }
        else if (_visible.Count > 0)
        {
            _selectedIndex = Math.Clamp(_selectedIndex, 0, _visible.Count - 1);
        }
        else
        {
            _selectedIndex = 0;
        }

        RebuildRows();
        Raise(nameof(IsEmpty));
        Raise(nameof(HasQuery));
        Raise(nameof(CanReorder));
        Raise(nameof(HasRows));
        Raise(nameof(VisibleCountText));
        RaiseEmptyState();
        Raise(nameof(CanPlay));
        Raise(nameof(CanPrevious));
        Raise(nameof(CanNext));
        UpdateNavSelection();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        for (var index = 0; index < _visible.Count; index++)
        {
            var row = new StationRowViewModel(_visible[index], index, this);
            row.IsSelected = index == _selectedIndex && _keyboardNavigated;
            Rows.Add(row);
        }

        UpdateRowState();
    }

    private void UpdateRowState()
    {
        var currentId = _services.Player.Station?.Id;
        var status = _services.Player.Status;
        foreach (var row in Rows)
        {
            row.IsCurrent = row.Station.Id == currentId;
            row.IsPlaying = row.IsCurrent && status == PlaybackState.Playing;
            row.IsLoading = row.IsCurrent && status is PlaybackState.Loading or PlaybackState.Reconnecting;
            row.IsSelected = row.Index == _selectedIndex && _keyboardNavigated;
            row.RefreshFavorite();
        }
    }

    /// <summary>Mirrors the macOS app: hiding the panel resets search and filter.</summary>
    public void ResetAfterHide()
    {
        _shell.CloseModal();
        _searchText = string.Empty;
        Raise(nameof(SearchText));
        _filter = StationFilter.All;
        Raise(nameof(Filter));
        _selectedIndex = 0;
        _keyboardNavigated = false;
        Recompute(resetSelection: true);
    }

    public bool IsEmpty => _visible.Count == 0;

    public bool HasRows => _visible.Count > 0;

    public string VisibleCountText => Loc.Format("Table.Count", _visible.Count);

    public bool CanReorder => _filter.Kind == StationFilterKind.All && string.IsNullOrWhiteSpace(_searchText);

    public int SelectedIndex => _selectedIndex;

    // MARK: - Navigation

    private void RebuildNav()
    {
        GroupItems.Clear();
        foreach (var group in _services.Library.Groups)
        {
            GroupItems.Add(new NavItemViewModel(group, StationFilter.Group(group), this));
        }

        GenreItems.Clear();
        foreach (var genre in _services.Library.Genres)
        {
            GenreItems.Add(new NavItemViewModel(genre, StationFilter.Genre(genre), this));
        }

        Raise(nameof(HasGroups));
        UpdateNavSelection();
    }

    private void UpdateNavSelection()
    {
        AllItem.IsSelected = _filter.Kind == StationFilterKind.All;
        AllItem.IsPlaying = _services.Player.IsPlaying;
        FavoritesItem.IsSelected = _filter.Kind == StationFilterKind.Favorites;
        FavoritesItem.IsPlaying = _services.Player.IsPlaying;
        foreach (var item in GroupItems.Concat(GenreItems))
        {
            item.IsSelected = item.Filter == _filter;
            item.IsPlaying = _services.Player.IsPlaying;
        }
    }

    private void OnLibraryChanged()
    {
        RebuildNav();
        Recompute(resetSelection: false);
        Raise(nameof(HasGroups));
    }

    // MARK: - Rows

    public void Play(StationRowViewModel row)
    {
        _selectedIndex = row.Index;
        _keyboardNavigated = false;
        _services.Player.Play(row.Station);
        UpdateRowState();
    }

    public void ToggleFavorite(StationRowViewModel row)
    {
        _services.Library.ToggleFavorite(row.Station.Id);
        row.RefreshFavorite();
        if (_filter.Kind == StationFilterKind.Favorites)
        {
            Recompute(resetSelection: false);
        }
    }

    public void Edit(StationRowViewModel row) => _shell.ShowModal(new StationEditorViewModel(_services, _shell, row.Station));

    public void NewStation() => _shell.ShowModal(new StationEditorViewModel(_services, _shell, null));

    public void AskDelete(StationRowViewModel row) =>
        _shell.ShowModal(new ConfirmDialogViewModel(
            Loc.T("Row.DeleteTitle"),
            Loc.Format("Row.DeleteMessage", row.Station.Name),
            Loc.T("Common.Delete"),
            isDestructive: true,
            () => Delete(row.Station)));

    private void Delete(RadioStation station)
    {
        if (_services.Player.Station?.Id == station.Id)
        {
            _services.Player.Stop();
        }

        _services.Library.Remove(station.Id);
        _services.Toasts.ShowSuccess(Loc.T("Toast.StationDeleted"));
    }

    // MARK: - Keyboard

    public void MoveSelection(int delta)
    {
        if (_visible.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _visible.Count - 1);
        _keyboardNavigated = true;
        UpdateRowState();
        SelectionChanged?.Invoke(_visible[_selectedIndex].Id);
    }

    public void PlaySelected()
    {
        if (_visible.Count == 0)
        {
            return;
        }

        _services.Player.Play(_visible[Math.Clamp(_selectedIndex, 0, _visible.Count - 1)]);
    }

    public void PlayVisible(int index)
    {
        if (index >= 0 && index < _visible.Count)
        {
            _selectedIndex = index;
            _keyboardNavigated = true;
            UpdateRowState();
            _services.Player.Play(_visible[index]);
        }
    }

    public event Action<string>? SelectionChanged;

    public event Action? FocusSearchRequested;

    public void RequestFocusSearch() => FocusSearchRequested?.Invoke();

    public event Action? ResignSearchRequested;

    public void ResignSearch()
    {
        IsSearchFocused = false;
        ResignSearchRequested?.Invoke();
    }

    // MARK: - Reordering

    public void MoveStation(string id, string targetId)
    {
        if (CanReorder)
        {
            _services.Library.Move(id, targetId);
        }
    }

    public void MoveStationToEnd(string id)
    {
        if (CanReorder)
        {
            _services.Library.MoveToEnd(id);
        }
    }

    // MARK: - Empty state

    public IconKind EmptyIcon =>
        HasQuery ? IconKind.Search
        : _filter.Kind == StationFilterKind.Favorites ? IconKind.Star
        : _filter.Kind == StationFilterKind.Group ? IconKind.Folder
        : IconKind.RadioWaves;

    public string EmptyTitle =>
        HasQuery ? Loc.T("Empty.SearchTitle")
        : _filter.Kind == StationFilterKind.Favorites ? Loc.T("Empty.FavoritesTitle")
        : _filter.Kind == StationFilterKind.Group ? Loc.T("Empty.GroupTitle")
        : Loc.T("Empty.AddTitle");

    public string EmptySubtitle =>
        HasQuery ? Loc.T("Empty.SearchSubtitle")
        : _filter.Kind == StationFilterKind.Favorites ? Loc.T("Empty.FavoritesSubtitle")
        : _filter.Kind == StationFilterKind.Group ? Loc.T("Empty.GroupSubtitle")
        : Loc.T("Empty.AddSubtitle");

    public string EmptyActionTitle =>
        HasQuery ? Loc.T("Empty.SearchAction")
        : _filter.Kind is StationFilterKind.Favorites or StationFilterKind.Group ? Loc.T("Empty.FavoritesAction")
        : Loc.T("Empty.AddAction");

    private void RunEmptyAction()
    {
        if (HasQuery)
        {
            ClearSearch();
            SetFilter(StationFilter.All);
        }
        else if (_filter.Kind is StationFilterKind.Favorites or StationFilterKind.Group)
        {
            SetFilter(StationFilter.All);
        }
        else
        {
            NewStation();
        }
    }

    private void RaiseEmptyState()
    {
        Raise(nameof(EmptyIcon));
        Raise(nameof(EmptyTitle));
        Raise(nameof(EmptySubtitle));
        Raise(nameof(EmptyActionTitle));
    }

    // MARK: - Player presentation

    public string PlayerTitle => _nowPlaying?.DisplayTitle ?? _services.Player.Station?.Name ?? Loc.T("Player.NoStation");

    public string PlayerSubtitle
    {
        get
        {
            var player = _services.Player;
            if (player.Status == PlaybackState.Reconnecting)
            {
                return player.ErrorMessage ?? Loc.T("Player.Loading");
            }

            if (player.Status == PlaybackState.Error)
            {
                return player.ErrorMessage ?? Loc.T("Player.OpenFailed");
            }

            if (_nowPlaying is not null)
            {
                var parts = new List<string>();
                if (_nowPlaying.DisplayArtist is { } artist)
                {
                    parts.Add(artist);
                }

                if (player.Station is { } station)
                {
                    parts.Add(station.Name);
                }

                return string.Join(" — ", parts);
            }

            if (player.Status == PlaybackState.Loading)
            {
                return Loc.T("Player.Loading");
            }

            if (player.IsActive)
            {
                return Loc.T("Player.NoTrack");
            }

            return player.Station?.Genre ?? Loc.T("Player.SelectStation");
        }
    }

    public string HeroArtworkUrl =>
        _nowPlaying?.ArtworkUrl ?? _services.Player.Station?.LogoUrl ?? string.Empty;

    public string HeroTooltip => _services.Player.Station is { } station
        ? $"{station.Name} — {Loc.T("Player.Pause")} / {Loc.T("Player.Play")}"
        : Loc.T("Player.NoStation");

    public bool IsPlaying => _services.Player.IsPlaying;

    public bool IsLoading => _services.Player.Status is PlaybackState.Loading or PlaybackState.Reconnecting;

    public bool IsError => _services.Player.Status == PlaybackState.Error;

    public bool IsReconnect => _services.Player.Status == PlaybackState.Reconnecting;

    public bool IsLive => _services.Player.IsPlaying;

    public IconKind PlayPauseGlyph => IsPlaying ? IconKind.Pause : IconKind.Play;

    public bool CanPlay => _services.Player.Station is not null || _services.Player.IsActive || _visible.Count > 0;

    public bool CanPrevious => CurrentIndex is { } index && index > 0;

    public bool CanNext => CurrentIndex is { } index && index < _visible.Count - 1;

    public double Volume
    {
        get => _services.Player.Volume;
        set => _services.Player.SetVolume(value);
    }

    public bool EqualizerEnabled => _services.Equalizer.Enabled;

    public string EqualizerSubtitle => _services.Equalizer.Enabled
        ? Loc.Format("Eq.On", Loc.T(_services.Equalizer.PresetLabelKey))
        : Loc.T("Eq.Off");

    public bool SleepActive => _services.Sleep.IsActive;

    public string SleepText => _services.Sleep.RemainingText;

    public string SleepTooltip => SleepActive
        ? Loc.Format("Player.SleepActive", SleepText)
        : Loc.T("Player.Sleep");

    public IReadOnlyList<int> SleepOptions => _services.Sleep.Options;

    private int? CurrentIndex
    {
        get
        {
            var id = _services.Player.Station?.Id;
            if (id is null)
            {
                return null;
            }

            for (var index = 0; index < _visible.Count; index++)
            {
                if (_visible[index].Id == id)
                {
                    return index;
                }
            }

            return null;
        }
    }

    /// <summary>Global-hotkey and tray entry point for "next station".</summary>
    public void PlayNextStation() => Move(1);

    public void PlayPreviousStation() => Move(-1);

    private void Move(int delta)
    {
        if (CurrentIndex is not { } index)
        {
            return;
        }

        var next = index + delta;
        if (next >= 0 && next < _visible.Count)
        {
            _services.Player.Play(_visible[next]);
        }
    }

    private void TogglePlayPause()
    {
        if (_services.Player.Station is null)
        {
            var first = _visible.FirstOrDefault() ?? _services.Library.Stations.FirstOrDefault();
            if (first is not null)
            {
                _services.Player.Play(first);
            }

            return;
        }

        _services.Player.TogglePlayPause();
    }

    private void OnPlayerChanged()
    {
        if (!ReferenceEquals(_nowPlaying, _services.Player.NowPlaying))
        {
            _nowPlaying = _services.Player.NowPlaying;
        }

        UpdateRowState();
        Raise(nameof(PlayerTitle));
        Raise(nameof(PlayerSubtitle));
        Raise(nameof(HeroArtworkUrl));
        Raise(nameof(HeroTooltip));
        Raise(nameof(IsPlaying));
        Raise(nameof(IsLoading));
        Raise(nameof(IsError));
        Raise(nameof(IsReconnect));
        Raise(nameof(IsLive));
        Raise(nameof(PlayPauseGlyph));
        Raise(nameof(CanPlay));
        Raise(nameof(CanPrevious));
        Raise(nameof(CanNext));
        Raise(nameof(Volume));
        UpdateNavSelection();
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();

        if (_services.Player.Status == PlaybackState.Error
            && _services.Player.ErrorMessage is { } message)
        {
            _services.Toasts.ShowError(message);
        }
    }

    private void RaiseSleep()
    {
        Raise(nameof(SleepActive));
        Raise(nameof(SleepText));
        Raise(nameof(SleepTooltip));
    }

    // MARK: - Commands

    public RelayCommand PlaySelectedCommand { get; }

    public RelayCommand AddStationCommand { get; }

    public RelayCommand OpenBrowserCommand { get; }

    public RelayCommand OpenTransferCommand { get; }

    public RelayCommand OpenEqualizerCommand { get; }

    public RelayCommand TogglePlayPauseCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand PreviousCommand { get; }

    public RelayCommand NextCommand { get; }

    public RelayCommand RetryCommand { get; }

    public RelayCommand EmptyActionCommand { get; }

    public RelayCommand SleepCommand { get; }

    public RelayCommand SleepCancelCommand { get; }

    public RelayCommand CloseCommand { get; }
}
