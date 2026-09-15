using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using TheArchitect.TheArchitectCode.Audio;
using TheArchitect.TheArchitectCode.Powers;

internal static class WatcherAudioTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Watcher audio: all owners cue, only the exact local creature drives loops", () =>
        {
            var local = Owner();
            var remote = Owner();
            var corrupted = Owner();
            var entries = new List<PowerModel>();
            var audio = new WatcherStanceAudio(local, entries.Add, _ => { });
            var remoteCalm = Apply<CalmStancePower>(remote);
            var corruptedWrath = Apply<WrathStancePower>(corrupted);
            audio.Enter(remoteCalm);
            audio.Enter(corruptedWrath);
            Check(entries.Count == 2 && audio.LocalStance == null);
            var localCalm = Apply<CalmStancePower>(local);
            audio.Enter(localCalm);
            Check(audio.LocalStance == localCalm && audio.OwnerCount == 3);
            audio.Enter(remoteCalm);
            audio.Enter(localCalm);
            Check(entries.Count == 3, "Same active power must not replay its cue.");
            localCalm.RemoveInternal();
            Check(audio.LocalStance == null && audio.OwnerCount == 2,
                "Remote Calm must not keep the local loop alive.");
            audio.Clear();
            Check(audio.OwnerCount == 0);
        });

        test("Watcher audio: true switches cue once, independent simultaneous entries are not coalesced", () =>
        {
            var local = Owner();
            var remote = Owner();
            var entries = new List<PowerModel>();
            var leaves = new List<PowerModel>();
            var audio = new WatcherStanceAudio(local, entries.Add, leaves.Add);
            var calm = Apply<CalmStancePower>(local);
            audio.Enter(calm);
            audio.Enter(Apply<CalmStancePower>(remote));
            Check(entries.Count == 2);
            calm.RemoveInternal();
            var wrath = Apply<WrathStancePower>(local);
            audio.Enter(wrath);
            Check(entries.Count == 3 && leaves.SequenceEqual([calm]) && audio.LocalStance == wrath);
            local.Reset();
            Check(audio.LocalStance == null && leaves.SequenceEqual([calm, wrath]));
            audio.Clear();
        });

        test("Watcher audio: death, removal, reset and repeated teardown detach exact subscriptions", () =>
        {
            foreach (var mode in new[] { "hp", "death", "remove", "reset", "combat" })
            {
                var local = Owner();
                var leaves = new List<PowerModel>();
                var audio = new WatcherStanceAudio(local, _ => { }, leaves.Add);
                var calm = Apply<CalmStancePower>(local);
                audio.Enter(calm);
                switch (mode)
                {
                    case "hp": local.SetCurrentHpInternal(0); break;
                    case "death": local.InvokeDiedEvent(); break;
                    case "remove": calm.RemoveInternal(); break;
                    case "reset": local.Reset(); break;
                    case "combat": audio.Clear(); break;
                }
                audio.Clear();
                Check(leaves.SequenceEqual([calm]) && audio.LocalStance == null && audio.OwnerCount == 0, mode);
                local.SetCurrentHpInternal(100);
                local.InvokeDiedEvent();
                Check(leaves.Count == 1, "Teardown must unsubscribe rather than leave orphan listeners.");
            }
        });

        test("Watcher audio: dead, detached and preview powers never become playback owners", () =>
        {
            var owner = Owner();
            var entries = new List<PowerModel>();
            var audio = new WatcherStanceAudio(owner, entries.Add, _ => { });
            var calm = Apply<CalmStancePower>(owner);
            owner.SetCurrentHpInternal(0);
            audio.Enter(calm);
            owner.SetCurrentHpInternal(100);
            owner.CombatState = null;
            audio.Enter(calm);
            owner.CombatState = MarkerCombat();
            calm.RemoveInternal();
            audio.Enter(calm);
            Check(entries.Count == 0 && audio.OwnerCount == 0);
        });
    }

    private static Creature Owner()
    {
        var creature = new Creature(new TenHpMonster().ToMutable(), CombatSide.Enemy, null);
        creature.SetMaxHpInternal(100);
        creature.SetCurrentHpInternal(100);
        creature.CombatState = MarkerCombat();
        return creature;
    }

    // Ownership accounting only needs a combat identity, not a running game/renderer.
    private static CombatState MarkerCombat() =>
        (CombatState)RuntimeHelpers.GetUninitializedObject(typeof(CombatState));

    private static T Apply<T>(Creature creature) where T : PowerModel, new()
    {
        var power = (T)ModelDb.Power<T>().ToMutable();
        power.ApplyInternal(creature, 1);
        return power;
    }

    private static void Check(bool condition, string message = "Unexpected audio ownership.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
