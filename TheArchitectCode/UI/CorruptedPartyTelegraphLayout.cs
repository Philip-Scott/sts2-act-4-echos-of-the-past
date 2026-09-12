using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using TheArchitect.TheArchitectCode.Monsters;

namespace TheArchitect.TheArchitectCode.UI;

internal sealed class CorruptedPartyTelegraphLayout
{
    private static readonly ConditionalWeakTable<ICombatState, CorruptedPartyTelegraphLayout> Layouts = new();
    private readonly ICombatState _combat;
    private readonly Dictionary<NCreature, Member> _members = new();
    private int _users;
    private bool _membersDirty = true;
    private ulong _frame = ulong.MaxValue;
    private (int Revision, Vector2 Size, Vector2 Viewport, float Top)? _placementInputs;
    internal int RebuildCount { get; private set; }

    private sealed class Member
    {
        internal Vector2 Position;
        internal float Spacing;
        internal Vector2 Intent;
        internal Transform2D Transform;
        internal Vector2 PanelPosition;
    }

    private CorruptedPartyTelegraphLayout(ICombatState combat)
    {
        _combat = combat;
        combat.CreaturesChanged += InvalidateMembers;
    }

    internal static CorruptedPartyTelegraphLayout Acquire(ICombatState combat)
    {
        var layout = Layouts.GetValue(combat, state => new CorruptedPartyTelegraphLayout(state));
        layout._users++;
        return layout;
    }

    internal void Release()
    {
        if (--_users != 0)
            return;
        _combat.CreaturesChanged -= InvalidateMembers;
        _members.Clear();
        Layouts.Remove(_combat);
    }

    private void InvalidateMembers(ICombatState _)
    {
        _membersDirty = true;
        _frame = ulong.MaxValue;
    }

    internal float Spacing(NCreature anchor)
    {
        var frame = Engine.GetProcessFrames();
        if (_frame != frame)
        {
            _frame = frame;
            bool changed = _membersDirty;
            if (_membersDirty)
            {
                _members.Clear();
                _membersDirty = false;
                foreach (var enemy in _combat.Enemies)
                {
                    if (enemy.Monster is not CorruptedPlayer)
                        continue;
                    if (enemy.GetCreatureNode() is { } node)
                        _members.Add(node, new Member());
                    else
                        _membersDirty = true;
                }
            }
            foreach (var (node, member) in _members)
            {
                if (!GodotObject.IsInstanceValid(node))
                {
                    _membersDirty = true;
                    continue;
                }
                var position = new Vector2(node.Visuals.IntentPosition.GlobalPosition.X, node.Position.Y);
                var intent = node.Visuals.IntentPosition.GlobalPosition;
                var transform = node.GetGlobalTransform();
                changed |= position != member.Position || intent != member.Intent || transform != member.Transform;
                member.Position = position;
                member.Intent = intent;
                member.Transform = transform;
            }
            if (changed)
            {
                RebuildCount++;
                foreach (var (node, member) in _members)
                {
                    member.Spacing = float.PositiveInfinity;
                    foreach (var (otherNode, other) in _members)
                        if (node != otherNode && Math.Abs(other.Position.Y - member.Position.Y) < 120f)
                            member.Spacing = Math.Min(member.Spacing, Math.Abs(other.Position.X - member.Position.X));
                }
            }
        }
        return _members.TryGetValue(anchor, out var entry) ? entry.Spacing : float.PositiveInfinity;
    }

    internal Vector2 Position(NCreature anchor, Vector2 size, Vector2 viewport, float topMargin)
    {
        Spacing(anchor);
        var inputs = (RebuildCount, size, viewport, topMargin);
        if (_placementInputs != inputs)
        {
            var members = _members.Values.ToArray();
            var desired = members.Select(member =>
            {
                var scale = CorruptedPartyLayoutGeometry.Scale(member.Spacing, size.X, member.Transform.Scale.X);
                var displayed = size * member.Transform.Scale * scale;
                return new CorruptedPartyLayoutGeometry.Bounds(
                    member.Intent.X - displayed.X / 2f,
                    Math.Min(member.Intent.Y - displayed.Y + 24f, viewport.Y - displayed.Y - 12f),
                    displayed.X, displayed.Y);
            }).ToArray();
            var placed = CorruptedPartyLayoutGeometry.Place(desired, viewport.X, viewport.Y, topMargin);
            for (var index = 0; index < members.Length; index++)
                members[index].PanelPosition = new Vector2(placed[index].X, placed[index].Y);
            _placementInputs = inputs;
        }
        return _members.TryGetValue(anchor, out var member) ? member.PanelPosition : Vector2.Zero;
    }
}
