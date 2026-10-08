using System.IO;
using System.Text.Json;
using BaseLib.Abstracts;
using BaseLib.Patches.Saves;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.Multiplayer;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class DeckPreviewPlaytest
{
    internal static async Task Run(NGame game)
    {
        var menu = game.MainMenu ?? throw new InvalidOperationException("The native main menu is unavailable.");
        await Task.Delay(5000);
        CheckMessageRoundtrip();
        CheckDisposedScreenCleanup();
        var stack = menu.SubmenuStack;
        var screen = stack.GetSubmenuType<NCharacterSelectScreen>();
        screen.InitializeSingleplayer();
        stack.Push(screen);
        await Task.Delay(800);
        var button = screen.GetNode<CorruptedDeckButton>("ArchitectDeckPreview/ViewDeck");
        var confirm = screen.GetNode<Control>("ConfirmButton");
        Check(button.GetGlobalRect().Position.Y > confirm.GetGlobalRect().End.Y,
            "Preview button is underneath the single-player checkmark.");
        await NativeDemoPlaytest.Capture("deck-character-select");
        button.GrabFocus();
        Check(button.HasFocus(), "Preview button accepts controller focus.");
        if (NativeDemoSafety.SharedVisible)
        {
            // Shared-visible captures deliberately disable viewport input.
            Press(button);
        }
        else
        {
            Input.ParseInputEvent(new InputEventAction { Action = MegaInput.select, Pressed = true });
            await Task.Delay(100);
            Input.ParseInputEvent(new InputEventAction { Action = MegaInput.select, Pressed = false });
        }
        await Task.Delay(100);
        Check(NModalContainer.Instance?.OpenModal is CorruptedDeckPreview, "First Visit preview opens without a run.");
        await NativeDemoPlaytest.Capture("deck-first-visit");
        ((CorruptedDeckPreview)NModalContainer.Instance!.OpenModal!).Close();
        await Task.Delay(100);
        stack.Pop();
        await Task.Delay(400);

        var snapshotPath = Path.Combine(NativeDemoSafety.RuntimePath, "snapshot-input.json");
        var snapshot = File.Exists(snapshotPath)
            ? CorruptedPlayerStore.Load(snapshotPath)?.Snapshot ??
                throw new InvalidDataException("The supplied deck-preview snapshot could not be loaded.")
            : SampleDeck();
        Check(CorruptedPlayerStore.Commit("deck-preview-smoke", "ArchitectWin", snapshot) != null,
            "Disposable snapshot is saved.");
        var before = JsonSerializer.Serialize(CorruptedPlayerStore.Load());
        screen.InitializeSingleplayer();
        stack.Push(screen);
        await Task.Delay(800);
        button = screen.GetNode<CorruptedDeckButton>("ArchitectDeckPreview/ViewDeck");
        await NativeDemoPlaytest.Capture("deck-character-select-saved");
        Press(button);
        var preview = NModalContainer.Instance?.OpenModal as CorruptedDeckPreview
            ?? throw new InvalidOperationException("Saved deck preview did not open.");
        await Task.Delay(700);
        var grid = preview.GetChildren().OfType<NCardGrid>().Single();
        Check(grid.CurrentlyDisplayedCards.Any(card => card.CurrentUpgradeLevel > 0),
            "Saved upgraded cards are rendered.");
        Check(grid.CurrentlyDisplayedCards.All(card => card.Owner == null), "Preview cards are detached from any player.");
        await NativeDemoPlaytest.Capture("deck-singleplayer");
        grid.EmitSignal(NCardGrid.SignalName.HolderPressed, grid.CurrentlyDisplayedCardHolders.First());
        await NativeDemoPlaytest.Capture("deck-inspection");
        preview.Close();
        await Task.Delay(100);
        Check(JsonSerializer.Serialize(CorruptedPlayerStore.Load()) == before,
            "Opening and inspecting cards never changes the snapshot.");
        stack.Pop();
        await Task.Delay(400);

        var host = new NetHostGameService(default);
        Check(host.StartENetHost(0, 3) == null, "Isolated multiplayer host starts.");
        screen.InitializeMultiplayerAsHost(host, 4);
        stack.Push(screen);
        await Task.Delay(800);
        button = screen.GetNode<CorruptedDeckButton>("ArchitectDeckPreview/ViewDeck");
        Check(button.IsVisibleInTree() && button.GetGlobalRect().Position.Y > confirm.GetGlobalRect().End.Y,
            "Preview button is underneath the multiplayer checkmark.");
        Press(button);
        preview = NModalContainer.Instance?.OpenModal as CorruptedDeckPreview
            ?? throw new InvalidOperationException("Multiplayer preview did not open.");
        await NativeDemoPlaytest.Capture("deck-multiplayer-waiting");
        preview.SetPreview([snapshot, snapshot], null);
        await Task.Delay(700);
        var tabs = preview.GetChildren().OfType<HBoxContainer>().Single();
        Check(tabs.GetChildCount() == 2, "Each Corrupted Party member has a deck tab.");
        Press(tabs.GetChild<CorruptedDeckButton>(1));
        await NativeDemoPlaytest.Capture("deck-multiplayer-party");
        preview.Close();
        await Task.Delay(100);
        stack.Pop();
        await Task.Delay(400);
        Check(!host.IsConnected, "Closing the multiplayer submenu still disconnects its lobby.");
        CharacterSelectDeckPreview.Detach(screen);
        CharacterSelectDeckPreview.Detach(screen);
        Check(screen.GetNodeOrNull<CharacterSelectDeckPreview>("ArchitectDeckPreview") == null,
            "Repeated cleanup of a live character-select screen leaves no preview.");
        Check(JsonSerializer.Serialize(CorruptedPlayerStore.Load()) == before,
            "Multiplayer preview does not replace the single-player snapshot.");
        await CheckContinuePreview(menu);
        MainFile.Logger.Info("DECK PREVIEW SMOKE PASSED: character select and native solo/multiplayer continue screens, frozen selections, first visits, upgrades, inspection, party tabs, focus, and live/disposed screen cleanup.");
        game.GetTree().Quit();
    }

    private static async Task CheckContinuePreview(NMainMenu menu)
    {
        Check(NativeDemoSafety.Enabled, "Continue preview probes require the isolated native save manager.");
        var saveManager = SaveManager.Instance;
        var runSaves = (RunSaveManager)AccessTools.Field(typeof(SaveManager), "_runSaveManager").GetValue(saveManager)!;
        var snapshot = SampleDeck();
        var save = ResumeSave([1]);
        var state = new ArchitectRun
        {
            Entered = true,
            EntrySnapshot = new CorruptedPlayerEnvelope { Revision = 1, Snapshot = snapshot }
        };
        SetResumeSelection(save, state);
        // The profile's current lineage must deliberately disagree with the run's frozen deck.
        var otherCards = snapshot.Deck.Take(1).ToArray();
        var other = new CorruptedPlayerSnapshot(snapshot.CharacterId, 99, otherCards,
            CorruptedPlayerSnapshot.Hash(snapshot.CharacterId, 99, otherCards));
        Check(CorruptedPlayerStore.Commit("continue-newer-lineage", "ArchitectWin", other) != null,
            "Continue probe has a different disposable on-disk successor.");
        var lineageBefore = JsonSerializer.Serialize(CorruptedPlayerStore.Load());
        await runSaves.SaveRun(save, isMultiplayer: false);
        var savedBefore = SavedRunJson(saveManager.LoadRunSave().SaveData!);
        var nativeContinue = menu.GetNode<NButton>("MainMenuTextButtons/ContinueButton");
        var originalNeighbor = nativeContinue.FocusNeighborRight;
        menu.RefreshButtons();
        await Task.Delay(200);
        var button = menu.GetNode<CorruptedDeckButton>("ArchitectContinueDeckPreview/ViewDeck");
        Check(nativeContinue.IsVisibleInTree() && button.IsVisibleInTree(),
            "Native Continue refresh attaches a visible saved-deck button.");
        Check(nativeContinue.FocusNeighborRight == button.GetPath(), "Native Continue navigates to the preview.");
        button.GrabFocus();
        Press(button);
        await Task.Delay(200);
        var preview = OpenPreview();
        CheckPreviewCards(preview, snapshot.Deck.Length);
        await NativeDemoPlaytest.Capture("deck-continue-solo-frozen");
        preview.Close();
        await Task.Delay(200);
        Check(button.HasFocus(), "Closing the resumed deck restores focus to its opener.");

        var submenu = menu.SubmenuStack.GetSubmenuType<NCharacterSelectScreen>();
        submenu.InitializeSingleplayer();
        menu.SubmenuStack.Push(submenu);
        await Task.Delay(200);
        Check(!button.IsVisibleInTree(), "Continue preview is hidden behind native submenus.");
        menu.SubmenuStack.Pop();
        await Task.Delay(200);
        Check(button.IsVisibleInTree(), "Continue preview returns after closing a submenu.");
        Check(SavedRunJson(saveManager.LoadRunSave().SaveData!) == savedBefore &&
            JsonSerializer.Serialize(CorruptedPlayerStore.Load()) == lineageBefore,
            "Continue preview leaves the serialized run and current lineage unchanged.");

        SetResumeSelection(save, new ArchitectRun { Entered = true });
        await runSaves.SaveRun(save, isMultiplayer: false);
        menu.RefreshButtons();
        button = menu.GetNode<CorruptedDeckButton>("ArchitectContinueDeckPreview/ViewDeck");
        Press(button);
        await Task.Delay(100);
        CheckPreviewMessage(OpenPreview(), "FROZEN_FIRST_VISIT");
        OpenPreview().Close();
        await Task.Delay(100);

        var strings = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save].DictForType<string>();
        strings.Remove(ContinueRunPreviewSelection.SaveKey);
        strings["OtherMod.Save"] = "unrelated";
        Check(((System.Collections.IDictionary)strings)[ContinueRunPreviewSelection.SaveKey] == null,
            "BaseLib's actual non-generic dictionary indexer returns null for an absent save key.");
        await runSaves.SaveRun(save, isMultiplayer: false);
        menu.RefreshButtons();
        button = menu.GetNode<CorruptedDeckButton>("ArchitectContinueDeckPreview/ViewDeck");
        Press(button);
        await Task.Delay(100);
        CheckPreviewMessage(OpenPreview(), "NOT_SELECTED");
        saveManager.DeleteCurrentRun();
        menu.RefreshButtons();
        await Task.Delay(100);
        Check(menu.GetNodeOrNull<ContinueRunDeckPreview>("ArchitectContinueDeckPreview") == null &&
            NModalContainer.Instance?.OpenModal == null && nativeContinue.FocusNeighborRight == originalNeighbor,
            "Native save removal closes the preview, detaches its button, and restores navigation.");
        await CheckUnselectedPartyPreview(menu, runSaves, snapshot, other);

        var host = new NetHostGameService(default);
        Check(host.StartENetHost(0, 3) == null, "Isolated resumed multiplayer host starts.");
        save = ResumeSave([host.NetId, host.NetId + 1]);
        var participants = save.Players.Select((player, index) =>
            new SuccessorParticipant(player.NetId, index == 0 ? CorruptedPlayerStore.GetProfileUuid() :
                new Guid(index + 1, 0, 0, new byte[8]))).ToArray();
        var origin = new FrozenCorruptedParty
        {
            RunId = "continue-party", HostNetId = host.NetId, HostProfileUuid = participants[0].ProfileUuid,
            GroupKey = SuccessorGroup.Key(participants.Select(player => player.ProfileUuid)), Participants = participants
        };
        state = new ArchitectRun();
        state.BindOrigin(origin);
        state.EnterParty(origin with
        {
            Lineage = new CorruptedPartyEnvelope
            {
                HostProfileUuid = origin.HostProfileUuid, GroupKey = origin.GroupKey, Revision = 1,
                TerminalRunId = "continue-party-prior", Outcome = "ArchitectWin",
                Members = [new(participants[0].ProfileUuid, snapshot), new(participants[1].ProfileUuid, other)]
            }
        });
        SetResumeSelection(save, state);
        // Exercise BaseLib's native save-extension deserialization, not only an in-memory test dictionary.
        save = JsonSerializer.Deserialize(SavedRunJson(save), JsonSerializationUtility.GetTypeInfo<SerializableRun>())!;
        var partyBefore = SavedRunJson(save);
        var load = menu.SubmenuStack.GetSubmenuType<NMultiplayerLoadGameScreen>();
        load.InitializeAsHost(host, save);
        menu.SubmenuStack.Push(load);
        await Task.Delay(500);
        button = load.GetNode<CorruptedDeckButton>("ArchitectContinueDeckPreview/ViewDeck");
        Press(button);
        await Task.Delay(200);
        preview = OpenPreview();
        CheckPreviewCards(preview, snapshot.Deck.Length);
        var tabs = preview.GetChildren().OfType<HBoxContainer>().Single();
        Check(tabs.GetChildCount() == 2, "Resume preview keeps every frozen party member before peers reconnect.");
        Press(tabs.GetChild<CorruptedDeckButton>(1));
        CheckPreviewCards(preview, other.Deck.Length);
        await NativeDemoPlaytest.Capture("deck-continue-party-frozen");
        Check(SavedRunJson(save) == partyBefore && JsonSerializer.Serialize(CorruptedPlayerStore.Load()) == lineageBefore,
            "Resumed party preview is read-only and does not reselect a local lineage.");
        preview.Close();
        await Task.Delay(100);
        var profiles = participants.Select(player => player.ProfileUuid).ToArray();
        foreach (var id in new[] { "reset-party-1", "reset-party-2" })
            CorruptedPartyStore.Commit(CorruptedPlayerStore.ProfileDirectory, origin.HostProfileUuid,
                origin with { RunId = id }, "ArchitectWin", state.EntryParty!.Lineage!.Members);
        for (int click = 1; click <= 9; click++)
        {
            RightClick(button);
            await Task.Delay(80);
            Check(SavedRunJson(save) == partyBefore &&
                CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, origin.HostProfileUuid, profiles) != null,
                $"Party right click {click} does not reset early.");
        }
        await NativeDemoPlaytest.Capture("deck-reset-party-before-tenth");
        RightClick(button);
        await Task.Delay(2500);
        Check(CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, origin.HostProfileUuid, profiles) == null,
            "Tenth host click deletes only this party's primary and backup.");
        Check(JsonSerializer.Serialize(CorruptedPlayerStore.Load()) == lineageBefore,
            "Party reset preserves the solo lineage.");
        CheckResetSave(runSaves.LoadMultiplayerRunSave().SaveData!, save.Players.Select(player => player.NetId).ToArray());
        await Task.Delay(200);
        Check(!host.IsConnected && NModalContainer.Instance?.OpenModal == null &&
            load.GetNodeOrNull<ContinueRunDeckPreview>("ArchitectContinueDeckPreview") == null,
            "Closing the native multiplayer resume lobby closes its preview and cleans up the button.");
        ContinueRunDeckPreview.Detach(load);
        ContinueRunDeckPreview.Detach(load);
        Check(load.GetNodeOrNull<ContinueRunDeckPreview>("ArchitectContinueDeckPreview") == null,
            "Repeated cleanup of a live multiplayer resume screen leaves no preview.");
        await CheckSoloReset(menu, runSaves, snapshot);
    }

    private static async Task CheckUnselectedPartyPreview(NMainMenu menu, RunSaveManager runSaves,
        CorruptedPlayerSnapshot first, CorruptedPlayerSnapshot second)
    {
        var hostProfile = CorruptedPlayerStore.GetProfileUuid();
        var otherProfile = Guid.NewGuid();
        var profiles = new[] { hostProfile, otherProfile };
        foreach (bool hasHistory in new[] { true, false })
        {
            var host = new NetHostGameService(default);
            Check(host.StartENetHost(0, 3) == null, "Isolated pre-Act-4 resume host starts.");
            var save = ResumeSave([host.NetId, host.NetId + 1]);
            var origin = new FrozenCorruptedParty
            {
                RunId = "continue-before-act4", HostNetId = host.NetId, HostProfileUuid = hostProfile,
                GroupKey = SuccessorGroup.Key(profiles),
                Participants = [new(host.NetId, hostProfile), new(host.NetId + 1, otherProfile)]
            };
            var state = new ArchitectRun();
            state.BindOrigin(origin);
            SetResumeSelection(save, state);
            if (hasHistory)
                CorruptedPartyStore.Commit(CorruptedPlayerStore.ProfileDirectory, hostProfile,
                    origin with { RunId = "previous-party-victory" }, "ArchitectWin",
                    [new(hostProfile, first), new(otherProfile, second)]);
            else
                CorruptedPartyStore.Delete(CorruptedPlayerStore.ProfileDirectory, hostProfile, profiles);
            await runSaves.SaveRun(save, isMultiplayer: true);
            save = runSaves.LoadMultiplayerRunSave().SaveData!;
            var before = SavedRunJson(save);
            var lineageBefore = JsonSerializer.Serialize(
                CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, hostProfile, profiles));
            var screen = menu.SubmenuStack.GetSubmenuType<NMultiplayerLoadGameScreen>();
            screen.InitializeAsHost(host, save);
            menu.SubmenuStack.Push(screen);
            await Task.Delay(500);
            var lobby = (MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby)
                AccessTools.Field(typeof(NMultiplayerLoadGameScreen), "_runLobby").GetValue(screen)!;
            Check(lobby.PlayerCount == 1 && save.Players.Count == 2,
                "Only the host is connected but the saved party contains two players.");
            var button = screen.GetNode<CorruptedDeckButton>("ArchitectContinueDeckPreview/ViewDeck");
            Press(button);
            await Task.Delay(200);
            var preview = OpenPreview();
            if (hasHistory)
            {
                var tabs = preview.GetChildren().OfType<HBoxContainer>().Single();
                Check(tabs.GetChildCount() == 2, "Pre-Act-4 Continue shows both saved opponents before the other player joins.");
                // Party files sort by profile UUID rather than network/lobby order.
                var saved = CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, hostProfile, profiles)!;
                for (int index = 0; index < saved.Members.Length; index++)
                {
                    Press(tabs.GetChild<CorruptedDeckButton>(index));
                    CheckPreviewCards(preview, saved.Members[index].Snapshot.Deck.Length);
                }
            }
            else
                CheckPreviewMessage(preview, "FIRST_VISIT_PARTY");
            await NativeDemoPlaytest.Capture(hasHistory ? "deck-continue-party-before-act4" : "deck-continue-party-no-history");
            Check(SavedRunJson(save) == before && SavedRunJson(runSaves.LoadMultiplayerRunSave().SaveData!) == before,
                "Pre-Act-4 Continue does not freeze a selection or rewrite the run.");
            Check(JsonSerializer.Serialize(CorruptedPartyStore.Load(
                CorruptedPlayerStore.ProfileDirectory, hostProfile, profiles)) == lineageBefore,
                "Pre-Act-4 Continue leaves the host's party history unchanged.");
            menu.SubmenuStack.Pop();
            await Task.Delay(200);
            Check(!host.IsConnected && NModalContainer.Instance?.OpenModal == null &&
                screen.GetNodeOrNull<ContinueRunDeckPreview>("ArchitectContinueDeckPreview") == null,
                "Closing the pre-Act-4 Continue lobby cleans up its synchronized preview.");
        }
    }

    private static async Task CheckSoloReset(NMainMenu menu, RunSaveManager runSaves, CorruptedPlayerSnapshot snapshot)
    {
        var save = ResumeSave([1]);
        SetResumeSelection(save, new ArchitectRun
        {
            Entered = true, EntrySnapshot = new CorruptedPlayerEnvelope { Snapshot = snapshot }
        });
        var strings = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save].DictForType<string>();
        strings["OtherMod.Save"] = "preserved by reset";
        await runSaves.SaveRun(save, isMultiplayer: false);
        menu.RefreshButtons();
        await Task.Delay(200);
        var button = menu.GetNode<CorruptedDeckButton>("ArchitectContinueDeckPreview/ViewDeck");
        var savedBefore = SavedRunJson(SaveManager.Instance.LoadRunSave().SaveData!);
        var identity = CorruptedPlayerStore.GetProfileUuid();
        for (int click = 1; click <= 9; click++)
        {
            RightClick(button);
            await Task.Delay(80);
            Check(SavedRunJson(SaveManager.Instance.LoadRunSave().SaveData!) == savedBefore &&
                CorruptedPlayerStore.Load() != null, $"Solo right click {click} does not reset early.");
        }
        Check(NModalContainer.Instance?.OpenModal == null, "Right clicks never activate the normal preview.");
        await NativeDemoPlaytest.Capture("deck-reset-solo-before-tenth");
        RightClick(button);
        button._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true,
            Position = Vector2.Zero });
        button._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        button.Hide();
        button.Show();
        await Task.Delay(2500);
        Check(CorruptedPlayerStore.Load() == null && CorruptedPlayerStore.GetProfileUuid() == identity,
            "Tenth solo click removes primary and backup but preserves profile identity.");
        var corrected = SaveManager.Instance.LoadRunSave().SaveData!;
        CheckResetSave(corrected, [1]);
        Check(ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[corrected].DictForType<string>()
                ["OtherMod.Save"] == "preserved by reset",
            "Reset preserves other mods' saved run extensions.");
        Check(JsonSerializer.Serialize(corrected.Players) == JsonSerializer.Serialize(save.Players) &&
            corrected.StartTime == save.StartTime && corrected.CurrentActIndex == save.CurrentActIndex,
            "Reset preserves the player's deck, run identity and act.");
        Check(!button.Visible, "Successful reset burns away the button.");
        Check(!ContinueRunDeckPreview.IsResetting(menu),
            "Outside input and temporary hiding during reset do not strand the Continue button.");
        await NativeDemoPlaytest.Capture("deck-reset-solo-complete");
        button.ResetGesture();
        Check(button.Visible, "Resetting the gesture restores a burned button for a different party.");
    }

    private static void CheckResetSave(SerializableRun save, ulong[] players)
    {
        var json = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save].DictForType<string>()
            [ContinueRunPreviewSelection.SaveKey];
        Check(ContinueRunPreviewSelection.Read(json, players).StatusKey == "FROZEN_FIRST_VISIT",
            "Native save reload retains the reset frozen First Visit.");
        var restored = ResumeRun(players);
        ExtendedSaveHandlers<IRunState, SerializableRun>.Load(save, restored);
        Check(ArchitectRun.Get(restored).EncounterSnapshots.Count == 0,
            "BaseLib restores a run with no Corrupted Player after reset.");
    }

    private static void RightClick(CorruptedDeckButton button)
    {
        button._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true,
            Position = button.Size / 2 });
        button._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false,
            Position = button.Size / 2 });
    }

    private static void CheckDisposedScreenCleanup()
    {
        var characterSelect = new NCharacterSelectScreen();
        characterSelect.Free();
        Check(!GodotObject.IsInstanceValid(characterSelect), "Character-select cleanup probe uses a freed native screen.");
        CharacterSelectDeckPreview.Detach(characterSelect);
        CharacterSelectDeckPreview.Detach(characterSelect);
        Check(true, "Character-select cleanup after disposal is safe and repeatable.");

        var load = new NMultiplayerLoadGameScreen();
        load.Free();
        Check(!GodotObject.IsInstanceValid(load), "Multiplayer resume cleanup probe uses a freed native screen.");
        ContinueRunDeckPreview.Detach(load);
        ContinueRunDeckPreview.Detach(load);
        Check(true, "Multiplayer resume cleanup after disposal is safe and repeatable.");
    }

    private static RunState ResumeRun(ulong[] players) =>
        RunState.CreateForNewRun(players.Select(id =>
                Player.CreateForNewRun(ModelDb.Character<Defect>(), UnlockState.all, id)).ToArray(),
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, 0, "CONTINUE-DECK-PREVIEW");

    private static SerializableRun ResumeSave(ulong[] players)
    {
        var run = ResumeRun(players);
        return new SerializableRun
        {
            SchemaVersion = SaveManager.Instance.GetLatestSchemaVersion<SerializableRun>(),
            Acts = run.Acts.Select(act => act.ToSave()).ToList(),
            Players = run.Players.Select(player => player.ToSerializable()).ToList(),
            SerializableOdds = run.Odds.ToSerializable(), SerializableRng = run.Rng.ToSerializable(),
            SerializableSharedRelicGrabBag = run.SharedRelicGrabBag.ToSerializable(),
            SaveTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), StartTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private static void SetResumeSelection(SerializableRun save, ArchitectRun state) =>
        ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save].DictForType<string>()
            [ContinueRunPreviewSelection.SaveKey] = JsonSerializer.Serialize(state);

    private static string SavedRunJson(SerializableRun save) =>
        JsonSerializer.Serialize(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>());

    private static CorruptedDeckPreview OpenPreview() =>
        NModalContainer.Instance?.OpenModal as CorruptedDeckPreview ??
            throw new InvalidOperationException("The native resume preview did not open.");

    private static void CheckPreviewCards(CorruptedDeckPreview preview, int count)
    {
        var grid = preview.GetChildren().OfType<NCardGrid>().Single();
        // CurrentlyDisplayedCards contains only the grid's virtualized window.
        var cards = (IReadOnlyCollection<CardModel>)AccessTools.Field(typeof(NCardGrid), "_cardsCache").GetValue(grid)!;
        Check(cards.Count == count, $"Continue preview contains exactly {count} frozen cards, got {cards.Count}.");
    }

    private static void CheckPreviewMessage(CorruptedDeckPreview preview, string key) =>
        Check(preview.GetChildren().OfType<Label>().Any(label => label.Text == CorruptedDeckButton.Localize(key)),
            $"Continue preview displays the saved selection status {key}.");

    private static CorruptedPlayerSnapshot SampleDeck()
    {
        var cards = Enumerable.Range(0, 35).Select(index =>
        {
            CardModel card = index % 2 == 0 ? ModelDb.Card<Zap>().ToMutable() : ModelDb.Card<Coolheaded>().ToMutable();
            if (index % 3 == 0)
                card.UpgradeInternal();
            return JsonSerializer.SerializeToElement(card.ToSerializable(), JsonSerializationUtility.GetTypeInfo<SerializableCard>());
        }).ToArray();
        string character = ModelDb.Character<Defect>().Id.ToString();
        return new CorruptedPlayerSnapshot(character, 80, cards, CorruptedPlayerSnapshot.Hash(character, 80, cards));
    }

    private static void Press(NButton button) => button.EmitSignal(NClickableControl.SignalName.Released, button);

    private static void CheckMessageRoundtrip()
    {
        var packet = new LobbyPreviewPacket
        {
            Kind = LobbyPreviewMessageKind.Snapshot,
            RequestId = Guid.NewGuid().ToString("N") + ":1",
            Epoch = Guid.NewGuid().ToString("N") + ":2",
            MemberNetIds = [10, 20, 30, 40],
            Payload = new string('x', PartyPayloadBuffer.ChunkSize),
            Digest = new string('a', 64),
            Index = 2,
            Count = 3
        };
        var bus = new NetMessageBus(new PacketReader(), new PacketWriter());
        var bytes = bus.SerializeMessage(10, new CustomMessageWrapper
        {
            Message = new LobbyDeckPreviewMessage { Packet = packet }
        }, out int length);
        Check(bus.TryDeserializeMessage(bytes[..length], out var restored, out var sender) && sender == 10 &&
            restored is CustomMessageWrapper { Message: LobbyDeckPreviewMessage message } &&
            JsonSerializer.Serialize(message.Packet) == JsonSerializer.Serialize(packet),
            "Native networking roundtrips a full-sized preview chunk and all four participant IDs.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        MainFile.Logger.Info("DECK PREVIEW: " + message);
    }
}
