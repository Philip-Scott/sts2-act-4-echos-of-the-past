using Godot;

namespace TheArchitect.TheArchitectCode.UI;

// Verdigris Armillary: patinated bronze bands with lilac afterimages, orbiting echo-glass
// shards and a plum ground pool. The back layer draws the ground and far band halves
// behind the body; the front layer draws the near halves over it.
public partial class CorruptedPlayerBinding : Node2D
{
    private const int RingSamples = 72;
    private const int GroundSamples = 96;
    private static readonly Color Plum = Color.FromHtml("#2b2134");
    private static readonly Color Wash = Color.FromHtml("#5d4a7c");
    private static readonly Color Lilac = Color.FromHtml("#c3b2de");
    private static readonly Color Ivory = Color.FromHtml("#f1e3bd");
    private static readonly Color DashGold = Color.FromHtml("#c9a45c");
    private static readonly Color RuneGold = Color.FromHtml("#d8b56a");
    private static readonly Color PoolCenter = new(43 / 255f, 33 / 255f, 52 / 255f, 0.55f);
    private static readonly Color PoolMiddle = new(60 / 255f, 44 / 255f, 78 / 255f, 0.35f);
    private static readonly (float Offset, Color Color)[] Bronze =
    [
        (0f, Color.FromHtml("#3f5d55")), (0.3f, Color.FromHtml("#7f9a7f")), (0.52f, Color.FromHtml("#d0aa5f")),
        (0.78f, Color.FromHtml("#8a6a3a")), (1f, Color.FromHtml("#46584f"))
    ];
    private static readonly (float Offset, Color Color)[] Glass =
    [
        (0f, Color.FromHtml("#e6dcf0")), (0.45f, Color.FromHtml("#a796c4")), (1f, Color.FromHtml("#4f4270"))
    ];
    // Hand-cut glass outlines; each orbiting shard has its own silhouette.
    private static readonly Vector2[][] Shards =
    [
        [new(0, -1.45f), new(0.62f, -0.25f), new(0.3f, 1.05f), new(-0.55f, 0.4f)],
        [new(-0.2f, -1.3f), new(0.7f, -0.55f), new(0.45f, 0.95f), new(-0.6f, 0.7f), new(-0.5f, -0.3f)],
        [new(0.1f, -1.5f), new(0.55f, 0.2f), new(-0.15f, 1.1f), new(-0.5f, -0.15f)],
        [new(-0.35f, -1.2f), new(0.5f, -1.0f), new(0.6f, 0.55f), new(-0.1f, 1.15f), new(-0.6f, 0.1f)],
        [new(0, -1.35f), new(0.68f, 0.1f), new(0.05f, 1.2f), new(-0.62f, -0.05f)]
    ];

    private Rect2 _bounds;
    private bool _built;
    private float _time;
    private float _strength;
    private float _width;
    private Vector2 _floor;
    private Vector2 _floorRadius;
    private Vector2[] _poolPoints = [];
    private int[] _poolIndices = [];
    private Color[] _poolColors = [];
    private readonly List<Vector2> _scratch = [];
    internal int GeometryBuildCount { get; private set; }

    public bool Front { get; init; }

    public void UpdateEffect(Rect2 bounds, float time, float strength)
    {
        if (!_built || !_bounds.IsEqualApprox(bounds))
            RebuildGeometry(bounds);
        _time = time;
        _strength = strength;
        // The armillary turns continuously, so each frame submits fresh drawing commands;
        // only bounds-derived geometry is cached.
        QueueRedraw();
    }

    private void RebuildGeometry(Rect2 bounds)
    {
        _bounds = bounds;
        _built = true;
        GeometryBuildCount++;
        var size = bounds.Size;
        _width = Mathf.Clamp(size.Y * 0.0075f, 1.4f, 3f);
        _floor = new Vector2(bounds.Position.X + size.X * 0.5f, bounds.End.Y - 2f);
        _floorRadius = new Vector2(size.X * 0.78f, Mathf.Clamp(size.Y * 0.05f, 8f, 15f));
        if (Front)
            return;
        // Radial plum pool: centre, 70% and rim rings with the prototype's gradient stops.
        var pool = _floorRadius * new Vector2(1.06f, 1.2f);
        _poolPoints = new Vector2[1 + GroundSamples * 2];
        _poolColors = new Color[_poolPoints.Length];
        _poolPoints[0] = _floor;
        for (var i = 0; i < GroundSamples; i++)
        {
            float angle = Mathf.Tau * i / GroundSamples;
            var unit = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * pool;
            _poolPoints[1 + i] = _floor + unit * 0.7f;
            _poolPoints[1 + GroundSamples + i] = _floor + unit;
        }
        var indices = new List<int>(GroundSamples * 9);
        for (var i = 0; i < GroundSamples; i++)
        {
            int next = (i + 1) % GroundSamples;
            int inner = 1 + i, innerNext = 1 + next, outer = 1 + GroundSamples + i, outerNext = 1 + GroundSamples + next;
            indices.AddRange([0, inner, innerNext, inner, outer, outerNext, inner, outerNext, innerNext]);
        }
        _poolIndices = indices.ToArray();
    }

    public override void _Draw()
    {
        if (!_built || !(_strength > 0))
            return;
        float opacity = Mathf.Min(1f, _strength);
        float t = _time;
        var size = _bounds.Size;
        float cx = _bounds.Position.X + size.X * 0.5f, top = _bounds.Position.Y, w = _width;

        float tilt = 0.3f * Mathf.Sin(t * 0.35f), rx = size.X * 0.74f, ry = size.Y * 0.2f;
        var equator = new Ring(new Vector2(cx, top + size.Y * 0.56f),
            new Vector3(rx * Mathf.Cos(tilt), rx * Mathf.Sin(tilt), 0), new Vector3(-ry * Mathf.Sin(tilt), ry * Mathf.Cos(tilt), 1));
        float phi = 0.5f * Mathf.Sin(t * 0.27f + 1f), radius = size.X * 0.56f, height = size.Y * 0.49f;
        // Leaning back keeps the meridian's top arc behind the head and its foot arc in front.
        var meridian = new Ring(new Vector2(cx, top + size.Y * 0.5f),
            new Vector3(radius * Mathf.Cos(phi), 0, Mathf.Sin(phi)), new Vector3(0, -height, -0.35f));

        if (!Front)
            DrawGround(opacity, w, t);
        // The meridian's echo grows less so its foot arc stays clear of the health bar.
        DrawBandEchoes(opacity, meridian, w * 0.8f, t + 1.7f, 4.6f, 0.06f, Front ? 0.45f : 0.3f);
        DrawBand(opacity, meridian, w * 0.85f, Front ? 0.8f : 0.45f);
        DrawBandEchoes(opacity, equator, w * 1.6f, t, 3.4f, 0.13f, Front ? 0.6f : 0.35f);
        DrawBand(opacity, equator, w * 1.6f, Front ? 0.95f : 0.55f);
        DrawOrbitingShards(opacity, equator, t, Mathf.Clamp(size.Y * 0.03f, 5f, 11f), w);
    }

    // A patinated bronze ribbon: plum underpaint, metal body, ivory catch-light and engraved ticks.
    private void DrawBand(float opacity, Ring ring, float w, float strength)
    {
        float alpha = opacity * strength;
        var extent = new Vector2(Mathf.Abs(ring.U.X) + Mathf.Abs(ring.V.X), Mathf.Abs(ring.U.Y) + Mathf.Abs(ring.V.Y));
        var from = ring.Center - extent;
        var axis = extent * 2f;
        foreach (var run in Runs(ring, Front, 1f))
        {
            DrawPolyline(run, WithAlpha(Plum, alpha * 0.5f), w * 1.9f, true);
            DrawPolylineColors(run, MetalColors(run, from, axis, alpha), w, true);
            DrawPolyline(Offset(run, new Vector2(0, -w * 0.28f)), WithAlpha(Ivory, alpha * (Front ? 0.6f : 0.3f)),
                Mathf.Max(0.6f, w * 0.26f), true);
        }
        _scratch.Clear();
        for (var k = 0; k < 28; k++)
        {
            float angle = k / 28f * Mathf.Tau;
            var (point, depth) = ring.Point(angle);
            if (depth >= 0 != Front)
                continue;
            var tangent = ring.Tangent(angle);
            var normal = new Vector2(tangent.Y, -tangent.X) * (w * (k % 7 == 0 ? 0.75f : 0.45f));
            _scratch.Add(point + normal);
            _scratch.Add(point - normal);
        }
        if (_scratch.Count > 0)
            DrawMultiline(_scratch.ToArray(), WithAlpha(Plum, alpha * 0.7f), Mathf.Max(0.7f, w * 0.2f), true);
    }

    // Lilac afterimages peel off a band and drift outward, like a struck bell's echo.
    private void DrawBandEchoes(float opacity, Ring ring, float w, float t, float period, float growth, float strength)
    {
        for (var k = 0; k < 2; k++)
        {
            float phase = Frac(t / period + k / 2f), scale = 1f + growth * phase;
            float alpha = strength * (1f - phase) * Mathf.Min(1f, phase * 5f);
            if (alpha <= 0)
                continue;
            foreach (var run in Runs(ring, Front, scale))
            {
                DrawPolyline(run, WithAlpha(Wash, opacity * alpha * 0.22f), w * 2.4f, true);
                DrawPolyline(run, WithAlpha(Lilac, opacity * alpha * 0.8f), Mathf.Max(0.6f, w * 0.32f), true);
            }
        }
    }

    private void DrawOrbitingShards(float opacity, Ring ring, float t, float size, float w)
    {
        for (var i = 0; i < Shards.Length; i++)
        {
            float angle = t * 0.4f + i * Mathf.Tau / Shards.Length;
            var (point, depth) = ring.Point(angle);
            if (depth >= 0 != Front)
                continue;
            float scale = size * (0.85f + 0.2f * Mathf.Clamp(depth, -1f, 1f)), alpha = Front ? 1f : 0.55f;
            for (var echo = 2; echo >= 1; echo--)
            {
                float trail = angle - echo * 0.13f;
                var (ghost, ghostDepth) = ring.Point(trail);
                if (ghostDepth >= 0 == Front)
                    DrawShard(ghost, scale * (1 + echo * 0.08f), trail * 0.7f + i, Shards[i],
                        opacity * alpha * (0.36f - echo * 0.12f), true, w);
            }
            DrawShard(point, scale, angle * 0.7f + i, Shards[i], opacity * alpha, false, w);
        }
    }

    private void DrawShard(Vector2 position, float size, float rotation, Vector2[] shape, float alpha, bool ghost, float w)
    {
        DrawSetTransform(position, rotation);
        var points = new Vector2[shape.Length + 1];
        for (var i = 0; i < shape.Length; i++)
            points[i] = shape[i] * size;
        points[^1] = points[0];
        if (ghost)
            DrawPolyline(points, WithAlpha(Lilac, alpha), Mathf.Max(0.6f, w * 0.4f), true);
        else
        {
            var fill = points[..^1];
            var colors = new Color[fill.Length];
            for (var i = 0; i < fill.Length; i++)
                colors[i] = WithAlpha(Sample(Glass, (fill[i].X + fill[i].Y + 2 * size) / (4 * size)), alpha);
            DrawPolygon(fill, colors);
            DrawPolyline(points, WithAlpha(Plum, alpha), Mathf.Max(0.8f, w * 0.5f), true);
            DrawLine(points[0], shape[2] * size * 0.4f, WithAlpha(Ivory, alpha * 0.8f), Mathf.Max(0.5f, w * 0.3f), true);
        }
        DrawSetTransformMatrix(Transform2D.Identity);
    }

    // A plum pool with ripples spreading from the feet, a broken bronze rim and engraved runes.
    private void DrawGround(float opacity, float w, float t)
    {
        for (var i = 0; i < _poolColors.Length; i++)
        {
            var color = i == 0 ? PoolCenter : i <= GroundSamples ? PoolMiddle : PoolMiddle with { A = 0 };
            _poolColors[i] = WithAlpha(color, color.A * opacity);
        }
        RenderingServer.CanvasItemAddTriangleArray(GetCanvasItem(), _poolIndices, _poolPoints, _poolColors);

        var center = _floor;
        var radius = _floorRadius;
        for (var i = 0; i < 3; i++)
        {
            float phase = Frac(t / 4.2f + i / 3f), scale = 0.3f + 0.72f * phase;
            float alpha = (1f - phase) * Mathf.Min(1f, phase * 4f);
            var ripple = Ellipse(center, radius * scale, 0, Mathf.Tau, GroundSamples);
            DrawPolyline(ripple, WithAlpha(Wash, opacity * alpha * 0.25f), w * 2.2f, true);
            DrawPolyline(ripple, WithAlpha(Lilac, opacity * alpha * 0.75f), Mathf.Max(0.6f, w * 0.35f), true);
        }

        DrawMultiline(DashedEllipse(center, radius * 0.7f, w * 1.6f, w * 2.8f, -t * w * 1.5f),
            WithAlpha(DashGold, opacity * 0.7f), Mathf.Max(0.6f, w * 0.3f), true);

        float spin = t * 0.07f;
        var from = center - radius;
        var axis = radius * 2f;
        for (var i = 0; i < 6; i++)
        {
            float start = spin + i * Mathf.Tau / 6 + 0.09f, end = start + Mathf.Tau / 6 - 0.18f;
            var arc = Ellipse(center, radius, start, end, 18);
            DrawPolyline(arc, WithAlpha(Plum, opacity * 0.5f), w * 2.2f, true);
            var metal = MetalColors(arc, from, axis, opacity);
            DrawPolylineColors(arc, metal, w * 1.15f, true);
            // Round caps make the rim read as separate cast segments.
            DrawCircle(arc[0], w * 0.575f, metal[0], true, -1, true);
            DrawCircle(arc[^1], w * 0.575f, metal[^1], true, -1, true);
            DrawPolyline(Ellipse(center + new Vector2(0, -w * 0.3f), radius, start + 0.1f, end - 0.1f, 14),
                WithAlpha(Ivory, opacity * 0.45f), Mathf.Max(0.5f, w * 0.25f), true);
        }

        _scratch.Clear();
        for (var i = 0; i < 12; i++)
        {
            float angle = -spin * 1.6f + i * Mathf.Tau / 12;
            var mark = center + new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y) * 0.85f;
            float h = radius.Y * (i % 3 == 0 ? 0.32f : 0.2f), arm = w * (i % 2 == 1 ? 0.9f : 1.4f);
            var tip = mark + new Vector2(0, -h * 0.5f);
            var notch = mark + new Vector2(0, -h * 0.1f);
            _scratch.AddRange([mark + new Vector2(0, h * 0.5f), tip,
                mark + new Vector2(-arm, -h * 0.5f), notch, notch, mark + new Vector2(arm, -h * 0.5f)]);
        }
        DrawMultiline(_scratch.ToArray(), WithAlpha(RuneGold, opacity * 0.85f), Mathf.Max(0.6f, w * 0.38f), true);

        // Four splinters of echo glass planted in the rim, behind the feet.
        for (var i = 0; i < 4; i++)
        {
            float angle = spin + i * Mathf.Tau / 4 + Mathf.Tau / 12;
            var inset = center + new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y - w * 1.6f);
            DrawShard(inset, w * 1.6f, i % 2 == 1 ? 0.25f : -0.2f, Shards[i], opacity * 0.9f, false, w * 0.8f);
        }
    }

    // Contiguous runs of the ring on one side of the body; each run overlaps the crossover by one sample.
    internal static List<Vector2[]> Runs(Ring ring, bool front, float scale, int samples = RingSamples)
    {
        var runs = new List<List<Vector2>>();
        List<Vector2>? current = null;
        Vector2? previous = null;
        bool startsAtZero = false;
        for (var i = 0; i <= samples; i++)
        {
            var (point, depth) = ring.Point(i / (float)samples * Mathf.Tau, scale);
            if (depth >= 0 == front)
            {
                if (current == null)
                {
                    current = previous is { } prior ? [prior] : [];
                    runs.Add(current);
                    startsAtZero |= i == 0;
                }
                current.Add(point);
            }
            else if (current != null)
            {
                current.Add(point);
                current = null;
            }
            previous = point;
        }
        if (runs.Count > 1 && startsAtZero && current != null)
        {
            current.AddRange(runs[0].Skip(1));
            runs.RemoveAt(0);
        }
        return runs.Where(run => run.Count > 1).Select(run => run.ToArray()).ToList();
    }

    private static Vector2[] Ellipse(Vector2 center, Vector2 radius, float start, float end, int samples)
    {
        var points = new Vector2[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            float angle = start + (end - start) * i / samples;
            points[i] = center + new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y);
        }
        return points;
    }

    // Canvas-style dashes along an ellipse: a point at arc length s is inked when (s + offset) mod period < dash.
    internal static Vector2[] DashedEllipse(Vector2 center, Vector2 radius, float dash, float gap, float offset,
        int samples = 160)
    {
        var segments = new List<Vector2>();
        float period = dash + gap, travelled = 0;
        var previous = center + new Vector2(radius.X, 0);
        for (var i = 1; i <= samples; i++)
        {
            float angle = Mathf.Tau * i / samples;
            var point = center + new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y);
            float length = previous.DistanceTo(point), position = 0;
            while (length > 0 && position < length)
            {
                float phase = Mathf.PosMod(travelled + position + offset, period);
                float step = phase < dash ? dash - phase : period - phase;
                float next = Mathf.Min(length, position + Mathf.Max(step, 1e-4f));
                if (phase < dash)
                {
                    segments.Add(previous.Lerp(point, position / length));
                    segments.Add(previous.Lerp(point, next / length));
                }
                position = next;
            }
            travelled += length;
            previous = point;
        }
        return segments.ToArray();
    }

    private static Color[] MetalColors(Vector2[] points, Vector2 from, Vector2 axis, float alpha)
    {
        var colors = new Color[points.Length];
        float lengthSquared = axis.LengthSquared();
        for (var i = 0; i < points.Length; i++)
            colors[i] = WithAlpha(Sample(Bronze, (points[i] - from).Dot(axis) / lengthSquared), alpha);
        return colors;
    }

    private static Color Sample((float Offset, Color Color)[] stops, float position)
    {
        position = Mathf.Clamp(position, 0f, 1f);
        for (var i = 1; i < stops.Length; i++)
            if (position <= stops[i].Offset)
                return stops[i - 1].Color.Lerp(stops[i].Color,
                    (position - stops[i - 1].Offset) / (stops[i].Offset - stops[i - 1].Offset));
        return stops[^1].Color;
    }

    private static Vector2[] Offset(Vector2[] points, Vector2 offset)
    {
        var moved = new Vector2[points.Length];
        for (var i = 0; i < points.Length; i++)
            moved[i] = points[i] + offset;
        return moved;
    }

    private static Color WithAlpha(Color color, float alpha) => color with { A = alpha };

    private static float Frac(float value) => value - Mathf.Floor(value);

    // A 3D circle projected orthographically: U and V carry (screen X, screen Y, depth).
    internal readonly record struct Ring(Vector2 Center, Vector3 U, Vector3 V)
    {
        internal (Vector2 Point, float Depth) Point(float angle, float scale = 1f)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            return (Center + scale * new Vector2(U.X * cos + V.X * sin, U.Y * cos + V.Y * sin), U.Z * cos + V.Z * sin);
        }

        internal Vector2 Tangent(float angle)
        {
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            var tangent = new Vector2(-U.X * sin + V.X * cos, -U.Y * sin + V.Y * cos);
            return tangent.LengthSquared() > 0 ? tangent.Normalized() : Vector2.Right;
        }
    }
}
