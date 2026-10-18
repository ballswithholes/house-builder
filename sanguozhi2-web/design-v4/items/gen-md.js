'use strict';
// Generates the catalogue tables for ITEMS.md from items.json (so the doc and the data never disagree).
const fs = require('fs');
const d = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const scen = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const by = new Map(d.items.map(x => [x.id, x]));
const cname = c => d.cultures[c].name;
const cityName = {};
for (const sid of ['classic', 'world']) for (const c of scen[sid].cities) cityName[c.key] = c.name;
const facName = {};
for (const f of scen.world.factions) facName[f.key] = f.name;
const slotName = s => d.slots[s].name;
const typeName = t => d.types[t].name;
const rz = r => d.rarities[r].name;
const acqText = (a) => {
  if (!a) return '—';
  switch (a.kind) {
    case 'shop': return a.cities ? '商店：' + a.cities.map(k => cityName[k]).join('、') + (a.price ? `（${a.price} 金）` : '') : '商店';
    case 'ruler': return `斩杀${facName[a.faction] || a.faction}君主${a.holder}`;
    case 'signature': return `${a.holder}开局持有`;
    case 'hidden': return `搜索：${cityName[a.city]}${a.from ? '（' + a.from.replace('-', ' 年 ') + ' 月起）' : ''}`;
    case 'cityTreasure': return `镇城之宝：${cityName[a.city]}`;
    case 'tournament': return { champion: '武林大会冠军奖池', title3: '武林大会三冠', title5: '武林大会五冠', foreign: '武林大会「异域奇珍」' }[a.pool];
    case 'armoury': return `开局在${facName[a.faction]}府库`;
    default: return a.kind;
  }
};
const fmtStats = s => Object.entries(s).map(([k, v]) => {
  const sk = d.statKeys[k];
  if (Array.isArray(v)) return (sk ? sk.name : k) + ':' + v.join('/');
  if (!sk) return k + ' ' + v;
  const u = sk.unit === '%' ? '%' : sk.unit === 's' ? 's' : sk.unit === 'm' ? 'm' : '';
  return sk.name + (v > 0 ? ' +' : ' ') + v + u;
}).join('，');
const out = [];
const P = (s) => out.push(s);

// 1. legendary + epic
P('<!-- GENERATED:LE -->');
P('| 品级 | 名称 | 文化 | 部位 / 类型 | 获取（世界剧本） | 获取（经典剧本） | 能力 | 主要数值 |');
P('|---|---|---|---|---|---|---|---|');
for (const x of d.items.filter(x => x.rarity === 'legendary' || x.rarity === 'epic')) {
  const acts = x.ability.effects.filter(e => e.name).map(e => '「' + e.name + '」').join('');
  P(`| ${rz(x.rarity)} | **${x.name}**${x.orig ? '<br>' + x.orig : ''} | ${cname(x.culture)} | ${slotName(x.slot)}·${typeName(x.type)} | ${acqText(x.availability.world)} | ${acqText(x.availability.classic)} | 「${x.ability.name}」${acts ? ' 宝具' + acts : ''} | ${fmtStats(x.stats)} |`);
}
P('<!-- /GENERATED:LE -->');
P('');
// 2. ruler-uniques
P('<!-- GENERATED:RULERS -->');
P('| 地域 | 势力 | 君主 | 专属兵器 | 品级 | 类型 | 能力 | 典据类别 |');
P('|---|---|---|---|---|---|---|---|');
const regionOf = {};
for (const r of scen.world.regions) for (const f of r.factions) regionOf[f] = r.name;
for (const f of scen.world.factions) {
  const r = d.world.rulers[f.key];
  const x = by.get(r.item);
  P(`| ${regionOf[f.key]} | ${f.name} | ${f.ruler} | ${x.name} | ${rz(x.rarity)} | ${typeName(x.type)} | 「${x.ability.name}」${x.ability.desc.replace(/\|/g, '／')} | ${{ history: '史实', archaeology: '考古', myth: '神话', novel: '演义', liberty: '游戏取舍' }[x.source.kind]} |`);
}
P('<!-- /GENERATED:RULERS -->');
P('');
// 3. signature
P('<!-- GENERATED:SIGNATURE -->');
P('| 开局持有者 | 物品 | 品级 | 部位 | 剧本 | 能力 | 典据 |');
P('|---|---|---|---|---|---|---|');
const sigW = d.world.signature;
const sigC = new Set(d.classic.signature.map(s => s.item));
for (const s of sigW) {
  const x = by.get(s.item);
  P(`| ${s.holder} | ${x.name} | ${rz(x.rarity)} | ${slotName(x.slot)}·${typeName(x.type)} | ${sigC.has(x.id) ? '经典 + 世界' : '世界'} | 「${x.ability.name}」 | ${x.source.refs.slice(0, 2).join('；')} |`);
}
P('<!-- /GENERATED:SIGNATURE -->');
P('');
// 4. hidden / city / armoury
P('<!-- GENERATED:HIDDEN -->');
P('| 物品 | 品级 | 方式 | 地点 | 基础几率 / 次 | 剧本 | 线索 |');
P('|---|---|---|---|---|---|---|');
for (const h of d.world.hidden) {
  const x = by.get(h.item);
  P(`| ${x.name} | ${rz(x.rarity)} | 搜索 | ${cityName[h.city]} | ${Math.round(h.p * 100)}% + 智力×${h.intelK * 100}% | ${x.availability.classic && x.availability.classic.kind === 'hidden' ? '经典 + 世界' : '世界'} | ${h.clue || '—'} |`);
}
for (const h of d.world.cityTreasure) {
  const x = by.get(h.item);
  P(`| ${x.name} | ${rz(x.rarity)} | 镇城之宝 | ${cityName[h.city]} | 随城易主 | ${x.availability.classic && x.availability.classic.kind === 'cityTreasure' ? '经典 + 世界' : '世界'} | — |`);
}
for (const h of d.world.armoury) {
  const x = by.get(h.item);
  P(`| ${x.name} | ${rz(x.rarity)} | 开局府库 | ${facName[h.faction]} | — | ${d.classic.armoury.some(a => a.item === x.id) ? '经典 + 世界' : '世界'} | — |`);
}
P('<!-- /GENERATED:HIDDEN -->');
P('');
// 5. tournament
P('<!-- GENERATED:TOURNEY -->');
P('| 物品 | 品级 | 文化 | 部位 | 能力 | 典据 |');
P('|---|---|---|---|---|---|');
for (const id of d.world.tournament.champion.concat(d.world.tournament.title3, d.classic.tournament.title5)) {
  const x = by.get(id);
  P(`| ${x.name} | ${rz(x.rarity)} | ${cname(x.culture)} | ${slotName(x.slot)}·${typeName(x.type)} | 「${x.ability.name}」${x.ability.desc.replace(/\|/g, '／')} | ${x.source.refs.slice(0, 2).join('；')} |`);
}
P('<!-- /GENERATED:TOURNEY -->');
P('');
// 6. shop rares
P('<!-- GENERATED:SHOPRARE -->');
P('| 物品 | 品级 | 文化 | 类型 | 世界剧本商店 | 经典剧本商店 | 基础价 | 能力 |');
P('|---|---|---|---|---|---|---|---|');
for (const x of d.items.filter(x => x.price != null && (x.rarity === 'rare' || x.rarity === 'epic'))) {
  const w = x.availability.world, c = x.availability.classic;
  P(`| ${x.name} | ${rz(x.rarity)} | ${cname(x.culture)} | ${typeName(x.type)} | ${w && w.kind === 'shop' ? w.cities.map(k => cityName[k]).join('、') : '—'} | ${c && c.kind === 'shop' ? c.cities.map(k => cityName[k]).join('、') : '—'} | ${x.price} | 「${x.ability.name}」 |`);
}
P('<!-- /GENERATED:SHOPRARE -->');
P('');
// 7. commons per culture
P('<!-- GENERATED:CULTURE -->');
P('| 文化 | 普通 | 精良 | 稀有 | 史诗 / 传说 | 兵器 | 装备 | 世界剧本本文化城数 | 举例（普通 / 精良） |');
P('|---|---|---|---|---|---|---|---|---|');
for (const c of Object.keys(d.cultures)) {
  const xs = d.items.filter(x => x.culture === c);
  const n = r => xs.filter(x => x.rarity === r).length;
  const ex = xs.filter(x => x.rarity === 'common').slice(0, 3).map(x => x.name).concat(xs.filter(x => x.rarity === 'uncommon').slice(0, 2).map(x => x.name));
  const ncity = scen.world.cities.filter(k => k.culture === c).length;
  P(`| ${cname(c)}（${c}） | ${n('common')} | ${n('uncommon')} | ${n('rare')} | ${n('epic') + n('legendary')} | ${xs.filter(x => x.cat === 'weapon').length} | ${xs.filter(x => x.cat === 'gear').length} | ${ncity} | ${ex.join('、')} |`);
}
P('<!-- /GENERATED:CULTURE -->');
P('');
// 8. shop tiers summary
P('<!-- GENERATED:TIERS -->');
P('| 剧本 | 城数 | 一级 村市 | 二级 市集 | 三级 大市 | 四级 都会 | 有稀有品的城 | 每城本地普通品池（最少 / 平均） | 每城精良品池（平均） |');
P('|---|---|---|---|---|---|---|---|---|');
for (const sid of ['classic', 'world']) {
  const s = Object.values(d.shops[sid]);
  const t = k => s.filter(v => v.tier === k).length;
  const avg = a => (a.reduce((x, y) => x + y, 0) / a.length).toFixed(1);
  P(`| ${sid === 'classic' ? '经典' : '世界'} | ${s.length} | ${t(1)} | ${t(2)} | ${t(3)} | ${t(4)} | ${s.filter(v => v.rare.length || v.epic.length).length} | ${Math.min(...s.map(v => v.common.length))} / ${avg(s.map(v => v.common.length))} | ${avg(s.map(v => v.uncommon.length))} |`);
}
P('<!-- /GENERATED:TIERS -->');
fs.writeFileSync(process.argv[4], out.join('\n'));
console.log('ok', out.length, 'lines');
