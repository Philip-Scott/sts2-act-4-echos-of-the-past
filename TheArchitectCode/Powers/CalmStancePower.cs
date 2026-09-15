using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Powers;

public sealed class CalmStancePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override string CustomPackedIconPath => "res://TheArchitect/images/relics/violet_lotus.png";
    public override string CustomBigIconPath => "res://TheArchitect/images/relics/big/violet_lotus.png";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForEnergy(this)];

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
}
