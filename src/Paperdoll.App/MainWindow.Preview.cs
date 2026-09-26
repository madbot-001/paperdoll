using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Paperdoll.App.Preview;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App;

public partial class MainWindow
{
    private static readonly string[] ZoomLevels = ["2x", "3x", "4x", "6x", "8x", "10x"];
    private static readonly string[] Facings = ["South", "North", "East", "West"];

    private int _zoom = 6;
    private Direction _direction = Direction.South;
    private readonly Border[] _facingCells = new Border[4];
    private readonly Image[] _facingImages = new Image[4];
    private readonly Dictionary<string, Bitmap> _portraits = [];

    private void SetUpPreview()
    {
        ZoomBox.ItemsSource = ZoomLevels;
        ZoomBox.SelectedItem = "6x";

        for (var i = 0; i < 4; i++)
        {
            var direction = (Direction)i;
            var image = new Image { Width = 72, Height = 72, Stretch = Avalonia.Media.Stretch.None };
            Avalonia.Media.RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(image);
            stack.Children.Add(new TextBlock { Text = Facings[i] });
            var cell = new Border { Classes = { "facing" }, Child = stack };
            ToolTip.SetTip(cell, $"Show facing {Facings[i].ToLowerInvariant()}");
            cell.PointerPressed += (_, _) =>
            {
                _direction = direction;
                RefreshPreview();
            };
            _facingCells[i] = cell;
            _facingImages[i] = image;
            FacingStrip.Children.Add(cell);
        }
    }

    private List<(string Id, string Name)> _jobs = [];

    /// <summary>Fills the job list for the loaded fork and selects the job being shown.</summary>
    private void BuildJobList()
    {
        var session = _session!;
        _jobs = session.Content!.Outfits.SelectableJobs()
            .Select(j => (j.Id, session.Content.Strings.Get(j.NameKey)))
            .OrderBy(j => j.Item2, StringComparer.CurrentCulture)
            .ToList();
        var wasRefreshing = _refreshing;
        _refreshing = true;
        JobBox.ItemsSource = _jobs.Select(j => j.Name).ToList();
        SelectDressedJob();
        _refreshing = wasRefreshing;
    }

    private void SelectDressedJob()
    {
        var dressed = _session?.DressedJob();
        JobBox.SelectedIndex = _jobs.FindIndex(j => j.Id == dressed);
    }

    private void OnJobChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _session == null || JobBox.SelectedIndex < 0)
            return;
        _session.PreviewJob = _jobs[JobBox.SelectedIndex].Id;
        RefreshAll();
    }

    private void OnClothesToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_session == null)
            return;
        _session.ShowClothes = ClothesBox.IsChecked == true;
        if (!_refreshing)
            RefreshAll();
    }

    private void OnZoomChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (ZoomBox.SelectedItem is string text && int.TryParse(text.TrimEnd('x'), out var zoom))
        {
            _zoom = zoom;
            RefreshPreview();
        }
    }

    private void OnPreviewResized(object? sender, SizeChangedEventArgs e) => RefreshPreview();

    /// <summary>Draws the character on the floor canvas and in the four facing cells.</summary>
    private void RefreshPreview()
    {
        if (_session?.Look == null)
            return;
        var width = (int)PreviewHost.Bounds.Width;
        var height = (int)PreviewHost.Bounds.Height;
        if (width < 16 || height < 16)
            (width, height) = (640, 420);

        var scale = _session.SpriteScale();
        using (var sprite = _session.Render(_direction))
        using (var canvas = FloorCanvas.Compose(width, height, _zoom, sprite, scale))
            Replace(PreviewImage, FloorCanvas.ToAvalonia(canvas));

        for (var i = 0; i < 4; i++)
        {
            using var sprite = _session.Render((Direction)i);
            using var canvas = FloorCanvas.Compose(72, 72, 2, sprite, scale);
            Replace(_facingImages[i], FloorCanvas.ToAvalonia(canvas));
            _facingCells[i].Classes.Set("chosen", (Direction)i == _direction);
        }
    }

    /// <summary>A small picture of each species as a new character, for the species table.</summary>
    private void BuildPortraits()
    {
        foreach (var bitmap in _portraits.Values)
            bitmap.Dispose();
        _portraits.Clear();

        var catalog = _session!.Content!.Characters;
        foreach (var species in _session.Selectable())
        {
            try
            {
                var look = LookDefaults.Create(catalog, species.Id, species.Sexes[0], catalog.DefaultSkin(species), Rgba.Parse("#000000"));
                using var sprite = _session.Renderer!.Render(look);
                var scale = Core.Profiles.CharacterSize.HasHeight(_session.Fork!) ? species.BaseScale : (1f, 1f);
                using var canvas = FloorCanvas.Compose(36, 36, 1, sprite, scale);
                _portraits[species.Id] = FloorCanvas.ToAvalonia(canvas);
            }
            catch (Exception)
            {
                // A species that cannot be drawn simply has no portrait.
            }
        }
    }

    private static void Replace(Image image, Bitmap bitmap)
    {
        var old = image.Source as IDisposable;
        image.Source = bitmap;
        old?.Dispose();
    }
}
