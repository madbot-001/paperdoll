// The atlas layout read here is the one RobustToolbox writes (Robust.Shared/Resources/RsiLoading.cs,
// GenerateAtlas and CalculateFrameCounts; Robust.Packaging/AssetProcessing/Passes/AssetPassPackRsis.cs),
// MIT licensed: Copyright (c) 2019 Space Station 14 Contributors. See THIRD-PARTY-NOTICES.md.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace Paperdoll.Core.Store;

/// <summary>
/// Unpacks an <c>.rsic</c> file, as the SS14 launcher holds an RSI: one PNG holding every frame of
/// every state, its meta.json in a text chunk named <see cref="MetaKeyword"/>. Gives back the
/// meta.json and one PNG per state, laid out as an RSI folder's are, so they read as the RSI did
/// before it was packed. The file is taken as untrusted: its chunks are checked (lengths and
/// CRCs), and its size, frame size and frame and state counts are capped before it is decoded.
/// </summary>
public static class Rsic
{
    public const string MetaKeyword = "robusttoolbox_rsic_meta";

    /// <summary>The widest or tallest packed picture taken.</summary>
    public const int MaxSide = 16384;

    /// <summary>The most pixels a packed picture may hold: 128 MB once decoded.</summary>
    public const long MaxPixels = 32L * 1024 * 1024;

    public const int MaxFrameSide = 4096;
    public const int MaxStates = 4096;
    public const int MaxFrames = 65536;

    /// <summary>The largest meta.json taken, decompressed.</summary>
    public const int MaxMeta = 1024 * 1024;

    private const int MaxChunks = 100_000;

    /// <summary>An unpacked RSI: its meta.json, and each state's name and picture, in meta order.</summary>
    public sealed record Unpacked(byte[] MetaJson, IReadOnlyList<(string State, byte[] Png)> States);

    /// <summary>One state as meta.json gives it: its name and how many frames it has in all.</summary>
    public readonly record struct StateFrames(string Name, int Frames);

    /// <summary>Unpacks an .rsic file; throws <see cref="FormatException"/> saying what is wrong with it.</summary>
    public static Unpacked Unpack(byte[] png)
    {
        var (width, height, metaText) = ReadChunks(png);
        if (metaText == null)
            throw new FormatException($"it has no {MetaKeyword} text, which every packed RSI has");
        var metaJson = Encoding.UTF8.GetBytes(metaText);
        var (frameWidth, frameHeight, states) = ReadMeta(metaJson);

        var total = states.Sum(s => (long)s.Frames);
        if (total == 0)
            throw new FormatException("its meta.json lists no states");
        if (total > MaxFrames)
            throw new FormatException($"it has more than {MaxFrames} frames");
        var (columns, rows) = Grid((int)total);
        if ((long)columns * frameWidth != width || (long)rows * frameHeight != height)
            throw new FormatException($"its picture is {width} by {height}, not the {columns * frameWidth} by {rows * frameHeight} its {total} frames of {frameWidth} by {frameHeight} take");

        var pixels = Decode(png, width, height);
        var unpacked = new List<(string, byte[])>(states.Count);
        var at = 0;
        foreach (var state in states)
        {
            unpacked.Add((state.Name, StatePicture(pixels, width, frameWidth, frameHeight, columns, at, state.Frames)));
            at += state.Frames;
        }
        return new Unpacked(metaJson, unpacked);
    }

    /// <summary>
    /// The columns and rows <paramref name="frames"/> frames take: as many columns as the square
    /// root rounded up, then as many rows as needed. RSI pictures and packed ones both use it.
    /// </summary>
    public static (int Columns, int Rows) Grid(int frames)
    {
        // As the engine works it out, in single precision.
        var columns = (int)MathF.Ceiling(MathF.Sqrt(frames));
        var rows = (int)MathF.Ceiling((float)frames / columns);
        return (columns, rows);
    }

    // The picture's size from IHDR and the meta.json text, every chunk's length and CRC checked.
    private static (int Width, int Height, string? Meta) ReadChunks(byte[] png)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < signature.Length || !png.AsSpan(0, signature.Length).SequenceEqual(signature))
            throw new FormatException("it is not a PNG");
        var at = signature.Length;
        int width = 0, height = 0;
        string? meta = null;
        for (var chunks = 0; ; chunks++)
        {
            if (chunks > MaxChunks)
                throw new FormatException("it has too many PNG chunks");
            if (png.Length - at < 12)
                throw new FormatException("it ends part way through a PNG chunk");
            var length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            if (length > (uint)(png.Length - at - 12))
                throw new FormatException("a PNG chunk runs past its end");
            var type = Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, (int)length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + (int)length));
            if (Crc32(png.AsSpan(at + 4, 4 + (int)length)) != crc)
                throw new FormatException($"its {type} PNG chunk fails its CRC check");
            at += 12 + (int)length;

            if (chunks == 0)
            {
                if (type != "IHDR" || length != 13)
                    throw new FormatException("it does not start with a PNG header");
                var w = BinaryPrimitives.ReadUInt32BigEndian(data);
                var h = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                if (w == 0 || h == 0 || w > MaxSide || h > MaxSide || (long)w * h > MaxPixels)
                    throw new FormatException($"its picture is {w} by {h}, larger than Paperdoll takes ({MaxSide} by {MaxSide} at most, {MaxPixels / (1024 * 1024)} million pixels in all)");
                (width, height) = ((int)w, (int)h);
            }
            else if (type is "tEXt" or "zTXt" or "iTXt" && TextOf(type, data) is { } text)
            {
                if (meta != null)
                    throw new FormatException($"it has more than one {MetaKeyword} text");
                meta = text;
            }
            else if (type == "IEND")
                return (width, height, meta);
        }
    }

    // The text of a tEXt, zTXt or iTXt chunk named MetaKeyword, or null for one named otherwise.
    private static string? TextOf(string type, ReadOnlySpan<byte> data)
    {
        var nul = data.IndexOf((byte)0);
        if (nul < 0 || !data[..nul].SequenceEqual(Encoding.Latin1.GetBytes(MetaKeyword)))
            return null;
        var rest = data[(nul + 1)..];
        switch (type)
        {
            case "tEXt":
                return Latin1(rest);
            case "zTXt":
                if (rest.Length < 1 || rest[0] != 0)
                    throw new FormatException("its meta.json text is compressed in a way PNG does not define");
                return Encoding.Latin1.GetString(Inflate(rest[1..]));
            default:
                // iTXt: compressed flag, method, language tag, translated keyword, then UTF-8 text.
                if (rest.Length < 2 || rest[0] > 1 || rest[1] != 0)
                    throw new FormatException("its meta.json text is compressed in a way PNG does not define");
                var compressed = rest[0] == 1;
                rest = rest[2..];
                for (var skip = 0; skip < 2; skip++)
                {
                    var end = rest.IndexOf((byte)0);
                    if (end < 0)
                        throw new FormatException("its meta.json text chunk is cut short");
                    rest = rest[(end + 1)..];
                }
                return Utf8(compressed ? Inflate(rest) : rest.ToArray());
        }
    }

    private static string Latin1(ReadOnlySpan<byte> text) =>
        text.Length > MaxMeta ? throw new FormatException("its meta.json is larger than 1 MB") : Encoding.Latin1.GetString(text);

    private static string Utf8(byte[] text)
    {
        if (text.Length > MaxMeta)
            throw new FormatException("its meta.json is larger than 1 MB");
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(text);
        }
        catch (DecoderFallbackException)
        {
            throw new FormatException("its meta.json is not UTF-8 text");
        }
    }

    // Inflates zlib data, stopping past MaxMeta.
    private static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        try
        {
            using var stream = new ZLibStream(new MemoryStream(data.ToArray()), CompressionMode.Decompress);
            var buffer = new byte[MaxMeta + 1];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return read > MaxMeta ? throw new FormatException("its meta.json is larger than 1 MB") : buffer[..read];
        }
        catch (InvalidDataException e)
        {
            throw new FormatException($"its meta.json text could not be decompressed: {e.Message}");
        }
    }

    /// <summary>
    /// The frame size and each state's frame count from a meta.json, read as the engine reads it:
    /// property names in any case, numbers written as text taken. A state has the sum of its
    /// delay lists' lengths in frames, an empty list counting one, or one per direction without
    /// delays. What the engine refuses (directions other than 1, 4 or 8, a delay list per
    /// direction missing, a state named twice) is refused too, as are caps passed.
    /// </summary>
    public static (int FrameWidth, int FrameHeight, IReadOnlyList<StateFrames> States) ReadMeta(byte[] json)
    {
        if (json.Length > MaxMeta)
            throw new FormatException("its meta.json is larger than 1 MB");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException e)
        {
            throw new FormatException($"its meta.json is not JSON: {e.Message}");
        }
        using (document)
        {
            var root = document.RootElement;
            if (Get(root, "size") is not { } size || Int(Get(size, "x")) is not { } x || Int(Get(size, "y")) is not { } y
                || x is < 1 or > MaxFrameSide || y is < 1 or > MaxFrameSide)
                throw new FormatException($"its meta.json gives no frame size from 1 to {MaxFrameSide}");
            if (Get(root, "states") is not { ValueKind: JsonValueKind.Array } list)
                throw new FormatException("its meta.json has no list of states");
            if (list.GetArrayLength() > MaxStates)
                throw new FormatException($"its meta.json has more than {MaxStates} states");
            var states = new List<StateFrames>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in list.EnumerateArray())
            {
                if (Get(state, "name") is not { ValueKind: JsonValueKind.String } nameValue || nameValue.GetString() is not { Length: > 0 } name)
                    throw new FormatException("a state in its meta.json has no name");
                if (!names.Add(name))
                    throw new FormatException($"its meta.json names the state {LauncherCache.Shown(name)} twice");
                var directions = Get(state, "directions") is { } d ? Int(d) : 1;
                if (directions is not (1 or 4 or 8))
                    throw new FormatException($"the state {LauncherCache.Shown(name)} in its meta.json has a number of directions other than 1, 4 or 8");
                var frames = directions.Value;
                if (Get(state, "delays") is { } delays)
                {
                    if (delays.ValueKind != JsonValueKind.Array || delays.GetArrayLength() != directions
                        || delays.EnumerateArray().Any(list => list.ValueKind != JsonValueKind.Array))
                        throw new FormatException($"the state {LauncherCache.Shown(name)} in its meta.json does not give one list of delays per direction");
                    frames = delays.EnumerateArray().Sum(list => Math.Max(1, list.GetArrayLength()));
                }
                states.Add(new StateFrames(name, frames));
            }
            return (x, y, states);
        }
    }

    // A property by name in any case, as the engine's web-style JSON reading finds it.
    private static JsonElement? Get(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
        }
        return null;
    }

    // A whole number, or one written as text.
    private static int? Int(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.Number } n when n.TryGetInt32(out var i) => i,
        { ValueKind: JsonValueKind.String } t when int.TryParse(t.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) => i,
        _ => null,
    };

    // The picture's pixels as RGBA, not premultiplied, so every pixel comes out as it went in.
    private static byte[] Decode(byte[] png, int width, int height)
    {
        using var codec = SKCodec.Create(new MemoryStream(png), out var result);
        if (codec == null || codec.Info.Width != width || codec.Info.Height != height)
            throw new FormatException($"its picture could not be read ({result})");
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[(long)width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var decoded = codec.GetPixels(info, handle.AddrOfPinnedObject());
            if (decoded != SKCodecResult.Success)
                throw new FormatException($"its picture could not be read ({decoded})");
        }
        finally
        {
            handle.Free();
        }
        return pixels;
    }

    // One state's frames, from the packed picture's frame `first` on, as a PNG in the layout an
    // RSI folder's state picture has: frames left to right, then top to bottom, in Grid's columns.
    private static byte[] StatePicture(byte[] atlas, int atlasWidth, int frameWidth, int frameHeight, int atlasColumns, int first, int frames)
    {
        var (columns, rows) = Grid(frames);
        var info = new SKImageInfo(columns * frameWidth, rows * frameHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = new byte[(long)info.Width * info.Height * 4];
        var row = frameWidth * 4;
        for (var i = 0; i < frames; i++)
        {
            var from = first + i;
            var (fromX, fromY) = (from % atlasColumns * frameWidth, from / atlasColumns * frameHeight);
            var (toX, toY) = (i % columns * frameWidth, i / columns * frameHeight);
            for (var y = 0; y < frameHeight; y++)
                Array.Copy(atlas, ((long)(fromY + y) * atlasWidth + fromX) * 4, pixels, ((long)(toY + y) * info.Width + toX) * 4, row);
        }
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject());
            using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6))
                ?? throw new FormatException("a state's picture could not be written");
            return data.ToArray();
        }
        finally
        {
            handle.Free();
        }
    }

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>The CRC-32 PNG chunks carry, over a chunk's type and data.</summary>
    public static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
