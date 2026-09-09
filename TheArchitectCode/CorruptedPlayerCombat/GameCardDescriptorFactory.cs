using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

/// <summary>Reconstructs detached cards, but never invokes play, previews, global hooks, or calculated values.</summary>
public sealed class GameCardDescriptorFactory : ICardDescriptorFactory<SerializableCard>
{
    private readonly ExactCardAdapterRegistry registry;
    public GameCardDescriptorFactory(ExactCardAdapterRegistry? registry = null) =>
        this.registry = registry ?? new ExactCardAdapterRegistry();

    public CardDescriptor Describe(SerializableCard saved, string instanceId) =>
        Describe(JsonSerializer.SerializeToElement(saved, JsonSerializationUtility.GetTypeInfo<SerializableCard>()), instanceId);

    public CardDescriptor Describe(JsonElement original, string instanceId)
    {
        string raw = original.GetRawText();
        var fallback = new CardDescriptor(instanceId, "CARD.MISSING", raw, 0, 0, false,
            SimulationKeywords.None, ImmutableDictionary<string, decimal>.Empty,
            Eligibility: CardEligibility.Unknown);
        try
        {
            var saved = original.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>());
            if (saved == null || saved.Id == null) return fallback with { UnsupportedReason = "missing-model-id" };
            fallback = fallback with { ModelId = saved.Id.ToString(), UpgradeLevel = saved.CurrentUpgradeLevel };
            if (saved.CurrentUpgradeLevel is < 0 or > 1)
                return fallback with { UnsupportedReason = "malformed-card: unsupported upgrade level" };
            var card = CardModel.FromSerializable(saved);
            if (card is DeprecatedCard || card.Id != saved.Id)
                return fallback with { UnsupportedReason = "missing-model" };
            var keywords = SimulationKeywords.None;
            foreach (var keyword in card.GetKeywordsWithSources(KeywordSources.Local))
            {
                keywords |= keyword switch
                {
                    CardKeyword.Innate => SimulationKeywords.Innate,
                    CardKeyword.Retain => SimulationKeywords.Retain,
                    CardKeyword.Ethereal => SimulationKeywords.Ethereal,
                    CardKeyword.Exhaust => SimulationKeywords.Exhaust,
                    CardKeyword.Unplayable => SimulationKeywords.Unplayable,
                    _ => SimulationKeywords.None
                };
            }
            fallback = fallback with { Keywords = keywords, EnergyCost = card.EnergyCost.GetWithModifiers(CostModifiers.None), CostsX = card.EnergyCost.CostsX };
            if (card.GetType().Assembly != typeof(CardModel).Assembly ||
                typeof(CardModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion != ExactCardAdapterRegistry.AssemblySourceRevision)
                return fallback with { UnsupportedReason = "adapter-version-mismatch" };
            if (SavedCardModifierPolicy.HasUnsupportedModifiers(original))
                return fallback with { UnsupportedReason = "unsupported-saved-modifier" };
            // Even unsupported native plays can supply audited counting metadata.
            fallback = fallback with { HasStrikeTag = card.Tags.Contains(CardTag.Strike) };
            if (!registry.HasAdapter(fallback.ModelId)) return fallback with { UnsupportedReason = "adapter-missing" };
            var values = ImmutableDictionary.CreateBuilder<string, decimal>(StringComparer.Ordinal);
            foreach (string name in registry.RequiredVariables(fallback.ModelId))
            {
                if (!card.DynamicVars.TryGetValue(name, out var value))
                    return fallback with { UnsupportedReason = $"malformed-card:missing-{name}" };
                values.Add(name, value.BaseValue);
            }
            return fallback with { Values = values.ToImmutable(), Eligibility = CardEligibility.Eligible };
        }
        catch (Exception error)
        {
            return fallback with { UnsupportedReason = $"descriptor-error:{error.GetType().Name}" };
        }
    }
}
