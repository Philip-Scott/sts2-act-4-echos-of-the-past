using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeCombatRoster
{
    internal static IReadOnlyList<Creature> PlayerCreatures(ICombatState combat) =>
        combat is CombatState
            ? combat.Creatures.Where(NativeCombatCallSites.IsPartyPlayer).ToArray()
            : combat.PlayerCreatures;

    internal static IReadOnlyList<Player> Players(ICombatState combat) =>
        combat is CombatState
            ? PlayerCreatures(combat).Select(creature => creature.Player!).ToArray()
            : combat.Players;
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.PlayerCreatures), MethodType.Getter)]
internal static class NativeCombatPlayerCreaturesPatch
{
    private static bool Prefix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        __result = NativeCombatRoster.PlayerCreatures(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.Players), MethodType.Getter)]
internal static class NativeCombatPlayersPatch
{
    private static bool Prefix(CombatState __instance, ref IReadOnlyList<Player> __result)
    {
        __result = NativeCombatRoster.Players(__instance);
        return false;
    }
}
