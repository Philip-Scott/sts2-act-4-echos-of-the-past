using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.UI;

internal static class CorruptedPlayerAttackVfx
{
    private static readonly Dictionary<MethodInfo, MethodInfo> Replacements = new()
    {
        [AccessTools.Method(typeof(NDaggerSprayFlurryVfx), nameof(NDaggerSprayFlurryVfx.Create),
            [typeof(Creature), typeof(Color), typeof(bool)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(DaggerFlurry)),
        [AccessTools.Method(typeof(NDaggerSprayImpactVfx), nameof(NDaggerSprayImpactVfx.Create),
            [typeof(Creature), typeof(Color), typeof(bool)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(DaggerImpact)),
        [AccessTools.Method(typeof(NScratchVfx), nameof(NScratchVfx.Create), [typeof(Creature), typeof(bool)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(Scratch)),
        [AccessTools.Method(typeof(NStabVfx), nameof(NStabVfx.Create),
            [typeof(Creature), typeof(bool), typeof(VfxColor)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(Stab)),
        [AccessTools.Method(typeof(NHyperbeamVfx), nameof(NHyperbeamVfx.Create),
            [typeof(Creature), typeof(Creature)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(Hyperbeam)),
        [AccessTools.Method(typeof(NHyperbeamImpactVfx), nameof(NHyperbeamImpactVfx.Create),
            [typeof(Creature), typeof(Creature)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(HyperbeamImpact)),
        [AccessTools.Method(typeof(NSweepingBeamVfx), nameof(NSweepingBeamVfx.Create),
            [typeof(Creature), typeof(List<Creature>)])] =
            AccessTools.Method(typeof(CorruptedPlayerAttackVfx), nameof(SweepingBeam))
    };

    internal static MethodInfo? Replacement(MethodInfo called) => Replacements.GetValueOrDefault(called);

    // Only native card call sites use these bridges. Impact factories receive the
    // victim, not the attacker; their hard-coded player-facing flags are relative
    // to that victim's side. Monster factories keep their explicit native flags.
    private static bool ImpactDirection(Creature? target, bool goingRight) =>
        target?.Side == CombatSide.Player ? !goingRight : goingRight;

    private static NDaggerSprayFlurryVfx? DaggerFlurry(Creature owner, Color tint, bool goingRight) =>
        NDaggerSprayFlurryVfx.Create(owner, tint,
            NativeCorruptedPlayer.TryGet(owner, out _) ? !goingRight : goingRight);

    private static NDaggerSprayImpactVfx? DaggerImpact(Creature target, Color tint, bool goingRight) =>
        NDaggerSprayImpactVfx.Create(target, tint, ImpactDirection(target, goingRight));

    private static NScratchVfx? Scratch(Creature target, bool goingRight) =>
        NScratchVfx.Create(target, ImpactDirection(target, goingRight));

    private static NStabVfx? Stab(Creature? target, bool facingEnemies, VfxColor color) =>
        NStabVfx.Create(target, ImpactDirection(target, facingEnemies), color);

    private static Vector2 BeamOrigin(NativeCorruptedPlayer actor)
    {
        var node = actor.Body.GetCreatureNode()
            ?? throw new InvalidOperationException("The Corrupted Player beam has no source node.");
        var offset = actor.Player.Character is Defect ? Defect.EyelineOffset : Vector2.Zero;
        return node.VfxSpawnPosition + new Vector2(-offset.X, offset.Y);
    }

    private static NHyperbeamVfx? Hyperbeam(Creature owner, Creature target)
    {
        var effect = NHyperbeamVfx.Create(owner, target);
        if (effect != null && NativeCorruptedPlayer.TryGet(owner, out var actor))
        {
            var origin = BeamOrigin(actor);
            effect.GlobalPosition = origin;
            // Hyperbeam already rotates towards its target; do not also flip it.
            effect.ApplyRotation(origin, target.GetCreatureNode()!.VfxSpawnPosition);
        }
        return effect;
    }

    private static NHyperbeamImpactVfx? HyperbeamImpact(Creature owner, Creature target)
    {
        var effect = NHyperbeamImpactVfx.Create(owner, target);
        if (effect != null && NativeCorruptedPlayer.TryGet(owner, out var actor))
            effect.ApplyRotation(BeamOrigin(actor), target.GetCreatureNode()!.VfxSpawnPosition);
        return effect;
    }

    private static NSweepingBeamVfx? SweepingBeam(Creature owner, List<Creature> targets)
    {
        var effect = NSweepingBeamVfx.Create(owner, targets);
        if (effect != null && NativeCorruptedPlayer.TryGet(owner, out var actor))
        {
            effect.GlobalPosition = BeamOrigin(actor);
            effect.Scale = new Vector2(-effect.Scale.X, effect.Scale.Y);
        }
        return effect;
    }
}
