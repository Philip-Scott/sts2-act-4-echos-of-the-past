using BaseLib.Abstracts;
using BaseLib.Patches.Saves;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeCardSerialization
{
    internal static SerializableCard ToSerializable(CardModel card)
    {
        var saved = card.ToSerializable();
        var extended = ExtendedSaveHandlers<CardModel, SerializableCard>.ExtendedData;
        if (!extended[saved].DictForType<List<CardModifier.ModifierSave>>().ContainsKey("BaseLibCardModifiers"))
        {
            // Invoke the same registered save getters as BaseLib's native postfix.
            extended[saved] = new ExtendedSaveHandlers<CardModel, SerializableCard>.ExtendedSaveData(card);
            MainFile.Logger.Info($"NATIVE restored omitted BaseLib card save data: {card.Id}");
        }
        return saved;
    }
}
