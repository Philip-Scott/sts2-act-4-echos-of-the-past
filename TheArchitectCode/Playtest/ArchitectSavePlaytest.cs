using System.IO;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using TheArchitect.TheArchitectCode.Ancients;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Playtest;

// Python owns deck composition and file replacement. These hooks only cross into the native runtime.
[HarmonyPatch(typeof(NGame), "LaunchMainMenu")]
internal static class ArchitectSavePlaytest
{
    private static bool _started;
    private sealed record Request(int ProfileId, string OutputDirectory);
    private sealed record Template(string Seed, int Ascension, string[] Characters);

    private static void Postfix(NGame __instance, Task __result)
    {
        if (_started || !CommandLineHelper.HasArg("architect-save-playtest"))
            return;
        _started = true;
        TaskHelper.RunSafely(Resume(__instance, __result));
    }

    private static async Task Resume(NGame game, Task menuReady)
    {
        await menuReady;
        var path = CommandLineHelper.GetValue("architect-save-playtest");
        if (path == null || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Save playtest requires an absolute request path.");
        var request = JsonSerializer.Deserialize<Request>(File.ReadAllText(path)) ??
            throw new InvalidDataException("Save playtest request is empty.");
        if (!Path.IsPathFullyQualified(request.OutputDirectory))
            throw new InvalidDataException("Save playtest output must be an absolute path.");
        try
        {
            var saves = SaveManager.Instance;
            if (NativeDemoSafety.Enabled || request.ProfileId is < 1 or > 3 ||
                saves.CurrentProfileId != request.ProfileId || saves.HasMultiplayerRunSave)
                throw new InvalidOperationException("Save playtest requires the selected solo profile.");
            var save = saves.LoadRunSave().SaveData ??
                throw new InvalidDataException("The prepared save is missing.");
            if (save.Players.Count != 1 || save.CurrentActIndex != 3)
                throw new InvalidDataException("Live save playtests require one player in Act 4.");
            var continueRun = AccessTools.MethodDelegate<Func<NMainMenu, Task>>(
                AccessTools.Method(typeof(NMainMenu), "OnContinueButtonPressedAsync"));
            await continueRun(game.MainMenu ??
                throw new InvalidOperationException("The native Continue menu is unavailable."));
            var run = RunManager.Instance.DebugOnlyGetState() ??
                throw new InvalidOperationException("Continue did not load the prepared run.");
            await Arrive(game, run, request.OutputDirectory, saved: true);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(request.OutputDirectory);
            File.WriteAllText(Path.Combine(request.OutputDirectory, "error.txt"), error.ToString());
            MainFile.Logger.Error($"SAVE PLAYTEST FAILED: {error}");
            throw;
        }
    }

    internal static async Task ExportTemplate(NGame game)
    {
        RequireIsolation();
        var root = NativeDemoSafety.RuntimePath;
        var config = JsonSerializer.Deserialize<Template>(File.ReadAllText(Path.Combine(root, "save-template.json"))) ??
            throw new InvalidDataException("Template request is empty.");
        if (string.IsNullOrWhiteSpace(config.Seed) || config.Ascension is < 0 or > 10 ||
            config.Characters == null || config.Characters.Length is < 1 or > 4)
            throw new InvalidDataException("Template requires a seed, Ascension from 0 to 10, and one to four characters.");
        var players = config.Characters.Select((id, index) => Player.CreateForNewRun(
            ModelDb.GetById<CharacterModel>(ModelId.Deserialize(id)), UnlockState.all, (ulong)index + 1)).ToArray();
        var run = RunState.CreateForNewRun(players,
            [ModelDb.Act<Overgrowth>().ToMutable(), ModelDb.Act<Hive>().ToMutable(), ModelDb.Act<Glory>().ToMutable()],
            [], GameMode.Standard, config.Ascension, config.Seed);
        var manager = RunManager.Instance;
        manager.SetUpTest(run, new NetSingleplayerGameService(), shouldSave: false);
        manager.GenerateRooms();
        ArchitectLifecycle.AppendAct(run);
        var state = ArchitectRun.Get(run);
        state.Entered = true;
        state.PlaytestOpponents = [CorruptedPlayerSnapshot.Capture(players[0])];
        await PreloadManager.LoadRunAssets(players.Select(player => player.Character).Distinct());
        manager.Launch();
        game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
        await manager.EnterAct(3, doTransition: false);
        File.WriteAllText(Path.Combine(root, "template.save"), SaveManager.ToJson(manager.ToSave(null)));
        File.WriteAllText(Path.Combine(root, "models.json"), JsonSerializer.Serialize(
            ModelDb.All.Select(model => model.Id.ToString()).Distinct().Order().ToArray()));
        MainFile.Logger.Info("SAVE TEMPLATE READY");
        game.GetTree().Quit();
    }

    internal static async Task LoadFixture(NGame game)
    {
        RequireIsolation();
        var save = SaveManager.FromJson<SerializableRun>(
            File.ReadAllText(Path.Combine(NativeDemoSafety.RuntimePath, "save-input.json"))).SaveData ??
            throw new InvalidDataException("Cannot deserialize the Python-prepared save.");
        if (save.Players.Count is < 1 or > 4 || save.CurrentActIndex != 3)
            throw new InvalidDataException("A local fixture requires one to four players in Act 4.");
        var run = RunState.FromSerializable(save);
        var state = ArchitectRun.Get(run);
        state.ValidatePlaytestOpponents();
        if (state.PlaytestOpponents == null)
            throw new InvalidDataException("The fixture has no scripted opponents.");
        var manager = RunManager.Instance;
        await manager.SetUpSavedSingleplayer(run, save);
        manager.CombatStateSynchronizer.IsDisabled = true;
        await game.LoadRun(run, save.PreFinishedRoom);
        for (var index = 0; index < run.Players.Count; index++)
            NativePartyHistory.AssertLoadout(run.Players[index], save.Players[index]);
        await Arrive(game, run, NativeDemoSafety.RuntimePath, saved: false, receiptName: "starting.json");
        var serialized = SaveManager.ToJson(manager.ToSave(null));
        var roundTrip = SaveManager.FromJson<SerializableRun>(serialized).SaveData ??
            throw new InvalidDataException("The scripted save could not be read after serialization.");
        var restored = RunState.FromSerializable(roundTrip);
        if (!ArchitectRun.Get(restored).EncounterSnapshots.Select(snapshot => snapshot.ContentHash)
            .SequenceEqual(state.EncounterSnapshots.Select(snapshot => snapshot.ContentHash)))
            throw new InvalidDataException("Scripted opponents did not survive the save roundtrip.");
        if (!NativeDemoSafety.SharedVisible)
        {
            await game.ReturnToMainMenu();
            run = restored;
            state = ArchitectRun.Get(run);
            await manager.SetUpSavedSingleplayer(run, roundTrip);
            manager.CombatStateSynchronizer.IsDisabled = true;
            await game.LoadRun(run, roundTrip.PreFinishedRoom);
            for (var index = 0; index < run.Players.Count; index++)
                NativePartyHistory.AssertLoadout(run.Players[index], save.Players[index]);
            await Arrive(game, run, NativeDemoSafety.RuntimePath, saved: false, receiptName: "starting.json");
        }
        // Match the existing fake-multiplayer inspection: start at the fight, without choosing gifts.
        await ArchitectPlaytest.EnterCorruptedElite(run);
        await NativeDemoPlaytest.PlayerTurn(run.Players[0], 1);
        var enemies = NativeCorruptedPlayer.In(run.Players[0].Creature.CombatState!).ToArray();
        if (enemies.Length != state.EncounterSnapshots.Count || enemies.Any(enemy => !enemy.HandPrepared))
            throw new InvalidDataException("The scripted Corrupted Players did not enter combat.");
        foreach (var enemy in enemies)
        {
            var expected = state.EncounterSnapshots[((CorruptedPlayer)enemy.Body.Monster!).FormationIndex];
            if (enemy.Player.Character.Id.ToString() != expected.CharacterId ||
                enemy.Player.Deck.Cards.Count != expected.Deck.Length ||
                enemy.Body.MaxHp != CorruptedPlayerHealth.CalculateMaxHp(expected.MaxHp, run.AscensionLevel))
                throw new InvalidDataException("The scripted enemy does not match its deck.");
        }
        File.Copy(Path.Combine(NativeDemoSafety.RuntimePath, "starting.json"),
            Path.Combine(NativeDemoSafety.RuntimePath, "ready.json"));
        if (!NativeDemoSafety.SharedVisible)
        {
            File.WriteAllText(Path.Combine(NativeDemoSafety.RuntimePath, "verified.json"), serialized);
            game.GetTree().Quit();
        }
    }

    private static async Task Arrive(NGame game, RunState run, string output, bool saved,
        string receiptName = "ready.json")
    {
        var state = ArchitectRun.Get(run);
        state.ValidatePlaytestOpponents();
        foreach (var snapshot in state.EncounterSnapshots)
            if (NativeCardSupport.Preflight(snapshot.Deck) is { } error)
                throw new InvalidDataException(error);
        if (run.CurrentRoom is not EventRoom { CanonicalEvent: TheUnwritten })
            await RunManager.Instance.EnterMapCoord(run.Map.StartingMapPoint.coord);
        if (run.CurrentRoom is not EventRoom { LocalMutableEvent: TheUnwritten ancient } ||
            ancient.CurrentOptions.Count != 3)
            throw new InvalidOperationException("The prepared run did not arrive at The Watcher.");
        if (saved)
            await SaveManager.Instance.SaveRun(null);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, receiptName), JsonSerializer.Serialize(new
        {
            Saved = saved, Profile = saved ? SaveManager.Instance.CurrentProfileId : (int?)null,
            Visible = game.GetWindow().Visible,
            Location = saved ? "Watcher" : "CorruptedPlayer",
            Seed = run.Rng.StringSeed, Ascension = run.AscensionLevel,
            Players = run.Players.Select(player => new
            {
                Character = player.Character.Id.ToString(), Cards = player.Deck.Cards.Count,
                Relics = player.Relics.Count, Hp = player.Creature.CurrentHp, MaxHp = player.Creature.MaxHp,
                player.Gold, player.MaxEnergy, player.BaseOrbSlotCount
            }),
            Opponents = state.EncounterSnapshots.Select(snapshot => new
            {
                Character = snapshot.CharacterId, Cards = snapshot.Deck.Length, snapshot.MaxHp, snapshot.ContentHash
            }),
            Offers = ancient.CurrentOptions.Select(option => option.Relic!.Id.ToString()).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        MainFile.Logger.Info("SAVE PLAYTEST ARRIVAL: natural Watcher offers, no selection made.");
    }

    private static void RequireIsolation()
    {
        if (!NativeDemoSafety.Enabled)
            throw new InvalidOperationException("Template export and fake multiplayer require the isolated launcher.");
    }

    [HarmonyPatch(typeof(AncientEventModel), "BeforeEventStarted")]
    private static class PreserveRequestedHealth
    {
        private static bool Prefix(AncientEventModel __instance, ref Task __result)
        {
            if (__instance is not TheUnwritten || RunManager.Instance.DebugOnlyGetState() is not { } run ||
                ArchitectRun.Get(run).PlaytestOpponents == null)
                return true;
            // Keep YAML arrival HP exact, including when Continue reloads this Ancient.
            __result = Task.CompletedTask;
            return false;
        }
    }
}
