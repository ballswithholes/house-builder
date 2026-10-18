#!/bin/bash
# 战略地图的目视对照（第三版）：网页版截图（headless Chromium）与 C# 软件光栅器（MapRender.cs）在同一镜头下的渲染，
# 左右拼成 side-<视角>.png（中原、罗马、天竺、洛阳近景）。需要 node + Playwright 的 Chromium、mcs / mono。
# 先运行 Tests~/map/run.sh（生成 refs.txt 与 UnityStub.partial.cs）。
# 用法：Tests~/map/render.sh        输出在 $OUT_DIR/map/render/
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/map"
R="$OUT/render"
mkdir -p "$R"
[ -f "$OUT/refs.txt" ] && [ -f "$OUT/UnityStub.partial.cs" ] || { echo "先运行 Tests~/map/run.sh" >&2; exit 99; }
# SKIP_WEB=1：沿用已有的网页版截图与 cams.json
if [ -z "${SKIP_WEB:-}" ] || [ ! -f "$R/cams.json" ]; then
  echo "== 网页版截图"
  node "$HERE/render.mjs" shots "$R" || exit 99
fi
SRC=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$S"/Battle/BattleTerrain.cs "$S"/View/WorldGeo.cs \
         "$S"/View/Raster.cs "$S"/View/Art.cs "$S"/View/CultureArt.cs "$S"/View/MapView.cs; do
  [ -f "$f" ] && SRC+=("$f")
done
echo "== 编译（软件光栅器）"
msg=$(mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -r:System.IO.Compression.dll -out:"$OUT/maprender.exe" \
      "$OUT/UnityStub.partial.cs" "$HERE/UnityViewStub.cs" "${SRC[@]}" "$HERE/MapRender.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== C# 渲染"
mono "$OUT/maprender.exe" "$OUT" "$R/cams.json" "$R" 960 540 || exit 99
echo "== 拼图"
node "$HERE/render.mjs" compose "$R"
