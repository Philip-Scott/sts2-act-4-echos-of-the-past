using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.Powers;

internal static class WatcherStances
{
    internal static async Task EnterWrath(PlayerChoiceContext context, CardModel card)
    {
        var owner = card.Owner.Creature;
        if (owner.IsDead || card.Owner.PlayerCombatState == null || owner.GetPower<WrathStancePower>() != null)
            return;

        if (owner.GetPower<CalmStancePower>() is { } calm)
        {
            await PowerCmd.Remove(calm);
            await PlayerCmd.GainEnergy(1, card.Owner);
        }
        await PowerCmd.Apply<WrathStancePower>(context, owner, 1, owner, card);
    }

    internal static async Task EnterCalm(PlayerChoiceContext context, CardModel card)
    {
        var owner = card.Owner.Creature;
        if (owner.IsDead || card.Owner.PlayerCombatState == null || owner.GetPower<CalmStancePower>() != null)
            return;

        if (owner.GetPower<WrathStancePower>() is { } wrath)
            await PowerCmd.Remove(wrath);
        await PowerCmd.Apply<CalmStancePower>(context, owner, 1, owner, card);
    }
}
