using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheArchitect.TheArchitectCode.Persistence;

public sealed partial record CorruptedPlayerSnapshot(string CharacterId, int MaxHp, JsonElement[] Deck, string ContentHash)
{
    internal static string Hash(string character, int hp, JsonElement[] cards)
    {
        var content = character + "|" + hp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
            string.Join("|", cards.Select(card => JsonSerializer.Serialize(card)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    public void Validate()
    {
        if (MaxHp <= 0 || string.IsNullOrWhiteSpace(CharacterId) || !CharacterId.Contains('.') ||
            Deck == null || Deck.Any(card => card.ValueKind != JsonValueKind.Object) ||
            ContentHash != Hash(CharacterId, MaxHp, Deck))
            throw new JsonException("Invalid Corrupted Player snapshot or content hash.");
    }
}

public sealed record CorruptedPlayerEnvelope
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public long Revision { get; init; }
    [JsonRequired] public Guid ProfileUuid { get; init; } = Guid.NewGuid();
    public string WrittenByModVersion { get; init; } = "1.0.1";
    public string GameVersion { get; init; } = "0.111.0";
    public string? TerminalRunId { get; init; }
    public string? Outcome { get; init; }
    [JsonRequired] public CorruptedPlayerSnapshot? Snapshot { get; init; }
}

public sealed record SuccessorParticipant(ulong NetId, Guid ProfileUuid);
public sealed record CorruptedPartyMember(Guid ProfileUuid, CorruptedPlayerSnapshot Snapshot);

public sealed record CorruptedPartyEnvelope
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public Guid HostProfileUuid { get; init; }
    [JsonRequired] public string GroupKey { get; init; } = "";
    [JsonRequired] public long Revision { get; init; }
    [JsonRequired] public string TerminalRunId { get; init; } = "";
    [JsonRequired] public string Outcome { get; init; } = "";
    [JsonRequired] public CorruptedPartyMember[] Members { get; init; } = [];

    public void Validate(Guid host, IEnumerable<Guid> profiles)
    {
        var expected = profiles.ToArray();
        if (SchemaVersion != 1)
            throw new NotSupportedException($"Unsupported Corrupted Party schema {SchemaVersion}; file preserved.");
        if (HostProfileUuid != host || Revision < 1 || string.IsNullOrWhiteSpace(TerminalRunId) ||
            Outcome is not ("ArchitectWin" or "ArchitectLoss") || Members == null ||
            Members.Any(member => member == null || member.Snapshot == null) ||
            GroupKey != SuccessorGroup.Key(expected) ||
            !Members.Select(member => member.ProfileUuid).Order().SequenceEqual(expected.Order()))
            throw new JsonException("Corrupted Party lineage does not match its complete host/group membership.");
        foreach (var member in Members)
            member.Snapshot.Validate();
    }
}

public static class SuccessorGroup
{
    public static string Key(IEnumerable<Guid> profiles)
    {
        var members = profiles.Order().ToArray();
        if (members.Length is < 2 or > 4 || members.Any(id => id == Guid.Empty) ||
            members.Distinct().Count() != members.Length)
            throw new JsonException("A successor group requires two to four distinct persistent profile identities.");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join(",", members.Select(id => id.ToString("N"))))));
    }
}

public sealed record FrozenCorruptedParty
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public string RunId { get; init; } = "";
    [JsonRequired] public ulong HostNetId { get; init; }
    [JsonRequired] public Guid HostProfileUuid { get; init; }
    [JsonRequired] public string GroupKey { get; init; } = "";
    [JsonRequired] public SuccessorParticipant[] Participants { get; init; } = [];
    [JsonRequired] public CorruptedPartyEnvelope? Lineage { get; init; }

    [JsonIgnore] public string Digest => Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));

    public void Validate(IReadOnlyCollection<ulong> networkIds, ulong hostNetId)
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(RunId) || HostNetId != hostNetId ||
            Participants == null || Participants.Any(player => player == null) ||
            !Participants.Select(player => player.NetId).Order().SequenceEqual(networkIds.Order()) ||
            Participants.Select(player => player.NetId).Distinct().Count() != Participants.Length ||
            Participants.SingleOrDefault(player => player.NetId == HostNetId)?.ProfileUuid != HostProfileUuid)
            throw new JsonException("The host's frozen party does not match the original run participants.");
        var profiles = Participants.Select(player => player.ProfileUuid).ToArray();
        if (GroupKey != SuccessorGroup.Key(profiles))
            throw new JsonException("The host's successor group key does not match its profile identities.");
        Lineage?.Validate(HostProfileUuid, profiles);
    }
}
