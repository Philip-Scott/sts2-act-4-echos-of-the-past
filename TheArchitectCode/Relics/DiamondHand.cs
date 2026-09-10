using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class DiamondHand : UnwrittenRelic
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new CardsVar(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromEnchantment<Glam>(1);

    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        var combat = Owner.PlayerCombatState;
        if (player != Owner || combat == null || combat.TurnNumber != 1)
            return Task.CompletedTask;

        var glam = ModelDb.Enchantment<Glam>();
        var candidates = combat.Hand.Cards
            .Where(card => card.Owner == Owner && card.Enchantment == null && glam.CanEnchant(card))
            .ToArray();
        if (candidates.Length == 0)
            return Task.CompletedTask;

        Flash();
        var card = candidates[Owner.PlayerRng.Rewards.NextInt(candidates.Length)];
        CardCmd.Enchant<Glam>(card, 1);
        return Task.CompletedTask;
    }
}
