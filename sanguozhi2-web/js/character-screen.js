'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版 · 人物菜单（UI-V4 §9）
   漆框窗口（四边留 8 px；手机全屏）：页首（‹ · 立绘 · 金字姓名 · 势力 / 官职 / 生年 · 等级印 + 经验条 · › · 收）、
   左侧 3D 立绘台（§9.2）、八个页签（总览 · 装备 · 武艺 · 计略 · 部队 · 官爵 · 关系 · 履历）、底部漆面操作条
   （赏赐 · 赐宝物 · 移动 · 授官 · 演武 ·（空）· 罢免 · 习得新招）。他国 / 在野武将只读。
   规则一律读各拥有者：SG.Progress（等级、招式格）、SG.Gear / SG.Items（装备，ITEMS §14.1）、SG.Combos（招式谱，COMBAT §12.1）、
   SG.Specials / SG.Tactics、SG.Titles / SG.TitleUI（官爵，TITLES §9.4）、SG.Mods.unitPreview（攻防会心，与战斗同一公式）。

   SG.CharacterScreen.open(gen, { list, tab }) → Promise（关闭时兑现）   list = ‹ › 的顺序；tab = 初始页签
   SG.CharacterScreen.tab(id)       切换页签（测试 / 案头「可习得」直达武艺）     SG.CharacterScreen.isOpen()  .current
   SG.CharacterScreen.registerTab(id, { label, icon, build(page, gen, ctx), badge?(gen), readOnly? })   其他模块的页签（关系 = ENGAGEMENT）
   SG.CharSheet.derive(gen) → { atkK, defK, crit, critK, tacK, duelHp, mob, morale, formation（出征）, formationDef（守城）, lines: { atkK: [...], … } }
   SG.CharSheet.battleFormation(gen, side = 0) · battleFormations(gen) → { atk, def }
   SG.CharStage.create(host, gen, { controls, poses, compact }) → stage { setGen, setPose(p), puff(), dispose(), fallback }
       一个渲染器（DESIGN-V2 §2）：推入一个全屏场景，先照常画地图，再以 scissor 在立绘台的位置画人物，随即 drawImage 到立绘台的 2D 画布
       （DOM 里的画布，叠在纸面上，任何窗口 / 对话框照常盖在它上面）。WebGL 不可用或人物模型未载入时退回 256 px 立绘 + 画出的台子。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});
  const esc = s => SG.esc(String(s === undefined || s === null ? '' : s));
  const UI = () => SG.UI;
  const G = () => SG.G;
  const icon = (id, size) => (SG.UIIcons ? SG.UIIcons.svg(id, size) : '');
  const n0 = v => Math.round(v || 0).toLocaleString('en-US');
  const pct = (v, d) => (v >= 0 ? '+' : '−') + Math.abs(Math.round(v * 100 * (d ? 10 : 1)) / (d ? 10 : 1)) + '%';
  function h(tag, cls, html, parent) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (html !== undefined && html !== null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  const reduced = () => !!(SG.Motion && SG.Motion.reduced);
  const STAT = [['war', '武力', 'var(--war)'], ['intel', '智力', 'var(--int)'], ['pol', '政治', 'var(--pol)']];
  const FAM_ZH = { war: '武官', intel: '谋臣', pol: '文官' };
  const FAM_ONE = { war: '武', intel: '谋', pol: '文' };

  // ================================================================ SG.CharSheet ==
  // 总览的派生数字：只调用拥有者（与战斗同一公式）；拥有者没载入的部分不计
  const CharSheet = {
    // 开战时这名武将的阵型：战斗模块支持「惯用阵型」（SG.BattleModel.PREF_FORMATION）且已选定 → gen.formationPref（攻守相同）；
    // 否则与 battle-model.js 开战时一样按 aiFormation 选：side 0 = 出征（攻方的打分），side 1 = 守城（守方看重防御）
    battleFormation(gen, side) {
      const F = SG.Defs && SG.Defs.formations, BM = SG.BattleModel;
      if (!F || !F.length || !gen) return 0;
      const ok = i => !!F[i] && (gen.intel | 0) >= (F[i].reqInt | 0) && (gen.war | 0) >= (F[i].reqWar | 0);
      if (BM && BM.PREF_FORMATION && gen.formationPref != null && ok(gen.formationPref)) return gen.formationPref;
      try { if (BM && BM.prototype && BM.prototype.aiFormation) return BM.prototype.aiFormation.call({ formationAllowed: (u, i) => ok(i) }, { gen, side: side === 1 ? 1 : 0 }) | 0; } catch (e) { /* 退回方圆 */ }
      return 0;
    },
    // { atk: 出征时的阵型序号, def: 守城时的阵型序号 }
    battleFormations(gen) { return { atk: CharSheet.battleFormation(gen, 0), def: CharSheet.battleFormation(gen, 1) }; },
    prefSupported() { return !!(SG.BattleModel && SG.BattleModel.PREF_FORMATION); },
    derive(gen) {
      const M = SG.Mods, P = SG.Progress, Gr = SG.Gear && SG.Items && SG.Items.enabled !== false ? SG.Gear : null, T = SG.Titles && SG.Titles.enabled !== false ? SG.Titles : null;
      const out = { atkK: 1, defK: 1, crit: 0.02, critK: 1.5, tacK: 1, duelHp: 100, mob: 0, morale: 0, formation: null, vetK: 1, lines: { atkK: [], defK: [], crit: [], tacK: [], duelHp: [], mob: [] } };
      let pv = null;
      const fIdx = CharSheet.battleFormation(gen);
      try { pv = M && M.unitPreview ? M.unitPreview(gen, { formation: fIdx }) : null; } catch (e) { pv = null; }
      const vk = pv ? pv.vetK : P && P.vetK ? P.vetK(gen) : 1;
      out.vetK = vk;
      if (pv) {
        out.atkK = pv.atk; out.defK = pv.def; out.crit = pv.crit; out.critK = pv.critK; out.morale = pv.morale; out.formation = pv.formation;
        const pa = pv.parts.atk, pd = pv.parts.def, pc = pv.parts.crit;
        if (vk > 1.0001) { out.lines.atkK.push('老练 ' + pct(vk - 1)); out.lines.defK.push('老练 ' + pct(vk - 1)); }
        if (pa.gear) out.lines.atkK.push('装备 ' + pct(pa.gear)); if (pa.title) out.lines.atkK.push('官 ' + pct(pa.title));
        if (pa.bond) out.lines.atkK.push('羁绊 ' + pct(pa.bond));
        if (pd.gear) out.lines.defK.push('装备 ' + pct(pd.gear)); if (pd.title) out.lines.defK.push('官 ' + pct(pd.title));
        if (pv.capped && pv.capped.atk) out.lines.atkK.push('封顶');
        if (pv.capped && pv.capped.def) out.lines.defK.push('封顶');
        const base = 0.02 + 0.0015 * Math.max(0, (gen.war | 0) - 50);
        out.lines.crit.push('基础 ' + Math.round(base * 1000) / 10);
        if (pc.gear) out.lines.crit.push('装备 ' + Math.round(pc.gear * 1000) / 10);
        if (pc.title) out.lines.crit.push('官 ' + Math.round(pc.title * 1000) / 10);
        if (pv.formation && pv.formation.crit) out.lines.crit.push(pv.formation.name + ' ' + Math.round(pv.formation.crit * 1000) / 10);
      } else if (vk > 1.0001) { out.atkK = out.defK = vk; out.lines.atkK.push('老练 ' + pct(vk - 1)); out.lines.defK.push('老练 ' + pct(vk - 1)); }
      // 计策威力：老练（战斗里的 tacK）× 装备 / 官职的计策加成
      let gt = 0, tt = 0;
      try { gt = Gr && Gr.battle && Gr.battle.tac ? Gr.battle.tac(gen) : 0; } catch (e) { gt = 0; }
      try { tt = T && T.bonus ? T.bonus(gen, 'tac') || 0 : 0; } catch (e) { tt = 0; }
      out.tacK = vk * (1 + gt + tt);
      if (vk > 1.0001) out.lines.tacK.push('老练 ' + pct(vk - 1));
      if (gt) out.lines.tacK.push('装备 ' + pct(gt));
      if (tt) out.lines.tacK.push('官 ' + pct(tt));
      // 单挑体力与机动
      if (Gr) {
        try {
          const s = Gr.summary(gen);
          // SG.Gear.summary().duel.hp 已是单挑的总体力（100 + 装备体力，预算截断之后；items.js duelMods）
          const hp = s.duel && s.duel.hp > 0 ? Math.round(s.duel.hp) : 100;
          out.duelHp = hp;
          if (hp !== 100) out.lines.duelHp.push('基础 100', '装备 ' + (hp > 100 ? '+' : '−') + Math.abs(hp - 100));
          out.mob = s.army.mob || 0;
          const mnt = Gr.item(gen, 'mount');
          out.lines.mob.push(mnt ? '骑 ' + (SG.Items.displayName ? SG.Items.displayName(mnt) : mnt.name) : '无马');
        } catch (e) { /* 装备未就绪 */ }
      } else out.lines.mob.push('—');
      const fm = SG.Defs && SG.Defs.formations ? SG.Defs.formations[fIdx] : null;
      if (!out.formation && fm) out.formation = { index: fIdx, name: fm.name, atk: fm.atk, def: fm.def, crit: fm.crit || 0 };
      // 守城时的阵型（攻守的打分不同，常常不是同一阵；总览的攻防会心按出征时算）
      const dIdx = CharSheet.battleFormation(gen, 1), dfm = SG.Defs && SG.Defs.formations ? SG.Defs.formations[dIdx] : null;
      out.formationDef = dfm ? { index: dIdx, name: dfm.name, atk: dfm.atk, def: dfm.def, crit: dfm.crit || 0 } : null;
      if (fm && fm.move) { out.mob += fm.move; out.lines.mob.push(fm.name + ' +' + fm.move); }
      if (!out.lines.duelHp.length) out.lines.duelHp.push('基础 100');
      return out;
    },
  };
  SG.CharSheet = CharSheet;

  // ================================================================ SG.CharStage ==
  const STAGES = [];      // 当前的立绘台（最上层 = 最后一个）
  let stageScreen = null;
  let paperTex = null;
  function paperBg() {
    if (paperTex) return paperTex;
    const c = document.createElement('canvas'); c.width = 64; c.height = 160;
    const x = c.getContext('2d');
    const g = x.createLinearGradient(0, 0, 0, 160);
    g.addColorStop(0, '#efdcb4'); g.addColorStop(0.55, '#f3e4c2'); g.addColorStop(1, '#d6bb8a');
    x.fillStyle = g; x.fillRect(0, 0, 64, 160);
    const r = x.createRadialGradient(32, 62, 2, 32, 62, 74);
    r.addColorStop(0, 'rgba(255,248,226,.85)'); r.addColorStop(1, 'rgba(255,248,226,0)');
    x.fillStyle = r; x.fillRect(0, 0, 64, 160);
    paperTex = new THREE.CanvasTexture(c);
    if ('colorSpace' in paperTex) paperTex.colorSpace = THREE.NoColorSpace !== undefined ? THREE.NoColorSpace : '';
    paperTex.needsUpdate = true;
    return paperTex;
  }
  function canStage() {
    try { return !!(window.THREE && SG.Gfx && SG.Gfx.renderer && SG.Figure && SG.Figure.general && SG.LP && SG.LP.Mood); } catch (e) { return false; }
  }
  function ensureScreen() {
    if (stageScreen) return;
    const Gfx = SG.Gfx;
    stageScreen = {
      scene: Gfx.scene, camera: Gfx.camera, isCharStage: true,
      update(dt) {
        // 宿主被别处移走（窗口没走 close() 就被清掉）时自动收起，免得场景一直压在栈上
        for (const s of STAGES.slice()) if (!s.host.isConnected) s.dispose();
        const st = STAGES[STAGES.length - 1]; if (st) st._update(dt);
      },
      render(r) {
        // 先照常画地图（窗口四周的边与半透明遮罩下仍是地图）
        try { if (Gfx.sky) Gfx.sky.position.copy(Gfx.camera.position); r.render(Gfx.scene, Gfx.camera); } catch (e) { /* 忽略 */ }
        const st = STAGES[STAGES.length - 1];
        if (st) { try { st._render(r); } catch (e) { if (!st._err) { st._err = true; console.error(e); } } }
      },
    };
    Gfx.pushScreen(stageScreen);
  }
  function releaseScreen() {
    if (!stageScreen || STAGES.length) return;
    try { SG.Gfx.popScreen(stageScreen); } catch (e) { /* 忽略 */ }
    stageScreen = null;
    try { const r = SG.Gfx.renderer; r.setScissorTest(false); r.setViewport(0, 0, SG.Gfx.width, SG.Gfx.height); } catch (e) { /* 忽略 */ }
  }
  const POSES = [['idle', '待机'], ['drill', '演武'], ['ride', '骑乘'], ['win', '得胜']];
  const STEP_CLIP = { L: ['light1', 'light2'], M: ['light3'], H: ['heavy'] };
  const HORSE_NAMES = ['赤兔', '的卢', '绝影', '爪黄飞电', '白龙驹'];
  const CharStage = {
    create(host, gen, opts) {
      opts = opts || {};
      host.classList.add('sg-stage');
      const cv = h('canvas', 'sg-stage-cv', null, host);
      const st = {
        host, cv, gen: null, pose: 'idle', t: 0, yaw: 0, vyaw: 0, zoom: 1, idle: 0, fallback: false, scene: null, cam: null, fig: null, horse: null, mood: null,
        disposed: false, ctx: cv.getContext('2d'), drill: null, motes: null,
        setGen(g2) { st.gen = g2; build(); },
        setPose(p) {
          if (p === 'ride' && !st.canRide()) { UI().deny(st.poseSeg, '未乘马——在「装备」里配一匹坐骑。'); if (st.poseSeg) st.poseSeg.set(st.pose); return; }
          st.pose = p; st.t = 0;
          if (st.poseSeg) st.poseSeg.set(p);
          if (!st.fallback) build(true);
          host.classList.toggle('is-win', p === 'win');
        },
        canRide() { const Gr = SG.Gear; return !(SG.Items && SG.Items.enabled !== false && Gr) || !!Gr.item(st.gen, 'mount'); },
        puff() { host.classList.remove('is-puff'); void host.offsetWidth; host.classList.add('is-puff'); },
        dispose() {
          if (st.disposed) return;
          st.disposed = true;
          const i = STAGES.indexOf(st); if (i >= 0) STAGES.splice(i, 1);
          clear3d();
          releaseScreen();
        },
      };
      // 姿势分段（台下）
      if (opts.poses !== false) {
        const seg = h('div', 'sg-stage-poses', null, host);
        st.poseSeg = UI().segmented(seg, POSES, 'idle', p => st.setPose(p));
      }
      if (opts.controls !== false && !SG.isTouch) h('div', 'sg-stage-hint', '拖动旋转 · 滚轮缩放', host);
      if (SG.UIArt && SG.UIArt.lantern) {
        h('div', 'sg-stage-lantern l', SG.UIArt.lantern(30, true), host);
        h('div', 'sg-stage-lantern r', SG.UIArt.lantern(30, true), host);
      }
      st.motes = h('div', 'sg-stage-motes', '<i></i><i></i><i></i><i></i><i></i><i></i>', host);
      // 拖动 = 转身（惯性 τ 0.25 秒），滚轮 = 缩放 0.8–1.6，双击 = 复位；触屏：点按换姿势
      let drag = null;
      cv.addEventListener('pointerdown', e => { drag = { x: e.clientX, t: performance.now(), moved: 0 }; try { cv.setPointerCapture(e.pointerId); } catch (err) { /* 忽略 */ } st.idle = 0; });
      cv.addEventListener('pointermove', e => {
        if (!drag) return;
        const dx = e.clientX - drag.x, now = performance.now();
        drag.moved += Math.abs(dx);
        st.yaw += dx * 0.012;
        st.vyaw = dx * 0.012 / Math.max(0.008, (now - drag.t) / 1000);
        drag.x = e.clientX; drag.t = now; st.idle = 0;
      });
      const up = () => {
        if (drag && drag.moved < 4 && opts.tapCycles) { const i = POSES.findIndex(p => p[0] === st.pose); st.setPose(POSES[(i + 1) % POSES.length][0]); }
        drag = null;
      };
      cv.addEventListener('pointerup', up); cv.addEventListener('pointercancel', up);
      cv.addEventListener('wheel', e => { e.preventDefault(); st.zoom = Math.max(0.8, Math.min(1.6, st.zoom * (e.deltaY > 0 ? 0.92 : 1.08))); st.idle = 0; }, { passive: false });
      cv.addEventListener('dblclick', () => { st.yaw = 0; st.vyaw = 0; st.zoom = 1; });

      function clear3d() {
        try {
          if (st.fig) { st.fig.dispose(); st.fig = null; }
          if (st.horse) { st.horse.dispose(); st.horse = null; }
          if (st.mood) { st.mood.dispose(); st.mood = null; }
          if (st.scene) {
            st.scene.traverse(o => { if (o.isMesh && o.userData.stageOwn) { o.geometry.dispose(); if (o.material && o.material.dispose) o.material.dispose(); } });
            st.scene = null;
          }
        } catch (e) { /* 忽略 */ }
      }
      function toFallback() {
        st.fallback = true;
        host.classList.add('is-fallback');
        cv.style.display = 'none';
        let fb = host.querySelector('.sg-stage-fb');
        if (!fb) fb = h('div', 'sg-stage-fb', null, host);
        fb.innerHTML = UI().frame(st.gen, { size: opts.compact ? 120 : 196, mood: st.pose === 'win' ? 'win' : undefined }) + '<i class="sg-stage-plat"></i>';
        if (st.poseSeg) st.poseSeg.style.display = 'none';
      }
      function build(poseOnly) {
        if (st.disposed || !st.gen) return;
        if (!canStage() || opts.portrait) { toFallback(); return; }
        try {
          const g = G(), gen = st.gen;
          const f = gen.faction >= 0 && g ? g.factions[gen.faction] : null;
          const color = f ? f.color : '#8a7a62';
          const ride = st.pose === 'ride' && st.canRide();
          if (poseOnly && st.scene && !!st.horse === ride) { st.t = 0; st.drill = drillPlan(gen); return; }
          clear3d();
          const scene = new THREE.Scene();
          scene.background = paperBg();
          // 台子：木面 + 漆边（24 段）
          const wood = new THREE.MeshLambertMaterial({ color: 0x8a5a36 });
          const rim = new THREE.MeshLambertMaterial({ color: 0x5c1d16 });
          const top = new THREE.Mesh(new THREE.CylinderGeometry(1.15, 1.2, 0.12, 24), wood);
          top.position.y = -0.06; top.receiveShadow = true; top.userData.stageOwn = true;
          const band = new THREE.Mesh(new THREE.CylinderGeometry(1.24, 1.3, 0.1, 24), rim);
          band.position.y = -0.14; band.receiveShadow = true; band.userData.stageOwn = true;
          scene.add(top, band);
          const fig = SG.Figure.general(gen, { lod: 'high', color, shadow: true, mounted: ride });
          let rootObj = fig.root;
          if (ride) {
            const mnt = SG.Gear ? SG.Gear.item(gen, 'mount') : null;
            const nm = mnt ? HORSE_NAMES.find(n => (mnt.name || '').indexOf(n) >= 0) : null;
            st.horse = SG.Figure.horse({ color, general: true, name: nm || undefined, lod: 'high' });
            st.horse.mount(fig);
            rootObj = st.horse.root;
          }
          scene.add(rootObj);
          st.fig = fig;
          // 取景：按包围盒
          rootObj.updateMatrixWorld(true);
          const box = new THREE.Box3().setFromObject(rootObj);
          const hgt = Math.max(1, box.max.y - box.min.y);
          st.frame = { cy: box.min.y + hgt * 0.5, h: hgt, w: 2 * Math.max(Math.abs(box.min.x), Math.abs(box.max.x), Math.abs(box.min.z), Math.abs(box.max.z)), ride };
          st.cam = new THREE.PerspectiveCamera(26, 0.6, 0.1, 60);
          st.mood = SG.Gfx.newMood(scene, { center: [0, st.frame.cy, 0], extent: Math.max(2.5, hgt * 1.3), shadowSize: 1024, fogMax: 0, shadowStrength: 0.55, maxLamps: 2 });
          if (st.mood) {
            st.mood.set(16.6);
            st.mood.addLamp([-1.1, hgt * 0.95, 0.6], { color: '#ffb35c', intensity: 1.1, range: 4.5, nightOnly: false, halo: false });
            st.mood.addLamp([1.1, hgt * 0.95, 0.6], { color: '#ffb35c', intensity: 1.1, range: 4.5, nightOnly: false, halo: false });
          } else {
            scene.add(new THREE.HemisphereLight(0xfff1d6, 0x6b5a44, 1.1));
            const d = new THREE.DirectionalLight(0xffe6c0, 1.2); d.position.set(2, 4, 3); scene.add(d);
          }
          st.scene = scene;
          st.drill = drillPlan(gen);
          st.t = 0;
          host.classList.remove('is-fallback'); cv.style.display = '';
          const fb = host.querySelector('.sg-stage-fb'); if (fb) fb.remove();
          if (st.poseSeg) st.poseSeg.style.display = '';
        } catch (e) { console.error(e); clear3d(); toFallback(); }
      }
      // 演武：他的八式连段，2.6 秒一轮
      function drillPlan(gen) {
        let seq = 'LLMHLMHH';
        try { const e = SG.Combos && SG.Combos.of(gen); if (e && e.seq) seq = e.seq; } catch (err) { /* 缺省 */ }
        const steps = [];
        let li = 0;
        for (let i = 0; i < 8; i++) {
          const t = seq[i];
          let clip = i === 7 ? (t === 'H' ? 'special' : t === 'M' ? 'light3' : 'heavy') : t === 'L' ? STEP_CLIP.L[li++ % 2] : STEP_CLIP[t][0];
          steps.push(clip);
        }
        const durs = steps.map(c => { const ci = st.fig && st.fig.clipInfo ? st.fig.clipInfo(c) : null; return ci ? ci.dur : 0.5; });
        const total = durs.reduce((a, b) => a + b, 0);
        const k = 2.6 / Math.max(0.5, total);
        return { steps, durs: durs.map(d => d * k), raw: durs, total: 2.6 + 0.6 };
      }
      st._update = dt => {
        if (st.disposed || st.fallback || !st.fig) return;
        st.t += dt;
        // 转身惯性与空闲环绕
        if (Math.abs(st.vyaw) > 0.001) { st.yaw += st.vyaw * dt; st.vyaw *= Math.exp(-dt / 0.25); }
        st.idle += dt;
        const orbit = SG.UISettings ? SG.UISettings.get('cam.idleOrbit') !== false : true;
        if (st.idle > 6 && orbit && !reduced()) st.yaw += dt * 12 * Math.PI / 180;
        const fig = st.fig, P = st.pose;
        try {
          if (P === 'idle') fig.setPose('idle', st.t);
          else if (P === 'ride') { if (st.horse) st.horse.setPose('idle', st.t); fig.setPose('idle', st.t); }
          else if (P === 'win') { const v = fig.clipInfo('victory'); const d = v ? v.dur : 2; if (st.t < d) fig.setPose('victory', st.t); else fig.setPose('cheer', st.t - d); }
          else if (P === 'drill' && st.drill) {
            const D = st.drill;
            let tt = st.t % D.total, i = 0;
            while (i < 8 && tt > D.durs[i]) { tt -= D.durs[i]; i++; }
            if (i >= 8) fig.setPose('idle', tt);
            else fig.setPose(D.steps[i], tt / D.durs[i] * D.raw[i]);
          }
          fig.update(dt);
        } catch (e) { /* 片段缺失 */ }
        const root = st.horse ? st.horse.root : fig.root;
        root.rotation.y = st.yaw;
      };
      st._render = r => {
        if (st.disposed || st.fallback || !st.scene) return;
        const rc = host.getBoundingClientRect(), cr = r.domElement.getBoundingClientRect();
        const x0 = Math.max(rc.left, cr.left), y0 = Math.max(rc.top, cr.top);
        const x1 = Math.min(rc.right, cr.right), y1 = Math.min(rc.bottom, cr.bottom);
        const w = Math.floor(x1 - x0), hh = Math.floor(y1 - y0);
        if (w < 8 || hh < 8) return;
        const pr = r.getPixelRatio();
        const bw = Math.round(w * pr), bh = Math.round(hh * pr);
        if (cv.width !== bw || cv.height !== bh) { cv.width = bw; cv.height = bh; }
        cv.style.left = (x0 - rc.left) + 'px'; cv.style.top = (y0 - rc.top) + 'px'; cv.style.width = w + 'px'; cv.style.height = hh + 'px';
        const cam = st.cam, F = st.frame;
        cam.aspect = w / hh;
        const fov = cam.fov * Math.PI / 180;
        const fitH = (F.h * 1.18) / 2 / Math.tan(fov / 2);
        // 骑乘：连马带人都要入画；步行：按身高取景（长兵可以出画，与样稿一致）
        const fitW = F.ride ? (F.w * 1.1) / 2 / Math.tan(fov / 2) / cam.aspect : 0;
        const dist = Math.max(fitH, fitW) / st.zoom;
        cam.position.set(0, F.cy + F.h * 0.08, dist);
        cam.lookAt(0, F.cy - F.h * 0.02, 0);
        cam.updateProjectionMatrix();
        const vx = x0 - cr.left, vyTop = y0 - cr.top, vy = cr.height - vyTop - hh;
        r.setScissorTest(true);
        r.setViewport(vx, vy, w, hh);
        r.setScissor(vx, vy, w, hh);
        r.render(st.scene, cam);
        r.setScissorTest(false);
        r.setViewport(0, 0, cr.width, cr.height);
        st.ctx.drawImage(r.domElement, Math.round(vx * pr), Math.round(vyTop * pr), bw, bh, 0, 0, bw, bh);
      };
      STAGES.push(st);
      st.gen = gen;
      build();
      if (!st.fallback) ensureScreen();
      return st;
    },
    count() { return STAGES.length; },
  };
  SG.CharStage = CharStage;

  // ================================================================ 页签 ==
  const TABS = [
    { id: 'overview', label: '总览', icon: 'list', ro: true },
    { id: 'gear', label: '装备', icon: 'armour', ro: true },
    { id: 'skills', label: '武艺', icon: 'combo', ro: true },
    { id: 'tactics', label: '计略', icon: 'tactic' },
    { id: 'army', label: '部队', icon: 'troops' },
    { id: 'office', label: '官爵', icon: 'titles', ro: true },
    { id: 'bonds', label: '关系', icon: 'bonds', ro: true },
    { id: 'history', label: '履历', icon: 'history', ro: true },
  ];
  const EXTRA = {};      // registerTab 登记的页签

  let cur = null;
  let lastVoice = 0;
  const CharacterScreen = {
    get current() { return cur; },
    isOpen() { return !!(cur && !cur.win.closed); },
    registerTab(id, def) { EXTRA[id] = def; if (!TABS.some(t => t.id === id)) TABS.push({ id, label: def.label, icon: def.icon || 'help', ro: def.readOnly !== false }); },
    tab(id) { if (cur && cur.tabs) cur.tabs.set(id); },
    close() { if (cur) cur.win.close(); },
    open(gen, opts) {
      opts = opts || {};
      if (!gen) return Promise.resolve();
      if (cur && !cur.win.closed) { cur.show(gen, opts.tab); return cur.promise; }
      let resolveP;
      const promise = new Promise(r => { resolveP = r; });
      const st = cur = { gen, list: (opts.list && opts.list.length ? opts.list : null), promise, stage: null, tabs: null, sel: { slot: 'weapon' } };
      const short = document.documentElement.classList.contains('sg-short');
      const win = st.win = UI().window('char', {
        title: '', width: 'calc(100vw - 16px)', height: 'calc(100vh - 16px)', className: 'sg-char', side: true, foot: true,
        tabs: TABS.map(t => ({ id: t.id, icon: t.icon, label: t.label })), tabValue: opts.tab || 'overview',
        onTab: id => { st.tab = id; renderTab(); },
        onClose: () => { if (st.stage) st.stage.dispose(); cur = null; resolveP(); try { const ss = SG.Game && SG.Game.strategy; if (ss && ss.hud) ss.refreshAll(); } catch (e) { /* 忽略 */ } },
      });
      st.tabs = win.tabs;
      st.tab = win.tabs.get();
      // 页首：‹ 立绘 姓名 / 两行 / 等级印 + 经验 ›
      const head = win.head;
      head.classList.add('sg-char-head');
      st.prev = UI().button(null, '', () => step(-1), { kind: 'icon', icon: 'play', className: 'sg-char-nav is-prev', ariaLabel: '上一位', label: '' });
      head.insertBefore(st.prev, head.firstChild);
      st.next = UI().button(win.tools, '', () => step(1), { kind: 'icon', icon: 'play', className: 'sg-char-nav is-next', ariaLabel: '下一位', label: '' });
      // 立绘台
      st.stageHost = h('div', 'sg-char-stage', null, win.side);
      st.stage = CharStage.create(st.stageHost, gen, { compact: short, tapCycles: !!SG.isTouch, controls: !short });
      // 操作条
      st.acts = h('div', 'sg-char-acts', null, win.foot);
      win.onKey = k => {
        if (k === '[' || k === 'PageUp') { step(-1); return true; }
        if (k === ']' || k === 'PageDown') { step(1); return true; }
        return false;
      };
      // 立绘台上左右滑动 = 上一位 / 下一位（触屏）
      if (SG.isTouch) {
        let sx = null;
        st.stageHost.addEventListener('touchstart', e => { sx = e.touches[0].clientX; }, { passive: true });
        st.stageHost.addEventListener('touchend', e => { if (sx === null) return; const dx = e.changedTouches[0].clientX - sx; sx = null; if (Math.abs(dx) > 60) step(dx > 0 ? -1 : 1); }, { passive: true });
      }
      function step(d) {
        if (!st.list || st.list.length < 2) return;
        const i = st.list.indexOf(st.gen);
        const n = st.list[(i + d + st.list.length) % st.list.length];
        UI().sfx('ui-page');
        st.show(n);
      }
      st.show = (g2, tab) => {
        st.gen = g2;
        if (st.stage) st.stage.setGen(g2);
        renderHead();
        renderActs();
        if (tab && tab !== st.tabs.get()) st.tabs.set(tab); else renderTab();
        voice(g2);
      };
      function renderHead() {
        const g = G(), x = st.gen;
        const f = x.faction >= 0 ? g.factions[x.faction] : null;
        const c = g.cities[x.city];
        const P = SG.Progress;
        const e = P && P.expInfo ? P.expInfo(x) : null;
        const role = g.isRuler(x) ? '君主' : c && c.governor === x.id ? c.name + '太守' : '';
        let office = '';
        try { if (SG.TitleUI && SG.TitleUI.officeTag) office = SG.TitleUI.officeTag(x) || ''; } catch (err) { office = ''; }
        const W = (SG.WorldInfo || {})[x.name];
        const age = x.born > 0 && g.year >= x.born ? '生年 ' + x.born + ' · ' + (g.year - x.born) + ' 岁' : '生年不详';
        const vk = P && P.vetK ? P.vetK(x) : 1;
        win.title.innerHTML = UI().frame(x, { size: 52 }) +
          '<span class="sg-char-name">' + esc(x.name) + '</span>' +
          '<span class="sg-char-meta"><span>' + (f ? '<i class="sg-pennant" style="--fc:' + f.color + '"></i>' + esc(f.name) : '在野') + (role ? ' · ' + esc(role) : '') + (office ? ' · ' + office : '') + '</span>' +
          '<span class="sg-muted">' + (W && W.full && W.full !== x.name ? esc(W.full) + ' · ' : '') + (W && W.role ? esc(W.role) + ' · ' : '') + esc(age) + '</span></span>';
        if (e) {
          win.info.innerHTML = '<span class="sg-lvseal"><small>等级</small><b>' + e.level + '</b></span><span class="sg-char-exp"><span>经验 ' + (e.max ? '已满级' : n0(e.exp) + ' / ' + n0(e.next)) + '</span>' +
            UI().brushBar(e.max ? 1 : e.pct / 100, 'var(--gold)', { cls: 'is-exp' }) + (vk > 1.0001 ? '<span class="sg-muted">老练 +' + Math.round((vk - 1) * 100) + '%</span>' : '') + '</span>';
        } else win.info.innerHTML = '';
        const many = st.list && st.list.length > 1;
        st.prev.style.visibility = many ? '' : 'hidden';
        st.next.style.visibility = many ? '' : 'hidden';
        // 新招 · 装备 角标
        const mine = isMine(x);
        const op = mine && P && P.openings ? P.openings(x) : { special: 0, tactic: 0 };
        st.tabs.setBadge('skills', op.special > 0 ? '新' : 0);
        st.tabs.setBadge('tactics', op.tactic > 0 ? '新' : 0);
        st.tabs.setBadge('gear', mine && gearHint(x) ? '新' : 0);
        for (const t of TABS) {
          const b = st.tabs.el.querySelector('[data-tab="' + t.id + '"]');
          if (b) { const off = !mine && !t.ro; b.disabled = off; b.classList.toggle('is-disabled', off); }
        }
        if (!mine && !TABS.find(t => t.id === st.tabs.get()).ro) { st.tabs.set('overview', true); st.tab = 'overview'; }
      }
      function renderActs() {
        const A = st.acts, x = st.gen, g = G();
        A.innerHTML = '';
        const mine = isMine(x);
        const ss = SG.Game && SG.Game.strategy;
        const busy = !ss || !ss.hud ? '战斗中只能查看。' : ss.busy || ss.endMonth ? '指令或月末处理进行中，请稍候。' : null;
        const c = g.cities[x.city];
        if (!mine) {
          const found = x.faction < 0 && g.freeFoundIn && c && g.freeFoundIn(c.id).indexOf(x) >= 0 && c.owner === g.player;
          if (found) UI().button(A, '登用', () => run(() => ss.cmdHire(c)), { kind: 'plaque', icon: 'hire', why: busy || (g.tokens > 0 ? null : '本月令牌已用完。') });
          h('div', 'sg-note', x.faction >= 0 ? '他国武将：只能查看' : '在野武将：只能查看', A);
          return;
        }
        const isR = g.isRuler(x);
        const inOwn = c && c.owner === g.player;
        const B = SG.Balance;
        UI().button(A, '赏赐', () => run(() => ss.cmdReward(c, x)), { kind: 'plaque', icon: 'reward',
          why: busy || (isR ? '君主无需赏赐。' : x.loyalty >= 100 ? '忠诚已满。' : !inOwn ? '不在己方城中。' : c.gold < (B ? B.RewardGold : 0) ? c.name + '金不足 ' + (B ? B.RewardGold : '') + '。' : null) });
        const itemsOn = !!(SG.ShopScreen && SG.Items && SG.Items.enabled !== false && g.items);
        if (itemsOn) UI().button(A, '赐宝物', () => run(() => SG.ShopScreen.gift(x)), { kind: 'plaque', icon: 'gift',
          why: busy || (isR ? '君主无需赏赐。' : !SG.Items.armoury(g.player).length ? '府库里没有可赐的宝物。' : null) });
        const C = SG.Commands;
        let mvWhy = busy || (x.moved ? '本月已行动。' : g.tokens > 0 ? null : '本月令牌已用完。');
        if (!mvWhy && C && C.moveBlocked) mvWhy = C.moveBlocked(c);
        UI().button(A, '移动', () => run(() => ss.cmdMove(c, x)), { kind: 'plaque', icon: 'move', why: mvWhy });
        const titlesOn = !!(SG.TitleUI && SG.Titles && SG.Titles.enabled !== false);
        if (titlesOn) UI().button(A, x.office ? '官职' : '授官', () => run(() => (x.office ? SG.TitleUI.manage(x) : SG.TitleUI.grant(x))), { kind: 'plaque', icon: 'title', why: busy || (isR ? '君主不任官职。' : null) });
        UI().button(A, '演武', () => { if (SG.DuelGame && SG.DuelGame.practice) run(() => SG.DuelGame.practice(x)); else { st.tabs.set('skills'); if (st.stage) st.stage.setPose('drill'); } }, { kind: 'plaque', icon: 'combo' });
        h('div', 'sg-spacer', null, A);
        if (titlesOn && x.office && !isR) UI().button(A, '罢免', () => run(() => SG.TitleUI.manage(x)), { kind: 'ghost', why: busy });
        const P = SG.Progress;
        if (P && P.candidates && P.candidates(x, 3).length) UI().button(A, '习得新招', () => run(() => ss.learnDialog(x)), { kind: 'seal', icon: 'special', why: busy });
      }
      // 动作：用原有指令（模态叠在人物菜单上），结束后重绘
      async function run(fn) {
        const ss = SG.Game && SG.Game.strategy;
        try {
          if (ss && ss._wrap) await ss._wrap(fn); else await fn();
        } catch (e) { console.error(e); }
        if (!st.win.closed) { renderHead(); renderActs(); renderTab(); if (st.stage) st.stage.setGen(st.gen); }
      }
      st.run = run;
      function renderTab() {
        const page = win.page;
        page.innerHTML = '';
        page.scrollTop = 0;
        page.dataset.tab = st.tab;
        const x = st.gen;
        try {
          const B = BUILD[st.tab] || (EXTRA[st.tab] && EXTRA[st.tab].build);
          if (B) B(page, x, st);
        } catch (e) { console.error(e); h('div', 'sg-empty', '这一页暂时无法显示', page); }
        if (SG.Motion) SG.Motion.play(page, [{ opacity: 0, transform: 'translateY(6px)' }, { opacity: 1, transform: 'none' }], { dur: 160 });
      }
      st.renderTab = renderTab;
      st.renderHead = renderHead;
      // 第四版 TITLES：官爵页里晋升 / 改任 / 换名号 / 罢免之后，头部的官名随之重绘（title-ui.js 的官爵页发出冒泡的 'sg-office-change'）
      win.el.addEventListener('sg-office-change', () => { if (!st.win.closed) renderHead(); });
      st.show(gen, null);
      return promise;
    },
  };
  function isMine(x) { const g = G(); return !!g && x.faction === g.player && x.faction >= 0; }
  function voice(x) {
    const now = performance.now();
    if (now - lastVoice < 6000) return;
    lastVoice = now;
    try { if (SG.Voice) SG.Voice.say(x, 'ui.generalOpen', { pri: 1, scene: 'strategy' }); } catch (e) { /* 无人声 */ }
  }
  function gearHint(x) {
    const Gr = SG.Gear;
    if (!Gr || !SG.Items || SG.Items.enabled === false || !G().items) return false;
    try {
      for (const sl of SG.Items.SLOTS) {
        if (x.gear && x.gear[sl] != null) continue;
        if (Gr.candidates(x, sl, { shop: false }).some(c => c.ok && c.from !== 'general' && c.delta > 0)) return true;
      }
    } catch (e) { /* 忽略 */ }
    return false;
  }
  function sec(parent, title, right) {
    return h('div', 'sg-sec', '<span class="sg-sec-t">' + title + '</span>' + (right ? '<span class="sg-sec-r">' + right + '</span>' : ''), parent);
  }
  const itemName = it => (SG.Items && SG.Items.displayName ? SG.Items.displayName(it) : it.name);
  const rarityColor = r => { try { return SG.Items.data.rarities[r].color; } catch (e) { return ''; } };

  // ---------------------------------------------------------------- 各页 --
  const BUILD = {
    // ---- 总览 ----
    overview(page, x, st) {
      const g = G();
      const cols = h('div', 'sg-ov', null, page);
      const L = h('div', 'sg-ov-l', null, cols), R = h('div', 'sg-ov-r', null, cols);
      // 能力
      let fam = null;
      try { fam = SG.Titles && SG.Titles.family ? SG.Titles.family(x) : null; } catch (e) { fam = null; }
      if (!fam) fam = x.war >= x.intel && x.war >= x.pol ? 'war' : x.intel >= x.pol ? 'intel' : 'pol';
      sec(L, '能　力', '最高：' + FAM_ONE[fam] + ' → ' + FAM_ZH[fam]);
      const Gr = SG.Gear && SG.Items && SG.Items.enabled !== false && g.items ? SG.Gear : null;
      const eq = Gr ? Gr.equipped(x) : [];
      const stats = h('div', 'sg-ov-stats', null, L);
      for (const [k, zh, col] of STAT) {
        const base = x[k] | 0;
        let eff = base;
        try { if (Gr) eff = Gr.eff(x, k); } catch (e) { eff = base; }
        const d = eff - base;
        const src = eq.filter(e => e.item && e.item.stats && e.item.stats[k]).map(e => esc(itemName(e.item)) + ' ' + (e.item.stats[k] > 0 ? '+' : '') + e.item.stats[k]).join(' · ');
        h('div', 'sg-ov-stat', '<span class="sg-ov-sl" style="color:' + col + '">' + zh + '</span><b class="sg-num">' + eff + (d ? '<sup>' + (d > 0 ? '+' : '') + d + '</sup>' : '') + '</b>' +
          UI().brushBar(Math.min(1, base / 110), col, { gear: d > 0 ? Math.min(1, d / 110) : 0 }) + '<span class="sg-ov-src">' + src + '</span>', stats);
      }
      // 战场与单挑
      const D = CharSheet.derive(x);
      // 阵型：出征与守城各按其打分自动布阵（battle-model aiFormation）；下面的攻防会心按出征时的阵型
      const fmLine = !D.formation ? '' : D.formationDef && D.formationDef.index !== D.formation.index
        ? '出征 ' + esc(D.formation.name) + ' · 守城 ' + esc(D.formationDef.name) + ' · 数字按出征'
        : '阵型 ' + esc(D.formation.name) + '（出征、守城相同）';
      sec(L, '战场与单挑', fmLine);
      const cards = h('div', 'sg-ov-cards', null, L);
      const card = (lb, v, lines) => h('div', 'sg-ov-card', '<small>' + lb + '</small><b class="sg-num">' + v + '</b><span>' + esc(lines.join(' · ') || '—') + '</span>', cards);
      card('攻击', '×' + D.atkK.toFixed(2), D.lines.atkK);
      card('防御', '×' + D.defK.toFixed(2), D.lines.defK);
      card('会心', Math.round(D.crit * 1000) / 10 + '%', D.lines.crit);
      card('计策威力', pct(D.tacK - 1), D.lines.tacK);
      card('单挑体力', String(D.duelHp), D.lines.duelHp);
      card('机动', (D.mob >= 0 ? '+' : '') + D.mob, D.lines.mob);
      // 招式
      sec(L, '招　式');
      h('div', 'sg-ov-moves', movesChips(x, st), L);
      // 右栏：官爵 · 装备 · 生平 · 忠诚
      sec(R, '官　爵');
      R.appendChild(officeCard(x, st));
      if (Gr || SG.Items) {
        sec(R, '装　备', isMine(x) ? '点按进入「装备」' : '');
        const strip = h('div', 'sg-ov-gear', null, R);
        for (const sl of (SG.Items && SG.Items.SLOTS) || ['weapon', 'offhand', 'armour', 'helmet', 'mount', 'treasure']) {
          const it = Gr ? Gr.item(x, sl) : null;
          const b = h('button', 'sg-ov-slot', UI().slot(it, { size: 48, slot: sl }), strip);
          b.type = 'button';
          UI().tip(b, () => it ? '<b style="color:' + rarityColor(it.rarity) + '">' + esc(itemName(it)) + '</b><br>' + esc(SG.ShopScreen ? SG.ShopScreen.statsText(it) : '') : UI().SLOT_ZH[sl] + '：空');
          b.addEventListener('click', () => { st.sel.slot = sl; st.tabs.set('gear'); });
        }
      }
      const W = (SG.WorldInfo || {})[x.name];
      sec(R, '生　平', W ? '<span class="sg-sealmark is-small">史</span>' : '');
      if (W && (W.note || W.role)) h('div', 'sg-ov-bio', esc(W.note || W.role) + (W.liberty ? '<div class="sg-muted">游戏取舍：' + esc(W.liberty) + '</div>' : ''), R);
      else h('div', 'sg-ov-bio sg-muted', '（' + esc(x.name) + '的生平小传待补。）', R);
      sec(R, '忠诚 · 羁绊');
      const isR = g.isRuler(x);
      h('div', 'sg-ov-loy', '<span class="sg-heart' + (!isR && x.loyalty < 60 ? ' is-low' : '') + '">' + icon('loyalty', 22) + '<b class="sg-num">' + (isR ? 100 : x.loyalty) + '</b></span>' + bondsInline(x), R);
    },

    // ---- 装备 ----
    gear(page, x, st) {
      const g = G(), Iv = SG.Items, Gr = SG.Gear;
      if (!Iv || Iv.enabled === false || !Gr || !g.items) {
        h('div', 'sg-empty', icon('armour', 40) + '<div>装备系统未开启（设置或存档中关闭）。</div>', page);
        return;
      }
      const mine = isMine(x);
      const box = h('div', 'sg-gear', null, page);
      const L = h('div', 'sg-gear-l', null, box), R = h('div', 'sg-gear-r', null, box);
      const grid = h('div', 'sg-gear-doll', null, L);
      const SL = Iv.SLOTS;
      if (SL.indexOf(st.sel.slot) < 0) st.sel.slot = SL[0];
      const slotEls = {};
      for (const sl of SL) {
        const it = Gr.item(x, sl);
        const b = h('button', 'sg-gear-slot' + (st.sel.slot === sl ? ' is-sel' : '') + (it ? '' : ' is-empty'), null, grid);
        b.type = 'button';
        b.innerHTML = UI().slot(it, { size: 52, slot: sl }) + '<span class="sg-gear-sd"><small>' + UI().SLOT_ZH[sl] + '</small>' +
          (it ? '<b style="color:' + rarityColor(it.rarity) + '">' + esc(itemName(it)) + '</b><span>' + esc(SG.ShopScreen ? SG.ShopScreen.statsText(it) : '') + '</span>'
            : '<b class="sg-muted">— 空 —</b><span>' + (mine ? '点按：从府库或市场选一件' : '') + '</span>') + '</span>';
        b.addEventListener('click', () => { st.sel.slot = sl; UI().sfx('ui-press-paper'); for (const k in slotEls) slotEls[k].classList.toggle('is-sel', k === sl); renderCands(); if (SG.isTouch && document.documentElement.classList.contains('sg-short')) candSheet(); });
        slotEls[sl] = b;
      }
      // 摘要：攻击 · 机动 · 会心 · 单挑体力（悬停候选时显示属性差）
      const D = CharSheet.derive(x);
      const sum = h('div', 'sg-gear-sum', null, L);
      const sc = (lb, v) => h('div', 'sg-ov-card', '<small>' + lb + '</small><b class="sg-num">' + v + '</b>', sum);
      sc('攻击', '×' + D.atkK.toFixed(2)); sc('防御', '×' + D.defK.toFixed(2)); sc('会心', Math.round(D.crit * 1000) / 10 + '%'); sc('单挑体力', String(D.duelHp));
      const delta = h('div', 'sg-gear-delta sg-muted', '把指针移到右侧候选上，比较属性的增减。', L);
      // 神兵之能
      let ab = [];
      try { ab = Gr.summary(x).abilities || []; } catch (e) { ab = []; }
      for (const a of ab) h('div', 'sg-gear-abil', UI().slot(Gr.item(x, a.slot), { size: 34, slot: a.slot }) + '<div><b>「' + esc(a.name) + '」</b> ' + esc(a.desc || '') + '</div>', L);
      let locks = [];
      try { locks = Gr.summary(x).locks || []; } catch (e) { locks = []; }
      for (const lk of locks) h('div', 'sg-gear-lock', icon('lock', 16) + esc(UI().SLOT_ZH[lk.slot] + '：' + (lk.why || '要求不足')), L);
      // 候选栏
      function renderCands() {
        R.innerHTML = '';
        const sl = st.sel.slot;
        const cur0 = Gr.item(x, sl);
        let cands = [];
        try { cands = mine ? Gr.candidates(x, sl) : []; } catch (e) { cands = []; }
        const nArm = cands.filter(c => c.from === 'armoury' || c.from === 'vault').length, nShop = cands.filter(c => c.from === 'shop').length;
        const c0 = g.cities[x.city];
        h('div', 'sg-sec', '<span class="sg-sec-t">' + icon(UI().SLOT_ICON[sl], 20) + UI().SLOT_ZH[sl] + (mine ? ' · 可换' : '') + '</span><span class="sg-sec-r">府库 ' + nArm + (nShop ? ' · ' + esc(c0 ? c0.name : '') + '市 ' + nShop : '') + '</span>', R);
        const list = h('div', 'sg-cands', null, R);
        if (!mine) { h('div', 'sg-empty', '他国 / 在野武将：只能查看', list); return; }
        if (!cands.length) h('div', 'sg-empty', '府库与本城市场都没有可用的' + UI().SLOT_ZH[sl], list);
        for (const c of cands.slice(0, 24)) {
          const it = c.item;
          const from = c.from === 'shop' ? esc(g.cities[c.city].name) + '市' : c.from === 'general' ? esc(g.generals[c.holder].name) + '佩' : c.from === 'vault' ? '城库' : '府库';
          const right = c.from === 'shop' ? '<b class="sg-num">' + n0(c.price) + ' 金</b>' : !c.ok ? '<span class="sg-bad">' + esc(c.why) + '</span>' : c.delta > 0 ? '<span class="sg-good">✓</span>' : '—';
          const row = h('button', 'sg-cand' + (c.ok ? '' : ' is-off') + (c.delta > 0 ? ' is-up' : c.delta < 0 ? ' is-down' : ''), UI().slot(it, { size: 40, slot: sl }) +
            '<span class="sg-cand-m"><b style="color:' + rarityColor(it.rarity) + '">' + esc(itemName(it)) + '</b><span>' + esc(SG.ShopScreen ? SG.ShopScreen.statsText(it) : '') + '</span></span>' +
            '<span class="sg-cand-r"><small>' + from + '</small>' + right + '</span>', list);
          row.type = 'button';
          row.addEventListener('pointerenter', () => { delta.classList.remove('sg-muted'); delta.innerHTML = cmpHtml(x, it, cur0); });
          row.addEventListener('click', () => pick(c));
        }
        if (cur0) {
          const un = UI().button(R, '卸下', () => doUnequip(sl), { kind: 'ghost', small: true, className: 'sg-cand-un' });
          let bound = false;
          try { bound = !!(x.gear && x.gear[sl] != null && Iv.inst(x.gear[sl]).bound); } catch (e) { bound = false; }
          if (bound) un.setEnabled('专属兵器不能卸下。');
        }
        h('div', 'sg-note', '绿 = 比现有好；红 = 变差或条件不符（灰显并说明）。换下的装备回到府库。', R);
      }
      function cmpHtml(gen, it, curIt) {
        const K = Iv.data && Iv.data.statKeys ? Iv.data.statKeys : {};
        const keys = new Set(Object.keys(it.stats || {}).concat(Object.keys((curIt && curIt.stats) || {})));
        const out = [];
        for (const k of keys) {
          const a = curIt && curIt.stats && typeof curIt.stats[k] === 'number' ? curIt.stats[k] : 0, b = typeof (it.stats || {})[k] === 'number' ? it.stats[k] : 0;
          const d = Math.round((b - a) * 100) / 100;
          if (!d) continue;
          const good = k === 'fireWeak' ? d < 0 : d > 0;
          const sk = K[k] || {};
          out.push('<span class="' + (good ? 'sg-good' : 'sg-bad') + '">' + esc(sk.name || k) + ' ' + (d > 0 ? '+' : '') + d + (sk.unit === '%' ? '%' : '') + (good ? ' ▲' : ' ▼') + '</span>');
        }
        return '<b>' + esc(itemName(it)) + '</b> 对比 ' + (curIt ? '「' + esc(itemName(curIt)) + '」' : '空位') + '：' + (out.length ? out.join(' · ') : '数值相同');
      }
      async function pick(c) {
        if (!c.ok) { UI().deny(null, esc(c.why || '不能装备')); return; }
        const ss = SG.Game && SG.Game.strategy;
        if (ss && (ss.busy || ss.endMonth)) { UI().deny(null, '指令执行中，请稍候。'); return; }
        if (c.from === 'shop') {
          const ok = await UI().confirm('买下「' + itemName(c.item) + '」并给' + x.name + '装备？', '买下 · ' + n0(c.price) + ' 金', '取消', { body: SG.ShopScreen ? SG.ShopScreen.card(c.id, { gen: x }) : '' });
          if (!ok) return;
          const r = Iv.buy(x.faction, g.cities[c.city], c.entry, { gen: x });
          if (!r || !r.ok) { UI().deny(null, esc((r && r.why) || '买不成。')); return; }
          UI().sfx('ui-coin-out');
        } else {
          let cost = null;
          try { if (c.from === 'general') cost = Iv.takeCost(g.generals[c.holder], c.uid, null); } catch (e) { cost = null; }
          if (cost && cost.cost) {
            const ok = await UI().confirm('从' + g.generals[c.holder].name + '处取来「' + itemName(c.item) + '」？', '取来', '取消', { body: '<span class="sg-bad">' + esc(g.generals[c.holder].name) + '忠诚 −' + cost.cost + '</span>' });
            if (!ok) return;
          }
          const r = Gr.equip(x, c.uid);
          if (!r.ok) { UI().deny(null, esc(r.why || '不能装备')); return; }
          if (r.loyaltyDelta) UI().toast(esc(x.name) + '忠诚 ' + (r.loyaltyDelta > 0 ? '+' : '') + r.loyaltyDelta, { seconds: 2 });
        }
        UI().sfx('ui-press-bronze');
        closeSheet();
        if (st.stage) { st.stage.puff(); st.stage.setGen(x); }
        st.renderHead(); st.renderTab();
      }
      function doUnequip(sl) {
        closeSheet();
        const r = Gr.unequip(x, sl);
        if (!r.ok) { UI().deny(null, esc(r.why)); return; }
        UI().sfx('ui-press-paper');
        if (st.stage) { st.stage.puff(); st.stage.setGen(x); }
        st.renderHead(); st.renderTab();
      }
      // 手机：点按装备位 → 候选在底部抽屉里（UI-V4 §9.4）；选定后关上抽屉
      function candSheet() {
        if (st._sheet) st._sheet.close();
        const s = st._sheet = UI().sheet({ title: UI().SLOT_ZH[st.sel.slot] + ' · 可换', onClose: () => { if (st._sheet === s) st._sheet = null; } });
        s.body.appendChild(R);
      }
      const closeSheet = () => { if (st._sheet) { const s = st._sheet; st._sheet = null; s.close(); } };
      renderCands();
    },

    // ---- 武艺 ----
    skills(page, x, st) {
      const g = G(), Cb = SG.Combos;
      const top = h('div', 'sg-sk', null, page);
      if (Cb && Cb.describe) {
        let d = null;
        try { d = Cb.describe(x, g.player, g); } catch (e) { d = null; }
        if (d) {
          let preset = 'v4';
          try { preset = SG.isTouch ? 'touch' : (SG.DuelGame && SG.DuelGame.settings ? (SG.DuelGame.settings().keys || 'v4') : 'v4'); } catch (e) { preset = 'v4'; }
          const keys = (Cb.KEYS && Cb.KEYS[preset]) || { L: 'J', M: 'K', H: 'L' };
          const L = h('div', 'sg-sk-l', null, top);
          sec(L, '招式谱 · 八式连段', esc(keys.L + ' 轻 · ' + keys.M + ' 中 · ' + keys.H + ' 重'));
          const row = h('div', 'sg-combo', null, L);
          d.steps.forEach((s, i) => {
            const t = s.type;
            const fin = i === 7;
            const tag = s.tags.length ? s.tags[s.tags.length - 1] === '可被看破' && s.tags.length > 1 ? s.tags[0] : s.tags[0] : '';
            const el = h('div', 'sg-cstep t-' + (t === '?' ? 'u' : t) + (fin ? ' is-fin' : ''), '<kbd>' + (s.known ? esc(keys[t] || '') : '？') + '</kbd>' +
              '<span class="sg-cseal">' + esc(s.glyph) + '</span><small>' + (s.known ? ({ L: '轻', M: '中', H: '重' }[t]) + ' ' + s.dmgPct + '%' : '未见识') + '</small>' +
              (fin ? '<span class="sg-ctag is-fin">终结</span>' : tag ? '<span class="sg-ctag">' + esc(tag) + '</span>' : ''), row);
            UI().tip(el, () => s.known ? '第 ' + s.i + ' 式 · ' + esc(s.tags.join(' · ') || '终结技') : '在单挑中见识过的招式会记下来');
            if (i < 7) h('i', 'sg-carrow', '›', row);
          });
          const Rr = h('div', 'sg-sk-r', null, top);
          h('div', 'sg-fin-name', esc(d.finisher.name), Rr);
          const T = d.totals;
          h('div', 'sg-fin-tot', '全中约 ' + T.dmgPct + '%' + (T.time ? ' · 用时 ' + T.time.toFixed(1) + ' 秒' : '') + ' · 终结会心 +' + Math.round(T.finCrit * 100) + '%', Rr);
          h('div', 'sg-stars', '<span>' + '★'.repeat(d.stars) + '<i>' + '☆'.repeat(Math.max(0, 3 - d.stars)) + '</i></span> ' + ['未练', '熟练', '精熟', '化境'][d.stars], Rr);
          const pr = UI().button(Rr, '演武场', () => { if (SG.DuelGame && SG.DuelGame.practice) st.run(() => SG.DuelGame.practice(x)); else if (st.stage) st.stage.setPose('drill'); }, { kind: 'seal', icon: 'tourney' });
          if (!(SG.DuelGame && SG.DuelGame.practice)) pr.title = '演武场随单挑模块开放；现在先在左侧立绘台演示这套连段';
          if (d.hint) h('div', 'sg-note', esc(d.hint), Rr);
        }
      }
      // 必杀
      const S = SG.Specials, P = SG.Progress;
      let list = [];
      try { list = S ? (typeof S.known === 'function' ? S.known(x) : [S.of(x)].filter(Boolean)) : []; } catch (e) { list = []; }
      const sl = P && P.specialSlots ? P.specialSlots(x) : null;
      sec(page, '必　杀', sl ? '已学 ' + sl.used + ' / ' + sl.max + (sl.open ? ' · <b class="sg-good">可学 ' + sl.open + '</b>' : '') : '');
      const grid = h('div', 'sg-spgrid', null, page);
      for (const sp of list) {
        const col = S.uiColor ? S.uiColor(sp) : sp.color || '#c0392b';
        const tier = sp.pool ? (S.tierName ? S.tierName(sp.tier) : sp.tier + '阶') : '专属';
        let rules = '';
        try { rules = S.rules ? S.rules(sp) : ''; } catch (e) { rules = ''; }
        h('div', 'sg-spcard', '<span class="sg-sp-ic" style="--c:' + col + '">' + icon('special', 26) + '</span><div><b>' + esc(sp.name) + '</b> <span class="sg-ctag ' + (sp.pool ? '' : 'is-sig') + '">' + esc(tier) + '</span>' +
          '<p>' + esc(sp.desc || '') + '</p>' + (rules ? '<small class="sg-muted">' + esc(rules) + '</small>' : '') + '</div>', grid);
      }
      if (isMine(x) && sl && sl.open > 0 && P.candidates(x, 3).length) {
        const b = h('button', 'sg-spcard is-open', icon('special', 22) + '<span>＋ 可习得新招（' + esc(S && S.tierName ? S.tierName(sl.tier) : sl.tier + '阶') + '以内）· 从 3 个候选中选择</span>', grid);
        b.type = 'button';
        b.addEventListener('click', () => st.run(() => SG.Game.strategy.learnDialog(x)));
      }
      if (!list.length && !(sl && sl.open)) h('div', 'sg-empty', '尚无必杀', grid);
    },

    // ---- 计略 ----
    tactics(page, x, st) {
      const T = SG.Tactics, P = SG.Progress;
      let list = [];
      try { list = T && T.known ? T.known(x) : SG.Defs.tactics.filter(t => x.intel >= t.reqInt); } catch (e) { list = []; }
      const sl = P && P.tacticSlots ? P.tacticSlots(x) : null;
      const D = CharSheet.derive(x);
      sec(page, '计　策', (sl ? '已学 ' + sl.used + ' / ' + sl.max + ' · ' : '') + '原四计按智力自带');
      if (D.lines.tacK.length) h('div', 'sg-note', '加成：' + esc(D.lines.tacK.join(' · ')) + '（计策威力 ' + pct(D.tacK - 1) + '）', page);
      const grid = h('div', 'sg-tacgrid', null, page);
      for (const t of list) {
        const isNew = T && T.isNew ? T.isNew(t) : false;
        const tier = isNew ? (T.TIER_NAME[t.tier] || '') : '自带';
        let p = null;
        // 对一般对手（智力 60）的成功率（与 battle-model.tacticChance 同式）
        if (typeof t.kind !== 'string') p = t.kind === (SG.TacticKind && SG.TacticKind.Inspire) ? 1 : Math.max(0.12, Math.min(0.92, 0.5 + (x.intel - 60) / 110));
        else if (t.roll) p = Math.max(t.roll.min, Math.min(t.roll.max, t.roll.base + (x.intel - 60) / t.roll.div));
        else p = 1;
        let rules = '';
        try { rules = T && T.rules && isNew ? T.rules(t) : ''; } catch (e) { rules = ''; }
        h('div', 'sg-taccard' + (isNew ? ' is-new' : ''), '<div class="sg-tac-h">' + icon('tactic', 22) + '<b>' + esc(t.name) + '</b><span class="sg-ctag">' + esc(tier) + '</span></div>' +
          '<p>' + esc((T && T.explain ? T.explain(t) : t.desc) || '') + '</p>' + (rules ? '<small class="sg-muted">' + esc(rules) + '</small>' : '') +
          '<div class="sg-tac-p">成功率约 <b class="sg-num">' + Math.round(p * 100) + '%</b><span class="sg-muted">（对智力 60）</span></div>', grid);
      }
      if (isMine(x) && sl && sl.open > 0 && P.candidates(x, 3).some(c => c.type === 'tactic')) {
        const b = h('button', 'sg-taccard is-open', '＋ 可习得新计（' + esc(T && T.TIER_NAME ? T.TIER_NAME[sl.tier] || '' : '') + '以内）', grid);
        b.type = 'button';
        b.addEventListener('click', () => st.run(() => SG.Game.strategy.learnDialog(x)));
      }
      if (sl && !sl.open && sl.max > sl.used) h('div', 'sg-note', '升级后可学下一计（智力越高可学越多、越强的计策）。', page);
    },

    // ---- 部队 ----
    army(page, x, st) {
      const g = G();
      const box = h('div', 'sg-army', null, page);
      const L = h('div', 'sg-army-l', null, box), R = h('div', 'sg-army-r', null, box);
      const max = SG.maxTroops ? SG.maxTroops(x) : 10000;
      const c = g.cities[x.city];
      sec(L, '兵　力');
      h('div', 'sg-army-big', '<b class="sg-num">' + n0(x.troops) + '</b><span>/ ' + n0(max) + ' 上限（武力 ' + x.war + '）</span>' + UI().brushBar(x.troops / Math.max(1, max), 'var(--war)'), L);
      if (c) h('div', 'sg-note', esc(c.name) + '：兵 ' + n0(g.troopsIn ? g.troopsIn(c) : 0) + ' · 金 ' + n0(c.gold) + ' · 粮 ' + n0(c.food), L);
      sec(L, '训　练');
      h('div', 'sg-army-big', '<b class="sg-num">' + (x.training | 0) + '</b><span>/ 100</span>' + UI().brushBar((x.training | 0) / 100, 'var(--gold)'), L);
      h('div', 'sg-note', '训练影响战场的攻防与士气。', L);
      const ss = SG.Game && SG.Game.strategy;
      if (isMine(x) && c && c.owner === g.player && ss && ss.hud) {
        const row = h('div', 'sg-army-btns', null, L);
        const busy = ss.busy || ss.endMonth ? '指令执行中，请稍候。' : g.tokens > 0 ? null : '本月令牌已用完。';
        UI().button(row, '征兵（' + esc(c.name) + '）', () => st.run(() => ss.cmdRecruit(c)), { kind: 'plaque', icon: 'recruit', why: busy });
        UI().button(row, '训练（' + esc(c.name) + '）', () => st.run(() => ss.cmdTrain(c)), { kind: 'plaque', icon: 'train', why: busy });
      }
      // 阵型：开战时的阵型（与战场一致，CharSheet.battleFormation）。战斗模块支持「惯用阵型」时可选定（再点一次 = 回到自动），
      // 否则八阵只作说明：开战时按能力自动布阵，战场上可随时变阵
      const pref = CharSheet.prefSupported();
      const curF = CharSheet.battleFormations(x), cur = curF.atk, curD = curF.def;
      sec(R, pref ? '惯用阵型' : '阵　型', pref ? (x.formationPref != null ? '开战时用此阵 · 再点一次改回自动' : '自动：出征与守城各按能力布阵') : '出征、守城各自动布阵 · 战场上可随时变阵');
      const fg = h('div', 'sg-forms', null, R);
      const F = (SG.Defs && SG.Defs.formations) || [];
      const mine = isMine(x);
      F.forEach((fm, i) => {
        const lock = (fm.reqInt && x.intel < fm.reqInt) ? '需智 ' + fm.reqInt : (fm.reqWar && x.war < fm.reqWar) ? '需武 ' + fm.reqWar : '';
        const parts = [];
        if (fm.atk !== 1) parts.push('攻 ×' + fm.atk); if (fm.def !== 1) parts.push('防 ×' + fm.def); if (fm.move) parts.push('机动 +' + fm.move);
        const onA = cur === i, onD = curD === i, on = onA || onD;
        const tagTxt = onA && onD ? '攻守' : onA ? '出征' : onD ? '守城' : '';
        const b = h('button', 'sg-form' + (on ? ' is-on' : '') + (lock ? ' is-locked' : '') + (pref ? '' : ' is-info'), formGlyph(i) + '<b>' + esc(fm.name) + (on ? '<em class="sg-form-cur">' + tagTxt + '</em>' : '') + '</b><small>' + esc(parts.join(' · ')) + (lock ? '<br>' + esc(lock) : '') + '</small>', fg);
        b.type = 'button';
        b.setAttribute('aria-pressed', on ? 'true' : 'false');
        UI().tip(b, esc(fm.desc || '') + (on ? '<br><span class="sg-muted">' + (onA && onD ? '出征与守城' : onA ? '出征' : '守城') + '时开战的阵型' + (pref && x.formationPref == null ? '（自动）' : '') + '</span>' : ''));
        b.addEventListener('click', () => {
          if (!mine) return;
          if (lock) { UI().deny(b, esc(fm.name + '：' + lock)); return; }
          if (!pref) { UI().toast(esc(fm.name) + '：开战时按武将的能力自动布阵，战场上可随时变阵。', { seconds: 2.4, seal: '阵' }); return; }
          x.formationPref = x.formationPref === i ? null : i; UI().sfx('ui-press-paper');
          st.renderTab();
        });
      });
      sec(R, '兵种 · 坐骑');
      const cul = x.culture || 'han';
      const CN = (SG.Figure && SG.Figure.cultures && SG.Figure.cultures[cul] && SG.Figure.cultures[cul].name) || '';
      const mnt = SG.Gear && SG.Items && SG.Items.enabled !== false && g.items ? SG.Gear.item(x, 'mount') : null;
      h('div', 'sg-army-unit', '<span class="sg-army-ic">' + icon('troops', 34) + '</span><div><b>' + esc(CN || '本地') + '兵</b><small class="sg-muted">步骑混编 · 外观随文化</small></div>' +
        UI().slot(mnt, { size: 40, slot: 'mount' }) + '<span class="sg-muted">' + (mnt ? esc(itemName(mnt)) : '未乘马') + '</span>', R);
    },

    // ---- 官爵 ----
    office(page, x) {
      if (SG.TitleUI && SG.TitleUI.tab && SG.Titles && SG.Titles.enabled !== false) {
        try { const el = SG.TitleUI.tab(x); if (el) { el.classList.add('is-v4'); page.appendChild(el); return; } } catch (e) { console.error(e); }
      }
      h('div', 'sg-empty', icon('titles', 40) + '<div>官制未开启。</div>', page);
    },

    // ---- 关系（ENGAGEMENT §11.4 登记后替换）----
    bonds(page, x) {
      const g = G();
      const isR = g.isRuler(x);
      sec(page, '忠　诚');
      h('div', 'sg-ov-loy', '<span class="sg-heart' + (!isR && x.loyalty < 60 ? ' is-low' : '') + '">' + icon('loyalty', 26) + '<b class="sg-num">' + (isR ? 100 : x.loyalty) + '</b></span>' +
        '<span class="sg-muted">' + (isR ? '君主' : x.loyalty >= 90 ? '忠心耿耿' : x.loyalty >= 70 ? '尚称忠诚' : x.loyalty >= 60 ? '心有动摇' : '恐生异心') + '</span>', page);
      sec(page, '羁　绊');
      const bl = bondsInline(x);
      h('div', bl ? 'sg-bonds' : 'sg-empty', bl || icon('bonds', 40) + '<div>结义 · 知己 · 师徒 · 宿敌——羁绊随第四版「人事」开放。</div>', page);
    },

    // ---- 履历 ----
    history(page, x, st) {
      const g = G(), rec = x.record || {};
      const strip = h('div', 'sg-hist-strip', null, page);
      const kv = (k, v) => h('span', null, k + ' <b class="sg-num">' + (v | 0) + '</b>', strip);
      kv('战', rec.battles); kv('胜', rec.wins); kv('击溃', rec.routs); kv('单挑胜', rec.duels);
      if (G().tourney && G().tourney.stats && G().tourney.stats[x.name]) { const ts = G().tourney.stats[x.name]; kv('大会冠', ts.wins || 0); }
      const F = [['all', '全部'], ['war', '战功'], ['office', '官职'], ['item', '宝物'], ['tourney', '大会']];
      st.histF = st.histF || 'all';
      const seg = UI().segmented(page, F, st.histF, v => { st.histF = v; draw(); });
      seg.classList.add('sg-hist-f');
      const tl = h('div', 'sg-timeline', null, page);
      const ev = [];
      for (const s of g.log || []) if (String(s).indexOf(x.name) >= 0) ev.push({ text: String(s), kind: /官|拜|授|罢|晋/.test(s) ? 'office' : /宝|剑|刀|马|甲|得到|获得/.test(s) ? 'item' : /大会|武林/.test(s) ? 'tourney' : 'war' });
      try { if (SG.Titles && SG.Titles.history) for (const s of SG.Titles.history(x) || []) ev.push({ text: String(s), kind: 'office' }); } catch (e) { /* 无官制 */ }
      function draw() {
        tl.innerHTML = '';
        const list = ev.filter(e => st.histF === 'all' || e.kind === st.histF);
        if (!list.length) { h('div', 'sg-empty', '尚无记录', tl); return; }
        for (const e of list.slice().reverse()) {
          const m = /^(\d+年\d+月)\s*(.*)$/.exec(e.text);
          h('div', 'sg-slip-row k-' + e.kind, (m ? '<small class="sg-num">' + esc(m[1]) + '</small>' : '') + '<span>' + esc(m ? m[2] : e.text) + '</span>', tl);
        }
      }
      draw();
    },
  };

  // ---- 小部件 ----
  function movesChips(x, st) {
    const out = [];
    const S = SG.Specials, T = SG.Tactics, P = SG.Progress;
    try { for (const sp of S ? (S.known ? S.known(x) : [S.of(x)].filter(Boolean)) : []) out.push('<span class="sg-chip-m is-sp">' + icon('special', 14) + esc(sp.name) + '</span>'); } catch (e) { /* 忽略 */ }
    try { if (isMine(x) && P && P.openings) { const op = P.openings(x); if (op.special + op.tactic > 0) out.push('<span class="sg-chip-m is-open">＋ 可习得 ' + (op.special + op.tactic) + ' 招</span>'); } } catch (e) { /* 忽略 */ }
    try { for (const t of T && T.known ? T.known(x) : []) out.push('<span class="sg-chip-m is-tac">' + esc(t.name) + '</span>'); } catch (e) { /* 忽略 */ }
    try {
      const Cb = SG.Combos;
      if (Cb && Cb.describe) {
        const d = Cb.describe(x, G().player, G());
        if (d) out.push('<span class="sg-chip-m is-combo">' + d.steps.map(s => '<i class="t-' + (s.type === '?' ? 'u' : s.type) + '">' + esc(s.glyph) + '</i>').join('') + ' ' + esc(d.finisher.name) + (d.stars ? ' ' + '★'.repeat(d.stars) : '') + '</span>');
      }
    } catch (e) { /* 忽略 */ }
    return out.join('') || '<span class="sg-muted">—</span>';
  }
  function officeCard(x, st) {
    const el = document.createElement('div');
    el.className = 'sg-office-card';
    const g = G();
    let o = null;
    try { o = SG.Titles && SG.Titles.enabled !== false && SG.Titles.officeOf ? SG.Titles.officeOf(x) : null; } catch (e) { o = null; }
    if (g.isRuler(x)) {
      let rk = '';
      try { rk = SG.Titles && SG.Titles.rulerRankName ? SG.Titles.rulerRankName(g.factions[x.faction]) : ''; } catch (e) { rk = ''; }
      el.innerHTML = '<span class="sg-oc-ins">' + icon('titles', 40) + '</span><div><b>' + esc(rk || '君主') + '</b><small>' + esc(g.factions[x.faction].name) + '之主</small></div>';
      return el;
    }
    if (!o) {
      el.classList.add('is-none');
      el.innerHTML = '<span class="sg-oc-ins">' + icon('titles', 40) + '</span><div><b class="sg-muted">无官职</b><small>' + (isMine(x) && SG.TitleUI ? '可在「官爵」页或操作条「授官」' : '') + '</small></div>';
      return el;
    }
    let ins = '';
    try { ins = SG.TitleUI && SG.TitleUI.icon ? SG.TitleUI.icon(o.post, g.factions[x.faction], 44) : ''; } catch (e) { ins = ''; }
    let fx = '';
    try { fx = SG.Titles.effects(o.post).map(e => e.text).join(' · '); } catch (e) { fx = ''; }
    const since = o.since >= 0 ? Math.floor(o.since / 12) + (g.startYear || 0) : null;
    el.dataset.fam = o.fam;
    el.innerHTML = '<span class="sg-oc-ins">' + (ins || icon('titles', 40)) + '</span><div><b>' + esc(o.name) + '</b><small>' + esc(FAM_ZH[o.fam] || '') + ' · ' + esc(gradeOf(o.tier)) + (o.suspended ? ' · <span class="sg-bad">虚衔</span>' : '') + '</small>' +
      (fx ? '<span class="sg-good">' + esc(fx) + '</span>' : '') + '</div>';
    void since;
    return el;
  }
  // 品阶名：TITLES 的 tier 1–5 = 九品 · 七品 · 五品 · 三品 · 一品
  function gradeOf(t) {
    try { const d = SG.Titles && SG.Titles.tier ? SG.Titles.tier(t) : null; if (d && d.grade) return d.grade; } catch (e) { /* 缺省 */ }
    return ['', '九品', '七品', '五品', '三品', '一品'][t] || '';
  }
  function bondsInline(x) {
    const B = SG.Bonds;
    if (!B || typeof B.of !== 'function') return '';
    try {
      const list = B.of(x) || [];
      return list.slice(0, 6).map(b => '<span class="sg-bond">' + UI().frame(b.gen, { size: 30 }) + esc(b.gen.name) + ' · <i>' + esc(b.name || b.type) + '</i></span>').join('');
    } catch (e) { return ''; }
  }
  // 阵型的小图：八个点阵（单位布局）
  const FORM_DOTS = [
    [[1, 0], [0, 1], [2, 1], [1, 2], [1, 1]], [[0, 1], [1, 1], [2, 1], [3, 1], [4, 1]], [[2, 0], [1, 1], [3, 1], [0, 2], [2, 2], [4, 2]],
    [[0, 0], [4, 0], [1, 1], [3, 1], [2, 2]], [[1, 0], [2, 0], [0, 1], [3, 1], [1, 2], [2, 2]], [[2, 0], [1, 1], [3, 1], [0, 2], [4, 2]],
    [[0, 2], [1, 1], [2, 0], [3, 1], [4, 2]], [[0, 0], [1, 0], [3, 0], [4, 0], [2, 1], [2, 2]],
  ];
  function formGlyph(i) {
    const d = FORM_DOTS[i] || FORM_DOTS[0];
    return '<svg class="sg-form-g" viewBox="-1 -1 7 5" width="44" height="30" aria-hidden="true">' + d.map(p => '<rect x="' + (p[0] - 0.35) + '" y="' + (p[1] - 0.35) + '" width="0.7" height="0.7" rx="0.12"/>').join('') + '</svg>';
  }

  SG.CharacterScreen = CharacterScreen;
})();
