#!/usr/bin/env bash
# Build and launch Lanternvale on YOUR computer (macOS or Linux) with your Unity Hub install and license.
#
#   Lanternvale/Tools/local/play.sh            build (first time ~5–15 min) and launch the game in a window
#   Lanternvale/Tools/local/play.sh --editor   open the project in the Unity editor instead (then press Play)
#   Lanternvale/Tools/local/play.sh --tour     launch with the autopilot tour (screenshots in Tools/.cache/local/shots)
#
# It finds the newest Unity 6 (or 2022.3) editor installed by Unity Hub, creates a project from the
# "Universal 2D" template next to the repo (Tools/.cache/local/LanternvaleProject), copies Assets/Lanternvale
# into it, builds a player for this computer and starts it. Override the editor with UNITY_EDITOR=/path/to/Unity.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$ROOT/Tools/.cache/local"
PROJECT="${LV_PROJECT:-$WORK/LanternvaleProject}"
MODE="${1:-play}"
log() { printf '\033[1;33m[lanternvale]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[lanternvale] %s\033[0m\n' "$*" >&2; exit 1; }

find_editor() {
  if [ -n "${UNITY_EDITOR:-}" ]; then echo "$UNITY_EDITOR"; return; fi
  local candidates=()
  case "$(uname -s)" in
    Darwin)
      for d in /Applications/Unity/Hub/Editor/*/ "$HOME/Applications/Unity/Hub/Editor"/*/; do
        [ -x "$d/Unity.app/Contents/MacOS/Unity" ] && candidates+=("$d/Unity.app/Contents/MacOS/Unity")
      done ;;
    *)
      for d in "$HOME/Unity/Hub/Editor"/*/ /opt/unity/Hub/Editor/*/ /opt/Unity/Hub/Editor/*/; do
        [ -x "$d/Editor/Unity" ] && candidates+=("$d/Editor/Unity")
      done ;;
  esac
  [ ${#candidates[@]} -gt 0 ] || return 1
  # prefer Unity 6 (6000.*), then 2022.3, newest first
  printf '%s\n' "${candidates[@]}" | awk '{v=$0; p=0; if (v ~ /\/6000\./) p=2; else if (v ~ /\/2022\.3\./) p=1; print p "\t" v}' \
    | sort -t$'\t' -k1,1nr -k2,2r | head -1 | cut -f2
}

template_for() { # editor binary -> path of the bundled Universal 2D template .tgz, if any
  local root
  case "$(uname -s)" in
    Darwin) root="$(cd "$(dirname "$1")/../.." && pwd)" ;;   # .../Unity.app (templates live in Contents/Resources)
    *) root="$(dirname "$1")" ;;                              # .../Editor (templates live in Data/Resources)
  esac
  find "$root" -maxdepth 6 -path "*ProjectTemplates*" -name "*universal-2d*.tgz" 2>/dev/null | sort | tail -1 || true
}

EDITOR_BIN="$(find_editor)" || die "No Unity editor found. Install Unity 6 with Unity Hub (or set UNITY_EDITOR=/path/to/Unity)."
log "Unity editor: $EDITOR_BIN"
mkdir -p "$WORK"

if [ ! -f "$PROJECT/ProjectSettings/ProjectVersion.txt" ]; then
  tpl="$(template_for "$EDITOR_BIN")"
  if [ -n "$tpl" ]; then
    log "Creating the project from the Universal 2D template (one time)"
    "$EDITOR_BIN" -batchmode -quit -createProject "$PROJECT" -cloneFromTemplate "$tpl" -logFile "$WORK/create.log" \
      || die "Project creation failed, see $WORK/create.log"
  else
    log "Universal 2D template not found; creating a plain project (the build step configures URP if it is installed)"
    "$EDITOR_BIN" -batchmode -quit -createProject "$PROJECT" -logFile "$WORK/create.log" \
      || die "Project creation failed, see $WORK/create.log"
  fi
fi

log "Copying Assets/Lanternvale into the project"
rm -rf "$PROJECT/Assets/Lanternvale"
cp -R "$ROOT/Assets/Lanternvale" "$PROJECT/Assets/Lanternvale"

if [ "$MODE" = "--editor" ]; then
  log "Opening the Unity editor — choose Lanternvale > Create Game Scene, then press Play"
  "$EDITOR_BIN" -projectPath "$PROJECT" -executeMethod Lanternvale.EditorTools.LanternvaleMenu.OpenGameScene &
  exit 0
fi

case "$(uname -s)" in
  Darwin) METHOD=BuildMacPlayer; PLAYER="$PROJECT/Build/Mac/Lanternvale.app" ;;
  *)      METHOD=BuildLinuxPlayer; PLAYER="$PROJECT/Build/Linux/Lanternvale.x86_64" ;;
esac
log "Building the game (the first build imports all the art and takes a while)"
"$EDITOR_BIN" -batchmode -quit -projectPath "$PROJECT" \
  -executeMethod "Lanternvale.EditorTools.LanternvaleBuild.$METHOD" -buildPath "$PLAYER" -logFile "$WORK/build.log" \
  || { grep -E "error|\[Lanternvale\]" "$WORK/build.log" | tail -30; die "Build failed, full log: $WORK/build.log"; }

ARGS=()
if [ "$MODE" = "--tour" ]; then ARGS=(-lv-autopilot -lv-shots "$WORK/shots"); log "Autopilot tour; screenshots → $WORK/shots"; fi
log "Launching Lanternvale"
case "$(uname -s)" in
  Darwin) open -n "$PLAYER" --args ${ARGS[@]+"${ARGS[@]}"} ;;
  *)      "$PLAYER" ${ARGS[@]+"${ARGS[@]}"} -logFile "$WORK/player.log" & ;;
esac
