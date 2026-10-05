#!/usr/bin/env bash
# Builds (quietly, when sources changed) and runs the offline 3D preview. See README.md.
#   Tools/preview3d/preview3d.sh map lanternvale out.png --hour 22.5
#   Tools/preview3d/preview3d.sh props props.png all
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
log="$here/bin/build.log"
mkdir -p "$here/bin"
if ! dotnet build "$here/Preview3D.csproj" -nologo -v q -c Release -clp:ErrorsOnly > "$log" 2>&1; then
  grep -E 'error' "$log" | sort -u | head -40 >&2
  echo "preview3d: build failed (full log: $log)" >&2
  exit 1
fi
exec dotnet "$here/bin/Release/net8.0/Preview3D.dll" "$@"
