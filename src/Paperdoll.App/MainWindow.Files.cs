using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Paperdoll.App;

/// <summary>What Paperdoll remembers between runs.</summary>
public sealed record AppSettings
{
    /// <summary>Keep a working copy of the character, saved a second after each change and reopened at start.</summary>
    public bool Autosave { get; init; } = true;

    /// <summary>The file the character was last opened from or saved to, for Save.</summary>
    public string? WorkingFile { get; init; }

    /// <summary>What Random changes; the rest is kept, like the lobby's locks.</summary>
    public Core.Characters.RandomParts RandomParts { get; init; } = Core.Characters.RandomParts.All;
}

public partial class MainWindow
{
    private static readonly FilePickerFileType CharacterFiles = new("Character files") { Patterns = ["*.yml"] };

    // Paperdoll's own folder (PAPERDOLL_DATA overrides it), holding settings and the working copy.
    private static string DataDirectory => Environment.GetEnvironmentVariable("PAPERDOLL_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paperdoll");
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private static string AutosavePath => Path.Combine(DataDirectory, "autosave.yml");

    private AppSettings _settings = new();
    // Only the real app saves; windows made for screenshots never touch the user's files.
    private bool _canAutosave;
    private IStorageFile? _currentFile;
    private bool _dirty;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1) };

    private void SetUpFiles()
    {
        _autosave.Tick += (_, _) =>
        {
            _autosave.Stop();
            WriteAutosave();
        };
        Closing += (_, _) =>
        {
            if (_autosave.IsEnabled)
                WriteAutosave();
        };
        AutosaveItem.PropertyChanged += (_, e) =>
        {
            if (e.Property == MenuItem.IsCheckedProperty && AutosaveItem.IsChecked != _settings.Autosave)
                OnAutosaveToggled();
        };
    }

    private void LoadSettings()
    {
        _canAutosave = true;
        try
        {
            if (File.Exists(SettingsPath))
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch (Exception e)
        {
            SetStatus($"Could not read settings, using the defaults: {e.Message}");
        }
        AutosaveItem.IsChecked = _settings.Autosave;
        BuildRandomParts();
    }

    private void SaveSettings()
    {
        if (!_canAutosave)
            return;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            SetStatus($"Could not save settings: {e.Message}");
        }
    }

    /// <summary>Marks the character unsaved and schedules an autosave.</summary>
    private void MarkChanged()
    {
        _dirty = true;
        UpdateTitle();
        if (_canAutosave && _settings.Autosave)
        {
            _autosave.Stop();
            _autosave.Start();
        }
    }

    private void WriteAutosave()
    {
        if (!_canAutosave || !_settings.Autosave || _session?.File == null)
            return;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(AutosavePath, _session.Export());
        }
        catch (Exception e)
        {
            SetStatus($"Could not autosave: {e.Message}");
        }
    }

    /// <summary>At start, reopens the working copy from last time, if there is one.</summary>
    private async Task RestoreAutosaveAsync()
    {
        if (!_settings.Autosave || !File.Exists(AutosavePath) || _session?.Content == null)
            return;
        try
        {
            var text = await File.ReadAllTextAsync(AutosavePath);
            _session.Open(text);
            if (_settings.WorkingFile is { } path && File.Exists(path))
            {
                _currentFile = await StorageProvider.TryGetFileFromPathAsync(path);
                _dirty = (await File.ReadAllTextAsync(path)) != text;
            }
            else
                _dirty = true;
            SelectDressedJob();
            RefreshAll();
            SetStatus($"Reopened {_session.File?.Name ?? "your character"} as you left it (autosaved {File.GetLastWriteTime(AutosavePath):g}).");
        }
        catch (Exception e)
        {
            SetStatus($"Could not reopen the autosaved character: {e.Message}");
        }
    }

    /// <summary>The fork an autosaved character was made for, so start can open that fork.</summary>
    private static string? AutosaveForkId()
    {
        try
        {
            return File.Exists(AutosavePath) ? Core.Profiles.CharacterFile.Parse(File.ReadAllText(AutosavePath)).ForkId : null;
        }
        catch
        {
            return null;
        }
    }

    private void UpdateTitle()
    {
        var name = _currentFile?.Name ?? (string.IsNullOrWhiteSpace(_session?.File?.Name) ? "Unnamed character" : _session!.File!.Name);
        Title = $"{name}{(_dirty ? " *" : "")} - Paperdoll";
    }

    private void OnAutosaveToggled()
    {
        _settings = _settings with { Autosave = AutosaveItem.IsChecked };
        SaveSettings();
        if (_settings.Autosave)
            WriteAutosave();
        SetStatus(_settings.Autosave
            ? "Autosave on: the character is kept a second after each change and reopened next time."
            : "Autosave off: use Save or Export to keep your work.");
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_session?.File == null)
            return;
        if (_currentFile == null)
        {
            OnExport(sender, e);
            return;
        }
        await WriteToAsync(_currentFile, "Saved");
    }

    // Writes the character to a file the user chose, which then becomes the one Save writes to.
    private async Task WriteToAsync(IStorageFile file, string verb)
    {
        try
        {
            var text = _session!.Export();
            await using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(text);
            }
            _currentFile = file;
            _dirty = false;
            _settings = _settings with { WorkingFile = file.TryGetLocalPath() };
            SaveSettings();
            WriteAutosave();
            RefreshAll();
            SetStatus($"{verb} {file.Name}. In the game, open the character editor and press Import.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save {file.Name}: {ex.Message}");
        }
    }

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
            _currentFile = files[0];
            _dirty = false;
            _settings = _settings with { WorkingFile = files[0].TryGetLocalPath() };
            SaveSettings();
            WriteAutosave();
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
        await WriteToAsync(file, "Exported");
    }
}
