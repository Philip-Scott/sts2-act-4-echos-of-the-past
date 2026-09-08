using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace TheArchitect.TheArchitectCode.UI;

internal static class ChallengerOrbs
{
    internal static void Attach(NCreature node)
    {
        if (node.OrbManager != null)
            throw new InvalidOperationException("The Challenger already has an orb manager.");
        var manager = NOrbManager.Create(node, isLocal: false);
        AccessTools.PropertySetter(typeof(NCreature), nameof(NCreature.OrbManager)).Invoke(node, [manager]);
        node.AddChild(manager);
        // The real CombatSetUp event already ran before the enemy's AfterAddedToRoom.
        // Zero-capacity characters still need this manager for OrbCmd's first acquired slot.
        manager.AddSlotAnim(node.Entity.Player!.PlayerCombatState!.OrbQueue.Capacity);
        AccessTools.Method(typeof(NCreature), "SetOrbManagerPosition").Invoke(node, []);
        node.UpdateNavigation();
    }

    internal static void Hide(NCreature? node)
    {
        if (GodotObject.IsInstanceValid(node) && GodotObject.IsInstanceValid(node!.OrbManager))
        {
            node.OrbManager!.ClearOrbs();
            node.OrbManager.Hide();
        }
    }
}
