using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace RadioWindows.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        SettingsHeader.PointerPressed += (_, e) =>
        {
            var current = e.Source as Avalonia.Visual;
            while (current is not null)
            {
                if (current is Button)
                {
                    return;
                }

                current = current.GetVisualParent();
            }

            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                (TopLevel.GetTopLevel(this) as Window)?.BeginMoveDrag(e);
            }
        };
    }
}
