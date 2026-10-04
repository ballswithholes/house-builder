'use strict';
/* ==========================================================================
   游戏主体：状态、移动、事件、城镇设施、菜单、存档
   ========================================================================== */

const SAVE_KEY = 'bawang_dalu_save_v1';
const STEP_WORLD = 0.17, STEP_IN = 0.15;

const Game = {
  S: null, world: null, ground: null,
  mode: 'loading', interior: null, interiors: {},
  player: { x: 0, y: 0, rx: 0, ry: 0, dir: 0, moving: false, walk: 0, t: 0, fx: 0, fy: 0, bump: 0 },
  cam: { x: 0, y: 0 }, ts: 48, dpr: 1, W: 0, H: 0, canvas: null, ctx: null,
  time: 0, last: 0, safeSteps: 0, titleCam: { x: 50, y: 20 },

  // ------------------------------------------------------------ 启动 --
  async boot() {
    UI.init(); setupPad();
    this.canvas = $('#view'); this.ctx = this.canvas.getContext('2d');
    addEventListener('resize', () => this.resize()); this.resize();
    await sleep(30);
    this.world = buildWorld();
    this.ground = paintGround(this.world);
    initClouds();
    $('#loading').classList.add('gone');
    this.mode = 'title';
    requestAnimationFrame(t => this.frame(t));
    this.title();
  },

  resize() {
    this.dpr = Math.min(2, devicePixelRatio || 1);
    const w = innerWidth, h = innerHeight;
    this.W = Math.round(w * this.dpr); this.H = Math.round(h * this.dpr);
    this.canvas.width = this.W; this.canvas.height = this.H;
    const css = clamp(Math.min(w / 15, h / 10.5), 34, 72);
    this.ts = Math.round(css * this.dpr);
    if (Battle.running) Battle.drawBG();
  },

  // ------------------------------------------------------------ 标题 --
  async title() {
    this.mode = 'title';
    Sfx.startMusic('title');
    const t = $('#title');
    t.classList.remove('hidden');
    let has = false;
    try { has = !!localStorage.getItem(SAVE_KEY); } catch (e) { }
    for (; ;) {
      const v = await UI.menu({
        cls: 'title-menu', cancel: false, start: has ? 1 : 0, items: [
          { label: '新的征程', value: 'new' },
          { label: '继续征程', value: 'load', disabled: !has },
          { label: '操作说明', value: 'help' },
        ],
      });
      Sfx.unlock(); Sfx.startMusic('title');
      if (v === 'help') { await this.help(); continue; }
      if (v === 'load' && this.load()) break;
      if (v === 'new') { await this.newGame(); break; }
    }
  },
  help() {
    return UI.panel(`<h3>操作说明</h3>
      <div class="help"><div><kbd>方向键</kbd> / <kbd>WASD</kbd> 移动、选择</div>
      <div><kbd>Enter</kbd> / <kbd>空格</kbd> / <kbd>Z</kbd> 确定、调查、对话</div>
      <div><kbd>Esc</kbd> / <kbd>X</kbd> 取消、打开军务菜单</div>
      <div>触屏：左下方向盘移动，右下 <b>A</b> 确定、<b>B</b> 取消 / 菜单</div>
      <hr><div>· 我军最多七员武将，前五员出阵，后两员为后备，前军兵力耗尽时自动替补。</div>
      <div>· 兵力即生命，在宿屋休息可完全恢复。行军消耗粮食，粮尽则士兵逃亡。</div>
      <div>· 军师决定可用的策略，策略值全军共用。</div>
      <div>· 「全军突击」可自动作战，按任意键停止。</div></div>`);
  },

  async newGame() {
    $('#title').classList.add('hidden');
    await this.intro();
    const S = this.S = {
      party: [], reserve: [], tactician: null, lv: 1, exp: 0, gold: 120, food: 300, tp: 0,
      items: { shangyao: 2 }, flags: {}, map: 'zhuojun', x: 0, y: 0, dir: 3, lastTown: 'zhuojun', time: 0,
    };
    for (const id of ['liubei', 'guanyu', 'zhangfei']) this.addGeneral(id, true);
    S.tp = Rules.maxTP(S);
    const loc = LOC.zhuojun;
    S.x = loc.x; S.y = loc.y;
    await this.enterInterior(loc, true);
    await UI.sayAll([['liubei', '二弟、三弟，涿郡太守刘焉正在招募义勇讨伐黄巾。'], ['zhangfei', '大哥，咱们这就去官府投军！'], ['guanyu', '官府就在城北，兄长请。']]);
    UI.toast('提示：按 <b>Esc</b> / <b>B</b> 打开军务菜单', 3500);
  },

  async intro() {
    const el = $('#intro');
    el.classList.remove('hidden');
    el.innerHTML = '<div class="intro-ink"></div><div class="intro-text"></div><div class="intro-skip">按确定键继续</div>';
    const txt = el.querySelector('.intro-text');
    for (const line of INTRO) {
      txt.innerHTML = `<p>${esc(line)}</p>`;
      txt.classList.remove('show'); void txt.offsetWidth; txt.classList.add('show');
      await new Promise(res => {
        const h = a => { if (a === 'ok' || a === 'cancel') { Input.remove(h); el.onclick = null; res(); } };
        el.onclick = () => h('ok');
        Input.push(h);
      });
    }
    await UI.fade(true, 500);
    el.classList.add('hidden');
  },

  // ------------------------------------------------------------ 存档 --
  save() {
    try {
      localStorage.setItem(SAVE_KEY, JSON.stringify(this.S));
      UI.toast('✦ 进度已记录');
      return true;
    } catch (e) { UI.toast('记录失败'); return false; }
  },
  load() {
    try {
      const S = JSON.parse(localStorage.getItem(SAVE_KEY));
      if (!S || !S.party) return false;
      this.S = S;
      $('#title').classList.add('hidden');
      if (S.map === 'world') this.enterWorld(S.x, S.y, true);
      else this.enterInterior(LOC[S.map], true);
      return true;
    } catch (e) { UI.toast('存档损坏'); return false; }
  },

  // ------------------------------------------------------------ 武将 --
  hasGeneral(id) { return this.S.party.some(m => m.id === id) || this.S.reserve.some(m => m.id === id); },
  addGeneral(id, silent) {
    const g = GENERALS[id];
    const m = { id, sol: Rules.maxSol(id, this.S.lv), weapon: g.weapon || null, armor: g.armor || null };
    if (this.S.party.length < 7) this.S.party.push(m); else this.S.reserve.push(m);
    if (!silent) this.hud();
    return m;
  },
  leader() { const S = this.S; if (!S) return 'liubei'; const m = S.party.find(m => m.sol > 0) || S.party[0]; return m.id; },
  tactician() {
    const S = this.S; if (!S) return null;
    if (S.tactician && S.party.some(m => m.id === S.tactician)) return S.tactician;
    let best = null;
    for (const m of S.party) if (!best || GENERALS[m.id].int > GENERALS[best].int) best = m.id;
    return best;
  },

  // ------------------------------------------------------------ 地图切换 --
  enterWorld(x, y, instant) {
    const S = this.S;
    S.map = 'world'; this.interior = null;
    this.place(x, y);
    this.mode = 'world';
    Sfx.startMusic('field');
    this.hud();
    UI.fade(false, instant ? 400 : 350);
  },
  async enterInterior(loc, instant) {
    const S = this.S;
    if (!instant) await UI.fade(true, 250);
    let m = this.interiors[loc.id];
    if (!m) m = this.interiors[loc.id] = genInterior(loc);
    this.interior = m;
    S.map = loc.id;
    this.place(m.entry.x, m.entry.y);
    this.player.dir = 3;
    this.mode = 'world';
    if (m.kind !== 'dungeon') S.lastTown = loc.id;
    Sfx.startMusic(m.kind === 'dungeon' ? 'dungeon' : 'town');
    this.hud();
    await UI.fade(false, 350);
    UI.banner(loc.name, m.kind === 'dungeon' ? (this.S.flags[BOSSES[loc.boss].flag] ? '已平定' : '敌军据点') : '');
  },
  async leaveInterior() {
    const loc = this.interior.loc;
    Sfx.play('door');
    await UI.fade(true, 250);
    this.enterWorld(loc.x, loc.y);
    this.player.dir = 0;
  },
  place(x, y) {
    const p = this.player;
    p.x = p.rx = x; p.y = p.ry = y; p.moving = false;
    this.S.x = x; this.S.y = y;
    const [tx, ty] = this.camTarget();
    this.cam.x = tx; this.cam.y = ty;
  },
  camTarget() {
    const p = this.player, vw = this.W / this.ts, vh = this.H / this.ts;
    let x = p.rx + 0.5, y = p.ry + 0.5;
    const mw = this.interior ? this.interior.w : WW, mh = this.interior ? this.interior.h : WH;
    x = mw <= vw ? mw / 2 : clamp(x, vw / 2, mw - vw / 2);
    y = mh <= vh ? mh / 2 : clamp(y, vh / 2, mh - vh / 2);
    return [x, y];
  },

  // ------------------------------------------------------------ 主循环 --
  frame(now) {
    const dt = Math.min(0.05, (now - (this.last || now)) / 1000);
    this.last = now; this.time += dt;
    if (this.S && this.mode !== 'title') this.S.time += dt;
    this.update(dt);
    this.draw();
    requestAnimationFrame(t => this.frame(t));
  },

  update(dt) {
    const p = this.player;
    if (p.bump > 0) p.bump -= dt;
    if (this.mode === 'world') {
      if (p.moving) {
        p.t += dt / (this.interior ? STEP_IN : STEP_WORLD);
        p.walk += dt * 2.6;
        const k = Math.min(1, p.t);
        p.rx = lerp(p.fx, p.x, k); p.ry = lerp(p.fy, p.y, k);
        if (p.t >= 1) { p.moving = false; p.rx = p.x; p.ry = p.y; this.arrive(); }
      }
      if (!p.moving && !Input.busy && this.mode === 'world') {
        const h = Input.held;
        let d = null;
        if (Input.lastDir && h[Input.lastDir]) d = Input.lastDir;
        else d = ['up', 'down', 'left', 'right'].find(k => h[k]) || null;
        if (d) this.tryMove(d);
      }
      if (this.interior) this.updateNpcs(dt);
      const [tx, ty] = this.camTarget();
      const k = Math.min(1, dt * 9);
      this.cam.x += (tx - this.cam.x) * k; this.cam.y += (ty - this.cam.y) * k;
    }
    if (this.mode === 'title') {
      this.titleCam.x += dt * 0.6;
      if (this.titleCam.x > 100) this.titleCam.x = 12;
      this.titleCam.y = 30 + Math.sin(this.time * 0.05) * 14;
    }
  },

  draw() {
    const c = this.ctx;
    if (this.mode === 'loading') return;
    if (this.mode === 'title' || !this.S) {
      renderWorld(c, this.W, this.H, Math.round(this.ts * 0.85), this.titleCam, this.time, { ry: -100, rx: -100 }, {});
      return;
    }
    if (this.mode === 'battle') return;
    if (this.interior) renderInterior(c, this.W, this.H, this.ts, this.cam, this.time, this.player, this.interior, this.S.flags);
    else renderWorld(c, this.W, this.H, this.ts, this.cam, this.time, this.player, this.S.flags);
    // 暗角
    const g = c.createRadialGradient(this.W / 2, this.H / 2, Math.min(this.W, this.H) * 0.35, this.W / 2, this.H / 2, Math.max(this.W, this.H) * 0.72);
    g.addColorStop(0, 'rgba(0,0,0,0)'); g.addColorStop(1, 'rgba(10,8,20,.42)');
    c.fillStyle = g; c.fillRect(0, 0, this.W, this.H);
  },

  // ------------------------------------------------------------ 移动 --
  dirVec(d) { return { up: [0, -1, 3], down: [0, 1, 0], left: [-1, 0, 1], right: [1, 0, 2] }[d]; },
  tryMove(d) {
    const p = this.player;
    const [dx, dy, dir] = this.dirVec(d);
    p.dir = dir;
    const nx = p.x + dx, ny = p.y + dy;
    if (this.walkable(nx, ny)) {
      p.fx = p.x; p.fy = p.y; p.x = nx; p.y = ny; p.t = 0; p.moving = true;
    } else if (p.bump <= 0) {
      p.bump = 0.35;
      this.bumpInto(nx, ny);
    }
  },
  walkable(x, y) {
    const S = this.S;
    if (this.interior) {
      const m = this.interior;
      if (x < 0 || y < 0 || x >= m.w || y >= m.h) return false;
      if (!IWALK.has(m.t[y * m.w + x])) return false;
      if (m.npcs.some(n => n.x === x && n.y === y)) return false;
      if (m.boss && !S.flags[BOSSES[m.boss.id].flag] && m.boss.x === x && m.boss.y === y) return false;
      return true;
    }
    if (x < 0 || y < 0 || x >= WW || y >= WH) return false;
    const i = y * WW + x, v = this.world.t[i], loc = this.world.locAt.get(i);
    if (loc) {
      if (loc.kind === 'gate') return !!S.flags[loc.need];
      if (loc.kind === 'bossgate') return !!S.flags[BOSSES[loc.boss].flag];
      if (loc.need && !S.flags[loc.need]) return false;
      return true;
    }
    return v === T.SAND || v === T.GRASS || v === T.FOREST || v === T.HILL || v === T.ROAD || v === T.BRIDGE;
  },

  async bumpInto(x, y) {
    if (this.interior) {
      const m = this.interior;
      const n = m.npcs.find(n => n.x === x && n.y === y);
      if (n) return this.talk(n);
      if (m.boss && m.boss.x === x && m.boss.y === y && !this.S.flags[BOSSES[m.boss.id].flag]) return this.bossEvent(m.boss.id);
      const ch = m.chests.find(c => c.x === x && c.y === y);
      if (ch) return this.openChest(ch);
      Sfx.play('bump');
      return;
    }
    const loc = this.world.locAt.get(y * WW + x);
    if (!loc) { Sfx.play('bump'); return; }
    if (loc.kind === 'bossgate') return this.bossEvent(loc.boss, loc);
    if (loc.block) { this.lock(); await UI.sayAll(loc.block); this.unlock(); }
  },
  lock() { this.mode = 'busy'; Input.clearHeld(); },
  unlock() { if (this.mode === 'busy') this.mode = 'world'; },

  interact() {
    const p = this.player;
    const [dx, dy] = [[0, 1], [-1, 0], [1, 0], [0, -1]][p.dir];
    this.bumpInto(p.x + dx, p.y + dy);
  },

  async arrive() {
    const S = this.S, p = this.player;
    S.x = p.x; S.y = p.y;
    if (this.interior) {
      const m = this.interior, v = m.t[p.y * m.w + p.x];
      if (v === IT.EXIT) return this.leaveInterior();
      if (v === IT.DOOR) {
        const b = m.buildings.find(b => p.x === b.x + 2 && p.y === b.y + 3);
        this.lock();
        Sfx.play('door');
        if (b) await this.facility(b.fac);
        // 退出门外
        p.y += 1; p.ry = p.y; p.dir = 0; S.y = p.y;
        this.unlock();
        return;
      }
      if (m.kind === 'dungeon' && v !== IT.CARPET) await this.maybeEncounter(m.loc.enc, m.theme);
      return;
    }
    const loc = this.world.locAt.get(p.y * WW + p.x);
    if (loc && (loc.kind === 'town' || loc.kind === 'village' || loc.kind === 'fort')) { this.lock(); await this.enterInterior(loc); this.unlock(); return; }
    // 粮食
    this.steps = (this.steps || 0) + 1;
    if (this.steps % 4 === 0) {
      const total = S.party.reduce((s, m) => s + Math.max(0, m.sol), 0);
      S.food -= Math.ceil(total / 600);
      if (S.food <= 0) {
        S.food = 0;
        for (const m of S.party) if (m.sol > 1) m.sol = Math.max(1, m.sol - Math.ceil(Rules.maxSol(m.id, S.lv) * 0.03));
        UI.toast('<b style="color:#ff8a7a">粮草断绝，士兵纷纷逃亡！</b>');
      } else if (S.food < 60 && this.steps % 24 === 0) UI.toast('粮食所剩无几，请尽快补充');
      this.hud();
    }
    if (loc) return;
    const v = this.world.t[p.y * WW + p.x];
    await this.maybeEncounter(zoneAt(p.x, p.y), v);
  },

  async maybeEncounter(zone, terrain) {
    this.safeSteps++;
    let rate;
    if (typeof terrain === 'string') rate = 1 / 16;
    else rate = { [T.GRASS]: 1 / 22, [T.SAND]: 1 / 22, [T.FOREST]: 1 / 14, [T.HILL]: 1 / 14, [T.ROAD]: 1 / 40, [T.BRIDGE]: 0 }[terrain] || 0;
    if (this.safeSteps < 5 || !chance(rate)) return;
    this.safeSteps = 0;
    const ids = weighted(ENCOUNTERS[zone]);
    let bg = 'plain';
    if (typeof terrain === 'string') bg = terrain;
    else if (terrain === T.FOREST) bg = 'forest';
    else if (terrain === T.HILL) bg = 'hill';
    else if (terrain === T.SAND) bg = 'sand';
    else if (zone[0] === 'D') bg = 'arid';
    await this.battle(ids, { bg });
  },

  // ------------------------------------------------------------ 战斗 --
  async battle(ids, opts = {}) {
    this.lock();
    this.mode = 'battle';
    $('#hud').classList.add('hidden');
    document.body.classList.add('in-battle');
    // 转场
    const sw = $('#swirl'); sw.classList.remove('go'); void sw.offsetWidth; sw.classList.add('go');
    await sleep(520);
    const r = await Battle.run(ids, opts);
    document.body.classList.remove('in-battle');
    sw.classList.remove('go');
    this.mode = 'busy';
    $('#hud').classList.remove('hidden');
    this.hud();
    Sfx.startMusic(this.interior ? (this.interior.kind === 'dungeon' ? 'dungeon' : 'town') : 'field');
    if (r === 'lose') { await this.gameOver(); return r; }
    await UI.fade(false, 300);
    this.unlock();
    return r;
  },

  async gameOver() {
    const S = this.S;
    $('#hud').classList.add('hidden');
    await UI.fade(true, 10);
    await UI.say('胜败乃兵家常事。\n刘备收拾残部，退回城中休整……');
    S.gold = Math.floor(S.gold / 2);
    for (const m of S.party.concat(S.reserve)) m.sol = Rules.maxSol(m.id, S.lv);
    S.tp = Rules.maxTP(S);
    const loc = LOC[S.lastTown] || LOC.zhuojun;
    this.interior = null;
    await this.enterInterior(loc, true);
    UI.toast('军资折损一半');
  },

  async bossEvent(id, gateLoc) {
    const b = BOSSES[id];
    if (this.S.flags[b.flag]) return;
    this.lock();
    await UI.sayAll(b.pre);
    const r = await this.battle(b.enemies, { boss: true, bg: b.bg });
    if (r === 'win') {
      this.S.flags[b.flag] = true;
      this.lock();
      await UI.sayAll(b.post);
      if (b.flag === 'f_dongzhuo') { await this.ending(); return; }
      UI.toast('目标：' + objectiveText(this.S.flags), 3200);
      this.hud();
    }
    this.unlock();
  },

  async ending() {
    const S = this.S;
    await UI.fade(true, 600);
    const el = $('#intro');
    el.classList.remove('hidden');
    const h = Math.floor(S.time / 3600), mi = Math.floor(S.time / 60) % 60;
    el.innerHTML = `<div class="intro-ink"></div><div class="ending"><div class="end-t">霸王的大陆</div><div class="end-s">第一部 · 讨董篇 完</div>
      <div class="end-stats"><div>全军等级 <b>Lv ${S.lv}</b></div><div>麾下武将 <b>${S.party.length + S.reserve.length}</b> 员</div><div>征战时间 <b>${h}时${mi}分</b></div></div>
      <div class="end-q">群雄并起，天下三分之势渐成。<br>刘玄德的霸业，才刚刚开始……</div><div class="intro-skip">按确定键继续</div></div>`;
    await UI.fade(false, 800);
    await new Promise(res => { const hh = a => { if (a === 'ok' || a === 'cancel') { Input.remove(hh); res(); } }; Input.push(hh); el.onclick = () => hh('ok'); });
    el.classList.add('hidden'); el.onclick = null;
    S.flags.f_ending = true;
    const v = await UI.menu({ title: '征程已毕', cancel: false, items: [{ label: '继续游历天下', value: 'go' }, { label: '记录并返回标题', value: 'title' }] });
    if (v === 'title') { this.save(); location.reload(); return; }
    this.unlock();
  },

  // ------------------------------------------------------------ 城镇 NPC --
  updateNpcs(dt) {
    const m = this.interior, p = this.player;
    for (const n of m.npcs) {
      if (n.moving) {
        n.t += dt / 0.32; n.walk += dt * 2.2;
        const k = Math.min(1, n.t);
        n.rx = lerp(n.fx, n.x, k); n.ry = lerp(n.fy, n.y, k);
        if (n.t >= 1) { n.moving = false; n.rx = n.x; n.ry = n.y; }
        continue;
      }
      if (Input.busy || this.mode !== 'world') continue;
      n.next -= dt;
      if (n.next > 0) continue;
      n.next = 1.2 + Math.random() * 2.8;
      const [dx, dy, dir] = pick([[0, 1, 0], [-1, 0, 1], [1, 0, 2], [0, -1, 3]]);
      n.dir = dir;
      const nx = n.x + dx, ny = n.y + dy;
      if (Math.abs(nx - n.home[0]) > 3 || Math.abs(ny - n.home[1]) > 3) continue;
      const v = m.t[ny * m.w + nx];
      if (!IWALK.has(v) || v === IT.DOOR || v === IT.EXIT) continue;
      if ((nx === p.x && ny === p.y) || m.npcs.some(o => o !== n && o.x === nx && o.y === ny)) continue;
      n.fx = n.x; n.fy = n.y; n.x = nx; n.y = ny; n.t = 0; n.moving = true;
    }
  },
  async talk(n) {
    this.lock();
    const p = this.player;
    n.dir = [3, 2, 1, 0][p.dir];
    await UI.say(n.line, { name: '百姓', glyph: '民', color: n.look.body });
    this.unlock();
  },
  async openChest(ch) {
    const S = this.S, key = 'chest_' + ch.id;
    this.lock();
    if (S.flags[key]) { await UI.say('宝箱是空的。'); this.unlock(); return; }
    S.flags[key] = true;
    Sfx.play('coin');
    const loot = chestLoot(ch.id, this.interior.loc.zone);
    if (loot.gold) { S.gold += loot.gold; await UI.say(`打开宝箱，获得了 ${loot.gold} 金！`); }
    else { S.items[loot.item] = (S.items[loot.item] || 0) + loot.n; await UI.say(`打开宝箱，获得了 ${ITEMS[loot.item].name}×${loot.n}！`); }
    this.hud();
    this.unlock();
  },

  // ------------------------------------------------------------ 设施 --
  async facility(fac) {
    const loc = this.interior.loc;
    const S = this.S;
    switch (fac) {
      case 'inn': return this.inn();
      case 'weapon': return this.equipShop('weapon', loc.shop.weapon || []);
      case 'armor': return this.equipShop('armor', loc.shop.armor || []);
      case 'item': return this.itemShop(loc.shop.item || []);
      case 'food': return this.foodShop();
      case 'tavern': return this.tavern(loc);
      case 'gov': return this.gov(loc);
    }
  },

  async inn() {
    const S = this.S;
    const dead = S.party.concat(S.reserve).filter(m => m.sol <= 0).length;
    const price = Math.ceil((8 + S.lv * 5) * (1 + dead * 0.5));
    for (; ;) {
      const v = await UI.menu({
        title: '宿屋', info: `掌柜：客官一路辛苦了。住一晚 <b>${price}</b> 金，兵马都能好好休整。`, items: [
          { label: '住宿休整', right: price + ' 金', value: 'rest', disabled: S.gold < price },
          { label: '记录进度', value: 'save' },
          { label: '离开', value: null },
        ],
      });
      if (v === 'rest') {
        S.gold -= price;
        await UI.fade(true, 400);
        for (const m of S.party.concat(S.reserve)) m.sol = Rules.maxSol(m.id, S.lv);
        S.tp = Rules.maxTP(S);
        Sfx.play('heal');
        this.hud();
        await sleep(500);
        await UI.fade(false, 400);
        await UI.say('一夜安歇，全军兵力与策略值完全恢复了！');
        if (await UI.confirm('是否记录进度？', '记录', '不必')) this.save();
        return;
      }
      if (v === 'save') { this.save(); continue; }
      return;
    }
  },

  async equipShop(kind, list) {
    const S = this.S, DB = kind === 'weapon' ? WEAPONS : ARMORS, stat = kind === 'weapon' ? 'atk' : 'def';
    const title = kind === 'weapon' ? '武器店' : '防具店';
    let start = 0;
    for (; ;) {
      const k = await UI.menu({
        title, info: `店主：上好的${kind === 'weapon' ? '兵器' : '甲胄'}，看看吧！　<span class="gold">持有 ${S.gold} 金</span>`, start,
        items: list.map(k => ({ label: DB[k].name, right: `${kind === 'weapon' ? '攻' : '防'}+${DB[k][stat]}　${DB[k].price}金`, value: k, disabled: DB[k].price > S.gold })),
      });
      if (!k) return;
      start = list.indexOf(k);
      const it = DB[k];
      const who = await UI.menu({
        title: `谁来装备${it.name}？`, cls: 'wide',
        items: S.party.concat(S.reserve).map(m => {
          const cur = m[kind] ? DB[m[kind]] : null;
          const diff = it[stat] - (cur ? cur[stat] : 0);
          return { label: `${GENERALS[m.id].name}<small>${cur ? cur.name : '无'}</small>`, right: `<span class="${diff > 0 ? 'up' : diff < 0 ? 'down' : ''}">${diff > 0 ? '▲' : diff < 0 ? '▼' : '='}${Math.abs(diff)}</span>`, value: m };
        }),
      });
      if (!who) continue;
      const old = who[kind] ? DB[who[kind]] : null;
      const refund = old && !old.unique ? Math.floor(old.price / 2) : 0;
      const ok = await UI.confirm(`购买${it.name}（${it.price}金）给${GENERALS[who.id].name}？` + (old ? `<br><small>原有的${old.name}将${refund ? `以 ${refund} 金卖出` : '被替换'}</small>` : ''), '购买', '算了');
      if (!ok) continue;
      S.gold -= it.price - refund;
      who[kind] = k;
      Sfx.play('coin');
      this.hud();
      UI.toast(`${GENERALS[who.id].name}装备了${it.name}`);
    }
  },

  async itemShop(list) {
    const S = this.S;
    let start = 0;
    for (; ;) {
      const k = await UI.menu({
        title: '道具店', info: `店主：行军打仗，药品可不能少。　<span class="gold">持有 ${S.gold} 金</span>`, start, cls: 'wide',
        items: list.map(k => ({ label: ITEMS[k].name + `<small>持有 ${S.items[k] || 0}</small>`, right: ITEMS[k].price + ' 金', value: k, desc: ITEMS[k].desc, disabled: ITEMS[k].price > S.gold })),
      });
      if (!k) return;
      start = list.indexOf(k);
      const it = ITEMS[k];
      const n = await UI.menu({
        title: `购买${it.name}`, cls: 'small',
        items: [1, 3, 5, 10].map(n => ({ label: `${n} 个`, right: `${n * it.price} 金`, value: n, disabled: n * it.price > S.gold || (S.items[k] || 0) + n > 99 })),
      });
      if (!n) continue;
      S.gold -= n * it.price; S.items[k] = (S.items[k] || 0) + n;
      Sfx.play('coin'); this.hud();
      UI.toast(`购买了 ${it.name}×${n}`);
    }
  },

  async foodShop() {
    const S = this.S;
    for (; ;) {
      const v = await UI.menu({
        title: '粮店', info: `店主：兵马未动，粮草先行！　<span class="gold">持有 ${S.gold} 金 · 粮 ${S.food}</span>`,
        items: [[100, 12], [500, 55], [1000, 100], [3000, 280]].map(([n, p]) => ({ label: `军粮 ${n}`, right: p + ' 金', value: [n, p], disabled: p > S.gold || S.food + n > 9999 })),
      });
      if (!v) return;
      S.gold -= v[1]; S.food += v[0];
      Sfx.play('coin'); this.hud();
      UI.toast(`购入军粮 ${v[0]}`);
    }
  },

  async tavern(loc) {
    const S = this.S;
    const rec = TAVERN_RECRUIT[loc.id];
    if (rec && !this.hasGeneral(rec.id) && (!rec.need || S.flags[rec.need])) {
      await UI.sayAll(rec.lines);
      if (await UI.confirm(`让${GENERALS[rec.id].name}加入？`, '欢迎加入', '改日再说')) {
        this.addGeneral(rec.id);
        Sfx.play('lvup');
        await UI.say(`${GENERALS[rec.id].name}加入了我军！` + (S.party.length >= 7 && S.party[6].id !== rec.id ? '\n（队伍已满，编入预备队，可在「阵容」中调整）' : ''));
        if (rec.gift) { S.gold += rec.gift; await UI.say(`${GENERALS[rec.id].name}献上了军资 ${rec.gift} 金！`); }
        this.hud();
      }
      return;
    }
    const tips = [
      `听说眼下要紧的事是——${objectiveText(S.flags)}。`,
      '在野外遇到的黄巾将领，打败之后说不定会归顺于你。',
      '军师的智力越高、全军等级越高，能使用的策略就越强。',
      '前军五员出战，若有人兵力耗尽，后备武将会立即顶上。',
    ];
    await UI.say('酒保：客官，来壶好酒吧！\n' + pick(tips), { name: '酒保', glyph: '酒', color: '#8a2f2a' });
  },

  async gov(loc) {
    const S = this.S, g = GOV_LINES[loc.id], key = 'f_gov_' + loc.id;
    if (g && !S.flags[key]) {
      await UI.sayAll(g.first);
      S.flags[key] = true;
      if (g.gift.gold) S.gold += g.gift.gold;
      if (g.gift.items) for (const k in g.gift.items) S.items[k] = (S.items[k] || 0) + g.gift.items[k];
      Sfx.play('coin'); this.hud();
      UI.toast('目标：' + objectiveText(S.flags), 3200);
      return;
    }
    await UI.say(`官吏：当务之急，是${objectiveText(S.flags)}。`, { name: '官吏', glyph: '官', color: '#28324a' });
  },

  // ------------------------------------------------------------ 军务菜单 --
  onAction(a) {
    if (this.mode === 'battle') {
      if (Battle.auto && !Battle.stopAuto) { Battle.stopAuto = true; UI.toast('本回合结束后停止突击'); }
      return;
    }
    if (this.mode !== 'world' || this.player.moving) return;
    if (a === 'ok') this.interact();
    else if (a === 'cancel' || a === 'menu') this.fieldMenu();
  },

  async fieldMenu() {
    this.lock();
    const S = this.S;
    let start = 0;
    for (; ;) {
      const inTown = this.interior && this.interior.kind !== 'dungeon';
      const v = await UI.menu({
        title: '军务', cls: 'field', start, info: this.summaryHtml(),
        items: [
          { label: '部队', value: 'status', icon: '旗' },
          { label: '道具', value: 'items', icon: '囊' },
          { label: '策略', value: 'tactics', icon: '策' },
          { label: '阵容', value: 'order', icon: '阵' },
          { label: '军师', value: 'advisor', icon: '师' },
          { label: '目标', value: 'goal', icon: '标' },
          { label: '记录', value: 'save', icon: '录', disabled: !inTown, desc: inTown ? '' : '只能在城镇中记录进度' },
          { label: '设置', value: 'settings', icon: '设' },
        ],
      });
      if (v === null) break;
      start = ['status', 'items', 'tactics', 'order', 'advisor', 'goal', 'save', 'settings'].indexOf(v);
      if (v === 'status') await this.statusMenu();
      if (v === 'items') await this.useItemMenu();
      if (v === 'tactics') await this.useTacticMenu();
      if (v === 'order') await this.orderMenu();
      if (v === 'advisor') await this.advisorMenu();
      if (v === 'goal') await UI.panel(`<h3>当前目标</h3><p class="goal">${objectiveText(S.flags)}</p>`);
      if (v === 'save') this.save();
      if (v === 'settings') { if (await this.settingsMenu() === 'title') return; }
      this.hud();
    }
    this.unlock();
  },

  summaryHtml() {
    const S = this.S;
    const need = Rules.nextExp(S.lv);
    return `<div class="sum"><span>Lv <b>${S.lv}</b></span><span>经验 <b>${S.exp}</b>/${need}</span><span>金 <b>${S.gold}</b></span><span>粮 <b>${S.food}</b></span><span>策略 <b>${S.tp}</b>/${Rules.maxTP(S)}</span></div>`;
  },

  memberRow(m, tag) {
    const S = this.S, g = GENERALS[m.id], max = Rules.maxSol(m.id, S.lv);
    return `<span class="mrow">${UI.medal(m.id, 30)}<span class="mname">${g.name}${tag ? `<em>${tag}</em>` : ''}</span>` +
      `<span class="msol ${m.sol <= 0 ? 'dead' : ''}">${m.sol <= 0 ? '阵亡' : m.sol + '/' + max}</span></span>`;
  },
  tagOf(m) {
    const S = this.S;
    const i = S.party.indexOf(m);
    const t = i < 0 ? '预备' : i < 5 ? '前军' : '后备';
    return t + (this.tactician() === m.id ? '·军师' : '');
  },

  async statusMenu() {
    const S = this.S;
    let start = 0;
    for (; ;) {
      const all = S.party.concat(S.reserve);
      const m = await UI.menu({ title: '部队', cls: 'wide', start, items: all.map(m => ({ label: this.memberRow(m, this.tagOf(m)), value: m })) });
      if (!m) return;
      start = all.indexOf(m);
      const g = GENERALS[m.id], max = Rules.maxSol(m.id, S.lv);
      const u = { str: g.str, lv: S.lv, watk: Rules.watk(m), adef: Rules.adef(m) };
      const bar = (v, c) => `<div class="sbar"><i style="width:${v}%;background:${c}"></i></div>`;
      await UI.panel(`<div class="gstat">${UI.medal(m.id, 84)}<div><div class="g-name">${g.name}<small>字${g.zi}</small></div><div class="g-tag">${this.tagOf(m)}</div></div></div>
        <div class="g-grid">
          <div>兵力</div><div><b>${Math.max(0, m.sol)}</b> / ${max}${bar(Math.max(0, m.sol) / max * 100, '#5fd38a')}</div>
          <div>武力</div><div><b>${g.str}</b>${bar(g.str, '#ff8a5c')}</div>
          <div>智力</div><div><b>${g.int}</b>${bar(g.int, '#6cb6ff')}</div>
          <div>速度</div><div><b>${g.agi}</b>${bar(g.agi, '#ffd35c')}</div>
          <div>武器</div><div>${m.weapon ? WEAPONS[m.weapon].name + ` <small>攻+${WEAPONS[m.weapon].atk}</small>` : '无'}</div>
          <div>防具</div><div>${m.armor ? ARMORS[m.armor].name + ` <small>防+${ARMORS[m.armor].def}</small>` : '无'}</div>
          <div>攻击力</div><div><b>${Math.round(Rules.atkPow(u))}</b></div>
          <div>防御力</div><div><b>${Math.round(Rules.defPow(u))}</b></div>
        </div>`, 'gpanel');
    }
  },

  async pickMember(title, filter) {
    const S = this.S;
    const all = S.party.concat(S.reserve).filter(filter || (() => true));
    if (!all.length) { UI.toast('没有合适的武将'); return null; }
    return UI.menu({ title, cls: 'wide', items: all.map(m => ({ label: this.memberRow(m, this.tagOf(m)), value: m })) });
  },

  async useItemMenu() {
    const S = this.S;
    for (; ;) {
      const keys = Object.keys(S.items).filter(k => S.items[k] > 0);
      if (!keys.length) { await UI.say('没有任何道具。'); return; }
      const k = await UI.menu({ title: '道具', cls: 'wide', items: keys.map(k => ({ label: ITEMS[k].name, right: '×' + S.items[k], value: k, desc: ITEMS[k].desc, disabled: !!ITEMS[k].battleOnly })) });
      if (!k) return;
      const it = ITEMS[k];
      if (it.type === 'heal' && it.target === 'ally') {
        const m = await this.pickMember('给谁使用？', m => m.sol > 0 && m.sol < Rules.maxSol(m.id, S.lv));
        if (!m) continue;
        m.sol = Math.min(Rules.maxSol(m.id, S.lv), m.sol + it.pow);
      } else if (it.type === 'heal') {
        for (const m of S.party.concat(S.reserve)) if (m.sol > 0) m.sol = Math.min(Rules.maxSol(m.id, S.lv), m.sol + it.pow);
      } else if (it.type === 'revive') {
        const m = await this.pickMember('复活哪位武将？', m => m.sol <= 0);
        if (!m) continue;
        m.sol = Math.round(Rules.maxSol(m.id, S.lv) * it.pow);
      } else if (it.type === 'tp') {
        if (S.tp >= Rules.maxTP(S)) { UI.toast('策略值已满'); continue; }
        S.tp = Math.min(Rules.maxTP(S), S.tp + it.pow);
      }
      S.items[k]--;
      Sfx.play('heal');
      UI.toast(`使用了${it.name}`);
    }
  },

  async useTacticMenu() {
    const S = this.S;
    const known = Rules.known(S);
    if (!known.length) { await UI.say('军师尚未掌握任何策略。'); return; }
    for (; ;) {
      const k = await UI.menu({
        title: `策略 · ${GENERALS[this.tactician()].name}　策略值 ${S.tp}/${Rules.maxTP(S)}`, cls: 'wide',
        items: Rules.known(S).map(k => ({ label: TACTICS[k].name, right: TACTICS[k].tp, value: k, desc: TACTICS[k].desc, disabled: TACTICS[k].type !== 'heal' || TACTICS[k].tp > S.tp })),
      });
      if (!k) return;
      const tac = TACTICS[k];
      const caster = { int: GENERALS[this.tactician()].int, lv: S.lv };
      if (tac.target === 'ally') {
        const m = await this.pickMember('补给哪位武将？', m => m.sol > 0 && m.sol < Rules.maxSol(m.id, S.lv));
        if (!m) continue;
        m.sol = Math.min(Rules.maxSol(m.id, S.lv), m.sol + Math.round(Rules.tacPow(tac, caster)));
      } else {
        const amt = Math.round(Rules.tacPow(tac, caster));
        for (const m of S.party) if (m.sol > 0) m.sol = Math.min(Rules.maxSol(m.id, S.lv), m.sol + amt);
      }
      S.tp -= tac.tp;
      Sfx.play('heal');
      UI.toast(`施展了${tac.name}`);
    }
  },

  async orderMenu() {
    const S = this.S;
    for (; ;) {
      const all = S.party.concat(S.reserve);
      const a = await UI.menu({ title: '阵容 · 选择要调动的武将', cls: 'wide', info: '前五员为前军出战，第六、七员为后备，其余为预备队（不参战）。', items: all.map(m => ({ label: this.memberRow(m, this.tagOf(m)), value: m })) });
      if (!a) return;
      const b = await UI.menu({ title: `与谁交换位置？`, cls: 'wide', items: all.filter(m => m !== a).map(m => ({ label: this.memberRow(m, this.tagOf(m)), value: m })) });
      if (!b) continue;
      const ia = all.indexOf(a), ib = all.indexOf(b);
      all[ia] = b; all[ib] = a;
      S.party = all.slice(0, 7); S.reserve = all.slice(7);
      if (!S.party.some(m => m.sol > 0)) { all[ia] = a; all[ib] = b; S.party = all.slice(0, 7); S.reserve = all.slice(7); UI.toast('前军中至少要有一员可战之将'); continue; }
      S.tp = Math.min(S.tp, Rules.maxTP(S));
      Sfx.play('ok');
    }
  },

  async advisorMenu() {
    const S = this.S;
    const cur = this.tactician();
    const m = await UI.menu({
      title: '任命军师', cls: 'wide', info: '军师的智力决定可习得的策略与策略值上限。军师须在队伍（前七员）之中。',
      items: S.party.map(m => {
        const g = GENERALS[m.id];
        const tmp = S.tactician; S.tactician = m.id; const tp = Rules.maxTP(S); const n = Rules.known(S).length; S.tactician = tmp;
        return { label: `${g.name}${m.id === cur ? '<em>现任</em>' : ''}`, right: `智${g.int}　策略值${tp}　${n}种`, value: m.id };
      }),
    });
    if (!m) return;
    S.tactician = m;
    S.tp = Math.min(S.tp, Rules.maxTP(S));
    UI.toast(`任命${GENERALS[m].name}为军师`);
  },

  async settingsMenu() {
    for (; ;) {
      const v = await UI.menu({
        title: '设置', items: [
          { label: '音效', right: Sfx.on ? '开' : '关', value: 'sfx' },
          { label: '音乐', right: Sfx.musicOn ? '开' : '关', value: 'music' },
          { label: '返回标题', value: 'title' },
        ],
      });
      if (!v) return;
      if (v === 'sfx') Sfx.on = !Sfx.on;
      if (v === 'music') { Sfx.musicOn = !Sfx.musicOn; const k = Sfx.musicKind; Sfx.stopMusic(); if (Sfx.musicOn) Sfx.startMusic(k || 'field'); else Sfx.musicKind = k; }
      if (v === 'title') { if (await UI.confirm('返回标题画面？未记录的进度将会丢失。')) { location.reload(); return 'title'; } }
    }
  },

  // ------------------------------------------------------------ HUD --
  hud() {
    const S = this.S; if (!S) return;
    const hud = $('#hud');
    hud.classList.remove('hidden');
    const place = this.interior ? this.interior.loc.name : ({ A: '幽州', B: '青州', C: '冀州', D1: '兖州', D2: '司隶', D3: '洛阳郊外' }[zoneAt(this.player.x, this.player.y)]);
    const total = S.party.reduce((s, m) => s + Math.max(0, m.sol), 0), totalMax = S.party.reduce((s, m) => s + Rules.maxSol(m.id, S.lv), 0);
    $('#hudInfo').innerHTML = `<div class="h-place">${esc(place)}</div>
      <div class="h-row"><span class="h-lv">Lv ${S.lv}</span><span>金 <b>${S.gold}</b></span><span class="${S.food < 60 ? 'warn' : ''}">粮 <b>${S.food}</b></span></div>
      <div class="h-bar"><i style="width:${totalMax ? total / totalMax * 100 : 0}%"></i></div>`;
  },
};

addEventListener('DOMContentLoaded', () => {
  $('#hudMenu').addEventListener('click', e => { e.stopPropagation(); Sfx.unlock(); if (Game.mode === 'world' && !Input.busy) Game.fieldMenu(); });
  Game.boot();
});
