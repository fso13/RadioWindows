using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RadioWindows.ViewModels;

namespace RadioWindows.Views;

public partial class MiniPlayerWindow : Window
{
    public MiniPlayerWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (AutoHideOnDeactivate)
            {
                Hide();
            }
        };
        PointerPressed += OnWindowPressed;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
    }

    /// <summary>Popover behaviour: clicking outside hides it. Disabled in headless tests.</summary>
    public bool AutoHideOnDeactivate { get; set; } = true;

    private void OnWindowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is Window)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnArtworkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MiniPlayerViewModel model && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            model.TogglePlayPauseCommand.Execute(null);
        }
    }
}
