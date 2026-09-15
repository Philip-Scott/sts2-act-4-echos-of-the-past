using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode.Audio;

internal enum WatcherSound { Mantra, DivinityLoop, Calm, CalmLoop, Wrath, WrathLoop }

internal partial class WatcherAudio : Node
{
    private const string AudioPath = "res://TheArchitect/audio/watcher/";
    private static readonly Dictionary<WatcherSound, AudioStreamOggVorbis> Streams = [];
    private readonly Dictionary<WatcherSound, AudioStreamPlayer> _loops = [];
    private readonly Dictionary<AudioStreamPlayer, WatcherSound> _cues = [];
    private WatcherStanceAudio _stances = null!;
    private RunState _run = null!;
    private EventRoom? _ancientRoom;
    private NCombatRoom? _combatView;
    private bool _closed;

    internal static WatcherAudio? Instance { get; private set; }
    internal int LoopCount(WatcherSound sound) => _loops.ContainsKey(sound) ? 1 : 0;
    internal int CueCount => _cues.Count;
    internal int OwnerCount => _stances.OwnerCount;

    internal static void Attach(NRun node)
    {
        if (TestMode.IsOn)
            return;
        var run = RunManager.Instance.DebugOnlyGetState()
            ?? throw new InvalidOperationException("Watcher audio requires an active run.");
        var local = run.Players.Single(player => player.NetId == RunManager.Instance.NetService.NetId).Creature;
        var audio = new WatcherAudio { Name = "WatcherAudio", _run = run };
        audio._stances = new WatcherStanceAudio(local, audio.StanceEntered, audio.StanceLeft);
        Instance?.Close();
        Instance = audio;
        node.AddChild(audio);
    }

    public override void _Ready()
    {
        if (AudioServer.GetBusIndex("SFX") < 0)
            throw new InvalidOperationException("Native SFX audio bus is unavailable.");
        RunManager.Instance.RoomEntered += RoomEntered;
        CombatManager.Instance.CombatEnded += CombatEnded;
    }

    internal static void Enter(PowerModel power)
    {
        if (TestMode.IsOn)
            return;
        // Only a power in this client's current combat is a playback event, never a model preview.
        if (Instance is { _closed: false } audio &&
            ReferenceEquals(power.Owner.CombatState, CombatManager.Instance.DebugOnlyGetState()) &&
            CombatManager.Instance.IsInProgress)
            audio._stances.Enter(power);
    }

    internal void AncientShown(EventModel model)
    {
        if (_closed ||
            _run.CurrentRoom is not EventRoom room ||
            !ReferenceEquals(room.LocalMutableEvent, model) || ReferenceEquals(_ancientRoom, room))
            return;
        StopAncient();
        _ancientRoom = room;
        PlayCue(WatcherSound.Mantra);
        StartLoop(WatcherSound.DivinityLoop);
    }

    private void RoomEntered()
    {
        // Closing the Ancient/choosing a gift opens the map without leaving its floor.
        if (_run.CurrentRoom is not MapRoom && !ReferenceEquals(_run.CurrentRoom, _ancientRoom))
            StopAncient();
        if (_run.CurrentRoom is not CombatRoom)
            StopStances();
    }

    private void CombatEnded(CombatRoom _) => StopStances();

    private void StanceEntered(PowerModel power)
    {
        if (_combatView == null && NCombatRoom.Instance is { } view)
        {
            _combatView = view;
            view.TreeExiting += StopStances;
        }
        var local = ReferenceEquals(_stances.LocalStance, power);
        if (local)
        {
            StopLoop(WatcherSound.CalmLoop);
            StopLoop(WatcherSound.WrathLoop);
        }
        PlayCue(power is CalmStancePower ? WatcherSound.Calm : WatcherSound.Wrath, () =>
        {
            // A cue may finish after a switch, death, reset, or another combat.
            if (local && !_closed && ReferenceEquals(_stances.LocalStance, power))
                StartLoop(power is CalmStancePower ? WatcherSound.CalmLoop : WatcherSound.WrathLoop);
        });
    }

    private void StanceLeft(PowerModel power)
    {
        if (_stances.LocalStance == null)
        {
            StopLoop(WatcherSound.CalmLoop);
            StopLoop(WatcherSound.WrathLoop);
        }
    }

    internal void StopStances()
    {
        if (_combatView != null)
        {
            _combatView.TreeExiting -= StopStances;
            _combatView = null;
        }
        _stances.Clear();
        StopLoop(WatcherSound.CalmLoop);
        StopLoop(WatcherSound.WrathLoop);
        foreach (var (player, sound) in _cues.ToArray())
            if (sound != WatcherSound.Mantra)
                ReleaseCue(player);
    }

    internal void StopAncient()
    {
        _ancientRoom = null;
        StopLoop(WatcherSound.DivinityLoop);
        foreach (var (player, sound) in _cues.ToArray())
            if (sound == WatcherSound.Mantra)
                ReleaseCue(player);
    }

    private void PlayCue(WatcherSound sound, Action? finished = null)
    {
        var player = NewPlayer(sound);
        _cues.Add(player, sound);
        player.Finished += () =>
        {
            ReleaseCue(player);
            finished?.Invoke();
        };
        player.Play();
    }

    private void ReleaseCue(AudioStreamPlayer player)
    {
        if (!_cues.Remove(player))
            return;
        player.Stop();
        player.QueueFree();
    }

    private void StartLoop(WatcherSound sound)
    {
        if (_loops.ContainsKey(sound))
            return;
        var player = NewPlayer(sound);
        _loops.Add(sound, player);
        player.Play();
    }

    private void StopLoop(WatcherSound sound)
    {
        if (!_loops.Remove(sound, out var player))
            return;
        player.Stop();
        player.QueueFree();
    }

    private AudioStreamPlayer NewPlayer(WatcherSound sound)
    {
        // Dedicated native SFX players do not steal BaseLib's exclusive ambience channel,
        // and keep loop state even when the master/SFX volume is currently zero.
        var player = new AudioStreamPlayer
        {
            Name = sound.ToString(), Stream = Load(sound), Bus = "SFX",
            ProcessMode = ProcessModeEnum.Pausable
        };
        AddChild(player);
        return player;
    }

    internal static AudioStreamOggVorbis Load(WatcherSound sound)
    {
        if (Streams.TryGetValue(sound, out var stream))
            return stream;
        var file = sound == WatcherSound.Mantra ? "Mantra_v3" : sound + "_v2";
        // Read the original Ogg from the quick PCK or exported PCK; no editor import is required.
        var path = AudioPath + $"STS_SFX_Watcher-{file}.ogg";
        stream = (Godot.FileAccess.FileExists(path + ".import")
            ? ResourceLoader.Load<AudioStreamOggVorbis>(path)
            : AudioStreamOggVorbis.LoadFromFile(path))
            ?? throw new InvalidOperationException($"Cannot load packaged Watcher sound: {sound}.");
        stream.Loop = sound is WatcherSound.DivinityLoop or WatcherSound.CalmLoop or WatcherSound.WrathLoop;
        stream.LoopOffset = 0;
        Streams.Add(sound, stream);
        return stream;
    }

    internal void Close()
    {
        if (_closed)
            return;
        _closed = true;
        RunManager.Instance.RoomEntered -= RoomEntered;
        CombatManager.Instance.CombatEnded -= CombatEnded;
        StopStances();
        StopAncient();
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    public override void _ExitTree() => Close();
}
