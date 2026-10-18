// 攻击画面（第三版，js/clash.js）的网页版参考：headless Chromium（SwiftShader WebGL）逐个打开
// tests/clash.html?embed=1&board=0&case=…&cult=…&scene=…&coast=1&freeze=1.66（用例见 cases.json），等场景建好、动画依次停在
// 用例的各个时刻（cases.json 的 times，缺省接敌 contact 与混战 melee；时刻表见下方 TIME）后：
//   web-<名>.json  布景（文化 / 群落 / 城墙 / 房屋 / 登陆与群落参数）、双方（姓名、颜色、识别色、文化、兵力、阵型）、随机种子、
//                  地形高度采样、地面网格（顶点 / 三角形数、位置 / 顶点色 / aGnd 的和）、道具网格（近 / 远：顶点数、位置与顶点色的和、
//                  aKind 直方图）、水面（河 / 海）、草丛与野花（实例数、位置 / 缩放 / 颜色的和）、两军（人数、阵列、每名士兵的兵种 /
//                  角色 / 阵位 / 倒地时刻）、武将、箭与兵力碎块、事件数、士兵群统计；各时刻的镜头（位置、四元数、视角、宽高比）、雾、
//                  HUD 顶栏下沿与每名士兵 / 旗手 / 武将的位置、朝向、动作片段——全部为 three 坐标
//   web-<名>-<时刻>.png   截图（缺省 960 × 540，用例可用 screen: [宽, 高] 改，如竖屏 [540, 960]；HUD 隐藏）
// 用法：node web.mjs refs <输出目录> [名…]        （由 Tests~/clash/run.sh 调用）
//       node web.mjs compose <输出目录> [名…]     把 web-<名>-<时刻>.png 与 unity-<名>-<时刻>.png 左右拼成 side-<名>-<时刻>.png
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(here, '../../../sanguozhi2-web');
const mode = process.argv[2];
const outDir = path.resolve(process.argv[3] || path.join(here, '../out/clash'));
const only = process.argv.slice(4);
fs.mkdirSync(outDir, { recursive: true });
const CASES = JSON.parse(fs.readFileSync(path.join(here, 'cases.json'), 'utf8')).filter(c => !only.length || only.includes(c.name));
// 时刻表（秒；与 ClashHarness.cs 的 TIME 相同）：行进、冲锋、接敌、必杀特效、混战、整队、收尾
const TIME = { march: 0.35, charge: 1.3, contact: 1.66, fx: 1.8, melee: 2.2, regroup: 2.9, end: 3.3 };
const timesOf = c => (c.times || ['contact', 'melee']).map(n => [n, TIME[n]]).sort((a, b) => a[1] - b[1]);
const sizeOf = c => (c.screen && c.screen.length === 2 ? { W: c.screen[0], H: c.screen[1] } : { W: 960, H: 540 });
for (const c of CASES) for (const [n, v] of timesOf(c)) if (v === undefined) { console.log('未知时刻 ' + n + '（' + c.name + '）'); process.exit(99); }
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
const page = await browser.newPage({ viewport: { width: 960, height: 540 }, deviceScaleFactor: 1 });
page.on('pageerror', e => console.log('页面错误', e.message));

if (mode === 'compose') {
  for (const c of CASES) for (const [tn] of timesOf(c)) {
    const { W, H } = sizeOf(c);
    const a = path.join(outDir, `web-${c.name}-${tn}.png`), b = path.join(outDir, `unity-${c.name}-${tn}.png`);
    if (!fs.existsSync(a) || !fs.existsSync(b)) { console.log('  缺 ' + c.name + ' ' + tn); continue; }
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
    }, [url(a), url(b), W, H, c.title + ' ' + tn]);
    fs.writeFileSync(path.join(outDir, `side-${c.name}-${tn}.png`), Buffer.from(data, 'base64'));
    console.log(`  side-${c.name}-${tn}.png`);
  }
  await browser.close();
  process.exit(0);
}

// ---- 页面内：静态部分（布景、网格、阵列）
function dumpStatic() {
  const sc = SG.Clash.active;
  const hex = c => (c && c.getHexString) ? '#' + c.getHexString() : (c || null);
  const L = sc.look;
  const look = { culture: L.culture, biome: L.biome, wall: L.wall, house: L.house, coast: !!L.coast, treeK: L.treeK, farm: L.farm, snow: L.snow, sandy: L.sandy, flowers: L.flowers,
    grass: hex(L.grass), leaf: hex(L.leaf), fleaf: hex(L.fleaf), tuft: hex(L.tuft), trees: L.trees.map(t => t[0] + ':' + t[1]).join(',') };
  const info = I => ({ name: I.name, side: I.side, color: I.color, accent: I.accent || null, culture: I.culture, before: I.before, after: I.after, formation: I.formation });
  // 地形高度采样（三行网格 + 远处）
  const hs = [];
  for (let z = 8; z >= -70; z -= 6) for (let x = -24; x <= 24; x += 4) hs.push(+sc.h(x, z).toFixed(9));
  // 网格
  const sumAttr = (g, name, n) => { const a = g.attributes[name]; if (!a) return null; const s = new Array(n).fill(0); for (let i = 0; i < a.count; i++) for (let k = 0; k < n; k++) s[k] += a.array[i * a.itemSize + k]; return s; };
  const meshes = { ground: null, props: [], water: [], grass: [] };
  for (const o of sc.scene.children) {
    if (!o.isMesh) continue;
    const g = o.geometry, m = o.material, nm = m && m.name;
    const vc = g.attributes.position.count, tris = (g.index ? g.index.count : vc) / 3;
    if (nm === 'ClashGround') meshes.ground = { verts: vc, tris, pos: sumAttr(g, 'position', 3), col: sumAttr(g, 'color', 3), gnd: sumAttr(g, 'aGnd', 4) };
    else if (nm === 'ClashProp' && !o.isInstancedMesh) {
      const k = g.attributes.aKind, hist = [0, 0, 0, 0, 0];
      if (k) for (let i = 0; i < k.count; i++) hist[Math.round(k.array[i])]++;
      meshes.props.push({ near: !!o.castShadow, verts: vc, tris, pos: sumAttr(g, 'position', 3), col: sumAttr(g, 'color', 3), kinds: hist });
    } else if (m && m.uniforms && m.uniforms.uAlpha) meshes.water.push({ verts: vc, tris, pos: sumAttr(g, 'position', 3), col: sumAttr(g, 'color', 3) });
    else if (nm === 'ClashGrass' && o.isInstancedMesh) {
      const a = o.instanceMatrix.array; let sx = 0, sy = 0, sz = 0, ss = 0, sh = 0, cr = 0, cg = 0, cb = 0;
      for (let i = 0; i < o.count; i++) {
        const q = i * 16; sx += a[q + 12]; sy += a[q + 13]; sz += a[q + 14];
        ss += Math.hypot(a[q], a[q + 1], a[q + 2]); sh += Math.hypot(a[q + 4], a[q + 5], a[q + 6]);
        if (o.instanceColor) { const c = o.instanceColor.array; cr += c[i * 3]; cg += c[i * 3 + 1]; cb += c[i * 3 + 2]; }
      }
      meshes.grass.push({ count: o.count, verts: vc, tris, pos: [sx, sy, sz], scale: [ss, sh], col: [cr, cg, cb], gpos: sumAttr(g, 'position', 3), gcol: sumAttr(g, 'color', 3) });
    }
  }
  const armies = sc.armies.map(A => ({
    n: A.n, dir: A.dir, xFront: A.xFront, minAll: A.minAll, routed: A.routed, brace: A.brace, chargeDX: A.chargeDX, genDX: A.genDX, cheer: A.cheer,
    hex: A.hex,
    soldiers: A.soldiers.map(s => ({ role: s.role, type: s.type, f: s.f, l: s.l, file: s.file, x0: s.x0, z0: s.z0, ed: s.ed, cd: s.cd, rd: s.rd, push: s.push,
      fallT: s.fallT === Infinity ? null : s.fallT, fallKind: s.fallKind, fallDir: s.fallDir, kb: s.kb, hits: s.hits.length, shotDelay: s.shotDelay })),
    general: { x0: A.general.x0, z0: A.general.z0, ed: A.general.ed, p0: A.general.p0, fallT: A.general.fallT === Infinity ? null : A.general.fallT },
  }));
  const arrows = sc.arrows.map(r => ({ S: r.S.si, E: r.E.si, t0: r.t0, T: r.T, land: r.land, spec: !!r.spec, target: r.target ? r.target.si + ':' + r.E.soldiers.indexOf(r.target) : null }));
  const chunks = sc.chunks.map(c => ({ si: c.si, t: c.t, amount: c.amount, kind: c.kind }));
  const ev = sc.events.map(e => +e.t.toFixed(6));
  return { kind: sc.kind, seed: sc.seed, xc: sc.xc, recolored: sc.recolored, look, A: info(sc.A), D: info(sc.D), heights: hs, meshes, armies, arrows, chunks, events: ev,
    crowd: sc.crowd.stats(), figs: sc.figs.length, snow: (sc.look && sc.look.snowH) || null };
}
// ---- 页面内：某时刻的镜头与人物
function dumpTime() {
  const sc = SG.Clash.active, cam = sc.camera;
  cam.updateMatrixWorld(true);
  const cr = sc.crowd;
  const yawOf = q => { const e = new THREE.Euler().setFromQuaternion(q, 'YXZ'); return e.y; };
  const sold = sc.armies.map(A => A.soldiers.map(s => {
    if (s.fig) {
      const r = s.fig.root; r.updateMatrixWorld(true);
      const fp = new THREE.Vector3(); if (s.flag) s.flag.mesh.getWorldPosition(fp);
      return { x: r.position.x, y: r.position.y, z: r.position.z, yaw: yawOf(r.quaternion), clip: s.fig._clipName || null, ct: s.fig._clipT || 0, flag: [fp.x, fp.y, fp.z] };
    }
    const it = cr.items.get(s.cid); if (!it) return null;
    const e = it.m.elements, q = new THREE.Quaternion(), p = new THREE.Vector3(), sv = new THREE.Vector3();
    it.m.decompose(p, q, sv);
    return { x: e[12], y: e[13], z: e[14], yaw: yawOf(q), s: sv.x, clip: it.clip, t0: it.t0, speed: it.speed };
  }));
  const gens = sc.armies.map(A => {
    const G = A.general, r = G.horse.root; r.updateMatrixWorld(true);
    const fp = new THREE.Vector3(); G.flag.mesh.getWorldPosition(fp);
    const pp = new THREE.Vector3(); G.pole.getWorldPosition(pp);
    return { x: r.position.x, y: r.position.y, z: r.position.z, yaw: yawOf(r.quaternion), clip: G.fig._clipName || null, ct: G.fig._clipT || 0, hclip: G.horse._clipName || null, flag: [fp.x, fp.y, fp.z], pole: [pp.x, pp.y, pp.z] };
  });
  return { t: sc.t, cam: { p: cam.position.toArray(), q: cam.quaternion.toArray(), fov: cam.fov, aspect: cam.aspect, far: cam.far, near: cam.near }, crowdTime: cr.time, soldiers: sold, generals: gens,
    hudNdc: sc.hudLineNdc ? sc.hudLineNdc() : null, lastDist: sc._lastDist || null,
    fog: sc.scene.fog ? [sc.scene.fog.near, sc.scene.fog.far] : null };
}

for (const c of CASES) {
  const t0 = Date.now();
  const TIMES = timesOf(c), { W, H } = sizeOf(c);
  await page.setViewportSize({ width: W, height: H });
  const q = new URLSearchParams({ embed: '1', board: '0', case: c.case, freeze: String(TIMES[0][1]) });
  if (c.cult) q.set('cult', c.cult);
  if (c.special) q.set('special', c.special);
  if (c.scene) q.set('scene', c.scene);
  if (c.biome) q.set('biome', c.biome);
  if (c.coast) q.set('coast', '1');
  await page.goto('file://' + path.join(WEB, 'tests/clash.html') + '?' + q.toString());
  const ref = { case: c, times: {} };
  for (const [tn, tv] of TIMES) {
    await page.evaluate(v => { SG.Clash.debug.freezeAt = v; }, tv);
    await page.waitForFunction(v => { const s = SG.Clash.active; return s && s.built && s.state === 'run' && s.t >= v - 1e-9; }, tv, { timeout: 900000, polling: 100 });
    if (!ref.static) ref.static = await page.evaluate(dumpStatic);
    // 停住后再渲染几帧（粒子按时间轴停住；水面 / 草随实际时间）
    await page.evaluate(() => new Promise(r => { let n = 0; const f = () => (++n >= 8 ? r() : requestAnimationFrame(f)); requestAnimationFrame(f); }));
    ref.times[tn] = await page.evaluate(dumpTime);
    await page.evaluate(() => { for (const e of document.querySelectorAll('.sg-clash')) e.style.visibility = 'hidden'; });
    await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
    await page.screenshot({ path: path.join(outDir, `web-${c.name}-${tn}.png`) });
    await page.evaluate(() => { for (const e of document.querySelectorAll('.sg-clash')) e.style.visibility = ''; });
  }
  fs.writeFileSync(path.join(outDir, `web-${c.name}.json`), JSON.stringify(ref));
  console.log(`  ${c.name}：${((Date.now() - t0) / 1000).toFixed(1)} 秒`);
}
await browser.close();
