# Sound effect credits and recorded overrides

Every sound in Lanternvale is synthesized at startup (`Game/Audio/SfxSynth.cs`); the repository ships **no recorded
samples**. The game can nevertheless play recorded files instead of a synthesized sound: any `.wav` or `.ogg` placed in
`Assets/Lanternvale/Resources/Audio/Sfx/<id>/` replaces every synthesized variant of `<id>` (several files = several
variants, played round-robin in name order; see `PresentationAPI.md` §6). When the folder is absent nothing changes.

## Rules for anything placed there

* **CC0 or owned only.** Public-domain (CC0) recordings, or audio you made yourself or hold full rights to. No CC-BY-NC,
  no "free for personal use", no royalty-free bundles that forbid redistributing the raw files, no re-uploads whose
  licence cannot be checked against the original source.
* **One line per file** in the table below: the path, where it came from (URL or "own recording"), the author and the
  licence. A file without a line here must not be committed.
* **Match the mix.** Mono, 44.1 kHz, short (most combat sounds are under 0.6 s), no silence at the start, peaks
  normalised to roughly the synthesized clip's peak (0.5–0.8 of full scale), so the layered hits stay balanced.
* Keep the cozy direction: physical and real, never gory.

## Files

| file | source | author | licence |
|---|---|---|---|
| — | — | — | — |
