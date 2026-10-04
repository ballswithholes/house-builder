'use strict';
/* ==========================================================================
   战斗
   ========================================================================== */

// ------------------------------------------------------------------ 规则 --
const Rules = {
  nextExp(lv) { return 10 * lv * lv + 10 * lv; },
  maxSol(id, lv) { const b = GENERALS[id].sol; return Math.round(b * (1 + (lv - 1) * 0.18) + lv * lv * 2); },
  watk(m) { return m.weapon ? WEAPONS[m.weapon].atk : 0; },
  adef(m) { return m.armor ? ARMORS[m.armor].def : 0; },
  atkPow(u) { return (u.str + u.watk) * (1 + u.lv * 0.12) * 1.4; },
  defPow(u) { return (u.str * 0.3 + u.adef) * (1 + u.lv * 0.1) * 0.6; },
  sf(u) { return 0.4 + 0.6 * Math.sqrt(Math.max(0, u.sol) / u.max); },
  tacPow(t, c) { return t.pow * (c.int / 70) * (1 + c.lv * 0.09) * rand(0.9, 1.1); },
  maxTP(s) { const t = Game.tactician(); return t ? Math.floor(GENERALS[t].int / 10 * (2 + s.lv * 0.5)) : 0; },
  // 敌军兵力倍率与按等级配备的兵器
  enemySol(d) { const b = Array.isArray(d.sol) ? randi(d.sol[0], d.sol[1]) : d.sol; return Math.round(b * 1.6); },
  enemyWatk(d) { return Math.round(d.lv * (d.boss ? 2.6 : 2.0)); },
  known(s) {
    const t = Game.tactician(); if (!t) return [];
    const int = GENERALS[t].int;
    return TACTIC_ORDER.filter(k => TACTICS[k].lv <= s.lv && TACTICS[k].int <= int);
  },
};

// ------------------------------------------------------------------ 战斗 --
const Battle = {
  root: null, units: [], enemies: [], active: [], reserve: [], fx: [], running: false,
  bgCanvas: null, auto: false, stopAuto: false, boss: false, bgType: 'plain', scouted: false,

  async run(enemyIds, opts = {}) {
    const S = Game.S;
    this.root = $('#battle');
    this.boss = !!opts.boss;
    this.bgType = opts.bg || 'plain';
    this.auto = false; this.stopAuto = false; this.scouted = false; this.fx = [];
    // 敌军
    this.enemies = enemyIds.map((k, i) => {
      const d = ENEMIES[k];
      const sol = Rules.enemySol(d);
      return { side: 'e', key: k, d, name: d.name, glyph: d.glyph, color: d.color, lv: d.lv, str: d.str, int: d.int, agi: d.agi,
               watk: Rules.enemyWatk(d), adef: d.def, sol, max: sol, conf: 0, i };
    });
    // 同名敌兵加上编号
    const cnt = {};
    for (const e of this.enemies) cnt[e.name] = (cnt[e.name] || 0) + 1;
    const seen = {};
    for (const e of this.enemies) if (cnt[e.name] > 1) { seen[e.name] = (seen[e.name] || 0) + 1; e.name += '甲乙丙丁戊'[seen[e.name] - 1]; }
    // 我军
    const mk = m => {
      const g = GENERALS[m.id];
      return { side: 'p', m, id: m.id, name: g.name, glyph: g.glyph, color: g.color, lv: S.lv, str: g.str, int: g.int, agi: g.agi,
               watk: Rules.watk(m), adef: Rules.adef(m), get sol() { return m.sol; }, set sol(v) { m.sol = v; }, max: Rules.maxSol(m.id, S.lv), conf: 0 };
    };
    const alive = S.party.filter(m => m.sol > 0).map(mk);
    this.active = alive.slice(0, 5);
    this.reserve = alive.slice(5);

    this.build();
    if (!this.root._tap) {
      this.root._tap = true;
      this.root.addEventListener('pointerdown', () => { if (this.auto && !this.stopAuto && !Input.busy) Input.fire('ok'); });
    }
    Sfx.play('encounter');
    Sfx.startMusic('battle');
    this.root.classList.remove('hidden');
    this.root.classList.add('intro');
    this.running = true;
    this.loop();
    await sleep(650);
    this.root.classList.remove('intro');
    const lead = this.enemies[0];
    await this.msg(this.boss ? `${lead.name}率军出战！` : (this.enemies.some(e => e.d.named) ? `敌将${this.enemies.find(e => e.d.named).name}出现了！` : '遭遇敌军！'), 900);

    let result = null;
    while (!result) {
      this.renderAll();
      let cmds;
      if (this.auto) {
        if (this.stopAuto) { this.auto = false; this.stopAuto = false; this.setAutoBadge(false); continue; }
        cmds = this.active.filter(u => u.sol > 0).map(u => ({ u, type: 'attack', target: this.pickEnemy() }));
      } else {
        const top = await this.mainMenu();
        if (top === 'flee') {
          if (this.boss) { await this.msg('此战无法撤退！', 900); continue; }
          const pa = avg(this.active.map(u => u.agi)), ea = avg(this.living(this.enemies).map(u => u.agi));
          if (chance(clamp(0.55 + (pa - ea) / 150, 0.2, 0.95))) { await this.msg('全军撤退！', 700); result = 'flee'; break; }
          await this.msg('撤退失败！', 700);
          cmds = [];
        } else if (top === 'auto') {
          this.auto = true; this.stopAuto = false; this.setAutoBadge(true);
          continue;
        } else {
          cmds = await this.commandPhase();
          if (!cmds) continue;
        }
      }
      result = await this.resolve(cmds);
    }
    this.setAutoBadge(false);
    if (result === 'win') await this.victory(opts);
    else if (result === 'lose') { await this.msg('我军全军覆没……', 1400); }
    await UI.fade(true, 300);
    this.running = false;
    this.root.classList.add('hidden');
    $('#bnums').innerHTML = '';
    return result;
  },

  // ---------------------------------------------------------- DOM 构建 --
  build() {
    const er = $('#benemies'), pr = $('#bparty');
    er.innerHTML = ''; pr.innerHTML = '';
    er.classList.toggle('many', this.enemies.length > 3);
    for (const e of this.enemies) {
      const el = document.createElement('div');
      el.className = 'ecard' + (e.d.boss ? ' boss' : '') + (e.d.named ? ' named' : '');
      el.style.setProperty('--c', e.color);
      el.innerHTML = `<div class="ec-banner"><div class="ec-glyph">${esc(e.glyph)}</div></div>
        <canvas class="troops" width="180" height="64"></canvas>
        <div class="ec-name">${esc(e.name)}</div>
        <div class="bar"><i></i></div><div class="ec-sol">???</div><div class="st"></div>`;
      er.appendChild(el); e.el = el;
    }
    for (const u of [...this.active, ...this.reserve]) this.makePCard(u);
    this.drawBG();
  },
  makePCard(u) {
    const pr = $('#bparty');
    const el = document.createElement('div');
    el.className = 'pcard';
    el.innerHTML = `${UI.medal(u.id, 44)}<div class="pc-body"><div class="pc-name">${esc(u.name)}${Game.tactician() === u.id ? '<em>军师</em>' : ''}</div>
      <div class="pc-sol"><b></b><span></span></div><div class="bar"><i></i></div></div><div class="st"></div>`;
    pr.appendChild(el); u.el = el;
  },
  renderAll() {
    for (const e of this.enemies) this.renderUnit(e);
    for (const u of [...this.active, ...this.reserve]) this.renderUnit(u);
    const tp = $('#btp');
    tp.innerHTML = `策略值 <b>${Game.S.tp}</b> / ${Rules.maxTP(Game.S)}`;
  },
  renderUnit(u) {
    if (!u.el) return;
    const p = clamp(u.sol / u.max, 0, 1);
    const bar = u.el.querySelector('.bar i');
    bar.style.width = (p * 100) + '%';
    bar.className = p < 0.25 ? 'low' : p < 0.5 ? 'mid' : '';
    u.el.classList.toggle('dead', u.sol <= 0);
    u.el.querySelector('.st').textContent = u.conf > 0 ? '乱' : '';
    if (u.side === 'e') {
      u.el.querySelector('.ec-sol').textContent = this.scouted ? `${Math.max(0, Math.round(u.sol))}` : '';
      drawTroops(u.el.querySelector('.troops'), u.color, p, u.d.boss);
    } else {
      u.el.classList.toggle('reserve', this.reserve.includes(u));
      u.el.querySelector('.pc-sol b').textContent = Math.max(0, Math.round(u.sol));
      u.el.querySelector('.pc-sol span').textContent = '/' + u.max;
    }
  },
  setAutoBadge(on) { $('#bauto').classList.toggle('show', on); },

  async msg(t, ms = 650) {
    const m = $('#bmsg');
    m.textContent = t; m.classList.remove('pop'); void m.offsetWidth; m.classList.add('pop');
    await sleep(this.auto ? ms * 0.7 : ms);
  },

  living(list) { return list.filter(u => u.sol > 0); },
  pickEnemy() { const l = this.living(this.enemies); return l[Math.floor(Math.random() * l.length)]; },

  // ---------------------------------------------------------- 指令 --
  mainMenu() {
    return UI.menu({
      cls: 'battle', cancel: false, items: [
        { label: '战斗', value: 'fight', icon: '⚔', desc: '逐一下达各武将的指令' },
        { label: '全军突击', value: 'auto', icon: '🚩', desc: '全军自动进攻，按任意键停止' },
        { label: '撤退', value: 'flee', icon: '↩', desc: this.boss ? '此战无法撤退' : '率军撤离战场' },
      ],
    });
  },

  async commandPhase() {
    const S = Game.S;
    const units = this.active.filter(u => u.sol > 0);
    const cmds = [];
    let i = 0, tpUsed = 0;
    while (i < units.length) {
      const u = units[i];
      this.focus(u);
      const known = Rules.known(S);
      const itemsHave = Object.keys(S.items).filter(k => S.items[k] > 0);
      const c = await UI.menu({
        cls: 'battle', title: `${u.name} 的行动`, items: [
          { label: '攻击', value: 'attack', icon: '⚔' },
          { label: '策略', value: 'tactic', icon: '☯', disabled: !known.length },
          { label: '道具', value: 'item', icon: '◈', disabled: !itemsHave.length },
        ],
      });
      if (c === null) {
        if (i === 0) { this.focus(null); return null; }
        i--; const last = cmds.pop(); if (last && last.type === 'tactic') tpUsed -= TACTICS[last.key].tp; continue;
      }
      if (c === 'attack') {
        const t = await this.chooseTarget('enemy');
        if (!t) continue;
        cmds.push({ u, type: 'attack', target: t }); i++;
      } else if (c === 'tactic') {
        const k = await UI.menu({
          cls: 'battle wide', title: `策略 · 剩余 ${S.tp - tpUsed}`,
          items: known.map(k => ({ label: TACTICS[k].name, right: TACTICS[k].tp, value: k, desc: TACTICS[k].desc, disabled: TACTICS[k].tp > S.tp - tpUsed })),
        });
        if (!k) continue;
        const tac = TACTICS[k];
        let target = null;
        if (tac.target === 'enemy') { target = await this.chooseTarget('enemy'); if (!target) continue; }
        else if (tac.target === 'ally') { target = await this.chooseTarget('ally'); if (!target) continue; }
        cmds.push({ u, type: 'tactic', key: k, target }); tpUsed += tac.tp; i++;
      } else if (c === 'item') {
        const pending = {};
        for (const cm of cmds) if (cm.type === 'item') pending[cm.key] = (pending[cm.key] || 0) + 1;
        const k = await UI.menu({
          cls: 'battle wide', title: '道具',
          items: itemsHave.map(k => ({ label: ITEMS[k].name, right: '×' + (S.items[k] - (pending[k] || 0)), value: k, desc: ITEMS[k].desc, disabled: S.items[k] - (pending[k] || 0) <= 0 })),
        });
        if (!k) continue;
        const it = ITEMS[k];
        let target = null;
        if (it.target === 'ally') { target = await this.chooseTarget('ally'); if (!target) continue; }
        else if (it.target === 'dead') { target = await this.chooseTarget('dead'); if (!target) continue; }
        cmds.push({ u, type: 'item', key: k, target }); i++;
      }
    }
    this.focus(null);
    return cmds;
  },

  focus(u) {
    for (const x of [...this.active, ...this.reserve]) x.el.classList.toggle('focus', x === u);
  },

  chooseTarget(kind) {
    let list;
    if (kind === 'enemy') list = this.living(this.enemies);
    else if (kind === 'ally') list = this.living([...this.active, ...this.reserve]);
    else list = Game.S.party.filter(m => m.sol <= 0).map(m => ({ m, name: GENERALS[m.id].name, dead: true }));
    if (!list.length) { UI.toast('没有可选择的目标'); return Promise.resolve(null); }
    const hl = el => { for (const x of document.querySelectorAll('.ecard,.pcard')) x.classList.toggle('target', x === el); };
    return UI.menu({
      cls: 'battle', title: kind === 'enemy' ? '选择目标' : kind === 'ally' ? '选择武将' : '选择阵亡武将',
      items: list.map(u => ({ label: u.name, right: kind === 'enemy' ? (this.scouted ? Math.round(u.sol) : '') : u.dead ? '阵亡' : `${Math.round(u.sol)}/${u.max}`, value: u })),
      onMove: it => hl(it && it.value.el),
      onClose: () => hl(null),
      clickEls: list.map((u, i) => [u.el, i]).filter(([el]) => el),
    });
  },

  // ---------------------------------------------------------- 结算 --
  async resolve(cmds) {
    const acts = [];
    for (const c of cmds) acts.push({ ...c, spd: c.u.agi * rand(0.85, 1.15) });
    for (const e of this.living(this.enemies)) {
      acts.push({ u: e, type: 'enemy', spd: e.agi * rand(0.85, 1.15) });
      if (e.d.double) acts.push({ u: e, type: 'enemy', spd: e.agi * rand(0.3, 0.6) });
    }
    acts.sort((a, b) => b.spd - a.spd);
    for (const a of acts) {
      if (a.u.sol <= 0) continue;
      if (a.u.side === 'p' && !this.active.includes(a.u)) continue;
      const r = await this.doAction(a);
      if (r) return r;
      const end = this.checkEnd();
      if (end) return end;
    }
    // 回合结束
    for (const e of [...this.enemies, ...this.active]) if (e.conf > 0 && e.sol > 0 && chance(0.35)) { e.conf = 0; this.renderUnit(e); await this.msg(`${e.name}恢复了神智。`, 500); }
    await this.fillFront();
    this.renderAll();
    return this.checkEnd();
  },

  checkEnd() {
    if (!this.living(this.enemies).length) return 'win';
    if (!this.living(this.active).length && !this.living(this.reserve).length) return 'lose';
    return null;
  },

  async fillFront() {
    for (let k = 0; k < this.active.length; k++) {
      if (this.active[k].sol > 0) continue;
      const j = this.reserve.findIndex(u => u.sol > 0);
      if (j < 0) break;
      const [r] = this.reserve.splice(j, 1);
      const [d] = this.active.splice(k, 1, r);
      this.reserve.push(d);
      const pr = $('#bparty');
      pr.insertBefore(r.el, d.el);
      pr.appendChild(d.el);
      this.renderUnit(r); this.renderUnit(d);
      await this.msg(`${r.name}率部上阵！`, 600);
    }
  },

  async doAction(a) {
    const S = Game.S;
    const u = a.u;
    if (a.type === 'enemy') return this.enemyAct(u);
    if (u.conf > 0) {
      if (chance(0.35)) { await this.msg(`${u.name}陷入混乱，不知所措……`, 600); return null; }
      const t = pick(this.living([...this.enemies, ...this.active]).filter(x => x !== u));
      if (t) { await this.msg(`${u.name}在混乱中胡乱攻击！`, 450); await this.attack(u, t); }
      return null;
    }
    if (a.type === 'attack') {
      let t = a.target && a.target.sol > 0 ? a.target : this.pickEnemy();
      if (!t) return null;
      await this.attack(u, t);
      return null;
    }
    if (a.type === 'tactic') {
      const tac = TACTICS[a.key];
      if (S.tp < tac.tp) { await this.msg(`${u.name}欲施展${tac.name}，但策略值不足！`, 700); return null; }
      S.tp -= tac.tp; this.renderAll();
      return this.castTactic(u, tac, a.target, true);
    }
    if (a.type === 'item') {
      const it = ITEMS[a.key];
      if (!S.items[a.key]) return null;
      S.items[a.key]--;
      this.lunge(u);
      await this.msg(`${u.name}使用了${it.name}！`, 500);
      if (it.type === 'heal') {
        const targets = it.target === 'allies' ? this.living([...this.active, ...this.reserve]) : [a.target].filter(x => x && x.sol > 0);
        this.spawnFx('heal', targets);
        Sfx.play('heal');
        for (const t of targets) this.heal(t, it.pow);
        await sleep(600);
      } else if (it.type === 'revive') {
        const m = a.target && a.target.m;
        if (m && m.sol <= 0) {
          const max = Rules.maxSol(m.id, S.lv);
          m.sol = Math.round(max * it.pow);
          let unit = [...this.active, ...this.reserve].find(x => x.m === m);
          if (!unit) {
            const g = GENERALS[m.id];
            unit = { side: 'p', m, id: m.id, name: g.name, glyph: g.glyph, color: g.color, lv: S.lv, str: g.str, int: g.int, agi: g.agi,
                     watk: Rules.watk(m), adef: Rules.adef(m), get sol() { return m.sol; }, set sol(v) { m.sol = v; }, max, conf: 0 };
            this.reserve.push(unit); this.makePCard(unit);
          }
          this.spawnFx('heal', [unit]); Sfx.play('heal');
          this.renderUnit(unit);
          await this.msg(`${unit.name}重整旗鼓，复活了！`, 700);
          await this.fillFront();
        }
      } else if (it.type === 'tp') {
        S.tp = Math.min(Rules.maxTP(S), S.tp + it.pow);
        this.renderAll(); Sfx.play('heal');
        await this.msg(`策略值恢复了！`, 600);
      } else if (it.type === 'bomb') {
        const targets = this.living(this.enemies);
        this.spawnFx('fire', targets); Sfx.play('fire');
        await sleep(350);
        for (const t of targets) this.damage(t, it.pow * rand(0.85, 1.15) * (1 + S.lv * 0.06));
        await sleep(600);
      }
      return null;
    }
    return null;
  },

  async attack(u, t, verbose = true) {
    this.lunge(u);
    const missP = 0.04 + Math.max(0, t.agi - u.agi) / 400;
    if (chance(missP)) {
      Sfx.play('cancel');
      this.spawnFx('slash', [t]);
      this.num(t, '闪避', 'miss');
      await this.msg(`${u.name}的攻击被${t.name}闪开了！`, 600);
      return;
    }
    const crit = chance(1 / 16);
    let dmg;
    const sf = Rules.sf(u);
    if (crit) dmg = Rules.atkPow(u) * sf * rand(1.5, 1.8);
    else {
      dmg = (Rules.atkPow(u) - Rules.defPow(t)) * sf * rand(0.85, 1.15);
      dmg = Math.max(dmg, Rules.atkPow(u) * 0.08 * sf * rand(0.8, 1.2));
    }
    dmg = Math.max(1, Math.round(dmg));
    this.spawnFx(crit ? 'crit' : 'slash', [t]);
    Sfx.play(crit ? 'crit' : 'hit');
    if (crit) this.shake();
    this.damage(t, dmg, crit);
    if (verbose) await this.msg(crit ? `会心一击！${u.name}对${t.name}造成 ${dmg} 损伤！` : `${u.name}攻击${t.name}，损兵 ${dmg}`, crit ? 900 : 620);
    if (t.sol <= 0) await this.ko(t);
  },

  async castTactic(u, tac, target, isParty) {
    this.lunge(u);
    await this.msg(`${u.name}施展「${tac.name}」！`, 550);
    const foes = isParty ? this.enemies : [...this.active];
    const friends = isParty ? [...this.active, ...this.reserve] : this.enemies;
    switch (tac.type) {
      case 'fire': case 'water': case 'rock': {
        let targets = tac.target === 'enemies' ? this.living(foes) : [target && target.sol > 0 ? target : pick(this.living(foes))];
        targets = targets.filter(Boolean);
        this.spawnFx(tac.type, targets);
        Sfx.play(tac.type);
        if (tac.target === 'enemies' || tac.pow > 200) this.shake();
        await sleep(450);
        let anyHit = false;
        for (const t of targets) {
          const p = clamp(0.62 + (u.int - t.int) / 120, 0.2, 0.97);
          if (!chance(p)) { this.num(t, '无效', 'miss'); continue; }
          anyHit = true;
          this.damage(t, Math.round(Rules.tacPow(tac, u)));
        }
        await sleep(650);
        if (!anyHit) await this.msg('策略没有奏效……', 600);
        for (const t of targets) if (t.sol <= 0) await this.ko(t);
        break;
      }
      case 'heal': {
        const targets = tac.target === 'allies' ? this.living(friends) : [target && target.sol > 0 ? target : null].filter(Boolean);
        this.spawnFx('heal', targets); Sfx.play('heal');
        for (const t of targets) this.heal(t, Math.round(Rules.tacPow(tac, u)));
        await sleep(700);
        break;
      }
      case 'confuse': {
        const t = target && target.sol > 0 ? target : pick(this.living(foes));
        if (!t) break;
        this.spawnFx('confuse', [t]); Sfx.play('confuse');
        await sleep(400);
        const p = clamp(0.55 + (u.int - t.int) / 100, 0.1, 0.9) * (t.d && t.d.boss ? 0.5 : 1);
        if (chance(p)) { t.conf = 1; this.renderUnit(t); await this.msg(`${t.name}陷入了混乱！`, 700); }
        else { this.num(t, '无效', 'miss'); await this.msg(`${t.name}不为所动。`, 650); }
        break;
      }
      case 'scout': {
        this.scouted = true; this.renderAll();
        this.spawnFx('scout', this.living(foes));
        Sfx.play('confuse');
        const rows = this.living(this.enemies).map(e => `<tr><td>${esc(e.name)}</td><td>Lv${e.lv}</td><td>${Math.round(e.sol)}</td><td>${e.str}</td><td>${e.int}</td><td>${e.agi}</td></tr>`).join('');
        await UI.panel(`<h3>谍报</h3><table class="tbl"><tr><th>武将</th><th>等级</th><th>兵力</th><th>武</th><th>智</th><th>速</th></tr>${rows}</table>`);
        break;
      }
      case 'escape': {
        if (this.boss) { await this.msg('敌军围困甚严，遁甲无效！', 800); break; }
        this.spawnFx('scout', this.living(this.active));
        await this.msg('奇门遁甲！全军隐去了身形……', 800);
        return 'flee';
      }
    }
    return null;
  },

  async enemyAct(e) {
    const S = Game.S;
    const front = this.living(this.active);
    if (!front.length) return null;
    if (e.conf > 0) {
      const r = Math.random();
      if (r < 0.35) { await this.msg(`${e.name}陷入混乱，不知所措……`, 600); return null; }
      const pool = this.living([...this.enemies, ...front]).filter(x => x !== e);
      const t = pick(pool);
      if (t) { await this.msg(`${e.name}在混乱中胡乱攻击！`, 450); await this.attack(e, t); }
      return null;
    }
    const tacs = e.d.tactics || [];
    if (tacs.length && chance(e.d.boss ? 0.4 : 0.3)) {
      const hurt = this.living(this.enemies).find(x => x.sol < x.max * 0.45);
      let k;
      if (tacs.includes('buji') && hurt && chance(0.6)) k = 'buji';
      else k = pick(tacs.filter(x => x !== 'buji'));
      if (k) {
        const tac = TACTICS[k];
        const target = tac.type === 'heal' ? hurt : pick(front);
        return this.castTactic(e, tac, target, false).then(r => r === 'flee' ? null : r);
      }
    }
    await this.attack(e, pick(front));
    return null;
  },

  damage(t, dmg, crit) {
    t.sol = Math.max(0, t.sol - dmg);
    t.el.classList.remove('hit'); void t.el.offsetWidth; t.el.classList.add('hit');
    this.num(t, dmg, crit ? 'crit' : (t.side === 'p' ? 'hurt' : 'dmg'));
    this.renderUnit(t);
  },
  heal(t, amt) {
    const before = t.sol;
    t.sol = Math.min(t.max, t.sol + amt);
    t.el.classList.remove('healed'); void t.el.offsetWidth; t.el.classList.add('healed');
    this.num(t, '+' + Math.round(t.sol - before), 'heal');
    this.renderUnit(t);
  },
  async ko(t) {
    Sfx.play('ko');
    this.renderUnit(t);
    this.spawnFx('ko', [t]);
    await this.msg(t.side === 'e' ? `${t.name}的部队溃散了！` : `${t.name}兵力耗尽，退出了战斗！`, 650);
  },
  lunge(u) {
    if (!u.el) return;
    u.el.classList.remove('act'); void u.el.offsetWidth; u.el.classList.add('act');
  },
  shake() {
    this.root.classList.remove('shake'); void this.root.offsetWidth; this.root.classList.add('shake');
  },
  num(t, text, cls) {
    if (!t.el) return;
    const r = t.el.getBoundingClientRect(), br = this.root.getBoundingClientRect();
    const n = document.createElement('div');
    n.className = 'bnum ' + cls;
    n.textContent = text;
    n.style.left = (r.left - br.left + r.width / 2 + rand(-14, 14)) + 'px';
    n.style.top = (r.top - br.top + r.height * 0.35) + 'px';
    $('#bnums').appendChild(n);
    setTimeout(() => n.remove(), 1300);
  },

  // ---------------------------------------------------------- 胜利 --
  async victory(opts) {
    const S = Game.S;
    let exp = 0, gold = 0;
    for (const e of this.enemies) { exp += e.d.exp; gold += e.d.gold; }
    gold = Math.round(gold * rand(0.9, 1.15));
    Sfx.stopMusic();
    Sfx.play('win');
    S.exp += exp; S.gold += gold;
    const lines = [`获得经验 <b>${exp}</b>`, `获得 <b>${gold}</b> 金`];
    // 掉落
    if (!this.boss && chance(0.08)) {
      const k = pick(['shangyao', 'shangyao', 'liangyao', 'bingshu']);
      S.items[k] = (S.items[k] || 0) + 1;
      lines.push(`缴获了 <b>${ITEMS[k].name}</b>`);
    }
    const oldLv = S.lv, oldKnown = Rules.known(S);
    while (S.exp >= Rules.nextExp(S.lv) && S.lv < 50) { S.exp -= Rules.nextExp(S.lv); S.lv++; }
    let lvHtml = '';
    if (S.lv > oldLv) {
      Sfx.play('lvup');
      for (const m of S.party.concat(S.reserve)) {
        const d = Rules.maxSol(m.id, S.lv) - Rules.maxSol(m.id, oldLv);
        if (m.sol > 0) m.sol += d;
      }
      S.tp = Math.min(Rules.maxTP(S), S.tp + Math.round(Rules.maxTP(S) * 0.25));
      lvHtml = `<div class="lvup">全军升至 <b>Lv ${S.lv}</b>！兵力上限提升</div>`;
      const learned = Rules.known(S).filter(k => !oldKnown.includes(k));
      if (learned.length) lvHtml += `<div class="learn">习得新策略：${learned.map(k => `<b>${TACTICS[k].name}</b>`).join('、')}</div>`;
    }
    await UI.panel(`<div class="victory"><div class="v-title">胜 利</div>${lines.map(l => `<div>${l}</div>`).join('')}${lvHtml}</div>`, 'center');
    // 降将
    for (const e of this.enemies) {
      const id = e.d.recruit;
      if (!id || Game.hasGeneral(id) || !chance(0.55)) continue;
      await UI.say(`败军之将，承蒙不杀……刘将军仁德之名，在下早有耳闻，愿归降效力！`, id);
      if (await UI.confirm(`收降${GENERALS[id].name}？`, '收降', '放走')) {
        Game.addGeneral(id);
        await UI.say(`${GENERALS[id].name}加入了我军！`);
      }
    }
  },

  // ---------------------------------------------------------- 画面 --
  drawBG() {
    const cv = $('#bbg');
    const W = this.root.clientWidth || innerWidth, H = this.root.clientHeight || innerHeight;
    const dpr = Math.min(2, devicePixelRatio || 1);
    cv.width = W * dpr; cv.height = H * dpr;
    const fx = $('#bfx'); fx.width = W * dpr; fx.height = H * dpr;
    const off = document.createElement('canvas'); off.width = cv.width; off.height = cv.height;
    const c = off.getContext('2d'); c.scale(dpr, dpr);
    paintBattleBG(c, W, H, this.bgType);
    this.bgCanvas = off;
  },
  loop() {
    if (!this.running) return;
    const cv = $('#bbg'), c = cv.getContext('2d');
    const t = performance.now() / 1000;
    if (this.bgCanvas) c.drawImage(this.bgCanvas, 0, 0);
    const W = cv.width, H = cv.height;
    // 飘尘
    c.save();
    const dust = this.bgType === 'cave' || this.bgType === 'palace' || this.bgType === 'fort' ? 'rgba(255,170,80,' : 'rgba(255,255,240,';
    for (let i = 0; i < 40; i++) {
      const x = ((hash2(i, 1, 5) * W + t * (20 + hash2(i, 2, 5) * 30) * (devicePixelRatio || 1)) % W);
      const y = (hash2(i, 3, 5) * H + Math.sin(t * 0.7 + i) * 20) % H;
      const a = 0.15 + 0.25 * Math.sin(t * 2 + i);
      if (a <= 0) continue;
      c.fillStyle = dust + a + ')';
      c.beginPath(); c.arc(x, y, 1.2 + hash2(i, 4, 5) * 2, 0, Math.PI * 2); c.fill();
    }
    c.restore();
    this.drawFx();
    requestAnimationFrame(() => this.loop());
  },

  center(u) {
    const r = u.el.getBoundingClientRect(), br = this.root.getBoundingClientRect();
    return [r.left - br.left + r.width / 2, r.top - br.top + r.height * 0.4, r.width, r.height];
  },
  spawnFx(type, targets) {
    for (const t of targets) {
      if (!t || !t.el) continue;
      const [x, y, w, h] = this.center(t);
      const P = (o) => this.fx.push(Object.assign({ x, y, vx: 0, vy: 0, life: 0, max: 0.8, size: 6, g: 0, type: 'dot', col: '#fff' }, o));
      switch (type) {
        case 'slash': case 'crit': {
          const n = type === 'crit' ? 3 : 1;
          for (let k = 0; k < n; k++) P({ type: 'slash', ang: -0.9 + k * 0.6 + rand(-0.2, 0.2), r: w * (0.45 + k * 0.08), max: 0.32, col: type === 'crit' ? '#ffd76a' : '#ffffff', delay: k * 0.06 });
          for (let k = 0; k < (type === 'crit' ? 26 : 12); k++) { const a = rand(0, Math.PI * 2), s = rand(80, 260); P({ vx: Math.cos(a) * s, vy: Math.sin(a) * s, max: rand(0.3, 0.6), size: rand(2, 4), col: type === 'crit' ? '#ffd76a' : '#fff6d8', g: 300 }); }
          if (type === 'crit') P({ type: 'ring', max: 0.45, r: w * 0.2, col: '#ffd76a' });
          break;
        }
        case 'fire':
          for (let k = 0; k < 60; k++) P({ x: x + rand(-w * 0.45, w * 0.45), y: y + rand(-h * 0.1, h * 0.45), vx: rand(-30, 30), vy: rand(-220, -60), max: rand(0.5, 1.1), size: rand(6, 16), col: pick(['#ffdf6b', '#ff9a3c', '#ff5a2a', '#ffb347']), type: 'flame', delay: rand(0, 0.35) });
          P({ type: 'glow', max: 0.9, r: w * 0.9, col: 'rgba(255,120,40,' });
          break;
        case 'water':
          for (let k = 0; k < 50; k++) { const a = rand(-Math.PI, 0); P({ x: x + rand(-w * 0.4, w * 0.4), y: y + h * 0.3, vx: Math.cos(a) * rand(40, 160), vy: Math.sin(a) * rand(150, 380), g: 700, max: rand(0.6, 1.0), size: rand(3, 7), col: pick(['#bfe9ff', '#5ab8ff', '#2f8de0', '#ffffff']), delay: rand(0, 0.3) }); }
          P({ type: 'wave', max: 0.9, r: w * 0.7, col: '#7fd0ff' });
          break;
        case 'rock':
          for (let k = 0; k < 9; k++) P({ x: x + rand(-w * 0.4, w * 0.4), y: y - h * 1.6 - rand(0, 80), vy: rand(500, 700), g: 900, max: 0.7, size: rand(10, 20), col: pick(['#8b8577', '#6d675b', '#a59f8f']), type: 'rock', floor: y + h * 0.35, delay: rand(0, 0.25) });
          break;
        case 'heal':
          for (let k = 0; k < 30; k++) P({ x: x + rand(-w * 0.45, w * 0.45), y: y + rand(-h * 0.2, h * 0.5), vy: rand(-110, -40), max: rand(0.7, 1.2), size: rand(3, 6), col: pick(['#b6ffcf', '#6cf0a0', '#ffffff', '#d8ffb0']), type: 'spark', delay: rand(0, 0.3) });
          P({ type: 'glow', max: 0.9, r: w * 0.7, col: 'rgba(90,255,160,' });
          break;
        case 'confuse':
          for (let k = 0; k < 3; k++) P({ type: 'spiral', max: 1.0, r: w * (0.25 + k * 0.1), col: '#c79bff', delay: k * 0.1 });
          break;
        case 'scout':
          P({ type: 'ring', max: 0.7, r: w * 0.3, col: '#8fd3ff' });
          break;
        case 'ko':
          for (let k = 0; k < 24; k++) P({ x: x + rand(-w * 0.4, w * 0.4), y: y + rand(-h * 0.2, h * 0.4), vx: rand(-40, 40), vy: rand(-60, -10), max: rand(0.8, 1.4), size: rand(8, 16), col: 'rgba(60,55,50,', type: 'smoke' });
          break;
      }
    }
  },
  drawFx() {
    const cv = $('#bfx'), c = cv.getContext('2d');
    const dpr = cv.width / (this.root.clientWidth || 1);
    c.setTransform(1, 0, 0, 1, 0, 0);
    c.clearRect(0, 0, cv.width, cv.height);
    c.setTransform(dpr, 0, 0, dpr, 0, 0);
    const now = performance.now();
    const dt = this._last ? Math.min(0.05, (now - this._last) / 1000) : 0.016;
    this._last = now;
    const keep = [];
    for (const p of this.fx) {
      if (p.delay > 0) { p.delay -= dt; keep.push(p); continue; }
      p.life += dt;
      if (p.life >= p.max) continue;
      keep.push(p);
      p.vy += p.g * dt; p.x += p.vx * dt; p.y += p.vy * dt;
      if (p.floor && p.y > p.floor) { p.y = p.floor; p.vy *= -0.3; p.vx = rand(-60, 60); }
      const k = p.life / p.max;
      c.save();
      switch (p.type) {
        case 'dot': case 'spark':
          c.globalCompositeOperation = 'lighter';
          c.globalAlpha = 1 - k; c.fillStyle = p.col;
          c.beginPath(); c.arc(p.x, p.y, p.size * (1 - k * 0.5), 0, Math.PI * 2); c.fill();
          if (p.type === 'spark') { c.fillRect(p.x - p.size * 1.5, p.y - 0.5, p.size * 3, 1); c.fillRect(p.x - 0.5, p.y - p.size * 1.5, 1, p.size * 3); }
          break;
        case 'flame': {
          c.globalCompositeOperation = 'lighter';
          const g = c.createRadialGradient(p.x, p.y, 0, p.x, p.y, p.size * (1 - k * 0.4));
          g.addColorStop(0, p.col); g.addColorStop(1, 'rgba(255,60,0,0)');
          c.globalAlpha = 1 - k; c.fillStyle = g; c.beginPath(); c.arc(p.x, p.y, p.size, 0, Math.PI * 2); c.fill();
          break;
        }
        case 'glow': {
          c.globalCompositeOperation = 'lighter';
          const a = Math.sin(k * Math.PI) * 0.5;
          const g = c.createRadialGradient(p.x, p.y, 0, p.x, p.y, p.r);
          g.addColorStop(0, p.col + a + ')'); g.addColorStop(1, p.col + '0)');
          c.fillStyle = g; c.fillRect(p.x - p.r, p.y - p.r, p.r * 2, p.r * 2);
          break;
        }
        case 'slash': {
          c.globalCompositeOperation = 'lighter';
          c.translate(p.x, p.y); c.rotate(p.ang);
          const sweep = Math.min(1, k * 2.2);
          c.strokeStyle = p.col; c.lineCap = 'round';
          c.globalAlpha = 1 - Math.max(0, k - 0.4) / 0.6;
          for (let j = 0; j < 3; j++) {
            c.lineWidth = (6 - j * 2) * (1 - k * 0.5);
            c.globalAlpha *= 0.85;
            c.beginPath(); c.arc(0, 0, p.r - j * 3, -1.2, -1.2 + sweep * 2.4); c.stroke();
          }
          break;
        }
        case 'ring':
          c.globalCompositeOperation = 'lighter';
          c.strokeStyle = p.col; c.globalAlpha = 1 - k; c.lineWidth = 4 * (1 - k) + 1;
          c.beginPath(); c.arc(p.x, p.y, p.r * (1 + k * 2.5), 0, Math.PI * 2); c.stroke();
          break;
        case 'wave':
          c.globalCompositeOperation = 'lighter';
          c.strokeStyle = p.col; c.globalAlpha = (1 - k) * 0.8; c.lineWidth = 6 * (1 - k) + 1;
          c.beginPath(); c.ellipse(p.x, p.y + 30, p.r * (0.3 + k * 1.2), p.r * (0.1 + k * 0.35), 0, 0, Math.PI * 2); c.stroke();
          break;
        case 'rock':
          c.globalAlpha = 1 - Math.max(0, k - 0.7) / 0.3;
          c.fillStyle = p.col; c.translate(p.x, p.y); c.rotate(p.life * 6);
          c.beginPath(); c.moveTo(-p.size, -p.size * 0.4); c.lineTo(-p.size * 0.2, -p.size); c.lineTo(p.size, -p.size * 0.3); c.lineTo(p.size * 0.6, p.size * 0.8); c.lineTo(-p.size * 0.7, p.size * 0.6); c.closePath(); c.fill();
          c.fillStyle = 'rgba(255,255,255,.2)'; c.fillRect(-p.size * 0.3, -p.size * 0.6, p.size * 0.5, p.size * 0.3);
          break;
        case 'spiral':
          c.globalCompositeOperation = 'lighter';
          c.strokeStyle = p.col; c.globalAlpha = 1 - k; c.lineWidth = 3;
          c.beginPath();
          for (let j = 0; j < 40; j++) { const a = j * 0.35 + p.life * 8, rr = p.r * j / 40; const xx = p.x + Math.cos(a) * rr, yy = p.y + Math.sin(a) * rr * 0.6; j ? c.lineTo(xx, yy) : c.moveTo(xx, yy); }
          c.stroke();
          break;
        case 'smoke':
          c.globalAlpha = (1 - k) * 0.5; c.fillStyle = p.col + '1)';
          c.beginPath(); c.arc(p.x, p.y, p.size * (1 + k), 0, Math.PI * 2); c.fill();
          break;
      }
      c.restore();
    }
    this.fx = keep;
  },
};
function avg(a) { return a.length ? a.reduce((s, v) => s + v, 0) / a.length : 0; }

// 敌军阵列小人
function drawTroops(cv, col, p, boss) {
  const c = cv.getContext('2d');
  const W = cv.width, H = cv.height;
  c.clearRect(0, 0, W, H);
  const total = boss ? 15 : 10;
  const n = Math.ceil(total * p);
  const perRow = boss ? 8 : 5;
  for (let i = 0; i < total; i++) {
    const row = Math.floor(i / perRow), colI = i % perRow;
    const rows = Math.ceil(total / perRow);
    const x = W / 2 + (colI - (perRow - 1) / 2) * (W / (perRow + 0.5)) + (row % 2) * 6;
    const y = H - 6 - (rows - 1 - row) * 18;
    const on = i < n;
    c.globalAlpha = on ? 1 : 0.12;
    // 枪
    c.strokeStyle = on ? '#cfc7b4' : '#888'; c.lineWidth = 1.5;
    c.beginPath(); c.moveTo(x + 6, y - 2); c.lineTo(x + 6, y - 26); c.stroke();
    c.fillStyle = '#e8e2d2'; c.beginPath(); c.moveTo(x + 6, y - 30); c.lineTo(x + 8, y - 25); c.lineTo(x + 4, y - 25); c.fill();
    // 身体
    c.fillStyle = col; c.beginPath(); c.moveTo(x - 5, y); c.lineTo(x + 5, y); c.lineTo(x + 4, y - 11); c.lineTo(x - 4, y - 11); c.closePath(); c.fill();
    c.fillStyle = '#f3cfa3'; c.beginPath(); c.arc(x, y - 14, 3.6, 0, Math.PI * 2); c.fill();
    c.fillStyle = shade(col, -0.3); c.fillRect(x - 4, y - 18, 8, 2.5);
  }
  c.globalAlpha = 1;
}

// 战斗背景
function paintBattleBG(c, W, H, type) {
  const P = {
    plain:  { sky: ['#5aa8f0', '#bfe3ff', '#ffe9c4'], far: '#9db8c9', mid: '#6f9a6a', ground: ['#86b85a', '#5b8f3f'], sun: 'rgba(255,240,200,' },
    forest: { sky: ['#4f98d8', '#a9d8f0', '#e6f3d0'], far: '#88a8a0', mid: '#3f6f45', ground: ['#5f9a48', '#36662e'], sun: 'rgba(240,255,220,', trees: true },
    hill:   { sky: ['#6aa6e0', '#cde4f5', '#f6e2b8'], far: '#a7a9b8', mid: '#8c9a5c', ground: ['#a8b464', '#77843f'], sun: 'rgba(255,235,190,' },
    sand:   { sky: ['#5fb2ee', '#c8ecff', '#fff3d0'], far: '#a8c4cc', mid: '#c9b27a', ground: ['#e6cf94', '#c5a96a'], sun: 'rgba(255,250,220,' },
    arid:   { sky: ['#e08a58', '#f6c48a', '#ffe6b8'], far: '#b48a7a', mid: '#9a7a52', ground: ['#c2a066', '#8f7244'], sun: 'rgba(255,220,160,' },
    gate:   { sky: ['#3a2a4a', '#c4605a', '#f6b070'], far: '#6a4a5a', mid: '#4a3a3a', ground: ['#8a7a5a', '#5a4a34'], sun: 'rgba(255,170,110,', wall: true },
    cave:   { inside: true, wall: '#3a3028', wall2: '#221a14', floor: ['#4a3e32', '#2a221a'] },
    fort:   { inside: true, wall: '#5a4028', wall2: '#2e2014', floor: ['#6b5640', '#3a2c1e'], palisade: true },
    palace: { inside: true, wall: '#6e2018', wall2: '#2e0c0a', floor: ['#5a4e48', '#2a2220'], pillars: true },
  }[type] || {};
  const hz = H * 0.46;
  if (P.inside) {
    let g = c.createLinearGradient(0, 0, 0, hz); g.addColorStop(0, P.wall2); g.addColorStop(1, P.wall);
    c.fillStyle = g; c.fillRect(0, 0, W, hz);
    g = c.createLinearGradient(0, hz, 0, H); g.addColorStop(0, P.floor[0]); g.addColorStop(1, P.floor[1]);
    c.fillStyle = g; c.fillRect(0, hz, W, H - hz);
    // 透视地砖
    c.strokeStyle = 'rgba(0,0,0,.25)'; c.lineWidth = 1;
    for (let k = -12; k <= 12; k++) { c.beginPath(); c.moveTo(W / 2 + k * W * 0.02, hz); c.lineTo(W / 2 + k * W * 0.16, H); c.stroke(); }
    for (let k = 1; k < 9; k++) { const y = hz + (H - hz) * Math.pow(k / 9, 1.8); c.beginPath(); c.moveTo(0, y); c.lineTo(W, y); c.stroke(); }
    if (P.pillars) {
      for (let k = 0; k < 6; k++) {
        const x = W * (0.06 + k * 0.176);
        const pg = c.createLinearGradient(x - 18, 0, x + 18, 0); pg.addColorStop(0, '#5a120e'); pg.addColorStop(0.4, '#b8342a'); pg.addColorStop(1, '#4a0e0a');
        c.fillStyle = pg; c.fillRect(x - 16, 0, 32, hz + 10);
        c.fillStyle = '#d8a84a'; c.fillRect(x - 20, hz + 4, 40, 8); c.fillRect(x - 20, 30, 40, 6);
      }
      c.fillStyle = 'rgba(216,168,74,.5)'; c.fillRect(0, 22, W, 4);
    } else if (P.palisade) {
      for (let x = -10; x < W + 20; x += 26) {
        const pg = c.createLinearGradient(x, 0, x + 24, 0); pg.addColorStop(0, '#8a6038'); pg.addColorStop(1, '#3e2814');
        c.fillStyle = pg; c.beginPath(); c.moveTo(x, hz); c.lineTo(x, hz * 0.35); c.lineTo(x + 12, hz * 0.25); c.lineTo(x + 24, hz * 0.35); c.lineTo(x + 24, hz); c.fill();
      }
      c.fillStyle = '#2a1a0c'; c.fillRect(0, hz * 0.55, W, 6);
    } else {
      for (let k = 0; k < 30; k++) { const x = hash2(k, 1, 3) * W, y = hash2(k, 2, 3) * hz, r = 20 + hash2(k, 3, 3) * 60; ellipse(c, x, y, r, r * 0.6, 'rgba(0,0,0,.18)'); }
      for (let k = 0; k < 14; k++) { const x = hash2(k, 4, 3) * W; c.fillStyle = 'rgba(30,22,16,.8)'; c.beginPath(); c.moveTo(x - 14, 0); c.lineTo(x + 14, 0); c.lineTo(x, 30 + hash2(k, 5, 3) * 70); c.fill(); }
    }
    // 火把
    for (const fx of [0.15, 0.85]) {
      const x = W * fx, y = hz * 0.55;
      const g2 = c.createRadialGradient(x, y, 0, x, y, 180); g2.addColorStop(0, 'rgba(255,170,70,.55)'); g2.addColorStop(1, 'rgba(255,120,30,0)');
      c.fillStyle = g2; c.fillRect(x - 180, y - 180, 360, 360);
      c.fillStyle = '#ffcf6a'; ellipse(c, x, y, 7, 14, '#ffcf6a');
    }
  } else {
    let g = c.createLinearGradient(0, 0, 0, hz);
    g.addColorStop(0, P.sky[0]); g.addColorStop(0.6, P.sky[1]); g.addColorStop(1, P.sky[2]);
    c.fillStyle = g; c.fillRect(0, 0, W, hz + 2);
    const sg = c.createRadialGradient(W * 0.74, hz * 0.55, 0, W * 0.74, hz * 0.55, W * 0.45);
    sg.addColorStop(0, P.sun + '0.9)'); sg.addColorStop(0.08, P.sun + '0.6)'); sg.addColorStop(1, P.sun + '0)');
    c.fillStyle = sg; c.fillRect(0, 0, W, hz);
    // 远山（水墨层次）
    const layer = (base, amp, col, seed, rough) => {
      c.beginPath(); c.moveTo(0, hz);
      for (let x = 0; x <= W; x += 6) { const n = fbm(x / W * rough, seed, seed, 4); c.lineTo(x, base - n * amp); }
      c.lineTo(W, hz + 2); c.lineTo(0, hz + 2); c.closePath(); c.fillStyle = col; c.fill();
    };
    layer(hz, hz * 0.55, P.far, 3, 5);
    c.globalAlpha = 0.85; layer(hz, hz * 0.32, shade(P.far, -0.15), 7, 8); c.globalAlpha = 1;
    // 地面
    g = c.createLinearGradient(0, hz, 0, H); g.addColorStop(0, P.ground[0]); g.addColorStop(1, P.ground[1]);
    c.fillStyle = g; c.fillRect(0, hz, W, H - hz);
    // 雾
    g = c.createLinearGradient(0, hz - 30, 0, hz + 40); g.addColorStop(0, 'rgba(255,255,255,0)'); g.addColorStop(0.5, 'rgba(255,255,255,.35)'); g.addColorStop(1, 'rgba(255,255,255,0)');
    c.fillStyle = g; c.fillRect(0, hz - 30, W, 70);
    if (P.trees) {
      for (let k = 0; k < 40; k++) {
        const x = hash2(k, 7, 1) * W, s = 20 + hash2(k, 8, 1) * 30;
        c.fillStyle = shade(P.mid, -0.1 - hash2(k, 9, 1) * 0.2);
        c.beginPath(); c.moveTo(x, hz - s * 2.2); c.lineTo(x + s * 0.6, hz + 4); c.lineTo(x - s * 0.6, hz + 4); c.fill();
      }
    }
    if (P.wall) {
      const wy = hz - 70;
      const wg = c.createLinearGradient(0, wy, 0, hz); wg.addColorStop(0, '#8a7f72'); wg.addColorStop(1, '#4a4038');
      c.fillStyle = wg; c.fillRect(0, wy, W, 72);
      c.fillStyle = '#9a8f80'; for (let x = 0; x < W; x += 28) c.fillRect(x, wy - 12, 16, 14);
      c.fillStyle = '#6e1e18'; c.fillRect(W / 2 - 90, wy - 60, 180, 50);
      c.fillStyle = '#2a2a3a'; c.beginPath(); c.moveTo(W / 2 - 120, wy - 58); c.quadraticCurveTo(W / 2, wy - 110, W / 2 + 120, wy - 58); c.lineTo(W / 2 + 140, wy - 50); c.lineTo(W / 2 - 140, wy - 50); c.fill();
      c.fillStyle = '#1a120c'; c.beginPath(); c.moveTo(W / 2 - 30, hz); c.lineTo(W / 2 - 30, wy + 30); c.arc(W / 2, wy + 30, 30, Math.PI, 0); c.lineTo(W / 2 + 30, hz); c.fill();
      for (const fx2 of [0.2, 0.35, 0.65, 0.8]) flagBG(c, W * fx2, wy - 12, '#3a2a2a', '董');
    }
    // 草叶
    c.strokeStyle = 'rgba(30,60,20,.35)'; c.lineWidth = 1.2;
    for (let k = 0; k < 220; k++) {
      const x = hash2(k, 11, 2) * W, y = hz + 20 + Math.pow(hash2(k, 12, 2), 0.7) * (H - hz - 20), s = 3 + (y - hz) / H * 16;
      c.beginPath(); c.moveTo(x, y); c.lineTo(x - s * 0.3, y - s); c.moveTo(x, y); c.lineTo(x + s * 0.3, y - s * 0.9); c.stroke();
    }
  }
  // 暗角
  const vg = c.createRadialGradient(W / 2, H * 0.45, Math.min(W, H) * 0.3, W / 2, H * 0.5, Math.max(W, H) * 0.75);
  vg.addColorStop(0, 'rgba(0,0,0,0)'); vg.addColorStop(1, 'rgba(0,0,0,.55)');
  c.fillStyle = vg; c.fillRect(0, 0, W, H);
}
function flagBG(c, x, y, col, glyph) {
  c.strokeStyle = '#2a1a10'; c.lineWidth = 3; c.beginPath(); c.moveTo(x, y); c.lineTo(x, y - 70); c.stroke();
  c.fillStyle = col; c.fillRect(x, y - 70, 38, 30);
  c.fillStyle = '#f0e0c0'; c.font = `700 20px ${CN_SERIF}`; c.textAlign = 'center'; c.textBaseline = 'middle'; c.fillText(glyph, x + 19, y - 55);
}
