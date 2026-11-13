using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace TheArchitect.TheArchitectCode.Playtest;

// Violet Lotus enchants deck cards with the mod's Calm and Wrath. Loading a saved run preloads every
// enchantment type by ID, so the run must survive repeated save-and-reload after the enumeration is hot.
internal static class NativeVioletLotusPlaytest
{
    internal static async Task Run(NGame game)
    {
        Require(NativeDemoSafety.Enabled, "Violet Lotus probes require disposable native-test storage.");
        var run = await game.StartNewSingleplayerRun(Player.CreateForNewRun(ModelDb.Character<Ironclad>(),
                SaveManager.Instance.GenerateUnlockStateFromProgress(), 1), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], "ARCHITECT-VIOLET-LOTUS", GameMode.Standard, false);
        await Task.Delay(1500);
        var player = run.Players.Single();
        Require(Relics.VioletLotus.CanReceive(player), "the starter deck can receive Violet Lotus");
        await RelicCmd.Obtain(ArchitectModels.VioletLotus.ToMutable(), player);
        var calm = Enchanted(player, ArchitectModels.CalmEnchantmentId);
        var wrath = Enchanted(player, ArchitectModels.WrathEnchantmentId);
        Require(calm.Length == 2 && wrath.Length == 2, $"Violet Lotus enchants two Calm and two Wrath cards " +
            $"(calm={string.Join(",", calm)}, wrath={string.Join(",", wrath)})");

        // A long session tiers up the game's enchantment preload enumeration before a later reload.
        for (var i = 0; i < 300; i++)
        {
            Require(ModelDb.DebugEnchantments.Any(enchantment => enchantment.Id == ArchitectModels.CalmEnchantmentId),
                "the enchantment preload list contains the mod's Calm");
            if (i % 30 == 0)
                await Task.Delay(250);
        }

        for (var reload = 1; reload <= 3; reload++)
        {
            await SaveManager.Instance.SaveRun(null);
            var saved = SaveManager.Instance.LoadRunSave().SaveData
                ?? throw new InvalidOperationException("Violet Lotus probe: disposable run save missing.");
            await game.ReturnToMainMenu();
            run = RunState.FromSerializable(saved);
            await RunManager.Instance.SetUpSavedSingleplayer(run, saved);
            await game.LoadRun(run, saved.PreFinishedRoom);
            await Task.Delay(1000);
            player = run.Players.Single();
            Require(Enchanted(player, ArchitectModels.CalmEnchantmentId).SequenceEqual(calm) &&
                Enchanted(player, ArchitectModels.WrathEnchantmentId).SequenceEqual(wrath),
                $"reload {reload} keeps the same Calm and Wrath cards");
        }
        MainFile.Logger.Info("NATIVE VIOLET LOTUS PASSED");
        game.GetTree().Quit();
    }

    private static string[] Enchanted(Player player, ModelId enchantment) =>
        player.Deck.Cards.Select((card, index) => (card, index))
            .Where(entry => entry.card.Enchantment?.Id == enchantment)
            .Select(entry => $"{entry.index}:{entry.card.Id.Entry}").ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Violet Lotus regression: " + message);
        MainFile.Logger.Info("NATIVE VIOLET LOTUS ASSERT: " + message);
    }
}
