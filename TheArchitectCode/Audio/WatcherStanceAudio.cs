using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.Audio;

internal sealed class WatcherStanceAudio(Creature localCreature, Action<PowerModel> enter,
    Action<PowerModel> leave)
{
    private sealed record Subscription(PowerModel Power, Action Removed, Action<Creature> Died,
        Action<int, int> HpChanged);

    private readonly Dictionary<Creature, Subscription> _owners = new(ReferenceEqualityComparer.Instance);
    internal PowerModel? LocalStance { get; private set; }
    internal int OwnerCount => _owners.Count;

    internal void Enter(PowerModel power)
    {
        if (power is not (CalmStancePower or WrathStancePower))
            throw new ArgumentException("Expected a Watcher stance power.", nameof(power));
        var owner = power.Owner;
        if (owner.IsDead || owner.CombatState == null || !owner.Powers.Contains(power))
            return;
        if (_owners.TryGetValue(owner, out var previous))
        {
            if (ReferenceEquals(previous.Power, power))
                return;
            Remove(previous.Power);
        }

        void Removed() => Remove(power);
        void Died(Creature _) => Remove(power);
        void HpChanged(int _, int hp)
        {
            if (hp <= 0)
                Remove(power);
        }
        _owners.Add(owner, new Subscription(power, Removed, Died, HpChanged));
        power.Removed += Removed;
        owner.Died += Died;
        owner.CurrentHpChanged += HpChanged;
        if (ReferenceEquals(owner, localCreature))
            LocalStance = power;
        enter(power);
    }

    private void Remove(PowerModel power)
    {
        if (!_owners.TryGetValue(power.Owner, out var subscription) ||
            !ReferenceEquals(subscription.Power, power))
            return;
        _owners.Remove(power.Owner);
        power.Removed -= subscription.Removed;
        power.Owner.Died -= subscription.Died;
        power.Owner.CurrentHpChanged -= subscription.HpChanged;
        if (ReferenceEquals(LocalStance, power))
            LocalStance = null;
        leave(power);
    }

    internal void Clear()
    {
        foreach (var subscription in _owners.Values.ToArray())
            Remove(subscription.Power);
    }
}
