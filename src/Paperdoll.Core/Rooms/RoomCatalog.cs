using System.Globalization;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rendering;

namespace Paperdoll.Core.Rooms;

/// <summary>
/// The demo rooms: those built into Paperdoll, each replaced by a file of the same name in the
/// user's rooms folder when there is one, so rooms can be edited. A user file that cannot be read
/// is noted in <see cref="Problems"/> and the built-in room is used.
/// </summary>
public sealed class RoomCatalog
{
    private const string ResourcePrefix = "Paperdoll.Core.Rooms.";

    /// <summary>The room for a job no room names, by its own id or its department.</summary>
    public const string FallbackRoom = "hallway";

    public RoomCatalog(IReadOnlyDictionary<string, Room> rooms, IReadOnlyList<string> problems)
    {
        Rooms = rooms;
        Problems = problems;
    }

    /// <summary>The rooms by id.</summary>
    public IReadOnlyDictionary<string, Room> Rooms { get; }

    /// <summary>Why a user's room file was not used, one line each.</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>Reads the built-in rooms and the user's replacements from <paramref name="userFolder"/>, if it exists.</summary>
    public static RoomCatalog Load(string? userFolder)
    {
        var assembly = typeof(RoomCatalog).Assembly;
        var rooms = new Dictionary<string, Room>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".yml", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var fileName = resource[ResourcePrefix.Length..];
            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            var builtIn = Room.TryParse(reader.ReadToEnd(), out var builtInProblem)
                ?? throw new InvalidDataException($"The built-in room {fileName} cannot be read: {builtInProblem}");

            var room = builtIn;
            var userFile = userFolder == null ? null : Path.Combine(userFolder, fileName);
            if (userFile != null && File.Exists(userFile))
            {
                try
                {
                    room = Room.TryParse(File.ReadAllText(userFile), out var problem)
                        ?? Fallback($"{userFile} could not be read: {problem}.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    room = Fallback($"{userFile} could not be opened: {e.Message}");
                }
            }
            rooms[room.Id] = room;

            Room Fallback(string problem)
            {
                problems.Add($"{problem} The built-in {fileName} is used.");
                return builtIn;
            }
        }
        return new RoomCatalog(rooms, problems);
    }

    /// <summary>
    /// The room for a job: one that names the job, else one that names a department the job is in
    /// (the fork's <c>department</c> prototypes, by weight as the game sorts them), else
    /// <see cref="FallbackRoom"/>.
    /// </summary>
    public Room For(string? jobId, PrototypeIndex prototypes)
    {
        var rooms = Rooms.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        if (jobId != null)
        {
            if (rooms.FirstOrDefault(r => r.Jobs.Contains(jobId)) is { } own)
                return own;
            var departments = prototypes.OfKind("department")
                .Select(d => (d.Id, Node: prototypes.Resolve("department", d.Id)))
                .Where(d => PrototypeYaml.Strings(d.Node, "roles").Contains(jobId))
                .OrderByDescending(d => int.TryParse(PrototypeYaml.Scalar(d.Node, "weight"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) ? weight : 0)
                .ThenBy(d => d.Id, StringComparer.Ordinal);
            foreach (var (department, _) in departments)
            {
                if (rooms.FirstOrDefault(r => r.Departments.Contains(department)) is { } room)
                    return room;
            }
        }
        return Rooms.GetValueOrDefault(FallbackRoom) ?? rooms[0];
    }
}
