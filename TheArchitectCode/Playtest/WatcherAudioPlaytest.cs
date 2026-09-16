using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using TheArchitect.TheArchitectCode.Ancients;
using TheArchitect.TheArchitectCode.Audio;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class WatcherAudioPlaytest
{
    private static readonly PlayerChoiceContext Context = new ThrowingPlayerChoiceContext();
    private static AudioEffectRecord _recording = null!;
    private static AudioEffectCapture _capture = null!;
    private static int _master;

    internal static void Begin(NGame game)
    {
        Check(NativeDemoSafety.Enabled && !NativeDemoSafety.SharedVisible &&
            AudioServer.GetDriverName() == "PulseAudio" &&
            System.Environment.GetEnvironmentVariable("PULSE_SERVER") == "unix:/tmp/watcher-audio.sock",
            "real PulseAudio driver uses only the disposable namespace's null sink");
        foreach (var sound in Enum.GetValues<WatcherSound>())
        {
            var stream = WatcherAudio.Load(sound);
            var loop = sound is WatcherSound.CalmLoop or WatcherSound.WrathLoop or WatcherSound.DivinityLoop;
            Check(stream.Loop == loop && stream.LoopOffset == 0 &&
                (loop ? stream.GetLength() is > 10 and < 10.1 : stream.GetLength() is > 2.5 and < 2.9),
                $"packaged {sound}: decoded original Ogg, length={stream.GetLength():F6}, loop={loop}");
        }
        Check(WatcherAudio.Instance is { CueCount: 0 } audio &&
            Enum.GetValues<WatcherSound>().All(sound => audio.LoopCount(sound) == 0),
            "asset loading and run creation do not play cues or ambience");
        game.DebugAudio.SetMasterAudioVolume(1);
        game.DebugAudio.SetSfxAudioVolume(1);
        SaveManager.Instance.PrefsSave.MuteInBackground = false;
        _master = AudioServer.GetBusIndex("Master");
        _recording = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
        _capture = new AudioEffectCapture { BufferLength = 1 };
        AudioServer.AddBusEffect(_master, _recording);
        AudioServer.AddBusEffect(_master, _capture);
        _recording.SetRecordingActive(true);
    }

    internal static async Task Ancient(NGame game, Player local)
    {
        if (local.RunState.CurrentRoom is not EventRoom { CanonicalEvent: TheUnwritten })
            await RunManager.Instance.EnterMapCoord(local.RunState.Map.StartingMapPoint.coord);
        var audio = Current();
        Check(audio.LoopCount(WatcherSound.DivinityLoop) == 1 && audio.CueCount == 1,
            $"Watcher arrival plays Mantra and exactly one Divinity loop (loops={audio.LoopCount(WatcherSound.DivinityLoop)}, cues={audio.CueCount})");
        var loop = Loop(WatcherSound.DivinityLoop);
        await UnwrittenPlaytest.ChooseBuildGift(local);
        Check(NMapScreen.Instance?.IsOpen == true && audio.LoopCount(WatcherSound.DivinityLoop) == 1 &&
            Loop(WatcherSound.DivinityLoop) == loop, "gift and map preserve the same Ancient loop");
        await Task.Delay(10500);
        Check(loop.Playing && loop.GetPlaybackPosition() < WatcherAudio.Load(WatcherSound.DivinityLoop).GetLength(),
            "Divinity survives its original Ogg boundary without a second voice");
        await Samples(audible: true, "Divinity background produces real nonzero PCM");
        await NativeDemoPlaytest.Capture("watcher-audio-map");
    }

    internal static async Task Combat(NGame game, Player[] players, NativeCorruptedPlayer[] actors)
    {
        var audio = Current();
        Check(audio.LoopCount(WatcherSound.DivinityLoop) == 0 && audio.CueCount == 0,
            "entering the next floor stops all Ancient audio");
        var local = players.Single(player => player.NetId == MegaCrit.Sts2.Core.Runs.RunManager.Instance.NetService.NetId);
        var remote = players.Single(player => player != local);
        var localCard = local.Deck.Cards[0];
        var remoteCard = remote.Deck.Cards[0];
        var corruptedCard = actors[0].Cards[0];
        await WatcherStances.EnterCalm(Context, remoteCard);
        Check(audio.CueCount == 1 && audio.LoopCount(WatcherSound.CalmLoop) == 0,
            "remote true entry plays a cue, not a loop");
        await WatcherStances.EnterCalm(Context, localCard);
        Check(audio.CueCount == 2 && audio.LoopCount(WatcherSound.CalmLoop) == 0,
            "local true entry has its own cue; its loop waits for that cue");
        await WatcherStances.EnterCalm(Context, localCard);
        Check(audio.CueCount == 2, "same-stance reentry does not replay or coalesce other owners' cues");
        await WaitFor(() => audio.LoopCount(WatcherSound.CalmLoop) == 1,
            "local Calm cue completion");
        Check(audio.LoopCount(WatcherSound.CalmLoop) == 1, "only one local Calm loop starts after the cue");
        var calmLoop = Loop(WatcherSound.CalmLoop);
        await WatcherStances.EnterWrath(Context, corruptedCard);
        Check(audio.CueCount == 1, "a Corrupted Player's true Wrath entry plays its cue");
        await WaitFor(() => audio.CueCount == 0, "Corrupted Wrath cue completion");
        Check(audio.LoopCount(WatcherSound.CalmLoop) == 1 && audio.LoopCount(WatcherSound.WrathLoop) == 0,
            "Corrupted Wrath does not create a background loop over local Calm");
        await Task.Delay(10500);
        Check(Loop(WatcherSound.CalmLoop) == calmLoop && calmLoop.Playing &&
            calmLoop.GetPlaybackPosition() < WatcherAudio.Load(WatcherSound.CalmLoop).GetLength(),
            "local Calm continuously loops through its Ogg boundary on the original voice");
        await Samples(true, "Calm loop produces nonzero PCM after its first wrap");
        await WatcherStances.EnterCalm(Context, localCard);
        Check(audio.CueCount == 0 && Loop(WatcherSound.CalmLoop) == calmLoop,
            "same-stance reentry preserves the playing loop without a cue");

        game.DebugAudio.SetSfxAudioVolume(0);
        await Samples(false, "native SFX mute silences the recorded loop without discarding its owner");
        Check(Loop(WatcherSound.CalmLoop) == calmLoop, "SFX mute retains loop identity");
        game.DebugAudio.SetSfxAudioVolume(1);
        await Samples(true, "native SFX unmute restores the existing loop");
        game.DebugAudio.SetMasterAudioVolume(0);
        Check(AudioServer.GetBusVolumeDb(_master) < -70 && calmLoop.Playing,
            "native master mute routes through the master bus, not a custom gain");
        game.DebugAudio.SetMasterAudioVolume(1);
        game.GetTree().Paused = true;
        await Task.Delay(150);
        var pausedPosition = calmLoop.GetPlaybackPosition();
        await Task.Delay(250);
        Check(calmLoop.StreamPaused && Math.Abs(calmLoop.GetPlaybackPosition() - pausedPosition) < 0.05,
            "tree pause freezes playback rather than restarting a loop");
        game.GetTree().Paused = false;
        await FocusMute(game, calmLoop);

        await WatcherStances.EnterWrath(Context, localCard);
        Check(audio.LoopCount(WatcherSound.CalmLoop) == 0 && audio.LoopCount(WatcherSound.WrathLoop) == 0,
            "leaving Calm stops it even while the remote player remains Calm");
        await WaitFor(() => audio.LoopCount(WatcherSound.WrathLoop) == 1, "local Wrath cue completion");
        Check(audio.LoopCount(WatcherSound.WrathLoop) == 1, "local Wrath begins after its entry cue");
        var wrathLoop = Loop(WatcherSound.WrathLoop);
        await Task.Delay(10500);
        Check(Loop(WatcherSound.WrathLoop) == wrathLoop && wrathLoop.Playing &&
            wrathLoop.GetPlaybackPosition() < WatcherAudio.Load(WatcherSound.WrathLoop).GetLength(),
            "local Wrath loops through its Ogg boundary without duplicate voices");
        await Samples(true, "Wrath loop produces nonzero PCM after its first wrap");
        local.Creature.GetPower<WrathStancePower>()!.RemoveInternal();
        Check(audio.LoopCount(WatcherSound.WrathLoop) == 0, "power removal synchronously stops local Wrath");
        await WatcherStances.EnterCalm(Context, localCard);
        local.Creature.SetCurrentHpInternal(0);
        await WaitFor(() => audio.CueCount == 0, "dead owner's cue completion");
        Check(audio.LoopCount(WatcherSound.CalmLoop) == 0, "death before cue completion cannot start an orphan loop");
        local.Creature.GetPower<CalmStancePower>()!.RemoveInternal();
        local.Creature.SetCurrentHpInternal(local.Creature.MaxHp);
        game.DebugAudio.SetSfxAudioVolume(0);
        await WatcherStances.EnterWrath(Context, localCard);
        await WaitFor(() => audio.LoopCount(WatcherSound.WrathLoop) == 1, "revived owner's new Wrath entry");
        await Samples(false, "entering while SFX-muted retains a silent but live loop");
        game.DebugAudio.SetSfxAudioVolume(1);
        await Samples(true, "unmuting after a muted stance entry restores its loop");
        local.Creature.SetCurrentHpInternal(0);
        Check(audio.LoopCount(WatcherSound.WrathLoop) == 0,
            "local death stops a live loop even while a Corrupted Player remains Wrath");
        local.Creature.GetPower<WrathStancePower>()!.RemoveInternal();
        local.Creature.SetCurrentHpInternal(local.Creature.MaxHp);
        await WatcherStances.EnterCalm(Context, localCard);
        await WatcherStances.EnterWrath(Context, localCard);
        await WaitFor(() => audio.CueCount == 0, "rapid stance switches");
        Check(audio.LoopCount(WatcherSound.CalmLoop) == 0 && audio.LoopCount(WatcherSound.WrathLoop) == 1,
            "an old cue completing after a rapid switch cannot start the wrong stance loop");
        local.Creature.GetPower<WrathStancePower>()!.RemoveInternal();
        await WatcherStances.EnterCalm(Context, localCard);
        CombatManager.Instance.Reset(graceful: false);
        Check(audio.OwnerCount == 0 && audio.CueCount == 0, "combat reset synchronously stops pending cues and ownership");
        await Task.Delay(3200);
        Check(audio.LoopCount(WatcherSound.CalmLoop) == 0, "a reset combat cannot restart delayed loops");
        // Use a real room transition after the reset, then tear down a still-playing Ancient.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ArchitectModels.Ancient);
        Check(audio.LoopCount(WatcherSound.DivinityLoop) == 1, "a subsequent real Ancient room starts its own ambience");
        await game.ReturnToMainMenu();
        Check(WatcherAudio.Instance == null && audio.OwnerCount == 0 && audio.CueCount == 0,
            "run/menu teardown releases the controller and all owner subscriptions");
        await Samples(false, "teardown leaves no orphan Watcher PCM");
        _recording.SetRecordingActive(false);
        var wav = _recording.GetRecording();
        Check(wav.Data.Any(value => value != 0), "PCM recording is nonempty and not a silent Dummy-driver result");
        var destination = System.IO.Path.Combine(NativeDemoSafety.RuntimePath, "watcher-audio.wav");
        Check(wav.SaveToWav(destination) == Error.Ok, "saved private PCM evidence: " + destination);
        AudioServer.RemoveBusEffect(_master, AudioServer.GetBusEffectCount(_master) - 1);
        AudioServer.RemoveBusEffect(_master, AudioServer.GetBusEffectCount(_master) - 1);
    }

    private static WatcherAudio Current() => WatcherAudio.Instance
        ?? throw new InvalidOperationException("No run-scoped Watcher audio controller.");

    private static AudioStreamPlayer Loop(WatcherSound sound) =>
        Current().GetChildren().OfType<AudioStreamPlayer>().Single(player =>
            player.Playing && player.Stream == WatcherAudio.Load(sound));

    private static async Task FocusMute(NGame game, AudioStreamPlayer loop)
    {
        var handler = new NMuteInBackgroundHandler();
        game.AddChild(handler);
        try
        {
            SaveManager.Instance.PrefsSave.MuteInBackground = true;
            handler._Notification((int)Node.NotificationWMWindowFocusOut);
            await WaitFor(() => AudioServer.GetBusVolumeDb(_master) < -70, "native background mute fade");
            Check(loop.Playing && !loop.StreamPaused && loop.Bus == "SFX",
                "native focus-out fades the shared Master bus without restarting or custom-pausing Watcher audio");
            handler._Notification((int)Node.NotificationWMWindowFocusIn);
            Check(Math.Abs(AudioServer.GetBusVolumeDb(_master) -
                Mathf.LinearToDb(Mathf.Pow(SaveManager.Instance.SettingsSave.VolumeMaster, 2))) < 0.01,
                "native focus-in restores the configured master gain");
        }
        finally
        {
            handler.QueueFree();
            SaveManager.Instance.PrefsSave.MuteInBackground = false;
            game.DebugAudio.SetMasterAudioVolume(1);
        }
    }

    private static async Task Samples(bool audible, string description)
    {
        await Task.Delay(200);
        _capture.ClearBuffer();
        await Task.Delay(300);
        var samples = _capture.GetBuffer(_capture.GetFramesAvailable());
        var peak = samples.Select(sample => Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y))).DefaultIfEmpty().Max();
        Check(samples.Length > 1000 && (audible ? peak > 0.00001f : peak < 0.000001f),
            $"{description} (frames={samples.Length}, peak={peak:F8})");
    }

    private static async Task WaitFor(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                var audio = Current();
                var voices = string.Join("; ", audio.GetChildren().OfType<AudioStreamPlayer>().Select(player =>
                    $"{player.Name}:playing={player.Playing},paused={player.StreamPaused},position={player.GetPlaybackPosition():F3}"));
                throw new InvalidOperationException($"Watcher audio timeout: {description}; owners={audio.OwnerCount}, cues={audio.CueCount}; {voices}");
            }
            await Task.Delay(100);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Watcher audio: " + message);
        MainFile.Logger.Info("WATCHER AUDIO PASS: " + message);
    }
}
