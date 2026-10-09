# sfxpreview: offline sound-effect and music preview and checks

A .NET 8 console tool for Lanternvale's procedural audio. It compiles the game's synthesis code by relative path:
`Game/Audio/SfxSynth.cs`, `SynthDsp.cs`, `SfxRecipes.*.cs` and `MusicEngine.cs`. That code is built against preview3d's
managed `UnityEngine` stand-in (`../preview3d/Stubs/UnityEngine.cs`) and a ten-line `AudioClip` stub
(`Stubs/AudioStub.cs`). If preview3d's stand-in ever gains an `AudioClip`, delete the stub.

The tool never edits game sources. Synthesis files may use only `System`, `UnityEngine.Mathf` and `AudioClip`, and
only `SynthBuffer.ToClip` touches `AudioClip`. Code that depends on Unity stays out of the tool: `Sfx.cs`,
`GameAudio.cs`, `Music.cs` and any `CombatSfx` facade.

## Run

```
Tools/sfxpreview/sfxpreview.sh check                                  # the audio test suite (exit code 0 = pass)
Tools/sfxpreview/sfxpreview.sh check --verbose --budget-ms 4000       # print every assertion
Tools/sfxpreview/sfxpreview.sh list                                   # ids, variant count K, tier, peak, length
Tools/sfxpreview/sfxpreview.sh stats materials                        # metrics of every variant
Tools/sfxpreview/sfxpreview.sh render /tmp/sfx hit_blade,mat_plate --variants --spec   # WAVs, PNGs, metrics.tsv
Tools/sfxpreview/sfxpreview.sh sheet /tmp/sheet.png all               # one spectrogram sheet per group: sheet_<group>.png
Tools/sfxpreview/sfxpreview.sh sheet /tmp/mat.png materials --variants  # rows = ids, columns = variants
Tools/sfxpreview/sfxpreview.sh music all /tmp/music --seconds 30      # renders every mood through the real MusicEngine
```

`sfxpreview.sh` builds the tool in Release when its sources change (the log is in `bin/build.log`) and runs it.
Selections can be `all`, a group name (`weapons materials generic defence swings ranged spells impacts deaths
footsteps hooks original`), or a comma list of ids. The `--threads N` option sets `SfxSynth.MaxThreads`.

### Sheets

Each cell is a log-frequency STFT computed by the tool (`Spectrogram.cs`): 50 Hz at the bottom, 16 kHz at the top.
Faint guide lines mark 200 Hz, 1 kHz and 5 kHz, which are the band edges of the spectral guard. The time window is
fixed per group (for example 0.6 s for materials), so clip lengths compare across cells. The colour covers 90 dB
below the cell's peak. `/usr/bin/ffmpeg` labels each cell with the key, the share of energy below 200 Hz, the share
in 1–5 kHz and the −20 dB decay time, then tiles the cells. ffmpeg's own `showspectrumpic fscale=log` is not used,
because its log axis puts 200 Hz near the top. Look at the sheets with the Read tool: materials and weapons should
show distinct modal lines, bands and decay shapes.

## What `check` asserts

| check | guards against |
|---|---|
| no synthesis errors; cold `GeneratePcm` ≤ `--budget-ms` (4000) and tier 0 ≤ `--tier0-budget-ms` (2000) | boot-time regressions (the game synthesizes on a worker thread at boot) |
| bit-identical output: parallel vs single thread vs repeated run | non-determinism, shared state between threads |
| every recipe publishes `id`, `id#1` … `id#K` (nothing more); the plain id is the same array as `#1`; K ≥ the §4 minimum (4 for hit_/mat_/swing/footstep ids, 2 otherwise); variants differ | the WP-A/WP-B key contract (`Sfx` groups variants by base id) |
| variant variety: outside the jingles (`hooks`) and the byte-pinned originals, no two variants of an id have a maximum normalised cross-correlation of 0.95 or more (first 0.74 s, every lag) | a round robin of one waveform with different noise (impact_arcane once measured 1.000, shout_horn 0.999) |
| every id of `Docs/Expansion.md` §4 and all 24 original ids exist | missing ids |
| the original UI/reward clips (`ui_*`, `heal`, `buff`, `debuff`, `death`, `level_up`, `quest`, `coin`, `chest_open`, `door`, `cast_start`) are byte-identical to the pre-expansion recipes (FNV hashes in `Spec.cs`) | accidental changes to the cosy UI sounds |
| every layered clip: finite, peak ≤ 0.9 and within 5 % of the declared peak, \|DC\| < 0.01, last 10 ms below −40 dBFS, length within the declared bounds | clipping, NaN, clicks, cut tails |
| spectral guard on `hit_*` and `mat_*`: ≤ 50 % of the energy below 200 Hz, ≥ 25 % in 1–5 kHz | the old "kick drum" hits (hit_physical was 98.7 % below 200 Hz) |
| distinctness per group: each variant is nearest its own id's centroid (leave-one-out; 22 third-octave levels plus the decay time), and the closest pair of ids differs by ≥ 3 dB | look-alike materials and weapons |
| every id literal in `Sfx.Play*`, `Ui.Sfx?.Invoke`, `Sfx.Has` and `Sfx.Clip` calls in `Assets/Lanternvale/Scripts` exists (or is an `Sfx` alias) | typos at call sites |
| music: `MoodLibrary.Normalize` cases (explicit `music_<mood>` keys beat map-id keywords), every mood maps to itself, every mood has its own tempo/metre/key/mode, 16 s offline renders are finite and below full scale with rms in (0.01, 0.4), every map's music key in the data plays the intended mood | silent or clipping music, raid maps playing the shrine mood |

The original clips keep their old small DC offset or tail (`level_up`, `quest`, `chest_open`). They are printed as
`note` lines, not failures, because byte-identity is the requirement for them.

## Timing (this container, 4 cores, .NET 8)

All three tiers synthesize 102 ids as 266 clips, about 167 s of audio and 28 MB of float in total. Cold
(including JIT): about 1.4–2.4 s wall clock, with tier 0 at about 0.5 s. Single-threaded: about 2.3 s. Unity's Mono
is expected to be 1–3× slower, and the work runs on the `Sfx` worker thread.
