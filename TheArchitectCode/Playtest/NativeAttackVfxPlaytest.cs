using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeAttackVfxPlaytest
{
    internal static void PrepareSnapshot()
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Attack VFX probes require the disposable smoke environment.");
        var character = ModelDb.Character<Defect>().Id.ToString();
        var cards = Enumerable.Range(0, 5).Select(_ => JsonSerializer.SerializeToElement(
            ModelDb.Card<Wound>().ToMutable().ToSerializable(),
            JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
        CorruptedPlayerStore.Commit("attack-vfx-probe", "Probe", new CorruptedPlayerSnapshot(
            character, 1000, cards, CorruptedPlayerSnapshot.Hash(character, 1000, cards)));
    }

    internal static async Task Run(NGame game, RunState run, CorruptedPlayerEnvelope snapshot)
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Attack VFX probes require the disposable smoke environment.");
        ArchitectLifecycle.AppendAct(run);
        await RunManager.Instance.SetActInternal(3);
        ArchitectRun.Get(run).EntrySnapshot = snapshot;
        await RunManager.Instance.EnterMapCoord(run.Map.BossMapPoint.coord);
        var human = run.Players.Single();
        await NativeDemoPlaytest.PlayerTurn(human, 1);
        var monster = (CorruptedPlayer)human.Creature.CombatState!.Enemies.Single().Monster!;
        var actor = monster.Native ?? throw new InvalidOperationException("Attack VFX actor was not created.");
        await Exercise(human, actor);
        game.GetTree().Quit();
    }

    private static async Task Exercise(Player human, NativeCorruptedPlayer actor)
    {
        var game = NGame.Instance!;
        var combat = human.Creature.CombatState!;
        await CreatureCmd.SetMaxHp(human.Creature, 1000);
        await CreatureCmd.Heal(human.Creature, 1000);
        human.Creature.RemoveAllPowersInternalExcept();
        actor.Body.RemoveAllPowersInternalExcept();
        OrbCmd.RemoveSlots(actor.Player, actor.State.OrbQueue.Capacity);
        human.MaxEnergy = actor.Player.MaxEnergy = 3;
        var seen = new List<Effect>();
        var corruptedEffects = false;
        var nativeSweepingScale = Vector2.One;
        void Observe(Node node)
        {
            if (node is NDaggerSprayFlurryVfx or NDaggerSprayImpactVfx or NScratchVfx or
                NStabVfx or NHyperbeamVfx or NHyperbeamImpactVfx or NSweepingBeamVfx or
                NBigSlashVfx or NShivThrowVfx)
            {
                var visual = (Node2D)node;
                var source = corruptedEffects ? actor.Body : human.Creature;
                var target = corruptedEffects ? human.Creature : actor.Body;
                seen.Add(new Effect(node.GetType(), visual.GlobalPosition, visual.Scale, visual.Rotation,
                    node is NStabVfx stab &&
                    AccessTools.FieldRefAccess<NStabVfx, bool>(stab, "_facingEnemies"),
                    source.GetCreatureNode()!.VfxSpawnPosition, target.GetCreatureNode()!.VfxSpawnPosition));
            }
        }
        game.GetTree().NodeAdded += Observe;
        try
        {
            await Probe<Hyperbeam>(typeof(NHyperbeamVfx), typeof(NHyperbeamImpactVfx));
            await Probe<SweepingBeam>(typeof(NSweepingBeamVfx));
            await Probe<DaggerSpray>(typeof(NDaggerSprayFlurryVfx), typeof(NDaggerSprayImpactVfx));
            await Probe<DaggerThrow>(typeof(NDaggerSprayFlurryVfx), typeof(NDaggerSprayImpactVfx));
            await Probe<Claw>(typeof(NScratchVfx));
            await Probe<RipAndTear>(typeof(NScratchVfx));
            await Probe<Skewer>(typeof(NStabVfx));
            await Probe<PerfectedStrike>(typeof(NBigSlashVfx));
            await Probe<Shiv>(typeof(NShivThrowVfx));

            // Native monster calls must retain explicitly supplied directions,
            // even while the Corrupted Player is present in the same encounter.
            var left = NDaggerSprayImpactVfx.Create(human.Creature, Colors.White, false)!;
            var right = NDaggerSprayImpactVfx.Create(human.Creature, Colors.White, true)!;
            Require(left.Scale.X == -1 && right.Scale.X == 1, "non-card factory directions remain unchanged");
            left.Free();
            right.Free();
        }
        finally
        {
            game.GetTree().NodeAdded -= Observe;
        }
        MainFile.Logger.Info("NATIVE ATTACK VFX PASSED");

        async Task Probe<T>(params Type[] expected) where T : CardModel
        {
            foreach (var pile in human.PlayerCombatState!.AllPiles)
                foreach (var card in pile.Cards.ToArray())
                {
                    pile.RemoveInternal(card);
                    combat.RemoveCard(card);
                }
            human.PlayerCombatState.ResetEnergy();
            var sample = combat.CreateCard<T>(human);
            var targeted = sample.TargetType == TargetType.AnyEnemy;
            combat.RemoveCard(sample);
            seen.Clear();
            corruptedEffects = false;
            await NativeDemoPlaytest.HumanPlay<T>(human, targeted ? actor.Body : null);
            Check(false, typeof(T).Name, expected);

            foreach (var pile in actor.State.AllPiles)
                foreach (var card in pile.Cards.ToArray())
                {
                    pile.RemoveInternal(card);
                    combat.RemoveCard(card);
                }
            var attack = combat.CreateCard<T>(actor.Player);
            actor.State.Hand.AddInternal(attack);
            for (var i = 0; i < 4; i++)
                actor.State.Hand.AddInternal(combat.CreateCard<Wound>(actor.Player));
            actor.State.ResetEnergy();
            var hp = human.Creature.CurrentHp;
            seen.Clear();
            corruptedEffects = true;
            var turn = human.PlayerCombatState.TurnNumber;
            PlayerCmd.EndTurn(human, false);
            await NativeDemoPlaytest.PlayerTurn(human, turn + 1);
            Require(human.Creature.CurrentHp < hp, $"{typeof(T).Name} still damages the human");
            Check(true, typeof(T).Name, expected);
            actor.AssertIdentity();
        }

        void Check(bool corrupted, string card, Type[] expected)
        {
            foreach (var type in expected)
                Require(seen.Any(effect => effect.Type == type), $"{card} emitted {type.Name} ({corrupted})");
            foreach (var effect in seen)
            {
                var beamOrigin = effect.SourcePosition + (corrupted
                    ? new Vector2(-Defect.EyelineOffset.X, Defect.EyelineOffset.Y) : Vector2.Zero);
                if (effect.Type == typeof(NDaggerSprayFlurryVfx) ||
                    effect.Type == typeof(NDaggerSprayImpactVfx) || effect.Type == typeof(NBigSlashVfx))
                    Require(effect.Scale.X == (corrupted ? -1 : 1), $"{card} follows attack direction ({corrupted})");
                if (effect.Type == typeof(NScratchVfx))
                    Require(effect.Scale.X == (corrupted ? 1 : -1), $"{card} scratch follows attack direction ({corrupted})");
                if (effect.Type == typeof(NStabVfx))
                    Require(effect.FacingEnemies == !corrupted, $"{card} stab follows attack direction ({corrupted})");
                if (effect.Type == typeof(NHyperbeamVfx) || effect.Type == typeof(NHyperbeamImpactVfx))
                {
                    var expectedAngle = (effect.TargetPosition - beamOrigin).Angle();
                    Require(Mathf.IsEqualApprox(effect.Rotation, expectedAngle),
                        $"{card} beam and impact aim at the actual target ({corrupted}): " +
                        $"rotation={effect.Rotation}, expected={expectedAngle}, origin={beamOrigin}, target={effect.TargetPosition}");
                    Require(effect.Scale == Vector2.One, $"{card} is not double-mirrored ({corrupted})");
                    Require(effect.Position.IsEqualApprox(effect.Type == typeof(NHyperbeamVfx)
                        ? beamOrigin : effect.TargetPosition), $"{card} uses the correct eye/impact anchor ({corrupted})");
                }
                if (effect.Type == typeof(NSweepingBeamVfx))
                {
                    if (!corrupted)
                        nativeSweepingScale = effect.Scale;
                    var expectedScale = corrupted
                        ? new Vector2(-nativeSweepingScale.X, nativeSweepingScale.Y) : nativeSweepingScale;
                    Require(nativeSweepingScale.X > 0 && effect.Scale.IsEqualApprox(expectedScale) &&
                        effect.Position.IsEqualApprox(beamOrigin),
                        $"{card} emitter faces its targets from the correct eye ({corrupted}): " +
                        $"scale={effect.Scale}, expected={expectedScale}, position={effect.Position}, origin={beamOrigin}");
                }
                if (effect.Type == typeof(NShivThrowVfx))
                    Require(Mathf.IsEqualApprox(effect.Rotation, (effect.TargetPosition - effect.SourcePosition).Angle()) &&
                        effect.Scale == Vector2.One, $"{card} retains native target-derived rotation ({corrupted})");
            }
        }
    }

    private sealed record Effect(Type Type, Vector2 Position, Vector2 Scale, float Rotation, bool FacingEnemies,
        Vector2 SourcePosition, Vector2 TargetPosition);

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException("NATIVE ATTACK VFX ASSERTION FAILED: " + description);
        MainFile.Logger.Info("NATIVE ATTACK VFX ASSERT: " + description);
    }
}
