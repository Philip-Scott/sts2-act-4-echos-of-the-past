using System.IO;
using System.Text.Json;
using BaseLib.Abstracts;
using BaseLib.Patches.Saves;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.Multiplayer;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.UI;

internal partial class ContinueRunDeckPreview : Control
{
    private const string NodeName = "ArchitectContinueDeckPreview";
    private SerializableRun save = null!;
    private CorruptedDeckButton button = null!;
    private CorruptedDeckPreview? preview;
    private NButton? neighbor;
    private NodePath? previousNeighbor;
    private bool stopped;
    private bool resetting;
    private LoadRunLobby? lobby;
    private int profileId;
    private ContinueRunPreviewSelection? selection;
    private LobbyPreviewSynchronizer? synchronizer;
    private string? loadError;
    private string? lastSyncError;

    internal static void Attach(Control screen, SerializableRun? save, LoadRunLobby? lobby = null)
    {
        if (save != null && screen.GetNodeOrNull<ContinueRunDeckPreview>(NodeName) is { resetting: true } existing &&
            existing.profileId == SaveManager.Instance.CurrentProfileId)
            return;
        Detach(screen);
        if (save != null)
            screen.AddChild(new ContinueRunDeckPreview
            {
                Name = NodeName, save = save, lobby = lobby, profileId = SaveManager.Instance.CurrentProfileId
            });
    }

    internal static void Detach(Control screen)
    {
        // Run loading can free the screen before CleanUpLobby resumes; _ExitTree already stopped the preview.
        if (!GodotObject.IsInstanceValid(screen))
            return;
        if (screen.GetNodeOrNull<ContinueRunDeckPreview>(NodeName) is not { } existing)
            return;
        existing.Stop();
        screen.RemoveChild(existing);
        existing.QueueFree();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        button = new CorruptedDeckButton
        {
            Name = "ViewDeck",
            Text = CorruptedDeckButton.Localize(save.Players.Count > 1 ? "BUTTON_PARTY" : "BUTTON"),
            CanReset = () => !stopped && !resetting && IsVisibleInTree() &&
                profileId == SaveManager.Instance.CurrentProfileId &&
                NModalContainer.Instance?.OpenModal == null &&
                (lobby == null || lobby.NetService.Type == NetGameType.Host &&
                    !lobby.IsPlayerReady(lobby.NetService.NetId)),
            ResetDeck = ResetDeck,
            ResetCompleted = ResetCompleted
        };
        AddChild(button);
        button.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        button.OffsetLeft = -350;
        button.OffsetRight = -30;
        button.OffsetTop = -190;
        button.OffsetBottom = -122;
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Open()));
        neighbor = GetParent() is NMainMenu
            ? GetParent().GetNode<NButton>("MainMenuTextButtons/ContinueButton")
            : GetParent().GetNode<NButton>("ConfirmButton");
        previousNeighbor = neighbor.FocusNeighborRight;
        neighbor.FocusNeighborRight = button.GetPath();
        button.FocusNeighborLeft = neighbor.GetPath();
        button.FocusNeighborBottom = neighbor.GetPath();
        VisibilityChanged += OnVisibilityChanged;
        UpdateVisibility();
        LoadSelection();
    }

    private void Open()
    {
        if (stopped || resetting || !IsVisibleInTree() || NModalContainer.Instance is not { OpenModal: null } modals)
            return;
        preview = new CorruptedDeckPreview { Name = "CorruptedDeckPreview" };
        if (synchronizer == null)
            LoadSelection();
        else if (synchronizer?.Error != null)
            RefreshSavedParty();
        UpdatePreview();
        preview.TreeExited += RestoreFocus;
        modals.Add(preview, showBackstop: false);
    }

    private void LoadSelection()
    {
        try
        {
            // Read the serialized extension directly: restoring a RunState executes save setters and model setup.
            var data = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save];
            string? json = null;
            if (data.Dictionaries.TryGetValue(typeof(string), out var strings) &&
                strings is IDictionary<string, string> values)
                values.TryGetValue(ContinueRunPreviewSelection.SaveKey, out json);
            selection = ContinueRunPreviewSelection.Read(json, save.Players.Select(player => player.NetId).ToArray());
            loadError = null;
            if (selection.UnselectedParty == null || lobby == null)
                return;
            var net = lobby.NetService;
            ulong host = net.Type == NetGameType.Host ? net.NetId :
                net is NetClientGameService client ? client.HostNetId :
                throw new InvalidOperationException("The preview requires the lobby's host/client service.");
            synchronizer = new LobbyPreviewSynchronizer(new LobbyPreviewEnvironment(
                net.NetId, host, CorruptedPlayerStore.GetProfileUuid,
                (profile, members) => CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, profile, members),
                (packet, target) =>
                {
                    // The saved group includes absent peers. Only connected peers can receive replies.
                    if (target is { } peer && lobby.PlayerIds.Contains(peer))
                        net.SendMessage(new CustomMessageWrapper
                        {
                            Message = new LobbyDeckPreviewMessage { Packet = packet }
                        }, peer);
                },
                UpdatePreview));
            net.RegisterMessageHandler<CustomMessageWrapper>(Receive);
            RefreshSavedParty();
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or
            NotSupportedException or IOException or UnauthorizedAccessException)
        {
            MainFile.Logger.Error($"Cannot load saved Corrupted Player deck preview: {error}");
            loadError = error.Message;
        }
    }

    private void RefreshSavedParty() =>
        synchronizer!.Refresh(save.Players.Select(player => player.NetId).ToArray(), selection!.UnselectedParty);

    private void Receive(CustomMessageWrapper message, ulong sender)
    {
        if (!stopped && !resetting && message.Message is LobbyDeckPreviewMessage deck)
            synchronizer!.Receive(deck.Packet, sender);
    }

    private void UpdatePreview()
    {
        if (synchronizer?.Error is { } syncError && syncError != lastSyncError)
            MainFile.Logger.Error("Cannot synchronize saved Corrupted Party preview: " + syncError);
        lastSyncError = synchronizer?.Error;
        if (!GodotObject.IsInstanceValid(preview))
            return;
        if ((loadError ?? lastSyncError) is { } error)
            preview!.SetPreview([], CorruptedDeckButton.Localize("ERROR") + "\n" + error);
        else if (synchronizer is { } sync)
        {
            var decks = sync.Party?.Lineage?.Members.Select(member => member.Snapshot).ToArray() ?? [];
            preview!.SetPreview(decks, sync.Party == null ? CorruptedDeckButton.Localize("LOADING") :
                decks.Length == 0 ? CorruptedDeckButton.Localize("FIRST_VISIT_PARTY") : null);
        }
        else if (selection is { } saved)
            preview!.SetPreview(saved.Snapshots,
                saved.StatusKey is { } key ? CorruptedDeckButton.Localize(key) : null);
    }

    private async Task<bool> ResetDeck()
    {
        resetting = true;
        neighbor!.Disable();
        var success = false;
        try
        {
            success = await CorruptedDeckReset.TryReset(() =>
                CorruptedDeckReset.ResetSavedRun(save, lobby?.NetService.NetId));
            return success;
        }
        finally
        {
            if (!success)
            {
                resetting = false;
                if (!stopped && GodotObject.IsInstanceValid(neighbor))
                    neighbor.Enable();
            }
        }
    }

    private void ResetCompleted()
    {
        if (stopped)
            return;
        if (lobby != null)
        {
            // Connected clients already received the old save. Rejoining distributes the corrected copy.
            var stack = NGame.Instance!.MainMenu!.SubmenuStack;
            if (stack.Peek() == GetParent())
                stack.Pop();
            return;
        }
        resetting = false;
        if (GodotObject.IsInstanceValid(neighbor))
        {
            neighbor!.Enable();
            if (previousNeighbor != null)
                neighbor.FocusNeighborRight = previousNeighbor;
            if (IsVisibleInTree() && NModalContainer.Instance?.OpenModal == null)
                neighbor.GrabFocus();
        }
    }

    private void RestoreFocus()
    {
        if (!stopped && IsInsideTree() && IsVisibleInTree() && NModalContainer.Instance?.OpenModal == null)
            button.GrabFocus();
    }

    private void UpdateVisibility()
    {
        if (GetParent() is NMainMenu menu)
            Visible = !menu.SubmenuStack.SubmenusOpen;
    }

    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree() && GodotObject.IsInstanceValid(preview))
            preview!.Close();
    }

    public override void _Process(double delta)
    {
        UpdateVisibility();
        if (!stopped && !resetting && synchronizer != null)
        {
            synchronizer.Tick(delta);
            if (synchronizer.Error != lastSyncError)
                UpdatePreview();
        }
    }

    private void Stop()
    {
        if (stopped)
            return;
        stopped = true;
        if (synchronizer != null)
            lobby!.NetService.UnregisterMessageHandler<CustomMessageWrapper>(Receive);
        if (resetting && GodotObject.IsInstanceValid(neighbor))
            neighbor!.Enable();
        if (GodotObject.IsInstanceValid(neighbor) && previousNeighbor != null)
            neighbor!.FocusNeighborRight = previousNeighbor;
        if (GodotObject.IsInstanceValid(preview))
            preview!.Close();
    }

    public override void _ExitTree() => Stop();

    internal static bool IsResetting(Control screen) =>
        screen.GetNodeOrNull<ContinueRunDeckPreview>(NodeName)?.resetting == true;
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.RefreshButtons))]
internal static class ContinueRunSoloDeckPreviewPatch
{
    private static void Postfix(NMainMenu __instance, ReadSaveResult<SerializableRun>? ____readRunSaveResult) =>
        ContinueRunDeckPreview.Attach(__instance, ____readRunSaveResult is { Success: true } result ? result.SaveData : null);
}

[HarmonyPatch(typeof(NMultiplayerLoadGameScreen), "AfterMultiplayerStarted")]
internal static class ContinueRunPartyDeckPreviewPatch
{
    private static void Postfix(NMultiplayerLoadGameScreen __instance, LoadRunLobby ____runLobby) =>
        ContinueRunDeckPreview.Attach(__instance, ____runLobby.Run, ____runLobby);
}

[HarmonyPatch(typeof(NMultiplayerLoadGameScreen), nameof(NMultiplayerLoadGameScreen.ShouldAllowRunToBegin))]
internal static class ContinueRunPartyDeckResetGuardPatch
{
    private static bool Prefix(NMultiplayerLoadGameScreen __instance, ref Task<bool> __result)
    {
        if (!ContinueRunDeckPreview.IsResetting(__instance))
            return true;
        __result = Task.FromResult(false);
        return false;
    }
}

[HarmonyPatch(typeof(NMultiplayerLoadGameScreen), "CleanUpLobby")]
internal static class ContinueRunPartyDeckPreviewCleanupPatch
{
    private static void Prefix(NMultiplayerLoadGameScreen __instance) => ContinueRunDeckPreview.Detach(__instance);
}
