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
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.addons.mega_text;

namespace TheArchitect.TheArchitectCode.UI;

public sealed record ChallengerTelegraphIntent(AbstractIntent Icon, string Value);

public sealed record ChallengerTelegraphCard(
    CardModel? Card, string InstanceId, string Name, int? PlayOrder, bool Unsupported,
    IReadOnlyList<ChallengerTelegraphIntent> Intents,
    IReadOnlyDictionary<string, decimal> PreviewValues,
    bool NativeCurrentState = false,
    string? Status = null);

public partial class ChallengerTelegraph : VBoxContainer
{
    private static readonly Vector2 CardFaceSize = new(120, 160);
    private NCreature _anchor = null!;
    private Creature _creature = null!;
    private Control? _pinned;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _row = null!;
    private Label _heading = null!;
    private HBoxContainer _resources = null!;
    private Label _energy = null!;
    private Button _draw = null!;
    private Button _discard = null!;
    private Button _exhaust = null!;
    private PlayerCombatState? _nativeState;
    private Action _refreshPlan = null!;
    private readonly List<CardCell> _cells = [];
    private CardCell? _inspected;
    private Control? _preview;
    private NCard? _enlarged;
    private NHoverTipSet? _tips;
    private bool _nativeDisplay;

    private sealed class CardCell(ChallengerTelegraphCard entry, Button face, HBoxContainer intents)
    {
        public ChallengerTelegraphCard Entry = entry;
        public Button Face { get; } = face;
        public HBoxContainer Intents { get; } = intents;
        public NCard? Miniature;
    }

    public static ChallengerTelegraph Attach(NCreature anchor, Creature creature, Action refreshPlan)
    {
        var panel = new ChallengerTelegraph
        {
            Name = "ChallengerTelegraph",
            MouseFilter = MouseFilterEnum.Stop,
            ZIndex = 30,
            _anchor = anchor,
            _creature = creature,
            _refreshPlan = refreshPlan
        };
        anchor.AddChild(panel);
        return panel;
    }

    public override void _Ready()
    {
        var toolbar = new HBoxContainer();
        _heading = new Label { Text = "Challenger · play →", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _heading.AddThemeFontSizeOverride("font_size", 16);
        toolbar.AddChild(_heading);
        _resources = new HBoxContainer { Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        toolbar.AddChild(_resources);
        _energy = new Label
        {
            Name = "Energy",
            TooltipText = "Current / maximum energy",
            CustomMinimumSize = new Vector2(100, 0)
        };
        _resources.AddChild(_energy);
        _draw = AddPileButton("Draw", () => _nativeState!.DrawPile);
        _discard = AddPileButton("Discard", () => _nativeState!.DiscardPile);
        _exhaust = AddPileButton("Exhaust", () => _nativeState!.ExhaustPile);
        var previous = new Button { Text = "‹", FocusMode = FocusModeEnum.All };
        var next = new Button { Text = "›", FocusMode = FocusModeEnum.All };
        var clear = new Button { Text = "Clear", FocusMode = FocusModeEnum.All };
        previous.Pressed += () => _scroll.ScrollHorizontal -= 100;
        next.Pressed += () => _scroll.ScrollHorizontal += 100;
        clear.Pressed += ClearInspection;
        toolbar.AddChild(previous);
        toolbar.AddChild(next);
        toolbar.AddChild(clear);
        _scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true,
            CustomMinimumSize = new Vector2(0, CardFaceSize.Y)
        };
        AddChild(_scroll);
        AddChild(toolbar);
        _row = new HBoxContainer();
        _row.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_row);
    }

    public void ShowPlan(IReadOnlyList<ChallengerTelegraphCard> cards, bool limited)
    {
        _heading.Text = limited ? "Challenger · play → · plan limit" : "Challenger · play →";
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
        var inspectedId = _inspected?.Entry.InstanceId;
        bool wasPinned = _pinned != null;
        int scroll = _scroll.ScrollHorizontal;
        ClearInspection();
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
        if (wasPinned && _cells.FirstOrDefault(cell => cell.Entry.InstanceId == inspectedId) is { } pinned)
        {
            _pinned = pinned.Face;
            Inspect(pinned);
        }
    }

    private Button AddPileButton(string name, Func<CardPile> getPile)
    {
        var button = new Button
        {
            Name = name + "Pile",
            FocusMode = FocusModeEnum.All,
            TooltipText = $"View the Corrupted character's {name.ToLowerInvariant()} pile"
        };
        button.Pressed += () =>
        {
            ClearInspection();
            NCardPileScreen.ShowScreen(getPile(), []);
        };
        _resources.AddChild(button);
        return button;
    }

    public void SetNativeState(PlayerCombatState state)
    {
        _nativeDisplay = true;
        _nativeState = state;
        _heading.Hide();
        _resources.Show();
        _energy.Text = $"Energy {state.Energy}/{state.MaxEnergy}";
        _draw.Text = $"Draw {state.DrawPile.Cards.Count}";
        _discard.Text = $"Discard {state.DiscardPile.Cards.Count}";
        _exhaust.Text = $"Exhaust {state.ExhaustPile.Cards.Count}";
        _energy.TooltipText = "Current / maximum energy; not a future damage forecast.\n" +
            "Plays left to right, reconsidering after each card. Choices select the first valid option.\n" +
            "Attack intent: any Attack in your CURRENT hand when the Challenger checks, regardless of cost.\n" +
            "Co-op-only cards and third-party card/modifier effects are preserved but unsupported.";
    }

    private void AddCard(ChallengerTelegraphCard entry)
    {
        var cell = new VBoxContainer { CustomMinimumSize = CardFaceSize };
        _row.AddChild(cell);
        var face = new Button
        {
            Name = "ChallengerCard",
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
            face.Pressed += () =>
            {
                ClearInspection();
                _pinned = face;
                Inspect(binding);
            };
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

    private static void UpdateCardText(NCard node, ChallengerTelegraphCard entry)
    {
        node.SetForceUnpoweredPreview(true);
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
        if ((_pinned != null && _pinned != cell.Face) || _inspected == cell || cell.Entry.Card == null ||
            NCapstoneContainer.Instance?.InUse == true || NHoverTipSet.shouldBlockHoverTips ||
            NGame.Instance?.HoverTipsContainer is not { } hoverContainer)
            return;
        ClearPreview();
        _enlarged = NCard.Create(cell.Entry.Card);
        if (_enlarged == null)
            return;
        _inspected = cell;
        _preview = new Control { Name = "ChallengerCardPreview", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 1000 };
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
        if (_pinned != owner && _inspected?.Face == owner)
            ClearPreview();
    }

    private void ClearInspection()
    {
        ClearPreview();
        _pinned = null;
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
        var source = _inspected.Face.GetGlobalRect();
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
        if (!GodotObject.IsInstanceValid(_anchor) || _creature.IsDead ||
            (_nativeDisplay && CombatManager.Instance.IsOverOrEnding))
        {
            Hide();
            ClearInspection();
            return;
        }
        _refreshPlan();
        var viewport = GetViewportRect().Size;
        var width = Mathf.Min(680f, Mathf.Max(220f, viewport.X - 32f));
        CustomMinimumSize = new Vector2(width, 248);
        Size = new Vector2(width, 248);
        var above = _anchor.Visuals.IntentPosition.GlobalPosition;
        GlobalPosition = new Vector2(
            Mathf.Clamp(above.X - width / 2f, 16f, Mathf.Max(16f, viewport.X - width - 16f)),
            Mathf.Clamp(above.Y - Size.Y + (_nativeDisplay ? 24f : -24f), 12f, Mathf.Max(12f, viewport.Y - Size.Y - 12f)));
        if (_nativeDisplay && _anchor.OrbManager is { } manager)
        {
            var slots = manager.GetNode<Control>("%Orbs").GetChildren().OfType<Control>()
                .Where(slot => slot.Visible).Select(slot => slot.GetGlobalRect().Grow(12f)).ToArray();
            if (slots.Any(slot => slot.Intersects(GetGlobalRect())))
                GlobalPosition = new Vector2(Mathf.Max(12f, slots.Min(slot => slot.Position.X) -
                    GetGlobalRect().Size.X - 16f), GlobalPosition.Y);
        }
        if (!IsVisibleInTree() || NCapstoneContainer.Instance?.InUse == true || NHoverTipSet.shouldBlockHoverTips)
            ClearInspection();
        else
            PositionPreview();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } && _pinned != null)
        {
            ClearInspection();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree() => ClearInspection();
}
