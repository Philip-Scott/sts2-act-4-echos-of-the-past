using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

// Bind only an already-loaded, verified mod. No Downfall assembly reference or load request.
internal sealed class NativeDownfallApi
{
    internal static readonly Version SupportedVersion = new(0, 1, 16, 0);
    internal Type Hexaghost { get; }
    internal Type SlimeBoss { get; }
    internal Type Ghostflame { get; }
    internal Type Stance { get; }
    internal Type NoStance { get; }
    internal Type Slime { get; }
    internal Type Spellbook { get; }
    internal Action<Player> ResetWheel { get; }
    internal Action<Player> ActivateWheel { get; }
    internal Action<Player> RefreshWheel { get; }
    internal Action<Player> HideWheel { get; }
    internal Func<Player, AbstractModel[]> GetWheel { get; }
    internal Func<Player, bool> IsIgnited { get; }
    internal Func<Player, int> GetWheelIndex { get; }
    internal Func<PlayerChoiceContext, Player, AbstractModel?, bool, bool, Task> Advance { get; }
    internal Func<Player, AbstractModel> GetStance { get; }
    internal Action<Player> RefreshSpellbook { get; }
    internal Func<Player, CardPile> GetSpellbook { get; }
    internal Action<Player, int> SetSlimeSlots { get; }
    internal Func<Player, int> GetSlimeSlots { get; }
    internal MethodInfo GhostCombatGetter { get; }
    internal MethodInfo StanceCombatGetter { get; }
    internal MethodInfo GhostExecuteWithContext { get; }
    internal MethodInfo SpellbookGetter { get; }
    internal MethodInfo SlimeCreationMoveNext { get; }
    internal FieldInfo SlimeCreationPlayer { get; }
    private readonly MethodInfo _ghostOwner;
    private readonly MethodInfo _stanceOwner;

    internal static bool TryBind(Assembly? assembly, out NativeDownfallApi? api, out string? reason)
    {
        api = null;
        reason = null;
        if (assembly == null)
            return false;
        var name = assembly.GetName();
        if (name.Name != "Downfall" || name.Version != SupportedVersion)
        {
            reason = $"Downfall compatibility requires the verified 0.1.16 API (found {name.Name} {name.Version}).";
            return false;
        }
        try
        {
            api = new NativeDownfallApi(assembly);
            return true;
        }
        catch (NotSupportedException error)
        {
            reason = error.Message;
            return false;
        }
    }

    private NativeDownfallApi(Assembly assembly)
    {
        Type Type(string name) => assembly.GetType(name, throwOnError: false) ??
            throw new NotSupportedException($"Downfall compatibility API is missing type {name}.");
        Hexaghost = Type("Hexaghost.HexaghostCode.Core.Hexaghost");
        SlimeBoss = Type("SlimeBoss.SlimeBossCode.Core.SlimeBoss");
        Ghostflame = Type("Hexaghost.HexaghostCode.Core.GhostflameModel");
        Stance = Type("Champ.ChampCode.Core.ChampStanceModel");
        NoStance = Type("Champ.ChampCode.Stance.ChampNoStance");
        Slime = Type("SlimeBoss.SlimeBossCode.Slimes.SlimeModel");
        var hexModel = Type("Hexaghost.HexaghostCode.Core.HexaghostModel");
        var hexCmd = Type("Hexaghost.HexaghostCode.Core.HexaghostCmd");
        var champ = Type("Champ.ChampCode.Core.ChampModel");
        var awakened = Type("Awakened.AwakenedCode.Core.AwakenedCmd");
        Spellbook = Type("Awakened.AwakenedCode.Piles.AwakenedPile");
        var slimes = Type("SlimeBoss.SlimeBossCode.Core.SlimeQueue");
        if (!typeof(CharacterModel).IsAssignableFrom(Hexaghost) ||
            !typeof(CharacterModel).IsAssignableFrom(SlimeBoss) ||
            !typeof(AbstractModel).IsAssignableFrom(Ghostflame) ||
            !typeof(AbstractModel).IsAssignableFrom(Stance) ||
            !Stance.IsAssignableFrom(NoStance) ||
            !typeof(MonsterModel).IsAssignableFrom(Slime) ||
            !typeof(CardPile).IsAssignableFrom(Spellbook))
            throw new NotSupportedException("Downfall compatibility model inheritance changed.");

        ResetWheel = Static(hexModel, "ResetWheel", typeof(void), typeof(Player)).CreateDelegate<Action<Player>>();
        ActivateWheel = Static(hexCmd, "ActivateGhostwheel", typeof(void), typeof(Player)).CreateDelegate<Action<Player>>();
        RefreshWheel = Static(hexCmd, "Refresh", typeof(void), typeof(Player)).CreateDelegate<Action<Player>>();
        HideWheel = Static(Type("Hexaghost.HexaghostCode.Core.HexaghostVisualsBridge"),
            "FadeFlamesOnDeath", typeof(void), typeof(Player)).CreateDelegate<Action<Player>>();
        GetWheel = Static(hexCmd, "GetWheel", Ghostflame.MakeArrayType(), typeof(Player))
            .CreateDelegate<Func<Player, AbstractModel[]>>();
        IsIgnited = Static(hexCmd, "IsIgnited", typeof(bool), typeof(Player)).CreateDelegate<Func<Player, bool>>();
        GetWheelIndex = Static(hexCmd, "GetCurrentIndex", typeof(int), typeof(Player)).CreateDelegate<Func<Player, int>>();
        Advance = Static(hexCmd, "Advance", typeof(Task), typeof(PlayerChoiceContext), typeof(Player),
            typeof(AbstractModel), typeof(bool), typeof(bool))
            .CreateDelegate<Func<PlayerChoiceContext, Player, AbstractModel?, bool, bool, Task>>();
        GetStance = Static(champ, "GetStanceModel", Stance, typeof(Player)).CreateDelegate<Func<Player, AbstractModel>>();
        RefreshSpellbook = Static(awakened, "RefreshSpellbook", typeof(void), typeof(Player))
            .CreateDelegate<Action<Player>>();
        SpellbookGetter = Static(awakened, "GetSpellbook", Spellbook, typeof(Player));
        var spellbookField = Spellbook.GetField("Spellbook", BindingFlags.Public | BindingFlags.Static);
        if (spellbookField?.FieldType != typeof(PileType))
            throw new NotSupportedException("Downfall spellbook pile registration changed.");
        var spellbookId = (PileType)spellbookField.GetValue(null)!;
        GetSpellbook = player =>
        {
            var pile = CustomPiles.GetCustomPile(player.PlayerCombatState, spellbookId);
            return Spellbook.IsInstanceOfType(pile) ? pile! :
                throw new InvalidOperationException("Downfall spellbook is unavailable in this player's combat state.");
        };
        SetSlimeSlots = Static(slimes, "SetSlots", typeof(void), typeof(Player), typeof(int))
            .CreateDelegate<Action<Player, int>>();
        GetSlimeSlots = Static(slimes, "GetSlots", typeof(int), typeof(Player)).CreateDelegate<Func<Player, int>>();
        GhostCombatGetter = Instance(Ghostflame, "get_CombatState", typeof(ICombatState));
        StanceCombatGetter = Instance(Stance, "get_CombatState", typeof(ICombatState));
        _ghostOwner = Instance(Ghostflame, "get_Owner", typeof(Player));
        _stanceOwner = Instance(Stance, "get_Owner", typeof(Player));
        GhostExecuteWithContext = Instance(Ghostflame, "ExecuteWithContext", typeof(Task),
            typeof(Func<PlayerChoiceContext, Task>));
        var creation = Static(slimes, "AddSlime", typeof(Task<(bool, int)>), typeof(Player), Slime);
        var machine = creation.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType ??
            throw new NotSupportedException("Downfall slime creation is no longer an async state machine.");
        SlimeCreationMoveNext = Instance(machine, "MoveNext", typeof(void));
        SlimeCreationPlayer = machine.GetField("player", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("Downfall slime creation no longer exposes its owner.");
        if (SlimeCreationPlayer.FieldType != typeof(Player))
            throw new NotSupportedException("Downfall slime creation owner type changed.");
    }

    internal Player Owner(AbstractModel model)
    {
        var getter = Ghostflame.IsInstanceOfType(model) ? _ghostOwner :
            Stance.IsInstanceOfType(model) ? _stanceOwner :
            throw new NotSupportedException($"Unrecognized Downfall owned model {model.GetType().FullName}.");
        return (Player)(getter.Invoke(model, null) ??
            throw new InvalidOperationException("Mutable Downfall model has no owner."));
    }

    private static MethodInfo Static(Type type, string name, Type result, params Type[] args) =>
        Method(type, name, result, BindingFlags.Static, args);

    private static MethodInfo Instance(Type type, string name, Type result, params Type[] args) =>
        Method(type, name, result, BindingFlags.Instance, args);

    private static MethodInfo Method(Type type, string name, Type result, BindingFlags kind, Type[] args)
    {
        var method = type.GetMethod(name, kind | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: args, modifiers: null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
            throw new NotSupportedException($"Downfall compatibility API changed: {type.FullName}.{name}.");
        return method;
    }
}
