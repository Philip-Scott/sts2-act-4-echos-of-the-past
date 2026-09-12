using Godot;
using MegaCrit.Sts2.Core.Assets;
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
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.addons.mega_text;
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
    private static readonly Vector2 CardFaceSize = new(120, 160);
    private NCreature _anchor = null!;
    private Creature _creature = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _row = null!;
    private Label _heading = null!;
    private HBoxContainer _resources = null!;
    private Control _energySlot = null!;
    private NEnergyCounter? _energy;
    private Control _starSlot = null!;
    private NStarCounter? _stars;
    private MegaLabel _draw = null!;
    private MegaLabel _discard = null!;
    private MegaLabel _exhaust = null!;
    private PlayerCombatState? _nativeState;
    private Action _refreshPlan = null!;
    private readonly List<CardCell> _cells = [];
    private CardCell? _inspected;
    private Control? _preview;
    private NCard? _enlarged;
    private NHoverTipSet? _tips;
    private bool _nativeDisplay;
    private bool _partyDisplay;
    private bool _stopped;
    private CombatStateTracker? _stateTracker;
    private (int Draw, int Discard, int Exhaust)? _pileCounts;
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
            ZIndex = 30,
            _anchor = anchor,
            _creature = creature,
            _partyDisplay = creature.CombatState?.Enemies.Count(enemy => enemy.Monster is CorruptedPlayer) > 1,
            _refreshPlan = refreshPlan
        };
        anchor.AddChild(panel);
        return panel;
    }

    public override void _Ready()
    {
        if (_partyDisplay)
            Scale = Vector2.One * 0.55f;
        var toolbar = new HBoxContainer();
        toolbar.AddThemeConstantOverride("separation", 8);
        _heading = new Label { Text = "Corrupted Player · play →", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _heading.AddThemeFontSizeOverride("font_size", 16);
        toolbar.AddChild(_heading);
        _resources = new HBoxContainer { Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _resources.AddThemeConstantOverride("separation", 8);
        toolbar.AddChild(_resources);
        _energySlot = new Control
        {
            Name = "Energy",
            CustomMinimumSize = new Vector2(72, 64),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Current / maximum energy; not a future damage forecast.\n" +
                "Plays left to right, reconsidering after each card. Choices select the first valid option.\n" +
                "Attack intent: any Attack in your CURRENT hand when the Corrupted Player checks, regardless of cost.\n" +
                "Installed modded cards use their own effects. Ally-only cards need a living corrupted teammate."
        };
        _resources.AddChild(_energySlot);
        _starSlot = new Control
        {
            Name = "Stars",
            CustomMinimumSize = new Vector2(64, 64),
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "The Corrupted Player's current stars.",
            Visible = false
        };
        _resources.AddChild(_starSlot);
        _draw = AddPileButton("Draw", () => _nativeState!.DrawPile);
        _discard = AddPileButton("Discard", () => _nativeState!.DiscardPile);
        _exhaust = AddPileButton("Exhaust", () => _nativeState!.ExhaustPile);
        var previous = AddToolbarButton("‹", "Scroll hand left");
        var next = AddToolbarButton("›", "Scroll hand right");
        previous.Pressed += () => _scroll.ScrollHorizontal -= 100;
        next.Pressed += () => _scroll.ScrollHorizontal += 100;
        toolbar.AddChild(previous);
        toolbar.AddChild(next);
        _scroll = new ScrollContainer
        {
            Name = "HandScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(0, CardFaceSize.Y)
        };
        _scroll.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(_scroll);
        AddChild(toolbar);
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_row);
        _stateTracker = CombatManager.Instance.StateTracker;
        _stateTracker.CombatStateChanged += OnCombatStateChanged;
        if (_partyDisplay)
            _partyLayout = CorruptedPartyTelegraphLayout.Acquire(_creature.CombatState!);
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
        if (_row.GetChildCount() > 0 && _cells.Count == cards.Count && _cells.Select(cell =>
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
        int scroll = _scroll.ScrollHorizontal;
        ClearPreview();
        _cells.Clear();
        foreach (var child in _row.GetChildren())
        {
            _row.RemoveChild(child);
            child.QueueFree();
        }
        if (cards.Count == 0)
            _row.AddChild(new Label { Text = "No cards", CustomMinimumSize = CardFaceSize });
        foreach (var card in cards)
            AddCard(card);
        _scroll.ScrollHorizontal = scroll;
    }

    private static Button AddToolbarButton(string text, string tooltip) => new()
    {
        Text = text,
        TooltipText = tooltip,
        Flat = true,
        CustomMinimumSize = new Vector2(32, 32),
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        FocusMode = FocusModeEnum.All
    };

    private MegaLabel AddPileButton(string name, Func<CardPile> getPile)
    {
        var button = new Button
        {
            Name = name + "Pile",
            Flat = true,
            CustomMinimumSize = new Vector2(72, 64),
            FocusMode = FocusModeEnum.All,
            TooltipText = $"View the Corrupted Player's {name.ToLowerInvariant()} pile"
        };
        foreach (var style in new[] { "normal", "hover", "pressed", "disabled" })
            button.AddThemeStyleboxOverride(style, new StyleBoxEmpty());

        var visuals = new Control
        {
            Name = "Visuals",
            Size = new Vector2(80, 80),
            Scale = Vector2.One * 0.75f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        // Reuse the native artwork, badge and typography without registering the
        // player's pile hotkeys or native combat UI animations on the enemy's HUD.
        var scene = PreloadManager.Cache.GetScene($"res://scenes/combat/{name.ToLowerInvariant()}_pile.tscn")
            .Instantiate<Control>();
        foreach (var path in new[] { "Icon", "CountContainer" })
        {
            var child = scene.GetNode<Control>(path);
            scene.RemoveChild(child);
            visuals.AddChild(child);
        }
        scene.Free();
        // Both badges sit to the right in the compact strip; the native discard
        // badge sits to the left because that pile normally hugs the screen edge.
        if (name != "Exhaust")
        {
            var badge = visuals.GetNode<Control>("CountContainer");
            badge.SetAnchorsPreset(LayoutPreset.TopLeft);
            badge.Position = new Vector2(48, 36);
        }
        IgnoreMouse(visuals);
        button.AddChild(visuals);
        var count = visuals.GetNode<MegaLabel>("CountContainer/Count");
        void UpdateHighlight()
        {
            var color = button.IsHovered() || button.HasFocus() ? new Color(1.2f, 1.2f, 1.2f) : Colors.White;
            visuals.Modulate = color;
        }
        button.MouseEntered += UpdateHighlight;
        button.MouseExited += UpdateHighlight;
        button.FocusEntered += UpdateHighlight;
        button.FocusExited += UpdateHighlight;
        button.Pressed += () =>
        {
            ClearPreview();
            NCardPileScreen.ShowScreen(getPile(), []);
        };
        _resources.AddChild(button);
        return count;
    }

    public void SetNativePlayer(Player player)
    {
        if (_stopped)
            return;
        var state = player.PlayerCombatState
            ?? throw new InvalidOperationException("The Corrupted Player HUD requires an active combat state.");
        if (!_nativeDisplay)
            _layoutInputs = null;
        _nativeDisplay = true;
        _nativeState = state;
        _heading.Hide();
        _resources.Show();
        if (_energy == null)
        {
            _energy = NEnergyCounter.Create(player)
                ?? throw new InvalidOperationException("Could not create the Corrupted Player's native energy counter.");
            _energySlot.AddChild(_energy);
            _energy.PivotOffset = Vector2.Zero;
            _energy.Position = new Vector2(4, 0);
            _energy.Scale = Vector2.One * 0.5f;
            IgnoreMouse(_energy);
        }
        if (_stars == null)
        {
            _stars = PreloadManager.Cache.GetScene("res://scenes/combat/energy_counters/star_counter.tscn")
                .Instantiate<NStarCounter>();
            // The native scene shares its shader resource; each actor animates its own count.
            var icon = _stars.GetNode<Control>("Icon");
            icon.Material = (Material)icon.Material.Duplicate();
            _starSlot.AddChild(_stars);
            _stars.SetAnchorsPreset(LayoutPreset.TopLeft);
            _stars.Position = Vector2.Zero;
            _stars.Scale = Vector2.One * 0.5f;
            IgnoreMouse(_stars);
            _stars.Initialize(player);
            state.StarsChanged += OnStarsChanged;
            UpdateStarVisibility();
        }
        var counts = (Draw: state.DrawPile.Cards.Count, Discard: state.DiscardPile.Cards.Count,
            Exhaust: state.ExhaustPile.Cards.Count);
        if (_pileCounts?.Draw != counts.Draw)
            _draw.SetTextAutoSize(counts.Draw.ToString());
        if (_pileCounts?.Discard != counts.Discard)
            _discard.SetTextAutoSize(counts.Discard.ToString());
        if (_pileCounts?.Exhaust != counts.Exhaust)
            _exhaust.SetTextAutoSize(counts.Exhaust.ToString());
        _pileCounts = counts;
    }

    private void UpdateStarVisibility()
    {
        _starSlot.Visible = _stars?.Visible == true;
        _layoutInputs = null;
    }

    private void OnStarsChanged(int oldStars, int newStars) => UpdateStarVisibility();

    private void UnsubscribeStars()
    {
        if (_nativeState != null)
            _nativeState.StarsChanged -= OnStarsChanged;
    }

    internal void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        UnsubscribeState();
        UnsubscribeStars();
        ReleaseLayout();
        SetProcess(false);
        Hide();
        ClearPreview();
        if (GodotObject.IsInstanceValid(_energy))
        {
            // Hiding or QueueFree alone leaves native combat callbacks subscribed
            // until after the owner's CombatState is gone. Exit the tree now.
            _energySlot.RemoveChild(_energy!);
            _energy!.QueueFree();
        }
        _energy = null;
        if (GodotObject.IsInstanceValid(_stars))
        {
            _starSlot.RemoveChild(_stars!);
            _stars!.QueueFree();
        }
        _stars = null;
    }

    private void AddCard(CorruptedPlayerTelegraphCard entry)
    {
        var cell = new VBoxContainer { CustomMinimumSize = CardFaceSize };
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
        var intents = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        cell.AddChild(intents);
        var binding = new CardCell(entry, face, intents);
        _cells.Add(binding);
        if (entry.Card is { } card)
        {
            var miniature = NCard.Create(card);
            if (miniature != null)
            {
                miniature.SetForceUnpoweredPreview(true);
                face.AddChild(miniature);
                binding.Miniature = miniature;
                miniature.Scale = Vector2.One * 0.36f;
                miniature.Position = CardFaceSize / 2f;
                IgnoreMouse(miniature);
                if (!entry.PlayOrder.HasValue && !entry.NativeCurrentState)
                    miniature.Modulate = new Color(0.42f, 0.42f, 0.42f, 0.65f);
                else if (entry.Unsupported)
                    miniature.Modulate = new Color(1f, 0.72f, 0.95f);
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

    private static void IgnoreMouse(Node node)
    {
        if (node is Control control)
        {
            control.MouseFilter = MouseFilterEnum.Ignore;
            control.FocusMode = FocusModeEnum.None;
        }
        foreach (var child in node.GetChildren())
            IgnoreMouse(child);
    }

    private void Inspect(CardCell cell)
    {
        if (_inspected == cell || cell.Entry.Card == null ||
            NCapstoneContainer.Instance?.InUse == true || NHoverTipSet.shouldBlockHoverTips ||
            NGame.Instance?.HoverTipsContainer is not { } hoverContainer)
            return;
        ClearPreview();
        _enlarged = NCard.Create(cell.Entry.Card);
        if (_enlarged == null)
            return;
        _inspected = cell;
        _preview = new Control { Name = "CorruptedPlayerCardPreview", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 1000 };
        hoverContainer.AddChild(_preview);
        _enlarged.SetForceUnpoweredPreview(true);
        _preview.AddChild(_enlarged);
        IgnoreMouse(_enlarged);
        UpdateCardText(_enlarged, cell.Entry);
        PositionPreview();
        // Only the detached preview owns tips, including native keyword/generated-card tips.
        NHoverTipSet.Remove(_preview);
        _tips = NHoverTipSet.CreateAndShow(_preview, cell.Entry.Card.HoverTips, TipAlignment());
    }

    private void DismissHover(Control owner)
    {
        if (_inspected?.Face == owner)
            ClearPreview();
    }

    private void ClearPreview()
    {
        if (GodotObject.IsInstanceValid(_preview))
        {
            NHoverTipSet.Remove(_preview!);
            _preview!.Hide();
            _preview.QueueFree();
        }
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
        var source = _partyDisplay
            ? _inspected.Face.GetGlobalTransform() * new Rect2(Vector2.Zero, _inspected.Face.Size)
            : _inspected.Face.GetGlobalRect();
        float scale = Mathf.Min(1f, Mathf.Min((viewport.X - 24f) / NCard.defaultSize.X,
            (viewport.Y - 24f) / NCard.defaultSize.Y));
        _enlarged.Scale = Vector2.One * Mathf.Max(0.1f, scale);
        _preview.Size = _enlarged.GetCurrentSize();
        float x = source.End.X + 12f;
        if (x + _preview.Size.X > viewport.X - 12f)
            x = source.Position.X - _preview.Size.X - 12f;
        _preview.GlobalPosition = new Vector2(
            Mathf.Clamp(x, 12f, Mathf.Max(12f, viewport.X - _preview.Size.X - 12f)),
            Mathf.Clamp(source.GetCenter().Y - _preview.Size.Y / 2f, 12f,
                Mathf.Max(12f, viewport.Y - _preview.Size.Y - 12f)));
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
            return;
        }
        if (_creature.IsDead)
        {
            Hide();
            ClearPreview();
            return;
        }
        UpdateLayout();
        if (!IsVisibleInTree() || NCapstoneContainer.Instance?.InUse == true || NHoverTipSet.shouldBlockHoverTips)
            ClearPreview();
        else
            PositionPreview();
    }

    private void UpdateLayout()
    {
        var viewport = GetViewportRect().Size;
        var above = _anchor.Visuals.IntentPosition.GlobalPosition;
        var anchor = _anchor.GetGlobalTransform();
        var spacing = _partyLayout?.Spacing(_anchor) ?? float.PositiveInfinity;
        var width = Mathf.Min(680f, Mathf.Max(220f, viewport.X - 32f));
        var partyPosition = _partyLayout?.Position(_anchor, new Vector2(width, 248), viewport, PartyTopMargin())
            ?? Vector2.Zero;
        var inputs = new LayoutInputs(viewport, above, anchor, spacing, Size, partyPosition);
        if (_layoutInputs == inputs)
            return;
        LayoutUpdateCount++;
        CustomMinimumSize = new Vector2(width, 248);
        Size = new Vector2(width, 248);
        if (_partyDisplay)
        {
            Scale = Vector2.One * CorruptedPartyLayoutGeometry.Scale(spacing, Size.X, anchor.Scale.X);
        }
        var displaySize = _partyDisplay ? (GetGlobalTransform() * new Rect2(Vector2.Zero, Size)).Size : Size;
        var topMargin = _partyDisplay ? 100f : 12f;
        GlobalPosition = _partyDisplay ? partyPosition : new Vector2(
            Mathf.Clamp(above.X - displaySize.X / 2f, 16f, Mathf.Max(16f, viewport.X - displaySize.X - 16f)),
            Mathf.Clamp(above.Y - displaySize.Y + (_nativeDisplay ? 24f : -24f), topMargin,
                Mathf.Max(topMargin, viewport.Y - displaySize.Y - 12f)));
        _layoutInputs = inputs with { PanelSize = Size };
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

    private void UnsubscribeState()
    {
        if (_stateTracker == null)
            return;
        _stateTracker.CombatStateChanged -= OnCombatStateChanged;
        _stateTracker = null;
    }

    public override void _ExitTree()
    {
        UnsubscribeState();
        UnsubscribeStars();
        ReleaseLayout();
        ClearPreview();
    }
}
