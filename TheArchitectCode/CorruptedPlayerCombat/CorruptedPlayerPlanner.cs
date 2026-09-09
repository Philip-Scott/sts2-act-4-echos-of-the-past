using System.Collections.Immutable;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

public sealed class CorruptedPlayerPlanner(ExactCardAdapterRegistry registry)
{
    public const int HandLimit = 10;
    public const int SelectionLimit = 20;
    public const int StartingEnergy = 3;
    public const int DrawPerTurn = 5;

    public CorruptedPlayerPlan BuildPlan(CorruptedPlayerState state, PlannerInputs inputs)
    {
        ValidateState(state);
        if (inputs.Opponents.IsDefault || inputs.Opponents.Any(p => p.Slot < 0) ||
            inputs.Opponents.Select(p => p.Slot).Distinct().Count() != inputs.Opponents.Length ||
            inputs.SelfBlock < 0 || inputs.SelfBlock > 100000 ||
            Math.Abs(inputs.Strength) > 10000 || Math.Abs(inputs.Dexterity) > 10000)
            throw new ArgumentException("Invalid bounded combat inputs.", nameof(inputs));

        var rng = new PrivateRng(state.RngState);
        var draw = state.Draw.ToList();
        var hand = state.Hand.ToList();
        var discard = state.Discard.ToList();
        var exhaust = state.Exhaust.ToList();
        var candidates = new List<CardDescriptor>(hand);
        var cards = ImmutableArray.CreateBuilder<PlannedCard>();
        var diagnostics = ImmutableArray.CreateBuilder<string>();
        var vulnerable = inputs.Opponents.ToDictionary(p => p.Slot, p => p.Vulnerable);
        int[] slots = vulnerable.Keys.Order().ToArray();
        int energy = StartingEnergy, selected = 0;
        decimal block = inputs.SelfBlock, strength = inputs.Strength, dexterity = inputs.Dexterity;
        long generatedCount = state.GeneratedCount;

        void AddHand(CardDescriptor card)
        {
            hand.Add(card);
            candidates.Add(card);
        }
        int Draw(int count)
        {
            int drawn = 0;
            while (count-- > 0 && hand.Count < HandLimit)
            {
                if (draw.Count == 0)
                {
                    if (discard.Count == 0) break;
                    draw.AddRange(discard);
                    discard.Clear();
                    rng.Shuffle(draw);
                }
                CardDescriptor card = draw[0];
                draw.RemoveAt(0);
                AddHand(card);
                drawn++;
            }
            return drawn;
        }

        int? CountStrikes()
        {
            int count = 0;
            // Native AllCards includes Exhaust and Play. Translation runs before
            // removing the current card from Hand, so it already counts once.
            foreach (var card in draw.Concat(hand).Concat(discard).Concat(exhaust))
            {
                if (!card.HasStrikeTag.HasValue) return null;
                if (card.HasStrikeTag.Value) count++;
            }
            return count;
        }

        if (state.Turn == 0)
        {
            foreach (CardDescriptor innate in draw.Where(c => c.Has(SimulationKeywords.Innate)).ToArray())
            {
                if (hand.Count == HandLimit) break;
                draw.Remove(innate);
                AddHand(innate);
            }
            Draw(Math.Max(0, DrawPerTurn - hand.Count));
        }
        else Draw(DrawPerTurn);

        bool capped = false;
        for (int cursor = 0; cursor < candidates.Count; cursor++)
        {
            var card = candidates[cursor];
            if (selected == SelectionLimit)
            {
                cards.Add(new(card, cursor, null, CardDisposition.Unplayed, 0, 0, [], "plan-limit"));
                continue;
            }
            if (card.Has(SimulationKeywords.Unplayable) || card.Eligibility == CardEligibility.Ineligible)
            {
                cards.Add(new(card, cursor, null, CardDisposition.Unplayed, 0, 0, [], "intrinsically-unplayable"));
                continue;
            }

            var result = registry.Translate(card, new(energy, block, hand.Count,
                card.ModelId == "CARD.PERFECTED_STRIKE" ? CountStrikes() : null));
            if (result.Eligibility == CardEligibility.Ineligible)
            {
                cards.Add(new(card, cursor, null, CardDisposition.Unplayed, 0, 0, [], result.Diagnostic ?? "condition-failed"));
                continue;
            }
            bool unsupported = result.Diagnostic != null || result.Eligibility == CardEligibility.Unknown;
            int cost = unsupported ? 0 : card.CostsX ? energy : card.EnergyCost;
            int x = !unsupported && card.CostsX ? energy : 0;
            if (cost > energy)
            {
                cards.Add(new(card, cursor, null, CardDisposition.Unplayed, 0, 0, [], "unaffordable"));
                continue;
            }

            energy -= cost;
            hand.Remove(card);
            var actions = ImmutableArray.CreateBuilder<PlannedAction>();
            if (unsupported)
            {
                actions.Add(new(ActionKind.Unknown));
                diagnostics.Add($"{card.InstanceId}: {result.Diagnostic ?? "eligibility-unknown"}");
            }
            else
            {
                foreach (var effect in result.Effects)
                {
                    switch (effect.Kind)
                    {
                        case EffectKind.Damage:
                        case EffectKind.BlockDamage:
                            decimal baseDamage = effect.Kind == EffectKind.BlockDamage ? block : effect.Amount;
                            foreach (int slot in slots)
                            {
                                decimal damage = Math.Max(0, baseDamage + strength);
                                if (inputs.Weak) damage *= 0.75m;
                                if (vulnerable[slot]) damage *= 1.5m;
                                actions.Add(new(ActionKind.Damage, decimal.Floor(damage), effect.Count, slot));
                            }
                            break;
                        case EffectKind.Block:
                            decimal gained = Math.Max(0, effect.Amount + dexterity);
                            if (inputs.Frail) gained *= 0.75m;
                            gained = decimal.Floor(gained);
                            block += gained;
                            actions.Add(new(ActionKind.Block, gained));
                            break;
                        case EffectKind.Strength:
                            strength += effect.Amount;
                            actions.Add(new(ActionKind.Strength, effect.Amount));
                            break;
                        case EffectKind.Dexterity:
                            dexterity += effect.Amount;
                            actions.Add(new(ActionKind.Dexterity, effect.Amount));
                            break;
                        case EffectKind.Vulnerable:
                        case EffectKind.Weak:
                            foreach (int slot in slots)
                            {
                                actions.Add(new(effect.Kind == EffectKind.Vulnerable ? ActionKind.Vulnerable : ActionKind.Weak, effect.Amount, 1, slot));
                                if (effect.Kind == EffectKind.Vulnerable && effect.Amount > 0) vulnerable[slot] = true;
                            }
                            break;
                        case EffectKind.Heal: actions.Add(new(ActionKind.Heal, effect.Amount)); break;
                        case EffectKind.HpLoss: actions.Add(new(ActionKind.HpLoss, effect.Amount)); break;
                        case EffectKind.Energy:
                            energy += (int)effect.Amount;
                            actions.Add(new(ActionKind.Energy, effect.Amount));
                            break;
                        case EffectKind.Draw:
                            actions.Add(new(ActionKind.Draw, Draw(effect.Count)));
                            break;
                        case EffectKind.GenerateHand:
                            int generated = 0;
                            for (int n = 0; n < effect.Count && hand.Count < HandLimit; n++)
                            {
                                AddHand(effect.GeneratedCard! with { InstanceId = $"generated:{++generatedCount}" });
                                generated++;
                            }
                            actions.Add(new(ActionKind.GenerateHand, generated));
                            break;
                        case EffectKind.CopyToDiscard:
                            for (int n = 0; n < effect.Count; n++)
                                discard.Add(card with { InstanceId = $"generated:{++generatedCount}" });
                            actions.Add(new(ActionKind.CopyToDiscard, effect.Count));
                            break;
                    }
                }
            }
            cards.Add(new(card, cursor, ++selected, unsupported ? CardDisposition.Unsupported : CardDisposition.Played,
                cost, x, actions.ToImmutable(), result.Diagnostic));
            if (selected == SelectionLimit && !capped)
            {
                diagnostics.Add("plan-limit: 20 selections reached; remaining cards are unplayed.");
                capped = true;
            }
            (card.Has(SimulationKeywords.Exhaust) ? exhaust : discard).Add(card);
        }

        foreach (var card in hand.ToArray())
        {
            if (card.Has(SimulationKeywords.Ethereal))
            {
                exhaust.Add(card);
                hand.Remove(card);
            }
            else if (!card.Has(SimulationKeywords.Retain))
            {
                discard.Add(card);
                hand.Remove(card);
            }
        }
        var next = new CorruptedPlayerState(state.Version, checked(state.Turn + 1), rng.State, generatedCount,
            draw.ToImmutableArray(), hand.ToImmutableArray(), discard.ToImmutableArray(), exhaust.ToImmutableArray());
        return new(next.Turn, cards.ToImmutable(), next, diagnostics.ToImmutable());
    }

    public static void ValidateState(CorruptedPlayerState state)
    {
        if (state.Version != CorruptedPlayerState.CurrentVersion || state.Turn < 0 || state.GeneratedCount < 0 ||
            state.Draw.IsDefault || state.Hand.IsDefault || state.Discard.IsDefault || state.Exhaust.IsDefault ||
            state.Hand.Length > HandLimit)
            throw new ArgumentException("Invalid Corrupted Player state or unsupported RNG schema.");
        var all = state.Draw.Concat(state.Hand).Concat(state.Discard).Concat(state.Exhaust).ToArray();
        if (all.Any(c => c == null || string.IsNullOrWhiteSpace(c.InstanceId)) ||
            all.Select(c => c.InstanceId).Distinct(StringComparer.Ordinal).Count() != all.Length)
            throw new ArgumentException("Corrupted Player piles contain duplicate or invalid instances.");
    }
}
