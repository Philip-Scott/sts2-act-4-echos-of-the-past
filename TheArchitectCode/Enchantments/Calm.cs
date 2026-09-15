using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.Enchantments;

public sealed class Calm : CustomEnchantmentModel
{
    public override bool HasExtraCardText => true;
    public override bool ShowAmount => false;
    protected override string CustomIconPath => "res://TheArchitect/images/relics/violet_lotus.png";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<CalmStancePower>()];

    public override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay? cardPlay) =>
        WatcherStances.EnterCalm(choiceContext, Card);
}
