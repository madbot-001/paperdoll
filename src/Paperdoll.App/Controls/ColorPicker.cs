using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App.Controls;

/// <summary>
/// Colour swatch that opens a hue/saturation/value picker with a hex field and recent colours.
/// The callback fires with <c>true</c> while dragging, then once more with <c>false</c> on close.
/// </summary>
public static class ColorPicker
{
    private static readonly List<Rgba> Recent = [];

    public static Button Swatch(Rgba color, Action<Rgba, bool> changed, double size = 14)
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

        var (h, s, v, a) = color.ToHsv();
        var hue = Slider(0, 360, h * 360);
        var sat = Slider(0, 100, s * 100);
        var val = Slider(0, 100, v * 100);
        var preview = new Border { Height = 28, BorderBrush = button.BorderBrush, BorderThickness = new Thickness(1), Background = Brush(color) };
        var hex = new TextBox { Text = color.ToHex()[..7], Classes = { "mono" }, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var recent = new WrapPanel { MaxWidth = 170 };
        var updating = false;
        var changedWhileOpen = false;

        void Show(Rgba c, bool fromSliders)
        {
            current = c;
            updating = true;
            preview.Background = Brush(c);
            button.Background = Brush(c);
            if (!fromSliders)
            {
                var (hh, ss, vv, _) = c.ToHsv();
                hue.Value = hh * 360;
                sat.Value = ss * 100;
                val.Value = vv * 100;
            }
            hex.Text = c.ToHex()[..7];
            updating = false;
        }

        void Pick(Rgba c, bool fromSliders)
        {
            Show(c, fromSliders);
            changedWhileOpen = true;
            changed(c, true);
        }

        void FromSliders()
        {
            if (!updating)
                Pick(Rgba.FromHsv((float)(hue.Value / 360), (float)(sat.Value / 100), (float)(val.Value / 100), a), fromSliders: true);
        }
        hue.ValueChanged += (_, _) => FromSliders();
        sat.ValueChanged += (_, _) => FromSliders();
        val.ValueChanged += (_, _) => FromSliders();
        hex.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && Rgba.TryParse(hex.Text?.Trim(), out var parsed))
                Pick(parsed, fromSliders: false);
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), Width = 240 };
        AddRow(grid, 0, "Hue", hue);
        AddRow(grid, 1, "Saturation", sat);
        AddRow(grid, 2, "Value", val);
        AddRow(grid, 3, "Hex", hex);
        AddRow(grid, 4, "Recent", recent);
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(6) };
        panel.Children.Add(preview);
        panel.Children.Add(grid);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opened += (_, _) =>
        {
            changedWhileOpen = false;
            recent.Children.Clear();
            foreach (var r in Recent)
            {
                var chip = new Button { Width = 16, Height = 16, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(1), Background = Brush(r), CornerRadius = new CornerRadius(0) };
                ToolTip.SetTip(chip, r.ToHex()[..7]);
                chip.Click += (_, _) => Pick(r, fromSliders: false);
                recent.Children.Add(chip);
            }
        };
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

    private static Slider Slider(double min, double max, double value) =>
        new() { Minimum = min, Maximum = max, Value = value, MinHeight = 18, Width = 160 };

    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Classes = { "hint" } };
        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    public static IBrush Brush(Rgba color) =>
        new SolidColorBrush(Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)));
}
