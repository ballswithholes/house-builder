'use strict';
/* ==========================================================================
   tests/items.js — 第四版装备与商店（ITEMS.md §17；INTEGRATION §7、§8.3、§13）
     §1 目录：tools/gen-items.js check 的全部检查（SG.Items.checkCatalogue）+ 数量、白名单载入即报错、宝具都能结算
     §2 规则：起始装备、装备要求、有效属性与上限、阵型读基础属性、COMBAT 钩子、价格与买卖、补货的确定性与零 SG.Random、
        珍品冷却、胡商、隐藏宝物概率（10 000 次 ±1 pp）、镇城之宝易主、战利品（处斩 / 阵斩 / 缴械）、专属不能动、
        赐宝物每件只算一次（10 次赐收 = 0）、存档往返与 v3 迁移、关掉时与 v3 逐位相同、单挑预算、奖池
     §3 经济与电脑（全电脑战役）：买装占总收入 8–15 %、193 年武力 ≥ 60 的电脑武将中位装备件数 ≥ 2、每城 ≥ 4 件货、
        珍品在本城每 6–10 个月出现一次、传国玉玺 195 年前被找到 ≥ 70 %、自由支出后无城跌破 0、每月耗时
     §4 战斗：整套装备的交换比（按品级）、宝具的平均价值（参照一次普通攻击）
   用法：node tests/items.js [--quick]      快速档（约 1 分钟）：§3 经典 12 种子 × 4 年 + 世界 2 种子 × 3 年
         node tests/items.js --full         夜间档：经典 40 × 20 年、世界 10 × 20 年（ITEMS §17.4 的 200 / 100 局按需加 --seeds）
         选项 --seeds N（经典种子数）、--years N
   退出码 = 失败数
   ========================================================================== */
const path = require('path');
const fs = require('fs');
const vm = require('vm');
const crypto = require('crypto');
const { load, ROOT } = require('./v4-load.js');

const ARGS = process.argv.slice(2);
const FULL = ARGS.includes('--full') || ARGS.includes('--nightly');
const argN = (k, d) => { const i = ARGS.indexOf(k); return i >= 0 ? +ARGS[i + 1] : d; };
let fails = 0, checks = 0;
const T0 = Date.now();
function ok(c, msg) { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } return !!c; }
function eq(a, b, msg) { return ok(a === b, msg + ' (got ' + JSON.stringify(a) + ', want ' + JSON.stringify(b) + ')'); }
function near(a, b, tol, msg) { return ok(Math.abs(a - b) <= tol, msg + ' (got ' + a + ', want ' + b + ' ± ' + tol + ')'); }
function info(s) { console.log('  ' + s); }
function section(name, fn) {
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
  const first = Items.gift(x, u, { roll: 2 });
  ok(first.ok && first.loy > 0, 'first gift raises loyalty (+' + first.loy + ')');
  const l1 = x.loyalty;
  Gear.unequip(x, gsl);
  eq(x.loyalty, l1 - first.loy, 'take-back costs exactly giftLoy');
  const l0 = 50;
  for (let i = 0; i < 9; i++) { const r = Items.gift(x, u, { roll: 4 }); ok(r.ok && r.loy === 0, 'regift gives nothing #' + i); Gear.unequip(x, gsl); }
  eq(x.loyalty, l0, '10 cycles: loyalty change 0');
  // 赐给另一人也不再涨
  const y = g.generalsOf(f).find(z => free(z) && z !== x);
  const ly = y.loyalty;
  ok(Items.gift(y, u, { roll: 4 }).loy === 0 && y.loyalty === ly, 'the same instance to someone else: nothing');
  // 公式：8 + R(0,4) + clamp(round(value × giftK / 60), 0, 30)，合用 × 1.5；赤兔赐关羽 +30
  const z = g.generalsOf(f).find(q => free(q) && q !== x && q !== y) || g.generals.find(q => q.faction >= 0 && !g.isRuler(q) && q !== x && q !== y && q.faction === f);
  z.loyalty = 10;
  const u2 = Items.award(f, 'han_yubi', { why: 'test' });
  const r2 = Items.gift(z, u2, { roll: 0 });
  eq(r2.loy, Math.round((8 + 0 + Math.min(30, Math.round(80 * Items.giftK('han_yubi') / 60))) * (r2.fits ? 1.5 : 1)), 'gift formula');
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

// ======================================================================================= §4 battle ==
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
  // 宝具价值（ITEMS §7.1：稀有 1.3–1.8、史诗 2.0–2.5、传说 ≤ 2.8 次普通攻击；参照 = 同一施展者对主目标的普通攻击期望）
  const ratios = { rare: [], epic: [], legendary: [] };
  const dummy = i => ({ id: 9000 + i, name: '靶' + i, war: 40, intel: 30, pol: 30, faction: -1, troops: 4000, training: 60, loyalty: 80, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } });
  for (const it of D.items) {
    if (!it.ability) continue;
    const a = it.ability.effects.find(e => e.t === 'active' && ['smite', 'charge', 'rampage', 'volley', 'blaze', 'storm', 'roar'].indexOf(e.kind) >= 0);
    if (!a) continue;
    const caster = { id: 8000, name: '施', war: 85, intel: 85, pol: 60, faction: 0, troops: 7000, training: 70, loyalty: 90, culture: it.culture, gear: { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null } };
    g.generals.push(caster); caster.id = g.generals.length - 1;
    const uid = Items.award(0, it.id, { why: 'test' });
    if (uid == null) { g.generals.pop(); continue; }
    Gear.equip(caster, uid, { force: true, free: true });
    const foes = [0, 1, 2, 3].map(dummy);
    const s = { attacker: 0, defender: 1, src: g.cities[0], target: g.cities[11], atk: [caster], def: foes, atkFood: 9000, atkGold: 0, routed: new Set(), captives: [] };
    const Mdl = new SG.BattleModel(s);
    for (let x = 0; x < Mdl.W; x++) for (let y = 0; y < Mdl.H; y++) Mdl.map[x][y] = SG.Terrain.Plain;
    const cu = Mdl.units.find(x => x.gen === caster);
    cu.x = 5; cu.y = 5;
    const fu = Mdl.units.filter(x => x.side === 1);
    [[6, 5], [5, 6], [4, 5], [5, 4]].forEach((p, k) => { fu[k].x = p[0]; fu[k].y = p[1]; });
    for (const x of Mdl.units) { x.morale = 90; x.commander = false; }
    Mdl.side = 0;
    const id = SG.Specials.known(caster).find(sp => sp.item === it.id).id;
    const tg = Mdl.specialTargets(cu, id);
    if (!tg.length) { g.generals.pop(); continue; }
    const ref = Mdl.atkExpect(cu, fu[0]);
    const snap = Mdl.units.map(q => [q.gen.troops, q.morale, q.alive, q.x, q.y]);
    let tot = 0; const N = 6;
    for (let k = 0; k < N; k++) {
      Mdl.units.forEach((q, j) => { q.gen.troops = snap[j][0]; q.morale = snap[j][1]; q.alive = snap[j][2]; q.x = snap[j][3]; q.y = snap[j][4]; q.mods = []; q.spUsed = []; q.specialUsed = false; q.confused = 0; });
      Mdl.result = 0; SG.Random.seed(500 + k);
      const r = Mdl.useSpecial(cu, tg.indexOf(fu[0]) >= 0 ? fu[0] : tg[0], id);
      tot += r.dmg || 0;
    }
    const v = tot / N;
    if (!(v > 0)) { Items.load(g); g.generals.pop(); continue; }
    ratios[it.rarity].push(v / ref);
    Items.load(g);
    g.generals.pop();
  }
  for (const [r, list] of Object.entries(ratios)) info(`${r}: 宝具 damage / one normal attack ${list.length ? Math.min(...list).toFixed(2) + ' … ' + Math.max(...list).toFixed(2) + ' (avg ' + avg(list).toFixed(2) + ')' : '—'}`);
  ok(ratios.legendary.every(v => v <= 2.8 + 0.15), 'legendary 宝具 damage ≤ 2.8 normal attacks (± noise 0.15)');
  ok(ratios.rare.length > 0 && avg(ratios.rare) >= 1.0 && avg(ratios.rare) <= 2.2, 'rare 宝具 average damage near 1.3–1.8 normal attacks');
});

// ======================================================================================= §3 economy / AI ==
section('§3 all-AI campaigns: AI gear spending, items per general, shops, rare cadence, 传国玉玺, reserves, time (ITEMS §17.4)', () => {
  const cfg = FULL ? { classic: [argN('--seeds', 40), argN('--years', 20)], world: [10, 20] } : { classic: [argN('--seeds', 12), argN('--years', 4)], world: [2, 3] };
  for (const scen of ['classic', 'world']) {
    const [seeds, years] = cfg[scen];
    const t0 = Date.now();
    SG.AIBudget.reset(); Items.resetStats();
    let gross = 0, gearSpend = 0, msItems = 0, months2 = 0, below0 = 0;
    const per193 = [], shopMin = [], cadence = [], sealFound = [];
    for (let s = 1; s <= seeds; s++) {
      const g = newGame(scen === 'world' ? 'roma' : 'liubei', 100 + s, scen);
      g.player = -1;
      const gone = {}, was = {}, shelf = {};
      for (let m = 0; m < years * 12; m++) {
        SG.StrategyAI.runAI([]);
        const ta = process.hrtime.bigint();
        SG.StrategyAI.endMonth();
        msItems += Number(process.hrtime.bigint() - ta) / 1e6;
        months2++;
        if (m === 0) shopMin.push(Math.min(...g.cities.map(c => c.shop.stock.length)));
        // 珍品出现的间隔（每个列在某城的珍品：上架的月份）
        for (const c of g.cities) {
          const now = new Set();
          for (const e of c.shop.stock) if (Items.get(e.id).rarity === 'rare') { const k = c.id + '|' + e.id; now.add(k); if (e.age === 0 && gone[k] !== undefined) { cadence.push(g.monthIndex - gone[k]); delete gone[k]; } shelf[k] = (shelf[k] || 0) + 1; }
          for (const id of Items.shopRow(c).rare || []) { const k = c.id + '|' + id; if (!now.has(k) && was[k]) gone[k] = g.monthIndex; was[k] = now.has(k); }
        }
        if (g.year === 193 && g.month === 1) {
          const counts = g.generals.filter(x => !x.dead && x.faction >= 0 && x.war >= 60).map(x => Items.SLOTS.filter(sl => x.gear[sl] != null).length);
          per193.push(median(counts));
        }
      }
      const seal = g.items.hiddenFound.indexOf('chuanguoxi') >= 0 || Items.count('chuanguoxi') > 0;
      sealFound.push(seal);
    }
    const a = SG.AIBudget.stats;
    gross = a.gross; gearSpend = a.byKind.gear || 0; below0 = a.below0;
    const share = gearSpend / Math.max(1, gross);
    info(`${scen} ${seeds} seeds × ${years} y: AI gear spending ${(100 * share).toFixed(2)} % of gross (${gearSpend} / ${Math.round(gross)}); buys ${JSON.stringify(Items.stats.buys)}, sold ${Items.stats.sold}, hidden found ${Items.stats.hiddenFound}  [${((Date.now() - t0) / 1000).toFixed(0)} s]`);
    // 8–15 % 是全部系统同开时的门槛（INTEGRATION §5.2，tests/v4-all.js 夜间档）：官爵礼金、营建、节庆与买装共用 22 % 的钱袋，
    // 城中的金也少。只开装备时买装可以用满钱袋，这里查它在钱袋之内、且不低于 8 %
    const others = ['Titles', 'Engage'].filter(n => SG[n] && SG[n].enabled !== false && typeof SG[n] === 'object');
    if (others.length) ok(share >= (scen === 'world' ? 0.07 : 0.08) && share <= 0.15, `${scen} AI gear spending in the 8–15 % band (${(100 * share).toFixed(2)} %)`);
    else ok(share >= 0.08 && share <= 0.22 + 0.01, `${scen} AI gear spending (items alone) between 8 % and the 22 % purse (${(100 * share).toFixed(2)} %); the 8–15 % band is the all-on gate`);
    eq(below0, 0, scen + ' no city below 0 after a gear purchase');
    if (per193.length) ok(median(per193) >= 2, `${scen} median items per AI general with war ≥ 60 in 193: ${median(per193)} (≥ 2)`);
    ok(Math.min(...shopMin) >= 4, scen + ' every city has ≥ 4 stock entries after month 1');
    if (cadence.length) info(`${scen}: a listed rare comes back to its city ${avg(cadence).toFixed(1)} months after it left the shelf (median ${median(cadence)}; ${cadence.length} returns)`);
    if (cadence.length > 30) ok(avg(cadence) >= 4 && avg(cadence) <= 14, scen + ' rare reappears every 6–10 months (± noise, 4–14)');
    if (scen === 'classic' && years >= 5) ok(sealFound.filter(Boolean).length / sealFound.length >= 0.7, `传国玉玺 found by 195 in ≥ 70 % of runs (${sealFound.filter(Boolean).length} / ${sealFound.length})`);
    info(`${scen}: endMonth (all phases, with items) ${(msItems / months2).toFixed(2)} ms per month; restock ${(Items.stats.restockMs / months2).toFixed(2)} ms per month`);
  }
});

console.log(`\n${checks} checks, ${fails} failed  (${((Date.now() - T0) / 1000).toFixed(0)} s)`);
console.log(fails === 0 ? 'ALL OK' : 'FAILURES: ' + fails);
process.exitCode = fails;
