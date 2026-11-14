using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Paperdoll.Core.Store;
using SkiaSharp;

namespace Paperdoll.Core.Tests;

/// <summary>How a made-up packed RSI holds its meta.json, as the game's packager writes one or another.</summary>
public enum TextChunk
{
    Text,
    Compressed,
    International,
    InternationalCompressed,
}

/// <summary>One file as a made-up content.db holds it, to be changed before it is written.</summary>
public sealed record TestBlob(byte[] Hash, long Size, long Compression, byte[] Data);

/// <summary>
/// A made-up SS14 launcher folder: a content.db with the launcher's own tables (from its
/// migrations), in WAL mode as the launcher keeps it, holding builds packed as the game's
/// packager packs them. Never a real launcher's.
/// </summary>
public sealed class TestLauncher
{
    /// <summary>The launcher's content.db tables, as its migrations make them.</summary>
    public const string Schema = """
        CREATE TABLE ContentVersion(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Hash BLOB NOT NULL,
            ForkId TEXT NULL,
            ForkVersion TEXT NULL,
            LastUsed DATE NOT NULL,
            ZipHash BLOB NULL);
        CREATE TABLE Content(
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Hash BLOB NOT NULL UNIQUE,
            Size INTEGER NOT NULL,
            Compression INTEGER NOT NULL,
            Data BLOB NOT NULL,
            CONSTRAINT UncompressedSameSize CHECK(Compression != 0 OR length(Data) = Size));
        CREATE TABLE ContentManifest(
            Id INTEGER PRIMARY KEY,
            VersionId INTEGER NOT NULL REFERENCES ContentVersion(Id) ON DELETE CASCADE,
            Path TEXT NOT NULL,
            ContentId INTEGER NOT NULL REFERENCES Content(Id) ON DELETE RESTRICT,
            CONSTRAINT NotDirectory CHECK (Path NOT LIKE '%/'));
        CREATE UNIQUE INDEX ContentManifestUniqueIndex ON ContentManifest(VersionId, Path);
        CREATE TABLE ContentEngineDependency(
            Id INTEGER PRIMARY KEY,
            VersionId INTEGER NOT NULL REFERENCES ContentVersion(Id) ON DELETE CASCADE,
            ModuleName TEXT NOT NULL,
            ModuleVersion TEXT NOT NULL);
        CREATE INDEX ContentManifest_ContentId ON ContentManifest(ContentId);
        CREATE TABLE InterruptedDownload(Id INTEGER PRIMARY KEY AUTOINCREMENT, Added DATE NOT NULL);
        CREATE TABLE RunningClient(ProcessId INTEGER PRIMARY KEY NOT NULL, MainModule TEXT NOT NULL, UsedVersion INTEGER NOT NULL);
        """;

    public TestLauncher(string folder, string schema = Schema)
    {
        Folder = Directory.CreateDirectory(folder).FullName;
        using var connection = Connect();
        Execute(connection, "PRAGMA journal_mode=WAL");
        Execute(connection, schema);
    }

    public string Folder { get; }

    public string Db => Path.Combine(Folder, "content.db");

    // No pooling, so the file is let go, and its -wal and -shm files gone, once written.
    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    /// <summary>Changes the database as the launcher would, such as while a copy is being taken.</summary>
    public void Execute(string sql)
    {
        using var connection = Connect();
        Execute(connection, sql);
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Adds a build: each file (by its path below Resources) stored with the compression given
    /// (0 none, 1 deflate, 2 zstd), changed by <paramref name="change"/> when given. A build still
    /// downloading has an all-zero hash. Returns its id.
    /// </summary>
    public long AddBuild(string? forkId, string? forkVersion, DateTime lastUsed, IReadOnlyDictionary<string, byte[]> files, long compression = 2,
        bool downloading = false, Func<string, TestBlob, TestBlob>? change = null)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var hash = downloading ? new byte[32] : Blake2Fast.Blake2b.ComputeHash(32, Encoding.UTF8.GetBytes(string.Join("\n", files.Keys.Order(StringComparer.Ordinal)) + forkVersion + lastUsed.Ticks));
        long version;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO ContentVersion(Hash, ForkId, ForkVersion, LastUsed) VALUES ($hash, $fork, $version, $used) RETURNING Id";
            command.Parameters.AddWithValue("$hash", hash);
            command.Parameters.AddWithValue("$fork", (object?)forkId ?? DBNull.Value);
            command.Parameters.AddWithValue("$version", (object?)forkVersion ?? DBNull.Value);
            command.Parameters.AddWithValue("$used", lastUsed.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            version = (long)command.ExecuteScalar()!;
        }
        foreach (var (path, data) in files)
        {
            var blob = Blob(data, compression);
            if (change != null)
                blob = change(path, blob);
            Execute(connection, "INSERT OR IGNORE INTO Content(Hash, Size, Compression, Data) VALUES ($hash, $size, $compression, $data)",
                ("$hash", blob.Hash), ("$size", blob.Size), ("$compression", blob.Compression), ("$data", blob.Data));
            Execute(connection, "INSERT INTO ContentManifest(VersionId, Path, ContentId) VALUES ($version, $path, (SELECT Id FROM Content WHERE Hash = $hash))",
                ("$version", version), ("$path", path), ("$hash", blob.Hash));
        }
        transaction.Commit();
        return version;
    }

    /// <summary>A file as the launcher stores it.</summary>
    public static TestBlob Blob(byte[] data, long compression) => new(Blake2Fast.Blake2b.ComputeHash(32, data), data.Length, compression, compression switch
    {
        0 => data,
        1 => Deflate(data),
        // Streamed, as the launcher does, so the frame does not say its size.
        _ => Zstd(data),
    });

    public static byte[] Deflate(byte[] data)
    {
        var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(data);
        return output.ToArray();
    }

    public static byte[] Zstd(byte[] data)
    {
        var output = new MemoryStream();
        using (var zstd = new ZstdSharp.CompressionStream(output, 3, leaveOpen: true))
            zstd.Write(data);
        return output.ToArray();
    }

    /// <summary>
    /// A made-up fork's files (from <see cref="TestForks.Store"/>) as the game's packager packs
    /// them for the launcher: paths below Resources, prototypes and strings merged per folder,
    /// and each RSI packed into an .rsic, but one with "rsic": false. A state without a picture
    /// is given a made-up one, as the packager needs every state's.
    /// </summary>
    public static Dictionary<string, byte[]> Build(string forkId, TextChunk form = TextChunk.Compressed) =>
        Package(TestForks.Store().FilesOf(forkId).ToDictionary(f => f.Key["Resources/".Length..], f => f.Value), form);

    /// <summary>Files below Resources packed as the game's packager packs them.</summary>
    public static Dictionary<string, byte[]> Package(IReadOnlyDictionary<string, byte[]> files, TextChunk form = TextChunk.Compressed)
    {
        var packed = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var rsis = files.Keys.Where(p => p.StartsWith("Textures/", StringComparison.Ordinal) && p.EndsWith(".rsi/meta.json", StringComparison.Ordinal))
            .Select(p => p[..^"/meta.json".Length]).ToList();
        var merged = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (path, data) in files)
        {
            var folder = path[..path.LastIndexOf('/')];
            if ((path.StartsWith("Prototypes/", StringComparison.Ordinal) && path.EndsWith(".yml", StringComparison.Ordinal))
                || (path.StartsWith("Locale/", StringComparison.Ordinal) && path.EndsWith(".ftl", StringComparison.Ordinal)))
            {
                var key = folder + "/__merged." + path[^3..];
                if (!merged.TryGetValue(key, out var list))
                    merged[key] = list = [];
                list.Add(path);
            }
            else if (!rsis.Any(r => path.StartsWith(r + "/", StringComparison.Ordinal)))
                packed[path] = data;
        }
        foreach (var (key, paths) in merged)
            packed[key] = Merge(paths.Order(StringComparer.Ordinal).Select(p => (p, files[p])), key.EndsWith(".yml", StringComparison.Ordinal));
        foreach (var rsi in rsis)
        {
            var meta = files[rsi + "/meta.json"];
            using var doc = JsonDocument.Parse(meta);
            var states = doc.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()!).ToList();
            if (doc.RootElement.TryGetProperty("rsic", out var rsic) && rsic.ValueKind == JsonValueKind.False)
            {
                foreach (var (path, data) in files.Where(f => f.Key.StartsWith(rsi + "/", StringComparison.Ordinal)))
                    packed[path] = data;
                continue;
            }
            packed[rsi + "c"] = PackRsi(meta, state => files.GetValueOrDefault($"{rsi}/{state}.png") ?? MadeUpState(meta, state), form);
        }
        return packed;
    }

    /// <summary>Files merged as the packager merges one folder's: each between BEGIN and END lines, its lines each ended by a newline.</summary>
    public static byte[] Merge(IEnumerable<(string Path, byte[] Data)> files, bool yml)
    {
        var text = new StringBuilder();
        foreach (var (path, data) in files)
        {
            text.Append(yml ? "--- # BEGIN " : "# BEGIN ").Append(path).Append('\n');
            foreach (var line in Lines(Encoding.UTF8.GetString(data)))
                text.Append(line).Append('\n');
            text.Append("# END ").Append(path).Append('\n');
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    // The lines of a text as a StreamReader reads them.
    public static IEnumerable<string> Lines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    // Frames per state as the engine counts them: delay lists' lengths, an empty one one, or one per direction.
    private static List<(string Name, int Frames)> Frames(byte[] meta)
    {
        using var doc = JsonDocument.Parse(meta);
        return doc.RootElement.GetProperty("states").EnumerateArray().Select(s =>
        {
            var directions = s.TryGetProperty("directions", out var d) ? d.GetInt32() : 1;
            var frames = s.TryGetProperty("delays", out var delays) ? delays.EnumerateArray().Sum(l => Math.Max(1, l.GetArrayLength())) : directions;
            return (s.GetProperty("name").GetString()!, frames);
        }).ToList();
    }

    private static (int X, int Y) Size(byte[] meta)
    {
        using var doc = JsonDocument.Parse(meta);
        var size = doc.RootElement.GetProperty("size");
        return (size.GetProperty("x").GetInt32(), size.GetProperty("y").GetInt32());
    }

    // A picture for a state with none: each frame a colour of its own.
    private static byte[] MadeUpState(byte[] meta, string state)
    {
        var (w, h) = Size(meta);
        var frames = Frames(meta).First(s => s.Name == state).Frames;
        return StatePng(w, h, frames, state.Length);
    }

    /// <summary>
    /// A state's picture in an RSI folder's layout (<paramref name="columns"/> to a row, the square
    /// root rounded up when not given), each frame a pattern of its own, partly see-through.
    /// </summary>
    public static byte[] StatePng(int width, int height, int frames, int seed, int? columns = null)
    {
        var across = columns ?? (int)Math.Ceiling(Math.Sqrt(frames));
        var down = (frames + across - 1) / across;
        return TestRooms.Png(across * width, down * height, (x, y) =>
        {
            var frame = y / height * across + x / width;
            if (frame >= frames)
                return SKColors.Transparent;
            return new SKColor((byte)(frame * 37 + seed), (byte)(x % width * 7 + seed), (byte)(y % height * 5), (byte)(64 + (x + y + frame) % 192));
        });
    }

    /// <summary>
    /// Packs an RSI as the game's packager does: every state's frames, in meta order, laid left to
    /// right then top to bottom in as many columns as the square root of their count rounded up,
    /// with the meta.json in a text chunk of the form given.
    /// </summary>
    public static byte[] PackRsi(byte[] meta, Func<string, byte[]> statePng, TextChunk form)
    {
        var (w, h) = Size(meta);
        var states = Frames(meta);
        var total = states.Sum(s => s.Frames);
        var columns = (int)MathF.Ceiling(MathF.Sqrt(total));
        var rows = (int)MathF.Ceiling((float)total / columns);
        using var atlas = new SKBitmap(new SKImageInfo(columns * w, rows * h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        atlas.Erase(SKColors.Transparent);
        var index = 0;
        foreach (var (name, frames) in states)
        {
            using var picture = Decode(statePng(name));
            var across = picture.Width / w;
            for (var i = 0; i < frames; i++, index++)
            {
                for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    atlas.SetPixel(index % columns * w + x, index / columns * h + y, picture.GetPixel(i % across * w + x, i / across * h + y));
            }
        }
        using var data = atlas.Encode(SKEncodedImageFormat.Png, 100);
        return WithText(data.ToArray(), Rsic.MetaKeyword, Encoding.UTF8.GetString(meta), form);
    }

    public static SKBitmap Decode(byte[] png)
    {
        using var codec = SKCodec.Create(new MemoryStream(png));
        var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(bitmap.Info, bitmap.GetPixels()));
        return bitmap;
    }

    /// <summary>A PNG with a text chunk put in after its header.</summary>
    public static byte[] WithText(byte[] png, string keyword, string text, TextChunk form)
    {
        var key = Encoding.Latin1.GetBytes(keyword);
        byte[] body = form switch
        {
            TextChunk.Text => [.. key, 0, .. Encoding.Latin1.GetBytes(text)],
            TextChunk.Compressed => [.. key, 0, 0, .. ZLib(Encoding.Latin1.GetBytes(text))],
            TextChunk.International => [.. key, 0, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)],
            _ => [.. key, 0, 1, 0, 0, 0, .. ZLib(Encoding.UTF8.GetBytes(text))],
        };
        var type = form switch { TextChunk.Text => "tEXt", TextChunk.Compressed => "zTXt", _ => "iTXt" };
        return WithChunk(png, type, body);
    }

    /// <summary>A PNG with a chunk put in after its header, its CRC right unless told otherwise.</summary>
    public static byte[] WithChunk(byte[] png, string type, byte[] body, bool badCrc = false)
    {
        var chunk = new byte[12 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)body.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        body.CopyTo(chunk, 8);
        var crc = Rsic.Crc32(chunk.AsSpan(4, 4 + body.Length));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + body.Length), badCrc ? ~crc : crc);
        // The signature (8) and the header chunk (25) come first.
        return [.. png[..33], .. chunk, .. png[33..]];
    }

    public static byte[] ZLib(byte[] data)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    /// <summary>
    /// Every file in a folder, with its size and when it last changed: to show that nothing in
    /// the launcher's folder was written, made or deleted, -wal, -shm and -journal files included.
    /// </summary>
    public static IReadOnlyList<string> Snapshot(string folder) => TestGameFolders.Snapshot(folder);
}
