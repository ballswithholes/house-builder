# 三国志-霸王的世界 · 第四版装备与商店设计（ITEMS-V4）

Player request 3 of v4: shops in every city that sell weapons, armour and other equipment for generals; more than 250 weapons and 250 pieces of armour / equipment; standard items in many cities, good ones in a minority, very rare ones in a few cities of the world; unique weapons that can only be taken by slaying the ruler of each power; historical and mythological research; unique, non-learnable abilities with epic looks and effects; rarities common / uncommon / rare / epic / legendary with exactly 4 legendary and 20 epic. This spec also covers the item side of request 9 (character menu: gear), request 13 (non-battle rewards) and request 14 (武林大会 prizes).

Read `DESIGN-V2.md` §1 (common rules), `DESIGN-V3.md`, `CONTRACT.md`, and the sibling v4 specs `COMBAT-V4.md` (crits, duel caps) and `TITLES.md` / `titles.json` (官爵) first. Where this file conflicts with them, this file wins for the systems it owns (items, shops, gear stats, acquisition). Platform order: browser first, then the Unity port (§18). All visuals assume the v4 art direction from the art pilot (Lanternvale: stylized low-poly, cozy Ghibli-warm, flat-shaded props, soft-lump foliage with a warm-top / cool-teal ramp, ink outlines, soft painted light with warm lantern pools at night). Effects are brush strokes, ink splashes, warm gold and vermillion seal stamps, never neon.

**The catalogue is data, not prose.** `design-v4/items.json` holds every item (567 entries) with id, name, original-language name, slot, type, culture, rarity, stats, price, availability for both scenarios, source note and references, model / icon / look description, and for rare+ items the unique ability with mechanics, parameters and effect description. It also holds the shop table for all 38 classic and 196 world cities, the ruler-unique table for all 18 / 70 factions, the signature, hidden, city-treasure, armoury and tournament tables, the stat dictionary, the duel weapon table and the starting kits. The tables in §9–§12 below are generated from it (`design-v4/items/gen-md.js`), so the two cannot disagree. The generator and its validation are in `design-v4/items/` (`build.js`, tables in `core.js`, `commons*.js`, `specials-*.js`, `statkeys.js`, `orig.js`; see its README); the implementation ports them to `tools/gen-items.js` (§1).

---

## 0. Key numbers at a glance

| Item | Value |
|---|---|
| Catalogue | **567 items: 275 weapons** (211 main-hand + 64 off-hand bows / crossbows / throwing weapons) and **292 armour / equipment** (51 armour, 40 helmets and caps, 19 shields, 44 mounts, 138 treasures) |
| Rarity | 普通 common 250 · 精良 uncommon 121 · 稀有 rare 172 · 史诗 epic **20** · 传说 legendary **4** |
| Cultures | all 18 culture codes have commons and uncommons; all 18 are represented among the 24 epic + legendary items |
| Abilities | 196 unique, non-learnable abilities (every rare, epic and legendary item); 90 effect ids (§7); 29 items carry a once-per-battle 宝具 active (天符印 offers three) |
| Slots | 兵器 weapon · 副手 off-hand (shield, or bow / crossbow / throwing weapon) · 铠甲 armour · 头盔 helmet / cap · 坐骑 mount · 宝物 treasure |
| Base prices | common 50–100 gold · uncommon 190–400 · rare 840–1750 (的卢 700, cursed) · epic 8000 (大宛天马, the only epic in a shop) · legendary never sold |
| Economy | median city income 56 gold / month (classic 58), median faction at start 65–67 / month and 600 gold. A common = 1–2 months of one city; uncommon ≈ 6 months; rare ≈ 2 years of one median city or 5 months of a 5-city realm |
| Shops | 4 tiers by 产业 + 町 and trade hubs (村市 4 slots, 市集 6, 大市 8, 都会 10). World: 40 / 96 / 47 / 13 cities per tier; classic: 8 / 18 / 9 / 3 |
| Spread | a common is stocked in 32 world cities on average (17%; all cities of its culture plus nearby trade cities); an uncommon in 7 (3.5%, ~35% of its culture's markets); a rare shop item in 1–3 cities world-wide (55 world cities carry at least one rare); one epic in one city |
| Restock | monthly, seeded (no `SG.Random` draw); a given rare reappears in its city about every 6–10 months; sold rares cool down 24 months |
| Ruler-uniques | 70 bound weapons (classic 18): 60 rare, 9 epic, 1 legendary (赫拉克勒斯之棍, 康茂德). Taken only by slaying the holder (§8.2) |
| Signature gear | 39 items held at the start by famous generals (青龙偃月刀 关羽, 方天画戟 and 赤兔 吕布, 丈八蛇矛 张飞, 乌戈藤甲 兀突骨 …) |
| Other routes | 17 hidden items found by 搜索 (传国玉玺 in 洛阳 from 191-01), 4 city treasures, 2 starting-armoury items, 12 tournament-only prizes + 金刚杵 (3 titles) |
| Army effect of a full set (weapon + armour + helmet + shield + horse) | exchange ratio common ×1.15, uncommon ×1.28, rare ×1.43, epic ×1.60, legendary ×1.83 (= veteran levels 3 / 4 / 6 / 8 / 10). Caps: 攻击 +40%, 防御 +45% |
| Duel effect | gear 武力 counts half in duels (cap +6); gear crit ≤ +5%, damage ≤ +4%, reach ≤ +5%, parry ≤ +0.02 s, HP ≤ +15 (COMBAT-V4 Q3). Estimated equal-war win rate: rare weapon 55–60%, epic 60–64%, legendary 67–72%; gear never turns a 15-war gap into better than ~27% |
| Gear crit (COMBAT-V4 hooks) | `SG.Gear.stat(gen, 'critArmy')` ≤ 0.04, `'critDuel'` ≤ 0.05, `'critMulArmy'` / `'critMulDuel'` ≤ 0.30 (titles add the rest of COMBAT's 0.06 / 0.08) |
| Save | one save-version bump shared with the other v4 specs (integration owns the number); ~1 500 item instances, +60–90 KB |

---

## 1. Files, modules and ownership

| File | Status | Content |
|---|---|---|
| `design-v4/items.json` | new (this task) | the catalogue and all tables; single source of truth |
| `tools/gen-items.js` | new | port of `design-v4/items/build.js` + `gen-md.js`: modes `build` (rewrite items.json from tables), `check` (CI validation, §17.1), `js` (writes `js/items-data.js`), `cs` (writes Unity `ItemsData.g.cs`), `md` (refreshes the generated tables in this file) |
| `js/items-data.js` | new, generated | `SG.ItemsData = { version, rarities, slots, types, statKeys, duelWeapons, cultures, items, shops, classic, world, kits, fig2type, styleFallback }` (≈ 880 KB minified JSON; loads after `world-data.js`). Load lazily if the 8 ms rule is hit: parse in an idle callback during the title screen |
| `js/items.js` | new | `SG.Items` (catalogue, instances, shops, acquisition, AI) and `SG.Gear` (per-general stats and hooks). Pure logic: no `THREE`, no DOM; Node-testable like `model.js` |
| `js/item-art.js` | new | icons (SVG → canvas cache), figure look overrides, bespoke model parts, fx registry (`SG.ItemArt`) |
| `js/shop-screen.js` | new | 市场 screen, 府库 screen, gift and compare dialogs, purchase / reveal animations |
| `tests/items.js` | new | Node: catalogue validation, rules, economy and AI runs (§17) |
| `tests/items.html` | new | gallery of all 567 icons and models, fx demo for the 29 宝具, phone layout check |
| `tests/shop.html` | new | shop / armoury / compare UI against a scripted state |
| `tools/export-unity-items.js` | new | Unity export (§18) |
| `js/model.js` | modified | save fields (§15), `Commands` hooks (§3.4), `Progress.gain` exp hook |
| `js/battle-model.js` | modified | stat hooks (§3.2), ability hooks (§7), 阵斩 roll (§8.2) |
| `js/specials.js` | modified | `known(gen)` appends item actives with `item: id` (§7.3) |
| `js/duel-game.js` | modified | `Fighter` reads `SG.Gear.duelMods` and the equipped weapon (§3.3), duel ability hooks, new duel keys `fan* staff* club* vajra*` |
| `js/strategy-ai.js` | modified | `endMonth` restock / auras / income hooks, AI buy + equip (§13), spoils in `execute`, city-treasure transfer in `apply` |
| `js/strategy-screen.js` | modified | 「市场」 command, 缴械释放 captive option, 缴获 prompt, gift through 赏赐 |
| `js/figure.js` | modified | new weapon ids (`ge club hammer bian ankusha vajra falx sword7 crossbow javelins`), new shields, `look` overrides from items (§5.2), camel / elephant / chariot mounts (art pilot coordination) |
| `js/battle-view.js` (add-only), `js/clash.js` | modified | item fx styles (§5.4), mounted / armoured look in the clash |
| `index.html` | modified | load `items-data.js`, `items.js` after `model.js`; `item-art.js`, `shop-screen.js` after `figure.js` |

Parallel work rule (DESIGN-V2 §3) still holds: each task edits only its own files and lists the rest under INTEGRATION.

---

## 2. Slots and equip rules

### 2.1 The six slots

| Slot | Counts as | Holds | Example |
|---|---|---|---|
| 兵器 weapon | weapon | one main weapon; decides the duel style (pole / one / dual / fan) and the figure's weapon model | 环首铁刀, 青龙偃月刀 |
| 副手 off-hand | weapon (bow / crossbow / throw) or gear (shield) | a shield **only with a one-handed weapon**; or a bow, crossbow or throwing weapon with any weapon (carried on the back / hip, used for the ranged mechanics) | 钩镶, 罗马大盾, 角弓, 臂张弩, 重标枪 |
| 铠甲 armour | gear | body armour, robes, rattan, hide / body paint | 铁札甲, 环片甲, 鹤氅, 菘蓝纹身 |
| 头盔 helmet | gear | helmets, crested helmets, face-mask helmets, scholars' caps, crowns | 铁兜鍪, 鹖冠, 纶巾, 进贤冠, 贵霜王冠 |
| 坐骑 mount | gear | horses, armoured horses, ponies, camels, elephants, chariots | 西凉大马, 果下马, 单峰驼, 天竺战象, 凯尔特战车 |
| 宝物 treasure | gear | military books, classics, seals, tools, banners, drums, instruments, jewels, medicine, amulets, relics | 六韬竹简, 氾胜之书, 铜印黑绶, 水排, 牙旗, 七弦琴, 合浦明珠 |

Only one item per slot. A general may hold at most one bound item (ruler-unique); this is automatic because a faction has one ruler.

### 2.2 Requirements and constraints

- **Shield**: `req.weaponStyle = 'one'`. Equipping a pole / dual / fan weapon unequips a shield into the armoury (UI asks). 天降神盾 follows the same rule.
- **Attribute requirements** (`req` in items.json): heavy weapons (大斧, 斩马刀, 镰形刀) 武力 50 / 55 / 60 / 65 / 70 by rarity; military books and fans 智力 40–70; classics 政治 40–70. Specials may set their own: 赤兔 武力 85 (`below: 'half'` — below the requirement every number is halved and the escape becomes 50%), 埃塞俄比亚王之弓 武力 80 (half), 射日神弓 武力 60 (cannot equip), 太平要术 智力 70, 传国玉玺 `ruler: true` (a non-ruler holder gets only 政治 +10).
- Requirements use **base** attributes, so gear cannot bootstrap gear.
- An item whose requirement fails while equipped (e.g. a loyalty-driven reassignment) gives no stats and shows a red lock.

### 2.3 Where an item instance is

Every item instance has exactly one location:

| Location | Notes |
|---|---|
| a general's slot | follows the general (move, sortie, capture, defection) |
| the faction armoury 府库 | global per faction (no transport needed), capacity 40; overflow must be sold or given away |
| a city shop's stock | owned by the market, priced per city |
| hidden in a city | waits for 搜索 (§8.5) |
| a city's vault (city treasure) | belongs to whoever owns the city (§8.6) |
| a tournament pool | not instantiated until awarded (§8.9) |

Commons and uncommons have unlimited instances (a shop may restock them). Rare shop items have at most one instance per listed city at a time. Every rare from another route, every epic and every legendary is a singleton: one instance in the world, ever. If a singleton is destroyed (it never is in v4), it does not respawn.

### 2.4 Base and effective attributes

```
gearSum(gen, k)   = Σ stats[k] over equipped items whose req is met
eff(gen, 'war')   = min(110, gen.war   + min(15, gearSum(gen, 'war')))
eff(gen, 'intel') = min(110, gen.intel + min(15, gearSum(gen, 'intel')))
eff(gen, 'pol')   = min(110, gen.pol   + min(15, gearSum(gen, 'pol')))
duelWar(gen)      = min(110, gen.war   + min(6, floor(gearSum(gen, 'war') / 2)))
```

| Uses **effective** attributes | Uses **base** attributes |
|---|---|
| battle `atkPower`, special and tactic scaling (`statOf`), tactic success, duel acceptance (`duelAccepts`), formation requirements, AI battle choices; domestic commands (开发 政治, 搜索 / 登用 / 离间 / 劝诱 智力, 训练 武力) | titles (TITLES.md requirements > 80 / > 90), `SG.maxTroops`, `Progress` slots and tiers, item requirements, AI personality, the duel formula (`duelWar` instead) |

The duel uses `duelWar` because gear 武力 at full weight dominates the fight: a throw-away estimate (`est/duel-estimate.js`, 200–300 AI-vs-AI duels per cell, gear modelled as extra 武力) gave +5 → 59–70% wins and +12 → 80–88%. At half weight, +2 / +4 / +6 give 53–60% / 60–62% / 67–72%, and a 65-war general with +6 still wins only 27% against an 80. The general stays the fighter; the weapon is the edge.

### 2.5 Stacking and caps

Per general, over all six slots (items.json `statKeys[k].cap`):

| Key | Cap | Key | Cap | Key | Cap |
|---|---|---|---|---|---|
| 武力 / 智力 / 政治 | +15 each | 攻击 atk | +40% | 防御 def | +45% |
| 会心率 crit | army +4 pp, duel +5 pp | 会心伤害 critDmg | +30% | 抗会心 critRes | 5 pp |
| 反击 counter | +40% | 压制 counterGuard | 35% | 先手 first | +40% |
| 冲锋 charge | +40% | 破甲 armorBreak | 25% | 士气 morale | +25 |
| 威慑 awe | 15 (side max) | 机动 mob | −1 … +2 | 脱身 escape | max of items, 100 |
| 计策 tac | +40% | 计策命中 tacHit | +12 pp | 体力 dHp | +15 |
| 单挑伤害 dDmg | +4% | 出招速度 dSpd | −6 … +4% | 格挡 dGuard | +8 pp (88%) |
| 格开窗口 dParry | +0.02 s | 破防 dGuardBreak | 12 pp | 判定距离 dReach | +0.10 m |
| 人望 fame | +20 | 德 virtue | +12 | 外交 / 登用 / 离间 / 劝诱 | +25 / +20 / +20 / +20 pp |
| 开发 land / ind / town | +60% | 征兵 recruit | +40% | 训练 train | +10 |

Abilities stack across different effect ids. Two items with the same effect id on one general: take the larger value, except `cmd` (different commands stack) and `aura` (auras from different units stack only if their keys differ; same key from several sources on one target: largest only).

### 2.6 Reassigning gear

- Free inside a faction (character menu, armoury).
- Taking an item away from a general who is not the ruler costs loyalty: common 0, uncommon −1, rare −3, epic −6, legendary −10. Taking a **signature** item from its original holder (关羽 and 青龙偃月刀) costs −15 and he refuses if his loyalty would drop below 50 (「此刀随我征战多年，恕难从命」).
- Giving an item is a gift (§6.6) and raises loyalty once per item per general.
- Bound ruler-uniques cannot be moved at all (§8.3).

---

## 3. Stats and formulas

### 3.1 Stat dictionary

items.json `statKeys` is the dictionary (Chinese name, unit, scope, cap, note). Units in items.json are integers: `%` (percent), `pp` (percentage points), `+` (flat), `s`, `m`. `SG.Gear.stat(gen, key)` returns the capped total; for the COMBAT-V4 keys it returns fractions:

| COMBAT-V4 key | from items | value |
|---|---|---|
| `critArmy` | Σ `crit` + conditional `crit` abilities that apply now | `min(0.04, Σ / 100)` |
| `critDuel` | Σ `crit` + `dCrit` abilities | `min(0.05, Σ / 100)` |
| `critMulArmy`, `critMulDuel` | Σ `critDmg` (+ `dCrit.critDmg` for duel, + `outnumbered.critDmg` when active) | `min(0.30, Σ / 100)` |

The TITLES spec adds its own `critArmy` / `critDuel` / `dmgDuel`; COMBAT clamps the sums (0.06 / 0.08 / 0.06). This spec takes 0.04 / 0.05 / 0.04 of those budgets; titles keep at least 0.02 / 0.03 / 0.02 (open question Q2).

### 3.2 Army battle (`battle-model.js`)

All hooks are `SG.Gear?.…` so the module runs without items. `Items.enabled = false` (test switch like `Progress.enabled`) turns every hook into the identity, and draws no extra random numbers, so `tests/sim.js` can pair runs.

```
atkPower(u)  = v3 formula with u.gen.war → eff(u.gen,'war')
             × (1 + min(40, gearSum(u.gen,'atk')) / 100)
defFactor(u) = v3 formula × (1 + min(45, gearSum(u.gen,'def')) / 100)

attack(a, t):                                    // v3 + COMBAT crit, then:
  k  = 1
  k *= 1 + charge(a)/100        if a moved ≥ 2 tiles in this action (BUnit.movedThisAct, set by move())
  k *= 1 + first(a)/100         if a.attacksThisBattle === 0
  k *= 1 + vsCommander(a)       if t.commander
  k *= 1 + siege(a)             if t stands on Gate / Castle
  k *= 1 + outnumbered(a).atk   if alive(enemy).length > alive(a.side).length
  k *= 1 + selfAtk(a)
  k *= 1 + focus bonus          if t was marked by an ally's 鸣镝 today
  defT    = defFactor(t) × (1 − min(25, armorBreak(a) + 100 × sunderAll(a)) / 100)
  dmg     = round(atkPower(a) × k × DamageK / defT × rangeFloat(0.85, 1.15)) × (1 + dmgTaken(t).attack)
            (COMBAT-V4 crit roll first, critK applied as in COMBAT-V4 §2)
  counter = v3 counter × (1 + counter(t)/100) × (1 − min(35, counterGuard(a))/100) × (1 + dmgTaken(a).counter)
            (no counter at all if a has noCounter)
  splash: other enemies adjacent to t take dmg × splash(a) (never rout-checked twice; no counter)
  procs (§7) after damage, in item order, each one SG.Random draw only if the item has that proc
```

| Stat | Hook |
|---|---|
| `crit`, `critDmg`, `critRes` | COMBAT-V4 §2: `p(a) += SG.Gear.stat(a.gen,'critArmy')`; `critK += critMulArmy`; new term `p(a) −= critRes(t)/100` (ask COMBAT to add it; clamp ≥ 0) |
| `mob`, `ease`, `hard` | `BUnit.mobility += clamp(Σ mob, −1, 2)`; `distances()` uses cost 1 for terrains in the unit's `ease` set and cost +1 for `hard` |
| `morale` | `BattleModel` constructor: start morale `70 + min(25, Σ morale)` (cap 100) |
| `awe` | at battle start, every enemy unit loses `max(awe of my side)` morale (side max, not sum) |
| `escape` | capture rolls (`Conquest.apply`, battle-controller captive step): `p × (1 − max(escape)/100)` |
| `tac`, `tacHit` | tactic / intel-special damage `× (1 + tac/100)`; success chance `+ tacHit/100` (cap 0.95) |
| `fireWeak` | fire damage (burning tiles, 火计, blaze specials) `× (1 + fireWeak/100)` |
| `antiHorse` | adjacent enemy units with a horse / warhorse / pony / chariot: `atkPower × (1 − antiHorse/100)` |
| `heal` | `settleProgress`: `gen.troops += round(lost × min(30, heal)/100)` (cap maxTroops), shown in the result card |
| `Conquest.autoResolve` | `power(gen)` uses eff war and `× (1 + (min(40,atk) + min(45,def)) / 250)`; abilities do not apply in auto-resolve except `defendCity` and `castle` (as defence ×) |

### 3.3 Duel (`duel-game.js`)

`SG.Gear.duelMods(gen) → { war, reachK, dmg, parry, hp, guard, guardBreak, spd, move, critDuel, critMulDuel, weapon, abilities }`:

```
Fighter(idx, gen, look):
  war      = duelWar(gen)                                  // §2.4
  weapon   = equipped weapon's items.json duel record { key, style, reach, pow }  (else v3 lookOf)
  reach    = WEAPONS[key].reach × (1 + min(0.05, reachK))  // dReach m → reachK = dReach / base reach
  pow      = WEAPONS[key].pow   × (1 + min(0.04, dDmg/100))
  hp       = 100 + min(15, Σ dHp)
  spd      = actSpeed(war) × STYLE_SPD[style] × (1 + clamp(Σ dSpd, −6, 4)/100)
  walk     = walkSpeed(war) × (1 + min(10, Σ dMove)/100)
  guardRed = min(0.88, 0.80 + Σ dGuard/100)                 // 天降神盾 0.90
  parryWin = parryWindow(war) + min(0.02, Σ dParry)
  vs guard: the defender's guardRed − min(0.12, attacker Σ dGuardBreak/100), floor 0.5
```

The duel weapon record comes from the equipped weapon (`items.json items[].duel`), so a general who picks up 方天画戟 fights with a ji. New duel keys (add to `WEAPONS` in duel-game.js): `fan*` (one, reach 1.55, pow 0.85), `staff*` (pole, 1.95, 0.88), `club*` (one, 1.78, 1.08), `vajra*` (one, 1.60, 1.05). Combo strings stay per general (COMBAT-V4 §10); the weapon only changes reach, timing style and finisher VFX (COMBAT Q4: the new owner of a slain ruler's weapon keeps his own string).

Duel ability hooks are listed in §7.4. Their budgets: a rare duel ability is worth ≤ 4 HP per duel on average, epic ≤ 8, legendary ≤ 12 (measured in §17.3).

### 3.4 Domestic commands (`model.js` `Commands`)

Gear of the **general who executes** the command counts (the general picked in the command dialog); city-wide effects (`income`, `harvest`, `pop`, `loyalAura`, `defendCity`) use the city's 太守 (governor), or the ruler when he sits in the city.

| Command | v3 | v4 |
|---|---|---|
| 开发 develop | `gain = DevelopBase + pol/6 + R(0,4)` | `round(gain(eff pol) × (1 + min(60, kindPct)/100))`, kindPct = `land` / `ind` / `town` stat + `cmd develop kind` abilities |
| 征兵 recruit | `TroopsPerGold` 5 | `5 × (1 + min(40, recruit)/100)` troops per gold (also in `recruitMax`) |
| 训练 train | `TrainBase + war/10 + R(0,4)` | uses eff war, `+ min(10, train)` |
| 搜索 search | `p = 0.22 + intel/260 + virtue/420` | eff intel, `+ search/100`; then the hidden-item roll (§8.5) |
| 登用 hire | `hireChance(target, recruiter, f)` | eff intel, `+ hire/100` of the recruiter, `+ hire` abilities; cap 0.95 |
| 赏赐 reward | `+8 + R(0,4)` loyalty for 100 gold | `+ reward` of the ruler's gear; item gifts §6.6 |
| 交易 trade | food price | buy `× (1 − t)`, sell `× (1 + t)`, `t = min(0.25, trade of the city's best general / 100)` |
| 外交 ally | `allyChance(f, target, gift)` | `+ max(dip of ruler, dip of envoy) / 100`, `+ dipPlus` abilities; alliance length `+ months` |
| 离间 / 劝诱 | `discord`, `persuadeChance` | eff intel and `+ discord / persuade` pp |
| 移动 / 输送 / 任命 / 出征 | — | no stat hooks (gear moves with the general; 出征 shows the army's gear summary) |

`Progress.gain(gen, amount)`: `amount × (1 + min(40, exp)/100)`, plus `expBoost` abilities.

### 3.5 Faction-level effects

- **人望 fame / 德 virtue**: `effFame(f) = fame + min(20, Σ fame of the ruler's gear)`; same for virtue (+12). Every v3 use of `faction.fame` / `virtue` (action points `2 + fame/20`, 搜索, 登用, 外交, 出奔) reads the effective value through `SG.Items.fame(f)` / `virtue(f)`. The stored fields never change.
- **Monthly** (`StrategyAI.endMonth`, after income): `income` and `harvest` multiply the governed city's gold / harvest; `loyalAura` raises loyalty of generals in the holder's city (or faction-wide) up to its cap; `expMonthly`; `talent` (chance to reveal one hidden general in the faction's cities); `omen` (的卢 rumours); restock (§6.4); AI shopping (§13).
- **AI attitudes**: `aggro` multiplies other AIs' target score against the holder's faction (传国玉玺 ×1.25, 金错刀 ×1.15); `deterrence` (埃塞俄比亚王之弓) multiplies it by 0.85 for AIs without a 武力 ≥ 90 general.

### 3.6 Worked examples

- **关羽** (武力 98) with 青龙偃月刀 + 铁札甲 + 铁兜鍪 + 汉家军马: eff war 106 (98 + 8), duelWar 102. atkPower × (0.5 + 106/200)/(0.5 + 98/200) × 1.14 = × 1.186; defFactor × 1.06; charge +3% after moving. critArmy +0.03, critMulArmy 0. Duel: reach 2.0, pow 1.05 × 1.04, HP 104, crit +3%, special 「青龙出海」 in the 宝具 menu, crescent wave on full-charge heavies.
- **诸葛亮** (智力 100) with 白羽扇 + 鹤氅 + 纶巾 + 孙子兵法竹简: eff intel 110 (capped), tac +8 +7 +10 = +25%, tacHit +2 +4 = +6 pp, allies within 3 tiles +5% atk/def, new-tactic cooldowns −1 day, 离间 +8 pp.
- **A 3-city player in 192** buying a 百辟刀 (1400 base) in 许昌 (cityK 0.95 → 1330 gold): about 7 months of all three cities' income — a real decision against 开发 (50 gold each).

---

## 4. Rarity, budgets, prices

### 4.1 Tiers

| Tier | UI name | Frame / colour | Base price | Sell | Ability | Instances | Found |
|---|---|---|---|---|---|---|---|
| 普通 common | 凡品 | slate `#9aa3ad` | 80 × type | 40% | — | unlimited | every city of its culture + nearby trade cities |
| 精良 uncommon | 良品 | jade `#5fae6e`, faint jade rim light | 320 × type | 40% | — | unlimited | ~35% of its culture's markets (tier ≥ 2) + some metropolises |
| 稀有 rare | 珍品 | lapis `#4a8fdc`, soft glow | 1400 × type | 50% | one unique | 1 per listed city (shop) or singleton | 1–3 cities world-wide, or a ruler, a famous general, a hidden spot, a tournament |
| 史诗 epic | 神品 | amethyst `#a464d8`, strong glow, idle particles | 6000–9000 value | not sold | spectacular multi-part | singleton | rulers, legends, hidden spots, one city treasure, one shop |
| 传说 legendary | 天宝 | gold-cinnabar `#f0a52a`, full-screen reveal | value 20000 | not sold | world-changing multi-part | singleton | 赤兔, 传国玉玺, 金刚杵, 赫拉克勒斯之棍 |

Type price factor: weapon 1.0, armour 1.15, helmet 0.6, shield 0.7, bow / crossbow 0.85, throwing 0.6, mount 1.25, treasure 1.0.

### 4.2 Stat budgets

Each type has a profile per tier (items.json `types[t].profile`, arrays [common, uncommon, rare, epic, legendary]); commons and uncommons use the profile plus small per-item tweaks; rare+ items override with hand-set numbers. Representative profiles:

| Type | Key stats (common → legendary) |
|---|---|
| 枪矛 spear | 武力 1/3/5/7/10 · 攻击 3/6/9/12/16% · 会心 0/1/1/2/3 · 单挑伤害 0/1/2/3/4% |
| 长柄刀 poleblade | 武力 1–10 · 攻击 4–17% · 会心 1/1/2/2/3 · 出招 −2…0% |
| 大斧 axe | 攻击 5–18% · 破甲 3–12% · 出招 −4…−2% · 武力要求 50–70 |
| 剑 sword | 武力 1/2/4/6/9 · 攻击 2–14% · 会心 1–3 · 格开窗口 0–0.02 s |
| 锤 / 棍 / 鞭 mace / club / bian | 攻击 3–17% · 破甲 3–14% · 破防 3–12 pp |
| 骑矛 lance | 冲锋 5/8/11/14/18% |
| 弓 / 长弓 / 弩 | 压制 6–22% · 先手 2–18% |
| 投掷 throw | 先手 6/10/14/18/24% |
| 盾 / 大盾 / 小盾 | 防御 2–16% · 格挡 1–7 pp (小盾 格开 +0.01–0.02 s; 大盾 出招 −2…0%) |
| 札甲 / 鳞甲 / 锁子甲 / 环片甲 / 胸甲 | 防御 4–19% · 体力 3–11 (锁子甲 抗会心 1–3) |
| 具装 cataphract | 防御 7–20% · 冲锋 3–12% · 机动 −1 · 出招 −4…−2% |
| 盔 / 缨盔 / 面甲 | 防御 2–9% · 体力 1–5 · 士气 / 威慑 2–9 |
| 巾帻 / 冠 / 王冠 | 智力 or 政治 1/2/3/5/7; 王冠 人望 0–5 |
| 战马 horse | 机动 0/0/1/1/2 · 冲锋 3–12% · 步速 2–10% · 脱身 10/20/35/50/70% |
| 兵书 / 典籍 / 印绶 / 器械 | 智力 / 政治 1–8 · 计策 3–16% · 开发 5–30% (器械 8–35%) · 外交 3–18 pp · 登用 2–12 pp |
| 旗 / 鼓 / 乐器 / 珍宝 / 医药 / 护符 / 圣物 | 士气 3–15 · 威慑 1–6 · 安抚 0–2 / month · 馈赠 ×1.0–2.0 · 救护 3–15% · 历练 3–16% |

The full-set exchange ratios in §0 come from the spear + 札甲 + 盔 + 盾 + 战马 profiles at war 80 with half of the attacks charging.

### 4.3 Prices, sell values and value

```
shopPrice(item, city, buyer) = round10( item.price × cityK(city) × originK × relK × (1 − disc) )
  cityK   = clamp(1.2 − (industry + town)/2500, 0.85, 1.15)   // big markets are cheaper
  originK = 1.0 native item · 1.2 foreign (neighbour / classic trade city) · 1.5 caravan (§6.5)
  relK    = 1.0 own city · 1.15 allied city (§6.1)
  disc    = min(0.125, trade stat of the best general in the city / 200)
sellPrice(item, city) = round10( (item.price ?? item.value) × sellK × cityK(city) )   // sellK 0.4 / 0.5
```

Epic, legendary, bound (ruler-unique) and signature items cannot be sold (「此物不可变卖」). Tournament rares sell at 50% of `value`. `value` (items.json) is the nominal worth of unsold items, used for gifts, AI scoring and the shop's sell offers.

### 4.4 Calibration against the economy

City gold income is `产业 × 0.12 + 町 × 0.05` per month, minus 2 per general (`Balance`). From the scenario data:

| | min | p25 | median | p75 | max |
|---|---|---|---|---|---|
| city income / month, classic | 31 | 44 | 58 | 73 | 116 |
| city income / month, world | 31 | 44 | 56 | 68 | 116 |
| starting gold per city (median) | | | 600 | | |
| faction income / month at start (world) | 37 (车师) | | 65 | | 1 523 (罗马) |

| Purchase | Months of one median city | Months of a median starting faction (65 / month) |
|---|---|---|
| common weapon 80 | 1.4 | 1.2 |
| common full set ≈ 380 | 6.8 | 5.8 |
| uncommon weapon 320 | 5.7 | 4.9 |
| rare weapon 1400 | 25 | 21 (5 months for a 5-city realm) |
| 大宛天马 8000 | — | ~3 years for a 5-city realm |

Targets: gear is a steady secondary sink next to 开发 (50) and 征兵 (≤ 500); AI factions spend 8–15% of their income on it (§13.3); the first rare is a mid-game goal for small players and an early buy for 洛阳 / 长安 / 罗马.

---

## 5. Types, duel styles, models, icons and effects

### 5.1 Type table

| Type | Slot | Duel style / key | figure.js id | Notes |
|---|---|---|---|---|
| spear 枪矛 | weapon | pole / `spear` | `spear` | 蛇矛 → `snake`, 涯角枪 → `yajiao`, 虎头湛金枪 → `hutou`, 霸王枪 → `bawang` |
| lance 骑矛 | weapon | pole / `lance` | `lance` | kontos, contus, 马槊 |
| ji 戟 | weapon | pole / `ji` | `halberd` | 方天画戟 `halberd`; 毗沙门天王戟 → `trident` look |
| ge 戈 | weapon | pole / `ji` | `ge*` (new) | Yayoi bronze ge |
| poleblade 长柄刀 | weapon | pole / `poleblade` | `poleblade` | 青龙偃月刀 → `guandao` (duel `guandao`), 凤嘴刀 → `fengzui` |
| bigblade 斩马刀 | weapon | pole / `bigblade` | `bigblade` | |
| axe 大斧 | weapon | pole / `axe` | `bigaxe` | 开山大斧 |
| trident 三叉 | weapon | pole / `ji` | `trident` | 三尖两刃刀 |
| falx 镰形刀 | weapon | pole / `bigblade` | `falx*` (new) | reserved for Dacian finds (no v4 item yet) |
| staff 杖节 | weapon | pole / `staff*` (new) | `staff` | 竹节杖, 鸠杖, 旄节 |
| sword 剑 | weapon | one / `sword` | `jian` | 倚天剑 → `yitian`; 七支刀 → `sword7*` (new) |
| dao 刀 | weapon | one / `dao` | `dao` | 古锭刀 → `guding` |
| gladius / spatha / tachi / khanda | weapon | one / `gladius` · `sword` · `tachi` · `sword` | same names | |
| mace 锤杵 | weapon | one / `mace` | `mace` | |
| club 棍棒 | weapon | one / `club*` (new) | `club*` (new); 多纳尔之锤 `hammer*` | |
| handaxe 战斧 | weapon | one / `handaxe` | `handaxe` | |
| shortji 手戟 | weapon | one / `shortji` | `shortji` | |
| bian 鞭 | weapon | one / `mace` | `bian*` (new) | 黄盖铁鞭 |
| ankusha 象钩 | weapon | one / `mace` | `ankusha*` (new) | |
| vajra 金刚杵 | weapon | one / `vajra*` (new) | `vajra*` (new) | |
| fan 羽扇 | weapon | fan → one / `fan*` (new) | `fan` | |
| twinsword / twindao / twinji | weapon | dual / same keys | `swords` / `twindao` / `twinji` | |
| bow / longbow / crossbow / throw | off-hand | — (ranged mechanics) | `bow` / `longbow` / `crossbow*` / `javelins*` (back or hip) | |
| shield / bigshield / buckler | off-hand | guard stats | `SG.Figure.shield(name)`; new `pelta`, `hex`, `figure8` (ancile), `hide` | |
| armour types | armour | duel HP / speed | `look.torso`: `zha`, `scale`, `segm`, `coat`, `bare` + new `mail*`, `cuirass*`, `rattan*` | |
| helmets | helmet | — | `look.helm` (existing helm ids; items map to the closest, §5.2) | |
| mounts | mount | walk speed | `SG.Figure.horse({ coat, armor })`; new `camel*`, `elephant*`, `chariot*` | |
| treasures | treasure | — | belt / back / hand props (book case, seal on sash, banner on back, drum carried by the guard) | |

### 5.2 Models (Lanternvale pipeline)

- Every item has `visual.model = { fig, torso, colors[3], ink, motif, parts[], glow, scaleVar }` and a Chinese `visual.look` sentence for artists. 567 hand-made meshes are not feasible, so models are **procedural variants**: the base builder (`fig`) + palette (`colors`, `ink` outline colour) + up to six attached **parts** + a ±6% size variation (`scaleVar`). Low-poly, flat-shaded, ink-outlined (the art pilot's outline pass), lit by the warm painted light; metal is a flat colour with one warm highlight band, never PBR chrome.
- **Parts vocabulary** (each ≤ 200 triangles at high LOD, generated in `item-art.js` and attached to the weapon / armour sockets): `dragonHead tigerHead lionHead phoenixCrest serpentCoil eagle sunDisk crescent stupa bells tassel ribbons gem chain rings inscription goldInlay flameFx lightningFx steamFx cloudPattern waterFx feathers furWrap boneParts rattanWeave ringPommel spiralEtch`. The parts list in items.json was derived from the motif and look text; artists may refine it per item.
- **Bespoke builders** for the 24 epic + legendary items and for the 15 most famous rares (双股剑, 古锭刀, 倚天剑, 七星宝刀, 涯角枪, 虎头湛金枪, 凤嘴刀, 双铁戟, 七支刀, 斩白蛇剑, 湛卢剑, 赤兔's horse look, 照夜玉狮子, 的卢, 绝影): a dedicated function per id in `item-art.js` with its own silhouette (e.g. 七支刀's six branches, 天降神盾's figure-8, 金刚杵's five-pronged ends, 卡维旗's jewelled square banner). Budget: weapon high LOD ≤ 1 200 triangles including parts, mid ≤ 400; everything shares the figure atlas (no new textures; colours via vertex colour, glow via the rim / emissive term).
- **Armour, helmets, mounts**: items map to figure looks — `torso` from the type, helm id from the item's type and motif (e.g. 鹖冠 → `han` + two feather parts, 角盔 → `celtic` + horns, 骑兵面甲盔 → `galea` + face plate), mount coat / armour from the item (赤兔 red, 照夜玉狮子 white, 绝影 black, 具装 → `armor: 'scale'`). The figure shows the equipped gear everywhere: duel, clash commanders, battle board commander figures, character menu, tournament stadium.
- **Treasures** are shown as small props: books / seals at the sash, banners on the back (背旗), drums carried by a guard figure in the clash, instruments in the character menu only.
- Rarity glow: uncommon a faint jade rim on edges; rare a soft steady glow in `icon.glow` colour with sparse motes; epic strong glow + idle particles (`flameFx`, `lightningFx`, …) + a weapon trail in the glow colour; legendary as epic plus a slow golden shimmer on the outline and a custom idle (赤兔's mane flames, 玉玺's floating motes, 金刚杵's sparks, the club's laurel leaves).

### 5.3 Icons

- Procedural SVG (`SG.ItemArt.icon(id, size)`): silhouette from `icon.shape` (about 70 shapes; unknown shapes fall back to the type's shape), filled with `icon.colors` in flat bands, ink outline, a motif glyph from the model parts (dragon head, sun disk, bells …), and the rarity frame (`frame`: slate / jade / lapis / amethyst / gold-cinnabar). Epic and legendary frames shimmer (CSS animation, off when `prefers-reduced-motion`).
- Uniqueness: two items never share (shape, colours, parts); the build checks this for rare+ and adds a hash-based ornament offset to commons.
- Sizes 48 / 96 / 192 px, rendered once to a canvas atlas in an idle callback (≤ 8 ms per slice), cached by id.

### 5.4 Effects

Ability fx keys (battle `V.specialFx` styles, duel fx, world fx). Existing styles are reused where they fit (`dragon havoc arrows poison shadow`). New styles, each ≤ 1.5 s, ≤ 2 extra draw calls, pooled particles, warm painterly look:

`redhare` (red comet streak, burning hoof prints), `jadeseal` (jade dragon, seal stamped on clouds, light pillars), `vajra` (white-elephant silhouette, branching bolt, lotus shockwave, rain), `quake` (radial cracks, laurel swirl, lion roar), `serpent` (wavy blue-ink spear trail), `grassblade` (grass flattening in a fan, fire turning back), `sunarrow` (second sun shattered), `wind` / `rain` / `cloud` (天符印), `oath` (blood-wine arc, gold oath runes), `boatarrow` (peacock-feather arrow, wave ring), `stupa` (golden stupa with bells), `oxmace` (ox roar, gold chains, mountain silhouette), `kaviani` (jewelled banner unfurling), `jinn` (golden sandstorm, smoke giant), `lughspear` (steaming spear flies and returns), `thunderhammer` (bolt onto hammer, thrown arc), `aressword` (sword planted, red pillar, sacrificial fires), `giantbow` (spear-sized arrow, fan of dust), `salii` (twelve shields dancing), `hail`, `yellowturban` (yellow talismans, 「黄天当立」), `taia` (purple sword-qi waterfall), `turtlebow` (gold turtle claw, splitting arrow), `harp` (notes and season petals).

Passive effects show small cues: burning procs, stun stars, guard shields, aura rings at day start, a 「宝」 badge on units with an unused 宝具.

### 5.5 Presentation of rarity in the UI

- Purchase: coins pour from a purse into the shop counter (sfx `coins`), the item card flips and bursts in its rarity colour; rare: the model spins on a small light pillar for 1.2 s.
- Spoils / reveal of epic and legendary: full-screen ink wash, the item's silhouette drawn in brush strokes, then colour fills in, a vermillion seal 「神品」 / 「天宝」 stamps the corner, the holder's portrait slides in with a 1-line cry. Music sting per rarity (audio task).
- Equip: the icon flies to the slot, metallic `clink` (weapon) / cloth `rustle` (armour) / hoofbeat (mount).

---

## 6. Shops (市场)

### 6.1 Where and how

- Every city has a 市场. Open it from the city panel command 「市场」 (no token, like 交易 and 赏赐; available every month) or by tapping the market building in the city zoom view (request 8: the market model grows with the city tier).
- The player buys in **own cities** at full price. **Allied cities adjacent to an own city** are open at ×1.15 (city panel of the allied city → 「市场」). Enemy and neutral cities are closed, except through caravans (§6.5).
- Payment comes from the gold of the city whose market it is (like 开发). Purchases go to the faction armoury; the dialog offers to equip at once.

### 6.2 Tiers

```
tier(city) = max( 1 + [ind+town ≥ 520] + [≥ 800] + [≥ 1100],  HUBS[city] )
slots: tier 1 → 4, 2 → 6, 3 → 8, 4 → 10
```

HUBS (trade cities raised to a tier): 洛阳 长安 罗马 亚历山大 安条克 泰西封 富楼沙 巴里加扎 穆济里斯 帕尔米拉 查拉克斯 蓝氏城 成都 (4); 番禺 交趾 疏勒 于阗 撒马尔罕 塔克西拉 阿杜利斯 喔㕭 马里卜 襄阳 大宛 潘提卡彭 麦罗埃 埃德萨 木鹿 (3); 敦煌 武威 廷吉斯 约尔 金官 邪马台 伊都 喀里多尼亚 塔拉 伊斯塔克尔 哈特拉 (2).

<!-- GENERATED:TIERS (gen-md.js; do not edit by hand) -->
| 剧本 | 城数 | 一级 村市 | 二级 市集 | 三级 大市 | 四级 都会 | 有稀有品的城 | 每城本地普通品池（最少 / 平均） | 每城精良品池（平均） |
|---|---|---|---|---|---|---|---|---|
| 经典 | 38 | 8 | 18 | 9 | 3 | 14 | 13 / 39.3 | 10.7 |
| 世界 | 196 | 40 | 96 | 47 | 13 | 55 | 7 / 21.6 | 4.3 |
<!-- /GENERATED:TIERS -->

### 6.3 Pools: who sells what

- **Commons**: every city of the item's culture (any tier), plus every tier ≥ 2 city of a **neighbour culture** (items.json `cultures[c].neighbours`) within 10° (longitude / latitude) of a city of the item's culture; tier-4 metropolises reach 25°. Foreign commons cost ×1.2. In the classic scenario the foreign cultures have no cities, so their commons go to fixed trade cities (`classicTrade`: 武威 天水 长安 洛阳 for the Silk Road, 北平 北海 for Korea, 会稽 建业 for the sea, 云南 建宁 for the southern road to India).
- **Uncommons**: about 35% of the tier ≥ 2 cities of their culture (all cities if the culture has fewer than three markets), chosen by hash so each city has a different selection; tier-4 metropolises of neighbour cultures within 25° carry each foreign uncommon with 25% chance.
- **Rares**: the named cities only (`availability.world.cities`), 1–3 per item, picked for history: 尚方斩马剑 in 洛阳 (the imperial workshop), 龙渊剑 in 汝南 (西平 龙渊), 宛冯剑 and 水排 in 宛 (南阳 iron), 太公兵法 in 下邳 (圯桥), 孙子兵法竹简 in 吴, 的卢 in 襄阳, 蒲元神刀 in 汉中 / 成都, 乌兹钢锭 in 巴里加扎 / 穆济里斯, 政事论 in 华氏城, 马尔吉亚纳钢剑 in 木鹿, 尼萨圣马 in 埃克巴坦那 / 尼萨, 哈特拉毒虫陶罐 in 哈特拉, 柑橘木桌 in 约尔 / 廷吉斯, 安敦金币 in 喔㕭 … (table §12.5).
- **Epic**: 大宛天马 in 大宛 (classic: 武威, ×9000, a 胡商's horse).

Measured spread (items.json `meta.distribution`): commons in 32 world cities on average (17%; 1–64), uncommons in 7 (3.5%; 1–14), rares in 1.4 (1–3); classic: commons 9 of 38, uncommons 4, rares 1.1.

### 6.4 Stock and restock

The stock is a list of `{ id, qty, price, age }` per city. Restock runs on the first day of each month after income (`StrategyAI.endMonth`), and once at game start. It uses `SG.SeededRandom(hash(G.seed, city.id, G.monthIndex))`, so it is reproducible and draws nothing from `SG.Random` (the `tests/sim.js` stream is untouched).

```
restock(city):
  for e in stock: e.age += 1
  remove commons / uncommons with age ≥ 4 with probability 0.35 each; remove rares with age ≥ 12
  if the city has an epic, it is not sold and not in stock → put it in (it occupies a slot until bought)
  for each free slot:
    r = rng()
    if tier ≥ 2 and a rare of this city is not in stock and not cooling down and r < 0.12 → that rare, qty 1
    else if tier ≥ 2 and the uncommon pool is not empty and r < 0.37 → a random uncommon, qty 1
    else → a common: native pool with 0.75, foreign pool with 0.25 (if any), qty 1 + floor(rng() × 3)
    never two entries with the same id in one shop
  sold-out rare → cooldownUntil[id] = monthIndex + 24 (items.json restockMonths)
```

Expected: a rare listed in a city shows up every 6–10 months and stays up to a year; a tier-2 market shows 1–2 uncommons most months.

### 6.5 Caravans (胡商 / 海商) — a non-battle event

- Each month, each player-owned tier ≥ 2 city that is a Silk Road or sea hub (`HUBS` or `classicTrade`) has an 8% chance to receive a caravan for 2 months. It offers 3 items from the pools of up to 3 cultures within 25° (any owner), one of them rare with 20% chance (a shop rare of another city, or in the classic scenario a world-only common / uncommon), at ×1.5.
- News line with the merchant's portrait (a generic 胡商 / 海商 portrait by culture) and a short animation of camels or a ship arriving at the city model. AI factions use caravans too (same budget rules).

### 6.6 Selling, gifts and the armoury

- 府库 screen (from the 市场 or the Generals menu): all faction items, filter by slot / rarity, sell (in the current city's market), give (§2.6), equip.
- **Gift through 赏赐**: 赏赐 gains an option 「赐宝物」. Loyalty gain = `8 + R(0,4) + clamp(round(value × giftK / 60), 0, 30)`, × 1.5 if the item fits the general (weapon to a 武 general, book / fan to a 智 general, seal / tool to a 政 general), once per (item instance, general). 赤兔 given to 关羽 gives +30 and a scripted line (演义第二十五回).
- **Gift through 外交**: the gift field accepts an item; its contribution is `value × giftK` gold-equivalent (`allyChance` uses `gift / 1200`).

### 6.7 UI flow

1. City panel → 「市场」 (or tap the market building). The screen: shopkeeper portrait and city tier banner on the left, stock grid (icon, name, rarity frame, price; foreign items tagged 「舶来」) in the middle, the selected general's six slots on the right with a 3D figure preview.
2. Tap an item → detail card (big icon, stats, ability, lore and sources, requirement check) with a **compare** column against the selected general's current slot (green / red deltas, including duel numbers and crit hooks).
3. 「购买」 → purchase animation (§5.5) → 「立即装备给 …」 / 「存入府库」.
4. Tabs: 购买 / 出售 / 府库 / 图鉴 (collection, §14.2). Touch targets ≥ 44 px; works at 844×390 (two-column layout) and 1280×720.

---

## 7. Unique abilities (神兵之能)

### 7.1 Rules

- Every rare, epic and legendary item has exactly one ability (`ability = { id, name, learnable: false, effects[], desc, fx }`). Names are unique across items and differ from every general's special name; effect lists are unique (checked by the build).
- Abilities are never learned, never enter `Progress` pools or slots, and leave with the item. They are listed in the character menu under the item, not under 武艺.
- **Passive** effects are always on while equipped (and the requirement met). **Active** effects (`active`, `activeChoice`) are 宝具: once per battle each, in a 「宝具」 menu line next to 「必杀」, costing the unit's action like a special.
- Scopes: battle, duel, domestic, diplomacy, world. Abilities do nothing in `Conquest.autoResolve` except `defendCity` and `castle`.
- Budgets: a rare's ability is worth about one tier step of stats; an epic's about two items; legendaries change how a battle or a realm plays. Active power: rare actives 1.3–1.8 normal attacks, epic 2.0–2.5, legendary ≤ 2.8 (within `SG.Specials.AREA_TOTAL`).

### 7.2 Effect vocabulary

`e.t` is the effect id. Parameters are given as used in items.json. "→" names the hook.

**Battle (tactical)**

| id | params | meaning → hook |
|---|---|---|
| `active` | `kind` (a `specials.js` kind: smite cleave charge rampage volley blaze storm roar rally command scheme fortify haste heal assassinate poison, plus new `plant`), `name`, kind params, `fx`, optional `cond: 'castle'`, `douse`, `douseSelf: r`, `bind: days`, `captureOnRout`, `returns` | once-per-battle 宝具 → `Specials.known` + `useSpecial`; extras handled in `useSpecial` post-step |
| `activeChoice` | `name`, `options[{label, kind, …}]` (new kind `cloud`: foe tactic hit −pp for turns) | 宝具 with a sub-menu; each option once per battle |
| `aura` | `radius`, `atk`, `def`, `morale`, `heal`, `move`, `ease`, `side` ally/foe, `cond` mounted/cultureHan, `days`, `bonusFor[]`, `bonusAtk` | day-start effect on units in range → new `BattleModel.applyAuras(side)` at side start |
| `opening` | `morale`, `awe`, `moveAll` | battle start (`awe` here is an extra, not side-max) |
| `defenderOpening` | `disableFoeRanged`, `foeAtk` | battle start when holder's side defends |
| `oath` | `names[]`, `atk`, `morale` | named allies in the battle get atk / start morale |
| `charge` | `k`, `noCounter`, `cond` mounted, `allMounted` | attack after moving ≥ 2 tiles |
| `first` | `k` | first attack of the battle |
| `vsCommander` / `siege` / `selfAtk` / `outnumbered` | `k` / `k` / `k` / `atk, critDmg` | attack multipliers (§3.2) |
| `crit` | `crit`, `critDmg`, `cond` charge/vsCommander/moreTroops/plain | adds to `critArmy` / `critMulArmy` when the condition holds |
| `splash` / `trample` | `k` | other enemies adjacent to the target take `k ×` damage (trample: only after charging) |
| `sunder` / `sunderAll` | `p, k, days` / `k` | proc: target def −k for days / always ignore k of target def |
| `noCounter` | — | the target cannot counter this unit's attacks |
| `counterUp` / `counterGuard` | `k` | counter dealt + / counter taken − |
| `dmgTaken` | `attack`, `counter`, `fire`, `tactic` | multipliers on damage taken (`fire: 1.5` → ×2.5) |
| `ward` | `fire`, `confuse`, `tactic`, `weather`, `crit`, `moraleLoss`, `pierce` | reductions / immunities (fire 1.0 = immune; pierce: ignore-terrain attacks lose that property) |
| `proc` | `on` attack/counter/crit, `p`, `then { burn, confuse, morale, stun, poison, days, extra, chain }` | after the hit, one `SG.Random` draw per proc |
| `focus` | `k` | target marked: other allies' attacks on it +k today |
| `throwAttack` | `range`, `perBattle`, `k`, `noCounter`, `returns` | new action 「掷」 (ranged attack at k × damage), uses the unit's action |
| `hitRun` | `move` | once a day, after attacking, may move again |
| `mobility` | `move`, `ease[]`, `cond` plain | §3.2 |
| `zoc` | `radius`, `cond` riverAdjacent | enemies entering adjacent tiles stop |
| `terrain` | `terrains[]`, `atk`, `def` | on those terrains |
| `castle` | `def`, `atk`, `counterUp` | while on Gate / Castle |
| `sea` | `atk`, `def` | battles that started as sea landings (either side) |
| `mount` | `elephant` / `camel`, `charge`, `atk`, `escape`, `aura` | only when the matching mount is equipped |
| `antiMounted` | `k` | damage vs units with a mount item |
| `regen` | `k`, `cond` groundTerrain | heal k × start troops per day |
| `lastStand` | `below`, `atk`, `def` | troops below `below` × start troops |
| `rally` | `below`, `morale`, `allAllies` | once per battle when morale < below |
| `onRout` | `atkStack, cap` / `morale, side` / `cmdMorale` | when this unit routs an enemy |
| `allyRout` | `morale` | when an ally routs, the others gain instead of losing |
| `guard` | `protect` commander/ruler, `radius`, `k` | protected ally takes −k damage |
| `tacAmp` / `tacReflect` | `power, hit, cd, fire` / `p` | tactics; reflect = enemy tactic on holder bounces |
| `capture` | `k` | enemies routed by this unit: capture chance + k |
| `escapeK` / `decoy` / `rescue` | `k` / `p, partner, partnerP` / `radius, p` | capture avoidance (holder / holder via decoy / nearby commander) |
| `medic` | `k` | §3.2 heal |
| `expBoost` | `k` | battle and all other exp |

**Duel**

| id | params | meaning |
|---|---|---|
| `dProc` | `on` first/dash/thrust/heavy/counter/comboEnd, `p`, `eff` knockdown/knockback/stun/burn, `dmgK`, `dps`, `dur`, `unblockable`, `k` | on-hit effects |
| `dProjectile` | `on` heavyFull/special/start, `dmg`, `reach`, `count`, `guardable`, `unparryable`, `shape` | projectile spawned by the move (crescent, lightning, sun arrow, wind blade, knives) |
| `dCombo` | `hit` (n or list), `bonusDmg`, `eff` lightning/hookPull, `pairFinisher`, `lightBonus` | COMBAT-V4 string hooks (`onStringHit(n)`, finisher) |
| `dCrit` | `crit`, `critDmg` | adds to `critDuel` / `critMulDuel` |
| `dGuard` / `dGuardBreak` / `dGuardBreakFull` / `dPerfectGuard` | `k, cap` / `k` / `minCharge` / `window` | guard numbers; perfect guard = guard pressed ≤ window before a hit counts as a parry |
| `dParry` | `window`, `counterK`, `cdMinus` | parry numbers (window within the 0.02 cap) |
| `dRegen` | `hpPerSec`, `cond` guarding/grounded/always | |
| `dRage` | `start`, `gainK` | |
| `dEndure` | `hp`, `rage` | first lethal hit leaves `hp` (once per duel) |
| `dLowHp` | `below`, `dmgK` | |
| `dArmor` | `on` heavy/heavyStart/light | super armour (no hitstun) |
| `dWeak` | `fire`, `onAirborne` | weaknesses |
| `dSpecial` | `dashDist`, `dashUnblockable` | dash and special tweaks |
| `dReach` | `m`, `on` | reach for one move class (within the cap) |
| `dLifesteal` / `dFear` / `dSpd` / `dDmgK` / `dHpAdd` / `dSuperKnock` | `k` / `foeRageK, foeSpdK` / `k` / `k` / `n` / `on, k` | |

**World and domestic**

| id | params | meaning |
|---|---|---|
| `cmd` | `cmd` develop/recruit/train/hire/reward/trade/discord/persuade, `kind`, `k` | §3.4 |
| `free` | `cmd`, `n` | n uses per month without a token |
| `income` / `harvest` / `pop` | `gold, food` / `k` / `k` | governed city |
| `loyalAura` | `n`, `factionWide`, `cap`, `culture` | monthly loyalty |
| `expMonthly` | `n`, `sameCity` | monthly exp |
| `talent` | `p` | monthly chance to reveal a hidden general in own cities |
| `fame` / `virtue` | `n` | ruler only (§3.5) |
| `dipPlus` | `k`, `months`, `noBreak`, `with` (culture), `persuade` | diplomacy |
| `giftK` | `k` | value multiplier when given |
| `treasureHunt` / `scout` | `k` | hidden-item chance × k / see enemy cities' generals and gear within k links |
| `shopDiscount` | `cat`, `k` | holder's city shop |
| `defendCity` | `k` | city defence in battle and auto-resolve |
| `aggro` / `deterrence` | `k` / `minWar, aggroK` | AI target scores (§3.5) |
| `omen` | `p`, `loyalty` | 的卢 |

### 7.3 宝具 implementation

- `SG.Specials.known(gen)` appends one entry per equipped item active: `{ id: 'item:' + itemId + ':' + n, item: itemId, name, kind, …params, color: icon.glow, fx, pool: false, itemActive: true }`, validated by `SG.Specials.validate` (ranges as in specials-data.js; `plant` and `cloud` added to the kind table).
- They use the v3 machinery: `specialUsable`, `specialTargets`, `useSpecial`, `u.spUsed`, AI `aiSpecial` value. The battle menu shows them under 「宝具」 so 必杀 counts stay as v3.
- Extras: `cond: 'castle'` (usable only on Gate / Castle), `douse` / `douseSelf`, `bind` (target `mods.move = −99` for days), `captureOnRout`, `returns` (visual).

### 7.4 Duel hooks

`DuelGame` builds `fighter.itemAbilities` from `duelMods(gen).abilities` and calls `onHit(f, foe, move, ctx)`, `onGuarded`, `onParry`, `onStringStep(n)`, `onFinisher`, `onLethal`, `tick(dt)` (regen), `onStart` (projectiles, rage). All random draws go through `SG.Random`, in a fixed order after COMBAT's crit draw; `DuelGame.simulate` honours them so `duel-bots.js` can measure them.

---

## 8. Acquisition

### 8.1 Routes

| Route | Items | Rule |
|---|---|---|
| Shop | commons, uncommons, 50 rares, 1 epic | §6 |
| Caravan | shop items of other regions | §6.5 |
| Ruler-unique (slay the ruler) | 70 (60 rare, 9 epic, 1 legendary) | §8.2–8.3 |
| Signature (held at start) | 39 | §8.4 |
| Hidden (搜索) | 17 (1 legendary, 5 epic, 11 rare) | §8.5 |
| City treasure | 4 (天降神盾 罗马; 斩白蛇剑, 孔子屐 洛阳; 埃拉伽巴路斯圣石 埃梅萨) | §8.6 |
| Starting armoury | 2 (青釭剑 曹操; 濊王之印 夫余) | §8.7 |
| Tournament | 12 rares, 金刚杵 (3 titles), classic 赫拉克勒斯之棍 (5 titles) and 异域奇珍 | §8.9 |
| Spoils | anything a slain or confiscated general carried | §8.8 |
| Gifts | AI factions may send items as alliance gifts (rare 5% of their gifts) | §13 |

### 8.2 Slaying (斩杀)

A ruler is **slain** when, while he is the ruler of a faction:

1. **Executed after capture (处斩)** — the player's captive dialog or the AI (`Conquest.execute`, AI executes captured rulers 50% as in v3). Always slain.
2. **Killed in battle (阵斩)** — new roll, rulers only, in tactical battles:
   - his unit is routed by an attack that was a crit (COMBAT-V4 `r.crit`): slain with 25%;
   - he loses a duel by KO and the KO blow was a crit, a special or a string finisher: 35%; any other KO: 15%; a time-out loss: 0;
   - an `assassinate` special or 宝具 succeeds against him: 50%.
   The roll is one `SG.Random.value()` at that moment (only when the routed / KO'd unit is a ruler; skipped when `Items.enabled = false`). A slain ruler is `dead = true`, `Conquest.succession` picks the heir (v3), and the battle continues.

Not slain: routed and escaped, captured and released, a faction destroyed with its ruler fleeing (he becomes 在野 and keeps his gear), captured AI rulers the AI releases. Ordinary generals are never slain in v4 (open question Q1).

**Spoils**: all items of the slain general go to the slayer's faction armoury (for 阵斩 the faction of the unit that dealt the blow or won the duel); the bound flag of a ruler-unique is cleared; a full-screen reveal for rare+ (§5.5) with 「缴获 X —— 立即装备给 Y？」 (Y = the slayer, if he meets the requirement).

### 8.3 Ruler-uniques

- Each faction's ruler at scenario start holds his faction's unique weapon (main hand or off-hand bow / crossbow), **bound**: it cannot be sold, gifted, confiscated, reassigned or dropped, even if he loses his realm and wanders as 在野, and even if another faction later hires him (rulers never surrender to 登用 in v3; a wandering ex-ruler may be hired by a third party and keeps it bound).
- It moves only by slaying its holder (§8.2). After that it is a normal (unbound) singleton: the slayer's faction can give it to anyone, and it can be taken again by capture, confiscation or slaying.
- Successors do not get a new unique; a faction has one unique ever.
- Classic: 18 (all rare). World: 70 — 60 rare, 9 epic (草薙剑 卑弥弓呼, 径路刀 於夫罗, 混填神弓 混盘况, 毗沙门天王戟 安国, 牛首重锤 沃洛吉斯, 卢赫之矛 阿尔特, 多纳尔之锤 巴洛马尔, 战神之剑 赞提库斯, 埃塞俄比亚王之弓 耶斯博赫) and 1 legendary (赫拉克勒斯之棍 康茂德). Full list §10.

### 8.4 Signature gear

- 39 items equipped at start by their historical / novel holders (§11), including 在野 generals (典韦, 许褚, 黄忠, 甘宁, 周瑜, 诸葛亮, 华佗, 太史慈, 徐晃, 沙摩柯, 蔡琰, 莫护跋, 轲比能 …): whoever hires them gets the item with them.
- Not bound: confiscation (§8.8), slaying, a gift or the player's reassignment (with the loyalty cost of §2.6) all move them.
- Which items are 演义 rather than 史实 is stated in every source note (青龙偃月刀, 丈八蛇矛 and 方天画戟 are novel names for historical weapon types; 赤兔, 绝影, 的卢, 两刃矛, 双铁戟, 太史慈手戟, 赤罽帻, 锦帆铃, 周郎琴, 白羽扇, 焦尾琴, 金步摇冠 are attested).

### 8.5 Hidden items (搜索)

`Commands.search(c, g)` keeps the v3 order (hidden generals first) and then, before the gold / food roll:

```
for h in hidden items of city c, not found, date ≥ h.from, c.owner === searcher's faction:
  p = min(0.40, (h.p + effIntel(g) × h.intelK + search(g)/200) × treasureHunt(g) × (g.name === '孙坚' && h.item === 'chuanguoxi' ? 2 : 1))
  if SG.Random.value() < p → found: item → armoury; reveal; G.addLog
```

One draw per eligible hidden item, only in cities that have one (so the v3 stream changes only there; `Items.enabled = false` skips it). Clues (`clue` in items.json) appear as a news line the first time a faction takes the city and on 30% of failed searches there (「城中老人说……」). Example: 传国玉玺 p = 0.06 + 0.0005 × intel per search from 191-01 (history: early 191); a governor with 智力 80 searching monthly finds it in about 10 months.

### 8.6 City treasures (镇城之宝)

Kept in the vault of their home city: the owner faction's armoury lists them with a 「镇」 tag and may equip them. When the home city changes hands, a treasure that is in the defender's armoury (not equipped by a general elsewhere) moves to the conqueror's armoury. If a general carries it away, it follows him (and can be taken by spoils as usual).

### 8.7 Starting armouries

青釭剑 in 曹操's armoury (演义: 夏侯恩 carried it; 夏侯恩 is not in the game), 濊王之印 in 夫余's. Every faction also starts with 1 common per city of its culture in its armoury (AI equips them, §13).

### 8.8 Captives, confiscation and other spoils

The captive dialog (strategy-screen.js) becomes 登用 / 释放 / **缴械释放** / 处斩:

| Choice | Gear |
|---|---|
| 登用 (success) | he joins with all his gear |
| 释放 | he leaves with all his gear (v3 behaviour) |
| 缴械释放 (new) | all his unbound gear goes to your armoury; he is released; his faction's AI aggro against you ×1.1 for 12 months; his loyalty to his own lord +5 (humiliation unites) |
| 处斩 | all his gear to your armoury (a slain ruler drops his unique too) |

AI captors: hire as v3; if they release, they confiscate rare+ unbound items 50% of the time. When a faction is destroyed, its armoury (not the gear on fleeing generals) goes to the conqueror of its last city.

### 8.9 Tournament prizes (武林大会, request 14)

The tournament spec owns the event; the item side is:

- 1st prize: 75% an item, 25% a skill (TOURNAMENT spec). The item is drawn from the `champion` pool (12 tournament-only rares, each awarded once) plus every shop rare that currently has no instance in the world; when all are gone, an uncommon of the champion's culture + 300 gold.
- 2nd: a random uncommon (60% the placer's culture, 40% any); 3rd: a random common of the placer's culture.
- Gold (proposal, TOURNAMENT owns it): 1st 400, 2nd 200, 3rd 100 to the placer's city.
- **Titles**: the first general to win three tournaments receives 金刚杵 (legendary) — the stadium's grand relic. In the classic scenario the first general to win five receives 赫拉克勒斯之棍 (「大秦使节所献」). In the classic scenario, from the 4th tournament (193) on, the 1st-prize item is replaced with 15% chance by one item of the `foreign` pool (world-only rares and epics of absent cultures, 67 items; at most one epic per tournament): the 异域奇珍 of the Silk Road.

<!-- GENERATED:TOURNEY (gen-md.js; do not edit by hand) -->
| 物品 | 品级 | 文化 | 部位 | 能力 | 典据 |
|---|---|---|---|---|---|
| 干将·莫邪 | 稀有 | 汉 | 兵器·双剑 | 「雌雄相随」阴阳双剑：单挑中连段终结时两剑交击追加一击（+4 点）；会心率 +2、会心伤害 +10%。 | 吴越春秋·阖闾内传 |
| 太阿剑 | 稀有 | 汉 | 兵器·剑 | 「威道之剑」登城一麾，三军破败：宝具「登城一麾」（每战一次）：以自身为中心半径 2 的威压（威力 0.4、士气 −28、40% 混乱）。 | 越绝书·外传记宝剑 |
| 越女剑 | 稀有 | 汉 | 兵器·剑 | 「越女剑道」见之似好妇，夺之似惧虎：单挑中格开窗口 +0.02 秒、格开冷却 −0.5 秒；出招速度 +4%。 | 吴越春秋·勾践阴谋外传 |
| 万人刀 | 稀有 | 汉 | 兵器·长柄刀 | 「万人之敌」万人之敌：普通攻击时相邻其他敌军受 20% 伤害；每击溃一支敌军，己方全军士气 +6。 | 陶弘景《古今刀剑录》；三国志·魏书·程昱传 |
| 新亭侯刀 | 稀有 | 汉 | 兵器·刀 | 「新亭侯」燕人张翼德：攻击有 20% 几率令目标士气再 −15。 | 陶弘景《古今刀剑录》；三国志·蜀书·张飞传 |
| 夔牛鼓 | 稀有 | 汉 | 宝物·鼓角 | 「声闻五百里」以威天下：开战时全体敌军士气 −10；首日周围 5 格敌军士气再 −8。 | 山海经·大荒东经 |
| 角斗士头盔 | 稀有 | 罗马 | 头盔·面甲盔 | 「万众欢呼」角斗场之星：单挑中怒气积累 +30%；对手出招速度 −4%。 | Pompeii gladiator barracks finds；Cassius Dio 73.17–19 |
| 奥林匹亚橄榄冠 | 稀有 | 罗马 | 头盔·王冠 | 「奥林匹克之冠」竞技会的桂冠：为君主时人望 +5；获得经验 +20%。 | Pausanias 5.7; 5.15 |
| 拳斗皮带 | 稀有 | 罗马 | 宝物·护符 | 「铁拳缠带」拳斗之术：单挑中轻击伤害 +8%；格挡对其减伤降低 8 个百分点。 | Virgil, Aeneid 5.400–405 |
| 野见宿祢力带 | 稀有 | 倭 | 宝物·护符 | 「相扑之祖」蹴折其肋：单挑中重击的击退 ×1.4；体力上限 +6。 | 日本书纪·垂仁纪 |
| 斯基泰黄金梳 | 稀有 | 萨尔马提亚 | 宝物·珍宝 | 「武士金梳」武士之宝：以之赏赐 / 外交时价值 ×1.5；单挑开场怒气 25%。 | Hermitage Museum, Solokha comb |
| 冒顿鸣镝 | 稀有 | 草原 | 副手·弓 | 「鸣镝所向」鸣镝所射而不悉射者，斩之：持有者攻击过的敌军，当日受到其他友军的攻击 +10%。 | 史记·匈奴列传 |
| 金刚杵 | 传说 | 天竺 | 兵器·金刚杵 | 「因陀罗之雷」因陀罗以之斩弗栗多、放出众水：宝具「金刚雷霆」（每战一次）：5 格内半径 2 的天雷（威力 1.0、40% 混乱、士气 −15），并浇灭范围内的火；普通攻击会心时，雷电跃至一支相邻敌军（40% 伤害）；单挑中满蓄重击射出不可格开的雷光（6 点、+1.5 米），连段第 8 击召雷（+5 点）。 | Rigveda 1.32; 1.61.6；Bhāgavata Purāṇa 6.10 |
| 赫拉克勒斯之棍 | 传说 | 罗马 | 兵器·棍棒 | 「罗马的赫拉克勒斯」康茂德披狮皮、执大棒，以赫拉克勒斯自居：每击溃一支敌军攻击 +4%（至多 12 层）；宝具「十二功业」（每战一次）：单体重击（威力 2.6、无视地形、30% 混乱）；单挑中重击出手带霸体，命中必定击倒。 | Herodian 1.14；Cassius Dio 73.15 |
<!-- /GENERATED:TOURNEY -->

### 8.10 Starting kits

At game start every general (all factions and 在野) receives commons so the figures match their gear:

```
weaponType = fig2type[ SG.Figure.lookOf(gen).weapon ]          // the v3 look decides the item type
weapon     = kits[culture].byType[weaponType] ?? first of styleFallback[style] in kits[culture].byType
armour     = war ≥ 55 ? kits[culture].armour : kits[culture].armourLight
helmet     = war ≥ 70 ? kits[culture].helmet : (intel > war + 25 ? kits[culture].cap : null)
civilians  (intel > war + 25 and war < 55): robe + cap; a fan (han) or staff instead of a melee weapon
mount      = war ≥ 65 ? kits[culture].mount : null              // 倭 has no horses (「其地无牛马」)
shield     = one-handed weapon and war ≥ 50 ? kits[culture].shield : null
signature / ruler-unique items replace the kit piece of their slot
```

A kit piece that would come from outside the culture's trade sphere is skipped (`null` in `kits`). About 1 500 instances in the world scenario, 500 in classic.

---

## 9. The 4 legendary and 20 epic items

Chosen so that (a) every one of the 18 culture codes has at least one, (b) history and myth are both present and labelled, (c) they are spread over all six slots and over every acquisition route, and (d) a player in any region has one within reach and the four legendaries need four different kinds of effort.

- **Legendary**: 赤兔 (history: 「人中有吕布，马中有赤兔」; take it from 吕布), 传国玉玺 (history: found by 孙坚 in a Luoyang well in 191; search 洛阳), 金刚杵 (myth: Indra's thunderbolt; win three 武林大会), 赫拉克勒斯之棍 (history and myth: Commodus as 「罗马的赫拉克勒斯」; slay 康茂德 in the world scenario, five titles in the classic one).
- **Epic, by culture**: 汉 青龙偃月刀 · 方天画戟 · 丈八蛇矛; 南中 乌戈藤甲; 倭 草薙剑; 夷洲 射日神弓; 东夷 天符印; 草原 径路刀; 南海 混填神弓; 西域 毗沙门天王戟; 贵霜·大宛 大宛天马; 安息·波斯 牛首重锤 · 卡维旗; 阿拉伯 所罗门之戒; 罗马 天降神盾 (+ the club); 凯尔特 卢赫之矛; 日耳曼 多纳尔之锤; 萨尔马提亚 战神之剑; 天竺 (金刚杵); 阿克苏姆·库施 埃塞俄比亚王之弓; 毛里 安泰俄斯之带.
- **By slot**: weapons 12, off-hand bows 3, shield 1, armour 2, mounts 2, treasures 4.
- **By route**: ruler-unique 10, signature 5, hidden 6, city treasure 1, shop 1, tournament 1.
- Rejected on purpose: the Ark of the Covenant (sacred to living faiths, and only a 14th-century Aksumite claim); Excalibur and Dhu al-Fiqar (centuries later than 190); 轩辕剑 (a modern game's name as much as a legend; 斩白蛇剑, the Han founder's real relic in the Luoyang armoury, takes its place as a rare city treasure).

<!-- GENERATED:LE (gen-md.js; do not edit by hand) -->
| 品级 | 名称 | 文化 | 部位 / 类型 | 获取（世界剧本） | 获取（经典剧本） | 能力 | 主要数值 |
|---|---|---|---|---|---|---|---|
| 传说 | **赤兔** | 汉 | 坐骑·战马 | 吕布开局持有 | 吕布开局持有 | 「驰城飞堑」 宝具「赤兔奔雷」 | 机动 +2，冲锋 +15%，步速 +10%，脱身 +100% |
| 传说 | **传国玉玺** | 汉 | 宝物·印绶 | 搜索：洛阳（191 年 01 月起） | 搜索：洛阳（191 年 01 月起） | 「受命于天」 宝具「天命所归」 | 政治 +10，人望 +15，外交 +20，登用 +10，德 +10 |
| 传说 | **金刚杵**<br>vajra | 天竺 | 兵器·金刚杵 | 武林大会三冠 | 武林大会三冠 | 「因陀罗之雷」 宝具「金刚雷霆」 | 武力 +12，攻击 +18%，会心率 +4 |
| 传说 | **赫拉克勒斯之棍**<br>clava Herculis | 罗马 | 兵器·棍棒 | 斩杀罗马君主康茂德 | 武林大会五冠 | 「罗马的赫拉克勒斯」 宝具「十二功业」 | 武力 +12，攻击 +17%，破甲 +14%，单挑伤害 +4%，破防 +12，会心伤害 +30% |
| 史诗 | **青龙偃月刀** | 汉 | 兵器·长柄刀 | 关羽开局持有 | 关羽开局持有 | 「冷艳锯」 宝具「青龙出海」 | 武力 +8，攻击 +14%，会心率 +3，单挑伤害 +4%，出招速度 -1% |
| 史诗 | **方天画戟** | 汉 | 兵器·戟 | 吕布开局持有 | 吕布开局持有 | 「画戟横空」 宝具「横扫千军」 | 武力 +9，攻击 +15%，反击 +12%，单挑伤害 +3%，会心率 +3 |
| 史诗 | **丈八蛇矛** | 汉 | 兵器·枪矛 | 张飞开局持有 | 张飞开局持有 | 「据水断桥」 宝具「蛇矛穿阵」 | 武力 +8，攻击 +13%，会心率 +3，单挑伤害 +3% |
| 史诗 | **乌戈藤甲**<br>—（演义） | 南中 | 铠甲·藤甲 | 兀突骨开局持有 | 兀突骨开局持有 | 「刀箭不入」 | 防御 +18%，体力 +10，畏火 +150% |
| 史诗 | **草薙剑**<br>草薙剣（天叢雲剣） | 倭 | 兵器·剑 | 斩杀狗奴君主卑弥弓呼 | 武林大会「异域奇珍」 | 「天丛云」 宝具「刈草返火」 | 武力 +8，攻击 +13%，会心率 +3，格开窗口 +0.02s |
| 史诗 | **射日神弓**<br>Atayal: two suns legend (口传) | 夷洲 | 副手·长弓 | 搜索：夷洲 | 武林大会「异域奇珍」 | 「射日」 宝具「射落骄阳」 | 压制 +15%，先手 +8%，会心率 +2 |
| 史诗 | **天符印**<br>천부인 | 东夷 | 宝物·印绶 | 搜索：国内城 | 武林大会「异域奇珍」 | 「风伯雨师云师」 宝具「三印」 | 政治 +5，人望 +3，外交 +12，登用 +9，智力 +3 |
| 史诗 | **径路刀**<br>径路（匈奴语；一说即突厥语 qïŋïraq「刀」之音译） | 草原 | 兵器·刀 | 斩杀南匈奴君主於夫罗 | 武林大会「异域奇珍」 | 「径路盟誓」 宝具「血酒盟誓」 | 武力 +7，攻击 +12%，会心率 +3 |
| 史诗 | **混填神弓**<br>Kauṇḍinya’s bow (Funan legend) | 南海 | 副手·弓 | 斩杀扶南君主混盘况 | 武林大会「异域奇珍」 | 「穿舶神矢」 宝具「穿舶神矢」 | 压制 +15%，会心率 +2 |
| 史诗 | **毗沙门天王戟**<br>Vaiśravaṇa | 西域 | 兵器·戟 | 斩杀于阗君主安国 | 武林大会「异域奇珍」 | 「鼠王护国」 宝具「天王托塔」 | 武力 +7，攻击 +12%，反击 +12%，单挑伤害 +3%，防御 +4% |
| 史诗 | **大宛天马**<br>汗血马（大宛 Ferghana） | 贵霜·大宛 | 坐骑·战马 | 商店：大宛（8000 金） | 商店：武威（9000 金） | 「汗血千里」 | 机动 +1，冲锋 +10%，步速 +9%，脱身 +50% |
| 史诗 | **牛首重锤**<br>gorz-e gāvsār | 安息·波斯 | 兵器·锤杵 | 斩杀安息君主沃洛吉斯 | 武林大会「异域奇珍」 | 「法里东之锤」 宝具「缚王于山」 | 武力 +7，攻击 +12%，破甲 +10%，破防 +10 |
| 史诗 | **卡维旗**<br>Derafsh-e Kāviān | 安息·波斯 | 宝物·旗帜 | 搜索：伊斯塔克尔 | 武林大会「异域奇珍」 | 「铁匠之旗」 宝具「王者之旗」 | 士气 +15，政治 +3 |
| 史诗 | **所罗门之戒**<br>Khātam Sulaymān | 阿拉伯 | 宝物·印绶 | 搜索：马里卜 | 武林大会「异域奇珍」 | 「役使精灵」 宝具「精灵沙暴」 | 政治 +4，人望 +3，外交 +12，登用 +9，智力 +4 |
| 史诗 | **卢赫之矛**<br>Sleá Lugha（Areadbhair） | 凯尔特 | 兵器·枪矛 | 斩杀爱尔兰君主阿尔特 | 武林大会「异域奇珍」 | 「无可抗之矛」 宝具「光之矛」 | 武力 +8，攻击 +13%，会心率 +2，单挑伤害 +3% |
| 史诗 | **多纳尔之锤**<br>Þunraz / Donar | 日耳曼 | 兵器·棍棒 | 斩杀马科曼尼君主巴洛马尔 | 武林大会「异域奇珍」 | 「雷神多纳尔」 宝具「雷霆之锤」 | 武力 +7，攻击 +12%，破甲 +10%，单挑伤害 +4%，破防 +10 |
| 史诗 | **战神之剑**<br>akinakes / gladius praelongus | 萨尔马提亚 | 兵器·长剑 | 斩杀雅济吉斯君主赞提库斯 | 武林大会「异域奇珍」 | 「插地为神」 宝具「战神降临」 | 武力 +8，攻击 +12%，冲锋 +8% |
| 史诗 | **埃塞俄比亚王之弓**<br>τόξον τοῦ Αἰθιόπων βασιλέος | 阿克苏姆·库施 | 副手·长弓 | 斩杀库施君主耶斯博赫 | 武林大会「异域奇珍」 | 「长寿者之弓」 宝具「巨弓之矢」 | 压制 +17%，先手 +8%，会心率 +2 |
| 史诗 | **安泰俄斯之带**<br>Ἀνταῖος | 毛里 | 铠甲·兽皮彩绘 | 搜索：廷吉斯 | 武林大会「异域奇珍」 | 「大地之子」 | 防御 +8%，威慑 +6，体力 +7 |
| 史诗 | **天降神盾**<br>ancile | 罗马 | 副手·盾 | 镇城之宝：罗马 | 武林大会「异域奇珍」 | 「萨利之舞」 宝具「十二神盾」 | 防御 +12%，格挡 +6 |
<!-- /GENERATED:LE -->

Design notes per item (full mechanics and fx text in items.json):

- **赤兔**: speed made rules — terrain-blind movement, no-counter charges, a 4-tile 宝具 charge, never captured. Requires 武力 85 to ride well, so it is a prize for the strongest, as in the novel when it goes to 关羽.
- **传国玉玺**: the realm item — fame, virtue, diplomacy, a free 外交 every month, loyalty for everyone, a war-wide 宝具, and the AI envy that historically doomed 袁术.
- **金刚杵**: the tournament's crown — the only legendary that any general of any faction can earn by skill in duels.
- **赫拉克勒斯之棍**: stacking power from routs (the twelve labours), a piercing smite, super-armour heavies that always knock down.
- **青龙偃月刀 / 方天画戟 / 丈八蛇矛**: three different fighting identities — fear on commander kills and a crescent wave; splash and hook-pull strings; a bridge-holding zone of control and the strongest parry counter.
- **乌戈藤甲**: the classic trade-off — −30% attack damage taken, ×2.5 fire; Zhuge Liang's answer is in the rules.
- **草薙剑**: fire immunity and turning fire back on the enemy (the grass-cutting legend).
- **射日神弓 / 混填神弓 / 埃塞俄比亚王之弓**: three bows with three stories — a sniper shot at a second sun, an arrow through a ship that makes the enemy surrender, a bow so strong that weak rulers dare not attack its owner.
- **天符印**: three seals, three once-per-battle choices (wind, rain, cloud) plus harvest and population.
- **径路刀**: the blood oath — unbreakable, longer alliances and a rally.
- **毗沙门天王戟**: defence — the rats that gnawed the Xiongnu bowstrings disable enemy bows when Khotan defends.
- **大宛天马**: the one epic you can buy, at a price that takes a realm years.
- **牛首重锤 / 卡维旗**: the Iranian pair — the mace that binds a tyrant to a mountain, the blacksmith's banner that makes routs rally the army and brings recruits.
- **所罗门之戒**: confusion immunity, a sandstorm and the best 离间 / 劝诱 item.
- **卢赫之矛**: no counter-attacks, fire procs and a spear that flies by itself.
- **多纳尔之锤**: thunder and a thrown hammer that returns.
- **战神之剑**: plant the sword, become an immovable war-god pillar for the army.
- **安泰俄斯之带**: regeneration while touching the earth, lost when lifted (Heracles' trick works in the duel).
- **天降神盾**: Rome's palladium-shield — tactic resistance, crit resistance, a dancing-shield 宝具 and the perfect guard.

---

## 10. Ruler-unique weapons (all 70)

Every faction of both scenarios (classic 18 are the first 18 rows). 「典据类别」: 史实 history, 考古 archaeology, 神话 myth, 演义 novel, 游戏取舍 game liberty (each source note in items.json says what is attested and what is invented).

<!-- GENERATED:RULERS (gen-md.js; do not edit by hand) -->
| 地域 | 势力 | 君主 | 专属兵器 | 品级 | 类型 | 能力 | 典据类别 |
|---|---|---|---|---|---|---|---|
| 中原 | 董卓 | 董卓 | 七星宝刀 | 稀有 | 刀 | 「七星献刀」暗藏杀机：每战第一次攻击伤害 +25%；单挑中第一次命中伤害 ×1.6。 | 演义 |
| 中原 | 袁绍 | 袁绍 | 本初横刀 | 稀有 | 刀 | 「横刀长揖」四世三公的威望：周围 2 格友军每日士气 +6、防御 +5%；为君主时人望 +3。 | 史实 |
| 中原 | 曹操 | 曹操 | 倚天剑 | 稀有 | 剑 | 「倚天削铁」削铁如泥：攻击有 20% 几率令目标防御 −15%（2 日）；单挑中格挡对其减伤降低 10 个百分点。 | 演义 |
| 中原 | 刘备 | 刘备 | 双股剑 | 稀有 | 双剑 | 「桃园之誓」桃园结义：周围 3 格友军攻击 +6%；关羽、张飞与之同阵时各得攻击 +12%、开战士气 +10。 | 演义 |
| 中原 | 孙坚 | 孙坚 | 古锭刀 | 稀有 | 刀 | 「猛虎下山」江东猛虎：移动 2 格以上后的攻击 +15%、会心率 +2。 | 演义 |
| 中原 | 袁术 | 袁术 | 金错刀 | 稀有 | 刀 | 「仲家僭号」僭号仲家，挥霍无度：所任太守之城金收入 +15%；但他国电脑对其势力的敌意 ×1.15。 | 史实 |
| 中原 | 公孙瓒 | 公孙瓒 | 两刃矛 | 稀有 | 枪矛 | 「白马冲阵」自持两刃矛驰出冲贼：骑乘时移动 2 格以上后的攻击 +20%，且敌军无法反击。 | 史实 |
| 中原 | 刘表 | 刘表 | 八俊玉具剑 | 稀有 | 剑 | 「八俊清议」名列八俊，荆州士人归心：同城武将每月忠诚 +1；每月有 4% 几率让本势力城中一名隐士自行现身。 | 史实 |
| 中原 | 刘焉 | 刘焉 | 益州牧旄节 | 稀有 | 杖节 | 「天子之气」益州分野有天子气：坐镇之城守城时防御 +15%；为君主时人望 +4。 | 史实 |
| 中原 | 马腾 | 马腾 | 伏波长矛 | 稀有 | 骑矛 | 「马革裹尸」男儿当死于边野：兵力降到开战时 35% 以下后攻击 +20%、防御 +10%。 | 游戏取舍 |
| 中原 | 陶谦 | 陶谦 | 丹阳铁剑 | 稀有 | 剑 | 「丹阳精兵」丹阳劲卒：训练时训练度额外 +3；相邻友军防御 +8%。 | 游戏取舍 |
| 中原 | 孔融 | 孔融 | 孔圣遗剑 | 稀有 | 剑 | 「座上客满」座上客常满：登用成功率 +8 个百分点；为君主时德 +3。 | 游戏取舍 |
| 中原 | 韩馥 | 韩馥 | 冀州长戟 | 稀有 | 戟 | 「带甲百万」冀州带甲百万：所在城征兵时每金得兵 +15%。 | 游戏取舍 |
| 中原 | 张鲁 | 张鲁 | 天师斩邪剑 | 稀有 | 剑 | 「五斗米道」符箓护身：受到混乱的几率减半、受计策伤害 −15%；所任太守之城粮收入（秋收）+10%。 | 史实 |
| 中原 | 张杨 | 张杨 | 并州环刀 | 稀有 | 刀 | 「并州骑风」并州骑兵来去如风：在平原上机动力 +1。 | 游戏取舍 |
| 中原 | 刘繇 | 刘繇 | 牟平宝剑 | 稀有 | 剑 | 「宗室名望」汉室宗亲：外交结盟成功率 +6 个百分点。 | 游戏取舍 |
| 中原 | 陆康 | 陆康 | 庐江守城弩 | 稀有 | 弩 | 「受敌二年」受敌二年：守城（位于本城 / 城门格）时防御 +25%、反击伤害 +20%。 | 史实 |
| 中原 | 孟获 | 孟获 | 松纹镶宝剑 | 稀有 | 双剑 | 「七擒不服」七擒七纵：溃散时被俘几率减半；士气跌破 30 时一次回复 20（每战一次）。 | 演义 |
| 辽东与朝鲜 | 公孙度 | 公孙度 | 辽东旄头戟 | 稀有 | 戟 | 「雄踞海东」雄踞海东：为君主时人望 +3；坐镇之城守城时防御 +10%；周围 2 格友军攻击 +5%。 | 史实 |
| 辽东与朝鲜 | 高句丽 | 故国川王 | 朱蒙神弓 | 稀有 | 弓 | 「一矢殪兽」限之一矢：每战第一次攻击伤害 +30%；会心率 +2。 | 史实 |
| 辽东与朝鲜 | 夫余 | 尉仇台 | 赤玉宝剑 | 稀有 | 剑 | 「赤玉之辉」夫余赤玉：以宝物赏赐 / 外交时价值 ×1.3；结盟成功率 +4 个百分点。 | 史实 |
| 辽东与朝鲜 | 百济 | 肖古王 | 七支刀 | 稀有 | 剑 | 「七支瑞刃」百济赠倭之礼：外交结盟成功率 +8 个百分点，结成的同盟期限 +6 个月。 | 考古 |
| 辽东与朝鲜 | 新罗 | 伐休 | 金饰环头大刀 | 稀有 | 刀 | 「占风云」占风云而知丰俭：所任太守之城秋收 +12%；受天候类计策（风、雷、水）伤害 −30%、其他计策 −10%。 | 史实 |
| 辽东与朝鲜 | 伽倻 | 首露王 | 龟旨铁剑 | 稀有 | 剑 | 「金卵铁国」铁之国：开发产业 +15%；所任太守之城金收入 +8%。 | 史实 |
| 倭国 | 邪马台 | 卑弥呼 | 广形铜矛 | 稀有 | 枪矛 | 「鬼道惑众」事鬼道，能惑众：周围 3 格敌军每日士气 −6；计策成功率 +6 个百分点。 | 考古 |
| 倭国 | 狗奴 | 卑弥弓呼 | 草薙剑 | 史诗 | 剑 | 「天丛云」斩八岐大蛇，尾中得剑；倭建命以之刈草退火：持有者所在格不会起火、免疫火伤；宝具「刈草返火」（每战一次）：先扑灭自身周围 2 格的火，再把火势卷向 3 格内的敌阵（半径 1、威力 1.2、燃烧 2 日）；单挑中绝技附带一道贴地疾行的风刃（6 点）。 | 神话 |
| 塞北 | 鲜卑 | 魁头 | 雹神角弓 | 稀有 | 弓 | 「雷雹之子」天雹化箭：宝具「冰雹箭雨」（每战一次）：向 4 格内一点齐射，命中目标与相邻敌军（威力 1.3）。 | 史实 |
| 塞北 | 东部鲜卑 | 素利 | 狼首铁刀 | 稀有 | 刀 | 「狼嚎」狼群嚎叫：攻击有 15% 几率令目标士气再 −10。 | 游戏取舍 |
| 塞北 | 乌桓 | 丘力居 | 乌桓锻铁刀 | 稀有 | 刀 | 「魂归赤山」锻金铁为兵：每击溃一支敌军，己方全军士气 +8。 | 史实 |
| 塞北 | 南匈奴 | 於夫罗 | 径路刀 | 史诗 | 刀 | 「径路盟誓」以径路刀、金留犁挠酒，共饮血盟：结盟成功率 +10 个百分点，结成的同盟期限 +12 个月，盟友电脑不会背盟；宝具「血酒盟誓」（每战一次）：周围 3 格友军士气 +30、攻击 +20%（2 日）、回复最大兵力 5%；单挑中造成伤害的 15% 回复体力。 | 史实 |
| 西域 | 鄯善 | 童格罗伽 | 鄯善精铁剑 | 稀有 | 剑 | 「楼兰古道」丝路要冲：交易价格优惠 12%；在本势力城中搜索宝物的几率 ×1.3。 | 史实 |
| 西域 | 于阗 | 安国 | 毗沙门天王戟 | 史诗 | 戟 | 「鼠王护国」毗沙门天之祚胤，鼠神夜啮匈奴弓弦：作为守方开战时，敌军副手的弓、弩失效，敌军攻击 −8%；宝具「天王托塔」（每战一次）：周围 2 格友军防御 ×1.5、反击 ×1.6（2 日）；单挑中格挡减伤 +4 个百分点，格挡时每秒回复 0.6 体力。 | 神话 |
| 西域 | 疏勒 | 和得 | 葱岭雪刃 | 稀有 | 刀 | 「猎中夺位」猎中射杀：攻击敌军主将时会心率 +3。 | 史实 |
| 西域 | 车师 | 阿罗多 | 天山长矛 | 稀有 | 枪矛 | 「天山雪岭」天山雪岭间来去：在山丘上攻击 +6%、防御 +12%。 | 史实 |
| 交州与南海 | 士燮 | 士燮 | 南越王铜钺 | 稀有 | 大斧 | 「交州威仪」鸣钟磬、备威仪：为君主时人望 +4；同城武将每月忠诚 +1；开发町 +10%。 | 考古 |
| 交州与南海 | 朱符 | 朱符 | 交州刺史节 | 稀有 | 杖节 | 「横征暴敛」侵虐百姓：所任太守之城金收入 +25%，但同城武将每月忠诚 −1。 | 史实 |
| 交州与南海 | 林邑 | 区连 | 林邑象牙矛 | 稀有 | 枪矛 | 「象林起兵」驱象而战：骑象时移动后攻击 +12%、周围 2 格敌军每日士气 −5。 | 史实 |
| 交州与南海 | 扶南 | 混盘况 | 混填神弓 | 史诗 | 弓 | 「穿舶神矢」神赐之弓，一矢穿舶，柳叶举众降：宝具「穿舶神矢」（每战一次）：6 格内单体狙射（威力 2.4、无视地形、50% 混乱）；若目标因此溃散，其主将必被俘；渡海作战时全军攻击 +15%。 | 神话 |
| 天竺与贵霜 | 贵霜 | 胡维色迦 | 胡维色迦金象钩 | 稀有 | 象钩 | 「法罗之光」王者光辉（Pharro）：为君主时人望 +4；周围 3 格友军每日士气 +5；骑象时冲锋伤害 +10%。 | 考古 |
| 天竺与贵霜 | 西总督 | 鲁德拉辛哈 | 狮子王剑 | 稀有 | 阔剑 | 「善见湖」修复善见湖之家业：开发土地 +18%。 | 考古 |
| 波斯 | 安息 | 沃洛吉斯 | 牛首重锤 | 史诗 | 锤杵 | 「法里东之锤」法里东以牛首锤击倒蛇王，缚之于山：普通攻击无视目标 20% 防御；宝具「缚王于山」（每战一次）：单体重击（威力 2.5、40% 混乱），目标 2 日不能移动；单挑中蓄力过半的重击即可破防。 | 神话 |
| 波斯 | 波西斯 | 戈契赫尔 | 阿娜希塔权杖 | 稀有 | 锤杵 | 「阿娜希塔之水」水神阿娜希塔：周围 2 格友军每日回复最大兵力 4%；受火伤害 −30%。 | 史实 |
| 波斯 | 伊比利亚 | 列夫 | 阿米拉尼之剑 | 稀有 | 剑 | 「缚山巨人」挣断锁链：单挑中第一次受到致命伤时以 1 点体力撑住；体力低于 30% 时伤害 +15%。 | 神话 |
| 阿拉伯 | 哈特拉 | 阿布萨米亚 | 沙玛什日轮矛 | 稀有 | 枪矛 | 「日神之城」两拒罗马：守城时防御 +20%、攻击 +10%。 | 史实 |
| 阿拉伯 | 奥斯若恩 | 阿布加尔 | 埃德萨王者之剑 | 稀有 | 剑 | 「埃德萨学园」哲人巴戴桑的学园：同城武将每月经验 +4。 | 游戏取舍 |
| 阿拉伯 | 萨巴 | 阿勒汗 | 马里卜青铜剑 | 稀有 | 剑 | 「马里卜大坝」古坝灌溉：所任太守之城秋收 +15%、开发土地 +10%。 | 史实 |
| 阿拉伯 | 希木叶尔 | 塔兰 | 扎法尔长矛 | 稀有 | 枪矛 | 「山城扎法尔」高原山城：在山丘上攻击 +10%。 | 游戏取舍 |
| 阿拉伯 | 哈德拉毛 | 伊勒阿兹 | 沙布瓦乳香刃 | 稀有 | 刀 | 「乳香之路」乳香之利：所任太守之城金收入 +12%；以宝物赏赐 / 外交时价值 ×1.2。 | 史实 |
| 非洲 | 阿克苏姆 | 加达拉特 | 阿克苏姆王矛 | 稀有 | 枪矛 | 「渡海远征」越红海远征阿拉伯：渡海进攻时全军攻击 +12%。 | 史实 |
| 罗马 | 罗马 | 康茂德 | 赫拉克勒斯之棍 | 传说 | 棍棒 | 「罗马的赫拉克勒斯」康茂德披狮皮、执大棒，以赫拉克勒斯自居：每击溃一支敌军攻击 +4%（至多 12 层）；宝具「十二功业」（每战一次）：单体重击（威力 2.6、无视地形、30% 混乱）；单挑中重击出手带霸体，命中必定击倒。 | 史实 |
| 蛮族 | 喀里多尼亚 | 阿根托科克斯 | 喀里多尼亚巨剑 | 稀有 | 长剑 | 「巨剑无尖」挥舞无尖巨剑：普通攻击时相邻的其他敌军受 30% 伤害。 | 史实 |
| 蛮族 | 马科曼尼 | 巴洛马尔 | 多纳尔之锤 | 史诗 | 棍棒 | 「雷神多纳尔」日耳曼人奉为「赫拉克勒斯」的雷神：宝具「雷霆之锤」（每战一次）：4 格内半径 1 的落雷（威力 1.0、30% 混乱）；每战一次可把锤掷向 2 格外的敌军（伤害同普通攻击、敌不能反击），锤自行飞回；单挑中重击命中附带 0.3 秒雷击晕眩。 | 神话 |
| 蛮族 | 雅济吉斯 | 赞提库斯 | 战神之剑 | 史诗 | 长剑 | 「插地为神」阿兰人拔剑插地，奉为战神：宝具「战神降临」（每战一次）：自身 2 日不能移动、防御 ×1.4，周围 3 格友军攻击 ×1.3、士气 +15；兵力多于目标时会心率 +2；单挑开场怒气 50%，体力低于 30% 时伤害 +30%。 | 史实 |
| 蛮族 | 哥特 | 菲利默 | 高特之矛 | 稀有 | 枪矛 | 「南迁之王」率族南迁：开战首日全军机动力 +1。 | 史实 |
| 蛮族 | 博斯普鲁斯 | 撒罗玛提斯 | 潘提卡彭金柄剑 | 稀有 | 长剑 | 「剿灭海盗」击退斯基泰与海盗：渡海作战（攻守）防御 +15%；对骑乘的敌军伤害 +8%。 | 史实 |
| 非洲 | 巴夸特 | 卡纳尔塔 | 巴夸特王标枪 | 稀有 | 枪矛 | 「来去如风」毛里轻骑：每日一次，攻击后可再移动 1 格。 | 史实 |
| 蛮族 | 汪达尔 | 劳斯 | 哈斯丁长剑 | 稀有 | 长剑 | 「双王南下」劳斯与拉普图斯：相邻友军攻击 +10%（若为拉普图斯或劳斯则 +20%）。 | 史实 |
| 蛮族 | 爱尔兰 | 阿尔特 | 卢赫之矛 | 史诗 | 枪矛 | 「无可抗之矛」持此矛者，无战能当：普通攻击敌军不能反击；会心伤害 +20%；攻击有 25% 几率点燃目标所在格（1 日）；宝具「光之矛」（每战一次）：长矛自行飞出贯穿 4 格后飞回（威力 2.2）；单挑中刺击使对手灼烧 3 秒（每秒 1 点）。 | 神话 |
| 波斯 | 阿迪亚贝纳 | 纳尔塞 | 阿尔贝拉铁锤 | 稀有 | 锤杵 | 「铁锤震击」铁锤震击：攻击有 15% 几率使目标晕眩（失去下一次行动）。 | 游戏取舍 |
| 阿拉伯 | 卡拉塞尼 | 阿坦贝洛斯 | 查拉克斯珍珠剑 | 稀有 | 剑 | 「珍珠商港」海湾商港：交易价格优惠 15%；所任太守之城金收入 +6%。 | 史实 |
| 波斯 | 埃利迈斯 | 奥罗德斯 | 唐萨尔瓦克骑矛 | 稀有 | 骑矛 | 「搏狮骑士」持矛搏狮：移动后的攻击 +10%；单挑中冲刺攻击伤害 ×1.3。 | 考古 |
| 阿拉伯 | 金达 | 拉比阿 | 法奥长剑 | 稀有 | 剑 | 「沙漠之王」骑驼的沙漠之王：骑骆驼时攻击 +8%、被俘几率再 −20%。 | 史实 |
| 非洲 | 库施 | 耶斯博赫 | 埃塞俄比亚王之弓 | 史诗 | 长弓 | 「长寿者之弓」待尔等能轻易拉开此弓，再来进攻：武力不足 80 者拉不满，数值减半；宝具「巨弓之矢」（每战一次）：5 格内一点及相邻敌军（威力 2.0）；没有武力 90 以上武将的电脑势力，对持有者势力的敌意 ×0.85。 | 史实 |
| 南天竺与师子国 | 百乘 | 耶若室利 | 百乘王剑 | 稀有 | 阔剑 | 「双桅船币」海贸之王：所任太守之城金收入 +10%；渡海进攻时全军攻击 +8%。 | 考古 |
| 南天竺与师子国 | 朱罗 | 迦梨迦罗 | 穆鲁甘神矛 | 稀有 | 枪矛 | 「文尼之战」以寡敌众：敌军部队多于我军时攻击 +12%、会心伤害 +20%。 | 史实 |
| 南天竺与师子国 | 潘地亚 | 内敦切利扬 | 双鱼剑 | 稀有 | 剑 | 「双鱼之徽」双鱼回环：反击伤害 +25%。 | 史实 |
| 南天竺与师子国 | 哲罗 | 森古图万 | 哲罗雕弓 | 稀有 | 长弓 | 「北取神石」远征北方：攻击时受到的反击再 −10%；获得经验 +15%。 | 史实 |
| 南天竺与师子国 | 师子国 | 室利那伽 | 师子国宝石剑 | 稀有 | 剑 | 「宝石之岛」宝石之岛：以宝物赏赐 / 外交时价值 ×1.25；在本势力城中搜索宝物的几率 ×1.5。 | 史实 |
| 天竺与贵霜 | 帕拉塔 | 米拉塔赫马 | 卍纹铜剑 | 稀有 | 剑 | 「卍字吉祥」吉祥卍纹：敌军对其的会心率降低 40%。 | 考古 |
| 塞北 | 拓跋 | 诘汾 | 天女金刀 | 稀有 | 刀 | 「天女之赐」天女所赐：持有者获得的经验 +25%；相邻友军每日回复最大兵力 3%。 | 史实 |
<!-- /GENERATED:RULERS -->

---

## 11. Signature historical weapons and gear

<!-- GENERATED:SIGNATURE (gen-md.js; do not edit by hand) -->
| 开局持有者 | 物品 | 品级 | 部位 | 剧本 | 能力 | 典据 |
|---|---|---|---|---|---|---|
| 吕布 | 赤兔 | 传说 | 坐骑·战马 | 经典 + 世界 | 「驰城飞堑」 | 三国志·魏书·吕布传；裴注引《曹瞒传》 |
| 关羽 | 青龙偃月刀 | 史诗 | 兵器·长柄刀 | 经典 + 世界 | 「冷艳锯」 | 三国演义第一回；三国志·蜀书·关羽传 |
| 吕布 | 方天画戟 | 史诗 | 兵器·戟 | 经典 + 世界 | 「画戟横空」 | 三国志·魏书·吕布传；三国演义第三回 |
| 张飞 | 丈八蛇矛 | 史诗 | 兵器·枪矛 | 经典 + 世界 | 「据水断桥」 | 三国志·蜀书·张飞传；三国演义第一回 |
| 兀突骨 | 乌戈藤甲 | 史诗 | 铠甲·藤甲 | 经典 + 世界 | 「刀箭不入」 | 三国演义第九十回 |
| 赵云 | 涯角枪 | 稀有 | 兵器·枪矛 | 经典 + 世界 | 「一身是胆」 | 三国志·蜀书·赵云传 裴注引《云别传》 |
| 赵云 | 照夜玉狮子 | 稀有 | 坐骑·战马 | 经典 + 世界 | 「单骑救主」 |  |
| 马超 | 虎头湛金枪 | 稀有 | 兵器·枪矛 | 经典 + 世界 | 「锦马超」 | 三国志·蜀书·马超传 裴注引《山阳公载记》；三国演义第五十八回 |
| 马超 | 狮盔 | 稀有 | 头盔·缨盔 | 经典 + 世界 | 「银甲白袍」 | 三国演义第五十八回 |
| 黄忠 | 凤嘴刀 | 稀有 | 兵器·长柄刀 | 经典 + 世界 | 「一战斩渊」 | 三国志·蜀书·黄忠传 |
| 黄忠 | 宝雕弓 | 稀有 | 副手·弓 | 经典 + 世界 | 「宝雕穿札」 | 三国演义第五十三回 |
| 典韦 | 双铁戟 | 稀有 | 兵器·双戟 | 经典 + 世界 | 「帐下壮士」 | 三国志·魏书·典韦传 |
| 许褚 | 虎痴斩马刀 | 稀有 | 兵器·斩马刀 | 经典 + 世界 | 「虎痴」 | 三国志·魏书·许褚传 |
| 太史慈 | 太史慈手戟 | 稀有 | 兵器·手戟 | 经典 + 世界 | 「项上手戟」 | 三国志·吴书·太史慈传 |
| 孙策 | 小霸王兜鍪 | 稀有 | 头盔·盔 | 经典 + 世界 | 「神亭兜鍪」 | 三国志·吴书·太史慈传 |
| 孙策 | 霸王枪 | 稀有 | 兵器·枪矛 | 经典 + 世界 | 「江东小霸王」 | 三国演义第十五回 |
| 孙坚 | 赤罽帻 | 稀有 | 头盔·缨盔 | 经典 + 世界 | 「脱帻代主」 | 三国志·吴书·孙破虏传 |
| 程普 | 铁脊蛇矛 | 稀有 | 兵器·枪矛 | 经典 + 世界 | 「江东宿将」 | 三国演义第五回；三国志·吴书·程普传 |
| 黄盖 | 黄盖铁鞭 | 稀有 | 兵器·鞭 | 经典 + 世界 | 「苦肉火船」 | 三国志·吴书·周瑜传；三国演义第五回 |
| 祖茂 | 祖茂双刀 | 稀有 | 兵器·双刀 | 经典 + 世界 | 「代主赴死」 | 三国演义第五回；三国志·吴书·孙破虏传 |
| 甘宁 | 锦帆铃 | 稀有 | 宝物·鼓角 | 经典 + 世界 | 「闻铃知宁」 | 三国志·吴书·甘宁传 裴注引《吴书》 |
| 周瑜 | 周郎琴 | 稀有 | 宝物·乐器 | 经典 + 世界 | 「曲有误周郎顾」 | 三国志·吴书·周瑜传 |
| 诸葛亮 | 白羽扇 | 稀有 | 兵器·羽扇 | 经典 + 世界 | 「指麾三军」 | 裴启《语林》（太平御览引） |
| 华佗 | 青囊书 | 稀有 | 宝物·医药 | 经典 + 世界 | 「此可以活人」 | 三国志·魏书·方技传（华佗）；三国演义第七十八回 |
| 曹操 | 孟德新书 | 稀有 | 宝物·兵书 | 经典 + 世界 | 「过目成诵」 | 三国志·魏书·武帝纪 裴注引《魏书》；三国演义第六十回 |
| 曹操 | 绝影 | 稀有 | 坐骑·战马 | 经典 + 世界 | 「负箭救主」 | 三国志·魏书·武帝纪 裴注引《魏书》 |
| 公孙瓒 | 公孙白马 | 稀有 | 坐骑·战马 | 经典 + 世界 | 「白马义从」 | 后汉书·公孙瓒传 |
| 徐晃 | 开山大斧 | 稀有 | 兵器·大斧 | 经典 + 世界 | 「周亚夫之风」 | 三国志·魏书·徐晃传 |
| 纪灵 | 三尖两刃刀 | 稀有 | 兵器·三叉 | 经典 + 世界 | 「三尖两刃」 | 三国演义第十六回 |
| 沙摩柯 | 铁蒺藜骨朵 | 稀有 | 兵器·锤杵 | 经典 + 世界 | 「五溪蛮王」 | 三国演义第八十三回 |
| 祝融 | 祝融飞刀 | 稀有 | 副手·投掷 | 经典 + 世界 | 「五口飞刀」 | 三国演义第九十回 |
| 高顺 | 陷阵营旗 | 稀有 | 宝物·旗帜 | 经典 + 世界 | 「每攻必破」 | 三国志·魏书·吕布传 裴注引《英雄记》 |
| 蔡琰 | 焦尾琴 | 稀有 | 宝物·乐器 | 世界 | 「焦尾清音」 | 后汉书·蔡邕传 |
| 莫护跋 | 金步摇冠 | 稀有 | 头盔·王冠 | 世界 | 「步摇慕容」 | 晋书·慕容廆载记；北票房身村出土金步摇 |
| 轲比能 | 轲比能铠楯 | 稀有 | 铠甲·札甲 | 世界 | 「教作铠楯」 | 三国志·魏书·乌丸鲜卑东夷传 |
| 塞维鲁 | 第十四军团鹰旗 | 稀有 | 宝物·旗帜 | 世界 | 「鹰旗所向」 | Cassius Dio 74.14；Carnuntum legionary fortress |
| 康茂德 | 涅墨亚狮皮 | 稀有 | 铠甲·兽皮彩绘 | 世界 | 「刀枪不入」 | Herodian 1.14；Cassius Dio 73.15–17 |
| 范师蔓 | 扶南大舶 | 稀有 | 宝物·器械 | 世界 | 「穷涨海」 | 梁书·诸夷传（扶南） |
| 巴戴桑 | 诸国律书 | 稀有 | 宝物·典籍 | 世界 | 「诸国律法」 | Book of the Laws of Countries |
<!-- /GENERATED:SIGNATURE -->

Who holds the famous Romance weapons at the start and how to get them:

| Weapon | Holder (190) | How to obtain |
|---|---|---|
| 青龙偃月刀 | 关羽 (刘备, 平原) | he is not a ruler, so: capture him, then 登用 (he joins with it), 缴械释放 or 处斩 |
| 丈八蛇矛 | 张飞 (刘备) | same |
| 方天画戟 + 赤兔 | 吕布 (董卓, 洛阳) | same; 吕布's loyalty is 70, so 登用 after capture is realistic |
| 双股剑 | 刘备 (ruler) | slay 刘备 (bound) |
| 古锭刀 | 孙坚 (ruler) | slay 孙坚 |
| 倚天剑 | 曹操 (ruler) | slay 曹操; 青釭剑 sits in 曹操's armoury and is lost with his realm or by capture of whoever wields it |
| 涯角枪 + 照夜玉狮子 | 赵云 (公孙瓒) | hire / capture |
| 虎头湛金枪 + 狮盔 | 马超 (马腾) | hire / capture |
| 凤嘴刀 + 宝雕弓 | 黄忠 (在野, 长沙) | find and hire him |
| 双铁戟 | 典韦 (在野, 陈留) | find and hire him |
| 三尖两刃刀 | 纪灵 (袁术) | capture |
| 七星宝刀 | 董卓 (ruler) | slay 董卓 |

---

## 12. Hidden, city, armoury, tournament and shop rares

### 12.1 Hidden, city treasures, starting armouries

<!-- GENERATED:HIDDEN (gen-md.js; do not edit by hand) -->
| 物品 | 品级 | 方式 | 地点 | 基础几率 / 次 | 剧本 | 线索 |
|---|---|---|---|---|---|---|
| 传国玉玺 | 传说 | 搜索 | 洛阳 | 6% + 智力×0.05% | 经典 + 世界 | 洛阳城南甄官井上，旦有五色气，举军惊怪（吴书）。 |
| 射日神弓 | 史诗 | 搜索 | 夷洲 | 8% + 智力×0.06% | 世界 | 夷洲老人说：从前天上有两个太阳…… |
| 天符印 | 史诗 | 搜索 | 国内城 | 6% + 智力×0.06% | 世界 | 国内城的老巫说：太伯山顶神檀树下，曾有天神降临…… |
| 卡维旗 | 史诗 | 搜索 | 伊斯塔克尔 | 6% + 智力×0.06% | 世界 | 伊斯塔克尔的祭司低语：铁匠卡维的皮围裙，就藏在阿娜希塔神庙…… |
| 所罗门之戒 | 史诗 | 搜索 | 马里卜 | 6% + 智力×0.06% | 世界 | 马里卜的老人说：示巴女王当年收到的那封信，是一只戴胜鸟送来的…… |
| 安泰俄斯之带 | 史诗 | 搜索 | 廷吉斯 | 7% + 智力×0.06% | 世界 | 廷吉斯的居民说：城外有巨人安泰俄斯的坟冢，塞多留曾掘开过它…… |
| 太平要术 | 稀有 | 搜索 | 邺 | 5% + 智力×0.05% | 经典 + 世界 | 邺城的老人说：当年张角的徒众，曾在附近埋下一卷天书…… |
| 汉委奴国王金印 | 稀有 | 搜索 | 伊都 | 8% + 智力×0.06% | 世界 | 伊都的渔夫说：志贺岛的石下，埋着汉朝皇帝赐的金子…… |
| 滇王之印 | 稀有 | 搜索 | 建宁 | 8% + 智力×0.06% | 经典 + 世界 | 建宁的巫师说：滇池边的古墓里，有汉帝赐给滇王的金印…… |
| 广陵王玺 | 稀有 | 搜索 | 广陵 | 7% + 智力×0.06% | 经典 + 世界 | 广陵的盐商说：甘泉山下有一座王陵…… |
| 湛卢剑 | 稀有 | 搜索 | 会稽 | 7% + 智力×0.06% | 经典 + 世界 | 会稽山中的铸剑池，传说欧冶子曾在此铸剑…… |
| 鱼肠剑 | 稀有 | 搜索 | 吴 | 7% + 智力×0.06% | 经典 + 世界 | 吴县的渔夫说：当年专诸刺王僚，剑就藏在炙鱼腹中…… |
| 安阳王神弩 | 稀有 | 搜索 | 交趾 | 7% + 智力×0.06% | 世界 | 交趾的老人说：古螺城的安阳王，有一张神人所造的弩…… |
| 卡娜吉足钏 | 稀有 | 搜索 | 马杜赖 | 7% + 智力×0.06% | 世界 | 马杜赖的诗人吟唱：那只装满红宝石的足钏…… |
| 迦腻色伽舍利函 | 稀有 | 搜索 | 富楼沙 | 7% + 智力×0.06% | 世界 | 富楼沙的僧人说：大塔下面供养着迦腻色伽王的舍利函…… |
| 达格达竖琴 | 稀有 | 搜索 | 塔拉 | 7% + 智力×0.06% | 世界 | 塔拉的吟游诗人说：达格达的竖琴会应主人之召自己飞回…… |
| 拓跋神兽 | 稀有 | 搜索 | 阴山 | 8% + 智力×0.06% | 世界 | 阴山的牧人说：先祖南迁时，有一头形似马、声如牛的神兽引路…… |
| 天降神盾 | 史诗 | 镇城之宝 | 罗马 | 随城易主 | 世界 | — |
| 斩白蛇剑 | 稀有 | 镇城之宝 | 洛阳 | 随城易主 | 经典 + 世界 | — |
| 孔子屐 | 稀有 | 镇城之宝 | 洛阳 | 随城易主 | 经典 + 世界 | — |
| 埃拉伽巴路斯圣石 | 稀有 | 镇城之宝 | 埃梅萨 | 随城易主 | 世界 | — |
| 青釭剑 | 稀有 | 开局府库 | 曹操 | — | 经典 + 世界 | — |
| 濊王之印 | 稀有 | 开局府库 | 夫余 | — | 世界 | — |
<!-- /GENERATED:HIDDEN -->

### 12.2 Shop rares and the shop epic

<!-- GENERATED:SHOPRARE (gen-md.js; do not edit by hand) -->
| 物品 | 品级 | 文化 | 类型 | 世界剧本商店 | 经典剧本商店 | 基础价 | 能力 |
|---|---|---|---|---|---|---|---|
| 大宛天马 | 史诗 | 贵霜·大宛 | 战马 | 大宛 | 武威 | 8000 | 「汗血千里」 |
| 尚方斩马剑 | 稀有 | 汉 | 斩马刀 | 洛阳 | 洛阳 | 1400 | 「断佞臣」 |
| 龙渊剑 | 稀有 | 汉 | 剑 | 汝南 | 汝南 | 1400 | 「龙渊淬火」 |
| 宛冯剑 | 稀有 | 汉 | 剑 | 宛 | 宛 | 1400 | 「宛冯利刃」 |
| 百辟刀 | 稀有 | 汉 | 刀 | 许昌 | 许昌 | 1400 | 「百辟五刀」 |
| 蒲元神刀 | 稀有 | 汉 | 刀 | 汉中、成都 | 汉中、成都 | 1400 | 「蜀江淬刃」 |
| 大黄弩 | 稀有 | 汉 | 弩 | 长安 | 长安 | 1190 | 「大黄射将」 |
| 蜀锦战袍 | 稀有 | 汉 | 袍服 | 成都 | 成都 | 1610 | 「锦官城」 |
| 的卢 | 稀有 | 汉 | 战马 | 襄阳 | 襄阳 | 700 | 「一踊三丈」 |
| 乌孙西极马 | 稀有 | 草原 | 战马 | 乌孙、武威 | 武威 | 1750 | 「西极」 |
| 合浦明珠 | 稀有 | 汉 | 珍宝 | 合浦 | 会稽 | 1400 | 「合浦珠还」 |
| 大秦琉璃杯 | 稀有 | 罗马 | 珍宝 | 日南、番禺 | 会稽、建业 | 1400 | 「大秦奇宝」 |
| 犀兕甲 | 稀有 | 汉 | 皮甲 | 交趾、合浦 | 长沙 | 1610 | 「犀兕七属」 |
| 九章算术 | 稀有 | 汉 | 典籍 | 洛阳、长安 | 洛阳、长安 | 1400 | 「方程正负」 |
| 氾胜之书 | 稀有 | 汉 | 典籍 | 长安 | 长安 | 1400 | 「区田法」 |
| 浑天仪 | 稀有 | 汉 | 器械 | 洛阳 | 洛阳 | 1400 | 「候风地动」 |
| 翻车渴乌 | 稀有 | 汉 | 器械 | 洛阳 | 洛阳 | 1400 | 「翻车提水」 |
| 水排 | 稀有 | 汉 | 器械 | 宛 | 宛 | 1400 | 「水排鼓铸」 |
| 孙子兵法竹简 | 稀有 | 汉 | 兵书 | 吴 | 吴 | 1400 | 「不战而屈人」 |
| 太公兵法 | 稀有 | 汉 | 兵书 | 下邳 | 下邳 | 1400 | 「圯上授书」 |
| 铜奔马 | 稀有 | 汉 | 圣物 | 武威 | 武威 | 1400 | 「马踏飞燕」 |
| 楛矢石砮 | 稀有 | 东夷 | 弓 | 挹娄、北沃沮 | — | 1190 | 「矢施毒」 |
| 乌兹钢锭 | 稀有 | 天竺 | 器械 | 巴里加扎、穆济里斯 | — | 1400 | 「乌兹钢」 |
| 政事论 | 稀有 | 天竺 | 典籍 | 华氏城 | — | 1400 | 「政事论」 |
| 铁甲战象 | 稀有 | 天竺 | 战象 | 华氏城、乌贾因、阿努拉陀 | — | 1750 | 「象军冲阵」 |
| 马尔吉亚纳钢剑 | 稀有 | 安息·波斯 | 长剑 | 木鹿 | — | 1400 | 「帕提亚铁」 |
| 尼萨圣马 | 稀有 | 安息·波斯 | 甲马 | 埃克巴坦那、尼萨 | — | 1750 | 「王室圣马」 |
| 杜拉马铠 | 稀有 | 安息·波斯 | 具装 | 泰西封 | — | 1610 | 「人马俱甲」 |
| 帕尔米拉复合弓 | 稀有 | 阿拉伯 | 弓 | 帕尔米拉 | — | 1190 | 「沙漠射手」 |
| 哈特拉毒虫陶罐 | 稀有 | 阿拉伯 | 投掷 | 哈特拉 | — | 840 | 「毒虫陶罐」 |
| 坎迪斯之矛 | 稀有 | 阿克苏姆·库施 | 枪矛 | 麦罗埃 | — | 1400 | 「独眼女王」 |
| 腹弩 | 稀有 | 罗马 | 弩 | 亚历山大 | — | 1190 | 「腹弩」 |
| 骑兵面甲盔 | 稀有 | 罗马 | 面甲盔 | 伦迪尼乌姆、埃博拉库姆 | — | 840 | 「骑兵竞技」 |
| 谋略 | 稀有 | 罗马 | 兵书 | 罗马 | — | 1400 | 「战例汇编」 |
| 盖伦医书 | 稀有 | 罗马 | 医药 | 以弗所、罗马 | — | 1400 | 「盖伦之术」 |
| 地理学 | 稀有 | 罗马 | 典籍 | 亚历山大 | — | 1400 | 「寰宇经纬」 |
| 推罗紫袍 | 稀有 | 罗马 | 袍服 | 安条克、凯撒利亚 | — | 1610 | 「帝王之紫」 |
| 柑橘木桌 | 稀有 | 毛里 | 珍宝 | 约尔、廷吉斯 | — | 1400 | 「价比黄金」 |
| 阿斯图里亚马 | 稀有 | 罗马 | 战马 | 塔拉科、埃梅里塔、科尔杜巴 | — | 1750 | 「溜蹄步」 |
| 比尔比利斯钢剑 | 稀有 | 罗马 | 短剑 | 塔拉科、科尔杜巴 | — | 1400 | 「伊比利亚淬火」 |
| 德斯克福德战号 | 稀有 | 凯尔特 | 鼓角 | 喀里多尼亚 | — | 1400 | 「野猪咆哮」 |
| 萨尔马提亚龙旗 | 稀有 | 萨尔马提亚 | 旗帜 | 雅济吉斯、潘提卡彭 | — | 1400 | 「嘶风布龙」 |
| 马蹄鳞甲 | 稀有 | 萨尔马提亚 | 鳞甲 | 雅济吉斯、奄蔡 | — | 1610 | 「蹄甲」 |
| 套马索 | 稀有 | 萨尔马提亚 | 投掷 | 奄蔡、雅济吉斯 | — | 840 | 「套索擒将」 |
| 中平纪年大刀 | 稀有 | 倭 | 直刀 | 邪马台、伊都 | — | 1400 | 「中平铭刀」 |
| 元戎连弩 | 稀有 | 汉 | 弩 | 成都 | 成都 | 1190 | 「十矢俱发」 |
| 五星锦护膊 | 稀有 | 西域 | 护符 | 精绝 | 武威 | 1400 | 「五星出东方」 |
| 蒂拉丘黄金冠 | 稀有 | 贵霜·大宛 | 王冠 | 蓝氏城 | — | 840 | 「折叠金冠」 |
| 安敦金币 | 稀有 | 罗马 | 珍宝 | 喔㕭 | — | 1400 | 「海上丝路」 |
| 军人公民权铜板 | 稀有 | 罗马 | 印绶 | 卡农图姆、阿昆库姆 | — | 1400 | 「服役期满」 |
| 金官铁甲 | 稀有 | 东夷 | 胸甲 | 金官 | — | 1610 | 「铁之国甲」 |
<!-- /GENERATED:SHOPRARE -->

### 12.3 Commons and uncommons per culture

<!-- GENERATED:CULTURE (gen-md.js; do not edit by hand) -->
| 文化 | 普通 | 精良 | 稀有 | 史诗 / 传说 | 兵器 | 装备 | 世界剧本本文化城数 | 举例（普通 / 精良） |
|---|---|---|---|---|---|---|---|---|
| 汉（han） | 40 | 30 | 78 | 5 | 82 | 71 | 49 | 环首铁刀、精铁长刀、汉铁剑、卅湅环首刀、玉具剑 |
| 南中（nanman） | 13 | 3 | 4 | 1 | 10 | 11 | 1 | 南中蛮刀、滇式铜钺、蛇首铜矛、石寨山饰人铜矛、虎皮披 |
| 倭（wa） | 11 | 5 | 4 | 1 | 12 | 9 | 5 | 素环头大刀、细形铜剑、倭铁矛、倭铁大刀、朱漆长木弓 |
| 夷洲（yi） | 7 | 2 | 0 | 1 | 6 | 4 | 1 | 鹿角矛、青石斧、青石镞弓、夷洲长竹弓、鹿角长矛 |
| 东夷（korea） | 12 | 8 | 8 | 1 | 15 | 14 | 10 | 韩式环头大刀、韩铁剑、东夷铁矛、金铜环头大刀、乐浪檀弓 |
| 草原（steppe） | 14 | 4 | 9 | 1 | 14 | 14 | 8 | 胡刀、胡式短剑、鲜卑骑矛、响箭角弓、鹰羽冠 |
| 西域（tarim） | 8 | 7 | 4 | 1 | 10 | 10 | 10 | 西域铁剑、服刀、西域矛、龟兹铁甲、焉耆马 |
| 贵霜·大宛（kushan） | 12 | 9 | 4 | 1 | 11 | 15 | 10 | 贵霜长剑、贵霜骑矛、塞种战斧、金饰贵霜长剑、贵霜权杖锤 |
| 南海（seasia） | 12 | 3 | 3 | 1 | 10 | 9 | 9 | 林邑长矛、扶南铁刀、南海木弓、金柄扶南刀、扶南金锭 |
| 天竺（india） | 18 | 5 | 9 | 1 | 15 | 18 | 16 | 阿西剑、维尔矛、伽陀锤、尼斯特林沙剑、番红漆竹弓 |
| 安息·波斯（persia） | 12 | 6 | 8 | 2 | 15 | 13 | 19 | 安息长矛、安息铁锤、安息长剑、金箍安息长矛、回马射弓 |
| 阿拉伯（arab） | 12 | 4 | 11 | 1 | 15 | 13 | 11 | 阿拉伯长矛、阿拉伯直剑、阿拉伯弓、波斯特拉复合弓、帕尔米拉具装 |
| 阿克苏姆·库施（africa） | 11 | 5 | 2 | 1 | 9 | 10 | 5 | 阿克苏姆长矛、麦罗埃铁剑、棕榈木长弓、阿克苏姆金冠、非洲森林象 |
| 罗马（roman） | 27 | 13 | 16 | 2 | 17 | 41 | 31 | 庞贝式短剑、骑兵长剑、罗马长矛、西班牙短剑、鹰首骑兵长剑 |
| 凯尔特（celt） | 12 | 7 | 3 | 1 | 9 | 14 | 3 | 凯尔特长剑、凯尔特长矛、凯尔特投矛、堡垒投石索、凯尔特锁子甲 |
| 日耳曼（german） | 11 | 6 | 2 | 1 | 11 | 9 | 4 | 弗雷亚矛、日耳曼战斧、火硬木棒、缴获的骑兵长剑、六角大盾 |
| 萨尔马提亚（sarmatian） | 9 | 2 | 5 | 1 | 8 | 9 | 3 | 萨尔马提亚长矛、萨尔马提亚长剑、萨尔马提亚弓、萨尔马提亚具装、金箍萨尔马提亚长矛 |
| 毛里（moor） | 9 | 2 | 2 | 1 | 6 | 8 | 1 | 毛里标枪、毛里短矛、象皮圆盾、努米底亚骏马、毛里铁头标枪 |
<!-- /GENERATED:CULTURE -->

---

## 13. AI

### 13.1 Valuation

```
score(gen, item) = Σ_k W[role(gen)][k] × stats[k]  +  abilityValue(item, gen)  −  (req not met or shield without one-hand ? ∞ : 0)
role(gen): 'ruler' (adds the ruler row), 'war' (war ≥ intel and war ≥ pol, or war ≥ 70), 'intel', 'pol' (governor with pol ≥ 70 counts as pol)
abilityValue = 6 × tier(item) + 4 if the ability's scope matches the role (battle/duel for war, tactics for intel, domestic for pol, world for ruler)
```

| role | main weights (per point / per %) |
|---|---|
| war | war 3 · atk 2.0 · def 1.8 · crit 2.5 · critDmg 0.5 · charge 0.6 · first 0.5 · counter 0.5 · counterGuard 0.8 · armorBreak 1.0 · morale 0.5 · awe 0.6 · mob 12 · escape 0.15 · dHp 0.6 · dDmg 1.5 · dGuard 0.8 · critRes 1.5 |
| intel | intel 3 · tac 1.5 · tacHit 2.5 · def 1.0 · atk 0.6 · search 1.0 · discord 1.0 · persuade 1.0 |
| pol | pol 3 · land / ind / town 1.2 · recruit 0.8 · train 1.5 · hire 1.5 · trade 1.0 · dip 1.5 · loyal 10 · reward 2 · heal 0.5 |
| ruler (added) | fame 4 · virtue 3 · dip 2 · hire 1 |

### 13.2 Equipping

On any change of the faction's items (purchase, spoils, start, hire) and once a month: generals sorted by `(isRuler ? 1000 : 0) + war + intel + pol + troops / 100`; for each, each slot gets the best-scoring available item if it beats the current one by ≥ 5%. The AI never moves bound items and avoids taking a signature item from its holder unless the receiver is the ruler.

### 13.3 Buying

```
budget(f)  = max(0, Σ city gold − (300 + 150 × cities(f))) × 0.15          per month
purchases  ≤ 2 per month (a rare counts 2); payment from the shop city's gold, which must stay ≥ 200
candidate  = stock entries in own cities (and caravans);  Δ = max over own generals of score(new) − score(current)
buy the best Δ / price if Δ > 2 and price ≤ budget; equip at once
```

Rares only if the faction has ≥ 3 cities or ≥ 1500 gold in that city; the epic only for factions with ≥ 8 cities. Targets (§17.4): AI spends 8–15% of income on gear; by 193 a median AI general with war ≥ 60 has ≥ 2 items.

### 13.4 Selling and armoury

The AI sells armoury overflow (lowest value first) when above 30 items, and sells commons that no general would equip.

### 13.5 Using abilities

宝具 use the v3 `aiSpecial` value (the item actives are ordinary specials to the AI). `throwAttack` and `hitRun` are evaluated in `planAction` like attacks. Duel abilities are passive for the AI.

### 13.6 Hunting treasure

AI target scores: +10% against a faction whose ruler holds an epic or legendary (it wants it), on top of `aggro`. AI factions owning a city with a hidden item search there at least every third month.

### 13.7 Cost

AI shopping and equipping for 70 factions ≤ 3 ms per month (scores cached per (general, item), recomputed on change).

---

## 14. Non-battle fun (requests 9, 13, 14)

### 14.1 Character menu (request 9) — equipment tab 「装备」

- Opened from the Generals button / list and from any portrait. Layout: the general's 3D figure in the centre (Lanternvale lighting, slow turntable, wearing his gear), the six slot tiles around it (weapon and off-hand left, armour / helmet right, mount and treasure below), a stats column (effective vs base attributes, army and duel numbers: attack, defence, crit, mobility, HP, reach), the abilities of equipped items with 「演示」 buttons (plays the fx on a mini board / dummy).
- Tap a slot → candidates from the armoury (and from the current city's shop with prices) with compare deltas; requirement failures greyed with the reason.
- API for the character-menu task: `SG.Gear.slots(gen) → { weapon: inst|null, … }`, `SG.Gear.candidates(gen, slot) → [{ inst, delta, ok, why }]`, `SG.Gear.equip(gen, inst) → { ok, why, loyaltyDelta }`, `SG.Gear.unequip(gen, slot)`, `SG.Gear.summary(gen) → { eff, army, duel, abilities }`, `SG.ItemArt.icon(id, size)`, `SG.ItemArt.preview(gen, opts)`.

### 14.2 Rewards outside battle (request 13)

- **Shopping** as a monthly ritual: every city's market changes; foreign goods arrive with caravans (§6.5); a rare's appearance is announced in the news (「襄阳市中有人牵来一匹额有白点的骏马……」).
- **Treasure hunting**: 17 hidden items with clues tied to history; 搜索 gets a big, exciting result screen when it finds one (request 6: good results get a better animation).
- **Gifting**: items are the strongest loyalty tool (§6.6) and an alliance currency; jewels (玉璧, 蜀锦, 乳香, 胡椒, 珍珠, 象牙, 琥珀, 柑橘木桌 …) exist mainly for this.
- **Domestic gear**: books, seals and tools make 开发, 征兵, 登用, 外交 visibly better; a governor's kit becomes something to build up (水排 + 九章算术 on a 政 general in 宛 = +37% 产业 per 开发).
- **图鉴 collection**: every item ever owned or seen in a market is recorded (`G.itemSeen`). Completion rewards per culture page (all its commons and uncommons owned once): ruler 人望 +1 (max +5 over all pages); all 24 epic + legendary seen: a title-screen seal. Each entry shows the history card (source note and references) — the game's encyclopedia of ancient arms.
- **Market festival** (庙会): every year in month 3 all own markets get +2 slots and −10% prices for the month.

### 14.3 武林大会 (request 14)

§8.9 gives the prize pools. The stadium shows the champion's prize item on a pedestal during the final, and the 金刚杵 in the main gate (the three-title relic). Equipment counts in tournament duels (they are duels).

---

## 15. Save data (v4)

```js
G.items = {
  next: 1,                                   // next instance uid
  inst: { [uid]: { id, loc: 'gen'|'arm'|'shop'|'city', owner: genId|factionId|cityId, gifted: [genId…], bound: bool, since: monthIndex } },
  hiddenFound: [itemId…], singletonsGiven: [itemId…], tourneyAwarded: [itemId…],
  cooldown: { [cityId]: { [itemId]: monthIndex } },
  seen: { [itemId]: 1 },                     // 图鉴
  titles: { [genName]: n }                   // 武林大会 wins (TOURNAMENT may own this; shared)
}
gen.gear   = { weapon: uid|null, offhand, armour, helmet, mount, treasure }
faction.armoury = [uid…]
city.shop  = { stock: [{ id, qty, price, age }], caravan: { until, stock: [...] } | null }
```

- Instances keep only the catalogue id; stats are read from `SG.ItemsData`, so balance patches apply to old saves.
- Migration from v3 saves: run §8.10 kits, signature, ruler-unique, armoury and city-treasure placement on load; hidden items unfound; shops restocked for the current month. `GameState.migratedFrom` notes it.
- Size: ~1 500 instances × ~45 bytes ≈ 70 KB in the world scenario.

---

## 16. Integration points (by file and function)

| File | Function | Change |
|---|---|---|
| `model.js` | `newGeneral` / `newFaction` / `newCity` | `gear`, `armoury`, `shop` defaults |
| | `GameState.newGame` | after `init()`: `SG.Items.setup(g)` (kits, signature, uniques, armouries, city treasures, hidden, first restock) |
| | `toJSON` / `fromJSON` | §15 fields, migration |
| | `Commands.develop/recruit/recruitMax/train/search/hireChance/hire/reward/foodPrice/buyFood/sellFood/allyChance/ally/discord/persuadeChance` | §3.4 hooks via `SG.Gear` (identity when `Items.enabled = false`) |
| | `Progress.gain` | exp multiplier |
| `battle-model.js` | `BattleModel` constructor | start morale, awe, openings, defenderOpening, oath |
| | `atkPower`, `defFactor`, `attack`, `distances`, `BUnit.mobility`, `move` | §3.2 (`movedThisAct`, `attacksThisBattle`) |
| | `endSide` / day start | `applyAuras`, `regen` |
| | `useTactic`, tactic chance, fire damage | tac, tacHit, fireWeak, ward, tacReflect |
| | rout and duel paths | 阵斩 roll (§8.2), capture / escape / rescue / decoy |
| | `settleProgress` | medic, expBoost |
| | `planAction` | `throwAttack`, `hitRun` options |
| `specials.js` | `known`, `validate` | item actives, kinds `plant`, `cloud` |
| `duel-game.js` | `WEAPONS`, `lookOf`, `Fighter` constructor, hit / guard / parry paths, `simulate` | §3.3, §7.4 |
| `strategy-ai.js` | `Conquest.autoResolve` power, `apply` (city treasure transfer, capture escape), `execute` (spoils), `aiDecideCaptives` (confiscation), `endMonth` (income / harvest / auras / restock / caravans / AI shopping / omens), target scoring (aggro, deterrence, treasure hunting), `actCity` (AI search priority) | §3.5, §6, §8, §13 |
| `strategy-screen.js` | `addCmd('市场')`, captive menu, 赏赐 「赐宝物」, 外交 gift picker, search result screen, top-bar or city-panel 府库 entry | §6.7, §8.8 |
| `battle-controller.js` | 宝具 menu line, throw action, spoils prompt after battle | §7.3, §8.2 |
| `figure.js`, `clash.js`, `battle-view.js` | §5 | |

---

## 17. Test plan and balance targets

### 17.1 Catalogue checks (`tests/items.js` §1, also `tools/gen-items.js check`)

All pass today in the design build: ≥ 250 weapons (275) and ≥ 250 gear (292); exactly 4 legendary and 20 epic; every culture has items and an epic / legendary; unique ids, names, ability names and ability effect lists; every rare+ has an ability; every faction of both scenarios has a ruler-unique; every city's pool has ≥ 4 commons including a weapon and an armour piece; every item has a route in at least one scenario; all stats finite. Added for the implementation: every `active` passes `SG.Specials.validate`; every effect id is in the §7.2 vocabulary with known params; icon tuples unique for rare+; prices within the §4.1 bands; caps respected by every single item; `fig` and duel keys exist (or are listed as new).

### 17.2 Rules (`tests/items.js` §2)

Equip constraints and requirements; base / effective attributes and caps; `duelWar`; COMBAT hook values (`critArmy` ≤ 0.04 etc.); buy / sell / price formula; restock determinism (same seed → same stock; zero `SG.Random` draws); rare cooldown; caravan; hidden search probability (10 000 trials ±1 pp); city-treasure transfer; spoils on 处斩, 阵斩 and 缴械释放; bound items cannot move; gifts (once per instance and general); save / load round trip and v3 migration; `Items.enabled = false` leaves every v3 output identical.

### 17.3 Battle and duel (`tests/sim.js` new section, `tests/duel-bots.js` new section)

| Check | Target |
|---|---|
| sim.js, 300 battles, items off vs on with AI kits only (paired seeds) | attacker win rate within ±3 pp; average battle days within ±1 |
| full set by rarity vs none, equal generals | exchange ratio common 1.10–1.20, uncommon 1.22–1.34, rare 1.35–1.50, epic 1.50–1.70 |
| 宝具 average value (refAction like specials) | rare 1.3–1.8, epic 2.0–2.5, legendary ≤ 2.8 normal attacks |
| 阵斩 | rulers slain in 2–6% of tactical battles they fight; ≤ 25% of world rulers slain by 200 in 100 all-AI runs |
| duel, rare weapon vs none, equal war (60 / 80 / 95) | 55–62% |
| duel, epic weapon | 60–66% |
| duel, legendary weapon | 66–74% |
| duel, best-in-slot gear vs none, 15-war gap | the weaker side ≤ 32% |
| duel abilities | rare ≤ 4 HP per duel average, epic ≤ 8, legendary ≤ 12 (measured as damage / heal delta) |

### 17.4 Economy and AI (`tests/items.js` §3, all-AI runs: 100 world × 20 years, 200 classic × 20 years)

| Check | Target |
|---|---|
| AI gear spending | 8–15% of AI income |
| items per AI general with war ≥ 60 in 193 | median ≥ 2 |
| shops | every city ≥ 4 stock entries after month 1; a given rare appears in its city every 6–10 months on average |
| 传国玉玺 | found by 195 in ≥ 70% of runs (history: 191) |
| faction survival and conquest pace vs items off | within ±10% |
| gold reserves | no AI faction drops below its reserve because of shopping |
| performance | `endMonth` + AI shopping ≤ 8 ms per month (desktop), restock ≤ 1 ms |

### 17.5 Browser

`tests/items.html`: all 567 icons and models render (no `console.error`), triangle budgets, 60 fps turntable on desktop / 30 on phone; the 29 宝具 fx play within 1.5 s and ≤ 2 extra draw calls. `tests/shop.html`: buy / sell / compare / gift flows, 844×390 and 1280×720 layouts, touch targets ≥ 44 px, reduced-motion mode.

---

## 18. Unity port (after the browser build is stable)

| Unity file | Change |
|---|---|
| `Assets/Sanguo/Scripts/Data/ItemsData.g.cs` (new) | generated by `tools/export-unity-items.js` from items.json (catalogue as structs, shops, tables) |
| `Assets/Sanguo/Scripts/Model/Items.cs`, `Gear.cs` (new) | rules port of `js/items.js`; same seeded restock; same `SG.Random` order |
| `GameState.cs`, `Commands.cs`, `BattleModel.cs`, `Conquest.cs`, `StrategyAI.cs`, `DuelSim.cs` | hooks of §16 |
| `UI/ShopScreen.cs`, `UI/ArmouryScreen.cs` (new) | UGUI versions of §6.7 |
| `View/ItemArt.cs` (new) | icon atlas (same SVG shapes rasterised by the exporter) and figure look overrides via `FigureData.g.cs` |
| `Tests~/items/` | catalogue checks and rule harness against JS refs (`node tests/items.js --refs`), exact to 1e-9 |

---

## 19. Open questions

- **Q1.** 阵斩 for all generals? Proposed: rulers only in v4 (so the famous cast survives and uniques stay special). Alternative: all generals at one third of the ruler probabilities, with a setting.
- **Q2.** Crit and duel-damage budget split with TITLES: items 0.04 / 0.05 / 0.04 of COMBAT's 0.06 / 0.08 / 0.06 (army crit / duel crit / duel damage). Agree with the titles task.
- **Q3.** Titles requirements use base attributes (proposed), so gear cannot buy titles. Confirm with TITLES.md.
- **Q4.** Allied markets: only allied cities adjacent to an own city at ×1.15 (proposed), or every allied city?
- **Q5.** Classic scenario 异域奇珍: 15% per tournament from 193, max one epic each — enough exotic flavour without breaking the 190 China setting?
- **Q6.** Should reassigning a signature item away from its holder be allowed at all (proposed: yes, −15 loyalty, refused below 50)?
- **Q7.** Religious and sacred objects (金刚杵, 毗沙门天王戟, 所罗门之戒, 天符印, 卡维旗) are presented as legend with sources and respectful effects. Does the publisher want a cultural review pass before release?
- **Q8.** Should the market building in the city zoom view (request 8) show stock items on its stalls (nice, costs draw calls)?
- **Q9.** Tournament gold amounts and the skill-prize share are proposals; the TOURNAMENT spec decides.
- **Q10.** Mounts in duels change only walk / dash speed (duels stay on foot). Should 赤兔 / 大宛天马 holders duel mounted? Proposed: no (COMBAT's frame data assumes foot).

---

## 20. Sources

Classical texts are cited in each item's `source.refs` (items.json). Web lookups used for verification in this task (direct page fetches of ctext / wikisource / wikipedia were blocked from this session; the following came through search, and quotations should be checked against the 中华书局点校本 / Loeb editions before they appear in the UI):

- 赤兔 / 曹瞒传: [新浪 · 赤兔、的卢、绝影 三国名马](https://news.sina.cn/2026-02-11/detail-inhmmvzf7564749.d.html?vt=4)
- 传国玺 / 甄官井: [汉典 甄官井](https://www.zdic.net/hant/甄官井), [ctext 三国演义第六回](https://ctext.org/sanguo-yanyi/ch6/zhs)
- 孙权六剑（古今注）: [萌娘百科 孙权（原型）](https://zh.moegirl.org.cn/%E5%AD%99%E6%9D%83(%E5%8E%9F%E5%9E%8B))
- 关羽「万人」刀、张飞「新亭侯」刀（古今刀剑录）: [网易 · 古今刀剑录](https://www.163.com/dy/article/L372OGLJ0523FKEA.html), [maigoo 中国古代名刀](https://www.maigoo.com/top/388501.html)
- 曹植《宝刀赋》百辟刀: [secretchina · 刀剑花纹](https://www.secretchina.com/news/gb/2013/01/06/481499.html)
- 晋书·五行志 武库火（斩白蛇剑、孔子屐）: [网易 · 武库火](https://m.163.com/dy/article/L330POHE0553R3NO.html)
- 元戎连弩: [新京报 / 腾讯 · 诸葛连弩](https://news.qq.com/rain/a/20241118A0037W00)
- 青釭剑、倚天剑（演义四十一回）: [萌娘百科 青釭剑](https://zh.moegirl.org.cn/%E9%9D%92%E9%87%AD%E5%89%91)
- 藤甲（演义第九十回）: [NTDTV 三國演義 第九十回](https://www.ntdtv.com/b5/2010/01/10/a385955.html)
- 径路刀 / 月氏王头饮器: [爱思想 · 头骨饮器](https://www.aisixiang.com/data/141373.html), [wikisource 汉书卷94下](https://zh.wikisource.org/zh-hans/%E6%BC%A2%E6%9B%B8/%E5%8D%B7094%E4%B8%8B)
- 混填与柳叶（梁书·扶南）: [国立情报学研究所 · 混填与苏物](https://stars.repo.nii.ac.jp/record/8115/files/KJ00006141347.pdf)
- 朱蒙善射（北史）: [wikisource 北史卷094](https://zh.wikisource.org/wiki/%E5%8C%97%E5%8F%B2/%E5%8D%B7094)
- 天符印（三国遗事）: [Wikipedia Hwanung](https://en.wikipedia.org/wiki/Hwanung)
- 大唐西域记 瞿萨旦那国（毗沙门、鼠神）: [wikisource 大唐西域記 卷十二](https://zh.wikisource.org/wiki/%E5%A4%A7%E5%94%90%E8%A5%BF%E5%9F%9F%E8%A8%98_(%E5%9B%9B%E9%83%A8%E5%8F%A2%E5%88%8A%E6%9C%AC)/%E5%8D%B7%E7%AC%AC%E5%8D%81%E4%BA%8C)
- 史记·大宛列传 天马: [澎湃 · 汗血马](https://m.thepaper.cn/baijiahao_19300031)
- 挹娄: [Wikipedia Yilou](https://en.wikipedia.org/wiki/Yilou)
- Gaya iron armour: [Wikipedia Korean armour](https://en.wikipedia.org/wiki/Korean_armour), [New World Encyclopedia Gaya](https://www.newworldencyclopedia.org/entry/Gaya_confederacy)
- 七支刀: [MLIT 多言語解説 Shichishitō](https://www.mlit.go.jp/tagengo-db/en/R6-00143.html), [Wikipedia Seven-Branched Sword](https://en.wikipedia.com/wiki/Chiljido)
- Kusanagi: [Wikipedia Kusanagi no Tsurugi](https://en.wikipedia.org/wiki/Kusanagi_no_Tsurugi), [JEMNE · The Sword in the Tail](https://jemne.org/issues/15/littleton.php)
- Atayal two suns: [USC Folklore Archive](https://folklore.usc.edu/?p=29585), [Island Folklore · Sundown](https://islandfolklore.com/sundown/)
- Vajra: [Wikipedia Tvashtr](https://en.wikipedia.org/wiki/Tvashtr)
- Herodotus 3.21: [Lexundria Histories 3.21](https://lexundria.com/hdt/3.21/mcly)
- Ammianus 31.2.23: [Perseids Res Gestae 31.2.23](https://cts.perseids.org/read/latinLit/stoa0023/stoa001/perseus-eng2/31.2.22-31.2.23)
- Herodian 3.9 (Hatra insect pots): [Livius Herodian 3.9](https://www.livius.org/sources/content/herodian-s-roman-history/herodian-3.9), [OUP blog · scorpion bombs](https://blog.oup.com/2014/12/scorpion-bombs-isis-insect-fear-terror/)
- Commodus as Hercules: [World History Encyclopedia Commodus](https://www.worldhistory.org/Commodus/)
- Spear of Lugh, Lia Fáil: [Wikipedia Four Treasures of the Tuatha Dé Danann](https://en.wikipedia.org/wiki/Four_Treasures_of_the_Tuatha_D%C3%A9_Danann)
- Ancile: [Wikipedia Ancile](https://en.wikipedia.org/wiki/Ancile)
- Pausanias 1.21.6 (hoof armour): [Perseids Description of Greece 1.21.6](https://cts.perseids.org/read/greekLit/tlg0525/tlg001/perseus-eng2/1.21.6-1.22.8)
- Pliny NH 34.145 (Seric / Parthian iron): [Schoff, The Eastern Iron Trade of the Roman Empire](https://penelope.uchicago.edu/Thayer/E/Journals/JAOS/35/The_Eastern_Iron_Trade_of_the_Roman_Empire*.html)
- Periplus (Adulis ivory, Indian steel): [Fordham Periplus](https://fordham.edu/halsall/ancient/periplus.asp)
- Tacitus Germania 6: [Harland · Germania in full](https://www.philipharland.com/Blog/2022/07/germans-tacitus-germania-in-full-late-first-century-ce/)
- Donar amulets: [Wikipedia Thor](https://Www.wikipedia.org/wiki/Thor), [Norse Mythology substack · Thunder goes clubbing](https://norsemythology.substack.com/p/the-germanic-thunderweapon-part-ii)
- Derafsh Kaviani: [Wikipedia Derafsh Kaviani](https://en.wikipedia.org/wiki/Derafsh_Kaviani)
- Plutarch Sertorius 9 (Antaeus): [Lexundria Life of Sertorius 9](https://lexundria.com/plut_sert/9/prr)
- Caledonians (Dio 77.12): [Wikipedia Caledonians](https://en.wikipedia.org/wiki/Caledonians)
- Huvishka coins: [Wikipedia Kushan coinage](https://en.wikipedia.org/wiki/Kushan_coinage), [British Museum C.1894,0506.1501](https://www.britishmuseum.org/collection/object/C_1894-0506-1501)
- Sangam emblems / Karikala: [Drishti IAS · Sangam Age](https://www.drishtiias.com/to-the-points/paper1/sangam-age-1)
- Apedemak, Ezana: [Wikipedia Apedemak](https://en.wikipedia.org/wiki/Apedemak), [Wikipedia Ezana Stone](https://en.wikipedia.org/wiki/Ezana_Stone)
- 冒顿鸣镝: [chinesenotes 鸣镝](https://chinesenotes.com/words/33936.html)
