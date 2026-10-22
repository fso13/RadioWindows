using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using RadioWindows.Core.Services;
using RadioWindows.ViewModels;

namespace RadioWindows.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        Closing += (_, e) =>
        {
            if (Exiting)
            {
                return;
            }

            // The tray agent keeps living with the window hidden.
            e.Cancel = true;
            HidePanel();
        };
    }

    public void HidePanel()
    {
        Hide();
        if (DataContext is ShellViewModel shell)
        {
            shell.Main.ResetAfterHide();
        }
    }

    /// <summary>True while the app is really shutting down (tray → Quit).</summary>
    public bool Exiting { get; private set; }

    public void PrepareForExit() => Exiting = true;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        var focused = FocusManager?.GetFocusedElement();
        var isEditing = focused is TextBox;

        if (shell.HasModal)
        {
            if (e.Key == Key.Escape)
            {
                shell.CloseModal();
                e.Handled = true;
            }

            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F:
                    shell.Main.RequestFocusSearch();
                    e.Handled = true;
                    return;
                case Key.N:
                    shell.Main.NewStation();
                    e.Handled = true;
                    return;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            if (isEditing)
            {
                shell.Main.ResignSearch();
                Focus();
            }
            else
            {
                HidePanel();
            }

            e.Handled = true;
            return;
        }

        if (isEditing || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                shell.Main.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                shell.Main.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                shell.Main.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                shell.Main.PlaySelected();
                e.Handled = true;
                break;
            default:
                if (e.Key is >= Key.D1 and <= Key.D9)
                {
                    shell.Main.PlayVisible((int)e.Key - (int)Key.D1);
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && DataContext is ShellViewModel shell && !shell.HasModal)
        {
            // Space is handled on KeyDown; swallow the button activation that follows.
            e.Handled = true;
        }
    }
}
