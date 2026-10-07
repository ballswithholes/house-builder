'use strict';
/* ==========================================================================
   由 research/*.json 生成 tests/world-cities.js（file:// 页面不能 fetch，故生成静态脚本）
   用法：node tests/gen-world-cities.js
   输出：window.WORLD_CITIES = { regions: [{ region, factions: [{key, name, color}], cities: [...], links: [[a, b, kind]],
                                             crossLinks: [[from, toKey, kind]] }] }
   只取地图测试需要的字段（键、名、经纬度、归属、文化、町）。
   ========================================================================== */
const fs = require('fs');
const path = require('path');

const dir = path.join(__dirname, '..', 'research');
const files = fs.readdirSync(dir).filter(f => f.endsWith('.json')).sort();
const out = { generated: new Date().toISOString().slice(0, 10), regions: [] };
for (const f of files) {
  let j;
  try { j = JSON.parse(fs.readFileSync(path.join(dir, f), 'utf8')); } catch (e) { console.warn('skip ' + f + ': ' + e.message); continue; }
  const cities = (j.cities || []).filter(c => typeof c.lon === 'number' && typeof c.lat === 'number').map(c => ({
    key: c.key, name: c.name, lon: c.lon, lat: c.lat, owner: c.owner || '', culture: c.culture || '', town: c.town || 300,
  }));
  out.regions.push({
    region: j.region || f.replace('.json', ''),
    file: f,
    factions: (j.factions || []).map(x => ({ key: x.key, name: x.name, color: x.color })),
    cities,
    links: (j.links || []).filter(l => Array.isArray(l) && l.length >= 2).map(l => [l[0], l[1], l[2] || 'land']),
    crossLinks: (j.crossLinks || []).map(l => [l.from, l.toKey, l.kind || 'land']),
  });
}
const js = '// 自动生成：node tests/gen-world-cities.js（来源 research/*.json）。请勿手改。\n' +
  'window.WORLD_CITIES = ' + JSON.stringify(out) + ';\n';
fs.writeFileSync(path.join(__dirname, 'world-cities.js'), js);
console.log('tests/world-cities.js: ' + out.regions.map(r => r.region + ' ' + r.cities.length).join(', '));
