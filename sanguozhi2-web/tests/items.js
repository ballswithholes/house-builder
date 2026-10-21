'use strict';
/* ==========================================================================
   tests/items.js — 第四版装备与商店（ITEMS.md §17；INTEGRATION §7、§8.3、§13）
     §1 目录：tools/gen-items.js check 的全部检查（SG.Items.checkCatalogue）+ 数量、白名单载入即报错、宝具都能结算
     §2 规则：起始装备、装备要求、有效属性与上限、阵型读基础属性、COMBAT 钩子、价格与买卖、补货的确定性与零 SG.Random、
        珍品冷却、胡商、隐藏宝物概率（10 000 次 ±1 pp）、镇城之宝易主、战利品（处斩 / 阵斩 / 缴械）、专属不能动、
        赐宝物每件只算一次（10 次赐收 = 0）、存档往返与 v3 迁移、关掉时与 v3 逐位相同、单挑预算、奖池
     §3 经济与电脑（全电脑战役）：买装占总收入 8–15 %、193 年武力 ≥ 60 的电脑武将中位装备件数 ≥ 2、每城 ≥ 4 件货、
        珍品在本城每 6–10 个月出现一次、传国玉玺 195 年前被找到 ≥ 70 %、自由支出后无城跌破 0、每月耗时
     §4 战斗：整套装备的交换比（按品级）、宝具的平均价值（参照一次普通攻击）；§4b 电脑对电脑战术战斗装备关 / 开（攻方胜率 ±3 pp、天数 ±1）
   用法：node tests/items.js [--quick]      快速档（约 1 分钟）：§3 经典 12 种子 × 5 年 + 世界 2 种子 × 3 年，另「全开」经典 4 × 8、世界 2 × 3
         node tests/items.js --full         夜间档：经典 40 × 20 年、世界 10 × 20 年（ITEMS §17.4 的 200 / 100 局按需加 --seeds）
         选项 --seeds N（经典种子数）、--years N、--only §4b,§3（只跑这几节）
   FLAG 行：没过的规格门槛——规格的预期与规格的算法 / 数据对不上、要规格作者定的数（快速档不算失败，夜间档算；报告列出）
   退出码 = 失败数（--full：失败数 + FLAG 数）
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');
const crypto = require('crypto');
const { load, ROOT } = require('./v4-load.js');

const ARGS = process.argv.slice(2);
const FULL = ARGS.includes('--full') || ARGS.includes('--nightly');
const argN = (k, d) => { const i = ARGS.indexOf(k); return i >= 0 ? +ARGS[i + 1] : d; };
let fails = 0, checks = 0, flags = 0;
const T0 = Date.now();
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } return !!c; }
function eq(a, b, msg) { return ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function near(a, b, tol, msg) { return ok(Math.abs(a - b) <= tol, msg + ' (got ' + a + ', want ' + b + ' ± ' + tol + ')'); }
function info(s) { console.log('  ' + s); }
// 规格与实现对不上、要规格作者决定的数（不算失败，单独计数，报告里列出）
function flag(c, msg) { if (!c) { flags++; console.log('  FLAG ' + msg); } return !!c; }
const ONLY = (() => { const i = ARGS.indexOf('--only'); return i >= 0 ? ARGS[i + 1].split(',') : null; })();   // --only §4b,§2c（按节名开头）
function section(name, fn) {
  if (ONLY && !ONLY.some(k => name.indexOf(k + ' ') === 0)) return;
  const t0 = Date.now();
  console.log('== ' + name);
  try { fn(); } catch (e) { fails++; console.log('  FAIL (threw) ' + (e.stack || e)); }
  const ms = Date.now() - t0;
  if (ms > 800) console.log('  (' + ms + ' ms)');
}
const median = a => { const b = a.slice().sort((x, y) => x - y); return b.length ? b[b.length >> 1] : 0; };
const avg = a => (a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0);

const SG = load({ v4: true, extra: ['items-data.js', 'items.js'] });
const { GameState, StrategyAI, Items, Gear, Mods } = SG;
const D = SG.ItemsData;
if (!Items || !D) { console.log('FAIL: js/items.js or js/items-data.js not loaded'); process.exit(1); }
// 只测装备：别的第四版模块（若在场）关掉，免得它们的数字混进来（INTEGRATION §1.2 的开关）
for (const n of ['Titles', 'Engage', 'Tourney']) if (SG[n] && typeof SG[n] === 'object' && 'enabled' in SG[n]) SG[n].enabled = false;
function newGame(key, seed, scen) {
  SG.ScenarioData = SG.Scenarios[scen || 'classic'];
  SG.Random.seed(seed);
  return (SG.G = GameState.newGame(key, scen || 'classic'));
}
const byName = (g, n) => g.generals.find(x => x.name === n);
const itemOn = (x, sl) => { const it = Gear.item(x, sl); return it ? it.id : null; };
function months(g, n) { for (let m = 0; m < n; m++) { StrategyAI.runAI([]); StrategyAI.endMonth(); } }

// ======================================================================================= §1 catalogue ==
section('§1 catalogue (ITEMS §17.1; tools/gen-items.js check)', () => {
  const rep = Items.checkCatalogue();
  for (const p of rep.problems.slice(0, 30)) ok(false, p);
  eq(rep.problems.length, 0, 'catalogue problems');
  eq(D.items.length, 602, '602 items');
  eq(rep.stats.weapons, 310, '310 weapons'); eq(rep.stats.gear, 292, '292 armour / equipment');
  eq(JSON.stringify(rep.stats.byRarity), JSON.stringify({ common: 273, uncommon: 133, rare: 172, epic: 20, legendary: 4 }), 'rarities');
  eq(D.items.filter(x => x.rarity === 'legendary' && x.availability.classic).length, 3, 'classic has 3 legendaries (赫拉克勒斯之棍 world-only)');
  ok(rep.stats.reach.classic.weapons >= 255 && rep.stats.reach.classic.gear >= 255, `classic ≥ 255 / ≥ 255 outside the 15 % roll (${rep.stats.reach.classic.weapons} / ${rep.stats.reach.classic.gear})`);
  eq(rep.stats.abilities, 196, '196 unique abilities'); eq(rep.stats.actives, 29, '29 items with a 宝具');
  eq(D.items.filter(x => x.availability.world && x.availability.world.kind === 'ruler').length, 70, '70 world ruler-uniques');
  eq(D.items.filter(x => x.availability.classic && x.availability.classic.kind === 'ruler').length, 18, '18 classic ruler-uniques');
  ok(!!D.items.find(x => x.name === '吉祥纹铜剑'), 'safe name 吉祥纹铜剑 kept (DESIGN-V4 §20 Q4)');
  eq(Object.keys(D.kitWeapon).length > 450, true, 'kit weapon table frozen at generation (' + Object.keys(D.kitWeapon).length + ' generals, from ' + D.kitWeaponSource + ')');
  // kitWeapon 与当前 figure.js 一致（美术推广改了外观的兵器之后要重跑 node tools/gen-items.js js）
  try {
    const kd = require(path.join(ROOT, 'tools', 'gen-items.js')).kitDrift(JSON.parse(fs.readFileSync(path.join(ROOT, 'design-v4', 'items.json'), 'utf8')));
    if (kd.skipped) info('kitWeapon drift check skipped: ' + kd.skipped);
    else ok(kd.changed.length === 0, 'kitWeapon matches the current SG.Figure.lookOf (' + kd.n + ' generals)' + (kd.changed.length ? ': ' + kd.changed.slice(0, 8).join(', ') : ''));
  } catch (e) { ok(false, 'kitWeapon drift check threw: ' + e.message); }
  // duelWeapons（style / reach / pow）与每件兵器自己的 duel 数都与 duel-game.js 的 WEAPONS 一致（W5 的兵器平衡为准；改了之后要重跑 node tools/gen-items.js js）
  try {
    const dd = require(path.join(ROOT, 'tools', 'gen-items.js')).duelDrift(JSON.parse(fs.readFileSync(path.join(ROOT, 'design-v4', 'items.json'), 'utf8')));
    if (dd.skipped) info('duelWeapons drift check skipped: ' + dd.skipped);
    else ok(dd.changed.length === 0, 'duelWeapons and every weapon\'s own duel style / reach / pow match duel-game.js WEAPONS (' + dd.n + ' keys, ' + dd.nItems + ' weapons)' + (dd.changed.length ? ': ' + dd.changed.slice(0, 8).join(', ') : ''));
    eq(D.duelWeaponsSource, 'duel-game.js', 'duelWeapons generated from duel-game.js');
  } catch (e) { ok(false, 'duelWeapons drift check threw: ' + e.message); }
  // giftK 的作用范围写在数据里（以宝物赏赐 = 持有者；以之赏赐 = 本件）
  const gk = D.items.filter(it => it.ability && it.ability.effects.some(e => e.t === 'giftK'));
  ok(gk.length === 10 && gk.every(it => it.ability.effects.filter(e => e.t === 'giftK').every(e => typeof e.holder === 'boolean' && e.holder === /以宝物/.test(it.ability.desc))), 'giftK effects carry holder scope (' + gk.filter(it => it.ability.effects.some(e => e.holder)).length + ' holder / ' + gk.length + ')');
  // 白名单之外的单挑字段：载入即报错（COMBAT-V4 §15 (15)）
  const bad = JSON.parse(JSON.stringify(D)); bad.items.find(x => x.id === 'yuenv').ability.effects.push({ t: 'dUnblockable', k: 1 });
  const ctx = { console: { log() {}, error() {} }, Math, JSON, Date }; ctx.window = ctx; vm.createContext(ctx);
  for (const f of ['core.js', 'registry.js', 'data.js', 'world-data.js', 'econ.js', 'model.js']) vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', f), 'utf8'), ctx);
  ctx.SG.ItemsData = bad;
  let threw = null; try { vm.runInContext(fs.readFileSync(path.join(ROOT, 'js', 'items.js'), 'utf8'), ctx); } catch (e) { threw = e; }
  ok(threw && /DUEL_ITEM_EFFECTS/.test(threw.message), 'a non-whitelisted duel field throws at load');
  // 每个宝具都变成规则层认得的招式（SG.Specials.byId），参数在机制表 + 宝具扩展范围之内
  let n = 0;
  for (const it of D.items) if (it.ability) it.ability.effects.forEach((e, i) => {
    const ids = e.t === 'active' ? ['item:' + it.id + ':' + i] : e.t === 'activeChoice' ? e.options.map((o, j) => 'item:' + it.id + ':' + i + ':' + j) : [];
    for (const id of ids) {
      const sp = SG.Specials.byId(id); n++;
      if (!ok(sp && SG.Specials.KINDS[sp.kind] && sp.itemActive && sp.pool && sp.tier === null && SG.Specials.FX.indexOf(sp.fx) >= 0, 'item active resolves: ' + id)) continue;
      ok(sp.name && sp.desc && /^#[0-9a-f]{6}$/.test(sp.color), 'item active has name / desc / colour: ' + id);
    }
  });
  eq(n, 31, '31 宝具 entries (28 actives + 3 options of 天符印)');
  eq(SG.Specials.tierName(null), '宝具', 'tierName(null) = 宝具');
});

// ======================================================================================= §2 rules ==
section('§2a starting gear (ITEMS §8.3–§8.10)', () => {
  for (const scen of ['classic', 'world']) {
    const g = newGame(scen === 'world' ? 'roma' : 'liubei', 3, scen);
    const n = Object.keys(g.items.inst).length;
    info(`${scen}: ${n} item instances at start (spec ≈ ${scen === 'world' ? 1500 : 500})`);
    ok(n >= (scen === 'world' ? 1200 : 400) && n <= (scen === 'world' ? 2400 : 900), scen + ' instance count plausible');
    const alive = g.generals.filter(x => !x.dead);
    eq(alive.filter(x => !Gear.item(x, 'weapon')).length, 0, scen + ': every general has a weapon');
    eq(alive.filter(x => !Gear.item(x, 'armour')).length, 0, scen + ': every general has armour');
    const T = D[scen];
    for (const f of g.factions) {
      const r = T.rulers[f.key], ru = g.generals[f.ruler];
      const u = ru.gear[Items.get(r.item).slot];
      ok(u != null && Items.inst(u).id === r.item && Items.inst(u).bound, scen + ' ruler ' + ru.name + ' holds his bound ' + r.item);
    }
    for (const s of T.signature) { const x = byName(g, s.holder); ok(x && SGItemsHas(x, s.item), scen + ' signature ' + s.holder + ' holds ' + s.item); }
    const instOfId = id => Object.keys(g.items.inst).filter(u => g.items.inst[u].id === id).map(Number);
    for (const t of T.cityTreasure) { const c = g.cityByKey(t.city); const us = instOfId(t.item); ok(us.length === 1 && Items.factionOf(us[0]) === c.owner, scen + ' city treasure ' + t.item + ' belongs to the owner of ' + c.name + ' (vault or equipped)'); }
    for (const a of T.armoury) { const f = g.factionByKey(a.faction); const us = instOfId(a.item); ok(us.length === 1 && Items.factionOf(us[0]) === f.id, scen + ' starting armoury ' + a.item + ' with ' + f.name); }
    for (const c of g.cities) ok(c.shop.stock.length >= 4, scen + ' ' + c.name + ' has ≥ 4 stock entries at start');
    // 倭无马；文官执扇（汉）
    if (scen === 'world') eq(g.generals.filter(x => x.culture === 'wa' && Gear.item(x, 'mount')).length, 0, '倭 generals have no horses (「其地无牛马」)');
    const civ = alive.filter(x => x.culture === 'han' && x.intel > x.war + 25 && x.war < 55 && !g.isRuler(x) && !D[scen].signature.some(s => s.holder === x.name));
    const bad = civ.filter(x => ['fan', 'staff'].indexOf((Gear.item(x, 'weapon') || {}).type) < 0);
    ok(civ.length > 0 && bad.length <= civ.length * 0.1, 'han civilians carry a fan or staff (' + (civ.length - bad.length) + ' / ' + civ.length + '; the AI may re-equip a few)');
    eq(alive.filter(x => { const o = Gear.item(x, 'offhand'); return o && Items.isShield(o) && Items.styleOf(Gear.item(x, 'weapon')) !== 'one'; }).length, 0, scen + ': no shield with a two-handed weapon');
  }
  const g = newGame('liubei', 5);
  const lb = byName(g, '吕布'), gy = byName(g, '关羽');
  eq(itemOn(lb, 'mount'), 'chitu', '吕布 rides 赤兔'); eq(itemOn(lb, 'weapon'), 'fangtian', '吕布 holds 方天画戟'); eq(itemOn(gy, 'weapon'), 'qinglong', '关羽 holds 青龙偃月刀');
});
function SGItemsHas(x, id) { for (const sl of Items.SLOTS) if (x.gear[sl] != null && Items.inst(x.gear[sl]).id === id) return true; return false; }

section('§2b equip constraints, effective attributes, caps, COMBAT hooks (ITEMS §2–§3)', () => {
  const g = newGame('liubei', 7);
  const f = g.factions[g.player];
  const gy = byName(g, '关羽');
  // 有效属性：98 + 8 = 106；单挑 98 + ⌊8 / 3⌋ = 100（INTEGRATION §8.3）
  eq(Gear.eff(gy, 'war'), 106, '关羽 eff war 106'); eq(Gear.duelWar(gy), 100, '关羽 duelWar 100'); eq(Mods.eff(gy, 'war'), 106, 'SG.Mods.eff uses the items provider');
  near(Mods.z('gearAtk', gy), 0.14, 1e-9, '关羽 gearAtk 14 %');
  gy.injury = { until: g.monthIndex + 2, war: 5, away: false };
  eq(Mods.eff(gy, 'war'), 101, 'eff − injury'); eq(Gear.duelWar(gy), 95, 'duelWar − injury'); delete gy.injury;
  // 上限：+15 与 110
  const x = byName(g, '张飞');
  const give = id => Items.award(f.id, id, { why: 'test' });
  const t = { id: 999, name: '测', war: 100, intel: 20, pol: 20, faction: f.id, city: x.city, loyalty: 80, troops: 1000, training: 50, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
  g.generals.push(t); t.id = g.generals.length - 1;
  for (const id of ['vajra', 'chitu', 'gladiator_helm', 'nemean', 'kaviani']) { const u = give(id); if (u) Gear.equip(t, u); }
  ok(Gear.eff(t, 'war') <= 110, 'eff war ≤ 110 (' + Gear.eff(t, 'war') + ')');
  ok(Gear.raw(t, 'war') <= 15, 'gear war ≤ +15');
  ok(Gear.stat(t, 'critArmy') <= 0.04 + 1e-12, 'critArmy ≤ 0.04'); ok(Gear.stat(t, 'critDuel') <= 0.03 + 1e-12, 'critDuel ≤ 0.03');
  ok(Gear.stat(t, 'critMulArmy') <= 0.30 + 1e-12 && Gear.stat(t, 'critMulDuel') <= 0.30 + 1e-12, 'crit multipliers ≤ 0.30');
  // 阵型读基础属性：70 武 + 15 装备武力仍不能用锋矢（需 85）
  const y = { id: 998, name: '测二', war: 70, intel: 30, pol: 30, faction: f.id, city: x.city, loyalty: 80, troops: 1000, training: 50, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
  g.generals.push(y); y.id = g.generals.length - 1;
  Gear.equip(y, give('clava'), { force: true });
  ok(Gear.eff(y, 'war') >= 82, 'gear raised eff war (' + Gear.eff(y, 'war') + ')');
  eq(SG.BattleModel.prototype.formationAllowed.call({}, { gen: y }, 5), false, '锋矢 (war 85) not unlocked by gear (base attributes)');
  // 盾须单手兵器；换双手兵器时盾回府库
  const z = { id: 997, name: '测三', war: 75, intel: 40, pol: 40, faction: f.id, city: x.city, loyalty: 80, troops: 1000, training: 50, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
  g.generals.push(z); z.id = g.generals.length - 1;
  const sh = give('han_gouxiang'), sp = give('han_mao'), dao = give('han_dao');
  eq(Gear.equip(z, sh).ok, false, 'shield refused without a one-handed weapon');
  ok(Gear.equip(z, dao).ok && Gear.equip(z, sh).ok, 'shield with a dao');
  const r = Gear.equip(z, sp);
  ok(r.ok && z.gear.offhand == null && f.armoury.indexOf(sh) >= 0, 'pole weapon sends the shield to the armoury');
  // 要求：射日神弓 武力 60 不能装备；赤兔 85 以下数值减半；传国玉玺 非君主只有政治 +10
  const w = { id: 996, name: '测四', war: 50, intel: 50, pol: 50, faction: f.id, city: x.city, loyalty: 80, troops: 1000, training: 50, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
  g.generals.push(w); w.id = g.generals.length - 1;
  eq(Gear.equip(w, give('sheri'), {}).ok, false, '射日神弓 refused below 武力 60');
  const ct = Items.award(f.id, 'han_ma', { why: 'test' }); Gear.equip(w, ct);
  const chitu = lbChitu(g);
  Gear.equip(w, chitu, { force: true, free: true });
  near(Gear.raw(w, 'charge'), 7.5, 1e-9, '赤兔 below 85: charge halved'); near(Gear.raw(w, 'escape'), 50, 1e-9, '赤兔 below 85: escape 50 %');
  const seal = give('chuanguoxi');
  Gear.equip(w, seal);
  eq(Gear.raw(w, 'pol'), 10, '传国玉玺 on a non-ruler: 政治 +10'); eq(Gear.raw(w, 'fame'), 0, '… and nothing else');
  eq(Items.actives(w).some(a => a.item === 'chuanguoxi'), false, '… and no 宝具');
  // 专属兵器不能动
  const lbei = g.generals[f.ruler];
  const uq = lbei.gear.weapon;
  eq(Gear.unequip(lbei, 'weapon').ok, false, 'bound ruler-unique cannot be unequipped');
  eq(Items.sell(f.id, uq, g.cities[lbei.city]).ok, false, 'bound ruler-unique cannot be sold');
  eq(Gear.equip(gy, uq).ok, false, 'bound ruler-unique cannot be reassigned');
  eq(Gear.equip(lbei, give('han_dao')).ok, false, 'a ruler cannot swap his bound weapon');
  // 招牌装备：离开原主 −15，忠诚会跌破 50 时拒绝
  gy.loyalty = 60;
  const rr = Gear.unequip(gy, 'weapon');
  eq(rr.ok, false, '关羽 refuses to give up 青龙偃月刀 below 50 loyalty');
  gy.loyalty = 90;
  const r2 = Gear.unequip(gy, 'weapon');
  ok(r2.ok && gy.loyalty === 75, 'signature removal costs 15 (' + gy.loyalty + ')');
});
function lbChitu(g) { const lb = byName(g, '吕布'); const u = lb.gear.mount; return u; }

section('§2c shops: tiers, prices, buy / sell, restock determinism and zero SG.Random, cooldown, merchant (ITEMS §4.3, §6)', () => {
  const g = newGame('cao', 11);
  const f = g.player;
  const xu = g.cities.find(c => c.owner === f);
  // 价格：城价 × 来源；同盟 ×1.15；商才折扣 ≤ 12.5 %
  const e0 = xu.shop.stock[0], it0 = Items.get(e0.id);
  near(e0.price, Math.round((it0.price || it0.value) * Items.cityK(xu) * (e0.foreign ? 1.2 : 1) / 10) * 10, 0, 'list price = round10(price × cityK × origin)');
  ok(Items.cityK(xu) >= 0.85 && Items.cityK(xu) <= 1.15, 'cityK within 0.85–1.15');
  // 买：金从本城出，进府库
  xu.gold = 5000;
  const before = xu.gold, i = xu.shop.stock.findIndex(e => Items.get(e.id).rarity === 'common');
  const pr = Items.entryPrice(xu, xu.shop.stock[i], f);
  const r = Items.buy(f, xu, i);
  ok(r.ok && xu.gold === before - pr && g.factions[f].armoury.indexOf(r.uid) >= 0, 'buy: gold paid, item in the armoury');
  const sp = Items.sellPrice(r.uid, xu);
  near(sp, Math.round((r.item.price) * 0.4 * Items.cityK(xu) / 10) * 10, 0, 'sell price 40 % of price × cityK');
  const s = Items.sell(f, r.uid, xu);
  ok(s.ok && xu.gold === before - pr + sp && !Items.inst(r.uid), 'sell: gold back, instance gone');
  // 神品 / 天宝 / 专属 / 招牌 不可变卖
  ok(!Items.sellable('chitu').ok && !Items.sellable('qinglong').ok && !Items.sellable('yitian').ok, 'epic / legendary / ruler / signature unsellable');
  // 敌城不开放
  const enemy = g.cities.find(c => c.owner >= 0 && c.owner !== f && !g.allied(c.owner, f));
  ok(Items.canTrade(f, enemy) !== null, 'enemy market closed');
  // 补货：同种子同货；不取 SG.Random
  const snap = () => JSON.stringify(g.cities.map(c => c.shop.stock));
  const g2 = JSON.parse(JSON.stringify(g));
  let draws = 0; const rv = SG.Random.value, rf = SG.Random.rangeFloat, ri = SG.Random.rangeInt;
  SG.Random.value = function () { draws++; return rv.apply(this, arguments); };
  SG.Random.rangeFloat = function () { draws++; return rf.apply(this, arguments); };
  SG.Random.rangeInt = function () { draws++; return ri.apply(this, arguments); };
  Items.restockAll();
  SG.Random.value = rv; SG.Random.rangeFloat = rf; SG.Random.rangeInt = ri;
  eq(draws, 0, 'restock draws nothing from SG.Random');
  const a = snap();
  const back = GameState.fromJSON(JSON.stringify(g2)); SG.G = back;
  Items.restockAll();
  eq(JSON.stringify(back.cities.map(c => c.shop.stock)), a, 'same seed and month → same stock');
  SG.G = g;
  // 珍品冷却：买走后 24 个月不再上架
  const rc = g.cities.find(c => (Items.shopRow(c).rare || []).length);
  const rid = Items.shopRow(rc).rare[0];
  rc.shop.stock.push({ id: rid, qty: 1, price: 1000, age: 0 });
  const owner = rc.owner;
  if (owner >= 0) {
    rc.gold = 99999;
    const q = Items.buy(owner, rc, rc.shop.stock.length - 1, { record: false });
    ok(q.ok, 'buy a rare');
    eq(g.items.cooldown[rc.id][rid], g.monthIndex + 24, 'rare cooldown 24 months');
  }
  // 胡商：3 件，×1.5，两个月
  const m = Items.openMerchant(xu, SG.SeededRandom(5));
  ok(m && m.stock.length === 3 && m.until === g.monthIndex + 2, 'merchant: 3 entries for 2 months');
  ok(m.stock.every(e => e.price >= Math.round(Items.price(e.id) * Items.cityK(xu) * 1.5 / 10) * 10 - 10), 'merchant ×1.5');
  // 每级的货架：村市 4 … 都会 10
  for (const c of g.cities) ok(Items.slots(c) === [0, 4, 6, 8, 10][Items.shopTier(c)] + (g.month === 3 ? 2 : 0), 'slots by tier ' + c.name);
});

section('§2d hidden items: probability (10 000 trials ±1 pp), clues, AI hunting (ITEMS §8.5)', () => {
  const g = newGame('cao', 13);
  const ly = g.cityByKey('luoyang');
  ly.owner = g.player;
  const x = g.generalsOf(g.player).sort((a, b) => b.intel - a.intel)[0];
  x.city = ly.id;
  g.year = 191; g.month = 2;
  const h = Items.hiddenIn(ly).find(q => q.item === 'chuanguoxi');
  ok(!!h, '传国玉玺 hidden in 洛阳 from 191-01');
  const p = Items.hiddenP(h, x, ly);
  near(p, Math.min(0.4, 0.06 + Mods.eff(x, 'intel') * 0.0005 + Gear.raw(x, 'search') / 200), 1e-12, 'p = 0.06 + 0.0005 × intel + search / 200');
  SG.Random.seed(4242);
  let found = 0;
  const N = 10000;
  for (let i = 0; i < N; i++) {
    const r = Items.searchHidden(ly, x);
    if (r) { found++; const s = g.items; s.hiddenFound.splice(s.hiddenFound.indexOf(r.id), 1); SGdestroy(g, r.item); }
  }
  near(found / N, p, 0.01, '10 000 searches find it at rate p');
  g.year = 190; g.month = 12;
  eq(Items.hiddenIn(ly).some(q => q.item === 'chuanguoxi'), false, 'not before 191-01');
  ok(typeof Items.clueFor(g.cityByKey('ye')) === 'string', 'clue text for 邺');
});
function SGdestroy(g, uid) { const r = g.items.inst[uid]; if (!r) return; const f = g.factions[r.owner]; if (r.loc === 'arm' && f) f.armoury.splice(f.armoury.indexOf(uid), 1); delete g.items.inst[uid]; Items.load(g); }

section('§2e spoils: 处斩, 阵斩, 缴械释放, city treasure transfer, faction death (ITEMS §8.2, §8.6, §8.8)', () => {
  const g = newGame('liubei', 17);
  const C = SG.Conquest;
  const dz = g.generals[g.factionByKey('dong').ruler];   // 董卓 holds 七星宝刀 (bound)
  const pf = g.player;
  // 处斩：处斩者所在城的主人得全部装备，专属解除绑定
  const tgt = g.cities.find(c => c.owner === pf);
  dz.city = tgt.id;
  C.pendingCaptives.add(dz.id);
  const qx = dz.gear.weapon;
  C.execute(dz);
  ok(Items.inst(qx) && Items.inst(qx).loc === 'arm' && Items.inst(qx).owner === pf && !Items.inst(qx).bound, '处斩: ruler-unique to the executor, unbound');
  ok(dz.dead && Object.values(dz.gear).every(u => u == null), 'executed general carries nothing');
  // 阵斩：slay()
  const ys = g.generals[g.factionByKey('yuanshao').ruler];
  const u2 = ys.gear.weapon;
  Items.slay(ys, pf, 'critRout');
  ok(ys.dead && Items.inst(u2).owner === pf && g.factions[g.factionByKey('yuanshao').id].ruler !== ys.id, '阵斩: spoils + succession');
  // 阵斩概率：君主 25 / 35 / 15 / 0 / 50 %；其他武将 0（设置「只限君主」）
  const cs = g.generals[g.factionByKey('cao').ruler];
  eq(Items.slayChance(cs, 'critRout'), 0.25, 'crit rout 25 %'); eq(Items.slayChance(cs, 'duelKO', { big: true }), 0.35, 'duel KO by crit / special / finisher 35 %');
  eq(Items.slayChance(cs, 'duelKO'), 0.15, 'other KO 15 %'); eq(Items.slayChance(cs, 'timeout'), 0, 'time-out 0'); eq(Items.slayChance(cs, 'assassinate'), 0.5, 'assassinate 50 %');
  eq(Items.slayChance(byName(g, '夏侯惇'), 'critRout'), 0, 'non-rulers are never slain (rulers only)');
  SG.Settings.set('items.slay', 'all');
  near(Items.slayChance(byName(g, '夏侯惇'), 'critRout'), 0.25 / 3, 1e-12, 'setting 所有武将: one third');
  SG.Settings.set('items.slay', 'rulers');
  // 缴械释放
  const zy = byName(g, '赵云');
  const n0 = g.factions[pf].armoury.length;
  const got = Items.disarm(zy, pf);
  ok(got.length >= 2 && g.factions[pf].armoury.length === n0 + got.length && Items.SLOTS.every(sl => zy.gear[sl] == null || Items.inst(zy.gear[sl]).bound), '缴械释放 takes all unbound gear');
  // 镇城之宝随城易主
  const ly = g.cityByKey('luoyang');
  const old = ly.owner;
  const zu = Number(Object.keys(g.items.inst).find(u => g.items.inst[u].id === 'zhanshe'));
  const holder = Items.genOf(zu);
  if (holder) ok(Gear.unequip(holder, Items.get('zhanshe').slot, { free: true }).ok && Items.inst(zu).loc === 'city', 'unequipped city treasure returns to the 洛阳 vault');
  ly.owner = pf;
  ok(Items.armoury(pf).some(u => Items.inst(u).id === 'zhanshe'), 'city treasure listed by the new owner of 洛阳');
  ok(!Items.armoury(old).some(u => Items.inst(u).id === 'zhanshe'), '… and no longer by the old owner');
});

section('§2f gifts once per instance (INTEGRATION §7.1): 10 give / take-back cycles change loyalty by 0', () => {
  const g = newGame('liubei', 19);
  const f = g.player;
  const gid = 'han_yubi', gsl = Items.get(gid).slot;   // 玉璧：没有属性要求
  const free = y => !g.isRuler(y) && (y.gear[gsl] == null || !['signature', 'ruler'].includes((Items.get(Items.inst(y.gear[gsl]).id).availability.classic || {}).kind));
  const x = g.generalsOf(f).find(free);
  x.loyalty = 50;
  const u = Items.award(f, gid, { why: 'test' });
  const first = Items.gift(x, u, { roll: 2, confirm: true });
  ok(first.ok && first.loy > 0, 'first gift raises loyalty (+' + first.loy + ')');
  const l1 = x.loyalty;
  Gear.unequip(x, gsl);
  eq(x.loyalty, l1 - first.loy, 'take-back costs exactly giftLoy');
  const l0 = 50;
  for (let i = 0; i < 9; i++) { const r = Items.gift(x, u, { roll: 4, confirm: true }); ok(r.ok && r.loy === 0, 'regift gives nothing #' + i); Gear.unequip(x, gsl); }
  eq(x.loyalty, l0, '10 cycles: loyalty change 0');
  // 赐给另一人也不再涨
  const y = g.generalsOf(f).find(z => free(z) && z !== x);
  const ly = y.loyalty;
  const ry = Items.gift(y, u, { roll: 4, confirm: true });
  ok(ry.ok && ry.loy === 0 && y.loyalty === ly + ry.eqDelta, 'the same instance to someone else: nothing (only the swap cost ' + ry.eqDelta + ')');
  // 公式：8 + R(0,4) + clamp(round(value × giftK / 60), 0, 30)，合用 × 1.5；赤兔赐关羽 +30
  const z = g.generalsOf(f).find(q => free(q) && q !== x && q !== y) || g.generals.find(q => q.faction >= 0 && !g.isRuler(q) && q !== x && q !== y && q.faction === f);
  z.loyalty = 10;
  const u2 = Items.award(f, 'han_yubi', { why: 'test' });
  const r2 = Items.gift(z, u2, { roll: 0, confirm: true });
  eq(r2.loy, Math.min(100 - r2.afterEquip, Math.round((8 + 0 + Math.min(30, Math.round(80 * Items.giftK('han_yubi', [g.ruler(f)]) / 60))) * (r2.fits ? 1.5 : 1))), 'gift formula');
});

section('§2g save / load round trip, v3 migration, items off = v3 (INTEGRATION §6)', () => {
  const g = newGame('cao', 23);
  months(g, 3);
  const t = JSON.stringify(g);
  const back = GameState.fromJSON(t);
  eq(JSON.stringify(back), t, 'v4 save round trip byte-identical');
  // v3 存档（没有装备）：迁移时补齐起始装备与商店
  const v3 = JSON.parse(t);
  v3.version = 3; delete v3.items; for (const x of v3.generals) delete x.gear; for (const f of v3.factions) delete f.armoury; for (const c of v3.cities) { delete c.shop; delete c.vault; }
  const m = GameState.fromJSON(JSON.stringify(v3));
  ok(m.migratedFrom === 3 && m.items && Object.keys(m.items.inst).length > 300 && m.generals.every(x => x.dead || x.gear), 'v3 save migrated: kits, signature, uniques, shops');
  // 关掉装备：整局 JSON 与只载入第三版规则文件的一局逐位相同
  const v3SG = load({ v4: false });
  const run = (S, on) => {
    if (S.Items) S.Items.enabled = on;
    for (const n of ['Combos', 'Titles', 'Engage', 'Tourney']) if (S[n] && 'enabled' in S[n] && typeof S[n].enabled !== 'function') S[n].enabled = false;
    if (S.Crit) S.Crit.enabled = false;
    S.ScenarioData = S.Scenarios.classic; S.Random.seed(29);
    const gg = S.G = S.GameState.newGame('liubei', 'classic'); gg.player = -1;
    for (let k = 0; k < 24; k++) { S.StrategyAI.runAI([]); S.StrategyAI.endMonth(); }
    return crypto.createHash('md5').update(JSON.stringify(gg).replace(/"version":\d/, '"version":X')).digest('hex');
  };
  const SGoff = load({ v4: true, extra: ['items-data.js', 'items.js'] });
  const h1 = run(v3SG, false), h2 = run(SGoff, false);
  eq(h2, h1, 'Items.enabled = false: 24 months identical to the v3 rules');
  SGoff.Items.enabled = true;
});

section('§2h duel budget (INTEGRATION §8.3) and prizes (INTEGRATION §7.2)', () => {
  const g = newGame('liubei', 31);
  const f = g.player;
  const t = { id: 0, name: '试', war: 80, intel: 40, pol: 40, faction: f, city: g.cities.find(c => c.owner === f).id, loyalty: 90, troops: 2000, training: 50, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
  g.generals.push(t); t.id = g.generals.length - 1;
  // 各部位 duel 数值最好的一件（按品级）
  const best = (rar, sl) => D.items.filter(i => i.rarity === rar && i.slot === sl && !(i.req && (i.req.war > 80 || i.req.intel > 40 || i.req.pol > 40 || i.req.ruler)))
    .sort((a, b) => ((b.stats.dHp || 0) * 0.4 + (b.stats.dDmg || 0) + (b.stats.dGuard || 0) * 0.2) - ((a.stats.dHp || 0) * 0.4 + (a.stats.dDmg || 0) + (a.stats.dGuard || 0) * 0.2))[0];
  for (const rar of ['common', 'uncommon', 'rare', 'epic', 'legendary']) {
    for (const sl of Items.SLOTS) if (t.gear[sl] != null) { SGfree(g, t.gear[sl]); }
    const ids = [];
    for (const sl of ['weapon', 'armour', 'helmet', 'mount', 'treasure', 'offhand']) { const it = best(rar, sl); if (!it) continue; const u = Items.award(f, it.id, { why: 'test' }); if (u && Gear.equip(t, u, { force: true, free: true }).ok) ids.push(it.id); }
    const dm = Gear.duelMods(t);
    ok(dm.budget.used <= 6 + 1e-9, `${rar} best-in-slot duel budget ${dm.budget.used} ≤ 6 (${ids.join(' ')})`);
    ok(dm.hp - 100 <= 10 && dm.dmg <= 0.06 + 1e-12 && dm.parry <= 0.01 + 1e-12 && dm.abilityHp <= 4, rar + ' duel caps (hp ' + dm.hp + ', dmg ' + dm.dmg + ')');
    ok(dm.war <= t.war + 3, rar + ' duelWar ≤ war + 3');
  }
  // 裁剪次序（INTEGRATION §8.3）：先体力、再伤害；两样都没了还超才削格挡。格挡最好的盾 + 体力最好的甲 + 官职单挑伤害 +6 %（满 dmgAll）
  {
    for (const sl of Items.SLOTS) if (t.gear[sl] != null) SGfree(g, t.gear[sl]);
    const top = k => D.items.filter(i => (i.stats[k] || 0) > 0 && !i.bound && !(i.req && (i.req.war > 80 || i.req.intel > 40 || i.req.pol > 40 || i.req.ruler)))
      .sort((a, b) => b.stats[k] - a.stats[k] || (a.id < b.id ? -1 : 1));
    // 可以发的（单件的神品多半已在别人手里，award 返回 null）：按数值排，取第一件发得出去、装得上的
    const put = (k, slot) => { for (const it of top(k).filter(i => i.slot === slot)) { const u = Items.award(f, it.id, { why: 'test' }); if (u && Gear.equip(t, u, { force: true, free: true }).ok) return it; } return null; };
    { const u = Items.award(f, 'han_jian', { why: 'test' }); if (u) Gear.equip(t, u, { force: true, free: true }); }   // 盾须配单手兵器
    const sh = put('dGuard', 'offhand'); put('dHp', 'armour');
    const guard0 = Math.min(Items.CFG.duel.guardPP, Gear.raw(t, 'dGuard'));
    const off = SG.Mods.provide({ titleDmgDuel: x => (x === t ? 6 : 0) }, { owner: 'test-trim' });
    const dm = Gear.duelMods(t), b = dm.budget;
    off();
    ok(guard0 > 0, `trim test set has guard (${sh && sh.id}: ${guard0} pp)`);
    ok(b.used <= 6 + 1e-9 && b.gearHp <= 1e-9, `trim: gear HP goes first (used ${b.used}, gear HP ${b.gearHp})`);
    ok(b.dmg < 6 && b.guardPP >= guard0 - 1e-9, `trim: damage before guard (dmg ${b.dmg.toFixed(2)} %, guard ${b.guardPP} of ${guard0} pp)`);
  }
  // 奖池：专属兵器永不出现；冠军奖品只在大会专属 + 无人持有的商店珍品之中
  const T = D.classic.tournament;
  for (let i = 0; i < 300; i++) {
    const p = Items.tourneyPrize(t, 1, SG.SeededRandom(i), { count: 5 });
    const it = p.id && Items.get(p.id);
    if (!it) continue;
    const a = it.availability.classic;
    ok(!it.bound && (!a || a.kind !== 'ruler'), 'no ruler-unique in the champion pool');
    ok(T.champion.indexOf(it.id) >= 0 || (a && a.kind === 'shop' && it.rarity === 'rare') || (T.foreign.indexOf(it.id) >= 0 && p.foreign), 'champion prize from the right pools: ' + it.id);
  }
  const of = Items.offers(t, 'uncommon', 3, SG.SeededRandom(3));
  ok(of.length === 3 && new Set(of).size === 3 && of.every(id => Items.get(id).rarity === 'uncommon'), '2nd place: three distinct uncommon offers');
  ok(Items.pool({ rarity: 'rare', culture: 'han' }).every(id => Items.count(id) === 0), 'rare pool: shop rares with no instance');
  eq(Items.pool({ rarity: 'rare' }).filter(id => Items.get(id).bound).length, 0, 'rare pool never has ruler-uniques');
  g.tourney = { wins: { [t.name]: 3 } };
  const v = Items.titleRelic(t);
  ok(v && Items.inst(v).id === 'vajra', '三冠: 金刚杵');
  eq(Items.titleRelic(t), null, '金刚杵 only once');
});
function SGfree(g, uid) { const r = g.items.inst[uid]; if (!r) return; const x = g.generals[r.owner]; for (const sl of Items.SLOTS) if (x.gear[sl] === uid) x.gear[sl] = null; delete g.items.inst[uid]; Items.load(g); }

section('§2i 宝具 in a battle (ITEMS §7.3; specials.js known / useSpecial)', () => {
  const g = newGame('liubei', 37);
  const gy = byName(g, '关羽');
  const ids = SG.Specials.known(gy).map(s => s.id);
  ok(ids.indexOf('item:qinglong:0') >= 0, '关羽 knows 青龙出海 through his weapon');
  const zf = byName(g, '张飞');
  const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: [gy], def: [zf], atkFood: 9000, atkGold: 0, routed: new Set(), captives: [] };
  gy.troops = 6000; zf.troops = 6000;
  const Mdl = new SG.BattleModel(s);
  const u = Mdl.units.find(x => x.gen === gy), t = Mdl.units.find(x => x.gen === zf);
  u.x = 5; u.y = 5; t.x = 6; t.y = 5;
  for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) Mdl.map[x][y] = SG.Terrain.Plain;
  Mdl.side = 0;
  const us = Mdl.specialUsable(u, 'item:qinglong:0');
  ok(us.ok, '宝具 usable (' + us.why + ')');
  const r = Mdl.useSpecial(u, t, 'item:qinglong:0');
  ok(r.success && r.dmg > 0, '宝具 deals damage (' + r.dmg + ')');
  ok(!u.specialUsed && Mdl.specialUsable(u, 'sp:关羽').ok, 'the 宝具 does not use up the signature special');
  eq(Mdl.specialUsable(u, 'item:qinglong:0').ok, false, '宝具 once per battle');
  SG.Specials.itemPostStep(Mdl, u, r);
  Items.enabled = false;
  eq(SG.Specials.known(gy).some(sp => sp.itemActive), false, 'items off: no 宝具');
  Items.enabled = true;
});

// 测试用：把 id 的现有实例（单件）拿掉，再发给 x 并装上（不计忠诚）
function giveTo(g, x, id) {
  for (const u of Object.keys(g.items.inst).filter(k => g.items.inst[k].id === id)) {
    const r = g.items.inst[u];
    if (r.loc === 'gen') { const y = g.generals[r.owner]; for (const sl of Items.SLOTS) if (y.gear[sl] === +u) y.gear[sl] = null; }
    else if (r.loc === 'arm') { const fa = g.factions[r.owner]; fa.armoury.splice(fa.armoury.indexOf(+u), 1); }
    else if (r.loc === 'city') { const c = g.cities[r.owner]; c.vault.splice(c.vault.indexOf(+u), 1); if (!c.vault.length) delete c.vault; }
    delete g.items.inst[u];
  }
  Items.load(g);
  const uid = Items.award(x.faction >= 0 ? x.faction : 0, id, { why: 'test' });
  if (uid == null) return null;
  const r = Gear.equip(x, uid, { force: true, free: true });
  return r.ok ? uid : null;
}

section('§2j review fixes: noCounter conditions, charge, awe, escape / capture, crit hooks, festival, gift swap, names, giftK scope, auras, defendCity, free / relFloor / scout', () => {
  const g = newGame('liubei', 5);
  const B = Gear.battle;
  // noCounter（§7.2）：赤兔 / 两刃矛只在移动 ≥ 2 格后的攻击；两刃矛还要骑乘；卢赫之矛的 noCounter 一直有效
  const lb = byName(g, '吕布'), gs = byName(g, '公孙瓒');
  eq(itemOn(lb, 'mount'), 'chitu', '吕布 rides 赤兔');
  eq(B.noCounter(lb), false, '赤兔: standing still → countered');
  eq(B.noCounter(lb, { moved: 1 }), false, '赤兔: moved 1 → countered');
  eq(B.noCounter(lb, { moved: 2 }), true, '赤兔: moved 2 → no counter');
  eq(itemOn(gs, 'weapon'), 'liangrenmao', '公孙瓒 holds 两刃矛');
  eq(B.noCounter(gs, { moved: 3 }), true, '两刃矛 mounted, moved 3 → no counter');
  Gear.unequip(gs, 'mount', { free: true });
  eq(B.noCounter(gs, { moved: 3 }), false, '两刃矛 on foot → countered');
  eq(B.chargeK(gs, {}), Gear.raw(gs, 'charge') / 100, '两刃矛 charge needs a mount');
  // allMounted（萨尔马提亚龙旗）：同方骑乘者冲锋 +6 %，持有者不骑马时自己不得
  const zy = byName(g, '赵云'), lbK = B.chargeK(lb, {});
  ok(giveTo(g, zy, 'draco') != null, '赵云 carries 萨尔马提亚龙旗');
  near(B.chargeK(lb, { allies: [lb, zy] }) - lbK, 0.06, 1e-9, 'allMounted: a mounted ally charges +6 %');
  Gear.unequip(zy, 'mount', { free: true });
  near(B.chargeK(zy, { allies: [zy] }), Gear.raw(zy, 'charge') / 100, 1e-9, 'allMounted: the holder on foot gets nothing');
  near(B.attackK(lb, { moved: 2, allies: [lb, zy] }), B.attackK(lb, { moved: 2 }) * (1 + B.chargeK(lb, { allies: [lb, zy] })) / (1 + lbK), 1e-9, 'attackK uses chargeK with allies');
  // 威（awe）是一方最大值；开场 opening.awe 另加（§7.2）
  const x0 = byName(g, '张飞');
  ok(giveTo(g, x0, 'kuiniugu') != null, '张飞 carries 夔牛鼓');
  eq(B.openingAwe(x0), 10, '夔牛鼓 opening awe 10 is separate');
  eq(B.awe(x0), Gear.raw(x0, 'awe'), 'awe = gear awe stat only');
  // 脱身与被俘（§3.2）：p × (1 − escape)；的卢 / 赤兔必定脱身；escapeK 与坐骑的脱身相乘
  eq(B.escape(lb), 1, '赤兔: always escapes');
  eq(Items.captureP(lb, 0.75, null), 0, 'captureP with 赤兔 = 0');
  const sj = byName(g, '孙坚'), zm = byName(g, '祖茂');
  const e0 = B.escape(sj), p0 = Items.captureP(sj, 0.75, { atk: [sj], def: [] });
  near(p0, 0.75 * (1 - e0) * (1 - 0.6), 1e-9, '赤罽帻 decoy: p × 0.4 without 祖茂');
  eq(Items.captureP(sj, 0.75, { atk: [sj, zm], def: [] }), 0, '赤罽帻 decoy with 祖茂 in the battle: always escapes');
  const gy = byName(g, '关羽');
  near(Items.captureP(gy, 0.35, { routedBy: { [gy.id]: x0.id } }), Math.min(1, 0.35 + B.capture(x0)) * (1 - B.escape(gy)), 1e-9, 'captureP adds the router\'s capture bonus');
  near(Items.captureP(gy, 0.5, { rescueP: { [gy.id]: 0.5 } }), 0.5 * (1 - B.escape(gy)) * 0.5, 1e-9, 'captureP: rescue halves');
  Items.enabled = false; eq(Items.captureP(gy, 0.75, null), 0.75, 'items off: captureP = p'); Items.enabled = true;
  // 会心（§3.1、§7.2）：古锭刀 cond charge +2；抗会心 critRes；护身 ward.crit
  const base = Gear.stat(sj, 'critArmy');
  near(B.critAdd(sj, { charge: true }), Math.min(0.04, (Gear.raw(sj, 'crit') + 2) / 100) - base, 1e-12, '古锭刀: +2 pp crit after a charge (inside the 0.04 cap)');
  eq(B.critAdd(sj, {}), 0, '古锭刀: nothing without the charge');
  near(Mods.z ? Mods.z('gearCritCond', sj, { charge: true }) : B.critAdd(sj, { charge: true }), B.critAdd(sj, { charge: true }), 1e-12, 'provider gearCritCond');
  near(B.critRes(gy), Gear.raw(gy, 'critRes') / 100, 1e-12, 'critRes from 铁兜鍪');
  near(Items.critTaken(0.2, gy), 0.2 - B.critRes(gy), 1e-12, 'critTaken subtracts critRes');
  const anc = g.generals.find(y => !y.dead && y.faction >= 0 && !g.isRuler(y) && Items.styleOf(Gear.item(y, 'weapon')) === 'one');
  ok(anc && giveTo(g, anc, 'ancile') != null, (anc ? anc.name : '?') + ' (one-handed) carries 天降神盾');
  near(Items.critTaken(0.2, anc), (0.2 - B.critRes(anc)) * 0.5, 1e-12, '天降神盾 ward.crit halves the crit chance taken');
  // 庙会（§14.2）：3 月的货是 2 月末备的：有主之城 +2 货位且买价 ×0.9；4 月两样都没有
  while (g.month !== 2) months(g, 1);
  months(g, 1);
  eq(g.month, 3, 'now month 3');
  // 玩家的城（电脑不在那里买）一定是满的；电脑的城在第 7 相可能已经买走一两件
  const own = g.cities.filter(c => c.owner >= 0), mine = own.filter(c => c.owner === g.player);
  ok(mine.length && mine.every(c => c.shop.stock.length === Items.CFG.slotsByTier[Items.shopTier(c)] + 2), 'month 3: the player\'s markets carry tier slots + 2 (' + mine.map(c => c.name + ' ' + c.shop.stock.length).join(', ') + ')');
  const full = own.filter(c => c.shop.stock.length >= Items.CFG.slotsByTier[Items.shopTier(c)] + 1).length;
  ok(full >= own.length * 0.75, `month 3: owned markets got the extra stalls (${full} / ${own.length} with ≥ tier slots + 1 after AI shopping)`);
  eq(Items.slots(own[0]), Items.CFG.slotsByTier[Items.shopTier(own[0])] + 2, 'Items.slots shows the festival slots in month 3');
  const pc = g.cities.find(c => c.owner === g.player && c.shop.stock.length);
  const e3 = pc.shop.stock[0], p3 = Items.entryPrice(pc, e3, g.player);
  months(g, 1);
  eq(g.month, 4, 'now month 4');
  ok(own.every(c => c.shop.stock.length <= Items.CFG.slotsByTier[Items.shopTier(c)] + (c.shop.stock.some(e => Items.tier(e.id) >= 2) ? 2 : 0)), 'month 4: the extra stalls packed up (commons trimmed back to the tier slots)');
  ok(pc.shop.stock[0] === e3, 'the oldest entry stays after the festival');
  const p4 = Items.entryPrice(pc, e3, g.player);
  ok(Math.abs(p3 - p4 * 0.9) <= 10, `festival price: month 3 ${p3} ≈ 0.9 × month 4 ${p4}`);
  ok(!Items.slots(pc) || Items.slots(pc) === Items.CFG.slotsByTier[Items.shopTier(pc)], 'month 4: no festival slots');
  // 赐宝物（§2.6、§6.6）：会顶掉名将旧物时先要确认；结果写明换装代价与赏赐所得
  const g2 = newGame('liubei', 19), gy2 = byName(g2, '关羽');
  gy2.loyalty = 100;
  const common = D.items.find(it => it.rarity === 'common' && it.slot === 'weapon' && it.culture === 'han' && !it.req && it.duel && it.duel.style === 'pole');
  const cu = Items.award(gy2.faction, common.id, { why: 'test' });
  const ask = Items.gift(gy2, cu, { roll: 2 });
  ok(!ask.ok && ask.needConfirm && ask.displaced && ask.displaced.id === 'qinglong' && ask.displaced.cost === 15, 'gift that would replace 青龙偃月刀 asks first (cost 15)');
  eq(itemOn(gy2, 'weapon'), 'qinglong', '… and changes nothing until confirmed');
  eq(gy2.loyalty, 100, '… loyalty untouched');
  const done = Items.gift(gy2, cu, { roll: 2, confirm: true });
  ok(done.ok && done.before === 100 && done.eqDelta === -15 && done.afterEquip === 85 && done.after === 85 + done.loy && done.displaced.indexOf('qinglong') >= 0, `confirmed gift reports 100 → ${done.after} (swap −15, gift +${done.loy})`);
  eq(Items.inst(cu).giftLoy, done.loy, 'giftLoy is the gift share only (the take-back costs that much)');
  // 吉祥纹铜剑的名字（DESIGN-V4 §20 问题 4 / Decisions.safeSwastikaName）
  eq(SG.Balance.Decisions.safeSwastikaName, true, 'Decisions.safeSwastikaName defaults to true (DESIGN-V4 §20)');
  eq(Items.displayName('paratarajas_jian'), '吉祥纹铜剑', 'safe name by default');
  Items.decide('safeSwastikaName', false);
  eq(Items.displayName('paratarajas_jian'), '卍纹铜剑', 'safeSwastikaName = false restores 卍纹铜剑');
  eq(D.items.filter(it => Items.displayName(it) !== it.name).length, 1, '… and renames nothing else');
  Items.decide('safeSwastikaName');
  eq(Items.displayName('paratarajas_jian'), '吉祥纹铜剑', 'override cleared');
  // giftK（§6.6）：「以之赏赐」乘这件本身；「以宝物赏赐」乘持有者送出的任何宝物
  near(Items.giftK('hepu_zhu'), (Items.get('hepu_zhu').stats.gift || 1) * 1.6, 1e-9, '合浦明珠 ×1.6 when given');
  const giver = byName(g2, '简雍') || g2.generalsOf(g2.player).find(y => !g2.isRuler(y));
  ok(giveTo(g2, giver, 'tillya_crown') != null, 'a giver wears 蒂拉丘黄金冠');
  near(Items.giftK('han_yubi', [giver]), Items.giftK('han_yubi') * 1.3, 1e-9, '蒂拉丘黄金冠: the holder\'s gifts ×1.3');
  eq(Items.giftK('tillya_crown', [giver]), Items.giftK('tillya_crown'), '… not on itself');
  near(Items.giftValue('han_yubi', [giver]), Math.round(Items.valueOf('han_yubi') * Items.giftK('han_yubi') * 1.3), 1, 'giftValue for the 外交 gift');
  const ally = g2.factions.find(fa => fa.alive && fa.id !== g2.player), tu = Items.award(g2.player, 'han_yubi', { why: 'test' });
  ok(Items.transfer(tu, ally.id, { from: g2.player }).ok && Items.factionOf(tu) === ally.id, '外交 gift: transfer puts the item in the partner\'s armoury');
  ok(!Items.transfer(+Object.keys(g2.items.inst).find(k => g2.items.inst[k].id === 'qinglong'), ally.id).ok, '… never a signature weapon (青龙偃月刀)');
  // 忠诚光环（§3.5）：负的（交州刺史节 −1）、有上限的（军人公民权铜板至 90）
  const city = g2.cities.find(c => c.owner === g2.player && g2.officersIn(c).length >= 3 && !g2.officersIn(c).every(o => g2.isRuler(o))) || g2.cities.find(c => c.owner >= 0 && g2.officersIn(c).filter(o => !g2.isRuler(o)).length >= 2);
  const gov = g2.officersIn(city).find(o => !g2.isRuler(o));
  city.governor = gov.id;
  const adm = Mods.admin(city);
  const others = g2.officersIn(city).filter(o => o !== adm && !g2.isRuler(o));
  ok(adm && others.length >= 1, 'a city with an administrator and officers (' + city.name + ')');
  if (adm && others.length) {
    for (const sl of Items.SLOTS) if (adm.gear[sl] != null && !Items.inst(adm.gear[sl]).bound) Gear.unequip(adm, sl, { free: true });
    ok(giveTo(g2, adm, 'jiaozhou_jie') != null, adm.name + ' holds 交州刺史节');
    for (const o of others) o.loyalty = 80;
    eq(Mods.loyAura(city), 0, '交州刺史节 is not a positive aura');
    Items.loyAuraSpecial({});
    ok(others.every(o => o.loyalty === 79), 'cursed aura: officers lose 1 a month');
    Gear.unequip(adm, 'weapon', { free: true });
    ok(giveTo(g2, adm, 'diploma') != null, adm.name + ' holds 军人公民权铜板');
    eq(Mods.loyAura(city), 0, 'capped aura is not in the provider (cap 90 < 95)');
    others[0].loyalty = 89; if (others[1]) others[1].loyalty = 90;
    Items.loyAuraSpecial({});
    ok(others[0].loyalty === 90 && (!others[1] || others[1].loyalty === 90), 'capped aura: +1 up to 90 only');
  }
  // 坐镇守城（defendCity）：自动结算时这一仗的守军战力 × (1 + k)
  const holder = g2.generals.find(y => !y.dead && y.gear && itemOn(y, 'weapon') === 'yizhou_maojie');
  if (holder) {
    const c = g2.cities[holder.city], d = g2.officersIn(c).find(y => !Gear.has(y, 'castle'));
    const pw0 = Mods.autoPower(d), s = { atk: [], def: g2.officersIn(c), target: c };
    SG.Events.emit('conquest.prepare', s);
    near(Mods.autoPower(d) / pw0, 1 + Items.defendCity(c), 1e-9, `益州牧旄节: defenders of ${c.name} ×${(1 + Items.defendCity(c)).toFixed(2)} in auto-resolve`);
    SG.Events.emit('conquest.resolved', s);
    near(Mods.autoPower(d), pw0, 1e-9, '… only in that battle');
  } else ok(false, '益州牧旄节 holder found');
  // 不耗令牌的外交（传国玉玺）、好感下限（径路刀）、斥候（浑天仪）
  const ru = g2.ruler(g2.player);
  ok(giveTo(g2, ru, 'chuanguoxi') != null, '刘备 wears 传国玉玺');
  eq(Items.freeUses(g2.player, 'diplomacy'), 1, '传国玉玺: one free 外交 a month');
  ok(Items.useFree(g2.player, 'diplomacy') && Items.freeUses(g2.player, 'diplomacy') === 0 && !Items.useFree(g2.player, 'diplomacy'), '… used up');
  months(g2, 1);
  eq(Items.freeUses(g2.player, 'diplomacy'), Gear.equipped(ru).some(e => e.id === 'chuanguoxi') ? 1 : 0, '… back next month');
  const other = g2.factions.find(fa => fa.alive && fa.id !== g2.player);
  Gear.unequip(ru, 'weapon', { free: true, force: true });
  if (ru.gear.weapon == null) {
    ok(giveTo(g2, ru, 'jinglu') != null, '刘备 wears 径路刀');
    eq(Items.relFloor(g2.player, other.id), 0, 'relFloor 0 without an alliance');
    g2.setAlliance(g2.player, other.id, g2.monthIndex + 12);
    eq(Items.relFloor(g2.player, other.id), 40, '径路刀: relation floor +40 while allied');
  }
  const sc = g2.generalsOf(g2.player).find(y => !g2.isRuler(y));
  ok(giveTo(g2, sc, 'huntianyi') != null, sc.name + ' carries 浑天仪');
  const near1 = g2.cities[sc.city].links.filter(j => g2.cities[j].owner !== g2.player);
  ok(near1.every(j => Items.scouted(g2.player).has(j)), '浑天仪: adjacent foreign cities are scouted');
});

// ======================================================================================= §4 battle ==
section('§2k AI gear credit (gearOfPurse × the AIBudget purse share; ITEMS §13.3 / §17.4)', () => {
  const g = newGame('liubei', 77);
  g.player = -1;
  const AIB = SG.AIBudget, A = Items.CFG.ai;
  AIB.reset();
  const f = g.factions.find(x => x.alive && g.cityCount(x.id) >= 3).id;
  for (const c of g.cities) if (c.owner === f) c.gold = 50000;
  const setPurse = v => { g.aiBudget = g.aiBudget || { purse: {} }; g.aiBudget.purse = g.aiBudget.purse || {}; g.aiBudget.purse[f] = v; };
  // 额度的份额 = gearOfPurse × 钱袋份额（Decisions.aiPurseShare / AIBudget.ShareByDifficulty，DESIGN-V4 §20 问题 2）
  const K = Items.gearShare();
  near(K, A.gearOfPurse * AIB.share, 1e-12, `gearShare() = gearOfPurse × purse share (${A.gearOfPurse} × ${AIB.share} = ${K.toFixed(4)})`);
  ok(K >= 0.12 && K <= 0.14, 'default allowance ≈ 13 % of gross at the default 22 % purse (' + (100 * K).toFixed(2) + ' %)');
  {
    const B0 = SG.Balance;
    SG.Balance = Object.assign({}, B0, { Decisions: Object.assign({}, B0.Decisions, { aiPurseShare: 0.30 }) });
    near(Items.gearShare(), A.gearOfPurse * 0.30, 1e-12, 'the allowance follows Decisions.aiPurseShare (0.30)');
    SG.Balance = Object.assign({}, B0, { AIBudget: Object.assign({}, B0.AIBudget, { ShareByDifficulty: { easy: 0.18, normal: 0.22, hard: 0.26 } }) });
    const d0 = g.difficulty; g.difficulty = 'hard';
    near(Items.gearShare(), A.gearOfPurse * 0.26, 1e-12, '… and AIBudget.ShareByDifficulty (hard 0.26)');
    g.difficulty = d0; SG.Balance = B0;
    A.gearShare = 0.05;
    eq(Items.gearShare(), 0.05, 'a numeric CFG.ai.gearShare overrides (tests / comparisons)');
    A.gearShare = null;
  }
  // 额度每月记一次：gearShare() × 本月总收入，至多 gearCapMonths 个月
  g.items.aiGear = {};
  AIB.income.set(f, 1000);
  const r = Items.gearCredit(f);
  near(r.c, K * 1000, 1e-9, 'credit accrues gearShare × gross once');
  Items.gearCredit(f);
  near(g.items.aiGear[f].c, K * 1000, 1e-9, 'a second call in the same month does not accrue again');
  g.items.aiGear[f] = { c: 1e9, mi: -1 };
  near(Items.gearCredit(f).c, A.gearCapMonths * K * 1000, 1e-6, `credit capped at ${A.gearCapMonths} months`);
  // 钱袋很满、额度只有一个月：这个月的买装 ≤ 额度
  setPurse(1e6);
  g.items.aiGear[f] = { c: 0, mi: -1 };
  const spent0 = AIB.stats.byKind.gear || 0;
  Items.aiShop(f);
  const spent = (AIB.stats.byKind.gear || 0) - spent0;
  ok(spent > 0 && spent <= K * 1000 + 1e-9, `AI gear spend in a month > 0 and ≤ the month's credit (${spent} ≤ ${(K * 1000).toFixed(1)})`);
  ok(g.items.aiGear[f].c >= -1e-9, 'credit never negative (' + g.items.aiGear[f].c + ')');
  // 没有额度（收入 0）：一件也不买，钱袋不动
  g.items.aiGear[f] = { c: 0, mi: -1 };
  AIB.income.set(f, 0);
  const p0 = AIB.purseOf(f);
  eq(Items.aiShop(f), 0, 'no credit → no purchase');
  eq(AIB.purseOf(f), p0, 'purse untouched without credit');
  // 额度大、钱袋空：仍不买（两样都要）
  g.items.aiGear[f] = { c: 1e6, mi: g.monthIndex };
  setPurse(0);
  eq(Items.aiShop(f), 0, 'empty purse → no purchase even with credit');
  // 钱袋空的月份额度照记（mi 是上个月、收入 > 0）：不买，额度涨一个月
  g.items.aiGear[f] = { c: 100, mi: g.monthIndex - 1 };
  AIB.income.set(f, 1000);
  const b0 = Items.stats.aiBuys;
  eq(Items.aiShop(f), 0, 'empty purse, income 1000: nothing bought …');
  near(g.items.aiGear[f].c, 100 + K * 1000, 1e-9, '… but the month\'s allowance still accrues (100 → ' + g.items.aiGear[f].c.toFixed(1) + ')');
  eq(Items.stats.aiBuys, b0, '… and no purchase was counted');
  // 买装的预检用本势力武将表数在城武将（免得每城扫全部武将）：与 SG.AIBudget.reserve 的算式、判据相同
  {
    const bad = g.cities.filter(c => c.owner >= 0 && Items.reserveGold(c) !== AIB.reserve(c).gold);
    eq(bad.length, 0, 'AI purchase precheck reserve == SG.AIBudget.reserve(c).gold in every owned city' + (bad.length ? ' (' + bad[0].name + ')' : ''));
  }
  // 尚未接入的效果（INTEGRATION：captureP 还没进 Conquest.apply；免令牌外交、同盟好感下限、查看敌城也没有调用方）：
  // pendingEffects 列出它们，商店与人物卡标「暂未生效」；接线标记设上之后不再列
  {
    const P = id => Items.pendingEffects(id);
    const esc = D.items.filter(it => it.stats && it.stats.escape).length;
    ok(P('chitu').indexOf('溃散时脱身') >= 0 && P('zhaoye').indexOf('救主') >= 0 && P(D.items.find(it => it.name === '套马索').id).indexOf('擒将') >= 0, 'pendingEffects: 赤兔 脱身, 照夜玉狮子 救主, 套马索 擒将');
    ok(D.items.filter(it => P(it).length).length >= esc, `every item with a 脱身 stat (${esc}) is marked pending`);
    Items.captureWired = true;
    eq(P('chitu').length, 0, 'captureWired = true → 赤兔 no longer pending');
    Items.captureWired = false;
    ok(P(D.items.find(it => it.name === '传国玉玺').id).indexOf('免令牌外交') >= 0 && P(D.items.find(it => it.name === '浑天仪').id).indexOf('查看敌城') >= 0, '传国玉玺 免令牌外交 / 浑天仪 查看敌城 pending until wired');
  }
  // 旧存档（没有 aiGear）：ensure 补一个空表
  delete g.items.aiGear;
  Items.ensure(g);
  eq(JSON.stringify(g.items.aiGear), '{}', 'ensure() adds an empty aiGear to an older save');
  AIB.reset();
  // 真的存档路径（GameState JSON → fromJSON）：读档后的下两个月与不读档一模一样（额度、买到的东西、钱袋）
  {
    const ga = newGame('cao', 31);
    ga.player = -1;
    AIB.reset();
    months(ga, 14);
    ok(Object.keys(ga.items.aiGear).length >= 3 && Object.values(ga.items.aiGear).some(x => x.c > 0), 'after 14 months several AI factions carry a credit (' + Object.keys(ga.items.aiGear).length + ')');
    const text = JSON.stringify(ga);
    const gb = GameState.fromJSON(text);
    eq(JSON.stringify(gb.items.aiGear), JSON.stringify(ga.items.aiGear), 'aiGear survives GameState save / load');
    const run = gg => {
      SG.G = gg; SG.Random.seed(4242); Items.resetStats();
      const pb = JSON.stringify(gg.aiBudget || {});
      months(gg, 2);
      return { aiGear: JSON.stringify(gg.items.aiGear), buys: JSON.stringify(Items.stats.buys), spend: Items.stats.spend, purse: JSON.stringify(gg.aiBudget || {}), armoury: JSON.stringify(gg.factions.map(x => (x.armoury || []).map(u => gg.items.inst[u] && gg.items.inst[u].id))), pb };
    };
    const ra = run(ga), rb = run(gb);
    eq(rb.aiGear, ra.aiGear, 'reloaded vs continuous: the same credits two months later');
    eq(rb.buys + ' ' + rb.spend, ra.buys + ' ' + ra.spend, `… the same AI purchases (${ra.buys}, ${ra.spend} gold)`);
    eq(rb.purse, ra.purse, '… the same purses');
    eq(rb.armoury, ra.armoury, '… the same armouries');
    ok(ra.spend > 0, 'some AI gear was bought in those two months');
    AIB.reset();
  }
});

section('§4 battle: full set exchange ratio by rarity (ITEMS §0, §17.3) and 宝具 values', () => {
  // 代表套装（spear + 札甲 + 盔 + 盾 + 战马 的品级 profile；一半攻击为冲锋），交换比 = 攻 × 防 × (1 + 冲锋 / 2)
  const P = k => D.types[k].profile;
  const bands = { common: [1.10, 1.20], uncommon: [1.22, 1.34], rare: [1.35, 1.50], epic: [1.50, 1.70] };
  RARE: for (const [r, [lo, hi]] of Object.entries(bands)) {
    const t = Items.TIER[r];
    const atk = P('spear').atk[t] / 100, def = (P('lamellar').def[t] + P('helm').def[t] + P('shield').def[t]) / 100, ch = P('horse').charge[t] / 100;
    const ex = (1 + atk) * (1 + def) * (1 + ch / 2);
    ok(ex >= lo - 1e-9 && ex <= hi + 1e-9, `${r} full set exchange ratio ${ex.toFixed(3)} in ${lo}–${hi}`);
  }
  // 战斗乘数经 SG.Mods（battle-math）：同一个 atkMul / defMul，且 ≤ ×1.60
  const g = newGame('liubei', 41);
  const lb = byName(g, '吕布');
  const u = { gen: lb, side: 0, x: 0, y: 0, alive: true };
  const am = Mods.atkMul(u, { units: [u] }), dm = Mods.defMul(u, { units: [u] });
  ok(am > 1 && am <= 1.6 && dm >= 1 && dm <= 1.6, `吕布 atkMul ${am.toFixed(3)} defMul ${dm.toFixed(3)} within the cap`);
  // 宝具价值（ITEMS §7.1 / §17.3：稀有 1.3–1.8、史诗 2.0–2.5、传说 ≤ 2.8 次普通攻击，「refAction like specials」）：
  // 与 tests/sim.js (5) 的必杀参考局面同一方法——施展者（武 85 智 85，7000 兵）在平原，四个靶（武 70 智 60，3500 兵）在相邻、斜角、
  // 隔一格；倍率 = specialPlan 的期望伤害（范围类最多计 2 支敌军，连斩计全部，暗杀计得手 × 兵力）÷ refAction（武力系 = 一次普通攻击
  // 的期望；智力 / 政治系 = 普通攻击与火计期望的较大者）。全部 29 件都量（已有主人的单件先收回再发给施展者）。
  function refAction(Mdl, u, t, sp) {
    let ref = Mdl.atkExpect(u, t);
    if (sp.stat !== 'war' && u.gen.intel >= 50) {
      const tt = Mdl.map[t.x][t.y];
      const terr = tt === SG.Terrain.Forest ? 1.8 : (tt === SG.Terrain.Castle || tt === SG.Terrain.Gate) ? 0.6 : 1;
      ref = Math.max(ref, SG.M.clamp(0.5 + (u.gen.intel - t.gen.intel) / 110, 0.12, 0.92) * (260 + u.gen.intel * 11) * terr * SG.M.clamp(t.troops / 2500, 0.4, 1.4) / Mdl.modK(t, 'def'));
    }
    return Math.min(ref, t.troops);
  }
  const DMG = ['smite', 'cleave', 'charge', 'rampage', 'volley', 'blaze', 'storm', 'flood', 'drain'];
  const ratios = { rare: [], epic: [], legendary: [] }, rows = [];
  const dummy = i => ({ id: 9000 + i, name: '靶' + i, war: 70, intel: 60, pol: 50, faction: -1, troops: 3500, training: 60, loyalty: 80, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } });
  let measured = 0;
  for (const it of D.items) {
    if (!it.ability || !it.ability.effects.some(e => e.t === 'active' || e.t === 'activeChoice')) continue;
    const caster = { name: '施', war: 85, intel: 85, pol: 60, faction: 0, troops: 7000, training: 70, loyalty: 90, culture: it.culture, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
    g.generals.push(caster); caster.id = g.generals.length - 1;
    if (Items.isShield(it)) giveTo(g, caster, D.items.find(w => w.rarity === 'common' && w.slot === 'weapon' && !w.req && w.duel && w.duel.style === 'one').id);   // 盾要单手兵器
    if (!ok(giveTo(g, caster, it.id) != null, '宝具 caster equips ' + it.id)) { g.generals.pop(); continue; }
    measured++;
    for (const sp of SG.Specials.known(caster).filter(q => q.item === it.id)) {
      if (DMG.indexOf(sp.kind) < 0) continue;
      const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: [caster], def: [1, 2, 3, 4].map(dummy), atkFood: 9000, atkGold: 0, routed: new Set(), captives: [] };
      const Mdl = new SG.BattleModel(s);
      for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) Mdl.map[x][y] = SG.Terrain.Plain;
      const [u, e1, e2, e3, e4] = Mdl.units;
      u.x = 4; u.y = 4; u.morale = 80;
      [[5, 4], [5, 5], [4, 6], [4, 2]].forEach((p, k) => { const e = [e1, e2, e3, e4][k]; e.x = p[0]; e.y = p[1]; e.morale = 70; });
      for (const q of Mdl.units) q.commander = false;
      Mdl.side = 0;
      const normal = refAction(Mdl, u, e1, sp);
      let best = 0;
      for (const t of Mdl.specialTargets(u, sp.id)) {
        const pl = Mdl.specialPlan(u, t, sp.id);
        if (!pl) continue;
        const per = new Map();
        for (const h of pl.dmg || []) per.set(h.unit, Math.min(h.unit.troops, (per.get(h.unit) || 0) + h.amt * (h.onFail && pl.kill ? 1 - pl.kill.p : 1)));
        let dmg = [...per.values()].sort((a, b) => b - a).slice(0, sp.kind === 'rampage' ? 99 : 2).reduce((a, b) => a + b, 0);
        if (pl.kill) dmg += pl.kill.p * pl.kill.unit.troops;
        best = Math.max(best, dmg);
      }
      const r = best / Math.max(1, normal);
      ratios[it.rarity].push(r);
      rows.push(`${it.name} ${sp.kind} ${r.toFixed(2)}`);
      ok(r > 1.2 && r < 3.0, `宝具 ${it.name}（${sp.kind}）${r.toFixed(2)} normal actions within 1.2–3.0 (tests/sim.js bound for damage specials)`);
    }
    g.generals.pop();
    Items.load(g);
  }
  eq(measured, 29, 'every item with a 宝具 measured (singletons taken back first)');
  for (const [r, list] of Object.entries(ratios)) info(`${r}: 宝具 damage / one normal action ${list.length ? Math.min(...list).toFixed(2) + ' … ' + Math.max(...list).toFixed(2) + ' (avg ' + avg(list).toFixed(2) + ', n ' + list.length + ')' : '—'}`);
  info('  ' + rows.join(' · '));
  ok(ratios.legendary.length === 3 && ratios.legendary.every(v => v <= 2.8 + 1e-9), 'legendary 宝具 ≤ 2.8 normal actions (' + ratios.legendary.map(v => v.toFixed(2)).join(', ') + ')');
  ok(avg(ratios.epic) >= 2.0 && avg(ratios.epic) <= 2.5, `epic 宝具 average ${avg(ratios.epic).toFixed(2)} in 2.0–2.5`);
  ok(avg(ratios.rare) >= 0.9 && avg(ratios.rare) <= 2.2, `rare 宝具 average ${avg(ratios.rare).toFixed(2)} near 1.3–1.8 (± 0.4)`);
  flag(avg(ratios.rare) >= 1.3 && avg(ratios.rare) <= 1.8, `rare 宝具 average ${avg(ratios.rare).toFixed(2)} outside the strict 1.3–1.8 band (items.json powers; spec question, see report)`);
});

// §4b ITEMS §17.3 第一行：电脑对电脑的战术战斗，同一局面、同一随机数种子，装备关 / 开（只有起始装备与名将旧物，没有买的），
// 攻方胜率差 ≤ 3 pp、平均天数差 ≤ 1（INTEGRATION §9.2 原型：−0.2 pp）。另报第 3 年局面（含电脑买的装备）作参考。
section('§4b tactical battles, items off vs on with AI kits only, paired seeds (ITEMS §17.3)', () => {
  // 夜间档 720 场：300 场的配对差噪声约 ±3 pp（实测 300 场 +4.0、720 场 +1.8），门槛 ±3 pp 要更多场才有意义
  const nSeeds = argN('--battle-seeds', FULL ? 12 : 3), perSeed = argN('--per-seed', FULL ? 60 : 40);
  function fight(Mdl) {
    let guard = 0;
    while (Mdl.result === 0 && guard++ < 2000) {
      for (let pass = 0; pass < 4 && Mdl.result === 0; pass++) {
        const order = Mdl.alive(Mdl.side).filter(u => Mdl.canAct(u));
        if (!order.length) break;
        for (const u of order) {
          if (!Mdl.canAct(u) || Mdl.result !== 0) continue;
          const plan = Mdl.planFor(u);
          if (plan.move) Mdl.move(u, plan.move);
          if (Mdl.result !== 0) break;
          const act = Mdl.planAction(u);
          if (act.kind === 'attack') Mdl.attack(u, act.target);
          else if (act.kind === 'tactic') Mdl.useTactic(u, act.tactic, act.target);
          else if (act.kind === 'duel') Mdl.duel(u, act.target);
          else if (act.kind === 'special') Mdl.useSpecial(u, act.target);
          Mdl.spend(u);
        }
      }
      if (Mdl.result === 0) Mdl.endSide();
    }
    if (Mdl.result === 0) throw new Error('battle never ended');
  }
  const was = Items.enabled;
  function checkState(g, where) {
    for (const f of g.factions) if (f.alive) { const r = g.generals[f.ruler]; if (!r || r.dead || r.faction !== f.id) throw new Error(where + ': bad ruler of ' + f.name); }
    for (const x of g.generals) if (x.dead && (Object.values(x.gear || {}).some(u => u != null) || SG.Conquest.isCaptive(x) && x.faction >= 0)) throw new Error(where + ': dead ' + x.name + ' still holds gear or is captive');
  }
  for (const [label, monthsIn, gate] of [['start (AI kits only)', 0, true], ['year 3 (kits + AI purchases)', 36, false]]) {
    const texts = [];
    for (let s = 1; s <= nSeeds; s++) { const g = newGame('liubei', 300 + s); g.player = -1; months(g, monthsIn); texts.push(JSON.stringify(g)); }
    // 第三种 'core'：装备开，但不算坐骑机动（Gear.battle.mobility）与宝具（Items.actives）——§17.3 的 ±1 天门槛出自
    // design-v4/integration/results/tactical-classic-8.txt 的原型，原型只模拟了攻防乘数与会心，这两样都没有（见报告）
    const st = { false: { n: 0, w: 0, days: 0, loss: 0 }, true: { n: 0, w: 0, days: 0, loss: 0 }, core: { n: 0, w: 0, days: 0, loss: 0 } };
    const modes = gate ? [false, true, 'core'] : [false, true];
    const GBt = SG.Gear.battle, mob0 = GBt.mobility, act0 = Items.actives;
    let rulerBattles = 0, slain0 = Items.stats.slain | 0;
    texts.forEach((text, si) => {
      for (let k = 0; k < perSeed; k++) {
        for (const mode of modes) {
          const on = mode === true;
          Items.enabled = mode !== false;
          const slainBefore = Items.stats.slain | 0;
          if (mode === 'core') { GBt.mobility = () => 0; Items.actives = () => []; }
          const g = SG.G = GameState.fromJSON(text);
          g.player = -1;
          SG.Random.seed(900 + si * 100 + k);
          const cand = [];
          for (const c of g.cities) {
            if (c.owner < 0) continue;
            for (const j of c.links) { const t = g.cities[j]; if (t.owner >= 0 && t.owner !== c.owner && g.officersIn(c).some(x => x.troops > 500) && g.officersIn(t).length) cand.push([c, t]); }
          }
          if (!cand.length) continue;
          const [c, t] = cand[(k * 7 + si) % cand.length];
          const atk = g.officersIn(c).filter(x => x.troops > 0).sort((a, b) => b.troops - a.troops || a.id - b.id).slice(0, 5);
          const s = SG.Conquest.prepare(c.owner, c, t, atk, 3000, 0);
          const Mdl = new SG.BattleModel(s);
          if (on) rulerBattles += Mdl.units.filter(u => g.isRuler(u.gen)).length;
          fight(Mdl);
          if (mode === 'core') { GBt.mobility = mob0; Items.actives = act0; Items.stats.slain = slainBefore; }   // 阵斩率只数 'on' 的战斗
          const o = st[mode]; o.n++; o.days += Mdl.day; if (Mdl.result === 1) o.w++;
          for (const u of Mdl.units) o.loss += Math.max(0, (u.troops0 || 0) - u.troops);
          if (on) { SG.Conquest.apply(s); checkState(g, 'after battle ' + k); }
        }
      }
    });
    Items.enabled = was;
    const off = st.false, on = st.true;
    const dw = 100 * (on.w / on.n - off.w / off.n), dd = on.days / on.n - off.days / off.n;
    const lossK = (on.loss / on.n) / Math.max(1, off.loss / off.n);
    info(`${label}: ${off.n} paired battles: attacker win ${(100 * off.w / off.n).toFixed(1)} % → ${(100 * on.w / on.n).toFixed(1)} % (${dw >= 0 ? '+' : ''}${dw.toFixed(1)} pp); days ${(off.days / off.n).toFixed(2)} → ${(on.days / on.n).toFixed(2)}; casualties per battle ×${lossK.toFixed(3)}`);
    ok(lossK <= 1.35, `${label}: casualties per battle on / off ×${lossK.toFixed(3)} ≤ 1.35 (DESIGN-V4 §16)`);
    // 阵斩（§8.2，§17.3：君主在他打的战术战斗中 2–6 % 被阵斩）——要 battle-model.js 的接线（INTEGRATION 请求）
    const slain = (Items.stats.slain | 0) - slain0;
    const wired = String(SG.BattleModel.prototype.attack).indexOf('rollSlay') >= 0;
    info(`${label}: rulers slain in battle ${slain} of ${rulerBattles} ruler-battles (${rulerBattles ? (100 * slain / rulerBattles).toFixed(1) : 0} %)` + (wired ? '' : ' — battle-model.js does not roll 阵斩 yet (INTEGRATION request)'));
    if (wired && rulerBattles >= 100) ok(slain / rulerBattles >= 0.005 && slain / rulerBattles <= 0.08, `${label}: 阵斩 rate ${(100 * slain / rulerBattles).toFixed(1)} % near 2–6 % (sample noise: 0.5–8 %)`);
    if (gate) {
      const tol = FULL ? 3 : 5;   // 快速档 120 场，噪声大些（±5 pp）
      ok(Math.abs(dw) <= tol, `attacker win rate items off → on within ±${tol} pp (${dw.toFixed(1)})`);
      const core = st.core, ddCore = core.days / core.n - off.days / off.n;
      const mobWired = String(SG.BattleModel.prototype.attack).indexOf('attackCtx') >= 0;
      info(`${label}: without mount mobility and 宝具 (what the §17.3 prototype modelled): days ${(off.days / off.n).toFixed(2)} → ${(core.days / core.n).toFixed(2)} (${ddCore >= 0 ? '+' : ''}${ddCore.toFixed(2)}); attacker win ${(100 * core.w / core.n).toFixed(1)} %` + (mobWired ? '' : ' — battle hooks not wired yet (INTEGRATION battle-model.patch)'));
      ok(Math.abs(ddCore) <= 1, `average battle days within ±1 for the stat / crit / rule hooks the gate was calibrated on (${ddCore.toFixed(2)})`);
      // 坐骑机动（ITEMS §3.2：BUnit.mobility += clamp(Σ mob, −1, 2)）与宝具（§7.3）都按规格数值；两样合起来实测约 −0.9…−1.15 天
      flag(Math.abs(dd) <= 1, `average battle days within ±1 with every item effect incl. mount mobility and 宝具 (${dd.toFixed(2)}; spec question: the ±1 gate predates both, see report)`);
      ok(Math.abs(dd) <= 2, `average battle days within ±2 with every item effect (sanity; ${dd.toFixed(2)})`);
    }
  }
  Items.enabled = was;
});

// ======================================================================================= §3 economy / AI ==
section('§3 all-AI campaigns: AI gear spending, items per general, shops, rare cadence, 传国玉玺, reserves, time (ITEMS §17.4)', () => {
  // 快速档：经典 12 种子 × 5 年（到 195 年，传国玉玺的门槛也量）+ 世界 2 × 3 年；另跑「全部第四版系统同开」一遍量 8–15 % 的买装门槛
  const cfg = FULL ? { classic: [argN('--seeds', 40), argN('--years', 20)], world: [10, 20] } : { classic: [argN('--seeds', 12), argN('--years', 5)], world: [2, 3] };
  // 全开的快速档用 4 × 8 年（同样 32 种子年）：前 4 年官爵礼金占去钱袋的一半，8 × 4 年量的是开局而不是 §17.4 的长局份额
  const cfgAll = FULL ? { classic: [20, 20], world: [4, 20] } : { classic: [4, 8], world: [2, 3] };
  const V4 = ['Titles', 'Engage', 'Tourney'].filter(n => SG[n] && typeof SG[n] === 'object' && 'enabled' in SG[n]);
  // 进程 CPU 时间（process.cpuUsage；每月两次系统调用，不像墙钟那样被别的进程拉长）
  const cpuMs = () => { const u = process.cpuUsage(); return (u.user + u.system) / 1000; };
  function campaign(scen, seeds, years, allOn) {
    const t0 = Date.now();
    for (const n of V4) SG[n].enabled = !!allOn;
    SG.AIBudget.reset(); Items.resetStats();
    let msItems = 0, months2 = 0, cpuEnd = 0, cpuGear = 0;
    // 第 7 相（自由支出）只有装备在花钱时（只开装备）= 电脑买装 + 换装 + 卖装（§13.7）
    const A = SG.StrategyAI, pd = A.phaseDiscretionary;
    A.phaseDiscretionary = function () { const t = cpuMs(); try { return pd.apply(this, arguments); } finally { cpuGear += cpuMs() - t; } };
    const per193 = [], shopMin = [], cadence = [], sealFound = [];
    let g = null;
    try {
    for (let s = 1; s <= seeds; s++) {
      g = newGame(scen === 'world' ? 'roma' : 'liubei', 100 + s, scen);
      g.player = -1;
      const gone = {}, was = {};
      for (let m = 0; m < years * 12; m++) {
        SG.StrategyAI.runAI([]);
        const ta = process.hrtime.bigint(), ca = cpuMs();
        SG.StrategyAI.endMonth();
        cpuEnd += cpuMs() - ca;
        msItems += Number(process.hrtime.bigint() - ta) / 1e6;
        months2++;
        if (m === 0) shopMin.push(Math.min(...g.cities.map(c => c.shop.stock.length)));
        // 珍品回到本城货架的间隔：只量「过期下架」后再上架；被买走的珍品冷却 24 个月（§0），不算在 6–10 个月里
        for (const c of g.cities) {
          const now = new Set();
          for (const e of c.shop.stock) if (Items.get(e.id).rarity === 'rare') { const k = c.id + '|' + e.id; now.add(k); if (e.age === 0 && gone[k] !== undefined) { cadence.push(g.monthIndex - gone[k]); delete gone[k]; } }
          for (const id of Items.shopRow(c).rare || []) { const k = c.id + '|' + id; if (!now.has(k) && was[k] && !((g.items.cooldown[c.id] || {})[id] > g.monthIndex)) gone[k] = g.monthIndex; was[k] = now.has(k); }
        }
        if (g.year === 193 && g.month === 1) per193.push(median(g.generals.filter(x => !x.dead && x.faction >= 0 && x.war >= 60).map(x => Items.SLOTS.filter(sl => x.gear[sl] != null).length)));
        if (g.year === 195 && g.month === 1) sealFound.push(g.items.hiddenFound.indexOf('chuanguoxi') >= 0 || Items.count('chuanguoxi') > 0);
      }
    }
    } finally { A.phaseDiscretionary = pd; }
    for (const n of V4) SG[n].enabled = false;
    const a = SG.AIBudget.stats;
    const share = (a.byKind.gear || 0) / Math.max(1, a.gross);
    const sy = seeds * years, buys = Object.assign({}, Items.stats.buys), perSY = {};
    for (const t of ['common', 'uncommon', 'rare', 'epic']) perSY[t] = (buys[t] || 0) / sy;
    const tag = `${scen}${allOn ? ' (all v4 on: ' + (V4.join(' + ') || 'none loaded') + ')' : ' (items alone)'} ${seeds} seeds × ${years} y`;
    info(`${tag}: AI gear spending ${(100 * share).toFixed(2)} % of gross (${a.byKind.gear || 0} / ${Math.round(a.gross)})` + (allOn ? `; title ${(100 * (a.byKind.title || 0) / Math.max(1, a.gross)).toFixed(2)} %` : '') +
      `; AI buys ${JSON.stringify(buys)} = per seed-year common ${perSY.common.toFixed(1)} / uncommon ${perSY.uncommon.toFixed(1)} / rare ${perSY.rare.toFixed(2)}, sold ${Items.stats.sold}, hidden found ${Items.stats.hiddenFound}  [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
    // 补货的稳态成本：最后一局最后一月的货架复制 40 份，各补一次下个月的货（不取 SG.Random；取最快的四分之一，避开机器上别的负载）
    const snap = g.cities.map(c => c.shop), cp = sh => ({ stock: sh.stock.map(e => Object.assign({}, e)), merchant: sh.merchant ? Object.assign({}, sh.merchant, { stock: sh.merchant.stock.map(e => Object.assign({}, e)) }) : null });
    const rs = [];
    for (let r = 0; r < 40; r++) { g.cities.forEach((c, i) => { c.shop = cp(snap[i]); }); const t = cpuMs(); Items.restockAll(); rs.push(cpuMs() - t); }
    g.cities.forEach((c, i) => { c.shop = snap[i]; });
    rs.sort((x, y) => x - y);
    return { share, below0: a.below0, per193, shopMin, cadence, sealFound, msItems, months2, restockMs: Items.stats.restockMs, perSY, sy,
      cpuEnd: cpuEnd / months2, cpuGear: cpuGear / months2, restockQ25: rs[Math.floor(rs.length / 4)], factions: g.factions.length };
  }
  for (const scen of ['classic', 'world']) {
    const [seeds, years] = cfg[scen];
    const r = campaign(scen, seeds, years, false);
    // 只开装备时钱袋全给买装（官爵礼金、营建、节庆都不在），买装额度（CFG.ai.gearOfPurse × 钱袋份额）仍把它留在 8–15 %
    const lo1 = scen === 'world' && !FULL ? 0.07 : 0.08;   // 世界快速档只有 2 × 3 年，下限留 1 pp 的噪声
    ok(r.share >= lo1 && r.share <= 0.15, `${scen} AI gear spending (items alone) in ${(100 * lo1).toFixed(0)}–15 % (${(100 * r.share).toFixed(2)} %)`);
    eq(r.below0, 0, scen + ' no city below 0 after a gear purchase');
    if (r.per193.length) ok(median(r.per193) >= 2, `${scen} median items per AI general with war ≥ 60 in 193: ${median(r.per193)} (≥ 2)`);
    ok(Math.min(...r.shopMin) >= 4, scen + ' every city has ≥ 4 stock entries after month 1');
    const cad = r.cadence;
    if (cad.length) info(`${scen}: a listed rare comes back to its city ${avg(cad).toFixed(1)} months after it left the shelf (median ${median(cad)}; ${cad.length} returns)`);
    // §6.4 / §17.4「每 6–10 个月」：算法照规格（每个空货位 12 % 抽珍品）。前几年货架周转快，平均数在 6–10 附近（门槛 4–14 容噪声）；
    // 长局里电脑武将都配齐了，货架只靠「4 个月后每月 35 % 下架」腾位，且一城列 1–3 件珍品轮流抽，平均数 16–18 个月（中位 7）——
    // 这是规格的预期与规格的算法之间的出入，记为 FLAG（报告里请规格作者定），不改算法
    if (cad.length > 30) {
      if (years <= 6) ok(avg(cad) >= 4 && avg(cad) <= 14, scen + ` rare reappears every 6–10 months (mean ${avg(cad).toFixed(1)}, ± noise 4–14)`);
      else {
        ok(median(cad) >= 4 && median(cad) <= 14, scen + ` rare reappears: long-run median ${median(cad)} in 4–14`);
        flag(avg(cad) >= 6 && avg(cad) <= 10, `${scen} rare reappears every 6–10 months on average: long-run mean ${avg(cad).toFixed(1)} (spec algorithm vs spec expectation, see report)`);
      }
    }
    if (r.sealFound.length) info(`${scen}: 传国玉玺 found by 195 in ${r.sealFound.filter(Boolean).length} / ${r.sealFound.length} runs`);
    if (r.sealFound.length) ok(r.sealFound.filter(Boolean).length / r.sealFound.length >= 0.7, `${scen} 传国玉玺 found by 195 in ≥ 70 % of runs (${r.sealFound.filter(Boolean).length} / ${r.sealFound.length})`);
    info(`${scen}: endMonth (all phases, with items) ${(r.msItems / r.months2).toFixed(2)} ms wall per month; restock ${(r.restockMs / r.months2).toFixed(2)} ms wall per month in the campaign`);
    // §17.4 / §13.7 的时间（台式机）：endMonth + 电脑买装 ≤ 8 ms、补货 ≤ 1 ms、70 个势力的买装与换装 ≤ 3 ms。这里量进程 CPU 时间；
    // 这台机器有别的进程同跑（负载见括号），硬门槛留 ×3 的余量，规格的数记为 FLAG（夜间档 FLAG 也算退出码）
    const load = require('os').loadavg()[0].toFixed(1);
    info(`${scen}: CPU per month (load ${load}): endMonth ${r.cpuEnd.toFixed(2)} ms (target 8), AI gear phase ${r.cpuGear.toFixed(2)} ms (target 3 for 70 factions), restock steady state ${r.restockQ25.toFixed(2)} ms (target 1)`);
    ok(r.cpuEnd <= 3 * 8, `${scen}: endMonth CPU ${r.cpuEnd.toFixed(2)} ms per month ≤ 24 (×3 headroom on the 8 ms target)`);
    ok(r.cpuGear <= 3 * 3, `${scen}: AI gear phase CPU ${r.cpuGear.toFixed(2)} ms per month ≤ 9 (×3 headroom on the 3 ms target)`);
    ok(r.restockQ25 <= 3 * 1, `${scen}: restock CPU ${r.restockQ25.toFixed(2)} ms ≤ 3 (×3 headroom on the 1 ms target)`);
    flag(r.cpuEnd <= 8, `${scen}: endMonth + AI shopping ≤ 8 ms per month (ITEMS §17.4): ${r.cpuEnd.toFixed(2)} ms CPU at load ${load}`);
    flag(r.cpuGear <= 3, `${scen}: AI shopping and equipping ≤ 3 ms per month (ITEMS §13.7): ${r.cpuGear.toFixed(2)} ms CPU at load ${load}`);
    flag(r.restockQ25 <= 1, `${scen}: restock ≤ 1 ms per month (ITEMS §17.4): ${r.restockQ25.toFixed(2)} ms CPU at load ${load}`);
  }
  // INTEGRATION §5.2 / ITEMS §17.4：全部第四版系统同开时买装占总收入 8–15 %（原型：经典 11.2–11.4 %、世界 8.5 %）
  if (!V4.length) info('all-on gear-spending gate skipped: no other v4 rules module loaded');
  else for (const scen of ['classic', 'world']) {
    const [seeds, years] = cfgAll[scen];
    const r = campaign(scen, seeds, years, true);
    const lo = scen === 'world' && !FULL ? 0.07 : 0.08;   // 世界快速档只有 2 × 3 年，下限留 1 pp 的噪声
    ok(r.share >= lo && r.share <= 0.15, `${scen} AI gear spending with all v4 systems on (${V4.join(', ')}) in ${(100 * lo).toFixed(0)}–15 %: ${(100 * r.share).toFixed(2)} %`);
    eq(r.below0, 0, scen + ' all-on: no city below 0 after a gear purchase');
    // 买到什么（不只是花了多少）：设计原型（design-v4/integration，经 js/econ.js 的流水线）经典全开每种子年买
    // 前 8 年 凡 24.8 / 良 6.6 / 珍 0.05，25 年 凡 9.7（10 731 / 1 200）/ 良 8.4 / 珍 1.8。良品与（长局的）珍品不得少于原型的一半
    if (scen === 'classic') {
      const ref = years <= 10 ? { uncommon: 6.6, rare: 0.05, w: '8-year' } : { uncommon: 8.4, rare: 1.8, w: '25-year' };
      ok(r.perSY.uncommon >= ref.uncommon / 2, `classic all-on: AI uncommon purchases ${r.perSY.uncommon.toFixed(1)} per seed-year ≥ half the prototype's ${ref.w} rate ${ref.uncommon}`);
      if (ref.rare >= 1) ok(r.perSY.rare >= ref.rare / 2, `classic all-on: AI rare purchases ${r.perSY.rare.toFixed(2)} per seed-year ≥ half the prototype's ${ref.w} rate ${ref.rare}`);
      else info(`classic all-on: AI rare purchases ${r.perSY.rare.toFixed(2)} per seed-year (prototype ${ref.w}: ${ref.rare}; gated in --full)`);
    }
  }
});

// ============================================================================ §3b survival / conquest pace ==
// ITEMS §17.4「势力存续与兼并速度 与关掉装备相比在 ±10 % 内」：只开装备 vs 全关（其他第四版模块都关），经典两组种子（1–48、1001–1048）
// × 10 年；看第 10 年存活势力数与「首个势力过 25 % 城」的中位年份。夜间档合并两组判 ±10 %，每组单独的出入记 FLAG；快速档跳过（太慢）
section('§3b faction survival and conquest pace, items alone vs off (ITEMS §17.4, --full)', () => {
  if (!FULL) { info('skipped in the quick run (2 × 96 classic campaigns × 10 y; run with --full or --only §3b --full)'); return; }
  const others = ['Titles', 'Engage', 'Tourney', 'Combos', 'Crit'].filter(n => SG[n] && typeof SG[n] === 'object' && 'enabled' in SG[n] && typeof SG[n].enabled !== 'function');
  const was = Items.enabled, wasO = others.map(n => SG[n].enabled);
  for (const n of others) SG[n].enabled = false;
  const years = argN('--surv-years', 10), per = argN('--surv-seeds', 48);
  const run = (on, s0) => {
    Items.enabled = on; SG.AIBudget.reset();
    const alive = [], f25 = [];
    for (let s = s0 + 1; s <= s0 + per; s++) {
      const g = newGame('liubei', s, 'classic'); g.player = -1;
      const total = g.cities.length; let t25 = null;
      for (let m = 0; m < years * 12; m++) {
        StrategyAI.runAI([]); StrategyAI.endMonth();
        if (t25 === null) for (const f of g.factions) if (f.alive && g.cityCount(f.id) > total * 0.25) { t25 = (m + 1) / 12; break; }
      }
      alive.push(g.factions.filter(f => f.alive).length); f25.push(t25 === null ? 99 : t25);
    }
    return { alive, f25 };
  };
  const pct = (a, b) => 100 * (b - a) / a;
  const all = { off: { alive: [], f25: [] }, on: { alive: [], f25: [] } };
  try {
    for (const s0 of [0, 1000]) {
      const off = run(false, s0), on = run(true, s0);
      for (const [k, v] of [['off', off], ['on', on]]) { all[k].alive.push(...v.alive); all[k].f25.push(...v.f25); }
      const da = pct(avg(off.alive), avg(on.alive)), df = pct(median(off.f25), median(on.f25));
      info(`seeds ${s0 + 1}–${s0 + per} × ${years} y: factions alive y${years} ${avg(off.alive).toFixed(2)} → ${avg(on.alive).toFixed(2)} (${da.toFixed(1)} %); first faction > 25 % median ${median(off.f25).toFixed(2)} → ${median(on.f25).toFixed(2)} y (${df.toFixed(1)} %)`);
      flag(Math.abs(da) <= 10 && Math.abs(df) <= 10, `seeds ${s0 + 1}–${s0 + per} alone within ±10 % (alive ${da.toFixed(1)} %, pace ${df.toFixed(1)} %; one 48-seed set is noisy — the gate is the pooled 96)`);
    }
    const da = pct(avg(all.off.alive), avg(all.on.alive)), df = pct(median(all.off.f25), median(all.on.f25));
    ok(Math.abs(da) <= 10, `pooled ${2 * per} seeds: factions alive y${years} items alone vs off within ±10 % (${avg(all.off.alive).toFixed(2)} → ${avg(all.on.alive).toFixed(2)}, ${da.toFixed(1)} %)`);
    ok(Math.abs(df) <= 10, `pooled ${2 * per} seeds: conquest pace (first faction > 25 %, median year) within ±10 % (${median(all.off.f25).toFixed(2)} → ${median(all.on.f25).toFixed(2)}, ${df.toFixed(1)} %)`);
  } finally { Items.enabled = was; others.forEach((n, i) => { SG[n].enabled = wasO[i]; }); }
});

console.log(`\n${checks} checks, ${fails} failed, ${flags} flagged (ITEMS §17 spec gates still open: the spec's number and the spec's own data / algorithm disagree)  (${((Date.now() - T0) / 1000).toFixed(0)} s)`);
// FLAG 是没过的规格门槛（等规格作者定），不是「通过」：有 FLAG 时不印 ALL OK；夜间档（--full）FLAG 也算进退出码
console.log(fails ? 'FAILURES: ' + fails + (flags ? ` (+ ${flags} spec gates open)` : '') : flags ? `NO FAILURES — ${flags} SPEC GATE(S) OPEN (FLAG lines above)` : 'ALL OK');
process.exitCode = fails + (FULL ? flags : 0);
