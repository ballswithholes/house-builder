#!/bin/bash
# 无头规则测试：把规则文件（Data、Data/Generated 中规则用的数据（必杀、世界剧本、地理、计策）、Model、Strategy 的 StrategyAI / Conquest、
# Battle/BattleModel、Battle/Specials、Battle/Tactics（存在时））与 UnityStub.cs、SimLib.cs、BattleTerrainStub.cs 一起编译，
# 每个 Harness*.cs 各成一个程序并运行。（头像、乐谱、模型等画面数据不进规则测试，免得它们用到替身没有的 UnityEngine API；
# 战场地形用 BattleTerrainStub.cs：同网页版 tests/sim.js 在 Node 下没有战略地图，一律退回旧的随机地形）
# 用法：Tests~/sim/run.sh               运行全部 Harness*.cs
#       Tests~/sim/run.sh Harness2 Data  只运行 Harness2.cs、HarnessData.cs
# 退出码 = 失败的程序数（编译失败也算）。输出程序写到 Tests~/out/sim/（已 gitignore），各程序的输出另存为同目录的 <程序名>.txt。
# JS 对照（jscompare.js）：与 C# 程序并行运行网页版 tests/sim.js、world.js、move.js（只运行所选程序对应的那些），
#   最后要求 JS 的每一行输出（去掉计时）都出现在对应 C# 程序的输出里；算作一个程序（"JS 对照"）。
#   找不到 node 或网页版（SANGUO_WEB，缺省 ../../../sanguozhi2-web）时判失败；SANGUO_ALLOW_NO_NODE=1 跳过，SANGUO_JS_COMPARE=0 不做对照。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/sim"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }

RULES=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/SpecialsData.g.cs "$S"/Data/Generated/WorldData.g.cs "$S"/Data/Generated/WorldGeo.g.cs \
         "$S"/Data/Generated/TacticsData.g.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$HERE"/BattleTerrainStub.cs; do
  [ -f "$f" ] && RULES+=("$f")
done

if [ $# -gt 0 ]; then
  H=(); for n in "$@"; do n="${n%.cs}"; [ -f "$HERE/$n.cs" ] || n="Harness$n"; H+=("$HERE/$n.cs"); done
else
  mapfile -t H < <(ls "$HERE"/Harness*.cs | sort)
fi

NAMES=(); for h in "${H[@]}"; do NAMES+=("$(basename "$h" .cs)"); done
JST=""; JSPID=""; jsskip=""
if [ "${SANGUO_JS_COMPARE:-1}" = 0 ]; then jsskip="SANGUO_JS_COMPARE=0"
elif ! command -v node >/dev/null 2>&1; then
  # 没有 node 就问不了 jscompare.js；按它的对应表（有 JS 对照的程序）判断这次是否需要对照
  for n in "${NAMES[@]}"; do case " Harness Harness2 HarnessSpecials HarnessProgress HarnessTactics HarnessWorld HarnessMove " in *" $n "*) jsskip="找不到 node";; esac; done
else
  JST=$(node "$HERE/jscompare.js" tests "${NAMES[@]}")
  if [ -n "$JST" ]; then
    rm -rf "$OUT/js"; mkdir -p "$OUT/js"
    node "$HERE/jscompare.js" run "$OUT" $JST > "$OUT/js/run.log" 2>&1 &
    JSPID=$!
  fi
fi

fails=0
for h in "${H[@]}"; do
  name="$(basename "$h" .cs)"
  [ -f "$h" ] || { echo "== $name：找不到文件"; fails=$((fails + 1)); continue; }
  echo "== $name"
  rm -f "$OUT/$name.txt"
  msg=$(mcs -langversion:7.2 -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -r:System.Numerics.dll -out:"$OUT/$name.exe" \
        "$HERE/UnityStub.cs" "$HERE/SimLib.cs" "${RULES[@]}" "$h" 2>&1)
  if [ $? -ne 0 ]; then
    printf '%s\n' "$msg" | grep -E 'error|错误' | head -30
    echo "== $name：编译失败"; fails=$((fails + 1)); continue
  fi
  t0=$(date +%s)
  (cd "$HERE" && mono "$OUT/$name.exe") | tee "$OUT/$name.txt"; st=${PIPESTATUS[0]}
  echo "== $name：$( [ $st -eq 0 ] && echo 通过 || echo "失败 $st" )（$(( $(date +%s) - t0 )) 秒）"
  [ $st -ne 0 ] && fails=$((fails + 1))
done
progs=${#H[@]}
if [ -n "$jsskip" ]; then
  progs=$((progs + 1))
  if [ "$jsskip" = "SANGUO_JS_COMPARE=0" ] || [ "${SANGUO_ALLOW_NO_NODE:-}" = 1 ]; then echo "== JS 对照：跳过（$jsskip）"
  else echo "== JS 对照：失败（$jsskip；设 SANGUO_ALLOW_NO_NODE=1 可跳过）"; fails=$((fails + 1)); fi
elif [ -n "$JST" ]; then
  progs=$((progs + 1))
  echo "== JS 对照（$(for t in $JST; do printf 'tests/%s.js ' "$t"; done)→ C# 输出）"
  t0=$(date +%s)
  wait "$JSPID"
  (cd "$HERE" && node "$HERE/jscompare.js" compare "$OUT" "${NAMES[@]}"); st=$?
  echo "== JS 对照：$( [ $st -eq 0 ] && echo 通过 || echo "失败 $st" )（等待 JS $(( $(date +%s) - t0 )) 秒）"
  [ $st -ne 0 ] && fails=$((fails + 1))
fi
echo "== 共 $progs 个程序，失败 $fails 个"
exit $fails
