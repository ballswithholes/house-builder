// 人物模型库的目视对照：网页版 tests/figures.html?shot=1 的截图与 C# 软件光栅器（FigureRender.cs）的同镜头渲染并排。
//   node render.mjs shots <输出目录>     headless Chromium（SwiftShader WebGL）逐个视角截图 web-<名>.png，
//                                       并按 figures.html 的摆放逻辑写出 scenes.json（人物、选项、位置、朝向、片段与时刻、镜头，three 坐标）
//   node render.mjs compose <输出目录>   把 web-<名>.png 与 unity-<名>.png 左右拼成 side-<名>.png（左：网页版，右：C#）
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const WEB = path.resolve(here, '../../../sanguozhi2-web');
const [mode, outDir] = [process.argv[2] || 'shots', path.resolve(process.argv[3] || path.join(here, '../out/figure/render'))];
fs.mkdirSync(outDir, { recursive: true });
// 视角（figures.html 的 URL 参数）：正面、四分之三、侧面、四个动作帧，另加名将一排、士兵、战马
const VIEWS = [
  { name: 'front', w: 540, h: 720, q: { mode: 'one', name: '关羽', pose: 'idle', yaw: 0, dist: 5.4, ty: 1.0 } },
  { name: 'q34', w: 540, h: 720, q: { mode: 'one', name: '关羽', pose: 'idle', yaw: 35, dist: 5.4, ty: 1.0 } },
  { name: 'side', w: 540, h: 720, q: { mode: 'one', name: '关羽', pose: 'idle', yaw: 90, dist: 5.4, ty: 1.0 } },
  { name: 'back', w: 540, h: 720, q: { mode: 'one', name: '关羽', pose: 'idle', yaw: 180, dist: 5.4, ty: 1.0 } },
  { name: 'act-light1', w: 540, h: 720, q: { mode: 'one', name: '关羽', pose: 'light1', t: 0.45, yaw: 30, dist: 5.4, ty: 1.0 } },
  { name: 'act-heavy', w: 540, h: 720, q: { mode: 'one', name: '张飞', pose: 'heavy', t: 0.5, yaw: -40, dist: 5.4, ty: 1.0 } },
  { name: 'act-run', w: 540, h: 720, q: { mode: 'one', name: '赵云', pose: 'run', t: 0.25, yaw: 70, dist: 5.4, ty: 1.0 } },
  { name: 'act-knockdown', w: 540, h: 720, q: { mode: 'one', name: '吕布', pose: 'knockdown', t: 0.9, yaw: 25, ty: 0.5, dist: 4.6, pitch: 0.3 } },
  { name: 'generals', w: 960, h: 540, q: { mode: 'generals', names: '诸葛亮,马超,夏侯惇,黄忠,卑弥呼,塞维鲁', pose: 'idle' } },
  { name: 'generals2', w: 960, h: 540, q: { mode: 'generals', names: '沃洛吉斯,胡维色迦,龙树,加达拉特,阿曼哈利卡,鲁德拉布提', pose: 'idle' } },
  { name: 'soldiers', w: 960, h: 540, q: { mode: 'soldiers', cults: 'han,roman,persia', pose: 'idle' } },
  { name: 'horses', w: 960, h: 540, q: { mode: 'horses', pose: 'idle' } },
];
const FCOL = { '关羽': '#2f7d4a', '张飞': '#2f7d4a', '赵云': '#2f7d4a', '诸葛亮': '#2f7d4a', '刘备': '#2f7d4a', '马超': '#2f7d4a', '黄忠': '#2f7d4a', '曹操': '#2c3d8f', '典韦': '#2c3d8f', '许褚': '#2c3d8f', '夏侯惇': '#2c3d8f', '张辽': '#2c3d8f', '孙策': '#c8382c', '孙权': '#c8382c', '周瑜': '#c8382c', '太史慈': '#c8382c', '甘宁': '#c8382c', '吕布': '#7a2a8a' };

const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
if (mode === 'shots') {
  // 武将数据（与 figures.html 相同：load-web.js 的 gen）
  const { createRequire } = await import('module');
  const { gen } = createRequire(import.meta.url)('./load-web.js');
  const scenes = {};
  for (const v of VIEWS) {
    const page = await browser.newPage({ viewport: { width: v.w, height: v.h }, deviceScaleFactor: 1 });
    page.on('pageerror', e => console.log('页面错误', e.message));
    const qs = new URLSearchParams(Object.assign({ shot: 1, ui: 0, dpr: 1 }, v.q)).toString();
    await page.goto('file://' + path.join(WEB, 'tests/figures.html') + '?' + qs);
    await page.waitForFunction(() => window.__fig && window.__fig.ready === true, null, { timeout: 600000 });
    await page.screenshot({ path: path.join(outDir, 'web-' + v.name + '.png') });
    const errs = await page.evaluate(() => window.__fig.errors);
    if (errs.length) console.log('  页面错误：', errs.join('；'));
    await page.close();
    // 摆放（figures.html 的 build / poseAll，shot 模式：不播放、不转台）
    const q = v.q, items = [];
    let tgt, dist, pitch;
    const clipT = () => q.t != null ? { tf: q.t } : { tf: null };
    if (q.mode === 'one') {
      const g = gen(q.name);
      items.push({ kind: 'general', gen: g, lod: 'high', color: FCOL[q.name] || g.color, x: 0, z: 0, yaw: 0, clip: q.pose, ...clipT() });
      tgt = [0, q.ty != null ? +q.ty : 1.15, 0]; dist = q.dist != null ? +q.dist : 3.6; pitch = q.pitch != null ? +q.pitch : 0.06;
    } else if (q.mode === 'generals') {
      const per = 6, list = q.names.split(','), rowsN = Math.ceil(list.length / per);
      list.forEach((n, i) => {
        const inRow = Math.min(per, list.length - Math.floor(i / per) * per);
        const g = gen(n);
        items.push({ kind: 'general', gen: g, lod: 'high', color: FCOL[n] || g.color, x: ((i % per) - (inRow - 1) / 2) * 1.35, z: -Math.floor(i / per) * 2.2, yaw: 0, clip: q.pose, ...clipT() });
      });
      tgt = [0, 1.05, -(rowsN - 1) * 1.1]; dist = 7.4 + (rowsN - 1) * 3.2; pitch = 0.08 + (rowsN - 1) * 0.1;
    } else if (q.mode === 'soldiers') {
      const cults = q.cults.split(','), types = ['spear', 'sword', 'archer', 'cavalry'], cols = ['#b02a24', '#2c3d8f', '#2f7d4a', '#c89a2a', '#6a2a8a'];
      cults.forEach((c, ci) => types.forEach((ty, ti) => {
        const nrow = Math.min(5, cults.length);
        items.push({ kind: 'soldier', type: ty, culture: c, lod: 'low', seed: ci + ti, color: cols[ci % cols.length], x: (ti - 1.5) * 0.9 + ((ci % 5) - (nrow - 1) / 2) * 4.2, z: -Math.floor(ci / 5) * 2.6, yaw: 0, clip: q.pose, ...clipT() });
      }));
      const rowsS = Math.ceil(cults.length / 5), nS = Math.min(5, cults.length);
      tgt = [0, 0.8, -(rowsS - 1) * 1.3]; dist = q.dist != null ? +q.dist : (rowsS > 1 ? 21 : 4.5 + nS * 3.3); pitch = rowsS > 1 ? 0.2 : 0.1;
    } else if (q.mode === 'horses') {
      const riders = ['吕布', '关羽', '马超', '赞提库斯'];
      riders.forEach((n, i) => {
        const g = gen(n), col = FCOL[n] || g.color;
        const hc = ['idle', 'walk', 'trot', 'gallop', 'rear', 'die'].includes(q.pose) ? q.pose : 'idle';
        items.push({ kind: 'general', gen: g, lod: 'mid', color: col, clip: q.pose, ...clipT(), x: (i - (riders.length - 1) / 2) * 2.2, z: 0, yaw: 0.6,
          horse: { lod: 'mid', seed: i, color: col, name: n === '吕布' ? '赤兔' : null, armor: n === '赞提库斯' ? 'scale' : 'cloth', general: true, clip: hc } });
      });
      tgt = [0, 1.4, 0]; dist = 11; pitch = 0.12;
    }
    const yaw = (+q.yaw || 0) * Math.PI / 180;
    const cam = [tgt[0] + Math.sin(yaw) * Math.cos(pitch) * dist, tgt[1] + Math.sin(pitch) * dist, tgt[2] + Math.cos(yaw) * Math.cos(pitch) * dist];
    scenes[v.name] = { w: v.w, h: v.h, fov: 30, near: 0.05, cam, target: tgt, items };
    console.log('  web-' + v.name + '.png');
  }
  fs.writeFileSync(path.join(outDir, 'scenes.json'), JSON.stringify(scenes, null, 1));
} else {
  const page = await browser.newPage();
  for (const v of VIEWS) {
    const a = path.join(outDir, 'web-' + v.name + '.png'), b = path.join(outDir, 'unity-' + v.name + '.png');
    if (!fs.existsSync(a) || !fs.existsSync(b)) { console.log('  缺 ' + v.name); continue; }
    const url = f => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64');
    const mf = path.join(outDir, 'mask-' + v.name + '.png');
    const data = await page.evaluate(async ([ua, ub, label, um]) => {
      const load = u => new Promise(r => { const im = new Image(); im.onload = () => r(im); im.src = u; });
      const [ia, ib] = await Promise.all([load(ua), load(ub)]);
      let pm = null;
      if (um) { const im = await load(um); const c2 = document.createElement('canvas'); c2.width = im.width; c2.height = im.height; const g2 = c2.getContext('2d'); g2.drawImage(im, 0, 0); pm = g2.getImageData(0, 0, im.width, im.height).data; }
      const cv = document.createElement('canvas'); cv.width = ia.width * 2 + 6; cv.height = ia.height + 22;
      const g = cv.getContext('2d'); g.fillStyle = '#111'; g.fillRect(0, 0, cv.width, cv.height);
      g.drawImage(ia, 0, 22); g.drawImage(ib, ia.width + 6, 22);
      // 数值对照：只统计人物像素（C# 的遮罩 mask-<视角>.png；没有遮罩时取两图任一与左上角背景色相差 > 12 的像素）——
      // 平均绝对差与平均亮度差（网页版 − C#），0..255
      const pa = g.getImageData(0, 22, ia.width, ia.height).data, pb = g.getImageData(ia.width + 6, 22, ia.width, ia.height).data;
      let n = 0, ad = 0, ld = 0;
      for (let i = 0; i < pa.length; i += 4) {
        if (pm) { if (pm[i] < 128) continue; }
        else {
          const fa = Math.abs(pa[i] - pa[0]) + Math.abs(pa[i + 1] - pa[1]) + Math.abs(pa[i + 2] - pa[2]) > 36, fb = Math.abs(pb[i] - pb[0]) + Math.abs(pb[i + 1] - pb[1]) + Math.abs(pb[i + 2] - pb[2]) > 36;
          if (!fa && !fb) continue;
        }
        n++; ad += (Math.abs(pa[i] - pb[i]) + Math.abs(pa[i + 1] - pb[i + 1]) + Math.abs(pa[i + 2] - pb[i + 2])) / 3;
        ld += (pa[i] * 0.299 + pa[i + 1] * 0.587 + pa[i + 2] * 0.114) - (pb[i] * 0.299 + pb[i + 1] * 0.587 + pb[i + 2] * 0.114);
      }
      g.fillStyle = '#eee'; g.font = '14px sans-serif'; g.fillText('网页版 three.js · ' + label, 6, 16); g.fillText('C#（FigureRender 软件光栅器，Unity 坐标）', ia.width + 12, 16);
      return { png: cv.toDataURL('image/png').split(',')[1], n, ad: n ? ad / n : 0, ld: n ? ld / n : 0 };
    }, [url(a), url(b), v.name, fs.existsSync(mf) ? url(mf) : null]);
    fs.writeFileSync(path.join(outDir, 'side-' + v.name + '.png'), Buffer.from(data.png, 'base64'));
    console.log('  side-' + v.name + '.png  人物像素 ' + data.n + '，平均绝对差 ' + data.ad.toFixed(1) + '，平均亮度差（网页 − C#）' + data.ld.toFixed(1));
  }
}
await browser.close();
