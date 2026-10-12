using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class ArchitectMusicPlaytest
{
    internal static string? LastNativeMusic { private get; set; }

    internal static AudioStreamPlayer AssertEliteMusic(bool checkDecoder = false)
    {
        var room = NCombatRoom.Instance ?? throw new InvalidOperationException("Music playtest has no combat room.");
        if (room.GetNodeOrNull<AudioStreamPlayer>(ArchitectMusic.PlayerName) is { Playing: true })
            throw new InvalidOperationException("The Architect theme must not play during the Corrupted Player Elite.");
        var controller = NRunMusicController.Instance!;
        var player = controller.GetNode<AudioStreamPlayer>(ArchitectMusic.CorruptedPlayerName);
        if (!player.Playing || ArchitectMusic.IsPlayingOutro ||
            player.Stream is not AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Forward } stream ||
            stream.MixRate != 44100 || stream.LoopBegin != 110250 || stream.LoopEnd != 805707 ||
            Math.Abs(stream.GetLength() - 37.754830) > .001 ||
            controller.GetChildren().OfType<AudioStreamPlayer>()
                .Count(node => node.Name == ArchitectMusic.CorruptedPlayerName) != 1)
            throw new InvalidOperationException("The Elite must play from 3:02.44 and loop 3:20.71 to 3:04.94.");
        var position = player.GetPlaybackPosition();
        controller.PlayCustomMusic(ArchitectMusic.CorruptedTrackPath);
        if (controller.GetNode<AudioStreamPlayer>(ArchitectMusic.CorruptedPlayerName) != player ||
            Math.Abs(player.GetPlaybackPosition() - position) > .1)
            throw new InvalidOperationException("Repeated Elite music requests must not restart playback.");
        if (checkDecoder)
        {
            AssertVolume(player);
            AssertEliteDecoder(stream);
        }
        MainFile.Logger.Info("ELITE MUSIC PASSED: 3:02.44 intro, 3:20.71 to 3:04.94 loop.");
        return player;
    }

    internal static AudioStreamPlayer AssertEliteOutro()
    {
        var controller = NRunMusicController.Instance!;
        var player = controller.GetNode<AudioStreamPlayer>(ArchitectMusic.CorruptedPlayerName);
        if (!player.Playing || !ArchitectMusic.IsPlayingOutro ||
            player.Stream is not AudioStreamWav { LoopMode: AudioStreamWav.LoopModeEnum.Disabled })
            throw new InvalidOperationException("Elite victory must release the loop without stopping the song.");
        var position = player.GetPlaybackPosition();
        controller.StopCustomMusic();
        if (!player.Playing || Math.Abs(player.GetPlaybackPosition() - position) > .1)
            throw new InvalidOperationException("Native music cleanup interrupted the Elite ending.");
        return player;
    }

    internal static async Task AssertEliteOutroFinishes(AudioStreamPlayer player)
    {
        player.Seek((float)player.Stream.GetLength() - .2f);
        await ArchitectPlaytest.WaitFor(() => !GodotObject.IsInstanceValid(player) || !player.Playing);
        await Task.Delay(200);
        if (ArchitectMusic.IsPlayingOutro ||
            NRunMusicController.Instance!.GetNodeOrNull<AudioStreamPlayer>(ArchitectMusic.CorruptedPlayerName) != null)
            throw new InvalidOperationException("The Elite ending must finish once and release its player.");
        MainFile.Logger.Info("ELITE ENDING PASSED: uninterrupted through rewards/map; finished once without repeating.");
    }

    private static void AssertEliteDecoder(AudioStreamWav source)
    {
        using var stream = (AudioStreamWav)source.Duplicate();
        foreach (var releaseTime in new[] { 1d, 18d, 35.04 })
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            using var playback = stream.InstantiatePlayback();
            playback.Start(0);
            if (Math.Abs(playback.GetPlaybackPosition()) > .02)
                throw new InvalidOperationException("The Elite must start at the beginning of its 3:02.44 excerpt.");
            Mix(releaseTime);
            var expected = releaseTime < 18.27 ? releaseTime : 3.5;
            if (Math.Abs(playback.GetPlaybackPosition() - expected) > .02)
                throw new InvalidOperationException("The Elite decoder did not wrap at the requested samples.");
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
            Mix(.5);
            if (Math.Abs(playback.GetPlaybackPosition() - expected - .5) > .02)
                throw new InvalidOperationException("Releasing the Elite loop must not seek or restart the song.");
            Mix(stream.GetLength());
            if (playback.IsPlaying() || playback.MixAudio(1, 4096).Any(sample => sample.LengthSquared() != 0))
                throw new InvalidOperationException("The Elite decoder repeated after the song ended.");

            void Mix(double seconds)
            {
                var remaining = (int)Math.Round(AudioServer.GetMixRate() * seconds);
                while (remaining > 0)
                {
                    var count = Math.Min(4096, remaining);
                    foreach (var sample in playback.MixAudio(1, count))
                        if (!float.IsFinite(sample.X) || !float.IsFinite(sample.Y))
                            throw new InvalidOperationException("The Elite decoder contains invalid samples.");
                    remaining -= count;
                }
            }
        }
    }

    internal static AudioStreamPlayer AssertPlaying(bool checkDecoder = false)
    {
        var room = NCombatRoom.Instance ?? throw new InvalidOperationException("Music playtest has no combat room.");
        var player = room.GetNode<AudioStreamPlayer>(ArchitectMusic.PlayerName);
        if (!player.Playing || player.Stream is not AudioStreamOggVorbis { Loop: true } stream ||
            Math.Abs(stream.LoopOffset - ArchitectMusic.LoopStartSeconds) > .000001 ||
            Math.Abs(stream.GetLength() - ArchitectMusic.LoopEndSeconds) > .001 ||
            room.GetChildren().OfType<AudioStreamPlayer>().Count(node => node.Name == ArchitectMusic.PlayerName) != 1)
            throw new InvalidOperationException("Architect music must be one playing OGG with the 2:54.75 to 0:18.01 loop.");
        var position = player.GetPlaybackPosition();
        ArchitectMusic.Play(NRunMusicController.Instance!);
        if (room.GetNode<AudioStreamPlayer>(ArchitectMusic.PlayerName) != player ||
            player.GetPlaybackPosition() < position)
            throw new InvalidOperationException("Repeated music requests must not replace or restart the loop.");
        if (checkDecoder)
        {
            AssertVolume(player);
            AssertDecoderLoop(stream);
        }
        return player;
    }

    internal static void AssertStopped(AudioStreamPlayer previous)
    {
        if (GodotObject.IsInstanceValid(previous) && previous.Playing)
            throw new InvalidOperationException("Architect music continued after leaving or finishing combat.");
    }

    internal static void AssertEndingMusicNotStarted()
    {
        if (LastNativeMusic == ArchitectMusic.EndingTrackPath)
            throw new InvalidOperationException("Run-termination music started before the Architect's final strike.");
    }

    internal static GodotObject AssertEndingMusic(bool allowFinished = false)
    {
        var nativeMusic = NAudioManager.Instance!.GetNode("Proxy").Get("music_track").AsGodotObject();
        if (!GodotObject.IsInstanceValid(nativeMusic))
            throw new InvalidOperationException("Architect ending has no native music event.");
        var playbackState = nativeMusic.Call("get_playback_state").AsInt32();
        var combatMusic = NRunMusicController.Instance!.GetNode("Proxy").Get("_musicEv").AsGodotObject();
        if (LastNativeMusic != ArchitectMusic.EndingTrackPath || ArchitectMusic.IsPlayingOutro ||
            !(playbackState is 0 or 3 || allowFinished && playbackState == 2) ||
            GodotObject.IsInstanceValid(combatMusic) && combatMusic.Call("get_playback_state").AsInt32() != 2)
            throw new InvalidOperationException(
                $"Architect ending expected {ArchitectMusic.EndingTrackPath} without combat music; " +
                $"got {LastNativeMusic}, state={playbackState}.");
        MainFile.Logger.Info($"ARCHITECT ENDING MUSIC PASSED: {ArchitectMusic.EndingTrackPath}, state={playbackState}.");
        return nativeMusic;
    }

    private static void AssertVolume(AudioStreamPlayer player)
    {
        var audio = NAudioManager.Instance!;
        var settings = SaveManager.Instance.SettingsSave;
        try
        {
            audio.SetBgmVol(0);
            if (!player.Playing || player.VolumeLinear != 0)
                throw new InvalidOperationException("Music mute must silence, not stop, the loop.");
            audio.SetBgmVol(.5f);
            if (Math.Abs(player.VolumeLinear - .25f) > .0001f)
                throw new InvalidOperationException("Architect music did not follow the native BGM slider.");
            audio.SetMasterVol(.5f);
            var master = AudioServer.GetBusIndex("Master");
            if (Math.Abs(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(master)) - .25f) > .0001f)
                throw new InvalidOperationException("Godot's Master bus did not follow the native master volume.");
        }
        finally
        {
            audio.SetBgmVol(settings.VolumeBgm);
            audio.SetMasterVol(settings.VolumeMaster);
        }
    }

    private static void AssertDecoderLoop(AudioStreamOggVorbis stream)
    {
        using var playback = stream.InstantiatePlayback();
        playback.Start(0);
        // Starting the resampler preloads a small buffer before the first mix.
        if (Math.Abs(playback.GetPlaybackPosition()) > .02)
            throw new InvalidOperationException("Architect music must start at the beginning, not the loop offset.");
        var rate = (int)AudioServer.GetMixRate();
        var loopLength = ArchitectMusic.LoopEndSeconds - ArchitectMusic.LoopStartSeconds;
        var remaining = (int)Math.Round(rate * (ArchitectMusic.LoopEndSeconds + loopLength + 1));
        var processed = 0;
        var silentSamples = 0;
        while (remaining > 0)
        {
            var count = Math.Min(4096, remaining);
            foreach (var sample in playback.MixAudio(1, count))
            {
                if (!float.IsFinite(sample.X) || !float.IsFinite(sample.Y))
                    throw new InvalidOperationException("Architect decoder output contains invalid samples.");
                silentSamples = sample.LengthSquared() < 1e-12f ? silentSamples + 1 : 0;
                var seconds = (double)processed++ / rate;
                var nearLoop = Math.Abs(seconds - ArchitectMusic.LoopEndSeconds) < .1 ||
                    Math.Abs(seconds - ArchitectMusic.LoopEndSeconds - loopLength) < .1;
                if (nearLoop && silentSamples > rate / 100)
                    throw new InvalidOperationException("Architect decoder inserted an audible silent gap.");
            }
            remaining -= count;
        }
        if (playback.GetLoopCount() != 2 || !playback.IsPlaying() ||
            Math.Abs(playback.GetPlaybackPosition() - ArchitectMusic.LoopStartSeconds - 1) > .1)
            throw new InvalidOperationException("Architect audio did not loop twice to the configured offset.");
        playback.Stop();
        MainFile.Logger.Info("ARCHITECT MUSIC PASSED: intro from zero, two 2:54.75 to 0:18.01 decoder wraps, mute and music/master volume.");
    }
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayMusic))]
internal static class ObserveNativeEndingMusicPatch
{
    private static void Postfix(string music, bool __runOriginal)
    {
        if (__runOriginal)
            ArchitectMusicPlaytest.LastNativeMusic = music;
    }
}
