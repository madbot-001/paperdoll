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
    // A working copy that could not be reopened, set aside rather than overwritten.
    private static string UnreadableAutosavePath => Path.Combine(DataDirectory, "autosave.unreadable.yml");

    private AppSettings _settings = new();
    // Only the real app saves; windows made for screenshots never touch the user's files.
    private bool _canAutosave;
    private IStorageFile? _currentFile;
    private bool _dirty;
    // A working copy from last time that has not been reopened yet, which nothing may overwrite.
    private bool _restorePending;
    // Whether this run's working copy is its own (reopened, or written since), not one left from before.
    private bool _restoredThisRun;
    // The working copy was due and could not be written; closing asks until it is.
    private bool _autosaveOwed;
    // The working copy from last time could neither be read nor set aside, so none is written
    // over it this run.
    private bool _autosaveStuck;
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
            // A working copy that cannot be written is not lost quietly: offer to save instead.
            if ((_autosave.IsEnabled || _autosaveOwed) && WriteAutosave() == Autosaved.Failed && !_closeConfirmed)
            {
                e.Cancel = true;
                _dirty = true;
                Dispatcher.UIThread.Post(async () =>
                {
                    if (await MayReplaceCharacterAsync("closing Paperdoll without its working copy"))
                    {
                        _closeConfirmed = true;
                        Close();
                    }
                });
            }
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

    private enum Autosaved
    {
        Written,
        NotKept,
        Failed,
    }

    /// <summary>Writes the working copy, when autosave is on and nothing holds it back.</summary>
    private Autosaved WriteAutosave()
    {
        if (!_canAutosave || !_settings.Autosave || _session?.File == null || _restorePending || _loading)
            return Autosaved.NotKept;
        if (_autosaveStuck)
        {
            _autosaveOwed = true;
            return Autosaved.Failed;
        }
        try
        {
            Directory.CreateDirectory(DataDirectory);
            // The character as last edited, not as a fork switch has since fitted it.
            Core.SafeFile.WriteAllText(AutosavePath, _session.WorkingCopy());
            _restoredThisRun = true;
            _autosaveOwed = false;
            return Autosaved.Written;
        }
        catch (Exception e)
        {
            _autosaveOwed = true;
            SetStatus($"Could not autosave: {e.Message}");
            return Autosaved.Failed;
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
        string text;
        try
        {
            text = await Core.Profiles.CharacterFile.ReadTextAsync(AutosavePath);
            // Should this fork's rules change it, it stays as it was until edited.
            _session.Open(text, keepUntilEdited: true);
            _restoredThisRun = true;
        }
        catch (Exception e)
        {
            // Set aside, so the next autosave does not overwrite it; failing that, not written
            // over at all this run.
            string where;
            try
            {
                File.Move(AutosavePath, UnreadableAutosavePath, overwrite: true);
                where = $"it is kept as {UnreadableAutosavePath}";
            }
            catch (Exception)
            {
                try
                {
                    File.Copy(AutosavePath, UnreadableAutosavePath, overwrite: true);
                    where = $"a copy is kept as {UnreadableAutosavePath}";
                }
                catch (Exception)
                {
                    _autosaveStuck = true;
                    where = $"it is left at {AutosavePath}, and autosave is paused so as not to write over it; use Save or Export";
                }
            }
            SetStatus($"Could not reopen the autosaved character ({e.Message}); {where}.");
            return;
        }

        // Reopened; now whether it still belongs with its file.
        var detached = "";
        try
        {
            var fileText = _settings.WorkingFile is { } path && File.Exists(path) ? await Core.Profiles.CharacterFile.ReadTextAsync(path) : null;
            if (fileText != null && WorkingFileMatches(fileText, _settings.WorkingFileHash, text))
            {
                _currentFile = await StorageProvider.TryGetFileFromPathAsync(_settings.WorkingFile!);
                // Compared as Paperdoll would write them, so a game export is not taken for a change.
                _dirty = _session.Normalized(fileText) != _session.Normalized(text);
                if (_settings.WorkingFileHash == null)
                {
                    _settings = _settings with { WorkingFileHash = Fingerprint(fileText) };
                    SaveSettings();
                }
            }
            else
            {
                if (fileText != null)
                    detached = $" {Path.GetFileName(_settings.WorkingFile)} has changed since, so Save will ask where to save it.";
                ForgetWorkingFile();
                _dirty = true;
            }
        }
        catch (Exception e)
        {
            detached = $" Its file could not be read ({e.Message}), so Save will ask where to save it.";
            ForgetWorkingFile();
            _dirty = true;
        }
        SelectDressedJob();
        RefreshAll();
        SetStatus($"Reopened {_session.File?.Name ?? "your character"} as you left it (autosaved {File.GetLastWriteTime(AutosavePath):g}).{detached}");
    }

    /// <summary>
    /// Before another character replaces the one being edited: with autosave on, the outgoing
    /// character is kept as the previous working copy. A working copy from last time that was
    /// never reopened is kept instead, and no longer waits to be reopened.
    /// </summary>
    private void KeepPreviousWorkingCopy()
    {
        if (!_canAutosave)
            return;
        try
        {
            if (_restorePending)
            {
                _restorePending = false;
                if (File.Exists(AutosavePath))
                    Core.SafeFile.WriteAllBytes(PreviousAutosavePath, File.ReadAllBytes(AutosavePath));
            }
            else if (_settings.Autosave && _session?.File != null)
                Core.SafeFile.WriteAllText(PreviousAutosavePath, _session.WorkingCopy());
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
            return File.Exists(AutosavePath) ? Core.Profiles.CharacterFile.Parse(Core.Profiles.CharacterFile.ReadText(AutosavePath)).ForkId : null;
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

    private async void OnFiles(object? sender, RoutedEventArgs e)
    {
        if (!_loading)
            await ShowFilesAsync(this);
    }

    /// <summary>Where Paperdoll keeps its files; also opened from the Forks window.</summary>
    internal async Task ShowFilesAsync(Window owner)
    {
        if (_session == null)
            return;
        await new FilesWindow(_session, AutosavePath, SettingsPath, () =>
        {
            _autosave.Stop();
            _restorePending = false;
        }).ShowDialog(owner);
    }

    private void OnAutosaveToggled()
    {
        _settings = _settings with { Autosave = AutosaveItem.IsChecked };
        SaveSettings();
        if (_settings.Autosave)
        {
            // A working copy left from when autosave was last on is kept, not overwritten.
            if (!_restoredThisRun && File.Exists(AutosavePath))
            {
                try
                {
                    Core.SafeFile.WriteAllBytes(PreviousAutosavePath, File.ReadAllBytes(AutosavePath));
                }
                catch (Exception e)
                {
                    SetStatus($"Could not keep the old working copy: {e.Message}");
                }
            }
            _restoredThisRun = true;
            WriteAutosave();
        }
        SetStatus(_settings.Autosave
            ? "Autosave on: the character is kept a second after each change and reopened next time."
            : "Autosave off: use Save or Export to keep your work.");
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!_loading)
            await SaveAsync();
    }

    // Saves to the working file, or asks where when there is none. True once saved.
    private async Task<bool> SaveAsync()
    {
        CommitTyping();
        if (_session?.File == null)
            return false;
        if (_currentFile == null)
            return await ExportAsync();
        // Another program (the game exporting, another Paperdoll) may have saved there since.
        if (_currentFile.TryGetLocalPath() is { } path && File.Exists(path) && _settings.WorkingFileHash is { } hash)
        {
            string? now;
            try
            {
                now = await Core.Profiles.CharacterFile.ReadTextAsync(path);
            }
            // Something else, or something too large to be a character, is there now.
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            {
                now = null;
            }
            if (now == null || Fingerprint(now) != hash)
            {
                SetStatus($"{_currentFile.Name} has changed or cannot be read since Paperdoll last saved it, so it is not overwritten; choose where to save.");
                return await ExportAsync();
            }
        }
        return await WriteToAsync(_currentFile, "Saved");
    }

    // Writes the character to a file the user chose, which then becomes the one Save writes to.
    private async Task<bool> WriteToAsync(IStorageFile file, string verb)
    {
        try
        {
            var text = _session!.Export();
            await PickedFile.WriteAsync(file, text);
            _session.KeepSwitchChanges();
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
        if (_loading || _session?.Content == null || !await MayReplaceCharacterAsync("opening another character"))
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
            var text = await Core.Profiles.CharacterFile.ReadTextAsync(stream);
            var wasOld = Core.Profiles.CharacterFile.Parse(text).IsOldModel;
            KeepPreviousWorkingCopy();
            _session.Open(text);
            _session.PreviewJob = null;
            // Paperdoll's own copies (the working copy, the previous one) are opened as new
            // characters, so Save never writes over them.
            var local = files[0].TryGetLocalPath();
            // Windows and Mac folders ignore letter case.
            var casing = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var own = local != null && Path.GetFullPath(local).StartsWith(Path.GetFullPath(DataDirectory) + Path.DirectorySeparatorChar, casing);
            if (own)
                ForgetWorkingFile();
            else
            {
                _currentFile = files[0];
                _settings = _settings with { WorkingFile = local, WorkingFileHash = Fingerprint(text) };
                SaveSettings();
            }
            _dirty = own;
            WriteAutosave();
            _refreshing = true;
            SelectDressedJob();
            _refreshing = false;
            RefreshAll();
            var converted = wasOld && _session.Content?.Characters.Species.GetValueOrDefault(_session.Look!.Species)?.Old == null;
            SetStatus($"Opened {files[0].Name}" + (converted ? ", converted from the old appearance model." : ".")
                + (own ? " It is one of Paperdoll's own copies, so Save asks where to keep it." : ""));
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open {files[0].Name}: {ex.Message}");
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (!_loading)
            await ExportAsync();
    }

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
