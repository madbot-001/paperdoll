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

    /// <summary>
    /// A fingerprint of the text last read from or written to <see cref="WorkingFile"/>, so a file
    /// that has changed since (another character saved there, the game exporting again) is never
    /// taken for the working copy's and overwritten.
    /// </summary>
    public string? WorkingFileHash { get; init; }

    /// <summary>Servers each fork was matched to: fork id to the server's name and the commit it ran.</summary>
    public Dictionary<string, MatchedServer> MatchedServers { get; init; } = [];

    /// <summary>What Random changes; the rest is kept, like the lobby's locks.</summary>
    public Core.Characters.RandomParts RandomParts { get; init; } = Core.Characters.RandomParts.All;

    /// <summary>
    /// How busy Random's markings get, in percent; 100 is upstream's lobby randomiser, which
    /// fills Euphoria's ten-marking layers with dozens.
    /// </summary>
    public int RandomStrength { get; init; } = 20;

    /// <summary>How the last picture to share was laid out.</summary>
    public Preview.ShareOptions? Share { get; init; }
}

public sealed record MatchedServer(string Name, string Commit);

public partial class MainWindow
{
    private static readonly FilePickerFileType CharacterFiles = new("Character files") { Patterns = ["*.yml"] };

    // Paperdoll's own folder (PAPERDOLL_DATA overrides it), holding settings and the working copy.
    internal static string DataDirectory => Environment.GetEnvironmentVariable("PAPERDOLL_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paperdoll");
    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    private static string AutosavePath => Path.Combine(DataDirectory, "autosave.yml");
    // The working copy before it was last replaced by another character, in case that was a mistake.
    private static string PreviousAutosavePath => Path.Combine(DataDirectory, "autosave.prev.yml");

    private AppSettings _settings = new();
    // Only the real app saves; windows made for screenshots never touch the user's files.
    private bool _canAutosave;
    private IStorageFile? _currentFile;
    private bool _dirty;
    // A working copy from last time that has not been reopened yet, which nothing may overwrite.
    private bool _restorePending;
    private bool _closeConfirmed;
    private readonly DispatcherTimer _autosave = new() { Interval = TimeSpan.FromSeconds(1) };

    private void SetUpFiles()
    {
        _autosave.Tick += (_, _) =>
        {
            _autosave.Stop();
            WriteAutosave();
        };
        Closing += (_, e) =>
        {
            CommitTyping();
            // With autosave off, closing would lose unsaved changes, so ask first.
            if (_canAutosave && !_settings.Autosave && _dirty && !_closeConfirmed)
            {
                e.Cancel = true;
                Dispatcher.UIThread.Post(async () =>
                {
                    if (await MayReplaceCharacterAsync("closing Paperdoll"))
                    {
                        _closeConfirmed = true;
                        Close();
                    }
                });
                return;
            }
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
            Core.SafeFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
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
        if (!_canAutosave || !_settings.Autosave || _session?.File == null || _restorePending)
            return;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            // The character as last edited, not as a fork switch has since fitted it.
            Core.SafeFile.WriteAllText(AutosavePath, _session.WorkingCopy());
        }
        catch (Exception e)
        {
            SetStatus($"Could not autosave: {e.Message}");
        }
    }

    /// <summary>
    /// Reopens the working copy from last time on the first fork that loads. If the fork's rules
    /// change it, the version from before is kept as the previous working copy.
    /// </summary>
    private async Task RestoreAutosaveAsync()
    {
        if (!_restorePending || _session?.Content == null)
            return;
        _restorePending = false;
        try
        {
            var text = await File.ReadAllTextAsync(AutosavePath);
            _session.Open(text);
            if (_session.LastFixes.Count > 0)
                KeepPreviousWorkingCopy();
            var detached = "";
            var fileText = _settings.WorkingFile is { } path && File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
            if (fileText != null && WorkingFileMatches(fileText, _settings.WorkingFileHash, text))
            {
                _currentFile = await StorageProvider.TryGetFileFromPathAsync(_settings.WorkingFile!);
                _dirty = fileText != text;
            }
            else
            {
                if (fileText != null)
                    detached = $" {Path.GetFileName(_settings.WorkingFile)} has changed since, so Save will ask where to save it.";
                ForgetWorkingFile();
                _dirty = true;
            }
            SelectDressedJob();
            RefreshAll();
            SetStatus($"Reopened {_session.File?.Name ?? "your character"} as you left it (autosaved {File.GetLastWriteTime(AutosavePath):g}).{detached}");
        }
        catch (Exception e)
        {
            // Kept aside, so the next autosave does not overwrite it.
            KeepPreviousWorkingCopy();
            SetStatus($"Could not reopen the autosaved character ({e.Message}); it is kept as autosave.prev.yml, which File > Files on this computer shows.");
        }
    }

    // Copies the working copy aside before another character replaces it.
    private void KeepPreviousWorkingCopy()
    {
        try
        {
            if (File.Exists(AutosavePath))
                File.Copy(AutosavePath, PreviousAutosavePath, overwrite: true);
        }
        catch (Exception e)
        {
            SetStatus($"Could not keep the previous working copy: {e.Message}");
        }
    }

    /// <summary>
    /// Before something replaces the character: true if its changes are saved, or the user saves
    /// them now or chooses to lose them; false to stop.
    /// </summary>
    private async Task<bool> MayReplaceCharacterAsync(string what)
    {
        CommitTyping();
        if (!_dirty || _session?.File == null)
            return true;
        var name = string.IsNullOrWhiteSpace(_session.File.Name) ? "This character" : _session.File.Name;
        return await new UnsavedWindow(name, what).ShowDialog<UnsavedChoice?>(this) switch
        {
            UnsavedChoice.Save => await SaveAsync(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether the working copy still belongs with its file: the file holds the text last read
    /// from or written to it, or the same text as the working copy (so nothing could be lost).
    /// </summary>
    public static bool WorkingFileMatches(string fileText, string? savedHash, string workingCopy) =>
        fileText == workingCopy || (savedHash != null && Fingerprint(fileText) == savedHash);

    public static string Fingerprint(string text) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    // New, or a working file that no longer matches: Save asks where to save.
    private void ForgetWorkingFile()
    {
        _currentFile = null;
        if (_settings.WorkingFile == null && _settings.WorkingFileHash == null)
            return;
        _settings = _settings with { WorkingFile = null, WorkingFileHash = null };
        SaveSettings();
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

    private async void OnFiles(object? sender, RoutedEventArgs e) => await ShowFilesAsync(this);

    /// <summary>Where Paperdoll keeps its files; also opened from the Forks window.</summary>
    internal async Task ShowFilesAsync(Window owner)
    {
        if (_session == null)
            return;
        await new FilesWindow(_session, AutosavePath, SettingsPath, () => _autosave.Stop()).ShowDialog(owner);
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

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    // Saves to the working file, or asks where when there is none. True once saved.
    private async Task<bool> SaveAsync()
    {
        CommitTyping();
        if (_session?.File == null)
            return false;
        return _currentFile == null ? await ExportAsync() : await WriteToAsync(_currentFile, "Saved");
    }

    // Writes the character to a file the user chose, which then becomes the one Save writes to.
    private async Task<bool> WriteToAsync(IStorageFile file, string verb)
    {
        try
        {
            var text = _session!.Export();
            await PickedFile.WriteAsync(file, text);
            _currentFile = file;
            _dirty = false;
            _settings = _settings with { WorkingFile = file.TryGetLocalPath(), WorkingFileHash = Fingerprint(text) };
            SaveSettings();
            WriteAutosave();
            RefreshAll();
            SetStatus($"{verb} {file.Name}. In the game, open the character editor and press Import.");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save {file.Name}: {ex.Message}");
            return false;
        }
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (_session?.Content == null || !await MayReplaceCharacterAsync("opening another character"))
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
            _settings = _settings with { WorkingFile = files[0].TryGetLocalPath(), WorkingFileHash = Fingerprint(text) };
            SaveSettings();
            KeepPreviousWorkingCopy();
            WriteAutosave();
            _refreshing = true;
            SelectDressedJob();
            _refreshing = false;
            RefreshAll();
            var converted = wasOld && _session.Content?.Characters.Species.GetValueOrDefault(_session.Look!.Species)?.Old == null;
            SetStatus($"Opened {files[0].Name}" + (converted ? ", converted from the old appearance model." : "."));
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open {files[0].Name}: {ex.Message}");
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e) => await ExportAsync();

    // Asks where to write the character and writes it there. True once written.
    private async Task<bool> ExportAsync()
    {
        CommitTyping();
        if (_session?.File == null)
            return false;
        var name = string.IsNullOrWhiteSpace(_session.File.Name) ? "character" : _session.File.Name;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export for the lobby's Import button",
            SuggestedFileName = name + ".yml",
            DefaultExtension = "yml",
            FileTypeChoices = [CharacterFiles],
        });
        return file != null && await WriteToAsync(file, "Exported");
    }
}
