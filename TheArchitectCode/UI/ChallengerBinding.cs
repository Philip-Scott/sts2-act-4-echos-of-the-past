using Godot;

namespace TheArchitect.TheArchitectCode.UI;

public partial class ChallengerBinding : Node2D
{
    private readonly Vector2[] _arc = new Vector2[65];
    private readonly Vector2[] _sigil = new Vector2[97];
    private readonly Vector2[] _seal = new Vector2[5];
    private Rect2 _bounds;
    private float _time;
    private float _strength;

    public bool Front { get; init; }

    public void UpdateEffect(Rect2 bounds, float time, float strength)
    {
        _bounds = bounds;
        _time = time;
        _strength = strength;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_strength <= 0)
            return;
        var size = _bounds.Size;
        var center = _bounds.Position + size * new Vector2(0.5f, 0.55f);
        var orbit = size * new Vector2(0.75f, 0.24f);
        float tilt = 0.35f * Mathf.Sin(_time * 0.35f);
        var ink = new Color(0.84f, 0.57f, 0.68f, _strength * 0.72f);
        var gold = new Color(1f, 0.72f, 0.35f, _strength * 0.9f);
        float width = Mathf.Clamp(size.Y * 0.006f, 1.1f, 2.4f);

        if (!Front)
        {
            var floor = new Vector2(center.X, _bounds.End.Y - 2f);
            var radius = size * new Vector2(0.75f, 0.045f);
            for (int i = 0; i < _sigil.Length; i++)
                _sigil[i] = floor + EllipsePoint(Mathf.Tau * i / (_sigil.Length - 1), radius, 0);
            Stroke(_sigil, ink, width);
            for (int i = 0; i < 12; i++)
            {
                float angle = Mathf.Tau * i / 12f + _time * 0.2f;
                DrawLine(floor + EllipsePoint(angle, radius * 0.78f, 0),
                    floor + EllipsePoint(angle, radius * 0.90f, 0), ink, width, true);
            }
        }

        DrawHalf(center, orbit, tilt, Front ? 0 : Mathf.Pi, ink, width);
        DrawHalf(_bounds.GetCenter(), size * new Vector2(0.53f, 0.52f), 0,
            Front ? Mathf.Pi / 2 : -Mathf.Pi / 2, new Color(ink.R, ink.G, ink.B, ink.A * 0.65f), width);

        float sealSize = Mathf.Clamp(size.Y * 0.022f, 3f, 7f);
        for (int i = 0; i < 6; i++)
        {
            float angle = _time * 0.45f + Mathf.Tau * i / 6f;
            if ((Mathf.Sin(angle) >= 0) != Front)
                continue;
            var position = center + EllipsePoint(angle, orbit, tilt);
            _seal[0] = position + new Vector2(0, -sealSize);
            _seal[1] = position + new Vector2(sealSize, 0);
            _seal[2] = position + new Vector2(0, sealSize);
            _seal[3] = position + new Vector2(-sealSize, 0);
            _seal[4] = _seal[0];
            Stroke(_seal, gold, width * 1.25f);
            DrawCircle(position, width * 0.7f, gold);
        }
    }

    private void DrawHalf(Vector2 center, Vector2 radius, float rotation, float start, Color color, float width)
    {
        for (int i = 0; i < _arc.Length; i++)
            _arc[i] = center + EllipsePoint(start + Mathf.Pi * i / (_arc.Length - 1), radius, rotation);
        Stroke(_arc, color, width);
    }

    private void Stroke(Vector2[] points, Color color, float width)
    {
        DrawPolyline(points, new Color(color.R, color.G, color.B, color.A * 0.12f), width * 4f, true);
        DrawPolyline(points, color, width, true);
    }

    private static Vector2 EllipsePoint(float angle, Vector2 radius, float rotation) =>
        new Vector2(Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y).Rotated(rotation);
}
