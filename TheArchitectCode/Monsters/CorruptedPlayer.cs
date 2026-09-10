using System.Text.Json;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;
using TheArchitect.TheArchitectCode.UI;
using TheArchitect.TheArchitectCode.Encounters;
using TheArchitect.TheArchitectCode.Lifecycle;

namespace TheArchitect.TheArchitectCode.Monsters;

public sealed class CorruptedPlayer : CustomMonsterModel
{
    private CharacterModel? _character;
    private IReadOnlyList<JsonElement>? _deck;
    private string? _seed;
    private int _maxHp = 80;
    private CorruptedPlayerTelegraph? _telegraph;
    private bool _deathHandled;
    private CorruptedPartyPhase? _party;
    private int _partyIndex;
    private Player? _counterpart;
    public NativeCorruptedPlayer? Native { get; private set; }
    private CharacterModel Character => _character ?? ModelDb.Character<Ironclad>();
    public override LocString Title
    {
        get
        {
            if (_counterpart != null)
            {
                var playerTitle = new LocString("monsters", Id.Entry + ".playerName");
                playerTitle.Add("Player", PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform,
                    _counterpart.NetId));
                return playerTitle;
            }
            if (_character == null)
                return base.Title;
            var characterTitle = new LocString("monsters", Id.Entry + ".characters." + _character.Id.Entry);
            if (characterTitle.Exists())
                return characterTitle;
            var title = new LocString("monsters", Id.Entry + ".characterName");
            title.Add("Character", _character.Title);
            return title;
        }
    }
    public override int MinInitialHp => _maxHp;
    public override int MaxInitialHp => _maxHp;
    public override string? CustomAttackSfx => Character.AttackSfx;
    public override string? CustomCastSfx => Character.CastSfx;
    public override string? CustomDeathSfx => Character.DeathSfx;
    public override IEnumerable<string> AssetPaths => ModelDb.AllCharacters
        .SelectMany(character => character.AssetPaths).Concat(Character.AssetPaths).Distinct();

    public void Configure(CharacterModel character, int maxHp, IReadOnlyList<SerializableCard> deck, string seed) =>
        Configure(character, maxHp, deck.Select(card =>
            JsonSerializer.SerializeToElement(card, JsonSerializationUtility.GetTypeInfo<SerializableCard>())).ToArray(), seed);

    public void Configure(CharacterModel character, int maxHp, IReadOnlyList<JsonElement> deck, string seed,
        Player? counterpart = null)
    {
        AssertMutable();
        if (_deck != null)
            throw new InvalidOperationException("A Corrupted Player can only be configured once.");
        if (maxHp <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxHp));
        _character = character;
        _maxHp = maxHp;
        _deck = deck.Select(card => card.Clone()).ToArray();
        _seed = seed;
        _counterpart = counterpart;
    }

    internal void JoinParty(CorruptedPartyPhase party, int index)
    {
        AssertMutable();
        _party = party;
        _partyIndex = index;
    }

    public override NCreatureVisuals CreateCustomVisuals()
    {
        var visuals = Character.CreateVisuals();
        var body = visuals.GetNode<Node2D>("%Visuals");
        body.Scale = new Vector2(-body.Scale.X, body.Scale.Y);
        CorruptedPlayerCorruption.Attach(visuals);
        return visuals;
    }

    public override CreatureAnimator SetupCustomAnimationStates(MegaSprite controller) =>
        Character.GenerateAnimator(controller, Creature);

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        if (_deck == null || _seed == null)
            throw new InvalidOperationException("Configure the Corrupted Player snapshot before adding it to combat.");
        Creature.SetMaxHpInternal(_maxHp);
        Creature.SetCurrentHpInternal(_maxHp);
        Native = new NativeCorruptedPlayer(Creature, Character, _deck, _seed, _counterpart?.Creature);
        var node = Creature.GetCreatureNode();
        if (node != null)
        {
            CorruptedPlayerOrbs.Attach(node, Native.State);
            _telegraph = CorruptedPlayerTelegraph.Attach(node, Creature, ShowNativeState);
            Native.Changed += ShowNativeState;
            ShowNativeState();
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var move = new MoveState("CORRUPTED_PLAYER_NATIVE", ExecuteTurn);
        move.FollowUpState = move;
        return new MonsterMoveStateMachine([move], move);
    }

    private Task ExecuteTurn(IReadOnlyList<Creature> targets) =>
        (Native ?? throw new InvalidOperationException("The native Corrupted Player has not been initialized.")).ExecuteTurn();

    public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature,
        bool wasRemovalPrevented, float deathAnimLength)
    {
        if (creature != Creature || wasRemovalPrevented || _deathHandled ||
            Creature.CombatState is not { } combat)
            return;
        _deathHandled = true;
        // Count confirmed deaths, not zero HP: another member in a lethal AoE may still revive.
        var lastMember = (_party ??= new CorruptedPartyPhase(1)).Defeat(_partyIndex);
        StopTelegraph();
        if (lastMember && combat.PlayerCreatures.Any(player => player.IsAlive))
            await CreatureCmd.Add(ArchitectModels.Boss.ToMutable(), combat);
        if (Native != null)
            foreach (var pet in Native.State.Pets.ToArray())
                await CreatureCmd.Kill(pet, true);
        Native?.Cleanup();
    }

    internal void StopTelegraph()
    {
        if (GodotObject.IsInstanceValid(_telegraph))
            _telegraph!.Stop();
    }

    [HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
    internal static class CorruptedPlayerPartyHealthPatch
    {
        private static bool Prefix(Creature __instance, EncounterModel? encounter, ref int actIndex)
        {
            if (__instance.Monster is CorruptedPlayer)
                return false;
            actIndex = ArchitectMultiplayerScaling.GetActIndex(encounter, actIndex);
            return true;
        }
    }

    private void ShowNativeState()
    {
        if (Native != null && GodotObject.IsInstanceValid(_telegraph))
            Native.Show(_telegraph!);
    }
}
