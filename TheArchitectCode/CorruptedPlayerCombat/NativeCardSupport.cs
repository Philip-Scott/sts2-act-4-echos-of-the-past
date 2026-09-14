using System.Text.Json;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeCardSupport
{
    // Preserve raw extension data in the snapshot, but don't run its deserializers
    // while restoring a card that has already been excluded from native execution.
    internal static SerializableCard ForNativeLoad(JsonElement raw, SerializableCard saved)
    {
        if (SavedReason(raw) == null)
        {
            // BaseLib 3.4.5 inserts its modifier loader at SavedProperties.Fill.
            // Vanilla's null-conditional skips that hook on cards without saved properties.
            if (raw.TryGetProperty("save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]", out var extension) &&
                extension.ValueKind == JsonValueKind.Object &&
                extension.TryGetProperty("BaseLibCardModifiers", out var modifiers) &&
                modifiers.ValueKind == JsonValueKind.Array && modifiers.GetArrayLength() > 0)
                saved.Props ??= new();
            return saved;
        }
        return new SerializableCard
        {
            Id = saved.Id,
            CurrentUpgradeLevel = saved.CurrentUpgradeLevel,
            Enchantment = saved.Enchantment,
            Props = saved.Props,
            FloorAddedToDeck = saved.FloorAddedToDeck
        };
    }

    internal static string? SavedReason(JsonElement raw)
    {
        foreach (var field in raw.EnumerateObject())
        {
            if (field.Name is "id" or "current_upgrade_level" or "floor_added_to_deck" or "props" or "enchantment")
                continue;
            // BaseLib owns these typed dictionaries, including nonempty modifier
            // saves. Keep the original SerializableCard so its extension data survives.
            if (field.Name.StartsWith("save_dict_", StringComparison.Ordinal) &&
                field.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Object)
                continue;
            return "Unsupported: unrecognized saved card/modifier data";
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
