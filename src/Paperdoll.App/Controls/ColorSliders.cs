// The layout and the gradient tracks follow RobustToolbox's colour sliders
// (Robust.Client/UserInterface/Controls/ColorSelectorSliders.cs and ColorSelectorStyleBox.cs),
// Copyright (c) 2019 Space Station 14 Contributors, MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App.Controls;

/// <summary>How the colour sliders show a colour: hue, saturation and value, or red, green and blue.</summary>
public enum ColorMode
{
    Hsv,
    Rgb,
}

/// <summary>One slider row: its letter, the slider, the − button, the number and the + button.</summary>
public sealed record ColorSliderRow(TextBlock Label, Slider Slider, Button Minus, TextBox Number, Button Plus);

/// <summary>
/// The lobby's colour sliders: a square of the colour, the HSV / RGB switch and the colour's
/// name; three rows, each a slider whose track shows the colours it leads to, with − and + and
/// the number; and the hex. <c>changed</c> gets each new colour, with <c>true</c> while a slider
/// is dragged and <c>false</c> once when it is let go, for a click on − or +, a typed number, a
/// hex, or a slider moved by keys.
/// </summary>
public sealed class ColorSliders : StackPanel
{
    private static ColorMode _mode;

    /// <summary>The HSV / RGB switch, one for the whole app: every set of sliders follows it.</summary>
    public static ColorMode Mode
    {
        get => _mode;
        set
        {
            if (value == _mode)
                return;
            _mode = value;
            ModeChanged?.Invoke(value);
        }
    }

    public static event Action<ColorMode>? ModeChanged;

    // Slider positions with the colour they gave, per field and mode. A grey has no hue of its
    // own, so positions are kept for as long as they still give the field's colour; then the
    // hue stays where it was when the page is built again.
    private sealed record Kept(int[] Values, string Color);
    private static readonly Dictionary<(string Key, ColorMode Mode), Kept> KeptByField = [];
    private readonly Dictionary<ColorMode, Kept> _kept = [];

    private static readonly int[] HsvMaximum = [360, 100, 100];
    private static readonly int[] RgbMaximum = [255, 255, 255];

    private readonly Action<Rgba, bool> _changed;
    private readonly Func<Rgba, string?> _describe;
    private readonly Action<Action, bool>? _typing;
    private readonly Border _square;
    private readonly int[] _values = new int[3];
    private Rgba _color;
    private ColorMode _shown;
    private bool _updating;
    // A slider is held with the mouse, and whether it has moved since it was taken.
    private bool _dragging;
    private bool _movedInDrag;
    // The slider whose track was pressed, which follows the pointer until it is let go.
    private Slider? _trackDrag;
    // Each box's commit, for CommitTyping.
    private readonly List<Action> _commits = [];

    /// <summary>The field the positions are kept for ("eyes"), or null to keep them only here.</summary>
    public string? Key { get; }

    public Rgba Color => _color;

    public ComboBox ModeBox { get; }

    public TextBlock NameText { get; }

    public IReadOnlyList<ColorSliderRow> Rows { get; }

    public TextBox HexBox { get; }

    /// <param name="describe">Names a colour, or gives null where the fork has no names.</param>
    /// <param name="typing">
    /// Told the commit of a box when it gets focus (<c>true</c>) and when it loses it, so the
    /// window can commit what is being typed before saving.
    /// </param>
    public ColorSliders(Rgba color, Action<Rgba, bool> changed, Func<Rgba, string?> describe, string? key = null, Action<Action, bool>? typing = null)
    {
        _color = color;
        _changed = changed;
        _describe = describe;
        _typing = typing;
        Key = key;
        Spacing = 2;
        Margin = new Thickness(2, 2, 4, 2);

        _square = new Border
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(2, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#8D97A3")),
            BorderThickness = new Thickness(1),
        };
        ModeBox = new ComboBox { ItemsSource = new[] { "HSV", "RGB" }, Width = 64, VerticalAlignment = VerticalAlignment.Center };
        ModeBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && ModeBox.SelectedIndex >= 0)
                Mode = ModeBox.SelectedIndex == 1 ? ColorMode.Rgb : ColorMode.Hsv;
        };
        NameText = new TextBlock { Classes = { "muted" }, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var header = new DockPanel();
        DockPanel.SetDock(_square, Dock.Left);
        DockPanel.SetDock(ModeBox, Dock.Left);
        header.Children.Add(_square);
        header.Children.Add(ModeBox);
        header.Children.Add(NameText);
        Children.Add(header);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*,Auto,40,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        var rows = new List<ColorSliderRow>();
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            var row = new ColorSliderRow(
                new TextBlock { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center },
                new Slider { Classes = { "color" }, Minimum = 0, SmallChange = 1, LargeChange = 10, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 0, 4, 0) },
                StepButton("−", "One less"),
                new TextBox { Classes = { "mono" }, MinHeight = 18, Padding = new Thickness(3, 0), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 1) },
                StepButton("+", "One more"));
            row.Slider.ValueChanged += (_, e) =>
            {
                if (_updating)
                    return;
                _values[index] = (int)Math.Round(e.NewValue);
                _movedInDrag |= _dragging;
                Changed(live: _dragging);
            };
            // Thumb and track handle the mouse themselves, so their events are seen when handled too.
            row.Slider.AddHandler(PointerPressedEvent, (_, e) => Press(row.Slider, e), RoutingStrategies.Tunnel, handledEventsToo: true);
            row.Slider.AddHandler(PointerMovedEvent, (_, e) =>
            {
                if (_trackDrag == row.Slider)
                    MoveTo(row.Slider, e);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            row.Slider.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (_trackDrag == row.Slider)
                    e.Pointer.Capture(null);
                EndDrag();
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            row.Slider.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
            row.Minus.Click += (_, _) => Step(index, -1);
            row.Plus.Click += (_, _) => Step(index, 1);
            CommitOnEnterOrLeave(row.Number, text => CommitNumber(index, text), () => _values[index].ToString(CultureInfo.InvariantCulture));
            Place(grid, i, 0, row.Label);
            Place(grid, i, 1, row.Slider);
            Place(grid, i, 2, row.Minus);
            Place(grid, i, 3, row.Number);
            Place(grid, i, 4, row.Plus);
            rows.Add(row);
        }
        Rows = rows;

        HexBox = new TextBox { Classes = { "mono" }, Width = 84, MinHeight = 18, Padding = new Thickness(3, 0), VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1) };
        CommitOnEnterOrLeave(HexBox, CommitHex, () => _color.ToHex()[..7]);
        var hexLabel = new TextBlock { Text = "Hex", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        hexRow.Children.Add(hexLabel);
        hexRow.Children.Add(HexBox);
        Grid.SetColumnSpan(hexRow, 5);
        Place(grid, 3, 0, hexRow);
        Children.Add(grid);

        ShowMode();
    }

    /// <summary>
    /// Commits what is being typed in a box, as leaving it would; for a pop-up about to close,
    /// whose boxes are taken off the screen before they lose focus.
    /// </summary>
    public void CommitTyping()
    {
        foreach (var commit in _commits)
            commit();
    }

    /// <summary>Shows another colour, as when a recent colour is picked; nothing is reported.</summary>
    public void SetColor(Rgba color)
    {
        _color = color;
        Resolve();
        Show();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ModeChanged += OnModeChanged;
        // The mode may have changed while this was not shown (a closed pop-up).
        if (_shown != Mode)
            ShowMode();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ModeChanged -= OnModeChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnModeChanged(ColorMode mode) => ShowMode();

    private int[] Maximum => _shown == ColorMode.Rgb ? RgbMaximum : HsvMaximum;

    private void ShowMode()
    {
        _shown = Mode == ColorMode.Rgb ? ColorMode.Rgb : ColorMode.Hsv;
        var labels = _shown == ColorMode.Rgb ? new[] { "R", "G", "B" } : ["H", "S", "V"];
        _updating = true;
        for (var i = 0; i < 3; i++)
        {
            Rows[i].Label.Text = labels[i];
            Rows[i].Slider.Maximum = Maximum[i];
        }
        _updating = false;
        Resolve();
        Show();
    }

    // The positions for the colour: kept ones if they still give it, else the colour's own.
    private void Resolve()
    {
        var hex = _color.ToHex();
        int[] values;
        if (Key != null && KeptByField.TryGetValue((Key, _shown), out var field) && field.Color == hex)
            values = field.Values;
        else if (_kept.TryGetValue(_shown, out var mine) && mine.Color == hex)
            values = mine.Values;
        else if (_shown == ColorMode.Rgb)
            values = [Channel(_color.R, 255), Channel(_color.G, 255), Channel(_color.B, 255)];
        else
        {
            var (h, s, v, _) = _color.ToHsv();
            values = [Channel(h, 360), Channel(s, 100), Channel(v, 100)];
        }
        values.CopyTo(_values, 0);
    }

    private static int Channel(float value, int maximum) => (int)Math.Round(Math.Clamp(value, 0f, 1f) * maximum);

    private Rgba FromValues(int a, int b, int c, float alpha = 1f) => _shown == ColorMode.Rgb
        ? new Rgba(a / 255f, b / 255f, c / 255f, alpha)
        : Rgba.FromHsv(a / 360f, b / 100f, c / 100f, alpha);

    private void Changed(bool live)
    {
        _color = FromValues(_values[0], _values[1], _values[2], _color.A);
        var kept = new Kept((int[])_values.Clone(), _color.ToHex());
        _kept[_shown] = kept;
        if (Key != null)
            KeptByField[(Key, _shown)] = kept;
        Show();
        _changed(_color, live);
    }

    // A press starts a drag. On the track away from the thumb it moves the thumb there, as the
    // game's sliders do, and the thumb then follows the pointer until it is let go.
    private void Press(Slider slider, PointerPressedEventArgs e)
    {
        (_dragging, _movedInDrag) = (true, false);
        if (!e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed || (e.Source as Visual)?.FindAncestorOfType<Thumb>(includeSelf: true) != null)
            return;
        _trackDrag = slider;
        e.Pointer.Capture(slider);
        MoveTo(slider, e);
        e.Handled = true;
    }

    // The value under the pointer. The thumb's centre runs from 4 to the width less 4 (half the
    // thumb of Slider.color in Steel.axaml), as the track is drawn.
    private static void MoveTo(Slider slider, PointerEventArgs e)
    {
        var share = Math.Clamp((e.GetPosition(slider).X - 4) / Math.Max(1, slider.Bounds.Width - 8), 0, 1);
        slider.Value = Math.Round(share * slider.Maximum);
    }

    // Letting go of a dragged slider commits the colour it was left at, once.
    private void EndDrag()
    {
        _trackDrag = null;
        if (!_dragging)
            return;
        _dragging = false;
        if (!_movedInDrag)
            return;
        _movedInDrag = false;
        _changed(_color, false);
    }

    private void Step(int index, int by)
    {
        var value = Math.Clamp(_values[index] + by, 0, Maximum[index]);
        if (value == _values[index])
            return;
        _values[index] = value;
        Changed(live: false);
    }

    // A number past the range is taken as the nearest end; text that is no number is dropped.
    private void CommitNumber(int index, string text)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
        {
            Show();
            return;
        }
        var value = (int)Math.Clamp(Math.Round(number), 0, Maximum[index]);
        if (value == _values[index])
        {
            Show();
            return;
        }
        _values[index] = value;
        Changed(live: false);
    }

    // The hex keeps the colour's alpha, as the game's does when it shows no alpha slider.
    private void CommitHex(string text)
    {
        if (!Rgba.TryParse(text.Trim(), out var parsed) || (parsed with { A = _color.A }).ToHex() == _color.ToHex())
        {
            Show();
            return;
        }
        _color = parsed with { A = _color.A };
        Resolve();
        Show();
        _changed(_color, false);
    }

    private void Show()
    {
        _updating = true;
        for (var i = 0; i < 3; i++)
        {
            Rows[i].Slider.Value = _values[i];
            Rows[i].Number.Text = _values[i].ToString(CultureInfo.InvariantCulture);
            Rows[i].Slider.Background = Track(i);
        }
        _square.Background = ColorPicker.Brush(_color.WithAlpha(1));
        NameText.Text = _describe(_color) ?? "";
        HexBox.Text = _color.ToHex()[..7];
        ModeBox.SelectedIndex = _shown == ColorMode.Rgb ? 1 : 0;
        _updating = false;
    }

    // The colours a slider leads to from the current ones: its own value from 0 to the top, the
    // others as they are. Hue goes round in six straight pieces; the rest are straight in RGB.
    private LinearGradientBrush Track(int index)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        };
        var steps = _shown == ColorMode.Hsv && index == 0 ? 6 : 1;
        for (var step = 0; step <= steps; step++)
        {
            var values = (int[])_values.Clone();
            values[index] = Maximum[index] * step / steps;
            var color = FromValues(values[0], values[1], values[2]);
            brush.GradientStops.Add(new GradientStop(Avalonia.Media.Color.FromRgb((byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255)), (double)step / steps));
        }
        return brush;
    }

    private static Button StepButton(string text, string tip)
    {
        var button = new Button
        {
            Classes = { "small" },
            Content = text,
            Width = 18,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static void Place(Grid grid, int row, int column, Control control)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    // Typing is committed on Enter or on leaving the box, when it differs from what the box was
    // shown. A box no longer on the page commits nothing: what it holds was never seen.
    private void CommitOnEnterOrLeave(TextBox box, Action<string> commit, Func<string> shown)
    {
        void Commit()
        {
            if (box.IsAttachedToVisualTree() && (box.Text ?? "") != shown())
                commit(box.Text ?? "");
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
                Commit();
        };
        _commits.Add(Commit);
        box.GotFocus += (_, _) => _typing?.Invoke(Commit, true);
        box.LostFocus += (_, _) =>
        {
            _typing?.Invoke(Commit, false);
            Commit();
        };
    }
}
