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
        if (__instance is not TheUnwritten ancient)
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
            var sigil = new TextureRect
            {
                Name = "UnwrittenSigil",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Texture = PreloadManager.Cache.GetTexture2D(ancient.CustomMapIconOutlinePath),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Position = new Vector2(280f, 170f),
                Size = new Vector2(520f, 520f),
                Modulate = new Color(0.71f, 0.92f, 0.92f)
            };
            root.AddChild(sigil);
            sigil.Owner = root;
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
