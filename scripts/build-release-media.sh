#!/usr/bin/env bash
# Rebuild promotional artwork from the mod's tower illustration; no game screenshots are altered.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="$ROOT/docs/media"
ART="$ROOT/TheArchitect/images/backgrounds/architect_approach/tower.png"
command -v magick >/dev/null
command -v fc-match >/dev/null
SERIF_FONT="${SERIF_FONT:-$(fc-match -f '%{file}' 'Noto Serif:style=Bold')}"
SANS_FONT="${SANS_FONT:-$(fc-match -f '%{file}' 'Lato:style=Heavy')}"
test -f "$SERIF_FONT"
test -f "$SANS_FONT"
mkdir -p "$OUTPUT"

magick "$ART" -resize '1024x1024^' -gravity center -extent 1024x1024 \
    \( -size 1024x1024 'gradient:rgba(10,12,23,0.04)-rgba(10,12,23,1)' \) -composite \
    -fill none -stroke '#d1ac70' -strokewidth 2 -draw 'rectangle 30,30 993,993' \
    -stroke 'rgba(209,172,112,0.4)' -strokewidth 1 -draw 'rectangle 40,40 983,983' \
    -stroke none -fill 'rgba(10,12,23,0.85)' -draw 'roundrectangle 413,65 611,125 8,8' \
    -font "$SANS_FONT" -fill '#f0d09a' -pointsize 30 -gravity north \
    -annotate +0+77 'ACT 4' \
    -font "$SERIF_FONT" -fill '#f5e8d4' -pointsize 112 -annotate +0+598 'ECHOS' \
    -pointsize 38 -annotate +0+710 'OF THE' \
    -pointsize 94 -annotate +0+752 'PAST' \
    -stroke '#d1ac70' -strokewidth 2 -draw 'line 350,849 674,849' \
    -stroke none -font "$SANS_FONT" -fill '#f0d09a' -pointsize 27 \
    -annotate +0+877 'YOUR PAST IS THE FINAL BOSS.' \
    -fill '#b5b0b3' -pointsize 20 -annotate +0+929 'A SLAY THE SPIRE 2 MOD' \
    -strip "PNG24:$OUTPUT/workshop-thumbnail.png"

magick "$ART" -resize '1920x1080^' -gravity center -extent 1920x1080 \
    \( -size 1080x1920 'gradient:rgba(9,12,22,0.98)-rgba(9,12,22,0.08)' -rotate -90 \) -composite \
    -fill none -stroke '#c9a66d' -strokewidth 2 -draw 'rectangle 36,36 1883,1043' \
    -stroke none -gravity northwest -font "$SANS_FONT" -fill '#edc98e' -pointsize 30 \
    -annotate +110+268 'ACT 4' \
    -font "$SERIF_FONT" -fill '#f5e8d4' -pointsize 136 -annotate +102+330 'ECHOS' \
    -pointsize 90 -annotate +106+480 'OF THE PAST' \
    -stroke '#c9a66d' -strokewidth 2 -draw 'line 110,596 485,596' \
    -stroke none -font "$SANS_FONT" -fill '#edc98e' -pointsize 34 \
    -annotate +110+638 'YOUR PAST IS THE FINAL BOSS.' \
    -fill '#d0c9c3' -pointsize 25 -annotate +110+925 'SLAY THE SPIRE 2  /  SINGLE-PLAYER + CO-OP' \
    -strip -sampling-factor 4:4:4 -quality 93 "$OUTPUT/release-banner.jpg"

magick "$OUTPUT/workshop-thumbnail.png" -resize 420x420 \
    -strip "PNG24:$ROOT/TheArchitect/mod_image.png"

if [[ $(wc -c < "$OUTPUT/workshop-thumbnail.png") -ge 1000000 ]]; then
    echo "Workshop thumbnail exceeds the conservative 1 MB upload budget." >&2
    exit 1
fi
printf 'Created Workshop thumbnail, release banner, and in-game mod image.\n'
