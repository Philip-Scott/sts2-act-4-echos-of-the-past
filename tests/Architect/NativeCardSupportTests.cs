using System.Text.Json;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeCardSupportTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        foreach (var (name, json) in new[]
        {
            ("native properties", """{"id":"CARD.STRIKE_IRONCLAD","props":{"custom":2}}"""),
            ("populated mod values", """{"save_dict_Int32":{"ModCounter":7}}"""),
            ("populated modifiers", """
                {"save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]":
                    {"BaseLibCardModifiers":[{"Id":"CARD_MODIFIER.PROBE","Amount":3}]}}
                """),
            ("empty modifiers", """
                {"save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]":{"BaseLibCardModifiers":[]}}
                """),
            ("empty extension", """{"save_dict_Int32":{}}"""),
            ("null extension", """{"save_dict_Int32":null}""")
        })
        {
            test($"native support: preserves {name} for the game's loader", () =>
            {
                using var document = JsonDocument.Parse(json);
                var raw = document.RootElement;
                var saved = new SerializableCard();
                check(NativeCardSupport.SavedReason(raw) == null, "Recognized native/BaseLib data must be executable.");
                check(ReferenceEquals(saved, NativeCardSupport.ForNativeLoad(raw, saved)),
                    "Do not replace the SerializableCard that owns BaseLib's extended save data.");
            });
        }

        foreach (var json in new[]
        {
            """{"unrecognized_extension":{"value":1}}""",
            """{"save_dict_Int32":[1,2]}""",
            """{"save_dict_Int32":7}"""
        })
        {
            test($"native support: retains explicit fallback for {json}", () =>
            {
                using var document = JsonDocument.Parse(json);
                var raw = document.RootElement;
                var saved = new SerializableCard { CurrentUpgradeLevel = 1, FloorAddedToDeck = 12 };
                check(NativeCardSupport.SavedReason(raw) != null, "Unknown save formats must not be silently executed.");
                var loaded = NativeCardSupport.ForNativeLoad(raw, saved);
                check(!ReferenceEquals(saved, loaded) && loaded.CurrentUpgradeLevel == 1 &&
                    loaded.FloorAddedToDeck == 12, "Fallback preserves native values without restoring unknown extensions.");
            });
        }
    }
}
