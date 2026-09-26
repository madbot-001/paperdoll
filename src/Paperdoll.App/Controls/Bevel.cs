using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Paperdoll.App.Controls;

public enum BevelKind
{
    /// <summary>Stands out: buttons, toolbars.</summary>
    Raised,

    /// <summary>Sits in: text fields, lists, status bar cells, the preview.</summary>
    Sunken,

    /// <summary>A thin sunken line, for group edges.</summary>
    Thin,

    /// <summary>No edge: flat toolbar buttons at rest.</summary>
    Flat,
}

/// <summary>
/// A classic two-tone 3D edge around its child: light on two sides, shadow on the other two.
/// </summary>
public sealed class Bevel : Decorator
{
    public static readonly StyledProperty<BevelKind> KindProperty =
        AvaloniaProperty.Register<Bevel, BevelKind>(nameof(Kind), BevelKind.Sunken);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<Bevel, IBrush?>(nameof(Background));

    static Bevel()
    {
        AffectsRender<Bevel>(KindProperty, BackgroundProperty);
        PaddingProperty.OverrideDefaultValue<Bevel>(new Thickness(2));
    }

    public BevelKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    private static readonly IPen White = new Pen(new SolidColorBrush(Color.Parse("#FFFFFF")));
    private static readonly IPen Light = new Pen(new SolidColorBrush(Color.Parse("#D4D0C8")));
    private static readonly IPen Shadow = new Pen(new SolidColorBrush(Color.Parse("#808080")));
    private static readonly IPen Dark = new Pen(new SolidColorBrush(Color.Parse("#404040")));

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (Background != null)
            context.FillRectangle(Background, new Rect(0, 0, w, h));

        switch (Kind)
        {
            case BevelKind.Raised:
                Edge(context, 0, w, h, White, Dark);
                Edge(context, 1, w, h, Light, Shadow);
                break;
            case BevelKind.Sunken:
                Edge(context, 0, w, h, Shadow, White);
                Edge(context, 1, w, h, Dark, Light);
                break;
            case BevelKind.Thin:
                Edge(context, 0, w, h, Shadow, White);
                break;
        }
    }

    // One ring of the edge, inset by `inset` pixels: top and left in one pen, bottom and right in the other.
    private static void Edge(DrawingContext context, int inset, double w, double h, IPen topLeft, IPen bottomRight)
    {
        var l = inset + 0.5;
        var t = inset + 0.5;
        var r = w - inset - 0.5;
        var b = h - inset - 0.5;
        context.DrawLine(topLeft, new Point(l, b), new Point(l, t));
        context.DrawLine(topLeft, new Point(l, t), new Point(r, t));
        context.DrawLine(bottomRight, new Point(r, t + 1), new Point(r, b));
        context.DrawLine(bottomRight, new Point(l + 1, b), new Point(r, b));
    }
}
