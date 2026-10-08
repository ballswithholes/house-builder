#!/bin/bash
# 无头地图测试：View/WorldGeo.cs（与渲染无关的地理查询与地形模型）+ 规则层文件 + sim/UnityStub.cs 一起编译，
# 与网页版（Node 运行 ../sanguozhi2-web 的 world-geo.js、map-view.js）的参考值比对：
#   投影 / 逆投影、整图范围与镜头地区、海岸 / 河流距离场、陆海判定、平滑场取样（高原、生物群落）、山脉、
#   高度、实际地表、整张地形网格、地表颜色、树木分布（注入网页版随机数）、海上航线（A* + 拉直 + 平滑）。
# 用法：Tests~/map/run.sh        退出码 = 失败的检查项数（编译失败 / 缺 node 为 99）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/map"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
command -v node >/dev/null 2>&1 || { echo "找不到 node（生成参考值需要）" >&2; exit 99; }

echo "== 参考值（网页版）"
node "$HERE/refs.js" "$OUT" || exit 99

SRC=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/View/WorldGeo.cs; do
  [ -f "$f" ] && SRC+=("$f")
done
echo "== 编译"
msg=$(mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -out:"$OUT/map.exe" \
      "$HERE/../sim/UnityStub.cs" "${SRC[@]}" "$HERE/MapHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对（地理与地形模型）"
mono "$OUT/map.exe" "$OUT"; fails=$?

# 地图视图冒烟测试：MapView / CultureArt / Art 与 UnityViewStub.cs 替身一起编译（把 sim 替身里的部分类型改成 partial）
sed -E 's/public struct (Vector2|Vector3)$/public partial struct \1/; s/public static class Debug$/public static partial class Debug/' \
  "$HERE/../sim/UnityStub.cs" > "$OUT/UnityStub.partial.cs"
VIEW=()
for f in "${SRC[@]}" "$S"/View/Art.cs "$S"/View/CultureArt.cs "$S"/View/MapView.cs; do VIEW+=("$f"); done
echo "== 编译（地图视图）"
msg=$(mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -out:"$OUT/mapview.exe" \
      "$OUT/UnityStub.partial.cs" "$HERE/UnityViewStub.cs" "${VIEW[@]}" "$HERE/MapViewHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 地图视图"
mono "$OUT/mapview.exe" "$OUT"; fails=$((fails + $?))
exit $fails
