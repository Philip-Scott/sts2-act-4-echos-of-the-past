using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace TheArchitect.TheArchitectCode.Challenger;

[HarmonyPatch]
internal static class NativeOstyScope
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.Method(typeof(OstyCmd), nameof(OstyCmd.Summon))
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var creature = AccessTools.PropertyGetter(typeof(Player), nameof(Player.Creature));
        var combat = AccessTools.PropertyGetter(typeof(Creature), nameof(Creature.CombatState));
        var replacements = 0;
        for (var i = 0; i < code.Count; i++)
        {
            if (i + 1 < code.Count && code[i].Calls(creature) && code[i + 1].Calls(combat))
            {
                var call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativeOstyScope), nameof(Scope)));
                call.MoveLabelsFrom(code[i]);
                call.MoveBlocksFrom(code[i]);
                yield return call;
                i++;
                replacements++;
            }
            else
                yield return code[i];
        }
        if (replacements != 1)
            throw new InvalidOperationException("The native Osty summon ownership boundary changed.");
    }

    private static ICombatState? Scope(Player player) =>
        NativeChallenger.TryGet(player, out var actor) ? actor.View : player.Creature.CombatState;
}
