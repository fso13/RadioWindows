using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace RadioWindows.Controls;

/// <summary>The icon vocabulary of the editorial design: thin strokes, sharp fills.</summary>
public enum IconKind
{
    None,
    Close,
    Menu,
    Search,
    Plus,
    Minus,
    Star,
    StarFilled,
    Dots,
    ArrowLeft,
    ArrowRight,
    ChevronDown,
    ChevronUp,
    Play,
    Pause,
    Stop,
    Previous,
    Next,
    Volume,
    Sliders,
    Moon,
    Refresh,
    Globe,
    Gear,
    Check,
    Warning,
    Info,
    Power,
    Folder,
    Clock,
    Doc,
    Copy,
    Download,
    ExternalLink,
    Trash,
    Pencil,
    Person,
    RadioWaves,
    DragHandle,
    Cd,
}

internal readonly record struct IconSpec(Geometry Geometry, bool Filled, double StrokeWidth = 1.6);

/// <summary>Vector icons drawn in a 24×24 box and scaled to the requested size.</summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<Icon, IconKind>(nameof(Kind));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 12);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<bool> FilledProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(Filled));

    private static readonly Dictionary<IconKind, IconSpec> Specs = new()
    {
        [IconKind.Close] = Stroke("M5 5 L19 19 M19 5 L5 19"),
        [IconKind.Menu] = Stroke("M4 7 H20 M4 12 H20 M4 17 H20"),
        [IconKind.Search] = Stroke("M16.4 10.6 A5.9 5.9 0 1 1 4.6 10.6 A5.9 5.9 0 1 1 16.4 10.6 M15.2 15.2 L20 20"),
        [IconKind.Plus] = Stroke("M12 5 V19 M5 12 H19"),
        [IconKind.Minus] = Stroke("M5 12 H19"),
        [IconKind.Star] = Stroke("M12 3.6 L14.6 9.1 L20.6 9.8 L16.2 14 L17.3 20 L12 17.1 L6.7 20 L7.8 14 L3.4 9.8 L9.4 9.1 Z", 1.5),
        [IconKind.StarFilled] = Fill("M12 3.6 L14.6 9.1 L20.6 9.8 L16.2 14 L17.3 20 L12 17.1 L6.7 20 L7.8 14 L3.4 9.8 L9.4 9.1 Z"),
        [IconKind.Dots] = Fill("M4 12 A1.35 1.35 0 1 1 6.7 12 A1.35 1.35 0 1 1 4 12 M10.65 12 A1.35 1.35 0 1 1 13.35 12 A1.35 1.35 0 1 1 10.65 12 M17.3 12 A1.35 1.35 0 1 1 20 12 A1.35 1.35 0 1 1 17.3 12"),
        [IconKind.ArrowLeft] = Stroke("M19 12 H5 M11 6 L5 12 L11 18"),
        [IconKind.ArrowRight] = Stroke("M5 12 H19 M13 6 L19 12 L13 18"),
        [IconKind.ChevronDown] = Stroke("M6 9 L12 15 L18 9", 1.5),
        [IconKind.ChevronUp] = Stroke("M6 15 L12 9 L18 15", 1.5),
        [IconKind.Play] = Fill("M7.5 4.5 L19 12 L7.5 19.5 Z"),
        [IconKind.Pause] = Fill("M7 5 H10.2 V19 H7 Z M13.8 5 H17 V19 H13.8 Z"),
        [IconKind.Stop] = Fill("M6.5 6.5 H17.5 V17.5 H6.5 Z"),
        [IconKind.Previous] = Fill("M5 5 H7.2 V19 H5 Z M18.5 5 L8.5 12 L18.5 19 Z"),
        [IconKind.Next] = Fill("M16.8 5 H19 V19 H16.8 Z M5.5 5 L15.5 12 L5.5 19 Z"),
        [IconKind.Volume] = Fill("M3 9 H7 L12 5 V19 L7 15 H3 Z"),
        [IconKind.Sliders] = Stroke("M4 7 H20 M4 12 H20 M4 17 H20 M9 7 m-1.8 0 a1.8 1.8 0 1 0 3.6 0 a1.8 1.8 0 1 0 -3.6 0 M15 12 m-1.8 0 a1.8 1.8 0 1 0 3.6 0 a1.8 1.8 0 1 0 -3.6 0 M7 17 m-1.8 0 a1.8 1.8 0 1 0 3.6 0 a1.8 1.8 0 1 0 -3.6 0", 1.5),
        [IconKind.Moon] = Fill("M20 14.6 A8.6 8.6 0 1 1 9.4 4 A7 7 0 0 0 20 14.6 Z"),
        [IconKind.Refresh] = Stroke("M20 5 V9.6 H15.4 M4.8 18.4 A8.2 8.2 0 0 1 5.4 6.6 M19.2 17.4 A8.2 8.2 0 0 1 8.2 20", 1.5),
        [IconKind.Globe] = Stroke("M12 4 A8 8 0 1 1 11.99 4 M4 12 H20 M12 4 C8.4 8 8.4 16 12 20 C15.6 16 15.6 8 12 4", 1.4),
        [IconKind.Gear] = Stroke("M12 8.7 A3.3 3.3 0 1 1 11.99 8.7 M12 2.6 V5.4 M12 18.6 V21.4 M2.6 12 H5.4 M18.6 12 H21.4 M5.3 5.3 L7.4 7.4 M16.6 16.6 L18.7 18.7 M18.7 5.3 L16.6 7.4 M7.4 16.6 L5.3 18.7", 1.5),
        [IconKind.Check] = Stroke("M5 12.6 L10 17.6 L19 7", 1.7),
        [IconKind.Warning] = Stroke("M12 4 L21 20 H3 Z M12 9.5 V14.2 M12 17.2 V17.4", 1.4),
        [IconKind.Info] = Stroke("M12 3 A9 9 0 1 1 11.99 3 M12 11 V17 M12 7.8 V8.1", 1.4),
        [IconKind.Power] = Stroke("M12 3 V11.4 M6.6 6.5 A8 8 0 1 0 17.4 6.5", 1.5),
        [IconKind.Folder] = Stroke("M3 7 H10 L12 9 H21 V19 H3 Z", 1.4),
        [IconKind.Clock] = Stroke("M12 4 A8 8 0 1 1 11.99 4 M12 7.4 V12.2 L15.4 14.2", 1.4),
        [IconKind.Doc] = Stroke("M6 3 H14 L18 7 V21 H6 Z M14 3 V7 H18", 1.4),
        [IconKind.Copy] = Stroke("M9 9 H20 V20 H9 Z M15 9 V4 H4 V15 H9", 1.4),
        [IconKind.Download] = Stroke("M12 4 V15 M7.4 10.6 L12 15.4 L16.6 10.6 M4 20 H20", 1.5),
        [IconKind.ExternalLink] = Stroke("M14 4 H20 V10 M20 4 L11 13 M18 14 V20 H4 V6 H10", 1.4),
        [IconKind.Trash] = Stroke("M4 7 H20 M9 7 V4 H15 V7 M6 7 L7 21 H17 L18 7 M10 11 V17 M14 11 V17", 1.4),
        [IconKind.Pencil] = Stroke("M4 20 L5.2 15 L16 4.2 L19.8 8 L9 18.8 Z", 1.4),
        [IconKind.Person] = Stroke("M12 4 A3.6 3.6 0 1 1 11.99 4 M4.6 20 A7.4 7.4 0 0 1 19.4 20", 1.4),
        [IconKind.RadioWaves] = Stroke("M10 14.6 m-2.1 0 a2.1 2.1 0 1 0 4.2 0 a2.1 2.1 0 1 0 -4.2 0 M6.2 17.9 A5.4 5.4 0 0 1 6 11 M3.4 20.2 A8.8 8.8 0 0 1 3 9.2", 1.5),
        [IconKind.DragHandle] = Fill("M9 6 A1.3 1.3 0 1 1 11.6 6 A1.3 1.3 0 1 1 9 6 M12.4 6 A1.3 1.3 0 1 1 15 6 A1.3 1.3 0 1 1 12.4 6 M9 12 A1.3 1.3 0 1 1 11.6 12 A1.3 1.3 0 1 1 9 12 M12.4 12 A1.3 1.3 0 1 1 15 12 A1.3 1.3 0 1 1 12.4 12 M9 18 A1.3 1.3 0 1 1 11.6 18 A1.3 1.3 0 1 1 9 18 M12.4 18 A1.3 1.3 0 1 1 15 18 A1.3 1.3 0 1 1 12.4 18"),
        [IconKind.Cd] = Stroke("M12 4 A8 8 0 1 1 11.99 4 M12 10.2 A1.8 1.8 0 1 1 11.99 10.2", 1.4),
    };

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, SizeProperty, ForegroundProperty, FilledProperty);
        AffectsMeasure<Icon>(SizeProperty);
    }

    public IconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
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

    /// <summary>Forces a filled variant of a stroke icon.</summary>
    public bool Filled
    {
        get => GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (!Specs.TryGetValue(Kind, out var spec))
        {
            return;
        }

        var brush = Foreground ?? Brushes.Black;
        var scale = Size / 24d;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale));
        if (spec.Filled || Filled)
        {
            context.DrawGeometry(brush, null, spec.Geometry);
        }
        else
        {
            context.DrawGeometry(null, new Pen(brush, spec.StrokeWidth), spec.Geometry);
        }
    }

    private static IconSpec Stroke(string data, double width = 1.6) =>
        new(Geometry.Parse(data), false, width);

    private static IconSpec Fill(string data) => new(Geometry.Parse(data), true);
}
