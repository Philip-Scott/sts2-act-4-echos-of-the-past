using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheArchitect.TheArchitectCode.Persistence;

public sealed partial class ArchitectRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Entered { get; set; }
    public CorruptedPlayerEnvelope? EntrySnapshot { get; set; }
    public ulong? StartingHostNetId { get; set; }
    public ulong[]? OriginalNetworkIds { get; set; }
    public FrozenCorruptedParty? Origin { get; set; }
    public FrozenCorruptedParty? PendingParty { get; set; }
    public FrozenCorruptedParty? EntryParty { get; set; }
    public string? Outcome { get; set; }
    public long? SnapshotRevision { get; set; }

    [JsonIgnore]
    public IReadOnlyList<CorruptedPlayerSnapshot> EncounterSnapshots =>
        EntryParty is { } party
            ? party.Lineage?.Members.Select(member => member.Snapshot).ToArray() ?? []
            : EntrySnapshot?.Snapshot is { } snapshot ? [snapshot] : [];

    [JsonIgnore]
    public long EncounterRevision => EntryParty is { } party
        ? party.Lineage?.Revision ?? 0
        : EntrySnapshot?.Revision ?? 0;

    public ulong? EncounterCounterpartNetId(int index)
    {
        if (EntryParty == null)
            return null;
        var lineage = EntryParty.Lineage ??
            throw new InvalidOperationException("A First Visit has no Corrupted Player counterpart.");
        var profile = lineage.Members[index].ProfileUuid;
        return EntryParty.Participants.Single(participant => participant.ProfileUuid == profile).NetId;
    }

    public void BindNetworkOrigin(ulong[] participants, ulong host)
    {
        if (participants.Length is < 2 or > 4 || participants.Distinct().Count() != participants.Length ||
            !participants.Contains(host) || StartingHostNetId is { } previousHost && previousHost != host ||
            OriginalNetworkIds is { } previous && !previous.Order().SequenceEqual(participants.Order()))
            throw new JsonException("Architect original participants or starting host changed; host migration is not supported.");
        StartingHostNetId = host;
        OriginalNetworkIds = participants.Order().ToArray();
    }

    public void BindOrigin(FrozenCorruptedParty origin)
    {
        BindNetworkOrigin(origin.Participants.Select(player => player.NetId).ToArray(), origin.HostNetId);
        if (Origin != null && (Origin.Digest != origin.Digest || Id != origin.RunId))
            throw new JsonException("Architect original participants or starting host changed; host migration is not supported.");
        Origin = origin;
        Id = origin.RunId;
    }

    public void EnterParty(FrozenCorruptedParty party)
    {
        if (Origin == null || party.RunId != Id || party.GroupKey != Origin.GroupKey ||
            party.HostNetId != Origin.HostNetId || party.HostProfileUuid != Origin.HostProfileUuid ||
            !party.Participants.SequenceEqual(Origin.Participants))
            throw new JsonException("Frozen Corrupted Party does not match the starting host and original participants.");
        if (Entered && EntryParty?.Digest != party.Digest)
            throw new JsonException("A multiplayer save must reload its frozen entry party, never reselect it.");
        EntrySnapshot = null;
        EntryParty = party;
        PendingParty = null;
        Entered = true;
    }
}
