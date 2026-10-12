using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectMusicTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("music: only the selected boss and Corrupted Player tracks are intercepted", () =>
        {
            Check(ArchitectMusic.IsTrack("res://TheArchitect/audio/heart_metal_cover.ogg"));
            Check(ArchitectMusic.IsTrack("res://TheArchitect/audio/heart_metal_cover_corrupted.wav"));
            Check(!ArchitectMusic.IsTrack(null));
            Check(!ArchitectMusic.IsTrack(""));
            Check(!ArchitectMusic.IsTrack("event:/music/act3_boss_test_subject"));
            Check(!ArchitectMusic.IsTrack("res://OtherMod/audio/heart_metal_cover.ogg"));
            Check(!ArchitectMusic.IsTrack("res://OtherMod/audio/heart_metal_cover_corrupted.wav"));
            Check(!ArchitectMusic.IsTrack("res://TheArchitect/audio/the_hollow_pulse.ogg"));
        });
        test("music: Corrupted Player intro and loop use the requested source timestamps", () =>
        {
            Check(ArchitectMusic.CorruptedStartSeconds == 182.44);
            Check(ArchitectMusic.CorruptedLoopStartSeconds == 184.94);
            Check(ArchitectMusic.CorruptedLoopEndSeconds == 200.71);
            Check(Math.Round((ArchitectMusic.CorruptedLoopStartSeconds - ArchitectMusic.CorruptedStartSeconds) * 44100)
                == 110250);
            Check(Math.Round((ArchitectMusic.CorruptedLoopEndSeconds - ArchitectMusic.CorruptedStartSeconds) * 44100)
                == 805707);
        });
        test("music: repeats run from 0:18.01 to 2:54.75", () =>
        {
            Check(ArchitectMusic.LoopStartSeconds == 18.01);
            Check(ArchitectMusic.LoopEndSeconds == 174.75);
        });
        test("music: the final strike uses the native run-termination cue", () =>
        {
            Check(ArchitectMusic.EndingTrackPath == "event:/temp/sfx/game_over");
            Check(!ArchitectMusic.IsTrack(ArchitectMusic.EndingTrackPath));
        });
        test("music: volume follows the native squared slider curve, including mute", () =>
        {
            Check(ArchitectMusic.MusicVolume(0f) == 0f);
            Check(ArchitectMusic.MusicVolume(.5f) == .25f);
            Check(ArchitectMusic.MusicVolume(1f) == 1f);
        });
        test("music: native lifecycle patch targets exist", () =>
        {
            Check(AccessTools.Method(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic),
                [typeof(string)]) != null);
            Check(AccessTools.Method(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic)) != null);
            Check(AccessTools.Method(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic)) != null);
            Check(AccessTools.Method(typeof(CombatRoom), nameof(CombatRoom.OnCombatEnded)) != null);
            Check(AccessTools.Method(typeof(CombatRoom), nameof(CombatRoom.Exit)) != null);
            Check(AccessTools.Method(typeof(NAudioManager), nameof(NAudioManager.SetBgmVol), [typeof(float)]) != null);
            Check(AccessTools.Method(typeof(NAudioManager), nameof(NAudioManager.PlayMusic), [typeof(string)]) != null);
        });
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Architect music contract failed.");
    }
}
