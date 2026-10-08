#!/bin/bash
# 单挑格斗的无头测试：把规则文件 + Battle/DuelSim.cs 与 Tests~/sim/UnityStub.cs 一起编译成 HarnessDuel.exe，
# 运行 AI 对 AI、脚本玩家（跳入 / 连按）对 AI 的胜率统计，并与网页版 node tests/duel-bots.js（同样的场数与种子）逐行对照。
# 用法：Tests~/duel/run.sh [每组场数，默认 40] [种子，默认 7] [容差（百分点），默认 0]
#   退出码：C# 自身检查的失败数 + 与 JS 不一致的行数（找不到 node 时只跑 C#）。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$(cd "$HERE/../.." && pwd)"
S="$PROJ/Assets/Sanguo/Scripts"
WEB="$(cd "$PROJ/.." && pwd)/sanguozhi2-web"
OUT="${OUT_DIR:-$HERE/../out}/duel"
N="${1:-40}"; SEED="${2:-7}"; TOL="${3:-0}"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }

RULES=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/DuelSim.cs; do
  [ -f "$f" ] && RULES+=("$f")
done
msg=$(mcs -langversion:7.2 -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -out:"$OUT/HarnessDuel.exe" \
      "$PROJ/Tests~/sim/UnityStub.cs" "$PROJ/Tests~/sim/SimLib.cs" "${RULES[@]}" "$HERE/HarnessDuel.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error' | head -30; echo "== HarnessDuel：编译失败"; exit 98; fi

t0=$(date +%s)
mono "$OUT/HarnessDuel.exe" "$N" "$SEED" > "$OUT/cs.txt"; cs=$?
cat "$OUT/cs.txt"
echo "== C#：$( [ $cs -eq 0 ] && echo 通过 || echo "失败 $cs" )（$(( $(date +%s) - t0 )) 秒）"

if ! command -v node >/dev/null 2>&1 || [ ! -f "$WEB/tests/duel-bots.js" ]; then echo "== 跳过 JS 对照（无 node 或网页版）"; exit $cs; fi
node "$WEB/tests/duel-bots.js" "$N" "$SEED" > "$OUT/js.txt"
# 逐行比较全部百分比数字（顺序相同）；容差内视为一致
diffs=$(python3 -I - "$OUT/cs.txt" "$OUT/js.txt" "$TOL" <<'PY'
import re, sys
def nums(p):
    return [line.rstrip('\n') for line in open(p, encoding='utf-8')]
a, b, tol = nums(sys.argv[1]), nums(sys.argv[2]), float(sys.argv[3])
bad = 0
for i, (x, y) in enumerate(zip(a, b)):
    if x == y: continue
    nx = [float(v) for v in re.findall(r'(?<![\w(])(\d+(?:\.\d+)?)(?=%| |$)', x)]
    ny = [float(v) for v in re.findall(r'(?<![\w(])(\d+(?:\.\d+)?)(?=%| |$)', y)]
    if len(nx) == len(ny) and all(abs(p - q) <= tol for p, q in zip(nx, ny)): continue
    bad += 1
    print('  C#: ' + x); print('  JS: ' + y)
if len(a) != len(b): bad += 1; print('  行数不同：C# %d / JS %d' % (len(a), len(b)))
print('BAD %d' % bad)
PY
)
printf '%s\n' "$diffs" | grep -v '^BAD'
bad=$(printf '%s\n' "$diffs" | sed -n 's/^BAD //p')
echo "== 与 JS 对照（${N} 场/组，种子 ${SEED}，容差 ${TOL}）：$( [ "${bad:-1}" = 0 ] && echo 一致 || echo "不一致 ${bad} 行" )"
exit $(( cs + ${bad:-1} ))
