using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paperdoll.App.Preview;
using SkiaSharp;

namespace Paperdoll.App;

/// <summary>What the share window pictures: the character facing each way (front first), and the words to go with it.</summary>
/// <param name="Room">The demo room the preview shows, when it shows one.</param>
public sealed record ShareRequest(IReadOnlyList<SKBitmap> Sides, (float X, float Y) Scale, ShareText Text, string FullCredits, string FileName, ShareRoom? Room = null);

/// <summary>
/// The demo room to put behind the character, as drawn, and the credit line and full credits that
/// then name the room's pictures drawn, given those pictures.
/// </summary>
public sealed record ShareRoom(Core.Rooms.RoomScene Scene, Core.Rooms.RoomPicture Picture, Func<IReadOnlyList<Core.Editing.CreditLine>, string> Credit,
    Func<IReadOnlyList<Core.Editing.CreditLine>, string> FullCredits);

/// <summary>
/// Makes a picture of the character to share: saved as a PNG or copied, never sent anywhere by
/// Paperdoll.
/// </summary>
public partial class ShareWindow : Window
{
    private static readonly int[] Zooms = SharePicture.Zooms;
    // The Room shown list's items, as ShareOptions.RoomTiles.
    private static readonly int[] RoomViews = [0, 3, 5, 7, ShareOptions.WholeRoom];
    private static readonly FilePickerFileType PngFiles = new("PNG pictures") { Patterns = ["*.png"], MimeTypes = ["image/png"] };

    private readonly ShareRequest? _request;
    private readonly Action<ShareOptions>? _remember;
    private ShareOptions _options = new();
    private SKBitmap? _picture;
    private Avalonia.Media.Imaging.Bitmap? _shown;
    // The full credits of the picture as drawn, which name the floor tiles in it.
    private string _fullCredits = "";
    private bool _reading;

    public ShareWindow() => InitializeComponent();

    public ShareWindow(ShareRequest request, ShareOptions options, Action<ShareOptions> remember) : this()
    {
        _request = request;
        // A background no longer offered, from older settings, is the plates.
        if (!Enum.IsDefined(options.Background))
            options = options with { Background = ShareBackground.Floor };
        _options = options;
        _remember = remember;

        _reading = true;
        AllSidesButton.IsChecked = options.AllSides;
        FrontOnlyButton.IsChecked = !options.AllSides;
        SizeBox.ItemsSource = Zooms.Select(z => $"{z}× ({32 * z} pixels a tile)").ToList();
        SizeBox.SelectedIndex = Math.Max(0, Array.IndexOf(Zooms, options.Zoom));
        // The demo room is offered only while the preview shows the room; otherwise the plates stand in.
        FloorButton.IsChecked = options.Background == ShareBackground.Floor || (options.Background == ShareBackground.Room && request.Room == null);
        PlainButton.IsChecked = options.Background == ShareBackground.Plain;
        TransparentButton.IsChecked = options.Background == ShareBackground.Transparent;
        RoomButton.IsVisible = request.Room != null;
        RoomButton.IsChecked = options.Background == ShareBackground.Room && request.Room != null;
        RoomShownText.IsVisible = RoomBox.IsVisible = request.Room != null;
        RoomBox.ItemsSource = new[] { "Close", "3 tiles", "5 tiles", "7 tiles", "Whole room" };
        RoomBox.SelectedIndex = Math.Max(0, Array.IndexOf(RoomViews, options.RoomTiles));
        RoomBox.IsEnabled = RoomButton.IsChecked == true;
        CaptionBox.IsChecked = options.Caption;
        CreditBox.IsChecked = options.Credit;
        _reading = false;

        Redraw();
        Closed += (_, _) =>
        {
            foreach (var side in request.Sides)
                side.Dispose();
            _picture?.Dispose();
            _shown?.Dispose();
        };
    }

    private void OnOptionChanged(object? sender, RoutedEventArgs e) => ReadOptions();

    private void OnSizeChanged(object? sender, SelectionChangedEventArgs e) => ReadOptions();

    private void ReadOptions()
    {
        if (_reading || _request == null)
            return;
        _options = new ShareOptions(
            AllSidesButton.IsChecked == true,
            Zooms[Math.Max(0, SizeBox.SelectedIndex)],
            TransparentButton.IsChecked == true ? ShareBackground.Transparent : PlainButton.IsChecked == true ? ShareBackground.Plain
                : RoomButton.IsChecked == true ? ShareBackground.Room : ShareBackground.Floor,
            CaptionBox.IsChecked == true,
            CreditBox.IsChecked == true,
            RoomViews[Math.Max(0, RoomBox.SelectedIndex)]);
        RoomBox.IsEnabled = RoomButton.IsChecked == true;
        _remember?.Invoke(_options);
        Redraw();
    }

    private void Redraw()
    {
        var sides = _options.AllSides ? _request!.Sides : [_request!.Sides[0]];
        _picture?.Dispose();
        var room = _options.Background == ShareBackground.Room ? _request.Room : null;
        var tiles = room != null ? SharePicture.RoomCredits(sides, _request.Scale, _options, room.Scene, room.Picture) : [];
        _fullCredits = room?.FullCredits(tiles) ?? _request.FullCredits;
        var credit = room?.Credit(tiles);
        _picture = SharePicture.Compose(sides, _request.Scale, _options, credit == null ? _request.Text : _request.Text with { Credit = credit }, room?.Picture);
        var shown = FloorCanvas.ToAvalonia(_picture);
        PictureImage.Source = shown;
        _shown?.Dispose();
        _shown = shown;
        // A wide view of the room may be drawn smaller than asked, to keep the picture within bounds.
        var zoom = SharePicture.Panel(sides, _request.Scale, _options, room?.Picture).Zoom;
        SizeText.Text = $"{_picture.Width} × {_picture.Height} pixels"
            + (zoom < Math.Clamp(_options.Zoom, 1, 16) ? $" (drawn at {zoom}× to stay within {SharePicture.MaxSide} pixels)" : "");
    }

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard == null || _shown == null)
                throw new InvalidOperationException("there is no clipboard to copy to");
            await Clipboard.SetBitmapAsync(_shown);
            StatusText.Text = "Copied. Paste it wherever you want to share it.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not copy the picture ({ex.Message}); save it as a PNG instead.";
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save the picture",
            SuggestedFileName = _request!.FileName,
            DefaultExtension = "png",
            FileTypeChoices = [PngFiles],
        });
        if (file == null || _picture == null)
            return;
        try
        {
            await PickedFile.WriteAsync(file, SharePicture.Png(_picture));
            StatusText.Text = $"Saved {file.Name}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not save {file.Name}: {ex.Message}";
        }
    }

    private async void OnCopyCredits(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard == null)
                throw new InvalidOperationException("there is no clipboard to copy to");
            await Clipboard.SetTextAsync(_fullCredits);
            StatusText.Text = "Copied the full sprite credits, to post along with the picture.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not copy the credits: {ex.Message}";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
