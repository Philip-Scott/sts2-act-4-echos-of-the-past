using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace TheArchitect.TheArchitectCode.UI;

internal static class CorruptedPlayerOrbs
{
    private static readonly ConditionalWeakTable<Creature, NOrbManager> Managers = new();

    internal static void Attach(NCreature node, PlayerCombatState state)
    {
        if (node.OrbManager != null)
            throw new InvalidOperationException("The Corrupted Player already has an orb manager.");
        var manager = NOrbManager.Create(node, isLocal: false);
        AccessTools.PropertySetter(typeof(NCreature), nameof(NCreature.OrbManager)).Invoke(node, [manager]);
        node.AddChild(manager);
        // The real CombatSetUp event already ran before the enemy's AfterAddedToRoom.
        // Zero-capacity characters still need this manager for OrbCmd's first acquired slot.
        manager.AddSlotAnim(state.OrbQueue.Capacity);
        AccessTools.Method(typeof(NCreature), "SetOrbManagerPosition").Invoke(node, []);
        node.UpdateNavigation();
        Managers.Add(node.Entity, manager);
    }

    internal static void Hide(Creature creature)
    {
        // Death removes the node from the room lookup before actor cleanup runs.
        if (Managers.TryGetValue(creature, out var manager))
        {
            Managers.Remove(creature);
            if (GodotObject.IsInstanceValid(manager))
            {
                manager.ClearOrbs();
                manager.Hide();
            }
        }
    }
}
