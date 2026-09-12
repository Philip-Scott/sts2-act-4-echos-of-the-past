using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.addons.mega_text;
using TheArchitect.TheArchitectCode.Ancients;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(NAncientNameBanner), "AnimateVfx")]
internal static class UnwrittenNameBanner
{
    internal const int TitleFontSize = 36;

    [HarmonyAfter("BaseLib")]
    private static void Postfix(NAncientNameBanner __instance, AncientEventModel ____ancient, ref Task __result)
    {
        if (____ancient is TheUnwritten)
            __result = CompactAfterSettled(__result, __instance);
    }

    private static async Task CompactAfterSettled(Task animation, NAncientNameBanner banner)
    {
        // Native animation sets the final font size; BaseLib then adds the source label.
        await animation;
        if (!GodotObject.IsInstanceValid(banner) || !banner.IsInsideTree())
            return;
        banner.GetNode<MegaRichTextLabel>("%Title").AddThemeFontSizeOverride("normal_font_size", TitleFontSize);
        banner.GetNode<MegaLabel>("%Epithet").GetNodeOrNull<Control>("BaseLibModSourceLabel")?.Hide();
    }
}
