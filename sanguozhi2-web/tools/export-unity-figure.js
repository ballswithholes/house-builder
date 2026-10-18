#!/usr/bin/env node
'use strict';
/* ==========================================================================
   导出人物 / 士兵 / 战马模型库（js/figure.js）的数据表到 Unity 版（UNITY-V3 §2）
     node tools/export-unity-figure.js [输出路径]
     node tools/export-unity-figure.js --check   只比较：figure.js 的数据改过却没重新导出时退出码 1
   在沙箱里载入 vendor/three.min.js、core.js、figure.js（Canvas 用桩：只读表格，不生成贴图），
   把 figure.js 闭包里的数据表原样写成
   ../sanguozhi2-unity/Assets/Sanguo/Scripts/Data/Generated/FigureData.g.cs：
     · 外观表（JSON，C# 端解析为 JObj，键按插入顺序）：OUTFIT、HAT_MAP、SIG、PWEAPON、CULT_WEAPON、CULT_SHIELD、
       SOLDIER、SOLDIER_WEAPON、WEAPONS、DUAL_BASE、LODP、TRI_BUDGET、SQUEEZE、HORSE_COATS、HORSE_NAMED、HCLIPS、
       MOUNT_CLIPS、CROWD_CLIPS、CULTURES、REGION_DEFS、GENERIC_FACES、STYLE_FALLBACK、片段目录（时长 / 循环 / 说明 / 是否步态）
     · 数值表：HPROF（头部截面）、PHI_Y（纬度 → 高度）、FEAT、HEAD_C、ARM_R、LEG_R、骨骼名与父骨
     · 动作关键帧：每个片段 × 每个武器流派（pole one dual fan none bow）经 compiled() 合并站姿后的完整关键帧
       （每帧 = t、缓动、17 个三元字段（缺省为 NaN）、8 个标量字段），C# 端按 samplePose 原样插值
   生成几何的算法（放样、头部曲面、衣甲、兵器、马……）在 C# 端手工翻译（View/FigureMesh.cs、FigureRig.cs、FigureAtlas.cs）。
   闭包里的表通过在 SG.Figure._internal 处注入一个只读出口取得（源码不改动）；表名改了会直接报错。
   无依赖，Node 14+。
   ========================================================================== */
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const WEB = path.resolve(__dirname, '..');
const CHECK = process.argv.includes('--check');
const OUT = process.argv.slice(2).find(a => a !== '--check') || path.resolve(WEB, '../sanguozhi2-unity/Assets/Sanguo/Scripts/Data/Generated/FigureData.g.cs');

// ---- 沙箱：three + core + figure（Canvas 桩：载入时不画贴图）
const noop = () => {};
const grad = { addColorStop: noop };
const ctx2d = cv => new Proxy({ canvas: cv, getImageData: (x, y, w, h) => ({ width: w, height: h, data: new Uint8ClampedArray(w * h * 4) }), createRadialGradient: () => grad, createLinearGradient: () => grad },
  { get(t, k) { return k in t ? t[k] : noop; }, set(t, k, v) { t[k] = v; return true; } });
const sandbox = { console: { log: noop, warn: noop, error: console.error }, Math, Date, performance: { now: () => Date.now() } };
sandbox.window = sandbox; sandbox.self = sandbox;
sandbox.document = { createElement: () => { const cv = { width: 1, height: 1, style: {} }; cv.getContext = () => ctx2d(cv); return cv; } };
vm.createContext(sandbox);
const TABLES = ['OUTFIT', 'HAT_MAP', 'SIG', 'PWEAPON', 'CULT_WEAPON', 'CULT_SHIELD', 'SOLDIER', 'SOLDIER_WEAPON', 'WEAPONS', 'DUAL_BASE', 'LODP', 'TRI_BUDGET', 'SQUEEZE',
  'HORSE_COATS', 'HORSE_NAMED', 'HCLIPS', 'MOUNT_CLIPS', 'CROWD_CLIPS', 'CULTURES', 'REGION_DEFS', 'GENERIC_FACES', 'STYLE_FALLBACK', 'STANCE',
  'HPROF', 'PHI_Y', 'FEAT', 'HEAD_C', 'ARM_R', 'LEG_R', 'BONES', 'PARENT', 'HBONES', 'HPARENT', 'FIELDS3', 'FIELDS1', 'SOLDIER_TYPES', 'ATLAS_N', 'CELL', 'SKIN_EXP', 'BAKE_FPS', 'SH_K', 'DL_K'];
let figSrc = fs.readFileSync(path.join(WEB, 'js', 'figure.js'), 'utf8');
const hook = '_internal: {';
if (!figSrc.includes(hook)) throw new Error('figure.js 里找不到 SG.Figure._internal');
figSrc = figSrc.replace(hook, '_internal: { __unity: { ' + TABLES.join(', ') + ' }, ');
for (const [f, src] of [['vendor/three.min.js', null], ['js/core.js', null], ['js/figure.js', figSrc]])
  vm.runInContext(src || fs.readFileSync(path.join(WEB, f), 'utf8'), sandbox, { filename: f });
const F = sandbox.SG.Figure, I = F._internal, T = I.__unity;

// 规范 JSON：键按插入顺序；数值用 JS 的格式（与 C# JObj 解析一致）；数组里的 undefined → null
function canon(v) {
  if (v === null || v === undefined) return 'null';
  if (typeof v === 'number') { if (!isFinite(v)) throw new Error('非有限数值'); return String(v); }
  if (typeof v === 'boolean') return v ? 'true' : 'false';
  if (typeof v === 'string') return JSON.stringify(v);
  if (Array.isArray(v)) return '[' + v.map(canon).join(',') + ']';
  if (typeof v === 'function') throw new Error('表里有函数');
  if (typeof v === 'object') {
    const parts = [];
    for (const k of Object.keys(v)) { if (v[k] === undefined) continue; parts.push(JSON.stringify(k) + ':' + canon(v[k])); }
    return '{' + parts.join(',') + '}';
  }
  throw new Error('无法导出的值：' + typeof v);
}
function fnv(h, s) { for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }
const cs = s => '"' + s.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n') + '"';
const num = v => { if (v === null || v === undefined || Number.isNaN(v)) return 'double.NaN'; if (!isFinite(v)) throw new Error('非有限数值'); let s = String(v); if (!/[.eE]/.test(s)) s += '.0'; return s; };
let hash = 2166136261 >>> 0;
const H = s => { hash = fnv(hash, s + '\u0002'); return s; };

// ---- 外观表（JSON）
const jsonTables = ['OUTFIT', 'HAT_MAP', 'SIG', 'PWEAPON', 'CULT_WEAPON', 'CULT_SHIELD', 'SOLDIER', 'SOLDIER_WEAPON', 'WEAPONS', 'DUAL_BASE', 'LODP', 'TRI_BUDGET', 'SQUEEZE',
  'HORSE_COATS', 'HORSE_NAMED', 'HCLIPS', 'MOUNT_CLIPS', 'CROWD_CLIPS', 'CULTURES', 'REGION_DEFS', 'GENERIC_FACES', 'STYLE_FALLBACK', 'STANCE', 'SOLDIER_TYPES'];
const jlines = [];
for (const k of jsonTables) { const j = canon(T[k]); H(k + '\u0001' + j); jlines.push(`            ${cs(k)}, ${cs(j)},`); }
// 片段目录
const clipCat = {};
for (const k in I.CL) { const c = I.CL[k]; clipCat[k] = { dur: c.dur, loop: c.loop, desc: c.desc, gait: !c.byStyle }; }
{ const j = canon(clipCat); H('CLIPS\u0001' + j); jlines.push(`            "CLIPS", ${cs(j)},`); }

// ---- 数值表
const arr = a => '{ ' + a.map(num).join(', ') + ' }';
const arr2 = a => '{ ' + a.map(r => 'new double[] ' + arr(r)).join(', ') + ' }';
const nlines = [];
const N2 = (name, a, cm) => { H(name + canon(a)); nlines.push(`        // ${cm}`, `        public static readonly double[][] ${name} = ${arr2(a)};`); };
N2('HPROF', T.HPROF, '头部截面（y, 半宽, 前缘 z, 后缘 z, 前半部方度）');
N2('PHI_Y', T.PHI_Y, '纬度参数 φ → 头部局部高度 y（米）');
N2('ARM_R', T.ARM_R, '臂的截面半径（s, rx, rz）');
N2('LEG_R', T.LEG_R, '腿的截面半径（s, rx, rz）');
H('HEAD_C' + canon(T.HEAD_C)); nlines.push('        public static readonly double[] HEAD_C = ' + arr(T.HEAD_C) + ';');
H('FEAT' + canon(T.FEAT));
nlines.push('        // 五官锚点（头部参数空间）', '        public const double ' + Object.keys(T.FEAT).map(k => 'FEAT_' + k + ' = ' + num(T.FEAT[k])).join(', ') + ';');
for (const k of ['ATLAS_N', 'CELL', 'BAKE_FPS']) { H(k + T[k]); nlines.push(`        public const int ${k} = ${T[k]};`); }
for (const k of ['SKIN_EXP', 'SH_K', 'DL_K']) { H(k + T[k]); nlines.push(`        public const double ${k} = ${num(T[k])};`); }
const sarr = a => '{ ' + a.map(cs).join(', ') + ' }';
H('BONES' + canon(T.BONES) + canon(T.PARENT) + canon(T.HBONES) + canon(T.HPARENT));
nlines.push('        // 人形骨架（下标 = 骨骼编号）与父骨（-1 = 无）');
nlines.push('        public static readonly string[] BONES = ' + sarr(T.BONES) + ';');
nlines.push('        public static readonly int[] PARENT = { ' + T.BONES.map(n => (T.PARENT[n] ? T.BONES.indexOf(T.PARENT[n]) : -1)).join(', ') + ' };');
nlines.push('        // 战马骨架');
nlines.push('        public static readonly string[] HBONES = ' + sarr(T.HBONES) + ';');
nlines.push('        public static readonly int[] HPARENT = { ' + T.HBONES.map(n => (T.HPARENT[n] ? T.HBONES.indexOf(T.HPARENT[n]) : -1)).join(', ') + ' };');
H('FIELDS' + canon(T.FIELDS3) + canon(T.FIELDS1));
nlines.push('        // 关键帧字段：三元字段（下标顺序）与标量字段');
nlines.push('        public static readonly string[] FIELDS3 = ' + sarr(T.FIELDS3) + ';');
nlines.push('        public static readonly string[] FIELDS1 = ' + sarr(T.FIELDS1) + ';');

// ---- 动作关键帧：每个片段 × 流派（compiled() 合并站姿后的完整关键帧）
const STYLES = ['pole', 'one', 'dual', 'fan', 'none', 'bow'];
const EASE = { io: 0, i: 1, o: 2, l: 3 };
const pool = [], poolIdx = new Map(), clipRows = [];
for (const name of Object.keys(I.CL)) {
  if (!I.CL[name].byStyle) continue;
  const ids = [];
  for (const st of STYLES) {
    const c = I.compiled(name, st);
    const flat = [];
    for (const k of c.keys) {
      flat.push(k.t, EASE[k.f.e] != null ? EASE[k.f.e] : 0);
      for (const f of T.FIELDS3) { const v = k.f[f]; if (v == null) flat.push(NaN, NaN, NaN); else flat.push(v[0], v[1], v[2]); }
      for (const f of T.FIELDS1) { const v = k.f[f]; flat.push(v == null ? NaN : v); }
    }
    const key = flat.map(v => (Number.isNaN(v) ? 'N' : String(v))).join(',');
    let id = poolIdx.get(key);
    if (id == null) { id = pool.length; pool.push(flat); poolIdx.set(key, id); }
    ids.push(id);
  }
  H(name + ids.join(','));
  clipRows.push(`            { ${cs(name)}, new[] { ${ids.join(', ')} } },`);
}
for (const p of pool) H(p.map(v => (Number.isNaN(v) ? 'N' : String(v))).join(','));
const stride = 2 + T.FIELDS3.length * 3 + T.FIELDS1.length;

const src = `// <auto-generated>
// 自动生成，勿手改：由 sanguozhi2-web/tools/export-unity-figure.js 从 js/figure.js 导出。
// 数据有变时在 sanguozhi2-web 目录运行 node tools/export-unity-figure.js 重新生成（--check 只检查是否过期）。
// 外观表为 JSON（运行时由 View/FigureMesh.cs 的 FigTables 解析成 JObj）；动作关键帧由 View/FigureRig.cs 采样。
// </auto-generated>
using System.Collections.Generic;

namespace Sanguo
{
    public static class FigureData
    {
        public const uint Hash = 0x${hash.toString(16).padStart(8, '0')}u;   // FNV-1a 32（全部表的规范文本）
        // 外观表：名称, JSON（键按插入顺序）
        public static readonly string[] Tables =
        {
${jlines.join('\n')}
        };
${nlines.join('\n')}
        // 动作关键帧：每帧 ${stride} 个数 = t, 缓动（0 io 1 i 2 o 3 l）, FIELDS3 × 3（缺省 NaN）, FIELDS1（缺省 NaN）
        public const int KeyStride = ${stride};
        public static readonly string[] ClipStyles = ${sarr(STYLES)};
        // 片段名 → 各流派（ClipStyles 顺序）在 ClipKeys 中的下标
        public static readonly Dictionary<string, int[]> ClipIndex = new Dictionary<string, int[]>
        {
${clipRows.join('\n')}
        };
        public static readonly double[][] ClipKeys =
        {
${pool.map(p => '            new double[] ' + arr(p) + ',').join('\n')}
        };
    }
}
`;
if (CHECK) {
  const same = fs.existsSync(OUT) && fs.readFileSync(OUT, 'utf8') === src;
  console.log(same ? `FigureData.g.cs 与 js/figure.js 一致（hash 0x${hash.toString(16).padStart(8, '0')}）` : 'FigureData.g.cs 已过期：请运行 node tools/export-unity-figure.js');
  process.exit(same ? 0 : 1);
}
fs.mkdirSync(path.dirname(OUT), { recursive: true });
fs.writeFileSync(OUT, src);
console.log(`FigureData.g.cs：${jsonTables.length + 1} 张外观表，${clipRows.length} 个片段 × ${STYLES.length} 流派（去重后 ${pool.length} 组关键帧），hash 0x${hash.toString(16).padStart(8, '0')} → ${path.relative(process.cwd(), OUT)}`);
