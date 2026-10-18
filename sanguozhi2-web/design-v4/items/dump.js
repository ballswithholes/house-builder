global.window = global;
const fs = require('fs'), vm = require('vm');
const base = '/home/user/house-builder/sanguozhi2-web/js/';
for (const f of ['core.js','data.js','world-data.js']) vm.runInThisContext(fs.readFileSync(base+f,'utf8'), {filename:f});
const SG = global.SG;
const out = {};
for (const sid of ['classic','world']) {
  const S = SG.Scenarios[sid];
  const cities = S.Cities.map(l => { const p = l.split('|'); return { key:p[0], name:p[1], lon:+p[2], lat:+p[3], land:+p[4], ind:+p[5], town:+p[6], pop:+p[7], gold:+p[8], food:+p[9], culture: p[10] || SG.WorldData.cultureOfCity(p[0]) }; });
  const factions = S.Factions.map(l => { const p = l.split('|'); return { key:p[0], name:p[1], ruler:p[2], color:p[3], virtue:+p[4], fame:+p[5], culture: p[6] || SG.WorldData.cultureOfFaction(p[0]), region: p[7] || SG.WorldData.regionOfFaction(p[0]) }; });
  const gens = S.Generals.map(l => { const p = l.split('|'); return { name:p[0], war:+p[1], intel:+p[2], pol:+p[3], faction:p[4], city:p[5], hidden:p[8]==='1', culture: p[9] || SG.WorldData.cultureOfGeneral(p[0]) }; });
  out[sid] = { cities, factions, gens, regions: S.Regions || null };
}
fs.writeFileSync(process.argv[2], JSON.stringify(out));
console.log('classic', out.classic.cities.length, out.classic.factions.length, out.classic.gens.length);
console.log('world', out.world.cities.length, out.world.factions.length, out.world.gens.length);
