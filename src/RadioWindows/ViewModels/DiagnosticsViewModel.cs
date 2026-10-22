using System.Collections.ObjectModel;
using RadioWindows.Core;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>Local diagnostics log viewer.</summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private string _search = string.Empty;

    public DiagnosticsViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
        CloseCommand = new RelayCommand(() => _shell.CloseModal());
        CopyCommand = new RelayCommand(async () => await CopyAsync());
        ClearCommand = new RelayCommand(() =>
        {
            _services.Diagnostics.Clear();
            Reload();
        });
        RevealCommand = new RelayCommand(() =>
            ShellOpen.Reveal(_services.Diagnostics.HasLog
                ? _services.Diagnostics.LogPath
                : _services.Paths.LogDirectory));
        Reload();
    }

    public string Title => Loc.T("Diagnostics.Title");

    public string Subtitle => Loc.T("Diagnostics.Subtitle");

    public ObservableCollection<string> Lines { get; } = [];

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

    public bool IsEmpty => Lines.Count == 0;

    public string EmptyLabel => Loc.T("Diagnostics.Empty");

    public RelayCommand CloseCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand RevealCommand { get; }

    private void Reload()
    {
        Lines.Clear();
        var query = _search.Trim();
        foreach (var line in _services.Diagnostics.ReadLines(1000))
        {
            if (query.Length == 0 || line.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Lines.Add(line);
            }
        }

        Raise(nameof(IsEmpty));
    }

    private async Task CopyAsync()
    {
        var text = string.Join(Environment.NewLine, Lines);
        var copied = await _services.Clipboard.SetTextAsync(text);
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
