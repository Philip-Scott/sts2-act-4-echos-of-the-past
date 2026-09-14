using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

namespace TheArchitect.TheArchitectCode.Persistence;

public sealed partial record CorruptedPlayerSnapshot
{
    public static CorruptedPlayerSnapshot Capture(Player player)
    {
        var cards = player.Deck.Cards.Select(card =>
            JsonSerializer.SerializeToElement(
                NativeCardSerialization.ToSerializable(card),
                JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray();
        var character = player.Character.Id.ToString();
        var hp = (int)player.Creature.MaxHp;
        return new(character, hp, cards, Hash(character, hp, cards));
    }

    public List<SerializableCard> RestoreDeck() => Deck.Select(card =>
        card.Deserialize(JsonSerializationUtility.GetTypeInfo<SerializableCard>()) ??
        throw new JsonException("Snapshot contains a null card.")).ToList();

    public CharacterModel? ResolveCharacter() =>
        ModelDb.GetByIdOrNull<CharacterModel>(ModelId.Deserialize(CharacterId));

}

public static class CorruptedPlayerStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static string FilePath => ProjectSettings.GlobalizePath(
        SaveManager.Instance.GetProfileScopedPath("TheArchitect/corrupted_player_snapshot.json"));

    internal static string ProfileDirectory => Path.GetDirectoryName(FilePath)!;

    public static Guid GetProfileUuid() => ProfileIdentityStore.GetOrCreate(
        Path.Combine(ProfileDirectory, "profile_identity.json"), () => Load()?.ProfileUuid);

    public static CorruptedPlayerEnvelope? Load(bool forWrite = false) => Load(FilePath, forWrite);

    internal static CorruptedPlayerEnvelope? Load(string path, bool forWrite = false)
    {
        if (!File.Exists(path) && !File.Exists(path + ".backup"))
            return null;
        foreach (var candidate in new[] { path, path + ".backup" })
        {
            if (!File.Exists(candidate))
                continue;
            try
            {
                var envelope = JsonSerializer.Deserialize<CorruptedPlayerEnvelope>(File.ReadAllText(candidate), Options)
                    ?? throw new JsonException("Snapshot envelope is null.");
                if (envelope.SchemaVersion != 1)
                {
                    var message = $"Unsupported Corrupted Player schema {envelope.SchemaVersion}; file preserved.";
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
                MainFile.Logger.Error($"Cannot load Corrupted Player snapshot '{candidate}': {error.Message}");
            }
        }
        MainFile.Logger.Warn("No valid Corrupted Player snapshot could be read. Using First Visit; corrupt files are preserved.");
        return null;
    }

    public static long? Commit(string terminalRunId, string outcome, CorruptedPlayerSnapshot snapshot) =>
        Commit(FilePath, terminalRunId, outcome, snapshot, GetProfileUuid());

    internal static long? Commit(string path, string terminalRunId, string outcome, CorruptedPlayerSnapshot snapshot,
        Guid? profileUuid = null)
    {
        try
        {
            var prior = Load(path, forWrite: true);
            if (prior?.TerminalRunId == terminalRunId)
                return prior.Revision;
            var next = (prior ?? new CorruptedPlayerEnvelope { ProfileUuid = profileUuid ?? Guid.NewGuid() }) with
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
            MainFile.Logger.Error($"Corrupted Player snapshot was not recorded; previous snapshot preserved: {error}");
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

    private static void Validate(CorruptedPlayerEnvelope envelope)
    {
        if (envelope.ProfileUuid == Guid.Empty || envelope.Revision < 0 ||
            envelope.Snapshot is not { MaxHp: > 0, Deck: not null, CharacterId: not null } snapshot)
            throw new JsonException("Invalid Corrupted Player envelope.");
        _ = ModelId.Deserialize(snapshot.CharacterId);
        snapshot.Validate();
    }
}
