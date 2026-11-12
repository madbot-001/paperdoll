using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App.Controls;

/// <summary>
/// Colour swatch that opens the colour sliders (<see cref="ColorSliders"/>) with recent colours.
/// The callback fires with <c>true</c> while the colour changes, then once more with <c>false</c> on close.
/// </summary>
public static class ColorPicker
{
    private static readonly List<Rgba> Recent = [];

    /// <param name="key">The field whose slider positions are kept, as <see cref="ColorSliders.Key"/>.</param>
    public static Button Swatch(Rgba color, Action<Rgba, bool> changed, Func<Rgba, string?> describe, string? key = null, double size = 14)
    {
        var current = color;
        var button = new Button
        {
            Width = size + 4,
            Height = size + 4,
            MinHeight = 0,
            Padding = new Thickness(0),
            Margin = new Thickness(2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brush(color),
            BorderBrush = new SolidColorBrush(Color.Parse("#8D97A3")),
            CornerRadius = new CornerRadius(0),
        };
        ToolTip.SetTip(button, "Pick a colour");

        var recent = new WrapPanel { MaxWidth = 200 };
        var changedWhileOpen = false;

        void Pick(Rgba c)
        {
            current = c;
            button.Background = Brush(c);
            changedWhileOpen = true;
            changed(c, true);
        }

        var sliders = new ColorSliders(color, (c, _) => Pick(c), describe, key) { Width = 260 };
        var recentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(2, 0) };
        recentRow.Children.Add(new TextBlock { Text = "Recent", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" } });
        recentRow.Children.Add(recent);
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(4) };
        panel.Children.Add(sliders);
        panel.Children.Add(recentRow);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opened += (_, _) =>
        {
            changedWhileOpen = false;
            recent.Children.Clear();
            foreach (var r in Recent)
            {
                var chip = new Button { Width = 16, Height = 16, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(1), Background = Brush(r), CornerRadius = new CornerRadius(0) };
                ToolTip.SetTip(chip, r.ToHex()[..7]);
                chip.Click += (_, _) =>
                {
                    sliders.SetColor(r);
                    Pick(r);
                };
                recent.Children.Add(chip);
            }
        };
        // A box being typed in is taken off the screen before it loses focus, so commit it first.
        flyout.Closing += (_, _) => sliders.CommitTyping();
        flyout.Closed += (_, _) =>
        {
            if (!changedWhileOpen)
                return;
            Recent.RemoveAll(r => r == current);
            Recent.Insert(0, current);
            if (Recent.Count > 10)
                Recent.RemoveAt(Recent.Count - 1);
            changed(current, false);
        };
        button.Flyout = flyout;
        return button;
    }

    public static IBrush Brush(Rgba color) =>
        new SolidColorBrush(Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)));
}
