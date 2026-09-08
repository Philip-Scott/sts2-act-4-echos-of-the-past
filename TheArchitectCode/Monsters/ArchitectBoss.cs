using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Random;
using TheArchitect.TheArchitectCode.Powers;
using VanillaArchitect = MegaCrit.Sts2.Core.Models.Monsters.Architect;

namespace TheArchitect.TheArchitectCode.Monsters;

public sealed class ArchitectBoss : CustomMonsterModel
{
    private int _iterations;

    public override int MinInitialHp => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 800, 750);
    public override int MaxInitialHp => MinInitialHp;
    public override bool HasDeathSfx => false;
    public override float DeathAnimLengthOverride => 0.5f;
    public int DamageLimit => AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, 200, 300);
    public int CompileHits => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 15, 12);
    public int ExecuteDamage => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 45, 40);
    public int StartingBeatOfDeath => AscensionHelper.GetValueIfAscension(AscensionLevel.DeadlyEnemies, 2, 1);
    public int NextIteration => _iterations + 1;
    public int OrdinaryStrengthGain => 2 - Math.Min(0, Creature.GetPowerAmount<StrengthPower>());

    public override IEnumerable<string> AssetPaths =>
        ModelDb.Monster<VanillaArchitect>().AssetPaths.Concat(GenerateMoveStateMachine().States.Values
            .OfType<MoveState>().SelectMany(move => move.Intents).SelectMany(intent => intent.AssetPaths));

    public override NCreatureVisuals CreateCustomVisuals()
    {
        var visuals = ModelDb.Monster<VanillaArchitect>().CreateVisuals();
        visuals.Modulate = new Color(0.83f, 0.72f, 1f);
        return visuals;
    }

    public override CreatureAnimator SetupCustomAnimationStates(MegaSprite controller) =>
        SetupAnimationState(controller, "idle_loop", deadName: "hurt", deadLoop: false,
            hitName: "hurt", attackName: "attack", castName: "attack");

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply(context, ArchitectModels.Invincible.ToMutable(), Creature, DamageLimit, Creature, null);
        await PowerCmd.Apply(context, ArchitectModels.BeatOfDeath.ToMutable(), Creature, StartingBeatOfDeath, Creature, null);
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var rewrite = new MoveState("REWRITE_MOVE", Rewrite, new RewriteIntent());
        var compileFirst = new MoveState("COMPILE_MOVE", Compile, new CompileIntent(CompileHits));
        var executeFirst = new MoveState("EXECUTE_MOVE", Execute, new ExecuteIntent(ExecuteDamage));
        var compileSecond = new MoveState("COMPILE_SECOND_MOVE", Compile, new CompileIntent(CompileHits));
        var executeSecond = new MoveState("EXECUTE_SECOND_MOVE", Execute, new ExecuteIntent(ExecuteDamage));
        var iterate = new MoveState("ITERATE_MOVE", Iterate, new IterateIntent(this));
        var cycle = new AttackCycleState(compileFirst.Id, executeFirst.Id);
        rewrite.FollowUpState = cycle;
        compileFirst.FollowUpState = executeSecond;
        executeFirst.FollowUpState = compileSecond;
        compileSecond.FollowUpState = iterate;
        executeSecond.FollowUpState = iterate;
        iterate.FollowUpState = cycle;
        return new MonsterMoveStateMachine(
            [rewrite, cycle, compileFirst, executeFirst, compileSecond, executeSecond, iterate], rewrite);
    }

    private async Task Rewrite(IReadOnlyList<Creature> targets)
    {
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<VulnerablePower>(context, targets, 2, Creature, null);
        await PowerCmd.Apply<WeakPower>(context, targets, 2, Creature, null);
        await PowerCmd.Apply<FrailPower>(context, targets, 2, Creature, null);
        await InsertStatus<Dazed>(targets);
        await InsertStatus<Slimed>(targets);
        await InsertStatus<Wound>(targets);
        await InsertStatus<Burn>(targets);
        await InsertStatus<MegaCrit.Sts2.Core.Models.Cards.Void>(targets);
    }

    private static Task InsertStatus<T>(IReadOnlyList<Creature> targets) where T : CardModel =>
        CardPileCmd.AddToCombatAndPreview<T>(targets, PileType.Draw, 1, null, CardPilePosition.Random);

    private Task Compile(IReadOnlyList<Creature> targets) =>
        new AttackCommand(2).FromMonster(this).WithHitCount(CompileHits).Execute(null);

    private Task Execute(IReadOnlyList<Creature> targets) =>
        new AttackCommand(ExecuteDamage).FromMonster(this).Execute(null);

    private async Task Iterate(IReadOnlyList<Creature> targets)
    {
        var context = new ThrowingPlayerChoiceContext();
        // STS2 represents pending Strength Down as positive TemporaryStrengthPower instances.
        foreach (var power in Creature.Powers.OfType<TemporaryStrengthPower>()
                     .Where(power => power.Type == PowerType.Buff).ToArray())
            await PowerCmd.Remove(power);
        await PowerCmd.Apply<StrengthPower>(context, Creature, OrdinaryStrengthGain, Creature, null);
        _iterations++;
        switch (_iterations)
        {
            case 1:
                await PowerCmd.Apply<ArtifactPower>(context, Creature, 2, Creature, null);
                break;
            case 2:
                await PowerCmd.Apply(context, ArchitectModels.BeatOfDeath.ToMutable(), Creature, 1, Creature, null);
                break;
            case 3:
                await PowerCmd.Apply<PainfulStabsPower>(context, Creature, 1, Creature, null);
                break;
            default:
                await PowerCmd.Apply<StrengthPower>(context, Creature, _iterations == 4 ? 10 : 50, Creature, null);
                break;
        }
    }

    private static LocString MoveDescription(string key) =>
        new("monsters", ArchitectModels.Boss.Id.Entry + ".moves." + key);

    private sealed class RewriteIntent : DebuffIntent
    {
        protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner) =>
            MoveDescription("REWRITE_MOVE.description");
    }

    private sealed class CompileIntent(int hits) : MultiAttackIntent(2, hits)
    {
        protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
        {
            var description = MoveDescription("COMPILE_MOVE.description");
            description.Add("Damage", GetSingleDamage(targets, owner));
            description.Add("Hits", Repeats);
            return description;
        }
    }

    private sealed class ExecuteIntent(int damage) : SingleAttackIntent(damage)
    {
        protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
        {
            var description = MoveDescription("EXECUTE_MOVE.description");
            description.Add("Damage", GetTotalDamage(targets, owner));
            return description;
        }
    }

    private sealed class IterateIntent(ArchitectBoss boss) : BuffIntent
    {
        protected override LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner)
        {
            var description = MoveDescription($"ITERATE_MOVE.description{Math.Min(5, boss.NextIteration)}");
            description.Add("Strength", 2 - Math.Min(0, owner.GetPowerAmount<StrengthPower>()));
            return description;
        }
    }

    private sealed class AttackCycleState(string compile, string execute) : MonsterState
    {
        public override string Id => "ATTACK_CYCLE";
        public override bool ShouldAppearInLogs => false;
        public override string GetNextState(Creature owner, Rng rng) => rng.NextBool() ? compile : execute;
        public override void RegisterStates(Dictionary<string, MonsterState> monsterStates) => monsterStates.Add(Id, this);
    }
}
