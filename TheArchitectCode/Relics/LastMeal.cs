using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace TheArchitect.TheArchitectCode.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class LastMeal : UnwrittenRelic
{
    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new MaxHpVar(20), new DynamicVar("Potions", 2), new CardsVar(3)];

    public override async Task AfterObtained()
    {
        // GainMaxHp already performs the native heal, including healing modifiers.
        await CreatureCmd.GainMaxHp(Owner.Creature, DynamicVars.MaxHp.BaseValue);
        await new RewardsSet(Owner).WithCustomRewards(CreateRewards()).Offer();
    }

    internal List<Reward> CreateRewards()
    {
        var rewards = new List<Reward>();
        for (var i = 0; i < DynamicVars["Potions"].IntValue; i++)
            rewards.Add(new PotionReward(Owner));

        var options = CardCreationOptions.ForNonCombatWithUniformOdds(
            [Owner.Character.CardPool], card => card.Rarity == CardRarity.Rare);
        rewards.Add(new CardReward(options, DynamicVars.Cards.IntValue, Owner));
        return rewards;
    }
}
