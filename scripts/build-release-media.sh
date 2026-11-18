#!/usr/bin/env bash
# Rebuild promotional artwork from reusable layers; gameplay gallery files stay untouched.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="$ROOT/docs/media"
ART="$ROOT/TheArchitect/images/backgrounds/architect_approach/tower.png"
ARCHITECT="$OUTPUT/source/architect.png"
SHOULDER="$OUTPUT/source/shoulder.png"
MOD_IMAGE="$OUTPUT/source/in-game-thumbnail.jpg"
command -v magick >/dev/null
command -v fc-match >/dev/null
SERIF_FONT="${SERIF_FONT:-$(fc-match -f '%{file}' 'Noto Serif:style=Bold')}"
SANS_FONT="${SANS_FONT:-$(fc-match -f '%{file}' 'Lato:style=Heavy')}"
test -f "$SERIF_FONT"
test -f "$SANS_FONT"
test -f "$ARCHITECT"
test -f "$SHOULDER"
test -f "$MOD_IMAGE"
mkdir -p "$OUTPUT"

magick "$MOD_IMAGE" -resize 920x920 \
    -strip "PNG24:$OUTPUT/workshop-thumbnail.png"

magick "$ART" -resize '1920x1080^' -gravity center -extent 1920x1080 \
    \( -size 1920x1080 'gradient:rgba(9,12,22,0.08)-rgba(9,12,22,0.98)' \) -composite \
    \( "$ARCHITECT" -resize x790 \
        -channel A -fx 'u*min(1,(h-1-j)/(0.24*h))' +channel \
        \( +clone -background '#050711' -shadow 65x18+0+8 \) \
        +swap -background none -layers merge +repage \) \
    -gravity northwest -geometry +1300+40 -composite \
    \( "$SHOULDER" -trim +repage -resize x580 \) \
    -gravity southwest -geometry +0+0 -composite \
    -geometry +0+0 \
    -fill none -stroke '#c9a66d' -strokewidth 2 -draw 'rectangle 36,36 1883,1043' \
    -stroke none -gravity north -font "$SANS_FONT" -fill '#edc98e' -pointsize 30 \
    -annotate +0+268 'ACT 4' \
    -font "$SERIF_FONT" -fill '#f5e8d4' -pointsize 136 -annotate +0+330 'ECHOES' \
    -pointsize 90 -annotate +0+480 'OF THE PAST' \
    -stroke '#c9a66d' -strokewidth 2 -draw 'line 772,596 1147,596' \
    -stroke none -font "$SANS_FONT" -fill '#edc98e' -pointsize 34 \
    -annotate +0+638 'YOUR PAST IS THE FINAL BOSS.' \
    -fill '#d0c9c3' -pointsize 25 -annotate +0+925 'SLAY THE SPIRE 2  /  SINGLE-PLAYER + CO-OP' \
    -strip -sampling-factor 4:4:4 -quality 93 "$OUTPUT/release-banner.jpg"

magick "$MOD_IMAGE" -resize 420x420 \
    -strip "PNG24:$ROOT/TheArchitect/mod_image.png"

if [[ $(wc -c < "$OUTPUT/workshop-thumbnail.png") -ge 1000000 ]]; then
    echo "Workshop thumbnail exceeds the conservative 1 MB upload budget." >&2
    exit 1
fi
printf 'Created Workshop thumbnail, release banner, and in-game mod image.\n'
