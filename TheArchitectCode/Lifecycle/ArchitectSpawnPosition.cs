using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using TheArchitect.TheArchitectCode.Encounters;
using TheArchitect.TheArchitectCode.Monsters;

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
        if (creatures.Count < 3 || creatures.Any(node => node.Entity.Monster is not CorruptedPlayer))
            return true;
        var columns = (int)Math.Ceiling(Math.Sqrt(creatures.Count));
        var cellWidth = (960f / scaling - 150f) / columns;
        for (var index = 0; index < creatures.Count; index++)
        {
            var row = index / columns;
            creatures[index].Position = new Vector2(
                150f + cellWidth * (index % columns + 0.5f), 200f - 240f * row);
        }
        return false;
    }
}
