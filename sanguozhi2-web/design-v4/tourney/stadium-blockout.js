'use strict';
/* ==========================================================================
   武林大会 · 万国擂台（TOURNAMENT.md §8）的白模 / 体块原型（SG.LP，Lanternvale 风格的低多边形）

   只为量预算、验视线、出示意图：每一处地标是「看得出是什么」的体块，不是成品模型（成品见 §8.4 的配方表，
   由美术试点按 SG.LPPilot.gateTower 的质量做）。场地坐标：Y 向上，擂台中心为原点，单挑轴沿 X（选手在 x ∈ [−7.2, 7.2]），
   单挑镜头在 +Z 看向 −Z。方位角 φ：0° = −Z（单挑默认的背景方向），90° = +X（东），180° = +Z，270° = −X。
   一个扇区按 φ = 0 建好（正面朝 +Z、看台向 −Z 升起），再绕 Y 轴转 −φ。

   接口（浏览器：stadium-blockout.html；Node：node design-v4/tourney/stadium-blockout.js 打印三角形预算）
     SG.StadiumBlockout.SLOTS                       16 个方位：{ id, name, phi, family? }
     SG.StadiumBlockout.build({ night, crowd, phone }) → { group, sectors: [{ id, mesh, crowd, tris, crowdTris }], lamps: [[x,y,z]], tris }
     SG.StadiumBlockout.face(group, slotId)          转动整座擂台，使该方位位于单挑镜头的背景正中（group.rotation.y = φ）
   ========================================================================== */
(function () {
  const root = typeof window !== 'undefined' ? window : global;
  const SG = root.SG;
  if (!SG || !SG.LP) return;   // Node：下方载入 SG.LP 后再执行
  const LP = SG.LP, P = LP.Paint, H = P.hex, PAL = LP.PAL;
  const D2R = Math.PI / 180;

  // ------------------------------------------------------------------ 布局常量（TOURNAMENT §8.2） --
  const L = {
    stageW: 18, stageD: 9, stageH: 1.2,      // 擂台：顶面 y = 0（单挑的地面）
    floorY: -1.2, floorR: 23,                // 沙地（场心）
    faceR: 22, podiumR: 24.5, podiumH: 1.6,  // 扇区门面立在沙地上 r ≈ 21.5–23.5（宽 ≤ 7 m、高 ≤ 9 m：单挑镜头的上沿在此约 7–8 m），其后看台前墙顶 y = 1.6
    standR0: 25, standR1: 41, standY0: 1.6, standY1: 13.5, rows: 14,
    wallR: 42, wallH: 16,                    // 外圈拱廊
    slot: 22.5,
  };
  const SLOTS = [
    { id: 'gate', name: '主门', phi: 90 },
    { id: 'hua', name: '华夏', phi: 67.5, family: 'hua' },
    { id: 'steppe', name: '草原', phi: 45, family: 'steppe' },
    { id: 'oasis', name: '绿洲', phi: 22.5, family: 'oasis' },
    { id: 'judges', name: '裁判台', phi: 0 },
    { id: 'persia', name: '安息', phi: 337.5, family: 'persia' },
    { id: 'north', name: '北方', phi: 315, family: 'north' },
    { id: 'roman', name: '罗马', phi: 292.5, family: 'roman' },
    { id: 'nations', name: '万国之门', phi: 270 },
    { id: 'nile', name: '尼罗', phi: 247.5, family: 'nile' },
    { id: 'arabia', name: '阿拉伯', phi: 225, family: 'arabia' },
    { id: 'indic', name: '天竺', phi: 202.5, family: 'indic' },
    { id: 'podium', name: '奖台', phi: 180 },
    { id: 'nanhai', name: '南海', phi: 157.5, family: 'nanhai' },
    { id: 'wa', name: '倭', phi: 135, family: 'wa' },
    { id: 'han3', name: '三韩', phi: 112.5, family: 'han3' },
  ];
  // 观众衣色（每个家族一组，暖调；势力色另由看台旗帜表现）
  const CROWD = {
    hua: ['#3f5f8a', '#a8463a', '#c9a45a', '#5e7a4e', '#e8dcc0'], steppe: ['#7a5634', '#a87a4a', '#5a4632', '#c8a070', '#3e5a6a'],
    oasis: ['#9a4a4a', '#c88a4a', '#e2c79a', '#4a6a8a'], persia: ['#2f3f7a', '#c8902a', '#7a2f3a', '#e6d6b4'],
    north: ['#5a7a3a', '#a85a2a', '#c8b46a', '#6a5a46', '#2a58b0'], roman: ['#ece6da', '#a83a2e', '#d8c8a4', '#6a5a8a'],
    nile: ['#ece6d8', '#c8902a', '#6c4026', '#3a6a5a'], arabia: ['#e8dfc8', '#7a4a2a', '#c86a3a', '#3a4a6a'],
    indic: ['#e8a83a', '#c84a3a', '#f0e0c0', '#3a7a6a'], nanhai: ['#c8a03a', '#a8463a', '#3a6a4a', '#e8d0a0'],
    wa: ['#d8ccb0', '#8a6a4a', '#3a4a6a', '#c8b48a'], han3: ['#e8e0d0', '#5a6a8a', '#a8463a', '#c8b07a'],
    judges: ['#2c2c33', '#7a2a3a', '#c8a45a'], gate: ['#c9503a', '#e3b34c', '#3a3134'], nations: ['#6a5a8a', '#c8902a'], podium: ['#e3b34c', '#c9503a'],
  };

  // ------------------------------------------------------------------ 小工具 --
  function C(mb, hex) { mb.color = typeof hex === 'string' ? H(hex) : hex; return mb; }
  function tri(geo) { return (geo && geo.userData && geo.userData.tris) || 0; }
  // 圆弧上的一段（φ = 0 的扇区内，角度 a 自 −Z 起顺时针，弧度）
  const ptR = (r, a, y) => [Math.sin(a) * r, y, -Math.cos(a) * r];
  // 看台：一排 = 顶面 + 立面，沿弧分 seg 段
  function standRows(mb, a0, a1, seg, col) {
    const n = L.rows;
    for (let i = 0; i < n; i++) {
      const r0 = L.standR0 + (L.standR1 - L.standR0) * i / n, r1 = L.standR0 + (L.standR1 - L.standR0) * (i + 1) / n;
      const yTop = L.standY0 + (L.standY1 - L.standY0) * (i + 1) / n, yLow = L.standY0 + (L.standY1 - L.standY0) * i / n;
      const tier = i === 5 || i === 10;               // 两道横向过道（纵向分三层）
      C(mb, tier ? '#c6bdae' : i % 2 ? col : P.shade(H(col), -0.04));
      for (let s = 0; s < seg; s++) {
        const aa = a0 + (a1 - a0) * s / seg, ab = a0 + (a1 - a0) * (s + 1) / seg;
        mb.quad(ptR(r0, aa, yTop), ptR(r0, ab, yTop), ptR(r1, ab, yTop), ptR(r1, aa, yTop), [0, 1, 0]);
        mb.quad(ptR(r0, aa, yLow), ptR(r0, ab, yLow), ptR(r0, ab, yTop), ptR(r0, aa, yTop), [-Math.sin((aa + ab) / 2), 0, Math.cos((aa + ab) / 2)]);
      }
    }
  }
  // 弧形墙：r 处、高 y0..y1、厚 t
  function arcWall(mb, r, t, y0, y1, a0, a1, seg, col) {
    C(mb, col);
    for (let s = 0; s < seg; s++) {
      const aa = a0 + (a1 - a0) * s / seg, ab = a0 + (a1 - a0) * (s + 1) / seg, am = (aa + ab) / 2;
      const n = [-Math.sin(am), 0, Math.cos(am)];
      mb.quad(ptR(r, aa, y0), ptR(r, ab, y0), ptR(r, ab, y1), ptR(r, aa, y1), n);                       // 内侧
      mb.quad(ptR(r + t, ab, y0), ptR(r + t, aa, y0), ptR(r + t, aa, y1), ptR(r + t, ab, y1), [-n[0], 0, -n[2]]); // 外侧
      mb.quad(ptR(r, aa, y1), ptR(r, ab, y1), ptR(r + t, ab, y1), ptR(r + t, aa, y1), [0, 1, 0]);        // 顶
    }
  }
  // 拱：两墩 + 五块拱石（体块）
  function arch(mb, x, y0, z, w, h, d, col, seg) {
    seg = seg || 5;
    const pw = Math.max(0.35, w * 0.16), spring = h - w / 2;
    C(mb, col);
    mb.boxOn([x - w / 2 - pw / 2, y0, z], [pw, spring, d]);
    mb.boxOn([x + w / 2 + pw / 2, y0, z], [pw, spring, d]);
    for (let i = 0; i < seg; i++) {
      const a = Math.PI * (i + 0.5) / seg, r = w / 2 + pw / 2;
      mb.push().translate(x - Math.cos(a) * r, y0 + spring + Math.sin(a) * r, z).rotate(0, 0, 90 - a / D2R);
      mb.box([0, 0, 0], [pw, Math.PI * r / seg + 0.05, d]);
      mb.pop();
    }
  }
  function column(mb, x, y0, z, r, h, col, capCol) {
    C(mb, col); mb.cylinder([x, y0, z], r * 1.15, r, h, 8, false, false, true);
    C(mb, capCol || col); mb.boxOn([x, y0 + h, z], [r * 2.8, r * 0.7, r * 2.8]);
  }
  function figurePeg(mb, x, y, z, col, s) {
    s = s || 1;
    C(mb, col); mb.cylinder([x, y, z], 0.22 * s, 0.17 * s, 0.62 * s, 5, false, false, true);
    C(mb, '#e2b48e'); mb.box([x, y + 0.75 * s, z], [0.24 * s, 0.26 * s, 0.24 * s], null, 'tfkrl');
  }
  function banner(mb, x, y, z, col, h) {
    h = h || 4;
    C(mb, '#553a2b'); mb.boxOn([x, y, z], [0.1, h, 0.1]);
    C(mb, col); mb.quadTwoSided([x, y + h - 0.2, z], [x + 1.1, y + h - 0.35, z], [x + 1.0, y + h - 2.0, z], [x, y + h - 1.9, z]);
  }

  // ------------------------------------------------------------------ 门面与地标的体块配方 --
  // 每个配方在 φ = 0 的扇区坐标中建：门面在 z ≈ −23（正对擂台），地标（crown）在看台之后 z ≈ −44 … −60
  const R = {};
  R.gate = (mb, o) => {           // 汉阙 + 门殿 + 君主看台；冠：平乐观「十二重五采华盖」
    for (const s of [-1, 1]) {
      C(mb, PAL.rammed); mb.taperedBox([s * 3.4, L.floorY, -23.5], [2.2, 7.4, 2.2], 0.12);
      C(mb, PAL.hanRed); mb.boxOn([s * 3.4, L.floorY + 7.4, -23.5], [2.4, 0.6, 2.4]);
      C(mb, PAL.tileSlate); mb.push().translate(s * 3.4, L.floorY + 8.0, -23.5); mb.roof([0, 0, 0], 2.6, 2.6, 1.1, 0.35, 0.12, true); mb.pop();
    }
    C(mb, PAL.hanRedDark); mb.boxOn([0, L.floorY, -24.6], [4.2, 5.2, 1.6]);
    C(mb, '#3a2618'); mb.box([0, L.floorY + 1.9, -23.75], [2.2, 3.8, 0.1]);
    C(mb, PAL.plaster); mb.boxOn([0, L.floorY + 5.2, -25], [7.4, 2.6, 3.2]);                 // 君主看台
    C(mb, PAL.tileSlate); mb.push().translate(0, L.floorY + 7.8, -25); mb.roof([0, 0, 0], 8.2, 3.6, 1.6, 0.5, 0.14, true); mb.pop();
    // 华盖：竿 + 十二重伞盖（五采：青赤黄白黑循环）
    const five = ['#3f6f8a', '#c9503a', '#e3b34c', '#f0ead8', '#3a3134'];
    C(mb, '#553a2b'); mb.cylinder([0, L.standY1, -47], 0.35, 0.3, 26, 6);
    for (let i = 0; i < 12; i++) { C(mb, five[i % 5]); mb.cylinder([0, L.standY1 + 3 + i * 1.9, -47], 3.6 - i * 0.18, 0.4, 0.9, 10, false, true, true); }
  };
  R.hua = (mb) => {               // 门面：夯土墙（版筑分层）+ 垛口；冠：长城（芦苇夹层）与烽燧
    for (let i = 0; i < 6; i++) { C(mb, i % 2 ? PAL.rammed : PAL.rammedDark); mb.boxOn([0, L.floorY + i * 1.05, -23.6], [9, 1.05, 1.4]); }
    for (let x = -4; x <= 4; x += 1.6) { C(mb, PAL.rammed); mb.boxOn([x, L.floorY + 6.3, -23.2], [0.8, 0.8, 0.6]); }
    C(mb, PAL.rammed); mb.taperedBox([-3, L.standY1, -48], [5, 13, 5], 0.2);                      // 烽燧
    C(mb, '#d9c189'); mb.boxOn([-3, L.standY1 + 13, -48], [3.4, 1.2, 3.4]);
    for (let s = -3; s <= 3; s++) { C(mb, s % 2 ? PAL.rammed : PAL.rammedDark); mb.boxOn([s * 3.2 + 4, L.standY1 - 1, -46 - Math.abs(s) * 0.6], [3.3, 4.2, 2.2]); }
  };
  R.steppe = (mb) => {            // 门面：穹庐两座 + 鹿石；冠：大穹庐与长旗
    for (const s of [-1, 1]) {
      C(mb, '#e2d2b0'); mb.cylinder([s * 2.6, L.floorY, -24.5], 2.2, 2.2, 1.6, 10);
      C(mb, '#d2c098'); mb.cone([s * 2.6, L.floorY + 1.6, -24.5], 2.35, 1.4, 10);
    }
    for (const x of [-4.4, -0.2, 4.4]) { C(mb, '#8a8478'); mb.boxOn([x, L.floorY, -23.2], [0.45, 3.0, 0.7]); C(mb, '#a99f92'); mb.boxOn([x, L.floorY + 2.2, -22.85], [0.46, 0.25, 0.05]); }
    C(mb, '#e8dcc0'); mb.cylinder([0, L.standY1, -48], 8, 8, 4.2, 14);
    C(mb, '#cdb88a'); mb.cone([0, L.standY1 + 4.2, -48], 8.4, 4.8, 14);
    for (const s of [-1, 1]) banner(mb, s * 9.5, L.standY1, -45, '#3a2a1a', 12);
  };
  R.oasis = (mb) => {             // 门面：交河式夯土崖城（门洞）；冠：土坯佛塔（方基 + 覆钵）
    C(mb, '#c8a878'); mb.boxOn([0, L.floorY, -24], [9, 6.4, 2.4]);
    C(mb, '#5a4232'); for (const x of [-2.8, 0, 2.8]) mb.box([x, L.floorY + 1.4, -22.78], [1.2, 2.8, 0.05]);
    C(mb, '#d8b888'); mb.boxOn([0, L.standY1, -48], [10, 5, 10]);
    C(mb, '#c8a878'); mb.boxOn([0, L.standY1 + 5, -48], [7.4, 3, 7.4]);
    C(mb, '#d2b282'); mb.sphere([0, L.standY1 + 8, -48], [3.4, 3.2, 3.4], 10, 5, false);
  };
  R.judges = (mb) => {            // 裁判台：两层楼阁 + 建鼓 + 漏壶
    C(mb, PAL.plaster); mb.boxOn([0, L.floorY, -24.5], [8, 3.4, 3]);
    C(mb, PAL.tileSlate); mb.push().translate(0, L.floorY + 3.4, -24.5); mb.roof([0, 0, 0], 8.6, 3.4, 1.3, 0.5, 0.12, true); mb.pop();
    C(mb, PAL.hanRed); mb.boxOn([0, L.floorY + 4.6, -24.8], [5.6, 2.2, 2.4]);
    C(mb, PAL.tileSlate); mb.push().translate(0, L.floorY + 6.8, -24.8); mb.roof([0, 0, 0], 6.4, 2.8, 1.2, 0.45, 0.12, true); mb.pop();
    C(mb, '#553a2b'); mb.cylinder([5.2, L.floorY, -22.5], 0.12, 0.12, 5.8, 6);                        // 建鼓
    C(mb, PAL.hanRed); mb.push().translate(5.2, L.floorY + 2.8, -22.5).rotate(90, 0, 0); mb.cylinder([0, -0.7, 0], 1.0, 1.0, 1.4, 12); mb.pop();
    for (let i = 0; i < 3; i++) { C(mb, PAL.brass); mb.cylinder([-5.2 + i * 0.25, L.floorY + i * 1.2, -22.8 + i * 0.1], 0.45, 0.5, 1.0, 8); }   // 漏壶
    C(mb, PAL.rammed); mb.boxOn([0, L.standY1, -47], [6, 9, 6]);                                      // 鼓楼
    C(mb, PAL.tileSlate); mb.push().translate(0, L.standY1 + 9, -47); mb.roof([0, 0, 0], 7, 7, 2.2, 0.6, 0.14, true); mb.pop();
  };
  R.persia = (mb) => {            // 门面：哈特拉式大伊万（筒拱敞厅）+ 两侧小伊万；冠：带阶梯雉堞的神殿体块
    C(mb, '#d8c8a0'); mb.boxOn([0, L.floorY, -25], [9.4, 8.4, 2.4]);
    C(mb, '#5a4a3a'); mb.box([0, L.floorY + 2.9, -23.78], [3.8, 5.8, 0.05]);
    arch(mb, 0, L.floorY, -23.6, 3.8, 7.6, 0.8, '#e6d6b4', 6);
    for (const s of [-1, 1]) { C(mb, '#5a4a3a'); mb.box([s * 3.5, L.floorY + 1.6, -23.78], [1.5, 3.2, 0.05]); arch(mb, s * 3.5, L.floorY, -23.6, 1.5, 4.2, 0.6, '#e6d6b4', 4); }
    C(mb, '#d8c8a0'); mb.boxOn([0, L.standY1, -48], [14, 9, 6]);
    for (let x = -6.5; x <= 6.5; x += 1.3) { C(mb, '#c8b890'); mb.boxOn([x, L.standY1 + 9, -45.2], [0.8, 0.9, 0.5]); mb.boxOn([x, L.standY1 + 9.9, -45.2], [0.45, 0.5, 0.5]); }
  };
  R.north = (mb) => {             // 门面：木栅 + 圆屋（茅草锥顶）；冠：巨石阵（三石塔一圈）
    for (let x = -4.4; x <= 4.4; x += 0.55) { C(mb, x % 1.1 ? '#8a6a4a' : '#7a5a3e'); mb.cylinder([x, L.floorY, -23.4], 0.16, 0.14, 3.2 + (Math.abs(x * 7) % 3) * 0.2, 5); }
    C(mb, '#d8ccb0'); mb.cylinder([0, L.floorY, -25], 3.2, 3.2, 2.2, 12);
    C(mb, '#b89a5a'); mb.cone([0, L.floorY + 2.2, -25], 3.7, 3.6, 12);
    for (let i = 0; i < 10; i++) {
      const a = i / 10 * Math.PI * 2, x = Math.sin(a) * 7, z = -50 + Math.cos(a) * 5;
      C(mb, '#a19ea9'); mb.boxOn([x - 0.8, L.standY1, z], [1.1, 5, 0.9]); mb.boxOn([x + 0.8, L.standY1, z], [1.1, 5, 0.9]);
      C(mb, '#b2aeb8'); mb.boxOn([x, L.standY1 + 5, z], [2.9, 0.8, 1.0]);
    }
  };
  R.roman = (mb, o) => {          // 门面：斗兽场式两层拱廊（石灰华色、壁柱）；冠：顶层阁楼 + 帆布帷顶的桅杆 + 赫拉克勒斯（康茂德头像）巨像
    C(mb, '#e2d6bc'); mb.boxOn([0, L.floorY, -24.2], [9.4, 8.6, 1.8]);
    for (let k = 0; k < 2; k++) for (let i = -1; i <= 1; i++) {
      C(mb, '#3a3134'); mb.box([i * 3, L.floorY + k * 4.2 + 1.6, -23.28], [1.9, 3.2, 0.05]);
      arch(mb, i * 3, L.floorY + k * 4.2, -23.1, 1.9, 3.6, 0.5, '#efe4c8', 4);
    }
    C(mb, '#d8cbb0'); mb.boxOn([0, L.floorY + 8.6, -24.2], [9.6, 0.5, 2.2]);
    C(mb, '#e2d6bc'); mb.boxOn([0, L.standY1, -43.5], [17, 6, 2]);
    for (let x = -7.5; x <= 7.5; x += 3) { C(mb, '#6e4b36'); mb.cylinder([x, L.standY1 + 6, -43.2], 0.14, 0.12, 6, 5); }
    C(mb, '#f0e6d0'); mb.quadTwoSided([-8, L.standY1 + 11.5, -43], [8, L.standY1 + 11.5, -43], [9, L.standY1 + 9.5, -36], [-9, L.standY1 + 9.5, -36]);
    // 巨像：铜色体块（狮皮、棒）
    C(mb, '#b07a3a'); mb.boxOn([0, L.standY1 + 6, -52], [3.2, 1.2, 3.2]);
    mb.capsule([0, L.standY1 + 7.2, -52], 1.3, 11, 8, false);
    mb.sphere([0, L.standY1 + 19.6, -52], 1.25, 8, 6, false);
    C(mb, '#8a5a2a'); mb.segment([1.6, L.standY1 + 14, -51.6], [3.4, L.standY1 + 9, -50.6], 0.35, 0.55, 6);
  };
  R.nations = (mb) => {           // 万国之门：四根高柱（牛首柱头）+ 两对人首翼牛 + 石门框（选手入场处）
    for (const x of [-4.2, -1.4, 1.4, 4.2]) { column(mb, x, L.floorY, -25, 0.55, 12.5, '#d8cbb0', '#c8b890'); C(mb, '#b8a888'); mb.boxOn([x, L.floorY + 12.9, -25], [2.4, 0.9, 0.9]); }
    C(mb, '#cfc2a6'); mb.boxOn([0, L.floorY + 13.8, -25], [10, 0.9, 1.6]);
    for (const s of [-1, 1]) {
      C(mb, '#c6b89a'); mb.boxOn([s * 2.6, L.floorY, -23], [1.6, 2.4, 3.4]);                           // 牛身
      mb.boxOn([s * 2.6, L.floorY + 2.4, -21.9], [1.3, 1.4, 1.1]);                                      // 人首
      C(mb, '#b8a888'); mb.box([s * 3.4, L.floorY + 2.6, -23.4], [0.3, 1.8, 2.8]);                      // 翼
      C(mb, '#3a3134'); mb.boxOn([s * 2.6, L.floorY + 3.8, -21.9], [1.4, 0.7, 1.2]);                    // 冠
    }
    C(mb, '#3a2f42'); mb.box([0, L.floorY + 3.2, -24.95], [2.6, 6.4, 0.05]);
  };
  R.nile = (mb) => {              // 门面：麦罗埃式陡峭小金字塔三座（≈70°）+ 塔门祠堂；冠：法罗斯灯塔（远景、夜里点火）
    for (const [x, hh] of [[-3.2, 6.8], [0.4, 8.2], [3.6, 6.2]]) { C(mb, '#b9875a'); mb.taperedBox([x, L.floorY, -25], [hh * 0.55, hh, hh * 0.55], 0.94); C(mb, '#a8764a'); mb.taperedBox([x, L.floorY, -25 + hh * 0.3], [hh * 0.4, hh * 0.3, 0.8], 0.2); }
    // 法罗斯：方塔 / 八角 / 圆亭 + 火
    const z = -64, y0 = L.floorY;
    C(mb, '#e8dcc4'); mb.taperedBox([6, y0, z], [10, 26, 10], 0.18);
    C(mb, '#e2d4b8'); mb.cylinder([6, y0 + 26, z], 3.8, 3.4, 12, 8);
    C(mb, '#dccdb0'); mb.cylinder([6, y0 + 38, z], 2.0, 1.8, 5, 8);
    C(mb, '#ffb65c'); mb.emission = 1; mb.sphere([6, y0 + 44.2, z], 1.4, 8, 5, false); mb.emission = 0;
  };
  R.arabia = (mb) => {            // 门面：佩特拉宝库（玫瑰色崖面上的两层柱廊）；冠：玫瑰色砂岩崖
    C(mb, '#c9785a'); mb.boxOn([0, L.floorY, -25.2], [9.6, 9.2, 2.6]);
    for (const x of [-3, -1.5, 1.5, 3]) column(mb, x, L.floorY + 0.6, -23.7, 0.28, 3.8, '#e0a080', '#d89070');
    C(mb, '#e0a080'); mb.push().translate(0, L.floorY + 4.9, -23.7); mb.roof([0, 0, 0], 7.2, 0.6, 1.0, 0.1, 0.2, true); mb.pop();
    C(mb, '#d89070'); mb.cylinder([0, L.floorY + 6.1, -23.9], 0.9, 0.9, 2.3, 8); mb.cone([0, L.floorY + 8.4, -23.9], 1.0, 0.7, 8);
    for (const x of [-3.2, 3.2]) column(mb, x, L.floorY + 6.1, -23.9, 0.24, 2.1, '#e0a080');
    C(mb, '#3a2622'); mb.box([0, L.floorY + 1.6, -23.88], [1.2, 2.6, 0.05]);
    for (let i = 0; i < 4; i++) { C(mb, i % 2 ? '#b9684a' : '#c9785a'); mb.blob([-8 + i * 5.4, L.standY1 + 2, -48 - (i % 2) * 3], [4.6, 9 + (i % 3) * 2, 3.8], 1, 0.12, 30 + i, true); }
  };
  R.indic = (mb) => {             // 门面：桑奇式陀兰那（两柱 + 三道横梁 + 象头柱头）；冠：覆钵大塔（鼓座、平头、三重伞盖）
    for (const s of [-1, 1]) { C(mb, '#d8b890'); mb.boxOn([s * 2.4, L.floorY, -23.4], [0.7, 6.2, 0.7]); C(mb, '#c8a880'); mb.boxOn([s * 2.4, L.floorY + 6.2, -23.4], [1.1, 0.9, 0.9]); }
    for (let k = 0; k < 3; k++) { C(mb, k % 2 ? '#c8a880' : '#d8b890'); mb.push().translate(0, L.floorY + 7.3 + k * 0.9, -23.4); mb.box([0, 0, 0], [6.6 + (k === 0 ? 0.6 : 0), 0.45, 0.6]); mb.pop(); }
    C(mb, '#e2d0b0'); mb.cylinder([0, L.standY1 - 2, -50], 10.4, 10.4, 2, 16);
    C(mb, '#ecdcc0'); mb.sphere([0, L.standY1, -50], [9.6, 8.4, 9.6], 16, 8, false);
    C(mb, '#d8c4a0'); mb.boxOn([0, L.standY1 + 8.2, -50], [2.6, 1.6, 2.6]);
    C(mb, '#553a2b'); mb.cylinder([0, L.standY1 + 9.8, -50], 0.15, 0.15, 3.2, 5);
    for (let k = 0; k < 3; k++) { C(mb, '#d8c4a0'); mb.cylinder([0, L.standY1 + 10.4 + k * 0.9, -50], 1.6 - k * 0.35, 1.5 - k * 0.35, 0.2, 10); }
  };
  R.podium = (mb) => {            // 奖台：三级领奖台 + 铜鼎 + 奖品台；冠：橄榄树（奥林匹亚的野橄榄冠之树）与胜利长旗
    C(mb, '#d8cbb0'); mb.boxOn([0, L.floorY, -24], [2.4, 2.2, 2.4]); mb.boxOn([-2.6, L.floorY, -24], [2.4, 1.6, 2.4]); mb.boxOn([2.6, L.floorY, -24], [2.4, 1.1, 2.4]);
    C(mb, PAL.hanRed); mb.boxOn([0, L.floorY, -26], [9, 0.4, 2]);
    for (const s of [-1, 1]) { C(mb, '#7a6a3a'); mb.cylinder([s * 5.2, L.floorY, -23], 0.9, 1.1, 1.3, 10); mb.cylinder([s * 5.2, L.floorY + 1.3, -23], 1.15, 1.2, 0.2, 10); }
    C(mb, '#e3b34c'); mb.boxOn([0, L.floorY + 2.2, -24], [0.7, 0.9, 0.7]);
    C(mb, '#7a6248'); mb.cylinder([0, L.standY1, -47], 0.8, 0.6, 5, 7);
    C(mb, '#8aa070'); mb.softLump([0, L.standY1 + 7.5, -47], [6, 4.2, 6], 1, 0, LP.canopyRamp(H('#9db866'), H('#7d9a55'), H('#4e7440'), L.standY1 + 4, L.standY1 + 11));
  };
  R.nanhai = (mb) => {            // 门面：干栏高脚屋 + 铜鼓台（东山铜鼓，鼓面太阳纹）；冠：扶南砖砌神祠与棕榈
    for (const x of [-3.6, -1.2, 1.2, 3.6]) { C(mb, '#6e4b36'); mb.cylinder([x, L.floorY, -24.8], 0.14, 0.14, 2.6, 5); }
    C(mb, '#9a7050'); mb.boxOn([0, L.floorY + 2.6, -24.8], [8.6, 0.3, 3]);
    C(mb, '#be9468'); mb.boxOn([0, L.floorY + 2.9, -24.8], [7, 2.2, 2.4]);
    C(mb, '#c9a04a'); mb.push().translate(0, L.floorY + 5.1, -24.8); mb.roof([0, 0, 0], 8.8, 3.4, 2.6, 0.6, 0.15, true); mb.pop();
    C(mb, '#a0783a'); mb.cylinder([0, L.floorY, -22.6], 1.2, 1.3, 1.1, 14);
    C(mb, '#c9a04a'); mb.disc([0, L.floorY + 1.11, -22.6], 1.3, 14);
    C(mb, '#b06a4a'); mb.taperedBox([-2, L.standY1, -48], [6, 11, 6], 0.35); mb.cone([-2, L.standY1 + 11, -48], 2.2, 3, 8);
    for (const x of [5, 8]) { C(mb, '#7a6248'); mb.cylinder([x, L.standY1, -46], 0.3, 0.22, 9, 5); C(mb, '#4e7440'); mb.cone([x, L.standY1 + 9, -46], 3, 1.2, 7); }
  };
  R.wa = (mb) => {                // 门面：城栅 + 楼观（吉野里式望楼）+ 高床仓；冠：高床大殿（主祭殿）
    for (let x = -4.4; x <= 4.4; x += 0.5) { C(mb, '#8a6a4a'); mb.boxOn([x, L.floorY, -22.9], [0.18, 3.0 + (Math.abs(x * 3) % 2) * 0.3, 0.18]); }
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) { C(mb, '#6e4b36'); mb.boxOn([-2.4 + sx * 1.1, L.floorY, -24.4 + sz * 1.1], [0.3, 7.6, 0.3]); }
    C(mb, '#9a7050'); mb.boxOn([-2.4, L.floorY + 7.6, -24.4], [2.9, 0.3, 2.9]);
    C(mb, '#c8a868'); mb.push().translate(-2.4, L.floorY + 8.6, -24.4); mb.roof([0, 0, 0], 2.8, 2.8, 1.8, 0.5, 0.15, true); mb.pop();
    for (const sx of [-1, 1]) { C(mb, '#6e4b36'); mb.boxOn([2.8 + sx * 0.9, L.floorY, -24.4], [0.25, 2.2, 0.25]); }
    C(mb, '#be9468'); mb.boxOn([2.8, L.floorY + 2.2, -24.4], [2.6, 1.6, 2.2]);
    C(mb, '#c8a868'); mb.push().translate(2.8, L.floorY + 3.8, -24.4); mb.roof([0, 0, 0], 3, 2.6, 1.8, 0.4, 0.15, true); mb.pop();
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) { C(mb, '#6e4b36'); mb.boxOn([sx * 4, L.standY1, -48 + sz * 3], [0.5, 4, 0.5]); }
    C(mb, '#be9468'); mb.boxOn([0, L.standY1 + 4, -48], [9, 3, 7]);
    C(mb, '#c8a868'); mb.push().translate(0, L.standY1 + 7, -48); mb.roof([0, 0, 0], 10, 8, 5.5, 0.9, 0.2, true); mb.pop();
  };
  R.han3 = (mb) => {              // 门面：高句丽式石城墙（楔形石层砌、门道）；冠：阶梯式积石冢与松
    for (let i = 0; i < 7; i++) { C(mb, i % 2 ? '#a99f92' : '#9a9184'); mb.boxOn([0, L.floorY + i * 0.9, -23.6 - i * 0.12], [9 - i * 0.1, 0.9, 1.6]); }
    C(mb, '#3a3134'); mb.box([0, L.floorY + 1.6, -22.79], [1.6, 3.2, 0.05]);
    for (let k = 0; k < 4; k++) { C(mb, k % 2 ? '#a99f92' : '#b8afa2'); mb.boxOn([0, L.standY1 + k * 2.2, -49], [12 - k * 2.6, 2.2, 12 - k * 2.6]); }
    for (const x of [-9, 9]) { C(mb, '#6e4b36'); mb.cylinder([x, L.standY1, -46], 0.3, 0.2, 4, 5); C(mb, '#3f6146'); mb.cone([x, L.standY1 + 3, -46], 2.2, 5, 7); }
  };

  // ------------------------------------------------------------------ 组装 --
  function sectorMesh(slot, o) {
    const a0 = -L.slot / 2 * D2R, a1 = L.slot / 2 * D2R;
    const mb = new LP.MeshBuilder(17 + slot.phi);
    mb.jitter = 0.05;
    // 看台前墙（门面之后、高 6 m）、看台、外圈拱廊
    arcWall(mb, L.podiumR - 0.6, 0.6, L.floorY, L.podiumH, a0, a1, 4, slot.family ? '#c6bdae' : '#b9a58c');
    standRows(mb, a0, a1, 4, '#bfb4a2');
    arcWall(mb, L.wallR, 1.4, L.floorY, L.wallH, a0, a1, 4, '#d8cbb0');
    for (let s = 0; s <= 4; s++) { const a = a0 + (a1 - a0) * s / 4; const p = ptR(L.wallR + 1.5, a, L.floorY); C(mb, '#cfc2a6'); mb.boxOn(p, [1.1, L.wallH - L.floorY, 1.1]); }
    // 势力旗：看台顶沿（家族扇区每区 4 面）
    if (slot.family) for (let s = 0; s < 4; s++) { const a = a0 + (a1 - a0) * (s + 0.5) / 4, p = ptR(L.wallR - 0.4, a, L.wallH); banner(mb, p[0], p[1], p[2], o.banners[(s + Math.floor(slot.phi / 22.5) * 3) % o.banners.length], 4.2); }
    // 门面与地标：配方另建一个网格；门面部分（z > −32）收窄到 ×0.72 并前移 1.4 m（立在看台前墙之前的沙地上，两侧露出观众）
    let lgeo = null;
    if (R[slot.id]) {
      const lb = new LP.MeshBuilder(29 + slot.phi);
      lb.jitter = 0.05;
      R[slot.id](lb, o);
      lgeo = lb.toGeometry();
      const pos = lgeo.attributes.position, sm = lgeo.attributes.lpSmooth;
      for (let i = 0; i < pos.count; i++) {
        const z = pos.getZ(i);
        if (z > -32) { pos.setX(i, pos.getX(i) * 0.72); pos.setZ(i, z + 1.4); }
        else if (pos.getY(i) < L.wallH - 3) pos.setY(i, pos.getY(i) + (L.wallH - 3 - L.standY1));
      }
      pos.needsUpdate = true;
      if (sm) sm.needsUpdate = true;
      lgeo.computeBoundingSphere();
    }
    // 灯笼：门面前沿与看台前沿（夜里点亮；每区 3 盏，取最近的 12 盏为点光源）
    const lamps = [];
    for (let s = 0; s < 3; s++) {
      const a = a0 + (a1 - a0) * (s + 0.5) / 3, p = ptR(L.podiumR - 0.9, a, L.podiumH + 0.9);
      mb.ribLantern(p, 0.32, 0.55, PAL.paper, !!o.night, PAL.gold, PAL.vermilion, 8, 0.2);
      lamps.push(p);
    }
    const geo = mb.toGeometry();
    // 观众（钉形小人，无墨线）
    let cgeo = null;
    if (o.crowd !== false) {
      const cb = new LP.MeshBuilder(91 + slot.phi);
      cb.jitter = 0.08; cb.aoStrength = 0;
      const pal = (CROWD[slot.family || slot.id] || CROWD.hua).map(H);
      const r = SG.SeededRandom(1000 + slot.phi * 10);
      const density = o.phone ? 0.38 : 0.85;
      for (let i = 0; i < L.rows; i++) {
        if (i === 5 || i === 10) continue;
        const rr = L.standR0 + (L.standR1 - L.standR0) * (i + 0.55) / L.rows;
        const y = L.standY0 + (L.standY1 - L.standY0) * (i + 1) / L.rows;
        const n = Math.floor(rr * L.slot * D2R / 0.72);
        for (let k = 0; k < n; k++) {
          if (r.nextDouble() > density) continue;
          const a = a0 + (a1 - a0) * (k + 0.5) / n;
          const p = ptR(rr, a, y);
          figurePeg(cb, p[0], p[1], p[2], pal[r.next(pal.length)], 0.95 + r.nextDouble() * 0.12);
        }
      }
      cgeo = cb.toGeometry();
    }
    return { geo, lgeo, cgeo, lamps };
  }

  function rotY(p, phi) { const a = -phi * D2R, c = Math.cos(a), s = Math.sin(a); return [p[0] * c + p[2] * s, p[1], -p[0] * s + p[2] * c]; }

  function build(o) {
    o = Object.assign({ night: false, crowd: true, phone: false, banners: ['#7a2a3a', '#2f6db5', '#2c3d8f', '#2f9a55', '#c8382c', '#c99a2e', '#e0e0e8', '#3aa5a0', '#8a5fc0', '#c2702a'] }, o || {});
    const root = new THREE.Group();
    root.name = 'Stadium';
    const group = new THREE.Group();     // 转动的外环（看台、门面、地标、观众）；擂台与沙地不转
    group.name = 'StadiumRing';
    root.add(group);
    const sectors = [];
    let tris = 0, crowdTris = 0;
    const lamps = [];
    for (const slot of SLOTS) {
      const s = sectorMesh(slot, o);
      const m = LP.mesh(s.geo, { outline: true });
      m.rotation.y = -slot.phi * D2R;
      m.name = 'sector:' + slot.id;
      group.add(m);
      if (s.lgeo) { const lm = LP.mesh(s.lgeo, { outline: true }); lm.rotation.y = -slot.phi * D2R; lm.name = 'landmark:' + slot.id; group.add(lm); }
      let cm = null;
      if (s.cgeo) { cm = LP.mesh(s.cgeo, { outline: false, castShadow: false }); cm.rotation.y = -slot.phi * D2R; cm.name = 'crowd:' + slot.id; group.add(cm); }
      sectors.push({ id: slot.id, name: slot.name, mesh: m, crowd: cm, tris: tri(s.geo) + tri(s.lgeo), shellTris: tri(s.geo), landmarkTris: tri(s.lgeo), crowdTris: tri(s.cgeo) });
      tris += tri(s.geo) + tri(s.lgeo); crowdTris += tri(s.cgeo);
      for (const p of s.lamps) lamps.push(rotY(p, slot.phi));
    }
    // 场心：沙地、擂台、外部地面
    const mb = new LP.MeshBuilder(5);
    C(mb, '#e2cfa6'); mb.push().translate(0, L.floorY, 0); mb.lathe([[0, 0], [L.floorR, 0]], 40, false, false, false); mb.pop();
    C(mb, '#d2bc90'); mb.push().translate(0, L.floorY + 0.01, 0); mb.lathe([[11.5, 0], [12.3, 0]], 40, false, false, false); mb.pop();
    C(mb, '#a99f92'); mb.boxOn([0, L.floorY, 0], [L.stageW, L.stageH, L.stageD]);
    C(mb, '#c6bdae'); mb.box([0, -0.06, 0], [L.stageW - 0.6, 0.12, L.stageD - 0.6], null, 't');
    C(mb, PAL.hanRed); mb.box([0, -0.05, 0], [L.stageW - 2, 0.1, 0.35], null, 't');
    for (let x = -8.4; x <= 8.4; x += 1.2) { C(mb, '#d8cbb0'); mb.boxOn([x, 0, -4.3], [0.18, 0.75, 0.18]); }
    C(mb, '#d8cbb0'); mb.boxOn([0, 0.62, -4.3], [17, 0.14, 0.2]);
    for (let k = 0; k < 3; k++) { C(mb, '#b8afa2'); mb.boxOn([0, L.floorY, L.stageD / 2 + 0.5 + k * 0.5], [6, (3 - k) * 0.4, 0.5]); }
    const core = mb.toGeometry();
    root.add(LP.mesh(core, { outline: true }));
    const gb = new LP.MeshBuilder(6);
    C(gb, '#9db866'); gb.push().translate(0, L.floorY - 0.02, 0); gb.lathe([[0, 0], [160, 0]], 24, false, false, false); gb.pop();
    root.add(LP.mesh(gb.toGeometry(), { outline: false, castShadow: false }));
    tris += tri(core);
    return { root, group, sectors, lamps, tris, crowdTris, coreTris: tri(core) };
  }
  function face(group, slotId) {
    const s = SLOTS.find(x => x.id === slotId);
    if (s) group.rotation.y = s.phi * D2R;
    return s;
  }

  SG.StadiumBlockout = { SLOTS, L, CROWD, build, face, recipes: R };
})();

// ------------------------------------------------------------------ Node：预算表 --
if (typeof module !== 'undefined' && require.main === module) {
  const path = require('path'), fs = require('fs'), vm = require('vm');
  const ROOT = path.join(__dirname, '..', '..');
  global.window = global; global.self = global;
  const fakeCtx = new Proxy({}, { get: (t, k) => (k in t ? t[k] : k === 'measureText' ? () => ({ width: 10 }) : (k === 'getImageData' || k === 'createImageData') ? (...a) => ({ data: new Uint8ClampedArray(4 * (a[2] || 1) * (a[3] || 1)) }) : (k === 'createLinearGradient' || k === 'createRadialGradient' || k === 'createPattern') ? () => ({ addColorStop() {} }) : () => {}), set: (t, k, v) => { t[k] = v; return true; } });
  global.document = { createElement: () => ({ getContext: () => fakeCtx, style: {}, width: 0, height: 0 }) };
  const load = f => vm.runInThisContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), { filename: f });
  const warn = console.warn; console.warn = () => {};
  load('vendor/three.min.js'); console.warn = warn;
  load('js/core.js'); load('js/lowpoly.js');
  vm.runInThisContext(fs.readFileSync(__filename, 'utf8').replace(/^'use strict';/, '').split('// ------------------------------------------------------------------ Node：预算表')[0], { filename: 'stadium-blockout.js' });
  for (const phone of [false, true]) {
    const S = global.SG.StadiumBlockout.build({ phone });
    console.log(`== ${phone ? 'phone' : 'desktop'}: static ${S.tris} tris (core ${S.coreTris}), crowd ${S.crowdTris} tris; with ink shells on static ≈ ${Math.round(S.tris * 2 + S.crowdTris)}`);
    if (!phone) console.log(S.sectors.map(s => `${s.name} ${s.shellTris}+${s.landmarkTris}/${s.crowdTris}`).join(' · '));
  }
}
