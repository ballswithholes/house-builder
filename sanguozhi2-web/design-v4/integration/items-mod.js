'use strict';
/* ==========================================================================
   design-v4/integration/items-mod.js — the ITEMS rules the all-on prototype needs (ITEMS.md §2–§8, §13), on items.json:
   instances, starting kits (§8.10), signature / ruler-unique / armoury / city-treasure placement, hidden items (AI search, §13.6),
   shops (tiers, seeded restock §6.4), AI equipping (§13.2) and buying (§13.3, through SG.AIBudget), prize and reward grants
   (INTEGRATION §7.2: prize pools draw shop rares not owned by any faction), and the gear stat providers for SG.Mods / SG.Econ.
   Approximations (stated in INTEGRATION §2): abilities are not simulated one by one; a battle-scope ability counts as an
   attack stand-in inside the battle cap (rare 3 %, epic 7 %, legendary 10 %); domestic and world abilities (cmd, income,
   harvest, loyalAura, fame, virtue, dipPlus, expBoost) are applied exactly. No SG.Random draws: every roll is seeded.
   ========================================================================== */

function createItems(SG, D, AIB, opts) {
  opts = opts || {};
  const G = () => SG.G;
  const B = SG.Balance;
  const ITEM = Object.fromEntries(D.items.map(x => [x.id, x]));
  const SLOTS = ['weapon', 'offhand', 'armour', 'helmet', 'mount', 'treasure'];
  const TIER = { common: 0, uncommon: 1, rare: 2, epic: 3, legendary: 4 };
  const UNIT = {}; for (const [k, v] of Object.entries(D.statKeys)) UNIT[k] = v;
  const hash32 = s => { let h = 0x811c9dc5; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 0x01000193); } return h >>> 0; };
  const rng = (...k) => SG.SeededRandom(hash32(G().seed + '|' + G().monthIndex + '|items|' + k.join('|')));
  const round10 = v => Math.round(v / 10) * 10;
  const ABILITY_ATK = [0, 0, 0.03, 0.07, 0.10];
  const ST = { spend: 0, buys: { common: 0, uncommon: 0, rare: 0, epic: 0 }, sold: 0, grants: {}, hiddenFound: 0, equipChanges: 0 };

  // ------------------------------------------------------------ state --
  function init(g) {
    const sid = g.scenario === 'world' ? 'world' : 'classic';
    g._it = { sid, owned: new Set(), arm: g.factions.map(() => []), cd: {}, hidden: (D[sid].hidden || []).map(h => Object.assign({ found: false }, h)), seen: 0 };
    for (const x of g.generals) x._gear = { weapon: null, offhand: null, armour: null, helmet: null, mount: null, treasure: null };
    // kits for everybody (§8.10)
    for (const x of g.generals) kit(x);
    // signature gear (§8.4), ruler-uniques (§8.3), armouries (§8.7), city treasures (§8.6)
    const byName = new Map(g.generals.map(x => [x.name, x]));
    for (const s of D[sid].signature || []) { const x = byName.get(s.holder); if (x) put(x, s.item); }
    for (const f of g.factions) { const r = D[sid].rulers[f.key]; const ru = g.ruler(f.id); if (r && ru) { put(ru, r.item); ru._bound = r.item; } }
    for (const a of D[sid].armoury || []) { const f = g.factionByKey(a.faction); if (f) addArm(f.id, a.item); }
    for (const t of D[sid].cityTreasure || []) { const c = g.cityByKey(t.city); if (c && c.owner >= 0) addArm(c.owner, t.item); }
    // one common per city of its culture into every faction armoury
    for (const c of g.cities) if (c.owner >= 0) { const k = D.kits[c.culture] || D.kits.han; if (k && k.armourLight) addArm(c.owner, k.armourLight); }
    for (const c of g.cities) { c._shop = { stock: [] }; restock(c, true); }
    for (const f of g.factions) if (f.alive) aiEquip(f.id);
  }
  function put(x, id) { const it = ITEM[id]; if (!it) return; const old = x._gear[it.slot]; if (old && old !== id && G()._it) { /* displaced kit piece is dropped */ } x._gear[it.slot] = id; if (it.rarity !== 'common' && it.rarity !== 'uncommon') G()._it.owned.add(id); }
  function addArm(f, id) { if (f < 0 || !ITEM[id]) return; G()._it.arm[f].push(id); const it = ITEM[id]; if (TIER[it.rarity] >= 2) G()._it.owned.add(id); }
  function cultureOf(x) { return x.culture || (x.faction >= 0 ? G().factions[x.faction].culture : 'han') || 'han'; }
  function kit(x) {
    const k = D.kits[cultureOf(x)] || D.kits.han;
    if (!k) return;
    const civ = x.intel > x.war + 25 && x.war < 55;
    let w = null;
    if (civ) w = k.byType.fan || k.byType.staff || null;
    if (!w) {
      const pref = x.war >= 75 ? ['spear', 'ji', 'poleblade', 'lance', 'dao', 'sword', 'spatha', 'tachi', 'khanda', 'gladius'] : ['dao', 'sword', 'spear', 'spatha', 'gladius', 'tachi', 'khanda', 'mace', 'ji', 'lance'];
      const opts2 = pref.map(t => k.byType[t]).filter(Boolean);
      if (opts2.length) w = opts2[hash32(x.name) % Math.min(3, opts2.length)];
    }
    if (w) x._gear.weapon = w;
    if (civ) { if (k.robe) x._gear.armour = k.robe; if (k.cap) x._gear.helmet = k.cap; }
    else { const a = x.war >= 55 ? k.armour : k.armourLight; if (a) x._gear.armour = a; if (x.war >= 70 && k.helmet) x._gear.helmet = k.helmet; else if (x.intel > x.war + 25 && k.cap) x._gear.helmet = k.cap; }
    if (x.war >= 65 && k.mount) x._gear.mount = k.mount;
    const wi = x._gear.weapon && ITEM[x._gear.weapon];
    if (wi && wi.duel && wi.duel.style === 'one' && x.war >= 50 && k.shield) x._gear.offhand = k.shield;
    else if (x.war >= 50 && k.bow && !civ) x._gear.offhand = k.bow;
  }

  // ------------------------------------------------------------ stats --
  function reqOK(x, it) {
    const r = it.req; if (!r) return 1;
    if (r.ruler) return G().isRuler(x) ? 1 : 0.5;
    for (const k of ['war', 'intel', 'pol']) if (r[k] && x[k] < r[k]) return r.below === 'half' ? 0.5 : 0;
    if (r.weaponStyle === 'one') { const w = x._gear.weapon && ITEM[x._gear.weapon]; if (!w || !w.duel || w.duel.style !== 'one') return 0; }
    return 1;
  }
  // raw sum of a stat over equipped items whose requirement is met (item units), capped by statKeys cap
  function raw(x, k) {
    if (!x || !x._gear) return 0;
    let s = 0;
    for (const sl of SLOTS) { const id = x._gear[sl]; if (!id) continue; const it = ITEM[id]; const v = it.stats && it.stats[k]; if (v) s += v * reqOK(x, it); }
    const cap = UNIT[k] && UNIT[k].cap;
    return cap !== undefined ? Math.min(cap, s) : s;
  }
  const frac = (x, k) => { const u = UNIT[k] && UNIT[k].unit; const v = raw(x, k); return u === '%' || u === 'pp' ? v / 100 : v; };
  function abilities(x, t) {
    const out = [];
    if (!x || !x._gear) return out;
    for (const sl of SLOTS) { const id = x._gear[sl]; if (!id) continue; const it = ITEM[id]; if (!it.ability || !reqOK(x, it)) continue; for (const e of it.ability.effects) if (!t || e.t === t) out.push(e); }
    return out;
  }
  const injury = x => (x.injury && x.injury.until > G().monthIndex ? x.injury.war || 0 : 0);
  function eff(x, k) { if (!x) return 0; const base = x[k]; const v = Math.min(110, base + Math.min(15, raw(x, k))); return k === 'war' ? v - injury(x) : v; }
  function duelWar(x) { return Math.min(110, x.war + Math.min(6, Math.floor(raw(x, 'war') / 2))) - injury(x); }
  const cmdK = (x, cmd, kind) => { let v = 0; for (const e of abilities(x, 'cmd')) if (e.cmd === cmd && (!kind || !e.kind || e.kind === kind)) v += e.k; return v; };
  const P = {
    eff,
    gearAtk: x => frac(x, 'atk'),
    gearDef: x => frac(x, 'def'),
    gearCrit: x => Math.min(0.04, raw(x, 'crit') / 100),
    gearCritMul: x => Math.min(0.30, raw(x, 'critDmg') / 100),
    gearMorale: x => raw(x, 'morale'),
    auraAtk: u => { let best = 0; if (!u.gen || !u.gen._gear) return 0; for (const sl of SLOTS) { const id = u.gen._gear[sl]; if (!id) continue; const it = ITEM[id]; if (it.ability && it.ability.effects.some(e => !/^(cmd|free|income|harvest|pop|loyalAura|expMonthly|talent|fame|virtue|dipPlus|giftK|treasureHunt|scout|shopDiscount|aggro|deterrence|omen|expBoost|d[A-Z].*)$/.test(e.t))) best += ABILITY_ATK[TIER[it.rarity]]; } return Math.min(0.12, best); },
    gearDev: (x, key) => { const st = key === 'land' ? 'land' : key === 'industry' ? 'ind' : 'town'; const ck = cmdK(x, 'develop', st); return Math.min(0.60, raw(x, st) / 100 + (ck > 1 ? ck / 100 : ck)); },
    gearRecruit: x => { const ck = cmdK(x, 'recruit'); return Math.min(0.40, raw(x, 'recruit') / 100 + (ck > 1 ? ck / 100 : ck)); },
    gearTrain: x => Math.min(10, raw(x, 'train') + cmdK(x, 'train')),
    gearSearch: x => raw(x, 'search') / 100,
    gearHire: x => Math.min(0.20, (raw(x, 'hire') + cmdK(x, 'hire')) / 100),
    gearDip: x => { let v = raw(x, 'dip'); for (const e of abilities(x, 'dipPlus')) v += e.k || 0; return Math.min(0.25, v / 100); },
    gearDiscord: x => Math.min(0.20, (raw(x, 'discord') + cmdK(x, 'discord')) / 100),
    gearPersuade: x => Math.min(0.20, (raw(x, 'persuade') + cmdK(x, 'persuade')) / 100),
    expBoost: x => { let v = raw(x, 'exp') / 100; for (const e of abilities(x, 'expBoost')) v += e.k; return Math.min(0.40, v); },
    gearFame: f => { const r = f >= 0 ? G().ruler(f) : null; if (!r) return 0; let v = raw(r, 'fame'); for (const e of abilities(r, 'fame')) v += e.n || 0; return Math.min(20, v); },
    gearVirtue: f => { const r = f >= 0 ? G().ruler(f) : null; if (!r) return 0; let v = raw(r, 'virtue'); for (const e of abilities(r, 'virtue')) v += e.n || 0; return Math.min(12, v); },
  };
  // city effects through the city administrator (INTEGRATION §4.6)
  function itemCity(c, admin, key) {
    if (!admin) return 0;
    let v = 0;
    if (key === 'income') for (const e of abilities(admin, 'income')) v += e.gold || 0;
    if (key === 'harvest') { for (const e of abilities(admin, 'harvest')) v += e.k || 0; for (const e of abilities(admin, 'income')) v += 0; }
    return Math.min(0.25, v);
  }
  function itemLoyAura(c, admin) { if (!admin) return 0; let v = 0; for (const e of abilities(admin, 'loyalAura')) if (!e.factionWide) v = Math.max(v, e.n || 0); return Math.min(3, v + raw(admin, 'loyal')); }

  // ------------------------------------------------------------ shops --
  const SHOPS = () => D.shops[G()._it.sid];
  function shopOf(c) { return SHOPS()[c.key]; }
  function priceIn(c, id, foreign) {
    const it = ITEM[id], s = shopOf(c); const base = it.price || it.value || 100;
    return round10(base * (s ? s.priceK : 1) * (foreign ? 1.2 : 1));
  }
  function restock(c, first) {
    const s = shopOf(c); if (!s) return;
    const st = c._shop.stock, k = rng('restock', c.id);
    for (const e of st) e.age++;
    for (let i = st.length - 1; i >= 0; i--) { const e = st[i], it = ITEM[e.id]; if ((TIER[it.rarity] <= 1 && e.age >= 4 && k.nextDouble() < 0.35) || (it.rarity === 'rare' && e.age >= 12)) st.splice(i, 1); }
    for (const id of s.epic || []) if (!G()._it.owned.has(id) && !st.some(e => e.id === id)) st.push({ id, qty: 1, price: priceIn(c, id), age: 0 });
    const cd = G()._it.cd[c.id] = G()._it.cd[c.id] || {};
    let guard = 0;
    while (st.length < s.slots && guard++ < 40) {
      const r = k.nextDouble();
      let id = null, foreign = false;
      const rares = (s.rare || []).filter(x => !st.some(e => e.id === x) && !(cd[x] > G().monthIndex));
      if (s.tier >= 2 && rares.length && r < 0.12) id = rares[Math.floor(k.nextDouble() * rares.length)];
      else if (s.tier >= 2 && (s.uncommon || []).length && r < 0.37) id = s.uncommon[Math.floor(k.nextDouble() * s.uncommon.length)];
      else { const useF = (s.commonForeign || []).length && k.nextDouble() < 0.25; const pool = useF ? s.commonForeign : s.common; if (pool && pool.length) { id = pool[Math.floor(k.nextDouble() * pool.length)]; foreign = useF; } }
      if (!id || st.some(e => e.id === id)) continue;
      st.push({ id, qty: TIER[ITEM[id].rarity] <= 0 ? 1 + Math.floor(k.nextDouble() * 3) : 1, price: priceIn(c, id, foreign), age: 0 });
    }
  }

  // ------------------------------------------------------------ AI valuation (§13.1) --
  const W = {
    war: { war: 3, atk: 2.0, def: 1.8, crit: 2.5, critDmg: 0.5, charge: 0.6, first: 0.5, counter: 0.5, counterGuard: 0.8, armorBreak: 1.0, morale: 0.5, awe: 0.6, mob: 12, escape: 0.15, dHp: 0.6, dDmg: 1.5, dGuard: 0.8, critRes: 1.5 },
    intel: { intel: 3, tac: 1.5, tacHit: 2.5, def: 1.0, atk: 0.6, search: 1.0, discord: 1.0, persuade: 1.0 },
    pol: { pol: 3, land: 1.2, ind: 1.2, town: 1.2, recruit: 0.8, train: 1.5, hire: 1.5, trade: 1.0, dip: 1.5, loyal: 10, reward: 2, heal: 0.5 },
    ruler: { fame: 4, virtue: 3, dip: 2, hire: 1 },
  };
  function role(x) { if (x.war >= x.intel && x.war >= x.pol || x.war >= 70) return 'war'; if (x.pol > x.intel) return 'pol'; return 'intel'; }
  function score(x, id) {
    if (!id) return 0;
    const it = ITEM[id];
    const ok = reqOK(x, it); if (!ok) return -1e9;
    const r = role(x), w = W[r];
    let s = 0;
    for (const [k, v] of Object.entries(it.stats || {})) s += (w[k] || 0) * v;
    if (G().isRuler(x)) for (const [k, v] of Object.entries(it.stats || {})) s += (W.ruler[k] || 0) * v;
    if (it.ability) s += 6 * TIER[it.rarity] + 4;
    return s * ok;
  }
  function genOrder(f) { const g = G(); return g.generalsOf(f).filter(x => !x.dead).sort((a, b) => ((g.isRuler(b) ? 1000 : 0) + b.war + b.intel + b.pol + b.troops / 100) - ((g.isRuler(a) ? 1000 : 0) + a.war + a.intel + a.pol + a.troops / 100)); }
  function aiEquip(f) {
    const g = G(), arm = g._it.arm[f];
    if (!arm.length) return;
    for (const x of genOrder(f)) {
      for (const sl of SLOTS) {
        if (x._bound && x._gear[sl] === x._bound) continue;
        let best = -1, bs = score(x, x._gear[sl]) * 1.05 + 0.01;
        for (let i = 0; i < arm.length; i++) { const it = ITEM[arm[i]]; if (it.slot !== sl) continue; const s = score(x, arm[i]); if (s > bs) { bs = s; best = i; } }
        if (best >= 0) { const id = arm.splice(best, 1)[0]; if (x._gear[sl]) arm.push(x._gear[sl]); x._gear[sl] = id; ST.equipChanges++; }
      }
    }
    // sell overflow (§13.4): lowest value first, above 30
    while (arm.length > 30) { arm.sort((a, b) => (ITEM[a].value || ITEM[a].price || 0) - (ITEM[b].value || ITEM[b].price || 0)); const id = arm.shift(); const cap = g.citiesOf(f)[0]; const it = ITEM[id]; if (cap && TIER[it.rarity] <= 2) { cap.gold += round10((it.price || it.value || 0) * (TIER[it.rarity] >= 2 ? 0.5 : 0.4)); ST.sold++; } if (TIER[it.rarity] >= 2) g._it.owned.delete(id); }
  }
  // AI buying through SG.AIBudget (INTEGRATION §5): ≤ 2 purchases a month (a rare counts 2)
  function aiShop(f) {
    const g = G();
    const ncity = g.cityCount(f);
    let slots = 2;
    const gens = genOrder(f);
    for (let pass = 0; pass < 2 && slots > 0; pass++) {
      let best = null, bv = 0;
      for (const c of g.citiesOf(f)) {
        for (const e of c._shop.stock) {
          const it = ITEM[e.id];
          const t = TIER[it.rarity];
          if (t >= 2 && slots < 2) continue;
          if (it.rarity === 'rare' && !(ncity >= 3 || c.gold >= 1500)) continue;
          if (it.rarity === 'epic' && ncity < 8) continue;
          if (!AIB.canSpend(c, e.price, 'gear')) continue;
          let d = 0;
          for (const x of gens.slice(0, 12)) { const dd = score(x, e.id) - score(x, x._gear[it.slot]); if (dd > d) d = dd; }
          if (d <= 2) continue;
          const v = d / e.price;
          if (v > bv) { bv = v; best = { c, e }; }
        }
      }
      if (!best) break;
      const { c, e } = best, it = ITEM[e.id];
      AIB.spend(c, e.price, 'gear'); ST.spend += e.price; ST.buys[it.rarity] = (ST.buys[it.rarity] || 0) + 1;
      if (--e.qty <= 0) c._shop.stock.splice(c._shop.stock.indexOf(e), 1);
      if (it.rarity === 'rare' || it.rarity === 'epic') { g._it.owned.add(e.id); (g._it.cd[c.id] = g._it.cd[c.id] || {})[e.id] = g.monthIndex + 24; }
      g._it.arm[f].push(e.id);
      slots -= TIER[it.rarity] >= 2 ? 2 : 1;
      aiEquip(f);
    }
  }
  // hidden items: AI cities with one search there every third month (§13.6); seeded roll, ITEMS §8.5 probability
  function hiddenSearch() {
    const g = G();
    for (const h of g._it.hidden) {
      if (h.found) continue;
      if (h.from) { const [y, m] = h.from.split('-').map(Number); if (g.year * 12 + g.month < y * 12 + m) continue; }
      const c = g.cityByKey(h.city); if (!c || c.owner < 0) continue;
      if ((g.monthIndex + c.id) % 3 !== 0) continue;
      const s = g.officersIn(c).sort((a, b) => eff(b, 'intel') - eff(a, 'intel'))[0]; if (!s) continue;
      const p = Math.min(0.40, h.p + eff(s, 'intel') * h.intelK + P.gearSearch(s) / 2);
      if (rng('hidden', h.item).nextDouble() < p) { h.found = true; addArm(c.owner, h.item); ST.hiddenFound++; aiEquip(c.owner); }
    }
  }
  // grants from other systems (events, missions, caravans, expeditions, 文会, tournament): INTEGRATION §7.2
  function pool(rarity, culture, f) {
    const g = G();
    const items = D.items.filter(it => it.rarity === rarity && it.availability[g._it.sid]);
    if (rarity === 'rare') {   // shop rares not owned by any faction (a prize copy never blocks the shop; shops may hold their own copy)
      const shopRares = items.filter(it => it.availability[g._it.sid].kind === 'shop' && !g._it.owned.has(it.id));
      return shopRares;
    }
    const native = items.filter(it => it.culture === culture && it.availability[g._it.sid].kind === 'shop');
    return native.length ? native : items.filter(it => it.availability[g._it.sid].kind === 'shop');
  }
  function grant(f, rarity, why, culture, k) {
    const g = G(); if (f < 0 || !g.factions[f].alive) return null;
    const cult = culture || g.factions[f].culture || 'han';
    let pl = pool(rarity, cult, f);
    if (!pl.length && rarity === 'rare') { pl = pool('uncommon', cult, f); }
    if (!pl.length) return null;
    const r = k || rng('grant', f, why, ST.grants[why] || 0);
    const it = pl[Math.floor(r.nextDouble() * pl.length)];
    addArm(f, it.id); ST.grants[why] = (ST.grants[why] || 0) + 1;
    aiEquip(f);
    return it.id;
  }
  // a faction destroyed: its armoury goes to the conqueror of its last city (§8.8)
  function transferArmoury(from, to) { const g = G(); if (from < 0 || to < 0) return; g._it.arm[to].push(...g._it.arm[from]); g._it.arm[from] = []; }
  function monthEnd() {
    const g = G();
    for (const c of g.cities) restock(c);
    hiddenSearch();
  }
  function aiPhase(f) { aiShop(f); }
  return { ITEM, init, P, eff, duelWar, raw, itemCity, itemLoyAura, restock, aiEquip, aiShop, aiPhase, monthEnd, grant, transferArmoury, score, ST, abilities };
}
module.exports = { createItems };
