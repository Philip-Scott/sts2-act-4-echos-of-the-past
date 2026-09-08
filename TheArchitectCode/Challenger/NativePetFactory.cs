using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace TheArchitect.TheArchitectCode.Challenger;

internal static class NativePetFactory
{
    private static readonly ConditionalWeakTable<MonsterModel, Player> Owners = new();
    internal static void Register(MonsterModel monster, Player player) => Owners.Add(monster, player);

    internal static async Task<Creature> AddPet<T>(Player player) where T : MonsterModel
    {
        if (!NativeChallenger.TryGet(player, out var actor))
            return await PlayerCmd.AddPet<T>(player);
        var pet = actor.View.CreateCreature(ModelDb.Monster<T>().ToMutable(), player.Creature.Side, null);
        await PlayerCmd.AddPet(pet, player);
        return pet;
    }

    internal static IRunState CreationRun(CombatState combat, MonsterModel monster) =>
        Owners.TryGetValue(monster, out var owner) ? owner.RunState : combat.RunState;
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.CreateCreature))]
internal static class NativePetCreationRngPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(CombatState), nameof(CombatState.RunState));
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_1);
                load.MoveLabelsFrom(instruction);
                load.MoveBlocksFrom(instruction);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(NativePetFactory), nameof(NativePetFactory.CreationRun)));
                replacements++;
            }
            else
                yield return instruction;
        }
        if (replacements == 0)
            throw new InvalidOperationException("The native creature-creation RNG boundary changed.");
    }
}
