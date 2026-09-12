using System.Text.Json;
using BaseLib.Patches.Saves;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

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

    internal static void Attach(Control screen, SerializableRun? save)
    {
        Detach(screen);
        if (save != null)
            screen.AddChild(new ContinueRunDeckPreview { Name = NodeName, save = save });
    }

    internal static void Detach(Control screen)
    {
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
            Text = CorruptedDeckButton.Localize(save.Players.Count > 1 ? "BUTTON_PARTY" : "BUTTON")
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
    }

    private void Open()
    {
        if (stopped || !IsVisibleInTree() || NModalContainer.Instance is not { OpenModal: null } modals)
            return;
        preview = new CorruptedDeckPreview { Name = "CorruptedDeckPreview" };
        try
        {
            // Read the serialized extension directly: restoring a RunState executes save setters and model setup.
            var data = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save];
            string? json = null;
            if (data.Dictionaries.TryGetValue(typeof(string), out var strings) &&
                strings is IDictionary<string, string> values)
                values.TryGetValue(ContinueRunPreviewSelection.SaveKey, out json);
            var selection = ContinueRunPreviewSelection.Read(json, save.Players.Select(player => player.NetId).ToArray());
            preview.SetPreview(selection.Snapshots,
                selection.StatusKey is { } key ? CorruptedDeckButton.Localize(key) : null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or
            NotSupportedException)
        {
            MainFile.Logger.Error($"Cannot load saved Corrupted Player deck preview: {error}");
            preview.SetPreview([], CorruptedDeckButton.Localize("ERROR") + "\n" + error.Message);
        }
        preview.TreeExited += RestoreFocus;
        modals.Add(preview, showBackstop: false);
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

    public override void _Process(double delta) => UpdateVisibility();

    private void Stop()
    {
        if (stopped)
            return;
        stopped = true;
        if (GodotObject.IsInstanceValid(neighbor) && previousNeighbor != null)
            neighbor!.FocusNeighborRight = previousNeighbor;
        if (GodotObject.IsInstanceValid(preview))
            preview!.Close();
    }

    public override void _ExitTree() => Stop();
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
        ContinueRunDeckPreview.Attach(__instance, ____runLobby.Run);
}

[HarmonyPatch(typeof(NMultiplayerLoadGameScreen), "CleanUpLobby")]
internal static class ContinueRunPartyDeckPreviewCleanupPatch
{
    private static void Prefix(NMultiplayerLoadGameScreen __instance) => ContinueRunDeckPreview.Detach(__instance);
}
