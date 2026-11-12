using Avalonia;
using Avalonia.Controls;

namespace Paperdoll.App.Controls;

/// <summary>
/// Lays tab buttons out left to right, wrapping to a new row when the next one does not fit.
/// When every button fits on one row, buttons marked <see cref="IsUtilityProperty"/> sit flush right.
/// </summary>
public sealed class TabStripPanel : Panel
{
    public static readonly AttachedProperty<bool> IsUtilityProperty =
        AvaloniaProperty.RegisterAttached<TabStripPanel, Control, bool>("IsUtility");

    public static bool GetIsUtility(Control control) => control.GetValue(IsUtilityProperty);

    public static void SetIsUtility(Control control, bool value) => control.SetValue(IsUtilityProperty, value);

    static TabStripPanel()
    {
        AffectsParentMeasure<TabStripPanel>(IsUtilityProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            if (child.IsVisible)
                child.Measure(Size.Infinity);
        }

        var (_, size) = Place(availableSize.Width);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (placed, size) = Place(finalSize.Width);
        foreach (var (child, origin) in placed)
            child.Arrange(new Rect(origin, child.DesiredSize));
        return new Size(finalSize.Width, Math.Max(size.Height, finalSize.Height));
    }

    /// <summary>Positions of the visible children in a strip of this width, and the size they need.</summary>
    private (List<(Control Child, Point Origin)> Placed, Size Size) Place(double width)
    {
        var visible = Children.Where(c => c.IsVisible).ToList();
        var placed = new List<(Control, Point)>();
        var total = visible.Sum(c => c.DesiredSize.Width);

        if (total <= width)
        {
            var height = visible.Count == 0 ? 0 : visible.Max(c => c.DesiredSize.Height);
            var left = 0.0;
            var right = width;
            if (double.IsInfinity(width))
                right = total;
            foreach (var child in visible.Where(c => !GetIsUtility(c)))
            {
                placed.Add((child, new Point(left, 0)));
                left += child.DesiredSize.Width;
            }
            foreach (var child in visible.Where(GetIsUtility).Reverse())
            {
                right -= child.DesiredSize.Width;
                placed.Add((child, new Point(right, 0)));
            }
            return (placed, new Size(total, height));
        }

        double x = 0, y = 0, rowHeight = 0, widest = 0;
        foreach (var child in visible)
        {
            var w = child.DesiredSize.Width;
            if (x > 0 && x + w > width)
            {
                y += rowHeight;
                x = 0;
                rowHeight = 0;
            }
            placed.Add((child, new Point(x, y)));
            x += w;
            widest = Math.Max(widest, x);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }
        return (placed, new Size(widest, y + rowHeight));
    }
}
