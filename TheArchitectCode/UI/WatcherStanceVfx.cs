using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.UI;

public partial class WatcherStanceVfx : Node2D
{
    private const string NodeName = "WatcherStanceVfx";
    private PowerModel _power = null!;
    private NCreature _creature = null!;
    private Control _bounds = null!;
    private readonly List<Polygon2D> _layers = [];
    private Transform2D _boundsTransform;
    private Vector2 _boundsSize;
    private bool _subscribed;
    private bool _stopped;
    internal double AnimationTime { get; private set; }
    internal int GeometryBuildCount { get; private set; }
    internal PowerModel Power => _power;

    internal static void Attach(PowerModel power)
    {
        // Model-only simulations have no creature node. _Ready also restores visuals
        // for powers applied before their view was created (including saved combats).
        if (power.Owner.GetCreatureNode() is { } creature && creature.IsNodeReady())
            Attach(power, creature);
    }

    internal static void Detach(PowerModel power)
    {
        if (power.Owner.GetCreatureNode()?.Visuals.GetNodeOrNull<WatcherStanceVfx>(NodeName) is { } effect &&
            ReferenceEquals(effect._power, power))
            effect.Stop();
    }

    private static void Attach(PowerModel power, NCreature creature)
    {
        if (!ReferenceEquals(power.Owner, creature.Entity) || power.Owner.IsDead)
            return;
        var existing = creature.Visuals.GetNodeOrNull<WatcherStanceVfx>(NodeName);
        if (existing != null)
        {
            if (ReferenceEquals(existing._power, power))
                return;
            existing.Stop();
        }
        var effect = new WatcherStanceVfx
        {
            Name = NodeName,
            _power = power,
            _creature = creature,
            _bounds = creature.Visuals.GetNode<Control>("%Bounds")
        };
        creature.Visuals.AddChild(effect);
        // A negative canvas Z would put the aura behind the room background too.
        // Draw the rear quad first at the body's depth, and only lift the front wisps.
        creature.Visuals.MoveChild(effect, 0);
    }

    public override void _Ready()
    {
        foreach (bool front in new[] { false, true })
        {
            var material = new ShaderMaterial { Shader = WatcherStanceShader.Resource };
            material.SetShaderParameter("wrath", _power is WrathStancePower);
            material.SetShaderParameter("front_layer", front);
            var layer = new Polygon2D
            {
                Name = front ? "StanceFront" : "StanceBack",
                ZIndex = front ? 1 : 0,
                Material = material
            };
            AddChild(layer);
            _layers.Add(layer);
        }
        UpdateBounds();
        _power.Removed += Stop;
        _subscribed = true;
    }

    public override void _Process(double delta)
    {
        if (_creature.Entity != _power.Owner || _power.Owner.IsDead ||
            _power.Owner.CombatState == null || !_power.Owner.Powers.Contains(_power))
        {
            Stop();
            return;
        }
        UpdateBounds();
        AnimationTime += delta;
        foreach (var layer in _layers)
            ((ShaderMaterial)layer.Material).SetShaderParameter("effect_time", (float)AnimationTime);
    }

    private void UpdateBounds()
    {
        var transform = GlobalTransform.AffineInverse() * _bounds.GetGlobalTransform();
        var size = _bounds.Size;
        if (GeometryBuildCount > 0 && transform.IsEqualApprox(_boundsTransform) && size.IsEqualApprox(_boundsSize))
            return;
        var rect = new Rect2(transform * Vector2.Zero, Vector2.Zero)
            .Expand(transform * new Vector2(size.X, 0))
            .Expand(transform * size)
            .Expand(transform * new Vector2(0, size.Y));
        if (rect.Size.X <= 0 || rect.Size.Y <= 0)
            throw new InvalidOperationException("Watcher stance VFX requires non-empty creature bounds.");
        _boundsTransform = transform;
        _boundsSize = size;
        var area = rect.GrowIndividual(rect.Size.X * 0.4f, rect.Size.Y * 0.18f,
            rect.Size.X * 0.4f, rect.Size.Y * 0.13f);
        Vector2[] corners = [area.Position, new(area.End.X, area.Position.Y),
            area.End, new(area.Position.X, area.End.Y)];
        foreach (var layer in _layers)
        {
            layer.Polygon = corners;
            ((ShaderMaterial)layer.Material).SetShaderParameter("body_rect",
                new Vector4(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y));
        }
        GeometryBuildCount++;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
            return;
        _power.Removed -= Stop;
        _subscribed = false;
    }

    private void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;
        Visible = false;
        SetProcess(false);
        Unsubscribe();
        // Detach immediately: switching twice in one frame cannot leave a duplicate
        // named node or let the old power remove the replacement effect.
        GetParent()?.RemoveChild(this);
        QueueFree();
    }

    public override void _ExitTree() => Unsubscribe();

    [HarmonyPatch(typeof(NCreature), nameof(NCreature._Ready))]
    internal static class RestoreStanceVisuals
    {
        private static void Postfix(NCreature __instance)
        {
            PowerModel? power = __instance.Entity.GetPower<WrathStancePower>() ??
                (PowerModel?)__instance.Entity.GetPower<CalmStancePower>();
            if (power != null)
                Attach(power, __instance);
        }
    }
}
