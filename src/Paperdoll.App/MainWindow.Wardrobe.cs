using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Paperdoll.App;

/// <summary>
/// The Wardrobe tab: any of the fork's clothing tried on in the preview, slot by slot. It changes
/// the preview and pictures only, never the character.
/// </summary>
public partial class MainWindow
{
    private List<WardrobeRow> _wardrobeRows = [];

    // The rows the table shows now: those of the slot that pass the search.
    private System.Collections.ObjectModel.ObservableCollection<WardrobeRow> _wardrobeShown = [];

    // Downloads under way, by slot: the item, the dress-up it was picked in (see
    // EditorSession.DressUpCleared), and what stops it. A new pick stops only its own slot's.
    private readonly Dictionary<string, (string Entity, int Round, CancellationTokenSource Cancel)> _pending = [];

    // What the page says about the last pick in each slot (downloading, or why it could not be
    // worn), for the dress-up it was picked in.
    private readonly Dictionary<string, (string Text, int Round)> _wardrobeNotes = [];
    private TextBlock? _wardrobeNoteText;

    // The pause after a pick before its download starts, so arrowing down the list does not
    // download every row passed. Items already here are put on at once.
    private Func<CancellationToken, Task> _downloadPause = ct => Task.Delay(300, ct);

    // The row the table itself selected when it was filled, which the grid selects again later:
    // not a pick. Null once the user picks.
    private WardrobeRow? _wardrobeSelectedByTable;

    // The item being downloaded for a slot, in the current dress-up.
    private string? PendingIn(string slot) =>
        _pending.TryGetValue(slot, out var pending) && pending.Round == _session!.DressUpCleared ? pending.Entity : null;

    // The slots dress-up offers, in the order the inventory shows them.
    private List<string> WardrobeSlots() => _session!.WardrobeSlots().OrderBy(SlotOrder).ToList();

    // The two pockets share a name in the spawn list; here each has a row.
    private static string WardrobeSlotName(string slot) => slot switch
    {
        "pocket1" => "Pocket 1",
        "pocket2" => "Pocket 2",
        _ => SlotName(slot),
    };

    // What a slot holds: the job's item, nothing, or an item tried on.
    private string WornIn(string slot)
    {
        var session = _session!;
        if (session.DressUp.TryGetValue(slot, out var item))
            return item == null ? "nothing" : session.EntityName(item);
        return session.JobOutfit().GetValueOrDefault(slot) is { } job ? $"from the job: {session.EntityName(job)}" : "from the job: nothing";
    }

    private void InspectWardrobeSlot(string? slot)
    {
        var session = _session!;
        InspectorTitle.Text = slot == null ? "Wardrobe" : $"Wardrobe: {WardrobeSlotName(slot)}";
        AddWide(Hint("Dress-up is for the preview and pictures only. It is not saved in the character, and the game shows the job's outfit."));

        var reset = new Button { Classes = { "small" }, Content = "Reset to the job's outfit", IsEnabled = session.HasDressUp, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 4) };
        reset.Click += (_, _) =>
        {
            // Stops the picks still downloading too.
            session.ResetDressUp();
            RefreshAll();
            SetStatus("Every slot shows the job's outfit again.");
        };
        if (slot == null)
        {
            AddWide(Text("This body has no slot to try clothing on."));
            AddWide(reset);
            return;
        }

        AddCategory("Slot");
        AddRow("Wearing", Text(WornIn(slot)));
        AddWide(reset);
        if (!session.ShowClothes)
            AddWide(Hint("Clothes are hidden in the preview; tick Clothes to see them."));
        if (session.DressUp.GetValueOrDefault(slot) is { } item && !session.Draws(item, slot))
            AddWide(Hint($"The {session.EntityName(item)} has no picture for this slot on this body, so nothing is drawn.", "warning"));
        _wardrobeNoteText = Hint(_wardrobeNotes.TryGetValue(slot, out var note) && note.Round == session.DressUpCleared ? note.Text : "", "warning");
        _wardrobeNoteText.IsVisible = _wardrobeNoteText.Text!.Length > 0;
        AddWide(_wardrobeNoteText);
        AddWide(Hint("Pick from the list below: the job's item, nothing, or any item that fits this slot."));

        static TextBlock Hint(string text, string? also = null)
        {
            var hint = new TextBlock { Text = text, Classes = { "hint" }, Margin = new Thickness(4, 4), TextWrapping = TextWrapping.Wrap };
            if (also != null)
                hint.Classes.Add(also);
            return hint;
        }
    }

    // Shows what is happening with a pick, made in dress-up round `round`, on the page, where it
    // stays until the next pick in the slot or until dress-up is cleared.
    private void ShowWardrobeNote(string slot, string? text, int round)
    {
        if (round != _session!.DressUpCleared)
            return;
        if (text == null)
            _wardrobeNotes.Remove(slot);
        else
            _wardrobeNotes[slot] = (text, round);
        if (_wardrobeNoteText != null && _selected.Group == slot)
        {
            _wardrobeNoteText.Text = text ?? "";
            _wardrobeNoteText.IsVisible = text != null;
        }
    }

    private void BuildWardrobeTable()
    {
        var session = _session!;
        if (_selected is not { Kind: NodeKind.WardrobeSlot, Group: { } slot })
        {
            _wardrobeRows = [];
            FilterWardrobe();
            return;
        }
        var job = session.JobOutfit().GetValueOrDefault(slot);
        // Null for the job's item, "" for nothing, else the item tried on.
        var choice = session.DressUp.TryGetValue(slot, out var chosen) ? chosen ?? "" : null;
        _wardrobeRows =
        [
            new(choice == null ? "Yes" : "", job == null ? "From the job (nothing)" : $"From the job ({session.EntityName(job)})", "", null, true),
            new(choice == "" ? "Yes" : "", "Nothing", "", null, false),
            .. session.Wearables(slot).Select(i => new WardrobeRow(choice == i.Id ? "Yes" : "", i.Name, i.Id, i.Id, false)),
        ];
        FilterWardrobe();
    }

    // The job's item and nothing stay at the top whatever the search.
    private void FilterWardrobe()
    {
        var filter = WardrobeFilter.Text?.Trim() ?? "";
        var rows = _wardrobeRows.Where((r, i) => i < 2 || filter.Length == 0
            || r.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || r.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        var items = Math.Max(0, _wardrobeRows.Count - 2);
        WardrobeCount.Text = filter.Length == 0 ? $"{items} {(items == 1 ? "item fits" : "items fit")}" : $"{Math.Max(0, rows.Count - 2)} of {items} items";
        var wasRefreshing = _refreshing;
        _refreshing = true;
        // The same items as before (a pick only moved the "Yes"): change those rows in place, so
        // the list stays where it was scrolled to. A new list starts at its top.
        var sameItems = _wardrobeShown.Count == rows.Count && ReferenceEquals(WardrobeGrid.ItemsSource, _wardrobeShown)
            && _wardrobeShown.Zip(rows).All(p => p.First.Entity == p.Second.Entity && p.First.FromJob == p.Second.FromJob);
        if (sameItems)
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (_wardrobeShown[i] != rows[i])
                    _wardrobeShown[i] = rows[i];
            }
        }
        else
        {
            _wardrobeShown = new(rows);
            WardrobeGrid.ItemsSource = _wardrobeShown;
        }
        // The item being downloaded (none when the search hides it), else the one worn.
        var pending = _selected.Group is { } slot ? PendingIn(slot) : null;
        var worn = pending != null ? rows.FirstOrDefault(r => r.Entity == pending) : rows.FirstOrDefault(r => r.Worn == "Yes");
        WardrobeGrid.SelectedItem = _wardrobeSelectedByTable = worn;
        if (worn != null && !sameItems)
            WardrobeGrid.ScrollIntoView(worn, null);
        _refreshing = wasRefreshing;
    }

    // Picking a row puts it on: at once when its pictures are here, else after a short pause and
    // a download.
    private async void OnWardrobePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshing || _loading || _session?.Look == null || WardrobeGrid.SelectedItem is not WardrobeRow row
            || _selected is not { Kind: NodeKind.WardrobeSlot, Group: { } slot })
            return;
        // Filling or filtering the table picks nothing.
        if (row == _wardrobeSelectedByTable)
            return;
        _wardrobeSelectedByTable = null;
        var session = _session;
        var round = session.DressUpCleared;
        if (row.Entity != null && row.Entity == PendingIn(slot))
            return;
        if (row.Worn == "Yes")
        {
            // Picking what the slot wears while another item downloads keeps what it wears.
            if (!_pending.Remove(slot, out var running))
                return;
            running.Cancel.Cancel();
            ShowWardrobeNote(slot, null, round);
            RefreshAll();
            return;
        }
        if (_pending.Remove(slot, out var earlier))
            earlier.Cancel.Cancel();
        ShowWardrobeNote(slot, null, round);
        if (row.Entity == null)
        {
            if (row.FromJob)
                session.WearFromJob(slot);
            else
                session.WearNothing(slot);
            RefreshAll();
            return;
        }

        // The pick belongs to this dress-up: clearing it (another character or fork) stops the pick.
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(session.DressUpToken);
        try
        {
            if (session.MissingSprites(row.Entity).Count > 0)
            {
                _pending[slot] = (row.Entity, round, cancel);
                ShowWardrobeNote(slot, $"Downloading the pictures of the {row.Name}...", round);
                await _downloadPause(cancel.Token);
            }
            var result = await session.WearAsync(slot, row.Entity, round, cancel.Token);
            ShowWardrobeNote(slot, null, round);
            // Stopped by a fork load, update, removal or clean-up: not a failure.
            if (result == Core.Editing.WearResult.Stopped && round == session.DressUpCleared)
                SetStatus($"Stopped downloading the pictures of the {row.Name}.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            ShowWardrobeNote(slot, $"The pictures of the {row.Name} could not be downloaded, so the slot is as it was. {ex.Message}", round);
            if (round == session.DressUpCleared)
                SetStatus($"Could not download the pictures of the {row.Name}: {ex.Message}");
        }
        finally
        {
            if (_pending.TryGetValue(slot, out var mine) && mine.Cancel == cancel)
                _pending.Remove(slot);
            cancel.Dispose();
        }
        if (session == _session)
            RefreshAll();
    }

    // The tab that was open when the Wardrobe took the tabs' place, to go back to.
    private EditorTab _tabBeforeWardrobe = EditorTab.Appearance;

    // The toolbar's Wardrobe button: dress-up on puts the Wardrobe in place of the character's tabs; off,
    // the tabs come back, the preview wears the job's outfit, and what was tried on is kept.
    private void OnWardrobeMode(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_session?.Content == null || _session.Look == null)
            return;
        CommitTyping();
        if (!_session.DressUpShown)
            _tabBeforeWardrobe = _tab;
        _session.DressUpShown = !_session.DressUpShown;
        RefreshAll();
        ShowTab(_session.DressUpShown ? EditorTab.Wardrobe : _tabBeforeWardrobe);
    }
}

/// <summary>A choice for a slot in the Wardrobe tab: the job's item, nothing, or an item (<see cref="Entity"/>).</summary>
public sealed record WardrobeRow(string Worn, string Name, string Id, string? Entity, bool FromJob);
