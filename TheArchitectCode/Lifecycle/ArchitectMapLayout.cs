using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class ArchitectMapLayout
{
    internal static bool Applies(ActMap map) => map is ArchitectMap ||
        RunManager.Instance.DebugOnlyGetState() is { Act: ArchitectAct } run && ReferenceEquals(run.Map, map);

    private static float CompactExtent(float value, ActMap map) =>
        Applies(map) ? value switch
        {
            2325f => map.StartingMapPoint.PointType == MapPointType.Ancient ? 460f : 220f,
            -1980f => map.StartingMapPoint.PointType == MapPointType.Ancient ? -400f : -180f,
            740f => map.StartingMapPoint.PointType == MapPointType.Ancient ? 430f : 460f,
            720f => 300f,
            800f => 400f,
            _ => value
        } : value;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode != OpCodes.Ldc_R4 || instruction.operand is not float value ||
                (value != 2325f && value != -1980f && value != 740f && value != 720f && value != 800f))
                continue;
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return CodeInstruction.Call(typeof(ArchitectMapLayout), nameof(CompactExtent));
            count++;
        }
        if (count != 5)
            throw new InvalidOperationException("The beta map layout changed; expected five map layout constants.");
    }
}

[HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
internal static class ArchitectMapScroll
{
    private static void Prefix(ActMap ____map, ref Vector2 ____targetDragPos)
    {
        if (ArchitectMapLayout.Applies(____map))
            ____targetDragPos = new Vector2(0f, Math.Clamp(____targetDragPos.Y, -180f, 180f));
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class ArchitectMapOpeningPosition
{
    private static void Postfix(ActMap ____map, Control ____mapContainer, ref Vector2 ____targetDragPos)
    {
        if (!ArchitectMapLayout.Applies(____map))
            return;
        ____mapContainer.Position = Vector2.Zero;
        ____targetDragPos = Vector2.Zero;
    }
}

[HarmonyPatch(typeof(NMapScreen), "StartOfActAnim")]
internal static class ArchitectMapIntro
{
    private static readonly Action<NMapScreen> InitPrompt =
        AccessTools.MethodDelegate<Action<NMapScreen>>(AccessTools.Method(typeof(NMapScreen), "InitMapPrompt"));

    private static bool Prefix(NMapScreen __instance, ActMap ____map, Control ____mapContainer,
        ref Vector2 ____targetDragPos, ref Tween? ____actAnimTween, ref Task __result)
    {
        if (!ArchitectMapLayout.Applies(____map))
            return true;
        ____actAnimTween?.Kill();
        ____actAnimTween = null;
        ____mapContainer.Position = Vector2.Zero;
        ____targetDragPos = Vector2.Zero;
        InitPrompt(__instance);
        __result = Task.CompletedTask;
        return false;
    }
}
