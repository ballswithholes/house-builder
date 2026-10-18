'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 低多边形美术试点：四个样例配方（SG.LPPilot）

   用 SG.LP（js/lowpoly.js）按《Lanternvale》的风格建模：
   · 人物：关羽 —— FFX 式风格化低多边形，Lanternvale 的头身比（头半径 = 0.08 × 身高，约六头身）：枣红脸（眉下颊面略暗）、
     大丹凤眼（带高光）、卧蚕眉、巾帻下露出的黑发与鬓角、三绺美髯（上三分之一随头、其余随胸）、绿色巾帻（带飘带与玉饰）、
     翡翠绿袍（敞襟、暗绿衬里）罩金甲（护心镜、三层叠甲护肩、甲裙）、腰带与朱红束带、青龙偃月刀（烘进同一个蒙皮网格）。
     刚性蒙皮在 figure.js 的人形骨架上（SG.Figure.general 的骨骼与 setPose 片段直接驱动；袍裙侧片在胯部铰接）。
     另有同风格的汉军枪兵（SG.Figure.soldier 的骨架）：黑发、发髻、赤帻（两条短带随风）、圆眼带高光、封闭的铁甲裙片。
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
     SG.LPPilot.gateTower({ lit }) → THREE.Mesh（带墨线），userData.lamps = 灯笼的模型坐标，userData.tris
     SG.LPPilot.tree(seed?) / pine(seed?) / rock(seed?) / cityWall(len) → THREE.Mesh；bush(seed?) → Mesh（奇数种子：Group，带无墨线的小花）
     SG.LPPilot.lampPost(lit) → Mesh（userData.lamp = 灯光位置）
     SG.LPPilot.grassField(opts) → 草丛 + 野花 + 路上卵石（pebbles / onRoad）+ 树下落叶（litter）（无墨线，随风摆）；
     SG.LPPilot.ground(opts) → 顶点色地面（无墨线；ruts: 车辙折线 → 比网格更细的柔和暗线）
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
  const HEAD_TILT = -12, JAW_FROM = 0.3, JAW_DROP = 0.12, EYE_TURN = 32;
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
      // Lanternvale's head size: R = 0.08 × the figure's height (BipedKit: 0.14 m on 1.75 m, about six heads tall) — a big,
      // readable face from the high game camera. figure.js's skeleton is unchanged: the chin stays just above the neck joint
      // and the bigger head grows upwards around the same head bone.
      this.H = pr.H;
      this.R = o.headK ? o.headK * pr.H : (o.headR || 0.132) * k;
      this.headCY = J.head[1] - 0.03 * k + 1.12 * this.R;
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
    mark(name) { const t = this.M.triangleCount; this.parts = this.parts || {}; this.parts[name] = t - (this._last || 0); this._last = t; }
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
      const prof = [[this.waistR, this.spineY], [this.chestR, this.chestY], [this.chestR * 1.02, this.shoulderY - 0.07 * u], [this.shoulderR, this.shoulderY - 0.012 * u], [0.075 * u, this.neckY + 0.005 * u]];
      M.push().scale(1, 1, this.depthK);
      M.lathe(prof, 8, false, false, true, ringCols || null, 180 / 8);
      M.pop();
    }
    neck(c) {
      this.bone('neck'); this.M.color = c;
      this.M.segment([0, this.shoulderY - 0.03 * this.k, -0.012], [0, this.J.head[1] + 0.05 * this.k, -0.005], 0.052 * this.k * lerp(1, this.bulk, 0.7), 0.046 * this.k, 7);
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
    get headCenter() { return [0, this.headCY + 0.03 * this.R, -0.02 * this.R]; }
    /** cheekShade (< 1) darkens the face below the brow line a little: the brow plane catches more light than the cheeks. */
    headShape(chin = 1, cheekShade = 1) {
      const rings = 9, sides = 12, M = this.M;
      const narrow = clamp(0.2 - (chin - 1) * 0.3, 0.06, 0.4);
      const prof = [], cols = [];
      for (let i = 0; i <= rings; i++) {
        const phi = Math.PI * i / rings;
        let y = -Math.cos(phi), rad = Math.sin(phi);
        if (y < 0) { const q = -y, t = clamp01((q - JAW_FROM) / (1 - JAW_FROM)); rad *= 1 - narrow * t * t * (3 - 2 * t); y *= 1 + JAW_DROP * q * q; }
        prof.push([i === 0 || i === rings ? 0 : rad, y]);
        cols.push(P.shade(M.color, lerp(cheekShade, 1, sstep((y + 0.05) / 0.3))));
      }
      this.headProf = prof;
      M.push().translate(this.headCenter).scale(this.R, this.R, 0.96 * this.R).rotate(HEAD_TILT, 0, 0);
      M.lathe(prof, sides, true, false, false, cheekShade < 1 ? cols : null);
      M.pop();
    }
    insideHead(p) {
      const c = this.headCenter, R = this.R;
      const d = [(p[0] - c[0]) / R, (p[1] - c[1]) / R, (p[2] - c[2]) / (0.96 * R)];
      const a = -HEAD_TILT * D2R, ca = Math.cos(a), sa = Math.sin(a);
      const y = d[1] * ca - d[2] * sa, z = d[1] * sa + d[2] * ca, rho = Math.hypot(d[0], z);
      const pr = this.headProf;
      if (y <= pr[0][1] || y >= pr[pr.length - 1][1]) return false;
      for (let i = 0; i + 1 < pr.length; i++)
        if (y <= pr[i + 1][1]) return rho <= lerp(pr[i][0], pr[i + 1][0], (y - pr[i][1]) / (pr[i + 1][1] - pr[i][1]));
      return false;
    }
    /** The point on the front of the face at (x, y) head radii (y from the head centre line), lifted `lift` radii. */
    facePoint(x, y, lift = 0) {
      const R = this.R, px = x * R, py = this.headCY + y * R;
      let lo = this.headCenter[2], hi = lo + 1.4 * R;
      if (!this.insideHead([px, py, lo])) return [px, py, lo + lift * R];
      for (let i = 0; i < 20; i++) { const mid = (lo + hi) / 2; if (this.insideHead([px, py, mid])) lo = mid; else hi = mid; }
      return [px, py, lo + lift * R];
    }
    /** Small round ears (BipedKit.Head). */
    ears(c) {
      const M = this.M, R = this.R;
      this.bone('head'); M.color = c;
      for (const s of [1, -1]) M.sphere([s * 0.95 * R, this.headCY - 0.12 * R, -0.04 * R], [0.12 * R, 0.2 * R, 0.1 * R], 6, 2, true);
    }
    /** Hair cap over the cranium (BipedKit.HairCap): hairline φ at the forehead / over the ears / at the nape. */
    /**
     * Hair cap. lineFn(θ) overrides the hairline; topFn(θ) starts the shell lower (just under a headwrap, so its rows stay
     * short); np rows. Flat rows are chords: rows spanning more than ~30° of φ sink inside the head between their edges.
     */
    hairCap(c, front = 58, side = 100, back = 122, puff = 1.08, lineFn = null, topFn = null, np = 2) {
      const M = this.M, R = this.R;
      this.bone('head'); M.color = c;
      const C0 = [0, this.headCY + 0.05 * R, -0.03 * R], RR = [R * puff, R * puff, R * puff * 0.98];
      const line = lineFn || (th => { const a = Math.abs(th); if (a < 55) return lerp(front, front + 10, a / 55); if (a < 110) return lerp(front + 10, side, (a - 55) / 55); return lerp(side, back, (a - 110) / 70); });
      M.shell(C0, RR, -180, 180, 12, topFn || 0, line, np);
      this.hairC = C0; this.hairR = RR; this.hairLine = line;
    }
    /** A point on the hair ellipsoid (θ from +Z towards +X, φ from the top, degrees). */
    hairPoint(th, ph, k = 1) {
      const c = this.hairC, r = this.hairR, t = th * D2R, p = ph * D2R, s = Math.sin(p);
      return [c[0] + r[0] * k * s * Math.sin(t), c[1] + r[1] * k * Math.cos(p), c[2] + r[2] * k * s * Math.cos(t)];
    }
    /** Eyes: style 'round' | 'narrow' (phoenix eyes: narrow, outer corners up). */
    eyes(iris, brow, style = 'round', browTilt = 0, y = -0.1, spread = 0.36, size = 1, eyeTilt = 0, browK = 1) {
      const M = this.M, R = this.R, keepJ = M.jitter, keepE = M.emission;
      this.bone('head'); M.jitter = 0;
      for (const s of [1, -1]) {
        const p = this.facePoint(s * spread, y, 0);
        M.push().translate(V.add(p, [0, 0, -0.036 * R])).rotate(0, s * EYE_TURN, s * eyeTilt);
        // BipedKit.Eyes: a dark disc, the iris low in it, a bright catch-light high on the outer side
        const h = style === 'narrow' ? 0.12 : 0.2, wd = style === 'narrow' ? 0.17 : 0.13;
        M.color = P.shade(iris, 0.42);
        M.sphere([0, 0, 0], [wd * R * size, h * R * size, 0.065 * R], 7, 3);
        M.color = iris;
        M.sphere([0, -h * 0.35 * R * size, 0.012 * R], [wd * 0.75 * R * size, h * 0.45 * R * size, 0.06 * R], 6, 2);
        M.emission = 0.55; M.color = [1, 1, 0.97];
        M.sphere([s * 0.035 * R * size, h * 0.38 * R * size, 0.05 * R], 0.04 * R * size * (style === 'narrow' ? 0.8 : 1), 4, 2);
        M.emission = keepE;
        M.pop();
        if (brow) {
          const bp = this.facePoint(s * spread, y + (style === 'narrow' ? 0.22 : 0.27) * Math.max(1, size * 0.9), 0.01);
          M.color = brow;
          M.push().translate(bp).rotate(0, s * EYE_TURN, s * browTilt);
          M.box([0, 0, 0], [0.3 * R * browK, 0.075 * R * browK, 0.06 * R]);
          // a swept tail at the outer end (卧蚕眉 / bold brows)
          if (browK > 1.2) M.box([s * 0.19 * R * browK, 0.03 * R * browK, -0.01 * R], [0.12 * R * browK, 0.05 * R * browK, 0.05 * R]);
          M.pop();
        }
      }
      M.jitter = keepJ;
    }
    nose(c, size = 1, y = -0.3) { this.bone('head'); this.M.color = c; this.M.sphere(this.facePoint(0, y, 0.02 * size), V.mul([0.1, 0.085, 0.09], this.R * size), 5, 3); }
    smile(c, y = -0.56, width = 0.26) {
      const M = this.M, R = this.R; this.bone('head'); M.color = c;
      const Pt = t => this.facePoint(t * width * 0.5, y + 0.07 * t * t, 0.015);
      M.segment(Pt(-1), Pt(-0.35), 0.022 * R, 0.026 * R, 4);
      M.segment(Pt(-0.35), Pt(0.35), 0.026 * R, 0.026 * R, 4);
      M.segment(Pt(0.35), Pt(1), 0.026 * R, 0.022 * R, 4);
    }
    /** Hair cap / headwrap shell over the cranium; hairline φ (deg from the top) at front / side / back. */
    cap(c, front = 58, side = 100, back = 122, puff = 1.08) {
      const M = this.M, R = this.R;
      this.bone('head'); M.color = c;
      this.capC = [0, this.headCY + 0.05 * R, -0.03 * R];
      this.capR = [R * puff, R * puff, R * puff * 0.98];
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
    const kit = new Biped(fig, { seed: 23, bulk: 1.12, headK: 0.08 });
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
    const bands = [C.goldDark, C.gold, C.gold, C.goldLight, H('#7A5A30')];
    kit.torso(C.gold, C.goldDark, bands);
    kit.neck(P.mix(C.gySkinDark, C.gyHair, 0.55));
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
    M.panel([0, 0, 0], 36, 324, 7, [rp(0.09 * k, kit.neckY + 0.012 * k), rp(kit.shoulderR * 1.08, kit.shoulderY - 0.012 * k), rp(kit.chestR * 1.1, kit.chestY), rp(kit.waistR * 1.12, kit.spineY)], kit.depthK * 1.02, robe, C.robeLining, 0.012);
    kit.bone('spine');
    M.panel([0, 0, 0], 36, 324, 7, [rp(kit.waistR * 1.12, kit.spineY + 0.002), rp(kit.waistR * 1.14, kit.pelvisY + 0.03 * k)], kit.depthK * 1.02, robe, C.robeLining, 0.012);
    // trims along the opening (collar lapels): strips lying on the robe edge
    const edge = (th, r, y) => { const t = th * D2R; return [r * Math.sin(t), y, r * Math.cos(t) * kit.depthK * 1.02]; };
    M.color = C.robeTrim;
    for (const s of [1, -1]) {
      kit.bone('chest');
      const a = edge(s * 38, 0.105 * k, kit.neckY + 0.01 * k), b = edge(s * 38, kit.shoulderR * 1.1, kit.shoulderY - 0.02 * k), c = edge(s * 38, kit.chestR * 1.12, kit.chestY), d = edge(s * 38, kit.waistR * 1.14, kit.spineY);
      const out = [s * 0.6, 0.2, 1];
      M.strip(a, b, 0.036 * k, 0.04 * k, out, 0.016);
      M.strip(b, c, 0.04 * k, 0.04 * k, out, 0.016);
      M.strip(c, d, 0.04 * k, 0.036 * k, out, 0.016);
      kit.bone('spine');
      M.strip(d, edge(s * 38, kit.waistR * 1.16, kit.pelvisY + 0.03 * k), 0.036 * k, 0.036 * k, out, 0.016);
    }
    // collar band at the back of the neck
    kit.bone('chest'); M.color = C.robeTrim;
    M.push().translate(0, 0, -0.01).scale(1, 1, 0.9);
    M.lathe([[0.1 * k, kit.neckY - 0.03 * k], [0.092 * k, kit.neckY + 0.035 * k]], 8, false, false, false, null, 22.5);
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
      const r = kit.armR * 1.9, th0 = -180, th1 = 180;
      M.push().translate(V.add(sh, [s * 0.03 * k, 0.03 * k, -0.004]));
      for (let i = 2; i >= 0; i--) {
        const kk = 1 - i * 0.08, n = i === 0 ? 10 : 8;
        M.push().translate(s * i * 0.018 * k, -i * 0.06 * k, 0).rotate(0, 0, -s * (20 + i * 10));
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
    kit.headShape(1.05, 0.9);
    kit.ears(C.gySkin);
    kit.eyes(C.gyIris, C.gyBrow, 'narrow', 18, -0.1, 0.37, 1.25, 10, 1.45);
    kit.nose(C.gySkinDark, 1.0, -0.32);
    kit.mark('face');
    // ---- hair: near-black hair under the headwrap, showing at the temples, over the ears and at the nape; flat sideburns
    // the hairline drops right behind the ears, so the whole back of the head down to the nape is hair (no red skin)
    // (three short rows from just under the wrap: long flat rows sank inside the skull and the back showed red)
    const wrapLine = th => { const a = Math.abs(th); if (a < 55) return lerp(38, 48, a / 55); if (a < 110) return lerp(48, 80, (a - 55) / 55); return lerp(80, 104, (a - 110) / 70); };
    kit.hairCap(C.gyHair, 44, 104, 168, 1.08, th => { const a = Math.abs(th); if (a < 55) return lerp(44, 54, a / 55); if (a < 100) return lerp(54, 104, (a - 55) / 45); if (a < 128) return lerp(104, 166, sstep((a - 100) / 28)); return 168; }, th => wrapLine(th) - 12, 3);
    M.color = C.gyHair;
    for (const s of [1, -1]) M.strip(kit.facePoint(s * 0.82, 0.14, -0.01), kit.facePoint(s * 0.76, -0.26, 0.0), 0.11 * R, 0.025 * R, [s * 0.85, 0, 0.55], 0.016);
    kit.mark('hair');
    // ---- the beard (美髯): a short chin piece under the jaw line (the cheeks stay clear), then three flowing locks — the
    // centre one to the sternum, two shorter ones splayed ±12° — each a tapered five-sided loft curling forward at the tip,
    // lighter on its upper faces and darker towards the tip; the upper third rides the head, the rest the chest (so it
    // never slides through the chest plate when he turns his head). Moustache drooping either side of the mouth.
    kit.bone('head'); M.color = C.beard;
    M.shell([0, kit.headCY - 0.3 * R, 0.0], [1.02 * R, 0.86 * R, 1.0 * R], -64, 64, 8, 112, 160, 2);
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
    const rootC = [0, kit.headCY - 0.92 * R, 0.6 * R], tipC = [0, tipY, chestFront];
    const dir = V.sub(tipC, rootC), len = V.len(dir);
    lockOf(rootC, tipC, 0.27 * R, 1);
    for (const s of [1, -1]) {
      const root = [s * 0.3 * R, kit.headCY - 0.84 * R, 0.56 * R];
      const a = s * 12 * D2R, d = V.mul(V.norm(dir), len * 0.7);
      const tip = V.add(root, [d[0] * Math.cos(a) - d[1] * Math.sin(a), d[0] * Math.sin(a) + d[1] * Math.cos(a), d[2]]);
      tip[2] = Math.max(tip[2], chestFront + 0.01);
      lockOf(root, tip, 0.23 * R, 3 + s);
    }
    kit.bone('head'); M.color = C.beardTop;
    for (const s of [1, -1]) M.curve(kit.facePoint(s * 0.05, -0.43, 0.05), kit.facePoint(s * 0.42, -0.5, 0.06), V.add(kit.facePoint(s * 0.5, -0.78, 0.04), [0, -0.05 * k, 0]), 0.06 * R, 0.02 * R, 2, 4);
    kit.mark('beard');
    // ---- green headwrap (巾帻) worn high (the forehead shows), a darker band at its edge, a knot with two ribbons behind,
    // a jade ornament on the brow
    kit.cap(C.wrap, 38, 80, 104, 1.1);
    M.color = C.wrapBand;
    M.shell(kit.capC, V.mul(kit.capR, 1.03), -180, 180, 12, th => kit.capLine(th) - 14, th => kit.capLine(th) + 1, 1);
    // the knot (a soft bun in the wrap) and its two ribbons hanging behind (hair1: sways)
    kit.bone('head'); M.color = C.wrap;
    const bun = kit.capPoint(180, 40, 1.02);
    M.sphere(bun, [0.42 * R, 0.4 * R, 0.4 * R], 6, 3, false);
    M.color = C.wrapBand;
    M.band(V.add(bun, [0, -0.08 * R, 0.04 * R]), 0.36 * R, 0.37 * R, 0, 0.12 * R, 7);
    // two short ribbons: tapered (0.07k → 0.02k), flaring out and twisting a little, so they read as cloth, not poles
    kit.bone('hair1'); M.color = C.wrap;
    const rib0 = M.vertexCount;
    for (const s of [1, -1]) {
      const a = V.add(bun, [s * 0.2 * R, -0.22 * R, -0.25 * R]), m = V.add(a, [s * 0.05 * k, -0.07 * k, -0.035 * k]), t = V.add(a, [s * 0.11 * k, -0.14 * k, -0.03 * k]);
      M.strip(a, m, 0.07 * k, 0.045 * k, [s * 0.3, 0, -1], 0.01);
      M.strip(m, t, 0.045 * k, 0.02 * k, [s * 0.95, 0.1, -0.45], 0.01);
    }
    M.windBy(rib0, p => 0.5 * clamp01((bun[1] - 0.22 * R - p[1]) / (0.14 * k)));
    kit.bone('head');
    M.color = C.goldLight;
    const brow = kit.capPoint(0, kit.capLine(0) - 6, 1.06);
    M.sphere(brow, [0.16 * R, 0.13 * R, 0.08 * R], 6, 3, false);
    M.color = C.jade; M.emission = 0.15;
    M.sphere(V.add(brow, [0, 0, 0.05 * R]), [0.1 * R, 0.08 * R, 0.05 * R], 5, 2, false);
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
    const kit = new Biped(fig, { seed: 31 + (o.seed || 0), bulk: 1.0, headK: 0.08 });
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
    kit.neck(P.shade(C.soSkin, 0.9));
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
    kit.bone('chest'); M.color = tunic;
    M.push().translate(0, 0, -0.01).scale(1, 1, 0.92);
    M.lathe([[0.096 * k, kit.neckY - 0.03 * k], [0.088 * k, kit.neckY + 0.03 * k]], 8, false, false, false, null, 22.5);
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
    kit.headShape(0.95, 0.94);
    kit.ears(C.soSkin);
    kit.eyes(H('#3A2C28'), hairTone(C.soHair), 'round', -6, -0.1, 0.36, 1.3, 0, 0.95);
    kit.nose(P.shade(C.soSkin, 0.88), 1, -0.3);
    kit.smile(H('#7A4034'), -0.56, 0.22);
    kit.mark('face');
    // black hair (#262022) — Lanternvale's hair-tone lift turned it grey-brown here; the top-knot a shade lighter
    const hair = C.soHairCap;
    kit.hairCap(hair, 56, 100, 124, 1.07);
    M.color = C.soKnot;
    const knotAt = kit.hairPoint(180, 26, 1.0);
    M.sphere(V.add(knotAt, [0, 0.1 * R, 0.02 * R]), [0.34 * R, 0.3 * R, 0.32 * R], 7, 3, true);
    M.color = C.soWrap;
    M.band(V.add(knotAt, [0, -0.02 * R, 0.02 * R]), 0.3 * R, 0.32 * R, 0, 0.12 * R, 7);
    // the 赤帻: a red band round the head at the hairline (the hair shows above it), knotted behind
    M.shell(kit.hairC, V.mul(kit.hairR, 1.045), -180, 180, 12, th => kit.hairLine(th) - 20, th => kit.hairLine(th) - 3, 1);
    const back = kit.hairPoint(180, kit.hairLine(180) - 12, 1.06);
    M.sphere(back, [0.16 * R, 0.13 * R, 0.1 * R], 5, 2, false);
    kit.bone('hair1');
    const tails = M.vertexCount;
    for (const s of [1, -1]) {
      const a = V.add(back, [s * 0.06 * R, -0.08 * R, -0.04 * R]);
      M.curvedStrip(a, V.add(a, [s * 0.05 * k, -0.06 * k, -0.04 * k]), V.add(a, [s * 0.08 * k, -0.16 * k, -0.03 * k]), 0.06 * k, 0.045 * k, [s * 0.3, 0, -1], 0.01, 2);
    }
    M.windBy(tails, p => 0.5 * clamp01((back[1] - p[1]) / (0.16 * k)));
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

  function makeRig(fig, kit, o) {
    const geo = kit.M.toGeometry({ skinned: true });
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
  const STONES = [H('#8E877C'), H('#A39C8E'), H('#7E776E')];
  /** A course of stone blocks along local X from x0 to x1, front face at z = z0 (+Z), height h (blocks skip bottom / back). */
  function plinthRow(M, x0, x1, z0, h) {
    let x = x0;
    while (x < x1 - 0.08) {
      let w = 0.75 + M.random01() * 0.7;
      if (x1 - (x + w) < 0.5) w = x1 - x;
      const hh = h * (0.9 + M.random01() * 0.14), d = 0.1 + M.random01() * 0.06;
      M.color = P.shade(STONES[(M.random01() * 3) | 0], 0.94 + M.random01() * 0.12);
      M.box([x + w / 2, hh / 2, z0 + d / 2 - 0.03], [w - 0.035, hh, d + 0.06], null, 'tfrl');
      x += w;
    }
  }
  /**
   * Merlons (垛口) along local X on a parapet top at height y, front at z: tone ±0.08 each; on every 4th a cushion of moss
   * — `lumps` tiny soft lumps (smooth-shaded, a moss ramp from a cool underside to a sunlit top), not a flat patch.
   */
  const MOSS = LP.foliage(1, H('#7D9A55'), 6);
  function merlons(M, xsList, y, z, size, base, mossEvery = 4, lumps = 2) {
    xsList.forEach((x, i) => {
      M.color = P.shade(base, 1 + (M.random01() * 2 - 1) * 0.08);
      M.box([x, y + size[1] / 2, z], size, P.shade(PAL.stoneLight, 0.96 + M.random01() * 0.08), 'tfkrl');
      if (mossEvery && i % mossEvery === 1) {
        const top = y + size[1], ramp = LP.canopyRamp(MOSS.top, MOSS.side, MOSS.bottom, top - 0.04, top + 0.16);
        const mx = x + (M.random01() - 0.5) * size[0] * 0.3, mz = z + size[2] * 0.12;
        M.softLump([mx, top + 0.03, mz], [size[0] * 0.24, 0.1, size[2] * 0.3], 0, i * 37, ramp, 0.16);
        if (lumps > 1) M.softLump([mx + size[0] * 0.2, top + 0.02, mz - size[2] * 0.12], [size[0] * 0.14, 0.07, size[2] * 0.18], 0, i * 53 + 20, ramp, 0.16);
      }
    });
  }
  /** A small stud on a door (门钉): a gold square standing just off the leaf (two triangles), facing +Z at p. */
  function stud(M, p, r) { M.quad([p[0] - r, p[1] - r, p[2]], [p[0] + r, p[1] - r, p[2]], [p[0] + r, p[1] + r, p[2]], [p[0] - r, p[1] + r, p[2]], [0, 0, 1]); }

  // ===================================================== gate tower ==
  // 城门楼: battered rammed-earth gate base on a plinth of stone blocks, a brick-framed arch with a dark-wood name plaque,
  // two studded vermilion door leaves in a dark frame on a stone threshold, parapets with merlons (moss here and there); a
  // timber hall (vermilion columns, cream walls, lattice windows, painted brackets, railing, plaque) under a slate-blue
  // hip-and-gable roof with upturned eaves; ribbed red lanterns and faction banners. Front faces +Z. ~13 m wide, ~11 m tall.
  function buildGateTower(o) {
    o = o || {};
    const lit = !!o.lit;
    const M = new LP.MeshBuilder(101);
    M.jitter = 0.06; M.jitterAll = true; M.aoStrength = 0.35; M.aoHeight = 1.2;
    const decals = [];
    const parts = {}; let lastT = 0; const mark = n => { parts[n] = M.triangleCount - lastT; lastT = M.triangleCount; };
    const HB = 4.8, bat = 0.07, XB = 6.5, ZB = 3.4, PL = 0.6;
    const xs = y => XB - bat * y, zs = y => ZB - bat * y;
    const aw = 1.55, ys = 2.25, atop = ys + aw;
    const earth = PAL.rammed, earthDark = PAL.rammedDark, stone = PAL.stone, brick = PAL.brick, brickDark = PAL.brickDark;
    const archYs = []; for (let i = 1; i <= 5; i++) archYs.push(ys + aw * Math.sin(i / 5 * Math.PI / 2));
    const courseCol = (i, y) => (y < PL - 1e-3 ? stone : P.shade(i % 2 ? earth : P.mix(earth, earthDark, 0.45), y < 1.19 ? 0.88 : 1));
    const ow = y => (y <= ys ? aw : y >= atop ? 0 : Math.sqrt(Math.max(0, aw * aw - (y - ys) * (y - ys))));
    // front (on the plinth) and back walls with the arch opening, rammed-earth courses
    for (const f of [1, -1]) {
      const allY = (f > 0 ? [PL, 1.2, 1.8, ys] : [0, PL, 1.2, 1.8, ys]).concat(archYs).concat([4.3, HB]);
      for (let i = 0; i < allY.length - 1; i++) {
        const y0 = allY[i], y1 = allY[i + 1], col = courseCol(i + (f > 0 ? 1 : 0), y0);
        const z0 = f * zs(y0), z1 = f * zs(y1);
        const out = [0, bat, f];
        for (const s of [-1, 1]) {
          const a = [s * xs(y0), y0, z0], b = [s * xs(y1), y1, z1], c = [s * ow(y1), y1, z1], d = [s * ow(y0), y0, z0];
          if (ow(y0) <= 0 && ow(y1) <= 0) { if (s > 0) M.quad([-xs(y0), y0, z0], [xs(y0), y0, z0], [xs(y1), y1, z1], [-xs(y1), y1, z1], out, P.shade(col, 1 + (M.random01() - 0.5) * 0.1)); continue; }
          M.quad(a, b, c, d, out, P.shade(col, 1 + (M.random01() - 0.5) * 0.1));
        }
      }
    }
    const sideY = [PL, 1.2, 1.8, ys, 3.05, 3.8, 4.3, HB];
    for (const s of [-1, 1]) for (let i = 0; i < sideY.length - 1; i++) {
      const y0 = sideY[i], y1 = sideY[i + 1], col = courseCol(i + 1, y0);
      M.quad([s * xs(y0), y0, -zs(y0)], [s * xs(y0), y0, zs(y0)], [s * xs(y1), y1, zs(y1)], [s * xs(y1), y1, -zs(y1)], [s, bat, 0], P.shade(col, 1 + (M.random01() - 0.5) * 0.1));
    }
    // top platform: dark grout under paving slabs where the walk shows round the hall podium (front strip, side bands)
    M.quad([-xs(HB), HB, -zs(HB)], [xs(HB), HB, -zs(HB)], [xs(HB), HB, zs(HB)], [-xs(HB), HB, zs(HB)], [0, 1, 0], P.shade(PAL.stone, 0.78));
    {
      const pave = (x0, x1, z0, z1, nx, nz) => {
        for (let i = 0; i < nx; i++) for (let j = 0; j < nz; j++) {
          const a = lerp(x0, x1, i / nx) + 0.02, b = lerp(x0, x1, (i + 1) / nx) - 0.02, c = lerp(z0, z1, j / nz) + 0.02, d = lerp(z0, z1, (j + 1) / nz) - 0.02;
          M.quad([a, HB + 0.012, c], [b, HB + 0.012, c], [b, HB + 0.012, d], [a, HB + 0.012, d], [0, 1, 0], P.shade((i + j) % 2 ? PAL.stoneLight : PAL.stone, 0.9 + M.random01() * 0.08));
        }
      };
      const xIn = xs(HB) - 0.44, zIn = zs(HB) - 0.44;
      pave(-xIn, xIn, 2.22, zIn, 9, 1);
      for (const s of [-1, 1]) pave(s > 0 ? 4.72 : -xIn, s > 0 ? xIn : -4.72, -zIn, 2.22, 1, 5);
    }
    mark('walls');
    // the stone plinth: front (either side of the arch) and both ends
    M.aoStrength = 0.2;
    for (const s of [-1, 1]) { M.push(); if (s < 0) M.scale(-1, 1, 1); plinthRow(M, aw + 0.42, XB + 0.02, ZB, PL); M.pop(); }
    for (const s of [-1, 1]) { M.push().translate(s * XB, 0, 0).rotate(0, s * 90, 0); plinthRow(M, -ZB - 0.16, ZB + 0.16, 0, PL); M.pop(); }
    M.aoStrength = 0.35;
    // putlog holes on the front
    M.color = H('#6E5A44');
    for (const yy of [1.84, 3.09]) for (const hx of [2.7, 3.9, 5.1]) for (const s of [-1, 1]) {
      const x = s * hx, z0 = zs(yy) + 0.006;
      M.quad([x - 0.07, yy, z0], [x + 0.07, yy, z0], [x + 0.07, yy + 0.11, zs(yy + 0.11) + 0.006], [x - 0.07, yy + 0.11, zs(yy + 0.11) + 0.006], [0, bat, 1]);
    }
    mark('plinth');
    // tunnel: inward-facing side walls and vault (dark, warm), doors set back inside
    const tunnel = H('#6A5646'), tunnelDark = H('#4A3C34');
    M.aoStrength = 0;
    for (const s of [-1, 1]) M.quad([s * aw, 0, -zs(0)], [s * aw, 0, zs(0)], [s * aw, ys, zs(ys)], [s * aw, ys, -zs(ys)], [-s, 0, 0], tunnel);
    for (let i = 0; i < 6; i++) {
      const a0 = i / 6 * Math.PI, a1 = (i + 1) / 6 * Math.PI;
      const p = (a, z) => [Math.cos(a) * aw, ys + Math.sin(a) * aw, z];
      const zf = zs(ys + Math.sin(a0) * aw);
      M.quad(p(a0, -zf), p(a0, zf), p(a1, zf), p(a1, -zf), [-Math.cos((a0 + a1) / 2), -Math.sin((a0 + a1) / 2), 0], P.mix(tunnel, tunnelDark, i % 2 ? 0.3 : 0.6));
    }
    // the gate: a dark timber frame round the opening, two vermilion leaves meeting at a dark seam, 5 × 5 gold studs
    // (门钉) and a gold boss on each leaf, a stone threshold in front
    const doorZ = zs(0) - 0.9, red = H('#A83E2E'), frameC = H('#3E2A22');
    M.color = frameC;
    for (const s of [-1, 1]) M.box([s * (aw - 0.07), ys / 2, doorZ + 0.1], [0.14, ys, 0.16], null, 'fkrl');
    for (let i = 0; i < 5; i++) {
      const a0 = i / 5 * Math.PI, a1 = (i + 1) / 5 * Math.PI, rr = aw - 0.07;
      M.beam([Math.cos(a0) * rr, ys + Math.sin(a0) * rr, doorZ + 0.1], [Math.cos(a1) * rr, ys + Math.sin(a1) * rr, doorZ + 0.1], 0.16, 0.14, [0, 0, 1]);
    }
    M.color = H('#241A16');
    M.quad([-0.03, 0, doorZ - 0.02], [0.03, 0, doorZ - 0.02], [0.03, atop - 0.1, doorZ - 0.02], [-0.03, atop - 0.1, doorZ - 0.02], [0, 0, 1]);
    for (const s of [-1, 1]) {
      const pts = [[s * 0.02, 0], [s * (aw - 0.12), 0], [s * (aw - 0.12), ys]];
      for (let i = 1; i <= 4; i++) { const a = i / 4 * Math.PI / 2; pts.push([s * Math.max(0.02, Math.cos(a) * (aw - 0.12)), ys + Math.sin(a) * (aw - 0.12)]); }
      if (s < 0) pts.reverse();
      M.color = red;
      M.push().translate(0, 0, doorZ); M.flat(pts, 0.1); M.pop();
      M.color = P.shade(red, 0.86);
      M.box([s * (aw * 0.5), ys + 0.42, doorZ + 0.055], [aw - 0.3, 0.07, 0.02], null, 'tf');
      M.box([s * (aw * 0.5), 0.2, doorZ + 0.055], [aw - 0.3, 0.07, 0.02], null, 'tf');
      M.color = C.gold;
      for (let r = 0; r < 5; r++) for (let c = 0; c < 5; c++) stud(M, [s * lerp(0.26, aw - 0.38, c / 4), lerp(0.42, ys + 0.2, r / 4), doorZ + 0.058], 0.036);
      M.sphere([s * 0.16, 1.32, doorZ + 0.07], [0.07, 0.07, 0.035], 6, 2, false);
    }
    M.color = P.shade(PAL.stone, 0.92);
    M.box([0, 0.08, doorZ + 0.32], [aw * 2, 0.16, 0.36], null, 'tfkrl');
    M.color = P.shade(PAL.stone, 0.82);
    M.box([0, 0.03, (doorZ + zs(0)) / 2 + 0.25], [aw * 2, 0.06, zs(0) - doorZ - 0.3], null, 't');
    M.aoStrength = 0.35;
    mark('tunnel+doors');
    // brick voussoirs around the arch (front) with a keystone, brick jambs
    {
      const N = 7;
      for (let i = 0; i < N; i++) {
        const a = (i + 0.5) / N * Math.PI, r = aw + 0.2;
        const y = ys + Math.sin(a) * r, z = zs(y) + 0.03;
        const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, 0, a - Math.PI / 2));
        M.color = i === 3 ? P.mix(brick, PAL.stoneLight, 0.5) : i % 2 ? brick : brickDark;
        M.push().translate(Math.cos(a) * r, y, z).rotateQ(q); M.box([0, i === 3 ? -0.03 : 0, 0], [0.4, i === 3 ? 0.36 : 0.42, 0.12], null, 'tbfrl'); M.pop();
      }
      for (const s of [-1, 1]) { M.color = brickDark; M.box([s * (aw + 0.2), ys * 0.5, zs(ys * 0.5) + 0.02], [0.4, ys, 0.08], null, 'tfrl'); }
    }
    mark('voussoirs');
    // the name plaque over the arch: a dark-wood board in a gold border; the city's name is a painted decal on the board
    // (gold 楷书 on the wood, Lanternvale's signs), added by Pilot.gateTower
    {
      const py = 4.37, pz = zs(py) + 0.06;
      M.color = C.gold; M.box([0, py, pz], [1.46, 0.42, 0.06], null, 'tfrl');
      M.color = H('#3A2A24'); M.box([0, py, pz + 0.035], [1.28, 0.3, 0.03], null, 'tf');
      decals.push({ text: o.name || '荆州', board: '#3A2A24', ink: '#F2C45C', w: 1.26, h: 0.28, at: [0, py, pz + 0.054], tilt: 0 });
    }
    mark('plaque');
    // a stone cornice under the parapet
    M.color = PAL.stoneDark;
    for (const f of [1, -1]) M.box([0, HB - 0.12, f * (zs(HB) + 0.02)], [xs(HB) * 2 + 0.2, 0.24, 0.16], null, f > 0 ? 'tbf' : 'tbk');
    for (const s of [1, -1]) M.box([s * (xs(HB) + 0.02), HB - 0.12, 0], [0.16, 0.24, zs(HB) * 2 + 0.2], null, s > 0 ? 'tbr' : 'tbl');
    mark('cornice');
    // parapets (女墙) with merlons on the front, lower walls on the sides and back
    const pz = zs(HB) - 0.22, px = xs(HB) - 0.22;
    const pc = P.mix(brick, earth, 0.3);
    M.color = pc;
    M.box([0, HB + 0.3, pz], [px * 2 + 0.44, 0.6, 0.44], null, 'tfkrl');
    M.box([0, HB + 0.25, -pz], [px * 2 + 0.44, 0.5, 0.44], null, 'tfkrl');
    for (const s of [-1, 1]) M.box([s * px, HB + 0.3, 0], [0.44, 0.6, pz * 2], null, 'tkrl');
    const mx = [];
    for (let i = 0; i < 9; i++) { const x = lerp(-px + 0.35, px - 0.35, i / 8); if (Math.abs(x) >= 0.4) mx.push(x); }
    merlons(M, mx, HB + 0.6, pz, [0.82, 0.44, 0.46], pc, 4, 1);
    for (const s of [-1, 1]) { M.push().translate(s * px, 0, 0).rotate(0, 90, 0); merlons(M, [-pz + 0.6, 0, pz - 0.6], HB + 0.6, 0, [0.8, 0.44, 0.46], pc, 0); M.pop(); }
    mark('parapets');
    // ---- the hall: podium, columns, walls, windows, beams, brackets, railing
    const HY = HB + 0.32, HX = 4.2, HZ = 2.1, hz0 = -0.35, colH = 2.85;
    M.color = PAL.stoneLight;
    M.boxOn([0, HB, hz0], [HX * 2 + 1.1, 0.32, HZ * 2 + 1.0], PAL.stoneLight);
    const colX = [-4.2, -2.52, -0.84, 0.84, 2.52, 4.2];
    const vermilion = PAL.hanRed, cream = PAL.cream;
    M.aoStrength = 0.18; M.aoHeight = HY + 0.6;
    for (const x of colX) for (const zz of [HZ, -HZ]) {
      if (zz < 0 && Math.abs(x) < 4) continue;
      M.color = PAL.stone;
      M.box([x, HY + 0.06, hz0 + zz], [0.46, 0.12, 0.46], null, 'tfkrl');
      M.color = vermilion;
      M.cylinder([x, HY + 0.12, hz0 + zz], 0.165, 0.15, colH - 0.12, 6, false, false, false);
    }
    M.aoStrength = 0;
    mark('podium+columns');
    // walls set behind the front columns; doors / lattice windows / plain bays (faces that never show are skipped)
    const wz = hz0 + HZ - 0.45, wallTop = HY + colH;
    M.color = cream;
    M.box([0, (HY + wallTop) / 2, hz0 - HZ + 0.15], [HX * 2, colH, 0.3], null, 'tfk');
    for (const s of [-1, 1]) M.box([s * (HX - 0.15), (HY + wallTop) / 2, hz0 - 0.15], [0.3, colH, HZ * 2 - 0.3], null, s > 0 ? 'tr' : 'tl');
    const paper = lit ? P.mix(PAL.paper, PAL.glow, 0.6) : H('#E9D7AE'), lattice = H('#5A3426');
    for (let b = 0; b < 5; b++) {
      const x0 = colX[b], x1 = colX[b + 1], xm = (x0 + x1) / 2, bw = x1 - x0;
      M.color = cream;
      if (b !== 2) M.box([xm, HY + 0.4, wz], [bw, 0.8, 0.2], null, 'tf');            // dado
      M.box([xm, wallTop - 0.22, wz], [bw, 0.44, 0.2], null, 'fb');                    // over-panel
      const py0 = HY + (b === 2 ? 0.0 : 0.8), py1 = wallTop - 0.44;
      M.color = paper; M.emission = lit ? 0.92 : 0;
      M.box([xm, (py0 + py1) / 2, wz - 0.02], [bw - 0.1, py1 - py0, 0.1], null, 'f');
      M.emission = 0; M.color = lattice;
      for (const xx of [-0.33, 0.33]) M.box([xm + xx * (bw / 2 - 0.08), (py0 + py1) / 2, wz + 0.05], [0.07, py1 - py0, 0.06], null, 'frl');
      for (const r of [0.36, 0.7]) M.box([xm, lerp(py0, py1, r), wz + 0.05], [bw - 0.1, 0.06, 0.06], null, 'ftb');
    }
    mark('hallwalls');
    // architrave and painted frieze, bracket sets (斗拱) on the columns and between them
    M.color = vermilion;
    M.box([0, wallTop + 0.13, hz0 + HZ], [HX * 2 + 0.5, 0.26, 0.3], null, 'fbrl');
    M.box([0, wallTop + 0.13, hz0 - HZ], [HX * 2 + 0.5, 0.26, 0.3], null, 'kbrl');
    for (const s of [-1, 1]) M.box([s * HX, wallTop + 0.13, hz0], [0.3, 0.26, HZ * 2 + 0.3], null, s > 0 ? 'rb' : 'lb');
    M.color = PAL.teal;
    M.box([0, wallTop + 0.36, hz0 + HZ + 0.02], [HX * 2 + 0.6, 0.2, 0.3], null, 'fbrl');
    M.box([0, wallTop + 0.36, hz0 - HZ - 0.02], [HX * 2 + 0.6, 0.2, 0.3], null, 'kbrl');
    for (const s of [-1, 1]) M.box([s * (HX + 0.02), wallTop + 0.36, hz0], [0.3, 0.2, HZ * 2 + 0.6], null, s > 0 ? 'rb' : 'lb');
    for (let i = 0; i < 9; i++) {
      const x = lerp(-HX, HX, i / 8);
      M.color = i % 2 ? C.gold : H('#4E8C86');
      M.box([x, wallTop + 0.56, hz0 + HZ + 0.14], [0.34, 0.28, 0.46], null, 'fbrl');
    }
    mark('beams+brackets');
    // railing on the podium front and sides
    const rz = hz0 + HZ + 0.42;
    M.color = vermilion;
    for (let i = 0; i <= 6; i++) { const x = lerp(-HX - 0.4, HX + 0.4, i / 6); M.box([x, HY + 0.32, rz], [0.12, 0.66, 0.12], null, 'tfkrl'); }
    M.box([0, HY + 0.62, rz], [HX * 2 + 0.95, 0.08, 0.12], null, 'tfkb');
    M.box([0, HY + 0.2, rz], [HX * 2 + 0.95, 0.06, 0.08], null, 'tfb');
    for (const s of [-1, 1]) { M.box([s * (HX + 0.4), HY + 0.62, hz0 + 0.3], [0.12, 0.08, HZ * 2 + 0.3], null, 'tbrl'); M.box([s * (HX + 0.4), HY + 0.32, hz0 - HZ + 0.5], [0.11, 0.64, 0.11], null, 'tfkrl'); }
    mark('railing');
    // plaque (匾额) on the hall: hung under the eave in front of the middle bracket set and leaning out, a navy board in a
    // gold frame (its name a painted decal), below the roof — it never pokes through the lower roof
    {
      const pc = [0, wallTop + 0.27, hz0 + HZ + 0.45], tilt = 12;
      M.push().translate(pc).rotate(tilt, 0, 0);
      M.color = C.gold; M.box([0, 0, 0], [1.34, 0.5, 0.06], null, 'tfrl');
      M.color = H('#2E4466'); M.box([0, 0, 0.035], [1.16, 0.36, 0.03], null, 'tf');
      M.pop();
      decals.push({ text: o.hallName || '宾阳楼', board: '#2E4466', ink: '#F2C45C', w: 1.14, h: 0.34, at: pc, local: [0, 0, 0.054], tilt });
    }
    mark('hallplaque');
    // ---- roof: hip-and-gable (歇山), Lanternvale's CourseRoof in Han dress. Every slope is a gently concave surface laid
    // in overlapping courses (alternate tones, a dark step line under each course, per-face jitter); raised 筒瓦 tile ribs
    // run down the visible slopes (a lit and a shaded facet each, fanning out towards the upturned corners like 翼角
    // tiles) and end in a row of round 瓦当 eave-tile ends over the vermilion fascia; a two-step main ridge with curled
    // 鸱吻 ends and a gold bird (Han pottery towers carry one), hip ridges that follow the curve to upturned tips with
    // a small ridge beast. Dusty slate blue (Lanternvale's town-hall / river-house roofs).
    const yE = wallTop + 0.62, We = HX + 1.45, De = HZ + 1.35, yB = yE + 1.25, Lb = HX - 0.3, Db = HZ - 0.55, yR = yB + 1.45, up = 0.55;
    const tile = H('#808DA6'), tileAlt = H('#73809A'), ribC = H('#8794AD'), endC = H('#4F5B72'), ridgeC = H('#56627A'), soffit = H('#4F6E6A');
    const SAG = 0.16, STEP = 0.045, RIB_H = 0.1, RIB_W = 0.075;
    const lift = c => up * Math.pow(c, 3);
    const eave = (u, side) => {        // a point on the eave line of a slope (u 0..1 along it)
      const c = Math.abs(u * 2 - 1);
      if (side === 'f' || side === 'b') { const f = side === 'f' ? 1 : -1; return [lerp(-We, We, u) * (1 + 0.04 * Math.pow(c, 3)), yE + lift(c), f * De * (1 + 0.06 * Math.pow(c, 3)) + hz0]; }
      const f = side === 'r' ? 1 : -1; return [f * We * (1 + 0.04 * Math.pow(c, 3)), yE + lift(c), lerp(-De, De, u) * (1 + 0.06 * Math.pow(c, 3)) + hz0];
    };
    const brk = (u, side) => {
      if (side === 'f' || side === 'b') return [lerp(-Lb, Lb, u), yB, (side === 'f' ? Db : -Db) + hz0];
      return [(side === 'r' ? Lb : -Lb), yB, lerp(-Db, Db, u) + hz0];
    };
    const OUT = { f: [0, 1, 1], b: [0, 1, -1], r: [1, 1, 0], l: [-1, 1, 0] };
    // a point on a lower slope: u along the eave, v up the slope (0 the eave … 1 the break line); concave (举折)
    const lowerS = side => (u, v) => V.add(V.lerp(eave(u, side), brk(u, side), v), [0, -SAG * 4 * v * (1 - v), 0]);
    /**
     * One tiled slope S(u, v): o.cols base columns × o.courses (v breaks) of course quads, each course's foot lifted
     * STEP over the one below with a dark step face; o.ribs (or o.ribU) tile ribs sampled at o.ribV; o.ends: 瓦当 row;
     * o.fascia / o.soffit: the eave board and the painted underside (straight, under the sag).
     */
    const tiledSlope = (S, o) => {
      const out = o.out;
      const Nrm = (u, v) => {
        const e = 0.01, du = V.sub(S(u + e, v), S(u - e, v)), dv = V.sub(S(u, v + e), S(u, v - e));
        let n = V.norm(V.cross(du, dv)); if (V.dot(n, out) < 0) n = V.neg(n); return n;
      };
      const cs = o.courses;
      for (let k = 0; k < cs.length - 1; k++) {
        const v0 = cs[k], v1 = cs[k + 1], base = P.shade(k % 2 ? tileAlt : tile, 1 + 0.025 * (k - 1));
        for (let j = 0; j < o.cols; j++) {
          // each course's foot is lifted STEP over the course below (less at the slope's ends, where two slopes meet
          // under a hip ridge, so their lifted edges never open a gap there)
          const u0 = j / o.cols, u1 = (j + 1) / o.cols, lift = u => (k ? STEP * (u <= 0 || u >= 1 ? 0.15 : 1) : 0);
          const a0 = S(u0, v0), a1 = S(u1, v0), a = V.madd(a0, Nrm(u0, v0), lift(u0)), b = V.madd(a1, Nrm(u1, v0), lift(u1));
          M.quad(a, b, S(u1, v1), S(u0, v1), out, base);
          if (k) M.quad(a0, a1, b, a, V.sub(a0, S(u0, v0 + 0.05)), P.shade(base, 0.6));
        }
      }
      const ribU = o.ribU || (o.ribs ? Array.from({ length: o.ribs }, (_, i) => (i + 0.5) / o.ribs) : []);
      for (const u of ribU) {
        const st = o.ribV, T = v => V.norm(V.sub(S(u + 0.01, v), S(u - 0.01, v)));
        const pts = st.map(v => { const p = S(u, v), n = Nrm(u, v), t = T(v); return [V.madd(V.madd(p, t, -RIB_W), n, STEP * 0.45), V.madd(p, n, RIB_H), V.madd(V.madd(p, t, RIB_W), n, STEP * 0.45)]; });
        for (let q = 0; q < st.length - 1; q++) {
          const A = pts[q], B = pts[q + 1], vm = (st[q] + st[q + 1]) / 2, n = Nrm(u, vm), t = T(vm);
          M.quad(A[0], B[0], B[1], A[1], V.sub(n, t), P.shade(ribC, 1.14));
          M.quad(A[1], B[1], B[2], A[2], V.add(n, t), P.shade(ribC, 0.78));
        }
        if (o.ends) {
          // 瓦当: a round tile end (pentagon, fanned from a corner: 3 triangles) closing the rib over the eave
          const v = st[0], n = Nrm(u, 0), t = T(0), f = V.norm(V.sub(S(u, v), S(u, v + 0.08)));
          const w = V.norm(V.cross(f, t)), c = V.madd(V.madd(S(u, v), n, RIB_H * 0.45), f, 0.012);
          const pg = []; for (let q = 0; q < 5; q++) { const a = Math.PI / 2 + q * 2 * Math.PI / 5; pg.push(V.add(c, V.add(V.mul(t, Math.cos(a) * 0.088), V.mul(w, Math.sin(a) * 0.088)))); }
          for (let q = 1; q < 4; q++) M.triangleFacing(pg[0], pg[q], pg[q + 1], f, endC);
        }
      }
      const d = [0, -0.2, 0], fo = [out[0], 0, out[2]];
      for (let j = 0; j < o.cols && (o.fascia || o.soffit); j++) {
        const u0 = j / o.cols, u1 = (j + 1) / o.cols, e0 = S(u0, 0), e1 = S(u1, 0);
        if (o.fascia) M.quad(e0, e1, V.add(e1, d), V.add(e0, d), fo, P.shade(vermilion, 0.8));
        // the soffit is straight: it drops further towards the break line so it stays well under the sagging tiles (it
        // faces down; where it came through the tiles its ink hull showed as a black hole in the roof)
        if (o.soffit) { const d2 = [0, -0.45, 0], b0 = S(u0, 1), b1 = S(u1, 1); M.quad(V.add(e0, d), V.add(e1, d), V.add(b1, d2), V.add(b0, d2), V.neg(out), soffit); }
      }
    };
    const NR = 17;    // front tile ribs (≈ 0.46 m apart at the break line, splaying towards the eave corners)
    tiledSlope(lowerS('f'), { out: OUT.f, cols: 8, courses: [0, 0.34, 0.68, 1], ribs: NR, ribV: [-0.015, 0.5, 1], ends: true, fascia: true, soffit: true });
    tiledSlope(lowerS('b'), { out: OUT.b, cols: 4, courses: [0, 0.5, 1], fascia: true });
    for (const sd of ['l', 'r']) tiledSlope(lowerS(sd), { out: OUT[sd], cols: 3, courses: [0, 0.5, 1], ribs: 7, ribV: [-0.015, 0.5, 1], ends: true, fascia: true, soffit: true });
    mark('lowerroof');
    // upper gable roof (straight, steeper: with the lower slope's sag the whole profile reads concave) and the 山花
    const gx = Lb + 0.25;
    const upperS = f => (u, v) => V.lerp([lerp(-gx, gx, u), yB - 0.05, f * (Db + 0.15) + hz0], [lerp(-gx, gx, u), yR, hz0], v);
    // its ribs continue the lower ribs (same x where the two roofs meet)
    const ribU = Array.from({ length: NR }, (_, i) => (lerp(-Lb, Lb, (i + 0.5) / NR) + gx) / (2 * gx));
    tiledSlope(upperS(1), { out: [0, 1, 1], cols: 6, courses: [0, 0.5, 1], ribU, ribV: [0, 1] });
    tiledSlope(upperS(-1), { out: [0, 1, -1], cols: 4, courses: [0, 0.5, 1] });
    for (const s of [-1, 1]) {
      M.color = vermilion;
      M.triangleFacing([s * Lb, yB - 0.05, -Db + hz0], [s * Lb, yR - 0.08, hz0], [s * Lb, yB - 0.05, Db + hz0], [s, 0, 0]);
      M.color = C.gold;
      M.triangleFacing([s * (Lb + 0.02), yB + 0.35, -0.35 + hz0], [s * (Lb + 0.02), yB + 0.85, hz0], [s * (Lb + 0.02), yB + 0.35, 0.35 + hz0], [s, 0, 0]);
      M.color = ridgeC;
      for (const f of [1, -1]) M.beam([s * gx, yB - 0.02, f * (Db + 0.2) + hz0], [s * gx, yR + 0.08, hz0], 0.16, 0.16, [s, 1, 0]);
    }
    mark('gable');
    // main ridge (正脊) in two steps, 0.45 m: a dark course and a lighter capping course
    M.color = ridgeC;
    M.box([0, yR + 0.11, hz0], [gx * 2 + 0.3, 0.32, 0.34], P.shade(ridgeC, 1.12), 'tfkrl');
    M.color = P.shade(ridgeC, 1.06);
    M.box([0, yR + 0.34, hz0], [gx * 2 + 0.05, 0.14, 0.22], P.shade(ridgeC, 1.25), 'tfkrl');
    // 鸱吻: a curl rising from each end of the ridge and rolling back over it (a flattened five-sided tube)
    for (const s of [-1, 1]) {
      const pts = [[s * (gx + 0.06), yR + 0.05], [s * (gx + 0.2), yR + 0.42], [s * (gx + 0.12), yR + 0.8], [s * (gx - 0.1), yR + 0.92], [s * (gx - 0.24), yR + 0.74]];
      const rs = [0.17, 0.15, 0.12, 0.09, 0.035];
      const rings = pts.map(([x, y], i) => {
        const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)], t = V.norm([b[0] - a[0], b[1] - a[1], 0]), nx = [-t[1], t[0], 0];
        const ring = []; for (let j = 0; j < 5; j++) { const an = j * Math.PI * 2 / 5; ring.push(V.add([x, y, hz0], V.add(V.mul(nx, Math.cos(an) * rs[i]), [0, 0, Math.sin(an) * rs[i] * 0.75]))); }
        return ring;
      });
      M.color = ridgeC;
      M.loft(rings, true, true);
    }
    // a gold bird (朱雀) on the middle of the ridge: body, head, spread wings and a swept tail fan — thin closed slabs, not
    // two-sided quads (a two-sided quad's back copy fills with ink through the outline hull)
    {
      const by = yR + 0.42, bz = hz0;
      const slab = (pts, th) => {
        const n = V.norm(V.cross(V.sub(pts[1], pts[0]), V.sub(pts[2], pts[0]))), h = V.mul(n, th / 2);
        const T = pts.map(p => V.add(p, h)), Bm = pts.map(p => V.sub(p, h)), c = V.mul(pts.reduce((a, p) => V.add(a, p), [0, 0, 0]), 1 / pts.length);
        for (let i = 1; i < pts.length - 1; i++) { M.triangleFacing(T[0], T[i], T[i + 1], n); M.triangleFacing(Bm[0], Bm[i + 1], Bm[i], V.neg(n)); }
        for (let i = 0; i < pts.length; i++) { const j = (i + 1) % pts.length; M.quad(T[i], T[j], Bm[j], Bm[i], V.sub(V.mul(V.add(pts[i], pts[j]), 0.5), c)); }
      };
      M.color = C.gold;
      M.sphere([0, by + 0.1, bz], [0.11, 0.1, 0.19], 6, 3, false);
      M.sphere([0, by + 0.25, bz + 0.17], [0.065, 0.07, 0.075], 5, 2, false);
      M.color = C.goldLight;
      for (const sd of [1, -1]) slab([[sd * 0.07, by + 0.15, bz + 0.07], [sd * 0.07, by + 0.15, bz - 0.09], [sd * 0.36, by + 0.28, bz - 0.1]], 0.03);
      slab([[-0.035, by + 0.12, bz - 0.15], [0.035, by + 0.12, bz - 0.15], [0.15, by + 0.34, bz - 0.44], [-0.15, by + 0.34, bz - 0.44]], 0.03);
    }
    // hip ridges (垂脊 / 戗脊) along the curved hip edges, upturned tips and a small ridge beast on each
    for (const [u, side] of [[0, 'f'], [1, 'f'], [0, 'b'], [1, 'b']]) {
      const S = lowerS(side), sx = u ? 1 : -1, sz = side === 'f' ? 1 : -1;
      const hp = [1, 0.5, 0].map(v => V.add(S(u, v), [0, 0.09, 0]));
      M.color = ridgeC;
      M.beam(hp[0], hp[1], 0.17, 0.16);
      M.beam(hp[1], hp[2], 0.17, 0.16);
      const dir = V.norm(V.sub(hp[2], hp[1]));
      M.spike(V.sub(hp[2], V.mul(dir, 0.05)), V.add(V.mul(dir, 0.55), [0, 0.85, 0]), 0.095, 0.5, 4);
      M.color = P.shade(ridgeC, 1.15);
      M.sphere(V.add(S(u, 0.2), [sx * 0.01, 0.22, sz * 0.01]), [0.07, 0.1, 0.07], 4, 2, false);
    }
    mark('ridges');
    // ---- banners on the parapet corners (sway) and lanterns
    const flag = o.flagColor ? H(o.flagColor) : C.barding;
    for (const s of [-1, 1]) {
      const bx = s * (px - 0.1), bz = pz;
      M.color = H('#5A3E2C');
      M.segment([bx, HB + 0.5, bz], [bx, HB + 4.3, bz], 0.05, 0.04, 4);
      M.color = C.gold; M.cone([bx, HB + 4.3, bz], 0.07, 0.22, 4);
      M.windGradient = true; M.windY0 = HB + 3.0; M.windY1 = HB + 4.4; M.wind = 1.2;
      const fx = s * -1;
      M.color = flag;
      M.strip([bx + fx * 0.06, HB + 3.65, bz], [bx + fx * 1.25, HB + 3.6, bz + 0.05], 0.9, 0.85, [0, 0, 1], 0.03);
      M.color = C.bardingTrim;
      M.strip([bx + fx * 1.2, HB + 3.6, bz + 0.05], [bx + fx * 1.45, HB + 3.58, bz + 0.06], 0.86, 0.6, [0, 0, 1], 0.03);
      M.wind = 0; M.windGradient = false;
    }
    mark('banners');
    // ribbed red paper lanterns (灯笼): a faint glow by day, lit at night
    const lamps = [];
    const lantern = (c, r, h) => {
      M.color = H('#3E2C24');
      M.segment(V.add(c, [0, h * 0.56, 0]), V.add(c, [0, h * 0.56 + 0.3, 0]), 0.014, 0.014, 3, false, false);
      M.ribLantern(c, r, h, H('#C8402E'), lit, C.gold, C.gold, 6, 0.2);
      lamps.push(c);
    };
    for (const s of [-1, 1]) lantern([s * 3.36, yE - 0.9, hz0 + HZ + 0.62], 0.29, 0.42);
    for (const s of [-1, 1]) {
      const c = [s * 2.35, 3.5, zs(3.5) + 0.46];
      M.color = H('#3E2C24');
      M.beam([s * 2.35, 4.04, zs(4.04) - 0.02], [s * 2.35, 4.04, zs(4.04) + 0.52], 0.08, 0.08);
      lantern(c, 0.25, 0.36);
    }
    mark('lanterns');
    return { M, lamps, parts, decals };
  }

  Pilot.gateTower = function (o) {
    o = o || {};
    const key = 'lp_gate|' + (o.lit ? 1 : 0) + '|' + (o.flagColor || '') + '|' + (o.name || '') + '|' + (o.hallName || '');
    let lamps = null;
    const geo = LP.MeshCache.get(key, () => { const r = buildGateTower(o); lamps = r.lamps; const g = r.M.toGeometry(); g.userData.lamps = r.lamps; g.userData.parts = r.parts; g.userData.decals = r.decals; return g; });
    const mesh = LP.mesh(geo);
    mesh.userData.lamps = geo.userData.lamps || lamps;
    mesh.name = 'LP gate tower';
    // painted name boards (decals on the plaques)
    for (const d of geo.userData.decals || []) {
      const tex = LP.boardTexture(d.text, { w: 448, h: Math.round(448 * d.h / d.w), board: d.board, ink: d.ink });
      const dm = LP.decal(tex, d.w, d.h);
      dm.position.set(...d.at);
      dm.rotation.x = d.tilt * Math.PI / 180;
      if (d.local) dm.translateZ(d.local[2]);
      mesh.add(dm);
    }
    return mesh;
  };
  // =========================================================== trees ==
  /** Tapered faceted trunk (Lathe) squashed in depth. */
  function trunk(M, profile, sides, bark, depthScale, rings, angle = 0) {
    M.color = bark;
    M.push().scale(1, 1, depthScale);
    M.lathe(profile, sides, false, false, true, rings || null, angle);
    M.pop();
  }
  function roots(M, count, reach, radius, depthScale, startDeg, bark) {
    M.color = P.shade(bark, 0.92);
    for (let i = 0; i < count; i++) {
      const a = (startDeg + i * 360 / count) * D2R, dir = [Math.cos(a), 0, Math.sin(a) * depthScale];
      M.segment(V.add(V.mul(dir, radius * 0.4), [0, radius * 0.9, 0]), V.add(V.mul(dir, reach), [0, 0.02, 0]), radius * 0.5, radius * 0.12, 5, false, true);
    }
  }
  /**
   * 槐 (Chinese scholar tree), after Lanternvale's BuildOak: a stout faceted trunk (×1.2) forking at 3 m into four limbs that
   * show under the crown; the crown starts at ~3.6 m and is told by seven big soft masses (the central one 2.3 m across
   * the x radius) plus seven smaller rim lumps (±3 m) — a scalloped, leafy silhouette, not a pile of bubbles — in a
   * deeper green whose canopy ramp keeps the top close to the foliage colour and the underside cool teal.
   */
  function buildTree(b) {
    b = (b | 0) & 3;
    const M = new LP.MeshBuilder(211 + b * 7919);
    M.jitter = 0.06; M.aoStrength = 0.2; M.aoHeight = 1.2;
    M.windGradient = true; M.windY0 = 2.6; M.windY1 = 8; M.wind = 1.6;
    // warm mid-brown bark (Lanternvale's oak trunk ≈ (107, 83, 57) at noon; ours sits in the crown's soft shadow)
    const bark = H('#98724C'), barkDark = H('#735238');
    M.push().rotate(0, 0, (b - 1.5) * 1.6);
    trunk(M, [[0.53, 0], [0.4, 0.3], [0.34, 1.2], [0.31, 2.4], [0.26, 3.3]], 7, bark, 0.8, [barkDark, bark, bark, P.shade(bark, 1.06), bark]);
    roots(M, 5, 0.7, 0.24, 0.55, b * 23, bark);
    M.color = bark;
    const fork = [0, 3.0, 0];
    const tips = [[-1.5, 4.5, 0.1], [1.5, 4.6, 0.3], [0.2, 5.2, -0.3], [-0.2, 5.0, 0.85]];
    for (const t of tips) M.segment(fork, t, 0.24, 0.095, 5);
    M.pop();
    const tune = { topVal: 0.94, coolMix: 0.6 };
    const f = LP.foliage(b, H('#376634'), 7, tune);
    const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 3.6, 8.0, tune);
    const sx = 1 + 0.04 * ((b * 5) % 3);       // seeds differ a little in breadth
    const blobs = [
      [[0, 5.5, 0.2], [2.3, 1.9, 2.0]], [[-1.75, 4.7, 0.1], [1.5, 1.3, 1.4]], [[1.85, 4.85, 0.35], [1.5, 1.35, 1.4]],
      [[0.55, 6.7, 0.15], [1.55, 1.35, 1.45]], [[-0.95, 6.35, 0.55], [1.35, 1.2, 1.3]], [[0.35, 4.5, -1.05], [1.35, 1.15, 1.1]],
      [[-0.3, 5.1, 1.6], [1.5, 1.3, 1.2]],
    ];
    const lumps = [
      [[-2.95, 5.2, 0.3], [0.85, 0.75, 0.8]], [[3.0, 5.45, 0.55], [0.85, 0.75, 0.8]], [[-1.75, 7.1, 0.25], [0.8, 0.7, 0.75]],
      [[1.75, 6.95, 0.45], [0.9, 0.75, 0.8]], [[-1.35, 4.0, -0.75], [0.85, 0.62, 0.72]], [[1.5, 4.1, -0.6], [0.85, 0.62, 0.72]],
      [[0.15, 7.8, 0.4], [0.85, 0.62, 0.8]],
    ];
    blobs.forEach(([c, r], i) => M.softLump([c[0] * sx + (M.random01() - 0.5) * 0.3, c[1] + (M.random01() - 0.5) * 0.3, c[2]], [r[0] * sx, r[1], r[2]], 1, i * 47 + b * 13, ramp, 0.12));
    lumps.forEach(([c, r], i) => M.softLump([c[0] * sx, c[1], c[2]], r, 1, i * 61 + b * 17, ramp, 0.14));
    return M;
  }
  /** Pine (Lanternvale BuildPine): faceted trunk, six soft skirts with a canopy-wide ramp. */
  function buildPine(b) {
    b = (b | 0) & 3;
    const M = new LP.MeshBuilder(307 + b * 7919);
    M.jitter = 0.06; M.aoStrength = 0.3; M.aoHeight = 1.0;
    M.windGradient = true; M.windY0 = 1.5; M.windY1 = 11; M.wind = 1.4;
    const bark = H('#6B4C3B');
    trunk(M, [[0.32, 0], [0.23, 0.4], [0.18, 4], [0.1, 9], [0.04, 10.6]], 6, bark, 0.85);
    roots(M, 4, 0.48, 0.16, 0.55, 40 + b * 17, bark);
    const f = LP.foliage(b, H('#3F6B4A'), 6);
    const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2.2, 11.2);
    const tiers = 6;
    for (let i = 0; i < tiers; i++) {
      const y0 = 2.55 + i * 1.36, r = 2.3 - i * 0.33 + (i % 2) * 0.08, h = 2.45 - i * 0.15;
      M.push().translate(0, y0, 0).rotate((M.random01() - 0.5) * 6, 0, (M.random01() - 0.5) * 6);
      const under = P.shade(ramp(y0 - 0.3), 0.78);
      M.color = f.side;
      M.lathe([[r * 0.8, -0.42], [r * 0.97, -0.24], [r, -0.12], [r * 0.55, h * 0.36], [0, h]], 9, true, true, false,
        [under, P.mix(under, ramp(y0), 0.6), ramp(y0), ramp(y0 + h * 0.36), ramp(y0 + h)], i * 19 + b * 11);
      M.pop();
    }
    M.color = ramp(11.2);
    M.cone([0, 2.55 + tiers * 1.36 - 0.45, 0], 0.35, 1.3, 6);
    return M;
  }
  Pilot.tree = seed => { const g = LP.MeshCache.get('lp_tree2|' + ((seed | 0) & 3), () => buildTree(seed).toGeometry()); const m = LP.mesh(g); m.name = 'LP tree'; return m; };
  Pilot.pine = seed => { const g = LP.MeshCache.get('lp_pine2|' + ((seed | 0) & 3), () => buildPine(seed).toGeometry()); const m = LP.mesh(g); m.name = 'LP pine'; return m; };

  // ============================================== small scene props ==
  /**
   * Bush: three soft lumps (base #6A9A4A) and, on half the seeds, four to six tiny pink or white flower clusters (three
   * four-sided discs each, no glow) — the flowers are a separate mesh without ink, so they stay specks of colour.
   */
  const BUSH_LUMPS = [[[0, 0.55, 0], [0.75, 0.6, 0.65], 0], [[0.55, 0.42, 0.15], [0.48, 0.4, 0.45], 40], [[-0.5, 0.4, 0.1], [0.45, 0.38, 0.42], 80]];
  Pilot.bush = function (seed) {
    seed = seed | 0;
    const g = LP.MeshCache.get('lp_bush2|' + seed, () => {
      const M = new LP.MeshBuilder(401 + seed);
      M.jitter = 0.06; M.aoStrength = 0.3; M.aoHeight = 0.5;
      M.windGradient = true; M.windY0 = 0.2; M.windY1 = 1.3; M.wind = 0.7;
      const f = LP.foliage(seed & 3, H('#6A9A4A'), 8), ramp = LP.canopyRamp(f.top, f.side, f.bottom, 0.0, 1.2);
      BUSH_LUMPS.forEach(([c, r, yaw], i) => M.softLump(c, r, 1, yaw, ramp, i ? 0.14 : 0.12));
      return M.toGeometry();
    });
    const mesh = LP.mesh(g);
    mesh.name = 'LP bush';
    if (!(seed & 1)) return mesh;
    const fg = LP.MeshCache.get('lp_bushflowers|' + seed, () => {
      const M = new LP.MeshBuilder(431 + seed);
      M.windGradient = true; M.windY0 = 0.2; M.windY1 = 1.3; M.wind = 0.7;
      const col = (seed >> 1) & 1 ? H('#F5F0E6') : H('#F2B8C6');
      const n = 4 + (seed % 3);
      for (let i = 0; i < n; i++) {
        const [c, r] = BUSH_LUMPS[i % 3];
        const th = (i * 137.5 + seed * 40) * D2R, ph = (28 + ((i * 23) % 40)) * D2R;
        const u = [Math.sin(ph) * Math.cos(th), Math.cos(ph), Math.sin(ph) * Math.sin(th)];
        const p = V.add(c, [u[0] * r[0] * 1.06, u[1] * r[1] * 1.06, u[2] * r[2] * 1.06]);
        const nrm = V.norm([u[0] / r[0], u[1] / r[1], u[2] / r[2]]);
        M.push().translate(p).rotateQ(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(...nrm)));
        for (let j = 0; j < 3; j++) {
          const a = (j * 120 + i * 30) * D2R;
          M.color = P.shade(col, 0.95 + 0.1 * M.random01());
          M.disc([Math.cos(a) * 0.055, 0.01 * j, Math.sin(a) * 0.055], 0.045, 4);
        }
        M.pop();
      }
      return M.toGeometry();
    });
    const group = new THREE.Group();
    group.name = 'LP bush';
    group.add(mesh, LP.mesh(fg, { outline: false, castShadow: false }));
    return group;
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
  /**
   * Han lamp post (灯杆): a square timber post on a stone foot carrying a small lantern pavilion — a dark frame round cream
   * paper panels (a warm glow when lit) under a slate-tiled hip cap with upturned corners and a gold finial.
   * userData.lamp = the light's position (the middle of the paper box).
   */
  Pilot.lampPost = function (lit) {
    const by = 2.22, bh = 0.46, bw = 0.4;
    const g = LP.MeshCache.get('lp_lamppost2|' + (lit ? 1 : 0), () => {
      const M = new LP.MeshBuilder(601);
      M.jitter = 0.06; M.aoStrength = 0.3; M.aoHeight = 0.5;
      M.color = PAL.stone; M.cylinder([0, 0, 0], 0.25, 0.2, 0.22, 6, false, false, true);
      M.color = PAL.timber; M.box([0, 0.22 + (by - 0.22) / 2, 0], [0.15, by - 0.22, 0.15], null, 'tfkrl');
      M.color = PAL.timberDark; M.box([0, by - 0.25, 0], [0.18, 0.08, 0.18], null, 'tbfkrl');
      M.box([0, by + 0.03, 0], [bw + 0.12, 0.06, bw + 0.12]);
      // the paper box and its frame
      M.color = lit ? P.mix(PAL.paper, PAL.glow, 0.55) : P.shade(PAL.paper, 0.97); M.emission = lit ? 0.95 : 0.06;
      M.box([0, by + 0.06 + bh / 2, 0], [bw, bh, bw], null, 'fkrl');
      M.emission = 0; M.color = PAL.timberDark;
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.box([sx * bw / 2, by + 0.06 + bh / 2, sz * bw / 2], [0.055, bh, 0.055], null, 'fkrl');
      for (const s of [-1, 1]) { M.box([0, by + 0.06 + bh * 0.5, s * (bw / 2 + 0.005)], [bw, 0.035, 0.02], null, s > 0 ? 'f' : 'k'); M.box([s * (bw / 2 + 0.005), by + 0.06 + bh * 0.5, 0], [0.02, 0.035, bw], null, s > 0 ? 'r' : 'l'); }
      M.box([0, by + 0.06 + bh + 0.03, 0], [bw + 0.1, 0.06, bw + 0.1], null, 'bfkrl');
      // hip cap: four tiled slopes from flared, upturned eaves to the apex, a dark underside, corner tips and a gold finial
      const y0 = by + 0.06 + bh + 0.06, e = 0.4, apex = [0, y0 + 0.3, 0];
      const cn = [[-e, y0 + 0.07, -e], [e, y0 + 0.07, -e], [e, y0 + 0.07, e], [-e, y0 + 0.07, e]];
      const mids = [[0, y0, -e * 0.96], [e * 0.96, y0, 0], [0, y0, e * 0.96], [-e * 0.96, y0, 0]];
      for (let i = 0; i < 4; i++) {
        const a = cn[i], b = cn[(i + 1) % 4], m = mids[i], out = [m[0], 0.8, m[2]];
        M.color = i % 2 ? PAL.tileSlate : P.shade(PAL.tileSlate, 0.92);
        M.triangleFacing(a, m, apex, out); M.triangleFacing(m, b, apex, out);
        M.color = H('#4F5E6E');
        M.triangleFacing(a, b, [0, y0 - 0.02, 0], [0, -1, 0]);
        M.color = PAL.tileSlateDark;
        M.spike(a, [a[0], 0.9, a[2]], 0.025, 0.12, 3);
      }
      M.color = PAL.gold; M.sphere([apex[0], apex[1] + 0.04, apex[2]], 0.05, 5, 2, false);
      return M.toGeometry();
    });
    const m = LP.mesh(g);
    m.userData.lamp = [0, by + 0.06 + bh / 2, 0];
    m.name = 'LP lamp post';
    return m;
  };
  /**
   * A stretch of city wall, centred on x = 0, front +Z: a plinth of stone blocks in three tones, battered (0.12) rammed-earth
   * courses of uneven height darkening towards a damp foot, each laid in formwork panels of their own tone with a dark seam
   * between courses, 马面 buttresses (+0.8 m) every ~8 m, a parapet with merlons in varied tones (moss cushions on every
   * 4th), two stone drain spouts with damp streaks that taper and fade into the wall. On top a paved wall-walk: staggered
   * slabs in two tones over dark grout (a gutter strip along the parapet), a low inner parapet (宇墙) on the city side and
   * a few props (o.variant 0: a weapon rack and crates; 1: a banner, crates and a jar).
   */
  Pilot.cityWall = function (len, o) {
    o = o || {};
    const variant = (o.variant | 0) & 1;
    const g = LP.MeshCache.get('lp_wall3|' + len + '|' + variant + '|' + (o.flagColor || ''), () => {
      const M = new LP.MeshBuilder(701 + variant * 37);
      M.jitter = 0.06; M.jitterAll = true; M.aoStrength = 0.35; M.aoHeight = 1.2;
      const Hh = 4.1, Z = 2.4, bat = 0.12, X = len / 2, PL = 0.6, BW = 2.4, BD = 0.8;
      const zs = y => Z - bat * y;
      const ys = [PL, 1.2, 1.72, 2.36, 2.92, 3.56, Hh];
      const courseCol = i => (i % 2 ? PAL.rammed : P.mix(PAL.rammed, PAL.rammedDark, 0.32));
      const seamC = P.shade(PAL.rammedDark, 0.9);
      const nB = Math.max(1, Math.round(len / 8)), bxs = [];
      for (let i = 0; i < nB; i++) bxs.push(-X + (i + 0.5) * len / nB);
      // the runs of plain wall between the buttresses
      const runs = []; let x = -X;
      for (const bx of bxs) { runs.push([x, bx - BW / 2]); x = bx + BW / 2; }
      runs.push([x, X]);
      for (let i = 0; i < ys.length - 1; i++) {
        const y0 = ys[i], y1 = ys[i + 1], col = P.shade(courseCol(i), i === 0 ? 0.88 : 1);
        // each rammed-earth course: long formwork lifts (≈ 3 m, staggered) in close tones, each shaded from a lighter top lip
        // to a darker foot — the layers read as bulging rammed earth, not as milled boards
        const grad = (c, x0, x1, dz) => { const cb = P.shade(c, 0.93), ct = P.shade(c, 1.02); M.quadColors([x0, y0, zs(y0) + dz], [x1, y0, zs(y0) + dz], [x1, y1, zs(y1) + dz], [x0, y1, zs(y1) + dz], [0, bat, 1], [cb, cb, ct, ct]); };
        for (const [a, b] of runs) {
          let x0 = a, x1 = a + (i % 2 ? 1.5 : 3.0) + (M.random01() - 0.5) * 0.6;
          while (x0 < b - 1e-3) {
            x1 = Math.min(b, x1); if (b - x1 < 0.8) x1 = b;
            grad(P.shade(col, 1 + (M.random01() - 0.5) * 0.05), x0, x1, 0);
            x0 = x1; x1 = x0 + 3.0 + (M.random01() - 0.5) * 0.8;
          }
        }
        M.quad([-X, y0, -zs(y0)], [X, y0, -zs(y0)], [X, y1, -zs(y1)], [-X, y1, -zs(y1)], [0, bat, -1], col);
        // buttress faces: front (battered) and the two sides
        for (const bx of bxs) {
          const c2 = P.shade(col, 0.97 + (M.random01() - 0.5) * 0.06);
          grad(c2, bx - BW / 2, bx + BW / 2, BD);
          for (const s of [-1, 1]) M.quad([bx + s * BW / 2, y0, zs(y0)], [bx + s * BW / 2, y0, zs(y0) + BD], [bx + s * BW / 2, y1, zs(y1) + BD], [bx + s * BW / 2, y1, zs(y1)], [s, 0, 0], P.shade(c2, 0.93));
        }
      }
      // a dark seam between the courses, hand-drawn: broken into pieces of uneven thickness and height, with gaps
      for (let i = 1; i < ys.length - 1; i++) {
        const seg = (a, b, dz) => {
          let x0 = a;
          while (x0 < b - 0.05) {
            const x1 = Math.min(b, x0 + 0.9 + M.random01() * 1.8);
            if (M.random01() < 0.8) {
              const y = ys[i] + (M.random01() - 0.5) * 0.024, h0 = 0.007 + M.random01() * 0.011, h1 = 0.007 + M.random01() * 0.011;
              M.quad([x0, y - h0, zs(y - h0) + dz + 0.006], [x1, y - h1, zs(y - h1) + dz + 0.006], [x1, y + h1, zs(y + h1) + dz + 0.006], [x0, y + h0, zs(y + h0) + dz + 0.006], [0, bat, 1], seamC);
            }
            x0 = x1;
          }
        };
        for (const [a, b] of runs) seg(a, b, 0);
        for (const bx of bxs) seg(bx - BW / 2, bx + BW / 2, BD);
      }
      // weathered patches: irregular darker earth here and there on the lower courses
      const patchC = P.mix(PAL.rammedDark, PAL.earth, 0.2);
      for (const [a, b] of runs) {
        const n = Math.max(1, Math.round((b - a) / 3.2));
        for (let q = 0; q < n; q++) {
          const cx = lerp(a + 0.5, b - 0.5, (q + 0.3 + M.random01() * 0.4) / n), cy = 0.95 + M.random01() * 1.9, rx = 0.22 + M.random01() * 0.3, ry = 0.12 + M.random01() * 0.1;
          const pts = []; for (let j = 0; j < 6; j++) { const an = j * Math.PI / 3 + M.random01() * 0.4, rr = 0.7 + M.random01() * 0.4, y = cy + Math.sin(an) * ry * rr; pts.push([cx + Math.cos(an) * rx * rr, y, zs(y) + 0.009]); }
          const c = P.shade(patchC, 0.96 + M.random01() * 0.08);
          for (let j = 1; j < 5; j++) M.triangleFacing(pts[0], pts[j], pts[j + 1], [0, bat, 1], c);
        }
      }
      for (const s of [-1, 1]) { M.color = PAL.rammedDark; M.quad([s * X, 0, -Z], [s * X, 0, Z], [s * X, Hh, zs(Hh)], [s * X, Hh, -zs(Hh)], [s, 0, 0]); }
      // plinth: along the runs and round the buttresses; stone below it at the back
      M.aoStrength = 0.2;
      for (const [a, b] of runs) plinthRow(M, a, b, Z, PL);
      for (const bx of bxs) {
        plinthRow(M, bx - BW / 2 - 0.12, bx + BW / 2 + 0.12, Z + BD, PL);
        for (const s of [-1, 1]) { M.push().translate(bx + s * BW / 2, 0, Z + BD / 2).rotate(0, s * 90, 0); plinthRow(M, -BD / 2, BD / 2, 0, PL); M.pop(); }
      }
      M.color = PAL.stone; M.quad([-X, 0, -Z], [X, 0, -Z], [X, PL, -zs(PL)], [-X, PL, -zs(PL)], [0, bat, -1]);
      M.aoStrength = 0.35;
      // parapet and merlons (the buttresses carry theirs forward)
      const pc = P.mix(PAL.brick, PAL.rammed, 0.3), zt = zs(Hh), pz = zt - 0.22;
      M.color = pc;
      for (const [a, b] of runs) M.box([(a + b) / 2, Hh + 0.28, pz], [b - a, 0.56, 0.44], null, 'tfkrl');
      const mxs = [];
      for (const [a, b] of runs) { const n = Math.max(1, Math.floor((b - a) / 1.3)); for (let i = 0; i < n; i++) mxs.push(a + 0.65 + i * ((b - a) - 1.3) / Math.max(1, n - 1)); }
      merlons(M, mxs, Hh + 0.56, pz, [0.8, 0.44, 0.46], pc);
      for (const bx of bxs) {
        M.color = pc;
        M.box([bx, Hh + 0.28, pz + BD], [BW, 0.56, 0.44], null, 'tfkrl');
        for (const s of [-1, 1]) M.box([bx + s * (BW / 2 - 0.22), Hh + 0.28, pz + BD / 2], [0.44, 0.56, BD], null, 'tkrl');
        merlons(M, [bx - 0.65, bx + 0.65], Hh + 0.56, pz + BD, [0.8, 0.44, 0.46], pc, 2);
      }
      // ---- the wall-walk: dark grout bed (it shows as a gutter strip along the parapet and in the joints), staggered paving
      // slabs in two tones (jittered), the buttress tops paved too, a low inner parapet (宇墙) on the city side
      const grout = P.shade(PAL.stone, 0.74), zBack = -zt, ipD = 0.3, ipH = 0.45, zIn = pz - 0.22;
      M.quad([-X, Hh, zBack], [X, Hh, zBack], [X, Hh, zt], [-X, Hh, zt], [0, 1, 0], grout);
      for (const bx of bxs) M.quad([bx - BW / 2, Hh, zt], [bx + BW / 2, Hh, zt], [bx + BW / 2, Hh, zt + BD], [bx - BW / 2, Hh, zt + BD], [0, 1, 0], grout);
      const slabA = P.mix(PAL.stoneLight, PAL.stone, 0.3), slabB = P.mix(PAL.stoneLight, PAL.stone, 0.75);
      const slab = (x0, x1, z0, z1) => M.quad([x0 + 0.02, Hh + 0.014, z0 + 0.02], [x1 - 0.02, Hh + 0.014, z0 + 0.02], [x1 - 0.02, Hh + 0.014, z1 - 0.02], [x0 + 0.02, Hh + 0.014, z1 - 0.02], [0, 1, 0], P.shade(M.random01() < 0.5 ? slabA : slabB, 0.92 + M.random01() * 0.1));
      const pz0 = zBack + ipD, pz1 = zIn - 0.26, rows = 4;
      for (let r = 0; r < rows; r++) {
        const z0 = lerp(pz0, pz1, r / rows), z1 = lerp(pz0, pz1, (r + 1) / rows);
        let x0 = -X, x1 = -X + (r % 2 ? 0.55 : 1.15) + M.random01() * 0.3;
        while (x0 < X - 1e-3) { x1 = Math.min(X, x1); if (X - x1 < 0.4) x1 = X; slab(x0, x1, z0, z1); x0 = x1; x1 = x0 + 1.05 + M.random01() * 0.35; }
      }
      for (const bx of bxs) for (let r = 0; r < 2; r++) for (let c = 0; c < 2; c++) {
        const xa = bx - BW / 2 + 0.44, xb = bx + BW / 2 - 0.44, za = pz1 + 0.26, zb = pz + BD - 0.22;
        slab(lerp(xa, xb, c / 2), lerp(xa, xb, (c + 1) / 2), lerp(za, zb, r / 2), lerp(za, zb, (r + 1) / 2));
      }
      {
        const nS = Math.max(1, Math.round(len / 4.5));
        for (let k = 0; k < nS; k++) {
          const x0 = lerp(-X, X, k / nS), x1 = lerp(-X, X, (k + 1) / nS);
          M.color = P.shade(pc, 0.96 + M.random01() * 0.08);
          M.box([(x0 + x1) / 2, Hh + ipH / 2, zBack + ipD / 2], [x1 - x0, ipH, ipD], P.shade(PAL.stoneLight, 0.9), 'tfk' + (k === 0 ? 'l' : '') + (k === nS - 1 ? 'r' : ''));
        }
      }
      // ---- a few props on the walk, against the inner parapet
      const ground = Hh + 0.014, zp = pz0 + 0.28;
      const crate = (x, z, sz, rot) => {
        M.push().translate(x, ground, z).rotate(0, rot, 0);
        M.color = H('#9A7050'); M.box([0, sz / 2, 0], [sz, sz, sz * 0.9], H('#B08460'), 'tfkrl');
        M.color = H('#6E4B36'); M.box([0, sz * 0.5, sz * 0.455], [sz * 1.01, sz * 0.14, 0.012], null, 'f');
        M.pop();
      };
      if (variant === 0) {
        // weapon rack (兵器架): two posts, two rails, three spears leaning in it
        const rx = -X * 0.36;
        M.push().translate(rx, ground, zp);
        M.color = PAL.timber;
        for (const s of [-1, 1]) M.box([s * 0.62, 0.65, 0], [0.08, 1.3, 0.08], null, 'tfkrl');
        M.color = PAL.timberDark;
        M.box([0, 1.2, 0], [1.36, 0.07, 0.08], null, 'tfkrl');
        M.box([0, 0.35, 0], [1.36, 0.07, 0.08], null, 'tfkrl');
        for (const xx of [-0.36, 0, 0.36]) {
          M.color = C.wood; M.segment([xx, 0.02, 0.18], [xx + 0.05, 1.95, -0.08], 0.02, 0.018, 4, false, false);
          M.color = C.steel; M.spike([xx + 0.05, 1.94, -0.08], [0.026, 0.99, -0.135], 0.035, 0.18, 4);
          M.color = C.tassel; M.sphere([xx + 0.048, 1.88, -0.07], 0.035, 4, 2, false);
        }
        M.pop();
        crate(X * 0.42, zp, 0.6, 8); crate(X * 0.42 + 0.68, zp + 0.05, 0.5, -12); crate(X * 0.42 + 0.3, zp + 0.02, 0.42, 30);
        M.push().translate(0, 0.6, 0); crate(X * 0.42 + 0.05, zp, 0.42, 20); M.pop();
      } else {
        // a faction banner on a pole set in the inner parapet, crates and a water jar
        const bx = X * 0.18, bz = zBack + ipD / 2;
        M.color = H('#5A3E2C'); M.segment([bx, Hh, bz], [bx, Hh + 3.4, bz], 0.045, 0.035, 4);
        M.color = C.gold; M.cone([bx, Hh + 3.4, bz], 0.06, 0.2, 4);
        M.windGradient = true; M.windY0 = Hh + 2.2; M.windY1 = Hh + 3.5; M.wind = 1.1;
        M.color = H(o.flagColor || '#2f7d4a'); M.strip([bx + 0.05, Hh + 2.85, bz], [bx + 1.0, Hh + 2.8, bz + 0.04], 0.8, 0.74, [0, 0, 1], 0.03);
        M.wind = 0; M.windGradient = false;
        crate(-X * 0.4, zp, 0.6, -6); crate(-X * 0.4 + 0.66, zp + 0.04, 0.48, 14);
        M.color = H('#7E6250');
        M.push().translate(-X * 0.4 - 0.75, ground, zp + 0.05);
        M.lathe([[0.16, 0], [0.26, 0.2], [0.24, 0.45], [0.13, 0.55], [0.15, 0.6]], 6, false, false, true);
        M.pop();
      }
      // putlog holes (the formwork beams' sockets) in two rows
      M.color = H('#6E5A44');
      for (const [yy, off] of [[ys[2] + 0.06, 0.6], [ys[4] + 0.06, 1.7]]) for (const [a, b] of runs) for (let hx = a + off; hx < b - 0.3; hx += 2.2) {
        const z0 = zs(yy) + 0.008;
        M.quad([hx - 0.07, yy, z0], [hx + 0.07, yy, z0], [hx + 0.07, yy + 0.11, zs(yy + 0.11) + 0.008], [hx - 0.07, yy + 0.11, zs(yy + 0.11) + 0.008], [0, bat, 1]);
      }
      // drain spouts with damp streaks that taper to a point and fade into the earth, in the longest run
      const longest = runs.reduce((m, r) => (r[1] - r[0] > m[1] - m[0] ? r : m), runs[0]);
      M.aoStrength = 0;
      for (const sx of [-1, 1]) {
        const dx = (longest[0] + longest[1]) / 2 + sx * (longest[1] - longest[0]) * 0.27;
        const sy = Hh - 0.32, by = sy - 1.15;
        M.color = PAL.stoneDark;
        M.box([dx, sy, zs(sy) + 0.18], [0.2, 0.13, 0.46], PAL.stone, 'tbfrl');
        const damp = P.shade(PAL.rammedDark, 0.8), fade = P.shade(courseCol(3), 0.98);
        M.quadColors([dx - 0.11, sy - 0.06, zs(sy - 0.06) + 0.012], [dx + 0.11, sy - 0.06, zs(sy - 0.06) + 0.012], [dx + 0.03, by, zs(by) + 0.012], [dx - 0.02, by, zs(by) + 0.012], [0, bat, 1], [damp, damp, fade, fade]);
      }
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
