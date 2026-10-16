using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Powers;

// PowerModel.Icon/BigIcon can inline the path getters BaseLib patches, so the
// combat row falls back to a missing power_atlas sprite. Load our paths directly.
internal static class PowerIconPatches
{
    internal static bool TryGetPaths(PowerModel power, out string? packed, out string? big)
    {
        packed = big = null;
        if (power is not CustomPowerModel custom || power.GetType().Assembly != typeof(PowerIconPatches).Assembly)
            return false;
        packed = custom.CustomPackedIconPath;
        big = custom.CustomBigIconPath;
        return true;
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Icon), MethodType.Getter)]
internal static class PowerIconPatch
{
    private static bool Prefix(PowerModel __instance, ref Texture2D __result)
    {
        if (!PowerIconPatches.TryGetPaths(__instance, out var packed, out _) || packed == null)
            return true;
        __result = ResourceLoader.Load<Texture2D>(packed, null, ResourceLoader.CacheMode.Reuse);
        return false;
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.BigIcon), MethodType.Getter)]
internal static class PowerBigIconPatch
{
    private static bool Prefix(PowerModel __instance, ref Texture2D __result)
    {
        if (!PowerIconPatches.TryGetPaths(__instance, out _, out var big) || big == null)
            return true;
        __result = PreloadManager.Cache.GetTexture2D(big);
        return false;
    }
}
