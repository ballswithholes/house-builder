#!/bin/bash
# 界面文字的无头测试：UI/UIText.cs（行内头像标记、UI.faces 的移植、成长界面的文字）与规则文件、sim 的 UnityStub.cs / SimLib.cs
# 一起编译为 HarnessUI.exe 运行，输出与网页版（node ui-ref.js：ui.js 的 UI.faces 与 strategy-screen.js 的文字工具）逐行比较。
# 界面本身（UGUI 的对话框、行内头像的叠放、打字机）不能无头运行，只做编译检查（Tests~/build.sh）与代码审查。
# 用法：Tests~/ui/run.sh      退出码 0 = 全部一致且自检通过。OUT_DIR 可改输出目录（缺省 Tests~/out）。
# 找不到 node 或网页版（SANGUO_WEB，缺省 ../../../sanguozhi2-web）时判失败；SANGUO_ALLOW_NO_NODE=1 时只跑 C# 与自检。
set -u -o pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
SIM="$HERE/../sim"
OUT="${OUT_DIR:-$HERE/../out}/ui"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
RULES=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/SpecialsData.g.cs "$S"/Data/Generated/WorldData.g.cs "$S"/Data/Generated/WorldGeo.g.cs \
         "$S"/Data/Generated/TacticsData.g.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$SIM"/BattleTerrainStub.cs "$S"/UI/UIText.cs; do
  [ -f "$f" ] && RULES+=("$f")
done
echo "== 编译 HarnessUI"
msg=$(mcs -langversion:7.2 -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -r:System.Numerics.dll -out:"$OUT/HarnessUI.exe" \
      "$SIM/UnityStub.cs" "$SIM/SimLib.cs" "${RULES[@]}" "$HERE/HarnessUI.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 1; fi
fails=0
(cd "$HERE" && mono "$OUT/HarnessUI.exe" > "$OUT/cs.txt" 2> "$OUT/cs.err"); st=$?
cat "$OUT/cs.err"
[ $st -ne 0 ] && { echo "== C# 自检失败（$st）"; fails=$((fails + 1)); }
echo "C#：$(wc -l < "$OUT/cs.txt") 行，其中带头像 $(grep -c '\[\[F:' "$OUT/cs.txt") 行"
if ! command -v node >/dev/null 2>&1; then
  if [ "${SANGUO_ALLOW_NO_NODE:-}" = 1 ]; then echo "== JS 对照：跳过（找不到 node）"; exit $fails; fi
  echo "== JS 对照：失败（找不到 node；设 SANGUO_ALLOW_NO_NODE=1 可跳过）"; exit $((fails + 1))
fi
(cd "$HERE" && node "$HERE/ui-ref.js" > "$OUT/js.txt") || { echo "== ui-ref.js 运行失败"; exit $((fails + 1)); }
echo "JS：$(wc -l < "$OUT/js.txt") 行"
if cmp -s "$OUT/js.txt" "$OUT/cs.txt"; then
  echo "== JS 对照：逐行一致"
else
  echo "== JS 对照：不一致（前 20 处）"
  diff "$OUT/js.txt" "$OUT/cs.txt" | head -40
  fails=$((fails + 1))
fi
echo "== 界面文字：$( [ $fails -eq 0 ] && echo 全部通过 || echo "失败 $fails 项" )"
exit $fails
