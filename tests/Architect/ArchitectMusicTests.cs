using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using TheArchitect.TheArchitectCode.Lifecycle;

internal static class ArchitectMusicTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("music: only the selected Architect track is intercepted", () =>
        {
            Check(ArchitectMusic.IsTrack("res://TheArchitect/audio/the_hollow_pulse.ogg"));
            Check(!ArchitectMusic.IsTrack(null));
            Check(!ArchitectMusic.IsTrack(""));
            Check(!ArchitectMusic.IsTrack("event:/music/act3_boss_test_subject"));
            Check(!ArchitectMusic.IsTrack("res://OtherMod/audio/the_hollow_pulse.ogg"));
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
        });
    }

    private static void Check(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Architect music contract failed.");
    }
}
