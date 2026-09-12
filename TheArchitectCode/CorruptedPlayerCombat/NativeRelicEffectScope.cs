using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

// Source bonuses and receiver bonuses have different owners. Keep the real hook
// dispatcher (including opposing defensive powers), but never lend a human relic
// to a private actor's block, card repeats or power-amount calculation.
[HarmonyPatch]
internal static class NativeRelicEffectScope
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Hook), nameof(Hook.ModifyBlock));
        yield return AccessTools.Method(typeof(Hook), nameof(Hook.ModifyCardPlayCount));
        yield return AccessTools.Method(typeof(Hook), nameof(Hook.ModifyPowerAmountGiven));
        yield return AccessTools.Method(typeof(Hook), nameof(Hook.ModifyPowerAmountReceived));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        var parameterName = __originalMethod.Name switch
        {
            nameof(Hook.ModifyCardPlayCount) => "card",
            nameof(Hook.ModifyPowerAmountGiven) => "giver",
            _ => "target"
        };
        var parameter = __originalMethod.GetParameters().Single(p => p.Name == parameterName);
        var filter = AccessTools.Method(typeof(NativeRelicEffectScope),
            parameter.ParameterType == typeof(CardModel) ? nameof(ForCard) : nameof(ForCreature));
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Hook) &&
                method.Name == "IterateCombatHookListeners")
            {
                yield return new CodeInstruction(OpCodes.Ldarg, parameter.Position);
                yield return new CodeInstruction(OpCodes.Call, filter);
            }
        }
    }

    private static IEnumerable<AbstractModel> ForCard(IEnumerable<AbstractModel> listeners, CardModel card) =>
        ForCreature(listeners, card.Owner.Creature);

    internal static IEnumerable<AbstractModel> ForCreature(IEnumerable<AbstractModel> listeners, Creature creature) =>
        NativeCorruptedPlayer.TryGet(creature, out var actor)
            ? listeners.Where(model => model is not RelicModel relic || relic.Owner == actor.Player)
            : listeners;
}
