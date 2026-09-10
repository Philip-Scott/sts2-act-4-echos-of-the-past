using System.Text.Json;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.Multiplayer;

public sealed class PartyEntryConsensus
{
    private readonly HashSet<ulong> participants;
    private readonly HashSet<ulong> acknowledgements = [];
    public FrozenCorruptedParty Party { get; }
    public string Digest { get; }
    public string? Error { get; private set; }
    public bool Ready => Error == null && acknowledgements.SetEquals(participants);

    public PartyEntryConsensus(FrozenCorruptedParty party, IReadOnlyCollection<ulong> originalPlayers, ulong host)
    {
        party.Validate(originalPlayers, host);
        Party = party;
        Digest = party.Digest;
        participants = originalPlayers.ToHashSet();
    }

    public void Acknowledge(ulong sender, string digest)
    {
        if (!participants.Contains(sender))
            throw new JsonException("A non-participant acknowledged the Architect party.");
        if (digest != Digest)
            throw new JsonException("Architect party acknowledgement has a different frozen snapshot digest.");
        if (Error != null)
            throw new InvalidOperationException(Error);
        acknowledgements.Add(sender);
    }

    public void Reject(string error) => Error ??= error;
}

internal sealed class PartyPayloadBuffer
{
    internal const int ChunkSize = 16000;
    internal const int MaximumChunks = 256;
    private string[]? chunks;
    private string? digest;
    private int total;

    public string? Add(string payloadDigest, int index, int count, string chunk)
    {
        if (count is < 1 or > MaximumChunks || index < 0 || index >= count ||
            chunk.Length > ChunkSize || payloadDigest.Length != 64)
            throw new JsonException("Invalid Architect party packet size.");
        if (chunks == null)
        {
            chunks = new string[count];
            digest = payloadDigest;
        }
        if (chunks.Length != count || digest != payloadDigest)
            throw new JsonException("Conflicting Architect party transfers.");
        if (chunks[index] != null && chunks[index] != chunk)
            throw new JsonException("Conflicting Architect party packet contents.");
        if (chunks[index] == null)
        {
            chunks[index] = chunk;
            total++;
        }
        return total == chunks.Length ? string.Concat(chunks) : null;
    }
}
