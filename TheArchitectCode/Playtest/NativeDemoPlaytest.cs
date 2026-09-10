using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
internal static class NativeDemoPlaytest
{
    private static bool _started;
    private static void Postfix(NGame __instance, Task __result)
    {
        if (_started || !NativeDemoSafety.Enabled)
            return;
        _started = true;
        TaskHelper.RunSafely(RunSmoke(__instance, __result));
    }

    private static async Task RunSmoke(NGame game, Task menuReady)
    {
        try
        {
            await Demonstrate(game, menuReady);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Error($"NATIVE SMOKE FAILED: {exception}");
            game.GetTree().Quit(1);
            throw;
        }
    }

    private static async Task Demonstrate(NGame game, Task menuReady)
    {
        await menuReady;
        if (NativeDemoSafety.SharedVisible)
        {
            game.GetWindow().Unfocusable = true;
            game.GetViewport().GuiDisableInput = true;
            game.AddChild(new NativeDemoObserver());
        }
        await Task.Delay(2000);
        if (CommandLineHelper.HasArg("architect-native-ending"))
        {
            await ArchitectEndingPlaytest.Run(game);
            return;
        }
        if (CommandLineHelper.HasArg("architect-native-deck-preview"))
        {
            await DeckPreviewPlaytest.Run(game);
            return;
        }
        if (CommandLineHelper.HasArg("architect-native-party") || CommandLineHelper.HasArg("architect-native-party-layout"))
        {
            await NativePartyPlaytest.Run(game, CommandLineHelper.HasArg("architect-native-party-layout"));
            return;
        }
        if (CommandLineHelper.HasArg("architect-native-relic-art"))
        {
            await UnwrittenPlaytest.RenderRelicArt(game);
            return;
        }
        if (CommandLineHelper.HasArg("architect-native-saved-run"))
        {
            await NativeSavedRunPlaytest.Run(game);
            return;
        }
        if (CommandLineHelper.HasArg("architect-native-ancient"))
        {
            await UnwrittenPlaytest.Run(game);
            return;
        }
        if (CommandLineHelper.HasArg("architect-corruption-visuals"))
        {
            await CorruptionVisualPlaytest.Run(game);
            return;
        }
        game.GetWindow().Title = $"The Architect - NATIVE {Path.GetFileName(NativeDemoSafety.RuntimePath)}";
        var run = await game.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], "ARCHITECT-NATIVE-INTEGRATION", GameMode.Standard);
        Require(RunManager.Instance.ShouldSave, "disposable native save path enabled");
        var input = Path.Combine(NativeDemoSafety.RuntimePath, "snapshot-input.json");
        var destination = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath("TheArchitect/corrupted_player_snapshot.json"));
        Require(destination.StartsWith(NativeDemoSafety.RuntimePath + "/", StringComparison.Ordinal),
            "snapshot destination is disposable");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (CommandLineHelper.HasArg("architect-native-attack-vfx"))
            NativeAttackVfxPlaytest.PrepareSnapshot();
        else
            File.Copy(input, destination, overwrite: true);
        if (CommandLineHelper.HasArg("architect-native-nondefect"))
        {
            CardModel[] probe = [ModelDb.Card<Zap>().ToMutable(), ModelDb.Card<Coolheaded>().ToMutable(),
                ModelDb.Card<Defragment>().ToMutable(), ModelDb.Card<Capacitor>().ToMutable(), ModelDb.Card<Dualcast>().ToMutable()];
            var cards = probe.Select(c => JsonSerializer.SerializeToElement(c.ToSerializable(),
                JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
            var character = ModelDb.Character<Ironclad>().Id.ToString();
            CorruptedPlayerStore.Commit("non-defect-probe", "Probe", new CorruptedPlayerSnapshot(character, 80, cards,
                CorruptedPlayerSnapshot.Hash(character, 80, cards)));
        }
        var expected = CorruptedPlayerStore.Load() ?? throw new InvalidOperationException("Snapshot input not loaded.");
        if (CommandLineHelper.HasArg("architect-native-attack-vfx"))
        {
            await NativeAttackVfxPlaytest.Run(game, run, expected);
            return;
        }
        await Task.Delay(1500);
        var result = new DevConsole(shouldAllowDebugCommands: true).ProcessCommand("architect");
        if (!result.success || result.task == null)
            throw new InvalidOperationException($"Architect entry failed: {result.msg}");
        await result.task;
        Require(ArchitectRun.Get(run).EntrySnapshot?.Snapshot?.ContentHash == expected.Snapshot!.ContentHash,
            "normal encounter entry captured the real saved snapshot");
        var human = run.Players.Single();
        await UnwrittenPlaytest.ChooseBuildGift(human);
        await WaitFor(() => NMapScreen.Instance?.IsOpen == true);
        await Capture("map");
        var rest = run.Map.StartingMapPoint.Children.Single();
        await RunManager.Instance.EnterMapCoord(rest.coord);
        await Task.Delay(500);
        await Capture("rest");
        await RunManager.Instance.EnterMapCoord(rest.Children.Single().coord);
        await Task.Delay(500);
        await Capture("shop");
        await RunManager.Instance.EnterMapCoord(run.Map.BossMapPoint.coord);
        await PlayerTurn(human, 1);
        var combat = human.Creature.CombatState!;
        var monster = (CorruptedPlayer)combat.Enemies.Single().Monster!;
        var actor = monster.Native ?? throw new InvalidOperationException("Native actor not bound.");
        actor.AssertIdentity();
        Require(actor.HandPrepared && actor.State.Hand.Cards.Count > 0,
            "opening Corrupted Player hand is already drawn during the first human turn");
        Require(actor.Player.Character.Id.ToString() == expected.Snapshot.CharacterId &&
            actor.Player.Deck.Cards.Count == expected.Snapshot.Deck.Length, "saved character and entire deck restored");
        Require(actor.Player.Deck.Cards.Select(c => (Id: (ModelId?)c.Id, c.CurrentUpgradeLevel))
            .SequenceEqual(expected.Snapshot.RestoreDeck().Select(c => (c.Id, c.CurrentUpgradeLevel))),
            "all saved card IDs and upgrades restored");
        Require(combat.Players.Count == 1 && run.Players.Count == 1, "actor not enrolled in party or co-op scaling");
        Require(actor.Body.GetCreatureNode()?.OrbManager is { } manager &&
            manager.GetNode<Control>("%Orbs").GetChildCount() == actor.State.OrbQueue.Capacity,
            "native orb manager and initial slots attached for the saved character");
        await InspectCorruptedPlayerDisplay(game, actor);
        if (CommandLineHelper.HasArg("architect-native-previews"))
        {
            await NativeMechanicsPlaytest.Run(human, actor, previewsOnly: true);
            game.GetTree().Quit();
            return;
        }
        game.GetViewport().GuiReleaseFocus();
        if (!NativeDemoSafety.SharedVisible)
            Input.WarpMouse(game.GetViewportRect().Size / 2f);
        await Task.Delay(2500);
        await Capture("corrupted-player-display");
        var rngBefore = "";
        actor.TurnStarting += () => rngBefore = AllRng(human);
        actor.TurnFinished += () => Require(rngBefore == AllRng(human), "actor turn left human run/player RNG unchanged");
        var context = new ThrowingPlayerChoiceContext();
        var damageDealt = human.ExtraFields.DamageDealt;
        await HumanPlay<Neutralize>(human, actor.Body);
        Require(human.ExtraFields.DamageDealt == damageDealt + 3, "human damage metric preserved");
        await Capture("opening");
        for (var turn = 1; turn <= 5; turn++)
        {
            PlayerCmd.EndTurn(human, false);
            await PlayerTurn(human, turn + 1);
            Require(actor.CompletedTurns == turn, "native turn and side-end cleanup completed");
            var orbNodes = actor.Body.GetCreatureNode()!.OrbManager!.GetNode<Control>("%Orbs").GetChildren().OfType<NOrb>().ToArray();
            Require(orbNodes.Length >= actor.State.OrbQueue.Capacity &&
                actor.State.OrbQueue.Orbs.All(orb => orbNodes.Any(node => node.Model == orb)),
                "native orb slots and live orb models are rendered");
            actor.AssertIdentity();
            AssertCorruptedPlayerHandPosition(actor);
            if (turn == 1)
                await InspectCorruptedPlayerDisplay(game, actor);
            await Capture($"turn-{turn}");
            await CreatureCmd.Heal(human.Creature, human.Creature.MaxHp);
        }
        Require(actor.Player.RunState.Rng.Shuffle.ToSerializable().counter > 0, "private shuffle RNG advanced");
        Require(ArchitectRun.Get(run).EntrySnapshot!.Snapshot!.ContentHash == expected.Snapshot.ContentHash,
            "native execution preserved the raw captured deck");
        if (CommandLineHelper.HasArg("architect-native-loss"))
        {
            actor.Body.RemoveAllPowersInternalExcept();
            human.Creature.RemoveAllPowersInternalExcept();
            OrbCmd.RemoveSlots(actor.Player, actor.State.OrbQueue.Capacity);
            foreach (var pile in actor.State.AllPiles)
                foreach (var card in pile.Cards.ToArray())
                {
                    pile.RemoveInternal(card);
                    combat.RemoveCard(card);
                }
            actor.State.Hand.AddInternal(combat.CreateCard<TwinStrike>(actor.Player));
            for (var i = 0; i < 4; i++)
                actor.State.Hand.AddInternal(combat.CreateCard<Wound>(actor.Player));
            await CreatureCmd.Damage(context, human.Creature, human.Creature.CurrentHp - 1,
                ValueProp.Unblockable | ValueProp.Unpowered, human.Creature);
            MainFile.Logger.Info("NATIVE loss probe: first hit of native TwinStrike is lethal; remaining hit must not re-target.");
            PlayerCmd.EndTurn(human, false);
            await Finish(game, run, actor, expected.Revision, "ArchitectLoss");
            return;
        }
        await SaveManager.Instance.SaveRun(null);
        var saved = SaveManager.Instance.LoadRunSave().SaveData ?? throw new InvalidOperationException("Disposable room save missing.");
        var oldActor = actor;
        await game.ReturnToMainMenu();
        Require(oldActor.Cleaned, "actor cleaned on save-and-quit");
        run = RunState.FromSerializable(saved);
        await RunManager.Instance.SetUpSavedSingleplayer(run, saved);
        await game.LoadRun(run, saved.PreFinishedRoom);
        human = run.Players.Single();
        await PlayerTurn(human, 1);
        combat = human.Creature.CombatState!;
        actor = ((CorruptedPlayer)combat.Enemies.Single(c => c.Monster is CorruptedPlayer).Monster!).Native!;
        actor.TurnStarting += () => rngBefore = AllRng(human);
        actor.TurnFinished += () => Require(rngBefore == AllRng(human), "reloaded actor turn left human run/player RNG unchanged");
        Require(actor.CompletedTurns == 0 && actor.HandPrepared &&
            actor.Player.Deck.Cards.Count == expected.Snapshot.Deck.Length &&
            ArchitectRun.Get(run).EntrySnapshot!.Snapshot!.ContentHash == expected.Snapshot.ContentHash,
            "native room-boundary reload reconstructed the same saved deck without fixture or old actor state");
        if (!CommandLineHelper.HasArg("architect-native-nondefect"))
            await NativeMechanicsPlaytest.Run(human, actor);
        OrbCmd.RemoveSlots(actor.Player, actor.State.OrbQueue.Capacity);
        await OrbCmd.AddSlots(actor.Player, 2);
        await OrbCmd.Channel<LightningOrb>(context, actor.Player);
        await OrbCmd.Channel<LightningOrb>(context, actor.Player);
        var actorNode = actor.Body.GetCreatureNode()!;
        var orbManager = actorNode.OrbManager!;
        Require(actor.State.OrbQueue.Orbs.Count == 2 && orbManager.Visible,
            "handoff starts with two live lightning orbs");
        var observedOrbCleanup = false;
        void AfterActorRemoved(ICombatState _)
        {
            if (actor.Body.CombatState != null)
                return;
            Require(actor.Body.GetCreatureNode() == null && GodotObject.IsInstanceValid(orbManager),
                "death removed the room lookup while the orb UI still exists");
            Require(!orbManager.Visible && orbManager.DefaultFocusOwner == actorNode.Hitbox,
                "dead actor orb UI was hidden and emptied before deferred refresh");
            orbManager.UpdateVisuals(OrbEvokeType.None);
            observedOrbCleanup = true;
        }
        combat.CreaturesChanged += AfterActorRemoved;
        var hand = human.PlayerCombatState!.Hand.Cards.ToArray();
        var energy = human.PlayerCombatState.Energy;
        var before = human.Creature.CurrentHp;
        if (CommandLineHelper.HasArg("architect-native-poison"))
        {
            await PowerCmd.Apply<PoisonPower>(context, actor.Body, actor.Body.CurrentHp, human.Creature, null);
            var nextTurn = human.PlayerCombatState.TurnNumber + 1;
            PlayerCmd.EndTurn(human, false);
            await PlayerTurn(human, nextTurn);
        }
        else
        {
            await CreatureCmd.Damage(context, actor.Body, actor.Body.CurrentHp,
                ValueProp.Unblockable | ValueProp.Unpowered, human.Creature);
            Require(human.Creature.CurrentHp == before && human.PlayerCombatState.Energy == energy &&
                human.PlayerCombatState.Hand.Cards.SequenceEqual(hand), "continuous handoff preserved human state");
        }
        combat.CreaturesChanged -= AfterActorRemoved;
        Require(observedOrbCleanup && combat.Players.Count == 1 && run.Players.Count == 1,
            "orb cleanup survived handoff without enrolling the actor in the party");
        Require(actor.Cleaned, "dead actor unsubscribed and cleared native piles");
        var boss = combat.Enemies.Single(c => c.Monster is ArchitectBoss);
        await Capture("architect");
        await CreatureCmd.Kill(boss, true);
        await CombatManager.Instance.CheckWinCondition();
        await Finish(game, run, actor, expected.Revision, "ArchitectWin");
    }

    private static async Task InspectCorruptedPlayerDisplay(NGame game, NativeCorruptedPlayer actor)
    {
        await game.AwaitProcessFrame();
        AssertCorruptedPlayerHandPosition(actor);
        var telegraph = actor.Body.GetCreatureNode()!.GetNode<CorruptedPlayerTelegraph>("CorruptedPlayerTelegraph");
        Require((Control)telegraph is not PanelContainer, "Corrupted Player hand has no background panel");
        Require(telegraph.GetNode<ScrollContainer>("HandScroll").GetThemeStylebox("panel") is StyleBoxEmpty,
            "hand scroll area has no inherited translucent overlay");
        Require(!telegraph.FindChildren("*", "Button", true, false).OfType<Button>()
            .Any(button => button.Text == "Clear"), "hover previews do not need a Clear button");
        foreach (var (character, name) in new (CharacterModel, string)[]
        {
            (ModelDb.Character<Ironclad>(), "Corrupted Ironclad"),
            (ModelDb.Character<Silent>(), "Corrupted Silent"),
            (ModelDb.Character<Defect>(), "Corrupted Defect"),
            (ModelDb.Character<Necrobinder>(), "Corrupted Necrobinder"),
            (ModelDb.Character<Regent>(), "Corrupted Regent")
        })
        {
            var model = (CorruptedPlayer)ArchitectModels.CorruptedPlayer.ToMutable();
            model.Configure(character, 80, Array.Empty<JsonElement>(), "name-check");
            Require(model.Title.GetFormattedText() == name, $"enemy name is exactly {name}");
        }
        var energySlot = (Control)telegraph.FindChild("Energy", true, false);
        var energy = energySlot.GetChildren().OfType<NEnergyCounter>().Single();
        Require(energy.SceneFilePath == actor.Player.Character.EnergyCounterPath &&
            energy.Scale.IsEqualApprox(Vector2.One * 0.5f) &&
            energy.GetNode<Label>("Label").Text == $"{actor.State.Energy}/{actor.State.MaxEnergy}",
            "half-size native character energy orb displays current and maximum enemy energy");
        var face = telegraph.FindChild("CorruptedPlayerCard", true, false) as Button;
        Require((face != null) == (actor.State.Hand.Cards.Count > 0), "only hand cards appear in the telegraph");
        if (face != null)
        {
            var miniature = face.GetChildren().OfType<NCard>().Single();
            Require(face.Size.X >= 120 && face.Size.Y >= 160 &&
                miniature.Scale.IsEqualApprox(Vector2.One * 0.36f) &&
                miniature.GetCurrentSize().X <= face.Size.X && miniature.GetCurrentSize().Y <= face.Size.Y,
                "larger hand cards fit their clickable faces");
            var hoverContainer = game.HoverTipsContainer
                ?? throw new InvalidOperationException("Native hover container missing.");
            bool HasPreview() => hoverContainer.GetChildren().OfType<Control>()
                .Any(control => control.Name == "CorruptedPlayerCardPreview" && control.Visible);
            game.GetViewport().GuiReleaseFocus();
            face.EmitSignal(Control.SignalName.MouseEntered);
            await game.AwaitProcessFrame();
            Require(HasPreview(), "hovering a Corrupted Player card shows its enlarged preview");
            await Capture("corrupted-player-hover");
            face.EmitSignal(Button.SignalName.Pressed);
            face.EmitSignal(Control.SignalName.MouseExited);
            await game.AwaitProcessFrame();
            Require(!HasPreview(), "leaving a clicked card dismisses its preview instead of pinning it");
            face.EmitSignal(Button.SignalName.Pressed);
            await game.AwaitProcessFrame();
            Require(!HasPreview(), "clicking alone does not open a persistent card preview");
            face.GrabFocus();
            await game.AwaitProcessFrame();
            Require(HasPreview(), "keyboard or controller focus still previews a Corrupted Player card");
            game.GetViewport().GuiReleaseFocus();
            await game.AwaitProcessFrame();
            Require(!HasPreview(), "leaving keyboard or controller focus dismisses the preview");
        }
        var hand = actor.State.Hand.Cards.ToArray();
        var rng = AllRng(actor.Player);
        foreach (var (name, pile) in new[]
        {
            ("Draw", actor.State.DrawPile),
            ("Discard", actor.State.DiscardPile),
            ("Exhaust", actor.State.ExhaustPile)
        })
        {
            var button = (Button)telegraph.FindChild(name + "Pile", true, false);
            var visuals = button.GetNode<Control>("Visuals");
            Require(button.Text.Length == 0 &&
                visuals.GetNode<Label>("CountContainer/Count").Text == pile.Cards.Count.ToString() &&
                visuals.GetNode<TextureRect>("Icon").Texture != null &&
                visuals.Scale.IsEqualApprox(Vector2.One * 0.75f) &&
                (face == null || button.GetGlobalRect().Position.Y >= face.GetGlobalRect().End.Y),
                $"{name} uses compact native pile artwork and count below the hand");
            Require(visuals.GetNodeOrNull("HotkeyIcon") == null &&
                !telegraph.FindChildren("*", "NCombatCardPile", true, false).Any(),
                $"{name} visual reuse does not register the human player's pile hotkeys");
            var badgeNode = visuals.GetNode<Control>("CountContainer");
            var badge = badgeNode.GetGlobalTransform() * new Rect2(Vector2.Zero, badgeNode.Size);
            var hitbox = button.GetGlobalTransform() * new Rect2(Vector2.Zero, button.Size);
            Require(badge.Position.X >= hitbox.Position.X - 0.1f && badge.End.X <= hitbox.End.X + 0.1f &&
                badge.Position.Y >= hitbox.Position.Y - 0.1f && badge.End.Y <= hitbox.End.Y + 0.1f,
                $"{name} scaled badge {badge} fits inside its clickable area {hitbox}");
            var cards = pile.Cards.ToArray();
            button.EmitSignal(Button.SignalName.Pressed);
            await game.AwaitProcessFrame();
            Require(NCapstoneContainer.Instance?.CurrentCapstoneScreen is NCardPileScreen screen &&
                ReferenceEquals(screen.Pile, pile), $"{name} button opens the Corrupted Player's native pile browser");
            Require(pile.Cards.SequenceEqual(cards) && actor.State.Hand.Cards.SequenceEqual(hand) &&
                AllRng(actor.Player) == rng, $"{name} browsing preserves cards, pile order and RNG");
            NCapstoneContainer.Instance!.Close();
            await game.AwaitProcessFrame();
        }
    }

    private static void AssertCorruptedPlayerHandPosition(NativeCorruptedPlayer actor)
    {
        var node = actor.Body.GetCreatureNode()!;
        var telegraph = node.GetNode<CorruptedPlayerTelegraph>("CorruptedPlayerTelegraph");
        var expectedX = Mathf.Clamp(node.Visuals.IntentPosition.GlobalPosition.X - telegraph.Size.X / 2f,
            16f, Mathf.Max(16f, telegraph.GetViewportRect().Size.X - telegraph.Size.X - 16f));
        Require(Mathf.IsEqualApprox(telegraph.GlobalPosition.X, expectedX),
            "Corrupted Player hand stays anchored above its owner regardless of orb positions");
    }

    private static async Task Finish(NGame game, RunState run, NativeCorruptedPlayer actor, long initialRevision, string outcome)
    {
        await ArchitectEndingPlaytest.Complete(run, outcome);
        await WaitFor(() => NOverlayStack.Instance?.Peek() is NGameOverScreen);
        Require(ArchitectRun.Get(run).Outcome == outcome, $"native outcome preserved: {outcome}");
        Require(actor.Cleaned, "actor cleaned after terminal outcome");
        var committed = CorruptedPlayerStore.Load() ?? throw new InvalidOperationException("Terminal snapshot missing.");
        Require(committed.Revision == initialRevision + 1 && committed.Outcome == outcome &&
            committed.TerminalRunId == ArchitectRun.Get(run).Id, "terminal snapshot committed exactly once");
        Require(CorruptedPlayerStore.Commit(committed.TerminalRunId!, outcome, committed.Snapshot!) == committed.Revision,
            "terminal commit deduplicated");
        await Task.Delay(1500);
        await Capture("result");
        MainFile.Logger.Info($"NATIVE SMOKE PASSED ({outcome}): production actor with real snapshot and native terminal persistence.");
        game.GetTree().Quit();
    }

    internal static async Task HumanPlay<T>(Player human, Creature? target) where T : CardModel
    {
        var card = human.Creature.CombatState!.CreateCard<T>(human);
        await CardPileCmd.Add(card, PileType.Hand, skipVisuals: true);
        var action = new PlayCardAction(card, target);
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
        await action.CompletionTask;
        if (action.Exception != null)
            throw new InvalidOperationException("Native human card action failed.", action.Exception);
    }

    internal static string RngSnapshot(Player player) => JsonSerializer.Serialize(player.RunState.Rng.ToSerializable(),
        JsonSerializationUtility.GetTypeInfo<SerializableRunRngSet>());
    internal static string AllRng(Player player) => RngSnapshot(player) +
        JsonSerializer.Serialize(player.PlayerRng.ToSerializable(), new JsonSerializerOptions { IncludeFields = true });
    internal static Task PlayerTurn(Player player, int turn) => WaitFor(() =>
        player.PlayerCombatState is { Phase: PlayerTurnPhase.Play } state && state.TurnNumber == turn &&
        CombatManager.Instance.IsPartOfPlayerTurn(player) && !CombatManager.Instance.IsStarting &&
        player.Creature.CombatState is { CurrentSide: CombatSide.Player } combat &&
        NativeCorruptedPlayer.In(combat).All(actor => actor.State.Phase == PlayerTurnPhase.None && actor.HandPrepared));
    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("NATIVE ASSERTION FAILED: " + description);
        MainFile.Logger.Info("NATIVE ASSERT: " + description);
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Native smoke timed out waiting for game progression.");
            await NGame.Instance!.AwaitProcessFrame();
        }
    }
    internal static async Task Capture(string stage)
    {
        var game = NGame.Instance ?? throw new InvalidOperationException("Smoke game missing.");
        await game.AwaitProcessFrame();
        await game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var error = game.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(NativeDemoSafety.RuntimePath, stage + ".png"));
        if (error != Error.Ok)
            throw new InvalidOperationException($"Smoke capture {stage}: {error}");
        NativeDemoObserver.RecordCapture(stage);
        MainFile.Logger.Info($"NATIVE CAPTURE stage={stage} focused={game.GetWindow().HasFocus()} frames={Engine.GetProcessFrames()}");
    }
}
