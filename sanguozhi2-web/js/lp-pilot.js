'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 低多边形美术试点：四个样例配方（SG.LPPilot）

   用 SG.LP（js/lowpoly.js）按《Lanternvale》的风格建模：
   · 人物：关羽 —— FFX 式风格化低多边形，真实成人的头身比（头顶到下巴 = 身高 / 7.8，头宽 0.16 m；枪兵 7.5 头身、头宽 0.155 m；
     脖子、斜方肌、手脚都按真人尺寸，脸靠五官设计而不是大头来读：粗眉、带高光的眼、楔形鼻、胡须、头巾与发髻的轮廓）：
     枣红脸（眉下颊面略暗、方下颌、颧骨）、丹凤眼（带高光）、卧蚕眉、巾帻下露出的黑发与鬓角、三绺美髯（上三分之一随头、其余随胸）、绿色巾帻（带飘带与玉饰）、
     翡翠绿袍（敞襟、暗绿衬里）罩金甲（护心镜、三层叠甲护肩、甲裙）、腰带与朱红束带、青龙偃月刀（烘进同一个蒙皮网格）。
     刚性蒙皮在 figure.js 的人形骨架上（SG.Figure.general 的骨骼与 setPose 片段直接驱动；袍裙侧片在胯部铰接；
     figure.js 的头关节在真人尺寸头部的下巴之下，姿势之后把头骨移到寰椎处转动 —— Pilot.headPivot）。
     另有同风格的汉军枪兵（SG.Figure.soldier 的骨架）：黑发、发髻、赤帻（两条短带随风）、杏仁眼带高光、封闭的铁甲裙片。
     静止 / 行进时长兵向外后倾，不挡脸（POLE_LEAN）。
   · 生物：战马 —— Lanternvale 生物的敦实：深胸低腹、上粗下细的腿（膝 / 飞节 / 球节隆起、外撇的蹄）、两条白袜、
     圆头大眼（高光）、竖耳、白鼻梁、奶白口鼻、整片鬃毛（下缘分成六绺、两色、随风）、一体的马尾；势力色马衣与鞍、胸前红缨。
     刚性蒙皮在 figure.js 的战马骨架上（SG.Figure.horse：idle / walk / trot / gallop），可骑乘（horse.mount(关羽)）。
   · 建筑：城门楼 —— 三色石块勒脚、收分的夯土墩台（墙脚潮暗、版筑分段、桁孔）、券门（暗色门框、两扇朱门各 5×5 金门钉、
     石门槛）、券上暗木金边门额；垛口女墙（垛色各异、偶有苔藓）；其上木构城楼：朱红立柱、米白墙与格窗（夜里透出暖光）、
     彩画斗拱、栏杆、匾额；歇山顶（灰蓝筒瓦色带、起翘的檐角、正脊与鸱吻）；扁圆竖肋的红灯笼（白天微亮，夜里照亮周围）、势力旗。
   · 树：槐树（按 Lanternvale BuildOak 的布局：粗壮分面树干、3 米处分枝、3.6 米起的软团树冠、上为叶色下为青的色阶、随风摆动）
     与松树（层叠的软锥层）。
   · 场景小件：草丛、野花、石头、灌木（半数带小花）、汉式灯杆（灯亭 + 小歇山帽）、城墙段（马面、勒脚、出水口）、
     地面（顶点色草地与土路、两道车辙、卵石、落叶）。

   接口
     SG.LPPilot.guanYu(opts?)   → { fig, mesh, setPose(clip, t, opts), tris, height }   fig = figure.js 的 Figure（隐藏其原网格）
     SG.LPPilot.soldier(opts?)  → 同上（汉军枪兵；opts.color 势力色）
     SG.LPPilot.horse(opts?)    → { horse, mesh, setPose(clip, t), mount(rider), tris }   opts.color 马衣势力色，opts.coat 毛色
     （城门楼、城墙、槐树、松树、灌木、灯杆已移入共用道具库 js/lp-props.js（SG.LPProps：gate_tower、city_wall、tree_huai、
      tree_pine、bush 'pilot'、lamp_post），下列函数是对它的包装，须先载入 lp-props.js）
     SG.LPPilot.gateTower({ lit }) → THREE.Mesh（带墨线），userData.lamps = 灯笼的模型坐标，userData.tris
     SG.LPPilot.tree(seed?) / pine(seed?) / rock(seed?) / cityWall(len) → THREE.Mesh；bush(seed?) → Mesh（奇数种子：Group，带无墨线的小花）
     SG.LPPilot.lampPost(lit) → Mesh（userData.lamp = 灯光位置）
     SG.LPPilot.grassField(opts) → 草丛 + 野花 + 路上卵石（pebbles / onRoad）+ 树下落叶（litter）（无墨线，随风摆）；
     SG.LPPilot.ground(opts) → 顶点色地面（无墨线；ruts: 车辙折线 → 比网格更细的柔和暗线）
     SG.LPPilot.headPivot(fig, rest, d)：姿势之后让头绕寰椎（头内、约耳垂高）转动，而不是绕 figure.js 的头关节
     SG.LPPilot.POLE_LEAN = { roll, back, clips }：静止 / 行进片段里长兵向外、向后倾，杆子不挡脸（Lanternvale PoleHold）
     SG.LPPilot.geometry(name, opts?) → 对应的 BufferGeometry（缓存）；SG.LPPilot.colors = 调色板
   ========================================================================== */
(function () {
  const SG = window.SG, LP = SG.LP, P = LP.Paint, V = LP.V, PAL = LP.PAL, H = P.hex;
  const D2R = Math.PI / 180;
  const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const sstep = t => { t = clamp01(t); return t * t * (3 - 2 * t); };
  const Pilot = (SG.LPPilot = {});

  /** Near-black hair would read as a solid ink blob: lift dark values towards a warm dark brown (BipedKit.HairTone). */
  function hairTone(c) {
    const v = Math.max(c[0], c[1], c[2]), lifted = 0.25 + 0.5 * v;
    if (v >= lifted) return c;
    const k = lifted / Math.max(0.02, v);
    return P.mix(c.map(x => clamp01(x * k)), [lifted, lifted * 0.8, lifted * 0.66], 0.36 * (lifted - v) / lifted);
  }
  Pilot.hairTone = hairTone;

  // ======================================================= biped kit ==
  // Built on figure.js joints (fig.pr.J, A-pose bind: arms 40° out, elbows straight, every bone's bind rotation identity),
  // facing +Z, Y up, the character's LEFT is +X (figure.js: right hand on −X). Each part is rigid on one bone.
  const HEAD_TILT = -12, JAW_FROM = 0.3, JAW_DROP = 0.12, EYE_TURN = 30;
  /**
   * The unit head: a sphere whose lower half narrows and drops into a jaw (q = the sphere height of a ring, chin pole −1 →
   * crown pole +1), shaped per (θ, ring) into an adult face: a square jaw angle, a chin pushed forward, cheekbones, a brow
   * ridge and flatter temples. Columns every 30° (θf = 90° − 30° j from the front, so one column runs down the middle of
   * the face, others through the eyes, the cheeks and the ears); rings at chin, jaw, mouth, cheek, eye and brow heights.
   * Returns { ys, rad[i][j] } before the tilt (x = rad·sin θf, z = rad·cos θf).
   */
  const HEAD_Q = [-1, -0.95, -0.84, -0.68, -0.47, -0.26, -0.06, 0.14, 0.42, 0.68, 0.9, 1], HEAD_SIDES = 12;
  const gauss = (v, m, s) => Math.exp(-((v - m) / s) * ((v - m) / s));
  function headGrid(chin, shape) {
    const narrow = clamp(0.2 - (chin - 1) * 0.3, 0.06, 0.4), sh = Object.assign({ jaw: 0.1, chin: 0.06, cheek: 0.05, brow: 0.04, temple: 0.04 }, shape || {});
    const ys = [], rad = [];
    for (const q of HEAD_Q) {
      let y = q, r = Math.sqrt(Math.max(0, 1 - q * q));
      if (q < 0) { const t = clamp01((-q - JAW_FROM) / (1 - JAW_FROM)); r *= 1 - narrow * t * t * (3 - 2 * t); y = q * (1 + JAW_DROP * q * q); }
      ys.push(y);
      const row = [];
      for (let j = 0; j < HEAD_SIDES; j++) {
        let tf = 90 - j * 360 / HEAD_SIDES; tf = ((tf + 540) % 360) - 180;
        const at = Math.abs(tf);
        const g = 1 + sh.jaw * gauss(q, -0.72, 0.17) * gauss(at, 75, 32) + sh.chin * gauss(q, -0.9, 0.11) * gauss(tf, 0, 24)
          + sh.cheek * gauss(q, -0.24, 0.15) * gauss(at, 55, 22) + sh.brow * gauss(q, 0.12, 0.1) * gauss(at, 20, 36)
          - sh.temple * gauss(q, 0.36, 0.2) * gauss(at, 90, 24);
        row.push(r > 0 ? r * g : 0);
      }
      rad.push(row);
    }
    return { ys, rad };
  }
  /** Extents of the unit head after the tilt (before the per-axis scale). */
  function headExtents(chin, shape) {
    const G = headGrid(chin, shape), a = HEAD_TILT * D2R, ca = Math.cos(a), sa = Math.sin(a);
    const e = { x0: 1e9, x1: -1e9, y0: 1e9, y1: -1e9, z0: 1e9, z1: -1e9 };
    G.ys.forEach((y, i) => G.rad[i].forEach((r, j) => {
      const t = j * 2 * Math.PI / HEAD_SIDES, x = Math.cos(t) * r, z = Math.sin(t) * r;
      const yy = y * ca - z * sa, zz = y * sa + z * ca;
      e.x0 = Math.min(e.x0, x); e.x1 = Math.max(e.x1, x); e.y0 = Math.min(e.y0, yy); e.y1 = Math.max(e.y1, yy); e.z0 = Math.min(e.z0, zz); e.z1 = Math.max(e.z1, zz);
    }));
    return e;
  }
  class Biped {
    constructor(fig, o) {
      o = o || {};
      this.fig = fig;
      const pr = fig.pr, k = pr.k, J = pr.J, b = o.bulk || 1;
      this.J = J; this.k = k; this.bulk = b;
      this.BI = {};
      SG.Figure.boneNames.forEach((n, i) => { this.BI[n] = i; });
      this.M = new LP.MeshBuilder(o.seed || 1);
      this.M.jitter = 0.05;
      // A real adult head on figure.js's (real adult) skeleton: crown → chin = height / headsTall (7.5 for a soldier, ~7.8 for
      // a heroic general), about 0.155 m wide and 0.19 m deep — an egg, not a ball. The skull's crown sits at the figure's
      // height, so the chin comes out ~2–3 cm above the head joint and the neck shows between the shoulders and the jaw.
      // The face reads at the game camera through its features (bold brows, dark eye shapes with a catch-light, a wedge
      // nose, beard, hair and headwear silhouettes), not through a big head.
      //   Rx, Ry, Rz: the head's semi-axes (the lathe's unit profile scaled per axis); Rzu = Rz / 0.96 (the unit the shells
      //   over the cranium use in z, so they keep ~4 % of clearance front and back); R: the feature unit (≈ Rx) for eyes,
      //   brows, nose, beard locks and other face-sized parts.
      this.H = pr.H;
      this.chinK = o.chin || 1; this.headShapeK = o.face || null;
      this.headG = headGrid(this.chinK, this.headShapeK);
      const ex = headExtents(this.chinK, this.headShapeK);
      this.headH = o.headH || pr.H / (o.headsTall || 7.5);
      this.headW = o.headW || 0.155 * pr.H / 1.8;
      this.headD = o.headD || 0.19 * pr.H / 1.8;
      this.Rx = this.headW / (ex.x1 - ex.x0); this.Ry = this.headH / (ex.y1 - ex.y0); this.Rz = this.headD / (ex.z1 - ex.z0);
      this.Rzu = this.Rz / 0.96;
      this.R = (o.featureK || 1) * this.Rx;
      this.crownY = o.crownY || pr.H;
      this.headC = [0, this.crownY - ex.y1 * this.Ry, o.headZ != null ? o.headZ : 0.004];
      // the face's reference line (the old head centre line): eyes ≈ headCY − 0.03 Ry (the head's midpoint), brows above,
      // nose base ≈ −0.5, mouth ≈ −0.68, chin ≈ −1.08 (adult thirds: brow → nose base → chin)
      this.headCY = this.headC[1] - 0.03 * this.Ry;
      // the head turns about the atlas (inside the skull, at about the ear lobes), not about figure.js's head joint, which
      // now sits below the chin of a real-size head: see Pilot.headPivot (applied after every pose)
      this.headPivot = [0, this.headC[1] - 0.5 * this.Ry, this.headC[2] - 0.15 * this.Rz];
      this.upL = pr.upL; this.foL = pr.foL;
      this.hipY = J.thighL[1]; this.pelvisY = J.pelvis[1] + 0.03 * k; this.spineY = J.spine[1]; this.chestY = J.chest[1];
      this.shoulderY = J.upperArmL[1]; this.shoulderX = J.upperArmL[0]; this.neckY = J.neck[1];
      this.waistR = 0.13 * k * b; this.chestR = 0.165 * k * lerp(1, b, 0.8); this.shoulderR = 0.17 * k * lerp(1, b, 0.8); this.hipR = 0.138 * k * b;
      this.depthK = 0.74;
      this.armR = 0.054 * k * b; this.foreR = 0.045 * k * b; this.thighR = 0.08 * k * b; this.shinR = 0.057 * k * b;
      this.footLen = 0.25 * k * lerp(1, b, 0.4); this.footW = 0.1 * k * lerp(1, b, 0.5);
      this.skin = o.skin || H('#f2cfae');
    }
    bone(n) { this.M.bone = this.BI[n]; return this; }
    /** Triangle bookkeeping per part (kit.parts). */
    mark(name) {
      const t = this.M.triangleCount, v = this.M.vertexCount;
      this.parts = this.parts || {}; this.parts[name] = t - (this._last || 0); this._last = t;
      // vertex / triangle ranges per part (measuring and review tools: [first, end))
      this.vparts = this.vparts || {}; this.vparts[name] = [this._lastV || 0, v]; this._lastV = v;
      this.tparts = this.tparts || {}; this.tparts[name] = [t - this.parts[name], t];
    }
    /** Arm frame: origin at the shoulder joint, the arm hanging along local −Y (A-pose). */
    armFrame(s) { const S = s > 0 ? 'L' : 'R'; this.M.push().translate(this.J['upperArm' + S]).rotate(0, 0, s * 40); return this; }

    // ------------------------------------------------ body
    torso(top, pelvis, ringCols) {
      const M = this.M, u = this.k;
      this.bone('pelvis'); M.color = pelvis;
      M.body([0, 0, 0], [[this.hipR * 0.82, this.hipY - 0.085 * u], [this.hipR, this.hipY - 0.02 * u], [this.hipR * 0.98, this.pelvisY + 0.02 * u], [this.waistR * 0.98, this.pelvisY + 0.06 * u]], 8, this.depthK * 1.06, true, false);
      this.bone('spine'); M.color = top;
      M.body([0, 0, 0], [[this.waistR, this.pelvisY - 0.01 * u], [this.waistR * 1.04, (this.pelvisY + this.spineY) / 2 + 0.02 * u], [this.waistR * 1.02, this.spineY + 0.06 * u]], 8, this.depthK, false, false);
      this.bone('chest'); M.color = top;
      // the shoulder line rises over the trapezius to the neck (an adult's neck base sits ~5 cm above the shoulder joints,
      // so the neck does not show from the collarbones up)
      const prof = [[this.waistR, this.spineY], [this.chestR, this.chestY], [this.chestR * 1.02, this.shoulderY - 0.07 * u], [this.shoulderR, this.shoulderY - 0.012 * u], [this.shoulderR * 0.84, this.shoulderY + 0.024 * u], [0.084 * u, this.neckY + 0.035 * u]];
      M.push().scale(1, 1, this.depthK);
      M.lathe(prof, 8, false, false, true, ringCols || null, 180 / 8);
      M.pop();
    }
    /** Neck: from inside the shoulders up into the skull behind the jaw (its top cap hidden in the head); an adult neck is
     * about as wide as the jaw (0.11–0.12 m), slightly flared into the trapezius at its base. */
    neck(c, thick = 1) {
      const k = this.k, rb = 0.062 * k * thick * lerp(1, this.bulk, 0.7), rt = 0.056 * k * thick * lerp(1, this.bulk, 0.5);
      const top = [0, this.headPivot[1] + 0.03 * this.Ry, this.headPivot[2]];
      this.bone('neck'); this.M.color = c;
      this.M.segment([0, this.shoulderY - 0.03 * k, -0.02 * k], top, rb, rt, 7);
      this.neckTop = top;
    }
    /** Upper arm, forearm and fist (cols: [upper, lower, hand]). */
    arm(s, upper, lower, hand, o) {
      o = o || {};
      const M = this.M, S = s > 0 ? 'L' : 'R', k = this.k;
      this.armFrame(s);
      this.bone('upperArm' + S); M.color = upper;
      if (!o.noShoulder) M.sphere([0, -0.012 * k, 0], this.armR * 1.15, 7, 4);
      if (!o.noUpper) M.segment([0, 0, 0], [0, -this.upL, 0], this.armR, this.armR * 0.86, 7);
      this.bone('foreArm' + S); M.color = lower;
      if (!o.noElbow) M.sphere([0, -this.upL, 0], this.foreR * 1.05, 6, 3);
      M.segment([0, -this.upL, 0], [0, -this.upL - this.foL + 0.01 * k, 0], this.foreR, this.foreR * 0.82, o.foreSides || 7);
      M.pop();
      this.fist(s, hand, o.fistK);
    }
    /** Fist and thumb; lowK (< 1) builds them with fewer facets (a gauntlet). */
    fist(s, hand, lowK) {
      const M = this.M, S = s > 0 ? 'L' : 'R', k = this.k * 1.1 * lerp(1, this.bulk, 0.5);
      this.armFrame(s);
      this.bone('hand' + S); M.color = hand;
      const wy = -this.upL - this.foL;
      M.sphere([0, wy - 0.045 * k, 0.01 * k], [0.04 * k, 0.052 * k, 0.046 * k], lowK ? 6 : 7, lowK ? 3 : 4);
      M.sphere([-s * 0.024 * k, wy - 0.03 * k, 0.034 * k], [0.016 * k, 0.024 * k, 0.016 * k], lowK ? 4 : 5, lowK ? 2 : 3);
      M.pop();
    }
    /** Ring on the forearm (bracer / cuff), from..to along it (0 elbow … 1 wrist). */
    cuff(s, c, from, to, kk) {
      const M = this.M, S = s > 0 ? 'L' : 'R';
      this.armFrame(s); this.bone('foreArm' + S); M.color = c;
      const y0 = -this.upL - this.foL * from, y1 = -this.upL - this.foL * to;
      M.segment([0, y0, 0], [0, y1, 0], lerp(this.foreR, this.foreR * 0.82, from) * kk, lerp(this.foreR, this.foreR * 0.82, to) * kk, 7);
      M.pop();
    }
    leg(s, thigh, shin, boot, bootTop = 0.55, sole, o) {
      o = o || {};
      const M = this.M, S = s > 0 ? 'L' : 'R', J = this.J, k = this.k;
      const hip = [J['thigh' + S][0], this.hipY + 0.03 * k, J['thigh' + S][2]], knee = J['shin' + S], ankle = J['foot' + S];
      this.bone('thigh' + S); M.color = thigh;
      M.segment(hip, knee, this.thighR, this.thighR * 0.76, o.thighSides || 8);
      this.bone('shin' + S); M.color = thigh;
      if (o.knee !== false) M.sphere(V.add(knee, [0, 0, 0.004]), this.thighR * 0.78, 6, 3);
      M.color = shin;
      M.segment(knee, V.add(ankle, [0, 0.02 * k, 0]), this.shinR, this.shinR * 0.78, 7);
      if (bootTop > 0) {
        const top = V.lerp(ankle, knee, bootTop);
        M.color = boot;
        M.segment(top, V.add(ankle, [0, -0.005, 0]), this.shinR * 1.18, this.shinR * 1.02, 7);
        this.bootTop = top;
      }
      this.bone('foot' + S);
      this.shoe(ankle, this.footLen, this.footW, boot, sole || P.shade(boot, 0.6));
    }
    /** Boot/shoe (UnitMeshKit.Shoe): ankle at `ankle`, sole on y = 0, toe to +Z. */
    shoe(ankle, len, width, color, sole) {
      const M = this.M, keep = M.color;
      const zb = ankle[2] - len * 0.28, zm = ankle[2] + len * 0.38, zt = ankle[2] + len * 0.72;
      const h = Math.max(ankle[1] + width * 0.25, len * 0.36), hm = len * 0.26, ht = len * 0.2;
      const w = width * 0.5, wt = width * 0.46, x = ankle[0];
      M.color = color;
      const b0 = [x - w, 0.012, zb], b1 = [x + w, 0.012, zb], b2 = [x + wt, 0.012, zm], b3 = [x - wt, 0.012, zm];
      const t0 = [x - w * 0.92, h, zb + 0.01], t1 = [x + w * 0.92, h, zb + 0.01], t2 = [x + wt, hm, zm], t3 = [x - wt, hm, zm];
      M.quad(t0, t1, t2, t3, [0, 1, 0.5]);
      M.quad(b0, b1, t1, t0, [0, 0, -1]);
      M.quad(b1, b2, t2, t1, [1, 0, 0]);
      M.quad(b3, b0, t0, t3, [-1, 0, 0]);
      M.sphere([x, ht * 0.62, (zm + zt) / 2], [wt * 1.02, ht * 0.75, (zt - zm) * 0.62 + 0.01], 6, 2, true);
      M.box([x, hm * 0.45, zm - 0.004], [wt * 2, hm * 0.9, 0.012]);
      M.color = sole;
      M.box([x, 0.012, (zb + zt) / 2 + 0.005], [width * 1.04, 0.024, zt - zb + 0.04]);
      M.color = keep;
    }

    // ------------------------------------------------ head & face
    get headCenter() { return this.headC; }
    /** Head units → metres: [x Rx, y Ry, z Rzu] (positions and radii of things fitted to the head: hair, caps, buns). */
    hv(x, y, z) { return [x * this.Rx, y * this.Ry, z * this.Rzu]; }
    /** A point at head units (x, y, z) from the head centre. */
    headAt(x, y, z) { return V.add(this.headC, this.hv(x, y, z)); }
    /** cheekShade (< 1) darkens the face below the brow line a little: the brow plane catches more light than the cheeks. */
    headShape(cheekShade = 1) {
      const M = this.M, G = this.headG, n = G.ys.length, S = HEAD_SIDES;
      const P0 = G.ys.map((y, i) => G.rad[i].map((r, j) => { const t = j * 2 * Math.PI / S; return [Math.cos(t) * r, y, Math.sin(t) * r]; }));
      // smooth vertex normals from the grid's faces (the grid wraps round in j)
      const N0 = P0.map(row => row.map(() => [0, 0, 0]));
      const tris = [];
      for (let i = 0; i < n - 1; i++) for (let j = 0; j < S; j++) {
        const j1 = (j + 1) % S, A = [i, j], B = [i + 1, j], Cc = [i + 1, j1], Dd = [i, j1];
        if (G.rad[i + 1][0] > 1e-5) tris.push([A, B, Cc]);
        if (G.rad[i][0] > 1e-5) tris.push([A, Cc, Dd]);
      }
      for (const t of tris) {
        const [a, b, c] = t.map(([i, j]) => P0[i][j]), fn = V.cross(V.sub(b, a), V.sub(c, a));
        for (const [i, j] of t) { const s = N0[i][j]; s[0] += fn[0]; s[1] += fn[1]; s[2] += fn[2]; }
      }
      // the poles: one shared normal straight down / up
      for (let j = 0; j < S; j++) { N0[0][j] = [0, -1, 0]; N0[n - 1][j] = [0, 1, 0]; }
      const v0 = M.vertexCount, keep = M.color, idx = [];
      M.push().translate(this.headC).scale(this.Rx, this.Ry, this.Rz).rotate(HEAD_TILT, 0, 0);
      for (let i = 0; i < n; i++) {
        const c = P.shade(keep, lerp(cheekShade, 1, sstep((G.ys[i] + 0.05) / 0.3)));
        idx.push(P0[i].map((p, j) => M.addVertex(p, V.norm(N0[i][j]), c)));
      }
      M.pop();
      // outward triangles: counter-clockwise seen from outside (the grid runs round +Y anticlockwise from above? check by
      // the normal: flip the winding where the face normal points inwards)
      for (const t of tris) {
        const [a, b, c] = t.map(([i, j]) => P0[i][j]), fn = V.cross(V.sub(b, a), V.sub(c, a));
        const m = V.mul(V.add(V.add(a, b), c), 1 / 3);
        const ids = t.map(([i, j]) => idx[i][j]);
        if (V.dot(fn, m) >= 0) M.addTri(ids[0], ids[1], ids[2]); else M.addTri(ids[0], ids[2], ids[1]);
      }
      this.headV = [v0, M.vertexCount];
    }
    /** Radius of the unit head grid (after the tilt is undone) at height y and angle θf (rad, 0 = front, + towards +X):
     * the faceted surface (chords between the columns), interpolated between rings. */
    headRadius(y, tf) {
      const G = this.headG, ys = G.ys, S = HEAD_SIDES, n = ys.length;
      if (y <= ys[0] || y >= ys[n - 1]) return 0;
      let i = 0; while (i < n - 2 && y > ys[i + 1]) i++;
      const u = (y - ys[i]) / (ys[i + 1] - ys[i]);
      // column angle θf_j = 90° − j·360/S → j = (90° − θf) / step
      const step = 2 * Math.PI / S, f = ((Math.PI / 2 - tf) / step % S + S) % S, j0 = Math.floor(f) % S, j1 = (j0 + 1) % S;
      const chord = (r0, r1) => {
        const a0 = Math.PI / 2 - j0 * step, a1 = Math.PI / 2 - (j0 + 1) * step;
        const p0 = [r0 * Math.sin(a0), r0 * Math.cos(a0)], p1 = [r1 * Math.sin(a1), r1 * Math.cos(a1)], ud = [Math.sin(tf), Math.cos(tf)];
        const e = [p1[0] - p0[0], p1[1] - p0[1]], den = ud[0] * e[1] - ud[1] * e[0];
        return Math.abs(den) < 1e-9 ? r0 : (p0[0] * e[1] - p0[1] * e[0]) / den;
      };
      return lerp(chord(G.rad[i][j0], G.rad[i][j1]), chord(G.rad[i + 1][j0], G.rad[i + 1][j1]), u);
    }
    insideHead(p) {
      const c = this.headC;
      const d = [(p[0] - c[0]) / this.Rx, (p[1] - c[1]) / this.Ry, (p[2] - c[2]) / this.Rz];
      const a = -HEAD_TILT * D2R, ca = Math.cos(a), sa = Math.sin(a);
      const y = d[1] * ca - d[2] * sa, z = d[1] * sa + d[2] * ca, rho = Math.hypot(d[0], z);
      return rho <= this.headRadius(y, Math.atan2(d[0], z));
    }
    /** The point on the front of the face at x half-widths (Rx) and y half-heights (Ry, from the face's reference line
     * headCY), lifted `lift` feature units (R) off the surface. */
    facePoint(x, y, lift = 0) {
      const px = x * this.Rx, py = this.headCY + y * this.Ry;
      let lo = this.headC[2], hi = lo + 1.5 * this.Rz;
      if (!this.insideHead([px, py, lo])) return [px, py, lo + lift * this.R];
      for (let i = 0; i < 22; i++) { const mid = (lo + hi) / 2; if (this.insideHead([px, py, mid])) lo = mid; else hi = mid; }
      return [px, py, lo + lift * this.R];
    }
    /** Ears: between the brow and the nose base (≈ 6 cm tall on an adult), on the side of the head just behind its middle. */
    ears(c) {
      const M = this.M, R = this.R;
      this.bone('head'); M.color = c;
      for (const s of [1, -1]) {
        M.push().translate(this.headAt(s * 0.98, -0.2, -0.1)).rotate(-8, s * -18, 0);
        M.sphere([0, 0, 0], [0.2 * R, 0.36 * R, 0.24 * R], 6, 3, true);
        M.pop();
      }
    }
    /**
     * Hair cap over the cranium (BipedKit.HairCap): hairline φ at the forehead / over the ears / at the nape. lineFn(θ)
     * overrides the hairline; topFn(θ) starts the shell lower (just under a headwrap, so its rows stay short); np rows.
     * Flat rows are chords: rows spanning more than ~30° of φ sink inside the head between their edges.
     */
    hairCap(c, front = 58, side = 100, back = 122, puff = 1.08, lineFn = null, topFn = null, np = 2) {
      const M = this.M;
      this.bone('head'); M.color = c;
      const C0 = this.headAt(0, 0.02, -0.01), RR = this.hv(puff, puff, puff * 0.98);
      const line = lineFn || (th => { const a = Math.abs(th); if (a < 55) return lerp(front, front + 10, a / 55); if (a < 110) return lerp(front + 10, side, (a - 55) / 55); return lerp(side, back, (a - 110) / 70); });
      M.shell(C0, RR, -180, 180, 12, topFn || 0, line, np);
      this.hairC = C0; this.hairR = RR; this.hairLine = line;
    }
    /** A point on the hair ellipsoid (θ from +Z towards +X, φ from the top, degrees). */
    hairPoint(th, ph, k = 1) {
      const c = this.hairC, r = this.hairR, t = th * D2R, p = ph * D2R, s = Math.sin(p);
      return [c[0] + r[0] * k * s * Math.sin(t), c[1] + r[1] * k * Math.cos(p), c[2] + r[2] * k * s * Math.cos(t)];
    }
    /**
     * Eyes: style 'round' | 'almond' | 'narrow' (phoenix eyes: narrow, outer corners up). A dark eye shape, the iris low in
     * it and a bright catch-light — bolder than life (an adult eye opening is ~1 cm), so the face reads at the game camera.
     * y in Ry from headCY, spread in Rx, sizes in R.
     */
    eyes(iris, brow, style = 'round', browTilt = 0, y = -0.03, spread = 0.4, size = 1, eyeTilt = 0, browK = 1) {
      const M = this.M, R = this.R, keepJ = M.jitter, keepE = M.emission;
      this.bone('head'); M.jitter = 0;
      const h = style === 'narrow' ? 0.12 : style === 'almond' ? 0.145 : 0.2, wd = style === 'narrow' ? 0.17 : style === 'almond' ? 0.165 : 0.13;
      for (const s of [1, -1]) {
        const p = this.facePoint(s * spread, y, 0);
        M.push().translate(V.add(p, [0, 0, -0.036 * R])).rotate(0, s * EYE_TURN, s * eyeTilt);
        // BipedKit.Eyes: a dark disc, the iris low in it, a bright catch-light high on the outer side. Thin ink on the disc
        // and none round the catch-light: on a real-size face a full ink ring per part turns the eyes into a dark mask.
        M.ink = 0.4;
        M.color = P.shade(iris, 0.42);
        M.sphere([0, 0, 0], [wd * R * size, h * R * size, 0.065 * R], 7, 3);
        M.color = iris;
        M.sphere([0, -h * 0.35 * R * size, 0.012 * R], [wd * 0.75 * R * size, h * 0.45 * R * size, 0.06 * R], 6, 2);
        M.emission = 0.55; M.color = [1, 1, 0.97]; M.ink = 0;
        M.sphere([s * 0.035 * R * size, h * 0.38 * R * size, 0.05 * R], 0.045 * R * size * (style === 'narrow' ? 0.85 : 1), 4, 2);
        M.emission = keepE; M.ink = 1;
        M.pop();
        if (brow) {
          const dy = (style === 'narrow' ? 0.24 : style === 'almond' ? 0.27 : 0.3) * Math.max(1, size * 0.9) * R / this.Ry;
          const bp = this.facePoint(s * spread * 1.04, y + dy, 0.01);
          M.color = brow; M.ink = 0.7;
          M.push().translate(bp).rotate(0, s * EYE_TURN, s * browTilt);
          M.box([0, 0, 0], [0.36 * R * browK, 0.085 * R * browK, 0.07 * R]);
          // a swept tail at the outer end (卧蚕眉 / bold brows)
          if (browK > 1.2) M.box([s * 0.22 * R * browK, 0.03 * R * browK, -0.012 * R], [0.13 * R * browK, 0.055 * R * browK, 0.055 * R]);
          M.pop(); M.ink = 1;
        }
      }
      M.jitter = keepJ;
    }
    /**
     * Nose: a wedge from the bridge between the eyes down to the tip (two lit / shaded side planes and an ink outline, so it
     * reads in 3/4 view), nostril wings either side and a small round tip. y = the tip line (Ry from headCY), proj = how
     * far the tip stands off the face (R), w = the half-width of the wings (Rx).
     */
    nose(c, size = 1, y = -0.42, proj = 0.32, w = 0.2) {
      const M = this.M, R = this.R;
      this.bone('head'); M.color = c; M.ink = 0.6;
      const root = this.facePoint(0, y + 0.42, 0.03);
      const tip = this.facePoint(0, y, proj * size), base = this.facePoint(0, y - 0.1, 0.12 * size);
      const wing = s => this.facePoint(s * w * size, y - 0.05, 0.05 * size);
      const wl = wing(1), wr = wing(-1);
      // the wedge: two side planes (root → wing → tip), the underside (wing → base → tip)
      const out = (a, b, cc, hint) => { const n = V.cross(V.sub(b, a), V.sub(cc, a)); return V.dot(n, hint) >= 0 ? n : V.neg(n); };
      M.triangleFacing(root, wl, tip, out(root, wl, tip, [1, 0.2, 1]));
      M.triangleFacing(root, tip, wr, out(root, tip, wr, [-1, 0.2, 1]));
      M.triangleFacing(wl, base, tip, out(wl, base, tip, [0.4, -1, 0.6]));
      M.triangleFacing(tip, base, wr, out(tip, base, wr, [-0.4, -1, 0.6]));
      M.sphere(V.add(tip, [0, -0.025 * R, -0.035 * R]), [0.1 * R * size, 0.085 * R * size, 0.08 * R * size], 5, 2);
      M.ink = 1;
    }
    smile(c, y = -0.68, width = 0.3) {
      const M = this.M, R = this.R; this.bone('head'); M.color = c; M.ink = 0.3;
      const Pt = t => this.facePoint(t * width * 0.5, y + 0.06 * t * t, 0.015);
      M.segment(Pt(-1), Pt(-0.35), 0.024 * R, 0.028 * R, 4);
      M.segment(Pt(-0.35), Pt(0.35), 0.028 * R, 0.028 * R, 4);
      M.segment(Pt(0.35), Pt(1), 0.028 * R, 0.024 * R, 4);
      M.ink = 1;
    }
    /** Hair cap / headwrap shell over the cranium; hairline φ (deg from the top) at front / side / back. */
    cap(c, front = 58, side = 100, back = 122, puff = 1.08) {
      const M = this.M;
      this.bone('head'); M.color = c;
      this.capC = this.headAt(0, 0.02, -0.01);
      this.capR = this.hv(puff, puff, puff * 0.98);
      const line = th => { const a = Math.abs(th); if (a < 55) return lerp(front, front + 10, a / 55); if (a < 110) return lerp(front + 10, side, (a - 55) / 55); return lerp(side, back, (a - 110) / 70); };
      this.capLine = line;
      M.shell(this.capC, this.capR, -180, 180, 12, 0, line, 2);
    }
    /** A point on the cap ellipsoid (θ from +Z towards +X, φ from the top, degrees), k scales the radii. */
    capPoint(th, ph, k = 1) {
      const c = this.capC, r = this.capR, t = th * D2R, p = ph * D2R, s = Math.sin(p);
      return [c[0] + r[0] * k * s * Math.sin(t), c[1] + r[1] * k * Math.cos(p), c[2] + r[2] * k * s * Math.cos(t)];
    }

    // ------------------------------------------------ garments
    /**
     * Skirt panel (θ0..θ1 deg, around the hips) on a bone; prof [[r, y], …] top → bottom; with lining and hem band.
     * On a thigh bone the panel is hinged at the hip: vertices above the hip joint stay on the pelvis (rigid skinning per
     * vertex), so a side panel follows the leg below the hip instead of swinging out from the belt.
     */
    skirtPanel(bone, th0, th1, nt, prof, outer, lining, hem, hemH = 0.05, depthK = 0.86) {
      const M = this.M, start = M.vertexCount;
      this._skirtPanel(bone, th0, th1, nt, prof, outer, lining, hem, hemH, depthK);
      this.hinge(start, bone);
    }
    /** Vertices since `start` on a thigh bone: above the hip joint (+ dy) → pelvis. */
    hinge(start, bone, dy = 0.0) {
      if (!/^thigh/.test(bone)) return;
      const yH = this.J[bone][1] + dy * this.k, bi = this.BI[bone], bp = this.BI.pelvis;
      this.M.boneBy(start, p => (p[1] > yH ? bp : bi));
    }
    _skirtPanel(bone, th0, th1, nt, prof, outer, lining, hem, hemH = 0.05, depthK = 0.86) {
      const M = this.M;
      this.bone(bone);
      M.panel([0, 0, 0], th0, th1, nt, prof, depthK, outer, lining, 0.01);
      if (hem) {
        const last = prof[prof.length - 1], prev = prof[prof.length - 2];
        const t = hemH / Math.max(0.01, prev[1] - last[1]);
        const top = [lerp(last[0], prev[0], t) + 0.006, last[1] + hemH], bot = [last[0] + 0.006, last[1] - 0.002];
        M.panel([0, 0, 0], th0, th1, nt, [top, bot], depthK, hem, null);
      }
    }
  }
  Pilot.Biped = Biped;

  /** Rest (bind-pose) matrix of a figure socket: hand joint × socket local (bind rotations are identity). */
  function socketRest(fig, name) {
    const sock = fig.sockets[name], bone = sock.parent;
    const J = fig.pr.J[bone.name];
    return new THREE.Matrix4().makeTranslation(J[0], J[1], J[2]).multiply(new THREE.Matrix4().compose(sock.position, sock.quaternion, new THREE.Vector3(1, 1, 1)));
  }

  /** Hide figure.js's own meshes (an invisible material keeps the bones and sockets — children of the mesh — updating). */
  const INVISIBLE = new THREE.MeshBasicMaterial({ visible: false });
  function hideFigure(fig) {
    for (const m of fig.meshes || [fig.mesh]) {
      m.material = INVISIBLE;
      m.castShadow = false;
    }
  }

  // ====================================================== palettes ==
  const C = {
    // 关羽: deep jujube-red face, near-black hair and beard, jade-moss robe (Lanternvale's soft-saturated greens), warm gold
    gySkin: H('#B9483A'), gySkinDark: H('#9A3A30'), gyIris: H('#2E2224'), gyBrow: H('#241C1E'), gyHair: H('#2A2422'),
    beard: H('#2B2324'), beardTop: H('#45393A'),
    robe: H('#3F7F4A'), robeDark: H('#33683D'), robeTrim: H('#E8D8B0'), robeLining: H('#2F5E3A'),
    wrap: H('#3F7F4A'), wrapBand: H('#2F5E3A'), jade: H('#8FD4A8'),
    gold: H('#D9A441'), goldDark: H('#9A6A2A'), goldLight: H('#EDC46A'), gauntlet: H('#4A3426'),
    leather: H('#6E3B2C'), sash: H('#C24E34'), trousers: H('#4F5E55'), boots: H('#45302A'),
    shaft: H('#2E5E44'), blade: H('#E4EAEE'), bladeBack: H('#A9B9C6'), tassel: H('#D2402E'), dragon: H('#56A878'),
    // soldier
    soSkin: H('#EDBF96'), soHair: H('#2A2024'), soHairCap: H('#262022'), soKnot: H('#3A3032'), soWrap: H('#C2402E'), iron: H('#68707E'), ironDark: H('#4E5562'), tunicRed: H('#C2503C'),
    lamA: H('#5C6470'), lamB: H('#6E7784'), ironSeam: H('#3A3F4A'),
    cream: H('#EADFC4'), wrapBrown: H('#8C6A4C'), wood: H('#8C6A48'), steel: H('#C9D2DA'),
    // horse: chestnut coat, darker points, near-black-brown mane, cream socks / blaze / muzzle
    coat: H('#A85A36'), coatPoints: H('#6B3A26'), mane: H('#4A2A1E'), mane2: H('#5E3826'), hoof: H('#3A2A22'), marking: H('#EFE2C6'),
    muzzle: H('#C9A88C'), saddle: H('#5A3426'), barding: H('#2F7D4A'), bardingTrim: H('#EBDDB0'),
  };
  Pilot.colors = C;

  // ================================================== 关羽 (Guan Yu) ==
  function buildGuanYu(fig, o) {
    o = o || {};
    // heroic 1.95 m: 7.8 heads (crown → chin 0.25 m), a broad 0.16 m head with a square jaw
    const kit = new Biped(fig, { seed: 23, bulk: 1.12, headsTall: 7.8, headW: 0.16, headD: 0.198, chin: 1.4, face: { jaw: 0.16, chin: 0.07, cheek: 0.06, brow: 0.06 } });
    const M = kit.M, J = kit.J, k = kit.k, R = kit.R;
    const robe = o.robe || C.robe;
    // ---- legs and boots (dark green-grey trousers, dark boots with gold bands)
    for (const s of [1, -1]) {
      kit.leg(s, C.trousers, C.trousers, C.boots, 0.62, null, { knee: false, thighSides: 6 });
      const S = s > 0 ? 'L' : 'R';
      kit.bone('shin' + S); M.color = C.gold;
      M.band(kit.bootTop, kit.shinR * 1.26, kit.shinR * 1.24, -0.03 * k, 0.005 * k, 7);
    }
    kit.mark('legs');
    // ---- torso: gold lamellar armour (banded) — the robe is worn open over it
    const bands = [C.goldDark, C.gold, C.gold, C.goldLight, C.gold, H('#7A5A30')];
    kit.torso(C.gold, C.goldDark, bands);
    kit.neck(P.shade(C.gySkinDark, 0.78));
    // lamellar bands on the belly armour
    kit.bone('spine'); M.color = C.goldDark;
    M.band([0, 0, 0], kit.waistR * 1.06, kit.waistR * 1.06, kit.spineY - 0.035 * k, kit.spineY - 0.02 * k, 8, kit.depthK);
    // mirror plates (护心镜) either side of the beard: gold discs with a dark boss
    kit.bone('chest');
    for (const s of [1, -1]) {
      M.color = C.goldLight;
      M.push().translate(s * 0.098 * k, kit.chestY + 0.035 * k, kit.chestR * kit.depthK * 0.96 + 0.004).rotate(90, s * 22, 0);
      M.cylinder([0, -0.012, 0], 0.058 * k, 0.053 * k, 0.022, 8);
      M.color = C.goldDark;
      M.cylinder([0, 0.01, 0], 0.024 * k, 0.018 * k, 0.012, 6);
      M.pop();
    }
    kit.mark('torso');
    // ---- the open green robe over the armour (back and sides), cream-edged front opening
    const rp = (r, y) => [r, y];
    kit.bone('chest');
    // lined (dark green), so the inside of the far shoulder never shows as a hole through the front opening
    M.panel([0, 0, 0], 36, 324, 7, [rp(0.096 * k, kit.neckY + 0.045 * k), rp(kit.shoulderR * 0.9, kit.shoulderY + 0.03 * k), rp(kit.shoulderR * 1.08, kit.shoulderY - 0.012 * k), rp(kit.chestR * 1.1, kit.chestY), rp(kit.waistR * 1.12, kit.spineY)], kit.depthK * 1.02, robe, C.robeLining, 0.012);
    kit.bone('spine');
    M.panel([0, 0, 0], 36, 324, 7, [rp(kit.waistR * 1.12, kit.spineY + 0.002), rp(kit.waistR * 1.14, kit.pelvisY + 0.03 * k)], kit.depthK * 1.02, robe, C.robeLining, 0.012);
    // trims along the opening (collar lapels): strips lying on the robe edge
    const edge = (th, r, y) => { const t = th * D2R; return [r * Math.sin(t), y, r * Math.cos(t) * kit.depthK * 1.02]; };
    M.color = C.robeTrim;
    for (const s of [1, -1]) {
      kit.bone('chest');
      const a = edge(s * 38, 0.1 * k, kit.neckY + 0.045 * k), a2 = edge(s * 38, kit.shoulderR * 0.93, kit.shoulderY + 0.028 * k), b = edge(s * 38, kit.shoulderR * 1.1, kit.shoulderY - 0.02 * k), c = edge(s * 38, kit.chestR * 1.12, kit.chestY), d = edge(s * 38, kit.waistR * 1.14, kit.spineY);
      const out = [s * 0.6, 0.2, 1];
      M.strip(a, a2, 0.034 * k, 0.038 * k, out, 0.016);
      M.strip(a2, b, 0.038 * k, 0.04 * k, out, 0.016);
      M.strip(b, c, 0.04 * k, 0.04 * k, out, 0.016);
      M.strip(c, d, 0.04 * k, 0.036 * k, out, 0.016);
      kit.bone('spine');
      M.strip(d, edge(s * 38, kit.waistR * 1.16, kit.pelvisY + 0.03 * k), 0.036 * k, 0.036 * k, out, 0.016);
    }
    // standing collar round the back of the neck (rising out of the robe over the trapezius)
    kit.bone('chest'); M.color = C.robeTrim;
    M.push().translate(0, kit.neckY + 0.06 * k, -0.012).rotate(14, 0, 0).scale(1, 1, 0.9).translate(0, -kit.neckY - 0.06 * k, 0);
    M.lathe([[0.102 * k, kit.neckY + 0.03 * k], [0.086 * k, kit.neckY + 0.1 * k]], 8, false, false, false, null, 22.5);
    M.pop();
    kit.mark('robe');
    // ---- belt (leather, gold plaques and a beast-face buckle) and the vermilion sash
    kit.bone('pelvis');
    M.color = C.leather;
    M.band([0, 0, 0], kit.waistR * 1.2, kit.waistR * 1.2, kit.pelvisY - 0.005 * k, kit.pelvisY + 0.06 * k, 10, kit.depthK * 1.06);
    const bz = kit.waistR * 1.2 * kit.depthK * 1.06;
    M.color = C.goldLight;
    M.box([0, kit.pelvisY + 0.028 * k, bz + 0.012], [0.1 * k, 0.085 * k, 0.03]);
    M.color = C.goldDark;
    M.box([0, kit.pelvisY + 0.03 * k, bz + 0.03], [0.05 * k, 0.03 * k, 0.012]);
    M.color = C.gold;
    for (const s of [1, -1]) M.box([s * kit.waistR * 0.95, kit.pelvisY + 0.028 * k, bz * 0.72], [0.05 * k, 0.05 * k, 0.04]);
    M.color = C.sash;
    M.band([0, 0, 0], kit.waistR * 1.24, kit.waistR * 1.22, kit.pelvisY - 0.04 * k, kit.pelvisY - 0.004 * k, 10, kit.depthK * 1.06);
    // sash knot on the left front and two tails
    const knot = [0.07 * k, kit.pelvisY - 0.03 * k, bz + 0.02];
    M.sphere(knot, [0.045 * k, 0.035 * k, 0.03 * k], 5, 2, false);
    kit.bone('skirtF'); M.color = C.sash;
    M.curvedStrip(V.add(knot, [-0.01, -0.02, 0.005]), V.add(knot, [0.0, -0.18 * k, 0.04]), V.add(knot, [0.035 * k, -0.4 * k, 0.03]), 0.06 * k, 0.05 * k, [0, 0, 1], 0.012, 2);
    M.curvedStrip(V.add(knot, [0.015, -0.02, 0.0]), V.add(knot, [0.06 * k, -0.15 * k, 0.03]), V.add(knot, [0.09 * k, -0.33 * k, 0.015]), 0.05 * k, 0.04 * k, [0.3, 0, 1], 0.012, 2);
    kit.mark('belt');
    // ---- robe skirt: front / back flaps and side panels (split robe), dark green lining and cream hem; gold tassets over
    // the sides. The side panels and tassets hinge at the hip (top row on the pelvis, the rest on the thigh).
    const top = kit.pelvisY + 0.01 * k, hemY = 0.42 * k;
    const sk = f => [[kit.hipR * 1.14, top], [kit.hipR * 1.32, kit.hipY - 0.2 * k], [kit.hipR * 1.5 * f, hemY]];
    kit.skirtPanel('skirtF', -30, 30, 3, sk(1), robe, C.robeLining, C.robeTrim, 0.06 * k);
    kit.skirtPanel('skirtB', 148, 212, 3, sk(1.04), robe, C.robeLining, C.robeTrim, 0.06 * k);
    kit.skirtPanel('thighL', 32, 146, 4, sk(1.02), robe, C.robeLining, C.robeTrim, 0.06 * k);
    kit.skirtPanel('thighR', -146, -32, 4, sk(1.02), robe, C.robeLining, C.robeTrim, 0.06 * k);
    for (const s of [1, -1]) {
      const bn = s > 0 ? 'thighL' : 'thighR';
      const rows = [[kit.pelvisY - 0.0 * k, kit.hipY - 0.06 * k, C.gold], [kit.hipY - 0.06 * k, kit.hipY - 0.17 * k, C.goldDark], [kit.hipY - 0.17 * k, kit.hipY - 0.29 * k, C.gold]];
      rows.forEach(([y0, y1, col], i) => {
        const r0 = kit.hipR * (1.27 + i * 0.05), r1 = kit.hipR * (1.34 + i * 0.05);
        kit.skirtPanel(bn, s > 0 ? 58 : -122, s > 0 ? 122 : -58, 3, [[r0, y0 + 0.01], [r1, y1]], col, C.goldDark, null, 0, 0.9);
      });
    }
    kit.mark('skirt');
    // ---- arms: wide green robe sleeves to the elbow (cream cuff), gold bracers ending in a dark-gold ring at the wrist,
    // dark leather gauntlets (red fists read as boxing gloves next to the red face)
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R';
      kit.arm(s, robe, C.robeTrim, C.gauntlet, { noShoulder: true, noUpper: true, noElbow: true, foreSides: 6, fistK: 0.62 });
      kit.armFrame(s); kit.bone('upperArm' + S);
      M.color = robe;
      M.lathe([[kit.armR * 1.95, -kit.upL * 0.98], [kit.armR * 1.55, -kit.upL * 0.45], [kit.armR * 1.3, 0.02 * k]], 7, false, false, false);
      M.color = C.robeLining;
      M.lathe([[kit.armR * 1.92, -kit.upL * 0.98], [kit.armR * 1.6, -kit.upL * 0.9]], 7, false, false, false);
      M.color = C.robeTrim;
      M.band([0, 0, 0], kit.armR * 1.98, kit.armR * 2.02, -kit.upL * 1.02, -kit.upL * 0.94, 7);
      M.pop();
      kit.cuff(s, C.gold, 0.12, 0.86, 1.3);
      kit.cuff(s, C.goldDark, 0.86, 0.99, 1.4);
    }
    kit.mark('arms');
    // ---- shoulders: three overlapping curved gold plates per side (FFX-style lames, BipedKit.BigPauldron): each a 120°
    // shell over the outer shoulder, stepping out and down and fanning a little further outwards, with a dark under-edge
    // band; the underside shows only on the lowest lame. On the shoulder helper bones (follow the upper arm 85 %).
    // Rounded domes (the top lame ten-sided in two rows from the crown, the lower ones eight-sided) with the dark band as
    // the lowest row of each lame — no flat six-sided caps (they read as golden hex nuts).
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R', sh = J['upperArm' + S];
      kit.bone('sh' + S);
      const r = kit.armR * 1.58, th0 = -180, th1 = 180;
      M.push().translate(V.add(sh, [s * 0.03 * k, 0.03 * k, -0.004]));
      for (let i = 2; i >= 0; i--) {
        const kk = 1 - i * 0.08, n = i === 0 ? 10 : 8;
        M.push().translate(s * i * 0.015 * k, -i * 0.05 * k, 0).rotate(0, 0, -s * (20 + i * 10));
        const rad = [r * kk, r * 0.82 * kk, r * 0.96 * kk];
        M.color = i === 1 ? P.shade(C.gold, 0.93) : i === 0 ? C.goldLight : C.gold;
        if (i === 0) M.shell([0, 0, 0], rad, th0, th1, n, 0, 68, 2); else M.shell([0, 0, 0], rad, th0, th1, n, 36, 68, 1);
        M.color = C.goldDark;
        M.shell([0, 0, 0], rad, th0, th1, n, 68, 84, 1);
        if (i === 2) M.shell([0, 0, 0], V.mul(rad, 0.95), th0, th1, n, 40, 84, 1, true);
        M.pop();
      }
      M.pop();
    }
    kit.mark('pauldrons');
    // ---- head: deep jujube-red face (the cheek planes a shade darker under the brows), big phoenix eyes (丹凤眼) under bold
    // swept brows (卧蚕眉), button nose, small round ears
    kit.bone('head'); M.color = C.gySkin;
    kit.headShape(0.9);
    kit.ears(C.gySkin);
    kit.eyes(C.gyIris, C.gyBrow, 'narrow', 18, -0.03, 0.42, 1.25, 10, 1.45);
    kit.nose(C.gySkinDark, 1.15, -0.42, 0.34, 0.22);
    kit.mark('face');
    // ---- hair: near-black hair under the headwrap, showing at the temples, over the ears and at the nape; flat sideburns
    // the hairline drops right behind the ears, so the whole back of the head down to the nape is hair (no red skin)
    // (three short rows from just under the wrap: long flat rows sank inside the skull and the back showed red)
    const wrapLine = th => { const a = Math.abs(th); if (a < 55) return lerp(48, 56, a / 55); if (a < 110) return lerp(56, 84, (a - 55) / 55); return lerp(84, 106, (a - 110) / 70); };
    kit.hairCap(C.gyHair, 56, 104, 168, 1.08, th => { const a = Math.abs(th); if (a < 55) return lerp(56, 62, a / 55); if (a < 100) return lerp(62, 104, (a - 55) / 45); if (a < 128) return lerp(104, 166, sstep((a - 100) / 28)); return 168; }, th => wrapLine(th) - 12, 3);
    M.color = C.gyHair;
    for (const s of [1, -1]) M.strip(kit.facePoint(s * 0.9, 0.34, -0.045), kit.facePoint(s * 0.88, -0.24, -0.035), 0.26 * R, 0.08 * R, [s, 0, 0.3], 0.008);
    kit.mark('hair');
    // ---- the beard (美髯): a short chin piece under the jaw line (the cheeks stay clear), then three flowing locks — the
    // centre one to the sternum, two shorter ones splayed ±12° — each a tapered five-sided loft curling forward at the tip,
    // lighter on its upper faces and darker towards the tip; the upper third rides the head, the rest the chest (so it
    // never slides through the chest plate when he turns his head). Moustache drooping either side of the mouth.
    kit.bone('head'); M.color = C.beard;
    M.shell(kit.headAt(0, -0.33, 0.02), kit.hv(1.03, 0.86, 1.0), -66, 66, 8, 112, 160, 2);
    const chestFront = kit.chestR * kit.depthK + 0.045 * k;
    const tipY = J.chest[1] - 0.02;
    const lockOf = (root, tip, w0, seed) => {
      const ctrl = [(root[0] + tip[0]) / 2, lerp(root[1], tip[1], 0.42), Math.max(root[2], tip[2]) + 0.035 * k];
      const bez = t => { const u = 1 - t; return [u * u * root[0] + 2 * u * t * ctrl[0] + t * t * tip[0], u * u * root[1] + 2 * u * t * ctrl[1] + t * t * tip[1], u * u * root[2] + 2 * u * t * ctrl[2] + t * t * tip[2] + 0.035 * k * t * t * t]; };
      const N = 4, rings = [];
      for (let i = 0; i <= N; i++) {
        const t = i / N, p = bez(t), q = bez(Math.min(1, t + 0.02)), pp = bez(Math.max(0, t - 0.02));
        const T = V.norm(V.sub(q, pp)), X = V.norm(V.cross(T, [0, 0, 1])), U = V.cross(X, T);
        const r = w0 * Math.pow(1 - t, 0.75) * (i === 0 ? 1 : 1 + 0.08 * Math.sin(seed + i * 1.7));
        const ring = [];
        for (let j = 0; j < 5; j++) { const a = j * Math.PI * 2 / 5 + Math.PI / 2; ring.push(V.add(p, V.add(V.mul(X, Math.cos(a) * r), V.mul(U, Math.sin(a) * r * 0.6)))); }
        rings.push(ring);
      }
      const start = M.vertexCount;
      for (let i = 0; i < N; i++) {
        const tone = lerp(1, 0.74, i / (N - 1));
        M.loft([rings[i], rings[i + 1]], i === 0, false, n => P.shade(n[1] > 0.3 || n[2] > 0.75 ? C.beardTop : C.beard, tone));
      }
      const split = root[1] - (root[1] - tip[1]) / 3;
      M.boneBy(start, p => (p[1] > split ? kit.BI.head : kit.BI.chest));
    };
    const rootC = kit.headAt(0, -0.95, 0.62), tipC = [0, tipY, chestFront];
    const dir = V.sub(tipC, rootC), len = V.len(dir);
    lockOf(rootC, tipC, 0.4 * R, 1);
    for (const s of [1, -1]) {
      const root = kit.headAt(s * 0.34, -0.87, 0.58);
      const a = s * 12 * D2R, d = V.mul(V.norm(dir), len * 0.7);
      const tip = V.add(root, [d[0] * Math.cos(a) - d[1] * Math.sin(a), d[0] * Math.sin(a) + d[1] * Math.cos(a), d[2]]);
      tip[2] = Math.max(tip[2], chestFront + 0.01);
      lockOf(root, tip, 0.34 * R, 3 + s);
    }
    kit.bone('head'); M.color = C.beardTop;
    for (const s of [1, -1]) M.curve(kit.facePoint(s * 0.05, -0.56, 0.06), kit.facePoint(s * 0.44, -0.62, 0.07), V.add(kit.facePoint(s * 0.5, -0.95, 0.05), [0, -0.03 * k, 0]), 0.09 * R, 0.03 * R, 2, 4);
    kit.mark('beard');
    // ---- green headwrap (巾帻) worn high (the forehead shows), a darker band at its edge, a knot with two ribbons behind,
    // a jade ornament on the brow
    kit.cap(C.wrap, 48, 84, 106, 1.1);
    M.color = C.wrapBand;
    M.shell(kit.capC, V.mul(kit.capR, 1.03), -180, 180, 12, th => kit.capLine(th) - 14, th => kit.capLine(th) + 1, 1);
    // the knot (a soft bun in the wrap) and its two ribbons hanging behind (hair1: sways)
    kit.bone('head'); M.color = C.wrap;
    const bun = kit.capPoint(180, 40, 1.02), bunR = kit.hv(0.46, 0.42, 0.44);
    M.sphere(bun, bunR, 6, 3, false);
    M.color = C.wrapBand;
    M.band(V.add(bun, kit.hv(0, -0.08, 0.04)), bunR[0] * 0.86, bunR[0] * 0.88, 0, 0.12 * kit.Ry, 7, bunR[2] / bunR[0]);
    // two short ribbons: tapered (0.07k → 0.02k), flaring out and twisting a little, so they read as cloth, not poles
    kit.bone('hair1'); M.color = C.wrap;
    const rib0 = M.vertexCount;
    for (const s of [1, -1]) {
      const a = V.add(bun, kit.hv(s * 0.2, -0.24, -0.26)), m = V.add(a, [s * 0.04 * k, -0.06 * k, -0.03 * k]), t = V.add(a, [s * 0.09 * k, -0.12 * k, -0.026 * k]);
      M.strip(a, m, 0.055 * k, 0.036 * k, [s * 0.3, 0, -1], 0.009);
      M.strip(m, t, 0.036 * k, 0.016 * k, [s * 0.95, 0.1, -0.45], 0.009);
    }
    const ribTop = bun[1] - 0.24 * kit.Ry;
    M.windBy(rib0, p => 0.5 * clamp01((ribTop - p[1]) / (0.12 * k)));
    kit.bone('head');
    M.color = C.goldLight;
    const brow = kit.capPoint(0, kit.capLine(0) - 6, 1.06);
    M.sphere(brow, [0.21 * R, 0.17 * R, 0.1 * R], 6, 3, false);
    M.color = C.jade; M.emission = 0.15;
    M.sphere(V.add(brow, [0, 0, 0.06 * R]), [0.13 * R, 0.1 * R, 0.06 * R], 5, 2, false);
    M.emission = 0;
    kit.mark('wrap');
    // ---- 青龙偃月刀, baked onto the right hand (figure.js grip socket: shaft along +Y, edge towards +Z)
    kit.bone('handR');
    M.push().setMatrix(M.matrix.clone().multiply(socketRest(fig, 'gripR')));
    glaive(M, k);
    M.pop();
    kit.mark('glaive');
    return kit;
  }
  /** Green-dragon crescent glaive in the weapon frame (origin = right-hand grip; figure.js guandao proportions). */
  function glaive(M, k) {
    const keepJ = M.jitter;
    M.jitter = 0.04;
    M.color = C.shaft;
    M.segment([0, -0.9, 0], [0, 1.06, 0], 0.022, 0.02, 6);
    M.color = C.gold;
    for (const y of [-0.82, 0.25, 0.95]) M.band([0, 0, 0], 0.027, 0.027, y - 0.025, y + 0.025, 6);
    M.spike([0, -0.9, 0], [0, -1, 0], 0.024, 0.12, 5);
    // red tassel under the dragon
    M.color = C.tassel;
    M.lathe([[0.016, 1.04], [0.042, 0.98], [0.045, 0.91], [0.022, 0.87]].reverse(), 6, false, true, true);
    // dragon head clasping the blade (green-gold)
    M.color = C.dragon;
    M.sphere([0, 1.1, 0.012], [0.038, 0.055, 0.05], 6, 3, false);
    M.segment([0, 1.1, 0.04], [0, 1.13, 0.1], 0.026, 0.012, 5);
    // the crescent blade: a wedge (thick back, sharp edge) lofted through stations [y, edge z, back z]
    const st = [[1.12, 0.07, -0.02], [1.24, 0.14, -0.025], [1.38, 0.19, -0.03], [1.52, 0.2, -0.045], [1.64, 0.16, -0.07], [1.74, 0.09, -0.1], [1.81, -0.04, -0.14]];
    const rings = st.map(([y, ze, zb], i) => {
      const tb = i === st.length - 1 ? 0.002 : 0.011;
      return [[tb, y, zb], [0, y + 0.012, ze], [-tb, y, zb]];
    });
    M.color = C.blade;
    M.loft(rings, true, true);
    // blued back strip and the back hook
    M.color = C.bladeBack;
    for (let i = 0; i < st.length - 2; i += 2) M.beam([0, st[i][0], st[i][2] - 0.004], [0, st[i + 2][0], st[i + 2][2] - 0.004], 0.026, 0.018, [0, 0, -1]);
    M.cone([0, 1.36, -0.03], 0.018, 0.001, 4);
    M.spike([0, 1.34, -0.03], [0, 0.4, -1], 0.016, 0.09, 4);
    M.jitter = keepJ;
  }

  // ================================================== Han spearman ==
  function buildSoldier(fig, o) {
    o = o || {};
    // 1.80 m: 7.5 heads (crown → chin 0.24 m), head 0.155 m wide, 0.19 m deep
    const kit = new Biped(fig, { seed: 31 + (o.seed || 0), bulk: 1.0, headsTall: 7.5, chin: 1.25, face: { jaw: 0.12, chin: 0.06 } });
    const M = kit.M, J = kit.J, k = kit.k, R = kit.R;
    const tunic = o.color ? P.hsv(H(o.color), 0, 0.92, 1.22) : P.hsv(C.robe, 0, 0.9, 1.08);
    // legs: cream trousers with brown wraps, dark shoes
    for (const s of [1, -1]) {
      kit.leg(s, C.cream, C.cream, C.boots, 0.0);
      const S = s > 0 ? 'L' : 'R';
      kit.bone('shin' + S); M.color = C.wrapBrown;
      const kn = J['shin' + S], an = J['foot' + S];
      M.segment(V.lerp(an, kn, 0.72), V.add(an, [0, 0.01, 0]), kit.shinR * 1.18, kit.shinR * 1.06, 7);
      M.color = P.shade(C.wrapBrown, 0.82);
      M.band(V.lerp(an, kn, 0.4), kit.shinR * 1.18, kit.shinR * 1.18, -0.012, 0.012, 7);
    }
    kit.mark('legs');
    // torso: faction tunic under a Han lamellar vest (札甲): six rows of plates in two iron tones, a dark seam between rows,
    // a lacing line in the faction colour down the front and a straight cream-edged hem
    kit.torso(C.ironSeam, tunic, null);
    kit.neck(P.shade(C.soSkin, 0.9), 1.06);
    {
      const u = k, sp = [[kit.waistR, kit.pelvisY - 0.01 * u], [kit.waistR * 1.04, (kit.pelvisY + kit.spineY) / 2 + 0.02 * u], [kit.waistR * 1.02, kit.spineY + 0.06 * u]];
      const ch = [[kit.waistR, kit.spineY], [kit.chestR, kit.chestY], [kit.chestR * 1.02, kit.shoulderY - 0.07 * u], [kit.shoulderR, kit.shoulderY - 0.012 * u]];
      const rIn = (pr, y) => { for (let i = 0; i + 1 < pr.length; i++) if (y <= pr[i + 1][1]) return lerp(pr[i][0], pr[i + 1][0], clamp01((y - pr[i][1]) / (pr[i + 1][1] - pr[i][1]))); return pr[pr.length - 1][0]; };
      const rAt = y => (y < kit.spineY ? rIn(sp, y) : rIn(ch, y));
      // skinned per vertex like the torso under it (the spine section's top, spineY + 0.06, is the split), with a margin,
      // so the dark seam-coloured body never pokes through a plate row when the chest turns against the spine
      const yH = kit.pelvisY + 0.075 * u, yT = kit.shoulderY - 0.03 * u, NROW = 6, seam = 0.006, RK = 1.085, ySplit = kit.spineY + 0.06 * u;
      const v0 = M.vertexCount;
      for (let i = 0; i < NROW; i++) {
        const y0 = lerp(yH, yT, i / NROW) + seam, y1 = lerp(yH, yT, (i + 1) / NROW) - seam;
        M.color = i % 2 ? C.lamA : C.lamB;
        M.band([0, 0, 0], rAt(y0) * RK, rAt(y1) * RK, y0, y1, 8, kit.depthK);
      }
      // hem trim and the front lacing
      M.color = C.cream;
      M.band([0, 0, 0], rAt(yH) * (RK + 0.02), rAt(yH) * (RK + 0.02), yH - 0.026 * u, yH + seam, 8, kit.depthK);
      M.color = P.shade(tunic, 0.9);
      const fz = y => rAt(y) * RK * Math.cos(Math.PI / 8) * kit.depthK + 0.006;
      const lys = [yH - 0.01 * u, ySplit, kit.chestY, yT];
      for (let i = 0; i < lys.length - 1; i++) {
        const w = 0.012 * u;
        M.quad([-w, lys[i], fz(lys[i])], [w, lys[i], fz(lys[i])], [w, lys[i + 1], fz(lys[i + 1])], [-w, lys[i + 1], fz(lys[i + 1])], [0, 0, 1]);
      }
      M.boneBy(v0, p => (p[1] < ySplit - 1e-4 ? kit.BI.spine : kit.BI.chest));
    }
    // tunic collar round the neck base, tipped forward (higher at the nape than at the throat)
    kit.bone('chest'); M.color = tunic;
    M.push().translate(0, kit.neckY + 0.05 * k, -0.012).rotate(14, 0, 0).scale(1, 1, 0.92).translate(0, -kit.neckY - 0.05 * k, 0);
    M.lathe([[0.094 * k, kit.neckY + 0.025 * k], [0.074 * k, kit.neckY + 0.085 * k]], 8, false, false, false, null, 22.5);
    M.pop();
    // belt
    kit.bone('pelvis'); M.color = C.leather;
    M.band([0, 0, 0], kit.waistR * 1.18, kit.waistR * 1.18, kit.pelvisY - 0.005 * k, kit.pelvisY + 0.05 * k, 10, kit.depthK * 1.06);
    M.color = C.gold;
    M.box([0, kit.pelvisY + 0.022 * k, kit.waistR * 1.18 * kit.depthK * 1.06 + 0.01], [0.06 * k, 0.05 * k, 0.025]);
    kit.mark('torso');
    // tunic skirt to the knee (faction colour), 1.2 cm clear of the thighs: the top row on the hips, a row at hip height
    // and the hem on the thigh (the side panels hinge at the hip); front and back flaps on figure.js's skirt bones
    const top = kit.pelvisY + 0.01 * k, hemY = J.shinL[1] + 0.02 * k, gap = 0.012;
    const sk = f => [[kit.hipR * 1.1 + gap, top], [kit.hipR * 1.17 + gap, kit.hipY], [kit.hipR * 1.28 * f + gap, hemY]];
    kit.skirtPanel('skirtF', -30, 30, 2, sk(1), tunic, C.cream, P.shade(tunic, 0.78), 0.04 * k);
    kit.skirtPanel('skirtB', 148, 212, 2, sk(1.02), tunic, C.cream, P.shade(tunic, 0.78), 0.04 * k);
    kit.skirtPanel('thighL', 32, 146, 3, sk(1), tunic, P.shade(tunic, 0.7), P.shade(tunic, 0.78), 0.04 * k);
    kit.skirtPanel('thighR', -146, -32, 3, sk(1), tunic, P.shade(tunic, 0.7), P.shade(tunic, 0.78), 0.04 * k);
    // iron tassets: two closed lames per hip (thin boxes, so no open edges and the ink closes round them), hinged at the hip
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R', start = M.vertexCount;
      kit.bone('thigh' + S);
      for (let i = 0; i < 2; i++) {
        const yTop = kit.pelvisY + 0.01 * k - i * 0.1 * k, hgt = 0.12 * k, flare = 10 + i * 4;
        const r = kit.hipR * (1.3 + i * 0.05) + gap * 2;
        const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, 0, s * flare * D2R, 'YXZ'));
        M.color = i ? C.ironDark : C.iron;
        M.obox([s * r, yTop - hgt / 2, 0.012 * k], [0.014, hgt, 0.17 * k], q);
        M.color = C.gold;
        M.box([s * (r + 0.008), yTop - hgt * 0.75, 0.012 * k], [0.008, 0.016 * k, 0.016 * k], null, 'tfkrl');
      }
      kit.hinge(start, 'thigh' + S);
    }
    kit.mark('skirt');
    // arms: tunic sleeves, brown bracers, skin fists; iron shoulder flaps
    for (const s of [1, -1]) {
      const S = s > 0 ? 'L' : 'R';
      kit.arm(s, tunic, C.soSkin, C.soSkin);
      kit.cuff(s, C.wrapBrown, 0.35, 0.92, 1.22);
      kit.armFrame(s); kit.bone('upperArm' + S); M.color = tunic;
      M.lathe([[kit.armR * 1.35, -kit.upL * 0.95], [kit.armR * 1.2, 0.01]], 7, false, false, false);
      M.color = P.shade(tunic, 0.78);
      M.band([0, 0, 0], kit.armR * 1.36, kit.armR * 1.38, -kit.upL * 1.0, -kit.upL * 0.9, 7);
      M.pop();
      kit.bone('sh' + S);
      for (let i = 0; i < 2; i++) {
        M.color = i ? C.ironDark : C.iron;
        M.push().translate(V.add(J['upperArm' + S], [s * (0.012 + i * 0.024) * k, (0.03 - i * 0.055) * k, -0.006])).rotate(0, 0, s * (26 + i * 10));
        M.sphere([0, 0, 0], [(0.1 - i * 0.01) * k, 0.05 * k, (0.1 - i * 0.01) * k], 6, 3, false);
        M.pop();
      }
    }
    kit.mark('arms');
    // head: a friendly face with big round eyes (catch-lights), small round ears, black hair with a top-knot (髻) and a red
    // head-wrap (赤帻) with two short tails behind that sway
    kit.bone('head'); M.color = C.soSkin;
    kit.headShape(0.94);
    kit.ears(C.soSkin);
    kit.eyes(H('#3A2C28'), hairTone(C.soHair), 'almond', -6, -0.03, 0.41, 1.1, 0, 1.2);
    kit.nose(P.shade(C.soSkin, 0.86), 1, -0.42, 0.3, 0.2);
    kit.smile(H('#9A5244'), -0.68, 0.3);
    kit.mark('face');
    // black hair (#262022) — Lanternvale's hair-tone lift turned it grey-brown here; the top-knot a shade lighter
    const hair = C.soHairCap;
    kit.hairCap(hair, 60, 102, 138, 1.07, null, null, 4);
    M.color = C.soKnot;
    const knotAt = kit.hairPoint(180, 26, 1.0);
    const knotR = kit.hv(0.42, 0.34, 0.4);
    M.sphere(V.add(knotAt, kit.hv(0, 0.1, 0.02)), knotR, 7, 3, true);
    M.color = C.soWrap;
    M.band(V.add(knotAt, kit.hv(0, -0.02, 0.02)), knotR[0] * 0.86, knotR[0] * 0.92, 0, 0.12 * kit.Ry, 7, knotR[2] / knotR[0]);
    // the 赤帻: a red band round the head at the hairline (the hair shows above it), knotted behind
    M.shell(kit.hairC, V.mul(kit.hairR, 1.045), -180, 180, 12, th => kit.hairLine(th) - 20, th => kit.hairLine(th) - 3, 1);
    const back = kit.hairPoint(180, kit.hairLine(180) - 12, 1.06);
    M.sphere(back, [0.2 * R, 0.16 * R, 0.12 * R], 5, 2, false);
    kit.bone('hair1');
    const tails = M.vertexCount;
    for (const s of [1, -1]) {
      const a = V.add(back, [s * 0.06 * R, -0.08 * R, -0.04 * R]);
      M.curvedStrip(a, V.add(a, [s * 0.04 * k, -0.05 * k, -0.035 * k]), V.add(a, [s * 0.065 * k, -0.13 * k, -0.025 * k]), 0.048 * k, 0.036 * k, [s * 0.3, 0, -1], 0.009, 2);
    }
    M.windBy(tails, p => 0.5 * clamp01((back[1] - p[1]) / (0.13 * k)));
    kit.mark('hair');
    // spear on the right hand (figure.js spear proportions)
    kit.bone('handR');
    M.push().setMatrix(M.matrix.clone().multiply(socketRest(fig, 'gripR')));
    M.color = C.wood; M.segment([0, -0.95, 0], [0, 1.34, 0], 0.019, 0.017, 6);
    M.color = C.gold; M.band([0, 0, 0], 0.024, 0.024, 1.3, 1.36, 6);
    M.color = C.steel; M.spike([0, -0.95, 0], [0, -1, 0], 0.02, 0.07, 5);
    M.color = C.tassel;
    M.lathe([[0.016, 1.33], [0.05, 1.28], [0.05, 1.24], [0.02, 1.2]].reverse(), 6, false, true, true);
    // leaf-shaped blade: a flat diamond section lofted along the shaft
    M.color = C.steel;
    const leaf = [[1.35, 0.012], [1.42, 0.042], [1.5, 0.046], [1.58, 0.03], [1.66, 0.0015]];
    M.loft(leaf.map(([y, w]) => [[-w, y, 0], [0, y, 0.011], [w, y, 0], [0, y, -0.011]]), true, true);
    M.pop();
    kit.mark('spear');
    return kit;
  }

  // ======================================================= assembly ==
  /**
   * Lanternvale's PoleHold rule: a staff or spear held at rest on the camera side leans away from the camera, out beside
   * the head, so it never splits the face. figure.js's clips stay untouched; after the pose, the right hand (the weapon is
   * baked onto it) is turned in the character frame: `roll` degrees outwards (the top of the shaft towards the character's
   * right) and `back` degrees backwards. Only in the rest / travel clips (the strikes keep figure.js's weapon line).
   */
  Pilot.POLE_LEAN = { roll: 20, back: -24, clips: ['idle', 'walk', 'run', 'ride', 'gallop', 'march', 'stand'] };
  const _lq = { a: new THREE.Quaternion(), b: new THREE.Quaternion(), c: new THREE.Quaternion(), d: new THREE.Quaternion(), e: new THREE.Euler() };
  function poleLean(fig, lean) {
    const hand = fig.bones && fig.bones.handR, rootB = fig.bones && fig.bones.root;
    if (!hand || !rootB || !lean || (!lean.roll && !lean.back)) return;
    fig.root.updateMatrixWorld(true);
    const qR = rootB.getWorldQuaternion(_lq.a);
    const qH = hand.getWorldQuaternion(_lq.b);
    const qL = _lq.c.setFromEuler(_lq.e.set((lean.back || 0) * D2R, 0, (lean.roll || 0) * D2R, 'XZY'));
    // qH' = qR · qL · qR⁻¹ · qH  (the lean expressed in the character frame)
    const qW = _lq.d.copy(qR).multiply(qL).multiply(qR.clone().invert()).multiply(qH);
    const qP = hand.parent.getWorldQuaternion(_lq.c);
    hand.quaternion.copy(qP.invert().multiply(qW));
    hand.updateMatrixWorld(true);
  }
  Pilot.poleLean = poleLean;

  /**
   * figure.js's head joint is placed for its own head (the chin just above it); a real-size head sits higher, so a nod or
   * a turn about that joint would swing the whole head off the neck. After the pose, the head bone keeps its rotation but
   * is shifted so that it turns about `pivot` (the atlas, in the bind / character frame) instead: p = p₀ + (I − R)·d.
   */
  const _hv = new THREE.Vector3();
  function headPivot(fig, rest, d) {
    const hb = fig.bones && fig.bones.head;
    if (!hb) return;
    _hv.copy(d).applyQuaternion(hb.quaternion);
    hb.position.copy(rest).add(d).sub(_hv);
    hb.updateMatrixWorld(true);
  }
  Pilot.headPivot = headPivot;

  function makeRig(fig, kit, o) {
    const geo = kit.M.toGeometry({ skinned: true });
    const headRest = fig.bones.head.position.clone();
    const headD = new THREE.Vector3(kit.headPivot[0] - kit.J.head[0], kit.headPivot[1] - kit.J.head[1], kit.headPivot[2] - kit.J.head[2]);
    hideFigure(fig);
    const mesh = LP.skinnedMesh(geo, fig.skeleton, { receiveShadow: false });
    mesh.name = 'LP body';
    fig.root.add(mesh);
    const shadow = LP.blobShadow(0.5 * (o.shadowK || 1), 0.48 * (o.shadowK || 1), 0.5, 0.5);
    fig.root.add(shadow);
    const H_ = fig.pr.H;
    const lean = o.lean === false ? null : Object.assign({}, Pilot.POLE_LEAN, o.lean || {});
    return {
      fig, mesh, shadow, kit, root: fig.root, tris: geo.userData.tris, height: H_,
      setPose(clip, t, opts) {
        fig.setPose(clip, t, opts);
        headPivot(fig, headRest, headD);
        if (lean && lean.clips.indexOf(clip) >= 0 && fig.style === 'pole') poleLean(fig, lean);
        shadow.visible = !fig.mounted;
        return this;
      },
    };
  }
  /** 关羽 on the figure.js skeleton (general '关羽', glaive guandao). */
  Pilot.guanYu = function (opts) {
    opts = opts || {};
    const fig = SG.Figure.general(opts.gen || '关羽', { lod: 'low', weapon: 'guandao', shadow: false });
    const kit = buildGuanYu(fig, opts);
    return makeRig(fig, kit, opts);
  };
  /** A Han spearman in the same style (figure.js soldier skeleton). */
  Pilot.soldier = function (opts) {
    opts = opts || {};
    const fig = SG.Figure.soldier({ type: 'spear', culture: 'han', lod: 'low', seed: opts.seed || 0, shadow: false });
    const kit = buildSoldier(fig, opts);
    return makeRig(fig, kit, opts);
  };
  // ========================================================== horse ==
  // figure.js horse joints (k = 1): body (0, 1.15, −0.1), chest (0, 1.18, 0.38), neck1 (0, 1.36, 0.5), neck2 (0, 1.62, 0.74),
  // head (0, 1.86, 0.92), tail1 (0, 1.3, −0.8), tail2 (0, 0.95, −0.9), saddle (0, 1.47, −0.02); legs upF (±0.15, 1.02, 0.42)
  // → loF 0.55 → ftF 0.2; upH (±0.17, 0.98, −0.5) → loH (0.58, −0.7) → ftH (0.2, −0.64). Left = +X.
  // Lanternvale's creatures (CreatureKit) are stocky and readable: a deep barrel close to the ground (the clear leg under the
  // belly ≈ 0.9 × the barrel's depth), thick tapered upper legs over slim cannons with knee / hock / fetlock bulges, a big
  // rounded head with large eye discs, ears up, a mane and tail of a few broad locks that sway.
  function buildHorse(h, o) {
    o = o || {};
    const BI = {};
    SG.Figure.horseBoneNames.forEach((n, i) => { BI[n] = i; });
    const M = new LP.MeshBuilder(77);
    M.jitter = 0.05;
    const bone = n => { M.bone = BI[n]; };
    const coat = o.coat ? H(o.coat) : C.coat, coatDark = P.shade(coat, 0.84), points = o.points ? H(o.points) : C.coatPoints;
    const belly = P.mix(coat, H('#E8C9A0'), 0.2), mark = C.marking;
    const mane = o.mane ? H(o.mane) : C.mane, mane2 = o.mane ? P.shade(H(o.mane), 1.2) : C.mane2;
    const cloth = o.color ? H(o.color) : C.barding, clothDark = P.shade(cloth, 0.78), trim = C.bardingTrim;
    // ---- torso: one faceted bean from rump to chest, rear half on body, front half on chest (overlapping); the back line
    // stays under the saddle (1.47 m), the belly hangs low and round
    const z0 = -0.98, z1 = 0.76;
    const section = z => {
      const s = clamp01((z - z0) / (z1 - z0)), u = s * 2 - 1;
      const e = Math.pow(Math.max(0, 1 - Math.pow(Math.abs(u), 2.4)), 1 / 2.4);
      const withers = Math.exp(-Math.pow((s - 0.74) / 0.12, 2));
      const croup = Math.exp(-Math.pow((s - 0.2) / 0.12, 2));
      const cy = lerp(1.1, 1.14, sstep((s - 0.2) / 0.6)) + 0.03 * withers + 0.025 * croup;
      const w = 0.345 * e * lerp(1.0, 0.95, s);
      return { cy, w, top: 0.34 * e * (1 + 0.1 * withers + 0.06 * croup), bottom: 0.44 * e * (1 - 0.05 * Math.exp(-Math.pow((s - 0.5) / 0.2, 2))) };
    };
    const n = 12, zs = [];
    for (let i = 0; i <= n; i++) zs.push(lerp(z0, z1, 0.5 - 0.5 * Math.cos(Math.PI * i / n)));
    const mid = n / 2, sides = 10;
    const piece = (b, from, to) => {
      bone(b);
      const rings = [];
      for (let i = from; i <= to; i++) {
        const sc = section(zs[i]);
        let kk = 1;
        if (b === 'body' && i > mid) kk = 1 - 0.3 * sstep((i - mid) / 2);
        if (b === 'chest' && i < mid) kk = 1 - 0.3 * sstep((mid - i) / 2);
        const ring = [];
        for (let j = 0; j < sides; j++) {
          const a = j * Math.PI * 2 / sides, c = Math.cos(a);
          ring.push([Math.sin(a) * sc.w * kk, sc.cy + c * (c >= 0 ? sc.top : sc.bottom) * kk, zs[i]]);
        }
        rings.push(ring);
      }
      const keepJ = M.jitter; M.jitter = 0.03;
      M.loft(rings, from === 0, to === n, nrm => (nrm[1] < -0.6 ? belly : nrm[1] > 0.75 ? P.shade(coat, 1.04) : coat));
      M.jitter = keepJ;
    };
    piece('body', 0, mid + 2);
    piece('chest', mid - 2, n);
    // ---- neck (neck1 / neck2): thick and arched, deep at the chest
    const neckPts = [[0, 1.26, 0.52], [0, 1.46, 0.64], [0, 1.65, 0.77], [0, 1.81, 0.87], [0, 1.9, 0.91]];
    const neckW = [0.17, 0.145, 0.12, 0.1, 0.09], neckH = [0.31, 0.24, 0.18, 0.14, 0.12];
    // frame(): the unit vector across a neck / face ring towards the throat / jaw (its negative: the crest / the brow)
    const frame = (pts, i) => { const t = V.norm(V.sub(pts[Math.min(pts.length - 1, i + 1)], pts[Math.max(0, i - 1)])); return V.norm(V.cross([1, 0, 0], t)); };
    const neckRings = neckPts.map((c, i) => {
      const up = frame(neckPts, i), ring = [];
      for (let j = 0; j < 8; j++) {
        const a = j * Math.PI * 2 / 8;
        ring.push(V.add(V.add(c, [Math.sin(a) * neckW[i], 0, 0]), V.mul(up, Math.cos(a) * neckH[i] * (Math.cos(a) > 0 ? 1 : 0.85))));
      }
      return ring;
    });
    bone('neck1'); M.color = coat;
    M.loft(neckRings.slice(0, 3), true, false);
    bone('neck2'); M.color = coat;
    M.loft(neckRings.slice(1, 5), false, false);
    // ---- head: a broad rounded skull and jowl (soft), a tapering face into a soft cream muzzle with nostril dents, a white
    // blaze; big eye discs turned out from the sides (catch-lights), ears up, the plume small and behind the ears
    bone('head');
    const HJ = [0, 1.86, 0.92];
    M.push().translate(HJ).scale(1.15).translate(V.neg(HJ));
    const skull = [0, 1.87, 0.97];
    M.color = coat;
    M.sphere(skull, [0.118, 0.125, 0.14], 9, 5, true);
    M.sphere([0, 1.95, 0.9], [0.09, 0.07, 0.1], 7, 4, true);     // poll / forehead
    const faceAxis = [[0, 1.86, 1.04], [0, 1.77, 1.14], [0, 1.67, 1.23], [0, 1.6, 1.29]];
    const faceW = [0.1, 0.088, 0.078, 0.072], faceH = [0.105, 0.09, 0.08, 0.075];
    const faceRings = faceAxis.map((c, i) => {
      const up = frame(faceAxis, i), ring = [];
      for (let j = 0; j < 8; j++) { const a = j * Math.PI * 2 / 8; ring.push(V.add(V.add(c, [Math.sin(a) * faceW[i], 0, 0]), V.mul(up, Math.cos(a) * faceH[i]))); }
      return ring;
    });
    M.color = coat;
    M.loft(faceRings, false, false, nrm => (nrm[1] < -0.55 ? coatDark : coat));
    const muzzleC = [0, 1.565, 1.315];
    M.color = C.muzzle;
    M.sphere(muzzleC, [0.085, 0.078, 0.088], 8, 4, true);
    M.color = H('#3A241C');
    for (const s of [1, -1]) {
      M.push().translate(s * 0.042, 1.585, 1.392).rotate(-20, s * 22, 0);
      M.sphere([0, 0, 0], [0.018, 0.024, 0.008], 5, 2, false);
      M.pop();
    }
    M.color = P.shade(C.muzzle, 0.7);
    M.box([0, 1.512, 1.37], [0.07, 0.008, 0.02], null, 'tfrl');     // mouth line
    // white blaze down the front of the face
    const blaze = faceRings.map((r, i) => V.add(r[4], V.mul(frame(faceAxis, i), -0.001)));
    M.color = mark;
    for (let i = 0; i < blaze.length - 1; i++) M.strip(blaze[i], blaze[i + 1], lerp(0.045, 0.034, i / 3), lerp(0.045, 0.034, (i + 1) / 3), V.neg(frame(faceAxis, i)), 0.004);
    M.push().translate(V.add(skull, [0, 0.035, 0.128])).rotate(-35, 0, 0);
    M.sphere([0, 0, 0], [0.04, 0.05, 0.012], 6, 2, false);
    M.pop();
    // eyes (CreatureKit.Head / BipedKit.Eyes): dark disc, iris, white catch-light
    const keepJ = M.jitter; M.jitter = 0;
    for (const s of [1, -1]) {
      M.push().translate(s * 0.098, 1.885, 1.05).rotate(-8, s * 48, 0);
      M.color = H('#1E1612');
      M.sphere([0, 0, 0], [0.036, 0.038, 0.016], 7, 3, false);
      M.color = H('#4A2E22');
      M.sphere([0, -0.008, 0.008], [0.024, 0.022, 0.012], 6, 2, false);
      M.emission = 0.55; M.color = [1, 1, 0.96];
      M.sphere([s * 0.01, 0.014, 0.015], 0.009, 4, 2, false);
      M.emission = 0;
      M.pop();
    }
    M.jitter = keepJ;
    // ears: four-sided cones, inner face darker
    for (const s of [1, -1]) {
      const at = [s * 0.065, 2.0, 0.9];
      M.aim(at, [s * 0.28, 1, 0.12]);
      M.push().scale(1, 1, 0.6);
      M.color = coat; M.cone([0, 0, 0], 0.045, 0.145, 4);
      M.color = coatDark; M.cone([0, 0.01, 0.012], 0.028, 0.1, 4);
      M.pop();
      M.pop();
    }
    // forelock: a tuft falling over the brow between the ears (sways)
    let ws = M.vertexCount;
    M.color = mane;
    M.curvedStrip([0, 2.02, 0.88], [0.015, 2.0, 0.98], [0.03, 1.93, 1.03], 0.075, 0.035, [0, 0.6, 1], 0.03, 2);
    M.windBy(ws, p => 0.35 * clamp01((2.02 - p[1]) / 0.09));
    // a small red plume behind the ears
    M.color = C.gold;
    M.sphere([0, 2.02, 0.84], 0.02, 5, 2, false);
    M.color = C.tassel;
    M.push().translate(0, 2.02, 0.84).rotate(-24, 0, 0);
    M.lathe([[0.008, 0], [0.026, 0.026], [0.028, 0.055], [0.011, 0.09]], 6, false, true, true);
    M.pop();
    // bridle: noseband, cheek straps, brow band, gold rosettes
    M.color = C.saddle;
    M.push().translate(0, 1.66, 1.225).rotate(-50, 0, 0);
    M.band([0, 0, 0], 0.088, 0.088, -0.018, 0.018, 8, 1.15);
    M.pop();
    for (const s of [1, -1]) {
      M.color = C.saddle;
      M.strip([s * 0.11, 1.95, 0.87], [s * 0.086, 1.68, 1.2], 0.024, 0.024, [s, 0, 0], 0.012);
      M.strip([s * 0.11, 1.95, 0.87], [0, 1.99, 0.95], 0.024, 0.024, [0, 0.5, 1], 0.012);
      M.color = C.gold;
      M.sphere([s * 0.104, 1.76, 1.1], 0.02, 5, 2, false);
      M.sphere([s * 0.08, 1.62, 1.27], 0.018, 5, 2, false);
    }
    M.pop();
    // ---- mane, readable from every side: a thick crest ridge from poll to withers (a diamond section, dark on top, so the
    // neck has a mane line seen from either side and from above), the main curtain hanging down the left side, its lower
    // edge cut into six pointed locks in two tones and its upper edge tucked under the crest (jittered, no ruler line),
    // and a short fringe of five locks on the right side; neck1 / neck2 by position, swaying towards the tips
    const crestAt = t => {
      const f = t * 4, i0 = Math.min(3, Math.floor(f)), u = f - i0;
      const c = V.lerp(neckPts[i0], neckPts[i0 + 1], u), up = V.neg(frame(neckPts, Math.min(4, Math.round(f))));
      return { p: V.add(c, V.mul(up, lerp(neckH[i0], neckH[i0 + 1], u) * 0.9)), up, side: lerp(neckW[i0], neckW[i0 + 1], u) };
    };
    const zSplit = lerp(neckPts[0][2], neckPts[4][2], 0.48), crestYAt = z => crestAt(clamp01((z - neckPts[0][2]) / (neckPts[4][2] - neckPts[0][2]))).p[1];
    let ms = M.vertexCount;
    bone('neck1');
    {
      // a curtain on side sd (+1 left, −1 right): `locks` pointed locks, length k × the main curtain's
      const curtain = (sd, locks, lenK, inner) => {
        const cols = locks * 2, top = [], midO = [], bot = [], inn = [];
        for (let j = 0; j <= cols; j++) {
          const t = lerp(0.04, 0.97, j / cols), { p } = crestAt(t);
          const len = lerp(0.34, 0.2, t) * lenK * (j % 2 ? 1 : 0.55) * (j === 0 || j === cols ? 0.6 : 1);
          const tp = V.add(p, [sd * (0.045 + 0.012 * M.random01()), -0.02 - 0.015 * M.random01(), 0]);
          top.push(tp);
          midO.push(V.add(tp, [sd * (0.06 + 0.02 * (j % 2)), -len * 0.45, -0.01]));
          bot.push(V.add(tp, [sd * (0.085 + 0.02 * (j % 2)), -len, -0.03 - 0.015 * (j % 2)]));
          inn.push(V.add(tp, [-sd * 0.03, -len * 0.25, 0]));
        }
        for (let j = 0; j < cols; j++) {
          const col = (j >> 1) % 2 ? mane2 : mane, out = [sd, 0.2, 0];
          M.quad(top[j], top[j + 1], midO[j + 1], midO[j], out, P.shade(col, 1 + (M.random01() - 0.5) * 0.06));
          M.quad(midO[j], midO[j + 1], bot[j + 1], bot[j], out, P.shade(col, 0.94 + (M.random01() - 0.5) * 0.06));
          if (inner) {
            M.quad(top[j], top[j + 1], inn[j + 1], inn[j], [-sd, 0, 0], P.shade(mane, 0.8));
            M.quad(inn[j], inn[j + 1], bot[j + 1], bot[j], [-sd, -0.3, 0], P.shade(mane, 0.75));
          }
        }
      };
      curtain(1, 6, 1, true);
      curtain(-1, 5, 0.42, false);
      // the crest ridge: diamond rings (left, top, right, under) along the crest, darker on its top faces
      const rings = [];
      for (let i = 0; i <= 6; i++) {
        const t = lerp(0.0, 1.0, i / 6), { p, up } = crestAt(t), w = lerp(0.075, 0.06, t) * (i === 0 || i === 6 ? 0.6 : 1);
        const h = 0.075 * (i === 0 ? 0.5 : 1) + 0.012 * Math.sin(i * 2.1);
        rings.push([V.add(V.add(p, [w, 0, 0]), V.mul(up, -0.025)), V.add(p, V.mul(up, h)), V.add(V.add(p, [-w, 0, 0]), V.mul(up, -0.025)), V.add(p, V.mul(up, -0.07))]);
      }
      M.loft(rings, true, true, nrm => (nrm[1] > 0.55 ? mane : mane2));
    }
    M.boneBy(ms, q => (q[2] < zSplit ? BI.neck1 : BI.neck2));
    M.windBy(ms, q => 0.45 * clamp01((crestYAt(q[2]) - q[1]) / 0.3));
    // ---- legs: six-sided lathes — thick forearm / gaskin tapering to a slim cannon, knee and hock bulges, a fetlock bulge,
    // the pastern and a flared seven-sided hoof; darker points below the knee, cream socks on the hind legs
    const leg = (fh, S) => {
      const s = S === 'L' ? 1 : -1, front = fh === 'F', x0 = (front ? 0.15 : 0.17) * s;
      const lo = front ? [x0, 0.55, 0.45] : [x0, 0.58, -0.7], ft = front ? [x0, 0.2, 0.46] : [0.16 * s, 0.2, -0.64];
      const sock = !front, low = sock ? mark : points;
      bone('up' + fh + S); M.color = coat;
      if (front) {
        M.segment([x0, 1.0, 0.43], [x0, 0.8, 0.44], 0.105, 0.1, 7, false, false);                 // forearm
        M.segment([x0, 0.8, 0.44], V.add(lo, [0, 0.03, 0]), 0.1, 0.066, 7, false, false);
      } else {
        M.segment([x0, 0.98, -0.56], lo, 0.115, 0.066, 7, false, false);                         // gaskin
        M.sphere(V.add(V.lerp([x0, 0.98, -0.56], lo, 0.4), [s * 0.01, 0, -0.045]), [0.07, 0.12, 0.075], 7, 3, true);   // gaskin muscle
      }
      // lower leg (lo bone): knee / hock bulge, cannon
      bone('lo' + fh + S); M.color = front ? coat : coatDark;
      M.sphere(lo, front ? [0.068, 0.074, 0.07] : [0.07, 0.08, 0.075], 6, 3, false);
      if (!front) { M.color = coatDark; M.sphere(V.add(lo, [0, 0.02, -0.05]), [0.04, 0.05, 0.04], 5, 2, false); }   // point of hock
      M.color = points;
      M.segment(V.add(lo, [0, -0.03, 0]), V.add(ft, [0, 0.03, 0]), 0.05, 0.045, 6, false, false);
      // fetlock, pastern and hoof (ft bone)
      bone('ft' + fh + S); M.color = low;
      M.sphere(ft, [0.06, 0.062, 0.066], 6, 3, false);
      M.segment(ft, [ft[0], 0.105, ft[2] + 0.04], 0.052, 0.056, 6, false, false);
      M.color = C.hoof;
      M.push().translate(ft[0], 0, ft[2] + 0.045);
      M.lathe([[0.078, 0], [0.074, 0.03], [0.058, 0.115]], 7, false, true, true);
      M.pop();
    };
    for (const fh of ['F', 'H']) for (const S of ['L', 'R']) leg(fh, S);
    // ---- tail, volumetric from every side: a short dock from inside the rump, then three five-sided locks of hair falling
    // from it, fanned ±8° (≈ 0.2 m across at mid-length), tips staggered, mane / mane2 alternating; it hangs (the post-pose
    // in Pilot.horse lowers figure.js's idle / walk lift), the dock on tail1, the fall on tail2, swaying towards the tips
    ws = M.vertexCount;
    bone('tail1');
    const dockRing = (c, w, d) => { const r = []; for (let j = 0; j < 5; j++) { const a = j * Math.PI * 2 / 5; r.push([c[0] + Math.sin(a) * w, c[1], c[2] + Math.cos(a) * d]); } return r; };
    M.color = mane;
    M.loft([dockRing([0, 1.38, -0.86], 0.06, 0.06), dockRing([0, 1.24, -1.0], 0.085, 0.07)], true, false);
    const lockCols = [mane2, mane, mane2];
    [-1, 0, 1].forEach((f, li) => {
      const a = f * 9 * D2R, len = 0.74 + (li === 1 ? 0.06 : f > 0 ? -0.06 : 0);
      const pt = (t, bulge) => [Math.sin(a) * len * t + f * 0.025 * Math.sin(Math.PI * t), 1.25 - len * t * Math.cos(a), -1.01 - 0.11 * t - 0.05 * Math.sin(Math.PI * t) + bulge];
      const ts = [0, 0.3, 0.62, 1], ws_ = [0.062, 0.08, 0.064, 0.014], ds = [0.06, 0.078, 0.062, 0.012];
      const rings = ts.map((t, i) => dockRing(pt(t, 0), ws_[i], ds[i]));
      M.loft(rings, false, true, nrm => P.shade(lockCols[li], nrm[0] * f > 0.3 ? 1.1 : 1));
    });
    M.boneBy(ws, q => (q[1] > 1.06 ? BI.tail1 : BI.tail2));
    M.windBy(ws, q => 0.6 * clamp01((1.2 - q[1]) / 0.7));
    // ---- tack: saddle cloth (faction colour, cream hem, gold studs), saddle, girth, breast collar with red tassels
    bone('body');
    const cz = [-0.44, -0.24, -0.04, 0.16, 0.34], angs = [0, 26, 52, 78, 100, 112];
    const clothPt = (z, deg, off) => { const sc = section(z), a = deg * D2R, c = Math.cos(a); return [Math.sin(a) * (sc.w + off), sc.cy + c * (c >= 0 ? sc.top : sc.bottom) + off * Math.sign(c), z]; };
    for (const s of [1, -1]) for (let i = 0; i < cz.length - 1; i++) for (let kk = 0; kk < angs.length - 1; kk++) {
      const hem = kk === angs.length - 2, off = 0.028;
      const a = clothPt(cz[i], s * angs[kk], off), b = clothPt(cz[i], s * angs[kk + 1], off), c = clothPt(cz[i + 1], s * angs[kk + 1], off), d = clothPt(cz[i + 1], s * angs[kk], off);
      const zc = (cz[i] + cz[i + 1]) / 2, out = V.sub(V.mul(V.add(V.add(a, b), V.add(c, d)), 0.25), [0, section(zc).cy, zc]);
      const col = hem ? trim : (i === 0 || i === cz.length - 2) && kk > 0 ? clothDark : cloth;
      M.quad(a, b, c, d, out, P.shade(col, 1 + (M.random01() - 0.5) * 0.06));
      if (kk >= angs.length - 3) { const inn = V.mul(V.norm(out), -0.014); M.quad(V.add(a, inn), V.add(b, inn), V.add(c, inn), V.add(d, inn), V.neg(out), clothDark); }
    }
    for (const s of [1, -1]) for (const z of [cz[0], cz[cz.length - 1]]) for (let kk = 0; kk < angs.length - 1; kk++) {
      const a = clothPt(z, s * angs[kk], 0.028), b = clothPt(z, s * angs[kk + 1], 0.028);
      const inn = V.mul(V.norm([a[0], a[1] - section(z).cy, 0]), -0.014);
      M.quad(a, b, V.add(b, inn), V.add(a, inn), [0, 0, Math.sign(z)], trim);
    }
    M.color = C.gold;
    for (const s of [1, -1]) for (const z of [-0.34, -0.14, 0.06, 0.26]) M.sphere(clothPt(z, s * 106, 0.04), 0.024, 4, 2, false);
    // saddle: seat, raised pommel and cantle with gold rims
    const sy = section(-0.05).cy + section(-0.05).top;
    M.color = C.saddle;
    M.push().translate(0, sy - 0.02, -0.05);
    M.taperedBox([0, 0, 0], [0.34, 0.08, 0.5], 0.15, 0.08);
    M.color = P.shade(C.saddle, 1.15);
    M.push().translate(0, 0.02, 0.24).rotate(-18, 0, 0); M.taperedBox([0, 0, 0], [0.3, 0.11, 0.09], 0.45, 0.2); M.pop();
    M.push().translate(0, 0.02, -0.25).rotate(20, 0, 0); M.taperedBox([0, 0, 0], [0.36, 0.13, 0.09], 0.4, 0.2); M.pop();
    M.color = C.gold;
    M.box([0, 0.125, 0.255], [0.16, 0.022, 0.05]);
    M.box([0, 0.145, -0.275], [0.21, 0.022, 0.05]);
    M.pop();
    // girth strap under the belly
    M.color = C.saddle;
    {
      const sc = section(0.12), gp = a => { a *= D2R; return [Math.sin(a) * (sc.w + 0.012), sc.cy + Math.cos(a) * (sc.bottom + 0.012), 0.12]; };
      for (const s of [1, -1]) for (let i = 0; i < 3; i++) { const a0 = s * lerp(108, 180, i / 3), a1 = s * lerp(108, 180, (i + 1) / 3); M.strip(gp(a0), gp(a1), 0.06, 0.06, V.sub(gp((a0 + a1) / 2), [0, sc.cy, 0.12]), 0.01); }
    }
    // breast collar across the chest with three red tassels
    bone('chest');
    const bc = [];
    for (let i = 0; i <= 6; i++) {
      const a = lerp(-62, 62, i / 6) * D2R, z = 0.63 + Math.cos(a) * 0.1;
      const sc = section(Math.min(z, 0.72));
      bc.push([Math.sin(a) * (sc.w + 0.03), sc.cy + 0.02 - Math.cos(a) * 0.06, z + 0.02]);
    }
    M.color = C.saddle;
    for (let i = 0; i < 6; i++) M.strip(bc[i], bc[i + 1], 0.06, 0.06, V.add([bc[i][0], 0, 1], [0, 0, 0.5]), 0.016);
    for (const i of [2, 3, 4]) {
      const p = bc[i];
      M.color = C.gold; M.sphere(V.add(p, [0, -0.02, 0.012]), 0.022, 5, 2, false);
      M.color = C.tassel;
      M.push().translate(p[0], 0, p[2] + 0.015);
      M.lathe([[0.012, p[1] - 0.03], [0.042, p[1] - 0.08], [0.036, p[1] - 0.16], [0.012, p[1] - 0.2]].reverse(), 5, false, true, true);
      M.pop();
    }
    return M;
  }

  /** Degrees taken off figure.js's tail1 / tail2 lift per clip (the tail hangs in idle and walk). */
  Pilot.TAIL_REST = { idle: [18, 13], walk: [18, 14] };
  Pilot.horse = function (opts) {
    opts = opts || {};
    const horse = SG.Figure.horse({ lod: 'low', color: opts.color || '#2f7d4a', seed: opts.seed || 0, shadow: false });
    const M = buildHorse(horse, opts);
    const geo = M.toGeometry({ skinned: true });
    hideFigure(horse);
    const mesh = LP.skinnedMesh(geo, horse.skeleton, { receiveShadow: false });
    mesh.name = 'LP horse';
    horse.root.add(mesh);
    const shadow = LP.blobShadow(0.58, 1.3, 0.5, 0.55);
    horse.root.add(shadow);
    return {
      horse, mesh, shadow, root: horse.root, tris: geo.userData.tris, height: 2.05,
      setPose(clip, t) {
        horse.setPose(clip, t);
        // the tail hangs at rest: figure.js lifts it ~35–40° in idle / walk (a flag at the game camera); lower tail1 / tail2
        // so the fall stays within ~10° of vertical, keeping the side-to-side sway; trot / gallop keep their lift
        const lw = Pilot.TAIL_REST[clip];
        if (lw) { const b = horse.bones; b.tail1.rotation.x -= lw[0] * D2R; b.tail2.rotation.x -= lw[1] * D2R; }
        return this;
      },
      mount(rider) { horse.mount(rider.fig); if (rider.shadow) rider.shadow.visible = false; return this; },
    };
  };
  // ================================================ masonry helpers ==
  // Lanternvale's walls are never one flat plane: a plinth of jittered stone blocks in three tones, a darker damp foot,
  // per-block tone, moss on the copings. These are shared by the gate base and the city walls.
  // ============================================ props (from SG.LPProps) ==
  // The pilot's gate tower, city wall, 槐 tree, pine, bush and lamp post now live in the shared prop library
  // (js/lp-props.js: gate_tower, city_wall, tree_huai, tree_pine, bush 'pilot', lamp_post — the same models); these
  // wrappers keep the pilot's interface. lit: true = lit, false = unlit (the pilot's two looks).
  const Props = () => {
    if (!SG.LPProps) throw new Error('LPPilot: load js/lp-props.js before js/lp-pilot.js');
    return SG.LPProps;
  };
  Pilot.gateTower = function (o) {
    o = o || {};
    const m = Props().make('gate_tower', { lit: !!o.lit, color: o.flagColor || '#2f7d4a', name: o.name || '荆州', hallName: o.hallName || '宾阳楼' });
    m.name = 'LP gate tower';
    return m;
  };
  Pilot.tree = seed => { const m = Props().make('tree_huai', { seed: seed | 0 }); m.name = 'LP tree'; return m; };
  Pilot.pine = seed => { const m = Props().make('tree_pine', { seed: seed | 0 }); m.name = 'LP pine'; return m; };
  Pilot.bush = seed => { const m = Props().make('bush', { seed: seed | 0, variant: 'pilot' }); m.name = 'LP bush'; return m; };
  Pilot.lampPost = function (lit) {
    const m = Props().make('lamp_post', { lit: !!lit });
    m.userData.lamp = m.userData.lp.lamps[0].pos;
    m.name = 'LP lamp post';
    return m;
  };
  Pilot.cityWall = function (len, o) {
    o = o || {};
    return Props().make('city_wall', { len, variant: (o.variant | 0) & 1, color: o.flagColor || '#2f7d4a' });
  };
  Pilot.rock = function (seed) {
    const g = LP.MeshCache.get('lp_rock|' + (seed | 0), () => {
      const M = new LP.MeshBuilder(501 + (seed | 0));
      M.jitter = 0.07; M.aoStrength = 0.25; M.aoHeight = 0.3;
      M.facetBlob([0, 0.3, 0], [0.75, 0.55, 0.6], 1, 0.22, 3 + (seed | 0), 0.45, LP.byNormal(PAL.mossLight, PAL.stone, PAL.stoneDark, 0.55));
      M.facetBlob([0.6, 0.15, 0.25], [0.35, 0.28, 0.3], 0, 0.25, 9 + (seed | 0), 0.4, LP.byNormal(PAL.moss, PAL.stoneLight, PAL.stoneDark, 0.55));
      return M.toGeometry();
    });
    return LP.mesh(g);
  };
  /** Grass tufts and wild flowers scattered over an area, avoiding `avoid(x, z)` (no ink, sways). */
  Pilot.grassField = function (o) {
    o = o || {};
    const M = new LP.MeshBuilder(o.seed || 801);
    M.jitter = 0.08;
    M.windGradient = true; M.windY0 = 0; M.windY1 = 0.5; M.wind = 0.5;
    const n = o.count || 140, x0 = o.x0 ?? -20, x1 = o.x1 ?? 20, z0 = o.z0 ?? -10, z1 = o.z1 ?? 20;
    const greens = [H('#8DB45A'), H('#A6C46A'), H('#7AA44E'), H('#B4C878')];
    const flowers = [H('#F5F0E6'), H('#F2B8C6'), H('#F4D35E'), H('#C9A8FF'), H('#F08C6C')];
    let placed = 0, tries = 0;
    while (placed < n && tries < n * 20) {
      tries++;
      const x = lerp(x0, x1, M.random01()), z = lerp(z0, z1, M.random01());
      if (o.avoid && o.avoid(x, z)) continue;
      placed++;
      const h = 0.28 + M.random01() * 0.3;
      M.tuft([x, 0, z], h, 4 + ((M.random01() * 3) | 0), greens[(M.random01() * greens.length) | 0]);
      if (M.random01() < (o.flowerRate ?? 0.35)) {
        const fc = flowers[(M.random01() * flowers.length) | 0];
        for (let i = 0; i < 2; i++) {
          const fx = x + (M.random01() - 0.5) * 0.4, fz = z + (M.random01() - 0.5) * 0.4, fy = 0.18 + M.random01() * 0.2;
          M.color = H('#6E9A4E'); M.segment([fx, 0, fz], [fx, fy, fz], 0.01, 0.008, 3, false, false);
          M.color = fc; M.emission = 0.08;
          M.sphere([fx, fy + 0.02, fz], [0.05, 0.025, 0.05], 5, 2, false);
          M.color = H('#F4D35E'); M.sphere([fx, fy + 0.04, fz], 0.018, 4, 2, false);
          M.emission = 0;
        }
      }
    }
    // dense clumps of taller grass ([x, z, radius, count, y?, height?]: y lifts a clump onto a wall-walk or a ledge) and
    // flower patches ([x, z, radius, count]: blooms on stems)
    for (const [cx, cz, r, cnt, cy, ch] of o.clumps || []) {
      const y = cy || 0, hk = ch || 1;
      M.windY0 = y; M.windY1 = y + 0.5;
      for (let i = 0; i < cnt; i++) {
        const a = M.random01() * Math.PI * 2, d = Math.sqrt(M.random01()) * r;
        M.tuft([cx + Math.cos(a) * d, y, cz + Math.sin(a) * d], (0.38 + M.random01() * 0.32) * hk, 5, greens[(M.random01() * greens.length) | 0]);
      }
    }
    M.windY0 = 0; M.windY1 = 0.5;
    for (const [cx, cz, r, cnt] of o.flowerPatches || []) for (let i = 0; i < cnt; i++) {
      const a = M.random01() * Math.PI * 2, d = Math.sqrt(M.random01()) * r, x = cx + Math.cos(a) * d, z = cz + Math.sin(a) * d;
      const fy = 0.22 + M.random01() * 0.22, fc = flowers[(((i * 7 + Math.round(Math.abs(cx * 13 + cz * 5))) % flowers.length) + flowers.length) % flowers.length];
      M.color = H('#5E8E42'); M.segment([x, 0, z], [x, fy, z], 0.012, 0.009, 3, false, false);
      M.blade([x, 0.05, z], [x + 0.08, 0.16, z + 0.03], 0.06);
      M.color = fc; M.emission = 0.06;
      M.push().translate(x, fy, z).rotate(-20 + M.random01() * 40, M.random01() * 360, 0);
      M.disc([0, 0, 0], 0.075, 5); M.disc([0, -0.004, 0], 0.075, 5, true);
      M.color = H('#F4D35E'); M.sphere([0, 0.012, 0], 0.024, 4, 2, false);
      M.pop(); M.emission = 0;
    }
    // tiny flower specks flat on the grass (o.specks)
    M.wind = 0;
    for (let i = 0; i < (o.specks || 0); i++) {
      const x = lerp(x0, x1, M.random01()), z = lerp(z0, z1, M.random01());
      if (o.avoid && o.avoid(x, z)) continue;
      M.color = flowers[(M.random01() * flowers.length) | 0];
      M.emission = 0.05;
      M.disc([x, 0.03, z], 0.035 + M.random01() * 0.025, 5);
      M.emission = 0;
    }
    // pebbles on the road (o.pebbles count, o.onRoad(x, z) → true where they may lie): ten-triangle stones in three greys
    const greys = [H('#9C948A'), H('#B3AB9E'), H('#7F786F')];
    for (let i = 0, tries = 0; i < (o.pebbles || 0) && tries < 4000; tries++) {
      const x = lerp(x0, x1, M.random01()), z = lerp(z0, z1, M.random01());
      if (!o.onRoad || !o.onRoad(x, z)) continue;
      i++;
      const r = 0.06 + M.random01() * 0.06;
      M.color = P.shade(greys[i % 3], 0.92 + M.random01() * 0.14);
      M.push().translate(x, 0, z).rotate(0, M.random01() * 360, 0).scale(1, 1, 0.7 + M.random01() * 0.3);
      M.lathe([[r, 0.005], [r * 0.75, r * 0.45], [0, r * 0.62]], 5, false, false, false);
      M.pop();
    }
    // leaf litter under the trees (o.litter: [[x, z, radius, count]]): small brown and ochre flecks
    const leaves = [H('#9A6A3A'), H('#B48A48'), H('#7E5A36'), H('#A87F3E')];
    for (const [cx, cz, rad, cnt] of o.litter || []) for (let i = 0; i < cnt; i++) {
      const a = M.random01() * Math.PI * 2, d = Math.sqrt(M.random01()) * rad, x = cx + Math.cos(a) * d, z = cz + Math.sin(a) * d;
      if (o.avoid && o.avoid(x, z) && !(o.onRoad && o.onRoad(x, z))) continue;
      M.color = P.shade(leaves[i % 4], 0.92 + M.random01() * 0.16);
      M.push().translate(x, 0.025, z).rotate(0, M.random01() * 360, 0);
      M.disc([0, 0, 0], 0.05 + M.random01() * 0.03, 4);
      M.pop();
    }
    const geo = M.toGeometry();
    const mesh = LP.mesh(geo, { outline: false, castShadow: false });
    mesh.name = 'LP grass';
    return mesh;
  };

  /**
   * Vertex-coloured ground (no ink): rolling meadow greens with painterly blotches, darker under `shade(x, z)` (0..1),
   * and a dirt road where `road(x, z)` (0..1) — soft edges, warm ruts.
   */
  Pilot.ground = function (o) {
    o = o || {};
    const W = o.width || 60, D = o.depth || 50, cs = o.cell || 0.8, cx = o.cx || 0, cz = o.cz || 0;
    const M = new LP.MeshBuilder(o.seed || 901);
    const nx = Math.round(W / cs), nz = Math.round(D / cs);
    const noise = (x, z, s) => {
      const f = (i, j) => LP.hash01([i, j, s], 17);
      const ix = Math.floor(x), iz = Math.floor(z), tx = x - ix, tz = z - iz, sx = tx * tx * (3 - 2 * tx), sz = tz * tz * (3 - 2 * tz);
      return lerp(lerp(f(ix, iz), f(ix + 1, iz), sx), lerp(f(ix, iz + 1), f(ix + 1, iz + 1), sx), sz);
    };
    const meadow = c => P.mix(H(c), H('#D2D294'), 0.16), g1 = meadow('#98AE5C'), g2 = meadow('#AABD68'), g3 = meadow('#7E9A4C'), g4 = meadow('#6E8C46'), dirt = H('#AC8155'), dirtDark = H('#8E6744'), dirtLight = H('#BE9566');
    const colorAt = (x, z) => {
      const n1 = noise(x * 0.18, z * 0.18, 1), n2 = noise(x * 0.6, z * 0.6, 2), n3 = noise(x * 1.7, z * 1.7, 3);
      let c = P.mix(P.mix(g3, g1, sstep((n1 - 0.25) / 0.5)), g2, sstep((n2 - 0.55) / 0.3) * 0.7);
      c = P.mix(c, g4, sstep((0.32 - n2) / 0.2) * 0.6);
      c = P.shade(c, 0.93 + n3 * 0.12);
      const r = o.road ? o.road(x, z) : 0;
      if (r > 0) {
        let d = P.mix(dirt, dirtLight, sstep((n2 - 0.45) / 0.4) * 0.8);
        d = P.mix(d, dirtDark, sstep((n3 - 0.62) / 0.3) * 0.5);
        const rut = o.rut ? o.rut(x, z) : 0;
        if (rut > 0) d = P.mix(d, dirtDark, rut * 0.7);
        c = P.mix(c, d, sstep((r + (n3 - 0.5) * 0.45) * 1.5));
      }
      const sh = o.shade ? o.shade(x, z) : 0;
      if (sh > 0) c = P.shade(c, 1 - 0.28 * sh);
      return c;
    };
    const vc = [];
    for (let j = 0; j <= nz; j++) { const row = []; for (let i = 0; i <= nx; i++) { const x = cx - W / 2 + i * cs, z = cz - D / 2 + j * cs; row.push(colorAt(x, z)); } vc.push(row); }
    for (let j = 0; j < nz; j++) for (let i = 0; i < nx; i++) {
      const x0 = cx - W / 2 + i * cs, z0 = cz - D / 2 + j * cs, x1 = x0 + cs, z1 = z0 + cs;
      const avg = (a, b, c) => [(a[0] + b[0] + c[0]) / 3, (a[1] + b[1] + c[1]) / 3, (a[2] + b[2] + c[2]) / 3];
      M.triangleFacing([x0, 0, z0], [x1, 0, z0], [x1, 0, z1], [0, 1, 0], avg(vc[j][i], vc[j][i + 1], vc[j + 1][i + 1]));
      M.triangleFacing([x0, 0, z0], [x1, 0, z1], [x0, 0, z1], [0, 1, 0], avg(vc[j][i], vc[j + 1][i + 1], vc[j + 1][i]));
    }
    const gridVerts = M.triangleCount * 3;
    // the colour the grid shows at (x, z): its triangles interpolate the vertex colours
    const shown = (x, z) => {
      const fx0 = (x - (cx - W / 2)) / cs, fz0 = (z - (cz - D / 2)) / cs;
      const i = clamp(Math.floor(fx0), 0, nx - 1), j = clamp(Math.floor(fz0), 0, nz - 1), fx = clamp01(fx0 - i), fz = clamp01(fz0 - j);
      const c00 = vc[j][i], c10 = vc[j][i + 1], c11 = vc[j + 1][i + 1], c01 = vc[j + 1][i];
      const w = fx >= fz ? [1 - fx, fx - fz, fz, 0] : [1 - fz, 0, fx, fz - fx];
      return [0, 1, 2].map(q => c00[q] * w[0] + c10[q] * w[1] + c11[q] * w[2] + c01[q] * w[3]);
    };
    // cart ruts (o.ruts: polylines [[x, z], …]): thin ribbons just above the ground whose edges carry exactly the ground's
    // colour and whose centre line is darker (shade o.rutShade), so each rut is a soft painted line, finer than the grid
    for (const line of o.ruts || []) {
      const hw = o.rutWidth || 0.11, k = o.rutShade || 0.82;
      const pts = line.map(([x, z]) => [x, z]);
      const rows = pts.map((p, i) => {
        const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)];
        const d = V.norm([b[0] - a[0], 0, b[1] - a[1]]), nrm = [-d[2], d[0]];
        const w = hw * (0.8 + 0.4 * LP.hash01([p[0], 0, p[1]], 5));
        const L = [p[0] - nrm[0] * w, p[1] - nrm[1] * w], R = [p[0] + nrm[0] * w, p[1] + nrm[1] * w];
        return [[L, shown(L[0], L[1])], [p, P.shade(shown(p[0], p[1]), k * (0.97 + 0.06 * LP.hash01([p[0], 1, p[1]], 9)))], [R, shown(R[0], R[1])]];
      });
      for (let r = 0; r < rows.length - 1; r++) for (let s = 0; s < 2; s++) {
        const A = rows[r][s], B = rows[r][s + 1], Cc = rows[r + 1][s + 1], Dd = rows[r + 1][s];
        const v = q => M.addVertex([q[0][0], 0.012, q[0][1]], [0, 1, 0], q[1]);
        const ia = v(A), ib = v(B), ic = v(Cc), id = v(Dd);
        // wind each triangle so it faces up
        const up = (p, q, w_) => ((q[0][1] - p[0][1]) * (w_[0][0] - p[0][0]) - (q[0][0] - p[0][0]) * (w_[0][1] - p[0][1])) > 0;
        if (up(A, B, Cc)) { M.addTri(ia, ib, ic); M.addTri(ia, ic, id); } else { M.addTri(ia, ic, ib); M.addTri(ia, id, ic); }
      }
    }
    // smooth vertex colours: each grid vertex takes the grid colour at its own position (soft, painted ground)
    const geo = M.toGeometry();
    const pos = geo.attributes.position, col = geo.attributes.color;
    for (let i = 0; i < gridVerts; i++) {
      const gi = Math.round((pos.getX(i) - (cx - W / 2)) / cs), gj = Math.round((pos.getZ(i) - (cz - D / 2)) / cs);
      const c = vc[clamp(gj, 0, nz)][clamp(gi, 0, nx)];
      col.setXYZ(i, c[0], c[1], c[2]);
    }
    const mesh = LP.mesh(geo, { outline: false, castShadow: false, detail: o.detail === false ? null : LP.grassDetail(), planar: o.planar || 0.45, detailK: o.detailK != null ? o.detailK : 0.85 });
    mesh.name = 'LP ground';
    return mesh;
  };

  /** The geometry of a named sample (for triangle counts). */
  Pilot.geometry = function (name, o) {
    switch (name) {
      case 'gate': return Pilot.gateTower(o).geometry;
      case 'tree': return Pilot.tree(o && o.seed).geometry;
      case 'pine': return Pilot.pine(o && o.seed).geometry;
      default: return null;
    }
  };
})();
