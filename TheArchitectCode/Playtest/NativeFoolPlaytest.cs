using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.addons.mega_text;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.UI;

namespace TheArchitect.TheArchitectCode.Playtest;

// A Corrupted Fool: its corruption buff, arrow HUD and every Fool card, base and upgraded,
// played through the native actor's turn.
internal static class NativeFoolPlaytest
{
    internal static void CommitSnapshot()
    {
        var character = ModelDb.Character<Fool>();
        var cards = character.StartingDeck.Select(card => JsonSerializer.SerializeToElement(
            card.ToMutable().ToSerializable(), JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
        var id = character.Id.ToString();
        CorruptedPlayerStore.Commit("fool-probe", "Probe", new CorruptedPlayerSnapshot(id, character.StartingHp, cards,
            CorruptedPlayerSnapshot.Hash(id, character.StartingHp, cards)));
    }

    internal static async Task Run(NGame game, Player human, NativeCorruptedPlayer actor)
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Fool probes require the disposable smoke environment.");
        Require(actor.Player.Character is Fool, "the saved Fool is restored");
        Require(actor.Body.HasPower<DiscordantBowPower>(), "the Corrupted Fool carries Discordant Bow");
        Require(actor.Body.GetPowerAmount<AimPower>() >= 12, "Discordant Bow grants Aim at combat start");
        Require(actor.State.Arrows.Count == ModelDb.Character<Fool>().BaseArrowSlotCount,
            "the private player starts with the Fool's arrow slots");
        var arrows = CorruptedPlayerTelegraph.Of(actor.Body.GetCreatureNode()!).StatusIcon("Arrows")
            ?? throw new InvalidOperationException("Fool probe: the arrow icon is missing.");
        async Task RequireArrowIcon(string stage)
        {
            await game.AwaitProcessFrame();
            Require(arrows.IsVisibleInTree() &&
                arrows.GetNode<MegaLabel>("AmountLabel").Text == actor.State.AvailableArrowCount.ToString(),
                $"the arrow icon shows the ready arrows ({stage})");
        }
        await RequireArrowIcon("combat start");

        // Over 160 native turns follow; skip their animations.
        var fastMode = SaveManager.Instance.PrefsSave.FastMode;
        SaveManager.Instance.PrefsSave.FastMode = FastModeType.Instant;
        var combat = human.Creature.CombatState!;
        actor.Player.MaxEnergy = 10;
        actor.State.ResetEnergy();
        var played = new HashSet<ModelId>();
        void Played(CardModel card) => played.Add(card.Id);
        actor.CardPlayed += Played;
        try
        {
            var pool = ModelDb.CardPool<FoolCardPool>().AllCards.ToArray();
            foreach (var upgraded in new[] { false, true })
            {
                var cards = pool.Where(card => !upgraded || card.IsUpgradable).ToArray();
                for (var start = 0; start < cards.Length; start += 5)
                {
                    await CreatureCmd.SetMaxHp(human.Creature, 1000);
                    await CreatureCmd.Heal(human.Creature, 1000);
                    await CreatureCmd.Heal(actor.Body, actor.Body.MaxHp);
                    foreach (var pile in actor.State.AllPiles)
                        foreach (var card in pile.Cards.ToArray())
                        {
                            pile.RemoveInternal(card);
                            combat.RemoveCard(card);
                        }
                    foreach (var canonical in cards.Skip(start).Take(5))
                    {
                        var card = combat.CreateCard(canonical, actor.Player);
                        if (upgraded)
                        {
                            card.UpgradeInternal();
                            card.FinalizeUpgradeInternal();
                        }
                        actor.State.Hand.AddInternal(card);
                    }
                    var turn = human.PlayerCombatState!.TurnNumber;
                    var completed = actor.CompletedTurns;
                    PlayerCmd.EndTurn(human, false);
                    await NativeDemoPlaytest.PlayerTurn(human, turn + 1);
                    actor.AssertIdentity();
                    Require(actor.CompletedTurns > completed && actor.Body.IsAlive && human.Creature.IsAlive,
                        $"batch {start / 5} ({(upgraded ? "upgraded" : "base")}) finished a native turn");
                    Require(combat.Players.Count == 1, "the actor never joins the human party");
                    await RequireArrowIcon($"batch {start / 5}");
                }
            }
            var missing = pool.Select(card => card.Id).Where(id => !played.Contains(id)).ToArray();
            MainFile.Logger.Info($"NATIVE FOOL played {played.Count}/{pool.Length} Fool cards; never played: " +
                string.Join(",", missing.Select(id => id.Entry)));
            Require(played.Count >= pool.Length / 2, "most Fool cards are playable by the Corrupted Player");
        }
        finally
        {
            actor.CardPlayed -= Played;
            SaveManager.Instance.PrefsSave.FastMode = fastMode;
        }
        MainFile.Logger.Info("NATIVE FOOL PASSED");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Native Fool regression: " + message);
        MainFile.Logger.Info("NATIVE FOOL ASSERT: " + message);
    }
}
