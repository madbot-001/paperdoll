using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Paperdoll.Core.Characters;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App;

public partial class MainWindow
{
    private static readonly (string Value, string Label)[] Pronouns =
    [
        ("Epicene", "They / them"), ("Male", "He / him"), ("Female", "She / her"), ("Neuter", "It / its"),
    ];

    private void BuildProperties()
    {
        var session = _session!;
        var file = session.File!;
        var species = session.Content!.Characters.Species[session.Look!.Species];
        PropertyGrid.Children.Clear();
        PropertyGrid.RowDefinitions.Clear();

        var name = new TextBox { Text = file.Name ?? "", PlaceholderText = "Name" };
        CommitOnEnterOrLeave(name, text => Apply(s => s.Edit(f => f.Name = text)));
        AddRow("Name", name);

        AddRow("Species", new TextBlock { Text = session.DisplayName(species), Margin = new Thickness(3, 2), VerticalAlignment = VerticalAlignment.Center });

        var sex = new ComboBox { ItemsSource = species.Sexes, SelectedItem = file.Sex, HorizontalAlignment = HorizontalAlignment.Stretch };
        sex.SelectionChanged += (_, _) =>
        {
            if (sex.SelectedItem is string value && value != session.File?.Sex)
                Apply(s => s.Edit(f => f.Sex = value));
        };
        AddRow("Sex", sex);

        var pronouns = new ComboBox
        {
            ItemsSource = Pronouns.Select(p => p.Label).ToList(),
            SelectedIndex = Array.FindIndex(Pronouns, p => p.Value == file.Gender),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        pronouns.SelectionChanged += (_, _) =>
        {
            if (pronouns.SelectedIndex >= 0 && Pronouns[pronouns.SelectedIndex].Value != session.File?.Gender)
                Apply(s => s.Edit(f => f.Gender = Pronouns[pronouns.SelectedIndex].Value));
        };
        AddRow("Pronouns", pronouns);

        var age = new NumericUpDown
        {
            Minimum = species.MinAge,
            Maximum = species.MaxAge,
            Value = file.Age ?? species.MinAge,
            Increment = 1,
            FormatString = "0",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 20,
            Padding = new Thickness(3, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        age.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } value && (int)value != session.File?.Age)
                Apply(s => s.Edit(f => f.Age = (int)value));
        };
        AddRow("Age", age);

        var rule = session.Content.Characters.SkinRuleFor(species);
        if (rule.IsUnary)
        {
            var tone = new Slider { Minimum = 0, Maximum = 100, Value = rule.ToUnary(session.Look.SkinColor), MinHeight = 18 };
            var panel = new DockPanel();
            var swatch = (Border)Swatch(session.Look.SkinColor);
            DockPanel.SetDock(swatch, Dock.Right);
            panel.Children.Add(swatch);
            panel.Children.Add(tone);
            tone.ValueChanged += (_, e) =>
            {
                Apply(s => s.SetSkin(rule.FromUnary((float)e.NewValue)), keepProperties: true);
                swatch.Background = ((Border)Swatch(session.Look!.SkinColor)).Background;
            };
            AddRow("Skin tone", panel);
        }
        else
            AddRow("Skin colour", ColorField(session.Look.SkinColor, color => Apply(s => s.SetSkin(color))));

        AddRow("Eye colour", ColorField(session.Look.EyeColor, color => Apply(s => s.SetEyes(color))));

        var description = new TextBox
        {
            Text = file.FlavorText ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 54,
            PlaceholderText = "Description",
        };
        description.LostFocus += (_, _) =>
        {
            if (description.Text != (session.File?.FlavorText ?? ""))
                Apply(s => s.Edit(f => f.FlavorText = description.Text ?? ""));
        };
        AddRow("Description", description);
    }

    private void AddRow(string label, Control editor)
    {
        var row = PropertyGrid.RowDefinitions.Count;
        PropertyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var labelCell = new Border { Classes = { "label" }, Child = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } };
        var valueCell = new Border { Classes = { "value" }, Child = editor };
        Grid.SetRow(labelCell, row);
        Grid.SetRow(valueCell, row);
        Grid.SetColumn(valueCell, 1);
        PropertyGrid.Children.Add(labelCell);
        PropertyGrid.Children.Add(valueCell);
    }

    private void BuildMarkings()
    {
        var session = _session!;
        var look = session.Look!;
        var species = session.Content!.Characters.Species[look.Species];
        MarkingsPanel.Children.Clear();

        foreach (var organ in species.Organs.Where(o => o.MarkingGroup != null))
        {
            foreach (var layer in organ.MarkingLayers)
            {
                var available = session.AvailableMarkings(organ, layer);
                var applied = look.Markings.TryGetValue(organ.Category, out var byLayer) && byLayer.TryGetValue(layer, out var list) ? list : [];
                var limit = session.LayerLimit(organ, layer);
                if (applied.Count == 0 && (available.Count == 0 || limit == 0))
                    continue;

                MarkingsPanel.Children.Add(new Border
                {
                    Classes = { "group" },
                    Child = new TextBlock { Text = $"{Words(organ.Category)}: {Words(layer)}" + (limit is { } l ? $"   ({applied.Count} of {l})" : "") },
                });

                for (var i = 0; i < applied.Count; i++)
                    MarkingsPanel.Children.Add(MarkingRow(organ.Category, layer, i, applied[i]));

                if (available.Count > 0 && (limit == null || applied.Count < limit))
                {
                    var add = new ComboBox
                    {
                        PlaceholderText = "Add...",
                        ItemsSource = available.Select(m => session.MarkingName(m.Id)).ToList(),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(12, 1, 1, 3),
                    };
                    add.SelectionChanged += (_, _) =>
                    {
                        if (add.SelectedIndex >= 0)
                            Apply(s => s.AddMarking(organ.Category, layer, available[add.SelectedIndex].Id));
                    };
                    MarkingsPanel.Children.Add(add);
                }
            }
        }
    }

    private Control MarkingRow(string organ, string layer, int index, MarkingEntry entry)
    {
        var row = new DockPanel { Margin = new Thickness(12, 1, 1, 1) };
        var remove = new Button { Content = "x", Padding = new Thickness(4, 0), MinHeight = 16, Height = 16, FontSize = 10 };
        ToolTip.SetTip(remove, "Remove this marking");
        remove.Click += (_, _) => Apply(s => s.RemoveMarking(organ, layer, index));
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove);

        var colors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (var c = 0; c < entry.Colors.Count; c++)
        {
            var colorIndex = c;
            colors.Children.Add(ColorField(entry.Colors[c], color => Apply(s => s.SetMarkingColor(organ, layer, index, colorIndex, color)), compact: true));
        }
        DockPanel.SetDock(colors, Dock.Right);
        row.Children.Add(colors);

        row.Children.Add(new TextBlock
        {
            Text = _session!.MarkingName(entry.Id),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return row;
    }

    /// <summary>A colour swatch with its hex value; Enter or leaving the field applies it.</summary>
    private static Control ColorField(Rgba color, Action<Rgba> changed, bool compact = false)
    {
        var text = new TextBox { Text = color.ToHex()[..7], Width = compact ? 62 : 80, FontSize = compact ? 10 : 12, MinHeight = compact ? 16 : 20, Padding = new Thickness(2, 0) };
        CommitOnEnterOrLeave(text, value =>
        {
            if (Rgba.TryParse(value.Trim(), out var parsed))
                changed(parsed);
        });
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        panel.Children.Add(Swatch(color));
        panel.Children.Add(text);
        return panel;
    }

    private static Control Swatch(Rgba color) => new Border
    {
        Width = 14,
        Height = 14,
        Margin = new Thickness(2, 0),
        VerticalAlignment = VerticalAlignment.Center,
        BorderBrush = Brushes.Black,
        BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255))),
    };

    private static void CommitOnEnterOrLeave(TextBox box, Action<string> commit)
    {
        var original = box.Text ?? "";
        void Commit()
        {
            if ((box.Text ?? "") != original)
            {
                original = box.Text ?? "";
                commit(original);
            }
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Commit();
        };
        box.LostFocus += (_, _) => Commit();
    }

    // "FacialHair" -> "Facial hair", "ArmLeft" -> "Arm left"
    private static string Words(string id)
    {
        var words = Regex.Replace(id, "(?<=[a-z])(?=[A-Z])", " ");
        return words.Length == 0 ? words : words[0] + words[1..].ToLowerInvariant();
    }
}
