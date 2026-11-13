// Ported from RobustToolbox (Resources/Shaders/Internal/light_shared.swsl, the falloff;
// Robust.Client/Graphics/Clyde/Clyde.LightRendering.cs and Shaders/shadow-depth.vert, the shadow map;
// Resources/Shaders/Internal/light-blur.swsl and wall-bleed-blur.swsl, the blurs; Robust.Shared.Maths/Color.cs,
// FromSrgb and ToSrgb), Copyright (c) 2019 Space Station 14 Contributors, and Space Station 14
// (Content.Server/Light/EntitySystems/SpaceLightSystem.cs and Content.Client/Light/*Overlay.cs, the
// ambient light), Copyright (c) 2017-2026 Space Wizards Federation; both MIT licence. See THIRD-PARTY-NOTICES.md.

using System.Runtime.CompilerServices;
using System.Numerics;
using Paperdoll.Core.Rendering;
using SkiaSharp;

namespace Paperdoll.Core.Lighting;

/// <summary>
/// The light over a room as the engine's light pass computes it, in linear RGB, at
/// <see cref="SamplesPerTile"/> samples a tile over <see cref="Bounds"/>, and the multiply that
/// lights a picture with it.
/// </summary>
/// <remarks>
/// <para>
/// In order, as <c>Clyde.LightRendering.cs</c> and content's before-lighting overlays do it: the
/// map's ambient light over squares open to space (a station's grids have a black roof over every
/// other tile), blurred; each light added, <c>FromSrgb(colour) · energy · att(d) · cones · visible</c>,
/// with sums above 1 kept; the light buffer blurred (<c>light.blur</c>); then inside every occluder
/// the "wall bleed", a wider blur of the buffer times 1.1 per pass, so that walls are not black.
/// </para>
/// <para>
/// Shadows are hard: a shadow-casting light keeps, for each of 512 directions (the engine's shadow
/// map width), the nearest edge of the occluders' outline facing it, and a sample is lit when it is
/// not past that edge's line. The engine's soft-shadow penumbra (at most about 0.3 × softness tiles)
/// is left out. A light mask is read from its prototype's cones, as the engine's own CPU light level
/// does, not from its texture.
/// </para>
/// </remarks>
public sealed class LightField
{
    /// <summary>
    /// The engine's light buffer at its default zoom: half the viewport (<c>light.resolution_scale</c>)
    /// at 32 pixels a tile.
    /// </summary>
    public const int MaxSamplesPerTile = 16;

    /// <summary>
    /// The most light-sample pairs a field adds up: a room with more lights over more of it gets fewer
    /// samples a tile, so that the worst room still takes well under a second.
    /// </summary>
    public const double MaxWork = 40_000_000;

    // The engine's shadow map width (ShadowMapSize, Clyde.LightRendering.cs).
    private const int Bins = 512;

    // Each edge's angle is widened by 0.001 rad, so neighbouring edges leave no gap
    // (DEPTH_LEFTRIGHT_EXPAND_BIAS, Robust.Client/Graphics/Clyde/Shaders/shadow-depth.vert).
    private const float EdgeWidening = 0.001f;

    // The blurs, in tiles. Each is three passes of a five-tap kernel whose variance is its offset
    // squared, the offsets (i + 1) × factor for i = 3, 2, 1 (Clyde.LightRendering.cs, BlurRenderTarget
    // and BlurOntoWalls), so a Gaussian of sigma factor × √29:
    // light.blur_factor 0.001 × 14 for the light buffer;
    private static readonly float LightBlur = 0.014f * MathF.Sqrt(29);
    // × 5 more for the ambient light (Content.Client/Light/LightBlurOverlay.cs);
    private static readonly float AmbientBlur = 0.07f * MathF.Sqrt(29);
    // 7e-3 × 14 for the wall bleed, each of its six one-way passes times 1.1 (wall-bleed-blur.swsl).
    private static readonly float BleedBlur = 0.098f * MathF.Sqrt(29);
    private static readonly float BleedGain = MathF.Pow(1.1f, 6);

    // A station map without its own MapLight gets light.space_light_color made linear
    // (Content.Server/Light/EntitySystems/SpaceLightSystem.cs; Content.Shared/CCVar/CCVars.Lighting.cs);
    // BeforeLightTargetOverlay clears its target with it through RenderInRenderTarget, which takes a
    // clear colour for sRGB and makes it linear again (Robust.Client/Graphics/Clyde/Clyde.HLR.cs,
    // Clyde.Rendering.cs, ConvertClearFromSrgb). The engine's own default is black
    // (Robust.Shared/Map/Components/MapLightComponent.cs).
    private static readonly Rgba Starlight = Rgba.Parse("#8487db");

    private readonly float[] _rgb;
    private readonly int _width;
    private readonly int _height;

    private LightField(Box bounds, int samplesPerTile)
    {
        Bounds = bounds;
        SamplesPerTile = samplesPerTile;
        _width = (int)(bounds.MaxX - bounds.MinX) * samplesPerTile;
        _height = (int)(bounds.MaxY - bounds.MinY) * samplesPerTile;
        _rgb = new float[_width * _height * 3];
    }

    /// <summary>The tiles the field covers, whole tiles.</summary>
    public Box Bounds { get; }

    /// <summary>Samples along each side of a tile.</summary>
    public int SamplesPerTile { get; }

    /// <summary>
    /// Lights the room's tiles within <paramref name="bounds"/> (whole tiles, at most 64 a side). <paramref name="isSpace"/> says which squares
    /// are open to space, which a station leaves unroofed. Runs in parallel.
    /// </summary>
    public static LightField Compute(Box bounds, IReadOnlyList<LightSource> lights, Occluders occluders, Func<int, int, bool> isSpace,
        CancellationToken ct = default)
    {
        var tilesWide = bounds.MaxX - bounds.MinX;
        var tilesHigh = bounds.MaxY - bounds.MinY;
        if (bounds.IsEmpty || !float.IsFinite(tilesWide * tilesHigh) || tilesWide > 64 || tilesHigh > 64
            || bounds.MinX != MathF.Floor(bounds.MinX) || bounds.MinY != MathF.Floor(bounds.MinY) || tilesWide != MathF.Floor(tilesWide) || tilesHigh != MathF.Floor(tilesHigh))
            throw new ArgumentException($"The light covers whole tiles, at most 64 a side, not {bounds}.", nameof(bounds));

        var prepared = lights.Select(Prepare).OfType<Prepared>().ToArray();
        var field = new LightField(bounds, Resolution(prepared, tilesWide * tilesHigh));
        var options = new ParallelOptions { CancellationToken = ct };
        field.AddAmbient(isSpace);
        var edges = occluders.Edges;
        Parallel.ForEach(prepared.Where(l => l.CastShadows), options, light => light.Shadow = ShadowMap.Build(light, edges));
        field.Splat(prepared, options);
        field.Blur(LightBlur, 1, null);
        var inside = new bool[field._width * field._height];
        for (var j = 0; j < field._height; j++)
        {
            for (var i = 0; i < field._width; i++)
                inside[j * field._width + i] = occluders.Contains(field.Centre(i, j));
        }
        field.Blur(BleedBlur, BleedGain, inside);
        return field;
    }

    // The most samples a tile, up to the engine's, that keeps the lights' work within MaxWork.
    private static int Resolution(Prepared[] lights, float area)
    {
        var work = lights.Sum(l => Math.Min(Math.PI * l.Reach2, area));
        var samples = MaxSamplesPerTile;
        while (samples > 1 && (work * samples * samples > MaxWork || area * samples * samples > MaxWork))
            samples /= 2;
        return samples;
    }

    // Where sample (i, j) sits, in tiles: the centre of its square, row 0 at the south edge.
    private Vector2 Centre(int i, int j) => new(Bounds.MinX + (i + 0.5f) / SamplesPerTile, Bounds.MinY + (j + 0.5f) / SamplesPerTile);

    /// <summary>The light at a point in tiles, linear RGB, interpolated between samples; none outside <see cref="Bounds"/>.</summary>
    public (float R, float G, float B) LinearAt(double x, double y)
    {
        var fx = (x - Bounds.MinX) * SamplesPerTile - 0.5;
        var fy = (y - Bounds.MinY) * SamplesPerTile - 0.5;
        if (!(fx >= -0.5 && fy >= -0.5 && fx <= _width - 0.5 && fy <= _height - 0.5))
            return (0, 0, 0);
        var i0 = (int)Math.Floor(fx);
        var j0 = (int)Math.Floor(fy);
        var tx = (float)(fx - i0);
        var ty = (float)(fy - j0);
        var i1 = Math.Min(i0 + 1, _width - 1);
        var j1 = Math.Min(j0 + 1, _height - 1);
        i0 = Math.Max(i0, 0);
        j0 = Math.Max(j0, 0);
        int a = (j0 * _width + i0) * 3, b = (j0 * _width + i1) * 3, c = (j1 * _width + i0) * 3, d = (j1 * _width + i1) * 3;
        float wa = (1 - tx) * (1 - ty), wb = tx * (1 - ty), wc = (1 - tx) * ty, wd = tx * ty;
        return (_rgb[a] * wa + _rgb[b] * wb + _rgb[c] * wc + _rgb[d] * wd,
            _rgb[a + 1] * wa + _rgb[b + 1] * wb + _rgb[c + 1] * wc + _rgb[d + 1] * wd,
            _rgb[a + 2] * wa + _rgb[b + 2] * wb + _rgb[c + 2] * wc + _rgb[d + 2] * wd);
    }

    /// <summary>
    /// Lights a picture as the game's default shader lights a sprite: each pixel's colour made
    /// linear, times the light where it falls, clamped to 1 and made sRGB again
    /// (<c>Robust.Client/Graphics/Clyde/Shaders/base-default.frag</c>, <c>COLOR * MODULATE * LIGHT</c>
    /// on sRGB textures into an sRGB target). The picture is premultiplied RGBA; its top-left corner is
    /// at (<paramref name="left"/>, <paramref name="top"/>) in tiles, and each pixel is
    /// <paramref name="pixelWidth"/> by <paramref name="pixelHeight"/> tiles. What
    /// <paramref name="unshaded"/> (premultiplied RGBA of the same size) gives of a pixel, its unshaded
    /// layers drew, and keeps its colour (<c>unshaded</c> layers take LIGHT = 1); the rest is lit.
    /// </summary>
    public void LightPicture(SKBitmap picture, double left, double top, double pixelWidth, double pixelHeight, SKBitmap? unshaded = null)
    {
        if (picture.ColorType != SKColorType.Rgba8888 || picture.AlphaType != SKAlphaType.Premul)
            throw new ArgumentException("Only premultiplied RGBA pictures are lit.", nameof(picture));
        if (unshaded != null && (unshaded.ColorType != SKColorType.Rgba8888 || unshaded.AlphaType != SKAlphaType.Premul
            || unshaded.Width != picture.Width || unshaded.Height != picture.Height))
            throw new ArgumentException("The unshaded part is premultiplied RGBA the size of the picture.", nameof(unshaded));
        var width = picture.Width;
        var rowBytes = picture.RowBytes;
        var pixels = picture.GetPixelSpan().ToArray();
        var kept = unshaded?.GetPixelSpan().ToArray();
        var keptRowBytes = unshaded?.RowBytes ?? 0;
        Parallel.For(0, picture.Height, row =>
        {
            var y = top - (row + 0.5) * pixelHeight;
            for (var column = 0; column < width; column++)
            {
                var p = row * rowBytes + column * 4;
                int alpha = pixels[p + 3];
                if (alpha == 0)
                    continue;
                var (r, g, b) = LinearAt(left + (column + 0.5) * pixelWidth, y);
                if (kept == null)
                {
                    pixels[p] = Times(pixels[p], alpha, r);
                    pixels[p + 1] = Times(pixels[p + 1], alpha, g);
                    pixels[p + 2] = Times(pixels[p + 2], alpha, b);
                    continue;
                }
                // The shaded part is what is left once the unshaded part is taken out.
                var q = row * keptRowBytes + column * 4;
                for (var c = 0; c < 3; c++)
                {
                    var own = kept[q + c];
                    var shaded = (byte)Math.Max(0, pixels[p + c] - own);
                    pixels[p + c] = (byte)Math.Min(255, Times(shaded, alpha, c == 0 ? r : c == 1 ? g : b) + own);
                }
            }
        });
        pixels.CopyTo(picture.GetPixelSpan());
        picture.NotifyPixelsChanged();
    }

    // A premultiplied sRGB channel times a linear light, premultiplied again.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Times(byte premultiplied, int alpha, float light)
    {
        var straight = Math.Min(255, (premultiplied * 255 + alpha / 2) / alpha);
        var linear = Decode[straight] * light;
        var encoded = linear >= 1 ? 255 : linear > 0 ? Encode[(int)(linear * EncodeSteps + 0.5f)] : 0;
        return (byte)((encoded * alpha + 127) / 255);
    }

    /// <summary><c>Color.FromSrgb</c>: one sRGB channel, 0 to 1, to linear light.</summary>
    public static float FromSrgb(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary><c>Color.ToSrgb</c>: one linear channel, 0 to 1, to sRGB.</summary>
    public static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    private const int EncodeSteps = 4096;
    private static readonly float[] Decode = Enumerable.Range(0, 256).Select(v => FromSrgb(v / 255f)).ToArray();
    private static readonly byte[] Encode = Enumerable.Range(0, EncodeSteps + 1).Select(v => (byte)MathF.Round(ToSrgb(v / (float)EncodeSteps) * 255)).ToArray();

    /// <summary>
    /// The shader's falloff at <paramref name="distance"/> tiles from a light of <paramref name="radius"/>,
    /// before energy and mask: the light is a sphere lifted 1 tile off the floor (LIGHTING_HEIGHT), so it
    /// is below 1 even at its centre and reaches nothing at √(R² − 1) (<c>light_shared.swsl</c>).
    /// </summary>
    public static float Attenuation(float distance, float radius, float falloff = 6.8f, float curveFactor = 0f) =>
        Shape(Math.Clamp(MathF.Sqrt(distance * distance + 1) / radius, 0, 1), falloff, Math.Clamp(curveFactor, 0, 1));

    // clamp((1 − s²)² / (1 + falloff · mix(s, s², curve)), 0, 1); written so a NaN gives 0.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Shape(float s, float falloff, float curveFactor)
    {
        var s2 = s * s;
        var value = (1 - s2) * (1 - s2) / (1 + falloff * (s + (s2 - s) * curveFactor));
        return value >= 1 ? 1 : value > 0 ? value : 0;
    }

    // Squares open to space get the map's ambient light, blurred over the roof's edge
    // (Content.Client/Light/RoofOverlay.cs, LightBlurOverlay.cs); the lights are added after it.
    private void AddAmbient(Func<int, int, bool> isSpace)
    {
        var (r, g, b) = (FromSrgb(FromSrgb(Starlight.R)), FromSrgb(FromSrgb(Starlight.G)), FromSrgb(FromSrgb(Starlight.B)));
        var any = false;
        for (var j = 0; j < _height; j++)
        {
            for (var i = 0; i < _width; i++)
            {
                var centre = Centre(i, j);
                if (!isSpace((int)MathF.Floor(centre.X), (int)MathF.Floor(centre.Y)))
                    continue;
                var k = (j * _width + i) * 3;
                (_rgb[k], _rgb[k + 1], _rgb[k + 2]) = (r, g, b);
                any = true;
            }
        }
        if (any)
            Blur(AmbientBlur, 1, null);
    }

    /// <summary>
    /// A Gaussian blur of <paramref name="sigma"/> tiles, edges clamped, times <paramref name="gain"/>,
    /// written where <paramref name="only"/> says (everywhere when null).
    /// </summary>
    private void Blur(float sigma, float gain, bool[]? only)
    {
        var s = sigma * SamplesPerTile;
        var radius = Math.Max(1, (int)MathF.Ceiling(3 * s));
        var kernel = new float[2 * radius + 1];
        for (var k = -radius; k <= radius; k++)
            kernel[k + radius] = MathF.Exp(-k * k / (2 * s * s));
        var sum = kernel.Sum();
        for (var k = 0; k < kernel.Length; k++)
            kernel[k] /= sum;

        var across = new float[_rgb.Length];
        Pass(_rgb, across, kernel, radius, 1, 0, _width, _height);
        var both = new float[_rgb.Length];
        Pass(across, both, kernel, radius, 0, 1, _height, _width);
        for (var n = 0; n < _width * _height; n++)
        {
            if (only != null && !only[n])
                continue;
            for (var c = 0; c < 3; c++)
                _rgb[n * 3 + c] = both[n * 3 + c] * gain;
        }
    }

    // One direction of a separable blur: along x when (dx, dy) is (1, 0), along y when (0, 1).
    private void Pass(float[] from, float[] to, float[] kernel, int radius, int dx, int dy, int length, int lines)
    {
        var width = _width;
        Parallel.For(0, lines, line =>
        {
            for (var at = 0; at < length; at++)
            {
                float r = 0, g = 0, b = 0;
                for (var k = -radius; k <= radius; k++)
                {
                    var along = Math.Clamp(at + k, 0, length - 1);
                    var n = (dx == 1 ? line * width + along : along * width + line) * 3;
                    var w = kernel[k + radius];
                    r += from[n] * w;
                    g += from[n + 1] * w;
                    b += from[n + 2] * w;
                }
                var m = (dx == 1 ? line * width + at : at * width + line) * 3;
                (to[m], to[m + 1], to[m + 2]) = (r, g, b);
            }
        });
    }

    /// <summary>One light, ready to add: its linear colour times energy, its reach, cones and shadow map.</summary>
    private sealed class Prepared
    {
        public required LightSource Source;
        public required float X, Y, Reach2, InvRadius2, Falloff, Curve, R, G, B;
        public bool CastShadows => Source.CastShadows;
        public ShadowMap? Shadow;
    }

    private static Prepared? Prepare(LightSource light)
    {
        // A light of radius 1 or less never reaches the floor; one with a value that is not a number lights nothing.
        var reach2 = light.Radius * light.Radius - 1;
        if (light.Radius <= 1 || light.Energy == 0 || !float.IsFinite(reach2) || !float.IsFinite(light.Energy)
            || !float.IsFinite(light.Falloff) || !float.IsFinite(light.CurveFactor)
            || !float.IsFinite(light.Color.R + light.Color.G + light.Color.B))
            return null;
        return new Prepared
        {
            Source = light,
            X = light.Position.X,
            Y = light.Position.Y,
            Reach2 = reach2,
            InvRadius2 = 1 / (light.Radius * light.Radius),
            Falloff = light.Falloff,
            Curve = Math.Clamp(light.CurveFactor, 0, 1),
            // Clyde.ShaderProgram.cs makes a light's colour linear when it sets the uniform.
            R = FromSrgb(light.Color.R) * light.Energy,
            G = FromSrgb(light.Color.G) * light.Energy,
            B = FromSrgb(light.Color.B) * light.Energy,
        };
    }

    // Adds every light, a band of rows at a time, so bands run in parallel without sharing a sample.
    private void Splat(Prepared[] lights, ParallelOptions options)
    {
        var s = SamplesPerTile;
        var bands = (_height + s - 1) / s;
        Parallel.For(0, bands, options, band =>
        {
            var top = band * s;
            var bottom = Math.Min(top + s, _height) - 1;
            foreach (var light in lights)
            {
                var reach = MathF.Sqrt(light.Reach2);
                // Clamped before the casts, so a light of any reach stays within the rows.
                var first = (int)Math.Clamp(MathF.Floor((light.Y - reach - Bounds.MinY) * s - 0.5f), top, bottom + 1);
                var last = (int)Math.Clamp(MathF.Ceiling((light.Y + reach - Bounds.MinY) * s - 0.5f), top - 1, bottom);
                for (var j = first; j <= last; j++)
                    SplatRow(light, j);
            }
        });
    }

    private void SplatRow(Prepared light, int j)
    {
        var s = SamplesPerTile;
        var dy = Bounds.MinY + (j + 0.5f) / s - light.Y;
        var dy2 = dy * dy;
        if (dy2 >= light.Reach2)
            return;
        var half = MathF.Sqrt(light.Reach2 - dy2);
        var first = (int)Math.Clamp(MathF.Ceiling((light.X - half - Bounds.MinX) * s - 0.5f), 0, _width);
        var last = (int)Math.Clamp(MathF.Floor((light.X + half - Bounds.MinX) * s - 0.5f), -1, _width - 1);
        var cones = light.Source.Cones;
        for (var i = first; i <= last; i++)
        {
            var dx = Bounds.MinX + (i + 0.5f) / s - light.X;
            var d2 = dx * dx + dy2;
            if (d2 >= light.Reach2 || (light.Shadow != null && !light.Shadow.Lit(dx, dy)))
                continue;
            // s < 1 inside the reach, so only the result's clamp matters.
            var value = Shape(MathF.Sqrt((d2 + 1) * light.InvRadius2), light.Falloff, light.Curve);
            if (cones.Count > 0)
                value *= LightSources.ConeFactor(cones, light.Source.MaskRotation, dx, dy);
            var k = (j * _width + i) * 3;
            _rgb[k] += light.R * value;
            _rgb[k + 1] += light.G * value;
            _rgb[k + 2] += light.B * value;
        }
    }

    /// <summary>
    /// A shadow-casting light's view of the occluders: for each of <see cref="Bins"/> directions, the
    /// line of the nearest outline edge facing it, kept as a unit normal (pointing away from the light)
    /// and the line's distance from the light. Only edges facing the light cast shadows, so an
    /// occluder's own body is in its shadow, as in the engine (its depth pass culls back faces).
    /// </summary>
    private sealed class ShadowMap
    {
        private const float BinsPerRadian = Bins / (2 * MathF.PI);

        private readonly float[] _nx = new float[Bins];
        private readonly float[] _ny = new float[Bins];
        private readonly float[] _distance = new float[Bins];

        public static ShadowMap Build(Prepared light, Edge[] edges)
        {
            var map = new ShadowMap();
            Array.Fill(map._distance, float.PositiveInfinity);
            var nearest = new float[Bins];
            Array.Fill(nearest, float.PositiveInfinity);
            var reach = MathF.Sqrt(light.Reach2);
            var centre = new Vector2(light.X, light.Y);

            foreach (var edge in edges)
            {
                var a = edge.A - centre;
                var b = edge.B - centre;
                var d = b - a;
                // The outline runs counter-clockwise; this normal points into the occluder.
                var normal = new Vector2(-d.Y, d.X);
                var length = normal.Length();
                if (length < 1e-6f)
                    continue;
                normal /= length;
                var distance = Vector2.Dot(normal, a);
                // Only edges facing the light cast shadows.
                if (distance <= 1e-6f)
                    continue;
                var closest = DistanceToSegment(a, b);
                if (closest >= reach)
                    continue;
                var farthest = MathF.Max(a.Length(), b.Length());

                // Seen from the light a facing edge runs clockwise: from b's angle up to a's.
                var start = MathF.Atan2(b.Y, b.X) - EdgeWidening;
                var span = MathF.Atan2(a.Y, a.X) + EdgeWidening - start;
                if (span < 0)
                    span += 2 * MathF.PI;
                var firstBin = (int)MathF.Ceiling((start + MathF.PI) * BinsPerRadian - 0.5f);
                var lastBin = (int)MathF.Floor((start + span + MathF.PI) * BinsPerRadian - 0.5f);
                for (var k = firstBin; k <= lastBin; k++)
                {
                    var bin = ((k % Bins) + Bins) % Bins;
                    var angle = (bin + 0.5f) / BinsPerRadian - MathF.PI;
                    var (sin, cos) = MathF.SinCos(angle);
                    var along = normal.X * cos + normal.Y * sin;
                    var hit = along > 1e-6f ? Math.Clamp(distance / along, closest, farthest) : farthest;
                    if (hit >= nearest[bin])
                        continue;
                    nearest[bin] = hit;
                    map._nx[bin] = normal.X;
                    map._ny[bin] = normal.Y;
                    map._distance[bin] = distance;
                }
            }
            return map;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Lit(float dx, float dy)
        {
            var bin = (int)((MathF.Atan2(dy, dx) + MathF.PI) * BinsPerRadian);
            if (bin >= Bins)
                bin = Bins - 1;
            return _nx[bin] * dx + _ny[bin] * dy <= _distance[bin];
        }

        private static float DistanceToSegment(Vector2 a, Vector2 b)
        {
            var d = b - a;
            var t = Math.Clamp(-Vector2.Dot(a, d) / d.LengthSquared(), 0, 1);
            return (a + d * t).Length();
        }
    }
}
