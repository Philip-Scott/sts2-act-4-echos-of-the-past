using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace AnimatedPlayerHands;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Id = "AnimatedPlayerHands";
    internal static readonly Logger Logger = new(Id, LogType.Generic);

    public static void Initialize()
    {
        new Harmony(Id).PatchAll(typeof(ModEntry).Assembly);
        Logger.Info($"Animated Player Hands {typeof(ModEntry).Assembly.GetName().Version?.ToString(3)} initialized.");
    }
}
