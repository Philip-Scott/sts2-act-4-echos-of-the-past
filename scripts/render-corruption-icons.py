#!/usr/bin/env python3
"""Export the six original corruption relic SVGs. Requires ImageMagick and Pillow.

Run from any directory with: python scripts/render-corruption-icons.py
Writes 94px relics, white relic outlines, 64px powers, and 256px tooltip art.
Power filenames follow TheArchitectPower's <model_name>_power.png convention.
"""

from pathlib import Path
import random
import shutil
import subprocess
import tempfile

from PIL import Image, ImageFilter


ROOT = Path(__file__).resolve().parents[1] / "TheArchitect" / "images"
NAMES = (
    "singeing_ichor",
    "ring_of_the_viper",
    "tarnished_crown",
    "fractured_urn",
    "overvolted_matrix",
    "echo_shard",
)


def painted(image: Image.Image) -> Image.Image:
    rng = random.Random(2819)
    grain = Image.new("L", image.size)
    grain.putdata([rng.randrange(80, 177) for _ in range(image.width * image.height)])
    grain = grain.filter(ImageFilter.GaussianBlur(0.45))
    pixels, noise = image.load(), grain.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            delta = (noise[x, y] - 128) * 0.17 if max(r, g, b) > 65 else 0
            pixels[x, y] = tuple(max(0, min(255, round(c + delta))) for c in (r, g, b)) + (a,)
    return image


def main() -> None:
    magick = shutil.which("magick")
    if magick is None:
        raise SystemExit("ImageMagick's magick executable is required to render the SVG sources.")
    for directory in ("relics/big", "powers", "powers/big"):
        (ROOT / directory).mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="corruption-icons-") as temporary:
        for name in NAMES:
            rendered = Path(temporary) / f"{name}.png"
            subprocess.run(
                [magick, "-background", "none", "-density", "384",
                 str(ROOT / "relics" / f"{name}.svg"), str(rendered)],
                check=True,
            )
            with Image.open(rendered) as source:
                artwork = source.convert("RGBA").resize((240, 240), Image.Resampling.LANCZOS)
            big = Image.new("RGBA", (256, 256))
            big.paste(artwork, (8, 8))
            big = painted(big)
            big.save(ROOT / "relics" / "big" / f"{name}.png")
            big.save(ROOT / "powers" / "big" / f"{name}_power.png")
            small = big.resize((94, 94), Image.Resampling.LANCZOS)
            small.save(ROOT / "relics" / f"{name}.png")
            outline = Image.new("RGBA", small.size, "white")
            outline.putalpha(small.getchannel("A").filter(ImageFilter.MaxFilter(5)))
            outline.save(ROOT / "relics" / f"{name}_outline.png")
            big.resize((64, 64), Image.Resampling.LANCZOS).save(
                ROOT / "powers" / f"{name}_power.png"
            )
            print(f"Rendered {name}")


if __name__ == "__main__":
    main()
