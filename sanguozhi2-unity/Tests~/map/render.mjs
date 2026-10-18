// 战略地图的对照截图（第三版）：
//   node render.mjs shots <输出目录>      网页版：headless Chromium（SwiftShader WebGL）打开 web-shot.html，按 VIEWS 逐个渲染截图
//                                         （web-<名>.png），镜头参数（three 坐标）写入 cams.json，供 C# 软件光栅器（MapRender.cs）使用
//   node render.mjs compose <输出目录>    把 web-<名>.png 与 unity-<名>.png 左右拼成 side-<名>.png（左：网页版，右：C#）
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const [mode, outDir] = [process.argv[2] || 'shots', path.resolve(process.argv[3] || path.join(here, '../out/map/render'))];
fs.mkdirSync(outDir, { recursive: true });
// 视角：城池编号（世界剧本）、镜头距离、偏航
const VIEWS = [
  { name: 'zhongyuan', city: 11, dist: 44 },        // 中原（洛阳）
  { name: 'rome', city: 114, dist: 44 },            // 罗马
  { name: 'india', city: 186, dist: 44 },           // 天竺（华氏城）
  { name: 'luoyang-near', city: 11, dist: 14, yaw: 30 },
  { name: 'persia-far', city: 60, dist: 150 },      // 远景：远景地形栅格、远景城池、云
];
const W = 960, H = 540;
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
const page = await browser.newPage({ viewport: { width: W, height: H }, deviceScaleFactor: 1 });
page.on('pageerror', e => console.log('页面错误', e.message));
if (mode === 'shots') {
  await page.goto('file://' + path.join(here, 'web-shot.html'));
  await page.waitForFunction(() => window.__ready === true, null, { timeout: 600000 });
  console.log('网页版构建 ' + Math.round(await page.evaluate(() => window.__buildMs)) + ' ms');
  const cams = {};
  for (const v of VIEWS) {
    const xy = await page.evaluate(id => window.cityPos(id), v.city);
    cams[v.name] = await page.evaluate(a => window.shot(...a), [xy[0], xy[1], v.dist, v.yaw || 0]);
    await page.screenshot({ path: path.join(outDir, 'web-' + v.name + '.png') });
    console.log('  web-' + v.name + '.png');
  }
  fs.writeFileSync(path.join(outDir, 'cams.json'), JSON.stringify(cams, null, 1));
  // 远景树公告板图集（512²）
  const atlas = await page.evaluate(() => {
    const t = map._treeTiles.find(t => t.far && t.far.material && t.far.material.uniforms);
    return t.far.material.uniforms.uMap.value.image.toDataURL('image/png').split(',')[1];
  });
  fs.writeFileSync(path.join(outDir, 'web-atlas.png'), Buffer.from(atlas, 'base64'));
  console.log('  web-atlas.png');
} else if (mode === 'crop') {
  // node render.mjs crop <输出目录> <视角> x y w h：两张图的同一区域放大 2 倍左右拼接（crop-<视角>.png）
  const [name, x, y, w, h] = [process.argv[4], ...process.argv.slice(5, 9).map(Number)];
  const url = f => 'data:image/png;base64,' + fs.readFileSync(path.join(outDir, f)).toString('base64');
  const data = await page.evaluate(async ([ua, ub, x, y, w, h]) => {
    const load = u => new Promise(r => { const im = new Image(); im.onload = () => r(im); im.src = u; });
    const [ia, ib] = await Promise.all([load(ua), load(ub)]);
    const cv = document.createElement('canvas'); cv.width = w * 4 + 6; cv.height = h * 2;
    const g = cv.getContext('2d'); g.imageSmoothingEnabled = false;
    g.drawImage(ia, x, y, w, h, 0, 0, w * 2, h * 2); g.drawImage(ib, x, y, w, h, w * 2 + 6, 0, w * 2, h * 2);
    return cv.toDataURL('image/png').split(',')[1];
  }, [url('web-' + name + '.png'), url('unity-' + name + '.png'), x, y, w, h]);
  fs.writeFileSync(path.join(outDir, 'crop-' + name + '.png'), Buffer.from(data, 'base64'));
} else {
  for (const v of VIEWS.concat([{ name: 'atlas' }])) {
    const a = path.join(outDir, 'web-' + v.name + '.png'), b = path.join(outDir, 'unity-' + v.name + '.png');
    if (!fs.existsSync(a) || !fs.existsSync(b)) { console.log('  缺 ' + v.name); continue; }
    const url = f => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64');
    const data = await page.evaluate(async ([ua, ub, W, H]) => {
      const load = u => new Promise(r => { const im = new Image(); im.onload = () => r(im); im.src = u; });
      const [ia, ib] = await Promise.all([load(ua), load(ub)]);
      if (ia.width === ia.height) { W = ia.width; H = ia.height; }   // 图集：原尺寸、灰底
      const cv = document.createElement('canvas'); cv.width = W * 2 + 6; cv.height = H;
      const g = cv.getContext('2d'); g.fillStyle = ia.width === ia.height ? '#7a8a9a' : '#000'; g.fillRect(0, 0, cv.width, cv.height);
      g.drawImage(ia, 0, 0, W, H); g.drawImage(ib, W + 6, 0, W, H);
      g.font = 'bold 18px sans-serif'; g.fillStyle = '#fff'; g.strokeStyle = '#000'; g.lineWidth = 3;
      for (const [t, x] of [['网页版', 10], ['C# 软件光栅', W + 16]]) { g.strokeText(t, x, 26); g.fillText(t, x, 26); }
      return cv.toDataURL('image/png').split(',')[1];
    }, [url(a), url(b), W, H]);
    fs.writeFileSync(path.join(outDir, 'side-' + v.name + '.png'), Buffer.from(data, 'base64'));
    console.log('  side-' + v.name + '.png');
  }
}
await browser.close();
