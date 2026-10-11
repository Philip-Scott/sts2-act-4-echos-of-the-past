using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Playtest;

internal sealed record NativePartyHistory(string Seed, NativePartyHistory.Member[] Members)
{
    internal sealed record Member(string PlayerHistoryPath, string OpponentHistoryPath,
        int MaxEnergy, int BaseOrbSlotCount);

    internal static (NativePartyHistory Configuration, Player[] Players,
        CorruptedPlayerSnapshot[] Opponents, int Ascension) Load(string path, int count)
    {
        RequireRuntimePath(path);
        var config = JsonSerializer.Deserialize<NativePartyHistory>(File.ReadAllText(path)) ??
            throw new InvalidDataException("Party history configuration is empty.");
        if (string.IsNullOrWhiteSpace(config.Seed) || config.Members == null ||
            config.Members.Length != count)
            throw new InvalidDataException("Party history requires a seed and one history pair per player.");
        var players = new List<Player>();
        var opponents = new List<CorruptedPlayerSnapshot>();
        var histories = new HashSet<(string, long)>();
        var ascension = 0;
        foreach (var member in config.Members)
        {
            if (member == null || member.MaxEnergy <= 0 || member.BaseOrbSlotCount < 0)
                throw new InvalidDataException("Party history requires explicit, valid base resources.");
            RequireRuntimePath(member.PlayerHistoryPath);
            RequireRuntimePath(member.OpponentHistoryPath);
            var hero = ArchitectHistoryPlaytest.ReadVictory(member.PlayerHistoryPath);
            var opponent = ArchitectHistoryPlaytest.ReadVictory(member.OpponentHistoryPath);
            foreach (var victory in new[] { hero, opponent })
                if (!histories.Add((victory.History.Seed, victory.History.StartTime)))
                    throw new InvalidDataException("Choose distinct victories for all party history roles.");
            hero.ValidateLoadout();
            opponents.Add(opponent.CreateSnapshot());
            var character = ModelDb.GetById<CharacterModel>(hero.Player.Character);
            var save = Player.CreateForNewRun(character, UnlockState.all, (ulong)players.Count + 1)
                .ToSerializable();
            hero.RestoreLoadout(save, member.MaxEnergy, member.BaseOrbSlotCount);
            var player = Player.FromSerializable(save);
            AssertLoadout(player, save);
            if (players.Count == 0)
                ascension = hero.History.Ascension;
            players.Add(player);
        }
        return (config, players.ToArray(), opponents.ToArray(), ascension);
    }

    private static void RequireRuntimePath(string path)
    {
        if (!NativeDemoSafety.Enabled || string.IsNullOrWhiteSpace(path) ||
            !Path.IsPathFullyQualified(path) ||
            !Path.GetFullPath(path).StartsWith(NativeDemoSafety.RuntimePath + "/", StringComparison.Ordinal))
            throw new InvalidDataException("Party history inputs must be copies inside the isolated runtime.");
    }

    internal static void RestoreAfterInitialization(Player[] players, SerializablePlayer[] loadouts)
    {
        for (var index = 0; index < players.Length; index++)
        {
            var initialized = players[index].ToSerializable();
            loadouts[index].Rng = initialized.Rng;
            loadouts[index].Odds = initialized.Odds;
            loadouts[index].RelicGrabBag = initialized.RelicGrabBag;
            loadouts[index].ExtraFields = initialized.ExtraFields;
            players[index].SyncWithSerializedPlayer(loadouts[index]);
            players[index].BaseOrbSlotCount = loadouts[index].BaseOrbSlotCount;
            AssertLoadout(players[index], loadouts[index]);
        }
    }

    private static void AssertLoadout(Player player, SerializablePlayer expected)
    {
        var actual = player.ToSerializable();
        if (actual.CharacterId != expected.CharacterId || actual.CurrentHp != expected.CurrentHp ||
            actual.MaxHp != expected.MaxHp || actual.Gold != expected.Gold ||
            actual.MaxEnergy != expected.MaxEnergy || actual.BaseOrbSlotCount != expected.BaseOrbSlotCount ||
            actual.MaxPotionSlotCount != expected.MaxPotionSlotCount ||
            !Same(actual.Deck, expected.Deck) || !Same(actual.Relics, expected.Relics) ||
            !Same(actual.Potions, expected.Potions))
        {
            foreach (var entry in new[] { ("expected", expected), ("actual", actual) })
                File.WriteAllText(Path.Combine(NativeDemoSafety.RuntimePath,
                    $"player-{player.NetId}-{entry.Item1}.json"),
                    JsonSerializer.Serialize(entry.Item2, JsonSerializationUtility.GetTypeInfo<SerializablePlayer>()));
            throw new InvalidDataException("The restored party loadout does not match its victory history.");
        }
    }

    private static bool Same<T>(IReadOnlyList<T> actual, IReadOnlyList<T> expected) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair =>
            JsonNode.DeepEquals(Comparable(pair.First), Comparable(pair.Second)));

    private static JsonNode? Comparable<T>(T value)
    {
        var json = JsonSerializer.SerializeToNode(value, JsonSerializationUtility.GetTypeInfo<T>());
        // BaseLib writes an empty modifier list on loaded vanilla cards, even if history omitted it.
        if (json?["save_dict_List[BaseLib.Abstracts.CardModifier+ModifierSave]"] is JsonObject extension &&
            extension["BaseLibCardModifiers"] is JsonArray { Count: 0 })
            extension.Remove("BaseLibCardModifiers");
        return json;
    }

    internal void WriteReceipt(Player[] players, CorruptedPlayerSnapshot[] opponents, int ascension)
    {
        File.WriteAllText(Path.Combine(NativeDemoSafety.RuntimePath, "party-history-ready.json"),
            JsonSerializer.Serialize(new
            {
                Seed,
                Ascension = ascension,
                Saved = false,
                Players = players.Select(player => new
                {
                    Character = player.Character.Id.ToString(),
                    Cards = player.Deck.Cards.Count,
                    Relics = player.Relics.Count,
                    Hp = player.Creature.CurrentHp,
                    MaxHp = player.Creature.MaxHp,
                    player.Gold,
                    player.MaxEnergy,
                    player.BaseOrbSlotCount
                }),
                Opponents = opponents.Select(snapshot => new
                {
                    Character = snapshot.CharacterId,
                    Cards = snapshot.Deck.Length,
                    snapshot.MaxHp,
                    snapshot.ContentHash
                })
            }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
