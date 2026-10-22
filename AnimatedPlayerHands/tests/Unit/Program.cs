using System.Text.Json;
using AnimatedPlayerHands;
using Godot;
using MegaCrit.Sts2.Core.Settings;
using static AnimatedPlayerHands.HandLayoutGeometry;
using Environment = System.Environment;

int passed = 0;

void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error}"); Environment.ExitCode = 1; }
}

void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

Test("separation: a single card keeps the default gap", () =>
{
    Check(HandSeparation(0, CardWidth, 400f) == 8);
    Check(HandSeparation(1, CardWidth, 400f) == 8);
});

Test("separation: a ten-card hand overlaps to fit the strip", () =>
{
    var available = PanelWidth - 120f;
    var separation = HandSeparation(10, CardWidth, available);
    Check(separation < 0, $"expected overlap, got {separation}");
    Check(10 * CardWidth + 9 * separation <= available, "ten cards overflow the strip");
});

Owner At(float x, float barTop, float row, params Bounds[] obstacles) => new(x, barTop, row, 1f, obstacles);

void CheckRestsOnBar(Bounds hand, Owner owner, string who) =>
    Check(Math.Abs(hand.Bottom - (owner.BarTop - HealthBarGap)) < 0.01f, $"{who} does not rest on its health bar");

Test("fit: a lone hand is full size and centered on its owner's health bar", () =>
{
    var owner = At(800f, 600f, 200f);
    var hand = Fit([owner], 1920f, 1080f, 100f)[0];
    Check(Math.Abs(hand.Width - PanelWidth * MaxScale) < 0.01f, $"width {hand.Width}");
    Check(Math.Abs(hand.X + hand.Width / 2f - 800f) < 0.01f, "the hand is not centered on its owner");
    CheckRestsOnBar(hand, owner, "the hand");
});

Test("fit: hands in the same row split the room between them", () =>
{
    Owner[] owners = [At(300f, 600f, 200f), At(600f, 600f, 200f)];
    var hands = Fit(owners, 1920f, 1080f, 100f);
    Check(!hands[0].Intersects(hands[1], 0f), "neighbouring hands overlap");
    Check(hands[0].Right <= 450f - HandGap / 2f + 0.01f && hands[1].X >= 450f + HandGap / 2f - 0.01f,
        "a hand crosses the midpoint between its owners");
    CheckRestsOnBar(hands[0], owners[0], "the left hand");
});

Test("fit: zoomed-out encounters keep their two player rows apart", () =>
{
    // NCombatRoom places rows 120 scene units apart; Bygone Effigy zooms to 0.88, about 106 pixels on screen.
    Owner[] owners = [At(325f, 1141f, 200f), At(473f, 1036f, 80f)];
    var hands = Fit(owners, 1920f, 1080f, 100f);
    Check(hands.All(hand => Math.Abs(hand.Width - PanelWidth * MaxScale) < 0.01f), "rows constrain each other's width");
});

Test("fit: a hand at the screen's edge shrinks instead of sliding into its neighbour", () =>
{
    // The back row of a four-player fight: the left player stands near the screen's edge.
    Owner[] owners = [At(113f, 1047f, 80f), At(493f, 1047f, 80f)];
    var hands = Fit(owners, 1920f, 1080f, 100f);
    Check(hands[0].X >= EdgeMargin - 0.01f, "the hand leaves the screen");
    Check(!hands[0].Intersects(hands[1], 0f), "the edge hand slides into its neighbour");
    CheckRestsOnBar(hands[0], owners[0], "the edge hand");
    CheckRestsOnBar(hands[1], owners[1], "the neighbour");
});

Test("fit: hands shrink to clear another row's health bar and icons", () =>
{
    // A front-row bar at y=600; the back-row bar and icons span y 480-540 behind the hand's right half.
    var owner = At(200f, 600f, 200f, new Bounds(300f, 480f, 200f, 60f));
    var hand = Fit([owner], 1920f, 1080f, 100f)[0];
    Check(hand.Width < PanelWidth * MaxScale && hand.Width > PanelWidth * MinClearScale && hand.Y >= 540f + 4f,
        $"the hand ({hand}) still covers the bar");
    CheckRestsOnBar(hand, owner, "the hand");
    var blocked = Fit([At(200f, 600f, 200f, new Bounds(0f, 560f, 400f, 30f))], 1920f, 1080f, 100f)[0];
    Check(Math.Abs(blocked.Width - PanelWidth * MinClearScale) < 0.01f, "hands stop shrinking at a readable size");
});

Test("fit: four players never overlap each other or their own health bars", () =>
{
    // Positions from a four-player fight: the front-row remote and two back-row remotes with their status rows.
    var front = new Bounds(152f, 1167f, 192f, 58f);
    var backRight = new Bounds(397.62f, 1047f, 192f, 58f);
    var backLeft = new Bounds(17.62f, 1047f, 192f, 58f);
    Owner[] owners =
    [
        At(248f, 1167f, 200f, backRight, backLeft),
        At(493.62f, 1047f, 80f, front, backLeft),
        At(113.62f, 1047f, 80f, front, backRight)
    ];
    var hands = Fit(owners, 1920f, 1920f, 100f);
    for (var index = 0; index < hands.Length; index++)
    {
        CheckRestsOnBar(hands[index], owners[index], $"hand {index}");
        Check(hands.Where((_, other) => other != index).All(other => !hands[index].Intersects(other, 0f)),
            $"hand {index} overlaps another hand");
    }
});

Test("formation: players move toward the middle without crossing it", () =>
{
    Check(PlayerFormation.MiddleShift([-400f, -250f]) == 250f - PlayerFormation.MiddleMargin,
        "the rightmost creature does not stop at the margin");
    Check(PlayerFormation.MiddleShift([-10f]) == 0f, "creatures already near the middle move left");
});

Test("motion: draws are staggered like native CardPileCmd.Draw", () =>
{
    double next = 0;
    var stagger = HandMotion.DrawStagger(FastModeType.Normal);
    Check(HandMotion.ScheduleArrival(1.0, ref next, stagger) == 1.0);
    Check(HandMotion.ScheduleArrival(1.0, ref next, stagger) == 1.25);
    Check(HandMotion.ScheduleArrival(2.0, ref next, stagger) == 2.0);
    Check(HandMotion.DrawStagger(FastModeType.Fast) < stagger && HandMotion.DrawStagger(FastModeType.Instant) == 0);
    Check(!HandMotion.Animates(FastModeType.Instant) && HandMotion.Animates(FastModeType.Fast));
});

Test("motion: cards converge on and snap to their slot", () =>
{
    var position = new Vector2(0, 0);
    var scale = new Vector2(0.05f, 0.05f);
    var target = new Vector2(300, -120);
    var targetScale = new Vector2(0.288f, 0.288f);
    for (var frame = 0; frame < 240 && (position != target || scale != targetScale); frame++)
    {
        position = HandMotion.StepPosition(position, target, 1f / 60f);
        scale = HandMotion.StepScale(scale, targetScale, 1f / 60f);
    }
    Check(position == target && scale == targetScale, "flight never landed");
    Check(HandMotion.FlyArc(Vector2.Zero, new Vector2(10, 0), 0f) == 60f, "short hops keep a visible arc");
});

Test("manifest: client-side only and independent of other mods", () =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "../../../../../AnimatedPlayerHands.json");
    using var manifest = JsonDocument.Parse(File.ReadAllText(path));
    var root = manifest.RootElement;
    Check(root.GetProperty("id").GetString() == ModEntry.Id);
    Check(root.GetProperty("name").GetString() == "Animated Player Hands");
    Check(!root.GetProperty("affects_gameplay").GetBoolean(), "peers must not need this mod");
    Check(root.GetProperty("dependencies").GetArrayLength() == 0, "the mod must not depend on other mods");
    Check(root.GetProperty("has_dll").GetBoolean() && !root.GetProperty("has_pck").GetBoolean());
    Check(root.GetProperty("version").GetString() ==
        typeof(ModEntry).Assembly.GetName().Version?.ToString(3), "manifest and assembly versions differ");
});

Console.WriteLine($"{passed} passed");
