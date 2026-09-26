using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Paperdoll.App.Preview;
using SkiaSharp;

namespace Paperdoll.App;

/// <summary>What the share window pictures: the character facing each way (front first), and the words to go with it.</summary>
public sealed record ShareRequest(IReadOnlyList<SKBitmap> Sides, (float X, float Y) Scale, ShareText Text, string FullCredits, string FileName);

/// <summary>
/// Makes a picture of the character to share: saved as a PNG or copied, never sent anywhere by
/// Paperdoll.
/// </summary>
public partial class ShareWindow : Window
{
    private static readonly int[] Zooms = [2, 3, 4, 6, 8];
    private static readonly FilePickerFileType PngFiles = new("PNG pictures") { Patterns = ["*.png"], MimeTypes = ["image/png"] };

    private readonly ShareRequest? _request;
    private readonly Action<ShareOptions>? _remember;
    private ShareOptions _options = new();
    private SKBitmap? _picture;
    private Avalonia.Media.Imaging.Bitmap? _shown;
    private bool _reading;

    public ShareWindow() => InitializeComponent();

    public ShareWindow(ShareRequest request, ShareOptions options, Action<ShareOptions> remember) : this()
    {
        _request = request;
        _options = options;
        _remember = remember;

        _reading = true;
        AllSidesButton.IsChecked = options.AllSides;
        FrontOnlyButton.IsChecked = !options.AllSides;
        SizeBox.ItemsSource = Zooms.Select(z => $"{z}× ({32 * z} pixels a tile)").ToList();
        SizeBox.SelectedIndex = Math.Max(0, Array.IndexOf(Zooms, options.Zoom));
        FloorButton.IsChecked = options.Background == ShareBackground.Floor;
        PlainButton.IsChecked = options.Background == ShareBackground.Plain;
        TransparentButton.IsChecked = options.Background == ShareBackground.Transparent;
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
            TransparentButton.IsChecked == true ? ShareBackground.Transparent : PlainButton.IsChecked == true ? ShareBackground.Plain : ShareBackground.Floor,
            CaptionBox.IsChecked == true,
            CreditBox.IsChecked == true);
        _remember?.Invoke(_options);
        Redraw();
    }

    private void Redraw()
    {
        var sides = _options.AllSides ? _request!.Sides : [_request!.Sides[0]];
        _picture?.Dispose();
        _picture = SharePicture.Compose(sides, _request.Scale, _options, _request.Text);
        var shown = FloorCanvas.ToAvalonia(_picture);
        PictureImage.Source = shown;
        _shown?.Dispose();
        _shown = shown;
        SizeText.Text = $"{_picture.Width} × {_picture.Height} pixels";
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
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(SharePicture.Png(_picture));
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
            await Clipboard.SetTextAsync(_request!.FullCredits);
            StatusText.Text = "Copied the full sprite credits, to post along with the picture.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not copy the credits: {ex.Message}";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
