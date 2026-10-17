'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 计策目录（DESIGN-V3 §4）

   18 种计策，分 4 阶。原四计（火计、落石、混乱、激励）规则与数值不变：仍按智力自带（不占格），
   结算走 battle-model.js 第一版的代码（kind 仍为 SG.TacticKind 的数字 0..3）。
   其余 14 种为「可习得计策」：按智力与等级解锁（SG.Progress.tacticSlots），kind 为字符串（= id），
   由 battle-model.js 的 tacticPlan / useTactic 按下表的规则字段结算；特效沿用 V.specialFx 的风格（fx），
   不需要新的画面代码。载入顺序：data.js 之后、battle-controller.js 之前（规则层在调用时才读取 SG.Tactics；
   未载入本文件时只有原四计）。

   公开接口
     SG.Tactics.all() → 全部 18 种（冻结对象）    SG.Tactics.byId(id) → 计策 | null
     SG.Tactics.innate() → 原四计（kind 0..3）     SG.Tactics.learnable() → 14 种可习得计策
     SG.Tactics.known(gen) → 该武将已会的计策（原四计按智力 + 已习得；顺序 = gen.tactics；
                            SG.Progress.enabled = false 时只有原四计）
     SG.Tactics.isNew(t) → true：第三版新增（kind 为字符串）
     SG.Tactics.explain(t) → 中文效果说明（与数值一致）   SG.Tactics.rules(t) → 紧凑摘要（「单体 · 射程4 · 冷却1日 · 需临河」）
     SG.Tactics.TIER_NAME = ['', '初阶', '中阶', '高阶', '绝阶']
   控制层辅助（bc = SG.BattleController 实例，用到 bc.M、bc.V、bc.pickEnemy、bc.showCard、bc.updateHud）
     SG.Tactics.menuItems(bc, u) → [{ tactic, item }]  策略菜单条目（SG.UI.item；不可用时灰显并注明原因）
     await SG.Tactics.menu(bc, u, moved)   → 完整流程：选计策 → 选目标 → perform → Mdl.spend(u)（取消时与旧菜单相同）
     await SG.Tactics.perform(bc, u, t, target) → res（Mdl.useTactic 的结果；原四计的 { success, dmg } 补成同一结构）：原四计播放旧特效（fireFx / rockFx / magicFx），
         新计策播放 V.specialFx(u, targets, t.fx, { sp, res, color, onHit, target }) 与飘字；不调用 spend（由调用方 spend）

   规则字段（新计策；I = 施计者智力，tI = 主目标智力）
     tier 等阶 1–4 · reqInt 需求智力 · target 'enemy' | 'ally' | 'self' · range 射程（自身为中心时 0）·
     radius 波及半径（敌：主目标周围的其他敌军；友 / 自身：周围友军）· cd 冷却日数（用后 cd 日内不能再用；0 = 每次行动都可用）·
     need 'river'（目标在河上或紧邻河流）| 'cover'（目标在林地 / 山丘上或紧邻之）
     roll { base, div, min, max, forest?, lowMorale? }：整体成功率 = clamp(base + (I − tI)/div + 加成, min, max)（缺省 = 必定成功）
     dmg { base, per, splash?, forest?, pierce? }：伤害 = (base + per × I) × clamp(敌兵/2500, 0.4, 1.4) ÷ 敌防御加成
         （城门 / 本城内 ×0.6，pierce 时不减；林地 × forest；周围敌军 × splash；范围合计封顶同必杀技）
     morale / moraleI：士气变化 = morale + moraleI × I（敌为负、友为正）
     confuse { base, div, min, max, turns }：每支受波及敌军另判混乱（base ≥ 1 为整体成功后必定混乱）
     mod { atk, def, counter, move, dot, days }：限时加成 / 削弱（敌军为削弱，友军为加成）
     heal / healI：回复 (heal + healI × I) × 最大兵力（不超过出阵兵力）· cure 解除混乱 · douse 灭火 · food 每回复 food 兵耗粮 1
     foeConfuse { base, div, min, max, turns }（自身为中心）：周围 radius 格内的敌军另判混乱
     fx / color：特效风格（V.specialFx）与主色
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const TIER_NAME = ['', '初阶', '中阶', '高阶', '绝阶'];

  // ------------------------------------------------------------ 原四计 --
  // 与 SG.Defs.tactics 同 kind、同名、同需求智力；结算仍是 battle-model.js 第一版的代码
  const ORIGINAL = [
    { id: 'fire', kind: 0, tier: 1, target: 'enemy', range: 2, fx: 'fire', color: '#ff7a2a' },
    { id: 'rockfall', kind: 1, tier: 2, target: 'enemy', range: 2, fx: 'rock', color: '#c8a070', need: 'hill-self' },
    { id: 'confuse', kind: 2, tier: 1, target: 'enemy', range: 3, fx: 'spirit', color: '#bf80ff' },
    { id: 'inspire', kind: 3, tier: 1, target: 'self', range: 0, radius: 1, fx: 'aura', color: '#ffd966' },
  ];

  // ------------------------------------------------------------ 新计策 --
  const LEARNABLE = [
    // ---- 初阶（智力 50+，3 级起） ----
    {
      id: 'rumor', name: '伪报', tier: 1, reqInt: 50, target: 'enemy', range: 4, cd: 0,
      roll: { base: 0.55, div: 100, min: 0.15, max: 0.9 }, morale: -12, moraleI: -0.125, mod: { atk: 0.85, days: 1 },
      fx: 'spirit', color: '#c9a0ff', lore: '散布假消息动摇敌军军心。',
    },
    {
      id: 'feint', name: '疑兵', tier: 1, reqInt: 50, target: 'enemy', range: 3, radius: 1, cd: 1,
      roll: { base: 0.5, div: 110, min: 0.15, max: 0.85, lowMorale: 0.1 }, morale: -8, mod: { move: -2, days: 1 },
      fx: 'shadow', color: '#9aa7b8', lore: '遍插旌旗、多设灶火，使敌疑惧不前。',
    },
    {
      id: 'supply', name: '补给', tier: 1, reqInt: 50, target: 'ally', range: 1, cd: 2,
      heal: 0.04, healI: 0.0003, morale: 8, food: 5,
      fx: 'aura', color: '#e8c66a', lore: '开仓分粮，补充战损。',
    },
    // ---- 中阶（智力 65+，5 级起） ----
    {
      id: 'ambush', name: '伏兵', tier: 2, reqInt: 65, target: 'enemy', range: 2, cd: 1, need: 'cover',
      roll: { base: 0.45, div: 100, min: 0.15, max: 0.9, forest: 0.15 }, dmg: { base: 220, per: 9, forest: 1.3 },
      morale: -12, confuse: { base: 0.3, div: 120, min: 0.05, max: 0.6, turns: 1 },
      fx: 'arrows', color: '#e0b060', lore: '预伏精兵于林间山后，待敌近前一齐杀出。',
    },
    {
      id: 'water', name: '水计', tier: 2, reqInt: 65, target: 'enemy', range: 3, radius: 1, cd: 1, need: 'river',
      roll: { base: 0.5, div: 110, min: 0.15, max: 0.9 }, dmg: { base: 240, per: 10, splash: 0.6 }, morale: -10, douse: 1,
      fx: 'water', color: '#4fb4ff', lore: '壅水决堤，灌向临河之敌。',
    },
    {
      id: 'provoke', name: '挑拨', tier: 2, reqInt: 65, target: 'enemy', range: 4, cd: 1,
      roll: { base: 0.45, div: 90, min: 0.1, max: 0.9 }, morale: -6, mod: { def: 0.75, counter: 0.5, days: 2 },
      fx: 'shock', color: '#ff9a5a', lore: '辱骂激将，使敌将怒而失据。',
    },
    // ---- 高阶（智力 80+，7 级起） ----
    {
      id: 'chain', name: '连环', tier: 3, reqInt: 80, target: 'enemy', range: 3, radius: 1, cd: 2,
      morale: -8, confuse: { base: 0.45, div: 110, min: 0.1, max: 0.85, turns: 1 },
      fx: 'spirit', color: '#b48cff', lore: '计中套计，使敌军首尾相连、进退失据。',
    },
    {
      id: 'thunder', name: '落雷', tier: 3, reqInt: 80, target: 'enemy', range: 4, cd: 1,
      roll: { base: 0.4, div: 90, min: 0.1, max: 0.85 }, dmg: { base: 300, per: 12, pierce: 1 }, morale: -15,
      fx: 'lightning', color: '#bcd8ff', lore: '登坛作法，引天雷击敌。',
    },
    {
      id: 'sorcery', name: '妖术', tier: 3, reqInt: 80, target: 'enemy', range: 3, cd: 2,
      roll: { base: 0.5, div: 100, min: 0.12, max: 0.9 }, morale: -10, confuse: { base: 1, turns: 1 }, mod: { dot: 0.05, days: 3 },
      fx: 'poison', color: '#8be05a', lore: '以符水妖法迷惑敌军，瘴疠随之而起。',
    },
    {
      id: 'rouse', name: '鼓舞', tier: 3, reqInt: 80, target: 'self', range: 0, radius: 3, cd: 2,
      morale: 10, moraleI: 0.1, mod: { atk: 1.1, days: 1 },
      fx: 'blossom', color: '#ffd966', lore: '登高陈说利害，鼓舞全军。',
    },
    {
      id: 'cure', name: '治疗', tier: 3, reqInt: 80, target: 'ally', range: 2, cd: 2,
      heal: 0.1, healI: 0.001, morale: 5, cure: 1,
      fx: 'heal', color: '#7dffa8', lore: '延请医者疗治伤兵，收拢散卒。',
    },
    // ---- 绝阶（智力 92+，9 级起） ----
    {
      id: 'weather', name: '天变', tier: 4, reqInt: 92, target: 'enemy', range: 5, radius: 2, cd: 3,
      roll: { base: 0.55, div: 110, min: 0.2, max: 0.9 }, dmg: { base: 200, per: 8, splash: 0.8 }, morale: -10,
      fx: 'lightning', color: '#9fd8ff', lore: '观天象而知风雨，呼风唤雨、雷电交加。',
    },
    {
      id: 'bazhen', name: '八阵', tier: 4, reqInt: 92, target: 'self', range: 0, radius: 2, cd: 3,
      morale: 8, mod: { def: 1.35, counter: 1.3, days: 2 },
      fx: 'shield', color: '#79b8ff', lore: '依八阵图布阵，休、生、伤、杜、景、死、惊、开，进退有序。',
    },
    {
      id: 'qimen', name: '奇门遁甲', tier: 4, reqInt: 92, target: 'self', range: 0, radius: 2, cd: 3,
      morale: 6, cure: 1, mod: { move: 2, days: 1 }, foeConfuse: { base: 0.4, div: 100, min: 0.1, max: 0.8, turns: 1 },
      fx: 'haste', color: '#7affd9', lore: '推演奇门，借地遁形：我军来去如风，敌军迷失方向。',
    },
  ];

  // ------------------------------------------------------------ 说明 --
  function pct(v) { return Math.round(v * 100) + '%'; }
  function signed(v) { return v > 0 ? '+' + v : v < 0 ? '−' + Math.abs(v) : '0'; }
  function moraleText(t) {
    if (!t.morale) return '';
    const per = t.moraleI ? (t.moraleI > 0 ? ' + 智力/' : ' − 智力/') + Math.round(1 / Math.abs(t.moraleI)) : '';
    return '士气 ' + signed(t.morale) + per;
  }
  function needText(n) { return n === 'river' ? '目标须在河上或临河' : n === 'cover' ? '目标须在林地 / 山丘或其旁' : n === 'hill-self' ? '须立于山丘' : ''; }
  function modText(m) {
    if (!m) return '';
    const p = [];
    if (m.atk) p.push('攻击 ' + (m.atk >= 1 ? '+' : '−') + pct(Math.abs(m.atk - 1)));
    if (m.def) p.push('防御 ' + (m.def >= 1 ? '+' : '−') + pct(Math.abs(m.def - 1)));
    if (m.counter) p.push('反击 ' + (m.counter >= 1 ? '+' : '−') + pct(Math.abs(m.counter - 1)));
    if (m.move) p.push('机动力 ' + signed(m.move));
    if (m.dot) p.push('中毒（每日损兵 ' + pct(m.dot) + '）');
    return p.join('、') + '（' + m.days + ' 日）';
  }
  function explain(t) {
    if (!isNew(t)) return t.desc || '';
    const parts = [];
    const tgt = t.target === 'enemy' ? (t.range + ' 格内一支敌军' + (t.radius ? '及其周围 ' + t.radius + ' 格的敌军' : ''))
      : t.target === 'ally' ? (t.range + ' 格内一支友军（含自身）') : ('自身周围 ' + t.radius + ' 格内的友军');
    parts.push((t.target === 'self' ? '对' : '对 ') + tgt);
    if (t.dmg) parts.push('造成 (' + t.dmg.base + ' + 智力×' + t.dmg.per + ') 的计策伤害' + (t.dmg.forest ? '（林地 ×' + t.dmg.forest + '）' : '') +
      (t.dmg.splash ? '，周围敌军 ×' + t.dmg.splash : '') + (t.dmg.pierce ? '，不受城防减免' : '，城门 / 本城内 ×0.6'));
    if (t.heal) parts.push('回复 ' + pct(t.heal) + ' + 智力×' + (t.healI * 100).toFixed(2).replace(/\.?0+$/, '') + '% 的最大兵力（不超过出阵兵力）' + (t.food ? '，每回复 ' + t.food + ' 兵耗粮 1' : ''));
    const mt = moraleText(t);
    if (mt) parts.push(mt);
    if (t.mod) parts.push(modText(t.mod));
    if (t.confuse) parts.push(t.confuse.base >= 1 ? '并使其混乱 ' + t.confuse.turns + ' 日' : '可能陷入混乱 ' + t.confuse.turns + ' 日（随智力差）');
    if (t.foeConfuse) parts.push('周围 ' + t.radius + ' 格内的敌军可能混乱 ' + t.foeConfuse.turns + ' 日（随智力差）');
    if (t.cure) parts.push('解除混乱');
    if (t.douse) parts.push('扑灭火势');
    let s = (t.lore || '') + parts.join('，') + '。';
    if (t.roll) s += '成功率 ' + pct(t.roll.base) + ' 起，随智力差增减（' + pct(t.roll.min) + '–' + pct(t.roll.max) + '）' + (t.roll.forest ? '，目标在林地时 +' + pct(t.roll.forest) : '') + (t.roll.lowMorale ? '，目标士气低于 50 时 +' + pct(t.roll.lowMorale) : '') + '。';
    if (t.need) s += needText(t.need) + '。';
    if (t.cd) s += '用后冷却 ' + t.cd + ' 日。';
    return s;
  }
  function rules(t) {
    if (!t) return '';
    const p = [TIER_NAME[t.tier] || ''];
    if (t.target === 'self') p.push('自身周围' + (t.radius || 1));
    else p.push((t.target === 'ally' ? '友军 · ' : '') + '射程' + t.range + (t.radius ? ' · 范围' + t.radius : ''));
    if (t.dmg) p.push('伤害 ' + t.dmg.base + '+智×' + t.dmg.per);
    if (t.heal) p.push('回复' + pct(t.heal) + '起');
    if (t.confuse || t.foeConfuse || t.kind === 2) p.push('混乱');
    if (t.mod && t.mod.dot) p.push('中毒');
    if (t.roll) p.push('成功率' + pct(t.roll.base) + '±智力差');
    if (t.need) p.push(needText(t.need));
    if (t.cd) p.push('冷却' + t.cd + '日');
    p.push('需智力' + t.reqInt);
    return p.filter(Boolean).join(' · ');
  }

  // ------------------------------------------------------------ 目录 --
  const byIdMap = new Map();
  const ALL = [];
  for (const o of ORIGINAL) {
    const d = SG.Defs.tactics[o.kind];
    const t = Object.freeze(Object.assign({ name: d.name, reqInt: d.reqInt, power: d.power, desc: d.desc, radius: 0, cd: 0, innate: true }, o));
    ALL.push(t); byIdMap.set(t.id, t);
  }
  for (const o of LEARNABLE) {
    const t = Object.assign({ kind: o.id, radius: 0, cd: 0, innate: false }, o);
    if (t.mod) t.mod = Object.freeze(Object.assign({}, t.mod));
    t.desc = explain(t);
    const f = Object.freeze(t);
    ALL.push(f); byIdMap.set(f.id, f);
  }
  function isNew(t) { return !!t && typeof t.kind === 'string'; }
  function byId(id) { return byIdMap.get(id) || null; }
  function innate() { return ALL.filter(t => t.innate); }
  function learnable() { return ALL.filter(t => !t.innate); }
  function known(gen) {
    if (!gen) return [];
    const P = SG.Progress;
    const intelOk = t => (gen.intel | 0) >= t.reqInt;
    if (!P || !P.enabled || !Array.isArray(gen.tactics)) return innate().filter(intelOk);
    const out = [];
    for (const t of innate()) if (intelOk(t)) out.push(t);          // 原四计按智力（与 tacticUsable 一致）
    for (const id of gen.tactics) { const t = byId(id); if (t && !t.innate && out.indexOf(t) < 0) out.push(t); }
    return out;
  }

  // ============================================================ 控制层辅助 --
  function esc(s) { return SG.esc ? SG.esc(s) : String(s); }
  function wait(s) { return SG.wait ? SG.wait(s) : new Promise(r => setTimeout(r, s * 1000)); }
  function toast(html, sec) { try { if (SG.UI && SG.UI.toast) SG.UI.toast(html, sec || 1.6); } catch (e) { /* 无界面 */ } }
  function uiItem(label, right, enabled, desc) {
    if (SG.UI && SG.UI.item) return SG.UI.item(label, right, enabled, desc);
    return { label, right, enabled, desc, selected: false };
  }

  function menuItems(bc, u) {
    const Mdl = bc.M;
    const list = typeof Mdl.knownTactics === 'function' ? Mdl.knownTactics(u) : SG.Defs.tactics.filter(t => u.gen.intel >= t.reqInt);
    return list.map(t => {
      const us = Mdl.tacticUsable(u, t);
      let why = us.why || '';
      const ok = us.ok && Mdl.tacticTargets(u, t).length > 0;
      if (why === '' && !ok) why = '范围内无目标';
      const name = '<span style="color:' + (t.color || '#f3c969') + '">' + esc(t.name) + '</span>';
      if (!isNew(t)) return { tactic: t, item: uiItem(name, ok ? esc(t.desc) : '<span class="sg-muted">' + esc(why) + '</span>', ok, null) };
      // 新计策：右栏只放等阶 / 原因，数值摘要放在说明行
      const tag = '<span class="sg-muted">' + TIER_NAME[t.tier] + '</span>';
      // 矮屏（横屏手机）只留前三项摘要，免得菜单过长
      const short = typeof window !== 'undefined' && window.innerHeight > 0 && window.innerHeight < 560;
      const parts = rules(t).split(' · ').slice(1);
      return { tactic: t, item: uiItem(name, ok ? tag : tag + ' <span class="sg-muted">' + esc(why) + '</span>', ok, esc((short ? parts.slice(0, 3) : parts).join(' · '))) };
    });
  }

  async function pickTarget(bc, u, t) {
    const Mdl = bc.M;
    const list = Mdl.tacticTargets(u, t);
    if (!list.length) return null;
    if (t.target === 'self' || t.kind === 3) return u;
    if (list.length === 1) return list[0];
    if (typeof bc.pickEnemy === 'function') return (await bc.pickEnemy(list, t.name + '的目标')) || null;
    return list[0];
  }

  async function menu(bc, u, moved) {
    const Mdl = bc.M;
    const rows = menuItems(bc, u);
    if (!rows.length) { toast('尚未掌握计策'); if (moved) Mdl.spend(u); return null; }
    const r = await SG.UI.choose('策略', rows.map(x => x.item), null, 640);
    if (r < 0) { if (moved) Mdl.spend(u); return null; }
    const t = rows[r].tactic;
    const tgt = await pickTarget(bc, u, t);
    if (!tgt) { if (moved) Mdl.spend(u); return null; }
    const res = await perform(bc, u, t, tgt);
    Mdl.spend(u);
    return res;
  }

  function fxTargets(res, u) {
    const out = [];
    const add = x => { if (x && out.indexOf(x) < 0) out.push(x); };
    if (res.target && res.target !== u) add(res.target);
    for (const h of res.hits || []) add(h.unit);
    for (const x of res.affected || []) add(x);
    if (!out.length) add(res.target || u);
    return out;
  }

  async function perform(bc, u, t, target) {
    const Mdl = bc && bc.M, V = bc && bc.V;
    if (!Mdl || !u || !t) return null;
    const K = SG.TacticKind;
    try { if (typeof bc.showCard === 'function') bc.showCard(u); } catch (e) { console.error(e); }
    toast(esc(u.gen.name) + '施展「<span style="color:' + (t.color || '#f3c969') + '">' + esc(t.name) + '</span>」！', 1.5);
    let res = Mdl.useTactic(u, t, target);
    // 原四计的结果只有 { success, dmg }：补成与新计策相同的结构，方便界面统一处理
    if (!isNew(t) && typeof Mdl.newRes === 'function') {
      res = Object.assign(Mdl.newRes(), res, { tactic: t, kind: t.id, name: t.name, target,
        hits: res.dmg > 0 ? [{ unit: target, dmg: res.dmg, primary: true }] : [], resisted: res.success ? [] : [target],
        routed: target && !target.alive ? [target] : [] });
    }
    const T3 = typeof THREE !== 'undefined' ? THREE : null;
    const above = (x, k) => { const p = V.tile(x.x, x.y); if (T3) p.add(new T3.Vector3(0, k || 1.6, 0)); return p; };
    const text = (x, s, col, size, k) => { try { V.floatText(above(x, k), s, col, size || 34); } catch (e) { console.error(e); } };
    if (V) {
      if (!isNew(t)) {
        // 原四计：与 battle-controller.js 的 doTactic 相同的表现
        try {
          switch (t.kind) {
            case K.Fire: await V.fireFx(target); break;
            case K.Rockfall: await V.rockFx(target); break;
            case K.Confuse: await V.magicFx(target, '#bf80ff'); break;
            default: await V.magicFx(u, '#ffd966'); break;
          }
        } catch (e) { console.error(e); }
        if (!res.success) text(target, '识破', '#a8a194', 34);
        else if (res.dmg > 0) V.hit(target, res.dmg, true);
        else if (t.kind === K.Confuse) text(target, '混乱', '#cc99ff', 38);
        else if (t.kind === K.Inspire) text(u, '士气高涨', '#73d98c', 34);
      } else {
        const shown = (res.hits || []).map(() => false);
        const onHit = i => { if (i >= 0 && i < shown.length && !shown[i]) { shown[i] = true; try { V.hit(res.hits[i].unit, res.hits[i].dmg, true); } catch (e) { console.error(e); } } };
        const sp = { id: 'tac:' + t.id, name: t.name, kind: t.id, fx: t.fx, color: t.color, radius: t.radius || 1, range: t.range };
        if (res.success || (res.hits && res.hits.length)) {
          try {
            if (typeof V.specialFx === 'function') await V.specialFx(u, fxTargets(res, u), t.fx, { sp, res, color: t.color, model: Mdl, onHit, target: res.target || target });
            else if (typeof V.magicFx === 'function') await V.magicFx(target || u, t.color);
          } catch (e) { console.error(e); }
        } else {
          try { if (typeof V.magicFx === 'function') await V.magicFx(target || u, '#a8a194'); } catch (e) { console.error(e); }
        }
        for (let i = 0; i < shown.length; i++) if (!shown[i]) onHit(i);
        if (!res.success) text(res.target || target || u, '识破', '#a8a194', 34);
        for (const x of res.confused || []) text(x, '混乱', '#cc99ff', 38, 2.4);
        for (const x of res.resisted || []) if (res.success) text(x, '识破', '#a8a194', 30, 2.4);
        for (const h of res.healed || []) text(h.unit, '+' + h.amount, '#73d98c', 40);
        const word = { rumor: '动摇', feint: '迟疑', provoke: '阵脚大乱', sorcery: '中毒' }[t.id];
        for (const x of res.cursed || []) if (word) text(x, word, t.id === 'sorcery' ? '#9be06a' : '#ffb04a', 32, 2.6);
        const buff = { rouse: '士气高涨', bazhen: '八阵护体', qimen: '机动 +2', supply: '补给' }[t.id];
        for (const x of res.buffed || []) if (buff) text(x, buff, '#f3c969', 30, 2.3);
      }
      for (const x of Mdl.units) { try { V.refresh(x); } catch (e) { console.error(e); } }
    }
    try { if (typeof bc.updateHud === 'function') bc.updateHud(); } catch (e) { console.error(e); }
    await wait(0.4);
    if (V) {
      const gone = [];
      for (const x of (res.routed || []).concat(res.affected || [], target ? [target] : [])) if (x && !x.alive && gone.indexOf(x) < 0) gone.push(x);
      for (const x of gone) if (V.isShown && V.isShown(x)) { toast(esc(x.gen.name) + '部溃散！'); await V.rout(x); }
    }
    return res;
  }

  SG.Tactics = {
    TIER_NAME,
    all() { return ALL.slice(); }, byId, innate, learnable, known, isNew, explain, rules,
    menuItems, pickTarget, menu, perform,
  };
})();
