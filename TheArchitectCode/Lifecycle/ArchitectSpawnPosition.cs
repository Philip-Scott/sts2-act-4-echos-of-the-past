using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using TheArchitect.TheArchitectCode.Encounters;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))]
internal static class ArchitectSpawnPosition
{
    private static readonly Action<NCombatRoom, List<NCreature>, float> PositionEnemy =
        AccessTools.MethodDelegate<Action<NCombatRoom, List<NCreature>, float>>(
            AccessTools.Method(typeof(NCombatRoom), "PositionEnemies"));
    private static readonly Action<NCombatRoom> UpdateNavigation =
        AccessTools.MethodDelegate<Action<NCombatRoom>>(AccessTools.Method(typeof(NCombatRoom), "UpdateCreatureNavigation"));

    private static void Postfix(NCombatRoom __instance, Creature creature)
    {
        if (creature.Monster is ArchitectBoss &&
            creature.CombatState?.Encounter is ArchitectEncounter encounter &&
            __instance.GetCreatureNode(creature) is { } node)
        {
            // Mid-combat spawns with no named slot otherwise remain at the container's origin.
            PositionEnemy(__instance, [node], encounter.GetCameraScaling());
            UpdateNavigation(__instance);
        }

    }
}

[HarmonyPatch(typeof(NCombatRoom), "PositionEnemies")]
internal static class CorruptedPartySpawnPosition
{
    private static bool Prefix(List<NCreature> creatures, float scaling)
    {
        if (creatures.Count < 2 || creatures.Any(node => node.Entity.Monster is not CorruptedPlayer))
            return true;
        for (var index = 0; index < creatures.Count; index++)
        {
            var (x, y) = CorruptedPartyLayoutGeometry.EnemyPosition(index, creatures.Count, scaling);
            creatures[index].Position = new Vector2(x, y);
        }
        return false;
    }
}
