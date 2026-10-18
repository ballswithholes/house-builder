#!/bin/bash
# 人物贴图的对照：网页版（tex-web.mjs，headless Chromium 的 Canvas2D）与 C#（View/FigureAtlas.cs 经 View/Raster.cs）的
# 程序图集（反照率 / 染色遮罩 / 法线 / ORM，逐区域）、18 张名将面部贴图、环境贴图逐像素比较，并写出对照 PNG
# （tex-atlas-*.png、tex-region-*.png、tex-faces.png、tex-face-*.png、tex-env.png：左 网页版、中 C#、右 差 × 4）。
# 需要 node + Playwright 的 Chromium（/opt/node22/lib/node_modules/playwright）、mcs / mono。
# 用法：Tests~/figure/tex.sh      输出在 $OUT_DIR/figure/tex/；退出码 = 超出阈值的项数（99 = 环境 / 编译失败）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/figure/tex"
mkdir -p "$OUT"
FIG=("$S"/View/Raster.cs "$S"/View/Portrait.cs "$S"/Data/Generated/PortraitData.g.cs "$S"/Data/ScenarioData.cs "$S"/Data/Defs.cs
     "$S"/Data/Generated/WorldData.g.cs "$S"/Data/Generated/TacticsData.g.cs "$S"/Data/Generated/FigureData.g.cs
     "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs "$S"/View/FigureAtlas.cs)
if [ -z "${SKIP_WEB:-}" ] || [ ! -f "$OUT/web-tex.json" ]; then
  echo "== 网页版贴图"
  node "$HERE/tex-web.mjs" "$OUT" || exit 99
fi
echo "== 编译"
msg=$(mcs -langversion:7.2 -define:SANGUO_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -r:System.IO.Compression.dll \
      -out:"$OUT/../figuretex.exe" "${FIG[@]}" "$HERE/FigPng.cs" "$HERE/FigureTex.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对"
mono "$OUT/../figuretex.exe" "$OUT"
