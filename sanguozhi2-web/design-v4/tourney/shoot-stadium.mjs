// TOURNAMENT §8：万国擂台白模截图（Chromium，软件 WebGL）→ design-v4/tourney/shots/*.png + shots/stats.json
//   node design-v4/tourney/shoot-stadium.mjs
import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
const HERE = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(HERE, 'shots');
fs.mkdirSync(out, { recursive: true });
const SHOTS = [
  ['aerial', 'shot=aerial&mood=golden'],
  ['duel-roman', 'shot=duel&face=roman&mood=golden'],
  ['duel-hua', 'shot=duel&face=hua&mood=noon'],
  ['duel-indic-phone', 'shot=duel&face=indic&mood=golden&w=844&h=390&phone=1'],
  ['walkon', 'shot=walkon&face=judges&mood=dusk'],
  ['night', 'shot=night&mood=night'],
  ['ceremony', 'shot=ceremony&face=podium&mood=night'],
];
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const stats = {};
for (const [name, q] of SHOTS) {
  const m = /w=(\d+)&h=(\d+)/.exec(q);
  const page = await browser.newPage({ viewport: { width: m ? +m[1] : 1600, height: m ? +m[2] : 900 } });
  const errs = [];
  page.on('pageerror', e => errs.push(e.message));
  page.on('console', msg => { if (msg.type() === 'error') errs.push(msg.text()); });
  await page.goto('file://' + path.join(HERE, 'stadium-blockout.html') + '?' + q);
  await page.waitForFunction(() => window.__st && (window.__st.ready || window.__st.error), null, { timeout: 120000 });
  const st = await page.evaluate(() => window.__st);
  if (st.error) errs.push(st.error);
  await page.screenshot({ path: path.join(out, name + '.png') });
  stats[name] = Object.assign({ errors: errs }, st.info);
  console.log(name, JSON.stringify(stats[name]));
  await page.close();
}
fs.writeFileSync(path.join(out, 'stats.json'), JSON.stringify(stats, null, 1));
await browser.close();
