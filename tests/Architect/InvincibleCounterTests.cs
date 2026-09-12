using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.Powers;

internal static class InvincibleCounterTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Invincible counter: uses native Hardened Shell numeric presentation", () =>
        {
            var power = new ArchitectInvinciblePower();
            check(power.StackType == PowerStackType.Counter,
                "NPower hides DisplayAmount unless StackType is Counter.");
            check(power.StackType == new HardenedShellPower().StackType,
                "Invincible must use the native Hardened Shell counter contract.");
        });

        foreach (var limit in new[] { 200, 300, 520, 780, 1040, 1170, 1560 })
        {
            test($"Invincible counter {limit}: native display events track the shared budget and reset", () =>
            {
                var (owner, power) = CreatePower(limit);
                var displays = new List<int>();
                power.DisplayAmountChanged += () => displays.Add(power.DisplayAmount);

                Hit(owner, power, 50);
                Hit(owner, power, limit);
                Hit(owner, power, 20);
                check(displays.SequenceEqual(new[] { limit - 50, 0 }),
                    "Each HP loss must synchronously notify the native counter; capped hits spend nothing.");
                check(power.Amount == limit && owner.HasPower<ArchitectInvinciblePower>(),
                    "Reaching zero must not change or remove the underlying shared cap.");
                check(power.DynamicVars["Remaining"].IntValue == 0,
                    "The tooltip must agree with the numeric counter.");

                power.BeforeSideTurnStart(null!, CombatSide.Player, [owner], null!).GetAwaiter().GetResult();
                check(displays.Count == 2, "Player turns must not replenish the boss's shared budget.");
                power.BeforeSideTurnStart(null!, CombatSide.Enemy, [owner], null!).GetAwaiter().GetResult();
                check(displays.SequenceEqual(new[] { limit - 50, 0, limit }),
                    "The owner turn must notify the native counter when its cap resets.");
                check(owner.HpDisplay == HpDisplay.Normal &&
                      power.DynamicVars["Remaining"].IntValue == limit,
                    "The health bar, tooltip, and counter must reset together.");
            });
        }

        test("Invincible counter: encounter restart creates a fresh independent cap", () =>
        {
            var canonical = new ArchitectInvinciblePower();
            var (oldOwner, oldPower) = CreatePower(300, canonical);
            Hit(oldOwner, oldPower, 300);
            oldOwner.Reset();

            // Native CombatRoom reload reconstructs the encounter, not its in-progress powers.
            var (newOwner, newPower) = CreatePower(300, canonical);
            check(newPower.DisplayAmount == 300 && newOwner.HpDisplay == HpDisplay.Normal,
                "Reloading a restarted combat must not retain the previous instance's spent budget.");
            Hit(oldOwner, oldPower, 50);
            check(newPower.DisplayAmount == 300, "The discarded owner's listener must remain detached.");
            Hit(newOwner, newPower, 75);
            check(newPower.DisplayAmount == 225 && oldPower.DisplayAmount == 0,
                "The recreated counter must track only its new owner.");
        });
    }

    private static (Creature Owner, ArchitectInvinciblePower Power) CreatePower(
        int limit, ArchitectInvinciblePower? canonical = null)
    {
        var owner = new Creature(new TenHpMonster().ToMutable(), CombatSide.Enemy, null);
        owner.SetMaxHpInternal(10000);
        owner.SetCurrentHpInternal(10000);
        var power = (ArchitectInvinciblePower)(canonical ?? new ArchitectInvinciblePower()).ToMutable();
        power.ApplyInternal(owner, limit);
        power.AfterApplied(null, null).GetAwaiter().GetResult();
        return (owner, power);
    }

    private static void Hit(Creature owner, PowerModel power, decimal amount) =>
        owner.LoseHpInternal(
            power.ModifyHpLostAfterOstyLate(owner, amount, ValueProp.Move, null, null), ValueProp.Move);
}
