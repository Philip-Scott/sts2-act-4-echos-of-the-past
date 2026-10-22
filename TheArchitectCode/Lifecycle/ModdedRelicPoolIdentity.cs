using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Lifecycle;

// The native modded-pool selector can inline vanilla ID generation before BaseLib's prefix patch.
// Its lambda is shared generic code for every pool type, so patching it would also rewrite card
// and potion pools. Redirect only the non-generic relic pool caller to a relic-only selector.
[HarmonyPatch(typeof(RelicPoolModel), nameof(RelicPoolModel.AllRelics), MethodType.Getter)]
internal static class ModdedRelicPoolIdentity
{
    private static readonly IDictionary ModdedContent = (IDictionary)AccessTools.Field(typeof(ModHelper),
        "_moddedContentForPools").GetValue(null)!;
    private static readonly Type ContentType = AccessTools.Inner(typeof(ModHelper), "ModPoolContent");
    private static readonly FieldInfo IsFrozen = AccessTools.Field(ContentType, "isFrozen");
    private static readonly FieldInfo ModelsToAdd = AccessTools.Field(ContentType, "modelsToAdd");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var native = AccessTools.Method(typeof(ModHelper), nameof(ModHelper.ConcatModelsFromMods))
            .MakeGenericMethod(typeof(RelicModel));
        var replacement = AccessTools.Method(typeof(ModdedRelicPoolIdentity), nameof(ConcatRelicsFromMods));
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced = true;
            }
            yield return instruction;
        }
        if (!replaced)
            throw new MissingMethodException("The native relic pool no longer concatenates modded relics.");
    }

    // Mirrors ModHelper.ConcatModelsFromMods, including freezing the pool's modded additions.
    internal static IEnumerable<RelicModel> ConcatRelicsFromMods(IPoolModel poolModel, IEnumerable<RelicModel> pool)
    {
        var poolType = poolModel.GetType();
        var content = ModdedContent[poolType];
        if (content == null)
        {
            content = Activator.CreateInstance(ContentType, nonPublic: true)!;
            ModdedContent.Add(poolType, content);
        }
        IsFrozen.SetValue(content, true);
        return ModelsToAdd.GetValue(content) is List<Type> added
            ? pool.Concat(added.Select(type => ModelDb.GetById<RelicModel>(RegisteredId(type))))
            : pool;
    }

    // Use the registered canonical identity, including custom IDs, rather than regenerating it.
    private static ModelId RegisteredId(Type type) =>
        ModelDb.All.Single(model => model.GetType() == type).Id;
}
