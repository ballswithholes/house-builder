#!/bin/bash
# 无头规则测试：把规则文件（Data、Data/Generated、Model、Strategy 的 StrategyAI / Conquest、Battle/BattleModel、
# Battle/Specials（存在时））与 UnityStub.cs、SimLib.cs 一起编译，每个 Harness*.cs 各成一个程序并运行。
# 用法：Tests~/sim/run.sh               运行全部 Harness*.cs
#       Tests~/sim/run.sh Harness2 Data  只运行 Harness2.cs、HarnessData.cs
# 退出码 = 失败的程序数（编译失败也算）。输出程序写到 Tests~/out/sim/（已 gitignore）。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/sim"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }

RULES=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs; do
  [ -f "$f" ] && RULES+=("$f")
done

if [ $# -gt 0 ]; then
  H=(); for n in "$@"; do n="${n%.cs}"; [ -f "$HERE/$n.cs" ] || n="Harness$n"; H+=("$HERE/$n.cs"); done
else
  mapfile -t H < <(ls "$HERE"/Harness*.cs | sort)
fi

fails=0
for h in "${H[@]}"; do
  name="$(basename "$h" .cs)"
  [ -f "$h" ] || { echo "== $name：找不到文件"; fails=$((fails + 1)); continue; }
  echo "== $name"
  msg=$(mcs -langversion:7.2 -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -out:"$OUT/$name.exe" \
        "$HERE/UnityStub.cs" "$HERE/SimLib.cs" "${RULES[@]}" "$h" 2>&1)
  if [ $? -ne 0 ]; then
    printf '%s\n' "$msg" | grep -E 'error|错误' | head -30
    echo "== $name：编译失败"; fails=$((fails + 1)); continue
  fi
  t0=$(date +%s)
  (cd "$HERE" && mono "$OUT/$name.exe"); st=$?
  echo "== $name：$( [ $st -eq 0 ] && echo 通过 || echo "失败 $st" )（$(( $(date +%s) - t0 )) 秒）"
  [ $st -ne 0 ] && fails=$((fails + 1))
done
echo "== 共 ${#H[@]} 个程序，失败 $fails 个"
exit $fails
