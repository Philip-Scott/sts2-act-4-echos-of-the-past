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

    private static void Postfix(ActMap map, NBossMapPoint ____bossPointNode)
    {
        if (!Applies(map) || !map.GetAllMapPoints().Any(point => point.PointType == MapPointType.Elite))
            return;
        ____bossPointNode.PivotOffset = ____bossPointNode.Size / 2f;
        ____bossPointNode.Scale = Vector2.One * 0.6f;
    }

    private static float CompactExtent(float value, ActMap map)
    {
        if (!Applies(map))
            return value;
        var hasElite = map.GetAllMapPoints().Any(point => point.PointType == MapPointType.Elite);
        var hasAncient = map.StartingMapPoint.PointType == MapPointType.Ancient;
        return value switch
        {
            // The boss artwork extends below its node; leave room for the Elite beneath it.
            2325f => hasElite ? 350f : hasAncient ? 460f : 220f,
            -1980f => hasAncient || hasElite ? -400f : -180f,
            740f => hasElite ? 300f : hasAncient ? 430f : 460f,
            720f => 300f,
            800f => 400f,
            _ => value
        };
    }

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
