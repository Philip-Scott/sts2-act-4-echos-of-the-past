using Godot;
using HarmonyLib;
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
        var root = new Control { Name = "TheWatcher", MouseFilter = Control.MouseFilterEnum.Ignore };
        try
        {
            root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            ArchitectRoomBackgrounds.AddBackdrop(root, TheUnwritten.BackgroundTexturePath);
            var backdrop = root.GetNode<TextureRect>("ArchitectBackdrop");
            backdrop.Owner = root;
            __result = new PackedScene();
            var error = __result.Pack(root);
            if (error != Error.Ok)
                throw new InvalidOperationException($"Cannot create The Watcher's background: {error}.");
            return false;
        }
        finally
        {
            root.Free();
        }
    }
}
