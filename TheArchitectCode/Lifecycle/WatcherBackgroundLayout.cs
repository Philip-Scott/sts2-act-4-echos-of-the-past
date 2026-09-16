using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens;
using TheArchitect.TheArchitectCode.Ancients;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(NAncientEventLayout), "InitializeVisuals")]
internal static class WatcherBackgroundLayout
{
    private const string FullBleed = "TheArchitectFullBleedBackground";
    private static readonly Action<NAncientBgContainer> RestoreNativeLayout =
        AccessTools.MethodDelegate<Action<NAncientBgContainer>>(
            AccessTools.Method(typeof(NAncientBgContainer), "OnWindowChange"));

    private static void Postfix(AncientEventModel ____ancientEvent, NAncientBgContainer ____ancientBgContainer) =>
        Configure(____ancientBgContainer, ____ancientEvent is TheUnwritten);

    internal static void Configure(NAncientBgContainer container, bool fullBleed)
    {
        if (fullBleed)
        {
            container.SetMeta(FullBleed, true);
            Fill(container);
        }
        else if (container.HasMeta(FullBleed))
        {
            container.RemoveMeta(FullBleed);
            RestoreNativeLayout(container);
        }
    }

    private static void Fill(NAncientBgContainer container)
    {
        // The native presets frame oversized character portraits, not a viewport-sized backdrop.
        container.Position = Vector2.Zero;
        container.Scale = Vector2.One;
    }

    [HarmonyPatch(typeof(NAncientBgContainer), "OnWindowChange")]
    private static class Resize
    {
        private static void Postfix(NAncientBgContainer __instance)
        {
            if (__instance.GetMeta(FullBleed, false).AsBool())
                Fill(__instance);
        }
    }
}
