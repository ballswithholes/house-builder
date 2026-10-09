#!/usr/bin/env bash
# Build and run Lanternvale on a Linux machine without a desktop (cloud sessions, CI):
#   1. pulls a Unity 6 editor image (GameCI) and assembles a Unity project around Assets/Lanternvale,
#   2. builds a Linux player with Lanternvale.EditorTools.LanternvaleBuild.BuildLinuxPlayer,
#   3. runs the player on a virtual display (Xvfb, Mesa software OpenGL) with the in-game autopilot,
#      which plays a scripted tour and saves screenshots.
#
# Usage: Tools/cloud/run-game.sh [all|build|play|start|stop]
#   all    build (if needed) then play the autopilot tour (default)
#   build  build the Linux player only
#   play   run the autopilot tour with an existing build
#   start  start the player on display :99 for interactive driving (see Tools/cloud/x.sh), no autopilot
#   stop   stop the interactive player and Xvfb
#   prepare  only pull the editor image and assemble the Unity project (no license needed)
#
# Requirements
#   * Docker (the daemon is started if needed) and ~8 GB free disk.
#   * A Unity license, provided through the environment (never committed):
#       UNITY_LICENSE   contents of Unity_lic.ulf (Personal/Plus/Pro), or
#       UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD   (Plus/Pro serial activation)
#   * Network access to Docker Hub and Unity's license servers:
#       license.unity3d.com, activation.unity3d.com, core.cloud.unity3d.com, api.unity.com, login.unity.com
#
# Environment overrides: UNITY_IMAGE, LV_WORK (work dir), LV_SHOTS (screenshot dir), LV_CLASS, LV_LEVEL.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
IMAGE="${UNITY_IMAGE:-unityci/editor:ubuntu-6000.0.84f1-base-3}"
WORK="${LV_WORK:-$ROOT/Tools/.cache/cloud}"
PROJECT="$WORK/project"
SHOTS="${LV_SHOTS:-$WORK/shots}"
PLAYER="$PROJECT/Build/Linux/Lanternvale.x86_64"
MODE="${1:-all}"
SUDO=""; [ "$(id -u)" -ne 0 ] && SUDO="sudo"

log() { printf '\033[1;33m[lanternvale]\033[0m %s\n' "$*"; }
die() { printf '\033[1;31m[lanternvale] %s\033[0m\n' "$*" >&2; exit 1; }

ensure_host_deps() {
  local need=()
  command -v xvfb-run >/dev/null || need+=(xvfb)
  command -v xdotool >/dev/null || need+=(xdotool)
  command -v import >/dev/null || need+=(imagemagick)
  dpkg -s libgl1-mesa-dri >/dev/null 2>&1 || need+=(libgl1-mesa-dri libglu1-mesa libgl1)
  dpkg -s libxcursor1 >/dev/null 2>&1 || need+=(libxcursor1 libxrandr2 libxi6 libxinerama1 libxss1)
  if [ ${#need[@]} -gt 0 ]; then
    log "Installing host packages: ${need[*]}"
    $SUDO apt-get update -qq >/dev/null || true
    DEBIAN_FRONTEND=noninteractive $SUDO apt-get install -y -qq "${need[@]}" >/dev/null
  fi
}

ensure_docker() {
  command -v docker >/dev/null || die "Docker is not installed."
  if ! docker info >/dev/null 2>&1; then
    log "Starting the Docker daemon"
    (nohup $SUDO dockerd > "$WORK/dockerd.log" 2>&1 &)
    for _ in $(seq 1 30); do docker info >/dev/null 2>&1 && break; sleep 1; done
    docker info >/dev/null 2>&1 || die "The Docker daemon did not start (see $WORK/dockerd.log)."
  fi
  if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    log "Pulling $IMAGE (about 5 GB, once)"
    docker pull "$IMAGE"
  fi
}

editor_version() { echo "$IMAGE" | sed -E 's/.*ubuntu-([0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+).*/\1/'; }

prepare_project() {
  log "Assembling the Unity project in $PROJECT"
  mkdir -p "$PROJECT/Assets" "$PROJECT/ProjectSettings" "$PROJECT/Packages"
  rm -rf "$PROJECT/Assets/Lanternvale"
  cp -a "$ROOT/Assets/Lanternvale" "$PROJECT/Assets/Lanternvale"
  printf 'm_EditorVersion: %s\n' "$(editor_version)" > "$PROJECT/ProjectSettings/ProjectVersion.txt"
  # The 3D presentation renders with its own shaders on the built-in pipeline: only built-in modules are needed,
  # so the project builds without Unity's package registry.
  log "Render pipeline: built-in (Lanternvale's own stylized shaders)"
  if [ ! -f "$PROJECT/Packages/manifest.json" ] || [ "$(cat "$WORK/.pipeline" 2>/dev/null)" != "builtin3d" ]; then
    echo "builtin3d" > "$WORK/.pipeline"
    rm -rf "$PROJECT/Library/PackageCache" "$PROJECT/Packages/packages-lock.json"
    cat > "$PROJECT/Packages/manifest.json" <<JSON
{
  "dependencies": {
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.screencapture": "1.0.0",
    "com.unity.modules.animation": "1.0.0"
  }
}
JSON
  fi
}

license_args() {
  mkdir -p "$WORK/license"
  if [ -n "${UNITY_LICENSE:-}" ]; then
    printf '%s' "$UNITY_LICENSE" > "$WORK/license/Unity_lic.ulf"
    echo "-v $WORK/license/Unity_lic.ulf:/root/.local/share/unity3d/Unity/Unity_lic.ulf:ro"
  elif [ -n "${UNITY_SERIAL:-}" ] && [ -n "${UNITY_EMAIL:-}" ] && [ -n "${UNITY_PASSWORD:-}" ]; then
    echo ""
  else
    die "No Unity license. Set UNITY_LICENSE (contents of Unity_lic.ulf) or UNITY_SERIAL/UNITY_EMAIL/UNITY_PASSWORD in the environment settings."
  fi
}

run_editor() { # args...
  local lic; lic="$(license_args)"
  local creds=()
  if [ -z "${UNITY_LICENSE:-}" ]; then creds=(-serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD"); fi
  # shellcheck disable=SC2086
  docker run --rm $lic -v "$PROJECT:/project" -w /project "$IMAGE" \
    unity-editor -batchmode -nographics -quit -projectPath /project "${creds[@]}" "$@"
}

build() {
  ensure_docker
  prepare_project
  log "Building the Linux player (first import of the art takes a while)"
  set +e
  run_editor -executeMethod Lanternvale.EditorTools.LanternvaleBuild.BuildLinuxPlayer \
    -buildPath /project/Build/Linux/Lanternvale.x86_64 -logFile /project/build.log
  local code=$?
  set -e
  if [ $code -ne 0 ] || [ ! -f "$PLAYER" ]; then
    grep -E "error|Error|license|License|\[Lanternvale\]" "$PROJECT/build.log" 2>/dev/null | tail -40 || true
    die "Build failed (exit $code). Full log: $PROJECT/build.log"
  fi
  if [ -n "${UNITY_SERIAL:-}" ]; then run_editor -returnlicense -logFile /project/return-license.log || true; fi
  log "Built $PLAYER"
}

player_args() {
  echo -force-glcore -screen-fullscreen 0 -screen-width 1600 -screen-height 900
}

play() {
  [ -f "$PLAYER" ] || die "No player build yet: run '$0 build' first."
  mkdir -p "$SHOTS"
  rm -f "$SHOTS"/*.png
  log "Running the autopilot tour on a virtual display; screenshots → $SHOTS"
  LIBGL_ALWAYS_SOFTWARE=1 timeout 900 xvfb-run -a -s "-screen 0 1600x900x24" "$PLAYER" $(player_args) \
    -lv-autopilot -lv-quit -lv-shots "$SHOTS" -lv-class "${LV_CLASS:-Paladin}" -lv-level "${LV_LEVEL:-12}" \
    -logFile "$WORK/player.log" || true
  ls -1 "$SHOTS"/*.png 2>/dev/null || { tail -40 "$WORK/player.log"; die "No screenshots were written (player log: $WORK/player.log)."; }
}

start_interactive() {
  [ -f "$PLAYER" ] || die "No player build yet: run '$0 build' first."
  stop_interactive >/dev/null 2>&1 || true
  log "Starting Xvfb :99 and the player (drive it with Tools/cloud/x.sh)"
  (nohup Xvfb :99 -screen 0 1600x900x24 > "$WORK/xvfb.log" 2>&1 &)
  sleep 2
  (DISPLAY=:99 LIBGL_ALWAYS_SOFTWARE=1 nohup "$PLAYER" $(player_args) -logFile "$WORK/player-interactive.log" > /dev/null 2>&1 &)
  sleep 8
  log "Running. Example: Tools/cloud/x.sh shot menu.png; Tools/cloud/x.sh click 800 450"
}

stop_interactive() {
  pkill -f "Lanternvale.x86_64" || true
  pkill -f "Xvfb :99" || true
}

mkdir -p "$WORK"
case "$MODE" in
  all)   ensure_host_deps; [ -f "$PLAYER" ] || build; play ;;
  build) build ;;
  prepare) ensure_docker; prepare_project ;;
  play)  ensure_host_deps; play ;;
  start) ensure_host_deps; start_interactive ;;
  stop)  stop_interactive ;;
  *) die "usage: $0 [all|build|play|start|stop]" ;;
esac
