using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheArchitect.TheArchitectCode.Challenger;

[Flags]
public enum SimulationKeywords { None = 0, Innate = 1, Retain = 2, Ethereal = 4, Exhaust = 8, Unplayable = 16 }
public enum CardEligibility { Eligible, Ineligible, Unknown }
public enum CardDisposition { Played, Unplayed, Unsupported }
public enum ActionKind { Damage, Block, Heal, Strength, Dexterity, Vulnerable, Weak, HpLoss, Unknown, Energy, Draw, GenerateHand, CopyToDiscard }
public enum EffectKind { Damage, Block, Heal, Strength, Dexterity, Vulnerable, Weak, HpLoss, Energy, Draw, GenerateHand, CopyToDiscard, BlockDamage }

/// <summary>OriginalCardJson is the complete original SerializableCard, never a reserialized preview.</summary>
public sealed record CardDescriptor(
    string InstanceId,
    string ModelId,
    string OriginalCardJson,
    int UpgradeLevel,
    int EnergyCost,
    bool CostsX,
    SimulationKeywords Keywords,
    ImmutableDictionary<string, decimal> Values,
    string SourceVersion = ExactCardAdapterRegistry.SourceVersion,
    int AdapterSchema = ExactCardAdapterRegistry.SchemaVersion,
    CardEligibility Eligibility = CardEligibility.Eligible,
    string? UnsupportedReason = null,
    bool? HasStrikeTag = null)
{
    public bool Has(SimulationKeywords keyword) => (Keywords & keyword) != 0;
}

/// <summary>The only boundary a game-aware reconstruction layer needs to implement.</summary>
public interface ICardDescriptorFactory<in T>
{
    CardDescriptor Describe(T saved, string instanceId);
}

public sealed record OpponentInput(int Slot, bool Vulnerable = false);

/// <summary>Capture live powers once at the intent boundary; do not persist a parallel power timeline.</summary>
public sealed record PlannerInputs
{
    public decimal SelfBlock { get; init; }
    public decimal Strength { get; init; }
    public decimal Dexterity { get; init; }
    public bool Weak { get; init; }
    public bool Frail { get; init; }
    public ImmutableArray<OpponentInput> Opponents { get; init; } = ImmutableArray<OpponentInput>.Empty;
}

/// <summary>
/// Amounts include bounded power projections; execute damage/block unpowered.
/// Energy/Draw/GenerateHand/CopyToDiscard are informational: their changes already exist in NextState.
/// </summary>
public sealed record PlannedAction(ActionKind Kind, decimal Amount = 0, int Hits = 1, int? TargetSlot = null);

public sealed record PlannedCard(
    CardDescriptor Card,
    int Position,
    int? SelectionIndex,
    CardDisposition Disposition,
    int EnergySpent,
    int X,
    ImmutableArray<PlannedAction> Actions,
    string? Diagnostic = null);

public sealed record ChallengerPlan(
    int Turn,
    ImmutableArray<PlannedCard> Cards,
    ChallengerState NextState,
    ImmutableArray<string> Diagnostics)
{
    [JsonIgnore]
    public IEnumerable<PlannedCard> SelectedCards => Cards.Where(c => c.SelectionIndex.HasValue);
}

public sealed record ChallengerState(
    int Version,
    int Turn,
    ulong RngState,
    long GeneratedCount,
    ImmutableArray<CardDescriptor> Draw,
    ImmutableArray<CardDescriptor> Hand,
    ImmutableArray<CardDescriptor> Discard,
    ImmutableArray<CardDescriptor> Exhaust)
{
    public const int CurrentVersion = 1;

    public static ChallengerState Create(IEnumerable<CardDescriptor> cards, string runSeed, string snapshotIdentity, string encounterIdentity)
    {
        var deck = cards.ToList();
        if (deck.Select(c => c.InstanceId).Distinct(StringComparer.Ordinal).Count() != deck.Count)
            throw new ArgumentException("Snapshot copies require distinct instance IDs.", nameof(cards));
        var rng = PrivateRng.Create(runSeed, snapshotIdentity, encounterIdentity);
        rng.Shuffle(deck);
        return new(CurrentVersion, 0, rng.State, 0, deck.ToImmutableArray(), [], [], []);
    }
}

public sealed record ChallengerSave(int Version, ChallengerState State, ChallengerPlan? Plan)
{
    public const int CurrentVersion = 1;

    public static string Serialize(ChallengerState state, ChallengerPlan? plan) =>
        JsonSerializer.Serialize(new ChallengerSave(CurrentVersion, state, plan));

    public static ChallengerSave Deserialize(string json)
    {
        var save = JsonSerializer.Deserialize<ChallengerSave>(json) ?? throw new JsonException("Empty Challenger save.");
        if (save.Version != CurrentVersion || save.State.Version != ChallengerState.CurrentVersion ||
            (save.Plan != null && save.Plan.NextState.Version != ChallengerState.CurrentVersion))
            throw new JsonException("Unsupported Challenger save or RNG schema; refusing to reroll.");
        ChallengerPlanner.ValidateState(save.State);
        if (save.Plan != null)
        {
            ChallengerPlanner.ValidateState(save.Plan.NextState);
            if (save.Plan.Turn != save.State.Turn + 1 || save.Plan.NextState.Turn != save.Plan.Turn)
                throw new JsonException("Installed plan does not belong to saved state.");
            if (save.Plan.Cards.IsDefault || save.Plan.Diagnostics.IsDefault ||
                save.Plan.Cards.Any(c => c == null || c.Card == null || !Enum.IsDefined(c.Disposition) ||
                    c.Actions.IsDefault || c.EnergySpent < 0 || c.X < 0 ||
                    c.Actions.Any(a => a == null || !Enum.IsDefined(a.Kind) || a.Amount < 0 || a.Hits < 0 ||
                        (a.TargetSlot.HasValue && a.TargetSlot.Value < 0))))
                throw new JsonException("Installed Challenger plan is malformed.");
            int expectedSelection = 0;
            for (int position = 0; position < save.Plan.Cards.Length; position++)
            {
                var card = save.Plan.Cards[position];
                if (card.Position != position || (card.SelectionIndex.HasValue &&
                    (card.SelectionIndex.Value != ++expectedSelection || expectedSelection > ChallengerPlanner.SelectionLimit)) ||
                    (card.Disposition == CardDisposition.Unplayed) == card.SelectionIndex.HasValue)
                    throw new JsonException("Installed Challenger plan has an invalid card order.");
            }
        }
        return save;
    }
}

public sealed record AdapterEffect(EffectKind Kind, decimal Amount = 0, int Count = 1, CardDescriptor? GeneratedCard = null);
public sealed record AdapterContext(int Energy, decimal Block, int HandCount, int? StrikeCount = null);
public sealed record AdapterResult(CardEligibility Eligibility, ImmutableArray<AdapterEffect> Effects, string? Diagnostic = null)
{
    public static AdapterResult Unsupported(string reason) => new(CardEligibility.Unknown, [], reason);
    public static AdapterResult Supported(params AdapterEffect[] effects) => new(CardEligibility.Eligible, effects.ToImmutableArray());
}

public interface IExactCardAdapter
{
    string ModelId { get; }
    string SourceVersion { get; }
    int SchemaVersion { get; }
    int Tier { get; }
    AdapterResult Translate(CardDescriptor card, AdapterContext context);
}
