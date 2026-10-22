using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RadioWindows.Core.Services;
using RadioWindows.ViewModels;

namespace RadioWindows.Views;

public partial class TransferView : UserControl
{
    public TransferView()
    {
        InitializeComponent();
        Card.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Card.AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) => Wire();
        AttachedToVisualTree += (_, _) => Wire();
    }

    private TransferViewModel? _model;

    private void Wire()
    {
        if (ReferenceEquals(_model, DataContext as TransferViewModel))
        {
            return;
        }

        if (_model is not null)
        {
            _model.ImportRequested -= OnImportRequested;
            _model.ExportRequested -= OnExportRequested;
        }

        _model = DataContext as TransferViewModel;
        if (_model is not null)
        {
            _model.ImportRequested += OnImportRequested;
            _model.ExportRequested += OnExportRequested;
        }
    }

    private async void OnImportRequested()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || _model is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = _model.ImportLabel,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Станции")
                {
                    Patterns = ["*.json", "*.m3u", "*.m3u8", "*.opml", "*.xml"],
                },
            ],
        });

        if (files.Count > 0)
        {
            await _model.ImportFileAsync(files[0].Path.LocalPath);
        }
    }

    private async void OnExportRequested(StationExportFormat format)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || _model is null)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = _model.ExportLabel,
            SuggestedFileName = _model.SuggestedName(format),
            DefaultExtension = format.ToString().ToLowerInvariant(),
            FileTypeChoices =
            [
                new FilePickerFileType("Станции")
                {
                    Patterns = [$"*.{format.ToString().ToLowerInvariant()}"],
                },
            ],
        });

        if (file is not null)
        {
            await _model.ExportAsync(file.Path.LocalPath, format);
        }
    }

#pragma warning disable CS0618 // Avalonia 11.3 still exposes the synchronous drag API.
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_model is null || !e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var file = e.Data.GetFiles()?.FirstOrDefault();
        if (file is not null)
        {
            await _model.ImportFileAsync(file.Path.LocalPath);
        }

        e.Handled = true;
    }
#pragma warning restore CS0618
}
