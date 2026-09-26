using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Paperdoll.App;

public partial class MainWindow
{
    private static readonly FilePickerFileType CharacterFiles = new("Character files") { Patterns = ["*.yml"] };

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_session?.Content == null)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a character exported from the lobby",
            FileTypeFilter = [CharacterFiles],
        });
        if (files.Count == 0)
            return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            var text = await new StreamReader(stream).ReadToEndAsync();
            var wasOld = Core.Profiles.CharacterFile.Parse(text).IsOldModel;
            _session.Open(text);
            _session.PreviewJob = null;
            _refreshing = true;
            SelectDressedJob();
            _refreshing = false;
            RefreshAll();
            SetStatus($"Opened {files[0].Name}" + (wasOld ? ", converted from the old appearance model." : "."));
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open {files[0].Name}: {ex.Message}");
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (_session?.File == null)
            return;
        var name = string.IsNullOrWhiteSpace(_session.File.Name) ? "character" : _session.File.Name;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export for the lobby's Import button",
            SuggestedFileName = name + ".yml",
            DefaultExtension = "yml",
            FileTypeChoices = [CharacterFiles],
        });
        if (file == null)
            return;

        try
        {
            var text = _session.Export();
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
            RefreshAll();
            SetStatus($"Exported {file.Name}. In the game, open the character editor and press Import.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not export: {ex.Message}");
        }
    }
}
