using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Powers;

public sealed class WrathStancePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override string CustomPackedIconPath => "res://TheArchitect/images/relics/violet_lotus.png";
    public override string CustomBigIconPath => "res://TheArchitect/images/relics/big/violet_lotus.png";

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (TestMode.IsOff)
            WatcherStanceVfx.Attach(this);
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        if (TestMode.IsOff)
            WatcherStanceVfx.Detach(this);
        return Task.CompletedTask;
    }

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target == Owner && dealer != null && dealer.Side != Owner.Side)
            return 1.5m;
        return dealer == Owner && cardSource?.Type == CardType.Attack && props.IsPoweredAttack() ? 1.5m : 1m;
    }
}
