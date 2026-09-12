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
        Check(JsonSerializer.Serialize(CorruptedPlayerStore.Load()) == before,
            "Multiplayer preview does not replace the single-player snapshot.");
        await CheckContinuePreview(menu);
        MainFile.Logger.Info("DECK PREVIEW SMOKE PASSED: character select and native solo/multiplayer continue screens, frozen selections, first visits, upgrades, inspection, party tabs, focus, and cleanup.");
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

        var host = new NetHostGameService(default);
        Check(host.StartENetHost(0, 3) == null, "Isolated resumed multiplayer host starts.");
        save = ResumeSave([host.NetId, host.NetId + 1]);
        var participants = save.Players.Select((player, index) =>
            new SuccessorParticipant(player.NetId, new Guid(index + 1, 0, 0, new byte[8]))).ToArray();
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
        menu.SubmenuStack.Pop();
        await Task.Delay(200);
        Check(!host.IsConnected && NModalContainer.Instance?.OpenModal == null &&
            load.GetNodeOrNull<ContinueRunDeckPreview>("ArchitectContinueDeckPreview") == null,
            "Closing the native multiplayer resume lobby closes its preview and cleans up the button.");
    }

    private static SerializableRun ResumeSave(ulong[] players)
    {
        var run = RunState.CreateForNewRun(players.Select(id =>
                Player.CreateForNewRun(ModelDb.Character<Defect>(), UnlockState.all, id)).ToArray(),
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, 0, "CONTINUE-DECK-PREVIEW");
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

    private static void CheckPreviewCards(CorruptedDeckPreview preview, int count) =>
        Check(preview.GetChildren().OfType<NCardGrid>().Single().CurrentlyDisplayedCards.Count() == count,
            $"Continue preview renders exactly {count} frozen cards, not the current on-disk lineage.");

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
