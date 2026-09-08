using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.Challenger;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeMechanicsPlaytest
{
    internal static async Task Run(Player human, NativeChallenger actor)
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Mechanics probes require the disposable smoke environment.");
        var combat = human.Creature.CombatState!;
        var context = new ThrowingPlayerChoiceContext();
        await CreatureCmd.SetMaxHp(human.Creature, 1000);
        await CreatureCmd.Heal(human.Creature, 1000);
        human.Creature.RemoveAllPowersInternalExcept();
        actor.Body.RemoveAllPowersInternalExcept();
        OrbCmd.RemoveSlots(actor.Player, actor.State.OrbQueue.Capacity);
        actor.Player.MaxEnergy = 10;
        void Prepare(params CardModel[] cards)
        {
            foreach (var pile in actor.State.AllPiles)
                foreach (var card in pile.Cards.ToArray())
                {
                    pile.RemoveInternal(card);
                    combat.RemoveCard(card);
                }
            foreach (var card in cards)
                actor.State.DrawPile.AddInternal(card);
        }
        T Card<T>(bool upgraded = false) where T : CardModel
        {
            var card = combat.CreateCard<T>(actor.Player);
            if (upgraded)
            {
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
            }
            return card;
        }
        async Task Turn()
        {
            var turn = human.PlayerCombatState!.TurnNumber;
            PlayerCmd.EndTurn(human, false);
            await NativeDemoPlaytest.PlayerTurn(human, turn + 1);
            actor.AssertIdentity();
        }

        var perfected = Card<PerfectedStrike>(true);
        var impervious = Card<Impervious>(true);
        Prepare(Card<Inflame>(true), perfected, impervious, Card<AscendersBane>(), Card<Wound>());
        for (var i = 0; i < 3; i++)
            actor.State.ExhaustPile.AddInternal(Card<StrikeIronclad>());
        await PowerCmd.Apply<DexterityPower>(context, actor.Body, 2, human.Creature, null);
        await PowerCmd.Apply<FrailPower>(context, actor.Body, 1, human.Creature, null);
        await PowerCmd.Apply<WeakPower>(context, actor.Body, 1, human.Creature, null);
        await PowerCmd.Apply<VulnerablePower>(context, human.Creature, 1, actor.Body, null);
        var hp = human.Creature.CurrentHp;
        await Turn();
        Require(hp - human.Creature.CurrentHp == 23, "PerfectedStrike+ counts four Strike tags including exhaust; modifiers applied once");
        Require(actor.Body.Block == 31 && impervious.Pile == actor.State.ExhaustPile,
            "Impervious+ restores upgrade, Dexterity/Frail once, native exhaust");
        Require(actor.Body.GetPowerAmount<StrengthPower>() == 3, "native upgraded Inflame power persists");
        Require(actor.State.ExhaustPile.Cards.OfType<AscendersBane>().Any(), "native Ethereal and unplayable handling");

        actor.Body.RemoveAllPowersInternalExcept();
        human.Creature.RemoveAllPowersInternalExcept();
        var anger = Card<Anger>();
        var bane = Card<AscendersBane>();
        Prepare(Card<BouncingFlask>(), anger, Card<BladeDance>(), Card<TrueGrit>(true), bane);
        hp = human.Creature.CurrentHp;
        await Turn();
        Require(actor.Body.GetPowerAmount<PoisonPower>() == 0 && human.Creature.GetPowerAmount<PoisonPower>() > 0,
            "hardcoded enemy-list random targeting hits human, never actor");
        Require(actor.State.DiscardPile.Cards.OfType<Anger>().Count() == 2, "native Anger creates persistent independent copy");
        Require(actor.State.ExhaustPile.Cards.OfType<Shiv>().Any() && human.Creature.CurrentHp < hp,
            "native generated Shivs execute in same turn");
        Require(bane.Pile == actor.State.ExhaustPile, "upgraded TrueGrit resolves deterministic NPC hand choice");

        human.Creature.RemoveAllPowersInternalExcept();
        actor.Body.RemoveAllPowersInternalExcept();
        Prepare(Card<Discovery>(), Card<Whirlwind>(), Card<Burn>(), Card<Wound>(), Card<Wound>());
        var actorHp = actor.Body.CurrentHp;
        await Turn();
        Require(actor.Body.CurrentHp < actorHp || actor.Body.Block > 0, "native end-in-hand Burn effect resolves");
        Require(actor.State.Energy < actor.State.MaxEnergy, "native X-cost consumes private energy");

        actor.Body.RemoveAllPowersInternalExcept();
        human.Creature.RemoveAllPowersInternalExcept();
        var eyes = Card<GoForTheEyes>(true);
        var originalHand = human.PlayerCombatState!.Hand.Cards.ToArray();
        foreach (var card in originalHand)
            human.PlayerCombatState.Hand.RemoveInternal(card);
        Require(!NativeHumanIntent.IntendsToAttack(human.Creature, eyes), "empty current hand is not attack intent");
        var defend = combat.CreateCard<DefendIronclad>(human);
        human.PlayerCombatState.Hand.AddInternal(defend);
        Require(!NativeHumanIntent.IntendsToAttack(human.Creature, eyes), "non-Attack hand is not attack intent");
        var attack = combat.CreateCard<StrikeIronclad>(human);
        attack.AddKeyword(CardKeyword.Retain);
        human.PlayerCombatState.Hand.AddInternal(attack);
        human.PlayerCombatState.Energy = 0;
        Require(NativeHumanIntent.IntendsToAttack(human.Creature, eyes), "any Attack counts even when unaffordable");
        Prepare(eyes, Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await Turn();
        Require(human.PlayerCombatState.Hand.Cards.Contains(attack) && human.Creature.GetPowerAmount<WeakPower>() > 0,
            "native GoForTheEyes+ queries retained current Attack after dealing damage");
        attack.RemoveKeyword(CardKeyword.Retain);

        actor.Body.RemoveAllPowersInternalExcept();
        human.Creature.RemoveAllPowersInternalExcept();
        Prepare(Card<Rage>(), Card<Rampage>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await PowerCmd.Apply<ThornsPower>(context, human.Creature, 3, human.Creature, null);
        var beforeThorns = actor.Body.CurrentHp;
        await Turn();
        Require(actor.Body.CurrentHp == beforeThorns - 3 && actor.Body.Block == 3,
            "human Thorns damages native attacker before Rage block");
        human.Creature.RemoveAllPowersInternalExcept();
        Prepare(Card<FlameBarrier>(), Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await Turn();
        hp = human.Creature.CurrentHp;
        await NativeDemoPlaytest.HumanPlay<Bash>(human, actor.Body);
        Require(human.Creature.CurrentHp == hp - 4 && actor.Body.GetPowerAmount<VulnerablePower>() == 2,
            "human Bash triggers native FlameBarrier retaliation and applies Vulnerable");

        actor.Body.RemoveAllPowersInternalExcept();
        human.Creature.RemoveAllPowersInternalExcept();
        T Restored<T>(T source) where T : CardModel
        {
            var raw = JsonSerializer.SerializeToElement(source.ToSerializable(),
                JsonSerializationUtility.GetTypeInfo<SerializableCard>());
            Require(NativeCardSupport.Preflight([raw]) == null && NativeCardSupport.SavedReason(raw) == null,
                "native saved card properties/enchantment accepted");
            var saved = raw.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>())!;
            var deckCard = actor.Player.RunState.LoadCard(NativeCardSupport.ForNativeLoad(raw, saved), actor.Player);
            actor.Player.Deck.AddInternal(deckCard);
            var copy = (T)combat.CloneCard(deckCard);
            copy.DeckVersion = deckCard;
            return copy;
        }
        var genetic = Card<GeneticAlgorithm>(true);
        genetic.CurrentBlock = 25;
        genetic.IncreasedBlock = 24;
        var geneticCopy = Restored(genetic);
        var sharp = Card<StrikeIronclad>(true);
        CardCmd.Enchant<Sharp>(sharp, 4);
        var sharpCopy = Restored(sharp);
        Prepare(geneticCopy, sharpCopy, Card<Wound>(), Card<Wound>(), Card<Wound>());
        hp = human.Creature.CurrentHp;
        await Turn();
        Require(actor.Body.Block == 25 && geneticCopy.CurrentBlock == 29 &&
            geneticCopy.DeckVersion is GeneticAlgorithm { CurrentBlock: 29 } &&
            geneticCopy.Pile == actor.State.ExhaustPile, "saved native properties restore, execute and persist on deck/combat copy");
        Require(hp - human.Creature.CurrentHp == 13 && sharpCopy.Enchantment is Sharp { Amount: 4 },
            "saved Sharp enchantment and upgraded Strike apply exactly once");

        var unavailable = JsonSerializer.SerializeToElement(new SerializableCard { Id = new ModelId("CARD", "ARCHITECT_MISSING_TEST") },
            JsonSerializationUtility.GetTypeInfo<SerializableCard>());
        var preflight = NativeCardSupport.Preflight([unavailable]);
        Require(preflight?.Contains("not installed") == true, "missing saved model rejected before card restoration");
        NativeChallengerPreflight.Show(preflight!);
        Require(NModalContainer.Instance?.OpenModal is NErrorPopup, "unavailable snapshot model has visible native error modal");
        await NativeDemoPlaytest.Capture("missing-model");
        NModalContainer.Instance!.Clear();

        actor.Body.RemoveAllPowersInternalExcept();
        human.Creature.RemoveAllPowersInternalExcept();
        var humanPosition = human.Creature.GetCreatureNode()!.GlobalPosition;
        var challengerPosition = actor.Body.GetCreatureNode()!.GlobalPosition;
        void RequirePetLayout()
        {
            var petNode = actor.Player.Osty!.GetCreatureNode()!;
            var ownerNode = actor.Body.GetCreatureNode()!;
            Require(human.Creature.GetCreatureNode()!.GlobalPosition.IsEqualApprox(humanPosition) &&
                ownerNode.GlobalPosition.IsEqualApprox(challengerPosition),
                "enemy pet summon/revive preserves human and Challenger coordinates");
            Require(petNode.GetParent() == ownerNode.GetParent() &&
                petNode.GlobalPosition.X > NCombatRoom.Instance!.Size.X / 2 &&
                petNode.GlobalPosition.X < ownerNode.GlobalPosition.X &&
                petNode.Body.Scale.X < 0 && petNode.IsInteractable && petNode.Hitbox.Size.X > 0,
                "enemy Osty uses enemy container, owner-relative placement, left facing and native hitbox");
        }
        Prepare(Card<Bodyguard>(), Card<Unleash>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await Turn();
        RequirePetLayout();
        await NativeDemoPlaytest.Capture("enemy-osty");
        Require(actor.Player.Osty is { } pet && pet.Side == actor.Body.Side && pet.PetOwner == actor.Player &&
            !combat.Allies.Contains(pet), "native summon remains an enemy-owned pet outside human party");
        var osty = actor.Player.Osty!;
        await CreatureCmd.Kill(osty, true);
        Prepare(Card<Bodyguard>(), Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await Turn();
        RequirePetLayout();
        await NativeDemoPlaytest.Capture("enemy-osty-revived");
        Require(actor.Player.Osty == osty && osty.IsAlive, "native Osty revival reuses the same enemy pet");

        Prepare(Card<Mayhem>(), Card<Rampage>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await Turn();
        Require(actor.Body.GetPowerAmount<MayhemPower>() > 0, "native autoplay power installed");
        Prepare(Card<Anger>(), Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        hp = human.Creature.CurrentHp;
        await Turn();
        Require(human.Creature.CurrentHp < hp, "native automatic card play resolves against human");
        actor.Body.RemoveAllPowersInternalExcept();
        Prepare(Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await PowerCmd.Apply<AmbergrisPower>(context, actor.Body, 1, actor.Body, null);
        var turns = actor.CompletedTurns;
        await Turn();
        Require(actor.CompletedTurns == turns + 2 && actor.Body.GetPowerAmount<AmbergrisPower>() == 0,
            "native extra turn consumed exactly once without advancing human turn twice");
        actor.Body.RemoveAllPowersInternalExcept();
        var excluded = Card<EnergySurge>();
        Prepare(excluded, Card<Wound>(), Card<Wound>(), Card<Wound>(), Card<Wound>());
        await Turn();
        Require(actor.UnsupportedReason(excluded)?.Contains("co-op") == true &&
            excluded.Pile == actor.State.DiscardPile && actor.State.Energy == actor.State.MaxEnergy,
            "co-op-only native card visibly unsupported without play, spend or exhaust");
        actor.Player.MaxEnergy = actor.Player.Character.MaxEnergy;
        MainFile.Logger.Info("NATIVE MECHANICS PASSED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("NATIVE MECHANICS FAILED: " + message);
        MainFile.Logger.Info("NATIVE ASSERT: " + message);
    }
}
