'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 各文化的城池模型（DESIGN-V2 §4G、§6）

   战略地图上的城池按所属文化换成不同的低多边形模型；汉地城池（han）仍用 SG.Models.cityInto
   （js/art.js）的原模型。与原模型相同的约定：Unity 坐标（x 东、z 北、y 上），size ≈ 占地半径 r，
   底座 2.3r × 2.3r，城墙在 ±r，城门朝南（−z）；旗帜由 map-view 另画在 (0.9r, 0, 0.9r) 一角（东北），
   该角保持空旷；标签高度 size × 1.6 + 0.6 —— 模型最高处不超过约 1.5r，标签、光圈、选中圈照旧对齐。
   capital = true 时为都城变体（更大的宫殿、神庙、竞技场等）。全部写入调用方的 SG.MeshBuilder
   （按区块合并网格），只用顶点色，不增加材质或贴图。每座约 300–1500 个三角形。

   文化 → 模型
     roman      罗马：石墙圆塔、红瓦坡顶民居、列柱神庙；都城加圆形竞技场
     persia     安息 / 波斯：土坯城墙、阶梯垛口、平顶房、伊万拱门宫殿、阶梯状内城；都城加大伊万与拜火祠
     arab       哈特拉 / 阿拉伯：圆形石城、圆塔、石砌神庙（伊万 + 列柱）、平顶房、椰枣树（阿克苏姆另立石碑）
     kushan     贵霜：砖城圆角堡、白色窣堵波（佛塔）、僧院；都城为大塔与宫殿
     wa         倭：环壕、木栅、高床仓库、望楼、竖穴住居；都城加大型高床殿
     korea      高句丽 / 三韩：依山石城（山丘 + 不规则石墙）、灰瓦殿阁、城门楼
     steppe     草原：毡帐营地、车阵、马尾纛；都城为金顶大帐
     tarim      西域绿洲：夯土城墙、平顶房、钻天杨、小佛塔与烽燧
     seasia     南海（林邑、扶南）：高脚屋、鞍形屋顶神殿、椰树、水渠
     yi、nanman 夷洲 / 南中：竹木栅栏围成的高脚屋村落
     celt       凯尔特：山丘堡垒（土垒 + 木栅）、圆形茅屋
     german     日耳曼：山丘堡垒、长屋；都城为大厅
     sarmatian  萨尔马提亚：篷车与尖顶帐；都城（潘提卡彭等希腊城）为卫城与神庙

   公开接口
     SG.CultureArt.cultures                        已有模型的文化代号
     SG.CultureArt.cultureOfCity(city)             city.culture → SG.WorldData.cultureOfCity(city.key) → 'han'
     SG.CultureArt.isCapital(city, culture?)       汉地：洛阳、长安；其他文化：世界剧本各势力的初始都城（SG.FactionInfo）
     SG.CultureArt.cityInto(mb, size, culture, capital, city?) → mb    写入 MeshBuilder；han / 未知文化用原模型
     SG.CultureArt.city(size, culture, capital)    → BufferGeometry（测试用）
   测试页：tests/culture-art.html（全部文化 × 普通 / 都城的模型一览，可旋转）。
   ========================================================================== */
(function () {
  const SG = window.SG;
  if (!SG || SG.CultureArt) return;   // 防止重复载入（map-view.js 在 index.html 未载入本文件时会补载）

  const H = hex => new THREE.Color(hex);
  const shade = (c, k) => SG.Gfx.shade(c, k);
  const v3 = (x, y, z) => new THREE.Vector3(x, y, z);

  // ============================================================ 基本形体 --
  // 凸多边形面：pts 为 Unity 坐标；按 hint（大致的外向方向）自动定正反（MeshBuilder 为单面材质）
  function face(mb, pts, c, hint) {
    if (hint) {
      const a = pts[0], b = pts[1], d = pts[2];
      const ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, wx = d.x - a.x, wy = d.y - a.y, wz = d.z - a.z;
      const nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
      if (nx * hint.x + ny * hint.y + nz * hint.z < 0) pts = pts.slice().reverse();
    }
    for (let i = 1; i + 1 < pts.length; i++) mb.tri(pts[0], pts[i], pts[i + 1], c);
  }
  // 双面薄片（旗、叶）
  function sheet(mb, pts, c) {
    for (let i = 1; i + 1 < pts.length; i++) { mb.tri(pts[0], pts[i], pts[i + 1], c); mb.tri(pts[0], pts[i + 1], pts[i], c); }
  }
  // 任意朝向的墙段：从 (ax, az) 到 (bx, bz)，厚 t，底 y0，高 h；可选每端高度不同（h2）
  function seg(mb, ax, az, bx, bz, y0, h, t, c, h2) {
    const dx = bx - ax, dz = bz - az, L = Math.hypot(dx, dz) || 1;
    const nx = -dz / L * t / 2, nz = dx / L * t / 2;
    const hb = h2 === undefined ? h : h2;
    const p = [v3(ax + nx, y0, az + nz), v3(bx + nx, y0, bz + nz), v3(bx - nx, y0, bz - nz), v3(ax - nx, y0, az - nz)];
    const q = [v3(ax + nx, y0 + h, az + nz), v3(bx + nx, y0 + hb, bz + nz), v3(bx - nx, y0 + hb, bz - nz), v3(ax - nx, y0 + h, az - nz)];
    const cx = (ax + bx) / 2, cz = (az + bz) / 2, cy = y0 + (h + hb) / 4;
    const out = (pts) => { let x = 0, y = 0, z = 0; for (const o of pts) { x += o.x; y += o.y; z += o.z; } const n = pts.length; return v3(x / n - cx, y / n - cy, z / n - cz); };
    const top = [q[0], q[1], q[2], q[3]];
    face(mb, top, shade(c, 0.08), v3(0, 1, 0));
    for (let i = 0; i < 4; i++) {
      const f = [p[i], p[(i + 1) % 4], q[(i + 1) % 4], q[i]];
      face(mb, f, c, out(f));
    }
  }
  // 双坡屋顶（脊沿 x 轴；axis = 'z' 时脊沿 z 轴）：中心 (x, y, z)，宽 w（沿脊）、深 d、高 h，出檐 e
  function gable(mb, x, y, z, w, d, h, c, axis, e) {
    e = e || 0;
    const along = axis === 'z';
    const hw = w / 2 + e, hd = d / 2 + e;
    const P = (a, b, yy) => along ? v3(x + b, yy, z + a) : v3(x + a, yy, z + b);
    const r0 = P(-hw, 0, y + h), r1 = P(hw, 0, y + h);
    const s0 = P(-hw, -hd, y), s1 = P(hw, -hd, y), n0 = P(-hw, hd, y), n1 = P(hw, hd, y);
    const up = v3(0, 1, 0);
    const sideA = along ? v3(-1, 0.6, 0) : v3(0, 0.6, -1), sideB = along ? v3(1, 0.6, 0) : v3(0, 0.6, 1);
    face(mb, [s0, s1, r1, r0], c, sideA);
    face(mb, [n0, r0, r1, n1], shade(c, -0.12), sideB);
    const endA = along ? v3(0, 0, -1) : v3(-1, 0, 0), endB = along ? v3(0, 0, 1) : v3(1, 0, 0);
    face(mb, [s0, r0, n0], shade(c, -0.25), endA);
    face(mb, [s1, n1, r1], shade(c, -0.25), endB);
    // 檐下（从下面看不到，省略）
    void up;
  }
  // 四坡（攒尖）顶：底面 w × d，顶点高 h；ridge > 0 时为庑殿（脊长 ridge × w）
  function hip(mb, x, y, z, w, d, h, c, ridge) {
    const hw = w / 2, hd = d / 2, rr = (ridge || 0) * w / 2;
    const a = v3(x - hw, y, z - hd), b = v3(x + hw, y, z - hd), cc = v3(x + hw, y, z + hd), dd = v3(x - hw, y, z + hd);
    const t0 = v3(x - rr, y + h, z), t1 = v3(x + rr, y + h, z);
    face(mb, rr > 0 ? [a, b, t1, t0] : [a, b, t0], c, v3(0, 0.5, -1));
    face(mb, rr > 0 ? [cc, dd, t0, t1] : [cc, dd, t0], shade(c, -0.12), v3(0, 0.5, 1));
    face(mb, [b, cc, t1], shade(c, -0.06), v3(1, 0.5, 0));
    face(mb, [dd, a, t0], shade(c, -0.18), v3(-1, 0.5, 0));
  }
  // 低多边形穹顶（半球，由几段圆台叠成）
  function dome(mb, x, y, z, r, c, seg, squash) {
    seg = seg || 8; squash = squash || 1;
    const rings = [[1, 0], [0.92, 0.38], [0.7, 0.72], [0.38, 0.93]];
    for (let i = 0; i < rings.length - 1; i++) {
      const [r0, y0] = rings[i], [r1, y1] = rings[i + 1];
      mb.cylinder(v3(x, y + y0 * r * squash, z), r0 * r, r1 * r, (y1 - y0) * r * squash, seg, shade(c, i * 0.04), false);
    }
    mb.cone(v3(x, y + 0.93 * r * squash, z), 0.38 * r, 0.07 * r * squash + 0.02, seg, shade(c, 0.12));
  }
  // 圆柱列
  function column(mb, x, y, z, h, r, c) {
    mb.cylinder(v3(x, y, z), r, r * 0.88, h, 6, c, true);
    mb.box(v3(x, y + h + r * 0.4, z), v3(r * 2.6, r * 0.8, r * 2.6), shade(c, 0.05));
  }
  // 一圈城墙（正多边形）：中心 (0, 0)，半径 R，n 边，高 h，厚 t；gate = true 时南面中间断开（城门另画）
  function ringWall(mb, R, n, h, t, c, rot, y0) {
    rot = rot || 0; y0 = y0 || 0;
    for (let i = 0; i < n; i++) {
      const a0 = rot + i * Math.PI * 2 / n, a1 = rot + (i + 1) * Math.PI * 2 / n;
      seg(mb, Math.cos(a0) * R, Math.sin(a0) * R, Math.cos(a1) * R, Math.sin(a1) * R, y0, h, t, c);
    }
  }
  // 方形城墙 + 垛口（merlon = 垛口大小，0 = 无）
  function squareWall(mb, r, h, t, c, merlon, step) {
    mb.box(v3(0, h / 2, -r), v3(r * 2, h, t), c);
    mb.box(v3(0, h / 2, r), v3(r * 2, h, t), c);
    mb.box(v3(-r, h / 2, 0), v3(t, h, r * 2), c);
    mb.box(v3(r, h / 2, 0), v3(t, h, r * 2), c);
    if (merlon > 0) {
      const n = step || 3, top = shade(c, 0.06);
      for (let i = -n; i <= n; i++) {
        const x = i * r / (n + 0.5);
        if (Math.abs(i) <= 0) continue;   // 城门上方留空
        mb.box(v3(x, h + merlon / 2, -r), v3(merlon * 1.1, merlon, t * 1.05), top);
        mb.box(v3(x, h + merlon / 2, r), v3(merlon * 1.1, merlon, t * 1.05), top);
        mb.box(v3(-r, h + merlon / 2, x), v3(t * 1.05, merlon, merlon * 1.1), top);
        mb.box(v3(r, h + merlon / 2, x), v3(t * 1.05, merlon, merlon * 1.1), top);
      }
    }
  }
  // 木栅：沿折线 pts（[x, z]…，闭合）的栅墙 + 尖桩；h 高，n 每段尖桩数
  function palisade(mb, pts, h, c, n, y0) {
    y0 = y0 || 0; n = n || 5;
    const t = 0.05;
    for (let i = 0; i < pts.length; i++) {
      const a = pts[i], b = pts[(i + 1) % pts.length];
      seg(mb, a[0], a[1], b[0], b[1], y0, h, t, c);
      const dx = b[0] - a[0], dz = b[1] - a[1], L = Math.hypot(dx, dz);
      const k = Math.max(2, Math.round(n * L));
      for (let j = 0; j < k; j++) {
        const u0 = j / k, u1 = (j + 1) / k, um = (u0 + u1) / 2;
        sheet(mb, [v3(a[0] + dx * u0, y0 + h, a[1] + dz * u0), v3(a[0] + dx * um, y0 + h + 0.09, a[1] + dz * um), v3(a[0] + dx * u1, y0 + h, a[1] + dz * u1)], shade(c, 0.05));
      }
    }
  }
  // 环壕：平放的水面方环（内半径 ri，外半径 ro，正方形）
  function moat(mb, ri, ro, y, c) {
    const up = v3(0, 1, 0);
    face(mb, [v3(-ro, y, -ro), v3(ro, y, -ro), v3(ro, y, -ri), v3(-ro, y, -ri)], c, up);
    face(mb, [v3(-ro, y, ri), v3(ro, y, ri), v3(ro, y, ro), v3(-ro, y, ro)], c, up);
    face(mb, [v3(-ro, y, -ri), v3(-ri, y, -ri), v3(-ri, y, ri), v3(-ro, y, ri)], c, up);
    face(mb, [v3(ri, y, -ri), v3(ro, y, -ri), v3(ro, y, ri), v3(ri, y, ri)], c, up);
  }
  // 椰树 / 椰枣树
  function palm(mb, x, z, s, seed, y0) {
    y0 = y0 || 0;
    const rnd = SG.SeededRandom(seed | 0);
    const h = s * (0.75 + rnd.nextDouble() * 0.3);
    mb.cylinder(v3(x, y0, z), 0.035 * s, 0.025 * s, h, 4, H('#8a6a44'), false);
    const top = v3(x, y0 + h, z), leaf = H('#3f7a34');
    for (let i = 0; i < 6; i++) {
      const a = i * Math.PI / 3 + rnd.nextDouble() * 0.4;
      const ca = Math.cos(a), sa = Math.sin(a), L = 0.32 * s;
      const tip = v3(x + ca * L, y0 + h - 0.12 * s, z + sa * L);
      const mid = v3(x + ca * L * 0.5 - sa * 0.06 * s, y0 + h + 0.05 * s, z + sa * L * 0.5 + ca * 0.06 * s);
      const mid2 = v3(x + ca * L * 0.5 + sa * 0.06 * s, y0 + h + 0.05 * s, z + sa * L * 0.5 - ca * 0.06 * s);
      sheet(mb, [top, mid, tip, mid2], shade(leaf, (i % 2) * 0.08));
    }
  }
  // 钻天杨
  function poplar(mb, x, z, s, y0) {
    y0 = y0 || 0;
    mb.cylinder(v3(x, y0, z), 0.03 * s, 0.025 * s, 0.2 * s, 4, H('#7a6448'), false);
    mb.blob(v3(x, y0 + 0.5 * s, z), v3(0.1 * s, 0.38 * s, 0.1 * s), H('#5c8a3a'), (x * 97 + z * 31) | 0);
  }
  // 普通树（圆冠）
  function tree(mb, x, z, s, c, y0) {
    SG.Models.tree(mb, v3(x, y0 || 0, z), s, c || H('#4f7f3a'), false, (x * 53 + z * 17) | 0);
  }
  // 平顶房（土坯 / 石砌）：可带女儿墙
  function flatHouse(mb, x, z, w, d, h, c, y0) {
    y0 = y0 || 0;
    mb.box(v3(x, y0 + h / 2, z), v3(w, h, d), c, 0.12);
    mb.box(v3(x, y0 + h + 0.015, z), v3(w * 1.02, 0.03, d * 1.02), shade(c, 0.1));
    mb.box(v3(x, y0 + h * 0.3, z - d / 2 - 0.004), v3(w * 0.22, h * 0.6, 0.01), shade(c, -0.6));   // 门洞
  }
  // 坡顶房（墙 + 双坡顶）
  function gableHouse(mb, x, z, w, d, h, wallC, roofC, axis, y0, rh) {
    y0 = y0 || 0;
    mb.box(v3(x, y0 + h / 2, z), v3(w, h, d), wallC, 0.1);
    gable(mb, x, y0 + h, z, w, d, rh || Math.min(w, d) * 0.45, roofC, axis, Math.min(w, d) * 0.08);
  }
  // 高脚屋：四柱 + 地板 + 墙 + 陡坡顶（saddle = 鞍形翘脊）
  function stiltHouse(mb, x, z, w, d, legH, h, wallC, roofC, saddle, y0) {
    y0 = y0 || 0;
    const post = H('#5a4630');
    for (const sx of [-1, 1]) for (const sz of [-1, 1]) mb.box(v3(x + sx * w * 0.4, y0 + legH / 2, z + sz * d * 0.4), v3(0.035, legH, 0.035), post);
    mb.box(v3(x, y0 + legH + 0.015, z), v3(w * 1.05, 0.03, d * 1.05), shade(post, 0.15));
    mb.box(v3(x, y0 + legH + 0.03 + h / 2, z), v3(w * 0.9, h, d * 0.9), wallC, 0.1);
    const ry = y0 + legH + 0.03 + h;
    gable(mb, x, ry, z, w, d, Math.max(w, d) * 0.6, roofC, d > w ? 'z' : 'x', 0.05);
    if (saddle) {
      // 鞍形屋脊：两端上翘的尖角
      const along = d > w;
      const L = (along ? d : w) / 2 + 0.06, rh = Math.max(w, d) * 0.6;
      for (const sgn of [-1, 1]) {
        const base = along ? v3(x, ry + rh, z + sgn * L * 0.8) : v3(x + sgn * L * 0.8, ry + rh, z);
        const tip = along ? v3(x, ry + rh + 0.14, z + sgn * (L + 0.1)) : v3(x + sgn * (L + 0.1), ry + rh + 0.14, z);
        const side = along ? v3(0.04, 0, 0) : v3(0, 0, 0.04);
        sheet(mb, [base.clone().sub(side), tip, base.clone().add(side)], shade(roofC, -0.1));
      }
    }
  }
  // 毡帐（蒙古包）
  function yurt(mb, x, z, r, felt, y0, roofC) {
    y0 = y0 || 0;
    mb.cylinder(v3(x, y0, z), r, r, r * 0.7, 8, felt, false);
    mb.cylinder(v3(x, y0 + r * 0.7, z), r * 1.03, r * 0.18, r * 0.42, 8, roofC || shade(felt, -0.08), true);
    mb.box(v3(x, y0 + r * 0.25, z - r * 0.98), v3(r * 0.35, r * 0.5, 0.02), H('#a0442a'));   // 门（朝南）
  }
  // 尖顶帐
  function tent(mb, x, z, r, c, y0) {
    mb.cone(v3(x, y0 || 0, z), r, r * 1.5, 6, c);
  }
  // 马尾纛 / 竿旗：竿 + 顶饰 + 垂缨（或小旗）
  function standard(mb, x, z, h, c, kind, y0) {
    y0 = y0 || 0;
    mb.box(v3(x, y0 + h / 2, z), v3(0.03, h, 0.03), H('#5a4026'));
    if (kind === 'tail') {
      mb.cone(v3(x, y0 + h - 0.22, z), 0.07, 0.24, 5, c);
      mb.cone(v3(x, y0 + h, z), 0.025, 0.08, 4, H('#d8b050'));
    } else if (kind === 'vexillum') {
      mb.box(v3(x, y0 + h - 0.05, z), v3(0.22, 0.02, 0.02), H('#5a4026'));
      sheet(mb, [v3(x - 0.11, y0 + h - 0.06, z), v3(x + 0.11, y0 + h - 0.06, z), v3(x + 0.11, y0 + h - 0.3, z), v3(x - 0.11, y0 + h - 0.3, z)], c);
      mb.cone(v3(x, y0 + h, z), 0.04, 0.1, 4, H('#d8b050'));
    } else {
      sheet(mb, [v3(x + 0.015, y0 + h, z), v3(x + 0.26, y0 + h - 0.04, z), v3(x + 0.015, y0 + h - 0.18, z)], c);
    }
  }
  // 底座
  function base(mb, r, c) { mb.box(v3(0, 0.05, 0), v3(r * 2.3, 0.1, r * 2.3), c); }
  // 城门：门洞（深色）+ 两侧门楼
  function gateway(mb, r, w, h, wallC, towerC, round) {
    mb.box(v3(0, h * 0.4, -r - 0.012), v3(w, h * 0.8, 0.06), H('#2a1c12'));
    for (const sx of [-1, 1]) {
      if (round) mb.cylinder(v3(sx * w * 0.95, 0, -r), w * 0.42, w * 0.4, h * 1.3, 8, towerC, true);
      else mb.box(v3(sx * w * 0.95, h * 0.65, -r), v3(w * 0.75, h * 1.3, w * 0.75), towerC);
    }
  }

  // ============================================================ 各文化 --
  const B = {};

  // ---------------------------------------------------------- 罗马 --
  B.roman = function (mb, s, cap, city) {
    const r = s, wallH = 0.42 * s, t = 0.16 * s;
    const stone = H('#cdbf9f'), tile = H('#b4553a'), stucco = H('#ece2c8'), marble = H('#f2eee4');
    base(mb, r, H('#a89a7a'));
    squareWall(mb, r, wallH, t, stone, 0.08 * s, 3);
    // 圆角塔（东北角留给旗帜：缩小）
    for (const cx of [-1, 1]) for (const cz of [-1, 1]) {
      const big = !(cx > 0 && cz > 0);
      mb.cylinder(v3(cx * r, 0, cz * r), t * (big ? 1.05 : 0.8), t, wallH * (big ? 1.35 : 1.1), 8, shade(stone, -0.04), true);
      if (big) hip(mb, cx * r, wallH * 1.35, cz * r, t * 2.1, t * 2.1, t * 1.1, tile);
    }
    gateway(mb, r, 0.24 * s, wallH, stone, shade(stone, -0.05), true);
    // 红瓦民居
    const houses = [[-0.55, -0.45, 0.34, 0.26], [0.5, -0.5, 0.3, 0.26], [-0.6, 0.15, 0.28, 0.34], [0.62, 0.05, 0.26, 0.3], [-0.2, -0.62, 0.26, 0.2], [0.2, -0.25, 0.22, 0.2]];
    houses.forEach((h, i) => gableHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, 0.18 * s, i % 2 ? stucco : shade(stucco, -0.06), tile, h[3] > h[2] ? 'z' : 'x'));
    if (!cap) {
      // 列柱神庙（北侧中央）：基台 + 六柱 + 山墙顶
      const tz = 0.45 * s;
      mb.box(v3(0, 0.06 * s, tz), v3(0.62 * s, 0.12 * s, 0.7 * s), marble);
      mb.box(v3(0, 0.27 * s, tz + 0.08 * s), v3(0.44 * s, 0.3 * s, 0.44 * s), stucco);
      for (let i = 0; i < 4; i++) column(mb, (-0.24 + i * 0.16) * s, 0.12 * s, tz - 0.27 * s, 0.32 * s, 0.03 * s, marble);
      gable(mb, 0, 0.47 * s, tz, 0.6 * s, 0.68 * s, 0.17 * s, tile, 'z', 0.02 * s);
      mb.box(v3(0, 0.45 * s, tz - 0.33 * s), v3(0.6 * s, 0.05 * s, 0.03 * s), marble);
    } else {
      // 都城：竞技场（椭圆环，两层拱）+ 神庙
      const ax = -0.05 * s, az = 0.35 * s, R = 0.5 * s, n = 14, hh = 0.48 * s;
      for (let i = 0; i < n; i++) {
        const a0 = i * Math.PI * 2 / n, a1 = (i + 1) * Math.PI * 2 / n;
        const x0 = ax + Math.cos(a0) * R * 1.15, z0 = az + Math.sin(a0) * R * 0.85, x1 = ax + Math.cos(a1) * R * 1.15, z1 = az + Math.sin(a1) * R * 0.85;
        seg(mb, x0, z0, x1, z1, 0, hh, 0.1 * s, i % 2 ? stucco : shade(stucco, -0.04));
        // 拱：外立面上的深色窗洞（两层）
        const mx = (x0 + x1) / 2, mz = (z0 + z1) / 2, nx = Math.cos((a0 + a1) / 2), nz = Math.sin((a0 + a1) / 2);
        for (const yy of [0.14, 0.32]) {
          const px = mx + nx * 0.052 * s, pz = mz + nz * 0.052 * s;
          const tx = (x1 - x0) * 0.22, tz2 = (z1 - z0) * 0.22;
          face(mb, [v3(px - tx, yy * s, pz - tz2), v3(px + tx, yy * s, pz + tz2), v3(px + tx, (yy + 0.1) * s, pz + tz2), v3(px - tx, (yy + 0.1) * s, pz - tz2)], H('#4a3a2c'), v3(nx, 0, nz));
        }
      }
      face(mb, [v3(ax - R, 0.11, az - R * 0.75), v3(ax + R, 0.11, az - R * 0.75), v3(ax + R, 0.11, az + R * 0.75), v3(ax - R, 0.11, az + R * 0.75)], H('#d8c08a'), v3(0, 1, 0));
      // 小神庙（东侧）
      mb.box(v3(0.62 * s, 0.05 * s, 0.55 * s), v3(0.3 * s, 0.1 * s, 0.36 * s), marble);
      for (let i = 0; i < 3; i++) column(mb, (0.52 + i * 0.1) * s, 0.1 * s, 0.4 * s, 0.24 * s, 0.022 * s, marble);
      mb.box(v3(0.62 * s, 0.22 * s, 0.6 * s), v3(0.26 * s, 0.24 * s, 0.22 * s), stucco);
      gable(mb, 0.62 * s, 0.35 * s, 0.55 * s, 0.3 * s, 0.36 * s, 0.1 * s, tile, 'z', 0.01 * s);
    }
    standard(mb, -0.25 * s, -r - 0.18 * s, 0.75 * s, H('#a8261e'), 'vexillum');
    standard(mb, 0.25 * s, -r - 0.18 * s, 0.75 * s, H('#a8261e'), 'vexillum');
  };

  // ---------------------------------------------------- 安息 / 波斯 --
  B.persia = function (mb, s, cap) {
    const r = s, wallH = 0.42 * s, t = 0.2 * s;
    const mud = H('#c8a679'), mud2 = H('#b8946a'), glaze = H('#2f6e8e');
    base(mb, r, H('#b8a07a'));
    squareWall(mb, r, wallH, t, mud, 0.09 * s, 3);
    // 扶壁塔（方形，沿墙等距）
    for (const k of [-0.5, 0.5]) for (const side of [[0, -1], [0, 1], [-1, 0], [1, 0]]) {
      const x = side[0] ? side[0] * r : k * r * 1.2, z = side[1] ? side[1] * r : k * r * 1.2;
      if (x > 0 && z > 0.4 * r) continue;   // 东北角留给旗帜
      mb.box(v3(x, wallH * 0.58, z), v3(t * 1.2, wallH * 1.16, t * 1.2), mud2);
    }
    for (const cx of [-1, 1]) for (const cz of [-1, 1]) if (!(cx > 0 && cz > 0)) mb.box(v3(cx * r, wallH * 0.65, cz * r), v3(t * 1.6, wallH * 1.3, t * 1.6), mud2);
    // 城门：高拱门
    mb.box(v3(0, wallH * 0.62, -r), v3(0.5 * s, wallH * 1.24, t * 1.3), mud2);
    mb.box(v3(0, wallH * 0.42, -r - t * 0.66), v3(0.2 * s, wallH * 0.84, 0.02), H('#2a1c12'));
    mb.box(v3(0, wallH * 0.95, -r - t * 0.66), v3(0.3 * s, 0.05 * s, 0.02), glaze);
    // 平顶房
    const hs = [[-0.6, -0.5, 0.3, 0.26, 0.2], [0.55, -0.55, 0.26, 0.24, 0.18], [-0.62, -0.05, 0.26, 0.32, 0.22], [0.62, -0.1, 0.24, 0.3, 0.2], [-0.3, -0.58, 0.2, 0.2, 0.16], [0.28, -0.6, 0.2, 0.18, 0.15]];
    hs.forEach((h, i) => flatHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, h[4] * s, i % 2 ? mud : shade(mud, 0.08)));
    // 阶梯内城（北侧）：三层台
    const cz = 0.42 * s;
    [[0.9, 0.16], [0.68, 0.16], [0.46, 0.14]].reduce((y, l, i) => {
      mb.box(v3(-0.05 * s, y + l[1] * s / 2, cz), v3(l[0] * s, l[1] * s, l[0] * s * 0.7), i % 2 ? mud2 : shade(mud2, 0.06));
      return y + l[1] * s;
    }, 0);
    // 伊万宫殿（立在台顶）：方体 + 巨大拱门（深色）+ 釉砖边框 + 筒拱顶
    const py = 0.46 * s, iw = cap ? 0.42 : 0.3;
    mb.box(v3(-0.05 * s, py + 0.14 * s, cz), v3(iw * s, 0.28 * s, 0.26 * s), H('#d8bc8c'));
    mb.box(v3(-0.05 * s, py + 0.12 * s, cz - 0.131 * s), v3(iw * 0.5 * s, 0.22 * s, 0.01), H('#3a2818'));
    mb.box(v3(-0.05 * s, py + 0.245 * s, cz - 0.132 * s), v3(iw * 0.62 * s, 0.025 * s, 0.01), glaze);
    mb.cylinder(v3(-0.05 * s, py + 0.28 * s, cz), iw * 0.36 * s, 0.02 * s, 0.1 * s, 6, H('#c8ac7c'), false);
    if (cap) {
      // 都城：拜火祠（四柱亭 + 穹顶）与第二座伊万
      const fx = 0.55 * s, fz = 0.5 * s;
      mb.box(v3(fx, 0.04 * s, fz), v3(0.3 * s, 0.08 * s, 0.3 * s), mud2);
      for (const ox of [-1, 1]) for (const oz of [-1, 1]) mb.box(v3(fx + ox * 0.1 * s, 0.17 * s, fz + oz * 0.1 * s), v3(0.06 * s, 0.18 * s, 0.06 * s), H('#d8c098'));
      mb.box(v3(fx, 0.28 * s, fz), v3(0.28 * s, 0.05 * s, 0.28 * s), H('#d8c098'));
      dome(mb, fx, 0.3 * s, fz, 0.12 * s, H('#d8c098'), 8);
      mb.cone(v3(fx, 0.08 * s, fz), 0.03 * s, 0.07 * s, 5, H('#ff8a2a'));
    }
    standard(mb, -0.6 * s, 0.6 * s, 0.7 * s, H('#7a2a5a'), 'tail');
  };

  // ------------------------------------------------- 阿拉伯 / 哈特拉 --
  B.arab = function (mb, s, cap, city) {
    const r = s, R = 0.98 * r, wallH = 0.4 * s;
    const stone = H('#d8c49a'), stone2 = H('#c4ae84');
    base(mb, r, H('#c8b080'));
    ringWall(mb, R, 16, wallH, 0.15 * s, stone, Math.PI / 16);
    // 圆塔（跳过东北）
    for (let i = 0; i < 8; i++) {
      const a = i * Math.PI / 4 + Math.PI / 8;
      const x = Math.cos(a) * R, z = Math.sin(a) * R;
      if (x > 0.3 * r && z > 0.3 * r) continue;
      mb.cylinder(v3(x, 0, z), 0.11 * s, 0.1 * s, wallH * 1.3, 6, stone2, true);
    }
    // 南门
    mb.box(v3(0, wallH * 0.6, -R), v3(0.34 * s, wallH * 1.2, 0.22 * s), stone2);
    mb.box(v3(0, wallH * 0.4, -R - 0.112 * s), v3(0.14 * s, wallH * 0.8, 0.01), H('#2a1c12'));
    // 神庙院（中央）：围墙 + 双伊万神殿 + 列柱
    const tz = 0.12 * s;
    mb.box(v3(0, 0.05 * s, tz), v3(0.84 * s, 0.1 * s, 0.6 * s), shade(stone, 0.06));
    const n = cap ? 2 : 1;
    for (let k = 0; k < n; k++) {
      const x = (n === 1 ? 0 : (k ? 0.2 : -0.2)) * s;
      mb.box(v3(x, 0.1 * s + 0.17 * s, tz + 0.1 * s), v3(0.34 * s, 0.34 * s, 0.32 * s), stone);
      mb.box(v3(x, 0.1 * s + 0.14 * s, tz - 0.061 * s), v3(0.15 * s, 0.26 * s, 0.01), H('#3a2818'));
      mb.box(v3(x, 0.1 * s + 0.35 * s, tz + 0.1 * s), v3(0.36 * s, 0.03 * s, 0.34 * s), shade(stone, 0.12));
    }
    for (let i = 0; i < 6; i++) column(mb, (-0.36 + i * 0.144) * s, 0.1 * s, tz - 0.25 * s, 0.2 * s, 0.022 * s, H('#efe3c4'));
    // 平顶房 + 椰枣树
    const hs = [[-0.55, -0.45], [0.55, -0.42], [-0.62, 0.35], [-0.3, -0.62], [0.3, -0.64]];
    hs.forEach((h, i) => flatHouse(mb, h[0] * s, h[1] * s, 0.22 * s, 0.2 * s, (0.14 + (i % 3) * 0.03) * s, i % 2 ? stone : H('#e2d2ac')));
    palm(mb, 0.55 * s, 0.1 * s, s, 11); palm(mb, -0.15 * s, 0.6 * s, s * 0.9, 12); palm(mb, -0.72 * s, -0.12 * s, s * 0.85, 13);
    // 阿克苏姆：石碑
    if (city && city.key === 'aksum') {
      for (const [x, h] of [[0.42, 0.95], [0.6, 0.7], [0.28, 0.6]]) {
        mb.box(v3(x * s, h * s / 2, 0.55 * s), v3(0.07 * s, h * s, 0.04 * s), H('#9a8f80'));
        mb.cone(v3(x * s, h * s, 0.55 * s), 0.045 * s, 0.05 * s, 4, H('#9a8f80'));
      }
    }
    standard(mb, 0, 0.62 * s, 0.75 * s, H('#d8b050'), 'tail');
  };

  // ------------------------------------------------------------ 贵霜 --
  function stupa(mb, x, z, k, y0) {
    y0 = y0 || 0;
    const white = H('#ece6d6'), gold = H('#d8b050');
    mb.box(v3(x, y0 + 0.06 * k, z), v3(0.5 * k, 0.12 * k, 0.5 * k), H('#cfc4a8'));
    mb.cylinder(v3(x, y0 + 0.12 * k, z), 0.2 * k, 0.2 * k, 0.12 * k, 10, white, false);
    dome(mb, x, y0 + 0.24 * k, z, 0.2 * k, white, 10);
    mb.box(v3(x, y0 + 0.47 * k, z), v3(0.08 * k, 0.06 * k, 0.08 * k), H('#b88a4a'));
    mb.box(v3(x, y0 + 0.58 * k, z), v3(0.012 * k, 0.2 * k, 0.012 * k), gold);
    for (let i = 0; i < 3; i++) mb.cylinder(v3(x, y0 + (0.52 + i * 0.06) * k, z), (0.07 - i * 0.017) * k, (0.07 - i * 0.017) * k, 0.012 * k, 6, gold, true);
  }
  B.kushan = function (mb, s, cap) {
    const r = s, wallH = 0.4 * s, t = 0.18 * s;
    const brick = H('#b98f64'), brick2 = H('#a87e56'), plaster = H('#e2d4b4');
    base(mb, r, H('#a8966e'));
    squareWall(mb, r, wallH, t, brick, 0.07 * s, 3);
    for (const cx of [-1, 1]) for (const cz of [-1, 1]) if (!(cx > 0 && cz > 0)) mb.cylinder(v3(cx * r, 0, cz * r), t * 1.2, t * 1.1, wallH * 1.25, 8, brick2, true);
    gateway(mb, r, 0.22 * s, wallH, brick, brick2, false);
    // 窣堵波（北侧）
    stupa(mb, -0.15 * s, 0.38 * s, cap ? 1.35 * s : 1.0 * s);
    // 僧院 / 民居（平顶，木檐）
    const hs = [[-0.58, -0.45, 0.3, 0.24], [0.55, -0.45, 0.28, 0.24], [0.6, 0.0, 0.24, 0.3], [-0.65, 0.05, 0.22, 0.3], [0.15, -0.6, 0.24, 0.2]];
    hs.forEach((h, i) => flatHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, 0.17 * s, i % 2 ? plaster : brick));
    if (cap) {
      // 宫殿：高台 + 列柱厅
      mb.box(v3(0.5 * s, 0.08 * s, 0.45 * s), v3(0.44 * s, 0.16 * s, 0.36 * s), brick2);
      mb.box(v3(0.5 * s, 0.3 * s, 0.5 * s), v3(0.36 * s, 0.28 * s, 0.24 * s), plaster);
      for (let i = 0; i < 4; i++) column(mb, (0.36 + i * 0.093) * s, 0.16 * s, 0.33 * s, 0.26 * s, 0.018 * s, H('#f0e8d8'));
      mb.box(v3(0.5 * s, 0.45 * s, 0.45 * s), v3(0.42 * s, 0.04 * s, 0.34 * s), H('#8a5a34'));
    } else {
      poplar(mb, 0.45 * s, 0.4 * s, s * 0.9); poplar(mb, 0.62 * s, 0.32 * s, s * 0.8);
    }
    standard(mb, 0.3 * s, -0.25 * s, 0.7 * s, H('#d8b050'), 'flag');
  };

  // ------------------------------------------------------------- 倭 --
  B.wa = function (mb, s, cap) {
    const r = s;
    const wood = H('#8a6a46'), thatch = H('#a88a52'), thatch2 = H('#94784a');
    base(mb, r, H('#7d8a52'));
    moat(mb, r * 0.92, r * 1.12, 0.105, H('#4a7a8a'));
    // 土垒 + 木栅（内侧）
    const R = r * 0.84;
    palisade(mb, [[-R, -R], [-0.15 * s, -R], [0.15 * s, -R], [R, -R], [R, R], [-R, R]].filter((p, i) => i !== 2), 0.2 * s, wood, 6, 0.1);
    // 门（南，两柱 + 横木）
    for (const sx of [-1, 1]) mb.box(v3(sx * 0.15 * s, 0.17 * s, -R), v3(0.04 * s, 0.34 * s, 0.04 * s), shade(wood, -0.1));
    mb.box(v3(0, 0.33 * s, -R), v3(0.4 * s, 0.035 * s, 0.04 * s), shade(wood, -0.1));
    // 望楼（西南、西北、东南）
    for (const [x, z] of [[-0.7, -0.7], [0.7, -0.7], [-0.7, 0.62]]) {
      for (const ox of [-1, 1]) for (const oz of [-1, 1]) mb.box(v3((x + ox * 0.06) * s, 0.3 * s, (z + oz * 0.06) * s), v3(0.025 * s, 0.6 * s, 0.025 * s), wood);
      mb.box(v3(x * s, 0.58 * s, z * s), v3(0.18 * s, 0.03 * s, 0.18 * s), shade(wood, 0.1));
      hip(mb, x * s, 0.66 * s, z * s, 0.22 * s, 0.22 * s, 0.14 * s, thatch);
      mb.box(v3(x * s, 0.62 * s, z * s), v3(0.16 * s, 0.07 * s, 0.16 * s), shade(wood, -0.15));
    }
    // 高床仓库
    for (const [x, z] of [[0.45, 0.45], [0.2, 0.55]]) stiltHouse(mb, x * s, z * s, 0.2 * s, 0.16 * s, 0.14 * s, 0.1 * s, H('#a07c52'), thatch2, false);
    // 竖穴住居（圆锥茅顶）
    for (const [x, z, k] of [[-0.45, -0.35, 1], [0.0, -0.45, 0.9], [0.4, -0.35, 1], [-0.5, 0.15, 0.95], [-0.1, 0.05, 0.85]]) {
      mb.cylinder(v3(x * s, 0.1, z * s), 0.15 * k * s, 0.04 * k * s, 0.2 * k * s, 7, thatch, false);
      mb.cone(v3(x * s, 0.1 + 0.2 * k * s, z * s), 0.05 * k * s, 0.05 * k * s, 5, thatch2);
    }
    if (cap) {
      // 大型高床殿（邪马台宫室）
      stiltHouse(mb, 0.05 * s, 0.45 * s, 0.42 * s, 0.3 * s, 0.22 * s, 0.16 * s, H('#b08a5a'), thatch2, false);
      for (const sx of [-1, 1]) mb.box(v3((0.05 + sx * 0.24) * s, 0.86 * s, 0.45 * s), v3(0.02 * s, 0.16 * s, 0.02 * s), wood);   // 千木
    }
    standard(mb, -0.25 * s, -0.6 * s, 0.55 * s, H('#e8e0d0'), 'flag');
  };

  // ------------------------------------------------------------ 朝鲜 --
  B.korea = function (mb, s, cap) {
    const r = s;
    const rock = H('#a6a296'), rock2 = H('#8e8a80'), roof = H('#4e5560'), wood = H('#9a3a2c'), wall = H('#e6dccb');
    base(mb, r, H('#7e8a5c'));
    // 山丘
    mb.blob(v3(0.05 * s, 0.05, 0.25 * s), v3(0.85 * s, 0.38 * s, 0.7 * s), H('#6f7f4e'), 7);
    // 依山石墙：不规则多边形，北高南低
    const pts = [[-0.98, -0.95], [0.0, -1.0], [0.98, -0.9], [1.0, 0.1], [0.75, 0.95], [-0.2, 1.0], [-0.95, 0.7], [-1.0, -0.1]];
    for (let i = 0; i < pts.length; i++) {
      const a = pts[i], b = pts[(i + 1) % pts.length];
      if (i === 0) {   // 南墙中间是城门
        seg(mb, a[0] * r, a[1] * r, -0.16 * s, -r, 0, 0.3 * s, 0.13 * s, rock, 0.3 * s);
        seg(mb, 0.16 * s, -r, b[0] * r * 0 + 0.98 * r, -0.9 * r, 0, 0.3 * s, 0.13 * s, rock, 0.32 * s);
        continue;
      }
      if (i === 1) continue;
      const ha = (0.3 + Math.max(0, a[1]) * 0.25) * s, hb = (0.3 + Math.max(0, b[1]) * 0.25) * s;
      seg(mb, a[0] * r, a[1] * r, b[0] * r, b[1] * r, 0, ha, 0.13 * s, i % 2 ? rock : rock2, hb);
    }
    // 城门楼
    mb.box(v3(0, 0.17 * s, -r), v3(0.34 * s, 0.34 * s, 0.2 * s), rock2);
    mb.box(v3(0, 0.13 * s, -r - 0.101 * s), v3(0.14 * s, 0.26 * s, 0.01), H('#2a1c12'));
    mb.box(v3(0, 0.42 * s, -r), v3(0.3 * s, 0.16 * s, 0.16 * s), wood);
    mb.chineseRoof(v3(0, 0.5 * s, -r), 0.48 * s, 0.3 * s, 0.2 * s, roof);
    // 山上殿阁
    const hy = 0.3 * s;
    mb.box(v3(0.0, hy + 0.12 * s, 0.35 * s), v3(0.5 * s, 0.24 * s, 0.3 * s), wall);
    mb.box(v3(0.0, hy + 0.12 * s, 0.199 * s), v3(0.5 * s, 0.05 * s, 0.01), wood);
    mb.chineseRoof(v3(0.0, hy + 0.24 * s, 0.35 * s), 0.72 * s, 0.48 * s, 0.26 * s, cap ? H('#5a4a3a') : roof);
    if (cap) {
      mb.box(v3(0.0, hy + 0.42 * s, 0.35 * s), v3(0.3 * s, 0.14 * s, 0.2 * s), wall);
      mb.chineseRoof(v3(0.0, hy + 0.49 * s, 0.35 * s), 0.46 * s, 0.32 * s, 0.2 * s, H('#5a4a3a'));
    }
    // 山下民居（草顶 / 瓦顶）
    for (const [x, z, k] of [[-0.55, -0.55, 0], [0.5, -0.6, 1], [-0.62, -0.1, 1], [0.6, -0.15, 0]]) {
      mb.box(v3(x * s, 0.1 * s, z * s), v3(0.24 * s, 0.2 * s, 0.18 * s), wall);
      if (k) mb.chineseRoof(v3(x * s, 0.2 * s, z * s), 0.34 * s, 0.26 * s, 0.13 * s, roof);
      else hip(mb, x * s, 0.2 * s, z * s, 0.3 * s, 0.24 * s, 0.13 * s, H('#a88a52'), 0.4);
    }
    tree(mb, -0.45 * s, 0.45 * s, 0.6 * s, H('#3f6a34'), 0.15 * s);
    standard(mb, -0.3 * s, -r - 0.05 * s, 0.6 * s, H('#c8382c'), 'flag');
  };

  // ------------------------------------------------------------ 草原 --
  B.steppe = function (mb, s, cap) {
    const r = s;
    const felt = H('#e8e0cc'), felt2 = H('#d6cbb0'), wood = H('#7a5a3a');
    base(mb, r, H('#8a9a5a'));
    // 车阵 / 木篱（低矮的环）
    ringWall(mb, r * 0.95, 12, 0.1 * s, 0.04 * s, wood, Math.PI / 12);
    const ys = [[-0.45, -0.4, 0.2], [0.42, -0.45, 0.19], [-0.58, 0.18, 0.19], [0.12, -0.1, 0.18], [-0.12, -0.62, 0.15], [0.58, 0.02, 0.16]];
    ys.forEach((y, i) => yurt(mb, y[0] * s, y[1] * s, y[2] * s, i % 2 ? felt : felt2));
    // 首领大帐
    const big = cap ? 0.34 : 0.26;
    yurt(mb, -0.1 * s, 0.42 * s, big * s, H('#f2ecdc'), 0, cap ? H('#d8b050') : H('#c8bc9c'));
    if (cap) mb.cone(v3(-0.1 * s, big * s * 1.12, 0.42 * s), 0.05 * s, 0.14 * s, 5, H('#d8b050'));
    // 马尾纛
    for (const [x, z] of [[-0.35, -0.85], [0.35, -0.85], [-0.45, 0.62]]) standard(mb, x * s, z * s, 0.8 * s, H('#2a2420'), 'tail');
    standard(mb, 0.25 * s, 0.35 * s, 0.9 * s, H('#f2ecdc'), 'tail');
    // 马栏
    for (let i = 0; i < 4; i++) mb.box(v3((0.35 + i * 0.1) * s, 0.06 * s, -0.75 * s), v3(0.02 * s, 0.12 * s, 0.02 * s), wood);
    mb.box(v3(0.5 * s, 0.1 * s, -0.75 * s), v3(0.32 * s, 0.015 * s, 0.015 * s), wood);
  };

  // ------------------------------------------------------------ 西域 --
  B.tarim = function (mb, s, cap) {
    const r = s, wallH = 0.36 * s, t = 0.2 * s;
    const earth = H('#cfb487'), earth2 = H('#bca274');
    base(mb, r, H('#d2bc8a'));
    // 夯土墙：各段高低不一（风蚀）
    const W = [[-r, -r, -0.15 * s, -r], [0.15 * s, -r, r, -r], [r, -r, r, r], [r, r, -r, r], [-r, r, -r, -r]];
    W.forEach((w, i) => seg(mb, w[0], w[1], w[2], w[3], 0, wallH * (1 - (i % 2) * 0.12), t, i % 2 ? earth2 : earth, wallH * (0.88 + (i % 3) * 0.06)));
    for (const cx of [-1, 1]) for (const cz of [-1, 1]) if (!(cx > 0 && cz > 0)) mb.box(v3(cx * r, wallH * 0.6, cz * r), v3(t * 1.4, wallH * 1.2, t * 1.4), earth2);
    // 城门（两侧门墩）
    for (const sx of [-1, 1]) mb.box(v3(sx * 0.2 * s, wallH * 0.6, -r), v3(0.12 * s, wallH * 1.2, t * 1.3), earth2);
    // 绿洲：水池 + 钻天杨 + 果园
    face(mb, [v3(-0.55 * s, 0.105, 0.2 * s), v3(-0.25 * s, 0.105, 0.2 * s), v3(-0.25 * s, 0.105, 0.5 * s), v3(-0.55 * s, 0.105, 0.5 * s)], H('#4f8a8a'), v3(0, 1, 0));
    for (const [x, z] of [[-0.68, 0.15], [-0.68, 0.4], [-0.68, 0.65], [-0.1, 0.62], [0.12, 0.66], [0.5, -0.3]]) poplar(mb, x * s, z * s, s * 0.85);
    // 平顶房
    const hs = [[-0.5, -0.5, 0.28], [0.45, -0.55, 0.26], [0.55, -0.05, 0.24], [-0.15, -0.3, 0.22], [0.2, -0.3, 0.2]];
    hs.forEach((h, i) => flatHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[2] * 0.85 * s, (0.13 + (i % 2) * 0.04) * s, i % 2 ? earth : H('#dcc79c')));
    // 小佛塔（龟兹、于阗多佛寺）或烽燧
    if (cap) stupa(mb, 0.25 * s, 0.35 * s, 1.0 * s);
    else {
      mb.box(v3(0.3 * s, 0.25 * s, 0.4 * s), v3(0.16 * s, 0.5 * s, 0.16 * s), earth2);   // 烽燧
      mb.box(v3(0.3 * s, 0.52 * s, 0.4 * s), v3(0.2 * s, 0.04 * s, 0.2 * s), earth);
    }
    standard(mb, 0, -r - 0.12 * s, 0.6 * s, H('#a83a2a'), 'flag');
  };

  // ------------------------------------------------- 南海（林邑、扶南）--
  B.seasia = function (mb, s, cap) {
    const r = s;
    const bamboo = H('#a8945a'), thatch = H('#9a8048'), wood = H('#7a5a3a'), brick = H('#b0643e');
    base(mb, r, H('#6f8a4a'));
    // 水渠（南、西）
    face(mb, [v3(-r * 1.12, 0.105, -r * 1.12), v3(r * 1.12, 0.105, -r * 1.12), v3(r * 1.12, 0.105, -r * 0.95), v3(-r * 1.12, 0.105, -r * 0.95)], H('#4a7a7a'), v3(0, 1, 0));
    face(mb, [v3(-r * 1.12, 0.105, -r * 0.95), v3(-r * 0.95, 0.105, -r * 0.95), v3(-r * 0.95, 0.105, r * 1.12), v3(-r * 1.12, 0.105, r * 1.12)], H('#4a7a7a'), v3(0, 1, 0));
    // 木栅
    const R = r * 0.86;
    palisade(mb, [[-R, -R], [-0.14 * s, -R]], 0.16 * s, wood, 5);
    palisade(mb, [[0.14 * s, -R], [R, -R], [R, R * 0.4]], 0.16 * s, wood, 5);
    palisade(mb, [[R * 0.4, R], [-R, R], [-R, -R]], 0.16 * s, wood, 5);
    // 高脚屋（鞍形屋顶）
    const hs = [[-0.5, -0.45, 0.24, 0.2], [0.45, -0.5, 0.22, 0.2], [-0.55, 0.1, 0.2, 0.26], [0.55, -0.05, 0.2, 0.24], [0.0, -0.55, 0.2, 0.18]];
    hs.forEach((h, i) => stiltHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, 0.14 * s, 0.1 * s, bamboo, i % 2 ? thatch : shade(thatch, -0.08), true));
    // 神殿：砖台 + 高耸的多重顶
    const tx = -0.05 * s, tz = 0.35 * s, k = cap ? 1.25 : 1;
    mb.box(v3(tx, 0.08 * s * k, tz), v3(0.5 * s * k, 0.16 * s * k, 0.5 * s * k), brick);
    mb.box(v3(tx, 0.22 * s * k, tz), v3(0.34 * s * k, 0.12 * s * k, 0.34 * s * k), shade(brick, 0.08));
    mb.box(v3(tx, 0.36 * s * k, tz), v3(0.22 * s * k, 0.16 * s * k, 0.22 * s * k), H('#c8a050'));
    hip(mb, tx, 0.44 * s * k, tz, 0.34 * s * k, 0.34 * s * k, 0.12 * s * k, thatch);
    hip(mb, tx, 0.53 * s * k, tz, 0.22 * s * k, 0.22 * s * k, 0.2 * s * k, shade(thatch, -0.1));
    mb.cone(v3(tx, 0.72 * s * k, tz), 0.02 * s, 0.14 * s, 4, H('#d8b050'));
    if (cap) stiltHouse(mb, 0.5 * s, 0.5 * s, 0.32 * s, 0.24 * s, 0.16 * s, 0.14 * s, H('#b8a060'), shade(thatch, -0.12), true);
    // 椰树
    for (const [x, z, k2] of [[-0.75, 0.6, 1], [0.7, 0.3, 0.9], [-0.25, -0.2, 0.8], [0.3, 0.6, 0.95], [-0.72, -0.25, 0.85]]) palm(mb, x * s, z * s, s * k2, (x * 100 + z * 10) | 0);
    standard(mb, 0, -r * 0.95, 0.55 * s, H('#c8a030'), 'flag');
  };

  // ------------------------------------------------ 夷洲 / 南中：高脚村落 --
  B.village = function (mb, s, cap, city, culture) {
    const r = s;
    const bamboo = H('#a8945a'), thatch = culture === 'nanman' ? H('#8a7448') : H('#a08a50'), wood = H('#6a5034');
    base(mb, r, culture === 'nanman' ? H('#7a8452') : H('#6f8a4a'));
    const R = r * 0.9;
    palisade(mb, [[-R, -R], [-0.14 * s, -R]], 0.2 * s, wood, 6);
    palisade(mb, [[0.14 * s, -R], [R, -R], [R, R * 0.4]], 0.2 * s, wood, 6);
    palisade(mb, [[R * 0.4, R], [-R, R], [-R, -R]], 0.2 * s, wood, 6);
    const hs = [[-0.5, -0.45, 0.26, 0.2], [0.45, -0.5, 0.24, 0.2], [-0.55, 0.15, 0.22, 0.28], [0.5, -0.05, 0.22, 0.26], [0.0, -0.4, 0.2, 0.18], [-0.1, 0.45, 0.36, 0.26]];
    hs.forEach((h, i) => stiltHouse(mb, h[0] * s, h[1] * s, h[2] * s, h[3] * s, 0.12 * s, (i === 5 ? 0.14 : 0.1) * s, bamboo, i % 2 ? thatch : shade(thatch, -0.08), culture === 'nanman'));
    if (culture === 'nanman') {
      // 铜鼓祭台
      mb.cylinder(v3(0.4 * s, 0.1, 0.45 * s), 0.1 * s, 0.12 * s, 0.12 * s, 8, H('#7a8a5a'), true);
      mb.cylinder(v3(0.4 * s, 0.1 + 0.12 * s, 0.45 * s), 0.13 * s, 0.13 * s, 0.03 * s, 8, H('#9aa070'), true);
    } else {
      for (const [x, z] of [[0.45, 0.5], [0.65, 0.25]]) palm(mb, x * s, z * s, s * 0.9, (x * 77) | 0);
    }
    tree(mb, -0.72 * s, 0.7 * s, 0.6 * s, H('#3f6a34'));
    standard(mb, 0, -R, 0.5 * s, H('#b02a24'), 'flag');
  };

  // --------------------------------------------- 欧洲蛮族：山丘堡垒 --
  B.hillfort = function (mb, s, cap, city, culture) {
    const r = s;
    const earth = H('#7a8a4e'), earth2 = H('#6a7844'), wood = H('#7a5a3a'), thatch = H('#a08a50'), wall = H('#a08868');
    base(mb, r, H('#6f804a'));
    // 土垒：两圈（外低内高），用多边形环墙表现
    ringWall(mb, r * 1.02, 14, 0.12 * s, 0.16 * s, earth2, 0);
    mb.cylinder(v3(0, 0, 0), r * 0.86, r * 0.8, 0.2 * s, 14, earth, true);
    const y0 = 0.2 * s;
    // 木栅（土台边缘，南面留门）
    const R = r * 0.8, n = 12, pts = [];
    for (let i = 0; i < n; i++) { const a = -Math.PI / 2 + Math.PI / n + i * Math.PI * 2 / n; pts.push([Math.cos(a) * R, Math.sin(a) * R]); }
    for (let i = 0; i < n - 1; i++) palisade(mb, [pts[i], pts[i + 1]], 0.16 * s, wood, 5, y0);
    // 门楼
    for (const sx of [-1, 1]) mb.box(v3(sx * 0.12 * s, y0 + 0.14 * s, -R), v3(0.05 * s, 0.28 * s, 0.05 * s), wood);
    mb.box(v3(0, y0 + 0.28 * s, -R), v3(0.32 * s, 0.05 * s, 0.08 * s), wood);
    if (culture === 'celt') {
      // 圆形茅屋
      for (const [x, z, k] of [[-0.35, -0.3, 1], [0.35, -0.3, 0.9], [-0.4, 0.25, 0.95], [0.3, 0.3, 1], [0.0, 0.0, cap ? 1.5 : 1.15]]) {
        mb.cylinder(v3(x * s, y0, z * s), 0.14 * k * s, 0.14 * k * s, 0.1 * k * s, 8, wall, false);
        mb.cone(v3(x * s, y0 + 0.1 * k * s, z * s), 0.18 * k * s, 0.22 * k * s, 8, thatch);
      }
    } else {
      // 长屋（日耳曼）
      for (const [x, z, w, d] of [[-0.3, -0.3, 0.5, 0.2], [0.35, -0.2, 0.2, 0.46], [-0.3, 0.3, 0.46, 0.2]]) gableHouse(mb, x * s, z * s, w * s, d * s, 0.08 * s, wall, thatch, d > w ? 'z' : 'x', y0, 0.2 * s);
      if (cap) {
        gableHouse(mb, 0.2 * s, 0.35 * s, 0.56 * s, 0.26 * s, 0.14 * s, H('#8a6a48'), H('#8a7444'), 'x', y0, 0.3 * s);
        for (const sx of [-1, 1]) mb.box(v3((0.2 + sx * 0.3) * s, y0 + 0.48 * s, 0.35 * s), v3(0.02 * s, 0.12 * s, 0.02 * s), wood);   // 山墙交叉兽头
      }
    }
    tree(mb, 0.85 * s, -0.75 * s, 0.6 * s, H('#3f6a34'));
    tree(mb, -0.95 * s, 0.6 * s, 0.7 * s, H('#3f6a34'));
    standard(mb, -0.2 * s, 0.1 * s, 0.75 * s, culture === 'celt' ? H('#2a6a3a') : H('#7a2a24'), 'flag', y0);
  };

  // ------------------------------------------------ 萨尔马提亚：车营 / 希腊城 --
  B.sarmatian = function (mb, s, cap, city) {
    const r = s;
    if (cap || (city && city.town >= 350)) return B.greek(mb, s, cap, city);
    const felt = H('#c8b898'), wood = H('#6a4a2a');
    base(mb, r, H('#93a05e'));
    // 篷车围成的车阵
    for (let i = 0; i < 9; i++) {
      const a = Math.PI / 2 + i * Math.PI * 2 / 10;
      const x = Math.cos(a) * r * 0.78, z = Math.sin(a) * r * 0.78;
      if (x > 0.35 * r && z > 0.35 * r) continue;
      mb.box(v3(x, 0.14 * s, z), v3(0.3 * s, 0.07 * s, 0.17 * s), wood);
      mb.cylinder(v3(x, 0.175 * s, z), 0.12 * s, 0.04 * s, 0.18 * s, 6, felt, false);
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) mb.box(v3(x + sx * 0.1 * s, 0.07 * s, z + sz * 0.09 * s), v3(0.11 * s, 0.11 * s, 0.02 * s), shade(wood, -0.25));
    }
    for (const [x, z, k] of [[-0.3, -0.25, 1], [0.25, -0.3, 0.9], [0.0, 0.25, 1.3], [-0.4, 0.3, 0.85], [0.3, 0.2, 0.8]]) tent(mb, x * s, z * s, 0.2 * k * s, k > 1 ? H('#d8c8a0') : felt);
    for (const [x, z] of [[-0.15, -0.75], [0.15, -0.75]]) standard(mb, x * s, z * s, 0.8 * s, H('#a83a2a'), 'tail');
  };
  // 希腊城（博斯普鲁斯）：白色城墙、卫城丘与神庙
  B.greek = function (mb, s, cap) {
    const r = s, wallH = 0.36 * s, t = 0.15 * s;
    const stone = H('#dcd6c6'), tile = H('#b4553a'), marble = H('#f2eee4'), stucco = H('#ece4d0');
    base(mb, r, H('#a8a07c'));
    squareWall(mb, r, wallH, t, stone, 0.07 * s, 3);
    for (const cx of [-1, 1]) for (const cz of [-1, 1]) if (!(cx > 0 && cz > 0)) mb.box(v3(cx * r, wallH * 0.65, cz * r), v3(t * 1.7, wallH * 1.3, t * 1.7), shade(stone, -0.05));
    gateway(mb, r, 0.22 * s, wallH, stone, shade(stone, -0.05), false);
    mb.blob(v3(-0.1 * s, 0.05, 0.35 * s), v3(0.55 * s, 0.26 * s, 0.45 * s), H('#9a9670'), 5);
    const y0 = 0.22 * s, tz = 0.35 * s;
    mb.box(v3(-0.1 * s, y0 + 0.04 * s, tz), v3(0.46 * s, 0.08 * s, 0.32 * s), marble);
    for (let i = 0; i < 5; i++) column(mb, (-0.28 + i * 0.09) * s, y0 + 0.08 * s, tz - 0.12 * s, 0.22 * s, 0.018 * s, marble);
    mb.box(v3(-0.1 * s, y0 + 0.19 * s, tz + 0.04 * s), v3(0.34 * s, 0.22 * s, 0.2 * s), stucco);
    gable(mb, -0.1 * s, y0 + 0.31 * s, tz, 0.44 * s, 0.3 * s, 0.08 * s, tile, 'x', 0.01 * s);
    for (const [x, z] of [[-0.55, -0.5], [0.5, -0.5], [0.6, 0.0], [-0.65, -0.05]]) gableHouse(mb, x * s, z * s, 0.26 * s, 0.22 * s, 0.16 * s, stucco, tile, 'x');
    standard(mb, 0.25 * s, -0.3 * s, 0.6 * s, H('#a8261e'), 'flag');
  };

  // ============================================================ 接口 --
  const MODEL_OF = {
    roman: 'roman', persia: 'persia', arab: 'arab', kushan: 'kushan', wa: 'wa', korea: 'korea', steppe: 'steppe',
    tarim: 'tarim', seasia: 'seasia', yi: 'village', nanman: 'village', celt: 'hillfort', german: 'hillfort', sarmatian: 'sarmatian',
  };
  let capitals = null;
  function worldCapitals() {
    if (capitals) return capitals;
    capitals = new Set();
    try { const FI = SG.FactionInfo; if (FI) for (const k of Object.keys(FI)) if (FI[k] && FI[k].capital) capitals.add(FI[k].capital); } catch (e) { /* 无世界数据 */ }
    return capitals;
  }
  function cultureOfCity(city) {
    if (!city) return 'han';
    if (typeof city.culture === 'string' && city.culture) return city.culture;
    try { if (SG.WorldData && city.key && SG.Scenarios && SG.G && SG.G.scenario === 'world') return SG.WorldData.cultureOfCity(city.key) || 'han'; } catch (e) { /* 忽略 */ }
    return 'han';
  }
  function isCapital(city, culture) {
    if (!city) return false;
    if (city.key === 'luoyang' || city.key === 'changan') return true;
    const c = culture || cultureOfCity(city);
    return c !== 'han' && worldCapitals().has(city.key);
  }
  function cityInto(mb, size, culture, capital, city) {
    const m = MODEL_OF[culture];
    if (!m || !B[m]) return SG.Models.cityInto(mb, size, null, capital);
    B[m](mb, size, !!capital, city || null, culture);
    return mb;
  }

  SG.CultureArt = {
    cultures: Object.keys(MODEL_OF),
    cultureOfCity, isCapital, cityInto,
    city(size, culture, capital, city) { const mb = new SG.MeshBuilder(); cityInto(mb, size, culture, capital, city); return mb.toGeometry(); },
  };
})();
