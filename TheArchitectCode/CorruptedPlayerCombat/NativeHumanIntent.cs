using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeHumanIntent
{
    internal static bool IntendsToAttack(Creature target, CardModel source)
    {
        if (target.Monster is { } monster)
            return monster.IntendsToAttack;
        if (!NativeCorruptedPlayer.TryGet(source.Owner, out _) || target.Player is not { } player)
            throw new InvalidOperationException("An intent query requires a monster or a native Corrupted Player's human opponent.");
        return player.PlayerCombatState?.Hand.Cards.Any(card => card.Type == CardType.Attack) == true;
    }
}

// Preserve native attack-then-intent-check ordering, including reactive hand changes.
[HarmonyPatch]
internal static class NativeGoForTheEyesIntentPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(AccessTools.Method(typeof(GoForTheEyes), "OnPlay")
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var monster = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.Monster));
        var intent = AccessTools.PropertyGetter(typeof(MonsterModel), nameof(MonsterModel.IntendsToAttack));
        var owner = AccessTools.Field(__originalMethod.DeclaringType, "<>4__this");
        var replacements = 0;
        for (var i = 0; i < code.Count; i++)
        {
            if (i + 1 < code.Count && code[i].Calls(monster) && code[i + 1].Calls(intent))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.MoveLabelsFrom(code[i]);
                load.MoveBlocksFrom(code[i]);
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldfld, owner);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(NativeHumanIntent), nameof(NativeHumanIntent.IntendsToAttack)));
                i++;
                replacements++;
            }
            else
                yield return code[i];
        }
        if (replacements != 1)
            throw new InvalidOperationException("The native Go for the Eyes intent-query boundary changed.");
    }
}
