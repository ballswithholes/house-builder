'use strict';
/* ==========================================================================
   tests/domestic-tiers.js — 内政指令的结果分级（DOMESTIC-FX §4、§13.1；INTEGRATION §4.7、§13）
   node tests/domestic-tiers.js [--full] [seeds] [months]       退出码 = 失败数
     1. 规则：§4.2 每一行与 §5.18–§5.28 增补的手写记录 → 期望的分级与朱印（含边界：g0 未跨百、999 封顶的 roll 4、
        p = 0.2、rollRaw = 1.04 / 1.10、拉拢记录的 city 是城编号而不是布尔值……）；纯函数（同一记录同一结果、不改记录）
     2. 不取随机数：第四版规则层载入 / 不载入 domestic-tiers.js 各跑一局经典剧本（种子 7，玩家曹操按脚本下令 + 电脑 5 年），
        状态哈希相同、随机数次数相同；tier() 执行期间 SG.Random 一次也没被调用
     3. 结果记录的格式：每条指令写 Commands.lastResult 的 §3.2 字段（含 Econ 的 g0 / pct / mods，秋收的 rollRaw），
        model.js 写下的 res.tier = SG.DomesticTiers.tier(res).tier；开发的分级与 SG.Econ.tierDevelop 一致
     4. 分布（design-v4/domestic/tiers.js 的脚本玩家，搬到第四版全部规则上：12 种子 × 18 位君主 × 60 个月）：
        开发 ×3 与训练「大成」5–16 %、「好」18–40 %；任何指令（n ≥ 200）「大成」≤ 30 %；
        秋收（rollRaw）歉收 10–24 %、好 18–40 %、丰年 ≤ 17 %
   缺省（快速档，约 1 分钟）：分布跑 4 种子 × 18 位君主 × 36 个月；--full（夜间档）：规格的 12 种子 × 60 个月（约 4–5 分钟）。
   门槛两档相同（只对 n ≥ 200 的指令判定）。
   ========================================================================== */
const path = require('path');
const crypto = require('crypto');
const { load } = require('./v4-load.js');

const ARGS = process.argv.slice(2);
const FULL = ARGS.indexOf('--full') >= 0;
const QUICK = !FULL;
const NUMS = ARGS.filter(a => /^\d+$/.test(a)).map(Number);
const SEEDS = NUMS[0] || (FULL ? 12 : 4);
const MONTHS = NUMS[1] || (FULL ? 60 : 36);

let fails = 0, checks = 0;
const ok = (c, msg) => { checks++; if (!c) { fails++; console.log('  FAIL ' + msg); } };

// ============================================================ 1. rules ==
console.log('== 1. tier rules (§4.2, §5.18–§5.28)');
const SG = load({ v4: true });
const DT = SG.DomesticTiers;
ok(!!DT && typeof DT.tier === 'function', 'SG.DomesticTiers loaded with the rule layer');
const gw = (war, intel, pol) => ({ war, intel, pol });
const CASES = [
  // ---- 开发
  ['develop normal', { cmd: 'develop', kind: 'land', roll: 2, before: 312, after: 326, g0: 14 }, 'normal'],
  ['develop roll 4 → good', { cmd: 'develop', kind: 'land', roll: 4, before: 310, after: 330, g0: 20 }, 'good'],
  ['develop g0 crosses a hundred → good', { cmd: 'develop', kind: 'industry', roll: 1, before: 395, after: 403, g0: 8 }, 'good'],
  ['develop gain crosses 100 only through modifiers → normal', { cmd: 'develop', kind: 'town', roll: 1, before: 92, after: 100, g0: 7 }, 'normal'],
  ['develop before 99 after 100, g0 1 → good (the crossing is in g0)', { cmd: 'develop', kind: 'town', roll: 0, before: 99, after: 100, g0: 1 }, 'good'],
  ['develop land stage 300 → great 丰穰', { cmd: 'develop', kind: 'land', roll: 1, before: 295, after: 305, g0: 10 }, 'great', '丰穰'],
  ['develop industry stage 450 → great 百炼', { cmd: 'develop', kind: 'industry', roll: 0, before: 445, after: 455, g0: 10 }, 'great', '百炼'],
  ['develop town stage 800 → great 殷阜', { cmd: 'develop', kind: 'town', roll: 2, before: 790, after: 805, g0: 15 }, 'great', '殷阜'],
  ['develop visible stage unchanged (hysteresis snapshot) → good not great', { cmd: 'develop', kind: 'land', roll: 1, before: 590, after: 605, g0: 15, stBefore: 3, stAfter: 3 }, 'good'],
  ['develop snapshot stage up → great', { cmd: 'develop', kind: 'land', roll: 0, before: 440, after: 452, g0: 12, stBefore: 1, stAfter: 2 }, 'great', '丰穰'],
  ['develop roll 4 at the 999 cap → great (max)', { cmd: 'develop', kind: 'land', roll: 4, before: 990, after: 999, g0: 24 }, 'great', '丰穰'],
  ['develop already at 999 → good on roll 4', { cmd: 'develop', kind: 'town', roll: 4, before: 999, after: 999, g0: 20 }, 'good'],
  ['develop failed (no gold) → none', { cmd: 'develop', kind: 'land', ok: false, reason: 'gold' }, null],
  // ---- 征兵
  ['recruit 1000 → normal', { cmd: 'recruit', n: 1000, before: 2000, after: 3000, max: 8000, cityBefore: 5000, cityAfter: 6000 }, 'normal'],
  ['recruit 2000 → good', { cmd: 'recruit', n: 2000, before: 2000, after: 4000, max: 8000, cityBefore: 5000, cityAfter: 7000 }, 'good'],
  ['recruit fills the general → good', { cmd: 'recruit', n: 500, before: 7500, after: 8000, max: 8000, cityBefore: 5000, cityAfter: 5500 }, 'good'],
  ['recruit full with n ≥ 3000 → great', { cmd: 'recruit', n: 3000, before: 5000, after: 8000, max: 8000, cityBefore: 4000, cityAfter: 7000 }, 'great', '云集'],
  ['recruit city crosses 10 000 → great', { cmd: 'recruit', n: 1000, before: 1000, after: 2000, max: 8000, cityBefore: 9500, cityAfter: 10500 }, 'great', '云集'],
  ['recruit city lands exactly on 10 000 → great', { cmd: 'recruit', n: 1000, before: 1000, after: 2000, max: 8000, cityBefore: 9000, cityAfter: 10000 }, 'great'],
  ['recruit city crosses 20 000 → great', { cmd: 'recruit', n: 800, before: 1000, after: 1800, max: 8000, cityBefore: 19500, cityAfter: 20300 }, 'great'],
  ['recruit refused (limit) → none', { cmd: 'recruit', ok: false, n: 0, reason: 'limit' }, null],
  // ---- 训练
  ['train normal', { cmd: 'train', roll: 2, before: 50, after: 62, troops: 3000, max: 8000 }, 'normal'],
  ['train roll 4 → good', { cmd: 'train', roll: 4, before: 50, after: 64, troops: 3000, max: 8000 }, 'good'],
  ['train 百战 with ≥ 90 % troops → great', { cmd: 'train', roll: 1, before: 90, after: 100, troops: 7600, max: 8000 }, 'great', '精锐'],
  ['train 百战 with a thin army → good', { cmd: 'train', roll: 1, before: 90, after: 100, troops: 4000, max: 8000 }, 'good'],
  ['train crosses 80 on roll 3 → good', { cmd: 'train', roll: 3, before: 74, after: 87, troops: 3000, max: 8000 }, 'good'],
  ['train crosses 80 on roll 2 → normal', { cmd: 'train', roll: 2, before: 74, after: 86, troops: 3000, max: 8000 }, 'normal'],
  ['train no troops → none', { cmd: 'train', ok: false, reason: 'troops' }, null],
  // ---- 搜索
  ['search nothing → miss', { cmd: 'search', ok: false, outcome: 'none', value: 0 }, 'miss'],
  ['search gold 159 → normal', { cmd: 'search', outcome: 'gold', value: 159 }, 'normal'],
  ['search gold 160 → good', { cmd: 'search', outcome: 'gold', value: 160 }, 'good'],
  ['search food 1899 → normal', { cmd: 'search', outcome: 'food', value: 1899 }, 'normal'],
  ['search food 1900 → good', { cmd: 'search', outcome: 'food', value: 1900 }, 'good'],
  ['search talent → great 得贤', { cmd: 'search', outcome: 'talent', found: gw(70, 85, 60) }, 'great', '得贤', 'talent'],
  ['search 人中龙凤 (≥ 90) → great star', { cmd: 'search', outcome: 'talent', found: gw(60, 92, 80) }, 'great', '得贤', 'star'],
  ['search hidden item → great 宝现', { cmd: 'search', outcome: 'item', item: 12 }, 'great', '宝现'],
  // ---- 登用
  ['hire refused → miss', { cmd: 'hire', ok: false, p: 0.5, target: gw(60, 60, 60) }, 'miss'],
  ['hire max 70 p 0.6 → normal', { cmd: 'hire', ok: true, p: 0.6, target: gw(70, 50, 40) }, 'normal'],
  ['hire max 75 → good', { cmd: 'hire', ok: true, p: 0.6, target: gw(40, 75, 50) }, 'good'],
  ['hire max 90 → great 归心', { cmd: 'hire', ok: true, p: 0.6, target: gw(90, 50, 40) }, 'great', '归心'],
  ['hire long shot p 0.44 → good', { cmd: 'hire', ok: true, p: 0.44, target: gw(50, 50, 50) }, 'good'],
  ['hire p exactly 0.20 → good', { cmd: 'hire', ok: true, p: 0.20, target: gw(50, 50, 50) }, 'good'],
  ['hire p 0.19 → great', { cmd: 'hire', ok: true, p: 0.19, target: gw(50, 50, 50) }, 'great', '归心'],
  // ---- 移动
  ['move 1 general → normal', { cmd: 'move', gens: 1, troops: 3000, hops: 1, frontline: false }, 'normal'],
  ['move 3 generals → good', { cmd: 'move', gens: 3, troops: 6000, hops: 1 }, 'good'],
  ['move 10 000 troops → good', { cmd: 'move', gens: 2, troops: 10000, hops: 1 }, 'good'],
  ['move 3 cities of route → good', { cmd: 'move', gens: 1, troops: 1000, hops: 3 }, 'good'],
  ['move 5 generals → great 星驰', { cmd: 'move', gens: 5, troops: 9000, hops: 1 }, 'great', '星驰'],
  ['move 20 000 to the front → great', { cmd: 'move', gens: 2, troops: 20000, hops: 1, frontline: true }, 'great'],
  ['move 20 000 to the rear → good', { cmd: 'move', gens: 2, troops: 20000, hops: 1, frontline: false }, 'good'],
  ['move no route → none', { cmd: 'move', ok: false, reason: 'route' }, null],
  // ---- 输送
  ['transport small → normal', { cmd: 'transport', gold: 100, food: 1000, destFoodBefore: 5000, destNeed: 100 }, 'normal'],
  ['transport 500 gold → good', { cmd: 'transport', gold: 500, food: 0, destFoodBefore: 5000, destNeed: 100 }, 'good'],
  ['transport 5000 food → good', { cmd: 'transport', gold: 0, food: 5000, destFoodBefore: 50000, destNeed: 100 }, 'good'],
  ['transport 1500 gold → great 辇输', { cmd: 'transport', gold: 1500, food: 0, destFoodBefore: 5000, destNeed: 100 }, 'great', '辇输'],
  ['transport 15 000 food → great', { cmd: 'transport', gold: 0, food: 15000, destFoodBefore: 50000, destNeed: 100 }, 'great'],
  ['transport rescue (< 2 months → ≥ 6) → great', { cmd: 'transport', gold: 0, food: 500, destFoodBefore: 150, destNeed: 100 }, 'great', '辇输', 'rescue'],
  ['transport not a rescue (had 2.5 months) → normal', { cmd: 'transport', gold: 0, food: 500, destFoodBefore: 250, destNeed: 100 }, 'normal'],
  ['transport nothing → none', { cmd: 'transport', gold: 0, food: 0, destFoodBefore: 0, destNeed: 0 }, null],
  // ---- 同盟
  ['ally refused → miss', { cmd: 'ally', ok: false, p: 0.5, targetCities: 2, ourCities: 4 }, 'miss'],
  ['ally easy, small partner → normal', { cmd: 'ally', ok: true, p: 0.6, targetCities: 2, ourCities: 4 }, 'normal'],
  ['ally p 0.44 → good', { cmd: 'ally', ok: true, p: 0.44, targetCities: 2, ourCities: 4 }, 'good'],
  ['ally p 0.24 → great 盟成', { cmd: 'ally', ok: true, p: 0.24, targetCities: 2, ourCities: 4 }, 'great', '盟成'],
  ['ally a peer → good', { cmd: 'ally', ok: true, p: 0.6, targetCities: 4, ourCities: 4 }, 'good'],
  ['ally a great power (≥ 2× and ≥ 4) → great', { cmd: 'ally', ok: true, p: 0.6, targetCities: 8, ourCities: 4 }, 'great'],
  ['ally 3 cities vs our 1 (≥ 2× but < 4) → good', { cmd: 'ally', ok: true, p: 0.6, targetCities: 3, ourCities: 1 }, 'good'],
  // ---- 离间
  ['discord seen through → miss', { cmd: 'discord', ok: false, d: 0, loyaltyAfter: 70, p: 0.5 }, 'miss'],
  ['discord on a ruler → none', { cmd: 'discord', ok: false, d: 0, loyaltyAfter: 100, p: 0, reason: 'ruler' }, null],
  ['discord d 5 → normal', { cmd: 'discord', ok: true, d: 5, loyaltyAfter: 60 }, 'normal'],
  ['discord d 10 → good', { cmd: 'discord', ok: true, d: 10, loyaltyAfter: 60 }, 'good'],
  ['discord d 18 → great 离心', { cmd: 'discord', ok: true, d: 18, loyaltyAfter: 60 }, 'great', '离心'],
  ['discord falls below 40 → great', { cmd: 'discord', ok: true, d: 5, loyaltyAfter: 38 }, 'great', '离心', 'wavering'],
  ['discord already below 40 → normal', { cmd: 'discord', ok: true, d: 5, loyaltyAfter: 30 }, 'normal'],
  // ---- 拉拢
  ['persuade refused → miss', { cmd: 'persuade', ok: false, surrendered: false, target: gw(60, 60, 60) }, 'miss'],
  ['persuade defects → good', { cmd: 'persuade', ok: true, surrendered: false, target: gw(70, 60, 50) }, 'good'],
  ['persuade the record city is a city id, not a surrender → good', { cmd: 'persuade', ok: true, city: 3, surrendered: false, target: gw(70, 60, 50) }, 'good'],
  ['persuade 献城 → great 倒戈', { cmd: 'persuade', ok: true, city: 3, surrendered: true, target: gw(50, 50, 50) }, 'great', '倒戈'],
  ['persuade a star (≥ 85) → great', { cmd: 'persuade', ok: true, surrendered: false, target: gw(85, 40, 40) }, 'great'],
  // ---- 出征
  ['attack small → normal', { cmd: 'attack', gens: 2, troops: 8000, ruler: false }, 'normal'],
  ['attack 3 generals 12 000 → good', { cmd: 'attack', gens: 3, troops: 12000, ruler: false }, 'good'],
  ['attack 20 000 → good', { cmd: 'attack', gens: 1, troops: 20000, ruler: false }, 'good'],
  ['attack 40 000 → great 出师', { cmd: 'attack', gens: 5, troops: 40000, ruler: false }, 'great', '出师'],
  ['attack the ruler leads 5 with 20 000 → great', { cmd: 'attack', gens: 5, troops: 20000, ruler: true }, 'great'],
  ['attack the ruler leads 4 with 25 000 → good', { cmd: 'attack', gens: 4, troops: 25000, ruler: true }, 'good'],
  // ---- 赏赐
  ['reward normal', { cmd: 'reward', roll: 2, before: 70, after: 80, gift: null, giftFits: false }, 'normal'],
  ['reward roll 4 → good', { cmd: 'reward', roll: 4, before: 70, after: 82, gift: null, giftFits: false }, 'good'],
  ['reward reaches 100 from 90 → good', { cmd: 'reward', roll: 1, before: 90, after: 100, gift: null }, 'good'],
  ['reward reaches 100 from 92 → normal', { cmd: 'reward', roll: 0, before: 92, after: 100, gift: null }, 'normal'],
  ['reward 100 on a top roll → great 恩泽', { cmd: 'reward', roll: 4, before: 95, after: 100, gift: null }, 'great', '恩泽'],
  ['reward an item gift → good', { cmd: 'reward', before: 70, after: 80, gift: 17, giftFits: false }, 'good'],
  ['reward a fitting gift → great', { cmd: 'reward', before: 70, after: 95, gift: 17, giftFits: true }, 'great', '恩泽'],
  ['reward no gold → none', { cmd: 'reward', ok: false, reason: 'gold' }, null],
  // ---- 交易
  ['trade buy at 12 → normal', { cmd: 'trade', side: 'buy', price: 12, gold: 300, goldOut: 300 }, 'normal'],
  ['trade buy at 8 → good', { cmd: 'trade', side: 'buy', price: 8, gold: 300, goldOut: 300 }, 'good'],
  ['trade buy 600 gold → good', { cmd: 'trade', side: 'buy', price: 12, gold: 600, goldOut: 600 }, 'good'],
  ['trade buy 1000 at 8 → great 居奇', { cmd: 'trade', side: 'buy', price: 8, gold: 1000, goldOut: 1000 }, 'great', '居奇'],
  ['trade sell at 16 for 1200 → great', { cmd: 'trade', side: 'sell', price: 16, gold: 1200, goldOut: 1200 }, 'great'],
  ['trade sell at 12 for 700 → good', { cmd: 'trade', side: 'sell', price: 12, gold: 700, goldOut: 700 }, 'good'],
  ['trade sell at 16 for 100 → good', { cmd: 'trade', side: 'sell', price: 16, gold: 100, goldOut: 100 }, 'good'],
  ['trade nothing → none', { cmd: 'trade', ok: false, side: 'buy', price: 12, gold: 0 }, null],
  // ---- 任命
  ['appoint 60 → normal', { cmd: 'appoint', pol: 60, prevPol: 55 }, 'normal'],
  ['appoint 75 → good', { cmd: 'appoint', pol: 75, prevPol: 70 }, 'good'],
  ['appoint +15 over the previous governor → good', { cmd: 'appoint', pol: 70, prevPol: 55 }, 'good'],
  ['appoint 90 → great 得人', { cmd: 'appoint', pol: 90, prevPol: 80 }, 'great', '得人'],
  ['appoint the first governor (prev 0) → normal', { cmd: 'appoint', pol: 70, prevPol: 0 }, 'normal'],
  // ---- 市场 · 授官
  ['market common → normal', { cmd: 'market', side: 'buy', rarity: 'common' }, 'normal'],
  ['market uncommon → normal', { cmd: 'market', side: 'buy', rarity: 'uncommon' }, 'normal'],
  ['market rare → good', { cmd: 'market', side: 'buy', rarity: 'rare' }, 'good'],
  ['market epic → great 神兵', { cmd: 'market', side: 'buy', rarity: 'epic' }, 'great', '神兵'],
  ['market legendary → great', { cmd: 'market', side: 'buy', rarity: 'legendary' }, 'great'],
  ['market sell → normal', { cmd: 'market', side: 'sell', rarity: 'epic' }, 'normal'],
  ['title tier 1 → normal', { cmd: 'title', titleTier: 1 }, 'normal'],
  ['title tier 3 → good', { cmd: 'title', titleTier: 3 }, 'good'],
  ['title tier 4 → great 册封', { cmd: 'title', titleTier: 4 }, 'great', '册封'],
  ['title tier 5 → great', { cmd: 'title', titleTier: 5 }, 'great'],
  // ---- 秋收（rollRaw，不看收成 / 土地之比）
  ['harvest rollRaw 0.89 → miss', { cmd: 'harvest', rollRaw: 0.89, land: 700 }, 'miss'],
  ['harvest rollRaw 0.90 → normal', { cmd: 'harvest', rollRaw: 0.90, land: 700 }, 'normal'],
  ['harvest rollRaw 1.039 → normal', { cmd: 'harvest', rollRaw: 1.039, land: 700 }, 'normal'],
  ['harvest rollRaw 1.04 → good', { cmd: 'harvest', rollRaw: 1.04, land: 300 }, 'good'],
  ['harvest rollRaw 1.10, land 600 → great 大有', { cmd: 'harvest', rollRaw: 1.10, land: 600 }, 'great', '大有'],
  ['harvest rollRaw 1.10, land 599 → good', { cmd: 'harvest', rollRaw: 1.10, land: 599 }, 'good'],
  ['harvest: the modified ratio is ignored when rollRaw is there', { cmd: 'harvest', rollRaw: 1.0, roll: 1.2, land: 700 }, 'normal'],
  // ---- ENGAGEMENT（§5.18–§5.28）
  ['build start → normal', { cmd: 'build', project: 'canal', cost: 300, supervisor: gw(40, 50, 60), done: false }, 'normal'],
  ['build start, supervisor 政治 80 → good', { cmd: 'build', project: 'canal', cost: 300, supervisor: gw(40, 50, 80), done: false }, 'good'],
  ['build start, cost 500 → good', { cmd: 'build', project: 'canal', cost: 500, supervisor: gw(40, 50, 60), done: false }, 'good'],
  ['build completion → great 落成', { cmd: 'build', project: 'canal', cost: 300, done: true }, 'great', '落成'],
  ['build a wonder starts → great', { cmd: 'build', project: 'wonder', cost: 2000, wonder: true, done: false }, 'great'],
  ['caravan departs → normal', { cmd: 'caravan', to: 3, gold: 400 }, 'normal'],
  ['caravan lost → miss', { cmd: 'caravan', lost: true, profit: 0 }, 'miss'],
  ['caravan profit 100 → normal', { cmd: 'caravan', profit: 100, items: [] }, 'normal'],
  ['caravan profit 150 → good', { cmd: 'caravan', profit: 150, items: [] }, 'good'],
  ['caravan brings an item → good', { cmd: 'caravan', profit: 50, items: [{ rarity: 'common' }] }, 'good'],
  ['caravan profit 300 → great 满载', { cmd: 'caravan', profit: 300, items: [] }, 'great', '满载'],
  ['caravan a rare came home → great', { cmd: 'caravan', profit: 40, items: [{ rarity: 'rare' }] }, 'great'],
  ['explore departs → normal', { cmd: 'explore', site: 4 }, 'normal'],
  ['explore mishap → miss', { cmd: 'explore', outcome: 'mishap' }, 'miss'],
  ['explore exp, prestige 5 → normal', { cmd: 'explore', outcome: 'exp', prestige: 5 }, 'normal'],
  ['explore prestige 10 → good', { cmd: 'explore', outcome: 'exp', prestige: 10 }, 'good'],
  ['explore a common item → good', { cmd: 'explore', outcome: 'item', rarity: 'common' }, 'good'],
  ['explore a rare → great 探幽', { cmd: 'explore', outcome: 'item', rarity: 'rare' }, 'great', '探幽'],
  ['explore a treasure-map clue → great', { cmd: 'explore', outcome: 'clue' }, 'great'],
  ['gift Δ5 → normal', { cmd: 'gift', target: 1, gold: 100, dRel: 5 }, 'normal'],
  ['gift Δ10 → good', { cmd: 'gift', target: 1, gold: 200, dRel: 10 }, 'good'],
  ['gift Δ20 → great 交欢', { cmd: 'gift', target: 1, gold: 400, dRel: 20 }, 'great', '交欢'],
  ['gift an item → great', { cmd: 'gift', target: 1, item: 9, dRel: 8 }, 'great'],
  ['marriage refused → miss', { cmd: 'marriage', ok: false, p: 0.4 }, 'miss'],
  ['marriage → great 秦晋', { cmd: 'marriage', ok: true, p: 0.4 }, 'great', '秦晋'],
  ['tribute refused → miss', { cmd: 'tribute', mode: 'demand', ok: false, targetCities: 3, ourCities: 4 }, 'miss'],
  ['tribute offered to a peer → normal', { cmd: 'tribute', mode: 'offer', ok: true, targetCities: 2, ourCities: 2 }, 'normal'],
  ['tribute offered to a faction ≥ 2× → good', { cmd: 'tribute', mode: 'offer', ok: true, targetCities: 4, ourCities: 2 }, 'good'],
  ['tribute demanded from a small faction → good', { cmd: 'tribute', mode: 'demand', ok: true, targetCities: 2, ourCities: 6 }, 'good'],
  ['tribute demanded from ≥ 3 cities → great 来朝', { cmd: 'tribute', mode: 'demand', ok: true, targetCities: 3, ourCities: 6 }, 'great', '来朝'],
  ['hostage departs → normal', { cmd: 'hostage', gen: 4, back: false }, 'normal'],
  ['hostage returns home → great 归国', { cmd: 'hostage', gen: 4, back: true }, 'great', '归国'],
  ['truce refused → miss', { cmd: 'truce', ok: false, p: 0.5, targetTroops: 5000, ourTroops: 10000 }, 'miss'],
  ['truce easy, weaker foe → normal', { cmd: 'truce', ok: true, p: 0.6, targetTroops: 5000, ourTroops: 10000 }, 'normal'],
  ['truce p 0.44 → good', { cmd: 'truce', ok: true, p: 0.44, targetTroops: 5000, ourTroops: 10000 }, 'good'],
  ['truce with a foe ≥ our troops → great 息兵', { cmd: 'truce', ok: true, p: 0.6, targetTroops: 12000, ourTroops: 10000 }, 'great', '息兵'],
  ['tradepact refused → miss', { cmd: 'tradepact', ok: false, targetCities: 1, ourCities: 2 }, 'miss'],
  ['tradepact → normal', { cmd: 'tradepact', ok: true, rel: 20, targetCities: 1, ourCities: 2 }, 'normal'],
  ['tradepact relation 40 → good', { cmd: 'tradepact', ok: true, rel: 40, targetCities: 1, ourCities: 2 }, 'good'],
  ['tradepact partner ≥ 2× → great 互市', { cmd: 'tradepact', ok: true, rel: 10, targetCities: 4, ourCities: 2 }, 'great', '互市'],
  ['submit → great 归附', { cmd: 'submit', from: 3, cities: 2, gens: 5 }, 'great', '归附'],
  ['mission tier 1 → normal', { cmd: 'mission', id: 'm1', missionTier: 1 }, 'normal'],
  ['mission tier 2 → good', { cmd: 'mission', id: 'm2', missionTier: 2 }, 'good'],
  ['mission tier 3 → great 功成', { cmd: 'mission', id: 'm3', missionTier: 3 }, 'great', '功成'],
  ['festival modest → normal', { cmd: 'festival', grade: 'modest', family: 'hua' }, 'normal'],
  ['festival grand → great 同乐', { cmd: 'festival', grade: 'grand', family: 'hua' }, 'great', '同乐'],
  ['wenhui 3rd → normal', { cmd: 'wenhui', place: 3 }, 'normal'],
  ['wenhui 2nd → good', { cmd: 'wenhui', place: 2 }, 'good'],
  ['wenhui 1st → great 魁首', { cmd: 'wenhui', place: 1 }, 'great', '魁首'],
  // ENGAGEMENT 实际写的字段（js/engage.js：sub、invest、item、clue）
  ['build (engage) sub done → great', { cmd: 'build', project: 'canal', sub: 'done' }, 'great', '落成'],
  ['caravan (engage) sub depart → normal', { cmd: 'caravan', ok: true, sub: 'depart', invest: 300 }, 'normal'],
  ['caravan (engage) return profit 200 → good', { cmd: 'caravan', ok: true, sub: 'return', profit: 200, item: null, lost: false }, 'good'],
  ['caravan (engage) return with a rare item → great', { cmd: 'caravan', ok: true, sub: 'return', profit: 20, item: { rarity: 'rare' }, lost: false }, 'great'],
  ['explore (engage) return clue → great', { cmd: 'explore', ok: true, sub: 'return', clue: true }, 'great', '探幽'],
  ['explore (engage) return mishap → miss', { cmd: 'explore', ok: false, sub: 'return' }, 'miss'],
  ['explore (engage) return gold only → normal', { cmd: 'explore', ok: true, sub: 'return', gold: 80 }, 'normal'],
  ['festival (engage) sub grand → great', { cmd: 'festival', ok: true, sub: 'grand', fam: 'hua' }, 'great', '同乐'],
  ['stageUp → great 升格', { cmd: 'stageUp', city: 1, building: 'farms', stage: 2 }, 'great', '升格'],
  ['month → normal', { cmd: 'month' }, 'normal'],
];
let ruleBad = 0;
for (const [desc, rec, want, stamp, why] of CASES) {
  const snap = JSON.stringify(rec);
  const a = DT.tier(rec), b = DT.tier(rec);
  let good = a.tier === want && JSON.stringify(a) === JSON.stringify(b) && JSON.stringify(rec) === snap;
  if (stamp !== undefined) good = good && a.stamp === stamp;
  if (want !== 'great') good = good && a.stamp === null;
  if (why) good = good && a.why.indexOf(why) >= 0;
  if (!good) { ruleBad++; console.log('    ' + desc + ': got ' + JSON.stringify(a) + ', want ' + want + (stamp ? ' ' + stamp : '')); }
  checks++;
}
fails += ruleBad;
console.log('  ' + (CASES.length - ruleBad) + ' / ' + CASES.length + ' rule cases');
// every id of the sting registry has a tier rule and a great stamp
for (const id of DT.IDS) ok(id === 'month' || !!DT.STAMPS[id === 'search' ? 'search' : id], 'stamp for ' + id);
ok(Object.values(DT.STAMPS).every(s => s.length === 2), 'every seal has two characters');
ok(DT.trainRank(39) === '新募' && DT.trainRank(40) === '操练' && DT.trainRank(80) === '精锐' && DT.trainRank(100) === '百战', 'training ranks 40 / 60 / 80 / 100');
ok(JSON.stringify(DT.nextRank(72)) === JSON.stringify({ name: '精锐', at: 80, need: 8 }) && DT.nextRank(100) === null, 'nextRank');
// the prototype agrees on every v3-shaped case (tier only; the prototype has no tier-null and other field names)
{
  const proto = require(path.join(__dirname, '..', 'design-v4', 'domestic', 'tiers.js'));
  let agree = 0, n = 0;
  for (const [desc, rec, want] of CASES) {
    if (want === null || rec.stBefore !== undefined || (rec.cmd === 'market' && rec.side === 'sell') || ['harvest', 'build', 'caravan', 'explore', 'gift', 'marriage', 'tribute', 'hostage', 'truce', 'tradepact', 'submit', 'mission', 'festival', 'wenhui', 'stageUp', 'month'].indexOf(rec.cmd) >= 0) continue;
    if (rec.cmd === 'appoint' && rec.prevPol === 0) continue;    // documented: no previous governor → no "+15 over the previous" bonus
    const r = Object.assign({}, rec);
    if (r.cmd === 'develop') { const T = [300, 450, 600, 800], st = v => T.filter(x => v >= x).length; r.stBefore = st(r.before); r.stAfter = st(r.after); }
    if (r.cmd === 'persuade') r.city = !!r.surrendered;
    n++;
    const p = proto.tier(r).tier;
    // the prototype's develop "good" uses the multiplied gain; ours uses g0 (INTEGRATION §4.7) — skip that one case
    if (p === DT.tier(rec).tier || desc.indexOf('only through modifiers') >= 0) agree++;
    else console.log('    prototype differs on ' + desc + ': ' + p + ' vs ' + DT.tier(rec).tier);
  }
  ok(agree === n, 'the spec prototype agrees on ' + agree + ' / ' + n + ' shared cases');
}

// ============================================================ helpers: a scripted player on the v4 rules ==
const TIERS = ['miss', 'normal', 'good', 'great'];
function hashState(g) {
  // the result tier strings live only in records; drop them so a run without domestic-tiers.js compares equal
  const t = JSON.stringify(g, (k, v) => (k === 'tier' && TIERS.indexOf(v) >= 0 ? undefined : v));
  return crypto.createHash('md5').update(t).digest('hex') + ':' + t.length;
}

// design-v4/domestic/tiers.js's scripted domestic player, on the real v4 commands (records read from Commands.lastResult)
function simulate(S, sc, key, seed, months, tally, opts) {
  opts = opts || {};
  const M = S.M, B = S.Balance, C = S.Commands, K = S.DevKind;
  S.Random.seed(seed);
  const g = S.G = S.GameState.newGame(key, sc);
  const P = g.player;
  const pick = S.SeededRandom(seed * 7919 + key.length);
  const rnd = () => pick.nextDouble();
  const recs = [];
  const off = C.onCommand((cmd, res) => recs.push(res));
  const rec = (id, res) => {
    const o = S.DomesticTiers ? S.DomesticTiers.tier(res) : { tier: res.tier };
    if (opts.check && res.tier !== undefined && o.tier !== res.tier && res.cmd !== 'move') opts.check(id, res, o);
    if (!tally || !o.tier) return;
    const k = tally[id] = tally[id] || { miss: 0, normal: 0, good: 0, great: 0, n: 0 };
    k[o.tier]++; k.n++;
  };
  const last = () => C.lastResult;
  for (let m = 0; m < months; m++) {
    if (!g.playerFaction.alive) break;
    let guard = 0;
    while (g.tokens > 0 && guard++ < 20) {
      const mine = g.citiesOf(P);
      if (!mine.length) break;
      const c = mine[Math.floor(rnd() * mine.length)];
      const offs = g.officersIn(c);
      const avail = offs.filter(x => !x.moved);
      if (!offs.length) { g.tokens--; continue; }
      const r = rnd();
      const best = (arr, f) => arr.slice().sort((a, b) => f(b) - f(a))[0];
      if (r < 0.40 && c.gold >= B.DevelopCost) {
        const kinds = [[K.Land, 'land'], [K.Industry, 'industry'], [K.Town, 'town']];
        const kk = rnd() < 0.6 ? kinds.slice().sort((a, b) => c[a[1]] - c[b[1]])[0] : kinds[Math.floor(rnd() * 3)];
        C.develop(c, best(offs, x => x.pol), kk[0]);
        rec('develop.' + kk[1], last());
        g.tokens--; continue;
      }
      if (r < 0.55) {
        const tr = offs.filter(x => x.troops > 0 && x.training < 100);
        if (tr.length) { C.train(tr[Math.floor(rnd() * tr.length)]); rec('train', last()); g.tokens--; continue; }
      }
      if (r < 0.66) {
        const rc = offs.filter(x => x.troops < S.maxTroops(x) * 0.7);
        if (rc.length && c.gold >= 200) {
          const who = rc[0], max = C.recruitMax(c, who);
          const n = rnd() < 0.5 ? max : Math.max(100, Math.floor(max * 0.5 / 100) * 100);
          if (n > 0) { C.recruit(c, who, n); rec('recruit', last()); g.tokens--; continue; }
        }
      }
      if (r < 0.78 && avail.length) {
        const who = best(avail, x => x.intel);
        C.search(c, who);
        rec('search', last());
        g.tokens--;
        const f = C.searchFound;
        if (f && g.tokens > 0) { C.hire(f, who, P, c.id); rec('hire', last()); g.tokens--; }
        continue;
      }
      const free = g.freeFoundIn(c.id);
      if (free.length) { C.hire(free[0], best(offs, x => x.intel), P, c.id); rec('hire', last()); g.tokens--; continue; }
      if (r < 0.84) {
        const low = offs.filter(x => !g.isRuler(x) && x.loyalty < (rnd() < 0.5 ? 85 : 100));
        if (low.length && c.gold >= B.RewardGold) { C.reward(c, low[0]); rec('reward', last()); continue; }
      }
      if (r < 0.88) {
        const price = C.foodPrice();
        if (price <= 8 && c.gold > 400) { C.buyFood(c, Math.floor(c.gold * (0.3 + rnd() * 0.5))); rec('trade', last()); continue; }
        if (price >= 16 && c.food > 12000) { C.sellFood(c, Math.floor(c.food * 0.3)); rec('trade', last()); continue; }
        if (rnd() < 0.5 && c.gold > 300) { C.buyFood(c, 150 + Math.floor(rnd() * 300)); rec('trade', last()); continue; }
      }
      if (r < 0.92) {
        const d = rnd();
        if (d < 0.35) {
          const fs2 = g.factions.filter(f => f.alive && f.id !== P && !g.allied(f.id, P));
          if (fs2.length && c.gold >= 200) { const ff = fs2[Math.floor(rnd() * fs2.length)]; C.ally(c, P, ff.id, Math.min(c.gold, 200)); rec('ally', last()); g.tokens--; continue; }
        } else if (avail.length) {
          const agent = best(avail, x => x.intel);
          const targets = [];
          for (const i of c.links) { const x = g.cities[i]; if (x.owner >= 0 && x.owner !== P) for (const o of g.officersIn(x)) if (!g.isRuler(o)) targets.push(o); }
          if (targets.length) {
            const tg = targets[Math.floor(rnd() * targets.length)];
            agent.moved = true;
            if (d < 0.7) { C.discord(agent, tg); rec('discord', last()); } else { C.persuade(agent, tg, P); rec('persuade', last()); }
            g.tokens--; continue;
          }
        }
      }
      if (r < 0.96 && mine.length > 1) {
        const routes = C.moveTargets(c);
        if (routes.length && avail.length) {
          const rt = routes[Math.floor(rnd() * routes.length)];
          if (rnd() < 0.5) {
            const gens = avail.filter(x => !g.isRuler(x)).slice(0, 1 + Math.floor(rnd() * 3));
            if (gens.length) {
              const troops = gens.reduce((a, x) => a + x.troops, 0);
              let frontline = false;
              for (const x of gens) { C.move(x, rt.city); frontline = !!last().frontline; }
              // the vignette coalesces one 移动 command into one record (domestic-fx.js)
              rec('move', { cmd: 'move', gens: gens.length, troops, hops: rt.hops, frontline });
              g.tokens--; continue;
            }
          } else if (c.gold > 200 || c.food > 3000) {
            C.transport(c, rt.city, Math.floor(c.gold * 0.4), Math.floor(c.food * 0.4));
            rec('transport', last());
            g.tokens--; continue;
          }
        }
      }
      if (r < 0.98 && !offs.some(x => g.isRuler(x))) {
        const prev = c.governor >= 0 ? g.generals[c.governor] : null;
        const who = best(offs, x => x.pol);
        if (prev && who.id !== prev.id) { C.appoint(c, who); rec('appoint', last()); continue; }
      }
      const army = avail.filter(x => x.troops > 0).slice(0, 5);
      if (army.length) rec('attack', { cmd: 'attack', gens: army.length, troops: army.reduce((a, x) => a + x.troops, 0), ruler: army.some(x => g.isRuler(x)) });
      g.tokens--;
    }
    const news = [];
    const pb = S.StrategyAI.runAI(news);
    for (const s of pb) { S.Conquest.autoResolve(s); S.Conquest.apply(s); if (s.captives.length) S.Conquest.aiDecideCaptives(s, s.attackerWon ? s.attacker : s.defender); }
    const out = S.StrategyAI.endMonth();
    for (const h of out.harvests || []) {
      if (g.cities[h.city].owner !== P) continue;
      rec('harvest', Object.assign({ cmd: 'harvest' }, h));
    }
    if (opts.month) opts.month(m, g, out);
  }
  off();
  return { g, recs };
}

// ============================================================ 2. no RNG ==
console.log('== 2. no SG.Random draws (state hash with and without domestic-tiers.js)');
function campaign(withTiers) {
  const S = withTiers ? load({ v4: true }) : load({ v4: true, skip: ['domestic-tiers.js'] });
  let draws = 0, inTier = 0, tierDraws = 0;
  for (const k of ['value', 'rangeInt', 'rangeFloat']) {
    const f = S.Random[k];
    S.Random[k] = function () { draws++; if (inTier) tierDraws++; return f.apply(this, arguments); };
  }
  if (S.DomesticTiers) {
    const t0 = S.DomesticTiers.tier;
    S.DomesticTiers.tier = function (r) { inTier++; try { return t0(r); } finally { inTier--; } };
  }
  const r = simulate(S, 'classic', 'cao', 7, 60, null);
  return { hash: hashState(r.g), draws, tierDraws, recs: r.recs.length, has: !!S.DomesticTiers, S, recsList: r.recs };
}
const A = campaign(true), Bn = campaign(false);
ok(A.has && !Bn.has, 'one run with SG.DomesticTiers, one without');
ok(A.recs > 300 && A.recs === Bn.recs, 'same number of command records (' + A.recs + ' / ' + Bn.recs + ')');
ok(A.draws === Bn.draws, 'same number of SG.Random draws (' + A.draws + ' / ' + Bn.draws + ')');
ok(A.tierDraws === 0, 'tier() never draws (' + A.tierDraws + ' draws inside tier calls)');
ok(A.hash === Bn.hash, 'classic 5-year campaign state hash is identical (' + A.hash + ' vs ' + Bn.hash + ')');

// ============================================================ 3. lastResult schema ==
console.log('== 3. Commands.lastResult records (§3.2, INTEGRATION §4.7)');
{
  const S = A.S;
  const isNum = v => typeof v === 'number' && isFinite(v);
  const isObj = v => !!v && typeof v === 'object';
  const SCHEMA = {
    develop: { kind: 'string', roll: 'number', before: 'number', after: 'number', gain: 'number', cost: 'number', g0: 'number', pct: 'number', mods: 'object' },
    recruit: { n: 'number', cost: 'number', before: 'number', after: 'number', max: 'number', cityBefore: 'number', cityAfter: 'number', trainingBefore: 'number', trainingAfter: 'number', g0: 'number', pct: 'number', mods: 'object' },
    train: { roll: 'number', before: 'number', after: 'number', troops: 'number', max: 'number', g0: 'number', mods: 'object' },
    search: { outcome: 'string', value: 'number', p: 'number', mods: 'object' },
    hire: { ok: 'boolean', p: 'number', target: 'number', via: 'string', mods: 'object' },
    move: { gens: 'number', ids: 'object', troops: 'number', hops: 'number', to: 'number', frontline: 'boolean', ruler: 'boolean' },
    transport: { to: 'number', gold: 'number', food: 'number', destFoodBefore: 'number', destNeed: 'number' },
    ally: { ok: 'boolean', p: 'number', gift: 'number', target: 'number', targetCities: 'number', ourCities: 'number', mods: 'object' },
    discord: { ok: 'boolean', target: 'number', d: 'number', loyaltyAfter: 'number', p: 'number' },
    persuade: { ok: 'boolean', target: 'number', surrendered: 'boolean', p: 'number', mods: 'object' },
    reward: { before: 'number', after: 'number' },
    trade: { side: 'string', price: 'number', gold: 'number', food: 'number', goldOut: 'number' },
    appoint: { pol: 'number', prevPol: 'number' },
    attack: { gens: 'number', troops: 'number', ruler: 'boolean', to: 'number', food: 'number' },
  };
  const seenCmd = {};
  let schemaBad = 0, tierBad = 0, econBad = 0;
  // plus two multi-city rulers (移动 / 输送 / 任命 need a second city or a city without the ruler)
  const more = [];
  for (const key of ['yuanshao', 'liubiao']) more.push(...simulate(S, 'classic', key, 19, 24, null).recs);
  {
    // 任命 is rare in the scripted player: call it directly once (the model's own record)
    S.Random.seed(5);
    const g = S.G = S.GameState.newGame('yuanshao', 'classic');
    const c = g.citiesOf(g.player).find(x => g.officersIn(x).length >= 2) || g.citiesOf(g.player)[0];
    S.Commands.appoint(c, g.officersIn(c)[g.officersIn(c).length - 1]);
    more.push(S.Commands.lastResult);
  }
  for (const res of A.recsList.concat(more)) {
    if (!res || !res.cmd || typeof res.month !== 'number' || !('city' in res) || !('gen' in res) || !('f' in res)) { schemaBad++; continue; }
    const sc = SCHEMA[res.cmd];
    if (sc && !res.reason) {
      seenCmd[res.cmd] = (seenCmd[res.cmd] || 0) + 1;
      for (const [k, t] of Object.entries(sc)) {
        const v = res[k];
        const good = t === 'number' ? isNum(v) : t === 'object' ? isObj(v) : typeof v === t;
        if (!good) { schemaBad++; if (schemaBad < 6) console.log('    ' + res.cmd + '.' + k + ' = ' + JSON.stringify(v)); }
      }
    }
    const t = S.DomesticTiers.tier(res).tier;
    // ENGAGEMENT 的记录自己带分级（res.tier 预先填好，model.js 不再算）：只比 model.js / ITEMS / 出征的记录
    if (SCHEMA[res.cmd] && res.tier !== t) { tierBad++; if (tierBad < 4) console.log('    res.tier ' + res.tier + ' vs tier() ' + t + ' for ' + JSON.stringify(res).slice(0, 160)); }
    if (res.cmd === 'develop' && !res.reason && S.Econ && S.Econ.tierDevelop(res) !== t) econBad++;
  }
  const want = ['develop', 'recruit', 'train', 'search', 'hire', 'transport', 'ally', 'discord', 'persuade', 'reward', 'trade', 'appoint', 'move'];
  for (const c of want) ok(seenCmd[c] > 0, 'the scripted campaign produced ' + c + ' records (' + (seenCmd[c] || 0) + ')');
  ok(schemaBad === 0, 'every record carries its §3.2 fields (' + schemaBad + ' missing / mistyped)');
  ok(tierBad === 0, 'model.js res.tier = SG.DomesticTiers.tier(res).tier on every record (' + tierBad + ' differ)');
  ok(econBad === 0, '开发 tiers equal SG.Econ.tierDevelop (' + econBad + ' differ)');
  // the 出征 record (Conquest.prepare → Commands.record) and the harvest record (endMonth → news.harvests)
  S.Random.seed(11);
  const g = S.G = S.GameState.newGame('cao', 'classic');
  const src = g.cities[g.ruler(g.player).city];
  const tgt = src.links.map(i => g.cities[i]).find(x => x.owner !== g.player);
  const squad = g.officersIn(src).filter(x => x.troops > 0).slice(0, 2);
  if (tgt && squad.length) {
    S.Conquest.prepare(g.player, src, tgt, squad, 500, 0);
    const r = S.Commands.lastResult;
    ok(r.cmd === 'attack' && isNum(r.troops) && isNum(r.gens) && typeof r.ruler === 'boolean' && r.to === tgt.id && r.tier === S.DomesticTiers.tier(r).tier, 'attack record from Conquest.prepare');
  } else ok(false, 'no attack target found for the attack record');
  let hv = null;
  for (let m = 0; m < 12 && !hv; m++) { S.StrategyAI.runAI([]); const out = S.StrategyAI.endMonth(); if (out.harvests && out.harvests.length) hv = out.harvests; }
  ok(!!hv && hv.every(h => isNum(h.rollRaw) && h.rollRaw >= 0.85 && h.rollRaw <= 1.15 && isNum(h.land) && isNum(h.harvest)), 'harvest records carry rollRaw (news.harvests)');
  if (hv) ok(hv.every(h => S.DomesticTiers.tier(Object.assign({ cmd: 'harvest' }, h)).tier === h.tier), 'harvest record tier equals tier() (Econ.tierHarvest)');
}

// ============================================================ 4. distribution ==
console.log(`== 4. distribution: ${SEEDS} seeds × 18 rulers × ${MONTHS} months on the v4 rule layer`);
{
  const S = load({ v4: true });
  if (S.Progress) S.Progress.playerAutoLearn = true;
  const tally = {};
  let mismatch = 0;
  const runs = { classic: ['cao', 'liubei', 'sunjian', 'yuanshao', 'liubiao', 'mateng', 'gongsun', 'kongrong'] };
  const W = S.Scenarios.world.Factions.map(l => l.split('|')[0]);
  runs.world = [W[0], W[3], W[20], W[30], W[40], W[45], W[50], W[55], W[60], W[66]].filter(Boolean);
  const t0 = Date.now();
  for (const sc of Object.keys(runs)) for (const key of runs[sc]) for (let s = 1; s <= SEEDS; s++) {
    try { simulate(S, sc, key, s * 101 + key.length, MONTHS, tally, { check: () => { mismatch++; } }); }
    catch (e) { ok(false, sc + ' ' + key + ' seed ' + s + ' threw: ' + (e.stack || e).toString().split('\n').slice(0, 3).join(' | ')); }
  }
  console.log('  (' + ((Date.now() - t0) / 1000).toFixed(1) + ' s)');
  const pct = (a, n) => (100 * a / Math.max(1, n)).toFixed(1).padStart(5);
  console.log('  command            n      miss  normal   good  great');
  for (const k of Object.keys(tally).sort()) {
    const o = tally[k];
    console.log('  ' + k.padEnd(18) + String(o.n).padStart(6) + '  ' + pct(o.miss, o.n) + '   ' + pct(o.normal, o.n) + '  ' + pct(o.good, o.n) + '  ' + pct(o.great, o.n));
    const great = o.great / o.n, good = o.good / o.n;
    if (o.n >= 200) {
      if (k !== 'harvest') ok(great <= 0.30, k + ' great ≤ 30 % (' + (100 * great).toFixed(1) + ' %)');
      if (['develop.land', 'develop.industry', 'develop.town', 'train'].indexOf(k) >= 0) {
        ok(great >= 0.05 && great <= 0.16, k + ' great 5–16 % (' + (100 * great).toFixed(1) + ' %)');
        ok(good >= 0.18 && good <= 0.40, k + ' good 18–40 % (' + (100 * good).toFixed(1) + ' %)');
      }
      if (k === 'harvest') {
        const lean = o.miss / o.n;
        ok(lean >= 0.10 && lean <= 0.24, 'harvest lean 10–24 % (' + (100 * lean).toFixed(1) + ' %)');
        ok(good >= 0.18 && good <= 0.40, 'harvest good 18–40 % (' + (100 * good).toFixed(1) + ' %)');
        ok(great <= 0.17, 'harvest great ≤ 17 % (' + (100 * great).toFixed(1) + ' %)');
      }
    }
  }
  for (const k of ['develop.land', 'develop.industry', 'develop.town', 'train']) ok(tally[k] && tally[k].n >= (QUICK ? 100 : 200), k + ' has enough samples (' + (tally[k] ? tally[k].n : 0) + ')');
  ok(mismatch === 0, 'res.tier written by model.js equals tier() in the whole distribution run (' + mismatch + ' differ)');
}

console.log(`domestic-tiers: ${checks - fails} / ${checks} checks passed`);
console.log(fails ? 'FAIL' : 'ALL OK');
process.exitCode = fails;
