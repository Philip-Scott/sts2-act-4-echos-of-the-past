using TheArchitect.TheArchitectCode.UI;

internal static class CorruptedEnemyUiTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        foreach (var count in new[] { 2, 3, 4 })
        {
            test($"corrupted enemy layout: {count} bodies have distinct staggered positions", () =>
            {
                var positions = Enumerable.Range(0, count)
                    .Select(index => CorruptedPartyLayoutGeometry.EnemyPosition(index, count, 1f)).ToArray();
                check(positions.Distinct().Count() == count, "Each actor needs its own position.");
                check(positions.Select(position => position.Y).Distinct().Count() == 2,
                    "Even a two-member party must have a depth offset.");
                if (count > 2)
                    check(positions[0].X != positions[2].X,
                        "Front and rear bodies must not share a vertical axis.");
            });
        }

        test("corrupted enemy layout: wrapped relic inventory moves both overlapping rows", () =>
        {
            CorruptedPartyLayoutGeometry.Bounds[] desired =
            [
                new(900, 100, 360, 136), new(1300, 100, 360, 136),
                new(930, 250, 360, 136), new(1330, 250, 360, 136)
            ];
            foreach (var relicBottom in new[] { 100f, 200f, 320f })
            {
                var actual = CorruptedPartyLayoutGeometry.Place(desired, 1920f, 1080f, relicBottom);
                check(actual.All(rect => rect.Y >= relicBottom),
                    "No hand may sit underneath the human's wrapped relic inventory.");
                check(actual.All(rect => rect.X >= 16f && rect.Right <= 1904f && rect.Bottom <= 1068f),
                    "All compact hands must stay inside the viewport.");
                for (var index = 0; index < actual.Length; index++)
                    check(actual.Skip(index + 1).All(other => !actual[index].Intersects(other, 0f)),
                        "Moving the rear row must not hide a front-row hand.");
            }
        });

        test("corrupted enemy layout: edge clamping does not stack hand previews", () =>
        {
            CorruptedPartyLayoutGeometry.Bounds[] desired =
            [
                new(1050, 110, 350, 140), new(1200, 110, 350, 140)
            ];
            var actual = CorruptedPartyLayoutGeometry.Place(desired, 1280f, 720f, 180f);
            check(actual.All(rect => rect.Right <= 1264f),
                "Panels near the right screen edge must remain inspectable.");
            check(!actual[0].Intersects(actual[1], 0f),
                "Screen-edge clamping must resolve collisions, not put one hand atop another.");
        });

        test("corrupted enemy layout: collision resolution stays above the bottom edge", () =>
        {
            CorruptedPartyLayoutGeometry.Bounds[] desired =
            [
                new(1000, 500, 250, 130), new(1100, 540, 250, 130)
            ];
            var actual = CorruptedPartyLayoutGeometry.Place(desired, 1280f, 720f, 180f);
            check(actual.All(rect => rect.Y >= 180f && rect.Bottom <= 708f),
                "A shared upward shift must keep every hand inside the usable viewport.");
            check(!actual[0].Intersects(actual[1], 0f), "Shifting the group must retain hand separation.");
        });

        test("corrupted enemy layout: compact scale respects native anchor scaling", () =>
        {
            check(CorruptedPartyLayoutGeometry.Scale(float.PositiveInfinity, 680, 1) == 0.55f,
                "A lone member in its row retains the compact party scale.");
            var scale = CorruptedPartyLayoutGeometry.Scale(220, 680, 0.8f);
            check(Math.Abs(680 * scale * 0.8f - 204) < 0.01f,
                "Adjacent hands need their native camera scaling and sixteen-pixel gap.");
        });
    }
}
