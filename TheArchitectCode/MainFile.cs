using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace TheArchitect.TheArchitectCode;

//You're recommended but not required to keep all your code in this package and all your assets in the TheArchitect folder.
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "TheArchitect"; //Used for resource filepath
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        Playtest.NativeDemoSafety.Initialize();
        var assembly = Assembly.GetExecutingAssembly();
        Persistence.ArchitectRun.Register();

        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(assembly);
     
        Harmony harmony = new(ModId);

        harmony.PatchAll(assembly);
        Challenger.NativeCombatCallSites.Install(harmony);
        Logger.Info("The Architect 0.1.0 initialized for Slay the Spire 2 public-beta 0.111.0.");
    }
}
