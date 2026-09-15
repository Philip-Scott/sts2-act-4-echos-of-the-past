using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.Acts;
using MegaCrit.Sts2.Core.Map;
using BaseLib.Utils;
using System.Reflection;
using MegaCrit.Sts2.Core.Models.RelicPools;
using TheArchitect.TheArchitectCode.Relics;
using TheArchitect.TheArchitectCode.Playtest;
using System.Text.Json;

int passed = 0;
const int ownerMaxHp = 10000;

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
    owner.SetMaxHpInternal(ownerMaxHp);
    owner.SetCurrentHpInternal(ownerMaxHp);
    var power = (ArchitectInvinciblePower)new ArchitectInvinciblePower().ToMutable();
    power.ApplyInternal(owner, limit);
    power.AfterApplied(null, null).GetAwaiter().GetResult();
    return (owner, power);
}

decimal Capped(ArchitectInvinciblePower power, Creature owner, decimal amount, ValueProp props = ValueProp.Move) =>
    power.ModifyHpLostAfterOstyLate(owner, amount, props, null, null);

void Hit(ArchitectInvinciblePower power, Creature owner, decimal amount, ValueProp props = ValueProp.Move) =>
    owner.LoseHpInternal(Capped(power, owner, amount, props), props);

Test("cap: opts into native multiplayer power scaling", () =>
{
    Check(new ArchitectInvinciblePower().ShouldScaleInMultiplayer);
});

// Solo caps and native final-act boss caps for parties of two through four.
foreach (int limit in new[] { 200, 300, 520, 780, 1040, 1170, 1560 })
{
    Test($"cap {limit}: consecutive hits cannot exceed the shared budget", () =>
    {
        var (owner, power) = Power(limit);
        Hit(power, owner, limit - 25);
        Hit(power, owner, limit - 25);
        Hit(power, owner, limit - 25);
        Check(owner.CurrentHp == ownerMaxHp - limit && power.DisplayAmount == 0);
        Check(power.DynamicVars["Remaining"].IntValue == 0);
        Check(owner.HpDisplay == HpDisplay.InfiniteWithNumbers);
    });

    Test($"cap {limit}: health bar changes only when the damage budget is exhausted", () =>
    {
        var (owner, power) = Power(limit);
        Check(owner.HpDisplay == HpDisplay.Normal);
        Check(Capped(power, owner, limit) == limit);
        Check(owner.HpDisplay == HpDisplay.Normal, "Damage previews must not change the health bar.");
        Hit(power, owner, limit - 1);
        Check(owner.HpDisplay == HpDisplay.Normal);
        Hit(power, owner, 1, ValueProp.Unpowered | ValueProp.Unblockable);
        Check(owner.HpDisplay == HpDisplay.InfiniteWithNumbers);
        owner.SetCurrentHpInternal(ownerMaxHp);
        Check(owner.HpDisplay == HpDisplay.InfiniteWithNumbers, "Healing must not clear the capped display.");
        Hit(power, owner, 10);
        Check(owner.CurrentHp == ownerMaxHp && owner.HpDisplay == HpDisplay.InfiniteWithNumbers);
    });

    Test($"cap {limit}: preview does not spend budget; poison and power damage do", () =>
    {
        var (owner, power) = Power(limit);
        Check(Capped(power, owner, ownerMaxHp) == limit && Capped(power, owner, ownerMaxHp) == limit);
        Check(power.DisplayAmount == limit);
        Hit(power, owner, 70, ValueProp.Unpowered | ValueProp.Unblockable);
        Hit(power, owner, 90, ValueProp.Unpowered);
        Check(power.DisplayAmount == limit - 160);
    });

    Test($"cap {limit}: healing does not replenish the budget", () =>
    {
        var (owner, power) = Power(limit);
        Hit(power, owner, 100);
        owner.SetCurrentHpInternal(ownerMaxHp);
        Check(power.DisplayAmount == limit - 100);
        Hit(power, owner, ownerMaxHp);
        Check(owner.CurrentHp == ownerMaxHp - (limit - 100));
    });

    Test($"cap {limit}: reset only before an owner turn, not player or absent-owner turns", () =>
    {
        var (owner, power) = Power(limit);
        Hit(power, owner, ownerMaxHp);
        power.BeforeSideTurnStart(null!, CombatSide.Player, [owner], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == 0);
        Check(owner.HpDisplay == HpDisplay.InfiniteWithNumbers);
        power.BeforeSideTurnStart(null!, CombatSide.Enemy, [], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == 0);
        Check(owner.HpDisplay == HpDisplay.InfiniteWithNumbers);
        power.BeforeSideTurnStart(null!, CombatSide.Enemy, [owner], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == limit);
        Check(owner.HpDisplay == HpDisplay.Normal);
        Hit(power, owner, 25, ValueProp.Unpowered | ValueProp.Unblockable);
        power.AfterSideTurnStart(CombatSide.Enemy, [owner], null!).GetAwaiter().GetResult();
        Check(power.DisplayAmount == limit - 25, "Poison damage must not be erased by a later reset.");
        Check(owner.HpDisplay == HpDisplay.Normal);
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

    Test($"cap {limit}: power removal and combat reset restore the normal health bar", () =>
    {
        foreach (var reset in new[] { false, true })
        {
            var (owner, power) = Power(limit);
            Hit(power, owner, limit);
            Check(owner.HpDisplay == HpDisplay.InfiniteWithNumbers);
            if (reset)
                owner.Reset();
            else
                power.RemoveInternal();
            Check(owner.HpDisplay == HpDisplay.Normal);
            owner.LoseHpInternal(50, ValueProp.Move);
            Check(owner.HpDisplay == HpDisplay.Normal, "The removed power must not change the display.");
        }
    });
}

foreach (var hasAncient in new[] { true, false })
{
    Test($"map: fixed route (Ancient={hasAncient})", () =>
    {
        var map = new ArchitectMap(hasAncient);
        MapPointType[] expected = hasAncient
            ? [MapPointType.Ancient, MapPointType.RestSite, MapPointType.Shop, MapPointType.Boss]
            : [MapPointType.RestSite, MapPointType.Shop, MapPointType.Boss];
        var point = map.StartingMapPoint;
        for (var row = 0; row < expected.Length; row++)
        {
            Check(point.PointType == expected[row] && point.coord.col == 3 && point.coord.row == row);
            Check(!point.CanBeModified);
            if (row < expected.Length - 1)
                point = point.Children.Single();
        }
        Check(point == map.BossMapPoint && !point.Children.Any());
        Check(map.GetAllMapPoints().Count() == expected.Length - 2,
            "Starting Ancient and boss must be outside the interior grid.");
    });
}

UnwrittenBonusTests.Run(Test);
WatcherRelicTests.Run(Test);
WatcherAudioTests.Run(Test);
HistoryIconTests.Run(Test);
HandheldMirrorTests.Run(Test, Check);
NativeCardSupportTests.Run(Test, Check);
NativeDownfallApiTests.Run(Test, Check);
CorruptedPartyTests.Run(Test, Check);
CorruptedEnemyUiTests.Run(Test, Check);
InvincibleCounterTests.Run(Test, Check);

Test("Watcher: all fifteen current and legacy relics declare the native description pool", () =>
{
    Type[] relics = [typeof(LooseThread), typeof(CrookedNeedle), typeof(OrangePearl), typeof(DiamondHand),
        typeof(UnspentPossibility), typeof(LastMeal), typeof(BorrowedTomorrow), typeof(HandheldMirror),
        typeof(TheLastWish), typeof(GoldenEye), typeof(DeusExMachina), typeof(NurembergEgg),
        typeof(TheArchitect.TheArchitectCode.Relics.RitualDagger), typeof(VioletLotus),
        typeof(TheArchitect.TheArchitectCode.Relics.DevaForm)];
    foreach (var relic in relics)
        Check(relic.GetCustomAttribute<PoolAttribute>()?.PoolType == typeof(SharedRelicPool), relic.Name);
});

Test("Watcher: each current and legacy relic uses its own small, outline and large icon", () =>
{
    (Type Type, string Slug)[] icons =
    [
        (typeof(LooseThread), "loose_thread"), (typeof(CrookedNeedle), "crooked_needle"),
        (typeof(OrangePearl), "orange_pearl"), (typeof(DiamondHand), "diamond_hand"),
        (typeof(UnspentPossibility), "unspent_possibility"), (typeof(LastMeal), "last_meal"),
        (typeof(BorrowedTomorrow), "borrowed_tomorrow"), (typeof(HandheldMirror), "handheld_mirror"),
        (typeof(TheLastWish), "the_last_wish"), (typeof(GoldenEye), "golden_eye"),
        (typeof(DeusExMachina), "deus_ex_machina"), (typeof(NurembergEgg), "nuremberg_egg"),
        (typeof(TheArchitect.TheArchitectCode.Relics.RitualDagger), "ritual_dagger"),
        (typeof(VioletLotus), "violet_lotus"), (typeof(TheArchitect.TheArchitectCode.Relics.DevaForm), "deva_form")
    ];
    var outline = typeof(UnwrittenRelic).GetProperty("PackedIconOutlinePath",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var big = typeof(UnwrittenRelic).GetProperty("BigIconPath",
        BindingFlags.Instance | BindingFlags.NonPublic)!;
    var paths = new HashSet<string>();
    foreach (var (type, slug) in icons)
    {
        if (ModelDb.GetByIdOrNull<RelicModel>(ModelDb.GetId(type)) == null)
            ModelDb.Inject(type);
        var relic = ModelDb.GetById<RelicModel>(ModelDb.GetId(type));
        Check(relic.PackedIconPath == $"res://TheArchitect/images/relics/{slug}.png", slug);
        Check((string?)outline.GetValue(relic) == $"res://TheArchitect/images/relics/{slug}_outline.png", slug);
        Check((string?)big.GetValue(relic) == $"res://TheArchitect/images/relics/big/{slug}.png", slug);
        Check(paths.Add(relic.PackedIconPath), slug);
    }
    Check(paths.Count == 15);
});

Test("history setup: all native target slots accept a natural-offer configuration", () =>
{
    var root = Path.GetTempPath();
    foreach (var slot in new[] { 1, 2, 3 })
    {
        var setup = new ArchitectHistorySetup(slot, Path.Combine(root, "player.run"),
            Path.Combine(root, "opponent.run"), Path.Combine(root, "ready"), "NATURAL-SEED", 3, 0, false);
        setup.Validate();
        var restored = JsonSerializer.Deserialize<ArchitectHistorySetup>(JsonSerializer.Serialize(setup))!;
        restored.Validate();
        Check(restored.ProfileId == slot && !restored.ForceMirror && restored.Seed == "NATURAL-SEED");
    }
});

Test("history setup: invalid target slots, paths and resource values fail explicitly", () =>
{
    var root = Path.GetTempPath();
    var valid = new ArchitectHistorySetup(2, Path.Combine(root, "player.run"),
        Path.Combine(root, "opponent.run"), Path.Combine(root, "ready"), "SEED", 3, 0);
    var invalid = new[]
    {
        valid with { ProfileId = 0 }, valid with { ProfileId = 4 },
        valid with { MaxEnergy = 0 }, valid with { BaseOrbSlotCount = -1 },
        valid with { PlayerHistoryPath = "relative.run" },
        valid with { OpponentHistoryPath = "" },
        valid with { OutputDirectory = "relative" },
        valid with { Seed = " " }
    };
    foreach (var setup in invalid)
    {
        var rejected = false;
        try { setup.Validate(); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, $"Invalid setup was accepted: {setup}");
    }
    Check(valid.ForceMirror, "Legacy Mirror-specific configurations retain their default.");
});

EnchantmentIconTests.Run(Test);

Console.WriteLine($"{passed} Architect tests passed.");
