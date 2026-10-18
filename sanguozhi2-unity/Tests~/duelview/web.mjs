// 单挑画面（第三版）的网页版参考：headless Chromium（SwiftShader WebGL）打开 sanguozhi2-web/tests/duel.html，
// 按 cases.json 为每对武将开一场观战单挑，场景建好后接管画面（DuelScreen.update 换成空函数，界面隐藏），
//   refs：导出 web-<名>.json —— 双方武将（文化、兵器、盾、披风、弓将）、兵器长度校准（缩放、前冲上限、各招 tc / te / tip / grip /
//         target / lunge 与逐帧尖端采样）、场地（各网格的顶点数与坐标和——旗面先按 arenaT 摆好、实例化景物逐类统计、帅旗、火把、场地随机数的下一个值）；
//         每个镜头（cases.json 的 shots：按给定的 DuelFighter 字段摆好两人，视图推进 steps × dt）的片段、片段时刻、根节点、
//         转身角、前冲、辉光、刃尖 / 刃根与若干骨骼的世界坐标（three 坐标），并截图 web-<名>-<镜头>.png。
//   compose：web-<名>-<镜头>.png 与 unity-<名>-<镜头>.png 左右拼成 side-<名>-<镜头>.png（左 网页版、右 C#）。
//   gens：web-gens.json —— 用例武将的能力、文化与势力色（双方颜色过近时的改色由 DuelScreen 自己做）。
// 用法：node web.mjs refs|gens|compose <输出目录> [名…]
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(HERE, '../../../sanguozhi2-web');
const [mode, out, ...only] = process.argv.slice(2);
const cfg = JSON.parse(fs.readFileSync(path.join(HERE, 'cases.json'), 'utf8'));
const cases = cfg.cases.filter(c => !only.length || only.includes(c.name));
const W = 960, H = 540;

async function refs() {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  try {
    for (const c of cases) {
      const t0 = Date.now();
      const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
      const errs = [];
      page.on('pageerror', e => errs.push(String(e)));
      page.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
      await page.goto('file://' + WEB + '/tests/duel.html?seed=7&pr=1&side=watch&terrain=' + c.terrain);
      await page.waitForFunction(() => window.__ready, null, { timeout: 120000 });
      await page.evaluate(c => { window.runDuel({ a: c.a, b: c.b, side: 'watch', terrain: c.terrain }); }, c);
      // 场景建好（武将、场地都在）后立即接管：DuelScreen.update → 空函数（模拟与镜头都停住），界面隐藏
      await page.waitForFunction(() => { const d = window.SG.DuelGame.active; return d && d.war && d.arena && d.screen; }, null, { timeout: 300000 });
      await page.evaluate(() => {
        const d = window.SG.DuelGame.active;
        d.update = () => {};
        const st = document.createElement('style');
        st.textContent = '.sgd,.sgd-fade,#panel,#out{display:none !important}';
        document.head.appendChild(st);
      });
      await page.waitForTimeout(300);
      const data = await page.evaluate(cfg => {
        const SG = window.SG, d = SG.DuelGame.active, sim = d.sim;
        const r6 = v => Math.round(v * 1e6) / 1e6;
        const gens = [d.genA, d.genB].map(g => ({ name: g.name, war: g.war, intel: g.intel, pol: g.pol, culture: g.culture || null, color: g.color || null }));
        const cols = [d.colA, d.colB].map(c => typeof c === 'string' ? c : '#' + new THREE.Color(c).getHexString());
        const war = d.war.map(w => {
          const cal = {};
          for (const k in w.cal) {
            const q = w.cal[k], s = q.samp;
            cal[k] = { tc: q.tc, te: q.te, dur: q.dur, tip: q.tip, grip: q.grip, target: q.target, lunge: q.lunge, n: s ? s.n : 0,
              T: s ? Array.from(s.T) : [], X: s ? Array.from(s.X) : [], Y: s ? Array.from(s.Y) : [] };
          }
          return { name: w.f.gen.name, culture: w.look.culture, weapon: w.look.weapon, shieldLook: w.look.shield || null, figWeapon: w.figWeapon, fstyle: w.fstyle, style: w.style,
            wScale: w.wScale, lungeMax: w.lungeMax, hasShield: !!w.fig.shieldMesh, hasCape: !!w.fig._hasCape, bow: !!w.planted, bowName: w.bowName || null, wpn2: !!w.wpn2,
            reach: w.f.reach, spd: w.f.spd, cal };
        });
        // 场地（先按 arenaT 摆好旗面：网格坐标和与时刻无关，C# 侧同样先 arena.Update(arenaT)）
        const A = d.arena;
        A.update(cfg.arenaT);
        const meshes = [];
        A.group.traverse(o => {
          if (!o.isMesh || o.isInstancedMesh || o === A.sky) return;
          const p = o.geometry.attributes.position; if (!p) return;
          let sx = 0, sy = 0, sz = 0;
          o.updateMatrixWorld(true);
          const v = new THREE.Vector3();
          for (let i = 0; i < p.count; i++) { v.fromBufferAttribute(p, i).applyMatrix4(o.matrixWorld); sx += v.x; sy += v.y; sz += v.z; }
          meshes.push({ name: o.name || '', n: p.count, tris: o.geometry.index ? o.geometry.index.count / 3 : p.count / 3, sx: r6(sx), sy: r6(sy), sz: r6(sz) });
        });
        const inst = {};
        for (const k in A.inst || {}) {
          const L = A.inst[k]; const s = { n: L.length, x: 0, y: 0, z: 0, s: 0, yaw: 0 };
          for (const o of L) { s.x += o.x; s.y += o.y - (o.sink || 0) * o.s; s.z += o.z; s.s += o.s; s.yaw += o.yaw; }
          for (const q of ['x', 'y', 'z', 's', 'yaw']) s[q] = r6(s[q]);
          inst[k] = s;
        }
        const arena = { kind: A.kind, meshes, inst, torches: A.torches.map(t => [r6(t.x), r6(t.y), r6(t.z)]), banners: A.banners.map(b => [r6(b.cloth.position.x), r6(b.cloth.position.y), r6(b.cloth.position.z), b.w]),
          troops: A.troops ? [A.troops[0].length, A.troops[1].length] : null };
        return { gens, cols, war, arena };
      }, cfg);
      // 镜头
      data.shots = [];
      for (const name of c.shots) {
        const spec = cfg.shots[name];
        const shot = await page.evaluate(([spec, cfg]) => {
          const SG = window.SG, d = SG.DuelGame.active, sim = d.sim;
          const BONES = ['head', 'chest', 'handR', 'handL', 'footR', 'footL', 'cape3'];
          sim.clock = cfg.clock;
          for (let i = 0; i < 2; i++) {
            const f = sim.f[i], o = spec.f[i];
            Object.assign(f, { state: 'idle', t: 0, seq: 0, y: 0, vx: 0, vy: 0, charge: 0, stun: 0.3, hitH: 'mid', hitBack: false, rage: 0, ko: false, counterUntil: 0, parryReadyAt: 0 });
            f.face = i === 0 ? 1 : -1;
            Object.assign(f, o);
            d.war[i].intro = -1;
            // 弹簧（披风 / 盔缨 / 发）与人物时钟归零：各镜头互不影响，也不受接管前开场帧数的影响
            Object.assign(d.war[i].fig._sec, { cape: 0, capeV: 0, plume: 0, plumeV: 0, hair: 0, hairV: 0, live: false });
            d.war[i].fig._time = 0;
          }
          const steps = cfg.steps, dt = cfg.dt;
          for (let i = 0; i < 2; i++) d.war[i].time = cfg.time[i] - steps * dt;
          for (let k = 0; k < steps; k++)
            for (let i = 0; i < 2; i++) {
              const w = d.war[i];
              w.ctr = sim.countering(sim.f[i]) ? sim.f[i].counterUntil - sim.clock : 0;
              w.update(dt, false);
            }
          d.arena.update(cfg.arenaT);
          // 镜头（three 坐标）与随镜头的灯光
          const mid = (sim.f[0].x + sim.f[1].x) / 2, C = cfg.cam;
          d.camera.position.set(mid, C.h, C.dist); d.camera.up.set(0, 1, 0); d.camera.lookAt(mid, C.look, 0);
          d.camera.aspect = window.innerWidth / window.innerHeight; d.camera.updateProjectionMatrix(); d.camera.updateMatrixWorld(true);
          const L = d.lights;
          L.key.target.position.set(mid, 0, 0); L.key.position.set(mid - 6, 11, 8);
          L.rim.target.position.set(mid, 1, 0); L.rim.position.set(mid + 5, 5, -9);
          L.key.target.updateMatrixWorld(); L.rim.target.updateMatrixWorld();
          const r6 = v => Math.round(v * 1e6) / 1e6, P = v => [r6(v.x), r6(v.y), r6(v.z)];
          const res = { name: spec.name, f: [] };
          for (let i = 0; i < 2; i++) {
            const w = d.war[i], fig = w.fig;
            w.root.updateMatrixWorld(true);
            const bones = {};
            for (const b of fig.skeleton.bones) if (BONES.includes(b.name)) bones[b.name] = P(b.getWorldPosition(new THREE.Vector3()));
            res.f.push({ clip: fig._clipName, t: fig._clipT, key: w.key, root: P(w.root.position), turn: w.turn, lungeX: w.lungeX, tint: [r6(w.tint.r), r6(w.tint.g), r6(w.tint.b)],
              tip: P(w.tipW[0]), base: P(w.baseW[0]), bowOn: !!w.bowOn, bones });
          }
          return res;
        }, [spec, cfg]);
        shot.name = name;
        data.shots.push(shot);
        await page.waitForTimeout(120);
        await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
        await page.locator('#app canvas').first().screenshot({ path: path.join(out, `web-${c.name}-${name}.png`) });
      }
      data.arena.rndNext = await page.evaluate(() => window.SG.DuelGame.active.arena.rnd.nextDouble());
      data.errors = errs;
      fs.writeFileSync(path.join(out, `web-${c.name}.json`), JSON.stringify(data));
      console.log(`[web] ${c.name}: ${data.war.map(w => w.name + ' ' + w.figWeapon + ' ×' + w.wScale.toFixed(3)).join('，')}；${data.shots.length} 个镜头；${((Date.now() - t0) / 1000).toFixed(0)} 秒` + (errs.length ? '；错误 ' + errs.length + '：' + errs[0] : ''));
      await page.close();
    }
  } finally { await browser.close(); }
}

// 武将的势力色与能力（不开单挑，快速）：web-gens.json
async function gens() {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  try {
    const page = await browser.newPage({ viewport: { width: W, height: H } });
    await page.goto('file://' + WEB + '/tests/duel.html?seed=7&pr=1');
    await page.waitForFunction(() => window.__ready, null, { timeout: 120000 });
    const names = [...new Set(cases.flatMap(c => [c.a, c.b]))];
    const g = await page.evaluate(names => names.map(n => window.__gens.find(g => g.name === n)).map(g => ({ name: g.name, war: g.war, intel: g.intel, pol: g.pol, culture: g.culture || null, color: g.color })), names);
    fs.writeFileSync(path.join(out, 'web-gens.json'), JSON.stringify(g));
    console.log('[web] ' + g.length + ' 名武将');
  } finally { await browser.close(); }
}

async function compose() {
  const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
  try {
    const page = await browser.newPage({ viewport: { width: 2 * W + 12, height: H + 30 } });
    for (const c of cases) for (const s of c.shots) {
      const a = path.join(out, `web-${c.name}-${s}.png`), b = path.join(out, `unity-${c.name}-${s}.png`);
      if (!fs.existsSync(a) || !fs.existsSync(b)) continue;
      const u = f => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64');
      await page.setContent(`<body style="margin:0;background:#222;color:#ddd;font:13px sans-serif"><div style="display:flex;gap:12px"><img src="${u(a)}" width="${W}" height="${H}"><img src="${u(b)}" width="${W}" height="${H}"></div><div style="padding:6px">${c.name} · ${s} — 左：网页版　右：C#（软件光栅）</div></body>`);
      await page.screenshot({ path: path.join(out, `side-${c.name}-${s}.png`) });
    }
  } finally { await browser.close(); }
}

if (mode === 'refs') await refs();
else if (mode === 'compose') await compose();
else if (mode === 'gens') await gens();
else { console.error('用法：node web.mjs refs|compose <输出目录> [名…]'); process.exit(2); }
