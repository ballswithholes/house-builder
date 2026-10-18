# 三国志-霸王的世界 · 第四版战斗设计（COMBAT-V4）

Player requests 4 and 5 of v4: the army clash a bit slower plus critical strikes for army attacks and duels (§1–§3), and the duel's hold-back block, new medium attack, new controls, duel criticals and an 8-hit combo system with parry breaks (§4–§14).

Read `DESIGN-V2.md` §1 (common rules), `DESIGN-V3.md` §3 (parry) and `CONTRACT.md` first. **`INTEGRATION.md`** wins over this file for everything that crosses a system boundary (the battle multiplier caps of `SG.Mods`, the all-in duel budget for gear and titles, the `DUEL_ITEM_EFFECTS` whitelist below, save and migration, the test matrix). Otherwise this file wins for the systems it covers. Platform order: browser first (this repo), then the Unity port (`../sanguozhi2-unity`, §16.4). All visuals assume the v4 art direction from the art pilot (Lanternvale: stylized low-poly, cozy Ghibli-warm, flat-shaded props, ink outlines, soft painted light with warm lantern pools). Every effect below is specified in that style: brush-stroke trails, ink splashes, vermillion seal stamps, warm gold rather than neon.

Status: design spec only. No game code was changed. Supporting files are in `design-v4/combat/`:

| File | What it is |
|---|---|
| `combat/hand-combos.json` | The 65 hand-made 8-hit strings, with finisher names and sources (the data of §11, ready to paste into `js/combo-data.js`) |
| `combat/gen-combos-proto.js` | Reference prototype of the generator in §10 (Node, read-only on the game data). Prints the assignment stats quoted in this file |
| `combat/crit-estimate.js` | Throw-away estimate of army crits (monkey-patches `BattleModel.prototype.attack` in memory, 300 battles; base formula only: war, level, formation, without the morale, gear and title terms). Source of the numbers in §2.7 |

---

## 0. Key numbers at a glance

| Topic | v3 now | v4 |
|---|---|---|
| Clash length, normal (real seconds incl. fades) | 3.96 s | **4.86 s** (+23%); crit adds +0.43 s |
| Clash length, 快 mode | 2.18 s (×2) | **2.18 s** (×2.5, unchanged length) |
| Clash, enemy turn / 委任 | ×1.5 → 2.77 s / ×1.8 → 2.38 s | ×1.7 → **3.02 s** / ×2.0 → **2.63 s**; at speed ≥ 1.5 the music plays `clash-short` with its accent on `melee1` (contact + 1.62 s ÷ speed) instead of the full stinger (§1.4) |
| Army crit chance | — | 2% + 0.15%/武力 above 50 + 0.4%/**老练 level** (`vetLevel`, non-battle exp at half) above 1 + formation (−2 … +4%) + gear/title (≤ +6%), cap 25%. War 100, L10, 锋矢 ≈ 17%. All-on measured: 7.0–8.2 % of attacks, attacker win −0.2 … −1.5 pp (three army sets, INTEGRATION §9.2) |
| Army crit damage | — | ×1.5 attack damage, target morale −5 more; counter-attacks never crit |
| Army crit balance (estimate, 300 AI battles) | — | crit rate 6.6% of attacks, attacker win +1.4 pp, avg days −0.3, avg attack damage +4% |
| Duel attack buttons | J light, K heavy, L guard | **J 轻 · K 中 · L 重** (hold to charge); K+L 跃劈 overhead |
| Duel guard | hold L / ↓ | **hold back** (away from the opponent) with proximity guard; ↓+back = crouch guard; optional 防 button (off by default) |
| Lows / overheads | none | ↓J, ↓K sweep = low; air attacks and K+L 跃劈 = overhead (0.32 s startup, reactable) |
| Chains | L-L-L (3 hits) for everyone | each general's **unique 8-step string** of L/M/H (790 legal strings, 499 used, 291 spare) |
| Full string damage (equal war 76, all 8 hits land) | — | **28 HP** for strings with 1 heavy step, +4% per extra heavy step (29.1 / 30.2 HP); per-general normalisation `kStr` 0.84–1.03 |
| String time, first press → finisher hit | — | 1.26–1.62 s (base clock; ÷ spd 0.9–1.32 in real time) |
| Damage scaling by combo hit | none | 100, 100, 90, 80, 72, 65, 60, 60 %; finisher floor 75%, special floor 60% |
| Parry inside a combo | hitstun parry only on light3, window 0.6 × parry window | from **step 3** of any string; window × 0.55 (L) / 0.65 (M) / 0.80 (H) of the 0.15–0.22 s parry window; one try per hitstun |
| Duel crit | — | 3–7% by 武力 + gear/title (≤ +3 pp, inside the all-in duel budget, INTEGRATION §8.3) + counter-hit +15%, finisher +10%, tip hit +8%, low HP +5%; cap 35%; ×1.5 |
| Gear in the duel | — | only the `DUEL_ITEM_EFFECTS` whitelist (§3); one budget per fighter `dmg % + 0.4 × (HP + ability HP + guard HP) ≤ 6`; legendary set vs none at equal 武力 69–73 % (measured, INTEGRATION §8.3) |
| Duel length, AI 刘备 mirror | 38.4 s, 0% time-outs | target 20–40 s, ≤ 10% time-outs |

---

## 1. The clash (攻击画面) a bit slower

### 1.1 Current timeline (`js/clash.js` `TL`, speed 1)

`march1 0.62 · aim 0.34 · volley [0.46, 0.60] · flight 0.60 · charge0 1.06 · contact 1.66 · slowW 0.16 @ slowK 0.42 · melee1 2.56 · regroup1 3.08 · end 3.34`, then `FADE_IN 0.2` + `FADE_OUT 0.2`.

Real length = (end − slowW) / speed + slowW / (slowK · speed) + 0.4 = 3.18 + 0.38 + 0.4 = **3.96 s** normal. At ×2 (快) it is 2.18 s. The controller multiplies speed by 1.5 on enemy turns and 1.8 under 委任 (`battle-controller.js doAttack`).

### 1.2 What players miss and where the time goes

The melee is the part that reads as "the battle": 0.90 s of melee is too short to see who is winning. The march and volley already read well. So the extra time goes mostly into the charge and the melee, and a little into the contact slow-motion and regroup.

### 1.3 New timeline (speed 1)

| Mark | v3 | v4 | Segment length v3 → v4 |
|---|---|---|---|
| march1 | 0.62 | **0.70** | entrance 0.62 → 0.70 (×1.13) |
| aim | 0.34 | **0.38** | — |
| volley | [0.46, 0.60] | **[0.52, 0.68]** | — |
| flight | 0.60 | **0.66** | — |
| charge0 | 1.06 | **1.20** | march1→charge0 0.44 → 0.50 |
| contact | 1.66 | **1.92** | charge 0.60 → 0.72 (×1.2) |
| slowW / slowK | 0.16 / 0.42 | **0.24 / 0.40** | real contact slow-mo 0.38 → 0.60 s |
| melee1 | 2.56 | **3.18** | melee 0.90 → 1.26 (×1.4) |
| regroup1 | 3.08 | **3.80** | 0.52 → 0.62 |
| end | 3.34 | **4.10** | 0.26 → 0.30 |

Real lengths:

| Mode / context | Speed | v3 | v4 |
|---|---|---|---|
| 开, player's own attack | 1 | 3.96 s | **4.86 s** |
| 开, enemy turn | 1.7 (v3 1.5) | 2.77 s | **3.02 s** |
| 开, 委任 | 2.0 (v3 1.8) | 2.38 s | **2.63 s** |
| 快, own attack | 2.5 (v3 2.0) | 2.18 s | **2.18 s** |
| 快, enemy turn | 2.5 × 1.7 | 1.59 s | **1.45 s** |
| 关 | — | 0 | 0 |
| any, with an army crit | — | — | + 0.43 s / speed |

So 快 keeps its v3 length exactly (the request: keep fast mode). The enemy-turn and 委任 multipliers rise a little so that watching the AI does not get slower by the full 23%.

### 1.4 Implementation rule: one time map, no hand-edited offsets

`clash.js` has literal offsets relative to the TL marks (`TL.contact + 0.3`, `+ 0.68`, `m0 = contact + 0.08`, `m1 = melee1 − 0.2`, hit flashes in `[contact + 0.05, melee1 − 0.1]`, `planActs` start `contact − 0.08`, rout at `melee1 + 0.05`, `specialT = contact + 0.02` …). Do not retune each one. Add:

```js
// v3 时间 → v4 时间：按 TL 标记分段线性映射（各段的伸缩比见 COMBAT-V4 §1.3）
const TL3 = { march1: 0.62, charge0: 1.06, contact: 1.66, melee1: 2.56, regroup1: 3.08, end: 3.34 };
function tlMap(t3) { /* 分段：[0,march1] [march1,charge0] [charge0,contact] [contact,melee1] [melee1,regroup1] [regroup1,end]，超出 end 按最后一段外推 */ }
```

Then every literal absolute time t is written `tlMap(<v3 expression>)`. The soldiers' clip playback rates (`sp` in `planActs`, run cycles) stay as they are: the longer melee simply holds more swings, and run speed falls by about 17%. figure.js already derives stride frequency from displacement, so feet do not slide. `SKIP_HOLD` (0.4 s) and the fades stay real-time.

Mode values: `Clash.mode = 'fast'` sets `speed = 2.5` (was 2). `battle-controller.js doAttack`: `speed = autoPlayer ? 2.0 : watching() ? 1.7 : 1`. The cut-in (`cutIn`, 1.1 s ÷ speed, floor 0.7 s) is unchanged. In 快 it now floors at 0.7 s more often, which is fine.

Audio (consolidated with AUDIO-V4 §A8): at speed 1 the ruler-suite stinger starts on contact and its bar-2 downbeat lands 1.60 s later, on `melee1` (1.62 s; AUDIO-V4 replaces the old `speed × 3.96 / 4.86` workaround). **At speed ≥ 1.5** (enemy turn 1.7, 委任 2.0, 快 2.5) the 3.2 s phrase cannot fit: play the 1-bar `clash-short` instead, started so that its accent (the downbeat) lands at **contact + 1.62 / speed** (`melee1` at that speed). With a crit, the `clash-crit` stab is placed at **`tlMap(contact + 0.36)` ÷ speed** (the middle of the crit beat, §2.6) at every speed.

### 1.5 Acceptance

`tests/clash.html` measures real durations with the HUD clock and asserts: normal 4.86 ± 0.10 s, fast 2.18 ± 0.08 s, crit adds 0.43 ± 0.05 s at speed 1; the stinger accent at speeds **1.7 and 2.0** lands within ±0.2 s of `melee1` (`clash-short`'s downbeat at contact + 1.62 / speed; the same tolerance as speed 1), and the `clash-crit` stab within ±0.2 s of `tlMap(contact + 0.36) / speed`. The per-chunk casualty sum still equals `troopsBefore − troopsAfter`. Triangle and draw-call budgets are unchanged (≤ 350k, ≤ 120). The Unity port keeps `Clash.cs` constants in step (§16.4).

---

## 2. Army critical strikes (会心一击)

### 2.1 Rule

Only the attacker's strike in `BattleModel.attack(a, t)` can crit. The counter-attack, specials, tactics and the strategy-level `Conquest.autoResolve` never crit. Crits are a 武力 reward on the tactical board.

```
p(a) = clamp( 0.02
            + 0.0015 × max(0, a.gen.war − 50)          // 武力 100 → +7.5%
            + 0.004  × (lvl(a) − 1)                    // lvl = Progress.enabled ? vetLevel(gen) : 1 (ENGAGEMENT §4.4: non-battle exp at half) → L10 +3.6%
            + FORM_CRIT[a.formation]                   // 阵型，下表
            + min(0.06, gearCrit(a) + titleCrit(a))    // 装备 + 官爵（第四版其它规格提供，合计封顶 6%）
            + (a.morale >= 90 ? 0.01 : 0)
            − (a.confused > 0 ? 1 : 0),                // 混乱中不会会心
            0, 0.25)
critK(a) = 1.5 + min(0.3, gearCritMul(a))             // 传说 / 专属兵器可加会心伤害，至多 ×1.8
```

Implemented once as `SG.Mods.critChance(u)` / `SG.Mods.critK(u)` in `js/battle-math.js` (INTEGRATION §3.2); `BattleModel.critChanceOf(gen, formationIdx)` (the character-menu preview) calls the same function with the same `vetLevel`, and sim.js (1d) checks it (a general with domestic exp only gets half the level term); the gear and title terms are providers (`gearCrit`, `titleCrit`, `gearCritMul`). The crit factor is outside the ×1.60 battle-multiplier cap (it is a roll, not a modifier).

| 阵型 | 方圆 | 长蛇 | 鱼鳞 | 鹤翼 | 偃月 | 锋矢 | 雁行 | 衡轭 |
|---|---|---|---|---|---|---|---|---|
| FORM_CRIT | −2% | 0 | +2% | +1% | +3% | +4% | +1% | 0 |

Reasoning: 偃月 (主将当先) and 锋矢 (全军突击) are the attack formations, and the war requirements they already carry (75 / 85) make them the strong generals' tools. 方圆 is the turtle. The 变换阵型 menu line gains `会心±n%`.

Effect of a crit:

- `r.dmg` is computed as today, then multiplied by `critK` before `Math.max(10, …)` and the troop cap.
- The target's morale loss is the usual `dmg × 40 / …` plus 5.
- The attacker gets `xpHit` as usual and +2 exp extra (`xp.crits++`, `battleExp` adds 2 per crit, within `BATTLE_CAP`).
- `r.crit = true`, `r.critK` returned.

### 2.2 Random-number contract

`attack()` draws exactly one `SG.Random.value()` for the crit roll, first, on every call, even when `p = 0` or crits are switched off. It then draws the existing `rangeFloat` calls in their v3 order. Consequences:

- `BattleModel.critEnabled = false` (test switch, like `specialsEnabled`) gives the same random stream with no crits, so on/off runs are paired.
- In sim.js (7g), the opening battles (all level 1) stay identical with progression on and off, because the level term is `lvl − 1 = 0` either way.
- sim.js (1)–(3) outputs change versus v3 because the stream shifted. Update the printed expectations, not the tolerances.

### 2.3 Data and API

- `SG.Balance`: `CritBase 0.02, CritPerWar 0.0015, CritWarFrom 50, CritPerLevel 0.004, CritCap 0.25, CritK 1.5, CritMorale 5, CritGearCap 0.06, CritMulCap 0.3`.
- `SG.Defs.formations[i].crit` gets the FORM_CRIT value. `formation()` gains an 8th parameter, default 0.
- `BattleModel.prototype.critChance(u) → p` and the static `BattleModel.critChanceOf(gen, formationIdx) → p` (for the character menu, task 9).
- The gear and title numbers arrive as `SG.Mods` providers (`gearCrit` = ITEMS `critArmy` ≤ 0.04, `titleCrit` = TITLES `critArmy` ≤ 0.02, `gearCritMul` = ITEMS `critMulArmy` ≤ 0.30; INTEGRATION §3.3). Q3 is resolved.

### 2.4 AI

`aiNormalValue(u)` and the attack-target pick in `planAction` use `atkExpectCrit(a, t) = atkExpect(a, t) × (1 + p(a) × (critK(a) − 1))`. Every other use of `atkExpect` (mods, special values, refAction in tests) stays crit-free, so v3's special-versus-attack design ratios are untouched. The AI's formation choice already maximises `atk × …`. Add `+ FORM_CRIT × 0.5 × atk` to its formation score, so strong AIs pick 偃月 / 锋矢 slightly more often when attacking.

### 2.5 Presentation on the battle board (`battle-view.js`, add-only)

- `V.critHit(u, dmg)` (new) is called instead of `V.hit` when `r.crit`:
  - Gold float text `会心 −1234` at 1.3× size, with a dark ink outline.
  - A brush-stroke ink splash decal on the tile (0.6 s, warm vermillion core and gold rim).
  - The unit model shakes for 0.25 s and the camera nudges (amplitude 0.08, 0.2 s).
  - SFX `crit` (taiko hit + bright metal ring).
- Toast: `关羽部会心一击！`. The battle log line carries a gold `会心` tag.
- Unit card (`showCard`): `会心率 7%` under the attack value. The attack target preview shows `预计 1100–1500（会心 ×1.5 · 7%）`.

### 2.6 Presentation in the clash

`Clash.fromBattle(..., extra)` passes `crit: { side: 0, k: r.critK }` from `r`. With `crit` set, the clash adds a **crit beat**:

- Timeline window `[tlMap-contact + 0.30, + 0.42]` (0.12 s of timeline) runs at slowK 0.22, which is 0.55 s real at speed 1, so +0.43 s.
- At `charge0` the attacker's general moves to the front rank. In the beat he plays `light3` (spear, lance) or `heavy` (everything else) on horseback.
- A gold brush slash arc (ribbon mesh with a painted-stroke alpha texture, ink-edged) sweeps the defenders' front line. 5–8 front soldiers are blasted (`fallKind 'blast'`, as the special uses).
- Warm gold screen flash at 0.35 alpha. A vermillion **seal stamp** `会心一击` uses the existing `.sg-clash-stamp` with a seal variant `.seal` (square red stamp, brush characters, 0.42 s pop).
- A 35% share of the defender's loss is applied as a chunk of kind `crit` at the impact frame. It shows as a gold `−N!` number in the HUD. The rest is split as today.
- Shake 1.3. SFX `crit` plus a short war-cry hook `SG.Voice?.say(gen, 'crit')` (voices task, request 12); the music's `clash-crit` stab at `tlMap(contact + 0.36) / speed` (§1.4).
- In 快 the same beat plays at speed 2.5 (+0.17 s). With the clash off, only the board effect plays.

### 2.7 Balance targets (tests/sim.js new section (1d))

Run the (1c) battle set, 300 battles with crits off and on (same seeds, paired by §2.2):

| Metric | Target | Estimate from `combat/crit-estimate.js` |
|---|---|---|
| Crit rate per attack | 4–9% | 6.6% |
| Crit rate, attacker war ≥ 90 ÷ war 50–59 | ≥ 2× | 9.8% vs 1.2% |
| Attacker win rate shift | ≤ 4 pp | +1.4 pp |
| Average battle days shift | ≤ 1.0 | −0.3 |
| Average normal attack damage | +2% … +7% | +4% |
| Time-outs | not more than off + 10 | 20 vs 24 |
| (7h) progression swing | still ≥ 5 pp and ≤ 35 pp per side | — (crits add a level term) |
| Crit extra damage share of all attack damage | ≤ 6% | 3.7% |

Also add a scripted unit check: `p` at war 30/50/70/100 and level 1/10 matches the formula; a confused unit never crits; the counter-attack never crits; one random draw per call.

---

## 3. Duel critical strikes (会心)

Duel crits reward good play first and luck second. Random crits alone feel unfair in a fighting game. So most of the crit chance comes from situations the player creates.

```
p = 0.03 + 0.04 × clamp((war − 50) / 50, 0, 1)        // 武力 ≤ 50 → 3%，100 → 7%
  + min(0.03, gear.critDuel + title.critDuel)          // counted inside the all-in duel budget (INTEGRATION §8.3)
  + 0.15 if counter-hit (破绽: the defender was in startup / charge / parrywhiff — the v3 ×1.2 case)
  + 0.10 if the hit is a string finisher (§8.7)
  + 0.08 if 刃尖 tip hit: move is M, H, cH, ovh or a string M/H step and dx ≥ 0.85 × that move's reach
  + 0.05 if the attacker's HP ≤ 30 (背水)
cap 0.35
```

- **No crit** on guarded hits, chip, ripostes (already ×1.5), specials, arrows, or the deflect (硬挡) case.
- **Effect**: damage ×`critK` (1.5 + min(0.3, `gear.critMulDuel`)), applied after combo scaling (§8.5) and before the 1–40 clamp. Hitstop +0.05 s. Attacker rage +4. For standalone moves the defender's hitstun ×1.1. String steps keep their per-pair hitstun (§8.4), so a crit never breaks or extends a combo.
- **Random**: one `SG.Random.value()` per unguarded hit, drawn after the parry test and before the damage `rangeFloat`, on every hit even when `p = 0`. The duel-bots RNG stream changes versus v3 by design.
- **Presentation**:
  - A gold-white slash flare along the weapon trail.
  - A 4% camera push-in for 0.15 s.
  - An ink-splash burst at the contact point.
  - A gold `会心 −n` pop (the duel shows damage numbers only for crits and finishers).
  - SFX `crit`. A KO by crit uses the KO slow-motion with the seal stamp `会心`.
  - Log entry gets `crit: true`. The outcome stats add `crits`.
- **Expected effect**: about 5% of neutral hits, about 20% of counter-hits, about 15% of finishers, so 6–10% of all unguarded hits and about 5–8% more total damage. Targets are in §15.

**Item effects in the duel: the whitelist `DUEL_ITEM_EFFECTS`** (consolidation; INTEGRATION §8.2). `duel-game.js` reads only these item fields; `build.js check` rejects anything else, so no item can add a rule the duel does not know:

| Field | Meaning | Limit |
|---|---|---|
| `duelWar` | gear 武力 ÷ 3 added to the duel's 武力 | ≤ +3 |
| `dHp`, `dDmg`, `dGuard` | HP, damage %, guard reduction pp | inside the all-in budget `dmg % + 0.4 × (HP + ability HP + 0.5 × guard pp) ≤ 6` (gear HP ≤ 10, dmg ≤ 3 %, guard ≤ 6 pp); TITLES' `dmgDuel` and ENGAGEMENT's 宿敌 rival bonus join the dmg term (sum ≤ 6 %) |
| `crit`, `critDmg` (duel) | crit chance / multiplier | ≤ +3 pp with titles; multiplier ≤ +0.30 |
| `dParry` | `window` (≤ 0.01 s), `guardWindow` (≤ 0.02 s: a guard that early before a hit parries), `counterK` (≤ 1.7), `cdMinus` | |
| `dArmor` | `{ on: 'heavyStart' | 'lightTaken', dmgK 0.5–0.7 }`: no hitstun, the hit still deals `dmgK` of its damage | never full immunity |
| `dProc` | on-hit `knockdown` (only on full-charge heavies or with p < 1), `knockback`, `stun`, `burn` | |
| `dCombo` | `{ bonus ≤ 3 HP }` on a string step or finisher, `lightBonus ≤ 0.04` | |
| `dProjectile` | `{ hp ≤ 4 per projectile set }`, always parryable | |
| `dRegen`, `dLifesteal` (≤ 0.10), `dCounter { k ≤ 1.7 }`, `dRage`, `dLowHp (≤ ×1.15)`, `dDmgK (≤ 0.03)`, `dSpd`, `dReach` | | ability HP of all of them together ≤ 4 per duel (measured by section (15)) |

Removed in the consolidation: `unblockable`, `unparryable`, `dashUnblockable`, `dPerfectGuard` (→ `dParry.guardWindow`), and `noBreak` (diplomacy). Strings stay per general, never per weapon.

---

## 4. Duel v4: what changes

| v3 | v4 |
|---|---|
| J light (L-L-L chain), K heavy (hold), L guard (hold), ↓/S guard | J **轻 L**, K **中 M** (new), L **重 H** (hold to charge, as v3 K); guard = **hold back**; ↓/S = **crouch** |
| One generic chain light1 → light2 → light3 | Each general's **unique 8-step string** of L/M/H (§8); no generic chain |
| Hitstun parry only on the 3rd hit | Hitstun parry on **step ≥ 3** of any string, with a window that depends on the incoming step's type (§9) |
| All attacks mid | Lows (↓J, ↓K) must be crouch-blocked; overheads (air attacks, K+L 跃劈) must be stand-blocked (§6.4) |
| No crits | Crits (§3) |
| — | String knowledge: steps you have seen are remembered and shown (§12); practice yard 演武 (§12.4) |

Everything else from v3 stays: HP 100, 60 s, damage formula `base × (0.55 + war/100) × clamp(1 + Δwar/150, 0.6, 1.5)` with single hits clamped to 1–40, 武力-scaled speed and hitstun, rage and special, parry (window 0.15–0.22 s, stagger 0.6 s, counter window 0.7 s at ×1.5 and unblockable, whiff 0.4 s, cooldown 2.5 s), heavy charge and full-charge guard break, backstep and dash, jump-ins, bow specials, the AI's v3 refinements, and `Mdl.duelFinish` compatibility of `log`.

---

## 5. Controls

### 5.1 Keyboard (default preset `v4`)

| Key | Action |
|---|---|
| ← → / A D | move; **hold away from the opponent = guard** (§6.1); double-tap toward = dash, away = backstep (v3) |
| ↑ / W / Space | jump (one air attack per jump) |
| ↓ / S | crouch; **↓ + away = crouch guard** |
| J | 轻 light |
| K | 中 medium |
| L | 重 heavy (hold to charge as in v3; full charge breaks guard) |
| K + L together (within 0.05 s) | 跃劈 overhead |
| ↓ + J / K / L | crouch light (low) / sweep (low, knockdown) / rising slash (anti-air) |
| U | 格开 parry (unchanged) |
| I | 绝技 special (unchanged) |
| O | 防 stationary guard, only when the setting 「格挡键」 is on (§5.3) |

Why L moved from guard to heavy: J-K-L left to right becomes light-medium-heavy, which matches the combo notation 轻中重 = J K L. This is the standard three-strength layout (Samurai Shodown has light / medium / heavy slash, [Kotaku](https://kotaku.com/samurai-shodown-is-slow-and-deliberate-in-the-best-way-1835805404)). Guard becomes the hold-back the player asked for.

Preset `v3习惯` for returning players: J 轻, **K 重**, **L 中**. Muscle memory for heavy stays on K and the combo glyphs re-label automatically. It is selectable in 设置 → 单挑键位.

**Simultaneous-press rule:** when K or L goes down, the press is held for up to 0.05 s (3 logic steps) waiting for the other. Both → `ovh` tap. One → that press is committed with its original timestamp (the input buffer `BUFFER 0.16` absorbs the delay). Inside a string window (§8.2) K+L counts as whichever arrived first; there is no overhead inside strings.

### 5.2 Touch (iPhone landscape 844×390, iPad, touch laptops)

```
 ┌──────────────────────────────────────────────────────────────────────┐
 │ [portrait HP rage]                timer                [HP rage portrait] │
 │                                                                         │
 │                                                     (绝技)                │
 │   ▲                                         (格开◔)        (重)            │
 │ ◀   ▶      ← 8-way pad, diagonals = ▼◀ / ▼▶          (中)  ⟨中+重⟩  (轻)      │
 │   ▼                                                                     │
 │                        [招式谱 ribbon: ◆◆●▲◆●▲·▲]                          │
 └──────────────────────────────────────────────────────────────────────┘
```

- **Left pad** becomes 8-way. Zones are angular sectors from the pad centre: 45° sectors with a 14 px dead-zone.
  - Back = guard, down-back = crouch guard, down = crouch, up = jump, forward = walk, up-forward = forward jump.
  - Double-tap (two taps within 0.28 s) on ◀ or ▶ = dash or backstep as on keyboard.
  - The pad keeps v3's "one finger can slide between zones" capture.
- **Right cluster**, arranged in an arc for the right thumb:
  - 轻 is the big button at bottom-right.
  - 中 sits left of 轻, 重 above 轻.
  - Between 中 and 重 a small diamond target ⟨中+重⟩ fires the overhead. It is 44 px, overlapping both buttons by 8 px; a press that lands on it sends both.
  - 格开 has the cooldown ring (v3). 绝技 glows when rage = 100.
- Hit targets ≥ 56 px (≥ 44 px rule in DESIGN-V2 §1). Glyph colours match the HUD and character-menu glyphs (§12.1): 轻 teal circle ●, 中 amber diamond ◆, 重 vermillion triangle ▲.
- The optional 防 button (setting) sits above the pad's right edge. It keeps v3's slide-between 防 ↔ 格开.

### 5.3 Settings (`localStorage` key `sanguozhi2_duel_v4`, try/catch as always)

| Setting | Values (default first) |
|---|---|
| 单挑键位 | `v4` · `v3习惯` |
| 格挡键 | 关 · 开 (adds O / 防 = stationary guard, the same guard the AI uses, §6.6) |
| 招式提示 | 开 · 关 (shows the opponent's next known step, §12.3) |
| 招式谱条 | 自动 (on for the first 5 duels, then only in 演武) · 开 · 关 |

### 5.4 First v4 duel and the controls card

The first duel after the update shows a one-time banner above the controls card: **「操作更新：格挡改为按住背向对手的方向；L 为重击，K 为中击；↓ 蹲下，↓+背向为下段格挡。」**

The controls card (`DuelScreen.controlsCard`) shows, per device:

- move / guard: `← → 移动 · 按住背向对手 = 格挡`
- `↓ 蹲 · ↓+背向 = 下段格挡`
- `↑ 跳（空中攻击须站立格挡）`
- `J 轻 · K 中 · L 重（按住蓄力，满蓄破防）`
- `K+L 跃劈（中段，蹲挡挡不住）`
- `U 格开`: the v3 text, plus 「连段第三击起可看破」
- `I 绝技「…」`
- a new last row, **招式谱**: the general's eight glyphs with the finisher name, e.g. `◆ ◆ ● ▲ ◆ ● ▲ · ▲ 万众取首`

The legend strip at the bottom edge is updated the same way.

---

## 6. Guard by holding back

### 6.1 Input semantics

At each logic step the Sim derives from the controller's `held`:

```
back      = held.x === −f.face                 // 背向对手
wantBlock = back || held.guard                 // held.guard：防键（可选）与电脑 / 测试脚本用的「原地举防」
crouch    = held.down && grounded
```

Controller interface (v4): `held = { x, up, down, light, medium, heavy, guard, special, parry, dash }`, `taps = { light, medium, heavy, up, special, parry, dir, ovh }`. Old controllers that never set `down`, `medium` or `ovh` keep working: they cannot crouch or use medium. This matters for the v3 bots kept as regression (§15).

### 6.2 Proximity guard

Holding back does not freeze you. With no threat you walk backward at the v3 back-walk speed (0.72 × walk). When a **threat** exists you stop and raise the guard:

```
threat(f) = foe in an attack state whose phase is startup or active (or 'charge' with charge ≥ 0.2, or 'special')
            and |foe.x − f.x| ≤ foe.reach × reachK(foe.move) + BODY + 0.8
          || an enemy arrow within 4 m flying toward f
```

- `wantBlock` and threat → state `guard` (stand) or `guard` with `f.low = true` (crouch). No movement.
- When the threat ends, guard is kept 0.10 s more, then walking back resumes if back is still held.
- `held.guard` (防 button or AI) blocks with or without threat and never walks. This is exactly v3's guard.

### 6.3 Is the hit blocked?

Blocking is decided **at the moment of the hit**, not by state alone (standard fighting-game rule). So a back press that arrives on the same step as the hit still blocks, provided the defender is actionable.

```
canGuard = grounded && ( state ∈ {guard, guardstun} || ((actionable || state === 'crouch') && wantBlock) )
dirOK    = held.guard || back' ,  back' = (held.x === sign(o.x − f.x)), the direction AWAY from the attacker now;
           within 0.10 s after the attacker crossed over (x order changed), either direction counts (cross-up leniency)
heightOK = line === 'mid' || (line === 'low' && f.low) || (line === 'overhead' && !f.low)
guarded  = canGuard && dirOK && heightOK
```

v3's hard deflect (硬挡: in hitstun, holding guard, much higher 武力) keeps its formula with `holdGuard = wantBlock`. In strings it can fire at most once per string, and only from step 3.

Chip stays ×0.2 of the unscaled damage (v3). Guard break (full-charge heavy, special finisher, and the v4 string guard crush §8.7) stays 0.8 s × stunK.

Guard height may change during guardstun (switching ↓ is allowed). The next blocked hit uses the new height.

### 6.4 Crouch, lows, overheads

- `crouch` state: entered from idle/walk/guard while ↓ is held and the fighter is grounded. No crouch walking. Leaving takes 0 s.
- The hurtbox does not shrink. There are no high-only attacks in v4. A shorter hurtbox would make every L-starting string whiff on crouchers, and that would be half the roster's strings.
- Crouching does three things: crouch-guards lows, enables the crouch moves, and makes you lose to overheads.
- **Lows:** ↓J crouch light and ↓K sweep, and nothing else. String steps and finishers are all mid (§8.7).
- **Overheads:** every air attack (airL / airM / airH) and the K+L 跃劈.
- The 跃劈 startup is 0.32 s with a bright edge glint at t = 0 (like the heavy telegraph). It is reactable: human reaction ≈ 0.22–0.25 s, AI reaction 0.15–0.44 s. Lows are fast (0.10 / 0.16 s) and must be read.
- Balance intent: crouch-guard is the safe default posture. Overheads punish it but are slow and readable. The layer stays light, so the 武力-based balance and the v3 targets survive.

### 6.5 Air rules

- No guarding in the air. No parry in the air (v3). One air attack per jump (v3), chosen by J / K / L.
- Landing recovery as v3: 0.07 s if no air attack; after an air attack 0.20 s on hit or block, 0.30 s on whiff. airH whiff: 0.36 s.
- A defender hit while airborne at y > 0.35 is knocked down (v3). Such a knockdown ends any string in progress (§8.2).
- On landing, a held back or down-back takes effect after the landing recovery. With a threat present, the guard rises on the first actionable step.

### 6.6 Fairness between human and AI

The AI and test bots keep using `held.guard` (stationary guard, never walks). The human gets the same by tapping back as the attack comes, thanks to proximity guard and hit-time blocking, or by turning on the 防 button. The only remaining difference is pre-emptive guarding with no threat, where the human walks back and gives up space. This is accepted as the cost of the requested hold-back control. §15 (8) checks that hold-back guard and stationary guard give the same results within 6 pp.

---

## 7. Move list and frame data

All times are base seconds of the move clock. Real time = base ÷ `spd`, with `spd = actSpeed(war) × STYLE_SPD` as in v3 (0.9–1.12 × pole 1.0, one 1.14, dual 1.18). `dmg` is the base fed to `damage()`, × weapon `pow`. `stun` is hitstun on hit, × defender `stunK`. "On block" is the attacker's advantage in seconds after the move is blocked and the attacker does not continue; negative means the defender recovers first. The v3 jab startup 0.10 s ≈ 0.08–0.11 s real, so −0.09 or worse is punishable.

### 7.1 Standalone moves (from neutral)

| id | Input | Line | Startup | Active | Recovery | dmg | stun | Push | reachK | On block | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `L` | J | mid | 0.10 | 0.07 | 0.19 | 2.4 | 0.30 | 2.6 | 0.92 | −0.04 | = v3 light1; also the string starter when s1 = L |
| `M` | K | mid | 0.14 | 0.08 | 0.28 | 3.4 | 0.40 | 3.4 | 1.00 | −0.10 | new; longest-reaching quick poke (tip crit +8%); string starter when s1 = M |
| `H` | L (hold) | mid | `heavySU(charge)` | 0.10 | 0.40 | 4.8 + 5 × charge | 0.50 (× 1 + 0.3 charge) | 4.6 | 1.08 | −0.12 tap / −0.20 full | = v3 heavy: telegraph, charge ≥ 0.6 knockdown, ≥ 0.85 guard break |
| `cL` | ↓J | **low** | 0.10 | 0.06 | 0.20 | 1.8 | 0.26 | 2.0 | 0.90 | −0.03 | quick low poke |
| `cM` | ↓K | **low** | 0.16 | 0.08 | 0.40 | 3.0 | knockdown | — | 1.02 | −0.25 | sweep |
| `cH` | ↓L | mid | 0.13 | 0.12 | 0.42 | 4.2 | 0.45; airborne → knockdown | 3.0 | 0.95 | −0.22 | rising anti-air slash; hits y up to 9 (`hmax 9`); immune to air attacks from t = 0 to end of active |
| `ovh` | K+L | **overhead** | 0.32 | 0.08 | 0.36 | 4.4 | 0.46 | 3.2 | 1.00 | −0.18 | 跃劈: small hop (visual only, stays grounded for rules); edge glint at t = 0; not chargeable |
| `airL` | J in air | overhead | 0.05 | 0.20 | 0.10 | 3.0 | 0.36 | 3.0 | 0.88 | — | = v3 air |
| `airM` | K in air | overhead | 0.08 | 0.14 | 0.14 | 3.6 | 0.40 | 3.2 | 0.95 | — | |
| `airH` | L in air | overhead | 0.12 | 0.10 | 0.18 | 4.6 | 0.48 | 3.6 | 1.00 | — | landing recovery on whiff 0.36 |
| `special` | I | — | v3 | v3 | v3 | v3 | v3 | v3 | v3 | v3 | unchanged; can also be cancelled into from a string (§8.6) |

Guardstun for standalone moves: v3 formula `(0.12 + stun × 0.35) × stunK`. Then tune the per-move `blockstun` so the "On block" column holds at war 76 within ±0.02 s. `tests/duel-bots.js` (10g) measures it.

### 7.2 String steps (inside an 8-step string, §8)

| Step type | Startup | Active | Recovery | dmg | Push (hit / block) | Step-in | reachK | Hitstop | Notes |
|---|---|---|---|---|---|---|---|---|---|
| **L** step | 0.09 | 0.07 | 0.20 | 2.4 | 1.2 / 1.1 | 0.20 m | 0.95 | 0.065 | |
| **M** step | 0.13 | 0.08 | 0.26 | 3.4 | 1.5 / 1.35 | 0.26 m | 1.00 | 0.080 | |
| **H** step | 0.20 (+ delay ≤ 0.18) | 0.09 | 0.36 | 5.0 | 1.9 / 1.7 | 0.34 m | 1.04 | 0.110 | `big`; keep L held after pressing to delay the swing (§8.3) |
| Finisher **L** | 0.12 | 0.10 | 0.46 | 5.5 | knockdown / 3.0 | 0.30 m | 1.00 | 0.120 | |
| Finisher **M** | 0.16 | 0.10 | 0.50 | 6.5 | knockdown / 3.4 | 0.36 m | 1.04 | 0.130 | |
| Finisher **H** | 0.24 (+ delay ≤ 0.18) | 0.11 | 0.56 | 8.0 | knockdown / 3.8 | 0.42 m | 1.08 | 0.150 | |

Step-in is the attacker's forward travel during each step's startup. It roughly cancels the push, so the string stays in range: v3 pushback alone would carry the defender out of a spear's reach by step 4. Against the corner, v3's "push the attacker back" rule applies.

Hitstun and blockstun of string steps are not in this table. They depend on the next step (§8.4).

---

## 8. The combo system (招式谱 · 八式连段)

Every general has one fixed sequence of eight inputs over {L 轻, M 中, H 重}. Steps 1–7 are string steps (§7.2). Step 8 is the **finisher** (终结技), which has its own name and animation. Following the sequence chains the hits. A wrong button ends the string. Notation: `MMLHMLHH` = 中 中 轻 重 中 轻 重 · 重. In glyphs: ◆ ◆ ● ▲ ◆ ● ▲ · ▲.

### 8.1 Data model

```js
SG.Combos.of(gen) → {
  name: '关羽',
  seq: 'MMLHMLHH',                           // 8 字符，L / M / H
  steps: [{ type: 'M', variant: 'm_diag' }, …],   // 7 项：动作变体（§13.1），相邻两步不同变体
  finisher: { type: 'H', variant: 'f_pierce', name: '万众取首' },
  kStr: 0.856,                               // 伤害归一化系数（§8.5），由 seq 推出、不存
  src: 'hand' | 'gen' | 'fallback',          // 手写 / 生成表 / 运行时兜底（§10）
}
```

Data lives in `js/combo-data.js` (`SG.ComboData = { version: 1, entries: { 姓名: 'MMLHMLHH|万众取首|m_diag,m_spin,…,f_pierce' } }`). `SG.Combos` in `js/combos.js` parses it and provides the fallback, validation and knowledge (§12).

### 8.2 Starting and continuing a string

- **Start:** from a state that can start attacks (idle, walk, guard, `parryok` / riposte window, late backstep, dash, getting out of `land`), pressing the button of type `s1` starts the string at step 1. The other buttons give the standalone moves of §7.1. Crouch, air and K+L give their own moves and never start strings.
- **Step 1** is the neutral `L` or `M` of §7.1: its startup, active time, damage and reach apply. From the moment it starts a string it uses the string push and step-in (§7.2) and the per-pair hitstun and blockstun of §8.4. So the neutral M's large push of 3.4 applies only when it does not start a string, i.e. for generals whose s1 = L.
- **Window for step k+1** (k = 1…7), on the move clock of step k:
  - opens at `t = su_k − 0.06`, i.e. just before step k becomes active;
  - closes at `t = su_k + act_k + 0.55 × rec_k`.
- **Lock:** the first L/M/H press inside the window is locked in. Later presses in the same window are ignored.
- **Too early:** presses before the window opens are not buffered for the string. They are marked 早 in the training display. A player mashing during the startup of a step therefore drops the string. This is the main anti-mash rule.
- **Match:** step k+1 starts at `max(CP_k, press)`, where the cancel point `CP_k = su_k + 0.6 × act_k` (as v3's chain point). A press at or before `CP_k + 0.04` is **on-beat** (准). A later press is late (迟): legal, but see §8.4.
- **Mismatch:** the string ends after step k's normal recovery. The wrong press becomes a buffered standalone move, which fires if step k's recovery ends within `BUFFER = 0.16 s` of the press. Otherwise it is dropped. A mismatch costs time.
- **No press:** the string ends at step k (normal recovery). Stopping is always allowed. Where it is safe to stop, see §8.4.
- **Auto-end:** the string also ends if the defender is knocked down or KO'd, if the attacker is parried (`parried`), or if the attacker is hit (trade). Whiffed steps may continue: the player chose to.

### 8.3 Delayed heavy steps (迟滞)

After pressing L for an H step (or an H finisher), keeping L held pauses that swing at `t = su − 0.06` in a raised-blade pose, for up to 0.18 s or until L is released. Releasing shows the v3 heavy telegraph (edge glint + 「铮」).

A delay of 0.08 s or less is still a true combo. More than that releases the defender (§8.4). A delayed H is the attacker's answer to a defender who parries on the beat (§9.3). The cost: on hit it can be escaped, and on block it widens the frame-trap gap.

### 8.4 Hitstun and blockstun of string steps (true-combo guarantee)

For step k followed by step k+1, the on-beat time between their hits in real seconds is

```
gap_k = (0.6 × act_k + su_{k+1}) / spd_attacker          // su of a finisher from the finisher table
```

and step k sets

```
hitstun_k   = gap_k + 0.08 × stunK_defender                           // TOL = 0.08 s
blockstun_k = gap_k + 0.02                    if s_{k+1} ∈ {L, M}     // true blockstring: only a guardstun parry (§9) gets out
            = gap_k − 0.10                    if s_{k+1} = H          // frame-trap gap: a 0.09 s jab or a parry interrupts
finisher: knockdown on hit; blockstun 0.30 × stunK on block (or guard crush, §8.7)
```

So on hit, a string pressed on the beat is **uninterruptible**. An input up to 0.08 s late still combos. More than 0.08 s late (or a long delay on an H) lets the defender out, and a defender holding back blocks it.

Values at spd 1 (the attacker's war and style scale them):

| k → k+1 | gap | hitstun | blockstun | Advantage if the attacker stops after k on block |
|---|---|---|---|---|
| L → L | 0.132 | 0.212 | 0.152 | −0.08 (safe) |
| L → M | 0.172 | 0.252 | 0.192 | −0.04 (safe) |
| L → H | 0.242 | 0.322 | 0.142 | −0.09 (edge; fast war-100 jabs punish) |
| M → L | 0.138 | 0.218 | 0.158 | −0.13 (jab punishes) |
| M → M | 0.178 | 0.258 | 0.198 | −0.09 |
| M → H | 0.248 | 0.328 | 0.148 | −0.14 |
| H → L | 0.144 | 0.224 | 0.164 | −0.23 (jab and M punish) |
| H → M | 0.184 | 0.264 | 0.204 | −0.19 |
| finisher blocked | — | — | 0.30 × stunK | −0.20 … −0.30 |

The block-safety rule a player learns: **stop after a light step, never after a heavy one.** On-block push 0.9 × hit push, cancelled by step-in, keeps blockstrings in range.

### 8.5 Damage and scaling

```
dmg = clamp(round( damage(base_type × kStr × wpn.pow, war, foeWar)
                   × SCALE(n) × rangeFloat(0.92, 1.08) × (crit ? critK : 1) × (riposte ? 1.5 : 1) ), 1, 40)
SCALE(n) = [1.00, 1.00, 0.90, 0.80, 0.72, 0.65, 0.60][n − 1], 0.60 for n ≥ 7
finisher uses max(0.75, SCALE(n)); special hits use max(0.60, SCALE(n))
n = the hit's index in the current combo: v3's f.combo + 1, i.e. consecutive unguarded hits while the defender stays in hitstun
kStr = (28 / 1.31) / RAW(seq) × (1 + 0.04 × (nH − 1))
RAW(seq) = Σ_{i=1..7} BASE[s_i] × SCALE(i) + FIN[s_8] × 0.75     BASE L 2.4 · M 3.4 · H 5.0; FIN L 5.5 · M 6.5 · H 8.0
nH = number of H among steps 1–7 (1–3)
```

Chip on blocked steps is `damage(base × kStr × pow) × 0.2`, unscaled. Damage scaling is the genre standard (each later hit worth less, with a floor). Street Fighter 6 reportedly drops about 10 points per hit with floors for supers ([community report](https://steamcommunity.com/app/1364780/discussions/0/3810659147979043213); [scaling explainer](https://superjumpmagazine.com/how-damage-scaling-holds-fighting-games-together)). Our curve is gentler because our strings are fixed and parry-breakable.

Normalised damage, with `kStr` over all 790 legal strings at 0.835–1.025:

| Attacker vs defender war | Full 8 hits (1 / 2 / 3 heavy steps) | Notes |
|---|---|---|
| 30 vs 30 | 18.2 / 18.9 / 19.6 HP | |
| 50 vs 50 | 22.4 / 23.3 / 24.2 | |
| **76 vs 76** | **28.0 / 29.1 / 30.2** | the design target |
| 100 vs 100 | 33.1 / 34.5 / 35.8 | |
| 100 vs 76 | 38.4 / 40.0 / 41.5 | |
| 76 vs 100 | 23.5 / 24.5 / 25.4 | |
| 100 vs 30 | 48.6 / 50.5 / 52.5 | lopsided by design (v3 formula) |

The first three hits deal 8.9–13.9 HP at 76 vs 76; v3's L-L-L dealt 11.5, so early-string feel is preserved. Seven steps plus a special cancel deal 36.7–41.5 at 76 vs 76, and at most 49.1 at 100 vs 100: no single opening exceeds half the HP bar between equals. A riposte plus the full string deals 29.3–32.3.

The heavy-step premium (+4% each) pays for the risk: H steps open frame-trap gaps (§8.4), get the widest parry window (§9.2), and are unsafe to stop on.

### 8.6 Cancels and interactions

| With | Rule |
|---|---|
| **Special (I)** | From `CP_k` to the window close of any step 1–7, on hit or block (not whiff), with rage 100: cancel into the special. It keeps v3's freeze and invulnerable startup. Its hits use scale floor 0.6 and continue the combo count. |
| **Parry riposte** | The riposte's first hit may be step 1 of the string (press `s1` in `parryok`). That hit gets ×1.5, is unblockable and counts as combo hit 1. The rest of the string follows normally. |
| **Heavy charge** | Only the standalone H charges. String H steps never charge, but they can be delayed (§8.3). |
| **Jump / dash / backstep / parry / guard** | Cannot cancel a string step. The attacker can only stop (§8.2). |
| **Counter-hit (破绽, ×1.2)** | Applies to the string's first hit if it lands during the defender's startup, charge or parry whiff (v3). Crit +15% (§3). |
| **Hard deflect (硬挡)** | At most once per string, from step 3, using the v3 probability (§6.3). |
| **Opponent's string** | Getting hit ends your own string. Simultaneous hits trade (v3 same-step rule). |
| **Rage** | String step on hit: attacker +2 + 0.6 × dmg, defender +1.5 + 0.8 × dmg (lower than v3's 3 + 0.8 / 3 + 1.2 so an 8-hit string does not hand the victim a full bar). Blocked step: defender +1 + dmg, attacker +1. Standalone moves keep the v3 values. |

### 8.7 The finisher (终结技)

- **On hit:** knockdown (v3 knockdown physics); finisher crit chance +10% (§3); combo count ends.
  - Cinematic of 0.35 s: hitstop 0.15 s, then time scale 0.35 for 0.2 s.
  - Camera dollies 8% closer and tilts 2°.
  - An ink-wash ensō ring blooms at the defender.
  - The finisher name in brush calligraphy slides in on the attacker's side (DOM overlay, 0.9 s, does not block input).
  - SFX `finisher`, voice hook `SG.Voice?.say(gen, 'finisher')`.
  - First full completion in a match: a vermillion seal `八式·成` next to the combo counter.
- **On block:** blockstun 0.30 × stunK, attacker at −0.20 … −0.30, so it is punishable.
  - **Guard crush (压制破防):** if the 5 steps immediately before the finisher were all blocked, the blocked finisher becomes `guardbreak` (0.8 s × stunK, v3's guard-break rule). Event `guardcrush`, a cracking-ink effect over the guard.
  - This makes pure blocking against a full string lose. The defender's answer is a parry at a gap or on the finisher (§9).
- **On whiff:** full recovery (0.46–0.56 s), a big punish.
- **Line:** all finishers are mid.

### 8.8 HUD

- **Combo counter** (v3 `.sgd-combo`): `N 连击`, plus `k/8` while in your own string.
- **String ribbon** (player side, bottom-centre above the legend; setting §5.3):
  - Eight glyph slots of 26 px. The next expected glyph pulses while its window is open.
  - Each landed step fills with ink. Timing marks: gold tick 准, grey 迟, red ✕ for a mismatch, with a ghost of the expected glyph.
  - The finisher slot is a framed seal holding the finisher name.
- **Defender hint** (setting 招式提示, §12.3): during an enemy string, if the next step is known, a small glyph floats above the attacker's head 0.1 s after the current step hits.
- **Lanternvale treatment:** glyphs are painted seals (soft paper texture, ink outline, warm palette: teal ●, amber ◆, vermillion ▲). The ribbon sits on a translucent paper strip with a soft drop shadow, not a neon bar.

---

## 9. Parry against combos (看破连段)

v3's parry stays as it is: U, window `parryWindow(war)` = 0.15–0.22 s, stagger 0.6 s (+0.1 vs H), counter window 0.7 s at ×1.5 and unblockable, whiff 0.4 s, cooldown 2.5 s from the press. Specials and arrows cannot be parried. v4 generalises where a parry can interrupt a string.

### 9.1 Three ways to parry a string

| Situation | Window | Notes |
|---|---|---|
| **Stance:** standing or walking, before the string or at a gap | full `parryW` | v3 stance parry; parries step 1 or any step that arrives after a gap (late input, delayed H) |
| **Guardstun:** blocking a string step, then U for the next | full `parryW` | v3 guardstun parry. True blockstrings (§8.4) leave no other way out, so this is the main defence against blocked pressure, including the finisher. |
| **Hitstun:** being comboed, U for the next step k+1 | `min(parryW × K_type(s_{k+1}), gap_k − 0.03)`, valid only when **k + 1 ≥ 3**; K_L 0.55 · K_M 0.65 · K_H 0.80 · finisher 0.80 | generalises v3's "third hit, 0.6 × window". One attempt per hitstun (v3 `hsParrySeq`). The press must fall within `[T_hit − window, T_hit]`; earlier is 过早 (wasted, cooldown spent), later is simply hit. If the string stops instead, the armed parry leaves a 0.24 s whiff when the hitstun ends (v3: 0.6 × 0.4 s). |

The typed windows make heavy steps the easiest to break and light steps the hardest. The same idea appears in Killer Instinct, where heavy auto-doubles are slower and easier to break than lights ([Prima KI guide](https://primagames.com/eguides/killer-instinct-eguide/training/combo-breaker-system)). Since our steps have fixed startups (L 0.09, M 0.13, H 0.20), knowing the type of the next step **is** knowing when it lands. That is why string knowledge (§12) matters for defence.

### 9.2 Window sizes (real seconds)

| Defender 武力 | parryW | Hitstun window vs L / M / H / finisher |
|---|---|---|
| 30 | 0.150 | 0.083 / 0.098 / 0.120 / 0.120 |
| 65 | 0.185 | 0.102 / 0.120 / 0.148 / 0.148 |
| 100 | 0.220 | 0.121 / 0.143 / 0.176 / 0.176 |

Inter-hit gaps are 0.13–0.29 s on the base clock (§8.4), and as short as 0.10 s real for a fast dual-wielder's L → L. The `gap_k − 0.03` cap keeps every hitstun window at least 0.03 s shorter than the gap before it, so a press right after being hit is always 过早. That check is inherited from v3 test (4), which asserted it for light2 → light3 only. In practice the cap binds only for L steps of fast attackers against high-武力 defenders.

### 9.3 Delays versus parries

A defender times the hitstun parry for the on-beat hit. The attacker can:

- press late, up to 0.08 s, still combo;
- delay an H step, up to 0.18 s.

Either moves the hit later than the parry window, which makes the defender 过早. Counter-counter: delays beyond 0.08 s leave a gap that a defender holding back blocks, and a stance parry (full window) catches a long delay. So the delayed H is a real mind game, not a free win. The AI does all three (§14).

### 9.4 Punish

A successful parry of any string step:

- the attacker becomes `parried` (stagger 0.6 s; 0.7 s vs H steps and the finisher) and the string ends;
- the defender gets `parryok` plus the 0.7 s counter window. The riposte may start the defender's own string (§8.6), so a well-timed break turns about 30 HP of incoming damage into about 30 HP outgoing;
- the defender gains +8 rage and the attacker +2 (v3);
- a 0.13 s hitstop, a white-gold spark ring and the `parry` SFX as in v3, plus an ink-crack ✕ over the attacker's ribbon slot (`breakParry` event) and the voice hook `SG.Voice?.say(def, 'parry')`.

---

## 10. Uniqueness and generation for ~500 generals

### 10.1 Legal strings

A string is legal when:

- **C1** s1 ∈ {L, M}: a string cannot open with a heavy. Neutral H is the chargeable standalone heavy.
- **C2** s2 ≠ H: the opening is quick.
- **C3** in steps 1–7: at least 2 L, at least 2 M, and 1–3 H.
- **C4** no letter three times in a row anywhere, steps 1–8. Mashing one button gets at most 2 string hits.
- **C5** no two adjacent H within steps 1–7. H-H is allowed only as steps 7-8.

There are **790** legal strings (prototype). The finisher type is free: 255 L, 255 M and 280 H over the legal set.

### 10.2 Distinctness rules

- **R1** global uniqueness: no two generals share a string.
- **R2** within each **starting faction** (`Generals` column 5, not `-`), pairwise Hamming distance ≥ 2: two officers you fight side by side never differ by a single press.
- **R3** among the hand-made set (§11), pairwise Hamming distance ≥ 2.
- **R4** finisher names are globally unique and never equal any 必杀 or 计策 name (`SG.Specials.all()`, `SG.Defs.tactics`, the v3 tactics catalogue).

Hamming ≥ 2 across all 499 is impossible under C1–C5: the best greedy code found holds about 220 strings. So R2 limits the stronger rule to factions. `gen-combos-proto.js` assigns all 499 with R1–R4 and 0 relaxations, leaving **291 spare** strings for future generals.

### 10.3 Fit score (the string "fits" the weapon and the personality)

Weapon class from `DuelGame.lookOf(gen).weapon`:

- **pole**: 青龙偃月刀, 蛇矛, 画戟, 枪, 大刀, 骑矛
- **heavy**: 大斧, 斩马刀, 骨朵, 战斧
- **one**: 剑, 环首刀, 短戟, 短剑, 直刀
- **dual**: 双股剑, 双戟, 双刀

| Class | weight L | M | H | finisher L | M | H |
|---|---|---|---|---|---|---|
| pole | 0.8 | 1.2 | 1.0 | 0.5 | 1.0 | 1.2 |
| heavy | 0.6 | 1.0 | 1.4 | 0.3 | 0.8 | 1.5 |
| one | 1.2 | 1.1 | 0.7 | 0.9 | 1.2 | 0.8 |
| dual | 1.4 | 1.0 | 0.6 | 1.3 | 1.0 | 0.6 |

```
fit(g, s) = Σ_{i=1..7} w[s_i] (+0.3·a on each H) + 2 × wF[s_8] (+0.3·a if H)
          + 0.05 × intel/100 × (#type changes)          // 智：多变
          + 0.04 × pol/100 × (#i with s_i = s_{i−2})    // 政：规整
          + 0.9 × fnv32(name + '|' + s) / 2^32         // 确定性抖动
a = (war − intel) / 100                                  // 勇：重步
```

Resulting average steps 1–7 (L / M / H): one 3.00 / 2.76 / 1.24; pole 2.45 / 3.26 / 1.29; heavy 2.25 / 2.65 / 2.10; dual 3.75 / 2.00 / 1.25. Finisher types over all 499: 107 L, 222 M, 170 H.

### 10.4 Assignment algorithm (build tool, run once, output frozen)

1. Put the hand-made entries (§11) in first and check C1–C5 and R3.
2. Visit the other generals in the order **war descending, then name code-point ascending**.
3. For each, take the legal, unused string with the highest `fit` that satisfies R2 against the officers of its starting faction already assigned. If none fits, take the best unused string; the prototype needed this 0 times. Record it.
4. Write the result to `js/combo-data.js`. **Frozen:** once shipped, an entry never changes, so saves, the player's learned knowledge (§12) and their muscle memory stay valid. New generals in later versions are appended by running the tool in "append" mode, which skips existing names.
5. **Runtime fallback** (`SG.Combos.of` for a name missing from the table, e.g. a modded general): run step 3 against the frozen table plus the fallbacks already handed out this session, in the order those generals were first queried. The result is deterministic per session and flagged `src: 'fallback'`. It is never saved.

### 10.5 Finisher names and step variants

- **Finisher name** (generated entries): `prefix + suffix`.
  - Prefixes are culture prefixes (4–8 per culture code, e.g. roman 军团 / 罗马 / 凯旋 / 角斗, steppe 草原 / 控弦 / 狼骑 / 风雪) followed by weapon-class prefixes (24–32 per class).
  - Suffixes are 8 per finisher type: L 斩 / 闪 / 切 / 舞 / 连斩 / 疾风 / 乱刃 / 落叶 …
  - Start at `fnv32(name) mod N` and probe linearly until R4 holds. Examples from the prototype: 马克西米安 `LMHLLMLM`「罗马刺」, 沙伊鲁姆 `MLMHLMLM`「沙漠穿心」, 高览 `MLMLLHLM`「游龙穿心」.
- **Step variants** (animation, §13.1):
  - each step picks `variants[type][fnv32(name + i) mod 4]`;
  - if that equals the previous step's variant, take the next one, so swings alternate direction like real weapon play;
  - the finisher picks from the class's finisher list (§13.1).
  - Hand-made entries may pin variants.

### 10.6 Data checks (tests/combos.js, new)

- Every one of the 499 generals in both scenarios has a string.
- C1–C5 and R1–R4 hold.
- `kStr ∈ [0.83, 1.03]`.
- The table loads in < 5 ms.
- `SG.Combos.of` is deterministic, and its fallback never returns a string that is already in the table.
- Strings are frozen: diff against `tests/refs/combo-data.v4.json`. Any change to an existing entry fails unless it is listed in `tools/combo-migrations.json`.

---

## 11. The 65 hand-made strings

Data in `combat/hand-combos.json`; validated by `combat/gen-combos-proto.js`. Glyphs: ● 轻 L, ◆ 中 M, ▲ 重 H; the glyph after "·" is the finisher.

Sources are given in the table. Quotations from the 《三国志》 and its 裴注 are cited by chapter, as the text is commonly quoted. Web lookups in this session could only confirm some passages (see §19), so check every quotation against the 中华书局点校本 before it is shown to players. Entries drawn from the 《三国演义》 are marked as fiction. Foreign entries follow this repo's v2/v3 research notes.

| # | 武将 | 招式谱 | Glyphs | 终结技 | Fit | Source |
|---|---|---|---|---|---|---|
| 1 | 吕布 | `MLHMHLHH` | ◆ ● ▲ ◆ ▲ ● ▲ · ▲ | 人中吕布 (`f_cleave`) | 飞将：三个重步、重重收尾，最压迫的一套；画戟横扫 | 《三国志·吕布传》“便弓马，膂力过人，号为飞将”；裴注引《曹瞒传》“人中有吕布，马中有赤兔” |
| 2 | 张飞 | `LLMHMMHH` | ● ● ◆ ▲ ◆ ◆ ▲ · ▲ | 当阳横矛 (`f_pierce`) | 两记快刺起手，怒吼般连刺，重重收尾 | 《三国志·张飞传》“飞据水断桥，瞋目横矛” |
| 3 | 关羽 | `MMLHMLHH` | ◆ ◆ ● ▲ ◆ ● ▲ · ▲ | 万众取首 (`f_pierce`) | 中中起手：沉稳、威严，节奏层层加重；终结为策马一刺 | 《三国志·关羽传》“策马刺良于万众之中，斩其首还” |
| 4 | 马超 | `LMLMHLMH` | ● ◆ ● ◆ ▲ ● ◆ · ▲ | 西凉锦枪 (`f_pierce`) | 轻中交替的快枪，一重之后再起 | 《三国志·马超传》杨阜“超有信、布之勇” |
| 5 | 赵云 | `LLMLMLHM` | ● ● ◆ ● ◆ ● ▲ · ◆ | 一身是胆 (`f_pierce`) | 四个轻步、只有一记重：精准、克制；中段收尾 | 《三国志·赵云传》裴注引《云别传》“子龙一身都是胆也” |
| 6 | 孙策 | `MLMHLHMH` | ◆ ● ◆ ▲ ● ▲ ◆ · ▲ | 江东席卷 (`f_cleave`) | 两个重步隔开出现，一路压上 | 《三国志·孙策传》“转斗千里，尽有江东” |
| 7 | 典韦 | `LMMLHLHH` | ● ◆ ◆ ● ▲ ● ▲ · ▲ | 双戟八十斤 (`f_flurry`) | 双戟：轻中中起手，重重收尾如双戟齐落 | 《三国志·典韦传》“好持大双戟与长刀……帐下壮士有典君，提一双戟八十斤” |
| 8 | 许褚 | `MLHMLHMH` | ◆ ● ▲ ◆ ● ▲ ◆ · ▲ | 虎痴 (`f_grab`) | 力大而直：中轻重的笨重节奏反复两次，终结擒摔 | 《三国志·许褚传》“褚力如虎而痴，故号曰虎痴” |
| 9 | 张辽 | `LLMHLMHM` | ● ● ◆ ▲ ● ◆ ▲ · ◆ | 八百破阵 (`f_pierce`) | 不停前压：轻轻起手、两个重步，以中段收尾续冲 | 《三国志·张辽传》合肥之战“辽被甲持戟，先登陷陈”（八百人） |
| 10 | 文丑 | `MLLMHMHL` | ◆ ● ● ◆ ▲ ◆ ▲ · ● | 河北枪雨 (`f_flurry`) | 枪雨：轻步收尾的乱刺 | 《三国志·荀彧传》孔融“颜良、文丑，勇冠三军” |
| 11 | 颜良 | `MMHLMHLH` | ◆ ◆ ▲ ● ◆ ▲ ● · ▲ | 河北上将 (`f_cleave`) | 中中重的猛攻，重收 | 同上（孔融语）；颜良围白马见《三国志·关羽传》 |
| 12 | 庞德 | `LMHLMLHH` | ● ◆ ▲ ● ◆ ● ▲ · ▲ | 白马将军 (`f_cleave`) | 早出的重步、重重收尾：决死之势 | 《三国志·庞德传》“德常乘白马，羽军谓之白马将军”；“德亲与羽交战，射羽中额” |
| 13 | 甘宁 | `LLMLLMHL` | ● ● ◆ ● ● ◆ ▲ · ● | 锦帆铃响 (`f_flurry`) | 四个轻步的游击快刀，轻步收尾 | 《三国志·甘宁传》裴注引《吴书》“负毦带铃，民闻铃声，即知是宁” |
| 14 | 太史慈 | `LMLHMLMH` | ● ◆ ● ▲ ◆ ● ◆ · ▲ | 神亭夺戟 (`f_grab`) | 缠斗：轻中交错，终结夺戟擒摔 | 《三国志·太史慈传》“策刺慈马，而揽得慈项上手戟，慈亦得策兜鍪” |
| 15 | 黄忠 | `MLMHMLHM` | ◆ ● ◆ ▲ ◆ ● ▲ · ◆ | 定军斩将 (`f_shot`) | 老将稳重的中段节奏；终结退步一箭（弓将） | 《三国志·黄忠传》定军山“一战斩渊” |
| 16 | 孙坚 | `LMHMLMHL` | ● ◆ ▲ ◆ ● ◆ ▲ · ● | 阳人枭将 (`f_cleave`) | 古锭刀快斩，轻步收尾（一刀枭首） | 《三国志·孙坚传》阳人之战“大破卓军，枭其都督华雄等” |
| 17 | 兀突骨 | `MLHMHMLH` | ◆ ● ▲ ◆ ▲ ◆ ● · ▲ | 藤甲碎岳 (`f_cleave`) | 笨重的骨朵：重步多、节奏慢 | 《三国演义》第九十回 藤甲军 |
| 18 | 魏延 | `LMHLHMLH` | ● ◆ ▲ ● ▲ ◆ ● · ▲ | 子午奇谋 (`f_pierce`) | 早出的重步与重收：奇兵突出 | 《三国志·魏延传》裴注引《魏略》子午谷之计 |
| 19 | 夏侯渊 | `LLMLMHLM` | ● ● ◆ ● ◆ ▲ ● · ◆ | 三日五百 (`f_shot`) | 急行军般的轻快节奏；终结一箭 | 《三国志·夏侯渊传》“典军校尉夏侯渊，三日五百，六日一千” |
| 20 | 徐晃 | `MLMHLMHH` | ◆ ● ◆ ▲ ● ◆ ▲ · ▲ | 长驱径入 (`f_cleave`) | 大斧：中轻中起手，重重收尾 | 《三国志·徐晃传》太祖令“未有长驱径入敌围者也” |
| 21 | 华雄 | `MMLHMLLM` | ◆ ◆ ● ▲ ◆ ● ● · ◆ | 汜水关前 (`f_cleave`) | 关前猛将：中中起手，中段收尾 | 《三国演义》第五回；史载为孙坚所斩（《孙坚传》） |
| 22 | 张郃 | `LMLMLHML` | ● ◆ ● ◆ ● ▲ ◆ · ● | 识变数 (`f_flurry`) | 灵巧多变：轻中交替，重步在后，轻步收尾 | 《三国志·张郃传》“郃识变数，善处营陈” |
| 23 | 夏侯惇 | `MLHLMMHH` | ◆ ● ▲ ● ◆ ◆ ▲ · ▲ | 盲夏侯 (`f_pierce`) | 刚烈：早出重步、重重收尾 | 《三国志·夏侯惇传》“为流矢所中，伤左目”；裴注引《魏略》“军中号惇为盲夏侯” |
| 24 | 姜维 | `LMMHLLMM` | ● ◆ ◆ ▲ ● ● ◆ · ◆ | 胆大如斗 (`f_pierce`) | 智勇兼备：中段收尾，节奏多变 | 《三国志·姜维传》裴注引《世语》“维死时见剖，胆如斗大” |
| 25 | 周泰 | `MLMLHMLH` | ◆ ● ◆ ● ▲ ◆ ● · ▲ | 十二创 (`f_cleave`) | 耐战：中轻反复，一重之后重收 | 《三国志·周泰传》“泰以身卫权……被创十二处” |
| 26 | 沙摩柯 | `MLHMMHLH` | ◆ ● ▲ ◆ ◆ ▲ ● · ▲ | 五溪蛮锤 (`f_cleave`) | 铁蒺藜骨朵：重而野 | 《三国演义》第八十三回 |
| 27 | 张任 | `LMHMLLHM` | ● ◆ ▲ ◆ ● ● ▲ · ◆ | 雁桥死节 (`f_pierce`) | 严整的枪阵：中段收尾 | 《三国志·刘璋传》裴注引《益部耆旧杂记》张任“老臣终不复事二主” |
| 28 | 孟获 | `MMHMLLHH` | ◆ ◆ ▲ ◆ ● ● ▲ · ▲ | 七纵七擒 (`f_grab`) | 蛮力：中中重起手，重重收尾 | 《三国志·诸葛亮传》裴注引《汉晋春秋》“七纵七禽” |
| 29 | 董卓 | `MLHLMHMH` | ◆ ● ▲ ● ◆ ▲ ◆ · ▲ | 双鞬驰射 (`f_cleave`) | 膂力过人：重步多 | 《三国志·董卓传》“膂力过人，双带两鞬，左右驰射” |
| 30 | 高顺 | `LMLMLMHM` | ● ◆ ● ◆ ● ◆ ▲ · ◆ | 陷阵营 (`f_pierce`) | 军纪严整：轻中轻中轻中的规整节拍 | 《三国志·吕布传》裴注引《英雄记》“铠甲斗具皆精练齐整，每所攻击无不破者，名为陷陈营” |
| 31 | 曹仁 | `MLMLHLMH` | ◆ ● ◆ ● ▲ ● ◆ · ▲ | 将军天人 (`f_cleave`) | 守将：中轻反复，重收 | 《三国志·曹仁传》“左右叹曰：将军真天人也” |
| 32 | 乐进 | `LLHMHLMH` | ● ● ▲ ◆ ▲ ● ◆ · ▲ | 每战先登 (`f_pierce`) | 先登：第三步就出重 | 《三国志·乐进传》“每临战，常为先登” |
| 33 | 纪灵 | `MLMHMLLH` | ◆ ● ◆ ▲ ◆ ● ● · ▲ | 淮南上将 (`f_cleave`) | 三尖两刃刀：中段为主 | 《三国演义》第十六回（三尖两刃刀）；辕门射戟见《三国志·吕布传》 |
| 34 | 文聘 | `MMLMHLHL` | ◆ ◆ ● ◆ ▲ ● ▲ · ● | 江夏铁壁 (`f_flurry`) | 守城名将：中中起手，轻收 | 《三国志·文聘传》石阳坚守 |
| 35 | 马腾 | `MLLHMLMH` | ◆ ● ● ▲ ◆ ● ◆ · ▲ | 伏波之后 (`f_pierce`) | 西凉骑将 | 《三国志·马超传》裴注引《典略》“腾……马援后也” |
| 36 | 祝融 | `LLMLHMLL` | ● ● ◆ ● ▲ ◆ ● · ● | 五口飞刀 (`f_shot`) | 双刀快斩，终结掷出飞刀（远程收尾） | 《三国演义》第九十回 |
| 37 | 凌统 | `LMLLMHLM` | ● ◆ ● ● ◆ ▲ ● · ◆ | 三百扶捍 (`f_flurry`) | 双刀：轻步多，中段收尾 | 《三国志·凌统传》合肥“统率亲近三百人陷围，扶捍权出” |
| 38 | 邓艾 | `MLLMHLMM` | ◆ ● ● ◆ ▲ ● ◆ · ◆ | 阴平裹毡 (`f_pierce`) | 奇袭：重步在中段，以两中收尾 | 《三国志·邓艾传》“艾以毡自裹，推转而下” |
| 39 | 公孙瓒 | `LMLHLMLM` | ● ◆ ● ▲ ● ◆ ● · ◆ | 白马长史 (`f_pierce`) | 白马骑兵的轻快冲刺 | 《后汉书·公孙瓒传》“白马长史”、“白马义从” |
| 40 | 吕蒙 | `LLMMHLML` | ● ● ◆ ◆ ▲ ● ◆ · ● | 刮目相待 (`f_flurry`) | 士别三日：快而隐蔽，轻步收尾 | 《三国志·吕蒙传》裴注引《江表传》“士别三日，即更刮目相待”；白衣渡江“使白衣摇橹” |
| 41 | 刘备 | `LMLMHLLM` | ● ◆ ● ◆ ▲ ● ● · ◆ | 桃园同心 (`f_flurry`) | 双股剑：轻中交替的双剑连环 | 《三国演义》第一回；双股剑见《三国演义》 |
| 42 | 曹操 | `MLLMHMLM` | ◆ ● ● ◆ ▲ ◆ ● · ◆ | 倚天出鞘 (`f_pierce`) | 剑：中段起落，收放有度 | 《三国演义》第四十一回 倚天剑 |
| 43 | 周瑜 | `LMLMHMLL` | ● ◆ ● ◆ ▲ ◆ ● · ● | 曲有误 (`f_flurry`) | 讲究节拍的剑：最后两个轻步像拨弦 | 《三国志·周瑜传》“曲有误，周郎顾” |
| 44 | 黄盖 | `MMLHLLMH` | ◆ ◆ ● ▲ ● ● ◆ · ▲ | 赤壁火船 (`f_cleave`) | 老将：中中起手，重收 | 《三国志·周瑜传》黄盖火攻 |
| 45 | 张绣 | `LLMHMLML` | ● ● ◆ ▲ ◆ ● ◆ · ● | 宛城之变 (`f_flurry`) | 枪：快起手，轻步收尾 | 《三国志·张绣传》宛城袭曹 |
| 46 | 周仓 | `MMHLLMHM` | ◆ ◆ ▲ ● ● ◆ ▲ · ◆ | 扛刀随侍 (`f_cleave`) | 大刀：中中重 | 《三国演义》 |
| 47 | 关平 | `MMLHLMHL` | ◆ ◆ ● ▲ ● ◆ ▲ · ● | 父子同阵 (`f_cleave`) | 前四步与关羽相同（家传），后段不同 | 《三国志·关羽传》“羽子平” |
| 48 | 袁绍 | `MLMLMHLH` | ◆ ● ◆ ● ◆ ▲ ● · ▲ | 四世三公 (`f_cleave`) | 名门：中轻规整，重收 | 《三国志·袁绍传》“自安以下四世居三公位” |
| 49 | 诸葛亮 | `LMLMLHLM` | ● ◆ ● ◆ ● ▲ ● · ◆ | 卧龙吟 (`f_launch`) | 文臣之剑：规整的轻中节拍 | 《三国志·诸葛亮传》“推演兵法，作八阵图” |
| 50 | 马岱 | `LMHMLLMH` | ● ◆ ▲ ◆ ● ● ◆ · ▲ | 汉中追斩 (`f_pierce`) | 短戟：早出重步，重收 | 《三国志·魏延传》（马岱追斩魏延）；《三国演义》第一百五回 |
| 51 | 马克西明 | `MLHMLMHH` | ◆ ● ▲ ◆ ● ◆ ▲ · ▲ | 十六连胜 (`f_grab`) | 巨人摔跤手：重步多，终结擒摔 | 《罗马君王传·二马克西明》第3章（竞技中连胜十六人）；本仓库 research/europe.md |
| 52 | 芬恩 | `LMLHMLHL` | ● ◆ ● ▲ ◆ ● ▲ · ● | 菲安纳之誓 (`f_flurry`) | 神话英雄：轻快、多变 | 芬恩故事群（神话传说）；本仓库 research/v3/rome.md |
| 53 | 范师蔓 | `LLMHLMLH` | ● ● ◆ ▲ ● ◆ ● · ▲ | 扶南大王 (`f_cleave`) | 扶南雄主：快刀起手，重收 | 《梁书·扶南国传》“蔓勇健有权略”；本仓库 research/southeast-asia.md |
| 54 | 迦梨迦罗 | `MLMLHLHM` | ◆ ● ◆ ● ▲ ● ▲ · ◆ | 文尼大捷 (`f_pierce`) | 朱罗王：中轻节拍，中段收尾 | 桑伽姆诗歌《波鲁纳拉鲁帕代》（文尼之战）；本仓库 research/v3/india-kushan.md |
| 55 | 阿尔达希尔 | `LMMHLHMH` | ● ◆ ◆ ▲ ● ▲ ◆ · ▲ | 万王之王 (`f_pierce`) | 骑矛对冲：两个重步，重收（马上一刺） | 菲鲁扎巴德浮雕（阿尔达希尔马上击落阿尔达班四世），霍尔木兹甘之战（224 年）后称「万王之王」 |
| 56 | 纳尔奇苏斯 | `LLMLHMMH` | ● ● ◆ ● ▲ ◆ ◆ · ▲ | 摔跤锁喉 (`f_grab`) | 摔跤手：近身缠打，终结锁喉擒摔 | 卡西乌斯·狄奥《罗马史》73.22（勒死康茂德的摔跤手）；本仓库 research/europe.md |
| 57 | 内敦切利扬 | `MLMHLLML` | ◆ ● ◆ ▲ ● ● ◆ · ● | 塔莱大捷 (`f_flurry`) | 潘地亚王：轻步收尾 | 桑伽姆诗歌（塔莱亚兰加南之战）；本仓库 research/v3/india-kushan.md |
| 58 | 康茂德 | `LMLLMHML` | ● ◆ ● ● ◆ ▲ ◆ · ● | 左手剑 (`f_flurry`) | 角斗士皇帝：左手持剑（镜像架势），轻快的角斗节奏 | 卡西乌斯·狄奥《罗马史》73（以追击士 secutor 下场，自夸左撇子） |
| 59 | 塞维鲁 | `MMLMHLLH` | ◆ ◆ ● ◆ ▲ ● ● · ▲ | 军团拥立 (`f_pierce`) | 罗马短剑重刺：维吉提乌斯「刺而不砍」 | 维吉提乌斯《兵法简述》卷一「不要砍，要刺」 |
| 60 | 轲比能 | `LMHLLHMM` | ● ◆ ▲ ● ● ▲ ◆ · ◆ | 鲜卑控弦 (`f_shot`) | 草原骑将：两重步、两中收尾，终结回身一箭 | 《三国志·乌丸鲜卑东夷传》轲比能“控弦十余万骑” |
| 61 | 蹋顿 | `MLHLLMLH` | ◆ ● ▲ ● ● ◆ ● · ▲ | 白狼山 (`f_cleave`) | 乌桓骨朵：中轻重起手 | 《三国志·乌丸传》白狼山之战 |
| 62 | 神功皇后 | `LMLLHMLM` | ● ◆ ● ● ▲ ◆ ● · ◆ | 三韩征伐 (`f_pierce`) | 直刀：轻快、中段收尾 | 《日本书纪》神功皇后纪（传说） |
| 63 | 故国川王 | `LMMLHLML` | ● ◆ ◆ ● ▲ ● ◆ · ● | 平左可虑 (`f_flurry`) | 短戟：轻步收尾 | 《三国史记·高句丽本纪》故国川王（191 年平定左可虑之乱） |
| 64 | 巴洛马尔 | `MLHLMHLL` | ◆ ● ▲ ● ◆ ▲ ● · ● | 十一族盟 (`f_cleave`) | 日耳曼战斧：重步在前，轻轻收尾（连斧） | 本仓库 research/europe.md（166/167 年代表十一个部族议和） |
| 65 | 赞提库斯 | `MMLHMHLH` | ◆ ◆ ● ▲ ◆ ▲ ● · ▲ | 冰河铁骑 (`f_pierce`) | 萨尔马提亚双手长矛（kontos）：中中起手，重收 | 卡西乌斯·狄奥《罗马史》71.7（雅济吉斯人在结冰的多瑙河上作战）；塔西佗《历史》1.79（双手使用的长兵） |

Design notes on the hand-made set:

- **Families:**
  - 关平 inherits 关羽's first four steps (`MMLH`), a teachable "family" rhyme.
  - 张飞 and 赵云 both open `LLM` (spear jabs) but diverge at step 4 (张飞 goes heavy, 赵云 stays light).
  - 高顺's `LMLMLM` regular beat reads as drill discipline.
  - 周瑜's string ends `…MLL` like plucked strings (曲有误).
- **Weight:** 吕布 alone has three heavy steps plus an H finisher. 典韦, 庞德, 关羽, 张飞, 夏侯惇, 徐晃, 孟获 and 马克西明 have two heavy steps and an H finisher. These are the heaviest feels. 赵云, 甘宁, 夏侯渊, 张郃, 祝融, 康茂德 have one heavy step and an L/M finisher (the fastest).
- **Finisher variants:**
  - grapplers (许褚, 太史慈, 孟获, 马克西明, 纳尔奇苏斯) end with `f_grab`;
  - bow generals (黄忠, 夏侯渊, 轲比能) and 祝融 (thrown knives) end with `f_shot` (a short-range projectile at the finisher's hit time; it behaves as a melee hit for rules and range, it only looks like a shot);
  - 康茂德 fights **left-handed** (mirrored stance: `look.mirror = true`, rules unchanged).
- **Variant choice is cosmetic:** variants never change rules. Only the type (L / M / H / finisher type) does.

---

## 12. Knowledge, the character menu and practice

### 12.1 Character menu → 武艺 tab (request 9; the character-menu task owns the screen, this section owns the content)

For the selected general, top to bottom:

1. **招式谱** row: 8 painted seals (●◆▲, 40 px). Under each seal: the key for the current device (J / K / L, or the touch glyph). Finisher seal framed, with its name in brush calligraphy.
2. **Per-step chips** under the seals:
   - damage share (`≈ 3%`, versus an equal-war general);
   - safety tag on block: 安全 (L steps), 小破绽 (M), 大破绽 (H), 可迟滞 (H), 可被看破 (steps 3–8).
3. **Totals:** `全中约 28%` (versus equal 武力), `用时 1.4 秒`, and finisher crit `+10%`.
4. **Preview:** a small 3D viewport plays the general's figure performing the string in a 2.6 s loop (`SG.Figure` high LOD, Lanternvale shading, warm key light).
5. **Mastery stars** (§12.4) and the **演武** button (practice).
6. For other factions' generals: the same row, but **unseen steps show as "？"**, with the tooltip 「在单挑中见识过的招式会记下来」. Damage, safety and preview cover only the known prefix.

### 12.2 Learning other generals' strings (见识)

- Each time a step of a general's string is executed in a duel the player **fights or watches** (not skipped), that step becomes known to the player.
  - Rendered duels count, including 武林大会 matches (request 14).
  - Hits, blocks and whiffs all count.
- Storage: `G.comboSeen[name] = bitmask` (8 bits), saved; missing means 0. Own-faction generals are always fully known.
- A general captured or recruited by the player becomes fully known.
- Knowledge is per player save. AI knowledge is per duel (§14.2).

### 12.3 Defender hint in duels

With 招式提示 on, during an enemy string, if step k+1 is known, its glyph appears above the attacker 0.1 s after step k hits, and pulses in the colour of its type. This turns knowledge into parry timing (§9.1) without auto-parrying. With the hint off (purists), knowledge only fills the character menu.

### 12.4 Practice yard 演武 (from the 武艺 tab)

- `SG.DuelGame.practice({ gen, dummy })` opens the duel screen with no timer and no result. A 「退出」 button stays visible.
- Stage: a Lanternvale training yard (wooden posts 木人桩, straw targets, paper lanterns, warm dusk light).
- Dummy modes: 站立 (stands) · 格挡 (blocks everything standing) · 蹲挡 (crouch-guards) · 看破 (parries the player's string at step 4 if on the beat, so the player must delay or stop) · 对练 (the AI at a chosen 武力 30 / 60 / 90).
- Overlay:
  - the string ribbon with 准 / 迟 / 早 marks per step, plus the actual offset in ms from the cancel point;
  - an input history of the last 12 presses;
  - per-hit damage and combo count;
  - frame-advantage readout on block (`−0.08`).
- **Mastery stars (熟练, cosmetic only, saved per player general in `G.comboMastery[name]`):**
  - ★ complete the string on hit;
  - ★★ complete it 5 times with every step on the beat;
  - ★★★ complete it against 看破 (needs a delayed or late step at step 4).
  - Stars show in the character menu. At ★★★ the finisher gets a gold ink trail.
  - No stat bonus, so balance is untouched.

---

## 13. Presentation: animation, effects, sound

### 13.1 Animation clips (`js/figure.js`, `defClip` with per-style keyframes; styles pole / one / dual; bow, fan and none fall back to one)

Clip length = the move's total at spd 1. The figure's hit pose sits at `startup` (v3's `FIG_SWING` table gains one entry per new clip).

| Group | Clips (★ = already exists) | Used by |
|---|---|---|
| L step variants | `l_cut` ★light1 · `l_rise` ★light2 · `l_jab` short thrust · `l_pommel` pommel / shield bash (dual: cross-cut) | L steps, standalone L (`l_cut`) |
| M step variants | `m_diag` diagonal cleave · `m_spin` turning slash · `m_thrust` stepping thrust · `m_bash` shoulder / shield charge | M steps, standalone M (`m_diag`) |
| H step variants | `h_over` ★heavy · `h_rise` rising heavy · `h_sweep` flat heavy sweep · `h_lunge` leaping thrust · `hhold` raised-blade hold (delay §8.3) | H steps |
| Finishers | `f_cleave` spinning overhead · `f_launch` rising launch · `f_pierce` driving thrust · `f_flurry` three-beat flourish ending in one hit · `f_grab` seize-and-throw (needs a paired defender reaction `thrown`) · `f_shot` step-back release (bow, thrown knife) | step 8 |
| Stance / other | `crouch` · `cblock` crouch guard · `clow` ↓J · `sweep` ↓K · `rise` ↓L anti-air · `leapchop` K+L 跃劈 (hop is visual) · `airM` · `airH` · `thrown` | §7.1 |

That is 25 new clips, about 70 keyframe sets with the style variants. **Until a clip exists**, map it to the nearest existing clip, time-scaled to the move's length:

- `l_jab` → light1; `l_pommel` → light2;
- `m_*` → light3;
- `h_*` → heavy;
- `f_*` → the first 0.6 s of `special`;
- `crouch` / `cblock` → block with hip −0.18;
- `sweep` / `clow` → light1 with hip −0.25;
- `rise` → light2;
- `leapchop` → heavy plus a 0.25 m hop;
- `thrown` → knockdown.

So phase 1 (rules) can ship before the art lands.

Lanternvale treatment: clips keep the v3 hero proportions but read clearly in silhouette: one clean arc per swing, a settle beat at the end of recovery, cloth and plume springs (v3 `FIG_SEC`). The ink outline pass stays on during motion.

### 13.2 Effects (warm, painterly; pooled in the existing `Particles` / `Trail` classes; ≤ 2 extra draw calls per effect)

| Event | Effect |
|---|---|
| String step swing | brush-stroke trail tinted by type: L teal `#5fb3a8`, M amber `#e0a24a`, H vermillion `#d2553a`; ink-dark edge; width L 0.6 / M 0.8 / H 1.0 |
| On-beat step (player only) | tiny gold ink dot on the ribbon; no world effect (keeps the screen clean) |
| Delay hold (§8.3) | blade glint plus a slow warm glow building on the edge (the v3 heavy telegraph) |
| Crit | gold-white flare along the trail, ink splash burst, gold `会心 −n` pop |
| Finisher hit | ensō ink ring, finisher name in brush calligraphy, a short warm bloom (lantern-like, not neon) |
| Guard crush | cracking-ink lines over the guard, paper-tear sound |
| Parry of a string step | v3 white-gold spark ring with an ink edge; ink ✕ on the attacker's ribbon slot |
| Low hit / sweep | dust kick at the feet, low camera shake |
| Overhead startup | high edge glint at t = 0 (readable cue) |

### 13.3 Sound (names for `SG.Sfx.play`; recipes for the audio task; existing names as fallback)

| Name | Recipe | Fallback |
|---|---|---|
| `crit` | taiko hit + bright metal ring (≈ 2.4 kHz, 0.4 s decay) | `hit` + `duel` |
| `finisher` | big taiko + rising gong swell (1.2 s) | `win` |
| `breakParry` | bright clang + reversed whoosh | `parry` |
| `guardCrush` | crack + low thud | `hit` |
| `ovhGlint` | high "tsing" | v3 heavy release |
| `lowHit` | dull thump + dust | `hit` |
| `comboTick` | soft wood block, 0.15 vol, only with the ribbon visible | — |

Voice hooks for request 12: `SG.Voice?.say(gen, 'crit' | 'finisher' | 'parry' | 'guardcrush')`.

---

## 14. AI

The v3 AI (`class AI`, about 700 lines) stays the base. Every rule below goes through its existing perception (`sim.seen(idx, this.react)`), skill curves (`s`, `gs`, `rk`, `parryP`, `parryErr`) and memories.

**Honesty rule (tested):** the AI never reads the opponent's string beyond what it has seen in this duel, plus the optional battle memory (§14.2). It never reacts faster than `react`.

### 14.1 Using its own string

- **Start:** where v3 pressed `light` to poke or punish, v4 presses `s1` to start the string. Exception: when speed matters (punishing a short window, anti-poke, getup pressure) it takes the faster of standalone `L` and `s1`.
- **Spacing poke:** at distances between L reach and M reach it uses standalone `M` (tip crit) with probability `0.4 × gs`.
- **Continue on hit** (the defender in hitstun): each step continues with probability `cont = 1 − (1 − comboP) × 0.35`.
  - With v3's `comboP = 0.30 + 0.65 s`: war 100 → 0.98 per step (≈ 88% complete 8/8); war 60 → 0.91 (≈ 53%); war 20 → 0.85 (≈ 31%), before the slips below.
  - Press timing: on the beat with a jitter of `U(0, 0.03 + 0.06 (1 − s))` s after `CP`. Weak AIs are sometimes late; above +0.08 s that frees the defender, as for humans.
  - Wrong-button slip: `0.06 × (1 − s)` per step.
- **On block:** continue only into an L or M step, unless one of these holds:
  - (a) the foe's parry is on cooldown for longer than the time to the H hit (the cooldown is visible: v3 `foeCd`), with probability `gs`;
  - (b) **guard crush**: the run of blocked steps reaches 5 and the next step is the finisher, with probability `0.7 × gs × (foeCd > time to finisher ? 1 : 0.3)`.

  Otherwise stop at the last L step (safe, §8.4) and enter v3's "expect a counter, guard" logic (`guardUntil`).
- **Delay mix-up:** before an H step or H finisher, if the foe's hitstun-break tendency `pBreak` (§14.3) is above 0.35, delay with probability `rk × pBreak` by `U(0.06, 0.14)` s. Deliberately above 0.08 s only when the foe's parry is ready, accepting the gap.
- **Special cancel:** rage 100, current step landed, combo ≥ 3 → cancel with probability `0.6 + 0.3 s`. v3's neutral special use stays.
- **Bait (v3, generalised):** if `pBreak[k+1]` (the foe's habit of breaking at that step) > 0.4 and step k is an L step, stop after step k with probability `rk × pBreak[k+1] × 1.5` and set `baitUntil = now + 0.9` to punish the whiffed parry (v3 code path).

### 14.2 Knowledge of the opponent's string

- `this.known[1..8]`: a step's type is learned when the AI **sees** it executed (`seen` snapshot shows that step's state). Learned once, kept for the duel.
- **Battle memory:** `BattleController` passes `opts.aiKnow = Map(genName → bitmask)`. It persists for the battle, and for the whole 武林大会 tournament: generals watch each other. So a general who duels the same opponent twice in one battle starts the second duel knowing what it saw. Not saved.

### 14.3 Breaking strings

- **Hitstun break:** in hitstun from step k ≥ 2 of a foe string, with its parry ready:
  - attempt for step k+1 with `P = parryP × w(type) × (known ? 1 : 0.3) × (0.5 + 0.5 rkVs(foe))`, where `w` = L 0.25 · M 0.35 · H 0.55 · finisher 0.55;
  - unknown type → assume M timing;
  - the press is planned with v3's `planParry(T, K_type × 0.5, 0.65)` at the predicted on-beat hit time. If the foe's delay tendency `pDelay > 0.4`, aim +0.10 s later instead.
  - One decision per hitstun (v3 `stunSeq`). Since cooldown is 2.5 s, at most one break per string in practice.
- **Guardstun break:** blocking step k with step k+1 known → parry the next step with `parryP × (0.10 + 0.50 × known)`, full window.
- **Jab interrupt:** blocking step k with step k+1 = H known → press `L` at blockstun end with `punishP × 0.5`.
- **Habit memories** (EMA, as v3 `p3`):
  - `pBreak[k]`: the foe pressed parry during my string at step k;
  - `pDelay`: the foe delayed an H;
  - `pushy`: the foe continued into H on block.

  They decay at v3 rates.
- **Punish stops:** when the foe stops a string on block after an M or H step, `ctrSeq` (v3: punish a blocked big move's recovery) fires with v3's `punishP` logic using the §8.4 advantage values. A blocked finisher is always marked for punishment.

### 14.4 Guarding, height and the mixup layer

- **Guard height:** `held.down` is decided at three moments: on the threat (v3 `guardP` roll), on every guardstun, and on seeing a low or overhead startup.
  - Default: crouch-guard if `lowP` (the foe's EMA of lows used against my guard) > 0.25 and `rnd < gs`; otherwise stand.
  - On seeing an overhead (`ovh` or an air attack) with `remaining > react`: stand. On seeing a low: crouch. A 0.32 s overhead is reactable for war ≥ ~40 (react ≤ 0.30 s). Lows (0.10 / 0.16 s) mostly are not, so they are read via `lowP`.
- **Using lows / overheads:**
  - vs a foe who stand-guards a lot (v3 `turtle`), use ↓J / ↓K with `gs × turtle × 0.4` per poke decision;
  - vs a crouch-guarder (`crouchT` EMA), use `ovh` with `gs × crouchT × 0.3`;
  - sweep only at max range (it is −0.25 on block).
- **Anti-air:** v3's `aa === 'strike'` uses `cH` (↓L rising slash) instead of a jab when `dist ≤ reach × 0.95`; it beats air attacks.
- **Hold-back:** the AI keeps `held.guard` (stationary guard, §6.6). When it walks backward (`H.x = −face`) it now also auto-guards on threat, the same as players.

### 14.5 v3 → v4 mapping for the AI code

| v3 | v4 |
|---|---|
| `light1/2/3` states, `MOVES[cur.next]`, `nextChainHit` | state `str` with `f.step` and the string; `nextStepHit(sim, foe)` from the known next type (else M) and `CP` |
| `comboP` / `comboRoll` block for light1/light2 | `cont` per step (§14.1) |
| `p3` (foe parries the 3rd hit) | `pBreak[3..8]` |
| bait "stop after light2" | bait at any L step before a high-`pBreak` step |
| `mash` (foe chains without hit-confirm) | `mash` still observed on standalone repeats; plus `pushy` for strings |
| hitstun parry of light3 only | §14.3 typed windows, step ≥ 3 |
| `held.guard` | unchanged, plus `held.down` |
| anti-air jab | anti-air `cH` |

All v3 constants stay unless named here.

---

## 15. Test plan and balance targets

All rule code stays Node-testable (`Sim`, `AI`, `scriptBot`, `simulate` exported as in v3, plus `D.COMBO`, `D.GUARD`, `D.CRIT`). Each suite stays `ALL OK` and fast:

- `tests/duel-bots.js` (at N = 40) under 120 s; v3 takes 20 s;
- `tests/sim.js` under 60 s; v3 takes 20 s;
- `tests/combos.js` under 2 s.

### 15.1 New bots (`scriptBot` kinds; rules RNG via `SG.Random`; same human model as v3: reaction 0.22 ± 0.04 s, timing σ 0.045 s)

| Kind | Behaviour |
|---|---|
| `hbguard` | v3 `guard`, but blocks by holding back (`held.x = −face`) instead of `held.guard`; crouch-guards when it saw a low in the last 3 s |
| `combo` | `hbguard` that starts its own string, presses each step on the beat (σ 0.03 s), hit-confirms (continues on hit, stops at the last L step on block) |
| `breaker` | `combo` plus hitstun breaks of strings it has seen (any step ≥ 3, human σ, planned at on-beat), and guardstun parries of known next steps |
| `delay` | `combo` plus delaying H steps by U(0.06, 0.14) s when the opponent broke one of its strings in the last 10 s |
| `randmash` | presses a random J / K / L at 8–12 Hz, walks in; the "button masher" |
| `mixup` | `hbguard` that against a guarding opponent alternates ↓K sweep, K+L overhead and standing strings at random |
| v3 kinds | `jumper`, `masher` (J only), `spam`, `guard`, `parry`, `heavy`, and the file-local `turtle`, `cautious`, `turtleP`, `backstepper`: **kept**, unchanged, as regression bots |

### 15.2 duel-bots.js sections and targets

**(1)–(7): the v3 targets, kept in spirit.** Same pairs, same N:

| Check | v3 target (v3 measured, this repo, seed 7) | v4 target |
|---|---|---|
| (1) jumper / masher can't beat stronger AIs | 孙乾>吕布 ≤ 10, 刘备>吕布 ≤ 40, 关羽 mirror ≤ 75, 张飞>刘备 masher 50–100 (65) | same bounds; masher 张飞>刘备 ≥ 40 (J-only mashing now gets ≤ 2 string hits; if it stays ≥ 50, keep 50) |
| (2) masher vs equal 72–76 | avg ≤ 30, ≥ 5, worst ≤ 65 (26 / 40) | same |
| (2) weak (≤ 40) easy to mash | ≥ 80 (99) | same |
| (2) strong (≥ 90) hard but possible | 1–30 (5.0) | same |
| (3) AI vs AI | 吕布>孙乾 ≥ 95, 典韦>李典 ≥ 75, 关羽 mirror 25–75 | same |
| (3) duration | 刘备 mirror ≤ 42 s, time-outs ≤ 10% (38.4 s, 0%); 孙乾 time-outs ≤ 45% (35%) | 刘备 mirror **20–40 s**, time-outs ≤ 10%; 孙乾 time-outs ≤ 45% |
| (3) parries | 吕布 mirror ≥ 0.4 / match (1.05); 孙乾 0 tries | same |
| (4) parry rules | window 0.15–0.22 s; stagger 0.6; counter 0.7 ×1.5 unblockable; whiff 0.4; CD 2.5 | same, with the hitstun case rewritten: steps 1–2 never parryable from hitstun; step ≥ 3 with typed windows (§9.1); early → 过早; one attempt per hitstun; string stopped → 0.24 s whiff |
| (4) AI punishes whiffed parry | 吕布 ≥ 50 and > 孙乾 + 20 (90 / 53) | same |
| (5) spam / parry table | spam mid ≤ 45 and ≤ mash + 15; spam strong ≤ 30; parry ≥ guard − 5; parry strong ≤ 90; 刘备>吕布 parry ≤ 40; skilled vs weak ≥ 80 | same |
| (6) heavy reaction | tap < 0.17 s, full ≥ 0.24 s; AI full-heavy parries ≤ HEAVY_CAP + 12%; 0 unseen; AI ≥ react | same; **plus** K+L overhead startup ≥ 0.30 s real at war 100 |
| (7) defensive styles vs equal mid | guard ≤ 50 (43); parry 25–70 (44); cautious 25–65 (36); turtle ≤ 70 (54) with AI full-charge heavies ≥ 1 / match (3.4); turtleP ≤ 70 (64); backstepper ≤ 55 (33); ladders monotone, 30 → ≥ 85, 100 → ≤ 15; skilled vs 6–12 lower ≥ 65, ±6 ≥ 25 | same |

**(8) Hold-back guard.** On the MID set:

- `hbguard` vs `guard`: |Δ win| ≤ 6 pp; `hbguard` vs strong mirrors: |Δ| ≤ 6 pp.
- Proximity guard: while threatened, holding back never moves the fighter backward (scripted, Δx = 0).
- A back press on the same step as the hit blocks (scripted).
- Cross-up within 0.10 s blocks either way, after 0.10 s only the correct way (scripted).
- Air: never guarded.

**(9) Lows and overheads (scripted and vs AI).**

- Scripted: lows are blocked only crouching, overheads only standing, mids both; chip ×0.2.
- Human-latency bot stand-blocks ≥ 80% of K+L overheads seen from crouch.
- AI stand-blocks overheads: ≥ 70% at war ≥ 70, ≤ 40% at war ≤ 35.
- AI blocks the `mixup` bot's lows at 35–75%. Never more than 80% (it cannot react to a 0.10 s low); it never switches height sooner than `react` after the move began (honesty check).
- `mixup` vs equal mid AI ≤ 60%.

**(10) Strings: rules (scripted, deterministic).**

- **a** every general (499): a full on-beat string on a passive standing dummy at equal war 76 deals **28.0 ± 1.5 HP (1 H) / 29.1 ± 1.5 (2 H) / 30.2 ± 1.5 (3 H)**, averaged over 20 runs (`rangeFloat` noise).
- **b** true combo: for 30 sampled strings × attacker war {30, 100} × defender war {30, 100}, an on-beat string whose first hit lands hits 8/8 against a dummy holding back and spamming J.
- **c** late input +0.06 s still combos; +0.12 s releases (the dummy holding back blocks the next step); delayed H by 0.10 s is blocked by a dummy holding back.
- **d** mash protection: `randmash` completes ≤ 1% of the strings it starts (over 400 starts); J-only mashing never exceeds the string's leading run of L, at most 2.
- **e** wrong button ends the string and yields the standalone move only within BUFFER after recovery.
- **f** blockstrings: L/M → L/M leave no gap (a dummy pressing J at blockstun end is hit by the next step); the gap before H is 0.10 ± 0.02 s (that dummy's jab counter-hits the H).
- **g** on-block advantages within ±0.02 s of §7.1 and §8.4 at war 76; same sign at war 30 and 100.
- **h** guard crush after exactly 5 blocked steps; not after 4.
- **i** special cancel works on hit and block, not on whiff; scaled; any string + cancel ≤ 50 HP at equal war 100.
- **j** riposte → string: first hit ×1.5 unblockable, then normal scaling.
- **k** knockdown mid-string ends the string; the attacker hit mid-string ends it.

**(11) Strings: parry breaks (scripted).**

- Hitstun parries at steps 1–2 never succeed.
- At step ≥ 3 they succeed iff pressed within `parryW × K_type` before the hit.
- Early → 过早 event and cooldown spent.
- A guardstun parry of the next step uses the full window.
- After a break: `parried` 0.6 s (0.7 s vs H), counter window 0.7 s, riposte string starts.

**(12) Strings vs AI (balance).** MID = the v3 mid pairs, STRONG = 关羽, 吕布 mirrors, WEAK = 孙乾 mirror:

| Measure | Target |
|---|---|
| `combo` vs equal mid AI | ≥ `hbguard` − 5 pp and 30–65% (strings help a skilled player but don't break the AI) |
| `combo` vs STRONG | 5–40% |
| `breaker` vs equal mid AI | 35–70%; ≥ `combo` |
| `combo` vs WEAK | ≥ 85% |
| AI breaks of `combo`'s strings (after the AI has seen each step once) | war 90–100: 25–55% of full-string attempts; war 60–76: 10–35%; war ≤ 35: 0 |
| `delay` vs `combo`: AI break rate | ≥ 30% lower (relative) |
| AI string completion (8/8 on hit) per match | war 98–100 mirrors ≥ 0.5; mid mirrors ≥ 0.25; weak mirrors ≤ 0.15 |
| AI goes into an H step on block | ≤ 25% of opportunities when the foe's parry is ready |
| AI honesty | never plans a break on an unseen step at known-step accuracy (the planned time error for unknown steps ≥ 0.03 s); never parries a step before `react` after seeing the previous one |

**(13) Duel crits.**

- AI mid mirrors: crit share of unguarded hits 4–12%; crit damage share ≤ 15%.
- War 100 vs war 50: base crit ratio ≥ 1.6.
- Scripted: no crit on guarded / riposte / special / arrow; counter-hit +15% and finisher +10% are applied (measured over 2,000 forced rolls with a fixed seed: within ±1.5 pp).

**(14) Performance.** `D.simulate` of an AI 吕布 mirror ≤ 15 ms (Node). The Sim step stays allocation-free (the v3 pattern).

**(15) Items in the duel and weapon parity** (consolidation; the duel-bots section INTEGRATION §8 asks for). `--quick` runs 60 duels per cell, nightly 300:

- **Best-in-slot sets** (from `items.json`, per rarity, through `SG.Gear.duelMods`) vs ungeared, equal 武力 60 / 80 / 95: every rarity ≤ 75 % (the v3-Sim estimate with the final budget: common 55–58, uncommon 66–70, rare 60–63, epic 67–76, legendary 69–73 %; `integration/duel-gear-estimate.js`).
- **15-武力 gap**: 65 + legendary set vs 80 ungeared ≤ 30 % (estimate 23 %).
- **Abilities**: each `DUEL_ITEM_EFFECTS` ability alone, AI mirror at war 80: HP-equivalent effect (damage dealt − taken vs the same fighter without it) ≤ 4 per duel; all of one fighter's abilities together ≤ 4.
- **Whitelist**: a scripted item with any non-whitelisted duel field throws at load (`SG.Items.validate`).
- **Weapon-type parity**: each duel key vs 长剑 sword, no gear, equal 武力 80: 45–55 %. The v3 Sim today: pole weapons 39–47 % (spear and snake 39.3, guandao 41.0, ji 43.0), 刀 58.0, 双刀 56.7, 短戟 55.3, gladius **65.0** — P1 retunes reach / pow per key until all pass. **After P1, the tournament's `WIN_SCALE` is re-measured once with the v4 duel and frozen** (TOURNAMENT §10.1, INTEGRATION §8.4).

### 15.3 tests/sim.js

- **(1d)** army crits (§2.7 targets), paired off / on.
- The scripted crit unit checks of §2.7.
- (7g) and (7h) still pass with crits on.
- The (1)–(3) printouts are refreshed (random stream shift, §2.2).
- `Progress.battleExp` counts crits (+2 each, capped).

### 15.4 Browser test pages

- `tests/clash.html`: the durations of §1.5; a crit case per terrain; the 快 case.
- `tests/duel.html` (extended):
  - pick any general → **string viewer**: plays the string on a dummy, shows the frame data and the ribbon;
  - mode buttons for all dummy modes of §12.4;
  - hold-back, crouch and mixup demos;
  - crit toggle;
  - phone-size preview with the v4 touch layout;
  - `?duelauto=1` AI-vs-AI.
- `tests/figures.html`: the new clips per style (§13.1), side by side with the fallback mapping.

---

## 16. Modules, files, APIs, save data, Unity

### 16.1 Files (browser)

| File | Change |
|---|---|
| `js/combo-data.js` (new, generated) | `SG.ComboData = { version, entries }`; 499 lines, about 25 KB |
| `js/combos.js` (new) | `SG.Combos`: `of(gen)`, `all()`, `kStr(seq)`, `glyphs(seq, preset)`, `validate() → report`, `known(G, name) → bitmask`, `markSeen(G, name, stepIdx)`, `describe(gen, viewerFaction) → { seq, steps:[{type, known, dmgPct, tags}], finisher, totals, stars }` (content for the character menu), `mastery(G, name)`, `addMastery(G, name, result)` |
| `tools/gen-combos.js` (new) | from `design-v4/combat/gen-combos-proto.js`; modes `build` (first time), `append` (new generals only), `check` (CI); writes `js/combo-data.js` and `tests/refs/combo-data.v4.json` |
| `tests/combos.js` (new) | §10.6 |
| `js/duel-game.js` | Sim: v4 input, guard (§6), crouch / low / overhead, new moves (§7), strings (§8), parry generalisation (§9), crits (§3). AI (§14). PlayerInput: presets, `down`, `medium`, simultaneous K+L. `buildTouch`: 8-way pad, 轻 / 中 / 重 / ⟨中+重⟩ / 格开 / 绝技 / optional 防. HUD: ribbon, hint, crit pops, finisher overlay, seal stamps. `controlsCard` v4. `practice()`. FigWarrior clip mapping. Exports `MOVES`, `COMBO`, `GUARD`, `CRIT`, `PARRY` (v3) |
| `js/figure.js` | the clips of §13.1, `look.mirror` for left-handers |
| `js/battle-model.js` | crits (§2): `attack()` returns `crit`, `critK`; `critChance`, static `critChanceOf`; `critEnabled`; AI value |
| `js/data.js` | `Balance` crit keys; formation `crit` column |
| `js/model.js` | `Progress.battleExp` crit term; save fields of §16.3 |
| `js/clash.js` | v4 TL plus `tlMap`; fast = 2.5; `crit` option and crit beat; seal stamp variant |
| `js/battle-view.js` (add-only) | `V.critHit(u, dmg)` |
| `js/battle-controller.js` | clash speed 1 / 1.7 / 2.0; pass `crit`; `V.critHit`; battle-level `aiKnow` map to `DuelGame.play`; after a rendered duel, `SG.Combos.markSeen` for the steps the player saw (`res.seenSteps`) |
| `js/audio.js` | SFX names of §13.3 (audio task) |
| character menu (task 9) | renders `SG.Combos.describe`; opens `DuelGame.practice` |
| `index.html` | load `combo-data.js` and `combos.js` before `duel-game.js` |

`DuelGame.play(opts)` additions:

- `opts.aiKnow` (Map, mutated);
- `opts.practice` (dummy mode);
- the result `res.seenSteps = { [name]: bitmask }`;
- `res.stats[i]` gains `strings`, `stringHits`, `fullStrings`, `breaks`, `broken`, `crits`, `guardCrushes`.

`res.log` entries gain `step` (1–8 or 0), `crit`, `line`. They stay compatible with `Mdl.duelFinish` (who / dmg / hpA / hpB unchanged).

### 16.2 Sim state additions (per Fighter)

`low` (crouch-guard height), `strIdx` (0–7, −1 when not in a string), `seq` (cached string), `kStr`, `winOpen`, `lockType`, `pressAt`, `delayT`, `blockedRun`, `strHit` (any hit landed in this string), `deflectUsed`, `xSide` (last side for cross-up), `ovhPend` (simultaneous-press buffer), `crit` stats.

New states: `crouch`, `str` (with `strIdx`), `ovh`, `cL`, `cM`, `cH`, `airM`, `airH`, `thrown`. `guard` and `guardstun` reuse `low`. Standalone `L` and `M` reuse the `str` machinery with `seq = null` when they don't start a string, so that one code path does every "attack with a type".

### 16.3 Save data (save version 4, INTEGRATION §6.1; COMBAT migrates last with DOMESTIC-FX, §6.2; older saves load with empty maps)

```js
G.comboSeen    = { [genName]: 0..255 }                 // §12.2；本势力武将不存
G.comboMastery = { [genName]: { s: 0..3, done: n, onBeat: n, beatPract: n } }   // §12.4
// 设置（不入存档）：localStorage 'sanguozhi2_duel_v4' = { keys: 'v4'|'v3', guardBtn, hints, ribbon }
```

### 16.4 Unity port (after the browser build is stable)

| Unity file | Change |
|---|---|
| `Assets/Sanguo/Scripts/Battle/DuelSim.cs` | the same Sim changes; `DuelHeld` gains `down`, `medium`; `DuelTaps` gains `medium`, `ovh` |
| `DuelGame.cs`, `DuelHud.cs` | input presets, touch layout, ribbon, hint, practice |
| `DuelFigure.cs` | new clips (port of figure.js keyframes; the v3 exporter `FigureData.g.cs` is regenerated) |
| `Clash.cs` | constants `March1 … End` replaced with the v4 values plus `TlMap`; `Fast = 2.5f`; crit beat |
| `BattleModel.cs` | crit, one random draw |
| `Assets/Sanguo/Scripts/Data/ComboData.g.cs` (new) | generated from `js/combo-data.js` by `tools/gen-combos.js --cs` |
| `Tests~/duel/HarnessDuel.cs` | the v4 sections; scripted scenarios (§15.2 (8)–(11)) compared against JS refs `Tests~/refs/duel-v4.json` from `node tests/duel-bots.js --refs`, exact to 1e-9 (same RNG, same step order as v3's port) |
| `Tests~/clash`, `Tests~/sim/HarnessProgress.cs` | timings; crit stats against JS refs |

---

## 17. Phases and risks

| Phase | Content | Gate |
|---|---|---|
| P1 rules | combos data + tool + tests; Sim / AI / bots incl. section (15) items and weapon parity; army crit model (`SG.Mods.critChance`); clash TL constants and the stinger rule | duel-bots v4 (all 15 sections), sim, combos ALL OK; then `WIN_SCALE` is frozen (INTEGRATION §8.4) |
| P2 view | clips (with fallback mapping first), VFX, HUD, touch, controls card; clash crit beat; board `critHit` | DESIGN-V2 §1 budgets; no `console.error`; phone layout |
| P3 menus | 武艺 tab content, 演武, knowledge marking, settings | character-menu task integration |
| P4 Unity | §16.4 | Unity harnesses equal JS refs |

| Risk | Mitigation |
|---|---|
| AI regressions (the v3 AI is finely tuned) | the v3 bots stay as regression (§15.2 (1)–(7)); `DUEL_GAME_JS=` A/B runs (v3 harness feature) |
| Mixup layer too strong or too fiddly on touch | switch `DuelGame.MIXUPS` (default on). Off: ↓ attacks are mid, K+L becomes standalone M, crouch is visual only. Ship off if (9) or (12) cannot be met |
| Animation workload (25 clips × styles) | fallback mapping (§13.1) lets P1 / P2 ship first |
| Touch K+L | dedicated ⟨中+重⟩ target; overhead is never required (strings never need it) |
| Players miss the v3 L guard | first-run banner, `v3习惯` preset, optional 防 button |
| String knowledge feels like homework | hint on by default (§5.3); the character menu shows own strings fully; the 演武 yard and the ribbon teach by doing |

---

## 18. Open questions

- **Q1.** Default keys: J 轻 / K 中 / L 重 (recommended) or keep v3's K = heavy and put medium on L (`v3习惯` preset)?
- **Q2.** Mixups (lows, overheads, crouch-guard) on by default (recommended), or shelve for a later version (`MIXUPS = false`)?
- **Q3.** ~~Gear and title hooks~~ — resolved in the consolidation: army crit through `SG.Mods` providers (gear + title ≤ +6 %, multiplier ≤ +0.3); duel numbers through one `SG.Gear.duelMods(gen)` limited to the `DUEL_ITEM_EFFECTS` whitelist (§3) and the all-in budget (INTEGRATION §8.3: damage ≤ 3 %, parry window ≤ +0.01 s, reach ≤ +5 %). Strings stay per general, never per weapon.
- **Q4.** Should a ruler's unique weapon (taken by slaying the ruler) also teach the finisher's VFX to the new owner? Proposed: VFX only, the string stays the wielder's own.
- **Q5.** Low-武力 generals: all get 8-step strings (proposed). Alternative: war < 40 gets 6 steps (finisher at 6).
- **Q6.** Should AI knowledge persist across battles in the save (AI "studies" famous generals)? Proposed: no; per battle and per tournament only.
- **Q7.** Enemy-turn clash speed 1.7 and 委任 2.0: do they feel right with the slower clash? Playtest.
- **Q8.** Gamepad (Gamepad API) mapping as a follow-up (□ 轻, △ 中, ○ 重, R1 格开, R2 绝技, stick back = guard)?
- **Q9.** ~~Rule-changing title perks in duels~~ — resolved: none (TITLES §7.5); titles add only crit and damage inside the duel budget.

---

## 19. Sources

Game-design references (mechanics borrowed in spirit):

- Samurai Shodown (2019): light / medium / heavy slash, M+H universal overhead, just defense, deflect, rage. [Kotaku](https://kotaku.com/samurai-shodown-is-slow-and-deliberate-in-the-best-way-1835805404), [Game Informer review](https://www.gameinformer.com/index.php/review/samurai-shodown/an-intense-return-with-rough-edges), [EventHubs review](https://eventhubs.com/news/2019/jun/24/samurai-shodown-review-neutral-focused-fighting-game-s-basic-yet-brimming-depth), SNK web manual (cited by search: snk-corp.co.jp/webmanual/samuraispirits/eng/howtoplay2.php).
- Killer Instinct combo breakers (strength-matched breaks, heavy steps easier to break, lockout). [Prima KI eGuide: combo breaker system](https://primagames.com/eguides/killer-instinct-eguide/training/combo-breaker-system), [Prima: how to play](https://primagames.com/featured/killer-instinct-how-play-basics), [Infil KI guide: counter breakers](https://ki.infil.net/cbreaker2.html).
- Damage scaling: [Steam SF6 discussion](https://steamcommunity.com/app/1364780/discussions/0/3810659147979043213), [SuperJump: how damage scaling holds fighting games together](https://superjumpmagazine.com/how-damage-scaling-holds-fighting-games-together).

Historical and legendary references for §11:

- 《三国志·关羽传》 "策马刺良于万众之中": confirmed via [NCU OCW course notes](https://ocw.ncu.edu.tw/filedownload/4135) and [网易](https://c.m.163.com/news/a/KVBC5CVF05568TH9.html).
- 典韦's double halberds (军中语): [萌娘百科 典韦](https://mzh.moegirl.org.cn/%E5%85%B8%E9%9F%A6) (secondary; verify the wording against the 点校本).
- Other 《三国志》 and 裴注 passages in §11 are quoted from the standard text. Web lookups (zh.wikisource, ctext) were unreachable from this session. The integration task must verify the quotations against the 中华书局点校本 before they appear in the UI.
- Commodus fought as a left-handed *secutor* (Cassius Dio 73): [Wikipedia: Secutor](https://en.wikipedia.org/wiki/Secutor), [Imperium Romanum: Gladiator Commodus](https://imperiumromanum.pl/en/curiosities/gladiator-commodus/amp/), [Encyclopaedia Romana: Thraex](https://penelope.uchicago.edu/~grout/Encyclopaedia_romana/gladiators/thraex.html).
- Roman thrust doctrine (Vegetius I.12): [Encyclopaedia Romana](https://penelope.uchicago.edu/encyclopaedia_romana/gladiators/death.html), [archive.org De Re Militari](https://archive.org/stream/pdfy-sOkC3FmoLlr4C6zz/The+Military+Institutions+Of+The+Romans+%5BDe+Re+Militari%5D_djvu.txt).
- Sarmatian two-handed long weapons (Tacitus, *Histories* 1.79): [Hungaricana, Istvánovits](https://library.hungaricana.hu/hu/view/MEGY_SZSZ_Jamk_47_2001/?pg=156), [Wikipedia: Roxolani](https://en.wikipedia.com/wiki/Roxolani).
- Ardashir I unhorses Artabanus IV (Firuzabad relief, Hormozdgan 224): [Livius: Firuzabad relief I](https://livius.org/articles/place/ardasir-khureh-firuzabad/firuzabad-relief-i), [World History: Ardashir unseats Artabanus](https://www.worldhistory.org/image/15054/ardashir-i-unseats-artabanus-firuzabad-relief/), [Wikipedia: Battle of Hormozdgan](https://en.wikipedia.org/wiki/Battle_of_Hormozdgan).
- This repo's research notes (v2 and v3), for the foreign generals' biographies and liberties: `research/europe.md` (马克西明, 纳尔奇苏斯, 巴洛马尔, 赞提库斯, 康茂德), `research/v3/rome.md` (芬恩, legendary), `research/v3/india-kushan.md` (迦梨迦罗, 内敦切利扬), `research/southeast-asia.md` (范师蔓), `research/east-asia.md` (故国川王), `research/v3/east-steppe-sea.md` (神功皇后).

Measured in this repo (session of this design): `node tests/duel-bots.js 40 7` (95 checks, ALL OK, v3 numbers quoted in §15.2) and `node tests/sim.js` (ALL OK, v3 battle numbers quoted in §2.7). Prototype and estimate scripts are in `design-v4/combat/`.
