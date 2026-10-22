using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace RadioWindows.Controls;

/// <summary>Three animated bars: the "now playing" mark.</summary>
public sealed class EqualizerBars : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<EqualizerBars, bool>(nameof(IsActive));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<EqualizerBars, IBrush?>(nameof(Foreground));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public EqualizerBars()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Render, (_, _) =>
        {
            _phase += 0.38;
            InvalidateVisual();
        });
        _timer.Stop();
    }

    static EqualizerBars()
    {
        AffectsRender<EqualizerBars>(IsActiveProperty, ForegroundProperty);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set
        {
            SetValue(IsActiveProperty, value);
            if (value)
            {
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                InvalidateVisual();
            }
        }
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(10, 12);

    public override void Render(DrawingContext context)
    {
        var brush = Foreground ?? Brushes.Black;
        const double width = 2;
        for (var index = 0; index < 3; index++)
        {
            var height = IsActive
                ? 5 + (Math.Sin(_phase + index * 1.4) + 1) * 3
                : 4 + index * 2;
            var x = index * 4;
            var y = (12 - height) / 2;
            context.FillRectangle(brush, new Rect(x, y, width, height));
        }
    }
}
