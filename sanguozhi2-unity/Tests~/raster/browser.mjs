// 用 Playwright 在网页版的 tests/portraits.html 里渲染同一批头像（联系表），并与 C# 输出逐格比较。
//   node Tests~/raster/browser.mjs [sheet.json] [browser.png] [csharp.png]
// 依赖全局安装的 playwright（/opt/node22/lib/node_modules/playwright）；页面先载入世界数据（与游戏相同的外貌数据）。
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const here = path.dirname(fileURLToPath(import.meta.url));
const out = path.resolve(here, '../out/raster');
const sheetPath = process.argv[2] || path.join(out, 'sheet.json');
const pngPath = process.argv[3] || path.join(out, 'browser.png');
const csPath = process.argv[4] || path.join(out, 'csharp.png');
const web = path.resolve(here, '../../../sanguozhi2-web');
const sheet = JSON.parse(fs.readFileSync(sheetPath, 'utf8'));
const COLS = +(process.env.COLS || 8), CELL = Math.max(...sheet.map(s => s.opts.size)), PAD = 4;
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const page = await browser.newPage({ deviceScaleFactor: 1 });
page.on('pageerror', e => console.log('页面错误', e.message));
await page.goto('file://' + path.join(web, 'tests/portraits.html') + '?perf=0');
await page.waitForFunction(() => window.SG && SG.Portrait);
// 游戏的载入顺序里 world-data.js 在 portrait-data.js 之前：补载后重跑外貌数据（add 会合并并清缓存）
await page.addScriptTag({ path: path.join(web, 'js/world-data.js') });
await page.addScriptTag({ path: path.join(web, 'js/portrait-data.js') });
const csData = fs.existsSync(csPath) ? 'data:image/png;base64,' + fs.readFileSync(csPath).toString('base64') : null;
const res = await page.evaluate(async ({ sheet, COLS, CELL, PAD, csData }) => {
  const P = SG.Portrait;
  P.clearCache();
  const rows = Math.ceil(sheet.length / COLS);
  const W = COLS * (CELL + PAD) + PAD, H = rows * (CELL + PAD) + PAD;
  const c = document.createElement('canvas');
  c.width = W; c.height = H;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#16141c'; ctx.fillRect(0, 0, W, H);
  const times = [];
  for (let i = 0; i < sheet.length; i++) {
    const t0 = performance.now();
    const pc = await P.canvas(sheet[i].gen, sheet[i].opts);
    times.push(performance.now() - t0);
    ctx.drawImage(pc, PAD + (i % COLS) * (CELL + PAD), PAD + Math.floor(i / COLS) * (CELL + PAD));
  }
  const url = c.toDataURL('image/png');
  // 与 C# 联系表逐格比较：平均绝对差（0..255）与差 > 48 的像素比例
  let cmp = null;
  if (csData) {
    const img = new Image();
    await new Promise((ok, fail) => { img.onload = ok; img.onerror = fail; img.src = csData; });
    const c2 = document.createElement('canvas'); c2.width = img.width; c2.height = img.height;
    const x2 = c2.getContext('2d'); x2.drawImage(img, 0, 0);
    const A = ctx.getImageData(0, 0, W, H).data, B = x2.getImageData(0, 0, W, H).data;
    cmp = [];
    for (let i = 0; i < sheet.length; i++) {
      const ox = PAD + (i % COLS) * (CELL + PAD), oy = PAD + Math.floor(i / COLS) * (CELL + PAD);
      let sum = 0, big = 0;
      for (let y = 0; y < CELL; y++) for (let x = 0; x < CELL; x++) {
        const k = ((oy + y) * W + ox + x) * 4;
        const d = (Math.abs(A[k] - B[k]) + Math.abs(A[k + 1] - B[k + 1]) + Math.abs(A[k + 2] - B[k + 2])) / 3;
        sum += d; if (d > 48) big++;
      }
      cmp.push({ label: sheet[i].label, mad: +(sum / (CELL * CELL)).toFixed(2), bigPct: +(100 * big / (CELL * CELL)).toFixed(2) });
    }
  }
  return { url, times, cmp };
}, { sheet, COLS, CELL, PAD, csData });
fs.writeFileSync(pngPath, Buffer.from(res.url.split(',')[1], 'base64'));
const avg = res.times.reduce((a, b) => a + b, 0) / res.times.length;
console.log(`浏览器联系表 ${sheet.length} 张 → ${pngPath}（P.canvas 平均 ${avg.toFixed(1)} ms，含 PNG 编解码）`);
if (res.cmp) {
  const mads = res.cmp.map(c => c.mad);
  console.log('逐格比较（C# vs 浏览器）：平均绝对差 ' + (mads.reduce((a, b) => a + b, 0) / mads.length).toFixed(2) + ' / 255，最大 ' + Math.max(...mads).toFixed(2));
  for (const c of res.cmp) console.log(`  ${c.label.padEnd(14)} MAD ${String(c.mad).padStart(6)}  差>48 的像素 ${c.bigPct}%`);
}
await browser.close();
