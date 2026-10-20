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
        return AccessTools.GetDeclaredMethods(selector.MakeGenericType(typeof(RelicModel))).Single(IsPoolSelector);
    }

    private static bool IsPoolSelector(MethodInfo method) =>
        method.Name.Contains("<ConcatModelsFromMods>", StringComparison.Ordinal);

    // The selector lambda lives in a generic closure class whose code is shared by every
    // reference-type model (cards, potions, relics), so this patch replaces all of them. Harmony
    // compiles it against the RelicModel instantiation, so never emit a generic GetById<RelicModel>
    // here: card pools would fail its cast and lose their modded cards and card text formatting.
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getId = AccessTools.Method(typeof(ModelDb), nameof(ModelDb.GetId), [typeof(Type)]);
        var registeredId = AccessTools.Method(typeof(ModdedRelicPoolIdentity), nameof(RegisteredId));
        var registeredModel = AccessTools.Method(typeof(ModdedRelicPoolIdentity), nameof(RegisteredModel));
        bool replacedId = false, replacedModel = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getId))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = registeredId;
                replacedId = true;
            }
            else if (instruction.operand is MethodInfo { Name: nameof(ModelDb.GetById), IsGenericMethod: true } method &&
                method.DeclaringType == typeof(ModelDb))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = registeredModel;
                replacedModel = true;
            }
            yield return instruction;
        }
        if (!replacedId || !replacedModel)
            throw new MissingMethodException("The native modded pool model-ID selector changed.");
    }

    // The native selector can inline vanilla ID generation before BaseLib's prefix patch.
    // Use the registered canonical identity, including custom IDs, rather than regenerating it.
    private static ModelId RegisteredId(Type type)
    {
        var id = ModelDb.GetId(type);
        if (ModelDb.GetByIdOrNull<AbstractModel>(id)?.GetType() == type)
            return id;
        return ModelDb.All.FirstOrDefault(model => model.GetType() == type)?.Id ?? id;
    }

    private static AbstractModel RegisteredModel(ModelId id) => ModelDb.GetById<AbstractModel>(id);
}
