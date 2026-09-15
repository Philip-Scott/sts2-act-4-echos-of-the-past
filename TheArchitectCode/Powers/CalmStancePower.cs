using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;

namespace TheArchitect.TheArchitectCode.Powers;

public sealed class CalmStancePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override string CustomPackedIconPath => "res://TheArchitect/images/relics/violet_lotus.png";
    public override string CustomBigIconPath => "res://TheArchitect/images/relics/big/violet_lotus.png";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForEnergy(this)];
}
