using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace TheArchitect.TheArchitectCode.Persistence;

public sealed record ChallengerSnapshot(string CharacterId, int MaxHp, JsonElement[] Deck, string ContentHash)
{
    public static ChallengerSnapshot Capture(Player player)
    {
        var cards = player.Deck.Cards.Select(card =>
            JsonSerializer.SerializeToElement(card.ToSerializable(), JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
        var character = player.Character.Id.ToString();
        var hp = (int)player.Creature.MaxHp;
        return new(character, hp, cards, Hash(character, hp, cards));
    }

    public List<SerializableCard> RestoreDeck() => Deck.Select(card =>
        card.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>()) ??
        throw new JsonException("Snapshot contains a null card.")).ToList();

    public CharacterModel? ResolveCharacter() =>
        ModelDb.GetByIdOrNull<CharacterModel>(ModelId.Deserialize(CharacterId));

    internal static string Hash(string character, int hp, JsonElement[] cards)
    {
        var content = character + "|" + hp.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
            string.Join("|", cards.Select(card => JsonSerializer.Serialize(card)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }
}

public sealed record ChallengerEnvelope
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public long Revision { get; init; }
    [JsonRequired] public Guid ProfileUuid { get; init; } = Guid.NewGuid();
    public string WrittenByModVersion { get; init; } = "0.1.0";
    public string GameVersion { get; init; } = "0.111.0";
    public string? TerminalRunId { get; init; }
    public string? Outcome { get; init; }
    [JsonRequired] public ChallengerSnapshot? Snapshot { get; init; }
}

public static class ChallengerStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static string FilePath => ProjectSettings.GlobalizePath(
        SaveManager.Instance.GetProfileScopedPath("TheArchitect/challenger_snapshot.json"));

    public static ChallengerEnvelope? Load(bool forWrite = false) => Load(FilePath, forWrite);

    internal static ChallengerEnvelope? Load(string path, bool forWrite = false)
    {
        if (!File.Exists(path) && !File.Exists(path + ".backup"))
            return null;
        foreach (var candidate in new[] { path, path + ".backup" })
        {
            if (!File.Exists(candidate))
                continue;
            try
            {
                var envelope = JsonSerializer.Deserialize<ChallengerEnvelope>(File.ReadAllText(candidate), Options)
                    ?? throw new JsonException("Snapshot envelope is null.");
                if (envelope.SchemaVersion != 1)
                {
                    var message = $"Unsupported Challenger schema {envelope.SchemaVersion}; file preserved.";
                    if (forWrite)
                        throw new NotSupportedException(message);
                    MainFile.Logger.Warn(message);
                    return null;
                }
                Validate(envelope);
                return envelope;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                MainFile.Logger.Error($"Cannot load Challenger snapshot '{candidate}': {error.Message}");
            }
        }
        MainFile.Logger.Warn("No valid Challenger snapshot could be read. Using First Visit; corrupt files are preserved.");
        return null;
    }

    public static long? Commit(string terminalRunId, string outcome, ChallengerSnapshot snapshot) =>
        Commit(FilePath, terminalRunId, outcome, snapshot);

    internal static long? Commit(string path, string terminalRunId, string outcome, ChallengerSnapshot snapshot)
    {
        try
        {
            var prior = Load(path, forWrite: true);
            if (prior?.TerminalRunId == terminalRunId)
                return prior.Revision;
            var next = (prior ?? new ChallengerEnvelope()) with
            {
                Revision = checked((prior?.Revision ?? 0) + 1),
                TerminalRunId = terminalRunId,
                Outcome = outcome,
                Snapshot = snapshot
            };
            Validate(next);
            WriteAtomic(path, JsonSerializer.Serialize(next, Options),
                prior == null ? null : JsonSerializer.Serialize(prior, Options));
            return next.Revision;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            MainFile.Logger.Error($"Challenger snapshot was not recorded; previous snapshot preserved: {error}");
            return null;
        }
    }

    internal static void WriteAtomic(string path, string json, string? validatedBackup = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        if (validatedBackup != null)
        {
            var backupTemporary = path + ".backup.tmp";
            File.WriteAllText(backupTemporary, validatedBackup);
            File.Move(backupTemporary, path + ".backup", overwrite: true);
        }
        else if (File.Exists(path))
            File.Copy(path, path + ".invalid", overwrite: true);
        File.Move(temporary, path, overwrite: true);
    }

    private static void Validate(ChallengerEnvelope envelope)
    {
        if (envelope.ProfileUuid == Guid.Empty || envelope.Revision < 0 ||
            envelope.Snapshot is not { MaxHp: > 0, Deck: not null, CharacterId: not null } snapshot)
            throw new JsonException("Invalid Challenger envelope.");
        _ = ModelId.Deserialize(snapshot.CharacterId);
        if (snapshot.Deck.Any(card => card.ValueKind != JsonValueKind.Object))
            throw new JsonException("Invalid Challenger deck.");
        if (snapshot.ContentHash != ChallengerSnapshot.Hash(snapshot.CharacterId, snapshot.MaxHp, snapshot.Deck))
            throw new JsonException("Challenger snapshot content hash does not match.");
    }
}
