#!/bin/bash
# 单挑画面（第三版，Battle/DuelGame.cs + DuelFigure.cs + DuelArena.cs）的无头测试：
#   1. web.mjs refs：headless Chromium（SwiftShader WebGL）打开网页版 tests/duel.html，按 cases.json 为每对武将开一场观战单挑，
#      导出 web-<名>.json（兵器选择、兵器长度校准、场地统计、各镜头的姿势数据）并截图 web-<名>-<镜头>.png；
#   2. DuelViewHarness.cs：C# 移植（DuelGame.cs 原样编译；界面 DuelHud / DuelInput、UIKit、Sfx、Game 换成记录调用的替身）
#      —— 流程：真实的 DuelScreen（Setup → 协程 → 每帧 Update）由脚本玩家 / 电脑打完整场，检查各状态、事件、动作片段与结果；
#      —— 对照：同一组参数建场景，逐项比对网页版参考（校准逐值、场地统计、各镜头的片段与骨骼位置）；
#   3. DuelRender.cs：软件光栅器把 C# 场景画成 unity-<名>-<镜头>.png（DuelGround / DuelProp / Foliage / DuelWater / DuelSky /
#      Figure 着色器的 C# 参照实现，主光阴影、线性雾），web.mjs compose 左右拼成 side-<名>-<镜头>.png（左 网页版、右 C#）。人工查看。
# 编译：规则文件 + View/WorldGeo / Raster / Art / Portrait / Figure*（-define:SANGUO_HEADLESS）+ Battle/DuelSim / DuelFigure / DuelArena / DuelGame；
#   UnityEngine 替身 = ../sim/UnityStub.cs（改 partial）+ ../figure/FigureUnityStub.cs（改 partial、协程登记、Transform.position 含父缩放）
#   + ../figure/FigureUnityExt.cs + DuelStub.cs。人物模型库三个文件把 `#if !SANGUO_HEADLESS` 换成 `#if true`，
#   Figure.General(General) 改用 DuelFigure.Describe（头像模块的 Unity 部分无头时不编译）。
# 用法：Tests~/duelview/run.sh [名…]     退出码 = 不一致的项数（99 = 环境 / 编译）
#   SKIP_WEB=1 沿用已有的网页版参考；NO_RENDER=1 不画图；FLOW_ONLY=1 只跑流程测试
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
S="$ROOT/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/duelview"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
cp "$HERE/cases.json" "$OUT/cases.json"
if [ -z "${SKIP_WEB:-}" ] && [ -z "${FLOW_ONLY:-}" ]; then
  echo "== 网页版参考"
  node "$HERE/web.mjs" gens "$OUT" "$@" || exit 99
  node "$HERE/web.mjs" refs "$OUT" "$@" || exit 99
fi
sed -E 's/public struct (Vector2|Vector3|Color)$/public partial struct \1/; s/public static class (Debug|Application|Time|Mathf)$/public static partial class \1/' \
  "$HERE/../sim/UnityStub.cs" > "$OUT/UnityStub.partial.cs"
# 人物替身：几个类改 partial；MonoBehaviour.StartCoroutine 登记协程（由测试逐帧推进）；Transform.position 含父节点缩放（单挑的镜像 x 缩放）
sed -E 's/public static bool supportsInstancing => true;/public static bool supportsInstancing = true;/;
        s/^    public class (Object|Camera|Transform|MaterialPropertyBlock|Renderer|Material)( : [A-Za-z]+)?$/    public partial class \1\2/;
        s/^    public class Texture2D : Texture$/    public partial class Texture2D : Texture/;
        s/^    public struct Quaternion$/    public partial struct Quaternion/;
        s/^    public struct Matrix4x4$/    public partial struct Matrix4x4/;
        s/^    public static class (RenderSettings|Graphics|QualitySettings|Screen)$/    public static partial class \1/;
        s/public Coroutine StartCoroutine\(IEnumerator e\) \{ return new Coroutine\(\); \}/public Coroutine StartCoroutine(IEnumerator e) { DuelStubHooks.Coroutines.Add(e); return new Coroutine(); }/;
        s/get \{ return parent == null \? localPosition : parent.position \+ parent.rotation \* localPosition; \}/get { return parent == null ? localPosition : localToWorldMatrix.MultiplyPoint3x4(Vector3.zero); }/' \
  "$HERE/../figure/FigureUnityStub.cs" > "$OUT/FigureUnityStub.duel.cs"
for f in FigureAtlas Figure FigureCrowd; do
  sed -e 's/^#if !SANGUO_HEADLESS$/#if true/' -e 's/return General(Portrait.Describe(g), opts);/return General(DuelFigure.Describe(g), opts);/' "$S/View/$f.cs" > "$OUT/$f.unity.cs"
done
SRC=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$S"/Battle/BattleTerrain.cs \
         "$S"/View/WorldGeo.cs "$S"/View/Raster.cs "$S"/View/Art.cs "$S"/View/Portrait.cs \
         "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs; do
  [ -f "$f" ] && SRC+=("$f")
done
SRC+=("$OUT/FigureAtlas.unity.cs" "$OUT/Figure.unity.cs" "$OUT/FigureCrowd.unity.cs" "$S/Battle/DuelSim.cs" "$S/Battle/DuelFigure.cs" "$S/Battle/DuelArena.cs" "$S/Battle/DuelGame.cs")
STUB=("$OUT/UnityStub.partial.cs" "$OUT/FigureUnityStub.duel.cs" "$HERE/../figure/FigureUnityExt.cs" "$HERE/DuelStub.cs")
FLAGS=(-langversion:7.2 -define:SANGUO_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -r:System.IO.Compression.dll)
echo "== 编译"
msg=$(mcs "${FLAGS[@]}" -out:"$OUT/duelview.exe" "${STUB[@]}" "${SRC[@]}" "$HERE/DuelViewHarness.cs" "$HERE/DuelRender.cs" "$HERE/../figure/FigPng.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -40; echo "== 编译失败"; exit 99; fi
echo "== 流程与对照"
NO_RENDER="${NO_RENDER:-}" FLOW_ONLY="${FLOW_ONLY:-}" mono "$OUT/duelview.exe" "$OUT" "$@"; fails=$?
if [ -z "${NO_RENDER:-}" ] && [ -z "${FLOW_ONLY:-}" ]; then
  echo "== 拼图"
  node "$HERE/web.mjs" compose "$OUT" "$@" || exit 99
fi
exit $fails
