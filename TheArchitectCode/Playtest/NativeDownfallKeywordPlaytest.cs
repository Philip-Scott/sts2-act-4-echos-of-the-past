using System.Reflection;
using System.Text.Json;
using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.Playtest;

internal sealed class NativeDownfallKeywordPlaytest
{
    private readonly Player _human;
    private readonly NativeCorruptedPlayer _actor;
    private readonly string _name;
    private readonly CardModel[] _catalog;
    private readonly NativeDownfallApi _api;
    private readonly List<object> _results = [];
    private readonly List<string> _failures = [];
    private readonly HashSet<string> _covered = [];
    private readonly Dictionary<string, CardModel> _representatives = [];
    private List<string> _checks = [];
    private string _case = "";
    private readonly Assembly _assembly;
    private CanvasLayer? _recordingLayer;
    private Label? _recordingCaption;

    private NativeDownfallKeywordPlaytest(Player human, NativeCorruptedPlayer actor, string name,
        CardModel[] catalog, NativeDownfallApi api)
    {
        _human = human;
        _actor = actor;
        _name = name;
        _catalog = catalog;
        _api = api;
        _assembly = actor.Player.Character.GetType().Assembly;
    }

    internal static async Task Run(Player human, NativeCorruptedPlayer actor, string name,
        CardModel[] catalog, NativeDownfallApi api)
    {
        var suite = new NativeDownfallKeywordPlaytest(human, actor, name, catalog, api);
        actor.Player.MaxEnergy = 10;
        actor.State.ResetEnergy();
        suite.Check(human.Creature.MaxHp == 1000 && human.Creature.CurrentHp == 1000 &&
            actor.Body.MaxHp == 1000 && actor.Body.CurrentHp == 1000, "both sides start at 1000/1000 HP");
        try
        {
            suite.StartRecordingPresentation();
            switch (name)
            {
                case "Snecko": await suite.Snecko(); break;
                case "SlimeBoss": await suite.SlimeBoss(); break;
                case "Hermit": await suite.Hermit(); break;
                case "Hexaghost": await suite.Hexaghost(); break;
                case "Guardian": await suite.Guardian(); break;
                case "Champ": await suite.Champ(); break;
                case "Awakened": await suite.Awakened(); break;
                case "Automaton": await suite.Automaton(); break;
            }
            var deck = suite._representatives.Values.Select(suite.RestoreRepresentative).ToArray();
            await suite.Probe("representative-deck-until-all-played", [], [], _ => { },
                draw: deck, expected: deck, turns: 30, stopWhenPlayed: true);
            if (name == "Hermit")
                await suite.FatalBounty();
            suite.Check(suite.RequiredKeywords.Except(suite._covered).Count() == 0,
                "every required custom/native keyword and glossary type has an explicit passing probe or scoped control");
            await NativeDemoPlaytest.Capture("downfall-" + name.ToLowerInvariant());
        }
        catch (Exception error)
        {
            suite._failures.Add(suite._case + ": " + error);
            throw;
        }
        finally
        {
            actor.CardPlaybackPause = null;
            suite._recordingLayer?.QueueFree();
            suite.WriteReport();
        }
        if (suite._failures.Count != 0)
            throw new InvalidOperationException("Downfall keyword failures: " + string.Join("; ", suite._failures));
        MainFile.Logger.Info($"DOWNFALL SUITE PASSED: {name}; keyword effects={suite._covered.Count}");
    }

    private void StartRecordingPresentation()
    {
        if (!CommandLineHelper.HasArg("architect-native-recording"))
            return;
        var game = NGame.Instance!;
        var width = game.GetViewport().GetVisibleRect().Size.X - 48;
        _recordingLayer = new CanvasLayer { Layer = 100 };
        _recordingLayer.AddChild(new ColorRect
        {
            Position = new Vector2(24, 68), Size = new Vector2(width, 76),
            Color = new Color(0.025f, 0.035f, 0.05f, 0.92f), MouseFilter = Control.MouseFilterEnum.Ignore
        });
        _recordingCaption = new Label
        {
            Position = new Vector2(40, 72), Size = new Vector2(width - 32, 68),
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _recordingCaption.AddThemeFontSizeOverride("font_size", 26);
        _recordingCaption.AddThemeColorOverride("font_color", Colors.White);
        _recordingLayer.AddChild(_recordingCaption);
        game.AddChild(_recordingLayer);
        _actor.CardPlaybackPause = async card =>
        {
            Caption("Played " + card.Title + $"  |  Corrupted energy: {_actor.State.Energy}");
            await Task.Delay(1200);
        };
    }

    private void Caption(string detail)
    {
        if (_recordingCaption != null)
            _recordingCaption.Text = $"Downfall 0.1.16 — {_name} — {_case}\n{detail}";
    }

    private async Task PresentProbe(string phase, string[] keywords, bool? passed = null)
    {
        MainFile.Logger.Info("DOWNFALL KEYWORD PROBE " + JsonSerializer.Serialize(new
        {
            character = _name, probe = _case, phase, keywords, passed,
            recording = _recordingCaption != null
        }));
        Caption(phase == "begin"
            ? (keywords.Length == 0 ? "Natural draw / discard replay until every selected card plays"
                : "Testing: " + string.Join(", ", keywords))
            : $"{(passed == true ? "PASS" : "FAIL")} — {_checks.Count} assertions");
        if (_recordingCaption != null)
            await Task.Delay(phase == "begin" ? 2200 : 1800);
    }

    private CardModel Card(string name, string? character = null)
    {
        var canonical = _catalog.Single(card => card.GetType().Name == name &&
            card.GetType().Namespace?.StartsWith((character ?? _name) + ".", StringComparison.Ordinal) == true);
        var originals = CardModifier.Modifiers(canonical);
        var card = NativeCardCloning.ToMutable(canonical);
        var preserved = CardModifier.Modifiers(card).Select(modifier => modifier.Id)
            .SequenceEqual(originals.Select(modifier => modifier.Id));
        Check(preserved,
            $"{canonical.Id} fixture preserves canonical card modifiers " +
            $"(canonical={originals.Count}, clone={CardModifier.Modifiers(card).Count})");
        if (!preserved)
            throw new InvalidOperationException($"{canonical.Id} fixture clone lost native modifiers; refusing to play it.");
        return originals.Count == 0 ? card : RestoreRepresentative(card);
    }

    private static CardModel Wound() => ModelDb.Card<Wound>().ToMutable();
    private CardModel RestoreRepresentative(CardModel card)
    {
        var raw = JsonSerializer.SerializeToElement(NativeCardSerialization.ToSerializable(card),
            JsonSerializationUtility.GetTypeInfo<SerializableCard>());
        var saved = raw.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>())!;
        if (CardModifier.Modifiers(card).Count > 0)
            MainFile.Logger.Info("DOWNFALL KEYWORD MODIFIER SNAPSHOT " + raw.GetRawText());
        var restored = CardModel.FromSerializable(NativeCardSupport.ForNativeLoad(raw, saved));
        var preserved = CardModifier.Modifiers(restored).Select(modifier => modifier.Id).SequenceEqual(
            CardModifier.Modifiers(card).Select(modifier => modifier.Id));
        Check(preserved,
            card.Id + " preserves native modifiers through snapshot restoration before deck replay");
        if (!preserved)
            throw new InvalidOperationException($"{card.Id} snapshot restore lost native modifiers; refusing to play it.");
        return restored;
    }
    private decimal Power(Creature creature, string name) =>
        creature.Powers.Where(power => power.GetType().Name == name).Sum(power => power.Amount);
    private object? Static(string type, string method, params object[] args) =>
        _assembly.GetType(type, true)!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(info => info.Name == method && info.GetParameters().Length == args.Length &&
                info.GetParameters().Zip(args).All(pair => pair.First.ParameterType.IsInstanceOfType(pair.Second)))
            .Invoke(null, args);
    private CardPile Pile(string type, string field) => CustomPiles.GetCustomPile(_actor.State,
        (PileType)_assembly.GetType(type, true)!.GetField(field)!.GetValue(null)!)!;
    private CardPile Stasis => Pile("Guardian.GuardianCode.Piles.GuardianPile", "Stasis");
    private CardPile Sequence => Pile("Automaton.AutomatonCode.Piles.EncodePile", "FunctionSequence");
    private CardPile Stash => Pile("Automaton.AutomatonCode.Piles.StashPile", "Stash");
    private int Counter(CardModel card) =>
        (int)Static("Guardian.GuardianCode.Core.GuardianCmd", "GetStasisCounter", card)!;
    private bool IsAwakened => (bool)Static("Awakened.AwakenedCode.Core.AwakenedModel", "IsAwakened", _actor.Player)!;
    private string[] InstalledKeywords => _assembly.GetTypes()
        .Where(type => type.Namespace == $"{_name}.{_name}Code.CustomEnums")
        .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(field => field.FieldType.Name is "CardKeyword" or "StaticHoverTip")
        .Select(field => field.Name).Distinct().Order().ToArray();
    private IEnumerable<string> RequiredKeywords => InstalledKeywords.Concat(_name switch
    {
        "Automaton" => new[] { "Ethereal", "Innate", "Retain" },
        "Snecko" => new[] { "Unplayable", "Eternal" },
        "Hermit" => new[] { "Strike", "Defend" },
        _ => []
    });

    private void Check(bool condition, string assertion)
    {
        _checks.Add(assertion);
        if (!condition)
            _failures.Add(_case + ": " + assertion);
        MainFile.Logger.Info("DOWNFALL KEYWORD ASSERT " + JsonSerializer.Serialize(new
        {
            character = _name, probe = _case, assertion, passed = condition
        }));
    }

    private void CheckRoster()
    {
        var live = (CombatState)_human.Creature.CombatState!;
        var reflectedPlayers = (IReadOnlyList<Player>)typeof(CombatState).GetProperty(nameof(CombatState.Players))!
            .GetValue(live)!;
        Check(live.Players.SequenceEqual(new[] { _human }) &&
            ((ICombatState)live).Players.SequenceEqual(new[] { _human }) &&
            reflectedPlayers.SequenceEqual(new[] { _human }) &&
            live.PlayerCreatures.SequenceEqual(new[] { _human.Creature }),
            "concrete, interface and reflected live rosters contain only the human, never the corrupted actor");
        Check(_actor.View.Players.SequenceEqual(new[] { _actor.Player }) &&
            _actor.View.PlayerCreatures.SequenceEqual(new[] { _actor.Body }) &&
            _actor.Body.Side == CombatSide.Enemy,
            "card-effect roster retains its private corrupted player while absolute side stays Enemy");
    }

    private object State() => new
    {
        humanHp = _human.Creature.CurrentHp, corruptedHp = _actor.Body.CurrentHp,
        humanBlock = _human.Creature.Block, corruptedBlock = _actor.Body.Block,
        humanEnergy = _human.PlayerCombatState!.Energy, corruptedEnergy = _actor.State.Energy,
        hand = _actor.State.Hand.Cards.Select(card => card.Id.Entry).ToArray(),
        draw = _actor.State.DrawPile.Cards.Select(card => card.Id.Entry).ToArray(),
        discard = _actor.State.DiscardPile.Cards.Select(card => card.Id.Entry).ToArray(),
        exhaust = _actor.State.ExhaustPile.Cards.Select(card => card.Id.Entry).ToArray(),
        powers = _actor.Body.Powers.Select(power => new { id = power.Id.Entry, power.Amount }).ToArray(),
        humanPowers = _human.Creature.Powers.Select(power => new { id = power.Id.Entry, power.Amount }).ToArray()
    };

    private async Task Probe(string name, string[] keywords, CardModel[] hand, Action<CardModel> afterPlay,
        CardModel[]? draw = null, CardModel[]? exhaust = null, CardModel[]? expected = null,
        int turns = 1, Action<int>? afterTurn = null, bool preservePowers = false, bool stopWhenPlayed = false,
        Func<Task>? fixture = null, CardModel[]? nonPlayControls = null,
        Action<int>? beforeTurn = null, Action<int>? atTurnEnd = null)
    {
        _case = name;
        _checks = [];
        var beforeFailures = _failures.Count;
        var combat = _human.Creature.CombatState!;
        foreach (var pile in new[] { _actor.State.Hand, _actor.State.DrawPile, _actor.State.DiscardPile,
                     _actor.State.ExhaustPile, _actor.State.PlayPile, Stasis, Sequence, Stash })
            foreach (var card in pile.Cards.ToArray())
            {
                pile.RemoveInternal(card);
                combat.RemoveCard(card);
            }
        if (!preservePowers)
        {
            _actor.Body.RemoveAllPowersInternalExcept();
            _human.Creature.RemoveAllPowersInternalExcept();
        }
        _actor.Body.LoseBlockInternal(_actor.Body.Block);
        _human.Creature.LoseBlockInternal(_human.Creature.Block);
        _actor.Player.MaxEnergy = 10;
        _actor.State.ResetEnergy();
        if (fixture != null)
            await fixture();
        foreach (var (pile, cards) in new[]
                 {
                     (_actor.State.Hand, hand), (_actor.State.DrawPile, draw ?? []),
                     (_actor.State.ExhaustPile, exhaust ?? [])
                 })
            foreach (var card in cards)
            {
                combat.AddCard(card, _actor.Player);
                pile.AddInternal(card);
                Check(_actor.UnsupportedReason(card) == null, card.Id + " is executable, not an unsupported skip");
            }
        expected ??= hand.Concat(draw ?? []).Where(card => card is not MegaCrit.Sts2.Core.Models.Cards.Wound).ToArray();
        nonPlayControls ??= [];
        expected = expected.Concat(hand.Where(card => card is not MegaCrit.Sts2.Core.Models.Cards.Wound))
            .Except(nonPlayControls).Distinct().ToArray();
        if (!stopWhenPlayed)
            foreach (var card in expected.Concat(nonPlayControls.Where(card =>
                         !card.Keywords.Contains(CardKeyword.Unplayable))))
                _representatives.TryAdd(card.Id + "+" + card.CurrentUpgradeLevel + ":" +
                    string.Join(",", CardModifier.Modifiers(card).Select(modifier => modifier.Id)), card);
        var played = new HashSet<CardModel>();
        var fixtureCards = hand.Concat(draw ?? []).Concat(exhaust ?? []).ToHashSet();
        var generated = new HashSet<CardModel>();
        var playLog = new List<object>();
        var humanRng = "";
        var humanEnergy = 0;
        var activeTurn = 0;
        void Start()
        {
            humanRng = NativeDemoPlaytest.AllRng(_human);
            humanEnergy = _human.PlayerCombatState!.Energy;
            Check(_actor.State.Energy == 10, "corrupted turn starts with exactly 10 energy");
            if (_name == "SlimeBoss")
                CheckRoster();
        }
        void Played(CardModel card)
        {
            played.Add(card);
            Check(card.Owner == _actor.Player, card.Id + " is played by the corrupted owner");
            Check(_human.PlayerCombatState!.Energy == humanEnergy, card.Id + " preserves human energy");
            afterPlay(card);
            generated.UnionWith(new[] { _actor.State.Hand, _actor.State.DrawPile, _actor.State.DiscardPile,
                    _actor.State.ExhaustPile, Stasis, Sequence, Stash }
                .SelectMany(pile => pile.Cards).Except(fixtureCards));
            var evidence = new { card = card.Id.ToString(), upgraded = card.CurrentUpgradeLevel, state = State() };
            playLog.Add(evidence);
            MainFile.Logger.Info("DOWNFALL KEYWORD PLAY " + JsonSerializer.Serialize(evidence));
        }
        void Finish()
        {
            Check(humanRng == NativeDemoPlaytest.AllRng(_human), "turn preserves all human RNG streams");
            if (_name == "SlimeBoss")
                CheckRoster();
            atTurnEnd?.Invoke(activeTurn);
        }
        _actor.TurnStarting += Start;
        _actor.CardPlayed += Played;
        _actor.TurnFinished += Finish;
        try
        {
            await PresentProbe("begin", keywords);
            for (var turn = 1; turn <= turns; turn++)
            {
                activeTurn = turn;
                beforeTurn?.Invoke(turn);
                var humanTurn = _human.PlayerCombatState!.TurnNumber;
                PlayerCmd.EndTurn(_human, false);
                await NativeDemoPlaytest.PlayerTurn(_human, humanTurn + 1, timeoutSeconds: 180);
                afterTurn?.Invoke(turn);
                _actor.AssertIdentity();
                if (stopWhenPlayed && expected.All(played.Contains) &&
                    generated.All(card => played.Contains(card) || card.Keywords.Contains(CardKeyword.Unplayable)))
                    break;
            }
            foreach (var card in expected)
                Check(played.Contains(card), card.Id + " was actually played, not merely catalogued/drawn");
            foreach (var card in nonPlayControls)
                Check(!played.Contains(card), card.Id + " intentionally remains unplayed for its explicit lifecycle control");
            if (stopWhenPlayed)
                foreach (var card in generated.Where(card => !card.Keywords.Contains(CardKeyword.Unplayable)))
                    Check(played.Contains(card), card.Id + " generated/selected card was actually played before stopping");
        }
        catch (Exception error)
        {
            _failures.Add(name + ": " + error.Message);
            throw;
        }
        finally
        {
            _actor.TurnStarting -= Start;
            _actor.CardPlayed -= Played;
            _actor.TurnFinished -= Finish;
            var passed = beforeFailures == _failures.Count;
            if (passed)
                _covered.UnionWith(keywords);
            _results.Add(new
            {
                probe = name, keywords, passed, assertions = _checks.ToArray(), plays = playLog,
                unplayedSelected = expected.Where(card => !played.Contains(card)).Select(card => card.Id.ToString()).ToArray(),
                intentionalNonPlayControls = nonPlayControls.Select(card => new
                {
                    card = card.Id.ToString(), wasPlayed = played.Contains(card), finalPile = card.Pile?.Type.ToString()
                }).ToArray(),
                generatedNotPlayed = generated.Where(card => !played.Contains(card)).Select(card => new
                {
                    card = card.Id.ToString(), pile = card.Pile?.Type.ToString(),
                    unsupported = _actor.UnsupportedReason(card),
                    unplayableKeyword = card.Keywords.Contains(CardKeyword.Unplayable)
                }).ToArray()
            });
            WriteReport();
            await PresentProbe("end", keywords, passed);
        }
    }

    private void WriteReport()
    {
        var inventory = InstalledKeywords;
        var report = new
        {
            character = _name, assembly = _assembly.FullName, humanMaxHp = _human.Creature.MaxHp,
            corruptedMaxHp = _actor.Body.MaxHp, energyPerTurn = 10,
            installedKeywords = inventory, coveredKeywords = _covered.Order().ToArray(),
            requiredKeywords = RequiredKeywords.Order().ToArray(),
            uncoveredKeywords = RequiredKeywords.Except(_covered).ToArray(),
            scopeLimitations = _name switch
            {
                "Snecko" => new[]
                {
                    "Gift has combat non-trigger controls; acquisition rewards outside combat are not covered.",
                    "Eternal verifies native permanent-deck removal eligibility, not a positive combat effect or removal UI."
                },
                "Guardian" => new[] { "Socket fixture attaches a real Gem modifier; rest-site forging outside combat is not covered." },
                _ => []
            },
            failures = _failures.ToArray(), probes = _results
        };
        File.WriteAllText(Path.Combine(NativeDemoSafety.RuntimePath, "downfall-keywords.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task Snecko()
    {
        var gambit = Card("GlitteringGambit");
        var ordinaryStrike = Card("StrikeSnecko");
        var gold = _actor.Player.Gold;
        await Probe("Unplayable-and-Eternal-snapshot-control", ["Unplayable", "Eternal", "Gift"],
            [gambit, ordinaryStrike], _ =>
            {
                Check(!gambit.CanPlay() && _actor.State.Energy == 9,
                    "actual GlitteringGambit remains unplayable despite available energy; ordinary Strike spends only one");
                Check(_actor.Player.Gold == gold, "playing beside Gift does not trigger its acquisition gold reward");
            }, nonPlayControls: [gambit], fixture: () =>
            {
                var restored = RestoreRepresentative(gambit);
                Check(!gambit.IsRemovable && !restored.IsRemovable && ordinaryStrike.IsRemovable,
                    "Eternal prevents native permanent-deck removal eligibility before/after snapshot restore; ordinary Strike is eligible");
                return Task.CompletedTask;
            }, atTurnEnd: _ => Check(gambit.Pile == _actor.State.DiscardPile,
                "non-Ethereal Unplayable card follows normal discard cleanup without being played"));
        foreach (var count in new[] { 4, 5 })
        {
            var dice = Card("DiceBlock");
            await Probe("Overflow-" + count, ["Overflow"], [dice, .. Enumerable.Range(0, count).Select(_ => Wound())],
                _ => Check(_actor.Body.Block == (count == 5 ? 10 : 5), "Overflow boundary changes Block from 5 to 10"));
        }
        var sword = Card("RiskySword");
        var original = sword.EnergyCost.GetResolved();
        var hp = _human.Creature.CurrentHp;
        await Probe("Muddle-cost-and-hook", ["Muddle"], [Card("SoulExchange"), sword], card =>
        {
            if (card.GetType().Name == "SoulExchange")
                Check(sword.EnergyCost.GetResolved() is >= 0 and <= 3 && sword.EnergyCost.GetResolved() != original &&
                    sword.DynamicVars.Damage.BaseValue == 16, "Muddle changes cost, excludes original, triggers RiskySword increase");
            else
                Check(_human.Creature.CurrentHp == hp - 16, "Muddled RiskySword deals the increased 16 damage");
        });
        var offclass = Card("StrikeChamp", "Champ");
        var own = Card("StrikeSnecko");
        var deckCount = _actor.Player.Deck.Cards.Count;
        await Probe("Offclass-upgrade-and-Gift-combat-control", ["Offclass", "Gift"], [Card("WideSting"), offclass, own], card =>
        {
            if (card.GetType().Name == "WideSting")
            {
                Check(offclass.CurrentUpgradeLevel == 1 && own.CurrentUpgradeLevel == 0,
                    "WideSting upgrades offclass only, not the owner's own pool");
                Check(card.GetType().GetProperty("Gift")!.GetValue(card) != null &&
                    _actor.Player.Deck.Cards.Count == deckCount,
                    "WideSting has a real Gift, but combat play does not incorrectly award an acquisition reward");
            }
        });
    }

    private async Task SlimeBoss()
    {
        var hp = _human.Creature.CurrentHp;
        await Probe("Consume-positive", ["Consume", "Goop"], [Card("CorrosiveSpit"), Card("OpeningTackle")], card =>
        {
            if (card.GetType().Name == "CorrosiveSpit")
                Check(Power(_human.Creature, "GoopPower") == 6 && Power(_actor.Body, "GoopPower") == 0, "Goop targets only human");
            else
                Check(_human.Creature.CurrentHp == hp - 18 && Power(_human.Creature, "VulnerablePower") == 2 &&
                    Power(_human.Creature, "GoopPower") == 0, "Consume adds Goop damage, applies Vulnerable and removes Goop");
        });
        await Probe("Consume-negative", ["Consume"], [Card("OpeningTackle")], _ =>
            Check(Power(_human.Creature, "VulnerablePower") == 0, "without Goop, Consume does not apply Vulnerable"));
        var buried = Card("Lick");
        var normal = Card("DoubleLick");
        await Probe("Slurp-prefers-unburied", ["Slurp", "Buried"], [Card("Recollect")], card =>
        {
            if (card.GetType().Name == "Recollect")
                Check(_actor.State.Hand.Cards.Contains(normal) && buried.Pile == _actor.State.ExhaustPile,
                    "Slurp retrieves non-Buried DoubleLick before Buried Lick");
            if (ReferenceEquals(card, normal))
                Check(Power(_human.Creature, "GoopPower") == 8 && normal.Pile == _actor.State.ExhaustPile,
                    "retrieved DoubleLick actually plays twice and Exhausts again");
        }, exhaust: [buried, normal], expected: [normal]);
        buried = Card("Lick");
        await Probe("Slurp-buried-fallback", ["Slurp", "Buried"], [Card("Recollect")], card =>
        {
            if (card.GetType().Name == "Recollect")
                Check(_actor.State.Hand.Cards.Contains(buried), "Buried Lick is retrieved when no ordinary Licks remain");
            if (ReferenceEquals(card, buried))
                Check(Power(_human.Creature, "GoopPower") == 4, "retrieved Buried Lick actually applies Goop");
        }, exhaust: [buried], expected: [buried]);
        hp = _human.Creature.CurrentHp;
        await Probe("Split-and-Command", ["Command"], [Card("SplitBruiser")], _ =>
            Check(_actor.State.Pets.Count == 1 && _actor.State.Pets.All(pet => pet.PetOwner == _actor.Player &&
                pet.Side == _actor.Body.Side) && _human.Creature.CurrentHp == hp - 12,
                "owned Bruiser commands hit only human twice"), afterTurn: _ =>
            Check(_human.Creature.CurrentHp == hp - 18, "next hand draw automatically commands Bruiser"));
    }

    private async Task Hermit()
    {
        var loaded = Card("FullyLoaded");
        var basicStrike = Card("StrikeHermit");
        var basicDefend = Card("DefendHermit");
        var nonBasic = Card("Headshot");
        var beforeTagsHp = _human.Creature.CurrentHp;
        await Probe("Strike-and-Defend-tag-retrieval", ["Strike", "Defend"], [loaded], card =>
        {
            if (card == loaded)
                Check(basicStrike.Pile == _actor.State.Hand && basicDefend.Pile == _actor.State.Hand &&
                    nonBasic.Pile == _actor.State.DrawPile,
                    "FullyLoaded retrieves actual basic Strike and Defend tags, leaving untagged Headshot in Draw");
            if (card == basicStrike)
                Check(_human.Creature.CurrentHp == beforeTagsHp - 6,
                    "retrieved Strike actually plays for six damage");
            if (card == basicDefend)
                Check(_actor.Body.Block == 5, "retrieved Defend actually plays for five Block");
            beforeTagsHp = _human.Creature.CurrentHp;
        }, draw: [basicStrike, basicDefend, nonBasic], turns: 2);
        foreach (var middle in new[] { false, true })
        {
            var shot = Card("Headshot");
            var hp = _human.Creature.CurrentHp;
            await Probe("DeadOn-" + middle, ["DeadOn"],
                middle ? [Wound(), shot, Wound()] : [shot, Wound(), Wound()],
                _ => Check(_human.Creature.CurrentHp == hp - (middle ? 14 : 7),
                    "Headshot middle-position damage 14 versus off-center 7"));
        }
        foreach (var index in new[] { 1, 2 })
        {
            var hand = Enumerable.Range(0, 4).Select(_ => Wound()).ToArray();
            hand[index] = Card("Headshot");
            var hp = _human.Creature.CurrentHp;
            await Probe("DeadOn-even-hand-middle-" + index, ["DeadOn"], hand, _ =>
                Check(_human.Creature.CurrentHp == hp - 14, "either middle position of an even hand triggers DeadOn"));
        }
        var hp2 = _human.Creature.CurrentHp;
        await Probe("Concentrate-off-center", ["Concentrate"],
            [Card("EyeOfTheStorm"), Card("Headshot"), Wound(), Wound()], card =>
            {
                if (card.GetType().Name == "EyeOfTheStorm")
                    Check(_actor.State.Energy == 10, "EyeOfTheStorm refills private energy");
                else
                    Check(_human.Creature.CurrentHp == hp2 - 14, "Concentrate activates off-center DeadOn");
            });
        var gold = _actor.Player.Gold;
        var hp3 = _human.Creature.CurrentHp;
        await Probe("Bounty-nonfatal-control", ["Bounty"], [Card("DeadOrAlive")], _ =>
            Check(_human.Creature.CurrentHp == hp3 - 80 && _actor.Player.Gold == gold && _actor.State.Energy == 0,
                "nonfatal 10-energy Bounty hits ten times and grants no gold"));
    }

    private async Task FatalBounty()
    {
        _case = "Bounty-fatal-boss-reward";
        _checks = [];
        var failureCount = _failures.Count;
        foreach (var pile in new[] { _actor.State.Hand, _actor.State.DrawPile, _actor.State.DiscardPile })
            foreach (var card in pile.Cards.ToArray())
            {
                pile.RemoveInternal(card);
                _human.Creature.CombatState!.RemoveCard(card);
            }
        _actor.Body.RemoveAllPowersInternalExcept();
        _human.Creature.RemoveAllPowersInternalExcept();
        _human.Creature.LoseBlockInternal(_human.Creature.Block);
        // A final fatal-boundary fixture; the run and both maximum HP values remain 1000.
        _human.Creature.SetCurrentHpInternal(1);
        var bounty = Card("DeadOrAlive");
        _human.Creature.CombatState!.AddCard(bounty, _actor.Player);
        _actor.State.Hand.AddInternal(bounty);
        _actor.State.ResetEnergy();
        var originalHumanGold = _human.Gold;
        await PlayerCmd.GainGold(1, _human);
        Check(_human.Gold == originalHumanGold + 1, "ordinary human gold command still works unchanged");
        var gold = _actor.Player.Gold;
        var humanGold = _human.Gold;
        await PresentProbe("begin", ["Bounty"]);
        PlayerCmd.EndTurn(_human, false);
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (_actor.Player.Gold == gold && DateTime.UtcNow < deadline)
            await Task.Delay(100);
        Check(_human.Creature.CurrentHp <= 0 && _actor.Player.Gold == gold + 100 && _human.Gold == humanGold,
            "actual DeadOrAlive killing blow awards boss Bounty 100 only to corrupted player");
        _results.Add(new
        {
            probe = _case, keywords = new[] { "Bounty" }, passed = failureCount == _failures.Count,
            assertions = _checks.ToArray(), fixtureHumanCurrentHp = 1,
            card = bounty.Id.ToString(), corruptedGoldBefore = gold, corruptedGoldAfter = _actor.Player.Gold
        });
        WriteReport();
        await PresentProbe("end", ["Bounty"], failureCount == _failures.Count);
    }

    private async Task Hexaghost()
    {
        _api.ResetWheel(_actor.Player);
        await Probe("Advance-and-Retract", ["Advance", "Retract", "Wheel"],
            [Card("AdvancingGuard"), Card("BacktrackSmack")], card =>
                Check(_api.GetWheelIndex(_actor.Player) == (card.GetType().Name == "AdvancingGuard" ? 1 : 0),
                    "keywords move the private wheel forward/backward"));
        _api.ResetWheel(_actor.Player);
        await Probe("Ignite-Extinguish-reignite", ["Ignite", "Extinguish", "Wheel"],
            [Card("FleetingFlare"), Card("Kindle")], card =>
            {
                var flare = card.GetType().Name == "FleetingFlare";
                Check(_api.IsIgnited(_actor.Player) != flare && Power(_human.Creature, "SoulBurnPower") == (flare ? 6 : 12),
                    "Extinguish permits another Searing ignition, owned Soulburn reaches human only");
            }, afterTurn: _ => Check(_api.GetWheelIndex(_actor.Player) == 1, "ignited wheel advances once at turn end"));
        _api.ResetWheel(_actor.Player);
        var sear = Card("Sear");
        sear.AddKeyword(CardKeyword.Exhaust);
        var hp = _human.Creature.CurrentHp;
        await Probe("Afterlife-play-and-exhaust", ["Afterlife"], [sear], _ =>
            Check(_human.Creature.CurrentHp == hp - 5 && Power(_human.Creature, "SoulBurnPower") == 10 &&
                Power(_actor.Body, "SoulBurnPower") == 0 && sear.Pile == _actor.State.ExhaustPile,
                "played Sear deals 5, applies 5 Soulburn and Exhaust triggers its 5-Soulburn Afterlife again"));
    }

    private async Task Champ()
    {
        await Probe("Combo-no-stance-control", ["Combo"], [Card("Shatter")], _ =>
            Check(Power(_human.Creature, "VulnerablePower") == 0 && Power(_human.Creature, "WeakPower") == 0,
                "Shatter without a stance does not trigger Combo"));
        await Probe("Combo-active-and-finisher", ["Combo", "Finisher", "TriggerSkillBonus"],
            [Card("BerserkersShout"), Card("Shatter"), Card("DefensiveShout"), Card("Execute")], card =>
            {
                if (card.GetType().Name == "Shatter")
                    Check(Power(_human.Creature, "VulnerablePower") == 1 && Power(_human.Creature, "WeakPower") == 1,
                        "stance activates Shatter's Combo debuffs on human");
                if (card.GetType().Name == "Execute")
                    Check(_api.NoStance.IsInstanceOfType(_api.GetStance(_actor.Player)) && _actor.Body.Block == 6,
                        "Defensive Finisher blocks 6 and exits stance");
            });
        CardModel[] echoes = [];
        var hp = _human.Creature.CurrentHp;
        await Probe("Echo-generation-and-execution", ["Echo"], [Card("TripleStrike")], card =>
        {
            if (card.GetType().Name == "TripleStrike")
            {
                echoes = _actor.State.Hand.Cards.ToArray();
                Check(echoes.Length == 2 && echoes.All(echo => echo.GetType().Name == "StrikeChamp" &&
                    echo.Keywords.Contains(CardKeyword.Ethereal) && echo.Keywords.Contains(CardKeyword.Exhaust) &&
                    echo.EnergyCost.GetResolved() == 0), "TripleStrike creates two free Ethereal Exhaust Echoes");
            }
        }, afterTurn: _ => Check(echoes.Length == 2 && echoes.All(echo => echo.Pile == _actor.State.ExhaustPile) &&
            _human.Creature.CurrentHp == hp - 18, "both generated Echoes actually attack and Exhaust"));
    }

    private async Task Awakened()
    {
        var spellbook = _api.GetSpellbook(_actor.Player);
        var spells = spellbook.Cards.ToArray();
        await Probe("Conjure-and-Spellbook", ["Conjure", "Spellbook"], [Card("TalonRake")], card =>
        {
            if (card.GetType().Name == "TalonRake")
                Check(spellbook.Cards.Count == 3 && _actor.State.Hand.Cards.Any(spells.Contains),
                    "Conjure moves the next private Spellbook spell into owned hand");
        });
        var recitation = Card("Recitation");
        var chanted = Card("Recitation");
        var hp = _human.Creature.CurrentHp;
        await Probe("Chant-control-and-activation", ["Chant", "Chanted"],
            [recitation, Card("Ceremony"), chanted], card =>
            {
                if (ReferenceEquals(card, recitation))
                    Check(_human.Creature.CurrentHp == hp - 6, "unchanted Recitation attacks only once");
                else if (card.GetType().Name == "Recitation")
                    Check((bool)card.GetType().GetProperty("HasChanted")!.GetValue(card)! &&
                        _human.Creature.CurrentHp == hp - 20, "Power predecessor activates Chant's second powered attack");
            });
        hp = _human.Creature.CurrentHp;
        await Probe("Chanted-persists-without-new-Power", ["Chanted"], [chanted], _ =>
            Check(_human.Creature.CurrentHp == hp - 12 &&
                (bool)chanted.GetType().GetProperty("HasChanted")!.GetValue(chanted)!,
                "previously Chanted Recitation retains its second hit without another Power predecessor"));
        var immolation = Card("Immolation");
        await Probe("Drained-versus-card-cost", ["Drained"], [Card("StrikeAwakened"), Card("Spew"), immolation], card =>
        {
            if (card.GetType().Name == "StrikeAwakened")
                Check(immolation.EnergyCost.GetResolved() == 3, "ordinary energy payment is not Drained");
            if (card.GetType().Name == "Spew")
                Check(immolation.EnergyCost.GetResolved() == 2, "Spew's Drained event discounts Immolation");
            if (ReferenceEquals(card, immolation))
                Check(_actor.Body.Block == 13, "discounted Immolation executes its Block effect");
        });
        var powers = Enumerable.Range(0, 6).Select(_ => Card("Ceremony")).ToArray();
        var powerCount = 0;
        await Probe("Awaken-seven-power-threshold", ["Awaken"], powers, _ =>
        {
            powerCount++;
            Check(IsAwakened == (powerCount == 6), "Awaken stays inactive through six Powers and activates on seven");
        }, afterTurn: _ =>
            Check(IsAwakened, "seven actually played Power cards activate Awaken"));
        await Probe("Awaken-upgrades-conjured-spell", ["Awaken", "Conjure"], [Card("TalonRake")], card =>
        {
            if (card.GetType().Name == "TalonRake")
                Check(_actor.State.Hand.Cards.Where(c => c.GetType().Namespace?.Contains(".Token") == true)
                    .Any(c => c.CurrentUpgradeLevel > 0), "Awaken upgrades generated Spell before it is played");
        });
    }

    private async Task Automaton()
    {
        var opening = _actor.State.Hand.Cards.ToArray();
        var openingCorrect = opening.Length == 6 && opening.All(card => card.GetType().Name == "Backtrace") &&
            _actor.State.DrawPile.Cards.Count == 6 &&
            _actor.State.DrawPile.Cards.All(card => !card.Keywords.Contains(CardKeyword.Innate));
        await Probe("Innate-native-opening-overdraw", ["Innate"], opening, _ => { }, fixture: () =>
        {
            Check(openingCorrect,
                "real snapshot setup draws all six Innate Backtraces above normal five-card draw and leaves six non-Innate cards behind");
            return Task.CompletedTask;
        });
        var laterInnate = Card("Backtrace");
        var laterDraw = Enumerable.Range(0, 6).Select(_ => Card("DefendAutomaton")).Append(laterInnate).ToArray();
        await Probe("Innate-does-not-reorder-later-turns", ["Innate"], [], _ => { },
            draw: laterDraw, turns: 3, beforeTurn: turn =>
            {
                if (turn == 1)
                    _actor.State.DrawPile.MoveToBottomInternal(laterInnate);
            }, afterTurn: turn =>
            {
                if (turn == 1)
                    Check(laterInnate.Pile == _actor.State.DrawPile,
                        "after opening turn Innate does not jump ahead of six ordinary cards");
                if (turn == 2)
                    Check(laterInnate.Pile == _actor.State.Hand,
                        "later Innate eventually arrives through ordinary draw progression");
            });
        var energySink = Card("DigitalCarnage");
        energySink.EnergyCost.AddThisTurn(8);
        var fading = Card("DigitalCarnage");
        var retained = Card("StickyShield");
        var ordinary = Card("DefendAutomaton");
        await Probe("Ethereal-and-Retain-unplayed-cleanup", ["Ethereal", "Retain"],
            [energySink, fading, retained, ordinary], card =>
            {
                if (card == energySink)
                    Check(_actor.State.Energy == 0,
                        "explicit +8 temporary-cost fixture spends all ten energy by actually playing DigitalCarnage");
                if (card == retained)
                    Check(_actor.Body.Block == 9, "retained StickyShield plays on the following turn for nine Block");
            }, turns: 2, nonPlayControls: [fading], atTurnEnd: turn =>
            {
                if (turn != 1)
                    return;
                Check(fading.Pile == _actor.State.ExhaustPile,
                    "unplayed actual Ethereal DigitalCarnage exhausts at native turn end");
                Check(retained.Pile == _actor.State.Hand,
                    "unplayed actual Retain StickyShield stays in Hand through native flush");
                Check(ordinary.Pile == _actor.State.DiscardPile,
                    "ordinary non-Retain non-Ethereal Defend is discarded instead");
            });
        var playedEthereal = Card("DigitalCarnage");
        var playedRetain = Card("StickyShield");
        var lifecycleHp = _human.Creature.CurrentHp;
        await Probe("Ethereal-and-Retain-played-controls", ["Ethereal", "Retain"],
            [playedEthereal, playedRetain], card =>
            {
                if (card == playedEthereal)
                    Check(_human.Creature.CurrentHp == lifecycleHp - 20 && playedEthereal.Pile != _actor.State.ExhaustPile,
                        "played Ethereal DigitalCarnage deals twenty damage and is not exhausted by Ethereal");
                if (card == playedRetain)
                    Check(playedRetain.Pile == _actor.State.DiscardPile,
                        "played Retain StickyShield discards normally; Retain applies only while unplayed");
            }, atTurnEnd: _ => Check(playedEthereal.Pile != _actor.State.ExhaustPile,
                "Ethereal does not later exhaust a card already played out of Hand"));
        await Probe("Encode-starter-negative-control", ["Encode"],
            [Card("StrikeAutomaton"), Card("DefendAutomaton")], _ =>
                Check(Sequence.Cards.Count == 0, "starter cards expose encodings but cannot enter Sequence naturally"));
        var hp = _human.Creature.CurrentHp;
        var count = 0;
        await Probe("Encode-three-and-Compile", ["Encode", "Compile"],
            [Card("Boost"), Card("MinorBeam"), Card("Safeguard")], card =>
            {
                count++;
                if (count < 3)
                    Check(Sequence.Cards.Count == count, "Sequence holds encoded card until three-card threshold");
                if (count == 3)
                    Check(Sequence.Cards.Count == 0 && Power(_actor.Body, "StrengthPower") == 2 &&
                        _actor.State.Hand.Cards.Any(c => c.GetType().Name == "FunctionCard"),
                        "third Encode compiles Function and Boost grants private Strength");
                if (card.GetType().Name == "FunctionCard")
                    Check(_human.Creature.CurrentHp == hp - 14 && _actor.Body.Block == 20,
                        "generated Function plays merged damage and Block effects");
            });
        var strike = Card("StrikeAutomaton");
        var stashed = false;
        await Probe("Stash-FIFO-turn-draw", ["Stash"], [Card("BitShift")], card =>
        {
            if (card.GetType().Name == "BitShift" && !stashed)
            {
                Check(Stash.Cards.Contains(strike), "BitShift moves top draw card into private Stash");
                stashed = true;
            }
        }, draw: [strike], expected: [strike], turns: 2);
        var wounds = Enumerable.Range(0, 7).Select(_ => Wound()).ToArray();
        var stashes = 0;
        await Probe("Stash-five-card-capacity", ["Stash"],
            Enumerable.Range(0, 7).Select(_ => Card("BitShift")).ToArray(), _ =>
            {
                stashes++;
                Check(Stash.Cards.Count == Math.Min(stashes, 5), "Stash accepts five cards and never exceeds capacity");
                if (stashes > 5)
                    Check(wounds[stashes - 1].Pile == _actor.State.DiscardPile, "Stash overflow goes to Discard, not lost");
            }, draw: wounds, afterTurn: _ =>
                Check(wounds[0].Pile == _actor.State.Hand, "next turn draws the oldest stashed card first"));
    }

    private async Task Guardian()
    {
        var shield = Card("ShieldCharger");
        await Probe("Stasis-Tick-Volatile", ["Stasis", "Tick", "Volatile", "Brace"], [shield], card =>
        {
            if (ReferenceEquals(card, shield))
                Check(Stasis.Cards.Contains(shield) && Counter(shield) == 3,
                    "ShieldCharger enters private Stasis with cost+1 counter");
        }, turns: 3, afterTurn: turn =>
        {
            if (turn < 3)
                Check(Counter(shield) == 3 - turn && _actor.Body.Block == 10 &&
                    Power(_actor.Body, "ModeShiftPower") == 20 - turn * 4,
                    "native hand draw ticks Stasis, blocks 10 and Braces by 4");
            else
                Check(shield.Pile == _actor.State.ExhaustPile, "Volatile exhausts at zero instead of returning to hand");
        });
        shield = Card("ShieldCharger");
        await Probe("Accelerate-extra-tick", ["Accelerate"], [Card("TimeSifter"), shield], _ => { },
            afterTurn: _ => Check(Counter(shield) == 1 && _actor.Body.Block == 20,
                "TimeSifter adds one acceleration tick to the normal hand-draw tick"));
        var ruby = Card("Ruby");
        await Probe("Gem-and-Polish", ["Gem", "Polish"], [ruby, Card("ResilientPlate")], card =>
        {
            if (ReferenceEquals(card, ruby))
                Check(Power(_actor.Body, "StrengthPower") == 2 && Power(_actor.Body, "RubyGemPower") == 2 &&
                    Power(_human.Creature, "StrengthPower") == 0, "Ruby grants owned temporary Strength only");
            else
                Check(Power(_actor.Body, "StrengthPower") == 2 && Power(_actor.Body, "RubyGemPower") == 0 &&
                    Power(_actor.Body, "WeakPower") == 1 && Power(_actor.Body, "FrailPower") == 0 &&
                    Power(_actor.Body, "VulnerablePower") == 0,
                    "Polish converts temporary Strength and reduces Weak/Frail/Vulnerable by up to 2 each");
        }, afterTurn: _ => Check(Power(_actor.Body, "StrengthPower") == 2, "Polished Strength persists across turn cleanup"),
            fixture: () => _actor.RunOwnedChoice(async ctx =>
            {
                await PowerCmd.Apply<WeakPower>(ctx, _actor.Body, 3, _actor.Body, null);
                await PowerCmd.Apply<FrailPower>(ctx, _actor.Body, 1, _actor.Body, null);
                await PowerCmd.Apply<VulnerablePower>(ctx, _actor.Body, 2, _actor.Body, null);
            }));
        var canonicalRuby = _catalog.Single(card => card.GetType().FullName == "Guardian.GuardianCode.Cards.Abstract.Ruby");
        var missingModifiers = Card("Ruby");
        // Fault-inject only the omitted extension collection, not a Gem effect.
        // The production boundary must recover it through BaseLib's real clone callback.
        CardModifier.DirectModifiers(missingModifiers).Clear();
        NativeCardCloning.PreserveModifiers(canonicalRuby, missingModifiers);
        NativeCardCloning.PreserveModifiers(canonicalRuby, missingModifiers);
        await Probe("Gem-omitted-clone-collection-boundary", ["Gem"], [missingModifiers], _ =>
            Check(Power(_actor.Body, "StrengthPower") == 2 && Power(_actor.Body, "RubyGemPower") == 2,
                "repaired Ruby actually applies its native Gem effect once"), fixture: () =>
            {
                var modifiers = CardModifier.Modifiers(missingModifiers);
                Check(modifiers.Count == 1 && modifiers[0].Owner == missingModifiers &&
                    !ReferenceEquals(modifiers[0], CardModifier.Modifiers(canonicalRuby).Single()),
                    "explicit omitted-collection fault is repaired by BaseLib cloning; a second guard call is idempotent");
                return Task.CompletedTask;
            });
        var cloneCard = Card("Clone");
        var sourceRuby = Card("Ruby");
        CardModel? generatedRuby = null;
        var generatedRubyPlayed = false;
        decimal expectedStrength = 0;
        await Probe("Gem-native-generated-clone", ["Gem"], [cloneCard, sourceRuby], card =>
        {
            if (card == cloneCard)
            {
                generatedRuby = Stasis.Cards.Single();
                var sourceModifier = CardModifier.Modifiers(sourceRuby).Single();
                var generatedModifiers = CardModifier.Modifiers(generatedRuby);
                var preserved = generatedRuby.GetType() == sourceRuby.GetType() &&
                    generatedModifiers.Count == 1 && generatedModifiers[0].Id == sourceModifier.Id &&
                    generatedModifiers[0].Owner == generatedRuby &&
                    !ReferenceEquals(generatedModifiers[0], sourceModifier);
                Check(preserved, "actual Clone card generates an independently owned Ruby modifier through the native card factory");
                if (!preserved)
                    throw new InvalidOperationException("Native generated Ruby clone lost its Gem modifier.");
            }
            if (card.GetType().Name == "Ruby")
            {
                expectedStrength += 2;
                Check(Power(_actor.Body, "StrengthPower") == expectedStrength &&
                    Power(_human.Creature, "StrengthPower") == 0,
                    "original and naturally generated Ruby each execute their own temporary Strength effect");
                generatedRubyPlayed |= ReferenceEquals(card, generatedRuby);
            }
        }, turns: 2, beforeTurn: _ => expectedStrength = Power(_actor.Body, "StrengthPower"),
            afterTurn: turn =>
            {
                if (turn == 2)
                    Check(generatedRubyPlayed, "native generated Ruby leaves Stasis and is actually played");
            });
        var braceCount = 0;
        await Probe("Brace-threshold-DefensiveMode", ["Brace", "DefensiveMode"],
            [Card("ResilientPlate"), Card("ResilientPlate"), Card("ResilientPlate")], _ =>
            {
                braceCount++;
                if (braceCount < 3)
                    Check(Power(_actor.Body, "ModeShiftPower") == 20 - braceCount * 8 &&
                        Power(_actor.Body, "DefensiveModePower") == 0, "Brace below threshold does not enter Defensive Mode");
                else
                    Check(Power(_actor.Body, "DefensiveModePower") == 2 && Power(_actor.Body, "ThornsPower") == 3 &&
                        _actor.Body.Block == 16, "crossing threshold grants Defensive Mode, 3 Thorns and 16 Block");
            }, afterTurn: _ => Check(_actor.Body.Block == 16, "Defensive Mode preserves Block across turn boundary"));
        var humanHp = _human.Creature.CurrentHp;
        await NativeDemoPlaytest.HumanPlay<StrikeIronclad>(_human, _actor.Body);
        Check(_human.Creature.CurrentHp == humanHp - 3 && _actor.Body.Block == 10,
            "Defensive Mode's actual Thorns reflect 3 to attacking human while retained Block absorbs Strike");
        var beam = Card("RefractedBeam");
        var bismuth = Card("Bismuth");
        var gem = CardModifier.DirectModifiers(bismuth).Single();
        var clonedGem = (CardModifier)gem.GetType().GetMethod("CreateClone")!.Invoke(gem, null)!;
        CardModifier.AddModifier(beam, clonedGem);
        var slots = (int)Static("Guardian.GuardianCode.Core.GuardianCmd", "GetMaxStasisSlots", _actor.Player)!;
        var hp = _human.Creature.CurrentHp;
        await Probe("Socket-Aggravate-played-modifier", ["Socket", "Aggravate"], [beam], _ =>
        {
            Check(_human.Creature.CombatState!.IterateHookListeners().Count(model => ReferenceEquals(model, clonedGem)) == 1,
                "socketed modifier is a global combat hook listener exactly once");
            Check(_actor.State.Energy == 7 && _human.Creature.CurrentHp == hp - 9 &&
                (int)Static("Guardian.GuardianCode.Core.GuardianCmd", "GetMaxStasisSlots", _actor.Player)! == slots + 1,
                "socketed Bismuth adds 1 combat cost and 1 Stasis slot while RefractedBeam actually attacks");
        });
        CardModel? package = null;
        var packagePlayed = false;
        await Probe("Package-choice-and-generated-cards", ["Package"], [Card("CompilePackage")], card =>
        {
            if (card.GetType().Name == "CompilePackage")
            {
                package = Stasis.Cards.SingleOrDefault();
                Check(package != null && package.Owner == _actor.Player, "CompilePackage chooses an owned package into Stasis");
            }
            if (ReferenceEquals(card, package))
            {
                packagePlayed = true;
                Check(_actor.State.Hand.Cards.Count >= 3, "released Package actually creates its three constituent cards");
            }
        }, turns: 4, afterTurn: turn =>
        {
            if (turn == 4)
                Check(packagePlayed, "selected Package leaves Stasis and is actually played");
        });
    }
}
