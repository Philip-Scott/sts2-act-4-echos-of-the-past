using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using HarmonyLib;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Persistence;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Encounters;

public sealed class ArchitectEncounter : CustomEncounterModel
{
    private IRunState? _run;

    internal void Prepare(IRunState run) => _run = run;

    public ArchitectEncounter() : base(RoomType.Boss, autoAdd: false) { }

    public override bool IsValidForAct(ActModel act) => act is ArchitectAct;
    public override bool ShouldGiveRewards => false;
    public override bool HasScene => false;
    public override string BossNodePath => "res://TheArchitect/images/map/architect_boss";
    public override IReadOnlyList<string> Slots => [];
    public override string? CustomRunHistoryIconPath => BossNodePath + ".png";
    public override string? CustomRunHistoryIconOutlinePath => BossNodePath + "_outline.png";
    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        [ArchitectModels.Boss, ArchitectModels.CorruptedPlayer];

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        var run = _run ?? throw new InvalidOperationException("Architect encounter has no run context.");
        var envelope = ArchitectRun.Get(run).EntrySnapshot;
        if (envelope?.Snapshot is not { } snapshot)
            return [(ArchitectModels.Boss.ToMutable(), null)];
        if (NativeCardSupport.Preflight(snapshot.Deck) is { } error)
        {
            NativeCorruptedPlayerPreflight.Show(error);
            throw new NotSupportedException(error);
        }
        var character = snapshot.ResolveCharacter();
        if (character == null)
        {
            MainFile.Logger.Warn("Corrupted Player character unavailable at encounter entry; using First Visit.");
            return [(ArchitectModels.Boss.ToMutable(), null)];
        }
        var corruptedPlayer = (CorruptedPlayer)ArchitectModels.CorruptedPlayer.ToMutable();
        corruptedPlayer.Configure(character, snapshot.MaxHp, snapshot.Deck,
            $"{run.Rng.StringSeed}|{envelope.Revision}|{snapshot.ContentHash}|{Id}");
        return [(corruptedPlayer, null)];
    }
}

[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GenerateMonstersWithSlots))]
internal static class PrepareArchitectEncounterPatch
{
    private static void Prefix(EncounterModel __instance, IRunState runState)
    {
        if (__instance is ArchitectEncounter encounter)
            encounter.Prepare(runState);
    }
}
