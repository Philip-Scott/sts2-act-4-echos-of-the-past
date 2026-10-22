namespace AnimatedPlayerHands;

internal static class HandLayoutGeometry
{
    internal const float CardWidth = 96f;
    internal const float CardHeight = 128f;
    internal const float PanelWidth = 680f;
    internal const float PanelHeight = CardHeight;
    internal const float MaxScale = 0.55f;
    // Smallest a hand shrinks to make room for other players' health bars; below this, cards are unreadable.
    internal const float MinClearScale = 0.3f;
    internal const float HealthBarGap = 6f;
    // Space kept between neighbouring hands and from the screen's edges.
    internal const float HandGap = 12f;
    internal const float EdgeMargin = 16f;
    // NCombatRoom stands each formation row of players 120 scene units behind the one in front, before
    // the encounter's camera zoom. Rows are compared in those unzoomed units, half a row apart.
    internal const float SameRowTolerance = 60f;

    // Gap between hand cards. Negative values overlap cards like the native hand,
    // so a full ten-card hand fits the strip without scrolling.
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

    /// <summary>
    /// A hand's owner: the screen position of their health bar's top center, their formation row in
    /// unzoomed scene units, their on-screen scale, and the health bars and icons the hand must not cover.
    /// </summary>
    internal readonly record struct Owner(float X, float BarTop, float Row, float Scale, IReadOnlyList<Bounds> Obstacles);

    // Panel top that rests the bottom of the hand just above a health bar.
    internal static float TopAboveHealthBar(float barTop, float displayedHeight) =>
        barTop - displayedHeight - HealthBarGap;

    /// <summary>
    /// Places every hand on its owner's health bar. Each hand gets the room between the screen's edges and
    /// the midpoints to its neighbours in the same row, centered on its owner where that fits, and shrinks
    /// until it clears other players' health bars and icons. Hands therefore never overlap one another,
    /// and never cover their own health bar.
    /// </summary>
    internal static Bounds[] Fit(IReadOnlyList<Owner> owners, float viewportWidth, float viewportHeight, float topMargin)
    {
        var placed = new Bounds[owners.Count];
        for (var index = 0; index < owners.Count; index++)
        {
            var owner = owners[index];
            float left = EdgeMargin, right = viewportWidth - EdgeMargin;
            for (var other = 0; other < owners.Count; other++)
            {
                if (other == index || Math.Abs(owners[other].Row - owner.Row) >= SameRowTolerance)
                    continue;
                var midpoint = (owners[other].X + owner.X) / 2f;
                if (owners[other].X < owner.X || (owners[other].X == owner.X && other < index))
                    left = Math.Max(left, midpoint + HandGap / 2f);
                else
                    right = Math.Min(right, midpoint - HandGap / 2f);
            }
            var roomScale = Math.Max(0f, right - left) / PanelWidth;
            var scale = Math.Min(MaxScale * owner.Scale, roomScale);
            var minimum = Math.Min(scale, MinClearScale * owner.Scale);
            Bounds At(float candidate)
            {
                var width = PanelWidth * candidate;
                var height = PanelHeight * candidate;
                var x = Math.Clamp(owner.X - width / 2f, left, Math.Max(left, right - width));
                var y = Math.Clamp(TopAboveHealthBar(owner.BarTop, height), topMargin,
                    Math.Max(topMargin, viewportHeight - height - EdgeMargin));
                return new Bounds(x, y, width, height);
            }
            var candidate = scale;
            var hand = At(candidate);
            while (candidate > minimum && owner.Obstacles.Any(obstacle => hand.Intersects(obstacle, 4f)))
            {
                candidate = Math.Max(minimum, candidate - 0.005f);
                hand = At(candidate);
            }
            placed[index] = hand;
        }
        return placed;
    }
}
