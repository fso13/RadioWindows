using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RadioWindows.Controls;

/// <summary>Artwork loader with memory and disk caches; crops to fill the box.</summary>
public sealed class RemoteImage : Control
{
    public static readonly StyledProperty<string?> SourceUrlProperty =
        AvaloniaProperty.Register<RemoteImage, string?>(nameof(SourceUrl));

    public static readonly StyledProperty<IBrush?> PlaceholderBrushProperty =
        AvaloniaProperty.Register<RemoteImage, IBrush?>(nameof(PlaceholderBrush));

    public static readonly StyledProperty<double> PlaceholderSizeProperty =
        AvaloniaProperty.Register<RemoteImage, double>(nameof(PlaceholderSize), 22);

    private static ImageCache _cache = new();

    private Bitmap? _bitmap;
    private string? _loadedUrl;
    private CancellationTokenSource? _cancellation;

    static RemoteImage()
    {
        AffectsRender<RemoteImage>(SourceUrlProperty, PlaceholderBrushProperty, PlaceholderSizeProperty);
    }

    public string? SourceUrl
    {
        get => GetValue(SourceUrlProperty);
        set => SetValue(SourceUrlProperty, value);
    }

    public IBrush? PlaceholderBrush
    {
        get => GetValue(PlaceholderBrushProperty);
        set => SetValue(PlaceholderBrushProperty, value);
    }

    public double PlaceholderSize
    {
        get => GetValue(PlaceholderSizeProperty);
        set => SetValue(PlaceholderSizeProperty, value);
    }

    public static void Configure(ImageCache cache) => _cache = cache;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceUrlProperty)
        {
            _ = LoadAsync(SourceUrl);
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _cancellation?.Cancel();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (_bitmap is null)
        {
            DrawPlaceholder(context, bounds);
            return;
        }

        var source = new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
        var scale = Math.Max(bounds.Width / source.Width, bounds.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        var destination = new Rect(
            (bounds.Width - width) / 2,
            (bounds.Height - height) / 2,
            width,
            height);

        using (context.PushClip(bounds))
        {
            context.DrawImage(_bitmap, source, destination);
        }
    }

    private void DrawPlaceholder(DrawingContext context, Rect bounds)
    {
        var brush = PlaceholderBrush ?? Brushes.Gray;
        var size = Math.Min(PlaceholderSize, Math.Min(bounds.Width, bounds.Height) * 0.6);
        if (size <= 0)
        {
            return;
        }

        var scale = size / 24d;
        using var transform = context.PushTransform(Matrix.CreateTranslation(
            (bounds.Width - size) / 2, (bounds.Height - size) / 2));
        using var scaled = context.PushTransform(Matrix.CreateScale(scale, scale));
        var geometry = Geometry.Parse("M10 14.6 m-2.1 0 a2.1 2.1 0 1 0 4.2 0 a2.1 2.1 0 1 0 -4.2 0 M6.2 17.9 A5.4 5.4 0 0 1 6 11 M3.4 20.2 A8.8 8.8 0 0 1 3 9.2");
        context.DrawGeometry(null, new Pen(brush, 1.5), geometry);
    }

    private async Task LoadAsync(string? url)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;

        var trimmed = url?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _bitmap = null;
            _loadedUrl = null;
            InvalidateVisual();
            return;
        }

        if (string.Equals(trimmed, _loadedUrl, StringComparison.Ordinal) && _bitmap is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _cancellation = cancellation;
        var bitmap = await _cache.GetAsync(trimmed, cancellation.Token).ConfigureAwait(true);
        if (cancellation.IsCancellationRequested || !string.Equals(trimmed, SourceUrl?.Trim(), StringComparison.Ordinal))
        {
            return;
        }

        _bitmap = bitmap;
        _loadedUrl = bitmap is null ? null : trimmed;
        InvalidateVisual();
    }
}
