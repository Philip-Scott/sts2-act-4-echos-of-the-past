using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Acts;

namespace TheArchitect.TheArchitectCode.Lifecycle;

[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter._Ready))]
internal static class ArchitectRestVisuals
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(IRunState), nameof(IRunState.CurrentActIndex));
        var replacement = AccessTools.Method(typeof(ArchitectRestVisuals), nameof(AnimationActIndex));
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(getter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replaced = true;
            }
            yield return instruction;
        }
        if (!replaced)
            throw new MissingMethodException("The beta rest-site animation selector changed.");
    }

    private static int AnimationActIndex(IRunState run) => run.Act is ArchitectAct ? 2 : run.CurrentActIndex;
}
