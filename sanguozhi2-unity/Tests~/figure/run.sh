#!/bin/bash
# 人物模型库的无头测试（UNITY-V3 §4）：
#   1. Node 端（refs.js）载入网页版 vendor/three.min.js + figure.js（Canvas 用桩），导出参考值：武将 39 人 × 3 档 LOD、
#      士兵 18 文化 × 4 兵种、战马 25 种、兵器 34 种 × 3 档、盾、27 个片段 × 3 个时刻的骨骼矩阵（含选项 / grip / ikHand / 弹簧）、
#      骑乘、士兵群烘焙（半精度）；
#   2. C#（View/FigureMesh.cs、FigureRig.cs 与头像、数据文件，-define:SANGUO_HEADLESS 编译）重建并逐项比对（FigureHarness.cs）。
# 用法：Tests~/figure/run.sh [过滤]      过滤：atlas generals soldiers horses weapons poses mounted bakes
#       退出码 = 失败项数（编译失败 / 缺 node 为 99）。贴图比对见 tex.sh，画面比对见 render.sh。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/figure"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
command -v node >/dev/null 2>&1 || { echo "找不到 node（生成参考值需要）" >&2; exit 99; }
# 人物模型库的纯 C# 部分与它依赖的文件（头像、剧本数据、文化回退）
FIG=("$S"/View/Raster.cs "$S"/View/Portrait.cs "$S"/Data/Generated/PortraitData.g.cs "$S"/Data/ScenarioData.cs "$S"/Data/Defs.cs
     "$S"/Data/Generated/WorldData.g.cs "$S"/Data/Generated/TacticsData.g.cs "$S"/Data/Generated/FigureData.g.cs
     "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs "$S"/View/FigureAtlas.cs)
# SKIP_REFS=1：沿用已有的参考值
if [ -z "${SKIP_REFS:-}" ] || [ ! -f "$OUT/refs.json" ]; then
  echo "== 参考值（网页版）"
  node "$HERE/refs.js" "$OUT" || exit 99
fi
echo "== 编译"
msg=$(mcs -langversion:7.2 -define:SANGUO_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -r:System.IO.Compression.dll \
      -out:"$OUT/figure.exe" "${FIG[@]}" "$HERE/FigureHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对"
mono "$OUT/figure.exe" "$OUT" "$@"
