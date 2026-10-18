#!/bin/bash
# 无头地图测试（第三版）：View/WorldGeo.cs（与渲染无关的地理查询与地形模型）+ 规则层文件 + sim/UnityStub.cs 一起编译，
# 与网页版（Node 运行 ../sanguozhi2-web 的 world-geo.js、map-view.js、art.js、culture-art.js）的参考值逐项比对：
#   投影 / 逆投影、整图范围与镜头地区、Perlin、海岸 / 河流距离场、陆海判定、平滑场取样、山脉、高度、
#   主网格 / 远景栅格、城基、城外村落、数据纹理（逐字节）、实际地表、TerrainInfo、树木 / 石块 / 灌丛 / 村舍（逐个实例）、
#   道路 / 桥 / 关隘、海上航线。
# 第二步（地图视图）：MapView / CultureArt / Art / Raster 与 UnityViewStub.cs 替身一起编译，比对细节纹理（逐字节）、
#   平滑网格工具与植被 / 各文化城池模型的顶点数、地形块四叉树、各类网格的三角形数，并做构建 / 刷新 / 更新 / 行军冒烟测试。
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
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$S"/Battle/BattleTerrain.cs "$S"/View/WorldGeo.cs; do
  [ -f "$f" ] && SRC+=("$f")
done
echo "== 编译"
msg=$(mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169,0162,0219 -r:System.Core.dll -out:"$OUT/map.exe" \
      "$HERE/../sim/UnityStub.cs" "${SRC[@]}" "$HERE/MapHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对（地理与地形模型）"
mono "$OUT/map.exe" "$OUT"; fails=$?

# 地图视图冒烟测试：MapView / CultureArt / Art 与 UnityViewStub.cs 替身一起编译（把 sim 替身里的部分类型改成 partial）
sed -E 's/public struct (Vector2|Vector3|Color)$/public partial struct \1/; s/public static class (Debug|Application|Time)$/public static partial class \1/' \
  "$HERE/../sim/UnityStub.cs" > "$OUT/UnityStub.partial.cs"
VIEW=()
for f in "${SRC[@]}" "$S"/View/Raster.cs "$S"/View/Art.cs "$S"/View/CultureArt.cs "$S"/View/MapView.cs; do VIEW+=("$f"); done
echo "== 编译（地图视图）"
msg=$(mcs -langversion:7.2 -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -out:"$OUT/mapview.exe" \
      "$OUT/UnityStub.partial.cs" "$HERE/UnityViewStub.cs" "${VIEW[@]}" "$HERE/MapViewHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 地图视图"
mono "$OUT/mapview.exe" "$OUT"; fails=$((fails + $?))
exit $fails
