using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Ancients;
using TheArchitect.TheArchitectCode.Encounters;
using TheArchitect.TheArchitectCode.Monsters;
using TheArchitect.TheArchitectCode.Powers;

namespace TheArchitect.TheArchitectCode;

internal static partial class ArchitectModels
{
    internal static readonly ModelId ActId = new("ACT", "THEARCHITECT-ARCHITECT_ACT");
    internal static readonly ModelId EncounterId = new("ENCOUNTER", "THEARCHITECT-ARCHITECT_ENCOUNTER");
    internal static readonly ModelId AncientId = new("EVENT", "THEARCHITECT-THE_UNWRITTEN");
    private static readonly ModelId BossId = new("MONSTER", "THEARCHITECT-ARCHITECT_BOSS");
    private static readonly ModelId CorruptedPlayerId = new("MONSTER", "THEARCHITECT-CORRUPTED_PLAYER");
    private static readonly ModelId InvincibleId = new("POWER", "THEARCHITECT-ARCHITECT_INVINCIBLE_POWER");
    private static readonly ModelId BeatOfDeathId = new("POWER", "THEARCHITECT-ARCHITECT_BEAT_OF_DEATH_POWER");

    // Native generic lookups can bypass BaseLib's ID-prefix hook after normal startup.
    // Resolve the existing canonical IDs directly; do not register aliases or change saved IDs.
    internal static ArchitectAct Act => ModelDb.GetById<ArchitectAct>(ActId);
    internal static ArchitectEncounter Encounter => ModelDb.GetById<ArchitectEncounter>(EncounterId);
    internal static TheUnwritten Ancient => ModelDb.GetById<TheUnwritten>(AncientId);
    internal static ArchitectBoss Boss => ModelDb.GetById<ArchitectBoss>(BossId);
    internal static CorruptedPlayer CorruptedPlayer => ModelDb.GetById<CorruptedPlayer>(CorruptedPlayerId);
    internal static ArchitectInvinciblePower Invincible => ModelDb.GetById<ArchitectInvinciblePower>(InvincibleId);
    internal static ArchitectBeatOfDeathPower BeatOfDeath => ModelDb.GetById<ArchitectBeatOfDeathPower>(BeatOfDeathId);
}
