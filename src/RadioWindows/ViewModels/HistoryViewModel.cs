using System.Collections.ObjectModel;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(TrackHistoryEntry entry)
    {
        Entry = entry;
    }

    public TrackHistoryEntry Entry { get; }

    public string Line => Entry.Line;

    public string StationName => Entry.StationName;

    public string TimeText => Entry.PlayedAt.LocalDateTime.ToString("d MMM, HH:mm");
}

/// <summary>Track history sheet with search, copy and clear.</summary>
public sealed class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private string _search = string.Empty;

    public HistoryViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
        CloseCommand = new RelayCommand(() => _shell.CloseModal());
        CopyCommand = new RelayCommand(async () => await CopyAsync());
        ClearCommand = new RelayCommand(() =>
        {
            _services.History.Clear();
            Reload();
        });
        services.History.PropertyChanged += OnHistoryChanged;
        Reload();
    }

    private void OnHistoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Reload();

    public void Dispose() => _services.History.PropertyChanged -= OnHistoryChanged;

    public string Title => Loc.T("History.Title");

    public string Subtitle => Loc.T("History.Subtitle");

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
            {
                Reload();
            }
        }
    }

    public bool IsEmpty => Items.Count == 0;

    public string EmptyLabel => Loc.T("History.Empty");

    public RelayCommand CloseCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand ClearCommand { get; }

    private void Reload()
    {
        Items.Clear();
        var query = _search.Trim().ToLowerInvariant();
        foreach (var entry in _services.History.Entries)
        {
            if (query.Length == 0 || entry.SearchText.Contains(query, StringComparison.Ordinal))
            {
                Items.Add(new HistoryItemViewModel(entry));
            }
        }

        Raise(nameof(IsEmpty));
    }

    private async Task CopyAsync()
    {
        var text = _services.History.CopyText();
        var copied = await _services.Clipboard.SetTextAsync(text);
        if (copied)
        {
            _services.Toasts.ShowSuccess(Loc.T("History.Copied"));
        }
        else
        {
            _services.Toasts.ShowError(Loc.T("Toast.CopyFailed"));
        }
    }
}
