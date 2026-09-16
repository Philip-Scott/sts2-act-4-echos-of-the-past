using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Enchantments;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.Relics;

internal static class WatcherTooltipTests
{
    private const string WrathText = "Within this Stance, deal and receive 50% extra damage.";
    private const string CalmText =
        "Upon leaving this Stance, Gain [img]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]";

    internal static void Run(Action<string, Action> test)
    {
        test("Watcher tooltips: native enchantment factories emit exactly one tip per stance", () => WithPresentation(() =>
        {
            AssertTip(HoverTipFactory.FromEnchantment<Wrath>().Single(), "Wrath", WrathText);
            AssertTip(HoverTipFactory.FromEnchantment<Calm>().Single(), "Calm", CalmText);
        }));

        test("Watcher tooltips: Violet Lotus keeps exactly one Wrath and one Calm explanation", () => WithPresentation(() =>
        {
            var tips = ModelDb.Relic<VioletLotus>().HoverTipsExcludingRelic.ToArray();
            Check(tips.Length == 2);
            AssertTip(tips[0], "Wrath", WrathText);
            AssertTip(tips[1], "Calm", CalmText);
        }));

        test("Watcher tooltips: stance powers share exact wording and native Energy icon markup", () => WithPresentation(() =>
        {
            foreach (var (power, title, expected) in new (PowerModel, string, string)[]
            {
                (ModelDb.Power<WrathStancePower>(), "Wrath", WrathText),
                (ModelDb.Power<CalmStancePower>(), "Calm", CalmText)
            })
            {
                AssertTip(power.GetDumbHoverTip(), title, expected);
                var smart = power.SmartDescription;
                smart.Add("energyPrefix", "ironclad");
                Check(smart.GetFormattedText() == expected);
            }
        }));
    }

    private static void AssertTip(IHoverTip tip, string title, string description) =>
        Check(tip is HoverTip hover && hover.Title == title && hover.Description == description);

    private static void WithPresentation(Action action)
    {
        var previous = LocManager.Instance;
        var manager = (LocManager)RuntimeHelpers.GetUninitializedObject(typeof(LocManager));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "TheArchitect/localization/eng")))
            root = root.Parent;
        if (root == null)
            throw new DirectoryNotFoundException("Cannot locate Architect localization.");
        var tables = new Dictionary<string, LocTable>();
        foreach (var name in new[] { "enchantments", "powers" })
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(root.FullName, $"TheArchitect/localization/eng/{name}.json")))!;
            // Test-assembly models use native IDs without BaseLib's mod-registration prefix.
            tables.Add(name, new LocTable(name, entries.ToDictionary(
                pair => pair.Key["THEARCHITECT-".Length..], pair => pair.Value)));
        }
        typeof(LocManager).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, tables);
        typeof(LocManager).GetMethod("LoadLocFormatters", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, null);
        var instance = typeof(LocManager).GetProperty(nameof(LocManager.Instance))!;
        instance.SetValue(null, manager);
        var harmony = new Harmony("TheArchitect.Tests.WatcherTooltips");
        // Replace only texture loading and the absent local player's color; retain native tip assembly and formatting.
        var icons = new[] { AccessTools.PropertyGetter(typeof(EnchantmentModel), nameof(EnchantmentModel.Icon)),
            AccessTools.PropertyGetter(typeof(PowerModel), nameof(PowerModel.Icon)) };
        var prefix = AccessTools.Method(typeof(EnergyIconHelper), nameof(EnergyIconHelper.GetPrefix));
        try
        {
            foreach (var icon in icons)
                harmony.Patch(icon, prefix: new HarmonyMethod(typeof(WatcherTooltipTests), nameof(SkipTexture)));
            harmony.Patch(prefix, prefix: new HarmonyMethod(typeof(WatcherTooltipTests), nameof(EnergyPrefix)));
            action();
        }
        finally
        {
            foreach (var method in icons.Append(prefix))
                harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
            instance.SetValue(null, previous);
        }
    }

    private static bool SkipTexture() => false;
    private static bool EnergyPrefix(ref string __result)
    {
        __result = "ironclad";
        return false;
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Watcher tooltip assertion failed.");
    }
}
