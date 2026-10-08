#!/bin/bash
# 无头音乐测试：把 Core/MusicEngine.cs、Core/MusicInstruments.cs、Data/Generated/MusicScores.g.cs 与 AudioHarness.cs
# 一起编译（不引用 UnityEngine），然后：
#   1. 乐谱数据完整性（曲数 + 哈希与导出工具一致）+ 乐器缺省参数与 JS 一致
#   2. 编译全部乐曲（记谱无误）；有 node 时与网页版的编译结果逐条比对（compile-dump.js）
#   3. 有 node 时：采样型乐器（Karplus-Strong / 模态 / 膜鸣鼓）与 JS 逐点比对（samples.js）
#   4. 离线渲染每首前 20 秒为 WAV（峰值 ≤ 0.98、RMS 合理、无 NaN、渲染耗时）
#   5. 实时模式脚本（后台准备采样、交叉淡入淡出、冲锋乐句叠加、一次性短曲结束回调、单块渲染耗时）
#   6. 加 --browser：用 Playwright 在 tests/music.html 离线渲染同一批乐曲与 81 个乐器试听片段，
#      逐首比较峰值 / RMS / 倍频程频带 / 波形相关，并输出声谱图（上 JS / 下 C#）到 Tests~/out/audio/png/
# 输出写到 Tests~/out/audio/（已 gitignore）。退出码 = 失败的步骤数。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/audio"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
fails=0
step() { echo "== $1"; }

step "编译（不依赖 UnityEngine）"
rm -f "$OUT/audio.exe"
mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169 -out:"$OUT/audio.exe" \
  "$S/Core/MusicEngine.cs" "$S/Core/MusicInstruments.cs" "$S/Data/Generated/MusicScores.g.cs" "$HERE/AudioHarness.cs" \
  2>&1 | grep -v "^Compilation succeeded"
[ -f "$OUT/audio.exe" ] || { echo "编译失败"; exit 1; }
cd "$OUT"

step "乐谱数据与乐器表"
mono audio.exe data || fails=$((fails + 1))

step "记谱编译"
mono audio.exe validate | tail -1 || fails=$((fails + 1))

if command -v node >/dev/null 2>&1; then
  step "编译结果与网页版逐条比对"
  node "$HERE/compile-dump.js" js-compile.txt && mono audio.exe dump cs-compile.txt
  if diff -q js-compile.txt cs-compile.txt >/dev/null; then echo "通过：$(grep -c '' js-compile.txt) 行一致"; else echo "失败：编译结果不一致（diff js-compile.txt cs-compile.txt）"; diff js-compile.txt cs-compile.txt | head -10; fails=$((fails + 1)); fi
  step "采样与网页版逐点比对"
  node "$HERE/samples.js" js-samples.bin >/dev/null && mono audio.exe samples js-samples.bin | tail -5 || fails=$((fails + 1))
else
  echo "（没有 node：跳过与网页版的比对）"
fi

step "离线渲染每首前 20 秒"
mono audio.exe render 20 cs | tail -3 || fails=$((fails + 1))

step "实时模式脚本"
mono audio.exe live live.wav | tail -3 || fails=$((fails + 1))

if [ "${1:-}" = "--browser" ]; then
  step "网页版离线渲染（Playwright · tests/music.html）"
  node "$HERE/browser.mjs" js 20 --demos 6 | tail -2 || fails=$((fails + 1))
  step "逐首比对（峰值 / RMS / 频带 / 波形相关）+ 声谱图"
  node "$HERE/compare.mjs" js cs png | tail -1 || fails=$((fails + 1))
  step "乐器试听片段比对"
  mono audio.exe pieces js/demos.json 6 cs/demos >/dev/null && node "$HERE/compare.mjs" js/demos cs/demos png/demos | tail -1 || fails=$((fails + 1))
fi
echo "失败 $fails 项"
exit $fails
