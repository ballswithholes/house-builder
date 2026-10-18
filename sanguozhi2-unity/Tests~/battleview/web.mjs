// 战场画面（第三版，js/battle-view.js）的网页版参考：headless Chromium（SwiftShader WebGL）逐个打开
// tests/battle-view.html?real=1&terrain=1&sc=world&city=…&from=…&manual=1（先建世界地图，战场地形取自城池周边；
// 用例写 "terrain": false 时不带 terrain=1——旧的随机地形，战场画面走合成取样场），
// 等人物模型就绪（view.ready）后：
//   web-<名>.json  棋盘与部队（地图、部队的武将 / 位置 / 兵力）、view.stats（三角形数、树石草与点缀的数量、水面、船）、
//                  各实例批次（名称、数量、位置与缩放的和、实例色的和）、地形网格高度的和、各格站立高度、部队的贴地位置与朝向、
//                  士兵（兵种、骑乘、实例矩阵）、主将与战马的世界矩阵、镜头（位置、四元数、视角、宽高比）——three 坐标
//   web-<名>.png   截图（960 × 540）
// 用法：node web.mjs <输出目录> [名…]          （由 Tests~/battleview/run.sh 调用；cases.json 为用例表）
//       node web.mjs compose <输出目录>        把 web-<名>.png 与 unity-<名>.png 左右拼成 side-<名>.png
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(here, '../../../sanguozhi2-web');
const compose = process.argv[2] === 'compose';
const outDir = path.resolve(process.argv[compose ? 3 : 2] || path.join(here, '../out/battleview'));
const only = process.argv.slice(compose ? 4 : 3);
fs.mkdirSync(outDir, { recursive: true });
const CASES = JSON.parse(fs.readFileSync(path.join(here, 'cases.json'), 'utf8')).filter(c => !only.length || only.includes(c.name));
const W = 960, H = 540;
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
page.on('pageerror', e => console.log('页面错误', e.message));

if (compose) {
  for (const c of CASES) {
    const a = path.join(outDir, 'web-' + c.name + '.png'), b = path.join(outDir, 'unity-' + c.name + '.png');
    if (!fs.existsSync(a) || !fs.existsSync(b)) { console.log('  缺 ' + c.name); continue; }
    const url = f => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64');
    const data = await page.evaluate(async ([ua, ub, W, H, title]) => {
      const load = u => new Promise(r => { const im = new Image(); im.onload = () => r(im); im.src = u; });
      const [ia, ib] = await Promise.all([load(ua), load(ub)]);
      const cv = document.createElement('canvas'); cv.width = W * 2 + 6; cv.height = H;
      const g = cv.getContext('2d'); g.fillStyle = '#000'; g.fillRect(0, 0, cv.width, cv.height);
      g.drawImage(ia, 0, 0, W, H); g.drawImage(ib, W + 6, 0, W, H);
      g.font = 'bold 18px sans-serif'; g.fillStyle = '#fff'; g.strokeStyle = '#000'; g.lineWidth = 3;
      for (const [t, x] of [['网页版 ' + title, 10], ['C# 软件光栅', W + 16]]) { g.strokeText(t, x, 26); g.fillText(t, x, 26); }
      return cv.toDataURL('image/png').split(',')[1];
    }, [url(a), url(b), W, H, c.city + '<' + c.from]);
    fs.writeFileSync(path.join(outDir, 'side-' + c.name + '.png'), Buffer.from(data, 'base64'));
    console.log('  side-' + c.name + '.png');
  }
  await browser.close();
  process.exit(0);
}

for (const c of CASES) {
  const t0 = Date.now();
  // terrain: false 的用例不建战略地图：BattleModel 用旧的随机地形，战场画面由地块类型合成取样场（synthGrid）
  const q = new URLSearchParams({ real: '1', sc: 'world', city: c.city, from: c.from, manual: '1', dist: String(c.dist || 46) });
  if (c.terrain !== false) q.set('terrain', '1');
  await page.goto('file://' + path.join(WEB, 'tests/battle-view.html') + '?' + q.toString());
  await page.waitForFunction(() => window.__ready === true || window.__error, null, { timeout: 900000 });
  const err = await page.evaluate(() => window.__error || null);
  if (err) { console.log('  ' + c.name + ' 出错：' + err); continue; }
  await page.evaluate(async () => { await window.T.view.ready; });
  // 固定 dt 步进（标签避让、树的淡出、动作），最后一帧渲染
  await page.evaluate(() => { window.T.step(90, 1 / 60); });
  const ref = await page.evaluate(() => {
    const V = window.T.view, m = window.T.model, Tn = SG.Terrain, tr = m.terrain || null;
    const ch = 'pfhmrwgcW';
    const rows = [];
    for (let y = m.H - 1; y >= 0; y--) { let s = ''; for (let x = 0; x < m.W; x++) s += ch[m.map[x][y]] || '?'; rows.push(s); }
    const arr = a => Array.from(a);
    const props = (V._props || []).map(im => {
      const a = im.instanceMatrix.array; let sx = 0, sy = 0, sz = 0, ss = 0, sr = 0, sg = 0, sb = 0;
      for (let k = 0; k < im.count; k++) {
        const o = k * 16;
        sx += a[o + 12]; sy += a[o + 13]; sz += a[o + 14];
        ss += Math.hypot(a[o], a[o + 1], a[o + 2]) + Math.hypot(a[o + 4], a[o + 5], a[o + 6]);
        if (im.instanceColor) { const c = im.instanceColor.array; sr += c[k * 3]; sg += c[k * 3 + 1]; sb += c[k * 3 + 2]; }
      }
      return { name: im.name, count: im.count, tris: im.geometry.attributes.position.count / 3, sx, sy, sz, ss, sr, sg, sb };
    });
    let hs = 0; for (const v of V._grid.hh) hs += v;
    const cr = V.crowd;
    const units = [...V.visuals.values()].map(v => {
      v.group.updateMatrixWorld(true);
      const it = q => (cr && cr.items.get(q.id)) || null;
      return {
        id: v.u.gen.id, name: v.u.gen.name, side: v.u.side, x: v.u.x, y: v.u.y, troops: v.u.troops, alive: v.u.alive, commander: !!v.u.commander,
        culture: v.culture, pos: v.group.position.toArray(), rotY: v.group.rotation.y, ringY: v.ring.position.y,
        fig: v.fig ? { style: v.fig.style, horse: v.horse.root.matrixWorld.elements.slice(), rider: v.fig.root.matrixWorld.elements.slice() } : null,
        sold: v.sold.map(q => ({ type: q.type, mounted: q.mounted, x: q.x, z: q.z, clip: q.clip, m: it(q) ? it(q).m.elements.slice() : null })),
      };
    });
    const cam = SG.Gfx.camera; cam.updateMatrixWorld();
    return {
      W: m.W, H: m.H, castle: m.castle, map: rows,
      target: { id: m.setup.target.id, key: m.setup.target.key, name: m.setup.target.name, culture: m.setup.target.culture, lat: m.setup.target.lat },
      src: m.setup.src ? { id: m.setup.src.id, name: m.setup.src.name } : null, attacker: m.setup.attacker, defender: m.setup.defender,
      terrain: tr ? { seed: tr.seed, notes: tr.notes, angle: tr.angle, turned: tr.turned, h0: tr.h0, biome: tr.biome, bridges: tr.bridges } : null,
      look: { culture: V.look.culture, style: V.look.style, groundKind: V.look.groundKind },
      stats: JSON.parse(JSON.stringify(V.stats)), props, hhSum: hs, hhN: V._grid.hh.length, nx: V._grid.nx, ny: V._grid.ny,
      tileY: arr(V._tileY), units,
      cam: { pos: cam.position.toArray(), quat: cam.quaternion.toArray(), fov: cam.fov, aspect: cam.aspect, near: cam.near, far: cam.far },
      sunDir: SG.Gfx.sunDir ? SG.Gfx.sunDir.toArray() : null,
      viewMs: window.__viewMs, mapMs: window.__mapMs,
    };
  });
  fs.writeFileSync(path.join(outDir, 'web-' + c.name + '.json'), JSON.stringify(ref));
  await page.screenshot({ path: path.join(outDir, 'web-' + c.name + '.png') });
  const s = ref.stats;
  console.log(`  ${c.name}：${c.city}<${c.from} ${ref.look.style}/${ref.look.groundKind} 地形 ${s.terrainTris} 树 ${s.trees} 石 ${s.rocks} 草 ${s.tufts} 点缀 ${s.dress || 0} 城 ${s.fortTris} 水 ${s.waterTris || 0} 船 ${s.boats || 0}（${Math.round((Date.now() - t0) / 1000)} 秒）`);
}
await browser.close();
