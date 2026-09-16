using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Ancients;

namespace TheArchitect.TheArchitectCode.Audio;

[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class WatcherRunAudio
{
    private static void Postfix(NRun __instance) => WatcherAudio.Attach(__instance);
}

[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom._Ready))]
internal static class WatcherAncientAudio
{
    private static void Postfix(EventModel ____event)
    {
        if (____event is TheUnwritten)
            WatcherAudio.Instance?.AncientShown(____event);
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.Reset))]
internal static class WatcherCombatAudioReset
{
    private static void Prefix() => WatcherAudio.Instance?.StopStances();
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapPointInternal))]
internal static class WatcherAncientAudioNextFloor
{
    private static void Prefix() => WatcherAudio.Instance?.StopAncient();
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterAct))]
internal static class WatcherAncientAudioNextAct
{
    private static void Prefix() => WatcherAudio.Instance?.StopAncient();
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class WatcherRunAudioCleanup
{
    private static void Prefix() => WatcherAudio.Instance?.Close();
}
