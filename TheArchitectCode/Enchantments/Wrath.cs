using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.Enchantments;

public sealed class Wrath : CustomEnchantmentModel
{
    public override bool HasExtraCardText => true;
    public override bool ShowAmount => false;
    protected override string CustomIconPath => "res://TheArchitect/images/enchantments/wrath.png";

    public override Task OnPlay(PlayerChoiceContext choiceContext, CardPlay? cardPlay) =>
        WatcherStances.EnterWrath(choiceContext, Card);
}
