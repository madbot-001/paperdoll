using Avalonia.Controls;
using Paperdoll.App.Preview;
using Paperdoll.Core.Editing;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Rooms;

namespace Paperdoll.App;

/// <summary>
/// The preview's floor: plain plates, or the demo room, a small hand-made room, chosen by the
/// dressed job, drawn from the fork's own sprites, lit by its own lamps or evenly. The room and its
/// light are worked out once and kept, both ways; the character is drawn between its two pictures,
/// lit where it stands, so animation redraws only the character. Only the main preview shows the
/// room; the facings and tables keep the plates.
/// </summary>
public partial class MainWindow
{
    // The floors in the order the Floor box lists them.
    private static readonly FloorMode[] FloorChoices = [FloorMode.Plain, FloorMode.DemoRoom];

    // The built-in rooms and the user's own from this folder, read when first needed.
    private RoomCatalog? _rooms;
    private readonly string _roomsFolder = Path.Combine(DataDirectory, "rooms");

    // The room last chosen for a job of a fork, so the choice is not worked out at every frame.
    private (ForkContent Content, string? Job, Room Room)? _roomChosen;

    // What a room is drawn for: the fork as read and the room.
    private readonly record struct RoomKey(ForkContent Content, string Room);

    // The last room drawn, evenly lit and, when a light shines in it, lit. It is shown only while
    // it is the room the preview wants.
    private (RoomKey Key, RoomScene Scene, RoomPicture Picture, RoomPicture? Lit)? _room;

    // The request under way, or the last one made, and what stops it.
    private RoomKey? _roomAsked;
    private CancellationTokenSource? _roomCancel;

    // A request that failed: the note for the preview bar and why. Not asked again until the room
    // or fork changes, or the floor is chosen again.
    private (RoomKey Key, string Note, string Problem)? _roomEnded;

    // The room the main preview last drew and at what size, for the credits and pictures.
    private (RoomScene Scene, RoomPicture Picture, int Width, int Height, int Zoom)? _roomShown;

    /// <summary>Shows the floor settings.</summary>
    private void ShowFloorSettings()
    {
        var wasRefreshing = _refreshing;
        _refreshing = true;
        FloorBox.SelectedIndex = Array.IndexOf(FloorChoices, FloorShown());
        LightingBox.IsChecked = _settings.Lighting;
        LightingBox.IsEnabled = FloorShown() == FloorMode.DemoRoom;
        _refreshing = wasRefreshing;
    }

    // The floor chosen; a value no longer offered, from older settings, is plain plates.
    private FloorMode FloorShown() => _settings.Floor == FloorMode.DemoRoom ? FloorMode.DemoRoom : FloorMode.Plain;

    private void OnFloorChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || FloorBox.SelectedIndex < 0)
            return;
        _settings = _settings with { Floor = FloorChoices[FloorBox.SelectedIndex] };
        SaveSettings();
        // Choosing again tries again a room that could not be shown.
        _roomEnded = null;
        ForgetRoomFiles();
        LightingBox.IsEnabled = FloorShown() == FloorMode.DemoRoom;
        ShowFloorChange();
    }

    // Redraws the preview, and the credits and messages that follow the floor.
    private void ShowFloorChange()
    {
        if (_session?.Look == null)
            return;
        RefreshPreview();
        ShowMessageCount();
        _staleTabs.Add(EditorTab.Credits);
        _staleTabs.Add(EditorTab.Messages);
        RefreshShownTab();
    }

    private void ShowFloorNote(string? text, bool warning)
    {
        FloorNote.Text = text ?? "";
        FloorNote.IsVisible = text != null;
        FloorNote.Classes.Set("warning", warning);
    }

    private RoomCatalog Rooms() => _rooms ??= RoomCatalog.Load(_roomsFolder);

    // Choosing a floor reads the user's room files again when the demo room is next wanted, so a
    // file fixed or changed since shows; a room whose file is unchanged keeps its pictures.
    private void ForgetRoomFiles()
    {
        _rooms = null;
        _roomChosen = null;
        _roomAsked = null;
    }

    // The room the preview is to show, or null for another floor.
    private Room? WantedRoom()
    {
        if (_session?.Content is not { } content || FloorShown() != FloorMode.DemoRoom)
            return null;
        var job = _session.DressedJob();
        if (_roomChosen is not { } chosen || chosen.Content != content || chosen.Job != job)
            _roomChosen = chosen = (content, job, Rooms().For(job, content.Prototypes));
        return chosen.Room;
    }

    /// <summary>
    /// The demo room to draw around the character, asking for it when it is not here yet. Null for
    /// another floor, or for plain plates while it loads or when it could not be drawn.
    /// </summary>
    private (RoomScene Scene, RoomPicture Picture)? RoomFor()
    {
        if (_session?.Content is not { } content || WantedRoom() is not { } room)
        {
            _roomCancel?.Cancel();
            _roomAsked = null;
            ShowFloorNote(null, false);
            return null;
        }
        var key = new RoomKey(content, room.Id);
        if (_roomEnded is { } ended && ended.Key == key)
        {
            ShowFloorNote(ended.Note, true);
            return null;
        }
        if (_room is { } found && found.Key == key && found.Scene.Room.Text == room.Text)
        {
            if (!_settings.Lighting || found.Lit is not { } lit)
            {
                ShowFloorNote(_settings.Lighting ? LightingNote(found.Scene) : null, found.Scene.LightProblem != null);
                return (found.Scene, found.Picture);
            }
            ShowFloorNote(null, false);
            return (found.Scene, lit);
        }
        if (_roomAsked != key)
            AskRoom(key, room);
        ShowFloorNote("Loading the demo room...", false);
        return null;
    }

    // Asks the session for the room, in place of any request still under way. What comes back is
    // kept only while it is still what the preview wants.
    private async void AskRoom(RoomKey key, Room room)
    {
        _roomCancel?.Cancel();
        var cancel = _roomCancel = new CancellationTokenSource();
        _roomAsked = key;
        var session = _session!;
        try
        {
            var scene = await session.RoomAsync(room, cancel.Token);
            // Drawn once each way, away from the window's thread; every frame after reuses the pictures.
            var (picture, lit) = await Task.Run(() => (scene.Paint(FloorCanvas.Plate), scene.Light == null ? null : scene.Paint(FloorCanvas.Plate, lit: true)), cancel.Token);
            if (cancel.IsCancellationRequested)
                return;
            _room = (key, scene, picture, lit);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            // Other store work stopped it: the next drawing asks again.
            if (_roomCancel == cancel)
                _roomAsked = null;
            return;
        }
        catch (Exception ex)
        {
            if (cancel.IsCancellationRequested)
                return;
            _roomEnded = (key, "Could not show the demo room; plain plates instead. See Messages.", ex.Message);
            SetStatus($"Could not show the demo room: {ex.Message}");
        }
        finally
        {
            if (_roomCancel == cancel)
                _roomCancel = null;
            cancel.Dispose();
        }
        // Not drawn from inside a drawing, which a request that finished at once would be.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (session == _session)
                ShowFloorChange();
        });
    }

    // Why the demo room shown could not be drawn, and room files that could not be read, for the
    // Messages tab; only while the preview shows the demo room.
    private IReadOnlyList<string> RoomProblems()
    {
        if (WantedRoom() is not { } room)
            return [];
        var lines = Rooms().Problems.Select(p => $"Demo room: {p}").ToList();
        var key = new RoomKey(_session!.Content!, room.Id);
        if (_roomEnded is { } ended && ended.Key == key)
            lines.Add($"Floor: {ended.Problem} The preview shows plain plates instead.");
        if (_settings.Lighting && _room is { } shown && shown.Key == key && shown.Scene.LightProblem is { } problem)
            lines.Add($"Lighting: {problem} The demo room is drawn without lighting.");
        return lines;
    }

    // Why a room shows without lighting while Lighting is on: it has no light that shines, or its
    // light could not be worked out. Null when it is lit.
    private static string? LightingNote(RoomScene scene) =>
        scene.Light != null ? null
        : scene.LightProblem != null ? "Could not light the demo room; drawn without lighting. See Messages."
        : "No light shines in the demo room; drawn without lighting.";

    private void OnLightingToggled(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_refreshing || LightingBox.IsChecked == _settings.Lighting)
            return;
        _settings = _settings with { Lighting = LightingBox.IsChecked == true };
        SaveSettings();
        ShowFloorChange();
    }

    /// <summary>The credits of the room's pictures the main preview shows.</summary>
    private IReadOnlyList<CreditLine> RoomCredits() =>
        _roomShown is { } room ? FloorCanvas.CreditsShown(room.Scene, room.Width, room.Height, room.Zoom) : [];
}
