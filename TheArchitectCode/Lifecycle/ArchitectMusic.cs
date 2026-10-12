using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectMusic
{
    internal const string TrackPath = "res://TheArchitect/audio/heart_metal_cover.ogg";
    internal const double LoopStartSeconds = 18.01;
    internal const double LoopEndSeconds = 174.75;
    internal const string PlayerName = "ArchitectBossMusic";
    internal const string CorruptedTrackPath = "res://TheArchitect/audio/heart_metal_cover_corrupted.wav";
    internal const string CorruptedPlayerName = "CorruptedPlayerMusic";
    internal const double CorruptedStartSeconds = 182.44;
    internal const double CorruptedLoopStartSeconds = 184.94;
    internal const double CorruptedLoopEndSeconds = 200.71;
    internal const string EndingTrackPath = "event:/temp/sfx/game_over";
    private static AudioStreamPlayer? _player;
    private static NCombatRoom? _room;
    private static string? _track;
    internal static bool IsPlayingOutro { get; private set; }

    internal static bool IsTrack(string? path) => path is TrackPath or CorruptedTrackPath;
    internal static float MusicVolume(float setting) => MathF.Pow(Math.Clamp(setting, 0f, 1f), 2f);

    internal static void Play(NRunMusicController controller, string track = TrackPath)
    {
        if (TestMode.IsOn || NonInteractiveMode.IsActive)
            return;
        var room = NCombatRoom.Instance ??
            throw new InvalidOperationException("Architect music requires an active combat room.");
        if (GodotObject.IsInstanceValid(_player) && _room == room && _track == track)
            return;
        Stop();

        var corrupted = track == CorruptedTrackPath;
        AudioStream stream = corrupted ? LoadCorruptedStream() : LoadBossStream();
        var player = new AudioStreamPlayer
        {
            Name = corrupted ? CorruptedPlayerName : PlayerName,
            Stream = stream,
            Bus = "Master",
            ProcessMode = Node.ProcessModeEnum.Always,
            VolumeLinear = MusicVolume(SaveManager.Instance.SettingsSave.VolumeBgm)
        };
        player.TreeExiting += () =>
        {
            if (ReferenceEquals(_player, player))
                Clear();
        };
        // The Elite's ending can outlive its room, but never its run.
        if (corrupted)
        {
            controller.AddChild(player);
            room.TreeExiting += () =>
            {
                if (ReferenceEquals(_player, player) && !IsPlayingOutro)
                    Stop();
            };
            player.Finished += () =>
            {
                if (!ReferenceEquals(_player, player))
                    return;
                Stop();
                controller.StopCustomMusic();
            };
        }
        else
            room.AddChild(player);
        _player = player;
        _room = room;
        _track = track;
        CombatManager.Instance.CombatEnded += CombatEnded;
        // Encounter music normally stops the act's FMOD event through this same proxy.
        controller.GetNode("Proxy").Call("stop_music");
        player.Play(0);
    }

    private static AudioStreamOggVorbis LoadBossStream()
    {
        // Quick PCKs contain raw audio; Godot exports contain imported resources.
        var stream = Godot.FileAccess.FileExists(TrackPath + ".import")
            ? ResourceLoader.Load<AudioStreamOggVorbis>(TrackPath)
            : AudioStreamOggVorbis.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(TrackPath));
        if (stream == null)
            throw new InvalidOperationException($"Could not decode Architect music: {TrackPath}");
        stream.Loop = true;
        stream.LoopOffset = LoopStartSeconds;
        return stream;
    }

    private static AudioStreamWav LoadCorruptedStream()
    {
        var stream = Godot.FileAccess.FileExists(CorruptedTrackPath + ".import")
            ? ResourceLoader.Load<AudioStreamWav>(CorruptedTrackPath, cacheMode: ResourceLoader.CacheMode.Ignore)
            : AudioStreamWav.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(CorruptedTrackPath));
        if (stream == null || stream.GetLength() <= CorruptedLoopEndSeconds - CorruptedStartSeconds)
            throw new InvalidOperationException($"Could not load Corrupted Player music and ending: {CorruptedTrackPath}");
        // WAV supports an interior loop end, so disabling looping preserves the remaining song.
        stream.LoopBegin = (int)Math.Round((CorruptedLoopStartSeconds - CorruptedStartSeconds) * stream.MixRate);
        stream.LoopEnd = (int)Math.Round((CorruptedLoopEndSeconds - CorruptedStartSeconds) * stream.MixRate);
        stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        return stream;
    }

    internal static void FinishCombat(CombatRoom room)
    {
        if (!IsTrack(room.Encounter.CustomBgm) || room.Encounter.CustomBgm != _track)
            return;
        if (GodotObject.IsInstanceValid(_player) && _player!.Stream is AudioStreamWav stream)
        {
            IsPlayingOutro = true;
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        }
        else
            Stop();
    }

    internal static void PlayEnding()
    {
        if (TestMode.IsOn || NonInteractiveMode.IsActive)
            return;
        var controller = NRunMusicController.Instance ??
            throw new InvalidOperationException("Architect ending music requires an active run music controller.");
        var audio = NAudioManager.Instance ??
            throw new InvalidOperationException("Architect ending music requires an active audio manager.");
        // Native WinRun kills the party after the final strike. Reproduce only its audio,
        // without killing the bound players or changing the frozen combat result.
        controller.StopMusic();
        audio.PlayMusic(EndingTrackPath);
    }

    private static void CombatEnded(CombatRoom room)
    {
        if (room.Encounter.CustomBgm == _track && !IsPlayingOutro)
            Stop();
    }

    private static void Clear()
    {
        CombatManager.Instance.CombatEnded -= CombatEnded;
        _player = null;
        _room = null;
        _track = null;
        IsPlayingOutro = false;
    }

    internal static void Stop()
    {
        if (GodotObject.IsInstanceValid(_player))
        {
            _player!.Stop();
            _player.QueueFree();
        }
        Clear();
    }

    internal static void UpdateVolume(float volume)
    {
        if (GodotObject.IsInstanceValid(_player))
            _player!.VolumeLinear = MusicVolume(volume);
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic))]
internal static class ArchitectEncounterMusicPatch
{
    private static bool Prefix(NRunMusicController __instance, string customMusic)
    {
        if (!ArchitectMusic.IsTrack(customMusic))
        {
            ArchitectMusic.Stop();
            return true;
        }
        ArchitectMusic.Play(__instance, customMusic);
        return false;
    }
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))]
internal static class StopArchitectMusicPatch
{
    private static void Prefix() => ArchitectMusic.Stop();
}

[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic))]
internal static class StopArchitectCustomMusicPatch
{
    private static bool Prefix()
    {
        if (ArchitectMusic.IsPlayingOutro)
            return false;
        ArchitectMusic.Stop();
        return true;
    }
}

[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.OnCombatEnded))]
internal static class FinishArchitectMusicPatch
{
    private static void Prefix(CombatRoom __instance) => ArchitectMusic.FinishCombat(__instance);
}

[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.Exit))]
internal static class LeaveArchitectMusicPatch
{
    private static void Prefix(CombatRoom __instance)
    {
        if (ArchitectMusic.IsTrack(__instance.Encounter.CustomBgm) && !ArchitectMusic.IsPlayingOutro)
            ArchitectMusic.Stop();
    }
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.SetBgmVol))]
internal static class ArchitectMusicVolumePatch
{
    private static void Postfix(float volume) => ArchitectMusic.UpdateVolume(volume);
}
