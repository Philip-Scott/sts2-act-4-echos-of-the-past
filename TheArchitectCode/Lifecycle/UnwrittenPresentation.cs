using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Ancients;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(EventModel), nameof(EventModel.CreateBackgroundScene))]
internal static class UnwrittenPresentation
{
    private static bool Prefix(EventModel __instance, ref PackedScene __result)
    {
        if (__instance is not TheUnwritten)
            return true;

        // Runtime packing preserves the project's editor-free asset build and native Ancient layout.
        var root = new Control { Name = "TheUnwritten", MouseFilter = Control.MouseFilterEnum.Ignore };
        try
        {
            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            ArchitectRoomBackgrounds.AddBackdrop(root, ArchitectRoomBackgrounds.RestBackgroundPath);
            var backdrop = root.GetNode<TextureRect>("ArchitectBackdrop");
            backdrop.Owner = root;
            backdrop.Modulate = new Color(0.28f, 0.36f, 0.43f);
            var character = new TextureRect
            {
                Name = "UnwrittenCharacter",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Texture = PreloadManager.Cache.GetTexture2D(TheUnwritten.CharacterTexturePath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                // Keep the Ancient artwork above the native relic offers.
                Position = new Vector2(0f, 50f),
                Size = new Vector2(600f, 700f)
            };
            root.AddChild(character);
            character.Owner = root;
            __result = new PackedScene();
            var error = __result.Pack(root);
            if (error != Error.Ok)
                throw new InvalidOperationException($"Cannot create The Unwritten's background: {error}.");
            return false;
        }
        finally
        {
            root.Free();
        }
    }
}
