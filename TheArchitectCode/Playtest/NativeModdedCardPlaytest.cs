using System.Text.Json;
using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeModdedCardPlaytest
{
    internal static async Task Run(Player human, NativeCorruptedPlayer actor,
        Action<CardModel[]> prepare, Func<Task> turn)
    {
        Require(NativeDemoSafety.Enabled, "fixtures require disposable storage");
        var combat = human.Creature.CombatState!;
        var types = new[] { typeof(ModdedProbeCard), typeof(ModdedProbeModifier) };
        Require(types.All(ModelDb.Contains), "disposable-only fixtures have canonical model IDs");
        var humanHp = human.Creature.CurrentHp;
        var actorHp = actor.Body.CurrentHp;
        var humanBlock = human.Creature.Block;
        var actorBlock = actor.Body.Block;
        var actorEnergy = actor.State.Energy;
        ModdedProbeCard? restored = null;
        var played = false;
        var energyBefore = 0;
        var humanEnergyBefore = 0;
        void BeforeTurn()
        {
            energyBefore = actor.State.Energy;
            humanEnergyBefore = human.PlayerCombatState!.Energy;
        }
        void AfterPlay(CardModel card)
        {
            if (card != restored)
                return;
            played = true;
            Require(restored.Plays == 1 && restored.EnergySpent == 2 &&
                actor.State.Energy == energyBefore - 2,
                "custom OnPlay executes once and spends only its private two energy");
            Require(restored.Enemies.SequenceEqual(new[] { human.Creature }) &&
                restored.OwnerIsPlayer && restored.ResolvedOwner == actor.Player,
                "mod-assembly async call sites resolve enemies and creature ownership relative to the actor");
            Require(human.Creature.CurrentHp == humanHp - 15 && actor.Body.CurrentHp == actorHp,
                "custom attack and generated Shiv damage only the human, with owner Strength applied once");
            Require(restored.Generated is { Owner: var owner, Pile.Type: PileType.Exhaust } &&
                owner == actor.Player && actor.Body.GetPowerAmount<StrengthPower>() == 2 &&
                human.Creature.GetPowerAmount<StrengthPower>() == 0,
                "custom effect applies its power to the actor and autoplays its generated card");
            Require(actor.Body.Block == 3 &&
                CardModifier.Modifiers(restored).OfType<ModdedProbeModifier>().Single().Plays == 1,
                "populated BaseLib modifier save restores its amount and executes its OnPlay hook");
            Require(human.PlayerCombatState!.Energy == humanEnergyBefore,
                "custom effects do not spend human energy");
        }
        try
        {
            var canonical = ModelDb.GetById<ModdedProbeCard>(ModelDb.GetId(typeof(ModdedProbeCard)));
            LocManager.Instance.GetTable("cards").MergeWith(new Dictionary<string, string>
            {
                [canonical.Id.Entry + ".title"] = "Modded regression probe",
                [canonical.Id.Entry + ".description"] =
                    "Gain 2 Strength. Deal {Damage:diff()} damage to ALL enemies. Create and play a Shiv."
            });
            Require(canonical.GetType().Assembly != typeof(CardModel).Assembly &&
                !ModelDb.AllCards.Contains(canonical), "fixture is genuinely third-party and absent from reward pools");
            var source = canonical.ToMutable();
            var modifier = (ModdedProbeModifier)ModelDb.GetById<ModdedProbeModifier>(
                ModelDb.GetId(typeof(ModdedProbeModifier))).MutableClone();
            modifier.Amount = 3;
            CardModifier.AddModifier(source, modifier);
            var raw = JsonSerializer.SerializeToElement(source.ToSerializable(),
                JsonSerializationUtility.GetTypeInfo<SerializableCard>());
            Require(raw.TryGetProperty("save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]", out var data) &&
                data.GetProperty("BaseLibCardModifiers").GetArrayLength() == 1,
                "fixture snapshot contains a populated real BaseLib modifier dictionary");
            Require(NativeCardSupport.SavedReason(raw) == null && NativeCardSupport.Preflight([raw]) == null,
                "installed mod card and populated BaseLib save pass preflight");
            var saved = raw.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>())!;
            Require(ReferenceEquals(NativeCardSupport.ForNativeLoad(raw, saved), saved),
                "native loading preserves BaseLib extension data instead of stripping it");
            restored = (ModdedProbeCard)CardModel.FromSerializable(NativeCardSupport.ForNativeLoad(raw, saved));
            combat.AddCard(restored, actor.Player);
            Require(CardModifier.Modifiers(restored).Single().Amount == 3 &&
                actor.UnsupportedReason(restored) == null, "restored mod card is executable with its saved modifier");
            human.Creature.LoseBlockInternal(human.Creature.Block);
            actor.Body.LoseBlockInternal(actor.Body.Block);
            prepare([restored]);
            actor.State.ResetEnergy();
            actor.TurnStarting += BeforeTurn;
            actor.CardPlayed += AfterPlay;
            await turn();
            Require(played, "normal corrupted turn selects the restored third-party card");
            actor.AssertIdentity();
        }
        finally
        {
            actor.TurnStarting -= BeforeTurn;
            actor.CardPlayed -= AfterPlay;
            prepare([]);
            if (restored != null)
                combat.RemoveCard(restored);
            actor.Body.RemoveAllPowersInternalExcept();
            human.Creature.RemoveAllPowersInternalExcept();
            actor.Body.SetCurrentHpInternal(actorHp);
            human.Creature.SetCurrentHpInternal(humanHp);
            actor.Body.LoseBlockInternal(actor.Body.Block);
            human.Creature.LoseBlockInternal(human.Creature.Block);
            actor.Body.GainBlockInternal(actorBlock);
            human.Creature.GainBlockInternal(humanBlock);
            actor.State.Energy = actorEnergy;
            foreach (var type in types)
                ModelDb.Remove(type);
        }
        MainFile.Logger.Info("NATIVE MODDED CARD SUPPORT PASSED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Native modded card regression: " + message);
        MainFile.Logger.Info("NATIVE MODDED CARD PASS: " + message);
    }

    // ModelDb discovers even private nested models. Register these only in the
    // disposable process, early enough to populate the game's model-ID cache.
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllAbstractModelSubtypes), MethodType.Getter)]
    private static class HideFixtures
    {
        private static void Postfix(ref Type[] __result)
        {
            if (!NativeDemoSafety.Enabled)
                __result = __result.Where(type => type != typeof(ModdedProbeCard) &&
                    type != typeof(ModdedProbeModifier)).ToArray();
        }
    }

    // Disposable models provide their own runtime strings and never become saved content.
#pragma warning disable STS001, STS003
    private sealed class ModdedProbeCard : CardModel
    {
        public ModdedProbeCard() : base(2, CardType.Attack, CardRarity.Token, TargetType.AllEnemies,
            shouldShowInCardLibrary: false) { }

        public override CardPoolModel Pool => ModelDb.Card<StrikeIronclad>().Pool;
        public override string PortraitPath => ModelDb.Card<StrikeIronclad>().PortraitPath;
        public override string BetaPortraitPath => PortraitPath;
        protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7, ValueProp.Move)];
        internal int Plays { get; private set; }
        internal int EnergySpent { get; private set; }
        internal Creature[] Enemies { get; private set; } = [];
        internal bool OwnerIsPlayer { get; private set; }
        internal Player? ResolvedOwner { get; private set; }
        internal CardModel? Generated { get; private set; }

        protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        {
            Plays++;
            EnergySpent = cardPlay.Resources.EnergySpent;
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner.Creature, 2, Owner.Creature, this);
            // Deliberately use ordinary game getters after an await, not the bridges:
            // this exercises patching of the mod's compiler-generated state machine.
            Enemies = CombatState!.Enemies.ToArray();
            OwnerIsPlayer = Owner.Creature.IsPlayer;
            ResolvedOwner = Owner.Creature.Player;
            foreach (var enemy in Enemies)
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay)
                    .Targeting(enemy).Execute(choiceContext);
            Generated = CombatState.CreateCard<Shiv>(Owner);
            await CardPileCmd.AddGeneratedCardToCombat(Generated, PileType.Hand, Owner);
            await CardCmd.AutoPlay(choiceContext, Generated, Enemies.Single(), skipCardPileVisuals: true);
        }
    }
#pragma warning restore STS001, STS003

    private sealed class ModdedProbeModifier : CardModifier
    {
        public ModdedProbeModifier() { }
        internal int Plays { get; private set; }

        public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
        {
            Plays++;
            await CreatureCmd.GainBlock(cardPlay.Player.Creature, Amount, ValueProp.Move, cardPlay);
        }
    }
}
