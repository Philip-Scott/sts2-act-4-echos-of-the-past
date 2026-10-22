using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.TestSupport;

namespace AnimatedPlayerHands;

/// <summary>
/// Gives every remote player's creature a <see cref="RemoteHandPanel"/> and drives them each frame.
/// Panels are plain Controls rather than script subclasses, so the mod needs no Godot source generators.
/// </summary>
internal static class RemoteHands
{
    private static readonly List<RemoteHandPanel> Panels = [];
    private static readonly AccessTools.FieldRef<NCreature, bool> IsRemotePlayerOrPet =
        AccessTools.FieldRefAccess<NCreature, bool>("_isRemotePlayerOrPet");
    private static readonly AccessTools.FieldRef<NCreature, NCreatureStateDisplay> StateDisplay =
        AccessTools.FieldRefAccess<NCreature, NCreatureStateDisplay>("_stateDisplay");
    private static readonly List<Entry> Layout = [];
    private static bool _driving;

    private readonly record struct Inputs(Vector2 HealthBarTop, float Row, float AnchorScale);

    private sealed class Entry(RemoteHandPanel panel)
    {
        public RemoteHandPanel Panel { get; } = panel;
        public Inputs Inputs;
    }

    private static (Vector2 Viewport, float Top, int Count)? _placementInputs;
    private static (NCreature Owner, HandLayoutGeometry.Bounds Bounds)[] _obstacles = [];

    internal static IReadOnlyList<RemoteHandPanel> Active => Panels.Where(panel => !panel.Stopped).ToArray();

    internal static bool ShouldShow(NCreature node) =>
        TestMode.IsOff &&
        node.Entity is { IsPlayer: true, Player: { } player, CombatState: { } combat } &&
        combat.RunState.Players.Count > 1 &&
        LocalContext.NetId.HasValue &&
        !LocalContext.IsMe(player);

    internal static void Attach(NCreature node)
    {
        if (!ShouldShow(node) || Panels.Any(panel => panel.Anchor == node && !panel.Stopped))
            return;
        KeepHealthBarVisible(node);
        Panels.Add(new RemoteHandPanel(node, node.Entity.Player!));
        _placementInputs = null;
        if (_driving || Engine.GetMainLoop() is not SceneTree tree)
            return;
        tree.ProcessFrame += Tick;
        _driving = true;
    }

    // Natively a remote player's health bar and powers show only while hovered. Treating the creature
    // like the local player's keeps them up, shows the name on hover, and restores them on revival.
    private static void KeepHealthBarVisible(NCreature node)
    {
        IsRemotePlayerOrPet(node) = false;
        if (node.Entity.IsAlive)
            StateDisplay(node).AnimateIn(HealthBarAnimMode.SpawnedAtCombatStart);
    }

    private static void Tick()
    {
        Panels.RemoveAll(panel => panel.Stopped);
        if (Panels.Count == 0)
        {
            if (Engine.GetMainLoop() is SceneTree tree)
                tree.ProcessFrame -= Tick;
            _driving = false;
            Layout.Clear();
            return;
        }
        var delta = Panels[0].Root.IsInsideTree() ? Panels[0].Root.GetProcessDeltaTime() : 0d;
        foreach (var panel in Panels.ToArray())
        {
            try
            {
                panel.Process(delta);
            }
            catch (Exception error)
            {
                // A hand that keeps failing would fail every frame; close it for this combat instead.
                Faults.Report($"Player {panel.Player.NetId}'s hand", error);
                panel.Stop();
            }
        }
        try
        {
            UpdateLayout();
        }
        catch (Exception error)
        {
            Faults.Report("Remote hand layout", error);
        }
    }

    // Top-center of the creature's native health bar, falling back to its intent position.
    internal static Vector2 HealthBarTop(NCreature node)
    {
        if (node.GetNodeOrNull<Control>("%HealthBar") is { } display &&
            display.GetNodeOrNull<Control>("%HealthBar") is { } bar)
        {
            var rect = bar.GetGlobalRect();
            return new Vector2(rect.Position.X + rect.Size.X / 2f, rect.Position.Y);
        }
        return node.Visuals.IntentPosition.GlobalPosition;
    }

    private static void UpdateLayout()
    {
        Layout.RemoveAll(entry => entry.Panel.Stopped || !entry.Panel.Shown);
        foreach (var panel in Panels)
            if (panel.Shown && Layout.All(entry => entry.Panel != panel))
            {
                Layout.Add(new Entry(panel));
                _placementInputs = null;
            }
        if (Layout.Count == 0)
            return;
        var changed = false;
        foreach (var entry in Layout)
        {
            var anchor = entry.Panel.Anchor;
            var inputs = new Inputs(HealthBarTop(anchor), anchor.Position.Y,
                Mathf.Abs(anchor.GetGlobalTransform().Scale.X));
            changed |= inputs != entry.Inputs;
            entry.Inputs = inputs;
        }
        var obstacles = StatusBounds();
        changed |= !obstacles.SequenceEqual(_obstacles);
        _obstacles = obstacles;
        var viewport = Layout[0].Panel.Root.GetViewportRect().Size;
        var placement = (viewport, TopMargin(), Layout.Count);
        if (!changed && _placementInputs == placement)
            return;
        _placementInputs = placement;
        var owners = Layout.Select(entry => new HandLayoutGeometry.Owner(entry.Inputs.HealthBarTop.X,
            entry.Inputs.HealthBarTop.Y, entry.Inputs.Row, entry.Inputs.AnchorScale,
            obstacles.Where(obstacle => obstacle.Owner != entry.Panel.Anchor).Select(obstacle => obstacle.Bounds).ToArray()))
            .ToArray();
        var placed = HandLayoutGeometry.Fit(owners, viewport.X, viewport.Y, placement.Item2);
        for (var index = 0; index < Layout.Count; index++)
        {
            var root = Layout[index].Panel.Root;
            var parentScale = root.GetParent() is CanvasItem parent ? Mathf.Abs(parent.GetGlobalTransform().Scale.X) : 1f;
            Layout[index].Panel.Place(placed[index].Width / RemoteHandPanel.PanelSize.X / Mathf.Max(parentScale, 0.0001f),
                new Vector2(placed[index].X, placed[index].Y));
        }
    }

    // Every player's visible health bar and the status icons and powers beneath it.
    internal static (NCreature Owner, HandLayoutGeometry.Bounds Bounds)[] StatusBounds()
    {
        if (NCombatRoom.Instance is not { } room)
            return [];
        var bounds = new List<(NCreature, HandLayoutGeometry.Bounds)>();
        foreach (var node in room.CreatureNodes)
        {
            if (!node.Entity.IsPlayer || !GodotObject.IsInstanceValid(node) ||
                node.GetNodeOrNull<Control>("%HealthBar") is not { } display ||
                !display.IsVisibleInTree() || display.Modulate.A < 0.01f)
                continue;
            var rect = display.GetNodeOrNull<Control>("%HealthBar")?.GetGlobalRect() ?? display.GetGlobalRect();
            if (display.GetNodeOrNull<Control>("%PowerContainer") is { } powers)
                foreach (var icon in powers.GetChildren().OfType<Control>().Where(icon => icon.IsVisibleInTree()))
                    rect = rect.Merge(icon.GetGlobalRect());
            bounds.Add((node, new HandLayoutGeometry.Bounds(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y)));
        }
        return bounds.ToArray();
    }

    // Keep hands below the top bar and the relic inventory.
    internal static float TopMargin()
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

    [HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
    private static class CreatureReadyPatch
    {
        private static void Postfix(NCreature __instance)
        {
            try
            {
                Attach(__instance);
            }
            catch (Exception error)
            {
                // A display problem must never break combat setup.
                ModEntry.Logger.Error($"Could not attach a remote hand: {error}");
            }
        }
    }
}
