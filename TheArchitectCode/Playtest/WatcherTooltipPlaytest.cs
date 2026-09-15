using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Enchantments;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.Relics;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class WatcherTooltipPlaytest
{
    private const string WrathText = "Within this Stance, deal and receive 50% extra damage.";
    private const string CalmPrefix = "Upon leaving this Stance, Gain ";

    internal static async Task RunFocused(NGame game)
    {
        Require(NativeDemoSafety.Enabled && !NativeDemoSafety.SharedVisible,
            "Focused tooltip rendering requires the private isolated launcher.");
        await Task.Delay(5000);
        var player = Player.CreateForNewRun<Ironclad>(UnlockState.all, 1);
        var run = RunState.CreateForNewRun([player],
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, 0, "ARCHITECT-STANCE-TOOLTIPS");
        RunManager.Instance.SetUpTest(run, new NetSingleplayerGameService(),
            disableCombatStateSync: true, shouldSave: false);
        var combat = new CombatState(runState: run);
        player.ResetCombatState();
        combat.AddPlayer(player);
        await Run(player);
        game.GetTree().Quit();
    }

    internal static async Task Run(Player player)
    {
        Require(NativeDemoSafety.Enabled, "Tooltip probes require disposable native-test storage.");
        var energyPath = $"res://images/packed/sprite_fonts/{EnergyIconHelper.GetPrefix(player.Character.CardPool)}_energy_icon.png";
        var calmText = CalmPrefix + $"[img]{energyPath}[/img]";
        var energy = PreloadManager.Cache.GetTexture2D(energyPath);
        Require(energy.GetWidth() > 0 && energy.GetHeight() > 0, "The native Energy sprite loads.");

        foreach (var enchantment in new EnchantmentModel[] { ArchitectModels.WrathEnchantment, ArchitectModels.CalmEnchantment })
        {
            var card = player.Creature.CombatState!.CreateCard<DefendIronclad>(player);
            card.AddKeyword(CardKeyword.Exhaust);
            CardCmd.Enchant(enchantment.ToMutable(), card, 1);
            var title = enchantment.Title.GetFormattedText();
            var expected = enchantment is Wrath ? WrathText : calmText;
            var tips = card.HoverTips.ToArray();
            Require(tips.OfType<HoverTip>().Count(tip => tip.Title == title) == 1 &&
                card.Enchantment!.HoverTips.Count() == 1,
                $"{title} card emits only the native enchantment explanation.");
            Require(tips.Any(tip => tip.Id == HoverTipFactory.FromKeyword(CardKeyword.Exhaust).Id),
                $"{title} card retains its unrelated Exhaust hover tip.");
            Require(tips.OfType<HoverTip>().Single(tip => tip.Title == title).Description == expected,
                $"{title} card has the exact requested tooltip wording.");
            await Render(tips, $"watcher-tooltip-{title.ToLowerInvariant()}", card, calmText);
            player.Creature.CombatState.RemoveCard(card);
        }

        var lotus = ArchitectModels.VioletLotus.HoverTipsExcludingRelic.ToArray();
        Require(lotus.Length == 2 && lotus.OfType<HoverTip>().Count(tip => tip.Title == "Wrath") == 1 &&
            lotus.OfType<HoverTip>().Count(tip => tip.Title == "Calm") == 1,
            "Violet Lotus emits exactly one explanation for each stance.");
        var lotusEnergyPrefix = RunManager.Instance.GetLocalCharacterEnergyIconPrefix() ??
            EnergyIconHelper.GetPrefix(ArchitectModels.CalmEnchantment);
        var lotusCalmText = CalmPrefix + $"[img]res://images/packed/sprite_fonts/{lotusEnergyPrefix}_energy_icon.png[/img]";
        Require(lotus.OfType<HoverTip>().Single(tip => tip.Title == "Calm").Description == lotusCalmText,
            "Violet Lotus uses the local player's Energy sprite, or the native colorless sprite outside a run.");
        await Render(lotus, "watcher-tooltip-lotus", null, lotusCalmText);

        foreach (var power in new CustomPowerModel[]
            { (WrathStancePower)ArchitectModels.WrathStance.ToMutable(),
                (CalmStancePower)ArchitectModels.CalmStance.ToMutable() })
        {
            power.ApplyInternal(player.Creature, 1);
            try
            {
                var expected = power is WrathStancePower ? WrathText : calmText;
                var tip = power.HoverTips.OfType<HoverTip>().Single(tip => tip.Title == power.Title.GetFormattedText());
                Require(tip.Description == expected, $"{tip.Title} active power shares the exact tooltip wording.");
                MainFile.Logger.Info($"WATCHER TOOLTIP POWER ICON: {power.Id} path={power.PackedIconPath}, " +
                    $"resource={tip.Icon?.ResourcePath}, big={power.CustomBigIconPath}, beta={power.CustomBigBetaIconPath}");
                await Render(power.HoverTips.ToArray(), $"watcher-tooltip-power-{tip.Title!.ToLowerInvariant()}", null, calmText);
            }
            finally
            {
                power.RemoveInternal();
            }
        }
        MainFile.Logger.Info("WATCHER TOOLTIPS SMOKE PASSED: native card/Exhaust, Violet Lotus, active powers and rendered Energy sprite.");
    }

    private static async Task Render(IHoverTip[] tips, string capture, CardModel? card, string calmText)
    {
        var game = NGame.Instance!;
        var anchor = new Control { Position = new Vector2(650, 280), Size = new Vector2(200, 300) };
        var container = game.HoverTipsContainer ?? throw new InvalidOperationException("Native tooltip container is unavailable.");
        container.AddChild(anchor);
        try
        {
            if (card != null)
            {
                var visual = NCard.Create(card) ?? throw new InvalidOperationException("Tooltip card preview could not be created.");
                anchor.AddChild(visual);
                visual.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
                visual.Position = new Vector2(60, 160);
            }
            var set = NHoverTipSet.CreateAndShow(anchor, tips, HoverTipAlignment.Right) ??
                throw new InvalidOperationException("Native tooltip display was blocked.");
            await Task.Delay(300);
            await game.AwaitProcessFrame();
            var rendered = set.GetNode<Control>("textHoverTipContainer").GetChildren().OfType<Control>().ToArray();
            Require(rendered.Length == tips.Length, "Native tooltip renderer retains exactly the intended entries.");
            var descriptions = rendered.Select(tip => tip.GetNode<MegaRichTextLabel>("%Description")).ToArray();
            Require(descriptions.Select(label => label.Text).SequenceEqual(tips.OfType<HoverTip>().Select(tip => tip.Description)),
                "Rendered tooltip labels contain the native formatted descriptions.");
            if (tips.OfType<HoverTip>().Any(tip => tip.Title == "Calm"))
            {
                var label = descriptions.Single(description => description.Text == calmText);
                var parsed = label.GetParsedText();
                Require(parsed.StartsWith(CalmPrefix, StringComparison.Ordinal) &&
                    !parsed.Contains("[img]", StringComparison.Ordinal) && !parsed.Contains("res://", StringComparison.Ordinal),
                    "Calm renders the Energy image rather than exposing markup or a literal placeholder.");
            }
            await NativeDemoPlaytest.Capture(capture);
        }
        finally
        {
            NHoverTipSet.Remove(anchor);
            anchor.QueueFree();
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Watcher tooltips: " + message);
        MainFile.Logger.Info("WATCHER TOOLTIP PASS: " + message);
    }
}
