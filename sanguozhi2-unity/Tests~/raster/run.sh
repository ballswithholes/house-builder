#!/bin/bash
# 无头头像测试：把 View/Raster.cs、View/Portrait.cs、Data/Generated/PortraitData.g.cs、Data/ScenarioData.cs
# 与 RasterHarness.cs 以 -define:SANGUO_HEADLESS 编译（不引用 UnityEngine），然后：
#   1. 外貌数据完整性（条目数 + 哈希与导出工具一致）
#   2. 生成用例与网页版参考输出（Node：cases.js），C# 生成的 SVG 与 JS 逐条比对（490 例）
#   3. 渲染联系表 PNG（128 / 48 / 256 像素）并报告每张耗时；300 张 128px 的性能
#   4. 加 --browser：用 Playwright 在 tests/portraits.html 渲染同一批头像，逐格比较（需要全局 playwright）
# 输出写到 Tests~/out/raster/（已 gitignore）。退出码 = 失败的步骤数。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/raster"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
fails=0
step() { echo "== $1"; }

step "编译（SANGUO_HEADLESS）"
mcs -langversion:7.2 -define:SANGUO_HEADLESS -nowarn:0414,0649,0169 -r:System.IO.Compression.dll -out:"$OUT/raster.exe" \
  "$S/View/Raster.cs" "$S/View/Portrait.cs" "$S/Data/Generated/PortraitData.g.cs" "$S/Data/ScenarioData.cs" "$HERE/RasterHarness.cs" \
  2>&1 | grep -v "^Compilation succeeded"
[ -f "$OUT/raster.exe" ] || { echo "编译失败"; exit 1; }

step "外貌数据"
mono "$OUT/raster.exe" data || fails=$((fails + 1))

if command -v node >/dev/null 2>&1; then
  step "用例与网页版参考输出"
  node "$HERE/cases.js" "$OUT" || fails=$((fails + 1))
  step "SVG 逐条比对"
  mono "$OUT/raster.exe" svgdiff "$OUT/cases.json" "$OUT/js.json" || fails=$((fails + 1))
  step "联系表"
  mono "$OUT/raster.exe" sheet "$OUT/sheet.json" "$OUT/csharp.png" 8 128 | tail -1 || fails=$((fails + 1))
  mono "$OUT/raster.exe" sheet "$OUT/sheet48.json" "$OUT/csharp48.png" 8 48 | tail -1 || fails=$((fails + 1))
  mono "$OUT/raster.exe" sheet "$OUT/sheet256.json" "$OUT/csharp256.png" 8 256 | tail -1 || fails=$((fails + 1))
else
  echo "（没有 node：跳过 SVG 比对与联系表）"
fi

step "性能"
mono "$OUT/raster.exe" bench 300 128 || fails=$((fails + 1))

if [ "${1:-}" = "--browser" ]; then
  step "浏览器对照（tests/portraits.html）"
  for px in "" 48 256; do
    node "$HERE/browser.mjs" "$OUT/sheet$px.json" "$OUT/browser$px.png" "$OUT/csharp$px.png" | sed -n 1,2p || fails=$((fails + 1))
  done
fi
echo "失败 $fails 项"
exit $fails
