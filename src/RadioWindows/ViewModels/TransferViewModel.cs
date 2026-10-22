using System.Collections.ObjectModel;
using RadioWindows.Core;
using RadioWindows.Core.Models;
using RadioWindows.Core.Services;
using RadioWindows.Services;

namespace RadioWindows.ViewModels;

/// <summary>JSON/M3U/OPML import and export sheet.</summary>
public sealed class TransferViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private bool _replaceMode;
    private string _status = string.Empty;

    public TransferViewModel(AppServices services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;

        CloseCommand = new RelayCommand(() => _shell.CloseModal());
        ChooseImportCommand = new RelayCommand(() => ImportRequested?.Invoke());
        ExportJsonCommand = new RelayCommand(() => ExportRequested?.Invoke(StationExportFormat.Json));
        ExportM3uCommand = new RelayCommand(() => ExportRequested?.Invoke(StationExportFormat.M3u));
        ExportOpmlCommand = new RelayCommand(() => ExportRequested?.Invoke(StationExportFormat.Opml));
        MergeModeCommand = new RelayCommand(() => ReplaceMode = false);
        ReplaceModeCommand = new RelayCommand(() => ReplaceMode = true);
    }

    public string Title => Loc.T("Transfer.Title");

    public string Subtitle => Loc.T("Transfer.Subtitle");

    public string ImportLabel => Loc.T("Transfer.Import");

    public string ExportLabel => Loc.T("Transfer.Export");

    public string MergeLabel => Loc.T("Transfer.ModeMerge");

    public string ReplaceLabel => Loc.T("Transfer.ModeReplace");

    public string DropHint => Loc.T("Transfer.Drop");

    public string ChooseLabel => Loc.T("Transfer.Choose");

    public string ExportJsonLabel => Loc.T("Transfer.ExportJson");

    public string ExportM3uLabel => Loc.T("Transfer.ExportM3u");

    public string ExportOpmlLabel => Loc.T("Transfer.ExportOpml");

    public bool ReplaceMode
    {
        get => _replaceMode;
        private set
        {
            if (Set(ref _replaceMode, value))
            {
                Raise(nameof(IsMergeMode));
            }
        }
    }

    public bool IsMergeMode => !_replaceMode;

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

    public bool CanExport => _services.Library.Stations.Count > 0;

    public RelayCommand CloseCommand { get; }

    public RelayCommand ChooseImportCommand { get; }

    public RelayCommand ExportJsonCommand { get; }

    public RelayCommand ExportM3uCommand { get; }

    public RelayCommand ExportOpmlCommand { get; }

    public RelayCommand MergeModeCommand { get; }

    public RelayCommand ReplaceModeCommand { get; }

    public event Action? ImportRequested;

    public event Action<StationExportFormat>? ExportRequested;

    public async Task ImportFileAsync(string path)
    {
        try
        {
            var content = await File.ReadAllTextAsync(path);
            var parsed = StationTransfer.Parse(Path.GetFileName(path), content);
            if (parsed.Count == 0)
            {
                Status = Loc.T("Transfer.Invalid");
                _services.Toasts.ShowError(Status);
                return;
            }

            var result = _services.Library.Import(
                parsed,
                ReplaceMode ? StationImportMode.Replace : StationImportMode.Merge);
            Status = Loc.Format("Transfer.Imported", result.Added, result.Skipped);
            _services.Toasts.ShowSuccess(Status);
            Raise(nameof(CanExport));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = Loc.T("Transfer.Invalid");
            _services.Toasts.ShowError(Status);
        }
    }

    public async Task ExportAsync(string path, StationExportFormat format)
    {
        try
        {
            var content = StationTransfer.Export(_services.Library.Stations, format);
            await File.WriteAllTextAsync(path, content);
            Status = Loc.T("Transfer.Exported");
            _services.Toasts.ShowSuccess(Status);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _services.Toasts.ShowError(Loc.T("Transfer.Invalid"));
        }
    }

    public string SuggestedName(StationExportFormat format) =>
        $"RadioWindows-станции.{StationTransfer.ExtensionFor(format)}";
}
