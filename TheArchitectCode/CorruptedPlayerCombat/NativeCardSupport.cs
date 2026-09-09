using System.Text.Json;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeCardSupport
{
    // Preserve raw extension data in the snapshot, but don't run its deserializers
    // while restoring a card that has already been excluded from native execution.
    internal static SerializableCard ForNativeLoad(JsonElement raw, SerializableCard saved) =>
        SavedReason(raw) == null ? saved : new SerializableCard
        {
            Id = saved.Id,
            CurrentUpgradeLevel = saved.CurrentUpgradeLevel,
            Enchantment = saved.Enchantment,
            Props = saved.Props,
            FloorAddedToDeck = saved.FloorAddedToDeck
        };

    internal static string? Reason(CardModel card)
    {
        if (card.GetType().Assembly != typeof(CardModel).Assembly)
            return "Unsupported: third-party card effects";
        if (card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly)
            return "Unsupported: co-op-only card";
        if (card.Enchantment is { } enchantment && enchantment.GetType().Assembly != typeof(CardModel).Assembly)
            return "Unsupported: third-party enchantment";
        if (card.Affliction is { } affliction && affliction.GetType().Assembly != typeof(CardModel).Assembly ||
            CardModifier.Modifiers(card).Count > 0)
            return "Unsupported: third-party card modifier";
        return null;
    }

    internal static string? SavedReason(JsonElement raw)
    {
        foreach (var field in raw.EnumerateObject())
        {
            if (field.Name is "id" or "current_upgrade_level" or "floor_added_to_deck" or "props" or "enchantment")
                continue;
            if (field.Name == "save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]" &&
                field.Value.ValueKind == JsonValueKind.Object && field.Value.EnumerateObject().All(p =>
                    p.Name == "BaseLibCardModifiers" && p.Value.ValueKind == JsonValueKind.Array &&
                    p.Value.GetArrayLength() == 0))
                continue;
            if (field.Name.StartsWith("save_dict_", StringComparison.Ordinal) &&
                (field.Value.ValueKind == JsonValueKind.Null ||
                 field.Value.ValueKind == JsonValueKind.Object && !field.Value.EnumerateObject().Any()))
                continue;
            return "Unsupported: third-party saved card/modifier data";
        }
        return null;
    }

    internal static string? Preflight(IReadOnlyList<JsonElement> deck)
    {
        foreach (var raw in deck)
        {
            SerializableCard saved;
            try
            {
                saved = raw.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>())
                    ?? throw new JsonException("Null card.");
            }
            catch (JsonException error)
            {
                return $"Cannot read a saved Corrupted Player card: {error.Message}";
            }
            if (saved.Id == null || ModelDb.GetByIdOrNull<CardModel>(saved.Id) == null)
                return $"Saved Corrupted Player card is not installed: {saved.Id}. Install the matching game/mod version before entering.";
            if (saved.Enchantment is { } enchantment &&
                (enchantment.Id == null || ModelDb.GetByIdOrNull<EnchantmentModel>(enchantment.Id) == null))
                return $"Saved Corrupted Player enchantment is not installed: {enchantment.Id}. Install its matching mod before entering.";
            if (raw.TryGetProperty("save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]", out var extension) &&
                extension.ValueKind == JsonValueKind.Object && extension.TryGetProperty("BaseLibCardModifiers", out var modifiers))
            {
                if (modifiers.ValueKind != JsonValueKind.Array)
                    return "Cannot read the saved Corrupted Player card modifiers. Restore a valid snapshot backup.";
                foreach (var modifier in modifiers.EnumerateArray())
                {
                    if (modifier.ValueKind != JsonValueKind.Object || !modifier.TryGetProperty("Id", out var id))
                        return "A saved Corrupted Player card modifier has no model ID. Restore a valid snapshot backup.";
                    ModelId? model;
                    try
                    {
                        model = id.Deserialize(JsonSerializationUtility.GetTypeInfo<ModelId>());
                    }
                    catch (JsonException error)
                    {
                        return $"Cannot read a saved Corrupted Player modifier ID: {error.Message}";
                    }
                    if (model == null || ModelDb.GetByIdOrNull<CardModifier>(model) == null)
                        return $"Saved Corrupted Player card modifier is not installed: {model}. Install its matching mod before entering.";
                }
            }
        }
        return null;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ShouldPlay))]
internal static class NativeUnsupportedPlayPatch
{
    private static bool Prefix(CardModel card, ref bool __result, ref AbstractModel? preventer)
    {
        if (!card.IsMutable || card.Owner is not { } owner || !NativeCorruptedPlayer.TryGet(owner, out var actor) ||
            actor.UnsupportedReason(card) == null)
            return true;
        __result = false;
        preventer = card;
        return false;
    }
}
