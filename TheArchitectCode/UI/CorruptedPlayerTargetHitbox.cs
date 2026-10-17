using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using TheArchitect.TheArchitectCode.Monsters;

namespace TheArchitect.TheArchitectCode.UI;

[HarmonyPatch(typeof(NCreature), "UpdateBounds", [typeof(Node)])]
internal static class CorruptedPlayerTargetHitbox
{
    internal const float Padding = 24f;

    private static void Postfix(NCreature __instance)
    {
        if (__instance.Entity.Monster is not CorruptedPlayer)
            return;
        // Native bounds updates reset the hitbox first. Pad only input, leaving
        // the health bar, selection reticle and visual anchors at their native bounds.
        __instance.Hitbox.Position -= Vector2.One * Padding;
        __instance.Hitbox.Size += Vector2.One * (Padding * 2f);
    }
}
