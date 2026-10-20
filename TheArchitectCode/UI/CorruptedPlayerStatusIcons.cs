using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.addons.mega_text;

namespace TheArchitect.TheArchitectCode.UI;

// The Corrupted Player's energy, stars and card piles, drawn as power-sized icons ahead of its
// native powers. They are presentation only: no PowerModel exists, so nothing can remove or react to them.
internal sealed class CorruptedPlayerStatusIcons
{
    private static readonly ConditionalWeakTable<NPowerContainer, CorruptedPlayerStatusIcons> Attached = new();
    private static readonly AccessTools.FieldRef<NPowerContainer, List<NPower>> PowerNodes =
        AccessTools.FieldRefAccess<NPowerContainer, List<NPower>>("_powerNodes");
    private static readonly AccessTools.FieldRef<NPowerContainer, Vector2?> OriginalPosition =
        AccessTools.FieldRefAccess<NPowerContainer, Vector2?>("_originalPosition");
    private static readonly Action<NPowerContainer> UpdatePositions =
        AccessTools.MethodDelegate<Action<NPowerContainer>>(AccessTools.Method(typeof(NPowerContainer), "UpdatePositions"));
    // The compact star the multiplayer party panel uses.
    private const string StarIconPath = "res://images/ui/combat/energy_star.png";

    private readonly NCreature _anchor;
    private readonly NPowerContainer _container;
    private readonly Player _player;
    private readonly PlayerCombatState _state;
    private readonly Func<bool> _canInspect;
    private readonly Action _beforeOpen;
    private readonly List<Control> _icons = [];
    private readonly Dictionary<PileType, (TextureRect Icon, MegaLabel Count)> _piles = [];
    private readonly TextureRect _energyIcon;
    private readonly MegaLabel _energy;
    private readonly Control _stars;
    private readonly MegaLabel _starCount;
    private bool _detached;

    private CorruptedPlayerStatusIcons(NCreature anchor, NPowerContainer container, Player player,
        Func<bool> canInspect, Action beforeOpen)
    {
        _anchor = anchor;
        _container = container;
        _player = player;
        _state = player.PlayerCombatState
            ?? throw new InvalidOperationException("Corrupted Player status icons require an active combat state.");
        _canInspect = canInspect;
        _beforeOpen = beforeOpen;
        var energyTexture = ResourceLoader.Load<Texture2D>(player.Character.CardPool.EnergyIconPath, null,
            ResourceLoader.CacheMode.Reuse);
        (_, _energyIcon, _energy) = AddIcon("Energy", energyTexture,
            Tip("CORRUPTED_ENERGY"));
        (_stars, _, _starCount) = AddIcon("Stars",
            ResourceLoader.Load<Texture2D>(StarIconPath, null, ResourceLoader.CacheMode.Reuse), Tip("CORRUPTED_STARS"));
        _stars.Visible = false;
        AddPile(PileType.Draw, "draw_pile", "CORRUPTED_DRAW_PILE", () => _state.DrawPile);
        AddPile(PileType.Discard, "discard_pile", "CORRUPTED_DISCARD_PILE", () => _state.DiscardPile);
        AddPile(PileType.Exhaust, "exhaust_pile", "CORRUPTED_EXHAUST_PILE", () => _state.ExhaustPile);
        _state.EnergyChanged += OnEnergyChanged;
        _state.StarsChanged += OnStarsChanged;
        Refresh();
    }

    internal static CorruptedPlayerStatusIcons Attach(NCreature anchor, Player player, Func<bool> canInspect,
        Action beforeOpen)
    {
        var container = anchor.GetNode<Control>("%HealthBar").GetNode<NPowerContainer>("%PowerContainer");
        if (Attached.TryGetValue(container, out _))
            throw new InvalidOperationException($"{anchor.Name} already shows Corrupted Player status icons.");
        var icons = new CorruptedPlayerStatusIcons(anchor, container, player, canInspect, beforeOpen);
        Attached.Add(container, icons);
        UpdatePositions(container);
        return icons;
    }

    internal bool Uses(Player player) => !_detached && _player == player;

    internal Control? Icon(string name) => _icons.FirstOrDefault(icon => icon.Name == name);

    internal void Refresh()
    {
        if (_detached)
            return;
        var energy = _state.Energy;
        _energy.SetTextAutoSize(energy.ToString());
        _energy.AddThemeColorOverride(ThemeConstants.Label.FontColor, energy == 0 ? StsColors.red : StsColors.cream);
        _energyIcon.Modulate = energy == 0 ? Colors.DarkGray : Colors.White;
        _starCount.SetTextAutoSize(_state.Stars.ToString());
        foreach (var (pile, (_, count)) in _piles)
            count.SetTextAutoSize(Pile(pile).Cards.Count.ToString());
        // Like the native star counter, stars stay visible once shown.
        var stars = _stars.Visible || _player.Character.ShouldAlwaysShowStarCounter || _state.Stars > 0;
        if (stars != _stars.Visible)
        {
            _stars.Visible = stars;
            UpdatePositions(_container);
        }
    }

    internal Vector2? PileIconCenter(PileType pile) =>
        !_detached && _piles.TryGetValue(pile, out var entry) && entry.Icon.IsVisibleInTree()
            ? entry.Icon.GetGlobalTransform() * (entry.Icon.Size / 2f)
            : null;

    internal void Detach()
    {
        if (_detached)
            return;
        _detached = true;
        _state.EnergyChanged -= OnEnergyChanged;
        _state.StarsChanged -= OnStarsChanged;
        if (GodotObject.IsInstanceValid(_anchor))
            _anchor.HideHoverTips();
        foreach (var icon in _icons.Where(GodotObject.IsInstanceValid))
        {
            icon.GetParent()?.RemoveChild(icon);
            icon.QueueFree();
        }
        _icons.Clear();
        if (!GodotObject.IsInstanceValid(_container))
            return;
        Attached.Remove(_container);
        UpdatePositions(_container);
    }

    private CardPile Pile(PileType pile) => pile switch
    {
        PileType.Draw => _state.DrawPile,
        PileType.Discard => _state.DiscardPile,
        PileType.Exhaust => _state.ExhaustPile,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, null)
    };

    private void OnEnergyChanged(int oldEnergy, int newEnergy) => Refresh();

    private void OnStarsChanged(int oldStars, int newStars) => Refresh();

    private static HoverTip Tip(string key) => new(
        new LocString("static_hover_tips", $"THEARCHITECT-{key}.title"),
        new LocString("static_hover_tips", $"THEARCHITECT-{key}.description"));

    private void AddPile(PileType pile, string texture, string tip, Func<CardPile> getPile)
    {
        var (root, icon, count) = AddIcon(pile + "Pile",
            PreloadManager.Cache.GetTexture2D($"res://images/packed/combat_ui/{texture}.png"), Tip(tip));
        _piles[pile] = (icon, count);
        var button = (Button)root;
        button.FocusMode = Control.FocusModeEnum.All;
        button.Pressed += () =>
        {
            if (_detached || !_canInspect())
                return;
            _anchor.HideHoverTips();
            _beforeOpen();
            NCardPileScreen.ShowScreen(getPile(), []);
        };
    }

    private (Control Root, TextureRect Icon, MegaLabel Amount) AddIcon(string name, Texture2D texture,
        params IHoverTip[] tips)
    {
        // Reuse the native power's icon and amount label so these match neighboring buffs exactly.
        var power = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("combat/power")).Instantiate<Control>();
        var icon = power.GetNode<TextureRect>("Icon");
        var amount = power.GetNode<MegaLabel>("AmountLabel");
        Control root = name.EndsWith("Pile") ? new Button { Flat = true } : new Control();
        root.Name = name;
        root.MouseFilter = Control.MouseFilterEnum.Stop;
        root.CustomMinimumSize = power.CustomMinimumSize;
        root.Size = power.Size;
        if (root is Button button)
            foreach (var style in new[] { "normal", "hover", "pressed", "disabled", "focus" })
                button.AddThemeStyleboxOverride(style, new StyleBoxEmpty());
        foreach (Control child in new Control[] { icon, amount })
        {
            power.RemoveChild(child);
            child.MouseFilter = Control.MouseFilterEnum.Ignore;
            root.AddChild(child);
        }
        power.Free();
        icon.Texture = texture;
        icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        void Show()
        {
            if (_detached || !_canInspect())
                return;
            icon.Scale = Vector2.One * 1.1f;
            _anchor.ShowHoverTips(tips);
        }
        void Hide()
        {
            icon.Scale = Vector2.One;
            if (GodotObject.IsInstanceValid(_anchor))
                _anchor.HideHoverTips();
        }
        root.MouseEntered += Show;
        root.MouseExited += Hide;
        root.FocusEntered += Show;
        root.FocusExited += Hide;
        _container.AddChild(root);
        _icons.Add(root);
        return (root, icon, amount);
    }

    // Lay out these icons first, then the native powers, with the native grid and centering.
    private void Layout()
    {
        var nodes = _icons.Where(icon => icon.Visible).Concat(PowerNodes(_container)).ToArray();
        if (nodes.Length == 0)
            return;
        var step = nodes[0].Size.X;
        var columns = Mathf.Max(Mathf.CeilToInt(nodes.Length / 2f), Mathf.CeilToInt(_container.Size.X / step));
        for (var index = 0; index < nodes.Length; index++)
            nodes[index].Position = new Vector2(step * (index % columns), index / columns * step);
        var width = step * Mathf.Min(columns, nodes.Length);
        _container.Position = (OriginalPosition(_container) ?? _container.Position) +
            Vector2.Left * Mathf.Max(0f, width - _container.Size.X) / 2f;
    }

    [HarmonyPatch(typeof(NPowerContainer), "UpdatePositions")]
    private static class PowerLayoutPatch
    {
        private static void Postfix(NPowerContainer __instance)
        {
            if (Attached.TryGetValue(__instance, out var icons))
                icons.Layout();
        }
    }
}
