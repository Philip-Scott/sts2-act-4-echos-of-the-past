using System.Runtime.CompilerServices;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class WatcherPowerIconPlaytest
{
    internal static void CheckFallbacks()
    {
        Check(NativeDemoSafety.Enabled, "Power icon probes require disposable native-test storage.");
        var unrelated = "res://images/atlases/power_atlas.sprites/strength_power.tres";
        var native = PreloadManager.Cache.GetTexture2D(unrelated);
        var aliases = new List<ulong>();
        foreach (var stance in new[] { "wrath", "calm" })
            aliases.Add(CheckDirectFallback(stance));
        WatcherStanceIcons.Register();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (var i = 0; i < aliases.Count; i++)
        {
            var stance = i == 0 ? "wrath" : "calm";
            var path = $"res://images/atlases/power_atlas.sprites/thearchitect-{stance}_stance_power.tres";
            Check(ResourceLoader.Load<Texture2D>(path).GetInstanceId() == aliases[i],
                $"{stance}: alias survives collection and repeat registration");
            CheckPixels(PreloadManager.Cache.GetTexture2D(path), stance, "retained native fallback");
        }
        Check(PreloadManager.Cache.GetTexture2D(unrelated) == native, "Vanilla Strength resource is unchanged");
    }

    // Keep the probe's own resource references out of the GC-retention check.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong CheckDirectFallback(string stance)
    {
        var path = $"res://images/atlases/power_atlas.sprites/thearchitect-{stance}_stance_power.tres";
        Check(ResourceLoader.Exists(path), $"{stance}: native fallback exists in the resource cache");
        var alias = ResourceLoader.Load<Texture2D>(path);
        CheckPixels(alias, stance, "direct native fallback");
        return alias.GetInstanceId();
    }

    internal static void CheckPower(CustomPowerModel power, Texture2D? tipIcon)
    {
        var stance = power switch
        {
            WrathStancePower => "wrath",
            CalmStancePower => "calm",
            _ => throw new ArgumentException("Only Watcher stance powers are supported.", nameof(power))
        };
        CheckPixels(PreloadManager.Cache.GetTexture2D(power.PackedIconPath), stance, "power packed icon");
        CheckPixels(tipIcon, stance, "native power hover-tip icon");
        var big = PreloadManager.Cache.GetTexture2D(power.CustomBigIconPath ??
            throw new InvalidOperationException("Watcher stance inspection artwork is not configured."));
        CheckPixels(big, stance, "power inspection icon", 256);
    }

    internal static void CheckPixels(Texture2D? actual, string stance, string surface, int size = 64)
    {
        var path = $"res://TheArchitect/images/enchantments/{(size == 256 ? "big/" : "")}{stance}.png";
        var expected = PreloadManager.Cache.GetTexture2D(path);
        Check(actual != null && actual.GetSize() == new Vector2(size, size), $"{stance}: {surface} dimensions");
        using var actualImage = actual!.GetImage();
        using var expectedImage = expected.GetImage();
        actualImage.Convert(Image.Format.Rgba8);
        expectedImage.Convert(Image.Format.Rgba8);
        Check(actualImage.GetData().SequenceEqual(expectedImage.GetData()),
            $"{stance}: {surface} pixels match original art (not the missing-resource placeholder)");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Watcher power icons: " + message);
        MainFile.Logger.Info("WATCHER POWER ICON PASS: " + message);
    }
}
