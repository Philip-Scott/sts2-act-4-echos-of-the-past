using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class NativeDownfallPlaytest
{
    private static readonly string[] Characters =
        ["Snecko", "SlimeBoss", "Hermit", "Hexaghost", "Guardian", "Champ", "Awakened", "Automaton"];

    internal static string? RequestedCharacter => Characters.FirstOrDefault(name =>
        CommandLineHelper.HasArg("architect-native-downfall-" + name.ToLowerInvariant()));

    internal static CorruptedPlayerSnapshot[] InspectionParty()
    {
        Require(NativeDemoSafety.Enabled, "showcase decks require disposable storage");
        (string Character, string[] Cards)[] decks =
        [
            ("Hexaghost", ["Float", "Kindle", "Sear", "StrikeHexaghost", "DefendHexaghost"]),
            ("Awakened", ["TalonRake", "Envision", "Recitation", "Initiation", "DefendAwakened"]),
            ("SlimeBoss", ["CorrosiveSpit", "SplitBruiser", "OpeningTackle", "StrikeSlimeBoss", "DefendSlimeBoss"]),
            ("Champ", ["BerserkersShout", "DefensiveShout", "Execute", "StrikeChamp", "DefendChamp"])
        ];
        var registered = ModelDb.All.ToArray();
        return decks.Select(deck =>
        {
            var character = registered.OfType<CharacterModel>().Single(model =>
                model.GetType().FullName == $"{deck.Character}.{deck.Character}Code.Core.{deck.Character}");
            Require(NativeDownfallApi.TryBind(character.GetType().Assembly, out _, out _),
                $"{deck.Character}: showcase requires supported Downfall API");
            var cards = deck.Cards.Select(name => registered.OfType<CardModel>().Single(model =>
                model.GetType().Namespace?.StartsWith(deck.Character + ".", StringComparison.Ordinal) == true &&
                model.GetType().Name == name).ToMutable()).ToArray();
            var saved = cards.Select(Save).ToArray();
            var id = character.Id.ToString();
            MainFile.Logger.Info($"DOWNFALL SHOWCASE {id}: {string.Join(", ", cards.Select(card => card.Id.Entry))}");
            return new CorruptedPlayerSnapshot(id, character.StartingHp, saved,
                CorruptedPlayerSnapshot.Hash(id, character.StartingHp, saved));
        }).ToArray();
    }

    internal static async Task Run(NGame game, string name)
    {
        Require(NativeDemoSafety.Enabled, "requires disposable storage");
        Require(!typeof(NativeDownfallSupport).Assembly.GetReferencedAssemblies().Any(reference => reference.Name == "Downfall"),
            "production mod has no assembly reference to the optional Downfall mod");
        SaveManager.Instance.PrefsSave.FastMode = CommandLineHelper.HasArg("architect-native-recording")
            ? FastModeType.Normal : FastModeType.Instant;
        var registered = ModelDb.All.ToArray();
        MainFile.Logger.Info("DOWNFALL REGISTERED CHARACTERS: " +
            string.Join(", ", registered.OfType<CharacterModel>().Select(model => model.GetType().FullName)));
        var character = registered.OfType<CharacterModel>().SingleOrDefault(model =>
            model.GetType().FullName == $"{name}.{name}Code.Core.{name}")
            ?? throw new InvalidOperationException(
                $"Requested Downfall character {name} is absent from the canonical model registry.");
        MainFile.Logger.Info($"DOWNFALL BEGIN: {name}; assembly={character.GetType().Assembly.FullName}");
        var catalog = registered.OfType<CardModel>().Where(card => card.GetType().Assembly == character.GetType().Assembly &&
            card.GetType().Namespace?.StartsWith(name + ".", StringComparison.Ordinal) == true).ToArray();
        Require(catalog.Length > 0, $"{name}: actual installed card catalog found");
        foreach (var canonical in catalog)
        {
            var source = canonical.ToMutable();
            var raw = Save(source);
            Require(NativeCardSupport.Preflight([raw]) == null && NativeCardSupport.SavedReason(raw) == null,
                $"{name}: {canonical.Id} saved data accepted");
            var saved = raw.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>())!;
            var restored = CardModel.FromSerializable(NativeCardSupport.ForNativeLoad(raw, saved));
            Require(restored.GetType() == source.GetType() && Save(restored).GetRawText() == raw.GetRawText(),
                $"{name}: {canonical.Id} snapshot round trip");
        }
        MainFile.Logger.Info($"DOWNFALL CATALOG PASSED: {name}; {catalog.Length} cards (serialization, not effect coverage)");
        var keywordMode = CommandLineHelper.HasArg("architect-native-downfall-keywords");
        var deck = keywordMode && name == "Automaton"
            ? Enumerable.Range(0, 12).Select(index => Save(catalog.Single(card =>
                card.GetType().Name == (index < 6 ? "Backtrace" : "DefendAutomaton")).ToMutable())).ToArray()
            : character.StartingDeck.Select(card => Save(card.ToMutable())).ToArray();
        var characterId = character.Id.ToString();
        // Ascension zero doubles the saved victory HP when constructing the corrupted body.
        var startingHp = keywordMode ? 500 : character.StartingHp;
        var snapshot = new CorruptedPlayerSnapshot(characterId, startingHp, deck,
            CorruptedPlayerSnapshot.Hash(characterId, startingHp, deck));
        var human = Player.CreateForNewRun(ModelDb.Character<Ironclad>(), UnlockState.all, 1);
        if (keywordMode)
        {
            await CreatureCmd.SetMaxHp(human.Creature, 1000);
            await CreatureCmd.Heal(human.Creature, 1000);
        }
        var run = RunState.CreateForNewRun([human],
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, 0, "ARCHITECT-DOWNFALL-" + name);
        var manager = RunManager.Instance;
        manager.SetUpTest(run, new NetSingleplayerGameService(), disableCombatStateSync: true, shouldSave: false);
        manager.GenerateRooms();
        ArchitectLifecycle.AppendAct(run);
        var state = ArchitectRun.Get(run);
        state.EntrySnapshot = new CorruptedPlayerEnvelope { Revision = 1, Snapshot = snapshot };
        state.Entered = true;
        await PreloadManager.LoadRunAssets([human.Character, character]);
        manager.Launch();
        game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
        await manager.EnterAct(3, doTransition: false);
        CorruptionVisualPlaytest.RecordingNativeVisuals = true;
        try
        {
            await manager.EnterMapCoord(run.Map.BossMapPoint.coord);
        }
        finally
        {
            CorruptionVisualPlaytest.RecordingNativeVisuals = false;
        }
        await NativeDemoPlaytest.PlayerTurn(human, 1, timeoutSeconds: 180);
        var combat = human.Creature.CombatState!;
        var actor = ((CorruptedPlayer)combat.Enemies.Single().Monster!).Native
            ?? throw new InvalidOperationException($"{name}: corrupted actor was not bound.");
        actor.AssertIdentity();
        if (actor.Body.GetCreatureNode() is { } node)
        {
            if (node.Visuals.SpineBody != null)
                CorruptionVisualPlaytest.AssertHueMaterialContract(node.Visuals);
            if (name == "Hexaghost")
            {
                await CorruptionVisualPlaytest.AssertNativeCompositor(game, node);
                await NativeDemoPlaytest.Capture("downfall-hexaghost-native-compositor");
            }
        }
        var bound = NativeDownfallApi.TryBind(character.GetType().Assembly, out var boundApi, out _);
        Require(actor.DownfallInitialized && bound,
            $"{name}: optional compatibility bound the installed API");
        var api = boundApi!;
        var humanWheel = api.GetWheel(human);
        var humanWheelIndex = api.GetWheelIndex(human);
        var humanSpells = api.GetSpellbook(human).Cards.ToArray();
        var humanSlimeSlots = api.GetSlimeSlots(human);
        var wheel = api.GetWheel(actor.Player);
        Require(wheel.Length == 6 && wheel.All(flame => api.Owner(flame) == actor.Player) &&
            !ReferenceEquals(wheel, humanWheel), $"{name}: six private, correctly owned ghostflames initialized");
        Require(api.GetSpellbook(actor.Player).Cards.Count == 4 &&
            api.GetSpellbook(actor.Player).Cards.All(card => card.Owner == actor.Player),
            $"{name}: private spellbook has four owned spells before any card plays");
        Require(api.GetSlimeSlots(actor.Player) == (name == "SlimeBoss" ? 3 : 1),
            $"{name}: native character-specific slime slot count initialized");
        Require(combat.IterateHookListeners().Count(model => wheel.Contains(model)) == 6,
            $"{name}: each private ghostflame participates in combat hooks exactly once");
        Require(actor.Player.Character.Id == character.Id && actor.Player.Deck.Cards.Count == deck.Length &&
            actor.Player.Relics.Count == 0, $"{name}: saved character/deck restored without relics");
        if (keywordMode)
        {
            await NativeDownfallKeywordPlaytest.Run(human, actor, name, registered.OfType<CardModel>().ToArray(), api);
            game.GetTree().Quit();
            return;
        }
        await CreatureCmd.SetMaxHp(human.Creature, 10000);
        await CreatureCmd.Heal(human.Creature, 10000);
        var plays = 0;
        actor.CardPlayed += card =>
        {
            plays++;
            MainFile.Logger.Info($"DOWNFALL PLAY: {name}; {card.Id}; energy={actor.State.Energy}");
        };
        var humanRng = "";
        actor.TurnStarting += () => humanRng = NativeDemoPlaytest.AllRng(human);
        actor.TurnFinished += () => Require(NativeDemoPlaytest.AllRng(human) == humanRng,
            $"{name}: corrupted turn preserves human RNG");
        for (var turn = 1; turn <= 3; turn++)
        {
            var completed = actor.CompletedTurns;
            PlayerCmd.EndTurn(human, false);
            await NativeDemoPlaytest.PlayerTurn(human, turn + 1, timeoutSeconds: 180);
            Require(actor.CompletedTurns == completed + 1, $"{name}: starter turn {turn} completes");
            actor.AssertIdentity();
        }
        Require(plays > 0, $"{name}: installed starter cards execute");
        await NativeDemoPlaytest.Capture("downfall-" + name.ToLowerInvariant());
        MainFile.Logger.Info($"DOWNFALL STARTER PASSED: {name}; {plays} card plays");
        if (name == "Hexaghost")
            await Ghostwheel(human, actor, catalog, api);
        else
            await BasicCards(human, actor, name, catalog);
        await Mechanics(human, actor, name, catalog, api);
        Require(ReferenceEquals(api.GetWheel(human), humanWheel) && api.GetWheelIndex(human) == humanWheelIndex &&
            api.GetSpellbook(human).Cards.SequenceEqual(humanSpells) && api.GetSlimeSlots(human) == humanSlimeSlots,
            $"{name}: corrupted initialization and turns never reset or consume the human's custom resources");
        MainFile.Logger.Info($"DOWNFALL SUITE PASSED: {name}");
        game.GetTree().Quit();
    }

    private static async Task Ghostwheel(Player human, NativeCorruptedPlayer actor, CardModel[] catalog,
        NativeDownfallApi api)
    {
        CardModel Card(string name) => catalog.Single(card => card.GetType().Name == name).ToMutable();
        decimal Soulburn(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature) =>
            creature.Powers.Where(power => power.GetType().FullName ==
                "Downfall.DownfallCode.Powers.SoulBurnPower").Sum(power => power.Amount);
        api.ResetWheel(actor.Player);
        await Probe(human, actor, [Card("Float")], _ =>
            Require(api.GetWheelIndex(actor.Player) == 1, "Hexaghost: Float advances a populated wheel"),
            energyCosts: [0]);
        api.ResetWheel(actor.Player);
        var hp = human.Creature.CurrentHp;
        var bodyHp = actor.Body.CurrentHp;
        await Probe(human, actor, [Card("Kindle")], _ =>
            Require(Soulburn(human.Creature) == 6 && Soulburn(actor.Body) == 0 &&
                actor.Body.Block == 2 && human.Creature.CurrentHp == hp && actor.Body.CurrentHp == bodyHp,
                "Hexaghost: Kindle gives private Block and applies both Searing hits only to the human"));
        Require(api.GetWheelIndex(actor.Player) == 1, "Hexaghost: ignited wheel advances once at enemy turn end");
        api.ResetWheel(actor.Player);
        hp = human.Creature.CurrentHp;
        var strikes = 0;
        await Probe(human, actor, [Card("StrikeHexaghost"), Card("StrikeHexaghost")], _ =>
        {
            strikes++;
            Require(human.Creature.CurrentHp == hp - strikes * 6 && actor.Body.CurrentHp == bodyHp &&
                Soulburn(human.Creature) == (strikes == 2 ? 6 : 0) && Soulburn(actor.Body) == 0,
                "Hexaghost: second Attack naturally ignites Searing once with an actor-owned choice context");
        });
        Require(api.GetWheelIndex(actor.Player) == 1, "Hexaghost: natural ignition advances only the actor's wheel");
        api.ResetWheel(actor.Player);
        await actor.RunOwnedChoice(ctx => api.Advance(ctx, actor.Player, null, true, true));
        hp = human.Creature.CurrentHp;
        var skills = 0;
        await Probe(human, actor, [Card("DefendHexaghost"), Card("DefendHexaghost")], _ =>
        {
            skills++;
            Require(actor.Body.Block == skills * 5 && actor.Body.CurrentHp == bodyHp &&
                human.Creature.CurrentHp == hp - (skills == 2 ? 6 : 0),
                "Hexaghost: Crushing's two unpowered hits target the human, never the corrupted body");
        });
        Require(api.GetWheelIndex(actor.Player) == 2, "Hexaghost: Crushing advances at the actor's own turn end");
        api.ResetWheel(actor.Player);
        for (var i = 0; i < 5; i++)
            await actor.RunOwnedChoice(ctx => api.Advance(ctx, actor.Player, null, true, true));
        await Probe(human, actor, [Card("Kindle")], _ =>
            Require(api.GetWheelIndex(actor.Player) == 5 && api.IsIgnited(actor.Player),
                "Hexaghost: Kindle ignites Inferno before turn-end hooks"));
        Require(api.GetWheelIndex(actor.Player) == 0,
            "Hexaghost: Inferno wraps to Searing before the late hook extinguishes it");
        MainFile.Logger.Info("DOWNFALL MECHANIC PASSED: Hexaghost initialization, Float, ignition, targeting, advancement and Inferno wraparound");
    }

    private static async Task BasicCards(Player human, NativeCorruptedPlayer actor, string name, CardModel[] catalog)
    {
        var strike = catalog.Single(card => card.GetType().Name == "Strike" + name);
        var defend = catalog.Single(card => card.GetType().Name == "Defend" + name);
        var cards = new[] { strike.ToMutable(), defend.ToMutable(), strike.ToMutable(), defend.ToMutable() };
        foreach (var card in cards.Skip(2))
        {
            card.UpgradeInternal();
            card.FinalizeUpgradeInternal();
        }
        var restored = cards.Select(card => CardModel.FromSerializable(Save(card).Deserialize(
            JsonSerializationUtility.GetTypeInfo<SerializableCard>())!)).ToArray();
        Require(restored.Select(card => card.CurrentUpgradeLevel).SequenceEqual(new[] { 0, 0, 1, 1 }),
            $"{name}: upgraded attack and block survive snapshot loading");
        var humanHp = human.Creature.CurrentHp;
        var bodyHp = actor.Body.CurrentHp;
        var damage = 0m;
        var block = 0m;
        await Probe(human, actor, restored, card =>
        {
            if (card.Type == CardType.Attack)
                damage += card.DynamicVars.Damage.BaseValue;
            else
                block += card.DynamicVars.Block.BaseValue;
            Require(human.Creature.CurrentHp == humanHp - damage && actor.Body.CurrentHp == bodyHp,
                $"{name}: {card.Id}+{card.CurrentUpgradeLevel} damages only the human for {damage} total");
            Require(actor.Body.Block == block && human.Creature.Block == 0,
                $"{name}: {card.Id}+{card.CurrentUpgradeLevel} grants only private Block ({block})");
        });
        MainFile.Logger.Info($"DOWNFALL BASICS PASSED: {name}; normal/upgraded attack, block, energy and ownership");
    }

    private static async Task Mechanics(Player human, NativeCorruptedPlayer actor, string name, CardModel[] catalog,
        NativeDownfallApi api)
    {
        CardModel Card(string typeName) => catalog.Single(card => card.GetType().Name == typeName).ToMutable();
        if (name == "SlimeBoss")
        {
            var hp = human.Creature.CurrentHp;
            await Probe(human, actor, [Card("CorrosiveSpit"), Card("StrikeSlimeBoss")], card =>
            {
                var goop = human.Creature.Powers.Where(power => power.GetType().FullName ==
                    "SlimeBoss.SlimeBossCode.Powers.GoopPower").Sum(power => power.Amount);
                Require(card.GetType().Name == "CorrosiveSpit"
                        ? goop == 6 && human.Creature.CurrentHp == hp
                        : goop == 0 && human.Creature.CurrentHp == hp - 12,
                    "SlimeBoss: Goop 6 targets the human, doubles Strike to 12, then is consumed");
            });
            MainFile.Logger.Info("DOWNFALL MECHANIC PASSED: SlimeBoss Goop application, bonus damage and consumption");
            Require(actor.State.Pets.Count == 0, "SlimeBoss: deterministic pet probe starts with no slimes");
            hp = human.Creature.CurrentHp;
            var actorHp = actor.Body.CurrentHp;
            await Probe(human, actor, [Card("SplitBruiser")], _ =>
                Require(actor.State.Pets.Count == 1 && actor.State.Pets[0].PetOwner == actor.Player &&
                    actor.State.Pets[0].Side == actor.Body.Side && human.Creature.CurrentHp == hp - 12 &&
                    actor.Body.CurrentHp == actorHp,
                    "SlimeBoss: Bruiser is an owned enemy pet and its two commands hit only the human"));
            Require(human.Creature.CurrentHp == hp - 18,
                "SlimeBoss: the next private hand draw commands its Bruiser once more");
            MainFile.Logger.Info("DOWNFALL MECHANIC PASSED: SlimeBoss pet creation, ownership, private RNG and automatic commands");
        }
        if (name == "Snecko")
        {
            var dice = Card("DiceBlock");
            var wounds = Enumerable.Range(0, 5).Select(_ =>
                ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.Wound>().ToMutable()).ToArray();
            await Probe(human, actor, [dice, .. wounds], _ =>
                Require(actor.Body.Block == 10, "Snecko: Overflow doubles DiceBlock with five other cards"),
                expectedCards: [dice]);
            dice = Card("DiceBlock");
            await Probe(human, actor, [dice, .. wounds.Take(4)], _ =>
                Require(actor.Body.Block == 5, "Snecko: four other cards do not activate Overflow"),
                expectedCards: [dice]);
            MainFile.Logger.Info("DOWNFALL MECHANIC PASSED: Snecko Overflow five/four-other-card boundary");
        }
        if (name == "Champ")
        {
            var hp = human.Creature.CurrentHp;
            await Probe(human, actor, [Card("BerserkersShout"), Card("DefensiveShout"), Card("Execute")], card =>
            {
                var stance = api.GetStance(actor.Player);
                if (card.GetType().Name != "Execute")
                    Require(human.Creature.CombatState!.IterateHookListeners()
                        .Count(model => ReferenceEquals(model, stance)) == 1,
                        "Champ: the current mutable stance is a combat listener exactly once");
                else
                    Require(api.NoStance.IsInstanceOfType(stance) && human.Creature.CurrentHp == hp - 20 &&
                        actor.Body.Block == 6,
                        "Champ: Shout skill hooks grant Vigor and Defensive Execute hits twice, blocks and clears stance");
            }, energyCosts: [0, 0, 2]);
            MainFile.Logger.Info("DOWNFALL MECHANIC PASSED: Champ stance listeners, skill bonus and Defensive finisher");
        }
    }

    private static async Task Probe(Player human, NativeCorruptedPlayer actor, CardModel[] cards,
        Action<CardModel> afterPlay, CardModel[]? expectedCards = null, int[]? energyCosts = null)
    {
        expectedCards ??= cards;
        var combat = human.Creature.CombatState!;
        foreach (var pile in new[] { actor.State.Hand, actor.State.DrawPile, actor.State.DiscardPile,
                     actor.State.ExhaustPile, actor.State.PlayPile })
            foreach (var card in pile.Cards.ToArray())
            {
                pile.RemoveInternal(card);
                combat.RemoveCard(card);
            }
        actor.Body.RemoveAllPowersInternalExcept();
        human.Creature.RemoveAllPowersInternalExcept();
        human.Creature.LoseBlockInternal(human.Creature.Block);
        actor.Body.LoseBlockInternal(actor.Body.Block);
        OrbCmd.RemoveSlots(actor.Player, actor.State.OrbQueue.Capacity);
        actor.Player.MaxEnergy = 10;
        actor.State.ResetEnergy();
        foreach (var card in cards)
        {
            combat.AddCard(card, actor.Player);
            actor.State.Hand.AddInternal(card);
            Require(actor.UnsupportedReason(card) == null, $"{card.Id}: executable restored card");
        }
        var played = 0;
        var humanEnergy = 0;
        var expectedEnergy = actor.State.Energy;
        void BeforeTurn() => humanEnergy = human.PlayerCombatState!.Energy;
        void AfterPlay(CardModel card)
        {
            Require(played < expectedCards.Length && ReferenceEquals(expectedCards[played], card),
                $"{card.Id}: expected left-to-right card order");
            played++;
            expectedEnergy -= energyCosts?[played - 1] ?? 1;
            Require(actor.State.Energy == expectedEnergy && human.PlayerCombatState!.Energy == humanEnergy,
                $"{card.Id}: spends only the expected private energy, no human energy");
            afterPlay(card);
        }
        actor.TurnStarting += BeforeTurn;
        actor.CardPlayed += AfterPlay;
        try
        {
            var turn = human.PlayerCombatState!.TurnNumber;
            PlayerCmd.EndTurn(human, false);
            await NativeDemoPlaytest.PlayerTurn(human, turn + 1, timeoutSeconds: 180);
            Require(played == expectedCards.Length, "all controlled cards executed");
            actor.AssertIdentity();
        }
        finally
        {
            actor.TurnStarting -= BeforeTurn;
            actor.CardPlayed -= AfterPlay;
        }
    }

    private static JsonElement Save(CardModel card) => JsonSerializer.SerializeToElement(NativeCardSerialization.ToSerializable(card),
        JsonSerializationUtility.GetTypeInfo<SerializableCard>());

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Downfall regression: " + message);
        MainFile.Logger.Info("DOWNFALL PASS: " + message);
    }
}
