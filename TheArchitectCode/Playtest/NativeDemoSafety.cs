using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using TheArchitect.TheArchitectCode.Challenger;
using Environment = System.Environment;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeDemoSafety
{
    internal static bool Enabled => CommandLineHelper.HasArg("architect-native-test");
    internal static bool SharedVisible => Enabled && CommandLineHelper.HasArg("architect-shared-visible");
    internal static string RuntimePath { get; private set; } = "";

    internal static void Initialize()
    {
        if (!Enabled)
            return;
        RuntimePath = Environment.GetEnvironmentVariable("ARCHITECT_NATIVE_RUNTIME")
            ?? throw new InvalidOperationException("Use the isolated native-demo launcher.");
        if (SteamInitializer.Initialized || CommandLineHelper.GetValue("force-steam") != "off" ||
            !System.IO.Path.IsPathFullyQualified(RuntimePath) ||
            !OS.GetUserDataDir().StartsWith(RuntimePath + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("Native demo requires Steam OFF and disposable user data.");
        // Startup only, before any run exists. Do not use this to swap an active game's SaveManager.
        var memory = new MockGodotFileIo("user://native-demo-memory");
        var saves = new SaveManager(memory, memory);
        SaveManager.MockInstanceForTesting(saves);
        saves.InitSettingsData();
        saves.SettingsSave.Fullscreen = false;
        saves.SettingsSave.WindowSize = new Vector2I(1280, 720);
        saves.SettingsSave.SkipIntroLogo = true;
        saves.SettingsSave.SeenEaDisclaimer = true;
        saves.SettingsSave.ModSettings = new ModSettings { PlayerAgreedToModLoading = true };
        if (SharedVisible)
            saves.SettingsSave.LimitFpsInBackground = false;
        MainFile.Logger.Info($"INPROCESS ISOLATION steam=false stores=memory userData={OS.GetUserDataDir()} pid={Environment.ProcessId}");
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SeenFtue))]
internal static class NativeDemoTutorialPatch
{
    private static void Postfix(ref bool __result)
    {
        if (NativeDemoSafety.Enabled)
            __result = true;
    }
}
