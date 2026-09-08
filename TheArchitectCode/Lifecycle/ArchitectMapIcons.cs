using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;
using TheArchitect.TheArchitectCode.Encounters;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(NNormalMapPoint), nameof(NNormalMapPoint._Ready))]
internal static class ArchitectMapIcons
{
    private static readonly Lazy<Shader> NeutralInk = new(() => new Shader
    {
        Code = """
            shader_type canvas_item;
            varying vec4 node_tint;
            void vertex() {
                node_tint = COLOR;
            }

            void fragment() {
                COLOR = vec4(vec3(0.16), texture(TEXTURE, UV).a) * node_tint;
            }
            """
    });

    private static void Postfix(NNormalMapPoint __instance, IRunState ____runState)
    {
        if (____runState.Act is not ArchitectAct)
            return;
        __instance.GetNode<TextureRect>("%Icon").Material = new ShaderMaterial { Shader = NeutralInk.Value };
    }
}

// Route the beta's history helpers to the same artwork as the map.
[HarmonyPatch]
internal static class ArchitectHistoryIcons
{
    [HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconPath))]
    [HarmonyPostfix]
    private static void Icon(ModelId? modelId, ref string? __result)
    {
        if (__result != null && modelId == ArchitectModels.EncounterId)
            __result = ArchitectModels.Encounter.CustomRunHistoryIconPath;
    }

    [HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconOutlinePath))]
    [HarmonyPostfix]
    private static void Outline(ModelId? modelId, ref string? __result)
    {
        if (__result != null && modelId == ArchitectModels.EncounterId)
            __result = ArchitectModels.Encounter.CustomRunHistoryIconOutlinePath;
    }
}
