using System.Reflection;
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

    internal static void PreserveModifiers(CardModel source, CardModel clone)
    {
        var originals = CardModifier.Modifiers(source);
        if (originals.Count == 0 || CardModifier.Modifiers(clone).Count != 0)
            return;

        // An inlined/tiered native clone can miss BaseLib's MutableClone postfix.
        // Dispatch its actual registered callback, including AfterClonedOnCard,
        // only when the entire source modifier collection was omitted.
        ModifierField.Clone(source, clone);
        var copied = CardModifier.Modifiers(clone);
        if (copied.Count != originals.Count || copied.Zip(originals).Any(pair =>
                pair.First.Id != pair.Second.Id || pair.First.Owner != clone ||
                ReferenceEquals(pair.First, pair.Second)))
            throw new InvalidOperationException($"Native clone failed to preserve BaseLib modifiers for {source.Id}.");
        MainFile.Logger.Info($"NATIVE restored omitted BaseLib modifier clone: {source.Id}; count={copied.Count}");
    }
}
