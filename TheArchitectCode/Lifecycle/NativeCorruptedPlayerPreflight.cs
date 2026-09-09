using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
internal static class NativeCorruptedPlayerPreflight
{
    private static bool Prefix(RunManager __instance, MapCoord coord, ref Task __result)
    {
        var run = __instance.DebugOnlyGetState();
        if (run?.Act is not ArchitectAct || coord != run.Map.BossMapPoint.coord ||
            ArchitectRun.Get(run).EntrySnapshot?.Snapshot is not { } snapshot ||
            NativeCardSupport.Preflight(snapshot.Deck) is not { } error)
            return true;
        Show(error);
        NMapScreen.Instance?.SetTravelEnabled(true);
        __result = Task.CompletedTask;
        return false;
    }

    internal static void Show(string error)
    {
        MainFile.Logger.Error(error + " Original Corrupted Player snapshot preserved.");
        var popup = NErrorPopup.Create("Corrupted Player cannot start", error + "\n\nYour saved Corrupted Player has not been changed.", false);
        if (popup != null)
            (NModalContainer.Instance ?? throw new InvalidOperationException("The game's error popup container is unavailable.")).Add(popup);
    }
}
