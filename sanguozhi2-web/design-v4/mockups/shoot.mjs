// 样张截图：node design-v4/mockups/shoot.mjs [name-filter]
// 每个样张按桌面 1280×720 与手机横屏 844×390（?phone）各截一张，写入 design-v4/mockups/shots/。
// 需要 Playwright（/opt/node22/lib/node_modules/playwright）。整页样张（01-kit）按全页截图。
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import { fileURLToPath } from 'url';
import path from 'path';
const dir = path.dirname(fileURLToPath(import.meta.url));
const filter = process.argv[2] || '';
const JOBS = [
  ['01-kit', '', 'desk', { full: true }],
  ['02-title', '', 'desk'], ['02-title', 'phone', 'phone'],
  ['03-main', '', 'desk'], ['03-main', 'phone', 'phone'], ['03-main', 'night&nonotes', 'desk', { suffix: 'night' }], ['03-main', 'month&nonotes', 'desk', { suffix: 'month' }],
  ['04-menu', '', 'desk'], ['04-menu', 'phone', 'phone'],
  ['05-settings', '', 'desk'], ['05-settings', 'phone', 'phone'],
  ['06-generals', '', 'desk'], ['06-generals', 'phone', 'phone'],
  ['07-character', '', 'desk'], ['07-character', 'phone', 'phone'], ['07-character', 'tab=gear', 'desk', { suffix: 'gear' }], ['07-character', 'tab=gear&phone', 'phone', { suffix: 'gear' }],
  ['07-character', 'tab=skills', 'desk', { suffix: 'skills' }], ['07-character', 'tab=army', 'desk', { suffix: 'army' }],
  ['08-shop', '', 'desk'], ['08-shop', 'phone', 'phone'],
  ['09-titles', '', 'desk'], ['09-titles', 'phone', 'phone'], ['09-titles', 'view=grant', 'desk', { suffix: 'grant' }],
  ['10-tourney', '', 'desk'], ['10-tourney', 'phone', 'phone'], ['10-tourney', 'view=entry', 'desk', { suffix: 'entry' }], ['10-tourney', 'view=vs', 'desk', { suffix: 'vs' }], ['10-tourney', 'view=podium', 'desk', { suffix: 'podium' }], ['10-tourney', 'view=podium&phone', 'phone', { suffix: 'podium' }],
];
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist', '--allow-file-access-from-files'] });
const errs = [];
for (const [name, q, vp, o = {}] of JOBS) {
  if (filter && !name.includes(filter)) continue;
  const viewport = vp === 'phone' ? { width: 844, height: 390 } : { width: 1280, height: 720 };
  const ctx = await browser.newContext({ viewport, deviceScaleFactor: 1, hasTouch: vp === 'phone', reducedMotion: 'reduce' });
  const page = await ctx.newPage();
  page.on('pageerror', e => errs.push(`${name}?${q}: pageerror ${e.message}`));
  page.on('console', m => { if (m.type() === 'error') errs.push(`${name}?${q}: console ${m.text()}`); });
  const url = 'file://' + path.join(dir, name + '.html') + (q ? '?' + q : '');
  await page.goto(url);
  try { await page.waitForSelector('body[data-ready="1"]', { timeout: 20000 }); } catch (e) { errs.push(`${name}?${q}: not ready`); }
  await page.waitForTimeout(400);
  const out = path.join(dir, 'shots', `${name}${o.suffix ? '-' + o.suffix : ''}-${vp}.png`);
  await page.screenshot({ path: out, fullPage: !!o.full });
  console.log('shot', path.basename(out));
  await ctx.close();
}
console.log(JSON.stringify(errs, null, 1));
await browser.close();
