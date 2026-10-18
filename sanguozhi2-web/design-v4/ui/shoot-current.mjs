import { chromium } from '/opt/node22/lib/node_modules/playwright/index.mjs';
const OUT = process.argv[2];
const vpName = process.argv[3] || 'desk';
const vp = vpName === 'phone' ? { width: 844, height: 390 } : { width: 1280, height: 720 };
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, hasTouch: vpName === 'phone', isMobile: false });
const page = await ctx.newPage();
const errs = [];
page.on('pageerror', e => errs.push('pageerror ' + e.message));
page.on('console', m => { if (m.type() === 'error') errs.push('console ' + m.text()); });
const url = 'file:///home/user/house-builder/sanguozhi2-web/index.html?seed=7';
await page.goto(url);
const wait = ms => page.waitForTimeout(ms);
await page.waitForSelector('.sg-title-menu .sg-btn', { timeout: 120000 });
await wait(2500);
await page.screenshot({ path: `${OUT}/${vpName}-01-title.png` });
// 新的征程
await page.click('.sg-title-menu .sg-btn.primary');
await wait(800);
await page.screenshot({ path: `${OUT}/${vpName}-02-scenario.png` });
// choose classic (first item)
await page.click('.sg-modals .sg-item >> nth=0');
await wait(1500);
// intro say -> click through
for (let i = 0; i < 6; i++) {
  const say = await page.$('.sg-say-modal');
  if (!say) break;
  if (i === 0) await page.screenshot({ path: `${OUT}/${vpName}-03-intro-say.png` });
  await say.click();
  await wait(700);
}
await wait(800);
await page.screenshot({ path: `${OUT}/${vpName}-04-pick-ruler.png` });
// pick 刘备 if present
const items = await page.$$('.sg-modals .sg-item');
let idx = 0;
for (let i = 0; i < items.length; i++) { const t = await items[i].innerText(); if (t.includes('刘备')) { idx = i; break; } }
await items[idx].click();
await wait(1500);
await page.screenshot({ path: `${OUT}/${vpName}-05-confirm.png` });
await page.click('.sg-modals .sg-btn.primary');
await wait(4000);
// dismiss any say dialogs at start
for (let i = 0; i < 8; i++) {
  const say = await page.$('.sg-say-modal');
  if (!say) break;
  if (i === 0) await page.screenshot({ path: `${OUT}/${vpName}-06-start-say.png` });
  await say.click(); await wait(700);
}
await wait(1500);
await page.screenshot({ path: `${OUT}/${vpName}-07-strategy.png` });
// select player's capital
await page.evaluate(() => { const g = SG.G; const c = g.cities[g.ruler(g.player).city]; SG.Game.strategy.selectCity(c.id); SG.Game.rig.focusMap(SG.mapPos(c).x, SG.mapPos(c).y, 45, true); });
await wait(2500);
await page.screenshot({ path: `${OUT}/${vpName}-08-citypanel.png` });
// open 开发
await page.click('.sg-cmdgrid .sg-btn[data-cmd="开发"]');
await wait(1000);
await page.screenshot({ path: `${OUT}/${vpName}-09-develop-dialog.png` });
await page.keyboard.press('Escape'); await wait(600);
await page.keyboard.press('Escape'); await wait(600);
// general details
const row = await page.$('.sg-genrow');
if (row) { await row.click(); await wait(1200); await page.screenshot({ path: `${OUT}/${vpName}-10-general.png` }); await page.keyboard.press('Escape'); await wait(700); }
// faction list
const btns = await page.$$('.sg-topbar-actions .sg-btn');
if (btns[0]) { await btns[0].click(); await wait(1000); await page.screenshot({ path: `${OUT}/${vpName}-11-factions.png` }); await page.keyboard.press('Escape'); await wait(600); }
// end month confirm
const end = await page.$('.sg-topbar-actions .sg-btn.primary');
if (end) { await end.click(); await wait(900); await page.screenshot({ path: `${OUT}/${vpName}-12-endmonth-confirm.png` }); }
console.log(JSON.stringify(errs));
await browser.close();
