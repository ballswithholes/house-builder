#!/bin/bash
# tools/voice-build/build.sh — the offline voice pipeline of AUDIO-V4 §B7.2, end to end (developer machine, no network needed
# once the models are on disk). Outputs (committed): js/voice-data.js, voice/*.js, voice/mp3/*.js, voice/manifest.js, voice/LICENSES.md,
# voice/QA.md (the §B7.2 QA report: per-lingua results, the dropped clips, the clips flagged for a human listen)
#
#   tools/voice-build/build.sh <python venv> <models dir>[,<models dir 2>] <work dir> [--allow-nc] [--no-asr]
#
# The venv is the one of design-v4/audio/tts-lab (Python 3.11: piper-tts 1.8.0, sherpa-onnx 1.13.8, pyopenjtalk-plus, pypinyin,
# scipy); the models are the sherpa-onnx `tts-models` mirrors listed in design-v4/audio/tts-lab/dl.sh + dl2.sh, plus
# sherpa-onnx-whisper-base from the `asr-models` release for the QA round trip. Steps:
#   1. gen-voice-data.js   design-v4/audio/voice-lines.js → js/voice-data.js
#   2. export-lines.js     js/voice-data.js → <work>/lines.json (pack, voice, cue, variant, text, route, delivery, authored IPA)
#   3. synth.py            render + post chain → <work>/wav/<pack>/<cue>#<variant>.wav, synth.json   (resumable)
#   4. qa.py               length / ASR / clipping / licence gates → qa.json, qa.md (failed clips are dropped; a lingua that
#                          fails QA ships no pack and the game walks its fallback chain)
#   5. pack.py             Opus 16 kbps (+ MP3 32 kbps fallback) → voice/
set -e
VENV="$1"; MODELS="$2"; WORK="$3"; shift 3 || true
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
[ -x "$VENV/bin/python" ] || { echo "usage: build.sh <venv> <models[,models2]> <work dir> [--allow-nc] [--no-asr]"; exit 2; }
QA_FLAGS=""
for a in "$@"; do QA_FLAGS="$QA_FLAGS $a"; done
mkdir -p "$WORK"
node "$ROOT/tools/voice-build/gen-voice-data.js"
node "$ROOT/tools/voice-build/export-lines.js" "$WORK/lines.json"
"$VENV/bin/python" -I "$ROOT/tools/voice-build/synth.py" "$WORK/lines.json" --models "$MODELS" --out "$WORK/wav"
"$VENV/bin/python" -I "$ROOT/tools/voice-build/qa.py" "$WORK/wav" --models "$MODELS" $QA_FLAGS
"$VENV/bin/python" -I "$ROOT/tools/voice-build/pack.py" "$WORK/wav" "$ROOT"
cp "$WORK/wav/qa.md" "$ROOT/voice/QA.md"
node "$ROOT/tests/voice-lines.js"
