using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.Powers;

int passed = 0;

void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { Console.Error.WriteLine($"FAIL {name}: {error}"); Environment.ExitCode = 1; }
}

void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

(Creature, ArchitectInvinciblePower) Power(int limit)
{
    var owner = new Creature(new TenHpMonster().ToMutable(), CombatSide.Enemy, null);
    owner.SetMaxHpInternal(1000);
    owner.SetCurrentHpInternal(1000);
    var power = (ArchitectInvinciblePower)new ArchitectInvinciblePower().ToMutable();
    power.ApplyInternal(owner, limit);
    power.AfterApplied(null, null).GetAwaiter().GetResult();
    return (owner, power);
}

decimal Capped(ArchitectInvinciblePower power, Creature owner, decimal amount, ValueProp props = ValueProp.Move) =>
    power.ModifyHpLostAfterOstyLate(owner, amount, props, null, null);

void Hit(ArchitectInvinciblePower power, Creature owner, decimal amount, ValueProp props = ValueProp.Move) =>
    owner.LoseHpInternal(Capped(power, owner, amount, props), props);

foreach (int limit in new[] { 200, 300 })
{
    Test($"cap {limit}: consecutive hits cannot exceed the shared budget", () =>
    {
        var (owner, power) = Power(limit);
        Hit(power, owner, 175);
        Hit(power, owner, 175);
        Hit(power, owner, 175);
        Check(owner.CurrentHp == 1000 - limit && power.DisplayAmount == 0);
        Check(power.DynamicVars["Remaining"].IntValue == 0);
    });

    Test($"cap {limit}: preview does not spend budget; poison and power damage do", () =>
    {
        var (owner, power) = Power(limit);
        Check(Capped(power, owner, 500) == limit && Capped(power, owner, 500) == limit);
        Check(power.DisplayAmount == limit);
        Hit(power, owner, 70, ValueProp.Unpowered | ValueProp.Unblockable);
        Hit(power, owner, 90, ValueProp.Unpowered);
        Check(power.DisplayAmount == limit - 160);
    });

    Test($"cap {limit}: healing does not replenish the budget", () =>
    {
        var (owner, power) = Power(limit);
        Hit(power, owner, 100);
        owner.SetCurrentHpInternal(1000);
        Check(power.DisplayAmount == limit - 100);
        Hit(power, owner, 1000);
        Check(owner.CurrentHp == 1000 - (limit - 100));
    });

    Test($"cap {limit}: reset only before an owner turn, not player or absent-owner turns", () =>
    {
        var (owner, power) = Power(limit);
        Hit(power, owner, 1000);
        power.BeforeSideTurnStart(null!, CombatSide.Player, [owner], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == 0);
        power.BeforeSideTurnStart(null!, CombatSide.Enemy, [], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == 0);
        power.BeforeSideTurnStart(null!, CombatSide.Enemy, [owner], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == limit);
        Hit(power, owner, 25, ValueProp.Unpowered | ValueProp.Unblockable);
        power.AfterSideTurnStart(CombatSide.Enemy, [owner], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == limit - 25, "Poison damage must not be erased by a later reset.");
    });

    Test($"cap {limit}: other creatures are unaffected and removal detaches accounting", () =>
    {
        var (owner, power) = Power(limit);
        var (other, _) = Power(limit);
        Check(Capped(power, other, 500) == 500);
        power.RemoveInternal();
        owner.LoseHpInternal(50, ValueProp.Move);
        Check(power.DisplayAmount == limit);
    });

    Test($"cap {limit}: engine reset removes the synchronous HP listener", () =>
    {
        var (owner, power) = Power(limit);
        owner.Reset();
        owner.LoseHpInternal(50, ValueProp.Move);
        Check(power.DisplayAmount == limit && owner.Powers.Count == 0);
    });
}

Console.WriteLine($"{passed} Architect power tests passed.");
