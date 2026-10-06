'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 战场三维表现
   ← Battle/BattleView.cs（BattleView、UnitVisual、FloatUp）

   棋盘建在 Unity 原点 (400, 0, 0)，每格 T = 2。网格用 SG.MeshBuilder 以
   Unity 坐标构建（与 C# 数字一致），对外的世界坐标均为 three 坐标
   （SG.U(x, y, z) = (x, y, -z)）。
   动画（移动、冲锋、溃散、计策特效）为 async 函数，按帧推进，返回 Promise。
   粒子：自带的轻量系统（THREE.Points + 自定义着色器），在 update(dt) 中推进；
   火焰带点光源闪烁。
   ========================================================================== */
(function () {
  const SG = window.SG;
  const M = SG.M;

  const T = 2;
  const ORIGIN = { x: 400, y: 0, z: 0 };   // Unity 坐标
  const GRAVITY = 9.81;
  const SIDE_BAR = ['#73bfff', '#ff806b'];  // C#：攻方 (0.45,0.75,1)，守方 (1,0.5,0.42)
  const FIRE_LIGHT = 4;                       // 点光源强度（three，decay 1；C# intensity 3 / range 6 略降以免过曝）
  const FIRE_FLARE = 1.8;                     // 火计起火的第一秒：光源与地面火光加强的倍数
  const HEAT_TINT = new THREE.Color(1, 0.42, 0.1);   // 被火计灼烧的部队染成橙色
  const WHITE = new THREE.Color(1, 1, 1);

  function TR() {
    const t = SG.Terrain;
    return t || { Plain: 0, Forest: 1, Hill: 2, Mountain: 3, River: 4, Wall: 5, Gate: 6, Castle: 7 };
  }
  // Unity 坐标的向量（给 MeshBuilder 用）
  function V(x, y, z) { return new THREE.Vector3(x, y, z); }
  function C(r, g, b) { return new THREE.Color(r, g, b); }
  function shade(c, k) { return SG.Gfx.shade(c, k); }
  function lerpColor(a, b, t) {
    t = M.clamp01(t);
    return C(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
  }
  function sfx(name, vol) {
    try { if (SG.Sfx && SG.Sfx.play) SG.Sfx.play(name, vol === undefined ? 0.8 : vol); } catch (e) { /* 无音频时忽略 */ }
  }
  function now() { return performance.now() / 1000; }
  function cssHex(c) { return SG.rgbToHex(c.r, c.g, c.b); }
  // 解析 css 颜色（含 rgba 的透明度）
  function parseColor(c) {
    if (c && c.isColor) return { color: c.clone(), a: 1 };
    if (typeof c === 'string') {
      const m = /^\s*rgba?\(([^)]+)\)\s*$/i.exec(c);
      if (m) {
        const p = m[1].split(/[\s,\/]+/).filter(s => s.length).map(parseFloat);
        return { color: C(p[0] / 255, p[1] / 255, p[2] / 255), a: p.length > 3 ? p[3] : 1 };
      }
      if (/^#[0-9a-f]{8}$/i.test(c)) return { color: new THREE.Color(c.slice(0, 7)), a: parseInt(c.slice(7, 9), 16) / 255 };
      return { color: new THREE.Color(c), a: 1 };
    }
    if (Array.isArray(c)) return { color: C(c[0], c[1], c[2]), a: c.length > 3 ? c[3] : 1 };
    if (c && typeof c.r === 'number') return { color: C(c.r, c.g, c.b), a: typeof c.a === 'number' ? c.a : 1 };
    return { color: C(1, 1, 1), a: 1 };
  }
  // Unity CanvasScaler（参考 1600×900，match 0.6）对应的界面缩放
  function uiScale() {
    const w = Math.max(1, window.innerWidth || 1600), h = Math.max(1, window.innerHeight || 900);
    return Math.exp(0.4 * Math.log(w / 1600) + 0.6 * Math.log(h / 900));
  }

  // MapView.FlatDisc：带 UV 的水平方片（贴图决定形状）
  const discCache = new Map();
  function flatDisc(r) {
    let g = discCache.get(r);
    if (g) return g;
    const mb = new SG.MeshBuilder();
    mb.flatQuad(V(-r, 0, -r), V(-r, 0, r), V(r, 0, r), V(r, 0, -r), C(1, 1, 1));
    g = mb.toGeometry();
    discCache.set(r, g);
    return g;
  }

  // 移动 / 攻击范围的格子贴图：圆角方块，内部半透明、边缘发亮、外缘柔光
  let tileTex = null;
  function tileTexture() {
    if (tileTex) return tileTex;
    const n = 64, data = new Uint8Array(n * n * 4);
    const half = 0.84, rad = 0.24;
    for (let y = 0; y < n; y++)
      for (let x = 0; x < n; x++) {
        const u = (x + 0.5) / n * 2 - 1, v = (y + 0.5) / n * 2 - 1;
        const qx = Math.abs(u) - (half - rad), qy = Math.abs(v) - (half - rad);
        const d = Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) + Math.min(Math.max(qx, qy), 0) - rad;
        let a;
        // 内部填充接近 Unity SoftDot 中心的不透明度（高亮在草地上需清晰可辨）
        if (d < 0) a = 0.62 + 0.3 * M.smoothStep(0, 1, 1 + d / 0.35);
        else a = 0.75 * Math.exp(-d / 0.05);
        a = Math.max(a, Math.exp(-(d / 0.04) * (d / 0.04)));
        const i = (y * n + x) * 4;
        data[i] = data[i + 1] = data[i + 2] = 255;
        data[i + 3] = Math.round(M.clamp01(a) * 255);
      }
    tileTex = new THREE.DataTexture(data, n, n, THREE.RGBAFormat);
    tileTex.wrapS = tileTex.wrapT = THREE.ClampToEdgeWrapping;
    tileTex.magFilter = THREE.LinearFilter;
    tileTex.minFilter = THREE.LinearMipmapLinearFilter;
    tileTex.generateMipmaps = true;
    tileTex.needsUpdate = true;
    return tileTex;
  }

  // GLSL 式 smoothstep（注意 M.smoothStep 是 Unity 语义：在 from..to 间平滑插值）
  function sstep(e0, e1, x) { const t = M.clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); }

  // 火舌贴图：上尖下圆的水滴形（第 0 行 = 点精灵顶部，gl_PointCoord 原点在左上）
  let flameTex = null;
  function flameTexture() {
    if (flameTex) return flameTex;
    const n = 64, data = new Uint8Array(n * n * 4);
    const yc = 0.63, r = 0.31, yt = 0.02;
    for (let py = 0; py < n; py++)
      for (let px = 0; px < n; px++) {
        const x = (px + 0.5) / n - 0.5, y = (py + 0.5) / n;
        let d;
        if (y >= yc) d = Math.hypot(x, (y - yc) * 1.08) / r;
        else if (y > yt) d = Math.abs(x) / Math.max(1e-4, r * Math.pow((y - yt) / (yc - yt), 0.8));
        else d = 9;
        let a = 1 - sstep(0.42, 1, d);
        a *= sstep(yt, yt + 0.32, y);           // 火舌尖端渐隐
        const i = (py * n + px) * 4;
        data[i] = data[i + 1] = data[i + 2] = 255;
        data[i + 3] = Math.round(M.clamp01(a) * 255);
      }
    flameTex = new THREE.DataTexture(data, n, n, THREE.RGBAFormat);
    flameTex.wrapS = flameTex.wrapT = THREE.ClampToEdgeWrapping;
    flameTex.magFilter = THREE.LinearFilter;
    flameTex.minFilter = THREE.LinearMipmapLinearFilter;
    flameTex.generateMipmaps = true;
    flameTex.needsUpdate = true;
    return flameTex;
  }

  // ------------------------------------------------------------- 样式 --
  function injectStyle() {
    if (typeof document === 'undefined' || document.getElementById('sg-battle-view-style')) return;
    const s = document.createElement('style');
    s.id = 'sg-battle-view-style';
    // 层级：部队标签用负 z-index（标签层自身是层叠上下文），伤害飘字（z auto）始终压在最上面；
    // 选中部队 > 主将 > 其他。避让排布每帧再按优先级细排（见 BattleView._layoutLabels）。
    s.textContent = `
.sg-uinfo-anchor{position:absolute;left:0;top:0;width:0;height:0;pointer-events:none;z-index:-30;}
.sg-uinfo-anchor.cmd{z-index:-20;}
.sg-uinfo-anchor.sel{z-index:-10;}
.sg-uinfo-anchor .sg-stem{position:absolute;left:-1px;bottom:0;width:2px;height:0;opacity:0;border-radius:1px;
  background:linear-gradient(0deg,rgba(243,201,105,.25),rgba(243,201,105,.75));box-shadow:0 0 2px rgba(0,0,0,.6);}
.sg-uinfo-anchor .sg-stem::after{content:"";position:absolute;left:-2px;bottom:-3px;width:6px;height:6px;border-radius:50%;
  background:#f3c969;box-shadow:0 0 3px rgba(0,0,0,.7);}
.sg-uinfo{position:absolute;left:0;bottom:.35em;transform:translateX(-50%);box-sizing:border-box;
  font:600 clamp(11px,calc(1.0vmin + .45vmax),16px)/1.2 "PingFang SC","Microsoft YaHei","Noto Sans SC",system-ui,sans-serif,"WenQuanYi Zen Hei";
  color:#f5eddb;white-space:nowrap;min-width:6.6em;padding:.3em .6em .45em 1.0em;text-align:center;
  background:linear-gradient(180deg,rgba(34,31,42,.88),rgba(10,10,16,.82));
  border:1px solid rgba(243,201,105,.42);border-radius:.55em;
  box-shadow:0 .3em .9em rgba(0,0,0,.38),inset 0 1px 0 rgba(255,255,255,.09);
  -webkit-backdrop-filter:blur(3px);backdrop-filter:blur(3px);text-shadow:0 1px 2px rgba(0,0,0,.7);
  transition:opacity .2s ease;}
.sg-uinfo.cmd{border-color:rgba(243,201,105,.85);}
.sg-uinfo .sg-stripe{position:absolute;left:.25em;top:.25em;bottom:.25em;width:.34em;border-radius:.2em;
  box-shadow:0 0 .4em rgba(255,255,255,.18);}
.sg-uinfo .sg-star{color:#f3c969;margin-right:.18em;}
.sg-uinfo .sg-tr{font-size:.8em;font-weight:500;color:#d8cfbb;margin-left:.5em;font-variant-numeric:tabular-nums;}
.sg-uinfo .sg-bar{height:.36em;margin-top:.26em;background:rgba(0,0,0,.5);border-radius:.2em;overflow:hidden;
  box-shadow:inset 0 0 0 1px rgba(255,255,255,.05);}
.sg-uinfo .sg-bar>i{display:block;height:100%;border-radius:.2em;transition:width .35s ease;}
.sg-uinfo.sel{border-color:rgba(255,226,140,.95);box-shadow:0 .3em .9em rgba(0,0,0,.38),0 0 0 1px rgba(243,201,105,.35),inset 0 1px 0 rgba(255,255,255,.09);}
@media (max-height:540px){
  .sg-uinfo{font-size:11px;min-width:0;padding:.22em .5em .34em .9em;border-radius:.45em;
    box-shadow:0 .2em .5em rgba(0,0,0,.35),inset 0 1px 0 rgba(255,255,255,.08);}
  .sg-uinfo .sg-stripe{left:.22em;top:.22em;bottom:.22em;width:.3em;}
  .sg-uinfo .sg-star{margin-right:.08em;}
  .sg-uinfo .sg-tr{display:none;margin-left:.35em;}
  .sg-uinfo.sel .sg-tr{display:inline;}
  .sg-uinfo .sg-bar{height:.32em;margin-top:.2em;}
}
`;
    (document.head || document.documentElement).appendChild(s);
  }

  // ============================================================ 粒子系统 --
  const PARTICLE_VS = `
attribute vec4 aColor;
attribute float aSize;
uniform float uScale;
varying vec4 vColor;
void main() {
  vColor = aColor;
  vec4 mv = modelViewMatrix * vec4( position, 1.0 );
  gl_PointSize = max( 1.0, aSize * uScale / max( 0.05, - mv.z ) );
  gl_Position = projectionMatrix * mv;
}`;
  const PARTICLE_FS = `
uniform sampler2D uMap;
uniform float uSoft;
varying vec4 vColor;
void main() {
  float a = pow( texture2D( uMap, gl_PointCoord ).a, uSoft ) * vColor.a;
  if ( a < 0.004 ) discard;
  gl_FragColor = vec4( vColor.rgb, a );
}`;

  class ParticlePool {
    // blending：THREE.AdditiveBlending（火焰、火花）或 THREE.NormalBlending（烟尘）；map 缺省为柔光点
    constructor(max, blending, soft, renderOrder, map) {
      this.max = max;
      this.parts = [];
      this.pos = new Float32Array(max * 3);
      this.col = new Float32Array(max * 4);
      this.size = new Float32Array(max);
      const g = new THREE.BufferGeometry();
      this.aPos = new THREE.BufferAttribute(this.pos, 3); this.aPos.setUsage(THREE.DynamicDrawUsage);
      this.aCol = new THREE.BufferAttribute(this.col, 4); this.aCol.setUsage(THREE.DynamicDrawUsage);
      this.aSize = new THREE.BufferAttribute(this.size, 1); this.aSize.setUsage(THREE.DynamicDrawUsage);
      g.setAttribute('position', this.aPos);
      g.setAttribute('aColor', this.aCol);
      g.setAttribute('aSize', this.aSize);
      g.setDrawRange(0, 0);
      this.geometry = g;
      this.material = new THREE.ShaderMaterial({
        uniforms: { uMap: { value: map || SG.Gfx.softDotTexture }, uScale: { value: 600 }, uSoft: { value: soft } },
        vertexShader: PARTICLE_VS, fragmentShader: PARTICLE_FS,
        transparent: true, depthWrite: false, depthTest: true, blending, fog: false,
      });
      this.points = new THREE.Points(g, this.material);
      this.points.frustumCulled = false;
      this.points.renderOrder = renderOrder;
    }
    add(p) {
      if (this.parts.length >= this.max) this.parts.shift();
      this.parts.push(p);
    }
    update(dt, scale) {
      this.material.uniforms.uScale.value = scale;
      const P = this.parts;
      let n = 0;
      for (let i = 0; i < P.length; i++) {
        const p = P[i];
        p.age += dt;
        if (p.age >= p.life) continue;
        p.vy -= p.grav * GRAVITY * dt;
        if (p.drag) { const k = Math.max(0, 1 - p.drag * dt); p.vx *= k; p.vy *= k; p.vz *= k; }
        p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
        P[n++] = p;
      }
      P.length = n;
      for (let i = 0; i < n; i++) {
        const p = P[i];
        const t = p.age / p.life;
        const fade = p.fadeIn > 0 ? Math.min(1, t / p.fadeIn) : 1;
        this.pos[i * 3] = p.x; this.pos[i * 3 + 1] = p.y; this.pos[i * 3 + 2] = p.z;
        this.col[i * 4] = p.r + (p.r1 - p.r) * t; this.col[i * 4 + 1] = p.g + (p.g1 - p.g) * t; this.col[i * 4 + 2] = p.b + (p.b1 - p.b) * t;
        // 透明度 1 → 0（C# 渐变为线性；ac > 1 时前半段保持不透明，如火舌 a·(1−t²)）
        this.col[i * 4 + 3] = p.a * (p.ac === 1 ? 1 - t : 1 - Math.pow(t, p.ac)) * fade;
        this.size[i] = p.size * M.lerpUnclamped(1, p.sizeEnd, t);          // 尺寸 1 → sizeEnd
      }
      this.geometry.setDrawRange(0, n);
      if (n > 0) { this.aPos.needsUpdate = true; this.aCol.needsUpdate = true; this.aSize.needsUpdate = true; }
    }
    clear() { this.parts.length = 0; this.geometry.setDrawRange(0, 0); }
    dispose() { this.geometry.dispose(); this.material.dispose(); }
  }

  // 球内随机点
  function inSphere() {
    for (;;) {
      const x = Math.random() * 2 - 1, y = Math.random() * 2 - 1, z = Math.random() * 2 - 1;
      const d = x * x + y * y + z * z;
      if (d <= 1 && d > 1e-6) return { x, y, z, d: Math.sqrt(d) };
    }
  }

  // ============================================================ UnitVisual --
  // 一支部队：武将 + 若干士兵 + 头顶信息
  class UnitVisual {
    constructor(view, unit, col) {
      this.view = view; this.u = unit; this.col = col;
      this.group = new THREE.Group();
      this.group.name = 'Unit_' + unit.gen.name;
      view.group.add(this.group);
      this.material = SG.Gfx.newLowPoly();
      this.mesh = SG.Gfx.mesh(new THREE.BufferGeometry(), this.material, { castShadow: true, receiveShadow: true });
      this.group.add(this.mesh);
      this.ring = SG.Gfx.mesh(flatDisc(0.95), view.sideRing[unit.side === 0 ? 0 : 1], { castShadow: false, receiveShadow: false });
      this.ring.position.y = 0.04;
      this.ring.renderOrder = 2;
      this.group.add(this.ring);
      this.bucket = -1;
      this.pos = view.tile(unit.x, unit.y);      // 逻辑位置（动画写这里）
      this.facing = unit.side === 0 ? 90 : -90;  // Unity 偏航角（度）
      this.yaw = this.facing;
      this.flash = 0;
      this.heat = 0;                             // 火计灼烧的橙色发光（1 → 0）
      this.heatSeed = Math.random() * 10;
      this.confuseRing = null;
      this.labelPos = new THREE.Vector3();
      this.labelAlpha = 1;                       // C# WorldFollow.alphaMul
      this.holdLabel = false;                    // 控制器置 true 时，阵亡部队的标签暂不隐藏（单挑画面播完前）
      // 标签避让：目标偏移 (tdx, tdy↑, tfade) 与平滑后的当前值
      this.lbl = { dx: 0, dy: 0, fade: 1, tdx: 0, tdy: 0, tfade: 1, z: -30, shown: false };
      this.info = null; this.follow = null;
      this.group.position.copy(this.pos);
      this.group.rotation.y = SG.yawToThree(this.yaw);
      this._buildLabel();
      this.refresh();
    }

    get active() { return this.group.visible; }
    // 兼容 C# 写法 V.Vis(u).gameObject.activeSelf
    get gameObject() { const self = this; return { get activeSelf() { return self.group.visible; } }; }
    get transform() { return this.group; }

    _buildLabel() {
      if (typeof document === 'undefined' || !SG.UI || !SG.UI.follow) return;
      injectStyle();
      const u = this.u;
      const anchor = document.createElement('div');
      anchor.className = 'sg-uinfo-anchor';
      const card = document.createElement('div');
      card.className = 'sg-uinfo side' + u.side;
      const stripe = document.createElement('i');
      stripe.className = 'sg-stripe';
      stripe.style.background = cssHex(this.col);
      const name = document.createElement('div');
      name.className = 'sg-nm';
      const bar = document.createElement('div');
      bar.className = 'sg-bar';
      const fill = document.createElement('i');
      fill.style.background = `linear-gradient(90deg, ${SIDE_BAR[u.side === 0 ? 0 : 1]}, ${SG.Gfx.shade(SIDE_BAR[u.side === 0 ? 0 : 1], 0.35).getStyle()})`;
      bar.appendChild(fill);
      card.appendChild(stripe); card.appendChild(name); card.appendChild(bar);
      const stem = document.createElement('i');   // 标签被挤开时连回部队的引线
      stem.className = 'sg-stem';
      anchor.appendChild(stem);
      anchor.appendChild(card);
      this.info = {
        anchor, card, name, fill, stem, lastText: '', lastW: -1, sel: false,
        w: 0, h: 0, gap: 0, dirty: true, tf: '', stemH: -1, z: null,
      };
      this.follow = SG.UI.follow(anchor, () => this.labelPos, { offsetY: 0, hideBeyond: 90 });
      this._updateLabelPos();
    }
    _updateLabelPos() {
      // C#：WorldFollow 偏移 74（参考分辨率像素）≈ 旗顶之上
      this.labelPos.copy(this.group.position);
      this.labelPos.y += 2.25;
    }

    refresh() {
      const u = this.u;
      const b = M.clamp(Math.ceil(u.troops / 350), u.alive ? 1 : 0, 12);
      if (b !== this.bucket) {
        this.bucket = b;
        const mb = new SG.MeshBuilder();
        SG.Models.commander(mb, V(0, 0, 0.35), this.col, 1.5);
        for (let i = 0; i < b; i++) {
          const row = M.idiv(i, 4), cIdx = i % 4;
          const p = V((cIdx - 1.5) * 0.38 + (row % 2) * 0.1, 0, -0.25 - row * 0.36);
          SG.Models.soldier(mb, p, this.col, true, 1.3);
        }
        const old = this.mesh.geometry;
        this.mesh.geometry = mb.toGeometry();
        if (old) old.dispose();
      }
      if (this.info) {
        const I = this.info;
        const text = (u.commander ? '<span class="sg-star">★</span>' : '') + SG.esc(u.gen.name) + '<span class="sg-tr">' + u.troops + '</span>';
        if (text !== I.lastText) { I.name.innerHTML = text; I.lastText = text; I.dirty = true; }
        const max = Math.max(1, SG.maxTroops ? SG.maxTroops(u.gen) : (u.gen.maxTroops || u.troops || 1));
        const w = M.clamp01(u.troops / max);
        if (w !== I.lastW) { I.fill.style.width = (w * 100).toFixed(1) + '%'; I.lastW = w; }
        I.card.classList.toggle('cmd', !!u.commander);
        I.anchor.classList.toggle('cmd', !!u.commander);
        I.card.style.visibility = u.alive ? '' : 'hidden';
      }
      if (u.confused > 0 && !this.confuseRing) {
        this.confuseRing = SG.Gfx.mesh(flatDisc(0.6), SG.Gfx.unlit(C(0.75, 0.5, 1), SG.Gfx.ringTexture, 0.9), { castShadow: false, receiveShadow: false });
        this.confuseRing.position.y = 1.6;
        this.confuseRing.renderOrder = 5;
        this.group.add(this.confuseRing);
      }
      if (u.confused <= 0 && this.confuseRing) {
        this.group.remove(this.confuseRing);
        this.confuseRing.material.dispose();
        this.confuseRing = null;
      }
    }

    // dir 为 three 世界向量
    face(dir) {
      if (dir.x * dir.x + dir.y * dir.y + dir.z * dir.z > 0.001) this.facing = Math.atan2(dir.x, -dir.z) * M.rad2deg;
    }
    rest() { }
    flashHit() { this.flash = 1; }
    burn() { this.heat = 1; }
    // 选中（光标所在）部队：标签置顶、描边加亮，窄屏时也显示兵力
    setSelected(on) {
      const I = this.info;
      if (!I || I.sel === on) return;
      I.sel = on;
      I.card.classList.toggle('sel', on);
      I.anchor.classList.toggle('sel', on);
      I.dirty = true;
    }

    update(dt, time) {
      const u = this.u;
      this.yaw = M.lerpAngle(this.yaw, this.facing, dt * 10);
      this.group.rotation.y = SG.yawToThree(this.yaw);
      let shake = 0;
      if (this.flash > 0) {
        this.flash = Math.max(0, this.flash - dt * 4);
        shake = Math.sin(time * 80) * 0.03 * this.flash;
      }
      // 受击闪白（C# _Flash）与火计灼烧的橙色闪烁合成到同一个 flash / flashColor 上
      const burn = this.heat > 0 ? M.smoothStep(0, 1, this.heat) * 0.55 * (0.86 + 0.14 * Math.sin(time * 26 + this.heatSeed)) : 0;
      if (this.heat > 0) this.heat = Math.max(0, this.heat - dt * 0.42);
      const fc = this.material.flashColor;
      if (fc && fc.isColor) {
        if (burn > 0) fc.copy(HEAT_TINT).lerp(WHITE, this.flash / (this.flash + burn));
        else if (fc.r !== 1 || fc.g !== 1 || fc.b !== 1) fc.copy(WHITE);
      }
      this.material.flash = Math.max(this.flash, burn);
      this.group.position.set(this.pos.x + shake, this.pos.y, this.pos.z);
      if (this.confuseRing) this.confuseRing.rotation.y -= 200 * M.deg2rad * dt;
      this.labelAlpha = ((u.alive || this.holdLabel) && this.group.visible) ? (u.acted && u.side === this.view.model.side ? 0.55 : 1) : 0;
      if (this.follow) {
        this._updateLabelPos();
        this.follow.alpha = this.labelAlpha * this.lbl.fade;   // BattleView._layoutLabels 随后按避让结果再设
      }
    }

    dispose() {
      if (this.follow) { try { this.follow.remove(); } catch (e) { /* 忽略 */ } this.follow = null; }
      if (this.info && this.info.anchor.parentNode) this.info.anchor.parentNode.removeChild(this.info.anchor);
      this.info = null;
      if (this.mesh.geometry) this.mesh.geometry.dispose();
      this.material.dispose();
      if (this.confuseRing) this.confuseRing.material.dispose();
      if (this.group.parent) this.group.parent.remove(this.group);
    }
  }

  // ============================================================ BattleView --
  class BattleView {
    constructor(model) {
      this.model = model;
      this.M = model;                 // C# 字段名
      this.T = T;
      this.group = new THREE.Group();
      this.group.name = 'BattleView';
      this.visuals = new Map();       // BUnit → UnitVisual
      this.highlights = [];
      this.hlMats = new Map();
      this.time = 0;
      this.disposed = false;
      this.emitters = [];
      this.burnFx = new Map();        // "x,y" → { emitters }
      this.temp = [];                 // 临时网格（特效）
      this.cursorUnit = null;
      this._lblKey = '';              // 视口尺寸变化时重新量标签
      this._lblMeasureT = 0;
      this.build();
      if (SG.Gfx && SG.Gfx.scene) SG.Gfx.scene.add(this.group);
    }
    get root() { return this.group; }

    get boardCenter() { return SG.U(ORIGIN.x + this.model.W * T / 2, 0, ORIGIN.z + this.model.H * T / 2); }

    topH(x, y) {
      const Tn = TR(), m = this.model;
      switch (m.map[x][y]) {
        case Tn.River: return -0.25;
        case Tn.Wall: return 1.4;
        case Tn.Mountain: return 0.6;
        case Tn.Hill: return 0.45;
        case Tn.Gate: case Tn.Castle: return 0.2;
        default: return 0.08 + m.height[x][y] * 0.6;
      }
    }
    // 格子中心（three 世界坐标）
    tile(x, y) {
      const Tn = TR(), t = this.model.map[x][y];
      const h = t === Tn.River ? -0.05 : t === Tn.Wall ? 1.4 : this.topH(x, y);
      return SG.U(ORIGIN.x + x * T + T / 2, h, ORIGIN.z + y * T + T / 2);
    }

    // ---------------------------------------------------------- 构建 --
    build() {
      const m = this.model, Tn = TR();
      const mb = new SG.MeshBuilder();
      const deco = new SG.MeshBuilder();
      const setup = m.setup || {};
      const rnd = SG.SeededRandom(setup.target ? setup.target.id : 0);
      const atkCol = this._factionColor(setup.attacker);
      const defCol = this._factionColor(setup.defender);
      const ox = ORIGIN.x, oz = ORIGIN.z;
      const inset = 0.05, bot = -0.6;
      for (let x = 0; x < m.W; x++)
        for (let y = 0; y < m.H; y++) {
          const t = m.map[x][y];
          const h = this.topH(x, y);
          const px = ox + x * T, pz = oz + y * T;
          const c = BattleView.tileColor(t, rnd.nextDouble());
          const a = V(px + inset, h, pz + inset), b = V(px + inset, h, pz + T - inset);
          const cc = V(px + T - inset, h, pz + T - inset), d = V(px + T - inset, h, pz + inset);
          mb.quad(a, b, cc, d, c);
          // 侧面（让高低差可见）
          const dark = shade(c, -0.25);
          mb.quad(V(px + inset, bot, pz + inset), a, d, V(px + T - inset, bot, pz + inset), dark);
          mb.quad(V(px + T - inset, bot, pz + inset), d, cc, V(px + T - inset, bot, pz + T - inset), dark);
          mb.quad(V(px + T - inset, bot, pz + T - inset), cc, b, V(px + inset, bot, pz + T - inset), dark);
          mb.quad(V(px + inset, bot, pz + T - inset), b, a, V(px + inset, bot, pz + inset), dark);
          const cx = px + T / 2, cz = pz + T / 2;
          switch (t) {
            case Tn.Forest:
              for (let k = 0; k < 4; k++) {
                const dx = (rnd.nextDouble() - 0.5) * 1.3, dz = (rnd.nextDouble() - 0.5) * 1.3;
                const s = 0.9 + rnd.nextDouble() * 0.5;
                const leaf = shade(C(0.25, 0.5, 0.27), (rnd.nextDouble() - 0.5) * 0.15);
                SG.Models.tree(deco, V(cx + dx, h, cz + dz), s, leaf, rnd.nextDouble() < 0.4, k + x * 7 + y * 13);
              }
              break;
            case Tn.Hill:
              deco.blob(V(cx, h - 0.1, cz), V(0.85, 0.35, 0.85), C(0.55, 0.6, 0.35), x * 31 + y);
              break;
            case Tn.Mountain:
              deco.cone(V(cx - 0.2, h - 0.1, cz + 0.1), 0.9, 1.9 + rnd.nextDouble() * 0.6, 5, C(0.55, 0.53, 0.5));
              deco.cone(V(cx + 0.45, h - 0.1, cz - 0.35), 0.55, 1.1, 5, C(0.5, 0.48, 0.45));
              deco.cone(V(cx - 0.2, h + 1.25, cz + 0.1), 0.32, 0.9, 5, C(0.95, 0.96, 0.98));
              break;
            case Tn.Wall:
              deco.box(V(cx, h - 0.6, cz), V(T, 1.6, T), C(0.66, 0.63, 0.57));
              for (let k = -1; k <= 1; k += 2) deco.box(V(cx + k * 0.55, h + 0.32, cz + k * 0.55), V(0.45, 0.35, 0.45), C(0.7, 0.67, 0.6));
              break;
            case Tn.Gate: {
              // 城门顺着城墙走向摆放（两侧为城墙时旋转 90°）
              // 与 C# 一致：门楼一律沿 x 摆放（红色门面朝向镜头）。东西两侧城墙上的城门也不旋转，
              // 否则红色门身会藏在屋檐和两侧墙头之下，只剩一个深蓝屋顶。
              deco.box(V(cx, h + 0.75, cz), V(T * 0.9, 1.1, 0.4), C(0.62, 0.2, 0.16));
              deco.chineseRoof(V(cx, h + 1.3, cz), 2.2, 1.2, 0.7, C(0.22, 0.26, 0.34));
              break;
            }
            case Tn.Castle:
              deco.box(V(cx, h + 0.15, cz + 0.55), V(1.6, 0.3, 0.7), C(0.75, 0.72, 0.64));
              deco.box(V(cx, h + 0.75, cz + 0.6), V(1.2, 0.9, 0.5), C(0.66, 0.22, 0.18));
              deco.chineseRoof(V(cx, h + 1.2, cz + 0.6), 1.9, 1.0, 0.8, C(0.85, 0.65, 0.2));
              deco.flag(V(cx + 0.8, h, cz + 0.8), 2.4, 0.8, 0.55, defCol);
              break;
          }
        }
      // 棋盘底板：格子之间的缝隙向下看时是深色的“砖缝”，而不是透出背景
      {
        const BW = m.W * T, BH = m.H * T, uy = -0.55;
        mb.quad(V(ox, uy, oz), V(ox, uy, oz + BH), V(ox + BW, uy, oz + BH), V(ox + BW, uy, oz), C(0.17, 0.2, 0.12));
      }
      this.buildGrass(deco);
      const board = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly(), { castShadow: false, receiveShadow: true });
      board.name = 'Board';
      this.group.add(board);
      const decoMesh = SG.Gfx.mesh(deco.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      decoMesh.name = 'Deco';
      this.group.add(decoMesh);
      // 河面
      const wm = new SG.MeshBuilder();
      for (let x = 0; x < m.W; x++) for (let y = 0; y < m.H; y++)
        if (m.map[x][y] === Tn.River) {
          const px = ox + x * T, py = -0.05, pz = oz + y * T;
          wm.flatQuad(V(px, py, pz), V(px, py, pz + T), V(px + T, py, pz + T), V(px + T, py, pz), C(0.4, 0, 0));
        }
      if (wm.count > 0) {
        const river = SG.Gfx.mesh(wm.toGeometry(), SG.Gfx.water(), { castShadow: false, receiveShadow: false });
        river.name = 'River';
        river.renderOrder = 1;
        this.group.add(river);
      }
      this.buildSurroundings(rnd);
      // 光标
      this.cursor = SG.Gfx.mesh(flatDisc(T * 0.62), SG.Gfx.unlit(C(1, 0.85, 0.35), SG.Gfx.ringTexture, 1), { castShadow: false, receiveShadow: false });
      this.cursor.name = 'Cursor';
      this.cursor.renderOrder = 4;
      this.cursor.visible = false;
      this.group.add(this.cursor);
      this.cursorGlow = SG.Gfx.mesh(flatDisc(T * 0.62), SG.Gfx.additive(C(1, 0.78, 0.35)), { castShadow: false, receiveShadow: false });
      this.cursorGlow.material.opacity = 0.32;
      this.cursorGlow.renderOrder = 3;
      this.cursorGlow.visible = false;
      this.group.add(this.cursorGlow);
      this.hlGeo = flatDisc(T * 0.5);
      // 部队脚下的阵营光环（攻方蓝、守方红，与兵力条同色）
      this.sideRing = [0, 1].map(i => {
        const m = SG.Gfx.unlit(SIDE_BAR[i], SG.Gfx.ringTexture, 0.7);
        return m;
      });
      // 粒子与光（绘制顺序：烟尘 → 火底热光 → 火舌 → 火花 / 炽热火芯）
      this.glow = new ParticlePool(1200, THREE.AdditiveBlending, 1.0, 14);   // 火花、法术光点、火芯
      // 火焰：白天场景里纯叠加会被洗成一片亮黄，这里火舌用普通混合 + 水滴形贴图，
      // 底下垫一层叠加的热光，上面再叠炽热的火芯
      this.flame = new ParticlePool(1400, THREE.NormalBlending, 1.0, 13, flameTexture());
      this.heat = new ParticlePool(500, THREE.AdditiveBlending, 1.0, 12);
      this.dust = new ParticlePool(900, THREE.NormalBlending, 0.6, 11);      // 烟尘
      this.group.add(this.dust.points);
      this.group.add(this.heat.points);
      this.group.add(this.flame.points);
      this.group.add(this.glow.points);
      this.lights = [];
      for (let i = 0; i < 2; i++) {
        // C#：color (1,0.5,0.2)、range 6、intensity 3（按 Unity 的衰减曲线换算为 decay 1）
        const l = new THREE.PointLight(C(1, 0.5, 0.2), 0, 6, 1);
        l.castShadow = false;
        l.userData = { busy: false, base: 0, cur: 0, seed: i * 17.3, glow: null };
        this.group.add(l);
        this.lights.push(l);
      }
      // 部队
      for (const u of m.units) this.createUnit(u, u.side === 0 ? atkCol : defCol);
    }

    // 平地上的草丛与小花（独立的随机序列，不影响 C# 的地形随机数顺序）
    buildGrass(deco) {
      const m = this.model, Tn = TR();
      const setup = m.setup || {};
      const rnd = SG.SeededRandom(((setup.target ? setup.target.id : 0) | 0) * 7919 + 17);
      const flowers = [C(0.98, 0.95, 0.85), C(1, 0.82, 0.3), C(0.95, 0.55, 0.6), C(0.75, 0.62, 0.95)];
      const blade = (bx, by, bz, h, lean, ang, col) => {
        const ca = Math.cos(ang), sa = Math.sin(ang), w = 0.035;
        const a = V(bx - ca * w, by, bz - sa * w), b = V(bx + ca * w, by, bz + sa * w);
        const tip = V(bx + lean * -sa, by + h, bz + lean * ca);
        deco.tri(a, tip, b, col); deco.tri(a, b, tip, col);
      };
      for (let x = 0; x < m.W; x++)
        for (let y = 0; y < m.H; y++) {
          if (m.map[x][y] !== Tn.Plain) continue;
          const h = this.topH(x, y);
          const cx = ORIGIN.x + x * T + T / 2, cz = ORIGIN.z + y * T + T / 2;
          const tufts = 3 + rnd.next(3);
          for (let k = 0; k < tufts; k++) {
            let dx = (rnd.nextDouble() * 2 - 1) * 0.85, dz = (rnd.nextDouble() * 2 - 1) * 0.85;
            if (Math.abs(dx) < 0.5 && Math.abs(dz) < 0.6) dx = dx < 0 ? -0.5 - rnd.nextDouble() * 0.3 : 0.5 + rnd.nextDouble() * 0.3;
            const col = shade(C(0.34, 0.56, 0.25), (rnd.nextDouble() - 0.5) * 0.2);
            const s = 0.75 + rnd.nextDouble() * 0.5;
            for (let j = 0; j < 3; j++)
              blade(cx + dx + (rnd.nextDouble() - 0.5) * 0.08, h, cz + dz + (rnd.nextDouble() - 0.5) * 0.08,
                (0.13 + rnd.nextDouble() * 0.08) * s, (rnd.nextDouble() - 0.5) * 0.1, rnd.nextDouble() * Math.PI, shade(col, j * 0.05));
          }
          if (rnd.nextDouble() < 0.35) {
            const fc = flowers[rnd.next(flowers.length)];
            const n = 1 + rnd.next(3);
            for (let k = 0; k < n; k++) {
              const fx = (rnd.nextDouble() * 2 - 1) * 0.8, fz = (rnd.nextDouble() < 0.5 ? -1 : 1) * (0.62 + rnd.nextDouble() * 0.25);
              deco.box(V(cx + fx, h + 0.05, cz + fz), V(0.06, 0.04, 0.06), fc, 0.1);
              deco.box(V(cx + fx, h + 0.015, cz + fz), V(0.015, 0.05, 0.015), C(0.3, 0.5, 0.22), 0);
            }
          }
        }
    }

    _factionColor(idx) {
      const g = SG.G;
      const f = (g && idx !== undefined && idx >= 0 && g.factions) ? g.factions[idx] : null;
      if (!f) return C(0.5, 0.5, 0.5); // Color.gray
      const hex = SG.factionColor ? SG.factionColor(f) : f.color;
      return SG.Gfx.color(hex || '#808080');
    }

    static tileColor(t, r) {
      const Tn = TR();
      let c;
      switch (t) {
        case Tn.Forest: c = C(0.33, 0.55, 0.3); break;
        case Tn.Hill: c = C(0.6, 0.62, 0.38); break;
        case Tn.Mountain: c = C(0.52, 0.5, 0.46); break;
        case Tn.River: c = C(0.55, 0.5, 0.38); break;
        case Tn.Wall: c = C(0.62, 0.6, 0.55); break;
        case Tn.Gate: c = C(0.62, 0.58, 0.5); break;
        case Tn.Castle: c = C(0.7, 0.66, 0.56); break;
        default: c = C(0.47, 0.68, 0.36); break;
      }
      return shade(c, (r - 0.5) * 0.08);
    }

    buildSurroundings(rnd) {
      const m = this.model;
      const mb = new SG.MeshBuilder();
      const BW = m.W * T, BH = m.H * T;
      const size = 3, x0 = -40, z0 = -30, x1 = BW + 40, z1 = BH + 34;
      const ox = ORIGIN.x, oy = ORIGIN.y, oz = ORIGIN.z;
      const h = (px, pz) => {
        const de = Math.max(Math.max(-px, px - BW), Math.max(-pz, pz - BH));
        const v = Math.max(-0.5, (M.perlinNoise(px * 0.08 + 3, pz * 0.08 + 7) - 0.4) * 2 + Math.max(0, de - 6) * 0.25) - 0.3;
        // 紧贴棋盘的一圈压低到木框之下，避免地形穿过棋盘
        return M.lerpUnclamped(-0.42, v, M.smoothStep(0, 1, (de - 0.4) / 3.6));
      };
      const g0 = C(0.42, 0.6, 0.32), g1 = C(0.55, 0.55, 0.45);
      for (let x = x0; x < x1; x += size)
        for (let z = z0; z < z1; z += size) {
          // 完全被棋盘覆盖的格子跳过（部分重叠的保留，以免棋盘边缘露出缝隙）
          const inside = x >= -0.01 && z >= -0.01 && x + size <= BW + 0.01 && z + size <= BH + 0.01;
          if (inside) continue;
          const dEdge = Math.max(Math.max(-x, x - BW), Math.max(-z, z - BH));
          const a = V(ox + x, oy + h(x, z), oz + z), b = V(ox + x, oy + h(x, z + size), oz + z + size);
          const c = V(ox + x + size, oy + h(x + size, z + size), oz + z + size), d = V(ox + x + size, oy + h(x + size, z), oz + z);
          const col = lerpColor(g0, g1, M.inverseLerp(4, 16, (a.y + c.y) / 2 + 2));
          mb.tri(a, b, c, shade(col, (rnd.nextDouble() - 0.5) * 0.08));
          mb.tri(a, c, d, shade(col, (rnd.nextDouble() - 0.5) * 0.08));
          if (rnd.nextDouble() < 0.18 && dEdge > 1.5) {
            const tx = x + 1.5, tz = z + 1.5;
            SG.Models.tree(mb, V(ox + tx, oy + h(tx, tz), oz + tz), 1.2 + rnd.nextDouble(), C(0.24, 0.46, 0.26), rnd.nextDouble() < 0.5, Math.trunc(x * 13 + z));
          }
        }
      const sur = SG.Gfx.mesh(mb.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      sur.name = 'Surround';
      this.group.add(sur);
      // 棋盘边框
      const frame = new SG.MeshBuilder();
      const wood = C(0.36, 0.24, 0.14);
      frame.box(V(ox + BW / 2, 0.05, oz - 0.25), V(BW + 1, 0.4, 0.5), wood);
      frame.box(V(ox + BW / 2, 0.05, oz + BH + 0.25), V(BW + 1, 0.4, 0.5), wood);
      frame.box(V(ox - 0.25, 0.05, oz + BH / 2), V(0.5, 0.4, BH), wood);
      frame.box(V(ox + BW + 0.25, 0.05, oz + BH / 2), V(0.5, 0.4, BH), wood);
      const fr = SG.Gfx.mesh(frame.toGeometry(), SG.Gfx.lowPoly(), { castShadow: true, receiveShadow: true });
      fr.name = 'Frame';
      this.group.add(fr);
    }

    createUnit(u, col) {
      const uv = new UnitVisual(this, u, col);
      this.visuals.set(u, uv);
      return uv;
    }
    vis(u) { return this.visuals.get(u) || null; }
    isShown(u) { const v = this.visuals.get(u); return !!(v && v.group.visible); }

    // ---------------------------------------------------------- 高亮 --
    clearHighlights() {
      for (const h of this.highlights) this.group.remove(h);
      this.highlights.length = 0;
    }
    highlight(tiles, cssColor, alpha) {
      if (!tiles) return;
      const pc = parseColor(cssColor);
      const a = alpha === undefined || alpha === null ? pc.a : alpha;
      const key = cssHex(pc.color) + '|' + a.toFixed(3);
      let mat = this.hlMats.get(key);
      if (!mat) {
        mat = SG.Gfx.unlit(pc.color, tileTexture(), a);
        mat.userData.baseOpacity = a;
        this.hlMats.set(key, mat);
      }
      for (const t of tiles) {
        if (!t || !this._inBounds(t.x, t.y)) continue;
        const hl = new THREE.Mesh(this.hlGeo, mat);
        hl.position.copy(this.tile(t.x, t.y));
        hl.position.y += 0.06;
        hl.renderOrder = 2;
        this.group.add(hl);
        this.highlights.push(hl);
      }
    }
    setCursor(u) {
      const on = !!u;
      if (this.cursorUnit !== (u || null)) {
        this.cursorUnit = u || null;
        for (const v of this.visuals.values()) v.setSelected(v.u === this.cursorUnit);
      }
      this.cursor.visible = on;
      this.cursorGlow.visible = on;
      if (on) {
        this.cursor.position.copy(this.tile(u.x, u.y));
        this.cursor.position.y += 0.08;
        this.cursorGlow.position.copy(this.cursor.position);
        this.cursorGlow.position.y -= 0.01;
      }
    }

    _inBounds(x, y) { return x >= 0 && y >= 0 && x < this.model.W && y < this.model.H; }

    tileFromScreen(screenX, screenY) {
      if (!SG.Gfx || !SG.Gfx.camera) return null;
      const ray = SG.Gfx.screenRay(screenX, screenY);
      const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -(ORIGIN.y + 0.3));
      const hit = new THREE.Vector3();
      if (!ray.intersectPlane(plane, hit)) return null;
      const ux = hit.x - ORIGIN.x, uz = -hit.z - ORIGIN.z;
      const x = Math.floor(ux / T), y = Math.floor(uz / T);
      if (!this._inBounds(x, y)) return null;
      return { x, y };
    }

    // ---------------------------------------------------------- 每帧 --
    update(dt) {
      if (this.disposed) return;
      if (!(dt >= 0)) dt = 0;
      dt = Math.min(dt, 0.1);
      this.time += dt;
      const time = this.time;
      if (this.cursor.visible) {
        this.cursor.rotation.y -= 40 * M.deg2rad * dt;
        const s = 1 + Math.sin(time * 4) * 0.035;
        this.cursorGlow.scale.set(s, 1, s);
        this.cursorGlow.material.opacity = 0.26 + Math.sin(time * 4) * 0.08;
      }
      // 高亮轻微呼吸
      if (this.highlights.length) {
        const k = 0.88 + Math.sin(time * 3.2) * 0.12;
        for (const mat of this.hlMats.values()) mat.opacity = mat.userData.baseOpacity * k;
      }
      for (const v of this.visuals.values()) v.update(dt, time);
      this._layoutLabels(dt);
      this._updateBurning();
      this._updateEmitters(dt);
      this._updateLights(dt, time);
      const scale = this._pointScale();
      this.glow.update(dt, scale);
      this.flame.update(dt, scale);
      this.heat.update(dt, scale);
      this.dust.update(dt, scale);
    }

    // ------------------------------------------------------ 标签避让 --
    // 部队头顶标签互相遮挡时（窄屏上格距只有约 30px）按优先级排布：选中部队 > 主将 > 靠近镜头的部队。
    // 先放的标签留在原位；后放的若与已放的重叠，就在小范围内上移 / 下移 / 左右平移到最近的空位
    // （上移时显示一根引线连回部队），实在放不下则淡化并压到下层。层级同样按优先级排列。
    _layoutLabels(dt) {
      if (typeof document === 'undefined' || !SG.Gfx || typeof SG.Gfx.worldToScreen !== 'function') return;
      const key = (window.innerWidth | 0) + 'x' + (window.innerHeight | 0);
      let remeasure = false;
      if (key !== this._lblKey) { this._lblKey = key; remeasure = true; }
      if (this.time - this._lblMeasureT > 2) { this._lblMeasureT = this.time; remeasure = true; }   // 字体加载后的宽度变化
      const items = [];
      let order = 0;
      for (const v of this.visuals.values()) {
        const I = v.info, L = v.lbl;
        order++;
        if (!I || !v.follow) continue;
        if (!(v.labelAlpha > 0.001)) { L.shown = false; continue; }
        const s = SG.Gfx.worldToScreen(v.labelPos);
        if (!s || !s.visible || !(s.dist < 90) || !isFinite(s.x) || !isFinite(s.y)) { L.shown = false; continue; }
        if (I.dirty || remeasure) {
          I.w = I.card.offsetWidth; I.h = I.card.offsetHeight;
          I.gap = (parseFloat(getComputedStyle(I.card).fontSize) || 12) * 0.35;   // .sg-uinfo 的 bottom:.35em
          I.dirty = false;
        }
        const pr = (v.u === this.cursorUnit ? 2 : 0) + (v.u.commander ? 1 : 0);
        items.push({ v, x: s.x, y: s.y, pr, order });
      }
      // 优先级高的先放；同级时靠近镜头（屏幕上更低）的先放，后面的部队标签自然向上让开
      items.sort((a, b) => (b.pr - a.pr) || (b.y - a.y) || (a.order - b.order));
      const placed = [];
      const m = 2;   // 标签之间至少留 2px
      const hits = (list, l, t, r, b) => {
        for (const p of list) if (l < p.r + m && r > p.l - m && t < p.b + m && b > p.t - m) return true;
        return false;
      };
      const k = Math.min(1, dt * 14), kf = Math.min(1, dt * 8);
      for (let rank = 0; rank < items.length; rank++) {
        const it = items[rank], v = it.v, I = v.info, L = v.lbl;
        const w = I.w, h = I.h;
        const l0 = it.x - w / 2, r0 = it.x + w / 2, b0 = it.y - I.gap, t0 = b0 - h;
        let best = null;
        if (!hits(placed, l0, t0, r0, b0)) best = { dx: 0, dy: 0 };
        else {
          const maxUp = h * 1.6 + 4, maxDown = h * 0.45, maxSide = w * 0.42;
          // 只看附近的标签：候选偏移取“贴着它们的边”的位置
          const near = placed.filter(p => p.l < r0 + maxSide + m && p.r > l0 - maxSide - m && p.t < b0 + maxDown + m && p.b > t0 - maxUp - m);
          const dys = [0], dxs = [0];
          for (const p of near) {
            const up = b0 - p.t + m, down = p.b + m - t0, left = p.l - m - r0, right = p.r + m - l0;
            if (up > 0 && up <= maxUp) dys.push(up);
            if (down > 0 && down <= maxDown) dys.push(-down);
            if (left < 0 && -left <= maxSide) dxs.push(left);
            if (right > 0 && right <= maxSide) dxs.push(right);
          }
          let bestCost = Infinity;
          for (const dy of dys)
            for (const dx of dxs) {
              if (hits(near, l0 + dx, t0 - dy, r0 + dx, b0 - dy)) continue;
              // 上移最自然，下移会挡住自己的部队，平移次之；与上一帧的目标接近者优先（防抖）
              const cost = (dy >= 0 ? dy : -dy * 1.8) + Math.abs(dx) * 1.25
                + (Math.abs(dx - L.tdx) + Math.abs(dy - L.tdy)) * 0.3;
              if (cost < bestCost) { bestCost = cost; best = { dx, dy }; }
            }
        }
        if (best) {
          it.rect = { l: l0 + best.dx, r: r0 + best.dx, t: t0 - best.dy, b: b0 - best.dy };
          placed.push(it.rect);
          L.tdx = best.dx; L.tdy = best.dy; L.tfade = 1; L.z = -(1 + rank);
        } else {
          it.rect = null;
          L.tdx = 0; L.tdy = 0; L.tfade = 0.3; L.z = -(1 + items.length + rank);
        }
      }
      for (const it of items) {
        const v = it.v, I = v.info, L = v.lbl;
        if (!L.shown) { L.dx = L.tdx; L.dy = L.tdy; L.fade = L.tfade; L.shown = true; }
        else {
          L.dx += (L.tdx - L.dx) * k; L.dy += (L.tdy - L.dy) * k; L.fade += (L.tfade - L.fade) * kf;
        }
        const tf = 'translate(-50%,0) translate3d(' + L.dx.toFixed(1) + 'px,' + (-L.dy).toFixed(1) + 'px,0)';
        if (tf !== I.tf) { I.card.style.transform = tf; I.tf = tf; }
        // 引线的落点（部队上方）若被别的标签盖住，就不画，免得看起来连到了别人的标签
        let stemOk = L.dy > 2;
        if (stemOk) for (const p of placed) if (p !== it.rect && it.x > p.l && it.x < p.r && it.y > p.t && it.y - I.gap < p.b) { stemOk = false; break; }
        const stemH = stemOk ? Math.round(L.dy + I.gap) : 0;
        if (stemH !== I.stemH) {
          I.stemH = stemH;
          I.stem.style.height = stemH + 'px';
          I.stem.style.opacity = stemH ? M.clamp01((L.dy - 2) / 6).toFixed(2) : '0';
        }
        if (L.z !== I.z) { I.anchor.style.zIndex = String(L.z); I.z = L.z; }
        v.follow.alpha = v.labelAlpha * L.fade;
      }
    }

    _pointScale() {
      const r = SG.Gfx.renderer, cam = SG.Gfx.camera;
      if (!r || !cam) return 600;
      const h = r.domElement.height || 720;
      return h / (2 * Math.tan(cam.fov * M.deg2rad / 2));
    }

    // ---------------------------------------------------------- 动画 --
    // 按帧推进：fn(t)，t 从 0 递增，最后一帧不一定为 1（与 C# for 循环一致）
    async _loop(seconds, fn) {
      let t = 0, last = now();
      while (t < 1) {
        if (this.disposed) return;
        fn(t);
        await SG.frame();
        const n = now();
        t += Math.min(0.1, n - last) / seconds;
        last = n;
      }
    }
    async _sleep(seconds) {
      const end = now() + seconds;
      while (now() < end) {
        if (this.disposed) return;
        await SG.frame();
      }
    }

    async moveUnit(u, path) {
      const v = this.vis(u);
      if (!v || !path) return;
      sfx('march', 0.4);
      for (let i = 1; i < path.length; i++) {
        const a = this.tile(path[i - 1].x, path[i - 1].y), b = this.tile(path[i].x, path[i].y);
        v.face(b.clone().sub(a));
        await this._loop(0.18, t => {
          v.pos.lerpVectors(a, b, t);
          v.pos.y += Math.abs(Math.sin(t * Math.PI)) * 0.12;
          // 行军扬尘
          if (Math.random() < 0.35) this._dustPuff(v.pos, 0.6);
        });
        v.pos.copy(b);
      }
      v.rest();
    }

    async lunge(a, t) {
      const v = this.vis(a);
      if (!v) return;
      const from = this.tile(a.x, a.y), to = this.tile(t.x, t.y);
      v.face(to.clone().sub(from));
      await this._loop(0.28, k => { v.pos.lerpVectors(from, to, Math.sin(k * Math.PI) * 0.38); });
      v.pos.copy(from);
    }

    hit(u, dmg, big) {
      const v = this.vis(u);
      if (!v) return;
      v.flashHit();
      const p = this.tile(u.x, u.y);
      this.burst(p.clone().add(new THREE.Vector3(0, 0.6, 0)), C(1, 0.9, 0.6), 14, 2.5);
      this.floatText(p.clone().add(new THREE.Vector3(0, 1.6, 0)), '-' + dmg, big ? '#ffd14d' : '#ff8c73', big ? 44 : 34);
    }
    refresh(u) { const v = this.vis(u); if (v) v.refresh(); }

    async rout(u) {
      const v = this.vis(u);
      if (!v) return;
      sfx('lose', 0.25);
      this.smoke(this.tile(u.x, u.y).add(new THREE.Vector3(0, 0.3, 0)));
      const p = v.pos.clone();
      await this._loop(0.6, t => { v.pos.set(p.x, p.y - t * 0.8, p.z); });
      v.group.visible = false;
      if (v.info) v.info.card.style.visibility = 'hidden';
      if (v.follow) v.follow.alpha = 0;
    }

    floatText(worldPos, text, cssColor, size) {
      if (!SG.UI || !SG.UI.floatText) return;
      if (size === undefined) size = 34;
      const col = (cssColor && cssColor.isColor) ? cssHex(cssColor) : cssColor;
      const px = Math.max(15, Math.round(size * uiScale()));
      const p = new THREE.Vector3(worldPos.x, worldPos.y, worldPos.z);
      try { SG.UI.floatText(p, text, col, px); } catch (e) { console.error(e); }
    }

    // ---------------------------------------------------------- 粒子 --
    // C# MakeParticles 的一次性爆发：球形发射、寿命 life、初速 speed、大小 size、重力系数 gravity、半径 radius
    _emit(pool, pos, n, o) {
      const col = o.color;
      for (let i = 0; i < n; i++) {
        let px, py, pz, vx, vy, vz;
        if (o.box) {
          px = pos.x + (Math.random() - 0.5) * o.box.x;
          py = pos.y + (Math.random() - 0.5) * o.box.y;
          pz = pos.z + (Math.random() - 0.5) * o.box.z;
          const sp = o.speed * (0.55 + Math.random() * 0.6);
          const j = o.jitter === undefined ? 0.25 : o.jitter;
          vx = (Math.random() - 0.5) * 2 * j; vz = (Math.random() - 0.5) * 2 * j; vy = sp;
        } else {
          const s = inSphere();
          px = pos.x + s.x * o.radius; py = pos.y + s.y * o.radius; pz = pos.z + s.z * o.radius;
          const sp = o.speed * (o.speedVar ? 1 - o.speedVar * Math.random() : 1);
          vx = s.x / s.d * sp; vy = s.y / s.d * sp; vz = s.z / s.d * sp;
        }
        let r = col.r, g = col.g, b = col.b;
        if (o.color2 && Math.random() < (o.mix === undefined ? 0.35 : o.mix)) { r = o.color2.r; g = o.color2.g; b = o.color2.b; }
        const ce = o.colorEnd;
        pool.add({
          x: px, y: py, z: pz, vx, vy, vz, age: 0,
          r1: ce ? ce.r : r, g1: ce ? ce.g : g, b1: ce ? ce.b : b,
          life: o.life * (o.lifeVar ? 1 - o.lifeVar * Math.random() : 1),
          size: o.size * (o.sizeVar ? 1 - o.sizeVar * Math.random() : 1), sizeEnd: o.sizeEnd === undefined ? 0.3 : o.sizeEnd,
          r, g, b, a: o.alpha === undefined ? 1 : o.alpha, grav: o.gravity || 0, drag: o.drag || 0, fadeIn: o.fadeIn || 0,
          ac: o.alphaCurve || 1,
        });
      }
    }
    // 持续发射器（C# loop 粒子）
    _emitter(pool, pos, rate, o, duration) {
      const e = { pool, pos: pos.clone(), rate, o, acc: 0, end: this.time + duration, dead: false };
      this.emitters.push(e);
      return e;
    }
    _updateEmitters(dt) {
      for (let i = this.emitters.length - 1; i >= 0; i--) {
        const e = this.emitters[i];
        if (e.dead || this.time >= e.end) { this.emitters.splice(i, 1); continue; }
        e.acc += e.rate * dt;
        const n = Math.floor(e.acc);
        if (n > 0) { e.acc -= n; this._emit(e.pool, e.pos, n, e.o); }
      }
    }

    burst(worldPos, cssColor, n, speed) {
      const pc = parseColor(cssColor === undefined ? '#ffffff' : cssColor);
      this._emit(this.glow, worldPos, n === undefined ? 14 : n, {
        color: pc.color, alpha: pc.a, life: 0.5, speed: speed === undefined ? 2.5 : speed, size: 0.25, gravity: 0.6, radius: 0.3,
        lifeVar: 0.3, speedVar: 0.35,
      });
      // 中心一闪
      this._emit(this.glow, worldPos, 2, { color: pc.color, alpha: pc.a * 0.8, life: 0.22, speed: 0, size: 1.3, sizeEnd: 0.4, gravity: 0, radius: 0.05 });
    }
    smoke(worldPos) {
      // C#：颜色 (0.5,0.45,0.4,0.6)、寿命 1.4、30 粒、初速 0.8、大小 0.9、重力 -0.05、半径 0.6
      this._emit(this.dust, worldPos, 30, {
        color: C(0.5, 0.45, 0.4), alpha: 0.6, life: 1.4, speed: 0.8, size: 0.9, sizeEnd: 1.7, gravity: -0.05, radius: 0.6,
        lifeVar: 0.25, sizeVar: 0.3, drag: 0.8, fadeIn: 0.08,
      });
    }
    _dustPuff(pos, k) {
      this._emit(this.dust, pos, 1, {
        color: C(0.62, 0.56, 0.45), alpha: 0.35 * k, life: 0.7, speed: 0.35, size: 0.55, sizeEnd: 1.6, gravity: -0.02, radius: 0.45,
        lifeVar: 0.3, drag: 1.2, fadeIn: 0.1,
      });
    }

    _acquireLight() {
      for (const l of this.lights) if (!l.userData.busy) { l.userData.busy = true; l.userData.token = null; return l; }
      const l = this.lights[0];
      l.userData.busy = true; l.userData.token = null;
      return l;
    }
    _updateLights(dt, time) {
      for (const l of this.lights) {
        const d = l.userData;
        const target = d.busy ? d.base : 0;
        d.cur += (target - d.cur) * Math.min(1, dt * (d.busy ? 12 : 3));
        const flicker = 1 + Math.sin(time * 23 + d.seed) * 0.12 + Math.sin(time * 37.7 + d.seed * 2) * 0.08;
        l.intensity = Math.max(0, d.cur * flicker);
        if (d.glow) {
          // 地面火光：常态 0.32，起火第一秒（光源加强时）约 0.58
          d.glow.material.opacity = M.clamp(d.cur / FIRE_LIGHT, 0, FIRE_FLARE) * 0.32 * flicker;
          const s = 1 + Math.sin(time * 9 + d.seed) * 0.05;
          d.glow.scale.set(s, 1, s);
          if (!d.busy && d.cur < 0.02) {
            this.group.remove(d.glow);
            d.glow.material.dispose();
            d.glow = null;
          }
        }
      }
    }

    // 燃烧中的格子：持续的小火苗与烟（model.burning > 0）
    _updateBurning() {
      const m = this.model;
      if (!m.burning) return;
      const seen = new Set();
      for (let x = 0; x < m.W; x++) for (let y = 0; y < m.H; y++) {
        if (!(m.burning[x][y] > 0)) continue;
        const key = x + ',' + y;
        seen.add(key);
        if (this.burnFx.has(key)) continue;
        const p = this.tile(x, y);
        const flame = this._emitter(this.flame, p.clone().add(new THREE.Vector3(0, 0.15, 0)), 16, {
          color: C(1, 0.66, 0.2), colorEnd: C(0.7, 0.14, 0.04), alpha: 0.9, life: 0.75, speed: 1.1, size: 0.55, sizeEnd: 0.35, gravity: -0.2,
          box: { x: 1.5, y: 0.1, z: 1.5 }, jitter: 0.15, lifeVar: 0.4, sizeVar: 0.4, alphaCurve: 2,
        }, 1e9);
        const smk = this._emitter(this.dust, p.clone().add(new THREE.Vector3(0, 0.9, 0)), 3, {
          color: C(0.24, 0.22, 0.2), alpha: 0.32, life: 2.2, speed: 0.7, size: 0.9, sizeEnd: 2.2, gravity: -0.02,
          box: { x: 1.2, y: 0.2, z: 1.2 }, jitter: 0.2, lifeVar: 0.3, drag: 0.4, fadeIn: 0.15,
        }, 1e9);
        this.burnFx.set(key, [flame, smk]);
      }
      for (const [key, es] of this.burnFx) if (!seen.has(key)) { for (const e of es) e.dead = true; this.burnFx.delete(key); }
    }

    async fireFx(t) {
      sfx('fire');
      const p = this.tile(t.x, t.y);
      const base = p.clone().add(new THREE.Vector3(0, 0.2, 0));
      const up = (y) => p.clone().add(new THREE.Vector3(0, y, 0));
      // 起火的一瞬：橙色闪光 + 火星四溅
      this._emit(this.glow, up(0.6), 2, { color: C(1, 0.55, 0.18), alpha: 0.75, life: 0.32, speed: 0, size: 2.6, sizeEnd: 0.6, gravity: 0, radius: 0.1 });
      this._emit(this.glow, up(0.4), 26, {
        color: C(1, 0.85, 0.45), colorEnd: C(1, 0.35, 0.08), alpha: 1, life: 0.7, speed: 3.6, size: 0.16, sizeEnd: 0.5, gravity: 0.5, radius: 0.5,
        lifeVar: 0.4, speedVar: 0.5,
      });
      // 火底热光（叠加，垫在火舌之下）：让火团有亮度、在黄衣部队和亮草地上也能分辨
      const heat = this._emitter(this.heat, base, 30, {
        color: C(1, 0.56, 0.16), colorEnd: C(0.95, 0.24, 0.04), alpha: 0.9, life: 0.6, speed: 0.9, size: 1.3, sizeEnd: 0.7, gravity: -0.1,
        box: { x: 1.3, y: 0.15, z: 1.3 }, jitter: 0.15, lifeVar: 0.3, alphaCurve: 2,
      }, 3.5);
      // 火舌（普通混合、水滴形）。C#：循环粒子 90/秒、寿命 0.9、初速 1.6、大小 0.7、重力 -0.25、盒形 1.6×0.2×1.6、颜色 (1,0.55,0.15)
      // 这里加大火舌并让它在前半生保持不透明 a·(1−t²)，颜色由黄经橙到暗红
      const flames = this._emitter(this.flame, base, 90, {
        color: C(1, 0.66, 0.16), color2: C(1, 0.84, 0.36), mix: 0.3, colorEnd: C(0.72, 0.12, 0.03), alpha: 1,
        life: 0.9, speed: 1.6, size: 1.15, sizeEnd: 0.3, gravity: -0.25, box: { x: 1.6, y: 0.2, z: 1.6 }, jitter: 0.3, lifeVar: 0.35, sizeVar: 0.3,
        alphaCurve: 2,
      }, 3.5);
      // 炽热火芯（叠加，压在火舌之上，贴近地面）
      const core = this._emitter(this.glow, up(0.35), 36, {
        color: C(1, 0.9, 0.55), colorEnd: C(1, 0.45, 0.08), alpha: 0.85, life: 0.45, speed: 1.0, size: 0.8, sizeEnd: 0.35, gravity: -0.2,
        box: { x: 1.2, y: 0.15, z: 1.2 }, jitter: 0.2, lifeVar: 0.3, alphaCurve: 2,
      }, 3.5);
      const embers = this._emitter(this.glow, base, 16, {
        color: C(1, 0.8, 0.4), colorEnd: C(1, 0.35, 0.1), alpha: 1, life: 1.2, speed: 2.6, size: 0.14, sizeEnd: 0.4, gravity: -0.1,
        box: { x: 1.4, y: 0.2, z: 1.4 }, jitter: 0.6, lifeVar: 0.4,
      }, 3.5);
      // 浓烟柱：从火头上方升起，由黑褐渐淡、越升越大
      const smk = this._emitter(this.dust, up(1.3), 16, {
        color: C(0.15, 0.13, 0.12), colorEnd: C(0.4, 0.38, 0.36), alpha: 0.6, life: 2.4, speed: 1.3, size: 1.0, sizeEnd: 3.0, gravity: -0.03,
        box: { x: 1.0, y: 0.3, z: 1.0 }, jitter: 0.25, lifeVar: 0.3, drag: 0.3, fadeIn: 0.12,
      }, 3.5);
      const tv = this.vis(t);
      if (tv) tv.burn();
      const light = this._acquireLight();
      const own = light.userData.token = {};          // 1 秒内若被别的火计借走，就不再改它
      light.position.copy(p).add(new THREE.Vector3(0, 1, 0));
      light.userData.base = FIRE_LIGHT * FIRE_FLARE;
      if (!light.userData.glow) {
        const g = SG.Gfx.mesh(flatDisc(1.7), SG.Gfx.additive(C(1, 0.44, 0.14)), { castShadow: false, receiveShadow: false });
        g.material.opacity = 0;
        g.renderOrder = 3;
        this.group.add(g);
        light.userData.glow = g;
      }
      light.userData.glow.position.copy(p).add(new THREE.Vector3(0, 0.1, 0));
      await this._sleep(1.0);
      // C#：1 秒后 rateOverTime = 25
      flames.rate = 25; heat.rate = 9; core.rate = 10; embers.rate = 5; smk.rate = 6;
      if (light.userData.token !== own) return;
      light.userData.base = FIRE_LIGHT;
      // C#：Destroy(ps.gameObject, 2.5f) —— 2.5 秒后熄灭
      const token = light.userData.token = {};
      setTimeout(() => { if (light.userData.token === token) light.userData.busy = false; }, 2500);
    }

    async rockFx(t) {
      const p = this.tile(t.x, t.y);
      const rocks = [];
      const mat = SG.Gfx.lowPoly();
      for (let i = 0; i < 6; i++) {
        const mb = new SG.MeshBuilder();
        const r = 0.25 + Math.random() * 0.2;
        mb.blob(V(0, 0, 0), V(r, r, r), C(0.55, 0.52, 0.48), i);
        const go = SG.Gfx.mesh(mb.toGeometry(), mat, { castShadow: true, receiveShadow: true });
        go.position.set(p.x + (Math.random() - 0.5) * 1.4, p.y + 7 + i * 0.8, p.z + (Math.random() - 0.5) * 1.4);
        go.userData.landed = false;
        this.group.add(go);
        this.temp.push(go);
        rocks.push(go);
      }
      let t0 = 0, last = now();
      while (t0 < 0.9) {
        if (this.disposed) return;
        const dt = Math.min(0.1, now() - last);
        last = now();
        for (const r of rocks) {
          if (r.position.y > p.y + 0.2) {
            r.position.y -= dt * 14;
            if (r.position.y <= p.y + 0.2) {
              r.position.y = p.y + 0.2;
              if (!r.userData.landed) { r.userData.landed = true; this._rockImpact(r.position); }
            }
          }
          r.rotation.x += 200 * M.deg2rad * dt;
          r.rotation.y -= 90 * M.deg2rad * dt;
        }
        await SG.frame();
        t0 += dt;
      }
      sfx('rock');
      this.smoke(p);
      await this._sleep(0.3);
      for (const r of rocks) {
        this.group.remove(r);
        r.geometry.dispose();
        const k = this.temp.indexOf(r);
        if (k >= 0) this.temp.splice(k, 1);
      }
    }
    _rockImpact(pos) {
      this._emit(this.dust, pos, 6, {
        color: C(0.58, 0.52, 0.44), alpha: 0.5, life: 0.8, speed: 1.6, size: 0.5, sizeEnd: 1.5, gravity: 0.15, radius: 0.25,
        lifeVar: 0.3, drag: 2.0,
      });
      this._emit(this.glow, pos, 4, { color: C(1, 0.85, 0.6), alpha: 0.7, life: 0.3, speed: 2.4, size: 0.14, gravity: 0.8, radius: 0.1 });
    }

    async magicFx(t, cssColor) {
      sfx('magic', 0.6);
      const pc = parseColor(cssColor === undefined ? '#bf80ff' : cssColor);
      const p = this.tile(t.x, t.y);
      const ring = SG.Gfx.mesh(flatDisc(1.2), SG.Gfx.unlit(pc.color, SG.Gfx.ringTexture, pc.a), { castShadow: false, receiveShadow: false });
      ring.position.copy(p).add(new THREE.Vector3(0, 0.15, 0));
      ring.renderOrder = 4;
      const halo = SG.Gfx.mesh(flatDisc(1.2), SG.Gfx.additive(pc.color), { castShadow: false, receiveShadow: false });
      halo.position.copy(ring.position);
      halo.renderOrder = 3;
      this.group.add(ring); this.group.add(halo);
      this.temp.push(ring, halo);
      // C#：寿命 0.9、40 粒、初速 1.4、大小 0.3、重力 -0.4、半径 0.6
      this._emit(this.glow, p.clone().add(new THREE.Vector3(0, 0.5, 0)), 40, {
        color: pc.color, color2: shade(pc.color, 0.5), mix: 0.3, alpha: 1, life: 0.9, speed: 1.4, size: 0.3, gravity: -0.4, radius: 0.6, lifeVar: 0.3,
      });
      await this._loop(0.9, k => {
        const s = 0.5 + k * 1.4;
        ring.scale.set(s, 1, s);
        ring.rotation.y = -300 * M.deg2rad * k * 0.9;
        halo.scale.set(s * 0.9, 1, s * 0.9);
        halo.material.opacity = 0.5 * (1 - k);
      });
      for (const o of [ring, halo]) {
        this.group.remove(o);
        o.material.dispose();
        const i = this.temp.indexOf(o);
        if (i >= 0) this.temp.splice(i, 1);
      }
    }

    // ---------------------------------------------------------- 释放 --
    dispose() {
      if (this.disposed) return;
      this.disposed = true;
      for (const v of this.visuals.values()) v.dispose();
      this.visuals.clear();
      this.clearHighlights();
      for (const mat of this.hlMats.values()) mat.dispose();
      for (const mat of this.sideRing) mat.dispose();
      this.hlMats.clear();
      this.emitters.length = 0;
      this.burnFx.clear();
      this.glow.dispose(); this.flame.dispose(); this.heat.dispose(); this.dust.dispose();
      for (const l of this.lights) { l.intensity = 0; if (l.dispose) l.dispose(); }
      const shared = new Set([SG.Gfx.lowPoly(), SG.Gfx.water()]);
      this.group.traverse(o => {
        if (o.geometry && !discCacheHas(o.geometry) && o.geometry !== this.hlGeo) o.geometry.dispose();
        if (o.material && !shared.has(o.material) && !(o.isPoints)) {
          if (Array.isArray(o.material)) o.material.forEach(mm => mm.dispose()); else o.material.dispose();
        }
      });
      if (this.group.parent) this.group.parent.remove(this.group);
    }
  }
  function discCacheHas(g) { for (const v of discCache.values()) if (v === g) return true; return false; }

  BattleView.Origin = ORIGIN;
  BattleView.T = T;

  SG.BattleView = BattleView;
  SG.UnitVisual = UnitVisual;
})();
