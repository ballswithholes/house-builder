'use strict';
/* ==========================================================================
   design-v4/integration/titles-mod.js — the TITLES rules for the all-on prototype (TITLES.md §2, §5, §6, §7, §8 on titles.json),
   ported from design-v4/titles/titles-proto.js. Changes from that prototype, as INTEGRATION.md fixes them:
   - the 授官礼金 is paid through SG.AIBudget (kind 'title', from the discretionary purse, never below the city reserve);
   - AI granting runs in the discretionary phase of StrategyAI.endMonth (INTEGRATION §6.3), not at the start of runAI;
   - the city administrator rule is shared with ITEMS (INTEGRATION §4.6): admin(c) = the governor; when the governor is the
     ruler, the best active 文官 office-holder stationed in the city; otherwise the ruler (who holds no office).
   No SG.Random draws.
   ========================================================================== */

function createTitles(SG, D, AIB) {
  const G = () => SG.G;
  const B = SG.Balance;
  const POST = {}; for (const p of D.posts) POST[p.id] = p;
  const TIER = D.tiers;
  const FAMS = ['war', 'intel', 'pol'];
  const T = { rank: new Map(), seq: 0, grants: 0, gift: 0 };
  const stat = (g, f) => (f === 'war' ? g.war : f === 'intel' ? g.intel : g.pol);
  function famsOf(g) { const m = Math.max(g.war, g.intel, g.pol); return FAMS.filter(f => stat(g, f) === m); }
  function eligible(g, post) { const tr = TIER[post.tier - 1]; return famsOf(g).indexOf(post.family) >= 0 && (g.level | 0) >= tr.reqLevel && stat(g, post.family) > tr.reqStat; }
  const act = g => g && g._t && !g._t.suspended && !(SG.Conquest && SG.Conquest.isCaptive && SG.Conquest.isCaptive(g));
  function bonus(g, k) { return act(g) ? (POST[g._t.post].fx[k] || 0) : 0; }
  const curTier = g => (g._t ? g._t.tier : 0);
  function powerOf(f) { let P = 0; for (const c of G().cities) if (c.owner === f) P += 1 + (c.land + c.industry + c.town) / D.rulerRanks.power.devDiv; return P; }
  function updateRank(f) {
    const P = powerOf(f), th = D.rulerRanks.thresholds;
    let up = 1; for (let k = 0; k < th.length; k++) if (P >= th[k]) up = k + 1;
    let r = T.rank.get(f) || 1;
    if (up > r) r = up; else while (r > 1 && P < th[r - 1] * D.rulerRanks.hysteresis) r--;
    T.rank.set(f, r); return r;
  }
  const holders = f => G().generals.filter(x => x._t && x.faction === f && !x.dead);
  function validate() { const g = G(); for (const x of g.generals) if (x._t && (x.dead || x.faction !== x._t.faction || x.faction < 0 || g.isRuler(x))) delete x._t; }
  function seatCheck(f) {
    const caps = D.rulerRanks.caps[T.rank.get(f) || 1], hs = holders(f);
    for (let t = 1; t <= 5; t++) {
      const a = hs.filter(x => x._t.tier === t && !x._t.suspended).sort((p, q) => p._t.since - q._t.since);
      const s = hs.filter(x => x._t.tier === t && x._t.suspended).sort((p, q) => p._t.since - q._t.since);
      while (a.length > caps[t - 1]) { const x = a.pop(); x._t.suspended = true; }
      while (a.length < caps[t - 1] && s.length) { const x = s.shift(); x._t.suspended = false; a.push(x); }
    }
  }
  const seatsFree = (f, post) => !post.seats || holders(f).filter(x => x._t.post === post.id).length < post.seats;
  function grant(g, post) {
    const tr = TIER[post.tier - 1], best = g._tBest || 0;
    g.loyalty = Math.min(100, g.loyalty + Math.max(0, tr.loyGrant - (best ? TIER[best - 1].loyGrant : 0)));
    g._tBest = Math.max(best, post.tier);
    g._t = { post: post.id, tier: post.tier, family: post.family, since: ++T.seq, faction: g.faction, suspended: false, month: G().monthIndex };
    T.grants++;
  }
  function prefBranch(g, fam) {
    if (fam === 'war') return g.war >= 92 || g.war - g.intel >= 35 ? 'B' : 'A';
    if (fam === 'intel') return g.pol >= g.intel - 8 ? 'B' : 'A';
    return g.war > g.intel ? 'B' : 'A';
  }
  function pickPost(f, g, t) {
    for (const fam of famsOf(g)) {
      const list = D.posts.filter(p => p.tier === t && p.family === fam && eligible(g, p) && seatsFree(f, p));
      if (!list.length) continue;
      const pb = prefBranch(g, fam);
      return list.find(p => p.branch === pb) || list[0];
    }
    return null;
  }
  const merit = g => Math.max(g.war, g.intel, g.pol) + 3 * (g.level | 0);
  // TITLES §8, in the discretionary phase; the gift goes through SG.AIBudget (kind 'title')
  function aiManage(f) {
    const g = G();
    updateRank(f); seatCheck(f);
    const caps = D.rulerRanks.caps[T.rank.get(f)];
    const cands = g.generalsOf(f).filter(x => !g.isRuler(x) && !(SG.Conquest.isCaptive && SG.Conquest.isCaptive(x)));
    for (let t = 5; t >= 1; t--) {
      for (let guard = 0; guard < 12; guard++) {
        const hs = holders(f);
        if (hs.filter(x => x._t.tier === t && !x._t.suspended).length >= caps[t - 1]) break;
        const famCount = fam => hs.filter(x => !x._t.suspended && x._t.tier >= t && x._t.family === fam).length;
        let best = null, bm = -1, bp = null, bk = Infinity;
        for (const x of cands) {
          if (curTier(x) >= t || (x._t && x._t.month === g.monthIndex)) continue;
          const post = pickPost(f, x, t); if (!post) continue;
          const m = merit(x), k = famCount(post.family);
          if (k < bk || (k === bk && (m > bm || (m === bm && x.id < best.id)))) { bk = k; bm = m; best = x; bp = post; }
        }
        if (!best) break;
        const cost = TIER[t - 1].grantGold - (best._t ? TIER[best._t.tier - 1].grantGold : 0);
        const payer = cost > 0 ? AIB.payer(f, cost, 'title') : null;
        if (cost > 0 && !payer) break;
        if (cost > 0) { AIB.spend(payer, cost, 'title'); T.gift += cost; }
        grant(best, bp);
      }
    }
  }
  function monthStart() { validate(); for (const f of G().factions) if (f.alive) { updateRank(f.id); seatCheck(f.id); } }
  // the shared city administrator (INTEGRATION §4.6)
  function admin(c) {
    const g = G();
    if (c.governor < 0) return null;
    const gov = g.generals[c.governor];
    if (!g.isRuler(gov)) return gov;
    let best = null;
    for (const x of g.officersIn(c)) if (act(x) && x._t.family === 'pol' && (!best || x._t.tier > best._t.tier || (x._t.tier === best._t.tier && (x.pol > best.pol || (x.pol === best.pol && x.id < best.id))))) best = x;
    return best || gov;
  }
  function natBonus(f, k) { let v = 0; for (const x of holders(f)) v = Math.max(v, bonus(x, k)); return v; }
  function garrison(c) { let v = 0; for (const o of G().officersIn(c)) v = Math.max(v, bonus(o, 'garrison')); return v; }
  function shield(g) { return g._t && !g.dead ? TIER[g._t.tier - 1].effLoy : 0; }        // kept while suspended (§6.6)
  function persuadeRes(g) { return g._t ? 0.1 * g._t.tier : 0; }
  return { T, bonus, admin, natBonus, garrison, shield, persuadeRes, aiManage, monthStart, updateRank, validate, act, POST };
}
module.exports = { createTitles };
