#!/bin/bash
# 人物模型库 Unity 接口的冒烟测试：View/Figure.cs、FigureCrowd.cs、FigureAtlas.cs（含 Unity 部分）与 UnityEngine 替身
# （../sim/UnityStub.cs + FigureUnityStub.cs）一起编译运行（FigureUnityHarness.cs）。头像模块的 Unity 部分依赖 UGUI，
# 所以整体按 SANGUO_HEADLESS 编译，人物模型库这三个文件先把 `#if !SANGUO_HEADLESS` 换成 `#if true`
# （并去掉需要 Model/General 的一个重载）；Raster.ToTexture 由 FigureUnityExt.cs 补上。
# 用法：Tests~/figure/unity.sh      退出码 = 失败项数（99 = 编译失败）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
S="$(cd "$HERE/../.." && pwd)/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/figure"
mkdir -p "$OUT"
sed -E 's/public struct (Vector2|Vector3|Color)$/public partial struct \1/; s/public static class (Debug|Application|Time)$/public static partial class \1/' \
  "$HERE/../sim/UnityStub.cs" > "$OUT/UnityStub.partial.cs"
for f in FigureAtlas Figure FigureCrowd; do
  sed -e 's/^#if !SANGUO_HEADLESS$/#if true/' -e '/public static Figure General(General g, FigOpts opts = null)/d' "$S/View/$f.cs" > "$OUT/$f.unity.cs"
done
echo "== 编译"
msg=$(mcs -langversion:7.2 -define:SANGUO_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -r:System.IO.Compression.dll \
      -out:"$OUT/figunity.exe" "$OUT/UnityStub.partial.cs" "$HERE/FigureUnityStub.cs" "$HERE/FigureUnityExt.cs" \
      "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/View/Raster.cs "$S"/View/Art.cs "$S"/View/Portrait.cs "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs \
      "$OUT/FigureAtlas.unity.cs" "$OUT/Figure.unity.cs" "$OUT/FigureCrowd.unity.cs" "$HERE/FigureUnityHarness.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 冒烟测试"
mono "$OUT/figunity.exe"
