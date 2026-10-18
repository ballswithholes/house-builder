#!/bin/bash
# 人物模型库的目视对照（UNITY-V3 §4「画面」）：网页版 tests/figures.html?shot=1 的截图（headless Chromium，SwiftShader WebGL）
# 与 C# 软件光栅器（FigureRender.cs：C# 移植生成、摆好姿势的模型，在 Unity 坐标里蒙皮、背面剔除、着色）在同一镜头下的渲染，
# 左右拼成 side-<视角>.png：正面、四分之三、侧面、背面、四个动作帧（关羽 light1、张飞 heavy、赵云 run、吕布 knockdown）、
# 名将两排（三国 / 罗马 / 倭 / 安息 / 贵霜 / 天竺 / 阿克苏姆 / 库施 / 僧人）、三种文化的士兵、四名骑将与战马。
# 需要 node + Playwright 的 Chromium、mcs / mono。
# 用法：Tests~/figure/render.sh        输出在 $OUT_DIR/figure/render/（SKIP_WEB=1 沿用已有截图）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
R="${OUT_DIR:-$HERE/../out}/figure/render"
mkdir -p "$R"
FIG=("$S"/View/Raster.cs "$S"/View/Portrait.cs "$S"/Data/Generated/PortraitData.g.cs "$S"/Data/ScenarioData.cs "$S"/Data/Defs.cs
     "$S"/Data/Generated/WorldData.g.cs "$S"/Data/Generated/TacticsData.g.cs "$S"/Data/Generated/FigureData.g.cs
     "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs "$S"/View/FigureAtlas.cs)
if [ -z "${SKIP_WEB:-}" ] || [ ! -f "$R/scenes.json" ]; then
  echo "== 网页版截图"
  node "$HERE/render.mjs" shots "$R" || exit 99
fi
echo "== 编译（软件光栅器）"
msg=$(mcs -langversion:7.2 -define:SANGUO_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -r:System.IO.Compression.dll \
      -out:"$R/../figurerender.exe" "${FIG[@]}" "$HERE/FigPng.cs" "$HERE/FigureRender.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== C# 渲染"
mono "$R/../figurerender.exe" "$R" || exit 99
echo "== 拼图"
node "$HERE/render.mjs" compose "$R"
