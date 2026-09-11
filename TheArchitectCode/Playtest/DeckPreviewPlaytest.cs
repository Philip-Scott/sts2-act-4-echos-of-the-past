using System.IO;
using System.Text.Json;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
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
        MainFile.Logger.Info("DECK PREVIEW SMOKE PASSED: single-player, multiplayer, first visit, upgrades, inspection, party tabs, and cleanup.");
        game.GetTree().Quit();
    }

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
