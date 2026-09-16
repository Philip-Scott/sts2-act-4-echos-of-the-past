#!/usr/bin/env bash
# Rebuild promotional artwork from reusable layers; gameplay gallery files stay untouched.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT="$ROOT/docs/media"
ART="$ROOT/TheArchitect/images/backgrounds/architect_approach/tower.png"
ARCHITECT="$OUTPUT/source/architect.png"
SHOULDER="$OUTPUT/source/shoulder.png"
command -v magick >/dev/null
command -v fc-match >/dev/null
SERIF_FONT="${SERIF_FONT:-$(fc-match -f '%{file}' 'Noto Serif:style=Bold')}"
SANS_FONT="${SANS_FONT:-$(fc-match -f '%{file}' 'Lato:style=Heavy')}"
test -f "$SERIF_FONT"
test -f "$SANS_FONT"
test -f "$ARCHITECT"
test -f "$SHOULDER"
mkdir -p "$OUTPUT"

magick "$ART" -resize '1024x1024^' -gravity center -extent 1024x1024 \
    \( -size 1024x1024 'gradient:rgba(10,12,23,0.04)-rgba(10,12,23,1)' \) -composite \
    \( "$ARCHITECT" -resize x650 \
        -channel A -fx 'u*min(1,(h-1-j)/(0.24*h))' +channel \
        \( +clone -background '#050711' -shadow 65x14+0+6 \) \
        +swap -background none -layers merge +repage \) \
    -gravity northwest -geometry +580+56 -composite \
    \( "$SHOULDER" -trim +repage -resize x410 \) \
    -gravity southwest -geometry +0+0 -composite \
    -geometry +0+0 \
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
    -font "$SERIF_FONT" -fill '#f5e8d4' -pointsize 136 -annotate +0+330 'ECHOS' \
    -pointsize 90 -annotate +0+480 'OF THE PAST' \
    -stroke '#c9a66d' -strokewidth 2 -draw 'line 772,596 1147,596' \
    -stroke none -font "$SANS_FONT" -fill '#edc98e' -pointsize 34 \
    -annotate +0+638 'YOUR PAST IS THE FINAL BOSS.' \
    -fill '#d0c9c3' -pointsize 25 -annotate +0+925 'SLAY THE SPIRE 2  /  SINGLE-PLAYER + CO-OP' \
    -strip -sampling-factor 4:4:4 -quality 93 "$OUTPUT/release-banner.jpg"

magick "$OUTPUT/workshop-thumbnail.png" -resize 420x420 \
    -strip "PNG24:$ROOT/TheArchitect/mod_image.png"

if [[ $(wc -c < "$OUTPUT/workshop-thumbnail.png") -ge 1000000 ]]; then
    echo "Workshop thumbnail exceeds the conservative 1 MB upload budget." >&2
    exit 1
fi
printf 'Created Workshop thumbnail, release banner, and in-game mod image.\n'
