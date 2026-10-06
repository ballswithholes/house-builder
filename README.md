# House builder

A 3D make-your-own-house sandbox for a six-year-old. One self-contained HTML
file — no build step, no dependencies, and it makes no network requests at all
once the page has loaded. Works on an iPad, an iPhone and a desktop browser.

This repository is only the published page; the source lives elsewhere.

## Glim

`glim/index.html` is **Glim**, a one-tap flying game built for iPhone: tap to
keep a paper lantern aloft through a cut-paper world of thorny ink spires.
Like the house builder it is one self-contained HTML file with no network
requests; all art is drawn in code and all sound is synthesized.

- Five worlds that change as you fly (Dawn Hills, Canopy, Dusk Canyon, Night
  Woods, Aurora Peaks), with moving and "breathing" spires.
- PERFECT passes and near-miss GRAZEs build a ×5 chain and fill **SURGE**,
  which lets you smash straight through spires.
- Shield, Magnet, Slow-mo and Shrink power-ups; motes and gems to collect.
- A shop of 24 flyers and of trails, 12 repeating missions, medals, a daily
  challenge, and a gentler Breeze mode.
- Adaptive music, haptics on iPhone (iOS 18), and it pauses itself when you
  leave the app.

### Flyers

There are 24 flyers in six tiers, ordered so that each one is easier to fly
than the one before (measured with human-like bots: the top flyer reaches about
7× as many gates as Glim). A flyer's physics and traits apply only to how it
flies; the level layout of a seed (and the daily) is the same for every flyer.
Traits work on their own, so the game stays one-tap; the only extra input is
*hold* to glide (Drift).

Each tier gives the light a stronger body: folded paper (Fold), birds of prey
(Talon), night creatures (Shade), myth (Myth), sky bodies (Astral) and crowned
light (Zenith). Every flyer is cut-paper art drawn in code, with its own wing
beat and moving parts. None of them has a face: each shows feeling through its
light alone.

| # | Flyer | Tier | Unlock (motes) | Traits |
|---|---|---|---|---|
| 1 | Glim | I · Fold | free | none: the baseline, all skill |
| 2 | Dart | I · Fold | 750 | Head Start (SURGE starts 40 % charged) |
| 3 | Kite | I · Fold | 900 | Rebound 5 |
| 4 | Crane | I · Fold | 1,100 | Drift (hold to glide) |
| 5 | Swift | II · Talon | 1,400 | Aegis |
| 6 | Kestrel | II · Talon | 1,600 | Stormheart (SURGE starts 45 % charged, fills 1.4× faster, lasts 1.4× longer; wider PERFECT) |
| 7 | Peregrine | II · Talon | 1,850 | Rekindle |
| 8 | Eagle | II · Talon | 2,050 | Rebound 4 |
| 9 | Raven | III · Shade | 2,200 | Phase 9 |
| 10 | Vesper | III · Shade | 2,400 | Drift, Rekindle |
| 11 | Manta | III · Shade | 2,600 | Aegis, Rebound 4 |
| 12 | Luna | III · Shade | 2,800 | Ward 7 |
| 13 | Phoenix | IV · Myth | 3,100 | Rekindle, Aegis |
| 14 | Wyvern | IV · Myth | 3,300 | Ward 5, Rebound 3 |
| 15 | Tempest | IV · Myth | 3,600 | Phase 5, Surge Gain 1.5× |
| 16 | Dragon Koi | IV · Myth | 3,900 | Pull (light), Aegis |
| 17 | Cygnus | V · Astral | 4,300 | Phase 8, Rebound 4 |
| 18 | Eclipse | V · Astral | 4,700 | Ward 8, Rekindle |
| 19 | Comet | V · Astral | 5,100 | Phase 5, Rekindle, Surge Gain 1.5× |
| 20 | Nebula | V · Astral | 5,600 | Pull, Rebound 4, Aegis |
| 21 | Halcyon | VI · Zenith | 6,800 | Drift, Pull, Ward 5 |
| 22 | Seraph | VI · Zenith | 8,000 | Pull (strong), Rebound 4, Rekindle |
| 23 | Starwyrm | VI · Zenith | 8,600 | Phase 4, Rebound 4, Rekindle |
| 24 | Daystar | VI · Zenith | 9,400 | Pull, Rekindle, Lodestone |

What the traits do (a number is the gates it takes to recharge):

- **Drift**: while you hold, it falls no faster than a slow glide (never adds lift).
- **Rebound**: kicks off the ground instead of crashing.
- **Ward**: starts with a shield that grows back after it breaks.
- **Phase**: passes through one spire or blade.
- **Pull**: drawn toward the next gap; you still fly it.
- **Aegis**: starts every run with a shield.
- **Rekindle**: once per run, relights instead of crashing and clears the way ahead.
- **Head Start / Surge Gain / Stormheart**: a faster, longer SURGE.
- **Lodestone**: draws in nearby motes and gems.

Tier I is always open. Every other tier is sealed until you open it by **any
one** of its paths, in any mode (normal, Breeze or the daily); opening a tier
also opens every tier below it, once open it stays open, and a flyer you own
always flies.

| Tier | Best gates in one run | or gates flown in total | or |
|---|---|---|---|
| II · Talon | 25 | 1,000 | |
| III · Shade | 50 | 3,000 | a 3-day daily streak |
| IV · Myth | 60 | 6,000 | 24 missions done |
| V · Astral | 100 | 10,000 | a 7-day daily streak |
| VI · Zenith | 180 | 18,000 | |

Saves from v1 carry over: Mintleaf becomes Crane, Koi becomes Dragon Koi, Moth
becomes Luna, Comet stays Comet and Jelly becomes Nebula.

Add `?debug` to the URL for an FPS overlay and hitboxes.
