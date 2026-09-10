using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheArchitect.TheArchitectCode.Persistence;

public static class ProfileIdentityStore
{
    private sealed record Identity
    {
        [JsonRequired] public int SchemaVersion { get; init; } = 1;
        [JsonRequired] public Guid ProfileUuid { get; init; }
    }

    public static Guid GetOrCreate(string path, Func<Guid?> existingSnapshotIdentity)
    {
        if (File.Exists(path))
        {
            var identity = JsonSerializer.Deserialize<Identity>(File.ReadAllText(path));
            if (identity is not { SchemaVersion: 1 } || identity.ProfileUuid == Guid.Empty)
                throw new JsonException("Architect profile identity is invalid; restore it before playing multiplayer.");
            return identity.ProfileUuid;
        }
        var uuid = existingSnapshotIdentity() ?? Guid.NewGuid();
        if (uuid == Guid.Empty)
            throw new JsonException("Architect profile identity is empty.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new Identity { ProfileUuid = uuid });
        var pending = path + ".pending";
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, path);
        return uuid;
    }
}

public static class CorruptedPartyStore
{
    private static string FilePath(string directory, Guid host, IEnumerable<Guid> participants) =>
        Path.Combine(directory, "multiplayer", host.ToString("N"), SuccessorGroup.Key(participants) + ".json");

    public static CorruptedPartyEnvelope? Load(string directory, Guid host, Guid[] participants)
    {
        var path = FilePath(directory, host, participants);
        if (!File.Exists(path) && !File.Exists(path + ".backup"))
            return null;
        Exception? failure = null;
        foreach (var candidate in new[] { path, path + ".backup" })
        {
            if (!File.Exists(candidate))
                continue;
            try
            {
                var envelope = JsonSerializer.Deserialize<CorruptedPartyEnvelope>(File.ReadAllText(candidate))
                    ?? throw new JsonException("Corrupted Party envelope is null.");
                envelope.Validate(host, participants);
                return envelope;
            }
            catch (Exception error) when (error is JsonException or IOException)
            {
                failure = error;
            }
        }
        throw new JsonException("No complete, validated Corrupted Party lineage can be read; files preserved.", failure);
    }

    public static long Commit(string directory, Guid localProfile, FrozenCorruptedParty frozen,
        string outcome, CorruptedPartyMember[] members)
    {
        frozen.Validate(frozen.Participants.Select(player => player.NetId).ToArray(), frozen.HostNetId);
        if (localProfile != frozen.HostProfileUuid)
            throw new InvalidOperationException("Only the original host profile may write this successor lineage.");
        var profiles = frozen.Participants.Select(player => player.ProfileUuid).ToArray();
        var prior = Load(directory, localProfile, profiles);
        if (prior?.TerminalRunId == frozen.RunId)
            return prior.Revision;
        var next = new CorruptedPartyEnvelope
        {
            HostProfileUuid = localProfile,
            GroupKey = frozen.GroupKey,
            Revision = checked((prior?.Revision ?? 0) + 1),
            TerminalRunId = frozen.RunId,
            Outcome = outcome,
            Members = members.OrderBy(member => member.ProfileUuid).ToArray()
        };
        next.Validate(localProfile, profiles);
        AtomicSnapshotFile.Write(FilePath(directory, localProfile, profiles),
            JsonSerializer.Serialize(next), prior == null ? null : JsonSerializer.Serialize(prior));
        return next.Revision;
    }
}

internal static class AtomicSnapshotFile
{
    internal static void Write(string path, string json, string? validatedBackup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        WriteFlushed(path + ".pending", json);
        if (validatedBackup != null)
        {
            WriteFlushed(path + ".backup.pending", validatedBackup);
            File.Move(path + ".backup.pending", path + ".backup", overwrite: true);
        }
        else if (File.Exists(path))
            File.Copy(path, path + ".invalid", overwrite: true);
        File.Move(path + ".pending", path, overwrite: true);
    }

    private static void WriteFlushed(string path, string json)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(json));
        stream.Flush(flushToDisk: true);
    }
}
