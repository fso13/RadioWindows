using RadioWindows.Core;

namespace RadioWindows.ViewModels;

public enum StationFilterKind
{
    All,
    Favorites,
    Genre,
    Group,
}

/// <summary>The active library filter (All / Favorites / genre / group).</summary>
public sealed record StationFilter(StationFilterKind Kind, string? Value = null)
{
    public static readonly StationFilter All = new(StationFilterKind.All);

    public static readonly StationFilter Favorites = new(StationFilterKind.Favorites);

    public static StationFilter Genre(string value) => new(StationFilterKind.Genre, value);

    public static StationFilter Group(string value) => new(StationFilterKind.Group, value);

    public bool Matches(RadioWindows.Core.Models.RadioStation station) => Kind switch
    {
        StationFilterKind.Favorites => station.IsFavorite,
        StationFilterKind.Genre => string.Equals(station.Genre, Value, StringComparison.Ordinal),
        StationFilterKind.Group => string.Equals(station.Group, Value, StringComparison.CurrentCultureIgnoreCase),
        _ => true,
    };
}

/// <summary>Large editorial genre entry in the left column.</summary>
public sealed class NavItemViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isPlaying;

    public NavItemViewModel(string label, StationFilter filter, MainPageViewModel owner)
    {
        Label = label;
        Filter = filter;
        Command = new RelayCommand(() => owner.SetFilter(filter));
    }

    public string Label { get; }

    public StationFilter Filter { get; }

    public RelayCommand Command { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    public bool ShowsBars => IsSelected && IsPlaying;
}
