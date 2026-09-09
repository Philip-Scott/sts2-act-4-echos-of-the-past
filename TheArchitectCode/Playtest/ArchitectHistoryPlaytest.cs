using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.Ancients;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.Relics;

namespace TheArchitect.TheArchitectCode.Playtest;

[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
internal static class ArchitectHistoryPlaytest
{
    private static bool _started;
    private static bool Enabled => CommandLineHelper.HasArg("architect-history-setup");
    private static bool ResumeEnabled => CommandLineHelper.HasArg("architect-resume-slot2");
    private static readonly Lazy<Setup> Configuration = new(() =>
    {
        var path = CommandLineHelper.GetValue("architect-history-setup");
        if (path == null || !Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("History setup requires an absolute configuration file path.");
        var setup = JsonSerializer.Deserialize<Setup>(File.ReadAllText(path)) ??
            throw new InvalidDataException("History setup configuration is empty.");
        if (setup.ProfileId != 2 || setup.MaxEnergy <= 0 || setup.BaseOrbSlotCount < 0 ||
            !Path.IsPathFullyQualified(setup.OutputDirectory))
            throw new InvalidDataException("History setup is restricted to Slot 2 and valid base resources.");
        return setup;
    });

    private static void Postfix(NGame __instance, Task __result)
    {
        if ((!Enabled && !ResumeEnabled) || _started)
            return;
        _started = true;
        TaskHelper.RunSafely(ResumeEnabled ? ResumeRun(__instance, __result) : PrepareRun(__instance, __result));
    }

    private static async Task ResumeRun(NGame game, Task menuReady)
    {
        await menuReady;
        if (SaveManager.Instance.CurrentProfileId != 2 || NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Live resume requires the selected native Slot 2 profile.");
        var save = SaveManager.Instance.LoadRunSave().SaveData ??
            throw new InvalidDataException("Slot 2 has no saved run to resume.");
        if (save.Players.Count != 1)
            throw new InvalidDataException("Live resume only supports single-player saves.");
        var run = RunState.FromSerializable(save);
        await RunManager.Instance.SetUpSavedSingleplayer(run, save);
        await game.LoadRun(run, save.PreFinishedRoom);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var player = run.Players.Single();
        while (run.CurrentRoom is CombatRoom combat &&
            (CombatManager.Instance.IsStarting || player.PlayerCombatState?.Phase != PlayerTurnPhase.Play ||
                NativeCorruptedPlayer.In(combat.CombatState).Any(actor => !actor.HandPrepared)))
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The resumed Slot 2 combat did not become ready.");
            await game.AwaitProcessFrame();
        }
        MainFile.Logger.Info($"LIVE RESUME READY: profile=2, deck={player.Deck.Cards.Count}, " +
            $"relics={player.Relics.Count}, hand={player.PlayerCombatState?.Hand.Cards.Count}, " +
            $"opponentHands={string.Join(",", run.CurrentRoom is CombatRoom room
                ? NativeCorruptedPlayer.In(room.CombatState).Select(actor => actor.State.Hand.Cards.Count)
                : [])}.");
    }

    private static async Task PrepareRun(NGame game, Task menuReady)
    {
        await menuReady;
        var setup = Configuration.Value;
        var saves = SaveManager.Instance;
        if (saves.CurrentProfileId != setup.ProfileId || saves.HasRunSave || saves.HasMultiplayerRunSave)
            throw new InvalidOperationException("History setup requires empty Slot 2; existing runs are never overwritten.");
        if (NativeDemoSafety.Enabled)
            throw new InvalidOperationException("History setup cannot use the in-memory native demo save store.");

        var hero = ReadVictory(setup.PlayerHistoryPath);
        var opponent = ReadVictory(setup.OpponentHistoryPath);
        if (hero.History.StartTime == opponent.History.StartTime && hero.History.Seed == opponent.History.Seed)
            throw new InvalidDataException("Choose two different victories.");
        var snapshotCards = opponent.Player.Deck.Select(card => JsonSerializer.SerializeToElement(card,
            JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
        if (NativeCardSupport.Preflight(snapshotCards) is { } error)
            throw new InvalidDataException("Opponent deck cannot be restored: " + error);
        foreach (var card in hero.Player.Deck)
            _ = CardModel.FromSerializable(card);
        foreach (var relic in hero.Player.Relics)
            _ = RelicModel.FromSerializable(relic);

        Directory.CreateDirectory(setup.OutputDirectory);
        var character = ModelDb.GetById<CharacterModel>(hero.Player.Character);
        var ancient = ModelDb.GetById<TheUnwritten>(new ModelId("EVENT", "THEARCHITECT-THE_UNWRITTEN"));
        var seed = setup.ForceMirror ? FindMirrorSeed(setup.Seed, ancient.Id.Entry) : setup.Seed;
        var run = await game.StartNewSingleplayerRun(character, true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], seed, GameMode.Standard, hero.History.Ascension);

        var save = RunManager.Instance.ToSave(null);
        var restored = save.Players.Single();
        restored.Deck = hero.Player.Deck.ToList();
        restored.Relics = hero.Player.Relics.ToList();
        restored.Potions = hero.Player.Potions.ToList();
        restored.MaxPotionSlotCount = hero.Player.MaxPotionSlotCount;
        restored.MaxHp = hero.MaxHp;
        restored.CurrentHp = hero.CurrentHp;
        restored.Gold = hero.Gold;
        restored.MaxEnergy = setup.MaxEnergy;
        restored.BaseOrbSlotCount = setup.BaseOrbSlotCount;
        await game.ReturnToMainMenu();
        run = RunState.FromSerializable(save);
        await RunManager.Instance.SetUpSavedSingleplayer(run, save);
        await game.LoadRun(run, save.PreFinishedRoom);

        var snapshot = new CorruptedPlayerSnapshot(opponent.Player.Character.ToString(), opponent.MaxHp,
            snapshotCards, CorruptedPlayerSnapshot.Hash(opponent.Player.Character.ToString(), opponent.MaxHp, snapshotCards));
        if (CorruptedPlayerStore.Commit("history-setup-" + opponent.History.StartTime, "HistoryVictory", snapshot) == null)
            throw new IOException("Could not persist Slot 2's Corrupted Player.");
        var player = run.Players.Single();
        if (setup.ForceMirror && !HandheldMirror.CanOffer(player))
            throw new InvalidDataException("The selected victory does not own three distinct Mirror source relics.");
        ArchitectLifecycle.AppendAct(run);
        await RunManager.Instance.EnterAct(3);
        if (run.CurrentRoom is not EventRoom { CanonicalEvent: TheUnwritten })
            await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        if (run.CurrentRoom is not EventRoom { LocalMutableEvent: TheUnwritten unwritten } ||
            unwritten.CurrentOptions.Count != 3 ||
            (setup.ForceMirror && unwritten.CurrentOptions[2].Relic is not HandheldMirror))
            throw new InvalidOperationException("History setup did not produce the requested Ancient offers.");
        if (player.Deck.Cards.Count != restored.Deck.Count || player.Relics.Count != restored.Relics.Count ||
            ArchitectRun.Get(run).EntrySnapshot?.Snapshot?.ContentHash != snapshot.ContentHash)
            throw new InvalidOperationException("The prepared deck, relics, or opponent does not match its history.");

        saves.SaveProfile();
        await saves.SaveRun(null);
        File.WriteAllText(Path.Combine(setup.OutputDirectory, "ready.json"), JsonSerializer.Serialize(new
        {
            Profile = saves.CurrentProfileId,
            Player = player.Character.Id.ToString(),
            Cards = player.Deck.Cards.Count,
            Relics = player.Relics.Count,
            Hp = player.Creature.CurrentHp,
            MaxHp = player.Creature.MaxHp,
            Gold = player.Gold,
            Seed = seed,
            setup.ForceMirror,
            Opponent = snapshot.CharacterId,
            OpponentCards = snapshot.Deck.Length,
            OpponentHp = snapshot.MaxHp,
            Offers = unwritten.CurrentOptions.Select(option => option.Relic!.Id.ToString()).ToArray(),
            SaveDirectory = ProjectSettings.GlobalizePath(saves.GetProfileScopedPath("saves"))
        }, new JsonSerializerOptions { WriteIndented = true }));
        MainFile.Logger.Info($"HISTORY SETUP READY: Slot 2, original victory loadout, saved opponent, " +
            $"and {(setup.ForceMirror ? "Mirror offered" : "natural Ancient offers")}.");
    }

    private static string FindMirrorSeed(string prefix, string ancientEntry)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var seed = $"{prefix}-{attempt}";
            var rng = new Rng(new RunRngSet(seed).Seed, ancientEntry);
            rng.NextInt(4);
            rng.NextInt(2);
            if (rng.NextInt(2) == 1)
                return seed;
        }
        throw new InvalidOperationException("Could not find a native seed offering Mirror.");
    }

    private static Victory ReadVictory(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Victory history paths must be absolute.");
        var json = File.ReadAllText(path);
        var history = SaveManager.FromJson<RunHistory>(json).SaveData ??
            throw new InvalidDataException("Could not deserialize victory history: " + path);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.GetProperty("win").GetBoolean() || root.GetProperty("was_abandoned").GetBoolean() ||
            history.Players.Count != 1)
            throw new InvalidDataException("History setup requires completed single-player victories.");
        var stats = root.GetProperty("map_point_history").EnumerateArray()
            .SelectMany(act => act.EnumerateArray())
            .SelectMany(point => point.GetProperty("player_stats").EnumerateArray()).Last();
        var maxHp = stats.GetProperty("max_hp").GetInt32();
        var hp = stats.GetProperty("current_hp").GetInt32();
        if (maxHp <= 0 || hp <= 0 || hp > maxHp)
            throw new InvalidDataException("The victory has invalid terminal health.");
        return new Victory(history, history.Players.Single(), maxHp, hp, stats.GetProperty("current_gold").GetInt32());
    }

    private sealed record Victory(RunHistory History, RunHistoryPlayer Player, int MaxHp, int CurrentHp, int Gold);
    private sealed record Setup(int ProfileId, string PlayerHistoryPath, string OpponentHistoryPath,
        string OutputDirectory, string Seed, int MaxEnergy, int BaseOrbSlotCount, bool ForceMirror = true);

}
