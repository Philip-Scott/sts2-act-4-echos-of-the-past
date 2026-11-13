using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace AnimatedPlayerHands;

/// <summary>
/// Gives remote hands room in co-op: players standing side by side get a wider gap, and a formation that
/// the gap pushes off the left edge moves right just enough, without crossing the middle. Only positions on this client change; the
/// game still shrinks the gap when a formation would not fit its half of the screen.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets))]
internal static class PlayerFormation
{
    internal const float NativeGap = 70f;
    // A full-size hand is 374 px wide; typical bodies are about 230 px, so this keeps neighbours' hands apart.
    internal const float Gap = 170f;
    // Room kept between the leftmost body and the screen's left edge: a full-size hand reaches about
    // 75 past a typical body, plus the hands' own edge margin.
    internal const float EdgeMargin = 100f;
    // A formation never moves closer than this to the middle line (scene x = 0).
    internal const float MiddleMargin = 40f;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (!replaced && instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value &&
                value == NativeGap)
            {
                instruction.operand = Gap;
                replaced = true;
            }
            yield return instruction;
        }
        if (!replaced)
            ModEntry.Logger.Warn("The game's player spacing changed; players keep their native spacing.");
    }

    // Natively the formation is centered in the left half of the screen. It stays there unless the wider
    // gap pushes a player and their hand off the left edge, as the back row of a four-player fight does;
    // then it moves right only as far as needed, and never past the middle line. Encounters that
    // deliberately center the players, and solo runs, keep their native place.
    private static void Postfix(List<NCreature> creatureNodes, float scaling, bool fullyCenterPlayers)
    {
        try
        {
            if (fullyCenterPlayers || creatureNodes.Count(node => node.Entity.IsPlayer) < 2)
                return;
            var shift = Shift(creatureNodes.Select(LeftEdge), creatureNodes.Select(RightEdge), scaling);
            if (shift <= 0f)
                return;
            foreach (var node in creatureNodes)
                node.Position += new Godot.Vector2(shift, 0f);
        }
        catch (Exception error)
        {
            Faults.Report("Player formation", error);
        }
    }

    // NCombatRoom positions creatures by their center, with the middle line at x = 0.
    private static float LeftEdge(NCreature node) => node.Position.X - node.Visuals.Bounds.Size.X * 0.5f;

    private static float RightEdge(NCreature node) => node.Position.X + node.Visuals.Bounds.Size.X * 0.5f;

    // The screen's left edge is 960 design pixels from the middle; the scene is zoomed by the camera scaling.
    internal static float Shift(IEnumerable<float> leftEdges, IEnumerable<float> rightEdges, float scaling)
    {
        var leftmost = leftEdges.DefaultIfEmpty(0f).Min();
        var rightmost = rightEdges.DefaultIfEmpty(-MiddleMargin).Max();
        var needed = -960f / scaling + EdgeMargin - leftmost;
        var allowed = -MiddleMargin - rightmost;
        return Math.Clamp(needed, 0f, Math.Max(0f, allowed));
    }
}
