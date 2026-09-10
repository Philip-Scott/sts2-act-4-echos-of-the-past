using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativePartyPlaytest
{
    internal static async Task Run(NGame game, bool layoutOnly = false)
    {
        Require(NativeDemoSafety.Enabled, "party probes require disposable storage and Steam disabled");
        foreach (var (count, ascension) in layoutOnly ? new[] { (4, 8) } : [(2, 0), (3, 8), (4, 8)])
            await Exercise(game, count, ascension, layoutOnly);
        MainFile.Logger.Info(layoutOnly ? "NATIVE PARTY LAYOUT PASSED" :
            "NATIVE PARTY PASSED: 2-4 humans and saved enemies; fixed HP, independent turns, sequential/AoE handoff.");
        game.GetTree().Quit();
    }

    private static async Task Exercise(NGame game, int count, int ascension, bool layoutOnly)
    {
        CharacterModel[] characters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Defect>(),
            ModelDb.Character<Necrobinder>(), ModelDb.Character<Silent>()];
        var players = Enumerable.Range(0, count).Select(index =>
            Player.CreateForNewRun(characters[index], UnlockState.all, (ulong)index + 1)).ToArray();
        var snapshots = players.Select(CorruptedPlayerSnapshot.Capture).ToArray();
        foreach (var player in players)
        {
            player.Creature.SetMaxHpInternal(1000);
            player.Creature.SetCurrentHpInternal(1000);
        }
        var run = RunState.CreateForNewRun(players,
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, ascension, $"ARCHITECT-PARTY-{count}-{ascension}");
        var manager = RunManager.Instance;
        manager.SetUpTest(run, new NetSingleplayerGameService(), disableCombatStateSync: true, shouldSave: false);
        manager.GenerateRooms();
        ArchitectLifecycle.AppendAct(run);
        var participants = players.Select((player, index) =>
            new SuccessorParticipant(player.NetId, Guid.Parse((index + 1).ToString("x32")))).ToArray();
        var origin = new FrozenCorruptedParty
        {
            RunId = $"native-party-{count}-{ascension}", HostNetId = 1,
            HostProfileUuid = participants[0].ProfileUuid, Participants = participants,
            GroupKey = SuccessorGroup.Key(participants.Select(participant => participant.ProfileUuid)),
            Lineage = null
        };
        var frozen = origin with
        {
            Lineage = new CorruptedPartyEnvelope
            {
                HostProfileUuid = origin.HostProfileUuid, GroupKey = origin.GroupKey, Revision = 1,
                TerminalRunId = "prior-" + origin.RunId, Outcome = "ArchitectWin",
                Members = participants.Select((participant, index) =>
                    new CorruptedPartyMember(participant.ProfileUuid, snapshots[index])).ToArray()
            }
        };
        frozen.Validate(players.Select(player => player.NetId).ToArray(), 1);
        var state = ArchitectRun.Get(run);
        state.BindOrigin(origin);
        state.EnterParty(frozen);
        await PreloadManager.LoadRunAssets(characters.Take(count));
        manager.Launch();
        game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
        await manager.EnterAct(3, doTransition: false);
        await manager.EnterMapCoord(run.Map.BossMapPoint.coord);
        await Ready(players, 1);
        var combat = players[0].Creature.CombatState!;
        var bossProbe = new Creature(ArchitectModels.Boss.ToMutable(), CombatSide.Enemy, null);
        bossProbe.ScaleMonsterHpForMultiplayer(combat.Encounter, count, 3);
        Require(bossProbe.MaxHp == (int)Creature.ScaleHpForMultiplayer(
                bossProbe.Monster!.MaxInitialHp, combat.Encounter, count, 2),
            $"{count}: native boss HP initialization supports Act 4");
        var enemies = combat.Enemies.Where(creature => creature.Monster is CorruptedPlayer).ToArray();
        var actors = enemies.Select(creature => ((CorruptedPlayer)creature.Monster!).Native!).ToArray();
        Require(enemies.Length == count && combat.Players.Count == count && run.Players.Count == count,
            $"{count}: complete party loaded without enrolling private actors");
        Require(actors.Select(actor => actor.Player.NetId).Distinct().Count() == count,
            $"{count}: all private actors have different identities");
        for (var index = 0; index < count; index++)
        {
            actors[index].AssertIdentity();
            var expected = CorruptedPlayerHealth.CalculateMaxHp(snapshots[index].MaxHp, ascension);
            Require(enemies[index].MaxHp == expected, $"{count}: member {index} has exactly {expected} HP");
            enemies[index].ScaleMonsterHpForMultiplayer(combat.Encounter, count, 3);
            Require(enemies[index].MaxHp == expected, $"{count}: native multiplayer cannot add HP scaling");
        }
        await NativeDemoPlaytest.Capture($"party-{count}-opening");
        var panels = enemies.Select(enemy => enemy.GetCreatureNode()!
            .GetNode<CorruptedPlayerTelegraph>("CorruptedPlayerTelegraph")).ToArray();
        var bounds = panels.Select(panel => panel.GetGlobalTransform() * new Rect2(Vector2.Zero, panel.Size)).ToArray();
        Require(bounds.All(rect => game.GetViewport().GetVisibleRect().Encloses(rect)),
            $"{count}: all party hand previews remain on screen");
        Require(bounds.All(rect => rect.Position.Y >= 99f), $"{count}: previews stay below the top toolbar");
        for (var index = 0; index < bounds.Length; index++)
            Require(bounds.Skip(index + 1).All(other => !bounds[index].Intersects(other)),
                $"{count}: member {index} preview does not overlap another member");
        if (layoutOnly)
        {
            await game.ReturnToMainMenu();
            return;
        }

        await EndTurn(players, 2);
        Require(actors.All(actor => actor.CompletedTurns == 1), $"{count}: every saved member plays one native turn");
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<AmbergrisPower>(context, enemies[0], 1, enemies[0], null);
        await EndTurn(players, 3);
        Require(actors[0].CompletedTurns == 3 && actors.Skip(1).All(actor => actor.CompletedTurns == 2),
            $"{count}: extra turns do not advance other corrupted players");

        var hand = players[0].PlayerCombatState!.Hand.Cards.ToArray();
        var energy = players[0].PlayerCombatState!.Energy;
        var hp = players.Select(player => player.Creature.CurrentHp).ToArray();
        await CreatureCmd.Kill(enemies[0], force: true);
        Require(actors[0].Cleaned && actors.Skip(1).All(actor => !actor.Cleaned),
            $"{count}: an early death cleans only its own actor");
        Require(!HasEnergyCounter(panels[0]) && panels.Skip(1).All(HasEnergyCounter),
            $"{count}: an early death detaches only its own native energy counter");
        Require(!combat.Enemies.Any(creature => creature.Monster is ArchitectBoss),
            $"{count}: Architect is absent while any corrupted member remains");
        await CreatureCmd.Damage(context, enemies.Skip(1), 10000, ValueProp.Unpowered | ValueProp.Unblockable,
            players[0].Creature);
        Require(combat.Enemies.Count(creature => creature.Monster is ArchitectBoss) == 1 &&
            !combat.Enemies.Any(creature => creature.Monster is CorruptedPlayer) && actors.All(actor => actor.Cleaned),
            $"{count}: the final AoE produces exactly one Architect and cleans every actor");
        Require(panels.All(panel => !HasEnergyCounter(panel)),
            $"{count}: the final AoE detaches all defeated native energy counters");
        var boss = combat.Enemies.Single(creature => creature.Monster is ArchitectBoss);
        Require(boss.MaxHp == (int)Creature.ScaleHpForMultiplayer(boss.Monster!.MaxInitialHp, combat.Encounter, count, 2),
            $"{count}: Architect uses the native final-act boss HP tier");
        Require(players[0].PlayerCombatState!.Hand.Cards.SequenceEqual(hand) &&
            players[0].PlayerCombatState!.Energy == energy && players.Select(player => player.Creature.CurrentHp).SequenceEqual(hp),
            $"{count}: handoff preserves human hands, energy and HP");
        await NativeDemoPlaytest.Capture($"party-{count}-handoff");
        var limit = (int)Creature.ScaleHpForMultiplayer(ascension >= 8 ? 200 : 300,
            combat.Encounter, count, 2);
        var invincible = boss.GetPower<ArchitectInvinciblePower>()!;
        Require(invincible.Amount == limit && invincible.DisplayAmount == limit &&
            invincible.DynamicVars["Remaining"].IntValue == limit,
            $"{count}: Architect damage cap uses the same native final-act boss scaling as HP ({limit})");
        var bossHp = boss.CurrentHp;
        foreach (var player in players)
            await CreatureCmd.Damage(context, boss, limit - 1, ValueProp.Unpowered | ValueProp.Unblockable,
                player.Creature);
        Require(boss.CurrentHp == bossHp - limit && invincible.DisplayAmount == 0 &&
            invincible.DynamicVars["Remaining"].IntValue == 0 && boss.HpDisplay == HpDisplay.InfiniteWithNumbers,
            $"{count}: human players share one scaled damage budget");
        await EndTurn(players, 4);
        Require(invincible.Amount == limit && invincible.DisplayAmount == limit &&
            invincible.DynamicVars["Remaining"].IntValue == limit && boss.HpDisplay == HpDisplay.Normal,
            $"{count}: the next Architect turn restores the scaled budget without scaling it again");
        await game.ReturnToMainMenu();
    }

    private static bool HasEnergyCounter(CorruptedPlayerTelegraph panel) =>
        GodotObject.IsInstanceValid(panel) &&
        panel.FindChild("Energy", true, false).GetChildren().OfType<NEnergyCounter>().Any();

    private static async Task EndTurn(Player[] players, int nextTurn)
    {
        foreach (var player in players)
            PlayerCmd.EndTurn(player, false);
        await Ready(players, nextTurn);
    }

    private static async Task Ready(Player[] players, int turn)
    {
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (players.Any(player => player.PlayerCombatState is not { Phase: PlayerTurnPhase.Play } state ||
                   state.TurnNumber != turn || !CombatManager.Instance.IsPartOfPlayerTurn(player)) ||
               CombatManager.Instance.IsStarting ||
               NativeCorruptedPlayer.In(players[0].Creature.CombatState!).Any(actor => !actor.HandPrepared))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Native party smoke timed out waiting for a complete player turn.");
            await NGame.Instance!.AwaitProcessFrame();
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("NATIVE PARTY ASSERTION FAILED: " + description);
        MainFile.Logger.Info("NATIVE PARTY ASSERT: " + description);
    }
}
