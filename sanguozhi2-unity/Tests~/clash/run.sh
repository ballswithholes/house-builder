#!/bin/bash
# 攻击画面（第三版，Battle/Clash.cs）的无头测试：
#   1. web.mjs：headless Chromium（SwiftShader WebGL）打开网页版 tests/clash.html，按 cases.json 的用例（前四个必测：
#      汉 平原、罗马 石城城门、非洲 泥砖本城、凯尔特 海岸登陆；其后为各文化 / 群落 / 兵力比 / 必杀 / 竖屏）导出 web-<名>.json
#      （布景、地形网格、道具、草丛野花、两军阵列与倒地时刻、箭与碎块、镜头、雾）并在用例的各个时刻（缺省接敌 1.66 s 与
#      混战 2.2 s）截图 web-<名>-<时刻>.png（HUD 隐藏）；
#   2. ClashHarness.cs：先比对设定（开 / 快 / 关）与第三版迁移（Core/Game.cs 的 MigrateV3 原样摘出编译，对照网页版
#      clash.js loadMode + game.js migrateV3），再用 C# 移植（Clash.cs 以 CLASH_HEADLESS 编译，界面换成空实现）为同一组参数
#      建场景（BuildSync），逐项比对；最后是 Clash.Play 的流程冒烟测试（排队、接管与恢复全局设定、跳过、必杀、「关」）；
#   3. ClashRender.cs：软件光栅器按 C# 场景里的物体（地面、道具、草丛野花、水、旗、人物、帅旗杆、箭）与镜头在同样的时刻画成
#      unity-<名>-<时刻>.png（逐行移植 ClashGround / ClashProp / ClashGrass / ClashFlag / ClashWater 着色器与 LowPoly 光照，太阳阴影），
#      web.mjs compose 左右拼成 side-<名>-<时刻>.png（左 网页版、右 C#）。人工查看。
# 编译：规则文件 + View/WorldGeo / Art / Raster / CultureArt / Portrait / Figure*（-define:SANGUO_HEADLESS）+ Battle/Clash.cs
#   （-define:CLASH_HEADLESS）；UnityEngine 替身 = ../sim/UnityStub.cs（改 partial）+ ../figure/FigureUnityStub.cs（改 partial、
#   supportsInstancing 可写）+ ../figure/FigureUnityExt.cs + ClashStub.cs。人物模型库三个文件把 `#if !SANGUO_HEADLESS` 换成 `#if true`，
#   Figure.General(General) 改用 ClashHarness.cs 里的 Describe（头像模块的 Unity 部分无头时不编译）。
# 需要 node + Playwright 的 Chromium（/opt/node22/lib/node_modules/playwright，不要 playwright install）、mcs / mono。
# 用法：Tests~/clash/run.sh [名…]        退出码 = 不一致的项数（99 = 环境 / 编译）
#   SKIP_WEB=1 沿用已有的网页版参考；NO_RENDER=1 不画图；CLASH_CS=<路径> 用另一份 Clash.cs（草稿）
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
S="$ROOT/Assets/Sanguo/Scripts"
OUT="${OUT_DIR:-$HERE/../out}/clash"
mkdir -p "$OUT"
command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 99; }
cp "$HERE/cases.json" "$OUT/cases.json"
want_sceneof=1
if [ $# -gt 0 ]; then want_sceneof=0; for a in "$@"; do [ "$a" = sceneof ] && want_sceneof=1; done; fi
if [ -z "${SKIP_WEB:-}" ]; then
  echo "== 网页版参考"
  node "$HERE/web.mjs" refs "$OUT" "$@" || exit 99
fi
if [ $want_sceneof = 1 ] && { [ -z "${SKIP_WEB:-}" ] || [ ! -f "$OUT/sceneof.txt" ]; }; then
  echo "== 网页版参考：布景取自战场（sceneof.txt）"
  node "$HERE/sceneof.js" "$OUT" || exit 99
fi
CS="${CLASH_CS:-$S/Battle/Clash.cs}"
sed -E 's/public struct (Vector2|Vector3|Color)$/public partial struct \1/; s/public static class (Debug|Application|Time)$/public static partial class \1/' \
  "$HERE/../sim/UnityStub.cs" > "$OUT/UnityStub.partial.cs"
sed -E 's/public static bool supportsInstancing => true;/public static bool supportsInstancing = true;/;
        s/^    public class (Object|Camera|Transform|MaterialPropertyBlock)( : [A-Za-z]+)?$/    public partial class \1\2/;
        s/^    public class Texture2D : Texture$/    public partial class Texture2D : Texture/;
        s/^    public struct Quaternion$/    public partial struct Quaternion/;
        s/^    public static class (RenderSettings|Graphics)$/    public static partial class \1/' \
  "$HERE/../figure/FigureUnityStub.cs" > "$OUT/FigureUnityStub.clash.cs"
for f in FigureAtlas Figure FigureCrowd; do
  sed -e 's/^#if !SANGUO_HEADLESS$/#if true/' -e 's/return General(Portrait.Describe(g), opts);/return General(ClashHeadless.Describe(g), opts);/' "$S/View/$f.cs" > "$OUT/$f.unity.cs"
done
SRC=()
for f in "$S"/Data/*.cs "$S"/Data/Generated/*.cs "$S"/Model/*.cs "$S"/Strategy/StrategyAI.cs "$S"/Strategy/Conquest.cs \
         "$S"/Battle/BattleModel.cs "$S"/Battle/Specials.cs "$S"/Battle/Tactics.cs "$S"/Battle/BattleTerrain.cs \
         "$S"/View/WorldGeo.cs "$S"/View/Raster.cs "$S"/View/Art.cs "$S"/View/CultureArt.cs "$S"/View/Portrait.cs \
         "$S"/View/FigureMesh.cs "$S"/View/FigureRig.cs; do
  [ -f "$f" ] && SRC+=("$f")
done
# Game.MigrateV3（第三版的设定迁移）：从 Core/Game.cs 原样摘出（ClashResetMark 到方法结束），包进一个小类；没有时为空实现
MIG=$(awk '/public const string ClashResetMark/{p=1} p{print} p&&m&&/^        }$/{exit} /static void MigrateV3\(\)/{m=1}' "$S/Core/Game.cs")
{
  echo 'using UnityEngine;'
  echo 'namespace Sanguo { static class GameV3Migrate {'
  if printf '%s\n' "$MIG" | grep -q 'static void MigrateV3()'; then
    printf '%s\n' "$MIG"
    echo 'public const string Mark = ClashResetMark; public static readonly bool Found = true; public static void Run() { MigrateV3(); } } }'
  else
    echo 'public const string Mark = "sanguozhi2_v3_clash_reset"; public static readonly bool Found = false; public static void Run() { } } }'
  fi
} > "$OUT/GameMigrate.cs"
SRC+=("$OUT/FigureAtlas.unity.cs" "$OUT/Figure.unity.cs" "$OUT/FigureCrowd.unity.cs" "$CS" "$OUT/GameMigrate.cs")
STUB=("$OUT/UnityStub.partial.cs" "$OUT/FigureUnityStub.clash.cs" "$HERE/../figure/FigureUnityExt.cs" "$HERE/ClashStub.cs")
FLAGS=(-langversion:7.2 -define:SANGUO_HEADLESS -define:CLASH_HEADLESS -optimize+ -nowarn:0414,0649,0169,0162,0219,0067 -r:System.Core.dll -r:System.IO.Compression.dll)
echo "== 编译"
msg=$(mcs "${FLAGS[@]}" -out:"$OUT/clash.exe" "${STUB[@]}" "${SRC[@]}" "$HERE/ClashHarness.cs" "$HERE/ClashRender.cs" "$HERE/../figure/FigPng.cs" 2>&1)
if [ $? -ne 0 ]; then printf '%s\n' "$msg" | grep -E 'error|错误' | head -30; echo "== 编译失败"; exit 99; fi
echo "== 比对与渲染"
NO_RENDER="${NO_RENDER:-}" mono "$OUT/clash.exe" "$OUT" "$@"; fails=$?
if [ -z "${NO_RENDER:-}" ]; then
  echo "== 拼图"
  node "$HERE/web.mjs" compose "$OUT" "$@" || exit 99
fi
exit $fails
