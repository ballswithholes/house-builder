#!/usr/bin/env bash
# Re-renders the committed preview set (Docs/previews3d/), or into another directory given as $1.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
tool="$here/preview3d.sh"
out="${1:-$here/../../Docs/previews3d}"
mkdir -p "$out"
"$tool" map lanternvale "$out/lanternvale_noon.png"
"$tool" map lanternvale "$out/lanternvale_night.png" --hour 22.5
"$tool" map lanternvale "$out/lanternvale_far.png" --zoom 10.4
"$tool" map lanternvale "$out/lanternvale_close.png" --zoom 3 --yaw 30
"$tool" map lanternvale "$out/lanternvale_kusu.png" --spawn camphor --hour 17.5 --flags moppet_found
"$tool" map lanternvale "$out/lanternvale_pasture.png" --at 84,9 --hour 9 --flags shepherd_quest,bandits_peaceful
"$tool" map whisperwood "$out/whisperwood_noon.png" --hour 12.5
"$tool" map whisperwood "$out/whisperwood_greymane.png" --at 25,9 --hour 12.5 --flags shepherd_hunt
"$tool" map shrine "$out/shrine_dusk.png"
"$tool" map shrine "$out/shrine_keeper.png" --at 44,8
"$tool" units "$out/units.png" all --tile 260
"$tool" props "$out/props_all.png" all --views g --tile 240 > /dev/null
# keep the committed set light: JPEG (quality 88) instead of PNG when Pillow is available
if python3 -c "import PIL" 2>/dev/null; then
  python3 - "$out" <<'PY'
import os, sys
from PIL import Image
d = sys.argv[1]
for f in sorted(os.listdir(d)):
    if f.endswith(".png"):
        p = os.path.join(d, f)
        Image.open(p).convert("RGB").save(p[:-4] + ".jpg", quality=88, optimize=True)
        os.remove(p)
PY
fi
