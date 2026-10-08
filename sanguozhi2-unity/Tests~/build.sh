#!/bin/bash
# 编译检查：用 mcs 对照 Unity 引用程序集编译全部运行时脚本与 Editor 脚本（不运行 Unity）。
#   UnityEngine.Modules 2021.3.33（运行时模块）、Unity3D.SDK 2021.1.14.1（UnityEditor）、
#   Unity3D.UnityEngine.UI 2020.3.21（UGUI）——均来自 NuGet。
# 引用程序集放在 Tests~/refs/（已 gitignore）；缺失时先尝试从 $UNITYREF_CACHE（缺省 /tmp/unityref）复制，
# 再不行就从 NuGet 下载解压。输出程序集写到 Tests~/out/（已 gitignore）。
# 用法：Tests~/build.sh            退出码 0 = 全部编译通过
#       REFS_DIR=... OUT_DIR=... 可改路径；UNITYREF_CACHE= （置空）强制从 NuGet 下载。
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJ="$(cd "$HERE/.." && pwd)"
P="$PROJ/Assets/Sanguo"
REFS_DIR="${REFS_DIR:-$HERE/refs}"
OUT_DIR="${OUT_DIR:-$HERE/out}"
UNITYREF_CACHE="${UNITYREF_CACHE-/tmp/unityref}"
FACADE=/usr/lib/mono/4.5/Facades/netstandard.dll

# 包名 | 版本 | 子目录 | 用来判断是否已就绪的文件
PKGS=(
  "UnityEngine.Modules|2021.3.33|m|lib/net45/UnityEngine.CoreModule.dll"
  "Unity3D.SDK|2021.1.14.1|sdk|lib/UnityEditor.dll"
  "Unity3D.UnityEngine.UI|2020.3.21|ui|lib/UnityEngine.UI.dll"
)

fetch_pkg() {
  local name="$1" ver="$2" sub="$3" probe="$4"
  local dst="$REFS_DIR/$sub"
  [ -f "$dst/$probe" ] && return 0
  mkdir -p "$dst"
  if [ -n "$UNITYREF_CACHE" ] && [ -f "$UNITYREF_CACHE/$sub/$probe" ]; then
    echo "[refs] 从 $UNITYREF_CACHE/$sub 复制 $name"
    cp -r "$UNITYREF_CACHE/$sub/lib" "$dst/" && [ -f "$dst/$probe" ] && return 0
  fi
  local pkg="$REFS_DIR/_dl/$name.$ver.nupkg"
  mkdir -p "$REFS_DIR/_dl"
  echo "[refs] 下载 $name $ver"
  # 先走 api.nuget.org 的扁平容器（包名小写），再退回 www.nuget.org/api/v2（会重定向到 globalcdn）
  local lc; lc=$(printf '%s' "$name" | tr 'A-Z' 'a-z')
  curl -sSL --fail -o "$pkg" "https://api.nuget.org/v3-flatcontainer/$lc/$ver/$lc.$ver.nupkg" \
    || curl -sSL --fail -o "$pkg" "https://www.nuget.org/api/v2/package/$name/$ver" \
    || { echo "下载失败：$name $ver" >&2; return 1; }
  if command -v unzip >/dev/null 2>&1; then
    unzip -q -o "$pkg" 'lib/*' -d "$dst" || { echo "解压失败：$pkg" >&2; return 1; }
  else
    python3 -I -c 'import sys,zipfile; z=zipfile.ZipFile(sys.argv[1]); [z.extract(n, sys.argv[2]) for n in z.namelist() if n.startswith("lib/")]' "$pkg" "$dst" \
      || { echo "解压失败：$pkg" >&2; return 1; }
  fi
  [ -f "$dst/$probe" ] || { echo "包内缺少 $probe：$name" >&2; return 1; }
}

for spec in "${PKGS[@]}"; do
  IFS='|' read -r name ver sub probe <<<"$spec"
  fetch_pkg "$name" "$ver" "$sub" "$probe" || exit 2
done

command -v mcs >/dev/null 2>&1 || { echo "找不到 mcs（mono-devel）" >&2; exit 2; }
mkdir -p "$OUT_DIR"
REFS=""
for f in "$REFS_DIR"/m/lib/net45/*.dll; do REFS="$REFS -r:$f"; done
COMMON="-langversion:7.2 -r:System.Core.dll -r:$FACADE"

fail=0
show() { local t; t=$(printf '%s\n' "$1" | grep -v '^Compilation succeeded' | grep -v '^$' | head -"$2"); [ -n "$t" ] && printf '%s\n' "$t"; return 0; }
# 运行时脚本
mapfile -t SRC < <(find "$P/Scripts" -name '*.cs' | sort)
OUT=$(mcs -target:library $COMMON -nowarn:0414,0649,0169 $REFS -r:"$REFS_DIR/ui/lib/UnityEngine.UI.dll" \
  -out:"$OUT_DIR/game.dll" "${SRC[@]}" 2>&1); st=$?
show "$OUT" 60
[ $st -ne 0 ] && fail=1
echo "[build] Scripts: ${#SRC[@]} 个文件 → $( [ $st -eq 0 ] && echo 通过 || echo 失败 )"

# Editor 脚本
if [ $st -eq 0 ] && [ -d "$P/Editor" ] && ls "$P"/Editor/*.cs >/dev/null 2>&1; then
  OUT=$(mcs -target:library $COMMON $REFS -r:"$REFS_DIR/sdk/lib/UnityEditor.dll" -r:"$REFS_DIR/ui/lib/UnityEngine.UI.dll" \
    -r:"$OUT_DIR/game.dll" -out:"$OUT_DIR/editor.dll" "$P"/Editor/*.cs 2>&1); st=$?
  show "$OUT" 30
  [ $st -ne 0 ] && fail=1
  echo "[build] Editor: $( [ $st -eq 0 ] && echo 通过 || echo 失败 )"
fi
exit $fail
