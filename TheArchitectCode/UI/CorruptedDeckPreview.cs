using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.UI;

internal partial class CorruptedDeckPreview : Control, IScreenContext
{
    private NCardGrid grid = null!;
    private Label heading = null!;
    private Label message = null!;
    private HBoxContainer tabs = null!;
    private CorruptedDeckButton close = null!;
    private Control? inspection;
    private Control? inspectionAnchor;
    private NHotkeyManager hotkeys = null!;
    private CorruptedPlayerSnapshot[] snapshots = [];
    private string? status;
    private int selected;

    public Control? DefaultFocusedControl => grid.DefaultFocusedControl ?? close;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var background = new ColorRect { Color = new Color("101a20f5"), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        heading = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        heading.AddThemeFontSizeOverride("font_size", 36);
        AddChild(heading);
        heading.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        heading.OffsetTop = 35;
        heading.OffsetBottom = 90;

        tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        tabs.AddThemeConstantOverride("separation", 18);
        AddChild(tabs);
        tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        tabs.OffsetTop = 100;
        tabs.OffsetBottom = 158;

        grid = ResourceLoader.Load<PackedScene>("res://scenes/cards/card_grid.tscn").Instantiate<NCardGrid>();
        AddChild(grid);
        grid.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        grid.OffsetTop = 175;
        grid.OffsetBottom = -115;
        grid.ClipContents = true;
        grid.HolderPressed += Inspect;
        grid.HolderAltPressed += Inspect;

        message = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        message.AddThemeFontSizeOverride("font_size", 28);
        AddChild(message);
        message.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        message.OffsetLeft = 200;
        message.OffsetRight = -200;
        message.OffsetTop = 175;
        message.OffsetBottom = -115;

        close = new CorruptedDeckButton { Text = CorruptedDeckButton.Localize("CLOSE") };
        AddChild(close);
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        close.OffsetLeft = -140;
        close.OffsetRight = 140;
        close.OffsetTop = -95;
        close.OffsetBottom = -30;
        close.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Back()));

        hotkeys = NHotkeyManager.Instance ?? throw new InvalidOperationException("The menu hotkey manager is unavailable.");
        hotkeys.AddBlockingScreen(this);
        hotkeys.PushHotkeyPressedBinding(MegaInput.cancel, Back);
        hotkeys.PushHotkeyPressedBinding(MegaInput.pauseAndBack, Back);
        Render();
    }

    internal void SetPreview(CorruptedPlayerSnapshot[] decks, string? information)
    {
        snapshots = decks;
        status = information;
        selected = 0;
        if (IsNodeReady())
        {
            ClearInspection();
            Render();
        }
    }

    private void Render()
    {
        foreach (var child in tabs.GetChildren())
        {
            tabs.RemoveChild(child);
            child.QueueFree();
        }
        for (var i = 0; i < snapshots.Length && snapshots.Length > 1; i++)
        {
            int index = i;
            var tab = new CorruptedDeckButton
            {
                Text = $"{i + 1}. {CharacterName(snapshots[i])}",
                CustomMinimumSize = new Vector2(310, 58)
            };
            tabs.AddChild(tab);
            tab.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
            {
                selected = index;
                ShowSelectedDeck();
            }));
        }
        ShowSelectedDeck();
    }

    private static string CharacterName(CorruptedPlayerSnapshot snapshot) =>
        snapshot.ResolveCharacter()?.Title.GetFormattedText() ?? snapshot.CharacterId;

    private void ShowSelectedDeck()
    {
        ClearInspection();
        heading.Text = CorruptedDeckButton.Localize("TITLE");
        grid.ClearGrid();
        message.Text = status ?? "";
        if (snapshots.Length > 0)
        {
            var snapshot = snapshots[selected];
            heading.Text += $" - {CharacterName(snapshot)}";
            if (snapshots.Length > 1)
                heading.Text += $" ({selected + 1}/{snapshots.Length})";
            var problem = snapshot.ResolveCharacter() == null
                ? $"Saved Corrupted Player character is not installed: {snapshot.CharacterId}."
                : NativeCardSupport.Preflight(snapshot.Deck);
            if (problem != null)
            {
                MainFile.Logger.Error("Cannot preview Corrupted Player deck: " + problem);
                message.Text = problem;
            }
            else
            {
                try
                {
                    var saved = snapshot.RestoreDeck();
                    var cards = saved.Select((card, index) =>
                        CardModel.FromSerializable(NativeCardSupport.ForNativeLoad(snapshot.Deck[index], card))).ToList();
                    grid.SetCards(cards, PileType.Deck, [SortingOrders.Ascending]);
                    heading.Text += $" - {cards.Count} " + CorruptedDeckButton.Localize("CARDS");
                    if (cards.Count == 0)
                        message.Text = CorruptedDeckButton.Localize("EMPTY_DECK");
                }
                catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
                {
                    MainFile.Logger.Error($"Cannot preview Corrupted Player deck: {error}");
                    message.Text = CorruptedDeckButton.Localize("ERROR") + "\n" + error.Message;
                }
            }
        }
        message.Visible = message.Text.Length > 0;
        grid.Visible = !message.Visible;
    }

    private void Inspect(NCardHolder holder)
    {
        var model = holder.CardModel ?? throw new InvalidOperationException("The selected preview card is unavailable.");
        var card = NCard.Create(model) ?? throw new InvalidOperationException("The selected card preview could not be created.");
        ClearInspection();
        NHoverTipSet.Clear();
        inspection = new Control { MouseFilter = MouseFilterEnum.Stop };
        AddChild(inspection);
        inspection.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, 0.9f) };
        inspection.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        backdrop.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
                ClearInspection();
        };
        inspectionAnchor = new Control { MouseFilter = MouseFilterEnum.Ignore };
        inspection.AddChild(inspectionAnchor);
        inspectionAnchor.AddChild(card);
        card.SetForceUnpoweredPreview(true);
        card.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
        card.Scale = Vector2.One * Mathf.Min(1.5f, (Size.Y - 180) / NCard.defaultSize.Y);
        inspectionAnchor.Size = card.GetCurrentSize();
        inspectionAnchor.Position = (Size - inspectionAnchor.Size) * 0.5f;
        card.Position = inspectionAnchor.Size * 0.5f;
        card.MouseFilter = MouseFilterEnum.Ignore;
        var tips = NHoverTipSet.CreateAndShow(inspectionAnchor, model.HoverTips, HoverTipAlignment.Right);
        tips?.Reparent(inspection);
        grid.FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Disabled;
        tabs.FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Disabled;
        MoveChild(close, -1);
        close.GrabFocus();
    }

    private void ClearInspection()
    {
        if (inspection == null)
            return;
        NHoverTipSet.Remove(inspectionAnchor!);
        inspection.Hide();
        inspection.QueueFree();
        inspection = null;
        inspectionAnchor = null;
        grid.FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Inherited;
        tabs.FocusBehaviorRecursive = FocusBehaviorRecursiveEnum.Inherited;
    }

    private void Back()
    {
        if (inspection != null)
            ClearInspection();
        else
            Close();
    }

    internal void Close()
    {
        if (NModalContainer.Instance?.OpenModal == this)
            NModalContainer.Instance.Clear();
    }

    public override void _Process(double delta)
    {
        if (inspection != null || !grid.Visible)
            return;
        var top = tabs.GetChildCount() > 0 ? tabs.GetChild<Control>(selected) : close;
        foreach (var holder in grid.GetTopRowOfCardNodes() ?? [])
            holder.FocusNeighborTop = top.GetPath();
        if (grid.DefaultFocusedControl is { } card)
        {
            close.FocusNeighborTop = card.GetPath();
            foreach (var tab in tabs.GetChildren().OfType<Control>())
                tab.FocusNeighborBottom = card.GetPath();
        }
    }

    public override void _ExitTree()
    {
        ClearInspection();
        NHoverTipSet.Clear();
        hotkeys.RemoveHotkeyPressedBinding(MegaInput.cancel, Back);
        hotkeys.RemoveHotkeyPressedBinding(MegaInput.pauseAndBack, Back);
        hotkeys.RemoveBlockingScreen(this);
    }
}
