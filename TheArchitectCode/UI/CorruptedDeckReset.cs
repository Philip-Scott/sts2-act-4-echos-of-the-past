using System.IO;
using System.Text.Json;
using BaseLib.Patches.Saves;
using HarmonyLib;
using MegaCrit.Sts2.Core.Exceptions;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using TheArchitect.TheArchitectCode.Persistence;

namespace TheArchitect.TheArchitectCode.UI;

internal static class CorruptedDeckReset
{
    internal static async Task<bool> TryReset(Func<Task> reset)
    {
        try
        {
            await reset();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or InvalidOperationException or NotSupportedException or SaveException)
        {
            MainFile.Logger.Error($"Corrupted deck reset did not finish: {error}");
            var popup = NErrorPopup.Create(CorruptedDeckButton.Localize("RESET_ERROR_TITLE"),
                CorruptedDeckButton.Localize("RESET_ERROR") + "\n\n" + error.Message, false);
            if (popup != null)
                (NModalContainer.Instance ?? throw new InvalidOperationException("The error popup container is unavailable."))
                    .Add(popup);
            return false;
        }
    }

    internal static async Task ResetSavedRun(SerializableRun save, ulong? host)
    {
        var manager = SaveManager.Instance;
        var profileId = manager.CurrentProfileId;
        void RequireOriginalProfile()
        {
            if (manager.CurrentProfileId != profileId)
                throw new InvalidOperationException("The active profile changed before the reset finished. Return to the original profile to retry.");
        }
        var strings = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save].DictForType<string>();
        strings.TryGetValue(ContinueRunPreviewSelection.SaveKey, out var json);
        var selection = CorruptedDeckResetSelection.Create(json, save.Players.Select(player => player.NetId).ToArray(),
            host, host == null ? null : CorruptedPlayerStore.GetProfileUuid());
        var corrected = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(save, JsonSerializationUtility.GetTypeInfo<SerializableRun>()),
            JsonSerializationUtility.GetTypeInfo<SerializableRun>()) ??
            throw new JsonException("Cannot copy the saved run for the reset.");
        ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[corrected].DictForType<string>()
            [ContinueRunPreviewSelection.SaveKey] = selection.Json;
        if (manager.CurrentRunSaveTask is { } pending)
            await pending;
        RequireOriginalProfile();
        var runSaves = (RunSaveManager)AccessTools.Field(typeof(SaveManager), "_runSaveManager").GetValue(manager)!;
        await runSaves.SaveRun(corrected, isMultiplayer: host != null);
        strings[ContinueRunPreviewSelection.SaveKey] = selection.Json;
        RequireOriginalProfile();
        // The native store backs up the previous primary. A second write also removes the bad deck from recovery.
        await runSaves.SaveRun(corrected, isMultiplayer: host != null);
        RequireOriginalProfile();
        var written = host == null ? runSaves.LoadRunSave() : runSaves.LoadMultiplayerRunSave();
        if (!written.Success || written.SaveData == null ||
            !ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[written.SaveData].DictForType<string>()
                .TryGetValue(ContinueRunPreviewSelection.SaveKey, out var savedJson) || savedJson != selection.Json)
            throw new IOException("The corrected opponent could not be verified in the saved run.");
        if (selection.Party is { } party)
            CorruptedPartyStore.Delete(CorruptedPlayerStore.ProfileDirectory, party.HostProfileUuid,
                party.Participants.Select(player => player.ProfileUuid).ToArray());
        else
            CorruptedPlayerStore.Delete();
    }
}
