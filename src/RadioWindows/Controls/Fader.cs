using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RadioWindows.Controls;

/// <summary>
/// Thin editorial fader with a square thumb. Vertical by default (equalizer),
/// horizontal for volume. Dragging updates <see cref="Value"/> continuously.
/// </summary>
public sealed class Fader : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Value), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Minimum), -12);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(Maximum), 12);

    public static readonly StyledProperty<bool> IsVerticalProperty =
        AvaloniaProperty.Register<Fader, bool>(nameof(IsVertical), true);

    public static readonly StyledProperty<bool> IsBipolarProperty =
        AvaloniaProperty.Register<Fader, bool>(nameof(IsBipolar), true);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<Fader, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<Fader, IBrush?>(nameof(FillBrush));

    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<Fader, IBrush?>(nameof(ThumbBrush));

    public static readonly StyledProperty<double> ThumbSizeProperty =
        AvaloniaProperty.Register<Fader, double>(nameof(ThumbSize), 8);

    private bool _dragging;

    static Fader()
    {
        AffectsRender<Fader>(ValueProperty, MinimumProperty, MaximumProperty, IsVerticalProperty,
            IsBipolarProperty, TrackBrushProperty, FillBrushProperty, ThumbBrushProperty, ThumbSizeProperty);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public bool IsVertical
    {
        get => GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    public bool IsBipolar
    {
        get => GetValue(IsBipolarProperty);
        set => SetValue(IsBipolarProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    public IBrush? ThumbBrush
    {
        get => GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    public double ThumbSize
    {
        get => GetValue(ThumbSizeProperty);
        set => SetValue(ThumbSizeProperty, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        e.Pointer.Capture(this);
        UpdateFromPointer(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            UpdateFromPointer(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var track = TrackBrush ?? Brushes.LightGray;
        var fill = FillBrush ?? Brushes.Black;
        var thumb = ThumbBrush ?? Brushes.White;
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return;
        }

        var ratio = Math.Clamp((Value - Minimum) / range, 0, 1);
        var thickness = 2d;
        var thumbSize = ThumbSize;
        var zeroRatio = Math.Clamp((0 - Minimum) / range, 0, 1);

        if (IsVertical)
        {
            var x = bounds.Width / 2;
            var top = thumbSize / 2;
            var bottom = bounds.Height - thumbSize / 2;
            var usable = bottom - top;
            var valueY = bottom - ratio * usable;

            context.DrawLine(new Pen(track, thickness), new Point(x, top), new Point(x, bottom));

            var anchorY = IsBipolar ? bottom - zeroRatio * usable : bottom;
            context.DrawLine(new Pen(fill, thickness + 1),
                new Point(x, Math.Min(anchorY, valueY)), new Point(x, Math.Max(anchorY, valueY)));

            if (IsBipolar)
            {
                var zeroPen = new Pen(track, 1);
                context.DrawLine(zeroPen, new Point(x - 7, bottom - zeroRatio * usable),
                    new Point(x + 7, bottom - zeroRatio * usable));
            }

            var r = new Rect(x - thumbSize / 2, valueY - thumbSize / 2, thumbSize, thumbSize);
            context.FillRectangle(thumb, r);
        }
        else
        {
            var y = bounds.Height / 2;
            var left = thumbSize / 2;
            var right = bounds.Width - thumbSize / 2;
            var usable = right - left;
            var valueX = left + ratio * usable;

            context.DrawLine(new Pen(track, thickness), new Point(left, y), new Point(right, y));
            var anchorX = IsBipolar ? left + zeroRatio * usable : left;
            context.DrawLine(new Pen(fill, thickness + 1),
                new Point(Math.Min(anchorX, valueX), y), new Point(Math.Max(anchorX, valueX), y));

            var r = new Rect(valueX - thumbSize / 2, y - thumbSize / 2, thumbSize, thumbSize);
            context.FillRectangle(thumb, r);
        }
    }

    private void UpdateFromPointer(Point position)
    {
        var range = Maximum - Minimum;
        var thumbSize = ThumbSize;
        double ratio;
        if (IsVertical)
        {
            var usable = Math.Max(1, Bounds.Height - thumbSize);
            ratio = 1 - (position.Y - thumbSize / 2) / usable;
        }
        else
        {
            var usable = Math.Max(1, Bounds.Width - thumbSize);
            ratio = (position.X - thumbSize / 2) / usable;
        }

        ratio = Math.Clamp(ratio, 0, 1);
        Value = Math.Round(Minimum + ratio * range, 1);
    }
}
