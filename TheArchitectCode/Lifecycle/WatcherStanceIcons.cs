using Godot;

namespace TheArchitect.TheArchitectCode.Lifecycle;

internal static class WatcherStanceIcons
{
    // Godot's resource cache is weak. Retain these exact mod-owned aliases for the
    // native PowerModel getters that the JIT can inline around BaseLib's patches.
    private static Texture2D[] _nativeAliases = [];

    internal static void Register()
    {
        if (_nativeAliases.Length != 0)
            return;
        _nativeAliases = [CreateAlias("wrath"), CreateAlias("calm")];
    }

    private static Texture2D CreateAlias(string stance)
    {
        var path = $"res://TheArchitect/images/enchantments/{stance}.png";
        var texture = ResourceLoader.Load<Texture2D>(path) ??
            throw new InvalidOperationException($"Missing Watcher stance artwork: {path}");
        var alias = new AtlasTexture { Atlas = texture };
        alias.TakeOverPath($"res://images/atlases/power_atlas.sprites/thearchitect-{stance}_stance_power.tres");
        return alias;
    }
}
