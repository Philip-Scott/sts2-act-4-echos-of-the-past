using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Playtest;

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
internal static class ArchitectConsolePlaytest
{
    private static bool _started;

    private static void Postfix(NGame __instance, Task __result)
    {
        if (_started || !CommandLineHelper.HasArg("architect-console-smoke"))
            return;
        _started = true;
        TaskHelper.RunSafely(Smoke(__instance, __result));
    }

    private static async Task Smoke(NGame game, Task menuReady)
    {
        await menuReady;
        await Task.Delay(3000);
        var run = await game.StartNewSingleplayerRun(ModelDb.Character<Silent>(), false,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], "ARCHITECT-CONSOLE", GameMode.Standard);
        await Task.Delay(2000);
        MainFile.Logger.Info("Architect console smoke: normal startup ready.");
        var result = new DevConsole(shouldAllowDebugCommands: true).ProcessCommand("architect");
        if (!result.success || result.task == null)
            throw new InvalidOperationException($"Architect console command failed: {result.msg}");
        await result.task;
        if (run.Act is not ArchitectAct || run.Acts.Count != 4)
            throw new InvalidOperationException("Architect console command did not enter Act 4.");
        if (CommandLineHelper.HasArg("architect-repeat"))
            ArchitectRun.Get(run).EntrySnapshot = new CorruptedPlayerEnvelope
            {
                Revision = 1,
                Snapshot = CorruptedPlayerSnapshot.Capture(run.Players[0])
            };
        MainFile.Logger.Info("Architect console smoke: normal main menu -> new run -> architect command passed.");
        await ArchitectPlaytest.SmokeMapAndRooms(run.Players[0]);
    }
}
