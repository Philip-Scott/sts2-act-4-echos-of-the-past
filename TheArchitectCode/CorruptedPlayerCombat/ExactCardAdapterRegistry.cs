using System.Collections.Immutable;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

/// <summary>Whole-card allowlist audited against installed beta 0.111.0, not card metadata heuristics.</summary>
public sealed class ExactCardAdapterRegistry
{
    public const string SourceVersion = "beta-0.111.0";
    public const int SchemaVersion = 1;
    public const string AssemblySourceRevision = "0.1.0+41cef1ea4657c524aa50e870df009e56337e8c32";
    private readonly Dictionary<string, IExactCardAdapter> adapters = new(StringComparer.Ordinal);

    public ExactCardAdapterRegistry(bool includeBuiltIns = true)
    {
        if (includeBuiltIns) AddBuiltIns();
    }

    public IEnumerable<IExactCardAdapter> Adapters => adapters.Values;

    public void Register(IExactCardAdapter adapter)
    {
        if (!adapters.TryAdd(adapter.ModelId, adapter))
            throw new ArgumentException($"Adapter already registered for exact ID {adapter.ModelId}.");
    }

    public ImmutableArray<string> RequiredVariables(string modelId) =>
        adapters.TryGetValue(modelId, out var adapter) && adapter is AuditedAdapter audited ? audited.Variables : [];

    public bool HasAdapter(string modelId) => adapters.ContainsKey(modelId);

    public AdapterResult Translate(CardDescriptor card, AdapterContext context)
    {
        if (string.IsNullOrWhiteSpace(card.ModelId) || !Enum.IsDefined(card.Eligibility))
            return AdapterResult.Unsupported("malformed-card: invalid identity or eligibility");
        if (card.UnsupportedReason != null) return AdapterResult.Unsupported(card.UnsupportedReason);
        if (card.Eligibility == CardEligibility.Unknown) return AdapterResult.Unsupported("eligibility-unknown");
        if (!adapters.TryGetValue(card.ModelId, out var adapter)) return AdapterResult.Unsupported("adapter-missing");
        if (card.SourceVersion != adapter.SourceVersion || card.AdapterSchema != adapter.SchemaVersion)
            return AdapterResult.Unsupported("adapter-version-mismatch");
        if (card.EnergyCost < 0 || card.EnergyCost > 100 || card.UpgradeLevel < 0 || card.UpgradeLevel > 1 ||
            card.Values == null || card.Values.Any(v => v.Value < 0 || v.Value > 10000) ||
            context.Energy < 0 || context.Energy > 10000 || context.Block < 0 || context.Block > 1000000)
            return AdapterResult.Unsupported("malformed-card: value outside audited bounds");
        try
        {
            var result = adapter.Translate(card, context);
            if (result == null || !Enum.IsDefined(result.Eligibility) || result.Effects.IsDefault || result.Effects.Length > 32 ||
                result.Effects.Any(e => !IsSafe(e)))
                return AdapterResult.Unsupported("adapter-malformed: invalid or unbounded effect");
            return result;
        }
        catch (Exception error)
        {
            return AdapterResult.Unsupported($"adapter-error:{error.GetType().Name}");
        }
    }

    private bool IsSafe(AdapterEffect effect)
    {
        if (effect == null || !Enum.IsDefined(effect.Kind) || effect.Amount < 0 || effect.Amount > 10000 ||
            effect.Count < 0 || effect.Count > 1000) return false;
        if (effect.Kind == EffectKind.Energy && (effect.Amount != decimal.Truncate(effect.Amount) || effect.Amount > 100)) return false;
        if ((effect.Kind is EffectKind.Draw or EffectKind.GenerateHand or EffectKind.CopyToDiscard) && effect.Count > 100) return false;
        if (effect.Kind == EffectKind.GenerateHand)
        {
            var generated = effect.GeneratedCard;
            return generated != null && generated.UnsupportedReason == null && generated.SourceVersion == SourceVersion &&
                generated.AdapterSchema == SchemaVersion && generated.EnergyCost is >= 0 and <= 100 &&
                generated.Values != null && !string.IsNullOrWhiteSpace(generated.ModelId) && HasAdapter(generated.ModelId);
        }
        return true;
    }

    private sealed record AuditedAdapter(
        string ModelId,
        int Tier,
        ImmutableArray<string> Variables,
        bool CostsX,
        Func<CardDescriptor, AdapterContext, AdapterResult> Recipe) : IExactCardAdapter
    {
        public string SourceVersion => ExactCardAdapterRegistry.SourceVersion;
        public int SchemaVersion => ExactCardAdapterRegistry.SchemaVersion;
        public AdapterResult Translate(CardDescriptor card, AdapterContext context)
        {
            if (card.CostsX != CostsX || Variables.Any(v => !card.Values.ContainsKey(v)))
                return AdapterResult.Unsupported("malformed-card: required variable or cost shape mismatch");
            return Recipe(card, context);
        }
    }

    private static decimal Value(CardDescriptor card, string name) => card.Values[name];
    private static int Count(CardDescriptor card, string name)
    {
        decimal value = Value(card, name);
        if (value != decimal.Truncate(value) || value > 100) throw new ArgumentOutOfRangeException(name);
        return (int)value;
    }
    private static AdapterEffect Damage(CardDescriptor c, int hits = 1) => new(EffectKind.Damage, Value(c, "Damage"), hits);
    private static AdapterEffect Block(CardDescriptor c) => new(EffectKind.Block, Value(c, "Block"));
    private static AdapterEffect Draw(CardDescriptor c) => new(EffectKind.Draw, Count: Count(c, "Cards"));

    private void Add(string id, int tier, string[] variables, Func<CardDescriptor, AdapterContext, AdapterResult> recipe, bool x = false) =>
        Register(new AuditedAdapter(id, tier, variables.ToImmutableArray(), x, recipe));

    private void AddBuiltIns()
    {
        foreach (string id in new[] { "CARD.STRIKE_IRONCLAD", "CARD.STRIKE_SILENT", "CARD.STRIKE_DEFECT", "CARD.STRIKE_REGENT", "CARD.STRIKE_NECROBINDER" })
            Add(id, 1, ["Damage"], (c, _) => AdapterResult.Supported(Damage(c)));
        foreach (string id in new[] { "CARD.DEFEND_IRONCLAD", "CARD.DEFEND_SILENT", "CARD.DEFEND_DEFECT", "CARD.DEFEND_REGENT", "CARD.DEFEND_NECROBINDER", "CARD.LEAP" })
            Add(id, 1, ["Block"], (c, _) => AdapterResult.Supported(Block(c)));
        Add("CARD.IMPERVIOUS", 1, ["Block"], (c, _) =>
            c.Has(SimulationKeywords.Exhaust)
                ? AdapterResult.Supported(Block(c))
                : AdapterResult.Unsupported("malformed-card: missing Impervious Exhaust"));
        foreach (string id in new[] { "CARD.SLICE", "CARD.DRAMATIC_ENTRANCE", "CARD.SHIV" })
            Add(id, 1, ["Damage"], (c, _) => AdapterResult.Supported(Damage(c)));
        foreach (string id in new[] { "CARD.TWIN_STRIKE", "CARD.DAGGER_SPRAY" })
            Add(id, 1, ["Damage"], (c, _) => AdapterResult.Supported(Damage(c, 2)));
        foreach (string id in new[] { "CARD.POMMEL_STRIKE", "CARD.FLASH_OF_STEEL", "CARD.SWEEPING_BEAM" })
            Add(id, 1, ["Damage", "Cards"], (c, _) => AdapterResult.Supported(Damage(c), Draw(c)));
        foreach (string id in new[] { "CARD.SHRUG_IT_OFF", "CARD.BACKFLIP", "CARD.FINESSE" })
            Add(id, 1, ["Block", "Cards"], (c, _) => AdapterResult.Supported(Block(c), Draw(c)));
        foreach (string id in new[] { "CARD.IRON_WAVE", "CARD.DASH" })
            Add(id, 1, ["Block", "Damage"], (c, _) => AdapterResult.Supported(Block(c), Damage(c)));
        foreach (string id in new[] { "CARD.BASH", "CARD.BEAM_CELL", "CARD.THUNDERCLAP" })
            Add(id, 2, ["Damage", "VulnerablePower"], (c, _) => AdapterResult.Supported(
                Damage(c), new(EffectKind.Vulnerable, Value(c, "VulnerablePower"))));
        foreach (string id in new[] { "CARD.NEUTRALIZE", "CARD.SUCKER_PUNCH" })
            Add(id, 2, ["Damage", "WeakPower"], (c, _) => AdapterResult.Supported(
                Damage(c), new(EffectKind.Weak, Value(c, "WeakPower"))));
        Add("CARD.INFLAME", 2, ["StrengthPower"], (c, _) => AdapterResult.Supported(new AdapterEffect(EffectKind.Strength, Value(c, "StrengthPower"))));
        Add("CARD.FOOTWORK", 2, ["DexterityPower"], (c, _) => AdapterResult.Supported(new AdapterEffect(EffectKind.Dexterity, Value(c, "DexterityPower"))));
        Add("CARD.NOT_YET", 1, ["Heal"], (c, _) => AdapterResult.Supported(new AdapterEffect(EffectKind.Heal, Value(c, "Heal"))));
        Add("CARD.ADRENALINE", 1, ["Energy", "Cards"], (c, _) => AdapterResult.Supported(new(EffectKind.Energy, Value(c, "Energy")), Draw(c)));
        Add("CARD.BLOODLETTING", 2, ["HpLoss", "Energy"], (c, _) => AdapterResult.Supported(
            new(EffectKind.HpLoss, Value(c, "HpLoss")), new(EffectKind.Energy, Value(c, "Energy"))));
        Add("CARD.OFFERING", 2, ["HpLoss", "Energy", "Cards"], (c, _) => AdapterResult.Supported(
            new(EffectKind.HpLoss, Value(c, "HpLoss")), new(EffectKind.Energy, Value(c, "Energy")), Draw(c)));
        foreach (string id in new[] { "CARD.WHIRLWIND", "CARD.SKEWER" })
            Add(id, 2, ["Damage"], (c, context) => AdapterResult.Supported(Damage(c, context.Energy)), x: true);
        Add("CARD.BODY_SLAM", 2, ["CalculationBase", "ExtraDamage"], (c, _) =>
            Value(c, "CalculationBase") == 0 && Value(c, "ExtraDamage") == 1
                ? AdapterResult.Supported(new AdapterEffect(EffectKind.BlockDamage))
                : AdapterResult.Unsupported("unsupported-body-slam-scaling"));
        Add("CARD.PERFECTED_STRIKE", 2, ["CalculationBase", "ExtraDamage"], (c, context) =>
            c.HasStrikeTag != true || context.StrikeCount == null
                ? AdapterResult.Unsupported("strike-tag-metadata-unknown")
                : context.StrikeCount is < 1 or > 10000 ||
                    Value(c, "CalculationBase") != 6 || Value(c, "ExtraDamage") != 2 + c.UpgradeLevel
                    ? AdapterResult.Unsupported("unsupported-perfected-strike-scaling")
                    : AdapterResult.Supported(new AdapterEffect(EffectKind.Damage,
                        Value(c, "CalculationBase") + Value(c, "ExtraDamage") * context.StrikeCount.Value)));
        Add("CARD.ANGER", 3, ["Damage"], (c, _) => AdapterResult.Supported(Damage(c), new(EffectKind.CopyToDiscard)));
        Add("CARD.BLADE_DANCE", 3, ["Cards"], (c, _) => AdapterResult.Supported(
            new AdapterEffect(EffectKind.GenerateHand, Count: Count(c, "Cards"), GeneratedCard: BaseShiv)));
    }

    private static readonly CardDescriptor BaseShiv = new(
        "generated-template", "CARD.SHIV", "{\"id\":\"CARD.SHIV\"}", 0, 0, false, SimulationKeywords.Exhaust,
        ImmutableDictionary<string, decimal>.Empty.Add("Damage", 4), HasStrikeTag: false);
}
