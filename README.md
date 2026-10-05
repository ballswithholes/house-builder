# House builder

A 3D make-your-own-house sandbox for a six-year-old. One self-contained HTML
file — no build step, no dependencies, and it makes no network requests at all
once the page has loaded. Works on an iPad, an iPhone and a desktop browser.

This repository is only the published page; the source lives elsewhere.

## Glim

`glim/index.html` is **Glim**, a one-tap flying game built for iPhone: tap to
keep a little lantern spirit aloft through a cut-paper world of thorny ink
spires. Like the house builder it is one self-contained HTML file with no
network requests; all art is drawn in code and all sound is synthesized.

- Five worlds that change as you fly (Dawn Hills, Canopy, Dusk Canyon, Night
  Woods, Aurora Peaks), with moving and "breathing" spires.
- PERFECT passes and near-miss GRAZEs build a ×5 chain and fill **SURGE**,
  which lets you smash straight through spires.
- Shield, Magnet, Slow-mo and Shrink power-ups; motes and gems to collect.
- A shop of characters and trails, 12 repeating missions, medals, a daily
  challenge, and a Breeze mode for little pilots.
- Adaptive music, haptics on iPhone (iOS 18), and it pauses itself when you
  leave the app.

### Characters

Each character flies a little differently and has one ability that works on its
own (the only new input is Mintleaf's *hold*). Every step up the shop is easier
to fly than the one before; the level layout of a seed (and the daily) is the
same whichever character you pick.

| Character | Price | Feel | Ability |
|---|---|---|---|
| Glim | free | the baseline: no tricks, all skill | — |
| Mintleaf | 150 | a bit floatier and slower, falls more gently, smaller hitbox | **Drift**: keep your finger on the screen to glide down slowly |
| Koi | 300 | soft and slow, falls gently, smaller hitbox | **Splash Back**: bounces off the ground instead of crashing; ready again after 4 gates |
| Moth | 600 | long hang at the top of each hop, gentle fall, smaller hitbox | **Moon Ward**: starts with a shield that grows back 7 gates after it breaks |
| Comet | 1000 | the quickest after Glim, a little lighter, small hitbox | **Phase Tail**: slips through one spire, ready again after 5 gates; SURGE fills 1.5× faster |
| Jelly | 2000, or free with the Prism medal | the floatiest and slowest, smallest hitbox | **Tidal Pull**: gently drawn toward the next gap, and bounces off the ground; ready again after 4 gates |

Add `?debug` to the URL for an FPS overlay and hitboxes.
