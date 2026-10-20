using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch]
internal static class ModdedRelicPoolIdentity
{
    private static MethodBase TargetMethod()
    {
        var selector = typeof(ModHelper).GetNestedTypes(BindingFlags.NonPublic).Single(type =>
            type.IsGenericTypeDefinition && AccessTools.GetDeclaredMethods(type).Any(IsPoolSelector));
        // Reference-type instantiations share one native body, so this patch also replaces the card and
        // potion pool selectors. Harmony bakes the patched instantiation's GetById<T> into the replacement:
        // RelicModel made every modded card or potion fail its cast, so patch the type every model satisfies.
        return AccessTools.GetDeclaredMethods(selector.MakeGenericType(typeof(AbstractModel))).Single(IsPoolSelector);
    }

    private static bool IsPoolSelector(MethodInfo method) =>
        method.Name.Contains("<ConcatModelsFromMods>", StringComparison.Ordinal);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getId = AccessTools.Method(typeof(ModelDb), nameof(ModelDb.GetId), [typeof(Type)]);
        var registeredId = AccessTools.Method(typeof(ModdedRelicPoolIdentity), nameof(RegisteredId));
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getId))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = registeredId;
                replaced = true;
            }
            yield return instruction;
        }
        if (!replaced)
            throw new MissingMethodException("The native modded relic pool model-ID selector changed.");
    }

    // The native selector can inline vanilla ID generation before BaseLib's prefix patch.
    // Use the registered canonical identity, including custom IDs, rather than regenerating it.
    private static ModelId RegisteredId(Type type) =>
        ModelDb.All.Single(model => model.GetType() == type).Id;
}
