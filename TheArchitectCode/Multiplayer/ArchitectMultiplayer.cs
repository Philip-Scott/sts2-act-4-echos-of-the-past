using System.Runtime.CompilerServices;
using System.Text.Json;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Multiplayer;

public sealed class ArchitectPartyMessage : ICustomMessage
{
    public ArchitectPartyPacket Packet { get; set; } = new();
    public bool ShouldBroadcast => false;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt((int)Packet.Kind);
        writer.WriteULong(Packet.Seed);
        writer.WriteString(Packet.Payload);
        writer.WriteString(Packet.Digest);
        writer.WriteInt(Packet.Index);
        writer.WriteInt(Packet.Count);
    }

    public void Deserialize(PacketReader reader)
    {
        Packet = new ArchitectPartyPacket
        {
            Kind = (ArchitectPartyMessageKind)reader.ReadInt(),
            Seed = reader.ReadULong(),
            Payload = reader.ReadString(),
            Digest = reader.ReadString(),
            Index = reader.ReadInt(),
            Count = reader.ReadInt()
        };
    }

    public void HandleMessage(ulong senderId) => ArchitectMultiplayer.Receive(Packet, senderId);
}

public static class ArchitectMultiplayer
{
    private static readonly ConditionalWeakTable<IRunState, PartySynchronizer> Synchronizers = new();

    public static void Register() => RunManager.Instance.RunStarted += run =>
    {
        if (IsMultiplayer(RunManager.Instance))
            Get(run).Start();
    };

    public static bool IsMultiplayer(RunManager manager) =>
        manager.NetService.Type is NetGameType.Host or NetGameType.Client;

    internal static PartySynchronizer Get(IRunState run) => Synchronizers.GetValue(run, state =>
    {
        var net = RunManager.Instance.NetService;
        var host = net.Type == NetGameType.Host ? net.NetId :
            net is NetClientGameService client ? client.HostNetId :
            throw new InvalidOperationException("Architect multiplayer requires a native host/client service.");
        return new PartySynchronizer(new PartySyncEnvironment(
            ArchitectRun.Get(state), state.Players.Select(player => player.NetId).ToArray(),
            net.NetId, host, state.Rng.Seed, state.Act is ArchitectAct,
            CorruptedPlayerStore.GetProfileUuid,
            (profile, participants) => CorruptedPartyStore.Load(CorruptedPlayerStore.ProfileDirectory, profile, participants),
            ValidateContent,
            (packet, target) =>
            {
                var wrapper = new CustomMessageWrapper { Message = new ArchitectPartyMessage { Packet = packet } };
                if (target is { } peer)
                    net.SendMessage(wrapper, peer);
                else
                    net.SendMessage(wrapper);
            },
            NativeCorruptedPlayerPreflight.Show, () => net.IsConnected));
    });

    internal static void Receive(ArchitectPartyPacket message, ulong sender)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run != null && IsMultiplayer(RunManager.Instance))
            Get(run).Receive(message, sender);
    }

    internal static long? RecordOutcome(IRunState run, string outcome)
    {
        var manager = RunManager.Instance;
        if (manager.NetService.Type != NetGameType.Host)
            return null;
        var state = ArchitectRun.Get(run);
        var frozen = state.EntryParty ?? throw new InvalidOperationException("Architect terminal party was never synchronized.");
        frozen.Validate(run.Players.Select(player => player.NetId).ToArray(), manager.NetService.NetId);
        var members = frozen.Participants.Select(participant => new CorruptedPartyMember(participant.ProfileUuid,
            CorruptedPlayerSnapshot.Capture(run.Players.Single(player => player.NetId == participant.NetId)))).ToArray();
        return CorruptedPartyStore.Commit(CorruptedPlayerStore.ProfileDirectory,
            CorruptedPlayerStore.GetProfileUuid(), frozen, outcome, members);
    }

    private static void ValidateContent(FrozenCorruptedParty party)
    {
        foreach (var member in party.Lineage?.Members ?? [])
        {
            if (member.Snapshot.ResolveCharacter() == null)
                throw new JsonException($"Corrupted Party character '{member.Snapshot.CharacterId}' is unavailable.");
            if (NativeCardSupport.Preflight(member.Snapshot.Deck) is { } problem)
                throw new JsonException(problem);
        }
    }
}
