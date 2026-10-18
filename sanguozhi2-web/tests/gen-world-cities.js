'use strict';
/* ==========================================================================
   由游戏的世界剧本（js/world-data.js 的 SG.Scenarios.world）生成 tests/world-cities.js
   （file:// 页面不能 fetch，故生成静态脚本，供 tests/world-map.html 使用）
   用法：node tests/gen-world-cities.js
   输出：window.WORLD_CITIES = { regions: [{ region, factions: [{key, name, color}], cities: [...], links: [[a, b, kind]],
                                             crossLinks: [] }] }
   只取地图测试需要的字段（键、名、经纬度、开局归属、文化、町）。中原（经典 38 城）由页面直接取 SG.ScenarioData。
   第三版修订：原先读 research/*.json（第二版的研究稿），缺第三版新增的城池（华氏城等）；改为读游戏实际载入的数据，
   开局归属按 GameState.newGame(…, 'world') 推出（与游戏相同）。
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

global.window = global;
const root = path.join(__dirname, '..', 'js');
const load = f => vm.runInThisContext(fs.readFileSync(path.join(root, f), 'utf8'), { filename: f });
for (const f of ['core.js', 'world-geo.js', 'data.js', 'world-data.js']) load(f);
const SG = global.SG;
const CLASSIC = SG.ScenarioData;
const W = SG.Scenarios.world;
SG.ScenarioData = W;
for (const f of ['model.js', 'strategy-ai.js']) load(f);

SG.Random.seed(1);
const g = SG.G = SG.GameState.newGame(W.Factions[0].split('|')[0], 'world');
const facKey = W.Factions.map(l => l.split('|')[0]);
const classicCities = new Set(CLASSIC.Cities.map(l => l.split('|')[0]));
const sea = new Set(W.SeaLinks || []);
const cityRow = new Map(W.Cities.map(l => { const p = l.split('|'); return [p[0], p]; }));
const regionOfFaction = {};
for (const r of W.Regions) for (const k of r.factions) regionOfFaction[k] = r.id;

const out = { generated: new Date().toISOString().slice(0, 10), source: 'js/world-data.js', regions: [] };
const byRegion = new Map();
const regionOf = c => {
  // 城池按开局归属的势力分地域；空城按最近的有主邻城
  if (c.owner >= 0) return regionOfFaction[facKey[c.owner]] || 'other';
  for (const j of c.links) { const o = g.cities[j]; if (o.owner >= 0) return regionOfFaction[facKey[o.owner]] || 'other'; }
  return 'other';
};
for (const r of W.Regions) if (r.id !== 'zhongyuan') byRegion.set(r.id, { region: r.id, name: r.name, factions: [], cities: [], links: [], crossLinks: [] });
byRegion.set('other', { region: 'other', name: '其他', factions: [], cities: [], links: [], crossLinks: [] });
for (const l of W.Factions) {
  const p = l.split('|');
  const reg = byRegion.get(regionOfFaction[p[0]]);
  if (reg) reg.factions.push({ key: p[0], name: p[1], color: p[3] });
}
const keyOf = id => g.cities[id].key;
for (const c of g.cities) {
  if (classicCities.has(c.key)) continue;
  const p = cityRow.get(c.key);
  const reg = byRegion.get(regionOf(c)) || byRegion.get('other');
  reg.cities.push({ key: c.key, name: c.name, lon: +p[2], lat: +p[3], owner: c.owner >= 0 ? facKey[c.owner] : '', culture: p[10] || 'han', town: +p[6] || 300 });
}
const regionKey = new Map();
for (const r of byRegion.values()) for (const c of r.cities) regionKey.set(c.key, r.region);
for (const l of W.Links) {
  const [a, b] = l.split('-');
  if (classicCities.has(a) && classicCities.has(b)) continue;
  const kind = sea.has(l) || sea.has(b + '-' + a) ? 'sea' : 'land';
  const ra = regionKey.get(a), rb = regionKey.get(b);
  if (ra && (ra === rb || !rb)) byRegion.get(ra).links.push([a, b, kind]);
  else if (rb && !ra) byRegion.get(rb).links.push([b, a, kind]);
  else byRegion.get(ra).crossLinks.push([a, b, kind]);
}
for (const r of byRegion.values()) if (r.cities.length) out.regions.push(r);
void keyOf;

const js = '// 自动生成：node tests/gen-world-cities.js（来源 js/world-data.js 的世界剧本）。请勿手改。\n' +
  'window.WORLD_CITIES = ' + JSON.stringify(out) + ';\n';
fs.writeFileSync(path.join(__dirname, 'world-cities.js'), js);
console.log('tests/world-cities.js: ' + out.regions.map(r => r.region + ' ' + r.cities.length).join(', ') +
  '; total ' + out.regions.reduce((s, r) => s + r.cities.length, 0) + ' (+ classic ' + classicCities.size + ')');
