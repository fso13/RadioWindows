using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RadioWindows.Controls;

/// <summary>Editorial wordmark: "RadioWindows®" in ink, optionally with the glyph.</summary>
public sealed class BrandMark : Control
{
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<BrandMark, double>(nameof(FontSize), 11.5);

    public static readonly StyledProperty<bool> ShowsGlyphProperty =
        AvaloniaProperty.Register<BrandMark, bool>(nameof(ShowsGlyph));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<BrandMark, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> BadgeBrushProperty =
        AvaloniaProperty.Register<BrandMark, IBrush?>(nameof(BadgeBrush));

    private static readonly Typeface Face = new(
        new FontFamily("avares://Avalonia.Fonts.Inter/Assets#Inter"),
        FontStyle.Normal,
        FontWeight.SemiBold);

    private FormattedText? _text;
    private FormattedText? _registered;
    private double _builtFor = -1;
    private IBrush? _builtForBrush;

    static BrandMark()
    {
        AffectsRender<BrandMark>(FontSizeProperty, ShowsGlyphProperty, ForegroundProperty, BadgeBrushProperty);
        AffectsMeasure<BrandMark>(FontSizeProperty, ShowsGlyphProperty);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public bool ShowsGlyph
    {
        get => GetValue(ShowsGlyphProperty);
        set => SetValue(ShowsGlyphProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? BadgeBrush
    {
        get => GetValue(BadgeBrushProperty);
        set => SetValue(BadgeBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Build();
        var width = (_text?.Width ?? 0) + (_registered?.Width ?? 0) + 1;
        if (ShowsGlyph)
        {
            width += FontSize + 9;
        }

        return new Size(width + 1, Math.Max(FontSize + 6, 20));
    }

    public override void Render(DrawingContext context)
    {
        Build();
        var ink = Foreground ?? Brushes.Black;
        var x = 0d;
        if (ShowsGlyph)
        {
            var glyphSize = FontSize + 4;
            using (context.PushTransform(Matrix.CreateTranslation(x, (Bounds.Height - glyphSize) / 2)))
            {
                BrandGlyph.Draw(context, ink, BadgeBrush ?? Brushes.White, glyphSize);
            }

            x += glyphSize + 5;
        }

        if (_text is null || _registered is null)
        {
            return;
        }

        var y = (Bounds.Height - _text.Height) / 2;
        context.DrawText(_text, new Point(x, y));
        context.DrawText(_registered, new Point(x + _text.Width + 1, y + _text.Height * 0.08));
    }

    private void Build()
    {
        var ink = Foreground ?? Brushes.Black;
        if (Math.Abs(_builtFor - FontSize) < 0.01 && ReferenceEquals(_builtForBrush, ink) && _text is not null)
        {
            return;
        }

        _builtFor = FontSize;
        _builtForBrush = ink;
        _text = new FormattedText("RadioWindows", System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Face, FontSize, ink);
        _registered = new FormattedText("®", System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Face, FontSize * 0.52, ink);
    }
}
