using System.IO;
using System.Text.Json;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using TheArchitect.TheArchitectCode.Multiplayer;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.UI;

internal partial class CharacterSelectDeckPreview : Control
{
    private StartRunLobby lobby = null!;
    private CorruptedDeckButton button = null!;
    private LobbyPreviewSynchronizer? synchronizer;
    private CorruptedDeckPreview? preview;
    private CorruptedPlayerSnapshot[] snapshots = [];
    private string? status;
    private string? lastError;
    private bool stopped;

    internal static void Attach(NCharacterSelectScreen screen, StartRunLobby lobby)
    {
        Detach(screen);
        screen.AddChild(new CharacterSelectDeckPreview { Name = "ArchitectDeckPreview", lobby = lobby });
    }

    internal static void Detach(NCharacterSelectScreen screen)
    {
        if (screen.GetNodeOrNull<CharacterSelectDeckPreview>("ArchitectDeckPreview") is not { } existing)
            return;
        existing.Stop();
        screen.RemoveChild(existing);
        existing.QueueFree();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        bool multiplayer = lobby.NetService.Type is NetGameType.Host or NetGameType.Client;
        button = new CorruptedDeckButton
        {
            Name = "ViewDeck",
            Text = CorruptedDeckButton.Localize(multiplayer ? "BUTTON_PARTY" : "BUTTON")
        };
        AddChild(button);
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        button.OffsetLeft = -350;
        button.OffsetRight = -30;
        button.OffsetTop = -190;
        button.OffsetBottom = -122;
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Open()));
        var choices = GetParent().GetNode<Control>("CharSelectButtons/ButtonContainer");
        foreach (var choice in choices.GetChildren().OfType<NCharacterSelectButton>())
            choice.FocusNeighborBottom = button.GetPath();
        button.FocusNeighborTop = choices.GetChild<Control>(0).GetPath();
        button.FocusNeighborLeft = choices.GetChild<Control>(choices.GetChildCount() - 1).GetPath();

        if (!multiplayer)
        {
            LoadSingleplayer();
            return;
        }

        var net = lobby.NetService;
        ulong host = net.Type == NetGameType.Host ? net.NetId :
            net is NetClientGameService client ? client.HostNetId :
            throw new InvalidOperationException("The preview requires the lobby's host/client service.");
        synchronizer = new LobbyPreviewSynchronizer(new LobbyPreviewEnvironment(
            net.NetId, host, CorruptedPlayerStore.GetProfileUuid,
            (profile, members) => CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, profile, members),
            (packet, target) =>
            {
                var message = new CustomMessageWrapper { Message = new LobbyDeckPreviewMessage { Packet = packet } };
                if (target is { } peer)
                    net.SendMessage(message, peer);
                else
                    net.SendMessage(message);
            },
            UpdateMultiplayer));
        // BaseLib normally registers its custom-message dispatcher only after a run starts.
        net.RegisterMessageHandler<CustomMessageWrapper>(Receive);
        lobby.PlayerConnected += MembershipChanged;
        lobby.PlayerDisconnected += MembershipChanged;
        RefreshMembership();
    }

    private void LoadSingleplayer()
    {
        try
        {
            var snapshot = CorruptedPlayerStore.Load()?.Snapshot;
            if (snapshot?.ResolveCharacter() is not null)
                SetPreview([snapshot], null);
            else
                SetPreview([], CorruptedDeckButton.Localize(snapshot == null ? "FIRST_VISIT" : "UNAVAILABLE_CHARACTER"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException)
        {
            MainFile.Logger.Error($"Cannot load Corrupted Player deck preview: {error}");
            SetPreview([], CorruptedDeckButton.Localize("ERROR") + "\n" + error.Message);
        }
    }

    private void Receive(CustomMessageWrapper message, ulong sender)
    {
        if (!stopped && message.Message is LobbyDeckPreviewMessage deck)
            synchronizer!.Receive(deck.Packet, sender);
    }

    private void MembershipChanged(StartRunLobbyPlayer _) => RefreshMembership();

    private void RefreshMembership() => synchronizer!.Refresh(lobby.Players.Select(player => player.id).ToArray());

    private void UpdateMultiplayer()
    {
        var sync = synchronizer!;
        if (sync.Error is { } error)
        {
            if (lastError != error)
                MainFile.Logger.Error("Cannot synchronize Corrupted Party deck preview: " + error);
            lastError = error;
            SetPreview([], CorruptedDeckButton.Localize("ERROR") + "\n" + error);
        }
        else
        {
            lastError = null;
            if (sync.Party is { } party)
            {
                var decks = party.Lineage?.Members.Select(member => member.Snapshot).ToArray() ?? [];
                SetPreview(decks, decks.Length == 0 ? CorruptedDeckButton.Localize("FIRST_VISIT_PARTY") : null);
            }
            else
                SetPreview([], CorruptedDeckButton.Localize(sync.IsLoading ? "LOADING" : "WAITING"));
        }
    }

    private void SetPreview(CorruptedPlayerSnapshot[] decks, string? information)
    {
        snapshots = decks;
        status = information;
        button.TooltipText = information ?? CorruptedDeckButton.Localize("TITLE");
        if (GodotObject.IsInstanceValid(preview))
            preview!.SetPreview(decks, information);
    }

    private void Open()
    {
        if (stopped || NModalContainer.Instance is not { OpenModal: null } modals)
            return;
        if (synchronizer == null)
            LoadSingleplayer();
        else if (synchronizer.Error != null)
            RefreshMembership();
        preview = new CorruptedDeckPreview { Name = "CorruptedDeckPreview" };
        preview.SetPreview(snapshots, status);
        modals.Add(preview, showBackstop: false);
    }

    public override void _Process(double delta)
    {
        if (stopped || synchronizer == null)
            return;
        synchronizer.Tick(delta);
        if (synchronizer.Error != lastError)
            UpdateMultiplayer();
    }

    private void Stop()
    {
        if (stopped)
            return;
        stopped = true;
        if (synchronizer != null)
        {
            lobby.NetService.UnregisterMessageHandler<CustomMessageWrapper>(Receive);
            lobby.PlayerConnected -= MembershipChanged;
            lobby.PlayerDisconnected -= MembershipChanged;
        }
        if (GodotObject.IsInstanceValid(preview))
            preview!.Close();
    }

    public override void _ExitTree() => Stop();
}

[HarmonyPatch(typeof(NCharacterSelectScreen), "AfterInitialized")]
internal static class CharacterSelectDeckPreviewPatch
{
    private static void Postfix(NCharacterSelectScreen __instance, StartRunLobby ____lobby) =>
        CharacterSelectDeckPreview.Attach(__instance, ____lobby);
}

[HarmonyPatch(typeof(NCharacterSelectScreen), "CleanUpLobby")]
internal static class CharacterSelectDeckPreviewCleanupPatch
{
    private static void Prefix(NCharacterSelectScreen __instance) => CharacterSelectDeckPreview.Detach(__instance);
}
