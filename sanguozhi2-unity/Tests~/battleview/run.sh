#!/bin/bash
# 战场画面（第三版，Battle/BattleLook.cs 的 BattleBoard）的无头测试：
#   1. web.mjs：headless Chromium 打开网页版 tests/battle-view.html（世界剧本、真实地形），按 cases.json 的各个用例
#      导出 web-<名>.json（棋盘统计、实例批次、站立高度、部队与士兵 / 战马的矩阵、各网格的属性之和、镜头）与截图 web-<名>.png；
#   2. BoardHarness.cs：C# 移植为同一组（城池, 来向, 部队）建棋盘，逐项比对（见文件头）；21 个用例覆盖全部 12 种城墙样式与草原骑兵文化；
#   3. BoardRender.cs：软件光栅器把 C# 的棋盘（地形着色器、水面、实例、城池、船、主将与士兵的人物模型）按网页版的镜头画成
#      unity-<名>.png，web.mjs compose 左右拼成 side-<名>.png（左 网页版、右 C#）。
# 编译：规则文件 + View/WorldGeo / Art / Raster / CultureArt / Portrait / Figure*（-define:SANGUO_HEADLESS）+ Battle/BattleLook.cs，
# UnityEngine 替身 = ../sim/UnityStub.cs（改 partial）+ ../figure/FigureUnityStub.cs + FigureUnityExt.cs。
# 需要 node + Playwright 的 Chromium（/opt/node22/lib/node_modules/playwright，不要 playwright install）、mcs / mono。
# 用法：Tests~/battleview/run.sh [名…]        退出码 = 不一致的项数（99 = 环境 / 编译）
#   SKIP_WEB=1 沿用已有的网页版参考；NO_RENDER=1 不画图；BV_LOOK=<路径> 用另一份 BattleLook.cs（草稿）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/battleview"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
cp "$HERE/cases.json" "$OUT/cases.json"
if [ -z "${SKIP_WEB:-}" ]; then
  echo "== 网页版参考"
  node "$HERE/web.mjs" "$OUT" "$@" || exit 99
fi
LOOK="${BV_LOOK:-$S/Battle/BattleLook.cs}"
sed -E 's/public struct (Vector2|Vector3|Color)$/public partial struct \1/; s/public static class (Debug|Application|Time)$/public static partial class \1/' \
  "$HERE/../sim/UnityStub.cs" > "$OUT/UnityStub.partial.cs"
SRC=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$S"/Battle/BattleTerrain.cs \
         "$S"/View/WorldGeo.cs "$S"/View/Raster.cs "$S"/View/Art.cs "$S"/View/CultureArt.cs "$S"/View/Portrait.cs \
         "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs "$S"/View/FigureAtlas.cs "$LOOK"; do
  [ -f "$f" ] && SRC+=("$f")
done
STUB=("$OUT/UnityStub.partial.cs" "$HERE/../figure/FigureUnityStub.cs" "$HERE/../figure/FigureUnityExt.cs")
FLAGS=(-langversion:7.2 -define:SANGUO_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -r:System.IO.Compression.dll)
echo "== 编译（棋盘比对）"
msg=$(mcs "${FLAGS[@]}" -out:"$OUT/board.exe" "${STUB[@]}" "${SRC[@]}" "$HERE/BoardHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对"
mono "$OUT/board.exe" "$OUT" "$@"; fails=$?
if [ -z "${NO_RENDER:-}" ] && [ -f "$HERE/BoardRender.cs" ]; then
  echo "== 编译（软件光栅器）"
  msg=$(mcs "${FLAGS[@]}" -out:"$OUT/boardrender.exe" "${STUB[@]}" "${SRC[@]}" "$HERE/BoardHarness.cs" "$HERE/../figure/FigPng.cs" "$HERE/BoardRender.cs" -main:BoardRender 2>&1)
  if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
  echo "== C# 渲染"
  mono "$OUT/boardrender.exe" "$OUT" "$@" || exit 99
  echo "== 拼图"
  node "$HERE/web.mjs" compose "$OUT" "$@"
fi
exit $fails
