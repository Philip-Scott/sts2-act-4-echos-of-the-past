using System.IO;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.UI;

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
                await AssertHandoff(game, player, actor, reactiveDamage: false);
                await game.ReturnToMainMenu();
                run = RunState.FromSerializable(save);
                await RunManager.Instance.SetUpSavedSingleplayer(run, save);
                await game.LoadRun(run, save.PreFinishedRoom);
            }
            else
            {
                await AssertHandoff(game, player, actor, reactiveDamage: true);
            }
        }
        MainFile.Logger.Info("SAVED RUN SMOKE PASSED: preview, power play, reload, direct and reactive lethal handoffs.");
        game.GetTree().Quit();
    }

    private static async Task AssertHandoff(NGame game, Player player, NativeCorruptedPlayer actor, bool reactiveDamage)
    {
        var combat = player.Creature.CombatState!;
        var context = new ThrowingPlayerChoiceContext();
        var telegraph = actor.Body.GetCreatureNode()!.GetNode<CorruptedPlayerTelegraph>("CorruptedPlayerTelegraph");
        var counter = telegraph.FindChild("Energy", true, false).GetChildren().OfType<NEnergyCounter>().Single();
        if (reactiveDamage)
        {
            player.PlayerCombatState!.Energy = 99;
            await NativeDemoPlaytest.HumanPlay<SleightOfFlesh>(player, null);
            await NativeDemoPlaytest.HumanPlay<SleightOfFlesh>(player, null);
            await NativeDemoPlaytest.HumanPlay<Defy>(player, actor.Body);
            var damage = player.Creature.GetPowerAmount<SleightOfFleshPower>();
            if (damage <= 0 || actor.Body.IsDead)
                throw new InvalidOperationException("Reactive lethal handoff setup did not produce a live target and damage.");
            actor.Body.SetCurrentHpInternal(Math.Min(actor.Body.MaxHp, damage));
            await NativeDemoPlaytest.HumanPlay<EnfeeblingTouch>(player, actor.Body);
        }
        else
        {
            await CreatureCmd.Damage(context, actor.Body, actor.Body.CurrentHp,
                ValueProp.Unblockable | ValueProp.Unpowered, player.Creature);
        }
        if (!actor.Cleaned || combat.Enemies.Count(creature => creature.Monster is ArchitectBoss) != 1 ||
            (GodotObject.IsInstanceValid(counter) && counter.IsInsideTree()))
            throw new InvalidOperationException("Lethal handoff must detach the native energy counter and create one Architect.");
        if (GodotObject.IsInstanceValid(telegraph))
        {
            actor.Show(telegraph);
            if (telegraph.Visible || telegraph.FindChild("Energy", true, false).GetChildren().OfType<NEnergyCounter>().Any())
                throw new InvalidOperationException("A stopped hand preview must not recreate its native energy counter.");
        }
        actor.State.Energy++;
        await PowerCmd.Apply<StrengthPower>(context, player.Creature, 1, player.Creature, null);
        await game.AwaitProcessFrame();
        await game.AwaitProcessFrame();
        await NativeDemoPlaytest.Capture(reactiveDamage ? "handoff-reactive" : "handoff-direct");
        MainFile.Logger.Info($"SAVED RUN HANDOFF PASS: reactive={reactiveDamage}, counter detached, later combat updates survived.");
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
