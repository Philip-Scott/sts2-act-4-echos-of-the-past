using Godot;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class ArchitectMusicPlaytest
{
    internal static AudioStreamPlayer AssertPlaying(bool checkDecoder = false)
    {
        var room = NCombatRoom.Instance ?? throw new InvalidOperationException("Music playtest has no combat room.");
        var player = room.GetNode<AudioStreamPlayer>(ArchitectMusic.PlayerName);
        if (!player.Playing || player.Stream is not AudioStreamOggVorbis { Loop: true, LoopOffset: 0 } stream ||
            Math.Abs(stream.GetLength() - 64) > .001 ||
            room.GetChildren().OfType<AudioStreamPlayer>().Count(node => node.Name == ArchitectMusic.PlayerName) != 1)
            throw new InvalidOperationException("Architect music must be one playing, full-length OGG decoder loop.");
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

    internal static void AssertSame(AudioStreamPlayer previous)
    {
        if (AssertPlaying() != previous)
            throw new InvalidOperationException("Corrupted Player handoff must preserve the playing music instance.");
    }

    internal static void AssertStopped(AudioStreamPlayer previous)
    {
        if (GodotObject.IsInstanceValid(previous) && previous.Playing)
            throw new InvalidOperationException("Architect music continued after leaving or finishing combat.");
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
        playback.Start();
        var rate = (int)AudioServer.GetMixRate();
        var remaining = rate * 129;
        var processed = 0;
        var silentSamples = 0;
        while (remaining > 0)
        {
            var count = Math.Min(4096, remaining);
            foreach (var sample in playback.MixAudio(1, count))
            {
                if (!float.IsFinite(sample.X) || !float.IsFinite(sample.Y) ||
                    Math.Abs(sample.X) >= 1 || Math.Abs(sample.Y) >= 1)
                    throw new InvalidOperationException("Architect decoder output contains invalid or clipped samples.");
                silentSamples = sample.LengthSquared() < 1e-12f ? silentSamples + 1 : 0;
                if (processed++ > rate && silentSamples > rate / 100)
                    throw new InvalidOperationException("Architect decoder inserted an audible silent gap.");
            }
            remaining -= count;
        }
        if (playback.GetLoopCount() < 2 || !playback.IsPlaying())
            throw new InvalidOperationException("Architect audio did not loop twice inside the decoder.");
        playback.Stop();
        MainFile.Logger.Info("ARCHITECT MUSIC PASSED: packed 64-second OGG, two gapless decoder wraps, mute and music/master volume.");
    }
}
