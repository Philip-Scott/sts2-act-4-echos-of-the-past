using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
internal static class NativeCoopAllyTargetPatch
{
    private static bool Prefix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (!__instance.IsMutable || !NativeCorruptedPlayer.TryGet(__instance.Owner, out var actor) ||
            __instance.TargetType != TargetType.AnyAlly)
            return true;
        __result = target is { IsAlive: true } && target != actor.Body && actor.View.PlayerCreatures.Contains(target);
        return false;
    }
}

// These native co-op effects assume every card in combat belongs to the same
// party. Opposing player decks make that assumption false in both directions.
[HarmonyPatch]
internal static class NativeCoopCardEventPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CacophonyPower), nameof(CacophonyPower.AfterCardDrawn));
        yield return AccessTools.Method(typeof(SneakyPower), nameof(SneakyPower.AfterCardPlayed));
        yield return AccessTools.Method(typeof(Midnight), nameof(Midnight.AfterCardExhausted));
    }

    private static bool Prefix(AbstractModel __instance, object[] __args, ref Task __result)
    {
        var source = __args.OfType<CardModel>().FirstOrDefault() ??
            __args.OfType<CardPlay>().First().Card;
        var owner = __instance is PowerModel power ? power.Owner : ((CardModel)__instance).Owner.Creature;
        if (source.Owner.Creature.Side == owner.Side)
            return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Midnight), nameof(Midnight.AfterCardEnteredCombat))]
internal static class NativeCoopExhaustHistoryPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable) &&
                method.Name == nameof(Enumerable.OfType) &&
                method.GetGenericArguments().SequenceEqual([typeof(CardExhaustedEntry)]))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(NativeCoopExhaustHistoryPatch), nameof(FromParty)));
            }
        }
    }

    private static IEnumerable<CardExhaustedEntry> FromParty(IEnumerable<CardExhaustedEntry> entries, CardModel card) =>
        entries.Where(entry => entry.Actor?.Side == card.Owner.Creature.Side);
}

[HarmonyPatch]
internal static class NativeCoopDefensiveDurationPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(CoveredPower), nameof(CoveredPower.AfterSideTurnEnd));
        yield return AccessTools.Method(typeof(InterceptPower), nameof(InterceptPower.AfterSideTurnEnd));
        yield return AccessTools.Method(typeof(UnderworldPower), nameof(UnderworldPower.AfterSideTurnEnd));
    }

    [HarmonyPatch(typeof(TagTeamPower), nameof(TagTeamPower.ModifyCardPlayCount))]
    internal static class NativeCoopTagTeamPatch
    {
        private static bool Prefix(TagTeamPower __instance, CardModel card, int playCount, ref int __result)
        {
            if (card.Owner.Creature.Side != __instance.Owner.Side)
                return true;
            __result = playCount;
            return false;
        }
    }

    private static void Prefix(PowerModel __instance, ref CombatSide side)
    {
        if (NativeCorruptedPlayer.TryGet(__instance.Owner, out _))
            side = side == __instance.Owner.Side ? CombatSide.Player : CombatSide.Enemy;
    }
}
