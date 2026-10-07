#!/usr/bin/env bash
# Builds (quietly, when sources changed) and runs the offline sound-effect preview. See README.md.
#   Tools/sfxpreview/sfxpreview.sh check
#   Tools/sfxpreview/sfxpreview.sh render /tmp/sfx hit_blade,mat_plate
#   Tools/sfxpreview/sfxpreview.sh sheet /tmp/materials.png materials
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
log="$here/bin/build.log"
mkdir -p "$here/bin"
if ! dotnet build "$here/SfxPreview.csproj" -nologo -v q -c Release -clp:ErrorsOnly > "$log" 2>&1; then
  grep -E 'error' "$log" | sort -u | head -40 >&2
  echo "sfxpreview: build failed (full log: $log)" >&2
  exit 1
fi
exec dotnet "$here/bin/Release/net8.0/SfxPreview.dll" "$@"
