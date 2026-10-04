'use strict';
/* ==========================================================================
   核心：工具、噪声、输入、音效、界面（对话框 / 菜单）
   ========================================================================== */

const $ = (s, el = document) => el.querySelector(s);
const clamp = (v, a, b) => v < a ? a : v > b ? b : v;
const lerp = (a, b, t) => a + (b - a) * t;
const sleep = ms => new Promise(r => setTimeout(r, ms));
const rand = (a, b) => a + Math.random() * (b - a);
const randi = (a, b) => Math.floor(a + Math.random() * (b - a + 1));
const pick = arr => arr[Math.floor(Math.random() * arr.length)];
const chance = p => Math.random() < p;
function smoothstep(a, b, x) { const t = clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
function weighted(list) {
  let tot = 0; for (const [w] of list) tot += w;
  let r = Math.random() * tot;
  for (const [w, v] of list) { if ((r -= w) < 0) return v; }
  return list[list.length - 1][1];
}
function esc(s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

// ------------------------------------------------------------- 随机与噪声 --
function mulberry32(a) {
  return function () {
    a |= 0; a = a + 0x6D2B79F5 | 0;
    let t = Math.imul(a ^ a >>> 15, 1 | a);
    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
    return ((t ^ t >>> 14) >>> 0) / 4294967296;
  };
}
function hash2(x, y, s) {
  let h = Math.imul(x | 0, 374761393) + Math.imul(y | 0, 668265263) + Math.imul(s | 0, 982451653);
  h = Math.imul(h ^ (h >>> 13), 1274126177);
  h ^= h >>> 16;
  return (h >>> 0) / 4294967296;
}
function vnoise(x, y, s) {
  const xi = Math.floor(x), yi = Math.floor(y);
  const xf = x - xi, yf = y - yi;
  const u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
  const a = hash2(xi, yi, s), b = hash2(xi + 1, yi, s), c = hash2(xi, yi + 1, s), d = hash2(xi + 1, yi + 1, s);
  return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
}
function fbm(x, y, s, oct = 4) {
  let t = 0, a = 0.5, f = 1, n = 0;
  for (let i = 0; i < oct; i++) { t += a * vnoise(x * f, y * f, s + i * 17); n += a; a *= 0.5; f *= 2.03; }
  return t / n;
}

// 颜色
function hex2rgb(h) { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
function mixc(a, b, t) { return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]; }
function rgbs(c, a = 1) { return `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`; }
function shade(hex, amt) {
  const c = hex2rgb(hex);
  const t = amt < 0 ? [0, 0, 0] : [255, 255, 255];
  return rgbs(mixc(c, t, Math.abs(amt)));
}

// ------------------------------------------------------------------ 输入 --
const Input = {
  held: { up: false, down: false, left: false, right: false },
  stack: [],          // 模态处理器栈
  lastDir: null,
  push(fn) { this.stack.push(fn); },
  remove(fn) { const i = this.stack.lastIndexOf(fn); if (i >= 0) this.stack.splice(i, 1); },
  get busy() { return this.stack.length > 0; },
  fire(a) {
    Sfx.unlock();
    const h = this.stack[this.stack.length - 1];
    if (h) h(a); else if (typeof Game !== 'undefined') Game.onAction(a);
  },
  clearHeld() { for (const k in this.held) this.held[k] = false; },
};
(function setupKeys() {
  const map = {
    ArrowUp: 'up', KeyW: 'up', ArrowDown: 'down', KeyS: 'down', ArrowLeft: 'left', KeyA: 'left', ArrowRight: 'right', KeyD: 'right',
    Enter: 'ok', Space: 'ok', KeyZ: 'ok', KeyJ: 'ok', NumpadEnter: 'ok',
    Escape: 'cancel', KeyX: 'cancel', KeyK: 'cancel', Backspace: 'cancel',
    KeyM: 'menu', Tab: 'menu', KeyC: 'menu',
  };
  addEventListener('keydown', e => {
    const a = map[e.code];
    if (!a) return;
    e.preventDefault();
    if (a in Input.held && !e.repeat && !Input.busy) { Input.held[a] = true; Input.lastDir = a; }
    Input.fire(a);
  });
  addEventListener('keyup', e => {
    const a = map[e.code];
    if (a in Input.held) Input.held[a] = false;
  });
  addEventListener('blur', () => Input.clearHeld());
})();

// 触屏方向键
function setupPad() {
  const pad = $('#pad');
  const isTouch = matchMedia('(pointer: coarse)').matches || 'ontouchstart' in window;
  if (isTouch) document.body.classList.add('touch');
  const dpad = $('#dpad');
  let active = null;
  function setDir(d) {
    if (active === d) return;
    if (active) Input.held[active] = false;
    active = d;
    dpad.dataset.dir = d || '';
    if (d) { Input.held[d] = true; Input.lastDir = d; Input.fire(d); }
  }
  function fromPoint(e) {
    const r = dpad.getBoundingClientRect();
    const x = e.clientX - (r.left + r.width / 2), y = e.clientY - (r.top + r.height / 2);
    if (Math.hypot(x, y) < r.width * 0.12) return null;
    return Math.abs(x) > Math.abs(y) ? (x > 0 ? 'right' : 'left') : (y > 0 ? 'down' : 'up');
  }
  dpad.addEventListener('pointerdown', e => { e.preventDefault(); dpad.setPointerCapture(e.pointerId); setDir(fromPoint(e)); });
  dpad.addEventListener('pointermove', e => { if (e.buttons || e.pointerType === 'touch') setDir(fromPoint(e)); });
  const end = () => setDir(null);
  dpad.addEventListener('pointerup', end); dpad.addEventListener('pointercancel', end);
  for (const b of pad.querySelectorAll('[data-act]')) {
    b.addEventListener('pointerdown', e => { e.preventDefault(); b.classList.add('down'); Input.fire(b.dataset.act); });
    const up = () => b.classList.remove('down');
    b.addEventListener('pointerup', up); b.addEventListener('pointerleave', up); b.addEventListener('pointercancel', up);
  }
}

// ------------------------------------------------------------------ 音效 --
const Sfx = {
  ctx: null, master: null, on: true, musicOn: true, music: null,
  unlock() {
    if (this.ctx) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    try {
      this.ctx = new (window.AudioContext || window.webkitAudioContext)();
      this.master = this.ctx.createGain(); this.master.gain.value = 0.5; this.master.connect(this.ctx.destination);
    } catch (e) { this.ctx = null; }
    if (this.ctx && this.pending) { const k = this.pending; this.pending = null; this.startMusic(k); }
  },
  tone(f, dur = 0.12, type = 'square', vol = 0.12, slide = 0, delay = 0, dest) {
    if (!this.ctx || !this.on) return;
    const t = this.ctx.currentTime + delay;
    const o = this.ctx.createOscillator(), g = this.ctx.createGain();
    o.type = type; o.frequency.setValueAtTime(f, t);
    if (slide) o.frequency.exponentialRampToValueAtTime(Math.max(30, f * slide), t + dur);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(vol, t + 0.008);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(g); g.connect(dest || this.master);
    o.start(t); o.stop(t + dur + 0.02);
  },
  noise(dur = 0.2, vol = 0.2, hp = 800, delay = 0) {
    if (!this.ctx || !this.on) return;
    const t = this.ctx.currentTime + delay;
    const len = Math.floor(this.ctx.sampleRate * dur);
    const buf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const d = buf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
    const s = this.ctx.createBufferSource(); s.buffer = buf;
    const f = this.ctx.createBiquadFilter(); f.type = 'highpass'; f.frequency.value = hp;
    const g = this.ctx.createGain(); g.gain.value = vol;
    s.connect(f); f.connect(g); g.connect(this.master); s.start(t);
  },
  play(n) {
    switch (n) {
      case 'move': this.tone(880, 0.04, 'triangle', 0.05); break;
      case 'ok': this.tone(660, 0.06, 'triangle', 0.08); this.tone(990, 0.08, 'triangle', 0.07, 0, 0.05); break;
      case 'cancel': this.tone(440, 0.08, 'triangle', 0.07, 0.7); break;
      case 'bump': this.tone(120, 0.08, 'square', 0.05); break;
      case 'hit': this.noise(0.14, 0.25, 1200); this.tone(180, 0.12, 'square', 0.08, 0.5); break;
      case 'crit': this.noise(0.25, 0.35, 600); this.tone(90, 0.3, 'sawtooth', 0.12, 0.4); break;
      case 'fire': this.noise(0.5, 0.18, 300); this.tone(220, 0.4, 'sawtooth', 0.05, 2); break;
      case 'water': this.noise(0.6, 0.14, 200); this.tone(500, 0.5, 'sine', 0.06, 0.4); break;
      case 'rock': this.noise(0.4, 0.3, 100); this.tone(70, 0.35, 'square', 0.1, 0.5); break;
      case 'heal': [523, 659, 784, 1047].forEach((f, i) => this.tone(f, 0.18, 'sine', 0.07, 0, i * 0.07)); break;
      case 'confuse': [700, 500, 800, 450].forEach((f, i) => this.tone(f, 0.1, 'triangle', 0.06, 0, i * 0.06)); break;
      case 'ko': this.tone(300, 0.4, 'sawtooth', 0.08, 0.3); break;
      case 'encounter': [392, 466, 554, 659, 784].forEach((f, i) => this.tone(f, 0.1, 'square', 0.06, 0, i * 0.045)); this.noise(0.4, 0.1, 2000, 0.2); break;
      case 'win': [523, 659, 784, 1047, 784, 1047].forEach((f, i) => this.tone(f, 0.2, 'triangle', 0.08, 0, i * 0.12)); break;
      case 'lvup': [659, 784, 988, 1319].forEach((f, i) => this.tone(f, 0.22, 'square', 0.05, 0, i * 0.09)); break;
      case 'coin': this.tone(1319, 0.06, 'square', 0.05); this.tone(1760, 0.12, 'square', 0.05, 0, 0.06); break;
      case 'door': this.tone(300, 0.1, 'triangle', 0.07, 1.5); break;
      case 'text': this.tone(1200 + Math.random() * 200, 0.02, 'square', 0.015); break;
    }
  },
  // 背景音乐：五声音阶的拨弦旋律
  startMusic(kind) {
    if (!this.ctx) { this.pending = kind; return; }
    if (this.musicKind === kind) return;
    this.stopMusic();
    this.musicKind = kind;
    if (!this.musicOn) return;
    const ctx = this.ctx;
    const bus = ctx.createGain(); bus.gain.value = 0.0001; bus.connect(this.master);
    bus.gain.exponentialRampToValueAtTime(kind === 'battle' ? 0.5 : 0.38, ctx.currentTime + 1);
    const scale = kind === 'battle' ? [0, 3, 5, 7, 10, 12, 15] : kind === 'dungeon' ? [0, 1, 5, 7, 8, 12] : [0, 2, 4, 7, 9, 12, 14];
    const root = kind === 'battle' ? 220 : kind === 'dungeon' ? 174.6 : kind === 'title' ? 196 : 261.6;
    const tempo = kind === 'battle' ? 0.16 : kind === 'title' ? 0.34 : 0.26;
    const r = mulberry32(kind.length * 977 + 3);
    const phrase = []; let idx = 2;
    for (let i = 0; i < 32; i++) {
      idx = clamp(idx + Math.round((r() - 0.5) * 3.2), 0, scale.length - 1);
      phrase.push(r() < (kind === 'battle' ? 0.15 : 0.3) ? -1 : idx);
    }
    const bass = [0, 0, 5, 7, 3, 3, 5, 7].map(v => kind === 'battle' ? [0, 0, 3, 5, 0, 0, 7, 5][v % 8] : v);
    let step = 0, next = ctx.currentTime + 0.1;
    const pluck = (f, t, vol, dur) => {
      const o = ctx.createOscillator(), g = ctx.createGain(), fl = ctx.createBiquadFilter();
      o.type = 'triangle'; o.frequency.value = f;
      fl.type = 'lowpass'; fl.frequency.setValueAtTime(f * 6, t); fl.frequency.exponentialRampToValueAtTime(f * 1.2, t + dur);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(vol, t + 0.006); g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
      o.connect(fl); fl.connect(g); g.connect(bus); o.start(t); o.stop(t + dur + 0.05);
    };
    const tick = () => {
      while (next < ctx.currentTime + 0.3) {
        const n = phrase[step % phrase.length];
        if (n >= 0) pluck(root * Math.pow(2, scale[n] / 12) * 2, next, 0.09, tempo * 3);
        if (step % 4 === 0) pluck(root * Math.pow(2, (bass[(step / 4) % 8] - 12) / 12), next, 0.12, tempo * 5);
        if (kind === 'battle' && step % 2 === 0) {
          const len = Math.floor(ctx.sampleRate * 0.05), buf = ctx.createBuffer(1, len, ctx.sampleRate), d = buf.getChannelData(0);
          for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
          const s = ctx.createBufferSource(), g = ctx.createGain(); s.buffer = buf; g.gain.value = step % 8 === 0 ? 0.2 : 0.06;
          s.connect(g); g.connect(bus); s.start(next);
        }
        next += tempo; step++;
      }
    };
    const id = setInterval(tick, 80); tick();
    this.music = { bus, id };
  },
  stopMusic() {
    if (this.music) {
      const { bus, id } = this.music;
      clearInterval(id);
      try { bus.gain.cancelScheduledValues(this.ctx.currentTime); bus.gain.setValueAtTime(bus.gain.value, this.ctx.currentTime); bus.gain.exponentialRampToValueAtTime(0.0001, this.ctx.currentTime + 0.4); } catch (e) { }
      setTimeout(() => bus.disconnect(), 600);
    }
    this.music = null; this.musicKind = null;
  },
};

// ------------------------------------------------------------------ 界面 --
const UI = {
  layer: null,
  init() { this.layer = $('#layer'); },

  // 头像徽章
  medal(who, size = 52) {
    const p = UI.who(who);
    return `<div class="medal" style="--c:${p.color};--s:${size}px"><span>${esc(p.glyph)}</span></div>`;
  },
  who(w) {
    if (!w) return null;
    if (typeof w === 'object') return w;
    if (GENERALS[w]) return { name: GENERALS[w].name, glyph: GENERALS[w].glyph, color: GENERALS[w].color };
    if (ENEMIES[w]) return { name: ENEMIES[w].name, glyph: ENEMIES[w].glyph, color: ENEMIES[w].color };
    return { name: w, glyph: w[0], color: '#8a7f6a' };
  },

  // 对话
  say(text, who = null) {
    return new Promise(resolve => {
      const p = UI.who(who);
      const el = document.createElement('div');
      el.className = 'dialog' + (p ? '' : ' narr');
      el.innerHTML = (p ? `<div class="dlg-who">${UI.medal(p, 56)}<div class="dlg-name">${esc(p.name)}</div></div>` : '') +
        `<div class="dlg-text"></div><div class="dlg-next">▼</div>`;
      UI.layer.appendChild(el);
      requestAnimationFrame(() => el.classList.add('show'));
      const tEl = el.querySelector('.dlg-text');
      const chars = [...text];
      let i = 0, done = false;
      const timer = setInterval(() => {
        if (i >= chars.length) { finish(); return; }
        const c = chars[i++];
        tEl.insertAdjacentHTML('beforeend', c === '\n' ? '<br>' : esc(c));
        if (i % 3 === 0) Sfx.play('text');
      }, 22);
      function finish() {
        clearInterval(timer);
        if (!done) { tEl.innerHTML = esc(text).replace(/\n/g, '<br>'); done = true; el.classList.add('done'); }
      }
      const h = a => {
        if (a !== 'ok' && a !== 'cancel') return;
        if (!done) { finish(); return; }
        close();
      };
      function close() {
        Input.remove(h);
        el.classList.remove('show'); el.classList.add('hide');
        setTimeout(() => el.remove(), 180);
        resolve();
      }
      el.addEventListener('pointerdown', e => { e.preventDefault(); Sfx.unlock(); h('ok'); });
      Input.push(h);
    });
  },
  async sayAll(list) {
    for (const [who, text] of list) {
      const w = who && !GENERALS[who] && !ENEMIES[who] ? { name: who, glyph: who[0], color: '#8a7f6a' } : who;
      await UI.say(text, w);
    }
  },

  /* 菜单
     opts: { title, items: [{label, right, desc, disabled, value}], cancel, start, cls, info } */
  menu(opts) {
    return new Promise(resolve => {
      const items = opts.items;
      const el = document.createElement('div');
      el.className = 'menu ' + (opts.cls || '');
      let html = opts.title ? `<div class="menu-title">${opts.title}</div>` : '';
      if (opts.info) html += `<div class="menu-info">${opts.info}</div>`;
      html += '<div class="menu-list">';
      items.forEach((it, i) => {
        html += `<div class="mi${it.disabled ? ' dis' : ''}" data-i="${i}">` +
          (it.icon ? `<span class="mi-ic">${it.icon}</span>` : '') +
          `<span class="mi-l">${it.label}</span>` + (it.right != null ? `<span class="mi-r">${it.right}</span>` : '') + '</div>';
      });
      html += '</div>';
      if (items.some(i => i.desc)) html += '<div class="menu-desc"></div>';
      el.innerHTML = html;
      UI.layer.appendChild(el);
      requestAnimationFrame(() => el.classList.add('show'));
      const rows = [...el.querySelectorAll('.mi')];
      const descEl = el.querySelector('.menu-desc');
      let sel = clamp(opts.start || 0, 0, items.length - 1);
      if (items[sel] && items[sel].disabled && !opts.allowDisabled) { const f = items.findIndex(i => !i.disabled); if (f >= 0) sel = f; }
      const cols = opts.cols || 1;
      function render() {
        rows.forEach((r, i) => r.classList.toggle('sel', i === sel));
        if (descEl) descEl.textContent = (items[sel] && items[sel].desc) || '';
        if (rows[sel]) rows[sel].scrollIntoView({ block: 'nearest' });
        if (opts.onMove) opts.onMove(items[sel], sel);
      }
      const extra = (opts.clickEls || []).map(([cel, i]) => {
        const f = e => { e.stopPropagation(); Sfx.unlock(); sel = i; render(); choose(i); };
        cel.addEventListener('click', f); cel.classList.add('pickable');
        return () => { cel.removeEventListener('click', f); cel.classList.remove('pickable'); };
      });
      function done(v) {
        extra.forEach(f => f());
        if (opts.onClose) opts.onClose();
        Input.remove(h);
        el.classList.remove('show'); el.classList.add('hide');
        setTimeout(() => el.remove(), 150);
        resolve(v);
      }
      function choose(i) {
        const it = items[i];
        if (!it) return;
        if (it.disabled) { Sfx.play('bump'); return; }
        Sfx.play('ok'); done(it.value !== undefined ? it.value : i);
      }
      const h = a => {
        const n = items.length;
        if (a === 'up') { sel = (sel - cols + n) % n; Sfx.play('move'); render(); }
        else if (a === 'down') { sel = (sel + cols) % n; Sfx.play('move'); render(); }
        else if (a === 'left' && cols > 1) { sel = (sel - 1 + n) % n; Sfx.play('move'); render(); }
        else if (a === 'right' && cols > 1) { sel = (sel + 1) % n; Sfx.play('move'); render(); }
        else if (a === 'ok') choose(sel);
        else if ((a === 'cancel' || a === 'menu') && opts.cancel !== false) { Sfx.play('cancel'); done(null); }
      };
      rows.forEach((r, i) => {
        r.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse') { sel = i; render(); } });
        r.addEventListener('click', e => { e.stopPropagation(); Sfx.unlock(); sel = i; render(); choose(i); });
      });
      if (opts.cancel !== false) {
        const x = document.createElement('div'); x.className = 'menu-x'; x.textContent = '✕';
        x.addEventListener('click', e => { e.stopPropagation(); Sfx.play('cancel'); done(null); });
        el.appendChild(x);
      }
      el._update = (i, label, right) => { const r = rows[i]; if (!r) return; r.querySelector('.mi-l').innerHTML = label; if (right != null) r.querySelector('.mi-r').innerHTML = right; };
      if (opts.onOpen) opts.onOpen(el);
      Input.push(h);
      render();
    });
  },

  confirm(title, yes = '是', no = '否') {
    return UI.menu({ title, items: [{ label: yes, value: true }, { label: no, value: false }], cls: 'small' }).then(v => !!v);
  },

  // 信息面板，按确定/取消关闭
  panel(html, cls = '') {
    return new Promise(resolve => {
      const el = document.createElement('div');
      el.className = 'panel ' + cls;
      el.innerHTML = html + '<div class="panel-hint">按确定键关闭</div>';
      UI.layer.appendChild(el);
      requestAnimationFrame(() => el.classList.add('show'));
      const h = a => {
        if (a === 'ok' || a === 'cancel' || a === 'menu') {
          Input.remove(h); Sfx.play('cancel');
          el.classList.remove('show'); el.classList.add('hide');
          setTimeout(() => el.remove(), 150); resolve();
        }
      };
      el.addEventListener('click', () => h('ok'));
      Input.push(h);
    });
  },

  toast(text, ms = 1800) {
    const t = document.createElement('div');
    t.className = 'toast'; t.innerHTML = text;
    $('#toasts').appendChild(t);
    requestAnimationFrame(() => t.classList.add('show'));
    setTimeout(() => { t.classList.remove('show'); setTimeout(() => t.remove(), 300); }, ms);
  },

  // 全屏渐变
  fade(on, ms = 350, color = '#05060a') {
    const f = $('#fade');
    f.style.background = color;
    f.style.transition = `opacity ${ms}ms ease`;
    f.style.opacity = on ? 1 : 0;
    return sleep(ms);
  },

  // 地名横幅
  banner(text, sub = '') {
    const b = $('#banner');
    b.innerHTML = `<div class="bn-line"></div><div class="bn-t">${esc(text)}</div>${sub ? `<div class="bn-s">${esc(sub)}</div>` : ''}<div class="bn-line"></div>`;
    b.classList.remove('show'); void b.offsetWidth; b.classList.add('show');
  },
};
