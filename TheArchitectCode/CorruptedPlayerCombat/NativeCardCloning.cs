using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeCardCloning
{
    private static readonly MethodInfo CloneMethod = AccessTools.Method(typeof(AbstractModel), "MutableClone");
    private static readonly ICloneableField ModifierField =
        (ICloneableField)AccessTools.Field(typeof(CardModifier), "_modifiers").GetValue(null)!;
    // Read without Modifiers(card), which would create the very collection we probe for.
    internal static readonly ConditionalWeakTable<CardModel, List<CardModifier>> ModifierCollections =
        (ConditionalWeakTable<CardModel, List<CardModifier>>)AccessTools
            .Field(ModifierField.GetType(), "_table").GetValue(ModifierField)!;

    internal static AbstractModel MutableClone(AbstractModel source)
    {
        var clone = (AbstractModel)CloneMethod.Invoke(source, null)!;
        if (source is CardModel card && clone is CardModel clonedCard)
            PreserveModifiers(card, clonedCard);
        return clone;
    }

    internal static CardModel ToMutable(CardModel source)
    {
        var clone = source.ToMutable();
        PreserveModifiers(source, clone);
        return clone;
    }

    internal static AbstractModel ClonePreservingMutability(AbstractModel source) =>
        source.IsMutable ? MutableClone(source) : source;

    // These clone call sites are rewritten for every card, including the human party's,
    // so this must never fail a clone the game itself would have accepted.
    internal static void PreserveModifiers(CardModel source, CardModel clone)
    {
        // BaseLib's clone callback always creates the clone's collection when it runs, even
        // if a modifier then drops itself from the copy (AfterClonedOnCard, stacking, ...).
        if (ModifierCollections.TryGetValue(clone, out _) || CardModifier.Modifiers(source).Count == 0)
            return;

        // An inlined/tiered native clone can miss BaseLib's MutableClone postfix.
        // Dispatch its actual registered callback, including AfterClonedOnCard.
        ModifierField.Clone(source, clone);
        MainFile.Logger.Info($"NATIVE restored omitted BaseLib modifier clone: {source.Id}; " +
            $"source={CardModifier.Modifiers(source).Count}, clone={CardModifier.Modifiers(clone).Count}");
    }
}
