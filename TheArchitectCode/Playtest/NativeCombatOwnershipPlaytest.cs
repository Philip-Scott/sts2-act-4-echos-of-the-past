using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeCombatOwnershipPlaytest
{
    internal static async Task Relics(Player human, NativeCorruptedPlayer actor)
    {
        Require(NativeDemoSafety.Enabled, "ownership probes require disposable storage");
        var combat = human.Creature.CombatState!;
        var relics = new RelicModel[]
        {
            ModelDb.Relic<Vambrace>().ToMutable(),
            ModelDb.Relic<ThrowingAxe>().ToMutable(),
            ModelDb.Relic<RuinedHelmet>().ToMutable()
        };
        var enemyCard = combat.CreateCard<DefendIronclad>(actor.Player);
        var humanCard = combat.CreateCard<DefendIronclad>(human);
        try
        {
            foreach (var relic in relics)
            {
                human.AddRelicInternal(relic, silent: true);
                await relic.BeforeCombatStart();
            }
            foreach (var scope in new[] { combat, actor.View })
            {
                Require(Hook.ModifyBlock(scope, actor.Body, 5, ValueProp.Move, enemyCard, null,
                    out var modifiers) == 5 && !modifiers.OfType<RelicModel>().Any(),
                    "human Vambrace cannot multiply corrupted block through live or preview hooks");
                Require(Hook.ModifyCardPlayCount(scope, enemyCard, 1, null, out var repeats) == 1 &&
                    !repeats.OfType<RelicModel>().Any(), "human Throwing Axe cannot repeat corrupted cards");
                Require(Hook.ModifyPowerAmountReceived(scope, ModelDb.Power<StrengthPower>(),
                    actor.Body, 2, actor.Body, out var powers) == 2 &&
                    !powers.OfType<RelicModel>().Any(), "human Ruined Helmet cannot multiply corrupted Strength");
            }
            Require(Hook.ModifyBlock(combat, human.Creature, 5, ValueProp.Move, humanCard, null,
                out var humanBlockModifiers) >= 10 && humanBlockModifiers.Contains(relics[0]),
                "human Vambrace still doubles its owner's first block card");
            Require(Hook.ModifyCardPlayCount(combat, humanCard, 1, null, out var humanRepeatModifiers) >= 2 &&
                humanRepeatModifiers.Contains(relics[1]),
                "human Throwing Axe still repeats its owner's card");
            Require(Hook.ModifyPowerAmountReceived(combat, ModelDb.Power<StrengthPower>(),
                human.Creature, 2, human.Creature, out var humanPowerModifiers) >= 4 &&
                humanPowerModifiers.Contains(relics[2]),
                "human Ruined Helmet still doubles its owner's Strength");
            Require(actor.Player.Relics.Count == 0, "private actor never acquires human relics");
        }
        finally
        {
            foreach (var relic in relics.Where(human.Relics.Contains))
                human.RemoveRelicInternal(relic, silent: true);
            combat.RemoveCard(enemyCard);
            combat.RemoveCard(humanCard);
        }
        MainFile.Logger.Info("NATIVE OWNERSHIP RELICS PASSED");
    }

    internal static async Task Party(Player[] humans, NativeCorruptedPlayer[] actors)
    {
        Require(NativeDemoSafety.Enabled && actors.Length > 1, "co-op probes require a disposable party");
        var actor = actors[0];
        var ally = actors[1];
        var combat = actor.Body.CombatState!;
        var context = new NativeChoiceContext(actor.Player);
        await PlayerCmd.GainEnergy(10, actor.Player);
        async Task<CardModel> Play<T>(MegaCrit.Sts2.Core.Entities.Creatures.Creature? target = null) where T : CardModel
        {
            var card = combat.CreateCard<T>(actor.Player);
            Require(actor.UnsupportedReason(card) == null, $"{card.Id}: native co-op card is supported");
            await CardCmd.AutoPlay(context, card, target, skipCardPileVisuals: true);
            return card;
        }
        foreach (var card in ModelDb.AllCards.Where(card =>
            card.GetType().Assembly == typeof(CardModel).Assembly &&
            card.MultiplayerConstraint == CardMultiplayerConstraint.MultiplayerOnly))
            Require(NativeCardSupport.Reason(card) == null, $"{card.Id}: no blanket co-op exclusion");
        Require(actor.View.Players.Count == actors.Length &&
            actors.All(member => actor.View.GetPlayer(member.Player.NetId) == member.Player) &&
            humans.All(human => actor.View.GetPlayer(human.NetId) == null),
            "co-op player lookup exposes only the opposing party");
        var believe = combat.CreateCard<BelieveInYou>(actor.Player);
        Require(actor.SelectTarget(believe) == ally.Body && believe.CanPlay() &&
            believe.IsValidTarget(ally.Body) && !believe.IsValidTarget(actor.Body) &&
            !believe.IsValidTarget(humans[0].Creature) &&
            actors.SelectMany(member => member.State.Pets).All(pet => !believe.IsValidTarget(pet)),
            "ally cards accept another corrupted player, never self, humans or pets");
        combat.RemoveCard(believe);
        var humanEnergy = humans.Select(human => human.PlayerCombatState!.Energy).ToArray();
        var energy = ally.State.Energy;
        await Play<BelieveInYou>(ally.Body);
        Require(ally.State.Energy == energy + ModelDb.Card<BelieveInYou>().DynamicVars.Energy.IntValue &&
            humans.Select(human => human.PlayerCombatState!.Energy).SequenceEqual(humanEnergy),
            "single-ally native energy goes only to the corrupted recipient");
        var energies = actors.Select(member => member.State.Energy).ToArray();
        await Play<EnergySurge>();
        Require(actors.Select((member, index) =>
            member.State.Energy == energies[index] + ModelDb.Card<EnergySurge>().DynamicVars.Energy.IntValue).All(value => value),
            "all-ally native IsPlayer predicates include every corrupted player");
        var humanBlock = humans.Select(human => human.Creature.Block).ToArray();
        var blocks = actors.Select(member => member.Body.Block).ToArray();
        await Play<Rally>();
        Require(actors.Select((member, index) => member.Body.Block > blocks[index]).All(value => value) &&
            humans.Select(human => human.Creature.Block).SequenceEqual(humanBlock),
            "all-ally native block reaches the corrupted party, not the humans");
        var ball = await Play<TheBall>(humans[0].Creature);
        Require(ball.Owner != actor.Player && actors.Skip(1).Any(member => member.Player == ball.Owner) &&
            ball.Pile?.Type == PileType.Draw, "The Ball uses native transfer to another corrupted draw pile");
        // Native draw-pile choices hide shuffle order by sorting the offered cards.
        var firstOffered = ally.State.DrawPile.Cards.OrderBy(card => card.Rarity).ThenBy(card => card.Id).FirstOrDefault();
        if (firstOffered != null && ally.State.Hand.Cards.Count < CardPile.MaxCardsInHand)
        {
            await Play<Tutor>(ally.Body);
            Require(firstOffered.Pile == ally.State.Hand, "Tutor resolves the recipient's native first-valid choice");
        }
        var humanStrike = combat.CreateCard<StrikeIronclad>(humans[0]);
        var allyStrike = combat.CreateCard<StrikeIronclad>(ally.Player);
        var midnight = combat.CreateCard<Midnight>(actor.Player);
        var cacophony = await PowerCmd.Apply<CacophonyPower>(context, actor.Body, 10, actor.Body, null);
        Require(cacophony != null, "native Cacophony applies to the corrupted actor");
        var drawsRemaining = cacophony!.DisplayAmount;
        await cacophony.AfterCardDrawn(context, humanStrike, false);
        Require(cacophony.DisplayAmount == drawsRemaining, "human draws cannot advance corrupted Cacophony");
        await cacophony.AfterCardDrawn(context, allyStrike, false);
        Require(cacophony.DisplayAmount == drawsRemaining - 1, "corrupted ally draws advance Cacophony once");
        await PowerCmd.Remove(cacophony);
        var cost = midnight.EnergyCost.GetWithModifiers(CostModifiers.Local);
        await midnight.AfterCardExhausted(context, humanStrike, false);
        Require(midnight.EnergyCost.GetWithModifiers(CostModifiers.Local) == cost,
            "human exhausts cannot reduce corrupted Midnight");
        await midnight.AfterCardExhausted(context, allyStrike, false);
        Require(midnight.EnergyCost.GetWithModifiers(CostModifiers.Local) == cost - 1,
            "corrupted ally exhausts reduce Midnight once");
        var underworld = await PowerCmd.Apply<UnderworldPower>(context, actor.Body, 1, actor.Body, null);
        Require(underworld != null, "native Underworld applies to the corrupted actor");
        await underworld!.AfterSideTurnEnd(context, CombatSide.Enemy, actors.Select(member => member.Body));
        Require(actor.Body.HasPower<UnderworldPower>(), "corrupted defensive co-op power survives its own turn");
        await underworld.AfterSideTurnEnd(context, CombatSide.Player, humans.Select(human => human.Creature));
        Require(!actor.Body.HasPower<UnderworldPower>(), "corrupted defensive co-op power expires after the human turn");
        combat.RemoveCard(humanStrike);
        combat.RemoveCard(allyStrike);
        combat.RemoveCard(midnight);
        foreach (var member in actors)
            member.AssertIdentity();
        MainFile.Logger.Info("NATIVE OWNERSHIP COOP PASSED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Native ownership regression: " + message);
    }
}
