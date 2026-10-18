// 人物模型库的参考值（UNITY-V3 §4）：在 Node 中运行网页版 three.min.js + figure.js（Canvas 用桩），导出
//   · 约 30 名武将 × 3 档 LOD、全部文化 × 兵种 × 2 档 LOD 的士兵、战马、全部兵器与盾的网格（逐顶点：位置、法线、UV、顶点色、
//     蒙皮骨与权重、索引）、三角形数、外观解析结果（头像外貌参数、衣甲层、兵器、颜色）
//   · 若干人物在 25 个动作片段 × 3 个时刻的骨骼世界矩阵，含 IK 关闭 / 片段混合 / 下半身片段 / grip / ikHand / 弹簧 update 等选项，
//     骑乘（马的 6 个片段带骑手）、士兵群烘焙的蒙皮矩阵
// 输出：<目录>/refs.json（清单）+ <目录>/refs.bin（二进制数组，按清单里的偏移读取）。C# 端 FigureHarness.cs 逐项比对。
// 用法：node Tests~/figure/refs.js <输出目录>
'use strict';
const fs = require('fs'), path = require('path');
const { SG, THREE, gen } = require('./load-web.js');
const OUT = process.argv[2] || path.resolve(__dirname, '../out/figure');
fs.mkdirSync(OUT, { recursive: true });
const F = SG.Figure, I = F._internal;

// ---- 二进制输出
const chunks = []; let off = 0;
function put(typed) {
  const b = Buffer.from(typed.buffer, typed.byteOffset, typed.byteLength);
  const pad = (8 - (off % 8)) % 8; if (pad) { chunks.push(Buffer.alloc(pad)); off += pad; }
  const o = off; chunks.push(b); off += b.length; return o;
}
function geo(g) {
  if (!g) return null;
  const a = g.attributes, bb = g.boundingBox || (g.computeBoundingBox(), g.boundingBox);
  return {
    verts: a.position.count, tris: g.index.count / 3,
    pos: put(Float32Array.from(a.position.array)), nrm: put(Float32Array.from(a.normal.array)), uv: put(Float32Array.from(a.uv.array)), col: put(Float32Array.from(a.color.array)),
    si: a.skinIndex ? put(Float32Array.from(a.skinIndex.array)) : -1, sw: a.skinWeight ? put(Float32Array.from(a.skinWeight.array)) : -1,
    idx: put(Uint32Array.from(g.index.array)),
    bmin: [bb.min.x, bb.min.y, bb.min.z], bmax: [bb.max.x, bb.max.y, bb.max.z],
  };
}
function canon(v) {
  if (v === null || v === undefined) return null;
  if (Array.isArray(v)) return v.map(canon);
  if (typeof v === 'object') { const o = {}; for (const k of Object.keys(v)) if (v[k] !== undefined && typeof v[k] !== 'function') o[k] = canon(v[k]); return o; }
  return v;
}
// figure.js 的 col()：'#rrggbb' → [r, g, b]（不经过 three 的色彩管理）
const hexArr = h => { h = String(h).replace('#', ''); if (h.length === 3) h = h.split('').map(x => x + x).join(''); const n = parseInt(h.slice(0, 6), 16) || 0; return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255, 0]; };
const colArr = c => (c == null ? null : Array.isArray(c) ? c.slice(0, 4) : (typeof c === 'string' ? hexArr(c) : c));
function lookSummary(look) {
  return canon({ kind: look.kind, name: look.name, culture: look.culture, sex: look.sex, age: look.age, height: look.height, build: look.build, fat: look.fat, skin: look.skin,
    weapon: look.weapon, shield: look.shield, backBow: look.backBow, quiver: look.quiver, ribbons: look.ribbons, face: look.face, seed: look.seed,
    hair: look.hair, beard: look.beard, mus: look.mus, hat: look.hat, L: look.L, spec: look.spec,
    C: Object.fromEntries(Object.entries(look.C).map(([k, v]) => [k, colArr(v)])) });
}
const mats = bones => { const a = new Float64Array(bones.length * 16); bones.forEach((b, i) => a.set(b.matrixWorld.elements, i * 16)); return put(a); };
const wi = w => (w ? canon({ id: w.id, name: w.name, style: w.style, len: w.len, tip: w.tip, base: w.base, off: w.off, string: w.string, nockTop: w.nockTop, nockBot: w.nockBot }) : null);

const M = { generals: [], soldiers: [], horses: [], weapons: [], shields: [], poses: [], mounted: [], bakes: [], atlas: null };
// 图集区域布局
M.atlas = Object.fromEntries(Object.entries(F.atlas().R).map(([k, r]) => [k, [r.px, r.py, r.pw, r.ph]]));

// ---- 武将 × 3 档 LOD
const NAMES = ['关羽', '张飞', '赵云', '吕布', '诸葛亮', '马超', '夏侯惇', '黄忠', '典韦', '许褚', '孙策', '曹操', '刘备', '祝融', '孟获', '卑弥呼',
  '塞维鲁', '康茂德', '沃洛吉斯', '胡维色迦', '范师蔓', '沙伊鲁姆', '阿根托科克斯', '巴洛马尔', '赞提库斯', '故国川王', '安国', '轲比能',
  '耶若室利', '迦梨迦罗', '室利那伽', '龙树', '提婆', '加达拉特', '耶斯博赫', '阿曼哈利卡', '卡纳尔塔', '鲁德拉布提', '童受'];
const t0 = Date.now();
for (const n of NAMES) for (const lod of ['high', 'mid', 'low']) {
  const g = gen(n);
  const opts = { lod, color: g.color };
  if (n === '塞维鲁' || n === '孙策' || n === '卡纳尔塔') opts.shield = true;     // 带盾（文化缺省）
  const f = F.general(g, opts);
  const t = f.tpl;
  M.generals.push({ name: n, gen: g, opts: canon(opts), lod, key: t.key, style: f.style, tris: f.triangles, drawCalls: f.drawCalls, squeeze: t.squeeze, faceSize: t.faceSize,
    weapon: wi(t.weapon), shieldInfo: t.shieldInfo || null, parts: t.parts, look: lookSummary(t.look),
    body: geo(t.geometry), head: geo(t.headGeo), wgeo: geo(t.weaponGeo), sgeo: geo(t.shieldGeo) });
  f.dispose();
}
const tG = Date.now() - t0;
// ---- 士兵：全部文化 × 兵种 × 2 档 LOD（seed 0），另有若干外观变体
const CULTS = F.cultures, TYPES = F.soldierTypes;
for (const c of CULTS) for (const ty of TYPES) for (const lod of ['low', 'mid']) for (const seed of (lod === 'low' && (c === 'han' || c === 'roman' || c === 'india') ? [0, 1, 2] : [0])) {
  const f = F.soldier({ type: ty, culture: c, lod, seed, color: '#2f7d4a' });
  const t = f.tpl;
  M.soldiers.push({ type: ty, culture: c, lod, seed, key: t.key, style: f.style, tris: f.triangles, squeeze: t.squeeze, weapon: wi(t.weapon), shieldInfo: t.shieldInfo || null, hasShield: !!t.hasShield,
    look: lookSummary(t.look), body: geo(t.geometry) });
  f.dispose();
}
// ---- 战马
const HORSES = [];
for (const lod of ['high', 'mid', 'low']) for (let seed = 0; seed < 6; seed++) HORSES.push({ lod, seed });
HORSES.push({ lod: 'mid', armor: 'scale', seed: 1 }, { lod: 'mid', armor: 'none', seed: 2 }, { lod: 'mid', general: true, name: '赤兔', seed: 0 }, { lod: 'high', general: true, seed: 3, metal: '#b8bcc4', plume: '#2040c0' },
  { lod: 'low', armor: 'scale', seed: 0 }, { lod: 'mid', coat: '#c8a070', seed: 4 }, { lod: 'mid', name: '的卢', chamfron: true, seed: 1 });
for (const o of HORSES) {
  const h = F.horse(Object.assign({ color: '#2c3d8f' }, o));
  M.horses.push({ opts: canon(o), key: h.tpl.key, tris: h.triangles, body: geo(h.tpl.geometry), J: Object.fromEntries(Object.entries(h.tpl.J)) });
  h.dispose();
}
// ---- 兵器与盾（独立网格）
for (const w of F.weapons) for (const lod of ['high', 'mid', 'low']) {
  const m = F.weapon(w.id, { lod });
  M.weapons.push({ id: w.id, lod, tris: m.userData.tris, info: canon({ id: m.userData.weapon.id, name: m.userData.weapon.name, style: m.userData.weapon.style, len: m.userData.weapon.len, tip: m.userData.weapon.tip.toArray(), base: m.userData.weapon.base.toArray(), off: m.userData.weapon.off ? m.userData.weapon.off.toArray() : null }), body: geo(m.geometry) });
  m.userData.dispose();
}
for (const s of ['scutum', 'round', 'oval', 'pai', 'wa', 'rattan', 'long', 'hide']) for (const lod of ['high', 'low']) {
  const m = F.shield(s, { lod });
  M.shields.push({ id: s, lod, tris: m.userData.tris, body: geo(m.geometry) });
}

// ---- 姿势：骨骼世界矩阵（root 在原点）
const CLIPS = ['idle', 'walk', 'run', 'dash', 'light1', 'light2', 'light3', 'heavy', 'charge', 'block', 'parry', 'hit', 'hitB', 'knockdown', 'down', 'getup', 'victory', 'defeat', 'cheer', 'die', 'guardbreak', 'jump', 'special', 'bowDraw', 'bowShoot', 'ride', 'gallop'];
const TIMES = [0.15, 0.5, 0.85];
function poseSeries(label, f, list) {
  for (const [clip, t, opts] of list) {
    f.setPose(clip, t, opts);
    f.root.updateMatrixWorld(true);
    M.poses.push({ fig: label, clip, t, opts: canon(opts || null), lift: f._lift || 0, mats: mats(f._bones) });
  }
}
const allClips = () => { const L = []; for (const c of CLIPS) { const d = F.clips[c].dur; for (const k of TIMES) L.push([c, d * k]); } return L; };
const POSE_FIGS = [['关羽', 'high'], ['张飞', 'high'], ['赵云', 'mid'], ['吕布', 'high'], ['诸葛亮', 'high'], ['马超', 'mid'], ['夏侯惇', 'high'], ['黄忠', 'high'], ['典韦', 'high'], ['刘备', 'high'],
  ['祝融', 'high'], ['塞维鲁', 'high'], ['沃洛吉斯', 'mid'], ['加达拉特', 'high'], ['龙树', 'high'], ['耶若室利', 'mid']];
for (const [n, lod] of POSE_FIGS) {
  const g = gen(n); const opts = { lod, color: g.color }; if (n === '塞维鲁') opts.shield = true;
  const f = F.general(g, opts);
  poseSeries(n + '@' + lod, f, allClips());
  f.dispose();
}
for (const [ty, c] of [['spear', 'han'], ['sword', 'roman'], ['archer', 'india'], ['cavalry', 'persia'], ['sword', 'africa'], ['spear', 'wa']]) {
  const f = F.soldier({ type: ty, culture: c, lod: 'low', seed: 0 });
  poseSeries('S:' + ty + '/' + c, f, allClips());
  f.dispose();
}
// 选项：IK 关闭、混合、下半身片段、贴地关闭；grip / ikHand；弹簧 update
{
  const f = F.general(gen('关羽'), { lod: 'high' });
  poseSeries('opts:关羽', f, [['heavy', 0.5, { ik: false }], ['light1', 0.15, { blend: { clip: 'walk', t: 0.3, w: 0.35 } }], ['idle', 0.4, { lower: 'run', lowerT: 0.2 }], ['knockdown', 0.9, { clamp: false }], ['victory', 1.2, { blend: { clip: 'hit', t: 0.07 } }]]);
  f.setPose('idle', 0.3); f.grip(new THREE.Vector3(-0.3, 1.3, 0.35), new THREE.Vector3(0.2, 0.9, 0.4), 15); f.root.updateMatrixWorld(true);
  M.poses.push({ fig: 'opts:关羽', clip: 'grip-dir', t: 0, opts: null, lift: f._lift || 0, mats: mats(f._bones) });
  f.grip(new THREE.Vector3(-0.2, 1.1, 0.4), new THREE.Quaternion().setFromEuler(new THREE.Euler(0.4, -0.3, 0.2, 'YXZ'))); f.root.updateMatrixWorld(true);
  M.poses.push({ fig: 'opts:关羽', clip: 'grip-quat', t: 0, opts: null, lift: f._lift || 0, mats: mats(f._bones) });
  f.ikHand('L', new THREE.Vector3(0.3, 1.25, 0.3)); f.root.updateMatrixWorld(true);
  M.poses.push({ fig: 'opts:关羽', clip: 'ikHand-L', t: 0, opts: null, lift: f._lift || 0, mats: mats(f._bones) });
  f.setPose('run', 0.1);
  for (let i = 0; i < 20; i++) { f.setPose('run', 0.1 + i / 60); f.update(1 / 60); }
  f.root.updateMatrixWorld(true);
  M.poses.push({ fig: 'opts:关羽', clip: 'update-run', t: 0, opts: null, lift: f._lift || 0, mats: mats(f._bones) });
  f.dispose();
}
// ---- 骑乘：马的 6 个片段（骑手随之摆姿）
for (const [n, hopt] of [['关羽', { seed: 1, general: true }], ['吕布', { name: '赤兔', general: true }], ['沃洛吉斯', { armor: 'scale', seed: 2 }]]) {
  const g = gen(n);
  const f = F.general(g, { lod: 'mid', color: g.color, mounted: true });
  const h = F.horse(Object.assign({ lod: 'mid', color: g.color }, hopt));
  h.mount(f);
  f.setPose('idle', 0);
  for (const hc of ['idle', 'walk', 'trot', 'gallop', 'rear', 'die']) for (const k of TIMES) {
    const t = F.horseClips[hc].dur * k;
    f.setPose(hc === 'gallop' ? 'light1' : 'idle', 0.12);
    h.setPose(hc, t);
    h.root.updateMatrixWorld(true);
    M.mounted.push({ fig: n, gen: g, horse: canon(hopt), clip: hc, t, riderClip: f._clipName, riderT: f._clipT, horseMats: mats(h._bones), riderMats: mats(f._bones) });
  }
  h.dispose(); f.dispose();
}
// ---- 士兵群烘焙：读实例化士兵群各组的烘焙贴图（半精度，three 坐标的 M·inv 前 3 行）与片段表
{
  const cr = F.crowd({ lod: 'low' });
  for (const o of [{ type: 'spear', culture: 'han', seed: 0 }, { type: 'archer', culture: 'roman', seed: 0 }, { type: 'cavalry', culture: 'persia', seed: 0, mounted: true }]) cr.add(o);
  for (const [gk, g] of cr.groups) M.bakes.push({ group: gk, W: g.bake.W, rows: g.bake.rows, table: g.bake.table, half: put(Uint16Array.from(g.bake.tex.image.data)) });
  cr.dispose();
}
fs.writeFileSync(path.join(OUT, 'refs.bin'), Buffer.concat(chunks));
fs.writeFileSync(path.join(OUT, 'refs.json'), JSON.stringify(M));
console.log(`参考值：武将 ${M.generals.length} 个模板（${tG} ms）、士兵 ${M.soldiers.length}、战马 ${M.horses.length}、兵器 ${M.weapons.length}、盾 ${M.shields.length}、姿势 ${M.poses.length}、骑乘 ${M.mounted.length}；${(off / 1048576).toFixed(1)} MB → ${OUT}`);
