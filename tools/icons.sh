#!/bin/sh
# Remakes the window icon (icon.png) and the Windows program icon (icon.ico) from icon.svg.
# Needs rsvg-convert and python3.
set -e
cd "$(dirname "$0")/../src/Paperdoll.App/Assets"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
rsvg-convert -w 256 -h 256 icon.svg -o icon.png
for n in 16 24 32 48 64 128 256; do
    rsvg-convert -w $n -h $n icon.svg -o "$tmp/$n.png"
done
# An .ico is a small directory of images; every size is stored as a PNG.
python3 - "$tmp" <<'PY'
import struct, sys
sizes = [16, 24, 32, 48, 64, 128, 256]
pngs = [open(f"{sys.argv[1]}/{n}.png", "rb").read() for n in sizes]
out = struct.pack("<HHH", 0, 1, len(sizes))
offset = 6 + 16 * len(sizes)
for n, png in zip(sizes, pngs):
    out += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(png), offset)
    offset += len(png)
open("icon.ico", "wb").write(out + b"".join(pngs))
PY
echo "icon.png and icon.ico written"
