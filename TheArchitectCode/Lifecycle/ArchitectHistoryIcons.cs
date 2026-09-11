using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectHistoryIcons
{
    internal static string? Resolve(ModelId? modelId, bool outline)
    {
        var icon = modelId switch
        {
            { Category: "EVENT", Entry: "THEARCHITECT-THE_UNWRITTEN" } => "the_unwritten",
            { Category: "ENCOUNTER", Entry: "THEARCHITECT-ARCHITECT_ENCOUNTER" } => "architect_boss",
            _ => null
        };
        return icon == null ? null : $"res://TheArchitect/images/map/{icon}{(outline ? "_outline" : "")}.png";
    }
}

// Resolve before BaseLib's model lookup and the native beta's filename fallback.
[HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconPath))]
internal static class ArchitectHistoryIconPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ModelId? modelId, ref string? __result)
    {
        if (ArchitectHistoryIcons.Resolve(modelId, outline: false) is not { } path)
            return true;
        __result = path;
        return false;
    }
}

[HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconOutlinePath))]
internal static class ArchitectHistoryIconOutlinePatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ModelId? modelId, ref string? __result)
    {
        if (ArchitectHistoryIcons.Resolve(modelId, outline: true) is not { } path)
            return true;
        __result = path;
        return false;
    }
}
