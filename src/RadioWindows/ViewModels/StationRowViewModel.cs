using RadioWindows.Controls;
using RadioWindows.Core;
using RadioWindows.Core.Models;

namespace RadioWindows.ViewModels;

/// <summary>One row of the station table.</summary>
public sealed class StationRowViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isCurrent;
    private bool _isPlaying;
    private bool _isLoading;

    public StationRowViewModel(RadioStation station, int index, MainPageViewModel owner)
    {
        Station = station;
        Index = index;
        Owner = owner;
        PlayCommand = new RelayCommand(() => owner.Play(this));
        ToggleFavoriteCommand = new RelayCommand(() => owner.ToggleFavorite(this));
        EditCommand = new RelayCommand(() => owner.Edit(this));
        DeleteCommand = new RelayCommand(() => owner.AskDelete(this));
        ReplayCommand = new RelayCommand(() => owner.Play(this));
    }

    public RadioStation Station { get; }

    public MainPageViewModel Owner { get; }

    public int Index { get; }

    public string Name => Station.Name;

    public string Number => (Index + 1).ToString();

    public string Genre => Station.Genre;

    public string SecondaryLine
    {
        get
        {
            if (Station.HasComment)
            {
                return Station.Comment!;
            }

            return Uri.TryCreate(Station.Url, UriKind.Absolute, out var uri) ? uri.Host : "—";
        }
    }

    public bool IsFavorite => Station.IsFavorite;

    public IconKind FavoriteIcon => Station.IsFavorite ? IconKind.StarFilled : IconKind.Star;

    public string FavoriteTooltip => Station.IsFavorite ? Loc.T("Row.Unfavorite") : Loc.T("Row.Favorite");

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (Set(ref _isCurrent, value))
            {
                Raise(nameof(ShowMarker));
                Raise(nameof(ShowBars));
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => Set(ref _isPlaying, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (Set(ref _isLoading, value))
            {
                Raise(nameof(ShowBars));
                Raise(nameof(ShowProgress));
            }
        }
    }

    public bool ShowMarker => IsCurrent;

    public bool ShowBars => IsCurrent && !IsLoading;

    public bool ShowProgress => IsCurrent && IsLoading;

    public RelayCommand PlayCommand { get; }

    public RelayCommand ReplayCommand { get; }

    public RelayCommand ToggleFavoriteCommand { get; }

    public RelayCommand EditCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public void RefreshFavorite()
    {
        Raise(nameof(IsFavorite));
        Raise(nameof(FavoriteIcon));
        Raise(nameof(FavoriteTooltip));
    }
}
