using System.Reflection;
using System.Reflection.Emit;
using TheArchitect.TheArchitectCode.CorruptedPlayerCombat;

internal static class NativeDownfallApiTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> check)
    {
        test("Downfall support: absence does not load a mod or report an error", () =>
        {
            var before = AppDomain.CurrentDomain.GetAssemblies().Where(IsDownfall).ToArray();
            check(!NativeDownfallApi.TryBind(null, out var api, out var reason) && api == null && reason == null,
                "An absent optional mod must be a normal no-op.");
            check(before.SequenceEqual(AppDomain.CurrentDomain.GetAssemblies().Where(IsDownfall)),
                "Optional binding must never load Downfall.");
            check(!typeof(NativeDownfallApi).Assembly.GetReferencedAssemblies().Any(name => name.Name == "Downfall"),
                "The adapter must compile without a Downfall dependency.");
        });
        test("Downfall support: unverified versions are explicitly rejected", () =>
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("Downfall") { Version = new Version(0, 1, 17, 0) }, AssemblyBuilderAccess.RunAndCollect);
            check(!NativeDownfallApi.TryBind(assembly, out var api, out var reason) && api == null &&
                reason?.Contains("verified 0.1.16") == true,
                "Do not guess at a changed optional mod's runtime semantics.");
        });
        test("Downfall support: incomplete API is rejected before any hooks are installed", () =>
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("Downfall") { Version = NativeDownfallApi.SupportedVersion }, AssemblyBuilderAccess.RunAndCollect);
            check(!NativeDownfallApi.TryBind(assembly, out var api, out var reason) && api == null &&
                reason?.Contains("missing type") == true,
                "A missing API must produce an explicit incompatibility reason.");
        });
    }

    private static bool IsDownfall(Assembly assembly) => assembly.GetName().Name == "Downfall";
}
