using System.Buffers.Binary;
using System.Reflection;
using System.Xml.Linq;
using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Enchantments;

internal static class EnchantmentIconTests
{
    internal static void Run(Action<string, Action> test)
    {
        test("Watcher enchantment art: distinct native-sized RGBA assets and editable sources", () =>
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "TheArchitect.csproj")))
                root = root.Parent;
            Check(root != null, "Cannot locate the project assets.");
            var assets = new List<byte[]>();
            foreach (var (type, slug) in new[] { (typeof(Wrath), "wrath"), (typeof(Calm), "calm") })
            {
                if (ModelDb.GetByIdOrNull<EnchantmentModel>(ModelDb.GetId(type)) == null)
                    ModelDb.Inject(type);
                var enchantment = ModelDb.GetById<EnchantmentModel>(ModelDb.GetId(type));
                var path = (string?)type.GetProperty("CustomIconPath",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(enchantment);
                Check(path == $"res://TheArchitect/images/enchantments/{slug}.png", $"{slug}: custom resource path");
                var file = Path.Combine(root!.FullName, path!["res://".Length..]);
                foreach (var size in new[] { 64, 256 })
                {
                    var png = File.ReadAllBytes(size == 64 ? file :
                        Path.Combine(Path.GetDirectoryName(file)!, "big", $"{slug}.png"));
                    Check(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
                        $"{slug}/{size}: PNG signature");
                    Check(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)) == size &&
                        BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) == size &&
                        png[24] == 8 && png[25] == 6, $"{slug}/{size}: native size, 8-bit RGBA");
                    assets.Add(png);
                }
                var svg = XDocument.Load(Path.ChangeExtension(file, ".svg")).Root!;
                Check(svg.Attribute("width")?.Value == "64" && svg.Attribute("height")?.Value == "64",
                    $"{slug}: matching editable SVG source");
            }
            Check(!assets[0].SequenceEqual(assets[2]) && !assets[1].SequenceEqual(assets[3]),
                "Wrath and Calm must not reuse one texture at either size.");
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
