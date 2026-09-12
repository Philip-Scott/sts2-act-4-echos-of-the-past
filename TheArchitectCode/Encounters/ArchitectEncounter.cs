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
        var state = ArchitectRun.Get(run);
        var snapshots = state.EncounterSnapshots;
        if (snapshots.Count == 0)
            return [(ArchitectModels.Boss.ToMutable(), null)];
        var characters = snapshots.Select(snapshot => snapshot.ResolveCharacter()).ToArray();
        if (characters.Any(character => character == null))
        {
            if (run.Players.Count == 1)
            {
                MainFile.Logger.Warn("Corrupted Player character unavailable at encounter entry; using First Visit.");
                return [(ArchitectModels.Boss.ToMutable(), null)];
            }
            const string error = "A saved Corrupted Player character is unavailable. Install the matching character mod before entering.";
            NativeCorruptedPlayerPreflight.Show(error);
            throw new NotSupportedException(error);
        }
        foreach (var snapshot in snapshots)
        {
            if (NativeCardSupport.Preflight(snapshot.Deck) is not { } error)
                continue;
            NativeCorruptedPlayerPreflight.Show(error);
            throw new NotSupportedException(error);
        }
        var phase = new CorruptedPartyPhase(snapshots.Count);
        var formation = run.Players.OrderBy(player => player.NetId != state.StartingHostNetId).ToArray();
        var monsters = new List<(MonsterModel, string?)>();
        for (var index = 0; index < snapshots.Count; index++)
        {
            var snapshot = snapshots[index];
            var counterpart = state.EncounterCounterpartNetId(index) is { } netId
                ? run.Players.Single(player => player.NetId == netId)
                : null;
            var corruptedPlayer = (CorruptedPlayer)ArchitectModels.CorruptedPlayer.ToMutable();
            corruptedPlayer.Configure(characters[index]!,
                CorruptedPlayerHealth.CalculateMaxHp(snapshot.MaxHp, run.AscensionLevel), snapshot.Deck,
                $"{run.Rng.StringSeed}|{state.EncounterRevision}|{snapshot.ContentHash}|{Id}" +
                (snapshots.Count > 1 ? $"|party:{index}" : ""), counterpart);
            corruptedPlayer.JoinParty(phase, index,
                counterpart == null ? index : Array.IndexOf(formation, counterpart));
            monsters.Add((corruptedPlayer, null));
        }
        return monsters;
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
