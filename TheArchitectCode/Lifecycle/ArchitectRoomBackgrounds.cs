using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectRoomBackgrounds
{
    internal const string RestBackgroundPath =
        "res://TheArchitect/images/backgrounds/architect_approach/tower.png";
    internal const string ShopBackgroundPath = RestBackgroundPath;

    internal static bool Applies(IRunState? run) => run is { Act: ArchitectAct };

    internal static void AddBackdrop(Control parent, string texturePath)
    {
        var background = new TextureRect
        {
            Name = "ArchitectBackdrop",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = PreloadManager.Cache.GetTexture2D(texturePath),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
        };
        parent.AddChildSafely(background);
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        parent.MoveChildSafely(background, 0);
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._Ready))]
internal static class ArchitectRestBackgroundPatch
{
    private static void Postfix(NRestSiteRoom __instance, IRunState ____runState)
    {
        if (!ArchitectRoomBackgrounds.Applies(____runState))
            return;

        // _Ready inserts the native rest background before the character containers.
        var scenery = __instance.GetNode<Control>("BgContainer").GetChild<Control>(0);
        ArchitectRoomBackgrounds.AddBackdrop(__instance, ArchitectRoomBackgrounds.RestBackgroundPath);
        foreach (var path in new[]
                 {
                     "RestSiteBG", "RestSiteForeground", "RestSiteForeground2",
                     "RestSiteForegroundDither2", "overlay_vfx",
                     "RestSiteLighting/WallLight1", "RestSiteLighting/WallLight2",
                     "RestSiteLighting/WallLight3", "RestSiteLighting/WallLight4"
                 })
            scenery.GetNode<CanvasItem>(path).Hide();
        // Leave logs, fire, %RestSiteLighting and character containers intact for native rest VFX.
    }
}

[HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom._Ready))]
internal static class ArchitectMerchantBackgroundPatch
{
    private static void Postfix(NMerchantRoom __instance)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (!ArchitectRoomBackgrounds.Applies(run))
            return;

        var scene = __instance.GetNode<Control>("SceneContainer");
        ArchitectRoomBackgrounds.AddBackdrop(scene, ArchitectRoomBackgrounds.ShopBackgroundPath);
        foreach (var path in new[] { "BgContainer", "light_small5", "light_small6", "stars" })
            scene.GetNode<CanvasItem>(path).Hide();
        // MerchantButton, CharacterContainer and Inventory are independent of the scenery.
    }
}
