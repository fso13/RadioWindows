using System.Collections.ObjectModel;
using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

public sealed class FilterOption
{
    public FilterOption(string? value, string label)
    {
        Value = value;
        Label = label;
    }

    public string? Value { get; }

    public string Label { get; }
}

/// <summary>One Radio Browser result.</summary>
public sealed class BrowserItemViewModel : ObservableObject
{
    private bool _isAdded;

    public BrowserItemViewModel(RadioBrowserStation station, BrowserViewModel owner)
    {
        Station = station;
        AddCommand = new RelayCommand(() => owner.Add(this));
        PreviewCommand = new RelayCommand(() => owner.Preview(this));
    }

    public RadioBrowserStation Station { get; }

    public string Name => Station.Name;

    public string Details => Station.Details;

    public string LogoUrl => Station.LogoUrl ?? string.Empty;

    public string FavoriteHint => Loc.T("Browser.Add");

    public RelayCommand AddCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public bool IsAdded
    {
        get => _isAdded;
        set
        {
            if (Set(ref _isAdded, value))
            {
                Raise(nameof(AddLabel));
            }
        }
    }

    public string AddLabel => IsAdded ? "✓" : "+";
}

/// <summary>Radio Browser search sheet.</summary>
public sealed class BrowserViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private string _query = string.Empty;
    private string _status = string.Empty;
    private bool _isSearching;
    private bool _initialized;
    private FilterOption _selectedGenre;
    private FilterOption _selectedCountry;

    public BrowserViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;

        Genres.Add(new FilterOption(null, Loc.T("Browser.AnyGenre")));
        Countries.Add(new FilterOption(null, Loc.T("Browser.AnyCountry")));
        _selectedGenre = Genres[0];
        _selectedCountry = Countries[0];

        CloseCommand = new RelayCommand(() => _shell.CloseModal());
        SearchCommand = new RelayCommand(async () => await SearchAsync());
        InitializeCommand = new RelayCommand(async () => await EnsureFiltersAsync());
        _ = EnsureFiltersAsync();
    }

    public string Title => Loc.T("Browser.Title");

    public string Subtitle => Loc.T("Browser.Subtitle");

    public string SearchLabel => Loc.T("Browser.Find");

    public string QueryLabel => Loc.T("Browser.Query");

    public string GenreLabel => Loc.T("Browser.Genre");

    public string CountryLabel => Loc.T("Browser.Country");

    public string EmptyLabel => Loc.T("Browser.Empty");

    public ObservableCollection<FilterOption> Genres { get; } = [];

    public ObservableCollection<FilterOption> Countries { get; } = [];

    public ObservableCollection<BrowserItemViewModel> Results { get; } = [];

    public string Query
    {
        get => _query;
        set
        {
            if (Set(ref _query, value))
            {
                Raise(nameof(HasQuery));
            }
        }
    }

    public bool HasQuery => _query.Trim().Length > 0;

    public FilterOption SelectedGenre
    {
        get => _selectedGenre;
        set
        {
            if (value is not null)
            {
                Set(ref _selectedGenre, value);
            }
        }
    }

    public FilterOption SelectedCountry
    {
        get => _selectedCountry;
        set
        {
            if (value is not null)
            {
                Set(ref _selectedCountry, value);
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set => Set(ref _isSearching, value);
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
            {
                Raise(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _status.Length > 0;

    public bool IsEmpty => Results.Count == 0 && !IsSearching;

    public RelayCommand CloseCommand { get; }

    public RelayCommand SearchCommand { get; }

    public RelayCommand InitializeCommand { get; }

    private async Task EnsureFiltersAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            var genres = await _services.Browser.GenresAsync();
            foreach (var genre in genres.Take(120))
            {
                Genres.Add(new FilterOption(genre, genre));
            }

            var countries = await _services.Browser.CountriesAsync();
            foreach (var country in countries)
            {
                Countries.Add(new FilterOption(country, country));
            }
        }
        catch (Exception)
        {
            // Filters are optional; search by name still works.
        }
    }

    private async Task SearchAsync()
    {
        if (IsSearching)
        {
            return;
        }

        IsSearching = true;
        Status = Loc.T("Browser.Searching");
        Raise(nameof(IsEmpty));
        try
        {
            var results = await _services.Browser.SearchAsync(
                _query.Trim(),
                _selectedGenre.Value,
                _selectedCountry.Value,
                50);
            Results.Clear();
            foreach (var station in results)
            {
                var item = new BrowserItemViewModel(station, this);
                item.IsAdded = _services.Library.IsDuplicate(station.Url, station.Id);
                Results.Add(item);
            }

            Status = results.Count == 0 ? Loc.T("Browser.Empty") : Loc.Format("Browser.Found", results.Count);
        }
        catch (Exception)
        {
            Status = Loc.T("Updates.Failed");
        }
        finally
        {
            IsSearching = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>Seeds results without a network round-trip (screenshots, tests).</summary>
    public void LoadResults(IEnumerable<RadioBrowserStation> stations)
    {
        Results.Clear();
        foreach (var station in stations)
        {
            var item = new BrowserItemViewModel(station, this)
            {
                IsAdded = _services.Library.IsDuplicate(station.Url, station.Id),
            };
            Results.Add(item);
        }

        Status = Results.Count == 0 ? Loc.T("Browser.Empty") : Loc.Format("Browser.Found", Results.Count);
        Raise(nameof(IsEmpty));
    }

    public void Add(BrowserItemViewModel item)
    {
        if (item.IsAdded || _services.Library.IsDuplicate(item.Station.Url, item.Station.Id))
        {
            item.IsAdded = true;
            _services.Toasts.Show(Loc.T("Browser.InLibrary"));
            return;
        }

        var station = item.Station.ToStation();
        _services.Library.Add(
            station.Name,
            station.Url,
            station.Genre,
            station.Comment,
            station.Group,
            station.LogoUrl,
            station.ExternalId);
        item.IsAdded = true;
        _services.Toasts.ShowSuccess(Loc.T("Toast.StationAdded"));
    }

    public void Preview(BrowserItemViewModel item)
    {
        _services.Player.Play(item.Station.ToStation(), persist: false);
    }
}
