using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;
using TheArchitect.TheArchitectCode.Enchantments;

namespace TheArchitect.TheArchitectCode.Playtest;

internal static class EnchantmentArtPlaytest
{
    internal static async Task Run(NGame game)
    {
        Check(NativeDemoSafety.Enabled && !NativeDemoSafety.SharedVisible,
            "Enchantment art probes require private, disposable native-test storage.");
        var run = await game.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()],
            [], "ARCHITECT-ENCHANTMENT-ART", GameMode.Standard);
        var player = run.Players.Single();
        var backdrop = new ColorRect
        {
            Color = new Color("243036"),
            Size = game.GetViewportRect().Size,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        game.AddChild(backdrop);
        EnchantmentModel[] enchantments =
            [ArchitectModels.WrathEnchantment, ArchitectModels.CalmEnchantment,
                ModelDb.Enchantment<Sown>(), ModelDb.Enchantment<Glam>()];
        var textures = new List<byte[]>();
        for (var i = 0; i < enchantments.Length; i++)
        {
            var enchantment = enchantments[i];
            var icon = PreloadManager.Cache.GetTexture2D(enchantment.IconPath);
            Check(icon.GetSize() == new Vector2(64, 64), $"{enchantment.Id}: native 64x64 texture");
            if (i < 2)
            {
                var slug = i == 0 ? "wrath" : "calm";
                Check(enchantment.IconPath == $"res://TheArchitect/images/enchantments/{slug}.png",
                    $"{enchantment.Id}: BaseLib resolves the custom icon, not the missing-enchantment fallback");
                using var image = icon.GetImage();
                CheckAlpha(image, slug);
                textures.Add(image.GetData());
                var big = PreloadManager.Cache.GetTexture2D($"res://TheArchitect/images/enchantments/big/{slug}.png");
                Check(big.GetSize() == new Vector2(256, 256), $"{enchantment.Id}: matching large texture");
                using var bigImage = big.GetImage();
                CheckAlpha(bigImage, slug);
            }
            var x = 275 + i * 440;
            backdrop.AddChild(new Label
            {
                Text = i < 2 ? enchantment.GetType().Name : $"Native {enchantment.GetType().Name}",
                Position = new Vector2(x - 90, 35)
            });
            backdrop.AddChild(new TextureRect { Texture = icon, Position = new Vector2(x - 32, 85) });

            foreach (var (pile, y, scale) in new[] { (PileType.Deck, 425f, 1.25f), (PileType.Hand, 865f, 0.8f) })
            {
                var model = ModelDb.Card<StrikeIronclad>().ToMutable();
                model.Owner = player;
                model.EnchantInternal(enchantment.ToMutable(), 1);
                // Exercise the same saved-deck restoration used by deck/history previews.
                if (pile == PileType.Deck)
                {
                    model = CardModel.FromSerializable(model.ToSerializable());
                    model.Owner = player;
                }
                var card = NCard.Create(model) ?? throw new InvalidOperationException("Native card creation failed.");
                backdrop.AddChild(card);
                card.UpdateVisuals(pile, CardPreviewMode.Normal);
                card.Position = new Vector2(x, y);
                card.Scale = Vector2.One * scale;
                var marker = card.GetNode<TextureRect>("%Enchantment");
                var markerIcon = marker.GetNode<TextureRect>("Icon");
                Check(marker.Visible && markerIcon.Texture == icon && markerIcon.Size == new Vector2(35, 35),
                    $"{enchantment.Id}: {pile} card displays the exact icon at native marker size");
                if (i < 2)
                    Check(!marker.GetNode<Label>("Label").Visible, $"{enchantment.Id}: no enchantment amount");
            }
        }
        Check(!textures[0].SequenceEqual(textures[1]), "Wrath and Calm load different packaged artwork");
        await Task.Delay(1000);
        await NativeDemoPlaytest.Capture("enchantment-art");
        MainFile.Logger.Info("ENCHANTMENT ART SMOKE PASSED: distinct packed 64x64 RGBA icons, restored deck and hand markers, native Sown/Glam comparison.");
        game.GetTree().Quit();
    }

    private static void CheckAlpha(Image image, string slug)
    {
        var size = image.GetWidth();
        var solidPixels = 0;
        var transparentEdges = true;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var alpha = image.GetPixel(x, y).A;
            if (x == 0 || y == 0 || x == size - 1 || y == size - 1)
                transparentEdges &= alpha == 0;
            if (alpha > 0.5f) solidPixels++;
        }
        Check(transparentEdges, $"{slug}/{size}: transparent edges without clipped outlines");
        Check(solidPixels > size * size / 4 && solidPixels < size * size * 4 / 5,
            $"{slug}/{size}: visible silhouette and transparent background");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Enchantment art: " + message);
        MainFile.Logger.Info("ENCHANTMENT ART PASS: " + message);
    }
}
