using System.Collections.Immutable;
using System.Text.Json;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

int passed = 0;
var registry = new ExactCardAdapterRegistry();
var planner = new CorruptedPlayerPlanner(registry);
var inputs = new PlannerInputs { Opponents = [new(0)] };

MultiplayerPersistenceTests.Run(Test);
MultiplayerSynchronizationTests.Run(Test);

void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error}"); Environment.ExitCode = 1; }
}
void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}
CardDescriptor Card(string id, string model = "CARD.STRIKE_IRONCLAD", int cost = 1,
    SimulationKeywords keywords = SimulationKeywords.None, bool x = false, params (string, decimal)[] values) =>
    new(id, model, $"{{\"id\":\"{model}\",\"original\":\"{id}\"}}", 0, cost, x, keywords,
        (values.Length == 0 ? new[] { ("Damage", 6m) } : values).ToImmutableDictionary(v => v.Item1, v => v.Item2),
        HasStrikeTag: false);
CardDescriptor Perfected(int level = 0) =>
    Card("perfected", "CARD.PERFECTED_STRIKE", 2, values: [("CalculationBase", 6m), ("ExtraDamage", 2m + level)])
        with { UpgradeLevel = level, HasStrikeTag = true };
CardDescriptor Impervious(int level = 0) =>
    Card("impervious", "CARD.IMPERVIOUS", 2, SimulationKeywords.Exhaust, values: [("Block", 30m + 10m * level)])
        with { UpgradeLevel = level };
CorruptedPlayerState State(params CardDescriptor[] cards) => new(1, 0, 123, 0, cards.ToImmutableArray(), [], [], []);
string Json<T>(T value) => JsonSerializer.Serialize(value);
CorruptedPlayerPlan Plan(params CardDescriptor[] cards) => planner.BuildPlan(State(cards), inputs);
void Register(string id, Func<CardDescriptor, AdapterContext, AdapterResult> recipe) => registry.Register(new TestAdapter(id, recipe));

foreach (int ascension in new[] { 0, 1, 7, 8, 9, 10 })
{
    Test($"maximum HP scales from the original snapshot at A{ascension}", () =>
    {
        Check(CorruptedPlayerHealth.CalculateMaxHp(80, ascension) == (ascension >= 8 ? 200 : 160));
        Check(CorruptedPlayerHealth.CalculateMaxHp(81, ascension) == (ascension >= 8 ? 203 : 162));
        Check(CorruptedPlayerHealth.CalculateMaxHp(1, ascension) == (ascension >= 8 ? 3 : 2));
    });
}
Test("maximum HP scaling rejects nonpositive snapshot HP", () =>
{
    foreach (int hp in new[] { 0, -1 })
    {
        try { CorruptedPlayerHealth.CalculateMaxHp(hp, 8); }
        catch (ArgumentOutOfRangeException) { continue; }
        throw new InvalidOperationException("Invalid snapshot HP must not produce enemy HP.");
    }
});

Test("five-card draw, three energy, original input unchanged", () =>
{
    var state = State(Enumerable.Range(0, 8).Select(i => Card(i.ToString())).ToArray());
    string before = Json(state);
    var plan = planner.BuildPlan(state, inputs);
    Check(plan.Cards.Length == 5 && plan.SelectedCards.Count() == 3);
    Check(plan.NextState.Draw.Length == 3 && plan.NextState.Discard.Length == 5);
    Check(Json(state) == before);
});
Test("deterministic seed, identity separation and repeatable UI reads", () =>
{
    var deck = Enumerable.Range(0, 40).Select(i => Card(i.ToString())).ToArray();
    var a = CorruptedPlayerState.Create(deck, "run", "snapshot", "encounter");
    var b = CorruptedPlayerState.Create(deck, "run", "snapshot", "encounter");
    var c = CorruptedPlayerState.Create(deck, "run", "snapshot-2", "encounter");
    Check(Json(a) == Json(b) && Json(a) != Json(c));
    Check(Json(planner.BuildPlan(a, inputs)) == Json(planner.BuildPlan(a, inputs)));
});
Test("private RNG known vector stays versioned", () =>
{
    var rng = new PrivateRng(0);
    var values = Enumerable.Range(0, 10).ToList();
    rng.Shuffle(values);
    Check(rng.State == unchecked(9UL * 0x9E3779B97F4A7C15UL));
    Check(string.Join(",", values) == "6,3,2,9,8,1,4,7,0,5", string.Join(",", values));
});
Test("Innate precedes ordinary cards and keeps more than five", () =>
{
    var cards = Enumerable.Range(0, 9).Select(i => Card(i.ToString(), cost: 99, keywords: i < 2 ? SimulationKeywords.None : SimulationKeywords.Innate)).ToArray();
    var plan = Plan(cards);
    Check(plan.Cards.Length == 7 && plan.Cards.All(c => c.Card.Has(SimulationKeywords.Innate)));
    Check(plan.NextState.Draw.Length == 2);
});
Test("Innate overflow leaves undrawn cards in draw", () =>
{
    var plan = Plan(Enumerable.Range(0, 13).Select(i => Card(i.ToString(), cost: 99, keywords: SimulationKeywords.Innate)).ToArray());
    Check(plan.Cards.Length == 10 && plan.NextState.Draw.Length == 3);
    Check(plan.NextState.Draw[0].InstanceId == "10");
});
Test("Retain caps next draw without burning undrawn cards", () =>
{
    var retained = Enumerable.Range(0, 8).Select(i => Card($"h{i}", cost: 99, keywords: SimulationKeywords.Retain)).ToImmutableArray();
    var state = State(Enumerable.Range(0, 8).Select(i => Card($"d{i}", cost: 99)).ToArray()) with { Turn = 1, Hand = retained };
    var plan = planner.BuildPlan(state, inputs);
    Check(plan.Cards.Length == 10 && plan.NextState.Draw.Length == 6 && plan.NextState.Hand.Length == 8);
});
Test("played Exhaust and unplayed Ethereal, including Retain, exhaust", () =>
{
    var plan = Plan(Card("played", keywords: SimulationKeywords.Exhaust),
        Card("ethereal", cost: 99, keywords: SimulationKeywords.Ethereal | SimulationKeywords.Retain),
        Card("retain", cost: 99, keywords: SimulationKeywords.Retain));
    Check(plan.NextState.Exhaust.Length == 2 && plan.NextState.Hand.Single().InstanceId == "retain");
});
Test("ordinary unplayed discards; played Retain does not retain", () =>
{
    var plan = Plan(Card("retained", keywords: SimulationKeywords.Retain), Card("costly", cost: 99));
    Check(plan.NextState.Discard.Length == 2 && plan.NextState.Hand.Length == 0);
});
Test("draw shortage shuffles discard deterministically", () =>
{
    var state = State(Card("draw")) with { Turn = 1, Discard = [Card("a"), Card("b"), Card("c"), Card("d"), Card("e")] };
    var plan = planner.BuildPlan(state, inputs);
    Check(plan.Cards.Length == 5 && plan.NextState.Draw.Length == 1 && plan.NextState.RngState != state.RngState);
    Check(Json(plan) == Json(planner.BuildPlan(state, inputs)));
});
Test("unsupported card is zero-energy neutral even when unaffordable", () =>
{
    var plan = Plan(Card("unknown", "MOD.UNKNOWN", 99), Card("strike"));
    var unknown = plan.Cards[0];
    Check(unknown.Disposition == CardDisposition.Unsupported && unknown.EnergySpent == 0);
    Check(unknown.Actions.Single().Kind == ActionKind.Unknown && plan.Cards[1].EnergySpent == 1);
    Check(plan.NextState.Discard[0].InstanceId == "unknown" && plan.Diagnostics.Length == 1);
});
Test("intrinsic unplayable is ordinary unplayed even unsupported", () =>
{
    var plan = Plan(Card("curse", "MOD.CURSE", keywords: SimulationKeywords.Unplayable));
    Check(plan.SelectedCards.Count() == 0 && plan.Cards.Single().Disposition == CardDisposition.Unplayed);
});
Test("missing and failed adapter diagnostics preserve original card", () =>
{
    var card = Card("missing") with { UnsupportedReason = "missing-model", OriginalCardJson = "{\"props\":{\"custom\":\"preserved\"}}" };
    var plan = Plan(card);
    Check(plan.Cards[0].Diagnostic == "missing-model" && plan.NextState.Discard[0].OriginalCardJson == card.OriginalCardJson);
});
Test("no revisit after later energy gain", () =>
{
    var plan = Plan(Card("too-expensive", cost: 4),
        Card("energy", "CARD.ADRENALINE", 0, SimulationKeywords.Exhaust, false, ("Energy", 2), ("Cards", 0)),
        Card("later", cost: 4));
    Check(plan.Cards[0].Disposition == CardDisposition.Unplayed && plan.Cards[2].Disposition == CardDisposition.Played);
});
Test("energy resets each later turn with no carryover", () =>
{
    var first = Plan(Card("energy", "CARD.ADRENALINE", 0, SimulationKeywords.Exhaust, false, ("Energy", 2), ("Cards", 0)));
    var second = planner.BuildPlan(first.NextState with { Draw = [Card("four", cost: 4)] }, inputs);
    Check(second.Cards.Single().Disposition == CardDisposition.Unplayed);
});
Test("same-turn draws append right, and playing frees hand slot", () =>
{
    var draw = Card("draw", "CARD.FINESSE", 0, SimulationKeywords.None, false, ("Block", 4), ("Cards", 1));
    var hand = Enumerable.Range(0, 9).Select(i => Card($"retained{i}", cost: 99, keywords: SimulationKeywords.Retain)).ToImmutableArray().Insert(0, draw);
    var plan = planner.BuildPlan(State(Card("new", cost: 0)) with { Turn = 1, Hand = hand }, inputs);
    Check(plan.Cards.Length == 11 && plan.Cards[^1].Card.InstanceId == "new");
    Check(plan.Cards[^1].Disposition == CardDisposition.Played && plan.NextState.Hand.Length == 9);
});
Test("Blade Dance generates bounded same-turn Shivs and exhausts", () =>
{
    var plan = Plan(Card("dance", "CARD.BLADE_DANCE", 1, SimulationKeywords.Exhaust, false, ("Cards", 3)));
    Check(plan.Cards.Length == 4 && plan.Cards.Skip(1).All(c => c.Card.ModelId == "CARD.SHIV"));
    Check(plan.NextState.Exhaust.Length == 4 && plan.NextState.GeneratedCount == 3);
});
Test("generated hand cards that cannot fit are never created", () =>
{
    var dance = Card("dance", "CARD.BLADE_DANCE", 1, SimulationKeywords.Exhaust, false, ("Cards", 4));
    var hand = ImmutableArray.Create(dance).AddRange(Enumerable.Range(0, 9).Select(i => Card($"h{i}", cost: 99, keywords: SimulationKeywords.Retain)));
    var plan = planner.BuildPlan(State() with { Turn = 1, Hand = hand }, inputs);
    Check(plan.NextState.GeneratedCount == 1 && plan.Cards.Count(c => c.Card.ModelId == "CARD.SHIV") == 1);
});
Test("Anger copies upgraded descriptor to discard without touching snapshot", () =>
{
    var anger = Card("anger", "CARD.ANGER", 0, values: [("Damage", 8m)]) with { UpgradeLevel = 1 };
    var plan = Plan(anger);
    Check(plan.NextState.Discard.Length == 2);
    Check(plan.NextState.Discard.All(c => c.UpgradeLevel == 1 && c.OriginalCardJson == anger.OriginalCardJson));
    Check(plan.NextState.Discard.Select(c => c.InstanceId).Distinct().Count() == 2);
});
Test("X consumes remaining energy and is safe at zero", () =>
{
    var x = Card("x", "CARD.WHIRLWIND", 0, x: true, values: [("Damage", 5m)]);
    var plan = Plan(Card("spend", cost: 3), x);
    Check(plan.Cards[1].X == 0 && plan.Cards[1].Disposition == CardDisposition.Played);
    Check(plan.Cards[1].Actions.Single().Hits == 0);
    var full = Plan(x).Cards.Single();
    Check(full.X == 3 && full.EnergySpent == 3 && full.Actions.Single().Hits == 3);
});
Test("selection limit preserves all admitted effects and grays remainder", () =>
{
    Register("TEST.CYCLE", (_, _) => AdapterResult.Supported(new AdapterEffect(EffectKind.Draw, Count: 5)));
    var plan = Plan(Enumerable.Range(0, 6).Select(i => Card(i.ToString(), "TEST.CYCLE", 0)).ToArray());
    Check(plan.SelectedCards.Count() == 20 && plan.Diagnostics.Any(d => d.StartsWith("plan-limit")));
    Check(plan.Cards.Count(c => c.Disposition == CardDisposition.Unplayed) > 0);
    Check(plan.NextState.Draw.Length + plan.NextState.Discard.Length + plan.NextState.Hand.Length == 6);
});
Test("adapter-proven eligibility failure is not neutral fallback", () =>
{
    Register("TEST.CONDITION", (_, _) => new(CardEligibility.Ineligible, [], "condition-failed"));
    var plan = Plan(Card("condition", "TEST.CONDITION"));
    Check(plan.SelectedCards.Count() == 0 && plan.Cards.Single().Diagnostic == "condition-failed");
});
Test("adapter exception cannot partially damage or spend energy", () =>
{
    Register("TEST.THROW", (_, _) => throw new InvalidOperationException("broken adapter"));
    var plan = Plan(Card("exception", "TEST.THROW", 3));
    Check(plan.Cards.Single().EnergySpent == 0 && plan.Cards.Single().Actions.Single().Kind == ActionKind.Unknown);
    Check(plan.Cards.Single().Diagnostic == "adapter-error:InvalidOperationException");
});
Test("malformed whole-card recipe rejects even its safe first effect", () =>
{
    Register("TEST.BAD", (_, _) => AdapterResult.Supported(new(EffectKind.Damage, 99), new((EffectKind)999)));
    var plan = Plan(Card("bad", "TEST.BAD"));
    Check(plan.Cards.Single().Actions.Single().Kind == ActionKind.Unknown);
});
Test("version mismatch, missing variables, negative cost and bounds are neutral", () =>
{
    foreach (var card in new[] { Card("version") with { SourceVersion = "different" }, Card("schema") with { AdapterSchema = 2 },
        Card("missing") with { Values = ImmutableDictionary<string, decimal>.Empty },
        Card("negative", cost: -1), Card("bounds", values: [("Damage", 10001m)]) })
        Check(Plan(card).Cards.Single().Disposition == CardDisposition.Unsupported);
});
Test("exact IDs cannot match suffix or different case", () =>
{
    Check(Plan(Card("x", "MOD.STRIKE_IRONCLAD")).Cards.Single().Disposition == CardDisposition.Unsupported);
    Check(Plan(Card("x", "card.strike_ironclad")).Cards.Single().Disposition == CardDisposition.Unsupported);
});
Test("Bash projects Vulnerable before next attack and fans out in slot order", () =>
{
    var plan = planner.BuildPlan(State(Card("bash", "CARD.BASH", 2, values: [("Damage", 8m), ("VulnerablePower", 2m)]), Card("strike")),
        inputs with { Opponents = [new(5), new(2)] });
    var bash = plan.Cards[0].Actions;
    Check(bash.Select(a => a.TargetSlot).SequenceEqual(new int?[] { 2, 5, 2, 5 }));
    Check(bash.Take(2).All(a => a.Amount == 8) && bash.Skip(2).All(a => a.Kind == ActionKind.Vulnerable));
    Check(plan.Cards[1].Actions.All(a => a.Amount == 9));
});
Test("bounded Strength Dexterity Weak Frail projections freeze exact values", () =>
{
    var plan = planner.BuildPlan(State(
        Card("inflame", "CARD.INFLAME", 0, values: [("StrengthPower", 2m)]),
        Card("footwork", "CARD.FOOTWORK", 0, values: [("DexterityPower", 2m)]),
        Card("wave", "CARD.IRON_WAVE", 1, values: [("Block", 5m), ("Damage", 5m)])),
        inputs with { Weak = true, Frail = true });
    Check(plan.Cards[2].Actions[0] == new PlannedAction(ActionKind.Block, 5));
    Check(plan.Cards[2].Actions[1] == new PlannedAction(ActionKind.Damage, 5, 1, 0));
});
Test("Body Slam uses earlier projected block, not a live callback", () =>
{
    var plan = Plan(Card("defend", "CARD.DEFEND_IRONCLAD", values: [("Block", 5m)]),
        Card("slam", "CARD.BODY_SLAM", values: [("CalculationBase", 0m), ("ExtraDamage", 1m)]));
    Check(plan.Cards[1].Actions.Single().Amount == 5);
});
Test("Perfected Strike base and upgrade count themselves exactly once", () =>
{
    foreach (int level in new[] { 0, 1 })
    {
        var plan = Plan(Perfected(level));
        var card = plan.Cards.Single();
        Check(card.Disposition == CardDisposition.Played && card.EnergySpent == 2);
        Check(card.Actions.Single() == new PlannedAction(ActionKind.Damage, 8 + level, 1, 0));
        Check(plan.NextState.Discard.Single().InstanceId == "perfected" && plan.NextState.Exhaust.IsEmpty);
    }
});
Test("Perfected Strike counts native tags across all piles and distinct copies, including Exhaust", () =>
{
    foreach (int level in new[] { 0, 1 })
    {
        var tagged = Card("played", cost: 0, keywords: SimulationKeywords.Exhaust) with { HasStrikeTag = true };
        var hand = ImmutableArray.Create(tagged, Perfected(level)).AddRange(
            Enumerable.Range(0, 8).Select(i => Card($"h{i}", cost: 99)));
        var state = State() with
        {
            Turn = 1,
            Hand = hand,
            Draw = [tagged with { InstanceId = "draw" },
                Card("name-only", "CARD.FAKE_STRIKE", 99)],
            Discard = [tagged with { InstanceId = "copy1" }, tagged with
            {
                InstanceId = "copy2", ModelId = "TEST.TAG_WITHOUT_NAME",
                Eligibility = CardEligibility.Unknown, UnsupportedReason = "adapter-missing"
            }],
            Exhaust = [tagged with { InstanceId = "already-exhausted" }]
        };
        var plan = planner.BuildPlan(state, inputs);
        Check(plan.Cards[1].Actions.Single().Amount == 6 + (2 + level) * 6);
        Check(plan.NextState.Exhaust.Count(c => c.InstanceId is "played" or "already-exhausted") == 2);
        Check(plan.NextState.Draw.Length == 2 && plan.NextState.GeneratedCount == 0);
        Check(Json(plan) == Json(planner.BuildPlan(state, inputs)));
    }
});
Test("Perfected Strike sees earlier same-turn copied and generated Strike tags", () =>
{
    Register("TEST.COPY_TAGGED", (_, _) => AdapterResult.Supported(new AdapterEffect(EffectKind.CopyToDiscard)));
    Register("TEST.GENERATE_TAGGED", (_, _) => AdapterResult.Supported(new AdapterEffect(
        EffectKind.GenerateHand, GeneratedCard: Card("template", cost: 99) with { HasStrikeTag = true })));
    var plan = Plan(Card("copy", "TEST.COPY_TAGGED", 0) with { HasStrikeTag = true },
        Card("generate", "TEST.GENERATE_TAGGED", 0), Perfected());
    Check(plan.Cards[2].Actions.Single().Amount == 14);
    Check(plan.NextState.GeneratedCount == 2);
    Check(plan.NextState.Discard.Count(c => c.HasStrikeTag == true) == 4);
    var shivs = Plan(Card("dance", "CARD.BLADE_DANCE", 1, SimulationKeywords.Exhaust, values: [("Cards", 3m)]), Perfected());
    Check(shivs.Cards[1].Actions.Single().Amount == 8);
    Check(shivs.Cards.Where(c => c.Card.ModelId == "CARD.SHIV").All(c => c.Card.HasStrikeTag == false));
    var anger = Plan(Card("anger", "CARD.ANGER", 0), Perfected());
    Check(anger.Cards[1].Actions.Single().Amount == 8);
});
Test("Perfected Strike base and upgrade use existing Strength Weak Vulnerable scaling once", () =>
{
    foreach (int level in new[] { 0, 1 })
    {
        var plan = planner.BuildPlan(State(Perfected(level)),
            inputs with { Strength = 3, Weak = true, Opponents = [new(2, true), new(0)] });
        Check(plan.Cards.Single().Actions.SequenceEqual(new[]
        {
            new PlannedAction(ActionKind.Damage, decimal.Floor((11m + level) * .75m), 1, 0),
            new PlannedAction(ActionKind.Damage, decimal.Floor((11m + level) * .75m * 1.5m), 1, 2)
        }));
    }
});
Test("Impervious base and upgrade grant exact block, spend two, and exhaust normally", () =>
{
    foreach (int level in new[] { 0, 1 })
    {
        var plan = Plan(Impervious(level), Card("slam", "CARD.BODY_SLAM",
            values: [("CalculationBase", 0m), ("ExtraDamage", 1m)]));
        Check(plan.Cards[0].Actions.Single() == new PlannedAction(ActionKind.Block, 30 + 10 * level));
        Check(plan.Cards[0].EnergySpent == 2 && plan.Cards[1].Actions.Single().Amount == 30 + 10 * level);
        Check(plan.NextState.Exhaust.Single().InstanceId == "impervious");
        var frail = planner.BuildPlan(State(Impervious(level)), inputs with { Dexterity = 3, Frail = true });
        Check(frail.Cards.Single().Actions.Single() == new PlannedAction(ActionKind.Block,
            decimal.Floor((33m + 10m * level) * .75m)));
        var unaffordable = Plan(Card("spend", cost: 2), Impervious(level));
        Check(unaffordable.Cards[1].Disposition == CardDisposition.Unplayed);
        Check(unaffordable.NextState.Exhaust.IsEmpty && unaffordable.NextState.Discard.Length == 2);
    }
});
Test("new adapters reject unsafe or missing metadata as whole-card no-ops", () =>
{
    foreach (var card in new[]
    {
        Perfected() with { HasStrikeTag = null }, Perfected() with { HasStrikeTag = false },
        Perfected() with { Values = ImmutableDictionary<string, decimal>.Empty },
        Perfected() with { Values = Perfected().Values.SetItem("ExtraDamage", 3) },
        Impervious() with { Keywords = SimulationKeywords.None },
        Impervious() with { Values = ImmutableDictionary<string, decimal>.Empty },
        Perfected() with { SourceVersion = "future" }, Impervious() with { AdapterSchema = 2 },
        Perfected() with { UnsupportedReason = "unsupported-saved-modifier" },
        Impervious() with { UnsupportedReason = "unsupported-saved-modifier" }
    })
    {
        var planned = Plan(card).Cards.Single();
        Check(planned.Disposition == CardDisposition.Unsupported && planned.EnergySpent == 0);
        Check(planned.Actions.Single() == new PlannedAction(ActionKind.Unknown));
        Check(planned.Card.OriginalCardJson == card.OriginalCardJson && planned.Diagnostic != null);
    }
    var unknownTag = Card("unknown", "MOD.UNKNOWN") with { HasStrikeTag = null };
    foreach (var state in new[]
    {
        State(Perfected()) with { Hand = [unknownTag] },
        State(Perfected()) with { Draw = [Perfected(), unknownTag] },
        State(Perfected()) with { Discard = [unknownTag] },
        State(Perfected()) with { Exhaust = [unknownTag] }
    })
    {
        var planned = planner.BuildPlan(state, inputs).Cards.Single(c => c.Card.InstanceId == "perfected");
        Check(planned.Diagnostic == "strike-tag-metadata-unknown" && planned.EnergySpent == 0);
        Check(planned.Actions.Single().Kind == ActionKind.Unknown);
    }
    foreach (int? count in new int?[] { null, -1, 0, 10001, 10000 })
        Check(registry.Translate(Perfected(), new(3, 0, 1, count)).Diagnostic != null);
    Check(Plan(unknownTag, Impervious()).Cards[1].Disposition == CardDisposition.Played);
});
Test("Strike metadata and installed plans roundtrip, and legacy missing metadata fails closed", () =>
{
    var state = State(Perfected(), Impervious()) with { Exhaust = [Card("old-strike") with { HasStrikeTag = true }] };
    var plan = planner.BuildPlan(state, inputs);
    var restored = CorruptedPlayerSave.Deserialize(CorruptedPlayerSave.Serialize(state, plan));
    Check(Json(restored.Plan) == Json(plan) && Json(restored.State) == Json(state));
    Check(Json(planner.BuildPlan(restored.State, inputs)) == Json(plan));
    var legacyCard = JsonSerializer.Deserialize<CardDescriptor>(Json(Card("legacy")).Replace(",\"HasStrikeTag\":false", ""))!;
    Check(legacyCard.HasStrikeTag == null);
    Check(Plan(legacyCard).Cards.Single().Disposition == CardDisposition.Played);
    Check(Plan(Perfected(), legacyCard).Cards[0].Diagnostic == "strike-tag-metadata-unknown");
});
Test("multi-hit amounts stay per hit and upgraded values stay exact", () =>
{
    var twin = Card("twin", "CARD.TWIN_STRIKE", values: [("Damage", 7m)]) with { UpgradeLevel = 1 };
    Check(Plan(twin).Cards.Single().Actions.Single() == new PlannedAction(ActionKind.Damage, 7, 2, 0));
});
Test("heal and HP loss preserve complete effect order", () =>
{
    var plan = Plan(Card("heal", "CARD.NOT_YET", 2, SimulationKeywords.Exhaust, false, ("Heal", 13)),
        Card("blood", "CARD.BLOODLETTING", 0, values: [("HpLoss", 3m), ("Energy", 2m)]), Card("later", cost: 3));
    Check(plan.Cards[0].Actions.Single().Kind == ActionKind.Heal);
    Check(plan.Cards[1].Actions[0] == new PlannedAction(ActionKind.HpLoss, 3));
    Check(plan.Cards[1].Actions[1] == new PlannedAction(ActionKind.Energy, 2));
    Check(plan.Cards[2].Disposition == CardDisposition.Played);
});
Test("empty and exhausted decks produce explicit stable no-op turns", () =>
{
    var plan = Plan();
    Check(plan.Cards.IsEmpty && plan.NextState.Turn == 1);
    var exhausted = planner.BuildPlan(plan.NextState with { Exhaust = [Card("gone")] }, inputs);
    Check(exhausted.Cards.IsEmpty && exhausted.NextState.Exhaust.Length == 1 && exhausted.NextState.Turn == 2);
});
Test("save roundtrip retains installed immutable plan, RNG, piles and original JSON", () =>
{
    var state = CorruptedPlayerState.Create(Enumerable.Range(0, 8).Select(i => Card(i.ToString())), "run", "snapshot", "encounter");
    var plan = planner.BuildPlan(state, inputs);
    var save = CorruptedPlayerSave.Deserialize(CorruptedPlayerSave.Serialize(state, plan));
    Check(Json(save.Plan) == Json(plan) && Json(save.State) == Json(state));
    Check(Json(planner.BuildPlan(save.Plan!.NextState, inputs)) == Json(planner.BuildPlan(plan.NextState, inputs)));
});
Test("installed plan cannot be rerolled through changed inputs", () =>
{
    var state = State(Card("attack"));
    var plan = planner.BuildPlan(state, inputs);
    var persisted = CorruptedPlayerSave.Deserialize(CorruptedPlayerSave.Serialize(state, plan));
    var changed = inputs with { Strength = 99 };
    Check(planner.BuildPlan(state, changed).Cards[0].Actions[0].Amount == 105);
    Check(persisted.Plan!.Cards[0].Actions[0].Amount == 6);
});
Test("future RNG or save schema fails rather than silently rerolling", () =>
{
    bool failed = false;
    try { CorruptedPlayerSave.Deserialize(CorruptedPlayerSave.Serialize(State() with { Version = 999 }, null)); }
    catch (JsonException) { failed = true; }
    Check(failed);
});
Test("registry covers exactly the 42 audited native card IDs, not every game card", () =>
{
    Check(new ExactCardAdapterRegistry().Adapters.Count() == 42);
    Check(registry.Adapters.Count(a => a.Tier == 1) >= 20);
    Check(registry.Adapters.Count(a => a.Tier == 2) >= 10);
    Check(registry.Adapters.Count(a => a.Tier == 3) >= 2);
});
Test("every audited adapter accepts complete base and upgraded source fixtures", () =>
{
    var fixtures = new List<(string Id, int Cost, bool X, (string Key, decimal Base, decimal Up)[] Values)>();
    void Add(string ids, int cost, params (string, decimal, decimal)[] values)
    {
        foreach (string id in ids.Split(' ')) fixtures.Add(("CARD." + id, cost, false, values));
    }
    Add("STRIKE_IRONCLAD STRIKE_SILENT STRIKE_DEFECT STRIKE_REGENT STRIKE_NECROBINDER", 1, ("Damage", 6, 9));
    Add("DEFEND_IRONCLAD DEFEND_SILENT DEFEND_DEFECT DEFEND_REGENT DEFEND_NECROBINDER", 1, ("Block", 5, 8));
    Add("LEAP", 1, ("Block", 9, 12));
    Add("IMPERVIOUS", 2, ("Block", 30, 40));
    Add("SLICE", 0, ("Damage", 6, 9));
    Add("DRAMATIC_ENTRANCE", 0, ("Damage", 11, 15));
    Add("SHIV", 0, ("Damage", 4, 6));
    Add("TWIN_STRIKE", 1, ("Damage", 5, 7));
    Add("DAGGER_SPRAY", 1, ("Damage", 4, 6));
    Add("POMMEL_STRIKE", 1, ("Damage", 9, 10), ("Cards", 1, 2));
    Add("FLASH_OF_STEEL", 0, ("Damage", 5, 8), ("Cards", 1, 1));
    Add("SWEEPING_BEAM", 1, ("Damage", 6, 9), ("Cards", 1, 1));
    Add("SHRUG_IT_OFF", 1, ("Block", 8, 11), ("Cards", 1, 1));
    Add("BACKFLIP", 1, ("Block", 5, 8), ("Cards", 2, 2));
    Add("FINESSE", 0, ("Block", 4, 7), ("Cards", 1, 1));
    Add("IRON_WAVE", 1, ("Block", 5, 7), ("Damage", 5, 7));
    Add("DASH", 2, ("Block", 10, 13), ("Damage", 10, 13));
    Add("BASH", 2, ("Damage", 8, 10), ("VulnerablePower", 2, 3));
    Add("BEAM_CELL", 0, ("Damage", 3, 4), ("VulnerablePower", 1, 2));
    Add("THUNDERCLAP", 1, ("Damage", 4, 7), ("VulnerablePower", 1, 1));
    Add("NEUTRALIZE", 0, ("Damage", 3, 4), ("WeakPower", 1, 2));
    Add("SUCKER_PUNCH", 1, ("Damage", 8, 10), ("WeakPower", 1, 2));
    Add("INFLAME", 1, ("StrengthPower", 2, 3));
    Add("FOOTWORK", 1, ("DexterityPower", 2, 3));
    Add("NOT_YET", 2, ("Heal", 10, 13));
    Add("ADRENALINE", 0, ("Energy", 1, 2), ("Cards", 2, 2));
    Add("BLOODLETTING", 0, ("HpLoss", 3, 3), ("Energy", 2, 3));
    Add("OFFERING", 0, ("HpLoss", 6, 6), ("Energy", 2, 2), ("Cards", 3, 5));
    Add("BODY_SLAM", 1, ("CalculationBase", 0, 0), ("ExtraDamage", 1, 1));
    Add("PERFECTED_STRIKE", 2, ("CalculationBase", 6, 6), ("ExtraDamage", 2, 3));
    Add("ANGER", 0, ("Damage", 6, 8));
    Add("BLADE_DANCE", 1, ("Cards", 3, 4));
    fixtures.Add(("CARD.WHIRLWIND", 0, true, [("Damage", 5, 8)]));
    fixtures.Add(("CARD.SKEWER", 0, true, [("Damage", 8, 11)]));
    Check(fixtures.Count == new ExactCardAdapterRegistry().Adapters.Count());
    foreach (var fixture in fixtures)
    {
        foreach (int level in new[] { 0, 1 })
        {
            var card = Card("fixture", fixture.Id, fixture.Id == "CARD.BODY_SLAM" && level == 1 ? 0 : fixture.Cost,
                keywords: fixture.Id == "CARD.IMPERVIOUS" ? SimulationKeywords.Exhaust : SimulationKeywords.None,
                x: fixture.X, values: fixture.Values.Select(v => (v.Key, level == 0 ? v.Base : v.Up)).ToArray())
                with { UpgradeLevel = level, HasStrikeTag = fixture.Id == "CARD.PERFECTED_STRIKE" };
            var result = registry.Translate(card, new(3, 12, 5, 4));
            Check(result.Diagnostic == null && result.Eligibility == CardEligibility.Eligible, $"{fixture.Id}+{level}: {result.Diagnostic}");
            Check(!result.Effects.IsEmpty, fixture.Id);
            if (fixture.Values.Any(v => v.Key == "Damage"))
                Check(result.Effects.Single(e => e.Kind == EffectKind.Damage).Amount == card.Values["Damage"], fixture.Id);
            if (fixture.Values.Any(v => v.Key == "Block"))
                Check(result.Effects.Single(e => e.Kind == EffectKind.Block).Amount == card.Values["Block"], fixture.Id);
            if (fixture.Id == "CARD.PERFECTED_STRIKE")
                Check(result.Effects.Single().Amount == 6 + (2 + level) * 4);
        }
    }
});
Test("fractional draw or energy rejects entire compound card", () =>
{
    Check(Plan(Card("partial", "CARD.POMMEL_STRIKE", values: [("Damage", 9m), ("Cards", 1.5m)]))
        .Cards.Single().Actions.Single().Kind == ActionKind.Unknown);
    Check(Plan(Card("partial", "CARD.ADRENALINE", values: [("Energy", 1.5m), ("Cards", 2m)]))
        .Cards.Single().Actions.Single().Kind == ActionKind.Unknown);
});
Test("malformed eligibility and identity are classified without exceptions", () =>
{
    Check(Plan(Card("invalid") with { Eligibility = (CardEligibility)999 }).Cards.Single().Disposition == CardDisposition.Unsupported);
    Check(Plan(Card("invalid") with { ModelId = null! }).Cards.Single().Disposition == CardDisposition.Unsupported);
});
Test("corrupt installed action enum is rejected on load", () =>
{
    var state = State(Card("strike"));
    var plan = planner.BuildPlan(state, inputs);
    plan = plan with { Cards = [plan.Cards[0] with { Actions = [new((ActionKind)999)] }] };
    bool rejected = false;
    try { CorruptedPlayerSave.Deserialize(CorruptedPlayerSave.Serialize(state, plan)); }
    catch (JsonException) { rejected = true; }
    Check(rejected);
});
Test("private energy draw and generation changes have frozen informational intents", () =>
{
    var draw = Card("draw", "CARD.ADRENALINE", 0, SimulationKeywords.Exhaust, false, ("Energy", 2), ("Cards", 2));
    var blockers = Enumerable.Range(0, 4).Select(i => Card($"blocked{i}", cost: 99));
    var plan = Plan(new[] { draw }.Concat(blockers).Append(Card("new", cost: 0)).ToArray());
    Check(plan.Cards[0].Actions.SequenceEqual(new[] { new PlannedAction(ActionKind.Energy, 2), new PlannedAction(ActionKind.Draw, 1) }));
    var dance = Plan(Card("dance", "CARD.BLADE_DANCE", 1, SimulationKeywords.Exhaust, false, ("Cards", 3)));
    Check(dance.Cards[0].Actions.Single() == new PlannedAction(ActionKind.GenerateHand, 3));
    var anger = Plan(Card("anger", "CARD.ANGER", 0));
    Check(anger.Cards[0].Actions[^1] == new PlannedAction(ActionKind.CopyToDiscard, 1));
});
Test("native empty SavedProperties and BaseLib dictionaries are not modifiers", () =>
{
    foreach (string json in new[]
    {
        """{"id":"CARD.STRIKE_IRONCLAD"}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":null}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":{}}""",
        """{"floor_added_to_deck":1,"id":"CARD.STRIKE_IRONCLAD","save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]":{"BaseLibCardModifiers":[]}}""",
        """{"id":"CARD.DEFEND_IRONCLAD","props":{"ints":[],"bools":null,"strings":[],"int_arrays":[],"model_ids":[],"cards":[],"card_arrays":[]}}""",
        """{"id":"CARD.BASH","current_upgrade_level":1,"props":{},"enchantment":null,"floor_added_to_deck":1,"save_dict_Int32":{},"save_dict_String":null}"""
    })
    {
        using var document = JsonDocument.Parse(json);
        Check(!SavedCardModifierPolicy.HasUnsupportedModifiers(document.RootElement), json);
    }
});
Test("real native saved values and unknown extensions remain unsupported", () =>
{
    foreach (string json in new[]
    {
        """{"id":"CARD.STRIKE_IRONCLAD","props":{"ints":[{"name":"Damage","value":0}]}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":{"strings":[{"name":"Custom","value":""}]}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":{"cards":[{}]}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":{"unknown":[]}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":{"ints":{}}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","props":[]}""",
        """{"id":"CARD.STRIKE_IRONCLAD","enchantment":{}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","save_dict_Int32":{"mod.cost":0}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","save_dict_Int32":[]}""",
        """{"id":"CARD.STRIKE_IRONCLAD","save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]":{"BaseLibCardModifiers":[{}]}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]":{"BaseLibCardModifiers":[],"unknown":[]}}""",
        """{"id":"CARD.STRIKE_IRONCLAD","unrecognized_extension":{}}"""
    })
    {
        using var document = JsonDocument.Parse(json);
        Check(SavedCardModifierPolicy.HasUnsupportedModifiers(document.RootElement), json);
    }
});
Console.WriteLine($"{passed} tests passed.");

sealed record TestAdapter(string ModelId, Func<CardDescriptor, AdapterContext, AdapterResult> Recipe) : IExactCardAdapter
{
    public string SourceVersion => ExactCardAdapterRegistry.SourceVersion;
    public int SchemaVersion => ExactCardAdapterRegistry.SchemaVersion;
    public int Tier => 1;
    public AdapterResult Translate(CardDescriptor card, AdapterContext context) => Recipe(card, context);
}
