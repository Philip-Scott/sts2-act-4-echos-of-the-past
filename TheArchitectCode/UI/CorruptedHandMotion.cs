using Godot;
using MegaCrit.Sts2.Core.Settings;

namespace TheArchitect.TheArchitectCode.UI;

// Native NHandCardHolder/CardPileCmd hand motion constants, applied to the Corrupted Player's compact hand.
public static class CorruptedHandMotion
{
    public const float MoveSpeed = 7f;
    public const float ScaleSpeed = 8f;
    public const float PileEmergeScale = 0.2f;
    public const float PileEmergeTilt = -0.35f;
    private const float PositionSnap = 1f;
    private const float ScaleSnap = 0.002f;

    // CardPileCmd.Draw awaits each card's 0.25s pile tween, so native draws arrive one after another.
    public static double DrawStagger(FastModeType mode) => mode switch
    {
        FastModeType.Instant => 0,
        FastModeType.Fast => 0.1,
        _ => 0.25
    };

    public static bool Animates(FastModeType mode) => mode != FastModeType.Instant;

    public static double ScheduleArrival(double now, ref double nextFree, double stagger)
    {
        var start = Math.Max(now, nextFree);
        nextFree = start + stagger;
        return start;
    }

    public static Vector2 StepPosition(Vector2 current, Vector2 target, float delta)
    {
        var next = current.Lerp(target, Math.Min(1f, delta * MoveSpeed));
        return next.DistanceSquaredTo(target) < PositionSnap ? target : next;
    }

    public static Vector2 StepScale(Vector2 current, Vector2 target, float delta)
    {
        var next = current.Lerp(target, Math.Min(1f, delta * ScaleSpeed));
        return Mathf.Abs(target.X - next.X) < ScaleSnap ? target : next;
    }

    // NCardFlyVfx arcs 500-900px over a full-screen trip; the enemy HUD's pile hops are much shorter.
    public static float FlyArc(Vector2 start, Vector2 end, float jitter) =>
        Mathf.Max(60f, start.DistanceTo(end) * 0.6f) + jitter;
}
