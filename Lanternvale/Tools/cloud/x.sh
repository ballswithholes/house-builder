#!/usr/bin/env bash
# Drive the player started by `Tools/cloud/run-game.sh start` (virtual display :99).
#   x.sh shot <file.png>          screenshot of the whole screen
#   x.sh click <x> <y> [button]   click at screen pixels (1600x900; button 1 = left, 3 = right)
#   x.sh move <x> <y>             move the mouse (hover tooltips)
#   x.sh key <keysym...>          press keys, e.g. x.sh key space, x.sh key Escape, x.sh key 1
#   x.sh type <text>              type text (name field)
#   x.sh wait <seconds>
set -euo pipefail
export DISPLAY="${DISPLAY_OVERRIDE:-:99}"
cmd="${1:-}"; shift || true
case "$cmd" in
  shot)  import -window root "${1:-shot.png}" && echo "${1:-shot.png}" ;;
  click) xdotool mousemove "$1" "$2" sleep 0.1 click "${3:-1}" ;;
  move)  xdotool mousemove "$1" "$2" ;;
  key)   for k in "$@"; do xdotool key "$k"; sleep 0.15; done ;;
  type)  xdotool type --delay 60 "$*" ;;
  wait)  sleep "${1:-1}" ;;
  *) echo "usage: x.sh shot|click|move|key|type|wait ..."; exit 2 ;;
esac
