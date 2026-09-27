using Avalonia.Interactivity;
using Paperdoll.Core.Profiles;
using Paperdoll.Core.Rendering;

namespace Paperdoll.App;

public partial class MainWindow
{
    private async void OnShare(object? sender, RoutedEventArgs e)
    {
        CommitTyping();
        if (_session?.Look == null || _session.File == null)
            return;
        try
        {
            var session = _session;
            var name = string.IsNullOrWhiteSpace(session.File.Name) ? "Unnamed" : session.File.Name.Trim();
            var sides = new[] { Direction.South, Direction.North, Direction.East, Direction.West }.Select(d => session.Render(d)).ToList();
            var request = new ShareRequest(sides, session.SpriteScale(), new Preview.ShareText(name, ShareSubtitle(), ShareCredit()), FullCredits(name), name + ".png");
            await new ShareWindow(request, _settings.Share ?? new(), options =>
            {
                _settings = _settings with { Share = options };
                SaveSettings();
            }).ShowDialog(this);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not make a picture: {ex.Message}");
        }
    }

    // "Vulpkanin · Security Officer": the species (or the custom species name) and the job the preview dresses for.
    private string ShareSubtitle()
    {
        var session = _session!;
        var species = session.Content!.Characters.Species[session.Look!.Species];
        var speciesName = session.Fork!.Extras.HasFlag(Core.Forks.ProfileExtras.CustomSpeciesName)
            && session.File!.GetValue(CustomSpeciesName.Key) is { Length: > 0 } custom
                ? custom
                : session.DisplayName(species);
        var dressed = session.ShowClothes || session.PreviewEntity() != null ? session.DressedJob() : null;
        var job = dressed == null ? null : _jobs.FirstOrDefault(j => j.Id == dressed).Name;
        return job == null ? speciesName : $"{speciesName} · {job}";
    }

    // One line for the picture: where the sprites come from and their licences.
    private string ShareCredit()
    {
        var licences = _session!.Credits().Select(c => c.License ?? "licence not stated").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return $"Sprites from {_session.Fork!.Name} for Space Station 14, {string.Join(", ", licences)}";
    }

    // Every sprite in the picture with its licence and authors, to post along with it.
    private string FullCredits(string name)
    {
        var session = _session!;
        var lines = new List<string>
        {
            $"Sprites in this picture of {name}, from {session.Fork!.Name} (github.com/{session.Fork.Repository}, {session.Content!.Commit[..8]}):",
        };
        lines.AddRange(session.Credits().Select(c => $"- {c.Rsi}: {c.License ?? "licence not stated"}; {c.Copyright ?? "authors not stated"}"));
        return string.Join(Environment.NewLine, lines);
    }
}
