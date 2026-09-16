using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectMusic
{
    internal const string TrackPath = "res://TheArchitect/audio/the_hollow_pulse.ogg";
    internal const string PlayerName = "ArchitectBossMusic";
    private static AudioStreamPlayer? _player;

    internal static bool IsTrack(string? path) => path == TrackPath;
    internal static float MusicVolume(float setting) => MathF.Pow(Math.Clamp(setting, 0f, 1f), 2f);

    internal static void Play(NRunMusicController controller)
    {
        if (TestMode.IsOn || NonInteractiveMode.IsActive)
            return;
        var room = NCombatRoom.Instance ??
            throw new InvalidOperationException("Architect music requires an active combat room.");
        if (GodotObject.IsInstanceValid(_player) && _player!.GetParent() == room)
            return;
        Stop();

        // The quick packer stores raw OGG; Godot exports an imported resource and remap instead.
        var stream = Godot.FileAccess.FileExists(TrackPath + ".import")
            ? ResourceLoader.Load<AudioStreamOggVorbis>(TrackPath)
            : AudioStreamOggVorbis.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(TrackPath));
        if (stream == null)
            throw new InvalidOperationException($"Could not decode Architect music: {TrackPath}");
        stream.Loop = true;
        stream.LoopOffset = 0;
        var player = new AudioStreamPlayer
        {
            Name = PlayerName,
            Stream = stream,
            Bus = "Master",
            ProcessMode = Node.ProcessModeEnum.Always,
            VolumeLinear = MusicVolume(SaveManager.Instance.SettingsSave.VolumeBgm)
        };
        player.TreeExiting += () =>
        {
            if (ReferenceEquals(_player, player))
                _player = null;
        };
        room.AddChild(player);
        _player = player;
        // Encounter music normally stops the act's FMOD event through this same proxy.
        controller.GetNode("Proxy").Call("stop_music");
        player.Play();
    }

    internal static void Stop()
    {
        if (GodotObject.IsInstanceValid(_player))
        {
            _player!.Stop();
            _player.QueueFree();
        }
        _player = null;
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
        ArchitectMusic.Play(__instance);
        return false;
    }
}

[HarmonyPatch]
internal static class StopArchitectMusicPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic)),
        AccessTools.Method(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic))
    ];

    private static void Prefix() => ArchitectMusic.Stop();
}

[HarmonyPatch]
internal static class LeaveArchitectMusicPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
    [
        AccessTools.Method(typeof(CombatRoom), nameof(CombatRoom.OnCombatEnded)),
        AccessTools.Method(typeof(CombatRoom), nameof(CombatRoom.Exit))
    ];

    private static void Prefix(CombatRoom __instance)
    {
        if (ArchitectMusic.IsTrack(__instance.Encounter.CustomBgm))
            ArchitectMusic.Stop();
    }
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.SetBgmVol))]
internal static class ArchitectMusicVolumePatch
{
    private static void Postfix(float volume) => ArchitectMusic.UpdateVolume(volume);
}
