#!/bin/bash
# 战场地形（第三版 §2）的无头测试：Battle/BattleTerrain.cs + View/WorldGeo.cs（TerrainModel）+ 规则层文件与 sim/UnityStub.cs 一起编译，
# 与网页版（Node 运行 ../sanguozhi2-web 的 world-geo.js、map-view.js、battle-terrain.js 等，见 refs.js）为经典 / 世界两个剧本的
# 全部（城池, 来向）生成的战场逐格比对，并检查可玩性与 BattleModel 接线。
# 用法：Tests~/battlemap/run.sh        退出码 = 失败的检查项数（编译失败 / 缺 node 为 99）
#   OUT_DIR=… 改输出目录（缺省 Tests~/out）；SKIP_REFS=1 沿用已有的参考值（refs.txt）
#   BM_DUMP=china:22:21,world:5:-1 …（不能与 SKIP_REFS 同用）：参考值另导出这几张战场的取样栅格，栅格逐字节不一致时打印 [G 诊断]（各字段不同的点数 / 最大差）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/battlemap"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }

if [ "${SKIP_REFS:-0}" = 1 ] && [ -f "$OUT/refs.txt" ]; then echo "== 参考值：沿用 $OUT/refs.txt"
else
  command -v node >/dev/null 2>&1 || { echo "找不到 node（生成参考值需要）" >&2; exit 99; }
  echo "== 参考值（网页版）"
  node "$HERE/refs.js" "$OUT" || exit 99
fi

SRC=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$S"/Battle/BattleTerrain.cs "$S"/View/WorldGeo.cs; do
  [ -f "$f" ] && SRC+=("$f")
done
echo "== 编译"
msg=$(mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -r:System.Numerics.dll -out:"$OUT/battlemap.exe" \
      "$HERE/../sim/UnityStub.cs" "$HERE/../sim/SimLib.cs" "${SRC[@]}" "$HERE/HarnessBattleMap.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对"
mono "$OUT/battlemap.exe" "$OUT"
