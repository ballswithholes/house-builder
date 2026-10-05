---
name: run-lanternvale
description: Build and launch the Lanternvale Unity game headlessly in a Linux container (Docker + Unity 6 editor image + Xvfb), play an automated tour with screenshots, or drive the running game with clicks/keys. Use when asked to run, launch, play, screenshot or visually check Lanternvale.
---

# Run Lanternvale (Unity) in a Linux container

Lanternvale is a Unity 6 project (`Lanternvale/Assets/Lanternvale`). There is no Unity install in the
container; `Lanternvale/Tools/cloud/run-game.sh` pulls the GameCI editor image, assembles a project around the
assets, builds a Linux player and runs it on a virtual display.

## Prerequisites (the user configures these in the environment settings — never ask for secrets in chat)

1. **Unity license** in the environment:
   * `UNITY_LICENSE` = contents of `Unity_lic.ulf` (from a machine where Unity Hub is signed in:
     Windows `C:\ProgramData\Unity\Unity_lic.ulf`, macOS `/Library/Application Support/Unity/Unity_lic.ulf`,
     Linux `~/.local/share/unity3d/Unity/Unity_lic.ulf`), or
   * `UNITY_SERIAL` + `UNITY_EMAIL` + `UNITY_PASSWORD` (Plus/Pro).
2. **Network access** (Custom network policy, keep the default package managers) to Docker Hub and Unity's license
   servers: `license.unity3d.com`, `activation.unity3d.com`, `core.cloud.unity3d.com`, `api.unity.com`,
   `login.unity.com`. (The game renders with its own shaders on the built-in pipeline, so Unity's package registry is
   not needed.)

Check quickly: `curl -s -o /dev/null -w '%{http_code}\n' https://license.unity3d.com/` (000 = blocked) and
`[ -n "$UNITY_LICENSE$UNITY_SERIAL" ] && echo license set`.

## Run the automated tour (build + play + screenshots)

```bash
cd /home/user/house-builder/Lanternvale
Tools/cloud/run-game.sh            # first run: ~5 GB image pull + first asset import (10–20 min)
ls Tools/.cache/cloud/shots/       # 01_main_menu.png, 02_opening_dialogue.png, … combat, night, whisperwood, shrine
```

Then **look at every screenshot** (Read the PNGs). A black or empty frame means the player did not render —
check `Tools/.cache/cloud/player.log`. Build problems: `Tools/.cache/cloud/project/build.log`.
Options: `LV_CLASS=Mage LV_LEVEL=30 Tools/cloud/run-game.sh play` (re-uses the build).

The tour is the in-game autopilot (`Scripts/Game/Debug/Autopilot.cs`, enabled by `-lv-autopilot`): main menu →
new game → opening dialogue → the 3D village by day → Character/Bags/Spellbook/Talents/Journal → training-dummy fight
using real abilities → village at night → Whisperwood → Old Shrine.

## Drive it interactively

```bash
Tools/cloud/run-game.sh start            # Xvfb :99 + player (1600x900), no autopilot
Tools/cloud/x.sh shot /tmp/s1.png        # screenshot → Read it
Tools/cloud/x.sh click 800 450           # left click (x y in 1600x900 pixels); add 3 for right click
Tools/cloud/x.sh key space               # keys: space, Return, Escape, 1..9, c, i, n, p, j, k, m, l, Tab, F5, F9
Tools/cloud/x.sh type Aria               # text into a focused field
Tools/cloud/run-game.sh stop
```

Take a screenshot after every action and look at it before the next one.

## Without a license

`Tools/cloud/run-game.sh prepare` pulls the image and assembles the project; launching the editor then stops
with "No valid Unity Editor license found". Tell the user which prerequisite is missing. The rules and
content can still be exercised headlessly with `Lanternvale/Tools/check.sh core` (full playthroughs per class).
