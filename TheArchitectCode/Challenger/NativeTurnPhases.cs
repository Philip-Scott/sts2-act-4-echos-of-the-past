using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace TheArchitect.TheArchitectCode.Challenger;

// Called explicitly from rewritten native turn-loop call sites, so pre-JIT or
// tiered inlining cannot bypass actor setup/cleanup by inlining a Hook wrapper.
internal static class NativeTurnPhases
{
    internal static async Task AfterSideTurnStart(ICombatState combatState, CombatSide side, IReadOnlyList<Creature> participants)
    {
        await Hook.AfterSideTurnStart(combatState, side, participants);
        if (side == CombatSide.Player)
            foreach (var actor in NativeChallenger.In(combatState))
                await actor.PrepareTurn();
    }

    internal static async Task AfterBlockCleared(ICombatState combatState, Creature creature)
    {
        await Hook.AfterBlockCleared(combatState, creature);
        if (NativeChallenger.TryGet(creature, out var actor))
            await actor.StartTurn();
    }

    internal static async Task BeforeSideTurnEnd(ICombatState combatState, CombatSide side, IEnumerable<Creature> participants)
    {
        await Hook.BeforeSideTurnEnd(combatState, side, participants);
        if (side == CombatSide.Enemy)
            foreach (var actor in NativeChallenger.In(combatState))
                await actor.FinishHand();
    }

    internal static async Task AfterSideTurnEnd(ICombatState combatState, CombatSide side, IEnumerable<Creature> participants)
    {
        await Hook.AfterSideTurnEnd(combatState, side, participants);
        if (side == CombatSide.Enemy)
        {
            foreach (var actor in NativeChallenger.In(combatState))
                actor.FinishTurn();
            foreach (var actor in NativeChallenger.In(combatState))
                await actor.TakeExtraTurns();
        }
    }

    internal static async Task FinishExtraTurn(ICombatState combatState, IEnumerable<Creature> participants)
    {
        await Hook.AfterSideTurnEnd(combatState, CombatSide.Enemy, participants);
        foreach (var actor in NativeChallenger.In(combatState))
            actor.FinishTurn();
    }
}
