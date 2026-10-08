#!/usr/bin/env node
'use strict';
/* 生成头像测试用例与网页版的参考输出（Node，无依赖）
     node Tests~/raster/cases.js [输出目录 = Tests~/out/raster]
   cases.json  全部比对用例（经典 162 人 × 不同尺寸 / 表情 / 镜像 / 边框，每种文化 8 个角色样例，
               有手工设定的世界武将，120 个随机生成的人）
   js.json     网页版 SG.Portrait.svg 对每个用例的输出（去掉 id 流水号）—— C# 逐条比对
   sheet.json  联系表用的 40 人（经典名将 + 每种文化一人 + 世界君主），128px
   载入顺序与游戏相同：core.js、data.js、world-data.js、portrait-data.js、portrait.js。 */
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const WEB = path.resolve(__dirname, '../../../sanguozhi2-web/js');
const OUT = path.resolve(process.argv[2] || path.join(__dirname, '../out/raster'));
fs.mkdirSync(OUT, { recursive: true });

const sb = { console };
sb.window = sb;
vm.createContext(sb);
for (const f of ['core.js', 'data.js', 'world-data.js', 'portrait-data.js', 'portrait.js']) vm.runInContext(fs.readFileSync(path.join(WEB, f), 'utf8'), sb, { filename: f });
const SG = sb.SG, P = SG.Portrait, SD = SG.ScenarioData;
const strip = s => s.replace(/q[0-9a-z]+(?=[a-z]*["#)])/g, '');

const fcol = {}; SD.Factions.forEach(l => { const p = l.split('|'); fcol[p[0]] = p[3]; });
const classic = {}; SD.Generals.forEach(l => { const p = l.split('|'); classic[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], color: fcol[p[4]] || '#8a8a92' }; });
const world = {}; const wfcol = {};
for (const l of SG.Scenarios.world.Factions) { const p = l.split('|'); wfcol[p[0]] = p[3]; }
for (const l of SG.Scenarios.world.Generals) { const p = l.split('|'); if (!classic[p[0]]) world[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], color: wfcol[p[4]] || '#8a8a92' }; }

// ---------------------------------------------------------------- 全部比对用例
const moods = ['neutral', 'angry', 'hurt', 'win'];
const cases = [];
let i = 0;
for (const l of SD.Generals) {
  const p = l.split('|');
  cases.push({ gen: { name: p[0], war: +p[1], intel: +p[2], pol: +p[3] }, opts: { size: [44, 96, 200][i % 3], scale: 1, color: fcol[p[4]] || '#8a8a92', mood: moods[i % 4], flip: i % 5 === 0, frame: i % 7 !== 3 } });
  i++;
}
const Y = SD.StartYear || 190;
const ROLES = [['武将', { war: 94, intel: 38, pol: 30, born: Y - 34 }], ['智将', { war: 76, intel: 80, pol: 55, born: Y - 40 }], ['谋士', { war: 28, intel: 93, pol: 70, born: Y - 36 }],
  ['君主', { war: 70, intel: 72, pol: 74, born: Y - 46, portrait: { role: 'ruler' } }], ['老者', { war: 45, intel: 70, pol: 82, born: Y - 70 }], ['少年', { war: 72, intel: 55, pol: 40, born: Y - 18 }],
  ['女性', { war: 55, intel: 70, pol: 60, born: Y - 24, sex: 'f' }], ['女将', { war: 82, intel: 40, pol: 30, born: Y - 28, sex: 'f' }]];
for (const c of P.cultures) for (const [role, base] of ROLES) {
  cases.push({ gen: Object.assign({ name: `〔样例〕${c}·${role}`, culture: c }, base), opts: { size: [64, 128, 200][i % 3], scale: 1, color: '#8a8a92', mood: moods[i % 4] } });
  i++;
}
for (const name of SG.PortraitData.worldHand || []) {
  const g = world[name] || classic[name] || { name, war: 60, intel: 60, pol: 60 };
  cases.push({ gen: { name, war: g.war, intel: g.intel, pol: g.pol }, opts: { size: [96, 160, 220][i % 3], scale: 1, color: g.color || '#5a6a8a', mood: moods[i % 4], flip: i % 3 === 0 } });
  i++;
}
for (let k = 0; k < 120; k++) {
  const c = P.cultures[k % P.cultures.length];
  const gen = { name: `〔随机〕${k}`, culture: c, war: 20 + (k * 37) % 80, intel: 20 + (k * 53) % 80, pol: 20 + (k * 29) % 80, sex: k % 6 === 0 ? 'f' : 'm' };
  if (k % 4) gen.born = 190 - 18 - (k * 7) % 60;
  cases.push({ gen, opts: { size: [48, 128, 256][k % 3], scale: 1, color: ['#c8382c', '#2c3d8f', '#3a7a3a', '#8a8a92'][k % 4], mood: moods[k % 4], flip: k % 2 === 1 } });
}
fs.writeFileSync(path.join(OUT, 'cases.json'), JSON.stringify(cases));
fs.writeFileSync(path.join(OUT, 'js.json'), JSON.stringify(cases.map(c => strip(P.svg(c.gen, c.opts)))));

// ---------------------------------------------------------------- 联系表 40 人
const famous = ['关羽', '张飞', '刘备', '诸葛亮', '曹操', '吕布', '赵云', '孙权', '黄忠', '夏侯惇', '典韦', '许褚', '马超', '周瑜', '董卓', '孙坚', '孙策', '貂蝉', '祝融', '孟获'];
const sheet = [];
let m = 0;
for (const n of famous) {
  const g = classic[n] || { name: n, war: 70, intel: 70, pol: 70, color: '#c8382c' };
  sheet.push({ label: n, gen: { name: n, war: g.war, intel: g.intel, pol: g.pol }, opts: { size: 128, scale: 1, color: g.color, mood: n === '张飞' ? 'angry' : n === '孙策' ? 'win' : n === '典韦' ? 'hurt' : 'neutral' } });
  m++;
}
// 每种文化一人（汉以外取该文化的生成样例，角色轮换）
const roleCycle = ['武将', '君主', '智将', '女性', '老者', '女将', '谋士', '少年'];
P.cultures.filter(c => c !== 'han').forEach((c, k) => {
  const [role, base] = ROLES.find(r => r[0] === roleCycle[k % roleCycle.length]);
  sheet.push({ label: `${c}·${role}`, gen: Object.assign({ name: `〔样例〕${c}·${role}`, culture: c }, base), opts: { size: 128, scale: 1, color: '#8a8a92' } });
});
// 世界君主
for (const n of ['康茂德', '塞维鲁', '卑弥呼', '沃洛吉斯', '胡维色迦', '故国川王']) {
  const g = world[n] || classic[n] || { name: n, war: 60, intel: 60, pol: 60, color: '#5a6a8a' };
  sheet.push({ label: n, gen: { name: n, war: g.war, intel: g.intel, pol: g.pol }, opts: { size: 128, scale: 1, color: g.color } });
}
fs.writeFileSync(path.join(OUT, 'sheet.json'), JSON.stringify(sheet));
// 小图（48px，细节档 0）与大图（256px，细节档 2）各 16 人：细线、虚线、甲片等细节的对照
const pick = [0, 1, 3, 4, 5, 6, 9, 13, 17, 18, 20, 24, 28, 30, 34, 37];
for (const px of [48, 256]) fs.writeFileSync(path.join(OUT, `sheet${px}.json`), JSON.stringify(pick.map((k, j) => Object.assign({}, sheet[k], { opts: Object.assign({}, sheet[k].opts, { size: px, flip: j % 5 === 2, mood: ['neutral', 'angry', 'hurt', 'win'][j % 4] }) }))));
console.log(`用例 ${cases.length} 个、联系表 ${sheet.length} 人 → ${OUT}`);
