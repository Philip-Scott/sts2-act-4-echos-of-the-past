using System.IO;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Monsters;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeSavedRunPlaytest
{
    internal static void PrepareIdentityProbe() => RuntimeHelpers.PrepareMethod(
        AccessTools.Method(typeof(NativeSavedRunPlaytest), nameof(UnbridgedOwner)).MethodHandle);

    internal static async Task Run(NGame game)
    {
        var path = Path.Combine(NativeDemoSafety.RuntimePath, "run-input.json");
        var save = SaveManager.FromJson<SerializableRun>(File.ReadAllText(path)).SaveData ??
            throw new InvalidDataException("Cannot restore the supplied disposable run.");
        var run = RunState.FromSerializable(save);
        await RunManager.Instance.SetUpSavedSingleplayer(run, save);
        await game.LoadRun(run, save.PreFinishedRoom);
        for (var load = 0; load < 2; load++)
        {
            var player = run.Players.Single();
            await NativeDemoPlaytest.PlayerTurn(player, 1);
            var actor = ((CorruptedPlayer)player.Creature.CombatState!.Enemies
                .Single(creature => creature.Monster is CorruptedPlayer).Monster!).Native!;
            MainFile.Logger.Info($"SAVED RUN identity: precompiledGetterMatches={UnbridgedOwner(actor.Body) == actor.Player}, " +
                $"explicitBridgeMatches={NativeCombatCallSites.CreatureOwner(actor.Body) == actor.Player}.");
            for (var iteration = 0; iteration < 100_000; iteration++)
                AssertOptimizedIdentity(actor);
            actor.AssertIdentity();
            if (!actor.HandPrepared || actor.State.Hand.Cards.Count == 0)
                throw new InvalidOperationException("Saved opponent did not prepare its preview hand.");
            var previews = game.FindChildren("CorruptedPlayerCard", "", true, false);
            if (previews.Count == 0 || !previews.Any(node => node.GetChildren().OfType<NCard>().Any()))
                throw new InvalidOperationException("Saved opponent's native card previews were not created.");
            if (player.PlayerCombatState!.Hand.Cards.Count == 0)
                throw new InvalidOperationException("Human cards were not drawn after loading the saved fight.");
            await NativeDemoPlaytest.Capture($"saved-run-{load}");
            await NativeDemoPlaytest.HumanPlay<Arsenal>(player, null);
            if (player.Creature.GetPowerAmount<ArsenalPower>() <= 0)
                throw new InvalidOperationException("The queued human power card did not apply its effect.");
            MainFile.Logger.Info($"SAVED RUN PASS: load={load}, opponentHand={actor.State.Hand.Cards.Count}, " +
                $"humanHand={player.PlayerCombatState.Hand.Cards.Count}, native power play completed.");
            if (load == 0)
            {
                await SaveManager.Instance.SaveRun(null);
                save = SaveManager.Instance.LoadRunSave().SaveData ??
                    throw new InvalidDataException("Disposable saved-fight roundtrip is missing.");
                await game.ReturnToMainMenu();
                run = RunState.FromSerializable(save);
                await RunManager.Instance.SetUpSavedSingleplayer(run, save);
                await game.LoadRun(run, save.PreFinishedRoom);
            }
        }
        MainFile.Logger.Info("SAVED RUN SMOKE PASSED: opponent preview, human cards, power play, and reload.");
        game.GetTree().Quit();
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static Player? UnbridgedOwner(Creature body) => body.Player;

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static void AssertOptimizedIdentity(NativeCorruptedPlayer actor)
    {
        actor.AssertIdentity();
        if (NativeCombatCallSites.CreatureOwner(actor.Body) != actor.Player ||
            NativeCombatCallSites.IsPartyPlayer(actor.Body))
            throw new InvalidOperationException("Optimized Corrupted Player identity reads bypassed the actor bridge.");
    }
}
