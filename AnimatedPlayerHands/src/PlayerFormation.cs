using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace AnimatedPlayerHands;

/// <summary>
/// Gives remote hands room in co-op: players standing side by side get a wider gap, and the formation
/// moves toward the middle of the screen without crossing it. Only positions on this client change; the
/// game still shrinks the gap when a formation would not fit its half of the screen.
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.PositionPlayersAndPets))]
internal static class PlayerFormation
{
    internal const float NativeGap = 70f;
    // A full-size hand is 374 px wide; typical bodies are about 230 px, so this keeps neighbours' hands apart.
    internal const float Gap = 170f;
    // The formation's rightmost body or pet stops this far left of the middle line (scene x = 0).
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

    // Natively the formation is centered in the left half of the screen, leaving open floor before the
    // middle. Encounters that deliberately center the players, and solo runs, keep their native place.
    private static void Postfix(List<NCreature> creatureNodes, bool fullyCenterPlayers)
    {
        try
        {
            if (fullyCenterPlayers || creatureNodes.Count(node => node.Entity.IsPlayer) < 2)
                return;
            var shift = MiddleShift(creatureNodes.Select(RightEdge));
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
    private static float RightEdge(NCreature node) => node.Position.X + node.Visuals.Bounds.Size.X * 0.5f;

    internal static float MiddleShift(IEnumerable<float> rightEdges)
    {
        var rightmost = rightEdges.DefaultIfEmpty(-MiddleMargin).Max();
        return Math.Max(0f, -MiddleMargin - rightmost);
    }
}
