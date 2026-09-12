using Godot;
using System.Text.Json;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
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
    internal static async Task Run(NGame game, bool layoutOnly = false, int? partySize = null,
        bool prototypeOnly = false, bool manual = false)
    {
        Require(NativeDemoSafety.Enabled, "party probes require disposable storage and Steam disabled");
        if (manual)
        {
            Require(NativeDemoSafety.SharedVisible && partySize is >= 1 and <= 4 &&
                !layoutOnly && !prototypeOnly, "manual inspection requires one visible party size");
            await Exercise(game, partySize!.Value, partySize <= 2 ? 0 : 8, false, false, manual: true);
            return;
        }
        var cases = partySize is { } size ? new[] { (size, size == 2 ? 0 : 8) } :
            layoutOnly ? [(4, 8)] : [(2, 0), (3, 8), (4, 8)];
        foreach (var (count, ascension) in cases)
            await Exercise(game, count, ascension, layoutOnly, prototypeOnly);
        if (prototypeOnly)
        {
            MainFile.Logger.Info("NATIVE PARTY LAYOUT PROTOTYPE CAPTURED: A/B/C for 2-4 enemies; not a combat-suite result.");
            game.GetTree().Quit();
            return;
        }
        MainFile.Logger.Info(layoutOnly ? "NATIVE PARTY LAYOUT PASSED" :
            $"NATIVE PARTY PASSED: {(partySize?.ToString() ?? "2-4")} humans and saved enemies; counterpart names/targets, AOE, fixed HP, independent turns, sequential/AoE handoff.");
        game.GetTree().Quit();
    }

    private static async Task Exercise(NGame game, int count, int ascension, bool layoutOnly, bool prototypeOnly,
        bool manual = false)
    {
        CharacterModel[] characters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Defect>(),
            ModelDb.Character<Necrobinder>(), ModelDb.Character<Silent>()];
        var players = Enumerable.Range(0, count).Select(index =>
            Player.CreateForNewRun(characters[index], UnlockState.all, (ulong)index + 1)).ToArray();
        var snapshots = players.Select(player => manual || layoutOnly
            ? InspectionSnapshot(player)
            : CorruptedPlayerSnapshot.Capture(player)).ToArray();
        var run = RunState.CreateForNewRun(players,
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, ascension, $"ARCHITECT-PARTY-{count}-{ascension}");
        var manager = RunManager.Instance;
        manager.SetUpTest(run, new NetSingleplayerGameService(), disableCombatStateSync: true, shouldSave: false);
        manager.GenerateRooms();
        ArchitectLifecycle.AppendAct(run);
        var state = ArchitectRun.Get(run);
        if (count == 1)
        {
            state.EntrySnapshot = new CorruptedPlayerEnvelope { Revision = 1, Snapshot = snapshots[0] };
            state.Entered = true;
        }
        else
        {
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
                        new CorruptedPartyMember(participant.ProfileUuid, snapshots[index])).Reverse().ToArray()
                }
            };
            frozen.Validate(players.Select(player => player.NetId).ToArray(), 1);
            state.BindOrigin(origin);
            state.EnterParty(frozen);
        }
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
        if (manual)
        {
            Require(enemies.Length == count && combat.Players.Count == count && run.Players.Count == count,
                $"{count}: complete manual party loaded");
            await NativeDemoPlaytest.Capture($"party-{count}-manual-opening");
            MainFile.Logger.Info($"NATIVE PARTY MANUAL READY: {count} humans and {count} corrupted enemies; unsaved local simulation, no automated turns.");
            return;
        }
        if (prototypeOnly)
        {
            await CorruptedPartyLayoutPrototype.Capture(game, enemies);
            await game.ReturnToMainMenu();
            return;
        }
        Require(enemies.Length == count && combat.Players.Count == count && run.Players.Count == count,
            $"{count}: complete party loaded without enrolling private actors");
        Require(actors.Select(actor => actor.Player.NetId).Distinct().Count() == count,
            $"{count}: all private actors have different identities");
        for (var index = 0; index < count; index++)
        {
            actors[index].AssertIdentity();
            var expected = CorruptedPlayerHealth.CalculateMaxHp(snapshots[count - index - 1].MaxHp, ascension);
            Require(enemies[index].MaxHp == expected, $"{count}: member {index} has exactly {expected} HP");
            enemies[index].ScaleMonsterHpForMultiplayer(combat.Encounter, count, 3);
            Require(enemies[index].MaxHp == expected, $"{count}: native multiplayer cannot add HP scaling");
            var counterpart = players[count - index - 1];
            Require(((CorruptedPlayer)enemies[index].Monster!).FormationIndex == count - index - 1,
                $"{count}: reversed lineage retains host-first formation");
            Require(enemies[index].Monster!.Title.GetFormattedText() ==
                $"Corrupted {PlatformUtil.GetPlayerName(manager.NetService.Platform, counterpart.NetId)}",
                $"{count}: member {index} is named for its matched human, not its saved character");
            var strike = combat.CreateCard<StrikeIronclad>(actors[index].Player);
            var area = combat.CreateCard<DaggerSpray>(actors[index].Player);
            var random = combat.CreateCard<SwordBoomerang>(actors[index].Player);
            Require(actors[index].SelectTarget(strike) == counterpart.Creature,
                $"{count}: reordered member {index} targets its own human");
            Require(actors[index].SelectTarget(area) == null && actors[index].SelectTarget(random) == null &&
                players.All(player => actors[index].View.HittableEnemies.Contains(player.Creature)),
                $"{count}: AOE and random effects retain the complete opponent pool");
            var counterpartHp = counterpart.Creature.CurrentHp;
            try
            {
                counterpart.Creature.SetCurrentHpInternal(0);
                Require(actors[index].SelectTarget(strike) ==
                    players.First(player => player != counterpart).Creature,
                    $"{count}: dead counterpart falls back to a living human");
            }
            finally
            {
                counterpart.Creature.SetCurrentHpInternal(counterpartHp);
            }
            Require(actors[index].SelectTarget(strike) == counterpart.Creature,
                $"{count}: revived counterpart is preferred again");
            combat.RemoveCard(strike);
            combat.RemoveCard(area);
            combat.RemoveCard(random);
        }
        await NativeDemoPlaytest.Capture($"party-{count}-opening");
        var panels = enemies.Select(enemy => enemy.GetCreatureNode()!
            .GetNode<CorruptedPlayerTelegraph>("CorruptedPlayerTelegraph")).ToArray();
        var bindings = enemies.SelectMany(enemy => enemy.GetCreatureNode()!.Visuals
            .FindChildren("BoundEcho*", recursive: true, owned: false).OfType<CorruptedPlayerBinding>()).ToArray();
        Require(bindings.Length == count * 2, $"{count}: every body has two binding layers");
        var geometryBuilds = bindings.Select(binding => binding.GeometryBuildCount).ToArray();
        var effects = enemies.Select(enemy => enemy.GetCreatureNode()!.Visuals
            .GetNode<CorruptedPlayerCorruption>("BoundEcho")).ToArray();
        var compositorUpdates = effects.Select(effect => effect.GeometryUpdateCount).ToArray();
        var refreshes = panels.Select(panel => panel.ContentRefreshCount).ToArray();
        var layoutUpdates = panels.Select(panel => panel.LayoutUpdateCount).ToArray();
        var partyRebuilds = panels[0].PartyLayoutRebuildCount;
        for (var frame = 0; frame < 10; frame++)
            await game.AwaitProcessFrame();
        Require(bindings.Select(binding => binding.GeometryBuildCount).SequenceEqual(geometryBuilds),
            $"{count}: animated bindings retain cached geometry on idle frames");
        Require(panels.Select(panel => panel.ContentRefreshCount).SequenceEqual(refreshes),
            $"{count}: idle party HUDs do not refresh content");
        Require(effects.Select(effect => effect.GeometryUpdateCount).SequenceEqual(compositorUpdates),
            $"{count}: idle compositors do not resubmit geometry uniforms");
        Require(panels.Select(panel => panel.LayoutUpdateCount).SequenceEqual(layoutUpdates) &&
            panels[0].PartyLayoutRebuildCount == partyRebuilds,
            $"{count}: unchanged party positions do not rebuild shared or individual layouts");
        await CorruptionVisualPlaytest.AssertCachedUpdates(game, enemies[0].GetCreatureNode()!);
        var anchor = enemies[0].GetCreatureNode()!;
        var originalPosition = anchor.Position;
        var contentSize = game.GetWindow().ContentScaleSize;
        try
        {
            anchor.Position += new Vector2(12, 0);
            await game.AwaitProcessFrame();
            await game.AwaitProcessFrame();
            Require(panels[0].PartyLayoutRebuildCount > partyRebuilds &&
                panels[0].LayoutUpdateCount > layoutUpdates[0],
                $"{count}: moving an actor invalidates party spacing and panel placement");
            var layoutsBeforeResize = panels[0].LayoutUpdateCount;
            game.GetWindow().ContentScaleSize = contentSize + new Vector2I(80, 40);
            await game.AwaitProcessFrame();
            await game.AwaitProcessFrame();
            Require(panels[0].LayoutUpdateCount > layoutsBeforeResize,
                $"{count}: resizing the viewport invalidates cached panel placement");
            var layoutsBeforePanelResize = panels[0].LayoutUpdateCount;
            panels[0].Size += new Vector2(0, 10);
            await game.AwaitProcessFrame();
            await game.AwaitProcessFrame();
            Require(panels[0].LayoutUpdateCount > layoutsBeforePanelResize,
                $"{count}: deferred panel sizing invalidates cached placement");
        }
        finally
        {
            anchor.Position = originalPosition;
            game.GetWindow().ContentScaleSize = contentSize;
        }
        await game.AwaitProcessFrame();
        await game.AwaitProcessFrame();
        var bounds = panels.Select(panel => panel.GetGlobalTransform() * new Rect2(Vector2.Zero, panel.Size)).ToArray();
        Require(bounds.All(rect => game.GetViewport().GetVisibleRect().Encloses(rect)),
            $"{count}: all party hand previews remain on screen");
        Require(bounds.All(rect => rect.Position.Y >= 99f), $"{count}: previews stay below the top toolbar");
        for (var index = 0; index < bounds.Length; index++)
            Require(bounds.Skip(index + 1).All(other => !bounds[index].Intersects(other)),
                $"{count}: member {index} preview does not overlap another member");
        await VerifyHandHover(game, panels);
        await NativeEnemyResourcePlaytest.Verify(game, actors.Select(actor => actor.Player).ToArray(), panels);
        await NativeEnemyResourcePlaytest.VerifyFullRelicBar(game, players[0], panels);
        if (layoutOnly)
        {
            await game.ReturnToMainMenu();
            return;
        }

        var finishTargetingProbe = count == 2 ? PrepareTargetingProbe(players, actors) : null;
        await EndTurn(players, 2);
        finishTargetingProbe?.Invoke();
        Require(actors.All(actor => actor.CompletedTurns == 1), $"{count}: every saved member plays one native turn");
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<AmbergrisPower>(context, enemies[0], 1, enemies[0], null);
        await EndTurn(players, 3);
        Require(actors[0].CompletedTurns == 3 && actors.Skip(1).All(actor => actor.CompletedTurns == 2),
            $"{count}: extra turns do not advance other corrupted players");
        await NativeCombatOwnershipPlaytest.Party(players, actors);

        var hand = players[0].PlayerCombatState!.Hand.Cards.ToArray();
        var energy = players[0].PlayerCombatState!.Energy;
        var hp = players.Select(player => player.Creature.CurrentHp).ToArray();
        await CreatureCmd.Kill(enemies[0], force: true);
        Require(actors[0].Cleaned && actors.Skip(1).All(actor => !actor.Cleaned),
            $"{count}: an early death cleans only its own actor");
        Require(!HasEnergyCounter(panels[0]) && panels.Skip(1).All(HasEnergyCounter),
            $"{count}: an early death detaches only its own native energy counter");
        Require(!HasStarCounter(panels[0]) && panels.Skip(1).All(HasStarCounter),
            $"{count}: an early death detaches only its own native star counter");
        Require(!combat.Enemies.Any(creature => creature.Monster is ArchitectBoss),
            $"{count}: Architect is absent while any corrupted member remains");
        await CreatureCmd.Damage(context, enemies.Skip(1), 10000, ValueProp.Unpowered | ValueProp.Unblockable,
            players[0].Creature);
        Require(combat.Enemies.Count(creature => creature.Monster is ArchitectBoss) == 1 &&
            !combat.Enemies.Any(creature => creature.Monster is CorruptedPlayer) && actors.All(actor => actor.Cleaned),
            $"{count}: the final AoE produces exactly one Architect and cleans every actor");
        Require(panels.All(panel => !HasEnergyCounter(panel)),
            $"{count}: the final AoE detaches all defeated native energy counters");
        Require(panels.All(panel => !HasStarCounter(panel)),
            $"{count}: the final AoE detaches all defeated native star counters");
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

    private static CorruptedPlayerSnapshot InspectionSnapshot(Player player)
    {
        var original = CorruptedPlayerSnapshot.Capture(player);
        var pool = ModelDb.AllCards.Where(card => card.Pool == player.Character.CardPool &&
        card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
        .OrderBy(card => card.Id.Entry, StringComparer.Ordinal).ToArray();
        var cards = pool.Where(card => card.Type == CardType.Attack).Take(2)
        .Concat(pool.Where(card => card.Type == CardType.Skill).Take(2))
        .Concat(pool.Where(card => card.Type == CardType.Power).Take(1))
        .Select(card => card.ToMutable()).ToArray();
        Require(cards.Length == 5, $"{player.Character.Id}: inspection deck has five distinct non-basic cards");
        cards[0].UpgradeInternal();
        var deck = cards.Select(card => JsonSerializer.SerializeToElement(card.ToSerializable(),
        JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
        MainFile.Logger.Info($"MANUAL PARTY DECK {player.Character.Id}: {string.Join(", ", cards.Select(card => card.Id.Entry))}");
        return original with
        {
        Deck = deck,
        ContentHash = CorruptedPlayerSnapshot.Hash(original.CharacterId, original.MaxHp, deck)
        };
    }

    private static async Task VerifyHandHover(NGame game, CorruptedPlayerTelegraph[] panels)
    {
        var viewport = game.GetViewport();
        var previews = game.HoverTipsContainer ??
        throw new InvalidOperationException("The party hover probe requires the native preview container.");
        var disabled = viewport.GuiDisableInput;
        try
        {
        viewport.GuiDisableInput = false;
        for (var index = 0; index < panels.Length; index++)
        {
            var cards = panels[index].FindChildren("CorruptedPlayerCard", recursive: true, owned: false)
                .OfType<Button>().ToArray();
            Require(cards.Length > 0, $"{panels.Length}: member {index} has hoverable cards");
            foreach (var card in cards)
            {
                var model = card.GetChildren().OfType<NCard>().Single().Model;
                var rect = card.GetGlobalTransform() * new Rect2(Vector2.Zero, card.Size);
                foreach (var fraction in new[] { 0.2f, 0.5f, 0.8f })
                {
                    var point = rect.Position + rect.Size * new Vector2(0.5f, fraction);
                    viewport.PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                    await game.AwaitProcessFrame();
                    await game.AwaitProcessFrame();
                    var hovered = viewport.GuiGetHoveredControl();
                    Require(hovered == card || hovered != null && card.IsAncestorOf(hovered),
                        $"{panels.Length}: member {index} card hover at {point} reaches its face (actual: {hovered?.GetPath()})");
                    Require(previews.GetChildren().OfType<Control>().Any(node =>
                        node.IsVisibleInTree() && node.GetChildren().OfType<NCard>().Any(preview => preview.Model == model)),
                        $"{panels.Length}: member {index} mouse hover opens the full card");
                }
            }
            await NativeDemoPlaytest.Capture($"party-{panels.Length}-member-{index}-hover");
        }
        }
        finally
        {
        viewport.PushInput(new InputEventMouseMotion { Position = new Vector2(10, 700) }, true);
        viewport.GuiDisableInput = disabled;
        }
    }

    private static bool HasStarCounter(CorruptedPlayerTelegraph panel) =>
        GodotObject.IsInstanceValid(panel) &&
        panel.FindChild("Stars", true, false).GetChildren().OfType<NStarCounter>().Any();

    private static Action PrepareTargetingProbe(Player[] players, NativeCorruptedPlayer[] actors)
    {
        var combat = players[0].Creature.CombatState!;
        var completed = new List<Action>();
        var hp = Array.Empty<int>();
        foreach (var actor in actors)
        {
            foreach (var pile in actor.State.AllPiles)
                foreach (var card in pile.Cards.ToArray())
                {
                    pile.RemoveInternal(card);
                    combat.RemoveCard(card);
                }
            var strike = combat.CreateCard<StrikeIronclad>(actor.Player);
            var area = combat.CreateCard<DaggerSpray>(actor.Player);
            actor.State.Hand.AddInternal(strike);
            actor.State.Hand.AddInternal(area);
            var counterpart = players[actors.Length - Array.IndexOf(actors, actor) - 1];
            var played = 0;
            void BeforeTurn() => hp = players.Select(player => player.Creature.CurrentHp).ToArray();
            void AfterCard(CardModel card)
            {
                Require(card == strike || card == area, "targeting probe plays only its prepared cards");
                for (var index = 0; index < players.Length; index++)
                {
                    var expected = card == area ? 8 : players[index] == counterpart ? 6 : 0;
                    Require(hp[index] - players[index].Creature.CurrentHp == expected,
                        $"{card.Id.Entry}: player {players[index].NetId} receives exactly {expected} damage");
                }
                hp = players.Select(player => player.Creature.CurrentHp).ToArray();
                played++;
            }
            actor.TurnStarting += BeforeTurn;
            actor.CardPlayed += AfterCard;
            completed.Add(() =>
            {
                actor.TurnStarting -= BeforeTurn;
                actor.CardPlayed -= AfterCard;
                Require(played == 2, "each actor executes both its targeted attack and its AOE attack");
            });
        }
        return () =>
        {
            foreach (var finish in completed)
                finish();
        };
    }

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
