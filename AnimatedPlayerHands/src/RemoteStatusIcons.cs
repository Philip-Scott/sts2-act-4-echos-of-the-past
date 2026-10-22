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

namespace AnimatedPlayerHands;

/// <summary>
/// A remote player's energy, stars and card piles, drawn as power-sized icons ahead of their powers
/// under the health bar. They are display only: no PowerModel exists, so nothing can react to them.
/// </summary>
internal sealed class RemoteStatusIcons
{
    private static readonly ConditionalWeakTable<NPowerContainer, RemoteStatusIcons> Attached = new();
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
    private readonly Dictionary<PileType, (Control Root, TextureRect Icon, MegaLabel Count)> _piles = [];
    private readonly TextureRect _energyIcon;
    private readonly MegaLabel _energy;
    private readonly Control _stars;
    private readonly MegaLabel _starCount;
    private bool _detached;

    private RemoteStatusIcons(NCreature anchor, NPowerContainer container, Player player, Func<bool> canInspect,
        Action beforeOpen)
    {
        _anchor = anchor;
        _container = container;
        _player = player;
        _state = player.PlayerCombatState
            ?? throw new InvalidOperationException("Remote status icons require an active combat state.");
        _canInspect = canInspect;
        _beforeOpen = beforeOpen;
        var energyTip = new LocString("static_hover_tips", "ENERGY_COUNT.description");
        energyTip.Add("energyPrefix", EnergyIconHelper.GetPrefix(player.Character.CardPool));
        (_, _energyIcon, _energy) = AddIcon("Energy",
            ResourceLoader.Load<Texture2D>(player.Character.CardPool.EnergyIconPath, null, ResourceLoader.CacheMode.Reuse),
            new HoverTip(new LocString("static_hover_tips", "ENERGY_COUNT.title"), energyTip));
        (_stars, _, _starCount) = AddIcon("Stars",
            ResourceLoader.Load<Texture2D>(StarIconPath, null, ResourceLoader.CacheMode.Reuse),
            Tip("STAR_COUNT", description: ("singleStarIcon", "[img]res://images/packed/sprite_fonts/star_icon.png[/img]")));
        _stars.Visible = false;
        AddPile(PileType.Draw, "draw_pile", "DRAW_PILE");
        AddPile(PileType.Discard, "discard_pile", "DISCARD_PILE");
        AddPile(PileType.Exhaust, "exhaust_pile", "EXHAUST_PILE");
        _state.EnergyChanged += OnEnergyChanged;
        _state.StarsChanged += OnStarsChanged;
        foreach (var pile in _piles.Keys)
            Pile(pile).ContentsChanged += SafeRefresh;
        Refresh();
    }

    internal static RemoteStatusIcons Attach(NCreature anchor, Player player, Func<bool> canInspect, Action beforeOpen)
    {
        var container = anchor.GetNode<Control>("%HealthBar").GetNode<NPowerContainer>("%PowerContainer");
        if (Attached.TryGetValue(container, out _))
            throw new InvalidOperationException($"{anchor.Name} already shows remote status icons.");
        var icons = new RemoteStatusIcons(anchor, container, player, canInspect, beforeOpen);
        Attached.Add(container, icons);
        UpdatePositions(container);
        return icons;
    }

    internal bool Uses(Player player) => !_detached && _player == player && player.PlayerCombatState == _state;

    internal Control? Icon(string name) => _icons.FirstOrDefault(icon => icon.Name == name);

    internal Control PileButton(PileType pile) => _piles[pile].Root;

    internal void Refresh()
    {
        if (_detached)
            return;
        var energy = _state.Energy;
        _energy.SetTextAutoSize(energy.ToString());
        _energy.AddThemeColorOverride(ThemeConstants.Label.FontColor, energy == 0 ? StsColors.red : StsColors.cream);
        _energyIcon.Modulate = energy == 0 ? Colors.DarkGray : Colors.White;
        _starCount.SetTextAutoSize(_state.Stars.ToString());
        foreach (var (pile, (_, _, count)) in _piles)
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
        foreach (var pile in _piles.Keys)
            Pile(pile).ContentsChanged -= SafeRefresh;
        if (GodotObject.IsInstanceValid(_anchor))
            _anchor.HideHoverTips();
        foreach (var icon in _icons.Where(GodotObject.IsInstanceValid))
            icon.QueueFree();
        _icons.Clear();
        if (!GodotObject.IsInstanceValid(_container))
            return;
        Attached.Remove(_container);
        // Freed icons leave the container at the end of the frame; lay the powers out again then.
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(_container) && _container.IsInsideTree())
                UpdatePositions(_container);
        }).CallDeferred();
    }

    private CardPile Pile(PileType pile) => pile switch
    {
        PileType.Draw => _state.DrawPile,
        PileType.Discard => _state.DiscardPile,
        PileType.Exhaust => _state.ExhaustPile,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, null)
    };

    // These events fire inside the game's commands; a display failure must not interrupt them.
    private void SafeRefresh()
    {
        try
        {
            Refresh();
        }
        catch (Exception error)
        {
            Faults.Report($"Player {_player.NetId}'s status icons", error);
        }
    }

    private void OnEnergyChanged(int oldEnergy, int newEnergy) => SafeRefresh();

    private void OnStarsChanged(int oldStars, int newStars) => SafeRefresh();

    // The game's own tooltips, with the variables their native counters supply.
    private static HoverTip Tip(string key, (string Name, string Value)? title = null,
        (string Name, string Value)? description = null)
    {
        var titleText = new LocString("static_hover_tips", $"{key}.title");
        var descriptionText = new LocString("static_hover_tips", $"{key}.description");
        if (title is var (titleName, titleValue))
            titleText.Add(titleName, titleValue);
        if (description is var (descriptionName, descriptionValue))
            descriptionText.Add(descriptionName, descriptionValue);
        return new HoverTip(titleText, descriptionText);
    }

    private void AddPile(PileType pile, string texture, string tip)
    {
        var (root, icon, count) = AddIcon(pile + "Pile",
            PreloadManager.Cache.GetTexture2D($"res://images/packed/combat_ui/{texture}.png"),
            // Remote piles have no keyboard shortcut.
            Tip(tip, title: ("Hotkey", "None")));
        _piles[pile] = (root, icon, count);
        var button = (Button)root;
        button.Pressed += () =>
        {
            if (_detached || !_canInspect())
                return;
            _anchor.HideHoverTips();
            _beforeOpen();
            NCardPileScreen.ShowScreen(Pile(pile), []);
        };
    }

    private (Control Root, TextureRect Icon, MegaLabel Amount) AddIcon(string name, Texture2D texture,
        params IHoverTip[] tips)
    {
        // Reuse the native power's icon and amount label so these match the neighbouring powers exactly.
        var power = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("combat/power")).Instantiate<Control>();
        var icon = power.GetNode<TextureRect>("Icon");
        var amount = power.GetNode<MegaLabel>("AmountLabel");
        Control root = name.EndsWith("Pile") ? new Button { Flat = true, FocusMode = Control.FocusModeEnum.None } : new Control();
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
            try
            {
                if (Attached.TryGetValue(__instance, out var icons))
                    icons.Layout();
            }
            catch (Exception error)
            {
                Faults.Report("Remote status icon layout", error);
            }
        }
    }
}
