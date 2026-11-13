using System.Collections.Concurrent;
using System.Globalization;
using Paperdoll.Core.Forks;
using Paperdoll.Core.Prototypes;
using Paperdoll.Core.Rooms;
using Paperdoll.Core.Store;
using SkiaSharp;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Editing;

/// <summary>The demo room floor: a small hand-made room drawn from the fork's own sprites.</summary>
public sealed partial class EditorSession
{
    // Each room as drawn, once per fork version; a room whose file has changed is drawn again.
    private readonly ConcurrentDictionary<(string Commit, string Room), RoomScene> _roomScenes = new();

    // Each loose picture a room draws (a tile's, or another), with its credit, read once per fork version.
    private readonly ConcurrentDictionary<(string Commit, string Path), (SKImage? Picture, CreditLine Credit)> _tilePictures = new();

    /// <summary>
    /// A room with every picture it draws from the loaded fork. The pictures are downloaded in one
    /// batch when first needed and read away from the caller's thread; a room once drawn is kept
    /// for this version of the fork. A download that fails or times out throws; only when stopped
    /// through <paramref name="ct"/> or by other store work (a fork load, update, removal or
    /// clean-up) does it throw <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<RoomScene> RoomAsync(Room room, CancellationToken ct = default)
    {
        var content = RequireContent();
        if (_roomScenes.TryGetValue((content.Commit, room.Id), out var known) && known.Room.Text == room.Text)
            return known;
        var otherWork = _downloads.Token;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, otherWork);
        try
        {
            return await Task.Run(async () =>
            {
                var plan = RoomPlan.Make(room, content.Prototypes);
                var textures = ForkContent.TexturesFolder + "/";
                var tilePictures = plan.TileIds.Select(id => SpritePath(content.Prototypes.Resolve("tile", id))).OfType<string>();
                var others = plan.RsiFiles.Select(f => textures + f).ToList();
                // The top folders of Textures that hold the RSIs, as the fork's own load lists them.
                var folders = ForkContent.ListingRoots(plan.RsiFiles.Select(f => f[..f.LastIndexOf('/')]));
                var files = await ReadWithPicturesAsync(content, tilePictures.Concat(plan.Pictures.Select(p => textures + p)), others, folders, stop.Token);
                var scene = RoomScene.Build(plan, content.Commit,
                    path => files.GetValueOrDefault(textures + path),
                    path => _tilePictures.TryGetValue((content.Commit, textures + path), out var picture) ? picture : null,
                    id => TilePictureOf(content, id));
                return _roomScenes[(content.Commit, room.Id)] = scene;
            }, stop.Token);
        }
        catch (OperationCanceledException e) when (!ct.IsCancellationRequested && !otherWork.IsCancellationRequested)
        {
            // A download that timed out stops itself: a failure, not a stop.
            throw new IOException($"The download stopped before it finished: {e.Message}", e);
        }
    }

    /// <summary>
    /// Downloads and reads, in one batch, the loose pictures (each a "Resources/..." path) not read
    /// before, with the credits beside them, and the other files wanted (also "Resources/..."
    /// paths), which are listed within <paramref name="otherFolders"/>; returns those of the other
    /// files the fork has.
    /// </summary>
    private async Task<Dictionary<string, byte[]>> ReadWithPicturesAsync(ForkContent content, IEnumerable<string> pictures,
        IReadOnlyCollection<string> others, IReadOnlyList<string> otherFolders, CancellationToken ct)
    {
        // Only the pictures not read before are downloaded, with the credits beside them.
        var missing = pictures
            .Where(p => !_tilePictures.ContainsKey((content.Commit, p)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count == 0 && others.Count == 0)
            return [];
        var folders = missing.Select(ParentOf).Distinct(StringComparer.Ordinal).ToList();
        var wanted = missing.Concat(folders.SelectMany(f => CreditFiles.Select(c => f + "/" + c))).Concat(others).ToHashSet(StringComparer.Ordinal);
        // A folder inside another is listed with it.
        var all = folders.Concat(otherFolders).Distinct(StringComparer.Ordinal).ToList();
        var listed = all.Where(f => !all.Any(o => f.StartsWith(o + "/", StringComparison.Ordinal))).ToList();
        var files = await ReadStoreFilesAsync(content, listed, wanted, ct);
        foreach (var path in missing)
        {
            var name = path.StartsWith(ForkContent.TexturesFolder + "/", StringComparison.Ordinal) ? path[(ForkContent.TexturesFolder.Length + 1)..] : path;
            var file = path[(path.LastIndexOf('/') + 1)..];
            var credit = CreditFiles.Select(c => TileCredits.Find(files.GetValueOrDefault(ParentOf(path) + "/" + c), file)).FirstOrDefault(c => c != null) is { } found
                ? new CreditLine(name, found.License, found.Copyright)
                : new CreditLine(name, null, "no credit listed");
            _tilePictures[(content.Commit, path)] = (Decode(files.GetValueOrDefault(path)), credit);
        }
        return others.Where(files.ContainsKey).ToDictionary(p => p, p => files[p], StringComparer.Ordinal);
    }

    // A tile prototype as drawn, once its picture has been read; without one, a plain plate.
    private TilePicture TilePictureOf(ForkContent content, string id)
    {
        var node = content.Prototypes.Resolve("tile", id);
        if (SpritePath(node) is not { } path || !_tilePictures.TryGetValue((content.Commit, path), out var picture))
            return new TilePicture(id, null, 1, false, null);
        var variants = int.TryParse(PrototypeIndex.Scalar(node!, "variants"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? Math.Max(1, count) : 1;
        var turns = PrototypeIndex.Scalar(node!, "allowRotationMirror") is "true" or "True";
        return new TilePicture(id, picture.Picture, variants, turns, picture.Credit);
    }

    // The credit lists a picture's folder may have; upstream's tile folders use both names.
    private static readonly string[] CreditFiles = ["attributions.yml", "attribution.yml"];

    // "Resources/Textures/Tiles/steel.png" -> "Resources/Textures/Tiles"
    private static string ParentOf(string path) => path[..Math.Max(0, path.LastIndexOf('/'))];

    // "/Textures/Tiles/steel.png" -> "Resources/Textures/Tiles/steel.png"
    private static string? SpritePath(YamlMappingNode? tile) =>
        tile != null && PrototypeIndex.Scalar(tile, "sprite") is { Length: > 0 } sprite ? "Resources/" + sprite.TrimStart('/') : null;

    // A picture that cannot be decoded is drawn as a plain plate.
    private static SKImage? Decode(byte[]? png)
    {
        if (png == null)
            return null;
        try
        {
            using var bitmap = SKBitmap.Decode(png);
            return bitmap == null ? null : SKImage.FromBitmap(bitmap);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Lists folders of the fork as read, downloads the wanted files among them and reads them, in
    // the store's turn.
    private async Task<Dictionary<string, byte[]>> ReadStoreFilesAsync(ForkContent content, IReadOnlyList<string> folders, IReadOnlySet<string> wanted, CancellationToken ct)
    {
        var forkId = content.Fork.Id;
        await _storeTurn.WaitAsync(ct);
        try
        {
            // The store lists the fork's version now, which an update may have moved on.
            if (await Store.CommitOfAsync(forkId, ct) != content.Commit)
                throw new InvalidOperationException($"{content.Fork.Name} has changed since it was loaded; load it again to see its floors.");
            var entries = (await Store.ListAsync(forkId, folders, ct)).Where(e => wanted.Contains(e.Path)).ToList();
            await Store.FetchAsync(forkId, entries, ct);
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            try
            {
                var reader = _reader ??= Store.OpenReader();
                foreach (var entry in entries)
                {
                    if (await reader.ReadAsync(entry.ObjectId, ct) is { } data)
                        files[entry.Path] = data;
                }
            }
            catch (Exception e) when (e is IOException or GitException && !ct.IsCancellationRequested)
            {
                // The reader has stopped; the next try starts another.
                await CloseReaderAsync();
                throw;
            }
            return files;
        }
        finally
        {
            _storeTurn.Release();
        }
    }
}
