using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RadioWindows.Controls;

/// <summary>The app glyph: emitter dot, broadcast arcs and a plus badge.</summary>
public sealed class BrandGlyph : Control
{
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<BrandGlyph, double>(nameof(Size), 20);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<BrandGlyph, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> BadgeBrushProperty =
        AvaloniaProperty.Register<BrandGlyph, IBrush?>(nameof(BadgeBrush));

    static BrandGlyph()
    {
        AffectsRender<BrandGlyph>(SizeProperty, ForegroundProperty, BadgeBrushProperty);
        AffectsMeasure<BrandGlyph>(SizeProperty);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Backdrop behind the plus badge (the panel colour).</summary>
    public IBrush? BadgeBrush
    {
        get => GetValue(BadgeBrushProperty);
        set => SetValue(BadgeBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context) =>
        Draw(context, Foreground ?? Brushes.Black, BadgeBrush ?? Brushes.White, Size);

    /// <summary>Draws the glyph into any context at <paramref name="size"/> pixels.</summary>
    public static void Draw(DrawingContext context, IBrush ink, IBrush badgeBackground, double size)
    {
        var scale = size / 24d;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));

        var center = new Point(10.0, 13.6);
        context.DrawEllipse(ink, null, center, 2.7, 2.7);

        foreach (var (radius, start, sweep, thickness) in new[]
                 {
                     (5.6, -2.32, 1.85, 2.1),
                     (8.8, -2.28, 1.72, 2.1),
                 })
        {
            var geometry = new StreamGeometry();
            using (var sink = geometry.Open())
            {
                const int steps = 48;
                for (var step = 0; step <= steps; step++)
                {
                    var angle = start + sweep * step / steps;
                    var point = new Point(
                        center.X + Math.Cos(angle) * radius,
                        center.Y + Math.Sin(angle) * radius);
                    if (step == 0)
                    {
                        sink.BeginFigure(point, false);
                    }
                    else
                    {
                        sink.LineTo(point);
                    }
                }

                sink.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(ink, thickness, lineCap: PenLineCap.Round), geometry);
        }

        var badge = new Point(18.4, 18.4);
        context.DrawEllipse(badgeBackground, null, badge, 4.5, 4.5);
        context.DrawEllipse(ink, null, badge, 3.6, 3.6);

        var plusPen = new Pen(badgeBackground, 1.5, lineCap: PenLineCap.Round);
        context.DrawLine(plusPen, new Point(badge.X - 1.6, badge.Y), new Point(badge.X + 1.6, badge.Y));
        context.DrawLine(plusPen, new Point(badge.X, badge.Y - 1.6), new Point(badge.X, badge.Y + 1.6));
    }
}
