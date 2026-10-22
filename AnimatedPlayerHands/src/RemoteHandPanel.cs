using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;

namespace AnimatedPlayerHands;

/// <summary>
/// A remote player's hand, drawn as a strip of compact cards resting on their health bar, between
/// badges for their draw pile and their discard and exhaust piles. Everything is read from the
/// combat state every client already simulates; nothing is sent over the network.
/// </summary>
internal sealed class RemoteHandPanel
{
    internal static readonly Vector2 CardFaceSize = new(HandLayoutGeometry.CardWidth, HandLayoutGeometry.CardHeight);
    internal static readonly Vector2 PanelSize = new(HandLayoutGeometry.PanelWidth, HandLayoutGeometry.CardHeight);
    private const float MiniatureScale = 0.288f;
    private const string HostName = "AnimatedPlayerHands";
    private static RemoteHandPanel? _previewOwner;

    private readonly HBoxContainer _row;
    private readonly List<Cell> _cells = [];
    private RemoteHandAnimator? _hand;
    private RemoteStatusIcons? _status;
    private CombatStateTracker? _stateTracker;
    private bool _refreshQueued;
    private bool _interactive = true;
    private Cell? _inspected;
    private Control? _preview;
    private NCard? _enlarged;
    private NHoverTipSet? _tips;

    internal NCreature Anchor { get; }
    internal Player Player { get; }
    internal Control Root { get; }
    internal bool Stopped { get; private set; }
    internal IReadOnlyList<NCard?> Miniatures => _cells.Select(cell => cell.Miniature).ToArray();
    internal int InFlight => _hand?.InFlight ?? 0;
    internal int PileArrivals => _hand?.PileArrivals ?? 0;
    internal Control Badge(PileType pile) => _status!.PileButton(pile);
    internal Control? StatusIcon(string name) => _status?.Icon(name);

    private sealed class Cell(CardModel card, Button face)
    {
        public CardModel Card { get; } = card;
        public Button Face { get; } = face;
        public NCard? Miniature;
    }

    internal RemoteHandPanel(NCreature anchor, Player player)
    {
        Anchor = anchor;
        Player = player;
        Root = new Control
        {
            Name = $"AnimatedPlayerHand{player.NetId}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = PanelSize,
            // Like CombatUi, escape the combat scene's negative Z without overtaking GlobalUi.
            ZAsRelative = false
        };
        // Centered over the owner's health bar. Energy and piles sit with their powers below it.
        _row = new HBoxContainer
        {
            Name = "Hand",
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _row.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(_row);
        Host(anchor).AddChild(Root);
        Root.TreeExiting += Stop;
        _stateTracker = CombatManager.Instance.StateTracker;
        _stateTracker.CombatStateChanged += OnCombatStateChanged;
        SafeRefresh();
    }

    // GUI picking follows tree order, so hands come after every creature body; as children of the
    // anchor, a later creature's hitbox would take hover from a hand drawn above it.
    private static Node Host(NCreature anchor)
    {
        var container = anchor.GetParent();
        var parent = container?.GetParent();
        if (container == null || parent == null)
            return anchor;
        if (parent.GetNodeOrNull<Control>(HostName) is { } host)
            return host;
        host = new Control { Name = HostName, MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(host);
        var last = parent.GetChildren().OfType<Node>()
            .Where(child => child != host && child.GetChildren().OfType<NCreature>().Any())
            .Max(child => child.GetIndex());
        parent.MoveChild(host, last + 1);
        return host;
    }

    private PlayerCombatState? State => Player.PlayerCombatState;

    private bool CanInspect => !Stopped && !Player.Creature.IsDead &&
        NCombatRoom.Instance is { } room && ActiveScreenContext.Instance.IsCurrent(room);

    // The hand sits over its owner's body; let card plays and ally targeting reach the creature.
    private static bool LocalPlayerAiming =>
        NCombatRoom.Instance?.Ui?.Hand is { InCardPlay: true } ||
        NTargetManager.Instance is { IsInSelection: true };

    private void OnCombatStateChanged(CombatState state)
    {
        if (!Stopped && Player.Creature.CombatState == state && !CombatManager.Instance.IsOverOrEnding)
            SafeRefresh();
    }

    private void SafeRefresh()
    {
        try
        {
            Refresh();
        }
        catch (Exception error)
        {
            Faults.Report($"Player {Player.NetId}'s hand refresh", error);
        }
    }

    // Native draws happen inside a single command; refresh once per frame so new cards get slots.
    private void QueueRefresh()
    {
        if (_refreshQueued || Stopped)
            return;
        _refreshQueued = true;
        Callable.From(() =>
        {
            _refreshQueued = false;
            if (!Stopped)
                SafeRefresh();
        }).CallDeferred();
    }

    internal void Refresh()
    {
        if (Stopped || State is not { } state || CombatManager.Instance.IsOverOrEnding)
            return;
        _hand ??= new RemoteHandAnimator(Player, CardFaceSize / 2f, MiniatureScale, PileCenter, TakeMiniature,
            QueueRefresh);
        if (_status?.Uses(Player) != true)
        {
            _status?.Detach();
            _status = RemoteStatusIcons.Attach(Anchor, Player, () => CanInspect && _interactive, () => ClearPreview());
        }
        var cards = state.Hand.Cards;
        if (_cells.Select(cell => cell.Card).SequenceEqual(cards))
        {
            foreach (var cell in _cells)
                if (cell.Miniature != null)
                    ShowCurrentState(cell.Miniature);
            if (_inspected != null && GodotObject.IsInstanceValid(_enlarged))
                ShowCurrentState(_enlarged!);
            return;
        }
        ClearPreview();
        // Cards still in hand keep their node and glide to their new slot instead of snapping.
        foreach (var cell in _cells)
            if (cell.Miniature is { } node && !_hand.Release(cell.Card, node))
                CardNodes.Return(node);
        _cells.Clear();
        foreach (var child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var card in cards)
            AddCard(card);
        _hand.DropUnclaimed();
        // Hands are capped at ten cards; overlapping them like the native hand always fits the strip.
        _row.AddThemeConstantOverride("separation", HandLayoutGeometry.HandSeparation(_cells.Count, CardFaceSize.X,
            PanelSize.X));
    }

    private Vector2? PileCenter(PileType pile) => Stopped ? null : _status?.PileIconCenter(pile);

    private NCard? TakeMiniature(CardModel card)
    {
        var cell = _cells.FirstOrDefault(cell => cell.Card == card && cell.Miniature != null);
        if (cell == null)
            return null;
        if (_inspected == cell)
            ClearPreview();
        var node = cell.Miniature;
        cell.Miniature = null;
        return node;
    }

    private void AddCard(CardModel card)
    {
        var face = new Button
        {
            Name = "RemoteHandCard",
            CustomMinimumSize = CardFaceSize,
            ClipContents = true,
            // Keep controller focus on the native hand and creatures.
            FocusMode = Control.FocusModeEnum.None,
            Flat = true
        };
        foreach (var style in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            face.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        _row.AddChild(face);
        var cell = new Cell(card, face);
        _cells.Add(cell);
        var reused = _hand!.Claim(card, face);
        var miniature = reused ?? CreateCard(card);
        if (miniature == null)
            face.Text = card.Id.Entry;
        else
        {
            if (reused == null)
            {
                face.AddChild(miniature);
                miniature.Scale = Vector2.One * MiniatureScale;
                miniature.Position = CardFaceSize / 2f;
                CardNodes.Borrow(miniature);
                _hand.Arrive(card, miniature, face);
            }
            cell.Miniature = miniature;
            ShowCurrentState(miniature);
        }
        face.MouseEntered += () => Inspect(cell);
        face.MouseExited += () => Dismiss(cell);
    }

    private static NCard? CreateCard(CardModel card)
    {
        try
        {
            return NCard.Create(card);
        }
        catch (Exception error)
        {
            Faults.Report($"Card {card.Id} display", error);
            return null;
        }
    }

    // The owner's live values, as their own hand shows them. A card whose live preview fails for a
    // remote owner, as modded cards may, falls back to its deck text.
    private static void ShowCurrentState(NCard node)
    {
        if (!GodotObject.IsInstanceValid(node))
            return;
        try
        {
            node.SetForceUnpoweredPreview(false);
            node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        }
        catch (Exception live)
        {
            Faults.Report($"Card {node.Model?.Id} live text", live);
            try
            {
                node.SetForceUnpoweredPreview(true);
                node.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            }
            catch (Exception deck)
            {
                Faults.Report($"Card {node.Model?.Id} deck text", deck);
            }
        }
    }

    private void Inspect(Cell cell)
    {
        if (_inspected == cell || !_interactive || !CanInspect || NHoverTipSet.shouldBlockHoverTips ||
            NCombatRoom.Instance?.Ui is not { } combatUi)
            return;
        ClearPreview();
        // One enlarged card at a time across every remote hand.
        if (_previewOwner != this && _previewOwner != null)
            _previewOwner.ClearPreview();
        _enlarged = CreateCard(cell.Card);
        if (_enlarged == null)
            return;
        _inspected = cell;
        _previewOwner = this;
        _preview = new Control { Name = "AnimatedPlayerHandPreview", MouseFilter = Control.MouseFilterEnum.Ignore };
        combatUi.AddChild(_preview);
        _preview.AddChild(_enlarged);
        CardNodes.Borrow(_enlarged);
        ShowCurrentState(_enlarged);
        PositionPreview();
        NHoverTipSet.Remove(_preview);
        _tips = NHoverTipSet.CreateAndShow(_preview, cell.Card.HoverTips, TipAlignment());
        // Keyword and generated-card tips belong to combat too, not the game-wide hover surface.
        _tips?.Reparent(_preview);
    }

    private void Dismiss(Cell cell)
    {
        if (_inspected == cell)
            ClearPreview();
    }

    internal void ClearPreview(bool teardown = false)
    {
        CardNodes.Return(_enlarged);
        if (GodotObject.IsInstanceValid(_preview))
        {
            NHoverTipSet.Remove(_preview!);
            _preview!.Hide();
            // Leave the tree now so a same-frame replacement keeps the preview name.
            if (!teardown)
                _preview.GetParent()?.RemoveChild(_preview);
            _preview.QueueFree();
        }
        if (_previewOwner == this)
            _previewOwner = null;
        _preview = null;
        _enlarged = null;
        _tips = null;
        _inspected = null;
    }

    private HoverTipAlignment TipAlignment() =>
        _preview!.GetGlobalRect().GetCenter().X > Root.GetViewportRect().Size.X / 2f
            ? HoverTipAlignment.Left : HoverTipAlignment.Right;

    private void PositionPreview()
    {
        if (_preview == null || _enlarged == null || _inspected == null)
            return;
        var viewport = Root.GetViewportRect().Size;
        var source = _inspected.Face.GetGlobalTransform() * new Rect2(Vector2.Zero, _inspected.Face.Size);
        var top = Mathf.Clamp(RemoteHands.TopMargin(), 12f, Mathf.Max(12f, viewport.Y - 24f));
        var scale = Mathf.Min(1f, Mathf.Min((viewport.X - 24f) / NCard.defaultSize.X,
            (viewport.Y - top - 12f) / NCard.defaultSize.Y));
        _enlarged.Scale = Vector2.One * Mathf.Max(0.1f, scale);
        _preview.Size = _enlarged.GetCurrentSize();
        var x = source.End.X + 12f;
        if (x + _preview.Size.X > viewport.X - 12f)
            x = source.Position.X - _preview.Size.X - 12f;
        _preview.GlobalPosition = new Vector2(
            Mathf.Clamp(x, 12f, Mathf.Max(12f, viewport.X - _preview.Size.X - 12f)),
            Mathf.Clamp(source.Position.Y, top, Mathf.Max(top, viewport.Y - _preview.Size.Y - 12f)));
        _enlarged.Position = _preview.Size / 2f;
        if (GodotObject.IsInstanceValid(_tips))
            _tips!.SetAlignment(_preview, TipAlignment());
    }

    /// <summary>Applies a scale and global top-left computed by <see cref="RemoteHands"/>.</summary>
    internal void Place(float scale, Vector2 globalPosition)
    {
        if (Root.Scale.X != scale)
            Root.Scale = Vector2.One * scale;
        if (Root.GlobalPosition != globalPosition)
            Root.GlobalPosition = globalPosition;
    }

    internal bool Shown => !Stopped && GodotObject.IsInstanceValid(Anchor) && Anchor.IsVisibleInTree() &&
        !Player.Creature.IsDead;

    internal void Process(double delta)
    {
        if (Stopped)
            return;
        if (!GodotObject.IsInstanceValid(Anchor) || !Anchor.IsInsideTree() || Player.Creature.CombatState == null ||
            CombatManager.Instance.IsOverOrEnding)
        {
            Stop();
            return;
        }
        if (_hand == null)
            SafeRefresh();
        Root.Visible = Shown;
        var interactive = CanInspect && !LocalPlayerAiming;
        if (interactive != _interactive)
        {
            _interactive = interactive;
            Root.MouseBehaviorRecursive = interactive
                ? Control.MouseBehaviorRecursiveEnum.Inherited
                : Control.MouseBehaviorRecursiveEnum.Disabled;
        }
        _hand?.Process(delta, Root.IsVisibleInTree());
        if (!Root.IsVisibleInTree() || !_interactive || NHoverTipSet.shouldBlockHoverTips)
            ClearPreview();
        else
            PositionPreview();
    }

    internal void Stop()
    {
        if (Stopped)
            return;
        Stopped = true;
        if (_stateTracker != null)
            _stateTracker.CombatStateChanged -= OnCombatStateChanged;
        _stateTracker = null;
        ClearPreview(teardown: true);
        _hand?.Dispose();
        _hand = null;
        _status?.Detach();
        _status = null;
        foreach (var cell in _cells)
            CardNodes.Return(cell.Miniature);
        _cells.Clear();
        if (GodotObject.IsInstanceValid(Root))
        {
            Root.TreeExiting -= Stop;
            // Stop may run while the combat room is tearing down its children; freeing is always safe.
            Root.Hide();
            Root.QueueFree();
        }
    }
}
