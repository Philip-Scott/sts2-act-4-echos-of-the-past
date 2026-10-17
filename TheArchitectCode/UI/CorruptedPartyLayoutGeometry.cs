namespace TheArchitect.TheArchitectCode.UI;

internal static class CorruptedPartyLayoutGeometry
{
    // Gap between telegraphed hand cards. Negative values overlap cards like the
    // native hand so a full ten-card hand fits the panel without scrolling.
    internal static int HandSeparation(int count, float cardWidth, float available, int gap = 8)
    {
        if (count <= 1)
            return gap;
        return (int)Math.Floor(Math.Min(gap, (available - count * cardWidth) / (count - 1)));
    }

    internal readonly record struct Bounds(float X, float Y, float Width, float Height)
    {
        internal float Right => X + Width;
        internal float Bottom => Y + Height;
        internal bool Intersects(Bounds other, float gap) =>
            X < other.Right + gap && Right + gap > other.X &&
            Y < other.Bottom + gap && Bottom + gap > other.Y;
    }

    internal static (float X, float Y) EnemyPosition(int index, int count, float scaling)
    {
        var columns = count == 2 ? 2 : (int)Math.Ceiling(Math.Sqrt(count));
        var cellWidth = (960f / scaling - 150f) / columns;
        var row = index / columns;
        if (count == 3)
            return index == 2
                ? (550f / scaling, -20f)
                : ((index == 0 ? 260f : 840f) / scaling, 200f);
        if (count == 4)
            return ((210f + 450f * (index % 2) + 210f * row) / scaling,
                220f - 260f * row);
        // Stagger the bodies, not their native visuals, hitboxes, or attached pets.
        var offset = count == 2 ? 0f : (row == 0 ? 35f : -35f);
        return (150f + cellWidth * (index % columns + 0.5f) + offset,
            count == 2 ? 200f - 80f * index : 200f - 220f * row);
    }

    // The 248px panel's cards and pile toolbar fill its top 228px.
    internal const float ContentFraction = 228f / 248f;
    internal const float HealthBarGap = 6f;

    // Panel top that rests the hand and pile toolbar just above a health bar.
    internal static float TopAboveHealthBar(float barTop, float displayedHeight) =>
        barTop - displayedHeight * ContentFraction - HealthBarGap;

    internal static float Scale(float spacing, float width, float anchorScale) =>
        Math.Clamp((spacing - 16f) / (width * anchorScale), 0.1f, 0.55f);

    internal static Bounds[] Place(IReadOnlyList<Bounds> desired, float viewportWidth, float viewportHeight,
        float topMargin)
    {
        var placed = new Bounds[desired.Count];
        var completed = new List<Bounds>(desired.Count);
        foreach (var index in Enumerable.Range(0, desired.Count).OrderBy(index => desired[index].Y))
        {
            var rect = desired[index];
            rect = rect with
            {
                X = Math.Clamp(rect.X, 16f, Math.Max(16f, viewportWidth - rect.Width - 16f)),
                Y = Math.Max(topMargin, rect.Y)
            };
            // Clamping a rear hand below a wrapped relic bar must also move any
            // front hand it would cover. Never hide the cards behind the inventory.
            foreach (var other in completed.OrderBy(other => other.Y))
                if (rect.Intersects(other, 12f))
                    rect = rect with { Y = other.Bottom + 12f };
            placed[index] = rect;
            completed.Add(rect);
        }
        if (placed.Length > 0)
        {
            var overflow = Math.Max(0, placed.Max(rect => rect.Bottom) - (viewportHeight - 12f));
            var shift = Math.Min(overflow, placed.Min(rect => rect.Y) - topMargin);
            if (shift > 0)
                for (var index = 0; index < placed.Length; index++)
                    placed[index] = placed[index] with { Y = placed[index].Y - shift };
        }
        return placed;
    }
}
