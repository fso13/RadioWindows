using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RadioWindows.Core;
using RadioWindows.ViewModels;

namespace RadioWindows.Views;

public partial class MainPage : UserControl
{
    private MainPageViewModel? _subscribed;

    public MainPage()
    {
        InitializeComponent();
        HeaderArea.PointerPressed += OnHeaderPressed;
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => AttachModel();
        AttachedToVisualTree += (_, _) => AttachModel();
    }

    private void AttachModel()
    {
        if (ReferenceEquals(_subscribed, Model))
        {
            return;
        }

        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnModelPropertyChanged;
            _subscribed.SelectionChanged -= OnSelectionChanged;
        }

        _subscribed = Model;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnModelPropertyChanged;
            _subscribed.SelectionChanged += OnSelectionChanged;
        }
    }

    private void OnModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainPageViewModel.PlayerTitle) or nameof(MainPageViewModel.IsPlaying))
        {
            BringCurrentIntoView();
        }
    }

    private void OnSelectionChanged(string stationId) => BringStationIntoView(stationId);

    private void BringCurrentIntoView()
    {
        if (Model?.Player.Station is { } station)
        {
            BringStationIntoView(station.Id);
        }
    }

    private void BringStationIntoView(string stationId)
    {
        if (Model is not { } model)
        {
            return;
        }

        for (var index = 0; index < model.Rows.Count; index++)
        {
            if (string.Equals(model.Rows[index].Station.Id, stationId, StringComparison.Ordinal))
            {
                RowsHost.ContainerFromIndex(index)?.BringIntoView();
                return;
            }
        }
    }

    private MainPageViewModel? Model => DataContext as MainPageViewModel;

    private ShellViewModel? Shell => (TopLevel.GetTopLevel(this) as Window)?.DataContext as ShellViewModel;

    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !IsWithinButton(e.Source))
        {
            (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
        }
    }

    private void OnHeroPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is { } model && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            model.TogglePlayPauseCommand.Execute(null);
        }
    }

    private void OnSearchGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (Model is { } model)
        {
            model.IsSearchFocused = true;
        }

        SearchUnderline.Classes.Set("focused", true);
    }

    private void OnSearchLostFocus(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
        {
            model.IsSearchFocused = false;
        }

        SearchUnderline.Classes.Set("focused", false);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (Model is { } model)
            {
                model.ResignSearch();
            }

            Focus();
            e.Handled = true;
        }
    }

    private StationRowViewModel? _dragCandidate;
    private Point _dragOrigin;

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not StationRowViewModel row)
        {
            return;
        }

        var point = e.GetCurrentPoint(border);
        if (point.Properties.IsRightButtonPressed)
        {
            ShowRowMenu(border, row);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed && !IsWithinButton(e.Source))
        {
            _dragCandidate = row.Owner.CanReorder ? row : null;
            _dragOrigin = e.GetPosition(this);
        }
    }

    private void OnRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: StationRowViewModel row } && !IsWithinButton(e.Source))
        {
            row.Owner.Play(row);
            e.Handled = true;
        }

        _dragCandidate = null;
    }

#pragma warning disable CS0618 // Avalonia 11.3 still exposes the synchronous drag API.
    private async void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragCandidate is not { } row
            || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed is false)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin;
        if (Math.Abs(delta.X) < 8 && Math.Abs(delta.Y) < 8)
        {
            return;
        }

        _dragCandidate = null;
        var data = new DataObject();
        data.Set(DataFormats.Text, row.Station.Id);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
    }

    private static void OnRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Text) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        if (Model is not { } model
            || sender is not Border { DataContext: StationRowViewModel target }
            || e.Data.GetText() is not { Length: > 0 } id)
        {
            return;
        }

        model.MoveStation(id, target.Station.Id);
        e.Handled = true;
    }

    private static void OnEndDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Text) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnEndDrop(object? sender, DragEventArgs e)
    {
        if (Model is not { } model || e.Data.GetText() is not { Length: > 0 } id)
        {
            return;
        }

        model.MoveStationToEnd(id);
        e.Handled = true;
    }
#pragma warning restore CS0618

    private void OnRowMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: StationRowViewModel row } button)
        {
            ShowRowMenu(button, row);
            e.Handled = true;
        }
    }

    private void ShowRowMenu(Control anchor, StationRowViewModel row)
    {
        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(Loc.T("Row.Play"), () => row.PlayCommand.Execute(null)));
        menu.Items.Add(MenuItem(row.FavoriteTooltip, () => row.ToggleFavoriteCommand.Execute(null)));
        menu.Items.Add(MenuItem(Loc.T("Row.Edit"), () => row.EditCommand.Execute(null)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Loc.T("Row.Delete"), () => row.DeleteCommand.Execute(null)));
        menu.Open(anchor);
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs e) => Shell?.ShowSettings();

    private void OnHide(object? sender, RoutedEventArgs e) => Shell?.RequestHide();

    private void OnQuit(object? sender, RoutedEventArgs e) => Shell?.RequestQuit();

    private static bool IsWithinButton(object? source)
    {
        var current = source as Visual;
        while (current is not null)
        {
            if (current is Button)
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

}
