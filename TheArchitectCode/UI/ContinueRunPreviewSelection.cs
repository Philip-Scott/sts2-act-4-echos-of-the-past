using System.Text.Json;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.UI;

internal sealed record ContinueRunPreviewSelection(CorruptedPlayerSnapshot[] Snapshots, string? StatusKey)
{
    internal const string SaveKey = "TheArchitect.Run.v1";

    internal static ContinueRunPreviewSelection Read(string? json, IReadOnlyCollection<ulong> players)
    {
        if (string.IsNullOrEmpty(json))
            return new([], "NOT_SELECTED");
        var state = JsonSerializer.Deserialize<ArchitectRun>(json) ??
            throw new JsonException("Architect run state is null.");
        if (players.Count > 1)
        {
            var party = state.EntryParty ?? state.PendingParty;
            if (party == null)
            {
                if (state.Entered)
                    throw new JsonException("The saved multiplayer run is missing its frozen Corrupted Party.");
                return new([], "NOT_SELECTED");
            }
            party.Validate(players, state.StartingHostNetId ??
                throw new JsonException("The saved multiplayer run is missing its starting host."));
            if (state.Origin is not { } origin || state.Id != party.RunId ||
                origin.RunId != party.RunId || origin.HostNetId != party.HostNetId ||
                origin.HostProfileUuid != party.HostProfileUuid || origin.GroupKey != party.GroupKey ||
                !origin.Participants.SequenceEqual(party.Participants) ||
                (state.EntryParty != null) != state.Entered)
                throw new JsonException("The saved Corrupted Party does not match its run lineage.");
            return Selected(party.Lineage?.Members.Select(member => member.Snapshot).ToArray() ?? []);
        }
        if (state.EntryParty != null || state.PendingParty != null || state.Origin != null ||
            state.StartingHostNetId != null)
            throw new JsonException("A saved multiplayer lineage cannot be previewed as a solo run.");
        if (!state.Entered)
            return new([], "NOT_SELECTED");
        if (state.EntrySnapshot is not { } envelope)
            return Selected([]);
        if (envelope.SchemaVersion != 1 || envelope.Revision < 0 || envelope.ProfileUuid == Guid.Empty ||
            envelope.Snapshot == null)
            throw new JsonException("The saved Corrupted Player envelope is invalid.");
        envelope.Snapshot.Validate();
        return Selected([envelope.Snapshot]);
    }

    private static ContinueRunPreviewSelection Selected(CorruptedPlayerSnapshot[] snapshots) =>
        new(snapshots, snapshots.Length == 0 ? "FROZEN_FIRST_VISIT" : null);
}
