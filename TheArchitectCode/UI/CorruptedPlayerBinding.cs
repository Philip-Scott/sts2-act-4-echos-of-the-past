using Godot;

namespace TheArchitect.TheArchitectCode.UI;

public partial class CorruptedPlayerBinding : Node2D
{
    private Node2D? _geometry;
    private CorruptedBindingStroke _orbit = null!;
    private CorruptedBindingStroke _vertical = null!;
    private CorruptedBindingStroke? _floor;
    private readonly CorruptedBindingStroke[] _seals = new CorruptedBindingStroke[6];
    private Rect2 _bounds;
    private float _tickTime;
    private float _strength;
    internal int GeometryBuildCount { get; private set; }

    public bool Front { get; init; }

    public void UpdateEffect(Rect2 bounds, float time, float strength)
    {
        if (_geometry == null)
        {
            // Godot retains these strokes; animation only changes their transforms and tint.
            _geometry = new Node2D { Modulate = new Color(1, 1, 1, strength) };
            AddChild(_geometry);
            _orbit = AddStroke();
            _vertical = AddStroke();
            if (!Front)
                _floor = AddStroke();
            for (var i = 0; i < _seals.Length; i++)
                _seals[i] = AddStroke();
            RebuildGeometry(bounds);
        }
        else if (!_bounds.IsEqualApprox(bounds))
            RebuildGeometry(bounds);
        if (_strength != strength)
            _geometry.Modulate = new Color(1, 1, 1, strength);
        _strength = strength;
        var size = _bounds.Size;
        var center = _bounds.Position + size * new Vector2(0.5f, 0.55f);
        var radius = size * new Vector2(0.75f, 0.24f);
        float tilt = 0.35f * Mathf.Sin(time * 0.35f);
        _orbit.Rotation = tilt;
        for (var i = 0; i < _seals.Length; i++)
        {
            float angle = time * 0.45f + Mathf.Tau * i / _seals.Length;
            _seals[i].Visible = (Mathf.Sin(angle) >= 0) == Front;
            _seals[i].Position = center + EllipsePoint(angle, radius, tilt);
        }
        if (!Front)
        {
            _tickTime = time;
            QueueRedraw();
        }
    }

    private CorruptedBindingStroke AddStroke()
    {
        var stroke = new CorruptedBindingStroke();
        _geometry!.AddChild(stroke);
        return stroke;
    }

    private void RebuildGeometry(Rect2 bounds)
    {
        _bounds = bounds;
        GeometryBuildCount++;
        var size = _bounds.Size;
        var center = _bounds.Position + size * new Vector2(0.5f, 0.55f);
        var ink = new Color(0.84f, 0.57f, 0.68f, 0.72f);
        var gold = new Color(1f, 0.72f, 0.35f, 0.9f);
        float width = Mathf.Clamp(size.Y * 0.006f, 1.1f, 2.4f);
        _orbit.Position = center;
        _orbit.Configure(Arc(size * new Vector2(0.75f, 0.24f), Front ? 0 : Mathf.Pi, Mathf.Pi, 65), ink, width);
        _vertical.Position = _bounds.GetCenter();
        _vertical.Configure(Arc(size * new Vector2(0.53f, 0.52f),
            Front ? Mathf.Pi / 2 : -Mathf.Pi / 2, Mathf.Pi, 65),
            new Color(ink.R, ink.G, ink.B, ink.A * 0.65f), width);
        if (_floor != null)
        {
            _floor.Position = new Vector2(center.X, _bounds.End.Y - 2f);
            _floor.Configure(Arc(size * new Vector2(0.75f, 0.045f), 0, Mathf.Tau, 97), ink, width);
            _geometry!.MoveChild(_floor, 0);
        }
        float sealSize = Mathf.Clamp(size.Y * 0.022f, 3f, 7f);
        foreach (var seal in _seals)
            seal.Configure([new(0, -sealSize), new(sealSize, 0), new(0, sealSize),
                new(-sealSize, 0), new(0, -sealSize)], gold, width * 1.25f, width * 0.7f);
    }

    private static Vector2[] Arc(Vector2 radius, float start, float sweep, int count)
    {
        var points = new Vector2[count];
        for (var i = 0; i < count; i++)
            points[i] = EllipsePoint(start + sweep * i / (count - 1), radius, 0);
        return points;
    }

    public override void _Draw()
    {
        if (Front || _strength <= 0)
            return;
        var floor = new Vector2(_bounds.GetCenter().X, _bounds.End.Y - 2f);
        var radius = _bounds.Size * new Vector2(0.75f, 0.045f);
        var ink = new Color(0.84f, 0.57f, 0.68f, _strength * 0.72f);
        float width = Mathf.Clamp(_bounds.Size.Y * 0.006f, 1.1f, 2.4f);
        for (var i = 0; i < 12; i++)
        {
            float angle = Mathf.Tau * i / 12f + _tickTime * 0.2f;
            DrawLine(floor + EllipsePoint(angle, radius * 0.78f, 0),
                floor + EllipsePoint(angle, radius * 0.90f, 0), ink, width, true);
        }
    }

    private static Vector2 EllipsePoint(float angle, Vector2 radius, float rotation) =>
        new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y).Rotated(rotation);
}

internal partial class CorruptedBindingStroke : Node2D
{
    private Vector2[] _points = [];
    private Color _color;
    private float _width;
    private float _dotRadius;

    internal void Configure(Vector2[] points, Color color, float width, float dotRadius = 0)
    {
        _points = points;
        _color = color;
        _width = width;
        _dotRadius = dotRadius;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawPolyline(_points, new Color(_color.R, _color.G, _color.B, _color.A * 0.12f), _width * 4f, true);
        DrawPolyline(_points, _color, _width, true);
        if (_dotRadius > 0)
            DrawCircle(Vector2.Zero, _dotRadius, _color);
    }
}
