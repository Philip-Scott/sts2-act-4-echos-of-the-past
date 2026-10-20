using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Lifecycle;

// The native modded-pool selector can inline vanilla ID generation before BaseLib's prefix patch.
// Patch only the non-generic relic pool getter: the selector lambda is generic code shared by
// every reference-type pool (cards, potions, ...), so patching it would also run the relic-typed
// body for those pools and break them (e.g. card pools, descriptions and energy icons).
[HarmonyPatch(typeof(RelicPoolModel), nameof(RelicPoolModel.AllRelics), MethodType.Getter)]
internal static class ModdedRelicPoolIdentity
{
    private static readonly MethodInfo NativeConcat = AccessTools.Method(typeof(ModHelper),
        nameof(ModHelper.ConcatModelsFromMods)).GetGenericMethodDefinition();

    private static readonly IDictionary PoolContent = (IDictionary)AccessTools.Field(typeof(ModHelper),
        "_moddedContentForPools").GetValue(null)!;

    private static readonly FieldInfo ModelsToAdd =
        AccessTools.Field(AccessTools.Inner(typeof(ModHelper), "ModPoolContent"), "modelsToAdd");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replacement = AccessTools.Method(typeof(ModdedRelicPoolIdentity), nameof(ConcatModelsFromMods));
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo { IsGenericMethod: true } method &&
                method.GetGenericMethodDefinition() == NativeConcat &&
                method.GetGenericArguments().SequenceEqual([typeof(RelicModel)]))
            {
                instruction.operand = replacement;
                replaced = true;
            }
            yield return instruction;
        }
        if (!replaced)
            throw new MissingMethodException("The native relic pool no longer concatenates modded relics.");
    }

    internal static IEnumerable<RelicModel> ConcatModelsFromMods(IPoolModel poolModel, IEnumerable<RelicModel> pool)
    {
        // The native call freezes the pool's modded additions and returns the pool unchanged when there are none.
        if (ReferenceEquals(ModHelper.ConcatModelsFromMods(poolModel, pool), pool))
            return pool;
        var added = (List<Type>)ModelsToAdd.GetValue(PoolContent[poolModel.GetType()])!;
        return pool.Concat(added.Select(RegisteredRelic));
    }

    // Use the registered canonical identity, including custom IDs, rather than regenerating it.
    private static RelicModel RegisteredRelic(Type type) =>
        (RelicModel)ModelDb.All.Single(model => model.GetType() == type);
}
