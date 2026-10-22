using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.addons.mega_text;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Monsters;

namespace TheArchitect.TheArchitectCode.UI;

public sealed record CorruptedPlayerTelegraphIntent(AbstractIntent Icon, string Value);

public sealed record CorruptedPlayerTelegraphCard(
    CardModel? Card, string InstanceId, string Name, int? PlayOrder, bool Unsupported,
    IReadOnlyList<CorruptedPlayerTelegraphIntent> Intents,
    IReadOnlyDictionary<string, decimal> PreviewValues,
    bool NativeCurrentState = false,
    string? Status = null,
    Creature? PreviewTarget = null);

public partial class CorruptedPlayerTelegraph : VBoxContainer
{
    private static readonly Vector2 CardFaceSize = new(
        CorruptedPartyLayoutGeometry.CardWidth, CorruptedPartyLayoutGeometry.CardHeight);
    private const float MiniatureScale = 0.288f;
    private NCreature _anchor = null!;
    private Creature _creature = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _row = null!;
    private HBoxContainer _toolbar = null!;
    private Label _heading = null!;
    private CorruptedPlayerStatusIcons? _status;
    private CorruptedPlayerHandAnimator? _hand;
    private bool _refreshQueued;
    private bool _handRendered;
    private Action _refreshPlan = null!;
    private readonly List<CardCell> _cells = [];
    private static CorruptedPlayerTelegraph? _previewOwner;
    private CardCell? _inspected;
    private Control? _preview;
    private NCard? _enlarged;
    private NHoverTipSet? _tips;
    private bool _nativeDisplay;
    private bool _partyDisplay;
    private bool _stopped;
    private CombatStateTracker? _stateTracker;
    internal int ContentRefreshCount { get; private set; }
    private CorruptedPartyTelegraphLayout? _partyLayout;
    private readonly record struct LayoutInputs(
        Vector2 Viewport, Vector2 Above, Transform2D Anchor, float Spacing, Vector2 PanelSize,
        Vector2 PartyPosition);
    private LayoutInputs? _layoutInputs;
    internal int LayoutUpdateCount { get; private set; }
    internal int PartyLayoutRebuildCount => _partyLayout?.RebuildCount ?? 0;

    private sealed class CardCell(CorruptedPlayerTelegraphCard entry, Button face, HBoxContainer intents)
    {
        public CorruptedPlayerTelegraphCard Entry = entry;
        public Button Face { get; } = face;
        public HBoxContainer Intents { get; } = intents;
        public NCard? Miniature;
    }

    public static CorruptedPlayerTelegraph Attach(NCreature anchor, Creature creature, Action refreshPlan)
    {
        var panel = new CorruptedPlayerTelegraph
        {
            Name = "CorruptedPlayerTelegraph",
            MouseFilter = MouseFilterEnum.Stop,
            // Like CombatUi, escape the combat scene's negative Z without overtaking GlobalUi.
            ZAsRelative = false,
            _anchor = anchor,
            _creature = creature,
            _partyDisplay = creature.CombatState?.Enemies.Count(enemy => enemy.Monster is CorruptedPlayer) > 1,
            _refreshPlan = refreshPlan
        };
        Host(anchor).AddChild(panel);
        Attached.AddOrUpdate(anchor, panel);
        return panel;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NCreature, CorruptedPlayerTelegraph>
        Attached = new();

    internal static CorruptedPlayerTelegraph Of(NCreature anchor) =>
        Attached.TryGetValue(anchor, out var panel) && GodotObject.IsInstanceValid(panel)
            ? panel
            : throw new InvalidOperationException($"{anchor.Name} has no Corrupted Player telegraph.");

    // GUI picking follows tree order, so hands must come after every creature body; as anchor children,
    // a later creature's hitbox or orbs would take hover from a hand drawn above it.
    private static Node Host(NCreature anchor)
    {
        var container = anchor.GetParent();
        var parent = container?.GetParent();
        if (container == null || parent == null)
            return anchor;
        if (parent.GetNodeOrNull<Control>(HostName) is { } host)
            return host;
        host = new Control { Name = HostName, MouseFilter = MouseFilterEnum.Ignore };
        parent.AddChild(host);
        var last = parent.GetChildren().OfType<Node>()
            .Where(child => child != host && child.GetChildren().OfType<NCreature>().Any())
            .Max(child => child.GetIndex());
        parent.MoveChild(host, last + 1);
        return host;
    }

    private const string HostName = "CorruptedPlayerTelegraphs";

    public override void _Ready()
    {
        if (_partyDisplay)
            Scale = Vector2.One * 0.55f;
        // The hand rests on the health bar; energy and piles sit with the powers beneath it.
        Alignment = AlignmentMode.End;
        _toolbar = new HBoxContainer();
        _heading = new Label { Text = "Corrupted Player · play →", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _heading.AddThemeFontSizeOverride("font_size", 16);
        _toolbar.AddChild(_heading);
        _scroll = new ScrollContainer
        {
            Name = "HandScroll",
            // Hands are capped at ten cards and HandSeparation always fits them.
            HorizontalScrollMode = ScrollContainer.ScrollMode.ShowNever,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(0, CardFaceSize.Y)
        };
        _scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(_scroll);
        AddChild(_toolbar);
        // Center the hand over its health bar; cards still play left to right.
        _row = new HBoxContainer { Alignment = AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _row.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_row);
        _stateTracker = CombatManager.Instance.StateTracker;
        _stateTracker.CombatStateChanged += OnCombatStateChanged;
        if (_partyDisplay)
            _partyLayout = CorruptedPartyTelegraphLayout.Acquire(_creature.CombatState!);
        ActiveScreenContext.Instance.Updated += OnActiveScreenUpdated;
        OnActiveScreenUpdated();
    }

    private bool CanInspect => !_stopped && !_creature.IsDead &&
        NCombatRoom.Instance is { } room && ActiveScreenContext.Instance.IsCurrent(room);

    private void OnActiveScreenUpdated()
    {
        bool active = CanInspect;
        MouseBehaviorRecursive = active ? MouseBehaviorRecursiveEnum.Inherited : MouseBehaviorRecursiveEnum.Disabled;
        FocusBehaviorRecursive = active ? FocusBehaviorRecursiveEnum.Inherited : FocusBehaviorRecursiveEnum.Disabled;
        if (!active)
            ClearPreview();
    }

    private void OnCombatStateChanged(CombatState state)
    {
        if (!_stopped && !_creature.IsDead && _creature.CombatState == state &&
            !CombatManager.Instance.IsOverOrEnding)
            _refreshPlan();
    }

    public void ShowPlan(IReadOnlyList<CorruptedPlayerTelegraphCard> cards, bool limited)
    {
        if (_stopped)
            return;
        ContentRefreshCount++;
        _heading.Text = limited ? "Corrupted Player · play → · plan limit" : "Corrupted Player · play →";
        if (_handRendered && _cells.Count == cards.Count && _cells.Select(cell =>
                (cell.Entry.InstanceId, cell.Entry.PlayOrder, cell.Entry.Unsupported))
            .SequenceEqual(cards.Select(card => (card.InstanceId, card.PlayOrder, card.Unsupported))))
        {
            for (int i = 0; i < cards.Count; i++)
            {
                _cells[i].Entry = cards[i];
                UpdateCell(_cells[i]);
            }

            if (_inspected != null && GodotObject.IsInstanceValid(_enlarged))
                UpdateCardText(_enlarged!, _inspected.Entry);
            return;
        }
        ClearPreview();
        // Cards still in hand keep their node and glide to their new slot instead of snapping.
        foreach (var cell in _cells)
            if (cell is { Miniature: { } node, Entry.Card: { } model } && _hand?.Release(model, node) != true)
                PooledCards.Return(node);
        _cells.Clear();
        foreach (var child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }
        _handRendered = true;
        foreach (var card in cards)
            AddCard(card);
        _hand?.DropUnclaimed();
        ApplyHandSpacing();
    }

    public void SetNativePlayer(Player player)
    {
        if (_stopped)
            return;
        if (player.PlayerCombatState == null)
            throw new InvalidOperationException("The Corrupted Player HUD requires an active combat state.");
        if (!_nativeDisplay)
            _layoutInputs = null;
        _nativeDisplay = true;
        if (_hand?.Uses(player) != true)
        {
            _hand?.Dispose();
            _hand = new CorruptedPlayerHandAnimator(player, CardFaceSize / 2f, MiniatureScale,
                PileIconCenter, TakeMiniature, QueueRefresh);
        }
        _toolbar.Hide();
        if (_status?.Uses(player) != true)
        {
            _status?.Detach();
            _status = CorruptedPlayerStatusIcons.Attach(_anchor, player, () => CanInspect, ClearPreview);
        }
        _status!.Refresh();
    }

    private Vector2? PileIconCenter(PileType pile) => _status?.PileIconCenter(pile);

    // Energy, Stars, DrawPile, DiscardPile or ExhaustPile, shown with the owner's powers.
    internal Control? StatusIcon(string name) => _status?.Icon(name);

    private NCard? TakeMiniature(CardModel card)
    {
        var cell = _cells.FirstOrDefault(cell => cell.Entry.Card == card && cell.Miniature != null);
        if (cell == null)
            return null;
        if (_inspected == cell)
            ClearPreview();
        var node = cell.Miniature;
        cell.Miniature = null;
        return node;
    }

    // Native draws happen inside a single command; refresh once per frame so new cards get slots.
    private void QueueRefresh()
    {
        if (_refreshQueued || _stopped)
            return;
        _refreshQueued = true;
        Callable.From(() =>
        {
            _refreshQueued = false;
            if (GodotObject.IsInstanceValid(this) && !_stopped && !_creature.IsDead &&
                !CombatManager.Instance.IsOverOrEnding)
                _refreshPlan();
        }).CallDeferred();
    }

    internal void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        ActiveScreenContext.Instance.Updated -= OnActiveScreenUpdated;
        UnsubscribeState();
        DetachStatus();
        ReleaseLayout();
        DisposeHand();
        SetProcess(false);
        Hide();
        ClearPreview();
    }

    private void AddCard(CorruptedPlayerTelegraphCard entry)
    {
        var cell = new VBoxContainer { CustomMinimumSize = CardFaceSize };
        cell.AddThemeConstantOverride("separation", (int)CorruptedPartyLayoutGeometry.IntentGap);
        _row.AddChild(cell);
        var face = new Button
        {
            Name = "CorruptedPlayerCard",
            CustomMinimumSize = CardFaceSize,
            ClipContents = true,
            FocusMode = FocusModeEnum.All,
            Flat = true
        };
        cell.AddChild(face);
        var intents = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(0, CorruptedPartyLayoutGeometry.IntentRowHeight)
        };
        cell.AddChild(intents);
        var binding = new CardCell(entry, face, intents);
        _cells.Add(binding);
        if (entry.Card is { } card)
        {
            var reused = _hand?.Claim(card, face);
            var miniature = reused ?? NCard.Create(card);
            if (miniature != null)
            {
                if (reused == null)
                {
                    miniature.SetForceUnpoweredPreview(true);
                    face.AddChild(miniature);
                    miniature.Scale = Vector2.One * MiniatureScale;
                    miniature.Position = CardFaceSize / 2f;
                    PooledCards.Borrow(miniature);
                }
                binding.Miniature = miniature;
                if (!entry.PlayOrder.HasValue && !entry.NativeCurrentState)
                    miniature.Modulate = new Color(0.42f, 0.42f, 0.42f, 0.65f);
                else if (entry.Unsupported)
                    miniature.Modulate = new Color(1f, 0.72f, 0.95f);
                else
                    miniature.Modulate = Colors.White;
                if (reused == null)
                    _hand?.Arrive(card, miniature, face);
            }
            face.MouseEntered += () => Inspect(binding);
            face.FocusEntered += () => Inspect(binding);
            face.MouseExited += () => DismissHover(face);
            face.FocusExited += () => DismissHover(face);
        }
        else
        {
            face.Text = entry.Name;
        }
        if (entry.PlayOrder is { } order)
        {
            var number = new Label { Text = order.ToString(), Position = new Vector2(4, 2), MouseFilter = MouseFilterEnum.Ignore };
            number.AddThemeFontSizeOverride("font_size", 18);
            face.AddChild(number);
        }
        UpdateCell(binding);
    }

    private void UpdateCell(CardCell cell)
    {
        cell.Face.TooltipText = cell.Entry.Status ?? "";
        cell.Intents.Visible = cell.Entry.Unsupported || cell.Entry.Intents.Count > 0;
        if (cell.Miniature != null)
            UpdateCardText(cell.Miniature, cell.Entry);
        foreach (var child in cell.Intents.GetChildren())
        {
            cell.Intents.RemoveChild(child);
            child.QueueFree();
        }
        if (cell.Entry.Unsupported)
        {
            var status = new Label { Text = "Unsupported", TooltipText = cell.Entry.Status ?? "" };
            status.AddThemeFontSizeOverride("font_size", 12);
            cell.Intents.AddChild(status);
        }
        foreach (var intent in cell.Entry.Intents)
        {
            cell.Intents.AddChild(new TextureRect
            {
                Texture = intent.Icon.GetTexture([], _creature),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(22, 22),
                MouseFilter = MouseFilterEnum.Ignore
            });
            if (intent.Value.Length > 0)
            {
                var value = new Label { Text = intent.Value, MouseFilter = MouseFilterEnum.Ignore };
                value.AddThemeFontSizeOverride("font_size", 14);
                cell.Intents.AddChild(value);
            }
        }
    }

    private static void UpdateCardText(NCard node, CorruptedPlayerTelegraphCard entry)
    {
        if (entry.NativeCurrentState && !entry.Unsupported)
        {
            node.SetForceUnpoweredPreview(false);
            node.SetPreviewTarget(entry.PreviewTarget);
            node.UpdateVisuals(PileType.Hand, CardPreviewMode.MultiCreatureTargeting);
            return;
        }
        node.SetForceUnpoweredPreview(true);
        node.SetPreviewTarget(null);
        node.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
        var card = entry.Card!;
        // NCard clears PreviewValue during UpdateVisuals. Apply the frozen values afterwards,
        // then use native formatting without assigning a player owner or invoking preview hooks.
        foreach (var (name, value) in entry.PreviewValues)
            if (card.DynamicVars.TryGetValue(name, out var variable))
                variable.PreviewValue = value;
        string description = card.GetDescriptionForPile(
            entry.PlayOrder.HasValue && !entry.Unsupported ? PileType.Play : PileType.Deck);
        var attacks = entry.Intents.Where(intent => intent.Icon is SingleAttackIntent).ToArray();
        if (attacks.Length > 1)
            description += "\n[gold]Damage · " + string.Join(" · ", attacks.Select(intent => intent.Value)) + "[/gold]";
        node.GetNode<MegaRichTextLabel>("%DescriptionLabel").SetTextAutoSize("[center]" + description + "[/center]");
    }

    private void Inspect(CardCell cell)
    {
        if (_inspected == cell || cell.Entry.Card == null || !CanInspect ||
            NHoverTipSet.shouldBlockHoverTips || NCombatRoom.Instance?.Ui is not { } combatUi)
            return;
        ClearPreview();
        // One enlarged card at a time across every party hand.
        if (_previewOwner != this && GodotObject.IsInstanceValid(_previewOwner))
            _previewOwner!.ClearPreview();
        _enlarged = NCard.Create(cell.Entry.Card);
        if (_enlarged == null)
            return;
        _inspected = cell;
        _previewOwner = this;
        _preview = new Control { Name = "CorruptedPlayerCardPreview", MouseFilter = MouseFilterEnum.Ignore };
        combatUi.AddChild(_preview);
        _enlarged.SetForceUnpoweredPreview(true);
        _preview.AddChild(_enlarged);
        PooledCards.Borrow(_enlarged);
        UpdateCardText(_enlarged, cell.Entry);
        PositionPreview();
        // Only the detached preview owns tips, including native keyword/generated-card tips.
        NHoverTipSet.Remove(_preview);
        _tips = NHoverTipSet.CreateAndShow(_preview, cell.Entry.Card.HoverTips, TipAlignment());
        // Generated-card and keyword tips belong to combat too, not the game-wide hover surface.
        _tips?.Reparent(_preview);
    }

    private void DismissHover(Control owner)
    {
        if (_inspected?.Face == owner)
            ClearPreview();
    }

    private void ClearPreview()
    {
        PooledCards.Return(_enlarged);
        if (GodotObject.IsInstanceValid(_preview))
        {
            NHoverTipSet.Remove(_preview!);
            _preview!.Hide();
            // Leave the tree now so a same-frame replacement keeps the preview name.
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
        _preview!.GetGlobalRect().GetCenter().X > GetViewportRect().Size.X / 2f
            ? HoverTipAlignment.Left : HoverTipAlignment.Right;

    private void PositionPreview()
    {
        if (_preview == null || _enlarged == null || _inspected == null)
            return;
        var viewport = GetViewportRect().Size;
        var source = _inspected.Face.GetGlobalTransform() * new Rect2(Vector2.Zero, _inspected.Face.Size);
        float top = Mathf.Clamp(PartyTopMargin(), 12f, Mathf.Max(12f, viewport.Y - 24f));
        float scale = Mathf.Min(1f, Mathf.Min((viewport.X - 24f) / NCard.defaultSize.X,
            (viewport.Y - top - 12f) / NCard.defaultSize.Y));
        _enlarged.Scale = Vector2.One * Mathf.Max(0.1f, scale);
        _preview.Size = _enlarged.GetCurrentSize();
        float x = source.End.X + 12f;
        if (x + _preview.Size.X > viewport.X - 12f)
            x = source.Position.X - _preview.Size.X - 12f;
        _preview.GlobalPosition = new Vector2(
            Mathf.Clamp(x, 12f, Mathf.Max(12f, viewport.X - _preview.Size.X - 12f)),
            Mathf.Clamp(source.Position.Y, top,
                Mathf.Max(top, viewport.Y - _preview.Size.Y - 12f)));
        _enlarged.Position = _preview.Size / 2f;
        if (GodotObject.IsInstanceValid(_tips))
            _tips!.SetAlignment(_preview, TipAlignment());
    }

    public override void _Process(double delta)
    {
        if (_stopped)
            return;
        if (!GodotObject.IsInstanceValid(_anchor) ||
            (_nativeDisplay && (_creature.CombatState == null || CombatManager.Instance.IsOverOrEnding)))
        {
            Stop();
            if (!GodotObject.IsInstanceValid(_anchor))
                QueueFree();
            return;
        }
        if (_creature.IsDead)
        {
            Hide();
            ClearPreview();
            DisposeHand();
            return;
        }
        Visible = _anchor.IsVisibleInTree();
        UpdateLayout();
        _hand?.Process(delta, IsVisibleInTree());
        if (!IsVisibleInTree() || !CanInspect || NHoverTipSet.shouldBlockHoverTips)
            ClearPreview();
        else
            PositionPreview();
    }

    private void UpdateLayout()
    {
        var viewport = GetViewportRect().Size;
        var above = CorruptedPartyTelegraphLayout.HealthBarTop(_anchor);
        var anchor = _anchor.GetGlobalTransform();
        var spacing = _partyLayout?.Spacing(_anchor) ?? float.PositiveInfinity;
        var width = Mathf.Min(680f, Mathf.Max(220f, viewport.X - 32f));
        // Native hands reserve their intent row; the pre-native heading may add a little more.
        var height = Mathf.Max(CorruptedPartyLayoutGeometry.HandHeight, GetCombinedMinimumSize().Y);
        var partyPosition = _partyLayout?.Position(_anchor, new Vector2(width, height), viewport, PartyTopMargin())
            ?? Vector2.Zero;
        var inputs = new LayoutInputs(viewport, above, anchor, spacing, Size, partyPosition);
        if (_layoutInputs == inputs)
            return;
        LayoutUpdateCount++;
        CustomMinimumSize = new Vector2(width, 0);
        Size = new Vector2(width, height);
        ApplyHandSpacing();
        var parentScale = GetParent() is CanvasItem parent ? Mathf.Abs(parent.GetGlobalTransform().Scale.X) : 1f;
        var anchorScale = Mathf.Abs(anchor.Scale.X) / Mathf.Max(parentScale, 0.0001f);
        Scale = Vector2.One * (_partyDisplay
            ? CorruptedPartyLayoutGeometry.Scale(spacing, Size.X, Mathf.Abs(anchor.Scale.X)) * anchorScale
            : anchorScale);
        var displaySize = (GetGlobalTransform() * new Rect2(Vector2.Zero, Size)).Size;
        var topMargin = _partyDisplay ? 100f : 12f;
        GlobalPosition = _partyDisplay ? partyPosition : new Vector2(
            Mathf.Clamp(above.X - displaySize.X / 2f, 16f, Mathf.Max(16f, viewport.X - displaySize.X - 16f)),
            Mathf.Clamp(CorruptedPartyLayoutGeometry.TopAboveHealthBar(above.Y, displaySize.Y), topMargin,
                Mathf.Max(topMargin, viewport.Y - displaySize.Y - 12f)));
        _layoutInputs = inputs with { PanelSize = Size };
    }

    private void ApplyHandSpacing()
    {
        var width = Mathf.Max(Size.X, CustomMinimumSize.X);
        if (width <= 0f)
            width = 680f;
        _row.AddThemeConstantOverride("separation",
            CorruptedPartyLayoutGeometry.HandSeparation(_cells.Count, CardFaceSize.X, width));
    }

    private static float PartyTopMargin()
    {
        var top = 100f;
        if (NRun.Instance?.GlobalUi is not { } ui)
            return top;
        // TopBar itself fills the viewport; only its background occupies the HUD strip.
        if (ui.TopBar.GetNodeOrNull<Control>("BgImage") is { } background && background.IsVisibleInTree())
            top = Math.Max(top, background.GetGlobalRect().End.Y + 12f);
        var inventory = ui.RelicInventory;
        if (inventory.IsVisibleInTree())
        {
            var height = inventory.GetBottomOfInventory().Y - inventory.GetDefaultPosition().Y;
            var bottom = inventory.GetGlobalTransform() * new Vector2(0, height);
            top = Math.Max(top, bottom.Y + 12f);
        }
        return top;
    }

    private void ReleaseLayout()
    {
        _partyLayout?.Release();
        _partyLayout = null;
    }

    private void DetachStatus()
    {
        _status?.Detach();
        _status = null;
    }

    private void DisposeHand()
    {
        _hand?.Dispose();
        _hand = null;
        foreach (var cell in _cells)
        {
            PooledCards.Return(cell.Miniature);
            cell.Miniature = null;
        }
    }

    private void UnsubscribeState()
    {
        if (_stateTracker == null)
            return;
        _stateTracker.CombatStateChanged -= OnCombatStateChanged;
        _stateTracker = null;
    }

    public override void _ExitTree()
    {
        ActiveScreenContext.Instance.Updated -= OnActiveScreenUpdated;
        UnsubscribeState();
        DetachStatus();
        ReleaseLayout();
        DisposeHand();
        ClearPreview();
    }
}
