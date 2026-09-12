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

        test("corrupted enemy layout: three players mirror the human triangle", () =>
        {
            foreach (var scaling in new[] { 0.8f, 1f, 1.2f })
            {
                var host = CorruptedPartyLayoutGeometry.EnemyPosition(0, 3, scaling);
                var second = CorruptedPartyLayoutGeometry.EnemyPosition(1, 3, scaling);
                var third = CorruptedPartyLayoutGeometry.EnemyPosition(2, 3, scaling);
                check(host.X < third.X && third.X < second.X,
                    "The host is nearest the humans, with player three centered behind the first two.");
                check(host.Y == second.Y && third.Y < host.Y,
                    "Player three sits above the two front-row players.");
                check(Math.Abs(third.X - (host.X + second.X) / 2f) < 0.01f,
                    "The rear player must stay horizontally centered at every camera scale.");
                check((second.X - host.X) * scaling >= 580f && host.Y - third.Y >= 220f,
                    "The triangle needs room for both bodies and compact card rows.");
            }
        });

        test("corrupted enemy layout: four players mirror a spacious staggered grid", () =>
        {
            foreach (var scaling in new[] { 0.8f, 1f, 1.2f })
            {
                var positions = Enumerable.Range(0, 4)
                    .Select(index => CorruptedPartyLayoutGeometry.EnemyPosition(index, 4, scaling)).ToArray();
                check(positions[0].X < positions[2].X && positions[2].X < positions[1].X &&
                    positions[1].X < positions[3].X,
                    "The rear pair is staggered away from the humans, not stacked above the front pair.");
                check(positions[0].Y == positions[1].Y && positions[2].Y == positions[3].Y &&
                    positions[0].Y - positions[2].Y >= 260f,
                    "Both rows must stay level with ample vertical separation.");
                check((positions[1].X - positions[0].X) * scaling >= 449.99f &&
                    (positions[3].X - positions[2].X) * scaling >= 449.99f &&
                    (positions[2].X - positions[0].X) * scaling >= 209.99f,
                    "Camera scaling must preserve the wide columns and diagonal offset.");
                check(positions[3].X * scaling <= 870.01f,
                    "The last player still needs room before the right edge.");
            }
        });

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
