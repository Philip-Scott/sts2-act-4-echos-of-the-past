using System.Text.Json;

namespace TheArchitect.TheArchitectCode.Challenger;

internal static class SavedCardModifierPolicy
{
    public static bool HasUnsupportedModifiers(JsonElement card)
    {
        if (card.ValueKind != JsonValueKind.Object) return true;
        foreach (var property in card.EnumerateObject())
        {
            switch (property.Name)
            {
                case "id":
                case "current_upgrade_level":
                case "floor_added_to_deck":
                    break;
                case "enchantment":
                    if (property.Value.ValueKind != JsonValueKind.Null) return true;
                    break;
                case "props":
                    if (!EmptyNativeProperties(property.Value)) return true;
                    break;
                case "save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]":
                    if (!EmptyBaseLibModifiers(property.Value)) return true;
                    break;
                default:
                    // BaseLib 3.4.5 ExtendedSaveHandlers emits save_dict_<type> even without entries.
                    if (!property.Name.StartsWith("save_dict_", StringComparison.Ordinal) ||
                        !EmptyObjectOrNull(property.Value))
                        return true;
                    break;
            }
        }
        return false;
    }

    private static bool EmptyNativeProperties(JsonElement props)
    {
        if (props.ValueKind == JsonValueKind.Null) return true;
        if (props.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in props.EnumerateObject())
        {
            if (property.Name is not ("ints" or "bools" or "strings" or "int_arrays" or "model_ids" or "cards" or "card_arrays"))
                return false;
            if (property.Value.ValueKind != JsonValueKind.Null &&
                (property.Value.ValueKind != JsonValueKind.Array || property.Value.GetArrayLength() != 0))
                return false;
        }
        return true;
    }

    private static bool EmptyObjectOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null ||
        (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any());

    private static bool EmptyBaseLibModifiers(JsonElement value) =>
        EmptyObjectOrNull(value) ||
        (value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(property =>
            property.Name == "BaseLibCardModifiers" && property.Value.ValueKind == JsonValueKind.Array &&
            property.Value.GetArrayLength() == 0));
}
