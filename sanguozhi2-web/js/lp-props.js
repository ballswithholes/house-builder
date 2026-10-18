'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 网页版 · 低多边形道具库（SG.LPProps）—— Lanternvale 风格的共用配方

   依赖 js/lowpoly.js（SG.LP）。所有配方都按 Lanternvale 的画风建模：粗壮易读的剪影、平面着色的木石与建筑、
   软团树冠（上暖下青的色阶）、顶点色每面抖动、贴地 AO、需要的地方描墨线（草、花、作物、水面、落叶等贴地部件不描）、
   树叶 / 旗 / 灯穗随风摆动；夜里窗纸与灯笼自动发光（MeshBuilder.nightEmission，由场景的 LP.Mood 点亮）。
   坐标：Y 向上，1 单位 = 1 米，道具立在原点、正面朝 +Z（本游戏默认镜头从 +Z 看向 −Z）。每个配方按种子确定
   （seed & 3 = 四个变体桶），按需有近 / 远两级细节（lod: 'near' | 'far'），预算：树 ≤ 1.5k 三角形、小道具 ≤ 400、
   大建筑 ≤ 3.2k（每个配方的实际数在 tests/lp-props.html 的道具表上）。

   接口
   --------------------------------------------------------------------------
   SG.LPProps.names(group?) → 配方名数组；SG.LPProps.groups → 分组；SG.LPProps.info(name) → { group, label, lod, keys, defaults }
   SG.LPProps.geometry(name, opts?) → { ink, plain, lamps, decals, anchors, tris, size }（缓存）
        ink   = 带墨线的部分（BufferGeometry 或 null）；plain = 不描墨线的部分（花、草、水面、落叶、火焰）
        lamps = [{ pos:[x,y,z], color, intensity, range, flicker, nightOnly, haloSize }]（灯笼 / 火的光源位置，模型坐标）
        decals = 牌匾 / 招牌的文字贴花；anchors = 命名的挂点（如 'flag'、'door'）；size = [w, h, d]
   SG.LPProps.make(name, opts?) → THREE.Object3D（LP.mesh + 墨线 + 贴花）；userData.lp = { name, lamps, anchors, tris, size }
   SG.LPProps.addLamps(obj, mood) → 把道具的灯接到场景的 LP.Mood（按 obj 的世界矩阵）→ 灯记录数组
   SG.LPProps.scatter(scatter, name, opts, x, y, z, yaw, scale, tint) → 加入一个 LP.Scatter（实例化；远处用远景细节）
   SG.LPProps.batch(staticBatch, name, opts, matrix4 | { x, y, z, yaw, scale }) → 加入一个 LP.StaticBatch（按块合并）
   opts 通用：{ seed, lod, color（势力色：旗、帐、帆、马车布）, variant, lit（true / false / 'night'：灯默认 'night'） }，
        各配方另有自己的选项（见 SG.LPProps.info(name).defaults）

   分组与配方（tests/lp-props.html?group=… 看道具表）
     tree    tree_huai 槐 · tree_oak 栎 · tree_elm 榆 · tree_willow 垂柳 · tree_poplar 杨 · tree_birch 桦 · tree_pine 松 ·
             tree_pine_cloud 黄山松（云片） · tree_spruce 云杉 · tree_cypress 柏（地中海） · tree_cedar 雪松 · bamboo 竹丛 ·
             palm_date 海枣 · palm_coconut 椰子 · tree_olive 橄榄 · tree_acacia 金合欢 · tree_baobab 猴面包树 ·
             tree_mangrove 红树 · tree_autumn 秋树（variant: maple 枫 / ginkgo 银杏） · tree_blossom 花树（variant: plum 梅 / peach 桃） ·
             pine_snow 雪松树 · tree_dead 枯树
     plant   bush（variant: pilot 默认 / round / flowering） · flowers 花丛 · grass 草丛 · reeds 芦苇 · cattails 香蒲 · lotus 荷叶 · fern 蕨
     rock    rock_small · rock_large · boulder 巨石 · cliff 崖石 · rock_snow · stones 碎石
     crop    rice_paddy 水田 · wheat 麦 / 粟（variant: wheat / millet） · vegetables 菜畦 · haystack 草垛
     fence   fence_wood 木栅 · fence_wattle 篱笆 · wall_stone 石垣 · fence_bamboo 竹篱
     light   lamp_post 汉式灯杆 · lantern_hanging 挂灯笼 · oil_lamp 铜灯台 · brazier 火盆 · torch 火把 · stone_lantern 石灯笼
     banner  banner 战旗 · flag 三角旗 · banner_hanging 垂幡
     clutter crate 木箱 · barrel 木桶 · jar 陶缸 · sacks 粮袋 · weapon_rack 兵器架 · woodpile 柴堆
     vehicle cart 马车（辎重车） · wheelbarrow 独轮车
     boat    boat_river 乌篷船 · junk 海船（沙船） · galley 罗马桨帆船 · dhow 三角帆船
     village well 水井 · signpost 路牌 · tent_army 军帐 · tent_command 主帅大帐 · yurt 蒙古包 · bridge_wood 木桥 ·
             bridge_stone 石拱桥 · shrine 土地庙 · stele 石碑（variant: turtle 赑屃 / plain） · pavilion 亭
     building house_han 汉式民居 · house_thatch 茅屋 · house_med 地中海民居 · house_mud 平顶土屋 · dome_hall 穹顶殿 ·
             stupa 佛塔（窣堵坡） · watchtower 望楼 · gate_tower 城门楼 · city_wall 城墙段 · wall_segment 墙段 · tower_round 圆塔
   建筑部件（给城池工作流组合用）：SG.LPProps.parts.*(M, opts) 直接画进一个 LP.MeshBuilder（当前变换下）：
     hanRoof（歇山 xieshan / 庑殿 hip / 悬山 gable / 攒尖 pyramid；筒瓦、瓦当、起翘、正脊与鸱吻）· thatchRoof · terracottaRoof ·
     flatRoof · dome · stupa · wall（rammed / brick / stone / mud / plaster；垛口、收分）· gate（券门 / 方门，门钉）· tower ·
     window（格窗，夜里亮）· door · plinth · merlons · columns · banner · ribLantern（见 LP.MeshBuilder）
   ========================================================================== */
(function () {
  const SG = window.SG;
  if (!SG || !SG.LP) return;
  const LP = SG.LP, P = LP.Paint, V = LP.V, PAL = LP.PAL, H = P.hex;
  const D2R = Math.PI / 180;
  const clamp01 = v => (v < 0 ? 0 : v > 1 ? 1 : v);
  const clamp = (v, a, b) => (v < a ? a : v > b ? b : v);
  const lerp = (a, b, t) => a + (b - a) * t;
  const sstep = t => { t = clamp01(t); return t * t * (3 - 2 * t); };
  const Props = (SG.LPProps = {});
  const R = Object.create(null);
  const GROUPS = ['tree', 'plant', 'rock', 'crop', 'fence', 'light', 'banner', 'clutter', 'vehicle', 'boat', 'village', 'building'];
  Props.groups = GROUPS.slice();

  function strHash(s) { let h = 2166136261 >>> 0; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h; }
  const hashN = (i, j, s) => LP.hash01([i * 1.37 + 0.11, j * 2.91 + 0.37, s * 0.53], 41) * 2 - 1;   // −1 … 1
  /** A builder seeded per recipe name and variant bucket (LV PropKit.Builder). */
  function builder(o, salt, jitter = 0.06, ao = 0.3, aoH = 1, seed) {
    const M = new LP.MeshBuilder(seed != null ? seed : (strHash(o.name + (salt || '')) + o.bucket * 7919) | 0);
    M.jitter = jitter; M.aoStrength = ao; M.aoHeight = aoH;
    return M;
  }
  /**
   * Registers a recipe: def = { group, label, build(o) → MeshBuilder | { ink, plain, lamps, decals, anchors, size },
   * lod: true (has a far level), seeds: 4 (variant buckets), keys: option names that change the mesh, defaults }.
   */
  function recipe(name, def) { def.name = name; def.keys = def.keys || []; R[name] = def; }
  Props.recipe = recipe;

  // ---------------------------------------------------------------- cache
  const CACHE = new Map();
  function normalize(def, o) {
    o = Object.assign({}, def.defaults || {}, o || {});
    const n = def.seeds || 4;
    o.seed = o.seed | 0;
    o.bucket = ((o.seed % n) + n) % n;
    o.lod = o.lod === 'far' && def.lod ? 'far' : 'near';
    o.far = o.lod === 'far';
    o.name = def.name;
    return o;
  }
  function keyOf(def, o) {
    const parts = [def.name, o.bucket, o.lod];
    for (const k of def.keys) { const v = o[k]; parts.push(k + '=' + (v == null ? '' : typeof v === 'object' ? JSON.stringify(v) : v)); }
    return parts.join('|');
  }
  Props.geometry = function (name, opts) {
    const def = R[name];
    if (!def) throw new Error('LPProps: unknown prop ' + name);
    const o = normalize(def, opts);
    const key = keyOf(def, o);
    let res = CACHE.get(key);
    if (res) return res;
    let out = def.build(o);
    if (out instanceof LP.MeshBuilder) out = { ink: out };
    const ink = out.ink && !out.ink.isEmpty ? out.ink.toGeometry() : null;
    const plain = out.plain && !out.plain.isEmpty ? out.plain.toGeometry() : null;
    const box = new THREE.Box3();
    if (ink) box.union(ink.boundingBox);
    if (plain) box.union(plain.boundingBox);
    const sz = box.isEmpty() ? [0, 0, 0] : [box.max.x - box.min.x, box.max.y - box.min.y, box.max.z - box.min.z];
    res = {
      name, key, ink, plain, lamps: out.lamps || [], decals: out.decals || [], anchors: out.anchors || {},
      tris: (ink ? ink.userData.tris : 0) + (plain ? plain.userData.tris : 0), size: out.size || sz, box,
      castShadow: def.castShadow !== false, plainShadow: !!def.plainShadow,
    };
    CACHE.set(key, res);
    return res;
  };
  Props.names = group => Object.keys(R).filter(n => !group || R[n].group === group);
  Props.info = name => { const d = R[name]; return d ? { group: d.group, label: d.label, lod: !!d.lod, keys: d.keys.slice(), defaults: Object.assign({}, d.defaults || {}), seeds: d.seeds || 4 } : null; };
  Props.has = name => !!R[name];
  Props.clearCache = () => { for (const r of CACHE.values()) { if (r.ink) r.ink.dispose(); if (r.plain) r.plain.dispose(); } CACHE.clear(); };

  function decalMesh(d) {
    const tex = LP.boardTexture(d.text, { w: 448, h: Math.round(448 * d.h / d.w), board: d.board, ink: d.ink, vertical: d.vertical, line: d.line });
    const dm = LP.decal(tex, d.w, d.h);
    dm.position.set(d.at[0], d.at[1], d.at[2]);
    if (d.yaw) dm.rotation.y = d.yaw * D2R;
    if (d.tilt) dm.rotation.x = d.tilt * D2R;
    if (d.local) dm.translateZ(d.local[2]);
    return dm;
  }
  /** A ready object: the inked mesh (with its outline), the plain mesh (no ink) and painted name boards. */
  Props.make = function (name, opts) {
    const g = Props.geometry(name, opts);
    const ink = g.ink ? LP.mesh(g.ink, { castShadow: g.castShadow }) : null;
    const plain = g.plain ? LP.mesh(g.plain, { outline: false, castShadow: g.plainShadow }) : null;
    // a single inked mesh is returned as is (name boards as its children); with a plain part, a group of both
    let root;
    if (ink && !plain) root = ink;
    else { root = new THREE.Group(); if (ink) root.add(ink); if (plain) root.add(plain); }
    for (const d of g.decals) root.add(decalMesh(d));
    root.name = 'LPProp ' + name;
    root.userData.lp = { name, lamps: g.lamps, anchors: g.anchors, tris: g.tris, size: g.size };
    root.userData.lamps = g.lamps.map(l => l.pos);
    root.userData.tris = g.tris;
    return root;
  };
  /** Hooks a placed prop's lamps to a scene mood (world positions from obj.matrixWorld). */
  Props.addLamps = function (obj, mood) {
    const info = obj.userData.lp;
    if (!info || !mood) return [];
    obj.updateMatrixWorld(true);
    const v = new THREE.Vector3();
    return info.lamps.map(l => { v.set(l.pos[0], l.pos[1], l.pos[2]).applyMatrix4(obj.matrixWorld); return mood.addLamp([v.x, v.y, v.z], l); });
  };
  /** Adds one copy to an LP.Scatter (near + far levels, inked and plain parts). */
  Props.scatter = function (sc, name, opts, x, y, z, yaw, scale, tint) {
    const near = Props.geometry(name, opts);
    const def = R[name];
    const far = def.lod ? Props.geometry(name, Object.assign({}, opts, { lod: 'far' })) : null;
    if (near.ink) sc.add(far && far.ink ? { near: near.ink, far: far.ink } : near.ink, x, y, z, yaw, scale, tint, { castShadow: near.castShadow });
    if (near.plain) sc.add(far && far.plain ? { near: near.plain, far: far.plain } : near.plain, x, y, z, yaw, scale, tint, { outline: false, castShadow: near.plainShadow });
    return near;
  };
  const _bm = new THREE.Matrix4(), _bq = new THREE.Quaternion(), _bv = new THREE.Vector3(), _bs = new THREE.Vector3(), _be = new THREE.Euler();
  /** Adds one copy to an LP.StaticBatch at a matrix (or { x, y, z, yaw, scale }); name boards stay separate meshes. */
  Props.batch = function (sb, name, opts, at) {
    const g = Props.geometry(name, opts);
    let m = at;
    if (!at || !at.isMatrix4) {
      at = at || {};
      _be.set(0, at.yaw || 0, 0); _bq.setFromEuler(_be);
      const s = at.scale == null ? 1 : at.scale;
      if (Array.isArray(s)) _bs.set(s[0], s[1], s[2]); else _bs.set(s, s, s);
      m = _bm.compose(_bv.set(at.x || 0, at.y || 0, at.z || 0), _bq, _bs).clone();
    }
    if (g.ink) sb.add(g.ink, m, { castShadow: g.castShadow, tint: opts && opts.tint });
    if (g.plain) sb.add(g.plain, m, { outline: false, castShadow: g.plainShadow, tint: opts && opts.tint });
    for (const d of g.decals) { const dm = decalMesh(d); dm.updateMatrix(); dm.matrixAutoUpdate = false; dm.matrix.premultiply(m); sb.extra.push(dm); }
    return g;
  };

  // ============================================================ helpers ==
  /** Tapered faceted trunk (Lathe) squashed in depth (PropNature.Trunk). */
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
  /** Soft-lump crown: big masses (always) and silhouette lumps (near only); far = detail 0. list entries [c, r]. */
  function crown(M, o, blobs, lumps, ramp, salt, lumpy = 0.12, jitterPos = 0.3) {
    const det = o.far ? 0 : 1, b = o.bucket;
    blobs.forEach(([c, r], i) => M.softLump([c[0] + (M.random01() - 0.5) * jitterPos, c[1] + (M.random01() - 0.5) * jitterPos, c[2]], r, det, i * 47 + b * 13 + salt, ramp, lumpy));
    if (!o.far && lumps) lumps.forEach(([c, r], i) => M.softLump(c, r, 1, i * 61 + b * 17 + salt, ramp, lumpy + 0.02));
  }
  /** A tapered rib through points (stilt roots, gnarled limbs). */
  function rib(M, pts, r0, r1, sides = 5) {
    for (let i = 0; i < pts.length - 1; i++) M.segment(pts[i], pts[i + 1], lerp(r0, r1, i / (pts.length - 1)), lerp(r0, r1, (i + 1) / (pts.length - 1)), sides, false, true);
  }
  /**
   * A hanging / arching frond: a flat diamond cross-section (width along tang, thickness along up) lofted through pts,
   * each quad coloured by colorAt(y, t) — closed, so its ink hull stays thin (WildStrand).
   */
  function strand(M, pts, w, t, tang, up, colorAt) {
    const rings = pts.map((p, i) => [V.madd(p, tang, w[i]), V.madd(p, up, t[i]), V.madd(p, tang, -w[i]), V.madd(p, up, -t[i])]);
    for (let i = 0; i < pts.length - 1; i++) {
      const c = V.mul(V.add(pts[i], pts[i + 1]), 0.5);
      for (let j = 0; j < 4; j++) {
        const k = (j + 1) % 4, A = rings[i][j], B = rings[i][k], Cc = rings[i + 1][k], D = rings[i + 1][j];
        const mid = V.mul(V.add(V.add(A, B), V.add(Cc, D)), 0.25), col = colorAt(mid[1], (i + 0.5) / (pts.length - 1)), jk = 1 + (M.random01() * 2 - 1) * M.jitter;
        M.quad(A, B, Cc, D, V.sub(mid, c), [col[0] * jk, col[1] * jk, col[2] * jk]);
      }
    }
    M.fan(rings[0], pts[0], V.sub(pts[0], pts[1]), () => colorAt(pts[0][1], 0));
  }
  /** Quadratic curve point. */
  const qb = (a, c, b, t) => { const u = 1 - t; return [u * u * a[0] + 2 * u * t * c[0] + t * t * b[0], u * u * a[1] + 2 * u * t * c[1] + t * t * b[1], u * u * a[2] + 2 * u * t * c[2] + t * t * b[2]]; };
  /** Octahedron-ish gem (berries, pebbles, buds). */
  const gem = (M, c, r) => M.sphere(c, Array.isArray(r) ? r : [r, r, r], 4, 2, false);
  /** Five-petal bloom facing `normal` (≈ 14 tris): petals + a raised heart (PropKit.Bloom). */
  function bloom(M, c, r, normal, petal, heart, spin = 0) {
    normal = V.norm(normal);
    const u = V.norm(V.cross(normal, Math.abs(normal[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0])), v = V.cross(normal, u);
    const Dir = deg => { const a = deg * D2R; return V.add(V.mul(u, Math.cos(a)), V.mul(v, Math.sin(a))); };
    const lift = V.mul(normal, r * 0.18);
    for (let i = 0; i < 5; i++) {
      const a = spin + i * 72, k = 1 + (M.random01() * 2 - 1) * M.jitter, col = [petal[0] * k, petal[1] * k, petal[2] * k];
      const tip = V.add(V.madd(c, Dir(a), r), lift), left = V.add(V.madd(c, Dir(a - 27), r * 0.62), V.mul(lift, 0.5)), right = V.add(V.madd(c, Dir(a + 27), r * 0.62), V.mul(lift, 0.5));
      M.triangleFacing(c, left, tip, normal, col);
      M.triangleFacing(c, tip, right, normal, col);
    }
    const top = V.madd(c, normal, r * 0.22);
    for (let i = 0; i < 4; i++) { const a = spin + 20 + i * 90; M.triangleFacing(V.madd(c, Dir(a), r * 0.3), V.madd(c, Dir(a + 90), r * 0.3), top, normal, heart); }
  }
  /** Heart-shaped leaf (single-sided, facing normal) from `at` towards dir (PropKit.Leaf). */
  function leaf(M, at, dir, normal, size, col) {
    dir = V.norm(dir);
    const s = V.norm(V.cross(normal, dir)), w = size * 0.42, tip = V.madd(at, dir, size);
    const r1 = V.madd(V.madd(at, dir, size * 0.22), s, w), r2 = V.madd(V.madd(at, dir, size * 0.62), s, w * 0.72);
    const l1 = V.madd(V.madd(at, dir, size * 0.22), s, -w), l2 = V.madd(V.madd(at, dir, size * 0.62), s, -w * 0.72), notch = V.madd(at, dir, size * 0.06);
    M.triangleFacing(notch, r1, r2, normal, col); M.triangleFacing(notch, r2, tip, normal, col);
    M.triangleFacing(notch, tip, l2, normal, P.shade(col, 0.92)); M.triangleFacing(notch, l2, l1, normal, P.shade(col, 0.92));
  }
  /** Flat slab: a convex polygon (3D points in one plane) extruded thickness behind normal (signs, boards, pads). */
  function slab(M, poly, normal, thick, col) {
    const n = poly.length;
    if (n < 3) return;
    normal = V.norm(normal);
    const back = V.mul(normal, -thick), c = V.mul(poly.reduce((a, p) => V.add(a, p), [0, 0, 0]), 1 / n);
    const front = col || M.faceColor(), rear = col ? P.shade(col, 0.9) : M.faceColor();
    for (let i = 0; i < n; i++) {
      const p = poly[i], q = poly[(i + 1) % n];
      M.triangleFacing(c, p, q, normal, front);
      M.triangleFacing(V.add(c, back), V.add(p, back), V.add(q, back), V.neg(normal), rear);
      M.quad(p, q, V.add(q, back), V.add(p, back), V.sub(V.mul(V.add(p, q), 0.5), c), col ? P.shade(col, 0.95) : undefined);
    }
  }
  /** Points of a regular polygon (for slab). */
  function ngon(c, rx, ry, sides, right, up, offDeg = 0, from = 0, to = 360) {
    const full = Math.abs(to - from) >= 359.9, count = full ? sides : sides + 1, pts = [];
    for (let i = 0; i < count; i++) { const a = (from + offDeg + (to - from) * i / sides) * D2R; pts.push(V.add(V.add(c, V.mul(right, Math.cos(a) * rx)), V.mul(up, Math.sin(a) * ry))); }
    return pts;
  }
  /** Blob coloured by normal (rocks: top / side / bottom) — LP.byNormal wrapper with a moss tint on top. */
  const mossy = (stone, moss, k = 0.5) => LP.byNormal(P.mix(stone, moss, Math.min(0.85, k + 0.25)), stone, P.shade(stone, 0.78), 0.42);
  const H6 = (...hs) => hs.map(H);
  Props.helpers = { trunk, roots, crown, rib, strand, qb, gem, bloom, leaf, slab, ngon, mossy, builder, hashN };

  // ======================================================== palettes ==
  const BARK = { warm: H('#98724C'), warmDark: H('#735238'), grey: H('#7A6A5C'), dark: H('#5A4436'), pine: H('#6B4C3B'), pale: H('#A89080'), olive: H('#8A7E6C') };
  const autumnRamp = (top, side, bottom, y0, y1) => {
    const cool = P.mix(bottom, H('#6A4A62'), 0.22), warm = P.mix(top, H('#FFF2B8'), 0.25);
    return y => {
      const t = clamp01((y - y0) / Math.max(0.01, y1 - y0));
      if (t < 0.4) return P.mix(cool, side, sstep(t / 0.4));
      if (t < 0.78) return P.mix(side, top, sstep((t - 0.4) / 0.38));
      return P.mix(top, warm, (t - 0.78) / 0.22);
    };
  };

  // ============================================================== trees ==
  // 槐 (Chinese scholar tree), after Lanternvale's BuildOak (the pilot's approved tree): a stout faceted trunk forking at
  // 3 m into four limbs under a scalloped crown of seven big soft masses and seven rim lumps, deep green, teal underside.
  recipe('tree_huai', {
    group: 'tree', label: '槐', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.2, 1.2, 211 + b * 7919);
      M.windGradient = true; M.windY0 = 2.6; M.windY1 = 8; M.wind = 1.6;
      const bark = BARK.warm, barkDark = BARK.warmDark;
      M.push().rotate(0, 0, (b - 1.5) * 1.6);
      trunk(M, [[0.53, 0], [0.4, 0.3], [0.34, 1.2], [0.31, 2.4], [0.26, 3.3]], o.far ? 5 : 7, bark, 0.8, [barkDark, bark, bark, P.shade(bark, 1.06), bark]);
      if (!o.far) roots(M, 5, 0.7, 0.24, 0.55, b * 23, bark);
      M.color = bark;
      const fork = [0, 3.0, 0];
      const tips = [[-1.5, 4.5, 0.1], [1.5, 4.6, 0.3], [0.2, 5.2, -0.3], [-0.2, 5.0, 0.85]];
      for (const t of o.far ? tips.slice(0, 2) : tips) M.segment(fork, t, 0.24, 0.095, o.far ? 4 : 5);
      M.pop();
      const tune = { topVal: 0.94, coolMix: 0.6 };
      const f = LP.foliage(b, H('#376634'), 7, tune);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 3.6, 8.0, tune);
      const sx = 1 + 0.04 * ((b * 5) % 3);
      const blobs = [
        [[0, 5.5, 0.2], [2.3, 1.9, 2.0]], [[-1.75, 4.7, 0.1], [1.5, 1.3, 1.4]], [[1.85, 4.85, 0.35], [1.5, 1.35, 1.4]],
        [[0.55, 6.7, 0.15], [1.55, 1.35, 1.45]], [[-0.95, 6.35, 0.55], [1.35, 1.2, 1.3]], [[0.35, 4.5, -1.05], [1.35, 1.15, 1.1]],
        [[-0.3, 5.1, 1.6], [1.5, 1.3, 1.2]],
      ].map(([c, r]) => [[c[0] * sx, c[1], c[2]], [r[0] * sx, r[1], r[2]]]);
      const lumps = [
        [[-2.95, 5.2, 0.3], [0.85, 0.75, 0.8]], [[3.0, 5.45, 0.55], [0.85, 0.75, 0.8]], [[-1.75, 7.1, 0.25], [0.8, 0.7, 0.75]],
        [[1.75, 6.95, 0.45], [0.9, 0.75, 0.8]], [[-1.35, 4.0, -0.75], [0.85, 0.62, 0.72]], [[1.5, 4.1, -0.6], [0.85, 0.62, 0.72]],
        [[0.15, 7.8, 0.4], [0.85, 0.62, 0.8]],
      ].map(([c, r]) => [[c[0] * sx, c[1], c[2]], r]);
      crown(M, o, blobs, lumps, ramp, 0);
      return M;
    },
  });

  // 栎 (oak): a short, thick trunk forking low into five spreading limbs under a broad, low, rounded crown in a
  // fresher green — wider than tall, the shade tree of a village green.
  recipe('tree_oak', {
    group: 'tree', label: '栎', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.22, 1.2);
      M.windGradient = true; M.windY0 = 2.0; M.windY1 = 7; M.wind = 1.5;
      const bark = H('#8C6A4C');
      M.push().rotate(0, b * 50, (b - 1.5) * 2);
      trunk(M, [[0.62, 0], [0.47, 0.3], [0.4, 1.0], [0.38, 1.9], [0.32, 2.4]], o.far ? 5 : 7, bark, 0.82, [P.shade(bark, 0.78), bark, bark, P.shade(bark, 1.05), bark]);
      if (!o.far) roots(M, 6, 0.8, 0.26, 0.6, b * 31, bark);
      M.color = bark;
      const fork = [0, 2.2, 0];
      const tips = [[-2.4, 3.9, 0.2], [2.5, 4.0, -0.1], [-0.6, 4.6, -1.2], [0.8, 4.5, 1.3], [0.1, 5.0, 0.1]];
      for (const t of o.far ? tips.slice(0, 2) : tips) M.segment(fork, t, 0.27, 0.1, o.far ? 4 : 5);
      M.pop();
      const f = LP.foliage(b, H('#4E7D3A'), 8);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 3.0, 6.8);
      const blobs = [
        [[0, 4.9, 0.1], [2.6, 1.6, 2.2]], [[-2.2, 4.4, 0.2], [1.6, 1.2, 1.5]], [[2.3, 4.5, 0.0], [1.6, 1.25, 1.5]],
        [[-0.9, 5.8, 0.3], [1.6, 1.2, 1.5]], [[1.0, 5.9, -0.2], [1.6, 1.2, 1.4]], [[0.2, 4.3, 1.5], [1.7, 1.1, 1.2]], [[0.1, 4.4, -1.4], [1.6, 1.1, 1.1]],
      ];
      const lumps = [
        [[-3.6, 4.3, 0.2], [0.85, 0.7, 0.8]], [[3.7, 4.4, 0.1], [0.85, 0.7, 0.8]], [[-2.3, 5.6, 0.6], [0.85, 0.7, 0.8]], [[2.4, 5.7, 0.4], [0.9, 0.7, 0.8]],
        [[0, 6.7, 0.2], [0.95, 0.6, 0.85]], [[-1.4, 3.7, 1.0], [0.85, 0.55, 0.7]], [[1.6, 3.8, 0.9], [0.85, 0.55, 0.7]],
      ];
      crown(M, o, blobs, lumps, ramp, 3);
      return M;
    },
  });

  // 榆 (elm): a tall clean trunk opening like a vase into upswept limbs; the crown is narrow below and wide on top.
  recipe('tree_elm', {
    group: 'tree', label: '榆', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.2, 1.2);
      M.windGradient = true; M.windY0 = 3.2; M.windY1 = 10; M.wind = 1.7;
      const bark = H('#857060');
      M.push().rotate(0, b * 40, (b - 1.5) * 1.4);
      trunk(M, [[0.46, 0], [0.34, 0.3], [0.29, 1.5], [0.26, 3.2], [0.22, 3.9]], o.far ? 5 : 7, bark, 0.82);
      if (!o.far) roots(M, 5, 0.65, 0.22, 0.55, b * 17, bark);
      M.color = bark;
      const fork = [0, 3.6, 0];
      const tips = [[-1.6, 6.2, 0.2], [1.7, 6.4, 0.1], [0.3, 7.0, -0.7], [-0.3, 6.8, 0.9], [0.9, 6.0, 0.7]];
      for (const t of o.far ? tips.slice(0, 2) : tips) M.segment(fork, t, 0.2, 0.07, o.far ? 4 : 5);
      M.pop();
      const f = LP.foliage(b, H('#557F3C'), 7);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 5.0, 9.8);
      const blobs = [
        [[0, 7.5, 0.1], [2.4, 1.6, 2.0]], [[-1.95, 6.7, 0.2], [1.4, 1.3, 1.3]], [[2.0, 6.9, 0.1], [1.45, 1.3, 1.3]],
        [[0.2, 8.7, 0.1], [1.7, 1.1, 1.4]], [[-0.5, 6.3, 1.2], [1.3, 1.1, 1.1]], [[0.6, 6.4, -1.1], [1.3, 1.1, 1.1]],
      ];
      const lumps = [
        [[-3.2, 7.4, 0.3], [0.85, 0.75, 0.8]], [[3.3, 7.6, 0.2], [0.85, 0.75, 0.8]], [[-1.7, 8.9, 0.3], [0.85, 0.65, 0.75]], [[1.9, 8.8, 0.2], [0.85, 0.65, 0.75]],
        [[0, 5.6, 0.0], [0.9, 0.6, 0.9]], [[-0.3, 9.6, 0.2], [0.8, 0.5, 0.7]], [[1.3, 6.0, 1.2], [0.75, 0.6, 0.7]],
      ];
      crown(M, o, blobs, lumps, ramp, 5);
      return M;
    },
  });

  // 垂柳 (weeping willow, Lanternvale BuildWillow in a fresher spring green): a soft dome on a leaning trunk and long
  // hanging fronds around its rim that sway most at their free ends; the front fronds hang shorter so whoever stands
  // beneath stays readable.
  recipe('tree_willow', {
    group: 'tree', label: '垂柳', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 1.2);
      const bark = H('#7C6C5E');
      M.push().rotate(0, b * 35, (b - 1.5) * 2.5);
      trunk(M, [[0.5, 0], [0.38, 0.4], [0.34, 1.6], [0.31, 2.6], [0.24, 3.3]], o.far ? 5 : 7, bark, 0.8);
      if (!o.far) roots(M, 5, 0.7, 0.22, 0.6, b * 19, bark);
      M.color = bark;
      const limbs = [[-1.6, 5.3, 0.3], [1.7, 5.5, 0.1], [0.1, 6.3, 0.6], [0.4, 5.4, -0.9], [-0.6, 5.6, 1.1]];
      for (const l of o.far ? limbs.slice(0, 2) : limbs) M.segment([0, 3.1, 0], l, 0.17, 0.06, 5);
      M.pop();
      const f = LP.foliage(b, H('#55843A'), 8, { topVal: 0.95, warmMix: 0.06 });
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 1.4, 7.6);
      M.windGradient = true; M.windY0 = 3.5; M.windY1 = 7.5; M.wind = 1.4;
      const dome = [[[0, 6.2, 0.2], [2.3, 1.2, 1.9]], [[-1.4, 5.7, 0.1], [1.35, 0.95, 1.3]], [[1.5, 5.85, 0.25], [1.35, 0.95, 1.3]], [[0.2, 7.0, 0.2], [1.4, 0.75, 1.2]]];
      crown(M, o, dome, null, ramp, 7, 0.12, 0);
      // weeping curtains
      M.windGradient = true; M.windY0 = 6.0; M.windY1 = 1.5; M.wind = 2.3;
      const drapes = o.far ? 12 : 32, sd = 211 + b;
      for (let i = 0; i < drapes; i++) {
        const a = (i * 360 / drapes + hashN(i, 1, sd) * 6) * D2R, inner = !o.far && i % 3 === 2;
        const rr = (inner ? 1.55 : 2.2) + 0.4 * (hashN(i, 2, sd) * 0.5 + 0.5);
        let len = (inner ? 2.2 : 2.7) + 0.9 * (hashN(i, 3, sd) * 0.5 + 0.5);
        if (Math.sin(a) > 0.45) len *= 0.68;              // the camera side (+Z) hangs shorter
        const topY = (inner ? 5.4 : 5.85) + 0.3 * hashN(i, 4, sd);
        const radial = [Math.cos(a), 0, Math.sin(a) * 0.85], tang = V.norm([-Math.sin(a), 0, Math.cos(a)]), off = [0, 0, 0.15];
        const pts = [V.add(off, V.madd([0, topY, 0], radial, rr)), V.add(off, V.madd([0, topY - len * 0.32, 0], radial, rr + 0.28)),
          V.add(off, V.madd([0, topY - len * 0.68, 0], radial, rr + 0.32)), V.add(off, V.madd([0, topY - len, 0], radial, rr + 0.24))];
        if (o.far) strand(M, [pts[0], pts[2], pts[3]], [0.42, 0.24, 0.04], [0.12, 0.08, 0.015], tang, V.norm(radial), y => ramp(y));
        else strand(M, pts, [0.28, 0.24, 0.16, 0.025], [0.1, 0.085, 0.06, 0.012], tang, V.norm(radial), y => ramp(y));
      }
      M.wind = 0; M.windGradient = false;
      return M;
    },
  });

  // 杨 (poplar): a tall, narrow column of soft masses on a straight pale trunk — the roadside windbreak tree of the north.
  recipe('tree_poplar', {
    group: 'tree', label: '杨', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.2, 1.2);
      M.windGradient = true; M.windY0 = 2.0; M.windY1 = 12.5; M.wind = 1.6;
      const bark = H('#9C9282'), k = 0.92 + b * 0.05;
      trunk(M, [[0.36, 0], [0.27, 0.3], [0.23, 2.0], [0.16, 6], [0.06, 10.5 * k]], o.far ? 5 : 6, bark, 0.85, [P.shade(bark, 0.8), bark, P.shade(bark, 1.06), bark, bark]);
      if (!o.far) roots(M, 4, 0.5, 0.17, 0.55, b * 29, bark);
      const f = LP.foliage(b, H('#5E8C3E'), 6);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2.0, 12.5 * k);
      const blobs = [
        [[0, 3.4 * k, 0], [1.25, 1.5, 1.15]], [[0.1, 5.2 * k, 0.05], [1.4, 1.6, 1.25]], [[-0.05, 7.1 * k, 0], [1.3, 1.6, 1.2]],
        [[0.05, 9.0 * k, 0.05], [1.05, 1.5, 1.0]], [[0, 10.7 * k, 0], [0.7, 1.2, 0.7]],
      ];
      const lumps = [
        [[-0.85, 4.3 * k, 0.3], [0.65, 0.85, 0.6]], [[0.9, 6.2 * k, 0.2], [0.65, 0.9, 0.6]], [[-0.8, 8.1 * k, 0.25], [0.6, 0.85, 0.55]],
        [[0.7, 3.0 * k, -0.4], [0.6, 0.7, 0.55]], [[0.55, 9.8 * k, 0.3], [0.5, 0.7, 0.5]], [[0, 11.9 * k, 0.1], [0.4, 0.7, 0.4]],
      ];
      crown(M, o, blobs, lumps, ramp, 9, 0.12, 0.15);
      return M;
    },
  });

  // 桦 (birch, Lanternvale BuildBirch): a slender white trunk with dark marks (a second, thinner one on odd seeds) and
  // airy clusters of small soft lumps around the branch tips.
  function birchTrunk(M, at, leanDeg, r0, height, white, mark, sides) {
    const prof = [], cols = [];
    const Pp = (r, y, c) => { prof.push([r, y]); cols.push(c); };
    const Rr = y => lerp(r0, r0 * 0.4, y / height);
    Pp(r0 * 1.15, 0, mark); Pp(r0, 0.35, white);
    for (const m of [0.9, 1.6, 2.5, 3.2, 4.1, 5.0]) {
      if (m > height - 0.4) break;
      const h = 0.05 + ((m * 7.3) % 1) * 0.06;
      Pp(Rr(m), m, white); Pp(Rr(m), m, mark); Pp(Rr(m + h), m + h, mark); Pp(Rr(m + h), m + h, white);
    }
    Pp(Rr(height) * 0.6, height, white);
    M.color = white;
    M.push().translate(at).rotate(0, 0, leanDeg).scale(1, 1, 0.9);
    M.lathe(prof, sides, false, false, true, cols);
    M.pop();
  }
  recipe('tree_birch', {
    group: 'tree', label: '桦', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.25, 1.0);
      M.windGradient = true; M.windY0 = 1.2; M.windY1 = 7.5; M.wind = 2.2;
      const white = H('#EDE8DC'), mark = H('#3E3836');
      birchTrunk(M, [0, 0, 0], 0, 0.17, 6.3, white, mark, o.far ? 5 : 6);
      if (b % 2 === 1 && !o.far) birchTrunk(M, [0.12, 0, 0.05], -11, 0.1, 4.8, white, mark, 5);
      M.color = H('#6E6560');
      if (!o.far) for (const [a, c] of [[[0, 3.6, 0], [-1.1, 5.0, 0.2]], [[0, 4.2, 0], [1.0, 5.6, -0.1]], [[0, 5.0, 0], [-0.4, 6.4, -0.4]]]) M.segment(a, c, 0.06, 0.025, 4);
      const f = LP.foliage(b, H('#7CA650'), 9);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 3.6, 7.8);
      const list = [[[-1.1, 5.2, 0.2], 0.95], [[1.0, 5.7, -0.1], 1.0], [[-0.35, 6.6, -0.3], 1.0], [[0.4, 7.0, 0.4], 0.85], [[0.05, 4.4, 0.6], 0.8], [[0.6, 4.6, -0.7], 0.7],
        [[-0.8, 4.1, -0.5], 0.6], [[-1.75, 5.5, 0.0], 0.5], [[1.7, 5.95, 0.1], 0.5], [[-0.5, 7.35, 0.1], 0.48], [[1.25, 4.85, -0.45], 0.45]];
      const toL = ([c, r]) => [c, [r * 1.1, r * 0.85, r]];
      crown(M, o, list.slice(0, o.far ? 6 : 7).map(toL), list.slice(7).map(toL), ramp, 11, 0.13, 0);
      return M;
    },
  });

  // 松 (pine, the pilot's / Lanternvale BuildPine): faceted trunk, six soft skirts with a canopy-wide ramp.
  function pineTiers(M, o, f, ramp, tiers, y0, dy, r0, dr, h0, dh, sides, salt, k = 1, capFn) {
    for (let i = 0; i < tiers; i++) {
      const y = (y0 + i * dy) * k, r = (r0 - i * dr + (i % 2) * 0.08), h = (h0 - i * dh) * k;
      M.push().translate(0, y, 0).rotate((M.random01() - 0.5) * 6, 0, (M.random01() - 0.5) * 6);
      const under = P.shade(ramp(y - 0.3), 0.78);
      M.color = f.side;
      M.lathe([[r * 0.8, -0.42], [r * 0.97, -0.24], [r, -0.12], [r * 0.55, h * 0.36], [0, h]], o.far ? 6 : sides, true, true, false,
        [under, P.mix(under, ramp(y), 0.6), ramp(y), ramp(y + h * 0.36), ramp(y + h)], i * 19 + o.bucket * 11 + salt);
      if (capFn) capFn(i, r, h);
      M.pop();
    }
  }
  recipe('tree_pine', {
    group: 'tree', label: '松', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 1.0, 307 + b * 7919);
      M.windGradient = true; M.windY0 = 1.5; M.windY1 = 11; M.wind = 1.4;
      const bark = BARK.pine;
      trunk(M, [[0.32, 0], [0.23, 0.4], [0.18, 4], [0.1, 9], [0.04, 10.6]], o.far ? 5 : 6, bark, 0.85);
      if (!o.far) roots(M, 4, 0.48, 0.16, 0.55, 40 + b * 17, bark);
      const f = LP.foliage(b, H('#3F6B4A'), 6);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2.2, 11.2);
      pineTiers(M, o, f, ramp, 6, 2.55, 1.36, 2.3, 0.33, 2.45, 0.15, 9, 0);
      M.color = ramp(11.2);
      M.cone([0, 2.55 + 6 * 1.36 - 0.45, 0], 0.35, 1.3, 6);
      return M;
    },
  });
  // 云杉 (spruce): narrower and taller than the pine, eight darker blue-green tiers to a sharp tip.
  recipe('tree_spruce', {
    group: 'tree', label: '云杉', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 1.0);
      M.windGradient = true; M.windY0 = 1.2; M.windY1 = 12; M.wind = 1.2;
      const bark = H('#5E4434'), k = 0.94 + b * 0.04;
      trunk(M, [[0.28, 0], [0.2, 0.4], [0.15, 4], [0.08, 9], [0.03, 11.4 * k]], 5, bark, 0.85);
      if (!o.far) roots(M, 4, 0.4, 0.14, 0.55, 20 + b * 23, bark);
      const f = LP.foliage(b, H('#2F5A4A'), 5);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 1.2, 12.2 * k);
      pineTiers(M, o, f, ramp, o.far ? 6 : 8, 1.3, o.far ? 1.62 : 1.22, 1.75, o.far ? 0.25 : 0.19, 2.05, 0.12, 8, 5, k);
      M.color = ramp(12.2 * k);
      M.cone([0, (1.3 + 8 * 1.22) * k - 0.6, 0], 0.26, 1.5, 5);
      return M;
    },
  });
  // 黄山松 (a Chinese painting pine): a crooked trunk throwing a few near-horizontal limbs, each carrying a flat "cloud" pad
  // of needles — the silhouette of a landscape scroll.
  recipe('tree_pine_cloud', {
    group: 'tree', label: '黄山松', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.25, 1.0), s = b % 2 ? -1 : 1;
      M.windGradient = true; M.windY0 = 2.0; M.windY1 = 7.5; M.wind = 1.1;
      const bark = H('#76604E');
      const path = [[0, 0, 0], [0.25 * s, 1.4, 0.05], [-0.15 * s, 2.7, -0.05], [0.35 * s, 4.0, 0.1], [0.1 * s, 5.4, 0], [0.45 * s, 6.4, 0.1]];
      M.color = bark;
      rib(M, path, 0.36, 0.12, o.far ? 5 : 6);
      if (!o.far) roots(M, 4, 0.55, 0.2, 0.6, b * 37, bark);
      // limbs: near-horizontal, slightly drooping then lifting at the tip
      const limbs = [[2.2, [-2.6 * s, 2.6, 0.3], 1.0], [3.4, [2.7 * s, 3.9, -0.2], 1.0], [4.6, [-2.1 * s, 5.0, 0.4], 0.85], [5.6, [1.8 * s, 6.0, 0.2], 0.75], [6.3, [0.4 * s, 6.9, 0], 0.7]];
      const f = LP.foliage(b, H('#3E6A44'), 5, { coolMix: 0.5 });
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2.2, 7.6, { coolMix: 0.5 });
      limbs.forEach(([y, tip, k], i) => {
        const from = [path[Math.min(5, Math.round(y / 1.3))][0], y, 0];
        M.color = bark;
        if (!o.far || i < 3) M.curve(from, [lerp(from[0], tip[0], 0.55), y - 0.15, tip[2] * 0.5], tip, 0.14 * k, 0.06, 3, 5);
        // a flat cloud pad: a broad, low soft mass with smaller pads around its rim
        const det = o.far ? 0 : 1;
        M.softLump([tip[0], tip[1] + 0.25, tip[2]], [1.45 * k, 0.42 * k, 1.1 * k], det, i * 31 + b * 7, ramp, 0.1);
        if (!o.far && i < 4) M.softLump([tip[0] + 0.85 * k * Math.sign(tip[0] || 1), tip[1] + 0.12, tip[2] + 0.3], [0.75 * k, 0.32 * k, 0.6 * k], 1, i * 43 + 5, ramp, 0.12);
        if (!o.far && i < 2) M.softLump([tip[0] - 0.7 * k * Math.sign(tip[0] || 1), tip[1] + 0.38, tip[2] - 0.25], [0.7 * k, 0.3 * k, 0.6 * k], 1, i * 53 + 9, ramp, 0.12);
      });
      return M;
    },
  });
  // 柏 (Mediterranean cypress): a tall, dark, narrow flame of stacked soft masses tapering to a point.
  recipe('tree_cypress', {
    group: 'tree', label: '柏', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.25, 1.0);
      M.windGradient = true; M.windY0 = 1.0; M.windY1 = 9.5; M.wind = 1.0;
      const bark = H('#6A5442'), k = 0.9 + b * 0.06;
      trunk(M, [[0.24, 0], [0.18, 0.3], [0.14, 1.2], [0.08, 5]], 5, bark, 0.9);
      const f = LP.foliage(b, H('#2D5636'), 5, { coolMix: 0.5 });
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 0.7, 9.4 * k, { coolMix: 0.5 });
      const col = [[0, 1.7, 0, 0.95, 1.3], [0, 3.2, 0, 1.02, 1.5], [0, 4.8, 0, 0.92, 1.5], [0, 6.3, 0, 0.74, 1.4], [0, 7.6, 0, 0.52, 1.15], [0, 8.6, 0, 0.3, 0.8]];
      const blobs = col.map(([x, y, z, r, h]) => [[x, y * k, z], [r, h * k, r * 0.95]]);
      const lumps = [[[0.55, 2.4 * k, 0.3], [0.5, 0.8, 0.45]], [[-0.5, 3.9 * k, 0.25], [0.5, 0.85, 0.45]], [[0.45, 5.5 * k, -0.2], [0.45, 0.8, 0.4]], [[-0.38, 6.8 * k, 0.2], [0.36, 0.7, 0.34]], [[0.15, 9.3 * k, 0.05], [0.16, 0.5, 0.16]]];
      crown(M, o, blobs, lumps, ramp, 13, 0.1, 0.1);
      return M;
    },
  });
  // 雪松 (cedar of Lebanon / deodar): a stout trunk with broad, flat, layered tiers of needles — the great tree of the
  // Levant and the Himalaya foothills.
  recipe('tree_cedar', {
    group: 'tree', label: '雪松', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.25, 1.0);
      M.windGradient = true; M.windY0 = 2.0; M.windY1 = 9.5; M.wind = 1.0;
      const bark = H('#6E5646');
      trunk(M, [[0.5, 0], [0.38, 0.4], [0.32, 2.5], [0.22, 6], [0.08, 9.2]], o.far ? 5 : 6, bark, 0.85);
      if (!o.far) roots(M, 5, 0.6, 0.2, 0.6, b * 13, bark);
      const f = LP.foliage(b, H('#3E6656'), 5, { coolMix: 0.45 });
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2.0, 9.5, { coolMix: 0.45 });
      // flat tiers with clear gaps between them; each tier a broad pad pushed off-centre plus side pads
      const tiers = [[2.7, 3.1, 0.6], [4.5, 2.6, -0.5], [6.0, 2.0, 0.45], [7.3, 1.4, -0.3], [8.3, 0.8, 0.1]];
      tiers.forEach(([y, r, off], i) => {
        const det = o.far ? 0 : 1;
        const a = (i * 67 + b * 31) * D2R, ox = off * Math.cos(a), oz = off * 0.6 * Math.sin(a);
        M.color = bark;
        if (!o.far && i < 3) for (const s of [-1, 1]) M.segment([0, y - 0.25, 0], [s * r * 0.75 * Math.cos(a) + ox, y + 0.02, s * r * 0.5 * Math.sin(a) + oz], 0.1, 0.04, 4);
        M.softLump([ox, y, oz], [r, 0.3 + 0.02 * i, r * 0.78], det, i * 29 + b * 5, ramp, 0.1);
        if (!o.far && r > 1.2) for (const s of [-1, 1]) M.softLump([ox + s * r * 0.66 * Math.cos(a + 0.9), y + 0.1, oz + s * r * 0.55 * Math.sin(a + 0.9)], [r * 0.46, 0.26, r * 0.4], 1, i * 37 + s * 11, ramp, 0.12);
      });
      M.color = ramp(9.5);
      M.cone([0, 8.6, 0], 0.3, 1.0, 5);
      return M;
    },
  });

  // 竹丛 (bamboo clump): segmented green culms leaning out from a clump, node bands and leaf sprays (no ink on the leaves).
  recipe('bamboo', {
    group: 'tree', label: '竹丛', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.25, 0.8), L = builder(o, 'leaves', 0.08, 0, 1);
      const sd = 301 + b, n = o.far ? 6 : 7;
      const green = H('#7FA046'), greenDark = H('#5E7E38'), node = H('#C8C08A');
      const lf = LP.foliage(b, H('#6E9A40'), 6);
      const ramp = LP.canopyRamp(lf.top, lf.side, lf.bottom, 2.0, 7.0);
      for (let i = 0; i < n; i++) {
        const a = (i * 137.5 + b * 40) * D2R, d = 0.12 + 0.32 * Math.sqrt((i + 0.5) / n);
        const root = [Math.cos(a) * d, 0, Math.sin(a) * d * 0.8], h = 5.2 + 1.6 * (hashN(i, 1, sd) * 0.5 + 0.5) - d * 1.2;
        const lean = V.add(V.mul([Math.cos(a), 0, Math.sin(a) * 0.8], 0.12 + 0.25 * d), [hashN(i, 2, sd) * 0.05, 0, 0]);
        const tip = V.add(root, [lean[0] * h, h, lean[2] * h]);
        const r0 = 0.07 - d * 0.03, segs = o.far ? 3 : Math.round(h / 0.7);
        M.windGradient = true; M.windY0 = 0.5; M.windY1 = 7.2; M.wind = 1.5;
        for (let s = 0; s < segs; s++) {
          // each internode: a pale node band at its foot (a wider first ring), green above, alternating tones
          const t0 = s / segs, t1 = (s + 1) / segs;
          const p0 = V.lerp(root, tip, t0), p1 = V.lerp(root, tip, t1);
          const ra = lerp(r0, r0 * 0.55, t0), rb = lerp(r0, r0 * 0.55, t1);
          M.push().aim(p0, V.sub(p1, p0));
          M.lathe([[ra * 1.16, 0], [rb, V.len(V.sub(p1, p0))]], o.far ? 4 : 5, false, false, s === segs - 1, [P.mix(node, green, 0.3), s % 2 ? green : P.mix(green, greenDark, 0.35)]);
          M.pop(); M.pop();
        }
        // leaf sprays on the upper part: fans of narrow drooping leaves (plain), and at a distance soft lumps
        L.windGradient = true; L.windY0 = 2.0; L.windY1 = 7.2; L.wind = 1.8;
        const sprays = o.far ? 0 : 6;
        for (let k = 0; k < sprays; k++) {
          const t = 0.42 + k * 0.095, at = V.lerp(root, tip, t), az = a + (k % 2 ? 1 : -1) * (0.9 + 0.4 * hashN(i, k, sd));
          const out = [Math.cos(az), 0, Math.sin(az)];
          for (let q = 0; q < 5; q++) {
            const spread = (q - 2) * 0.32, dir = V.norm([out[0] * Math.cos(spread) - out[2] * Math.sin(spread), -0.3 - 0.1 * (q % 2), out[0] * Math.sin(spread) + out[2] * Math.cos(spread)]);
            const base = V.madd(at, out, 0.08), side = V.norm(V.cross(dir, [0, 1, 0])), nrm = V.norm(V.add(V.cross(side, dir), [0, 0.6, 0]));
            // a narrow lance leaf (2 tris, facing up and out)
            const len = 0.7 + 0.14 * (q % 2), wl = 0.075, tipL = V.madd(base, dir, len), mid = V.madd(base, dir, len * 0.4);
            const nUp = V.dot(nrm, [0, 1, 0]) < 0 ? V.neg(nrm) : nrm, cl = P.shade(ramp(at[1]), 0.9 + 0.16 * ((q + k) % 3) / 2);
            L.triangleFacing(base, V.madd(mid, side, wl), tipL, nUp, cl); L.triangleFacing(base, tipL, V.madd(mid, side, -wl), nUp, P.shade(cl, 0.92));
          }
        }
        if (o.far) M.softLump(V.lerp(root, tip, 0.8), [0.7, 0.9, 0.6], 0, i * 17, ramp, 0.15);
      }
      return { ink: M, plain: L };
    },
  });

  // 海枣 (date palm): a tall straight trunk of leaf scars, a crown of arching fronds over a skirt of dry ones, date clusters.
  function palmFrond(M, base, az, len, rise, droop, width, ramp, twist = 0) {
    const dir = [Math.cos(az), 0, Math.sin(az)], tang = V.norm([-Math.sin(az), 0, Math.cos(az)]);
    const n = 5, pts = [], w = [], t = [];
    for (let i = 0; i <= n; i++) {
      const s = i / n;
      pts.push(V.add(base, [dir[0] * len * s, rise * s - droop * s * s, dir[2] * len * s]));
      w.push(width * Math.pow(Math.sin(Math.PI * Math.min(0.98, 0.12 + s * 0.88)), 0.7) + 0.02);
      t.push(0.035 * (1 - s) + 0.01);
    }
    const up = V.norm([twist * tang[0], 1, twist * tang[2]]);
    strand(M, pts, w, t, tang, up, (y, s) => ramp(s));
  }
  recipe('palm_date', {
    group: 'tree', label: '海枣', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.25, 1.0), sd = 401 + b;
      const bark = H('#9A7E5E'), barkDark = H('#7A6048'), hgt = 7.2 + b * 0.35;
      // trunk: alternating leaf-scar rings, a slight bend
      M.push().rotate(0, b * 50, (b - 1.5) * 1.5);
      const prof = [], cols = [];
      const rings = o.far ? 4 : 14;
      for (let i = 0; i <= rings; i++) { const y = hgt * i / rings, r = lerp(0.34, 0.25, i / rings) + (i === 0 ? 0.08 : 0); prof.push([r, y]); cols.push(i % 2 ? barkDark : bark); }
      M.color = bark;
      M.lathe(prof, o.far ? 5 : 6, false, false, true, cols);
      // crown
      const top = [0, hgt, 0];
      const f = { top: H('#9CB45A'), side: H('#6E8F3E'), bottom: H('#46663A') };
      const ramp = s => (s < 0.5 ? P.mix(f.bottom, f.side, s / 0.5) : P.mix(f.side, f.top, (s - 0.5) / 0.5));
      M.windGradient = true; M.windY0 = hgt - 0.5; M.windY1 = hgt + 2.5; M.wind = 1.4;
      const nF = o.far ? 7 : 14;
      for (let i = 0; i < nF; i++) {
        const az = (i * 360 / nF + hashN(i, 1, sd) * 12) * D2R, upper = i % 2 === 0;
        palmFrond(M, V.add(top, [0, 0.15, 0]), az, upper ? 3.0 : 3.4, upper ? 1.9 : 1.1, upper ? 1.5 : 2.2, 0.32, ramp, 0.25);
      }
      M.wind = 0.6;
      // dry fronds hanging under the crown
      if (!o.far) for (let i = 0; i < 4; i++) {
        const az = (i * 90 + 30 + b * 20) * D2R;
        palmFrond(M, V.add(top, [0, -0.35, 0]), az, 2.2, -0.2, 1.4, 0.18, s => P.mix(H('#8E7650'), H('#B49A6A'), s), 0);
      }
      // date clusters
      M.color = H('#D2843A');
      if (!o.far) for (let i = 0; i < 3; i++) {
        const az = (i * 120 + b * 30) * D2R, c = V.add(top, [Math.cos(az) * 0.4, -0.55, Math.sin(az) * 0.4]);
        M.softLump(c, [0.2, 0.32, 0.2], 0, i * 9, y => P.mix(H('#A8582C'), H('#E09A44'), clamp01((y - (c[1] - 0.3)) / 0.6)), 0.25);
      }
      M.color = barkDark; M.sphere(V.add(top, [0, 0.1, 0]), [0.32, 0.3, 0.32], 6, 3, false);
      M.pop();
      return M;
    },
  });
  // 椰子 (coconut palm): a long trunk curving away from the camera's side, lush drooping fronds, a cluster of coconuts.
  recipe('palm_coconut', {
    group: 'tree', label: '椰子', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.25, 1.0), sd = 433 + b;
      const bark = H('#A08868'), barkDark = H('#80694E'), hgt = 7.5 + b * 0.3;
      const s = b % 2 ? -1 : 1;
      const a0 = [0, 0, 0], c0 = [0.2 * s, hgt * 0.55, -0.1], top = [1.7 * s, hgt, -0.4];
      const n = o.far ? 4 : 10;
      for (let i = 0; i < n; i++) {
        const p0 = qb(a0, c0, top, i / n), p1 = qb(a0, c0, top, (i + 1) / n);
        M.color = i % 2 ? barkDark : bark;
        M.segment(p0, p1, lerp(0.3, 0.2, i / n) + (i === 0 ? 0.06 : 0), lerp(0.3, 0.2, (i + 1) / n), o.far ? 5 : 6, false, i === n - 1);
      }
      const f = { top: H('#A8C060'), side: H('#76983F'), bottom: H('#4A6A3A') };
      const ramp = t => (t < 0.5 ? P.mix(f.bottom, f.side, t / 0.5) : P.mix(f.side, f.top, (t - 0.5) / 0.5));
      M.windGradient = true; M.windY0 = hgt - 0.5; M.windY1 = hgt + 2.0; M.wind = 1.6;
      const nF = o.far ? 7 : 12;
      for (let i = 0; i < nF; i++) {
        const az = (i * 360 / nF + hashN(i, 1, sd) * 14) * D2R;
        palmFrond(M, V.add(top, [0, 0.1, 0]), az, 3.6 + 0.4 * (i % 3) / 2, 1.0, 2.6, 0.38, ramp, 0.3);
      }
      M.wind = 0;
      if (!o.far) {
        const nut = H('#6E5236');
        for (let i = 0; i < 4; i++) { const az = (i * 90 + 20) * D2R; M.color = i % 2 ? nut : H('#7E8A3E'); M.sphere(V.add(top, [Math.cos(az) * 0.3, -0.35, Math.sin(az) * 0.3]), 0.17, 6, 4, false); }
      }
      M.color = barkDark; M.sphere(V.add(top, [0, 0.05, 0]), [0.3, 0.26, 0.3], 6, 3, false);
      return M;
    },
  });

  // 橄榄 (olive): a gnarled, twisting double trunk and an open crown of small silvery-green masses (silver undersides).
  recipe('tree_olive', {
    group: 'tree', label: '橄榄', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.25, 0.8);
      M.windGradient = true; M.windY0 = 1.6; M.windY1 = 5.2; M.wind = 1.5;
      const bark = H('#8A7C6A');
      M.color = bark;
      rib(M, [[0, 0, 0], [0.3, 0.7, 0.1], [0.05, 1.5, -0.1], [0.45, 2.3, 0.05], [0.9, 3.0, 0.2]], 0.34, 0.14, o.far ? 5 : 6);
      rib(M, [[0.05, 0, 0.05], [-0.3, 0.8, 0], [-0.1, 1.6, 0.15], [-0.6, 2.4, 0], [-1.0, 3.1, -0.1]], 0.3, 0.12, o.far ? 5 : 6);
      if (!o.far) { roots(M, 5, 0.6, 0.22, 0.6, b * 21, bark); M.segment([0.45, 2.3, 0.05], [0.2, 3.4, -0.5], 0.1, 0.05, 4); M.segment([-0.6, 2.4, 0], [-0.2, 3.3, 0.7], 0.1, 0.05, 4); }
      const tune = { topVal: 1.02, warmMix: 0.05, coolMix: 0.35, cool: '#9AA898' };
      const f = LP.foliage(b, H('#7E9468'), 6, tune);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 2.6, 5.2, tune);
      const blobs = [[[1.0, 3.6, 0.2], [1.2, 0.8, 1.0]], [[-1.1, 3.7, -0.1], [1.2, 0.8, 1.0]], [[0.1, 4.3, 0.1], [1.3, 0.85, 1.1]], [[0.2, 3.5, -0.9], [1.0, 0.7, 0.8]], [[-0.2, 3.4, 1.0], [1.0, 0.7, 0.8]]];
      const lumps = [[[2.0, 3.4, 0.3], [0.6, 0.5, 0.55]], [[-2.1, 3.5, 0.0], [0.6, 0.5, 0.55]], [[0.9, 4.7, -0.3], [0.65, 0.5, 0.6]], [[-0.9, 4.7, 0.4], [0.65, 0.5, 0.6]], [[0.1, 3.0, 1.6], [0.55, 0.45, 0.5]]];
      crown(M, o, blobs, lumps, ramp, 17, 0.16, 0.2);
      return M;
    },
  });
  // 金合欢 (acacia): a short trunk forking into long limbs that hold up a wide, flat parasol of foliage — the savanna.
  recipe('tree_acacia', {
    group: 'tree', label: '金合欢', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.25, 1.0);
      M.windGradient = true; M.windY0 = 3.5; M.windY1 = 6.6; M.wind = 1.2;
      const bark = H('#7E6A58');
      M.push().rotate(0, b * 45, (b - 1.5) * 3);
      trunk(M, [[0.36, 0], [0.27, 0.3], [0.24, 1.4], [0.2, 2.4]], o.far ? 5 : 6, bark, 0.85);
      if (!o.far) roots(M, 4, 0.5, 0.18, 0.6, b * 11, bark);
      M.color = bark;
      const fork = [0, 2.2, 0];
      const limbs = [[-2.6, 5.3, 0.4], [2.7, 5.4, -0.2], [0.4, 5.7, 1.4], [-0.5, 5.6, -1.3]];
      for (const l of o.far ? limbs.slice(0, 2) : limbs) {
        const mid = [l[0] * 0.45, 3.6, l[2] * 0.45];
        M.segment(fork, mid, 0.17, 0.12, o.far ? 4 : 5); M.segment(mid, l, 0.12, 0.06, o.far ? 4 : 5);
      }
      M.pop();
      const tune = { topVal: 1.02, warmMix: 0.18, coolMix: 0.45 };
      const f = LP.foliage(b, H('#7A9040'), 8, tune);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 5.2, 6.6, tune);
      const blobs = [[[0, 6.0, 0.1], [2.6, 0.55, 2.1]], [[-2.4, 5.85, 0.3], [1.5, 0.45, 1.3]], [[2.5, 5.9, -0.1], [1.5, 0.45, 1.3]], [[0.3, 5.95, 1.5], [1.4, 0.42, 1.0]], [[-0.3, 5.9, -1.4], [1.4, 0.42, 1.0]]];
      const lumps = [[[-1.2, 6.35, 0.4], [1.0, 0.35, 0.8]], [[1.3, 6.35, -0.2], [1.0, 0.35, 0.8]], [[-3.6, 5.75, 0.3], [0.7, 0.32, 0.6]], [[3.7, 5.8, -0.1], [0.7, 0.32, 0.6]]];
      crown(M, o, blobs, lumps, ramp, 19, 0.1, 0.15);
      return M;
    },
  });
  // 猴面包树 (baobab): a vast bottle trunk in warm grey-pink, short thick root-like branches and a sparse crown.
  recipe('tree_baobab', {
    group: 'tree', label: '猴面包树', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 1.5);
      M.windGradient = true; M.windY0 = 5.5; M.windY1 = 8.5; M.wind = 0.8;
      const bark = H('#A89282'), barkDark = H('#8C7668');
      trunk(M, [[1.15, 0], [1.3, 0.5], [1.32, 1.6], [1.15, 3.2], [0.88, 4.6], [0.6, 5.6], [0.46, 6.0]], o.far ? 6 : 9, bark, 0.92, [barkDark, bark, P.shade(bark, 1.04), bark, P.shade(bark, 1.06), bark, bark]);
      M.color = bark;
      const tips = [];
      for (let i = 0; i < (o.far ? 4 : 7); i++) {
        const a = (i * 360 / 7 + b * 20) * D2R, lenK = 1 + 0.25 * hashN(i, 1, 503 + b);
        const tip = [Math.cos(a) * 1.9 * lenK, 7.0 + 0.6 * hashN(i, 2, 503 + b), Math.sin(a) * 1.5 * lenK];
        M.segment([Math.cos(a) * 0.25, 5.7, Math.sin(a) * 0.25], tip, 0.22, 0.08, 5);
        if (!o.far) M.segment(tip, V.add(tip, [Math.cos(a + 0.6) * 0.6, 0.5, Math.sin(a + 0.6) * 0.5]), 0.07, 0.03, 4);
        tips.push(tip);
      }
      const f = LP.foliage(b, H('#6E8E3E'), 8);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 6.4, 8.4);
      tips.forEach((t, i) => M.softLump(V.add(t, [0, 0.3, 0]), [1.15, 0.48, 0.95], o.far ? 0 : 1, i * 23 + b, ramp, 0.14));
      if (!o.far) M.softLump([0, 7.6, 0], [1.3, 0.55, 1.1], 1, 77 + b, ramp, 0.12);
      return M;
    },
  });
  // 红树 (mangrove, Lanternvale BuildMangrove): arching stilt roots, a low broad canopy, moss beards, a mossy hummock.
  recipe('tree_mangrove', {
    group: 'tree', label: '红树', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.35, 0.8), sd = 607 + b;
      const bark = H('#6E5E4E'), wet = H('#5A4C40');
      for (let i = 0; i < (o.far ? 5 : 8); i++) {
        const a = (i * 45 + b * 17 + hashN(i, 1, sd) * 12) * D2R, dir = [Math.cos(a), 0, Math.sin(a) * 0.72];
        const reach = 0.95 + 0.25 * (hashN(i, 2, sd) * 0.5 + 0.5);
        M.color = P.mix(bark, wet, 0.3);
        rib(M, [V.madd([0, 1.45, 0], dir, 0.12), V.madd([0, 1.42, 0], dir, reach * 0.45), V.madd([0, 0.95, 0], dir, reach * 0.82), V.madd([0, 0.35, 0], dir, reach), V.madd([0, -0.08, 0], dir, reach * 1.04)], 0.12, 0.06, 4);
      }
      trunk(M, [[0.34, 1.2], [0.3, 1.7], [0.26, 2.8], [0.2, 3.6]], 6, bark, 0.85);
      M.color = bark;
      const limbs = [[-1.7, 4.4, 0.2], [1.8, 4.6, 0.1], [0.2, 4.9, 0.9], [-0.3, 4.6, -0.8]];
      for (const l of limbs) M.segment([0, 3.3, 0], l, 0.15, 0.06, 5);
      if (!o.far) { M.color = P.shade(bark, 0.9); for (let i = 0; i < 3; i++) { const t = V.lerp([0, 3.3, 0], limbs[i], 0.75); M.segment(t, [t[0] * 1.1, -0.05, t[2] * 0.6], 0.04, 0.03, 4); } }
      M.windGradient = true; M.windY0 = 3.5; M.windY1 = 6.4; M.wind = 1.3;
      const f = LP.foliage(b, H('#4F7E4C'), 6);
      const ramp = LP.canopyRamp(f.top, f.side, f.bottom, 3.7, 6.4);
      const blobs = [[[0, 5.0, 0.2], [2.0, 1.1, 1.7]], [[-1.75, 4.6, 0.15], [1.25, 0.85, 1.2]], [[1.85, 4.75, 0.1], [1.3, 0.9, 1.2]], [[0.35, 5.7, 0.4], [1.35, 0.8, 1.2]], [[-0.4, 4.4, -1.0], [1.1, 0.7, 0.9]], [[0.6, 4.5, 1.35], [1.2, 0.75, 1.0]]];
      const lumps = [[[-2.7, 4.4, 0.4], [0.7, 0.55, 0.65]], [[2.8, 4.55, 0.3], [0.7, 0.55, 0.65]]];
      crown(M, o, blobs, lumps.slice(0, 1), ramp, 23, 0.14, 0);
      M.wind = 0.9;
      if (!o.far) {
        const beard = H('#A3AE8E');
        for (let i = 0; i < 5; i++) { const a = (i * 72 + b * 20) * D2R, c = [Math.cos(a) * 1.5, 3.75, Math.sin(a) * 1.1]; M.softLump(c, [0.14, 0.5, 0.12], 0, i * 33, y => P.mix(P.shade(beard, 0.8), beard, clamp01((y - 3.2) / 0.9)), 0.2); }
      }
      M.wind = 0; M.windGradient = false;
      M.facetBlob([0, 0.02, 0], [0.75, 0.2, 0.55], o.far ? 0 : 1, 0.2, sd, 0.5, LP.byNormal(H('#6E8A4A'), H('#7A6A52'), H('#5E4E40')));
      return M;
    },
  });
  // 秋树: an autumn crown (Lanternvale's golden tree): maple — rust, scarlet and amber; ginkgo — butter gold; a few leaves
  // drifting below, fallen leaves on the ground (plain).
  const AUTUMN = {
    maple: { side: H6('#D8682E', '#C8502E', '#E0843A', '#BE4A32'), top: H6('#F4A452', '#EE8A48', '#F6B65C', '#E6764A'), bottom: H6('#8A3A28', '#7E3026', '#9A4A2C', '#74302A') },
    ginkgo: { side: H6('#E3A23A', '#E6B446', '#DCA034', '#E8BA4C'), top: H6('#F8D774', '#FAE38A', '#F6CF68', '#FCE896'), bottom: H6('#A66A30', '#AE7636', '#9A6230', '#B4803A') },
  };
  recipe('tree_autumn', {
    group: 'tree', label: '秋树', lod: true, keys: ['variant'], defaults: { variant: 'maple' },
    build(o) {
      const b = o.bucket, M = builder(o, o.variant, 0.06, 0.3, 1.2), L = builder(o, 'leaves', 0.1, 0, 1);
      M.windGradient = true; M.windY0 = 1.8; M.windY1 = 8.5; M.wind = 1.7;
      const bark = H('#857565'), pal = AUTUMN[o.variant] || AUTUMN.maple;
      trunk(M, [[0.46, 0], [0.34, 0.35], [0.3, 1.4], [0.27, 2.6], [0.21, 3.5]], o.far ? 5 : 7, bark, 0.8);
      if (!o.far) roots(M, 5, 0.6, 0.21, 0.6, b * 29, bark);
      M.color = bark;
      const tips = [[-1.2, 5.0, 0.15], [1.3, 5.2, 0.25], [0.15, 5.9, -0.35], [-0.25, 5.5, 0.75]];
      for (const t of o.far ? tips.slice(0, 2) : tips) M.segment([0, 3.2, 0], t, 0.19, 0.07, 5);
      const ramp = autumnRamp(pal.top[b], pal.side[b], pal.bottom[b], 3.6, 8.8);
      const blobs = [[[0, 6.0, 0.2], [2.1, 2.2, 1.9]], [[-1.45, 5.1, 0.05], [1.35, 1.35, 1.3]], [[1.5, 5.25, 0.3], [1.35, 1.4, 1.3]], [[0.3, 7.5, 0.1], [1.45, 1.3, 1.35]], [[-0.7, 4.7, -0.95], [1.2, 1.0, 1.0]], [[0.6, 4.9, 1.3], [1.3, 1.15, 1.1]]];
      const lumps = [[[-2.45, 5.6, 0.3], [0.75, 0.7, 0.7]], [[2.55, 5.8, 0.4], [0.75, 0.72, 0.7]], [[-1.35, 7.55, 0.3], [0.75, 0.7, 0.7]], [[1.45, 7.35, 0.4], [0.8, 0.72, 0.75]], [[0.1, 8.55, 0.3], [0.8, 0.6, 0.75]], [[1.0, 4.3, -0.85], [0.75, 0.58, 0.65]], [[-1.85, 4.35, -0.5], [0.7, 0.58, 0.65]]];
      crown(M, o, blobs, lumps, ramp, 29, 0.13, 0.25);
      if (!o.far) {
        // drifting leaves (ink) and fallen leaves (plain, on the ground)
        M.wind = 2.2;
        for (let i = 0; i < 5; i++) { const a = i * 1.1 + b, at = [Math.cos(a) * (1.6 + (i % 2) * 0.6), 2.0 + (i % 3) * 0.7, Math.sin(a) * 1.2 + 0.4]; leaf(M, at, [Math.sin(a), 0.3, Math.cos(a)], [0.2, 0.6, 1], 0.16, i % 2 ? pal.side[b] : pal.top[b]); }
        M.wind = 0;
        const cols = [pal.side[b], pal.top[b], pal.bottom[b], P.mix(pal.side[b], pal.top[b], 0.5)];
        for (let i = 0; i < 46; i++) {
          const a = L.random01() * Math.PI * 2, d = Math.sqrt(L.random01()) * 2.6, x = Math.cos(a) * d, z = Math.sin(a) * d * 0.8 + 0.3;
          if (Math.hypot(x, z - 0.3) < 0.55) continue;
          L.color = P.shade(cols[i % 4], 0.9 + L.random01() * 0.16);
          L.push().translate(x, 0.02 + (i % 3) * 0.002, z).rotate(0, L.random01() * 360, 0);
          L.disc([0, 0, 0], 0.07 + L.random01() * 0.04, 4);
          L.pop();
        }
      }
      M.windGradient = false;
      return { ink: M, plain: o.far ? null : L };
    },
  });
  // 花树: blossom trees — plum (梅: a dark, angular, gnarled trunk; sparse pale-pink and white blossom clouds) and peach (桃:
  // a rounder crown of pink blossom); fallen petals on the ground (plain).
  const BLOSSOM = {
    plum: { side: H6('#F2C8D2', '#F6D8DE', '#EEBCCA', '#FAE4E8'), top: H6('#FFF0F2', '#FFF6F6', '#FCE6EC', '#FFFFFF'), bottom: H6('#C890A6', '#D0A0B2', '#BC849C', '#D6AABC'), bark: H('#4E3A34') },
    peach: { side: H6('#E8A2B6', '#E496AC', '#ECAEBE', '#E08EA6'), top: H6('#F8CCD6', '#F6C2CE', '#FAD6DE', '#F4BAC8'), bottom: H6('#A86A80', '#9E5E76', '#B2788C', '#965870'), bark: H('#6A4C40') },
  };
  recipe('tree_blossom', {
    group: 'tree', label: '花树', lod: true, keys: ['variant'], defaults: { variant: 'plum' },
    build(o) {
      const b = o.bucket, plum = o.variant !== 'peach', pal = plum ? BLOSSOM.plum : BLOSSOM.peach;
      const M = builder(o, o.variant, 0.06, 0.25, 1.0), L = builder(o, 'petals', 0.1, 0, 1);
      M.windGradient = true; M.windY0 = 1.6; M.windY1 = 5.5; M.wind = 1.3;
      const bark = pal.bark;
      M.color = bark;
      const s = b % 2 ? -1 : 1;
      if (plum) {
        // angular zig-zag limbs, the scroll painter's plum
        rib(M, [[0, 0, 0], [0.2 * s, 0.9, 0.05], [-0.15 * s, 1.7, -0.05], [0.25 * s, 2.5, 0.1]], 0.26, 0.15, o.far ? 5 : 6);
        const limbs = [[[0.25 * s, 2.5, 0.1], [1.6 * s, 3.3, 0.2], [2.3 * s, 4.3, 0]], [[-0.15 * s, 1.7, -0.05], [-1.3 * s, 2.6, 0.2], [-1.9 * s, 3.6, -0.2]], [[0.25 * s, 2.5, 0.1], [-0.3 * s, 3.6, -0.3], [0.2 * s, 4.6, 0.1]], [[0.25 * s, 2.5, 0.1], [0.6 * s, 3.4, 0.9]]];
        for (const l of o.far ? limbs.slice(0, 2) : limbs) rib(M, l, 0.12, 0.04, 4);
      } else {
        trunk(M, [[0.3, 0], [0.24, 0.3], [0.2, 1.2], [0.17, 1.8]], o.far ? 5 : 6, bark, 0.85);
        for (const t of [[-1.2, 3.2, 0.2], [1.2, 3.3, 0], [0.1, 3.8, -0.4], [0.0, 3.5, 0.8]]) M.segment([0, 1.7, 0], t, 0.13, 0.05, 5);
      }
      if (!o.far) roots(M, 4, 0.45, 0.15, 0.6, b * 19, bark);
      const tune = { coolMix: 0.25, cool: plum ? '#9A7A90' : '#8E5A78', warmMix: 0.05 };
      const ramp = LP.canopyRamp(pal.top[b], pal.side[b], pal.bottom[b], plum ? 2.6 : 2.4, plum ? 5.0 : 5.4, tune);
      const blobs = plum
        ? [[[2.1 * s, 4.15, 0.1], [0.66, 0.46, 0.6]], [[-1.75 * s, 3.5, -0.1], [0.62, 0.44, 0.56]], [[0.15 * s, 4.55, 0.1], [0.62, 0.46, 0.58]], [[1.4 * s, 3.3, 0.3], [0.5, 0.38, 0.46]], [[-1.1 * s, 2.7, 0.3], [0.46, 0.36, 0.42]], [[0.6 * s, 3.4, 0.95], [0.48, 0.36, 0.44]], [[-0.25 * s, 3.65, -0.3], [0.44, 0.34, 0.4]], [[1.0 * s, 4.0, -0.4], [0.42, 0.32, 0.4]]]
        : [[[0, 3.8, 0.1], [1.35, 1.0, 1.2]], [[-1.25, 3.3, 0.2], [0.85, 0.7, 0.8]], [[1.25, 3.35, 0.0], [0.85, 0.72, 0.8]], [[0.2, 4.55, 0.0], [0.95, 0.7, 0.85]], [[0.1, 3.2, 1.0], [0.8, 0.62, 0.65]], [[-0.4, 3.4, -0.9], [0.75, 0.6, 0.6]]];
      const lumps = plum
        ? [[[2.6 * s, 4.6, 0.3], [0.4, 0.32, 0.36]], [[-2.2 * s, 3.9, 0.2], [0.4, 0.3, 0.36]], [[-0.3 * s, 3.8, -0.4], [0.45, 0.35, 0.4]], [[0.5 * s, 5.0, 0.2], [0.38, 0.3, 0.34]]]
        : [[[-2.0, 3.5, 0.3], [0.5, 0.42, 0.46]], [[2.0, 3.55, 0.1], [0.5, 0.42, 0.46]], [[-0.8, 4.5, 0.5], [0.48, 0.4, 0.44]], [[1.0, 4.45, 0.4], [0.48, 0.4, 0.44]], [[0, 5.15, 0.1], [0.48, 0.36, 0.44]], [[1.5, 2.9, 0.7], [0.42, 0.34, 0.4]], [[-1.5, 2.85, -0.4], [0.42, 0.34, 0.4]]];
      crown(M, o, blobs, lumps, ramp, 31, 0.16, 0.15);
      if (!o.far) {
        const cols = [pal.side[b], pal.top[b], P.mix(pal.side[b], pal.bottom[b], 0.4)];
        for (let i = 0; i < 40; i++) {
          const a = L.random01() * Math.PI * 2, d = Math.sqrt(L.random01()) * (plum ? 2.2 : 2.4), x = Math.cos(a) * d, z = Math.sin(a) * d * 0.8 + 0.2;
          if (Math.hypot(x, z - 0.2) < 0.4) continue;
          L.color = P.shade(cols[i % 3], 0.94 + L.random01() * 0.1);
          L.push().translate(x, 0.02, z).rotate(0, L.random01() * 360, 0);
          L.disc([0, 0, 0], 0.045 + L.random01() * 0.03, 5);
          L.pop();
        }
      }
      M.windGradient = false;
      return { ink: M, plain: o.far ? null : L };
    },
  });
  // 雪松树 (snow-laden pine, Lanternvale BuildPineSnow): pine tiers capped with drooping snow tongues, a drift at the foot.
  recipe('pine_snow', {
    group: 'tree', label: '雪中松', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.3, 1.0);
      M.windGradient = true; M.windY0 = 1.5; M.windY1 = 11; M.wind = 1.0;
      const bark = H('#5E4636'), k = 0.9 + b * 0.06;
      const snow = H('#F2F5FA'), snowShade = H('#B8C6DA');
      trunk(M, [[0.32, 0], [0.23, 0.4], [0.18, 4 * k], [0.1, 9 * k], [0.04, 10.6 * k]], o.far ? 5 : 6, bark, 0.85);
      if (!o.far) roots(M, 4, 0.48, 0.16, 0.55, 40 + b * 17, bark);
      const f = LP.foliage(b, H('#2F5A4E'), 5);
      const ramp = LP.canopyRamp(P.mix(f.top, f.side, 0.4), f.side, f.bottom, 2.2 * k, 11.2 * k);
      pineTiers(M, o, f, ramp, 6, 2.55, 1.36, 2.3, 0.33, 2.45, 0.15, 9, 3, k, (i, r, h) => {
        if (o.far && i % 2) return;
        const Rr = y => (y <= h * 0.36 ? lerp(r, r * 0.55, clamp01((y + 0.12) / (h * 0.36 + 0.12))) : lerp(r * 0.55, 0, clamp01((y - h * 0.36) / (h * 0.64))));
        const ye = h * (0.2 + 0.035 * i), CV = o.far ? 8 : 14, ry = [ye, ye + 0.16, h * 0.62, h * 0.9], grow = [0.07, 0.08, 0.06, 0.04];
        const rings = ry.map((yy, q) => {
          const ring = [];
          for (let v = 0; v < CV; v++) {
            const a = (v * 360 / CV + i * 19 + b * 11) * D2R;
            let y = yy, rr = Rr(y) + grow[q];
            if (q === 0) { const tongue = Math.abs(hashN(v, i, b + 3)); if (v % 2 === 1) { y -= 0.1 + 0.12 * tongue; rr = Rr(y) + 0.09; } else y += 0.03 * tongue; }
            ring.push([Math.cos(a) * rr, y, Math.sin(a) * rr]);
          }
          return ring;
        });
        M.loft(rings, true, true, nn => (nn[1] > 0.55 ? snow : P.mix(snowShade, snow, clamp01(nn[1] / 0.55))));
      });
      M.color = snow;
      M.cone([0, (2.55 + 6 * 1.36) * k - 0.45, 0], 0.36, 1.3 * k, 6);
      M.windGradient = false; M.wind = 0;
      M.softLump([0.1, -0.06, 0.05], [0.95, 0.22, 0.7], o.far ? 0 : 1, b * 40, y => P.mix(snowShade, snow, clamp01((y + 0.05) / 0.23)), 0.12);
      return M;
    },
  });
  // 枯树 (a bare, dead tree): gnarled limbs forking into twigs — battlefields, winter, drought.
  recipe('tree_dead', {
    group: 'tree', label: '枯树', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.3, 1.0);
      const bark = H('#7A6858');
      M.push().rotate(0, b * 40, (b - 1.5) * 3);
      trunk(M, [[0.4, 0], [0.28, 0.4], [0.22, 2.2], [0.17, 3.3]], 6, bark, 0.75);
      if (!o.far) roots(M, 5, 0.52, 0.2, 0.55, 10, bark);
      M.color = bark;
      const top = [0, 3.2, 0];
      const dirs = [[-0.8, 0.9, 0.2], [0.9, 0.8, -0.1], [0.1, 1, 0.5]];
      dirs.forEach((d0, i) => {
        const d = V.norm(d0), len = 1.3 + (i === 2 ? 0.4 : 0), mid = V.add(V.madd(top, d, len), [0, 0, (M.random01() - 0.5) * 0.3]);
        M.segment(V.madd(top, d, -0.1), mid, 0.15, 0.08, 5);
        if (o.far) return;
        for (const k of [-1, 1]) {
          const tw = V.norm(V.add(d, [k * 0.6, 0.35, (M.random01() - 0.5) * 0.5])), end = V.madd(mid, tw, 0.8 + M.random01() * 0.5);
          M.segment(mid, end, 0.07, 0.02, 4);
          M.segment(end, V.madd(end, V.norm(V.add(tw, [-k * 0.5, 0.5, 0])), 0.4), 0.025, 0.008, 3, false, false);
        }
      });
      M.segment([0, 1.9, 0], [0.95, 2.5, -0.2], 0.1, 0.03, 4);
      M.pop();
      return M;
    },
  });


  // ============================================================= plants ==
  // Bushes: 'pilot' (default — the approved diorama bush: three soft lumps, a few pink or white flower specks on odd seeds,
  // plain), 'round' (Lanternvale BuildBush: seven lumps), 'flowering' (seven lumps dotted with five-petal blossoms).
  const BUSH3 = [[[0, 0.55, 0], [0.75, 0.6, 0.65], 0], [[0.55, 0.42, 0.15], [0.48, 0.4, 0.45], 40], [[-0.5, 0.4, 0.1], [0.45, 0.38, 0.42], 80]];
  const BUSH7 = [[[0, 0.66, 0.1], [0.68, 0.6, 0.5]], [[-0.4, 0.47, 0.15], [0.38, 0.4, 0.36]], [[0.42, 0.5, 0.17], [0.38, 0.42, 0.36]], [[0.1, 1.0, 0.12], [0.42, 0.38, 0.38]], [[-0.62, 0.33, 0.05], [0.24, 0.24, 0.24]], [[0.66, 0.36, 0.07], [0.24, 0.25, 0.24]], [[-0.3, 0.95, 0.2], [0.28, 0.26, 0.28]]];
  recipe('bush', {
    group: 'plant', label: '灌木', keys: ['variant'], defaults: { variant: 'pilot' }, lod: true,
    build(o) {
      const b = o.bucket, pilot = o.variant === 'pilot';
      const M = builder(o, o.variant, 0.06, 0.3, 0.5, pilot ? 401 + b : null), F = builder(o, 'flowers', 0.04, 0, 1, pilot ? 431 + b : null);
      M.windGradient = true; M.windY0 = 0.2; M.windY1 = 1.3; M.wind = pilot ? 0.7 : 0.8;
      F.windGradient = true; F.windY0 = 0.2; F.windY1 = 1.3; F.wind = pilot ? 0.7 : 0.8;
      if (o.variant === 'pilot') {
        const f = LP.foliage(b, H('#6A9A4A'), 8), ramp = LP.canopyRamp(f.top, f.side, f.bottom, 0.0, 1.2);
        BUSH3.forEach(([c, r, yaw], i) => M.softLump(c, r, o.far ? 0 : 1, yaw, ramp, i ? 0.14 : 0.12));
        if (b & 1 && !o.far) {
          const col = (b >> 1) & 1 ? H('#F5F0E6') : H('#F2B8C6'), n = 4 + (b % 3);
          for (let i = 0; i < n; i++) {
            const [c, r] = BUSH3[i % 3], th = (i * 137.5 + b * 40) * D2R, ph = (28 + ((i * 23) % 40)) * D2R;
            const u = [Math.sin(ph) * Math.cos(th), Math.cos(ph), Math.sin(ph) * Math.sin(th)];
            const p = V.add(c, [u[0] * r[0] * 1.06, u[1] * r[1] * 1.06, u[2] * r[2] * 1.06]), nrm = V.norm([u[0] / r[0], u[1] / r[1], u[2] / r[2]]);
            F.push().translate(p).rotateQ(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(...nrm)));
            for (let j = 0; j < 3; j++) { const a = (j * 120 + i * 30) * D2R; F.color = P.shade(col, 0.95 + 0.1 * F.random01()); F.disc([Math.cos(a) * 0.055, 0.01 * j, Math.sin(a) * 0.055], 0.045, 4); }
            F.pop();
          }
        }
        return { ink: M, plain: F };
      }
      const flowering = o.variant === 'flowering';
      const f = LP.foliage(b, flowering ? H('#5F8F4A') : H('#729E4E'), 8), ramp = LP.canopyRamp(f.top, f.side, f.bottom, 0.15, 1.35);
      BUSH7.forEach(([c, r], i) => { if (!o.far || i < 4) M.softLump(c, r, i < 4 && !o.far ? 1 : 0, i * 41 + b * 23, ramp, 0.12); });
      if (flowering && !o.far) {
        const petals = H6('#F6A9C0', '#FFD3E0', '#F07FA0');
        for (let i = 0; i < 14; i++) {
          const [c, r] = BUSH7[i % 4], a = F.random01() * Math.PI * 2, e = lerp(0.2, 1.15, F.random01());
          const dir = [Math.cos(a) * Math.cos(e), Math.sin(e), Math.sin(a) * Math.cos(e)];
          if (dir[2] < -0.3) dir[2] = -dir[2];
          const nrm = V.norm([dir[0] / r[0], dir[1] / r[1], dir[2] / r[2]]);
          bloom(F, V.add(c, [dir[0] * r[0] * 1.06, dir[1] * r[1] * 1.06, dir[2] * r[2] * 1.06]), 0.08, nrm, petals[i % 3], H('#F6D35A'), i * 29);
        }
      }
      return { ink: M, plain: flowering ? F : null };
    },
  });
  // A flower patch (no ink): stems, a few leaves and five-petal blooms in a seed palette (white / pink / gold / violet / mixed).
  const FLOWER_PALS = [H6('#F5F0E6', '#F2B8C6', '#F4D35E'), H6('#F2A0B8', '#E07A9A', '#FFD3E0'), H6('#F4D35E', '#F0A040', '#FFF0B0'), H6('#C9A8FF', '#8E7AE0', '#F5F0E6')];
  recipe('flowers', {
    group: 'plant', label: '花丛',
    build(o) {
      const F = builder(o, '', 0.06, 0, 1), pal = FLOWER_PALS[o.bucket];
      F.windGradient = true; F.windY0 = 0; F.windY1 = 0.5; F.wind = 0.6;
      const n = 16;
      for (let i = 0; i < n; i++) {
        const a = i * 2.399 + o.bucket, d = 0.12 + 0.55 * Math.sqrt((i + 0.5) / n), x = Math.cos(a) * d, z = Math.sin(a) * d * 0.8;
        const fy = 0.24 + 0.2 * (hashN(i, 1, o.bucket) * 0.5 + 0.5);
        F.color = H('#5E8E42'); F.segment([x, 0, z], [x + 0.02, fy, z], 0.012, 0.008, 3, false, false);
        if (i % 2 === 0) { F.color = H('#6E9A4E'); F.blade([x, 0.04, z], [x + Math.cos(a) * 0.14, 0.16, z + Math.sin(a) * 0.1], 0.07); }
        bloom(F, [x + 0.02, fy, z], 0.075 + 0.02 * (i % 2), [0.15 * Math.cos(a), 1, 0.15 * Math.sin(a) + 0.25], pal[i % 3], H('#F6D04D'), i * 37);
      }
      return { plain: F };
    },
  });
  // Grass: one tuft (≤ 60 tris; 'clump' = a few tufts) of tapering blades, no ink, swaying.
  recipe('grass', {
    group: 'plant', label: '草丛', keys: ['variant'], defaults: { variant: 'tuft' },
    build(o) {
      const F = builder(o, o.variant, 0.08, 0.3, 0.4);
      F.windGradient = true; F.windY0 = 0; F.windY1 = 0.5; F.wind = 0.6;
      const greens = H6('#8DB45A', '#A6C46A', '#7AA44E', '#B4C878');
      const tufts = o.variant === 'clump' ? [[0, 0, 0.55], [0.32, 0.18, 0.42], [-0.3, 0.12, 0.46], [0.05, -0.28, 0.38]] : [[0, 0, 0.5]];
      tufts.forEach(([x, z, h], i) => F.tuft([x, 0, z], h + 0.1 * (o.bucket % 2), o.variant === 'clump' ? 5 : 7, greens[(i + o.bucket) % 4]));
      return { plain: F };
    },
  });
  // Reeds (Lanternvale BuildReeds): a dense clump of two-part blades leaning out, feathery plumes on the tallest; no ink.
  recipe('reeds', {
    group: 'plant', label: '芦苇',
    build(o) {
      const b = o.bucket, F = builder(o, '', 0.07, 0.35, 0.6), sd = 711 + b;
      F.windGradient = true; F.windY0 = 0; F.windY1 = 1.9; F.wind = 1.8;
      const reed = H('#8E9A5A'), tipC = H('#C8B47A'), n = 18 + b * 2;
      for (let i = 0; i < n; i++) {
        const a = (i * 137.5 + b * 31) * D2R, d = 0.08 + 0.3 * Math.sqrt((i + 0.5) / n);
        const root = [Math.cos(a) * d, 0, Math.sin(a) * d * 0.8], h = 1.1 + 0.75 * (hashN(i, 1, sd) * 0.5 + 0.5) - d * 0.6;
        const lean = V.add(V.mul([Math.cos(a), 0, Math.sin(a) * 0.8], 0.18 + 0.25 * d), [hashN(i, 2, sd) * 0.08, 0, 0]);
        const tip = V.add(V.add(root, [0, h, 0]), V.mul(lean, h)), mid = V.sub(V.lerp(root, tip, 0.5), V.mul(lean, 0.12 * h));
        const side = [-Math.sin(a), 0, Math.cos(a)];
        F.color = P.mix(reed, H('#5E7448'), hashN(i, 3, sd) * 0.5 + 0.5); F.blade(root, mid, 0.07, side);
        F.color = P.mix(reed, tipC, 0.4 + 0.4 * (hashN(i, 4, sd) * 0.5 + 0.5)); F.blade(mid, tip, 0.05, side);
        if (i % 4 === 0) { F.color = H('#B89A72'); const d2 = V.norm(V.sub(tip, mid)); F.blade(V.madd(tip, d2, -0.05), V.add(V.madd(tip, d2, 0.2), [0.04, -0.05, 0]), 0.07, side); }
      }
      return { plain: F };
    },
  });
  // Cattails (Lanternvale): arching leaves and stems (no ink), inked brown heads.
  recipe('cattails', {
    group: 'plant', label: '香蒲',
    build(o) {
      const b = o.bucket, F = builder(o, 'leaves', 0.07, 0.35, 0.5), M = builder(o, 'heads', 0.06, 0, 1), sd = 733 + b;
      F.windGradient = true; F.windY0 = 0; F.windY1 = 1.7; F.wind = 1.6;
      M.windGradient = true; M.windY0 = 0; M.windY1 = 1.7; M.wind = 1.6;
      for (let i = 0; i < 14; i++) {
        const a = (i * 137.5 + b * 23) * D2R, dir = [Math.cos(a), 0, Math.sin(a) * 0.8], root = V.mul(dir, 0.12);
        const h = 0.9 + 0.5 * (hashN(i, 1, sd) * 0.5 + 0.5), mid = V.add(V.madd(root, dir, 0.18), [0, h * 0.6, 0]);
        const tip = V.add(V.madd(root, dir, 0.45 + 0.2 * (i % 3)), [0, h * (i % 3 === 0 ? 0.75 : 1), 0]), side = [-dir[2], 0, dir[0]];
        F.color = P.mix(H('#6E8A48'), H('#4F6E40'), hashN(i, 2, sd) * 0.5 + 0.5); F.blade(root, mid, 0.09, side);
        F.color = P.mix(H('#86A058'), H('#C8B47A'), 0.3 * (hashN(i, 3, sd) * 0.5 + 0.5)); F.blade(mid, tip, 0.07, side);
      }
      for (let i = 0; i < 7; i++) {
        const a = (i * 97 + b * 41) * D2R, d = 0.06 + 0.22 * ((i * 0.37 + b * 0.11) % 1), root = [Math.cos(a) * d, 0, Math.sin(a) * d * 0.8];
        const tip = V.add(root, [Math.cos(a) * 0.12, 1.25 + 0.45 * ((i * 0.61 + b * 0.23) % 1), Math.sin(a) * 0.1]), dd = V.norm(V.sub(tip, root));
        F.color = H('#7E9454'); F.blade(root, tip, 0.03, [1, 0, 0]);
        M.color = H('#7A4A2E'); M.segment(V.madd(tip, dd, -0.3), V.madd(tip, dd, -0.06), 0.045, 0.042, 6, false, true);
        M.color = H('#A89A6A'); M.segment(V.madd(tip, dd, -0.06), V.madd(tip, dd, 0.12), 0.01, 0.004, 3, false, true);
      }
      return { ink: M, plain: F };
    },
  });
  // Lotus (荷): pads with a notch, two pink blooms and a bud, floating on water at y = 0 (no ink).
  recipe('lotus', {
    group: 'plant', label: '荷叶',
    build(o) {
      const b = o.bucket, F = builder(o, '', 0.06, 0, 1), sd = 757 + b;
      const greens = H6('#6E9A4E', '#5E8A48', '#86AE5A', '#4F7A44'), n = 8 + (b & 1);
      for (let i = 0; i < n; i++) {
        const a = (i * 137.5 + b * 50) * D2R, d = 0.15 + 0.85 * Math.sqrt((i + 0.5) / n), c = [Math.cos(a) * d, 0.012 + (i % 3) * 0.004, Math.sin(a) * d * 0.75];
        const r = 0.16 + 0.14 * (hashN(i, 1, sd) * 0.5 + 0.5), notch = hashN(i, 2, sd) * 180;
        const pts = ngon(c, r, r, 9, [1, 0, 0], [0, 0, -1], notch + 20, 0, 320); pts.push(c);
        slab(F, pts, [0, 1, 0], 0.02, greens[i % 4]);
      }
      const pink = H('#F2A0B8'), pinkDeep = H('#E07A9A');
      for (let k = 0; k < 2; k++) {
        const a = (k * 160 + b * 40 + 30) * D2R, c = [Math.cos(a) * 0.45, 0.06, Math.sin(a) * 0.3];
        bloom(F, c, 0.16, [0.1, 1, 0.2], pinkDeep, H('#F6D04D'), k * 36);
        bloom(F, V.add(c, [0, 0.05, 0]), 0.11, [-0.1, 1, 0.2], pink, H('#F6D04D'), k * 36 + 30);
      }
      F.color = H('#6E9A4E'); F.segment([-0.35, 0.02, 0.35], [-0.33, 0.2, 0.35], 0.012, 0.012, 3);
      F.color = pinkDeep; gem(F, [-0.33, 0.27, 0.35], [0.045, 0.08, 0.045]);
      return { plain: F };
    },
  });
  // Fern: arching fronds (closed thin strands) radiating from a crown, no ink.
  recipe('fern', {
    group: 'plant', label: '蕨',
    build(o) {
      const b = o.bucket, F = builder(o, '', 0.07, 0.3, 0.4);
      F.windGradient = true; F.windY0 = 0; F.windY1 = 0.8; F.wind = 0.9;
      const ramp = (y, t) => P.mix(H('#4E7A3E'), H('#9CC060'), clamp01(t * 0.8 + y * 0.4));
      for (let i = 0; i < 9; i++) {
        const a = (i * 40 + b * 13) * D2R, dir = [Math.cos(a), 0, Math.sin(a)], tang = [-dir[2], 0, dir[0]], len = 0.75 + 0.2 * (i % 3) / 2;
        const pts = [0, 0.33, 0.66, 1].map(t => [dir[0] * len * t, 0.05 + 0.55 * t - 0.5 * t * t * len, dir[2] * len * t]);
        strand(F, pts, [0.03, 0.12, 0.1, 0.02], [0.012, 0.012, 0.01, 0.006], tang, [0, 1, 0], ramp);
      }
      return { plain: F };
    },
  });

  // ============================================================== rocks ==
  recipe('rock_small', {
    group: 'rock', label: '小石',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.35, 0.4), stone = P.hsv(H('#A8A296'), (b - 1.5) * 2.5, 0.85), sd = 801 + b * 13;
      M.facetBlob([0, 0.32, 0.05], [0.48, 0.42, 0.4], 1, 0.2, sd, 0.35, mossy(stone, PAL.moss, 0.5));
      M.facetBlob([0.52, 0.18, -0.05], [0.3, 0.24, 0.26], 0, 0.2, sd + 3, 0.3, mossy(P.shade(stone, 1.06), PAL.mossLight, 0.55));
      M.facetBlob([-0.48, 0.15, 0.1], [0.26, 0.2, 0.24], 0, 0.2, sd + 9, 0.3, mossy(P.shade(stone, 0.95), PAL.moss, 0.55));
      M.tuft([0.25, 0, 0.3], 0.28, 4, PAL.leaf);
      return M;
    },
  });
  recipe('rock_large', {
    group: 'rock', label: '大石', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.35, 0.6), stone = P.hsv(H('#A8A296'), (b - 1.5) * 2.5, 0.85), sd = 821 + b * 13;
      M.facetBlob([0, 0.92, 0.22], [0.98, 1.05, 0.72], o.far ? 0 : 1, 0.2, sd, 0.3, mossy(stone, PAL.moss, 0.5));
      M.facetBlob([0.55, 0.36, 0.12], [0.4, 0.38, 0.3], o.far ? 0 : 1, 0.2, sd + 5, 0.35, mossy(P.shade(stone, 1.05), PAL.mossLight, 0.55));
      if (!o.far) {
        M.facetBlob([-0.75, 0.25, 0.4], [0.3, 0.25, 0.25], 0, 0.22, sd + 7, 0.3, mossy(P.shade(stone, 0.96), PAL.moss, 0.5));
        for (let i = 0; i < 4; i++) { M.color = M.random01() < 0.5 ? PAL.stoneLight : stone; gem(M, [-0.85 + i * 0.5 + M.random01() * 0.2, 0.04, 0.42 + M.random01() * 0.2], [0.1, 0.07, 0.08]); }
        M.tuft([-0.7, 0, 0.5], 0.35, 5, PAL.leaf);
      }
      return M;
    },
  });
  // A big weathered boulder (2.5 m) in two stacked masses with a split, moss in the hollows.
  recipe('boulder', {
    group: 'rock', label: '巨石', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.35, 0.8), stone = P.hsv(H('#A6A096'), (b - 1.5) * 2.5, 0.85), sd = 841 + b * 7;
      M.facetBlob([0, 1.0, 0], [1.6, 1.15, 1.25], o.far ? 0 : 1, 0.24, sd, 0.4, mossy(stone, PAL.moss, 0.45));
      M.facetBlob([0.6, 2.0, -0.2], [0.95, 0.75, 0.85], o.far ? 0 : 1, 0.24, sd + 3, 0.3, mossy(P.shade(stone, 1.06), PAL.mossLight, 0.5));
      M.facetBlob([-1.3, 0.45, 0.5], [0.6, 0.48, 0.5], 0, 0.22, sd + 5, 0.3, mossy(P.shade(stone, 0.94), PAL.moss, 0.5));
      if (!o.far) { M.tuft([-0.9, 0, 0.9], 0.38, 5, PAL.leaf); M.tuft([1.2, 0, 0.8], 0.3, 4, PAL.leafDark); }
      return M;
    },
  });
  // Cliff piece (3–4 m): stacked angular strata, flat-shaded faces, a mossy top — for ridges, quarries and river banks.
  recipe('cliff', {
    group: 'rock', label: '崖石', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.3, 1.0), sd = 861 + b * 11;
      const stone = P.hsv(H('#A89A88'), (b - 1.5) * 4, 0.95), stoneD = P.shade(stone, 0.82), top = P.mix(stone, PAL.moss, 0.55);
      M.jitterAll = true;
      const layers = [[0, 1.3, 2.6, 1.8], [1.25, 1.1, 2.2, 1.5], [2.3, 0.9, 1.7, 1.15], [3.15, 0.6, 1.1, 0.8]];
      layers.forEach(([y, h, w, d], i) => {
        const ox = hashN(i, 1, sd) * 0.3, oz = hashN(i, 2, sd) * 0.2, k = o.far ? 0 : 1;
        // an irregular prism: 7-gon footprint, top slightly smaller and offset
        const poly = [], polyT = [];
        for (let j = 0; j < 7; j++) {
          const a = (j / 7) * Math.PI * 2 + i * 0.4, rr = 1 + 0.16 * hashN(i, j + 3, sd);
          poly.push([ox + Math.cos(a) * w * 0.5 * rr, oz + Math.sin(a) * d * 0.5 * rr]);
          polyT.push([ox + 0.06 + Math.cos(a) * w * 0.44 * rr, oz + Math.sin(a) * d * 0.44 * rr]);
        }
        for (let j = 0; j < 7; j++) {
          const p = poly[j], q = poly[(j + 1) % 7], pt = polyT[j], qt = polyT[(j + 1) % 7];
          M.quad([p[0], y, p[1]], [q[0], y, q[1]], [qt[0], y + h, qt[1]], [pt[0], y + h, pt[1]], [(p[0] + q[0]) / 2 - ox, 0.15, (p[1] + q[1]) / 2 - oz], P.shade(j % 2 ? stone : stoneD, 0.96 + 0.08 * hashN(i, j, sd)));
        }
        const ct = [ox + 0.06, y + h, oz];
        for (let j = 0; j < 7; j++) M.triangleFacing(ct, [polyT[j][0], y + h, polyT[j][1]], [polyT[(j + 1) % 7][0], y + h, polyT[(j + 1) % 7][1]], [0, 1, 0], i === layers.length - 1 ? top : P.mix(stone, top, 0.35));
        if (k && i < 3) M.softLump([ox + hashN(i, 9, sd) * w * 0.3, y + h + 0.02, oz + d * 0.2], [w * 0.22, 0.12, d * 0.18], 0, i * 13, LP.canopyRamp(PAL.mossLight, PAL.moss, PAL.leafDark, y + h - 0.05, y + h + 0.15), 0.18);
      });
      if (!o.far) { M.facetBlob([1.6, 0.25, 0.7], [0.4, 0.3, 0.35], 0, 0.2, sd + 1, 0.3, mossy(stone, PAL.moss)); M.tuft([-1.2, 0, 0.9], 0.36, 5, PAL.leaf); }
      return M;
    },
  });
  recipe('rock_snow', {
    group: 'rock', label: '雪石',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 0.6), sd = 881 + b * 7, stone = P.hsv(H('#9C9AA8'), (b - 1.5) * 5, 0.9);
      const snowy = (k) => n => (n[1] > 0.45 ? H('#F2F5FA') : n[1] > 0.2 ? P.mix(stone, H('#DDE6F2'), 0.5) : n[1] < -0.3 ? P.shade(stone, 0.78) : stone);
      M.facetBlob([0, 0.7, 0], [1.1, 0.75, 0.85], 1, 0.22, sd, 0.35, snowy());
      M.facetBlob([0.85, 0.35, 0.25], [0.5, 0.4, 0.42], 0, 0.22, sd + 3, 0.3, snowy());
      M.softLump([0, 0.03, 0.1], [1.4, 0.16, 1.0], 0, b * 20, y => P.mix(H('#B8C6DA'), H('#F2F5FA'), clamp01((y + 0.05) / 0.2)), 0.12);
      return M;
    },
  });
  // Scattered stones and pebbles (a path edge, a river bank).
  recipe('stones', {
    group: 'rock', label: '碎石',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.3, 0.2), greys = H6('#9C948A', '#B3AB9E', '#7F786F', '#A8A08E');
      for (let i = 0; i < 9; i++) {
        const a = i * 2.399 + b, d = 0.1 + 0.6 * Math.sqrt((i + 0.5) / 9), r = 0.07 + 0.08 * (hashN(i, 1, b) * 0.5 + 0.5) * (i < 3 ? 1.6 : 1);
        M.color = greys[(i + b) % 4];
        M.push().translate(Math.cos(a) * d, 0, Math.sin(a) * d * 0.8).rotate(0, i * 47, 0).scale(1, 1, 0.75);
        M.lathe([[r, 0.005], [r * 0.8, r * 0.5], [0, r * 0.7]], 5, false, false, false);
        M.pop();
      }
      return M;
    },
  });

  // ============================================================== crops ==
  // 水田 (rice paddy): an earthen bund round a sheet of still water, rows of rice clumps (young: green; ripe: gold); no ink.
  recipe('rice_paddy', {
    group: 'crop', label: '水田', keys: ['variant', 'w', 'd'], defaults: { variant: 'young', w: 4, d: 3 },
    build(o) {
      const F = builder(o, o.variant, 0.06, 0, 1), W = o.w / 2, D = o.d / 2, ripe = o.variant === 'ripe';
      const bund = H('#8C6E50'), bundTop = H('#7E9A50');
      // water: a slightly sky-tinted sheet (faint glow so it reads as water under any light)
      F.emission = 0.08; F.color = H('#8FB4C0');
      F.quad([-W, 0.06, -D], [W, 0.06, -D], [W, 0.06, D], [-W, 0.06, D], [0, 1, 0]);
      F.emission = 0;
      // bunds: low rounded ridges along the four sides
      for (const [a, c, len, rot] of [[[0, 0, D], 1, o.w, 0], [[0, 0, -D], 1, o.w, 0], [[W, 0, 0], 1, o.d, 90], [[-W, 0, 0], 1, o.d, 90]]) {
        F.push().translate(a).rotate(0, rot, 0);
        F.color = bund;
        F.quad([-len / 2 - 0.15, 0, 0.22], [len / 2 + 0.15, 0, 0.22], [len / 2 + 0.15, 0.16, 0.07], [-len / 2 - 0.15, 0.16, 0.07], [0, 0.5, 1]);
        F.quad([-len / 2 - 0.15, 0, -0.22], [len / 2 + 0.15, 0, -0.22], [len / 2 + 0.15, 0.16, -0.07], [-len / 2 - 0.15, 0.16, -0.07], [0, 0.5, -1]);
        F.color = bundTop;
        F.quad([-len / 2 - 0.15, 0.16, -0.07], [len / 2 + 0.15, 0.16, -0.07], [len / 2 + 0.15, 0.16, 0.07], [-len / 2 - 0.15, 0.16, 0.07], [0, 1, 0]);
        F.pop();
      }
      F.windGradient = true; F.windY0 = 0.06; F.windY1 = 0.7; F.wind = 0.9;
      const g = ripe ? H6('#D8B852', '#C8A040', '#E6C866') : H6('#7FAE4A', '#6E9E40', '#94BE58');
      const nx = Math.max(2, Math.round(o.w / 0.55)), nz = Math.max(2, Math.round(o.d / 0.5));
      for (let i = 0; i < nx; i++) for (let j = 0; j < nz; j++) {
        const x = lerp(-W + 0.35, W - 0.35, i / (nx - 1)), z = lerp(-D + 0.32, D - 0.32, j / (nz - 1));
        for (let q = 0; q < 3; q++) {
          const a = (q * 120 + i * 37 + j * 11) * D2R, tip = [x + Math.cos(a) * 0.12, 0.42 + 0.12 * ((i + j + q) % 3) / 2, z + Math.sin(a) * 0.1];
          F.color = g[(i + j + q) % 3];
          F.blade([x, 0.06, z], tip, 0.06, [-Math.sin(a), 0, Math.cos(a)]);
          if (ripe && q === 0) { F.color = H('#E8C860'); F.blade(tip, V.add(tip, [Math.cos(a) * 0.12, -0.1, Math.sin(a) * 0.1]), 0.05, [0, 0, 1]); }
        }
      }
      return { plain: F };
    },
  });
  // 麦 / 粟 (wheat or millet, after Lanternvale BuildWheat): rows of sheaf-shaped bunches with domed heads, ragged edges,
  // poppies among them; millet's heads droop heavy and orange.
  recipe('wheat', {
    group: 'crop', label: '麦田', keys: ['variant'], defaults: { variant: 'wheat' },
    build(o) {
      const b = o.bucket, M = builder(o, o.variant, 0.05, 0.45, 0.75), sd = 901 + b, millet = o.variant === 'millet';
      const HX = 1.7, HZ = 0.9, Rows = 4, PerRow = 9;
      const gold = P.hsv(millet ? H('#E0A050') : H('#E4C060'), (b - 1.5) * 3), goldDark = P.hsv(millet ? H('#B8783A') : H('#C49A46'), (b - 1.5) * 3);
      const light = millet ? H('#F2C27A') : H('#F6DE92'), stalk = H('#98894A');
      M.color = H('#7E6248');
      M.quad([-HX - 0.1, 0.012, -HZ - 0.08], [HX + 0.1, 0.012, -HZ - 0.08], [HX + 0.1, 0.012, HZ + 0.08], [-HX - 0.1, 0.012, HZ + 0.08], [0, 1, 0]);
      M.windGradient = true; M.windY0 = 0.15; M.windY1 = 1.0; M.wind = 1.3;
      const rowW = 2 * HZ / Rows, tops = [];
      for (let row = 0; row < Rows; row++) for (let i = 0; i < PerRow; i++) {
        const k = row * PerRow + i;
        const x = lerp(-HX + 0.14, HX - 0.14, (i + 0.5 * (row % 2)) / (PerRow - 0.5)) + hashN(k, 1, sd) * 0.06;
        const z = -HZ + rowW * (row + 0.5) + hashN(k, 2, sd) * 0.07;
        const edge = row === 0 || row === Rows - 1 || i === 0 || i === PerRow - 1;
        const h = (0.82 + 0.1 * hashN(k, 3, sd) + 0.05 * Math.sin(x * 2.6 + row)) * (edge ? 0.9 : 1), rt = 0.22 + 0.03 * hashN(k, 4, sd);
        const lz = row === 0 ? -0.07 : row === Rows - 1 ? 0.07 : hashN(k, 6, sd) * 0.05, lx = hashN(k, 5, sd) * 0.08 + (i === 0 ? -0.06 : i === PerRow - 1 ? 0.06 : 0);
        M.push().translate(x, 0, z).rotate(lz * 120 + (millet ? 8 : 0), k * 37, -lx * 120);
        M.lathe([[0.05, 0], [0.09, h * 0.45], [rt * 0.9, h * 0.8], [rt, h * 0.92], [0, h * 1.06]], 5, false, false, false, [stalk, P.mix(stalk, goldDark, 0.6), goldDark, gold, light], k * 13);
        M.pop();
        tops.push([x - lx * h * 2.1, h, z + lz * h * 2.1]);
      }
      for (let i = 0; i < 6; i++) {
        const tp = tops[(i * 13 + b * 5) % tops.length], petal = i % 3 === 2 ? H('#4F7FD8') : H('#E2453A');
        bloom(M, V.add(tp, [0.12, 0.06, 0.12]), 0.075, [0, 1, 0.45], petal, PAL.ink, i * 40);
      }
      M.wind = 0; M.windGradient = false;
      return M;
    },
  });
  // 菜畦 (vegetable bed): a low timber frame round dark tilled soil, rows of cabbages, leafy greens and a squash.
  recipe('vegetables', {
    group: 'crop', label: '菜畦',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 0.4), F = builder(o, 'leaves', 0.08, 0, 1);
      const W = 1.3, D = 0.65, wood = H('#8A6A4C');
      M.color = wood;
      M.box([0, 0.09, D], [W * 2 + 0.12, 0.18, 0.1], null, 'tfkrl'); M.box([0, 0.09, -D], [W * 2 + 0.12, 0.18, 0.1], null, 'tfkrl');
      M.box([W, 0.09, 0], [0.1, 0.18, D * 2], null, 'tfkrl'); M.box([-W, 0.09, 0], [0.1, 0.18, D * 2], null, 'tfkrl');
      F.color = H('#5E4634'); F.quad([-W, 0.12, -D], [W, 0.12, -D], [W, 0.12, D], [-W, 0.12, D], [0, 1, 0]);
      F.color = H('#4E3A2C');
      for (let r = 0; r < 3; r++) { const z = lerp(-D + 0.22, D - 0.22, r / 2); F.quad([-W + 0.05, 0.125, z - 0.08], [W - 0.05, 0.125, z - 0.08], [W - 0.05, 0.125, z + 0.08], [-W + 0.05, 0.125, z + 0.08], [0, 1, 0]); }
      F.windGradient = true; F.windY0 = 0.1; F.windY1 = 0.5; F.wind = 0.4;
      const cab = LP.canopyRamp(H('#B8D884'), H('#86B05A'), H('#4E7A3E'), 0.12, 0.42, { coolMix: 0.2 });
      for (let r = 0; r < 3; r++) for (let i = 0; i < 5; i++) {
        const x = lerp(-W + 0.28, W - 0.28, i / 4) + hashN(i, r, b) * 0.05, z = lerp(-D + 0.22, D - 0.22, r / 2);
        if (r === 1) { for (let q = 0; q < 4; q++) { const a = (q * 90 + i * 30) * D2R; F.color = q % 2 ? H('#6E9E40') : H('#8AB850'); F.blade([x, 0.12, z], [x + Math.cos(a) * 0.16, 0.42, z + Math.sin(a) * 0.12], 0.1); } continue; }
        M.softLump([x, 0.24, z], [0.17, 0.13, 0.17], 0, i * 31 + r * 7, cab, 0.15);
      }
      M.color = H('#E08A3A'); M.sphere([W - 0.25, 0.22, D + 0.25], [0.22, 0.16, 0.2], 8, 4, false);
      M.color = H('#6E8A3E'); M.segment([W - 0.25, 0.36, D + 0.25], [W - 0.2, 0.44, D + 0.22], 0.025, 0.02, 4);
      return { ink: M, plain: F };
    },
  });
  // 草垛 (straw stack): a round stack of straw under an overhanging conical straw cap with a ragged lip and a top-knot.
  recipe('haystack', {
    group: 'crop', label: '草垛',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.3, 0.6);
      const straw = P.hsv(H('#D6BE80'), (b - 1.5) * 3, 0.9), strawD = P.shade(straw, 0.78), cap = P.hsv(H('#C8A866'), (b - 1.5) * 3, 0.95);
      const R = 0.95 + 0.08 * b;
      M.color = straw;
      M.lathe([[R * 0.95, 0], [R, 0.3], [R * 1.02, 0.85], [R * 0.96, 1.25]], 10, false, false, false, [strawD, straw, P.shade(straw, 1.05), straw], b * 20);
      // the cap: a cone whose lip hangs ragged over the stack (alternate lip points lower)
      const n = 12, lip = [], ring2 = [];
      for (let i = 0; i < n; i++) { const a = (i / n) * Math.PI * 2, r = R * 1.22, y = 1.08 - (i % 2 ? 0.1 : 0) - 0.04 * hashN(i, b, 5); lip.push([Math.cos(a) * r, y, Math.sin(a) * r]); ring2.push([Math.cos(a) * R * 0.95, 1.22, Math.sin(a) * R * 0.95]); }
      const apex = [0, 2.25 + 0.1 * b, 0];
      for (let i = 0; i < n; i++) {
        const j = (i + 1) % n, mid = V.mul(V.add(lip[i], lip[j]), 0.5);
        M.triangleFacing(lip[i], lip[j], apex, [mid[0], 0.8, mid[2]], P.shade(cap, 0.94 + 0.1 * ((i % 3) / 2)));
        M.quad(lip[i], lip[j], ring2[j], ring2[i], [0, -1, 0], strawD);
      }
      M.color = P.shade(cap, 0.85); M.cone([0, apex[1] - 0.25, 0], 0.18, 0.45, 6);
      M.color = WOOD.dark; M.segment([0, apex[1] - 0.1, 0], [0.03, apex[1] + 0.35, 0], 0.04, 0.025, 4);
      M.color = straw;
      for (let i = 0; i < 6; i++) { const a = (i * 60 + b * 15) * D2R; M.blade([Math.cos(a) * R * 0.98, 0.02, Math.sin(a) * R * 0.98], [Math.cos(a) * (R + 0.35), 0.05, Math.sin(a) * (R + 0.3)], 0.12, [0, 1, 0]); }
      return M;
    },
  });

  // ============================================================= fences ==
  const WOOD = { light: H('#B08A5E'), mid: H('#9A7050'), dark: H('#6E4B36'), grey: H('#8A7A64'), rope: H('#C8B07A') };
  recipe('fence_wood', {
    group: 'fence', label: '木栅', keys: ['len'], defaults: { len: 2.4 },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.3, 0.6), L = o.len, sd = 951 + b;
      const n = Math.max(2, Math.round(L / 1.2) + 1), wood = b % 2 ? WOOD.mid : WOOD.grey;
      const xs = []; for (let i = 0; i < n; i++) xs.push(lerp(-L / 2, L / 2, i / (n - 1)));
      xs.forEach((x, i) => {
        M.color = P.shade(wood, 0.92 + 0.12 * (hashN(i, 1, sd) * 0.5 + 0.5));
        M.push().translate(x, 0, 0).rotate(hashN(i, 2, sd) * 3, hashN(i, 3, sd) * 20, hashN(i, 4, sd) * 3);
        M.box([0, 0.45, 0], [0.12, 0.9, 0.12], null, 'fkrl');
        M.taperedBox([0, 0.9, 0], [0.12, 0.1, 0.12], 0.85);
        M.pop();
      });
      M.color = wood;
      for (const [y, dz] of [[0.36, 0.08], [0.72, 0.08]]) for (let i = 0; i < n - 1; i++) {
        const y0 = y + hashN(i, 5 + y * 10, sd) * 0.03, y1 = y + hashN(i + 1, 5 + y * 10, sd) * 0.03;
        M.color = P.shade(wood, 0.95 + 0.1 * (hashN(i, 9, sd) * 0.5 + 0.5));
        M.beam([xs[i] - 0.06, y0, dz], [xs[i + 1] + 0.06, y1, dz], 0.06, 0.09);
      }
      return M;
    },
  });
  recipe('fence_wattle', {
    group: 'fence', label: '篱笆', keys: ['len'], defaults: { len: 2.2 },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.3, 0.5), L = o.len, sd = 971 + b;
      const n = Math.max(3, Math.round(L / 0.42) + 1), xs = [];
      for (let i = 0; i < n; i++) xs.push(lerp(-L / 2, L / 2, i / (n - 1)));
      xs.forEach((x, i) => { M.color = P.shade(H('#7A5E44'), 0.9 + 0.15 * (hashN(i, 1, sd) * 0.5 + 0.5)); M.segment([x, -0.02, 0], [x + hashN(i, 2, sd) * 0.04, 0.92 + 0.08 * hashN(i, 3, sd), 0], 0.03, 0.022, 4, false, true); });
      const withy = H6('#A08058', '#8E6E4C', '#B49068');
      for (let r = 0; r < 4; r++) {
        const y = 0.2 + r * 0.19, pts = xs.map((x, i) => [x, y + 0.02 * hashN(i, r + 7, sd), ((i + r) % 2 ? 1 : -1) * 0.04]);
        M.color = withy[r % 3];
        rib(M, pts, 0.034, 0.03, 3);
      }
      return M;
    },
  });
  // 石垣: a dry-stone wall: irregular blocks in courses, cap stones on top, moss here and there.
  recipe('wall_stone', {
    group: 'fence', label: '石垣', keys: ['len'], defaults: { len: 3 },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.35, 0.8), L = o.len, sd = 991 + b;
      const greys = H6('#A8A090', '#B8B0A0', '#94907E', '#C2B8A4');
      M.jitterAll = true;
      const courses = [[0, 0.3], [0.3, 0.26], [0.56, 0.22]];
      courses.forEach(([y, h], r) => {
        let x = -L / 2 + (r % 2 ? 0.18 : 0);
        if (r % 2) { M.color = greys[r]; M.box([-L / 2 + 0.09, y + h / 2, 0], [0.18, h * 0.96, 0.46 - r * 0.04], null, 'tfkl'); }
        let k = 0;
        while (x < L / 2 - 0.05) {
          let w = 0.32 + 0.22 * (hashN(k, r, sd) * 0.5 + 0.5);
          if (L / 2 - (x + w) < 0.2) w = L / 2 - x;
          const hh = h * (0.9 + 0.1 * hashN(k, r + 3, sd)), d = 0.46 - r * 0.04;
          M.color = greys[(k + r * 2 + b) % 4];
          M.push().translate(x + w / 2, y + hh / 2, hashN(k, r + 5, sd) * 0.02).rotate(0, hashN(k, r + 6, sd) * 4, hashN(k, r + 7, sd) * 3);
          M.box([0, 0, 0], [w - 0.03, hh, d], null, 'tfkrl');
          M.pop();
          x += w; k++;
        }
      });
      // cap stones
      let x = -L / 2, k = 0;
      while (x < L / 2 - 0.05) {
        const w = Math.min(L / 2 - x, 0.42 + 0.2 * (hashN(k, 9, sd) * 0.5 + 0.5));
        M.color = P.shade(greys[(k + 1) % 4], 1.04);
        M.push().translate(x + w / 2, 0.84, 0).rotate(hashN(k, 10, sd) * 4, 0, hashN(k, 11, sd) * 5);
        M.box([0, 0, 0], [w - 0.02, 0.12, 0.42], P.mix(greys[k % 4], PAL.moss, k % 3 === 0 ? 0.55 : 0.15), 'tfkrl');
        M.pop();
        x += w; k++;
      }
      const ramp = LP.canopyRamp(PAL.mossLight, PAL.moss, PAL.leafDark, 0.85, 1.0);
      for (let i = 0; i < 2; i++) M.softLump([-L / 2 + L * (0.25 + 0.45 * i) + hashN(i, 12, sd) * 0.2, 0.92, 0.05], [0.22, 0.07, 0.18], 0, i * 17, ramp, 0.15);
      M.tuft([-L / 2 + 0.3, 0, 0.32], 0.3, 4, PAL.leaf); M.tuft([L / 2 - 0.5, 0, 0.3], 0.26, 4, PAL.leafDark);
      return M;
    },
  });
  recipe('fence_bamboo', {
    group: 'fence', label: '竹篱', keys: ['len'], defaults: { len: 2.4 },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.25, 0.5), L = o.len;
      const g = H('#B8A86A'), gd = H('#9C8C52'), tie = H('#6E5236');
      const xs = [-L / 2, 0, L / 2];
      for (const x of xs) { M.color = gd; M.segment([x, 0, 0], [x, 1.15, 0], 0.045, 0.04, 5, false, true); }
      M.color = g;
      for (const y of [0.25, 1.0]) M.segment([-L / 2 - 0.08, y, 0.05], [L / 2 + 0.08, y, 0.05], 0.025, 0.025, 4);
      // diagonal lattice
      const n = Math.round(L / 0.36);
      for (let i = 0; i <= n; i++) {
        const x = lerp(-L / 2, L / 2, i / n);
        for (const s of [-1, 1]) { const x2 = clamp(x + s * 0.55, -L / 2, L / 2); M.color = (i + (s > 0 ? 1 : 0)) % 2 ? g : gd; M.segment([x, 0.25, 0.08], [x2, 0.25 + Math.abs(x2 - x) * (0.75 / 0.55), 0.08], 0.016, 0.016, 3, false, false); }
      }
      M.color = tie;
      for (const x of xs) for (const y of [0.25, 1.0]) M.box([x, y, 0.05], [0.1, 0.06, 0.1]);
      return M;
    },
  });

  // ============================================================== lights ==
  /** Fire tongues (always-on emission, no ink): crossed blades, orange outside, yellow inside. */
  function flame(F, c, h, r, k0 = 0) {
    const keepE = F.emission, keepW = F.wind, keepG = F.windGradient;
    F.windGradient = false; F.wind = 0.25;
    F.emission = 1;
    for (let i = 0; i < 4; i++) {
      const a = (i * 45 + k0 * 20) * D2R, side = [Math.cos(a), 0, Math.sin(a)];
      F.color = H('#FF8A3A');
      F.blade(V.add(c, V.mul(side, r * 0.2)), V.add(c, [Math.cos(a) * r * 0.25, h * (0.85 + 0.15 * (i % 2)), Math.sin(a) * r * 0.25]), r * 1.3, side);
    }
    for (let i = 0; i < 3; i++) {
      const a = (i * 60 + 30) * D2R, side = [Math.cos(a), 0, Math.sin(a)];
      F.color = H('#FFD25A');
      F.blade(V.add(c, [0, h * 0.02, 0]), V.add(c, [0, h * 0.6, 0]), r * 0.8, side);
    }
    F.emission = keepE; F.wind = keepW; F.windGradient = keepG;
  }
  Props.helpers.flame = flame;
  // Han lamp post (the pilot's 汉式灯杆): a square timber post on a stone foot carrying a lantern pavilion: a dark frame
  // round cream paper panels under a slate-tiled hip cap with upturned corners and a gold finial. The paper glows by night.
  recipe('lamp_post', {
    group: 'light', label: '灯杆', keys: ['lit'], defaults: { lit: 'night' },
    build(o) {
      const by = 2.22, bh = 0.46, bw = 0.4, M = builder(o, '', 0.06, 0.3, 0.5, 601), lit = o.lit;
      M.color = PAL.stone; M.cylinder([0, 0, 0], 0.25, 0.2, 0.22, 6, false, false, true);
      M.color = PAL.timber; M.box([0, 0.22 + (by - 0.22) / 2, 0], [0.15, by - 0.22, 0.15], null, 'tfkrl');
      M.color = PAL.timberDark; M.box([0, by - 0.25, 0], [0.18, 0.08, 0.18], null, 'tbfkrl');
      M.box([0, by + 0.03, 0], [bw + 0.12, 0.06, bw + 0.12]);
      M.color = lit === true ? P.mix(PAL.paper, PAL.glow, 0.55) : P.shade(PAL.paper, 0.97);
      M.emission = lit === true ? 0.95 : lit === false ? 0.06 : 0; M.nightEmission = lit === 'night' ? 0.95 : 0;
      M.box([0, by + 0.06 + bh / 2, 0], [bw, bh, bw], null, 'fkrl');
      M.emission = 0; M.nightEmission = 0; M.color = PAL.timberDark;
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.box([sx * bw / 2, by + 0.06 + bh / 2, sz * bw / 2], [0.055, bh, 0.055], null, 'fkrl');
      for (const s of [-1, 1]) { M.box([0, by + 0.06 + bh * 0.5, s * (bw / 2 + 0.005)], [bw, 0.035, 0.02], null, s > 0 ? 'f' : 'k'); M.box([s * (bw / 2 + 0.005), by + 0.06 + bh * 0.5, 0], [0.02, 0.035, bw], null, s > 0 ? 'r' : 'l'); }
      M.box([0, by + 0.06 + bh + 0.03, 0], [bw + 0.1, 0.06, bw + 0.1], null, 'bfkrl');
      const y0 = by + 0.06 + bh + 0.06, e = 0.4, apex = [0, y0 + 0.3, 0];
      const cn = [[-e, y0 + 0.07, -e], [e, y0 + 0.07, -e], [e, y0 + 0.07, e], [-e, y0 + 0.07, e]], mids = [[0, y0, -e * 0.96], [e * 0.96, y0, 0], [0, y0, e * 0.96], [-e * 0.96, y0, 0]];
      for (let i = 0; i < 4; i++) {
        const a = cn[i], b2 = cn[(i + 1) % 4], m = mids[i], out = [m[0], 0.8, m[2]];
        M.color = i % 2 ? PAL.tileSlate : P.shade(PAL.tileSlate, 0.92);
        M.triangleFacing(a, m, apex, out); M.triangleFacing(m, b2, apex, out);
        M.color = H('#4F5E6E'); M.triangleFacing(a, b2, [0, y0 - 0.02, 0], [0, -1, 0]);
        M.color = PAL.tileSlateDark; M.spike(a, [a[0], 0.9, a[2]], 0.025, 0.12, 3);
      }
      M.color = PAL.gold; M.sphere([apex[0], apex[1] + 0.04, apex[2]], 0.05, 5, 2, false);
      return { ink: M, lamps: [{ pos: [0, by + 0.06 + bh / 2, 0], color: '#ffe2a6', intensity: 0.9, range: 5.2, haloSize: 1.3 }] };
    },
  });
  // 挂灯笼: a red ribbed Han lantern hanging from the arm of a timber post with a little tiled cap.
  recipe('lantern_hanging', {
    group: 'light', label: '挂灯笼', keys: ['lit', 'paper'], defaults: { lit: 'night', paper: '#C8402E' },
    build(o) {
      const M = builder(o, '', 0.06, 0.3, 0.5);
      M.color = PAL.stone; M.box([0, 0.1, 0], [0.34, 0.2, 0.34], null, 'tfkrl');
      M.color = PAL.timber; M.box([0, 1.35, 0], [0.13, 2.5, 0.13], null, 'tfkrl');
      M.color = PAL.timberDark;
      M.beam([0, 2.35, 0], [0, 2.35, 0.95], 0.08, 0.1);
      M.beam([0, 1.95, 0.02], [0, 2.3, 0.45], 0.05, 0.05);
      M.color = PAL.tileSlate; M.taperedBox([0, 2.6, 0], [0.32, 0.14, 0.32], 0.7);
      M.color = PAL.gold; M.sphere([0, 2.78, 0], 0.045, 5, 2, false);
      M.color = H('#3E2C24'); M.segment([0, 2.3, 0.9], [0, 2.05, 0.9], 0.012, 0.012, 3, false, false);
      const c = [0, 1.8, 0.9];
      M.ribLantern(c, 0.26, 0.38, H(o.paper), o.lit, PAL.gold, PAL.gold, 6, 0.15);
      return { ink: M, lamps: [{ pos: c, color: '#ffb46c', intensity: 0.9, range: 4.6, haloSize: 1.3 }] };
    },
  });
  // 铜灯台: a bronze standing oil lamp (a wide foot, a slender stem with a knop, a dish) with a small flame.
  recipe('oil_lamp', {
    group: 'light', label: '铜灯台',
    build(o) {
      const M = builder(o, '', 0.05, 0.2, 0.3), F = builder(o, 'flame', 0, 0, 1);
      const bronze = H('#A07A3A'), bronzeD = H('#6E5A2E'), verd = H('#6E9A84');
      M.color = bronze;
      M.lathe([[0.26, 0], [0.24, 0.05], [0.1, 0.12], [0.05, 0.2], [0.04, 0.7], [0.07, 0.74], [0.04, 0.78], [0.04, 1.05], [0.18, 1.12], [0.2, 1.17]], 8, false, true, false,
        [bronzeD, P.mix(bronze, verd, 0.4), bronze, bronze, bronze, P.shade(bronze, 1.1), bronze, bronze, bronze, P.shade(bronze, 1.08)]);
      M.color = H('#3E3020'); M.disc([0, 1.13, 0], 0.17, 8);
      flame(F, [0, 1.14, 0], 0.22, 0.06);
      return { ink: M, plain: F, lamps: [{ pos: [0, 1.3, 0], color: '#ffb060', intensity: 0.7, range: 4.2, flicker: 0.15, nightOnly: false, haloSize: 0.8 }] };
    },
  });
  // 火盆: an iron tripod brazier: three curled legs, a bowl of glowing coals and flames.
  recipe('brazier', {
    group: 'light', label: '火盆',
    build(o) {
      const M = builder(o, '', 0.05, 0.2, 0.3), F = builder(o, 'flame', 0, 0, 1), iron = H('#4A4448');
      M.color = iron;
      for (let i = 0; i < 3; i++) {
        const a = (i * 120 + 30) * D2R, d = [Math.cos(a), 0, Math.sin(a)];
        M.curve(V.mul(d, 0.42), V.add(V.mul(d, 0.2), [0, 0.45, 0]), V.add(V.mul(d, 0.3), [0, 0.86, 0]), 0.03, 0.03, 3, 4);
        M.sphere(V.add(V.mul(d, 0.43), [0, 0.02, 0]), 0.045, 5, 3, false);
      }
      M.lathe([[0.12, 0.78], [0.32, 0.84], [0.42, 0.98], [0.44, 1.04]], 8, false, true, false);
      M.band([0, 0, 0], 0.45, 0.45, 1.0, 1.05, 8);
      M.emission = 0.6;
      for (let i = 0; i < 5; i++) { const a = i * 2.4; M.color = i % 2 ? H('#C8402A') : H('#7A2A20'); gem(M, [Math.cos(a) * 0.2, 1.0, Math.sin(a) * 0.2], [0.1, 0.05, 0.09]); }
      M.emission = 0;
      flame(F, [0, 1.0, 0], 0.65, 0.18);
      flame(F, [0.14, 1.0, 0.05], 0.4, 0.1, 1);
      return { ink: M, plain: F, lamps: [{ pos: [0, 1.5, 0], color: '#ff9a4a', intensity: 1.2, range: 6, flicker: 0.25, nightOnly: false, haloSize: 1.6 }] };
    },
  });
  // 火把: a pole torch: a wrapped head in an iron collar and a flame.
  recipe('torch', {
    group: 'light', label: '火把',
    build(o) {
      const M = builder(o, '', 0.05, 0.2, 0.3), F = builder(o, 'flame', 0, 0, 1);
      M.color = WOOD.dark; M.segment([0, 0, 0], [0, 1.85, 0], 0.045, 0.035, 5);
      M.color = H('#4A4448'); M.band([0, 0, 0], 0.06, 0.07, 1.72, 1.8, 6);
      M.color = H('#8A6A44'); M.lathe([[0.06, 1.8], [0.09, 1.86], [0.085, 2.02], [0.05, 2.06]], 6, false, true, true);
      flame(F, [0, 2.04, 0], 0.5, 0.12);
      return { ink: M, plain: F, lamps: [{ pos: [0, 2.3, 0], color: '#ffa050', intensity: 0.9, range: 5, flicker: 0.3, nightOnly: false, haloSize: 1.1 }] };
    },
  });
  // 石灯笼: a stone lantern (temple courtyards, the Japanese islands): foot, shaft, a fire box with lit windows, a
  // spreading roof with lifted corners and a jewel finial.
  recipe('stone_lantern', {
    group: 'light', label: '石灯笼', keys: ['lit'], defaults: { lit: 'night' },
    build(o) {
      const M = builder(o, '', 0.07, 0.3, 0.4), st = H('#A8A498'), stD = H('#8E8A80');
      M.jitterAll = true;
      M.color = stD; M.extrude(ngon([0, 0, 0], 0.4, 0.4, 6, [1, 0, 0], [0, 0, 1]).map(p => [p[0], p[2]]), 0, 0.18);
      M.color = st; M.cylinder([0, 0.18, 0], 0.15, 0.13, 0.75, 6, false, false, false);
      M.color = stD; M.extrude(ngon([0, 0, 0], 0.32, 0.32, 6, [1, 0, 0], [0, 0, 1]).map(p => [p[0], p[2]]), 0.93, 1.05);
      // fire box: four posts round paper windows
      M.color = P.shade(PAL.paper, 0.95); M.nightEmission = o.lit === 'night' ? 0.9 : 0; M.emission = o.lit === true ? 0.9 : 0;
      M.box([0, 1.22, 0], [0.3, 0.3, 0.3], null, 'fkrl');
      M.nightEmission = 0; M.emission = 0; M.color = st;
      for (const sx of [-1, 1]) for (const sz of [-1, 1]) M.box([sx * 0.15, 1.22, sz * 0.15], [0.07, 0.34, 0.07], null, 'fkrl');
      // roof: a hexagonal flared cap
      const ring = (r, y, lift) => ngon([0, y, 0], r, r, 6, [1, 0, 0], [0, 0, 1]).map((p, i) => [p[0], y + lift, p[2]]);
      M.color = st;
      M.loft([ring(0.52, 1.42, 0.06), ring(0.46, 1.5, 0.02), ring(0.18, 1.66, 0)], true, false);
      M.color = stD; M.sphere([0, 1.74, 0], [0.09, 0.11, 0.09], 6, 3, false);
      return { ink: M, lamps: [{ pos: [0, 1.22, 0], color: '#ffd59a', intensity: 0.6, range: 3.5, haloSize: 0.9 }] };
    },
  });

  // ============================================================= banners ==
  /** A cloth panel hanging from a top edge (closed slab rows that bend with the wind), width w, length h, at top-left a. */
  function hangingCloth(M, top, w, h, color, rows = 4, thick = 0.03, facing = [0, 0, 1]) {
    for (let r = 0; r < rows; r++) {
      const y0 = top[1] - h * r / rows, y1 = top[1] - h * (r + 1) / rows;
      M.color = P.shade(color, 1 - 0.04 * (r % 2));
      M.strip([top[0], y0, top[2]], [top[0], y1, top[2]], w, w, facing, thick);
    }
  }
  // 战旗 (war banner): a tall pole with a spear finial and a red tassel, a crossbar holding a long vertical flag in the
  // faction colour with a flame-tooth border (牙边) and a pale emblem disc; the flag sways from its top.
  recipe('banner', {
    group: 'banner', label: '战旗', keys: ['color', 'trim'], defaults: { color: '#2f7d4a', trim: '#F2E6C8' },
    build(o) {
      const M = builder(o, '', 0.04, 0.2, 0.5), col = H(o.color), trim = H(o.trim);
      M.color = PAL.stone; M.cylinder([0, 0, 0], 0.18, 0.15, 0.16, 6, false, false, true);
      M.color = WOOD.dark; M.segment([0, 0.1, 0], [0, 5.2, 0], 0.05, 0.04, 5);
      M.color = PAL.gold; M.band([0, 0, 0], 0.06, 0.06, 4.9, 4.97, 5);
      M.color = H('#C8C4BC'); M.spike([0, 5.2, 0], [0, 1, 0], 0.05, 0.36, 4);
      M.color = PAL.vermilion; M.windGradient = false; M.wind = 0.6; gem(M, [0, 5.12, 0], [0.09, 0.12, 0.09]); M.wind = 0;
      M.color = WOOD.dark; M.beam([0, 4.85, 0.02], [1.15, 4.85, 0.02], 0.05, 0.05);
      // the flag: hangs from the crossbar; wind grows towards its free (lower) edge
      M.windGradient = true; M.windY0 = 4.8; M.windY1 = 2.6; M.wind = 1.4;
      const top = [0.6, 4.8, 0.02], fw = 0.92, fh = 2.1;
      hangingCloth(M, top, fw, fh, col, 5, 0.03);
      // flame teeth along the free side and the foot
      M.color = trim;
      for (let i = 0; i < 6; i++) { const y = 4.8 - fh * (i + 0.5) / 6; slab(M, [[top[0] + fw / 2, y + 0.12, 0.02], [top[0] + fw / 2 + 0.16, y, 0.02], [top[0] + fw / 2, y - 0.12, 0.02]], [0, 0, 1], 0.02, trim); }
      for (let i = 0; i < 4; i++) { const x = top[0] - fw / 2 + fw * (i + 0.5) / 4; slab(M, [[x - 0.11, 4.8 - fh, 0.02], [x + 0.11, 4.8 - fh, 0.02], [x, 4.8 - fh - 0.18, 0.02]], [0, 0, 1], 0.02, trim); }
      // emblem: a pale disc on both faces
      M.color = trim;
      slab(M, ngon([top[0], 4.8 - fh * 0.42, 0.04], 0.3, 0.3, 10, [1, 0, 0], [0, 1, 0]), [0, 0, 1], 0.012, trim);
      slab(M, ngon([top[0], 4.8 - fh * 0.42, 0.0], 0.3, 0.3, 10, [1, 0, 0], [0, 1, 0]), [0, 0, -1], 0.012, trim);
      M.wind = 0; M.windGradient = false;
      return { ink: M, anchors: { emblem: [top[0], 4.8 - fh * 0.42, 0.05] } };
    },
  });
  // 三角旗 (pennant): a long swallow-tailed pennant on a short pole, swaying most at its tails.
  recipe('flag', {
    group: 'banner', label: '三角旗', keys: ['color'], defaults: { color: '#2f7d4a' },
    build(o) {
      const M = builder(o, '', 0.04, 0.2, 0.5), col = H(o.color);
      M.color = WOOD.dark; M.segment([0, 0, 0], [0, 3.4, 0], 0.045, 0.035, 5);
      M.color = PAL.gold; M.cone([0, 3.4, 0], 0.06, 0.18, 5);
      const start = M.vertexCount;
      M.color = col;
      const y = 3.25, len = 1.6;
      slab(M, [[0.04, y, 0], [len * 0.55, y - 0.18, 0], [len * 0.55, y - 0.42, 0], [0.04, y - 0.62, 0]], [0, 0, 1], 0.025, col);
      slab(M, [[len * 0.55, y - 0.18, 0], [len, y - 0.2, 0], [len * 0.72, y - 0.31, 0], [len * 0.55, y - 0.3, 0]], [0, 0, 1], 0.025, P.shade(col, 0.95));
      slab(M, [[len * 0.55, y - 0.3, 0], [len * 0.72, y - 0.31, 0], [len, y - 0.46, 0], [len * 0.55, y - 0.42, 0]], [0, 0, 1], 0.025, P.shade(col, 0.9));
      M.windBy(start, p => clamp01(p[0] / len) * 1.5);
      return M;
    },
  });
  // 垂幡: a long narrow banner on a T-frame, edged, with tassels at the foot.
  recipe('banner_hanging', {
    group: 'banner', label: '垂幡', keys: ['color', 'trim'], defaults: { color: '#C9503A', trim: '#E3B34C' },
    build(o) {
      const M = builder(o, '', 0.04, 0.2, 0.5), col = H(o.color), trim = H(o.trim);
      M.color = PAL.stone; M.box([0, 0.08, 0], [0.4, 0.16, 0.4], null, 'tfkrl');
      M.color = WOOD.dark; M.segment([0, 0.1, 0], [0, 3.6, 0], 0.05, 0.045, 5);
      M.beam([-0.5, 3.45, 0.06], [0.5, 3.45, 0.06], 0.06, 0.06);
      M.color = PAL.gold; for (const s of [-1, 1]) M.sphere([s * 0.53, 3.45, 0.06], 0.05, 5, 2, false);
      M.windGradient = true; M.windY0 = 3.4; M.windY1 = 1.0; M.wind = 1.0;
      hangingCloth(M, [0, 3.4, 0.1], 0.6, 2.2, col, 4, 0.025);
      M.color = trim;
      for (const s of [-1, 1]) M.strip([s * 0.3, 3.4, 0.11], [s * 0.3, 1.2, 0.11], 0.06, 0.06, [0, 0, 1], 0.03);
      for (let i = 0; i < 3; i++) { const x = (i - 1) * 0.2; M.color = trim; M.segment([x, 1.2, 0.1], [x, 0.95, 0.1], 0.02, 0.04, 4, false, true); }
      M.wind = 0; M.windGradient = false;
      return M;
    },
  });

  // ============================================================ clutter ==
  recipe('crate', {
    group: 'clutter', label: '木箱',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.3, 0.4), s = [0.78, 0.66, 0.9, 0.72][b];
      const wood = P.hsv(WOOD.light, (b - 1.5) * 4), batten = P.shade(wood, 0.78);
      M.color = wood; M.box([0, s / 2, 0], [s, s, s], P.shade(wood, 1.05), 'tfkrl');
      M.color = batten;
      for (const [x, z] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) M.box([x * (s / 2 - 0.02), s / 2, z * (s / 2 - 0.02)], [0.07, s + 0.01, 0.07], null, 'tfkrl');
      for (const y of [0.04, s - 0.04]) { M.box([0, y, s / 2 + 0.005], [s - 0.02, 0.07, 0.03], null, 'tf'); M.box([s / 2 + 0.005, y, 0], [0.03, 0.07, s - 0.02], null, 'tr'); M.box([-s / 2 - 0.005, y, 0], [0.03, 0.07, s - 0.02], null, 'tl'); }
      M.beam([-s / 2 + 0.06, 0.08, s / 2 + 0.01], [s / 2 - 0.06, s - 0.08, s / 2 + 0.01], 0.06, 0.025, [0, 0, 1]);
      M.color = P.shade(wood, 0.84);
      for (let i = 1; i < 3; i++) M.box([0, s * i / 3, s / 2 + 0.003], [s - 0.1, 0.012, 0.012], null, 'f');
      return M;
    },
  });
  recipe('barrel', {
    group: 'clutter', label: '木桶',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.3, 0.4), wood = P.hsv(H('#A07A52'), (b - 1.5) * 4), iron = H('#4F4A55');
      M.color = wood;
      M.lathe([[0.3, 0], [0.36, 0.25], [0.38, 0.45], [0.36, 0.65], [0.3, 0.9]], 10, false, true, true, [P.shade(wood, 0.85), wood, P.shade(wood, 1.06), wood, P.shade(wood, 0.95)]);
      M.color = iron;
      for (const [y, r] of [[0.12, 0.335], [0.78, 0.335], [0.42, 0.385]]) M.band([0, 0, 0], r, r, y, y + 0.06, 10);
      M.color = P.shade(wood, 1.12); M.disc([0, 0.902, 0], 0.27, 10);
      return M;
    },
  });
  // 陶缸 / 坛: a glazed wine jar (brown, ochre, celadon or black glaze) with a cloth-tied lid.
  recipe('jar', {
    group: 'clutter', label: '酒坛',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.25, 0.3);
      const glaze = H6('#7A4A32', '#B07A3A', '#7E9A84', '#3E3438')[b], clay = H('#B08866');
      M.color = glaze;
      M.lathe([[0.16, 0], [0.2, 0.04], [0.3, 0.25], [0.33, 0.42], [0.28, 0.56], [0.14, 0.64], [0.12, 0.7]], 10, true, true, false,
        [clay, P.shade(glaze, 0.9), glaze, P.shade(glaze, 1.08), glaze, glaze, P.shade(glaze, 0.92)]);
      M.color = PAL.vermilion; M.sphere([0, 0.72, 0], [0.15, 0.06, 0.15], 8, 3, false);
      M.color = WOOD.rope; M.band([0, 0, 0], 0.13, 0.13, 0.66, 0.69, 8);
      if (b === 1) { M.color = PAL.paper; slab(M, [[-0.1, 0.42, 0.33], [0.1, 0.42, 0.33], [0.1, 0.24, 0.31], [-0.1, 0.24, 0.31]], [0, 0.1, 1], 0.004, H('#E8D8B0')); }
      return M;
    },
  });
  // 粮袋: three grain sacks, tied at the neck, slumped against each other.
  recipe('sacks', {
    group: 'clutter', label: '粮袋',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 0.4), cloth = H6('#D8C8A0', '#CDBB90', '#E2D4B0');
      const sack = (c, r, h, yaw, tilt, col) => {
        M.color = col;
        M.push().translate(c).rotate(tilt, yaw, 0);
        M.lathe([[r * 0.85, 0], [r, h * 0.12], [r * 1.02, h * 0.4], [r * 0.85, h * 0.72], [r * 0.32, h * 0.9], [r * 0.2, h * 0.94], [r * 0.3, h * 1.04], [0, h * 1.06]], 8, true, false, false,
          [P.shade(col, 0.88), col, P.shade(col, 1.05), col, P.shade(col, 0.95), P.shade(col, 0.8), col, col], yaw);
        M.color = WOOD.rope; M.band([0, 0, 0], r * 0.24, r * 0.22, h * 0.88, h * 0.92, 6);
        M.pop();
      };
      sack([0, 0, 0], 0.3, 0.75, b * 30, 0, cloth[0]);
      sack([0.52, 0, 0.1], 0.28, 0.68, 40, -12, cloth[1]);
      sack([0.22, 0, 0.5], 0.27, 0.6, 80, 15, cloth[2]);
      return M;
    },
  });
  // 兵器架: a weapon rack with spears, a halberd (戟) and a glaive leaning in it.
  recipe('weapon_rack', {
    group: 'clutter', label: '兵器架',
    build(o) {
      const M = builder(o, '', 0.06, 0.3, 0.5), wood = WOOD.mid, steel = H('#C8C8C8');
      M.color = wood;
      for (const x of [-0.8, 0.8]) M.box([x, 0.6, 0], [0.1, 1.2, 0.1], null, 'tfkrl');
      M.box([0, 1.1, 0], [1.8, 0.08, 0.1], null, 'tfkrl'); M.box([0, 0.15, 0.18], [1.8, 0.1, 0.36], null, 'tfkrl');
      const poles = [[-0.55, 2.4, 'spear'], [-0.2, 2.6, 'ji'], [0.15, 2.3, 'spear'], [0.5, 2.5, 'glaive']];
      for (const [x, len, kind] of poles) {
        const base = [x, 0.2, 0.22], top = [x + 0.1, 0.2 + len, -0.05];
        M.color = WOOD.dark; M.segment(base, top, 0.025, 0.022, 4);
        const d = V.norm(V.sub(top, base));
        M.color = steel;
        if (kind === 'spear') { M.spike(top, d, 0.05, 0.32, 4); M.color = PAL.vermilion; M.sphere(V.madd(top, d, -0.06), [0.06, 0.08, 0.06], 5, 2, false); }
        else if (kind === 'ji') { M.spike(top, d, 0.045, 0.3, 4); M.box(V.madd(top, d, -0.12), [0.32, 0.05, 0.02]); }
        else { M.push().translate(top).rotateQ(new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), new THREE.Vector3(...d))); M.flat([[0, 0], [0.12, 0.1], [0.1, 0.5], [-0.02, 0.62], [-0.04, 0.1]], 0.02); M.pop(); }
      }
      return M;
    },
  });
  // 柴堆: a stack of split logs, end grain showing.
  recipe('woodpile', {
    group: 'clutter', label: '柴堆',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.3, 0.4), bark = H('#7A5A42'), end = H('#D8B88A'), ring = H('#B8925E');
      const rows = [[5, 0.13], [4, 0.36], [3, 0.58]];
      rows.forEach(([n, y], r) => {
        for (let i = 0; i < n; i++) {
          const x = (i - (n - 1) / 2) * 0.24 + hashN(i, r, b) * 0.02, rr = 0.11 + 0.015 * hashN(i, r + 3, b);
          M.push().translate(0, y, x * 0.9).rotate(0, hashN(i, r + 5, b) * 6, 90);
          M.lathe([[0, -0.5], [rr, -0.5], [rr, 0.5], [0, 0.5]], 5, false, false, false, [end, bark, bark, end], i * 20);
          M.pop();
        }
      });
      return M;
    },
  });


  // ====================================================== building parts ==
  // Every part draws into a MeshBuilder in its current transform: local Y up, the building's front facing +Z, the part
  // centred on x = 0, z = 0 unless noted. Colours are sRGB arrays or '#hex'. The cities workstream composes these.
  const ROOF_PAL = {
    slate: { tile: H('#808DA6'), tileAlt: H('#73809A'), rib: H('#8794AD'), end: H('#4F5B72'), ridge: H('#56627A'), soffit: H('#4F6E6A') },
    grey: { tile: H('#8C8A86'), tileAlt: H('#7E7C78'), rib: H('#969490'), end: H('#5A5856'), ridge: H('#5E5C5A'), soffit: H('#5E5A52') },
    green: { tile: H('#5E9070'), tileAlt: H('#548464'), rib: H('#6CA07E'), end: H('#3A5A46'), ridge: H('#3E604C'), soffit: H('#4F6E6A') },
    gold: { tile: H('#D8A838'), tileAlt: H('#CC9A30'), rib: H('#E4B848'), end: H('#9A6A20'), ridge: H('#A87424'), soffit: H('#4F6E6A') },
    terracotta: { tile: H('#C9785A'), tileAlt: H('#BA6A4E'), rib: H('#D88A68'), end: H('#8E4A36'), ridge: H('#9A5440'), soffit: H('#7A5A46') },
  };
  const toC = c => (c == null ? null : Array.isArray(c) ? c : H(c));
  /**
   * One tiled roof slope S(u, v) (u along the eave 0..1, v from the eave 0 up to 1): course quads in alternating tones,
   * each course's foot lifted over the one below with a dark step face; raised round-tile ribs (筒瓦) sampled at ribV,
   * eave-tile ends (瓦当), a fascia board and a painted soffit under the eave (the pilot gate roof, generalised).
   * o: { out, cols, courses, ribs | ribU, ribV, ends, fascia (colour), soffit (colour), pal, step, ribH, ribW }
   */
  function tiledSlope(M, S, o) {
    const pal = o.pal || ROOF_PAL.slate, out = o.out, STEP = o.step != null ? o.step : 0.045, RIB_H = o.ribH || 0.1, RIB_W = o.ribW || 0.075;
    const Nrm = (u, v) => {
      const e = 0.01, du = V.sub(S(u + e, v), S(u - e, v)), dv = V.sub(S(u, v + e), S(u, v - e));
      let n = V.cross(du, dv); if (V.dot(n, n) < 1e-12) return V.norm(out); n = V.norm(n); if (V.dot(n, out) < 0) n = V.neg(n); return n;
    };
    const cs = o.courses;
    for (let k = 0; k < cs.length - 1; k++) {
      const v0 = cs[k], v1 = cs[k + 1], base = P.shade(k % 2 ? pal.tileAlt : pal.tile, 1 + 0.025 * (k - 1));
      for (let j = 0; j < o.cols; j++) {
        const u0 = j / o.cols, u1 = (j + 1) / o.cols, lift = u => (k ? STEP * (u <= 0 || u >= 1 ? 0.15 : 1) : 0);
        const a0 = S(u0, v0), a1 = S(u1, v0), a = V.madd(a0, Nrm(u0, v0), lift(u0)), b = V.madd(a1, Nrm(u1, v0), lift(u1));
        M.quad(a, b, S(u1, v1), S(u0, v1), out, base);
        if (k && STEP > 0) M.quad(a0, a1, b, a, V.sub(a0, S(u0, v0 + 0.05)), P.shade(base, 0.6));
      }
    }
    const ribU = o.ribU || (o.ribs ? Array.from({ length: o.ribs }, (_, i) => (i + 0.5) / o.ribs) : []);
    for (const u of ribU) {
      const st = o.ribV, T = v => V.norm(V.sub(S(u + 0.01, v), S(u - 0.01, v)));
      const pts = st.map(v => { const p = S(u, v), n = Nrm(u, v), t = T(v); return [V.madd(V.madd(p, t, -RIB_W), n, STEP * 0.45), V.madd(p, n, RIB_H), V.madd(V.madd(p, t, RIB_W), n, STEP * 0.45)]; });
      for (let q = 0; q < st.length - 1; q++) {
        const A = pts[q], B = pts[q + 1], vm = (st[q] + st[q + 1]) / 2, n = Nrm(u, vm), t = T(vm);
        M.quad(A[0], B[0], B[1], A[1], V.sub(n, t), P.shade(pal.rib, 1.14));
        M.quad(A[1], B[1], B[2], A[2], V.add(n, t), P.shade(pal.rib, 0.78));
      }
      if (o.ends) {
        const v = st[0], n = Nrm(u, 0), t = T(0), f = V.norm(V.sub(S(u, v), S(u, v + 0.08)));
        const w = V.norm(V.cross(f, t)), c = V.madd(V.madd(S(u, v), n, RIB_H * 0.45), f, 0.012);
        const pg = []; for (let q = 0; q < 5; q++) { const a = Math.PI / 2 + q * 2 * Math.PI / 5; pg.push(V.add(c, V.add(V.mul(t, Math.cos(a) * RIB_W * 1.17), V.mul(w, Math.sin(a) * RIB_W * 1.17)))); }
        for (let q = 1; q < 4; q++) M.triangleFacing(pg[0], pg[q], pg[q + 1], f, pal.end);
      }
    }
    const d = [0, -(o.fasciaH || 0.2), 0], fo = [out[0], 0, out[2]];
    for (let j = 0; j < o.cols && (o.fascia || o.soffit); j++) {
      const u0 = j / o.cols, u1 = (j + 1) / o.cols, e0 = S(u0, 0), e1 = S(u1, 0);
      if (o.fascia) M.quad(e0, e1, V.add(e1, d), V.add(e0, d), fo, toC(o.fascia));
      if (o.soffit) { const d2 = [0, -(o.soffitDrop || 0.45), 0], b0 = S(u0, o.soffitV || 1), b1 = S(u1, o.soffitV || 1); M.quad(V.add(e0, d), V.add(e1, d), V.add(b1, d2), V.add(b0, d2), V.neg(out), toC(o.soffit)); }
    }
  }
  /** 鸱吻: a curl rising from a ridge end and rolling back over it (a flattened five-sided tube); s = ±1 (which end). */
  function ridgeCurl(M, x, y, z, s, k, col) {
    const pts = [[x + s * 0.06 * k, y + 0.05 * k], [x + s * 0.2 * k, y + 0.42 * k], [x + s * 0.12 * k, y + 0.8 * k], [x - s * 0.1 * k, y + 0.92 * k], [x - s * 0.24 * k, y + 0.74 * k]];
    const rs = [0.17, 0.15, 0.12, 0.09, 0.035].map(r => r * k);
    const rings = pts.map(([px, py], i) => {
      const a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)], t = V.norm([b[0] - a[0], b[1] - a[1], 0]), nx = [-t[1], t[0], 0];
      const ring = []; for (let j = 0; j < 5; j++) { const an = j * Math.PI * 2 / 5; ring.push(V.add([px, py, z], V.add(V.mul(nx, Math.cos(an) * rs[i]), [0, 0, Math.sin(an) * rs[i] * 0.75]))); }
      return ring;
    });
    M.color = col;
    M.loft(rings, true, true);
  }
  /**
   * Han tile roof (歇山 xieshan / 庑殿 hip / 悬山 gable / 攒尖 pyramid) over a w × d wall top at height y (ridge along X;
   * rotate the builder for a ridge along Z). opts: { style: 'hip', w, d, y, over: 0.9 (overhang), rise: ≈ 0.55 d, lift: 0.4
   * (corner upturn 起翘), sag (举折), detail: 'high' | 'mid' | 'low', palette: 'slate' | 'grey' | 'green' | 'gold' |
   * 'terracotta' | { tile, tileAlt, rib, end, ridge, soffit }, fascia: vermilion | null, soffit: true, ornaments: true
   * (鸱吻 ridge curls, hip-ridge beasts, pyramid finial), gableColor (悬山 gables), cx, cz }.
   * Returns { ridgeY, eaveY, W, D } (eave half-extents).
   */
  function hanRoof(M, o) {
    o = Object.assign({ style: 'hip', w: 4, d: 3, y: 3, over: 0.9, lift: 0.4, detail: 'mid', palette: 'slate', fascia: PAL.hanRed, soffit: true, ornaments: true, cx: 0, cz: 0 }, o);
    const pal = typeof o.palette === 'string' ? ROOF_PAL[o.palette] || ROOF_PAL.slate : Object.assign({}, ROOF_PAL.slate, o.palette);
    const det = o.detail, hi = det === 'high', lo = det === 'low';
    const W = o.w / 2 + o.over, D = o.d / 2 + o.over, rise = o.rise != null ? o.rise : (o.d / 2 + o.over) * 1.05, yE = o.y, yR = yE + rise;
    const up = o.style === 'gable' ? o.lift * 0.35 : o.lift, sag = o.sag != null ? o.sag : rise * 0.11;
    const cx = o.cx, cz = o.cz, K = clamp(Math.min(o.w, o.d) / 4.2, 0.4, 1);
    const liftC = c => up * c * c * c;
    const eave = (u, side) => {
      const c = Math.abs(u * 2 - 1);
      if (side === 'f' || side === 'b') { const f = side === 'f' ? 1 : -1; return [cx + lerp(-W, W, u) * (1 + 0.04 * c * c * c), yE + liftC(c), cz + f * D * (1 + 0.06 * c * c * c)]; }
      const f = side === 'r' ? 1 : -1; return [cx + f * W * (1 + 0.04 * c * c * c), yE + liftC(c), cz + lerp(-D, D, u) * (1 + 0.06 * c * c * c)];
    };
    const OUT = { f: [0, 1, 1], b: [0, 1, -1], r: [1, 1, 0], l: [-1, 1, 0] };
    const sagged = (p, v) => [p[0], p[1] - sag * 4 * v * (1 - v), p[2]];
    const courses = hi ? [0, 0.34, 0.68, 1] : lo ? [0, 1] : [0, 0.5, 1];
    const step = lo ? 0 : 0.045;
    const ribEvery = hi ? 0.46 : det === 'mid' ? 0.8 : 0;
    const colsF = hi ? 8 : lo ? 2 : 4, colsS = hi ? 3 : lo ? 1 : 2;
    const ribsOn = len => (ribEvery ? Math.max(2, Math.round(len / ribEvery)) : 0);
    const slope = (S, side, len, cols, extra) => tiledSlope(M, S, Object.assign({ out: OUT[side], cols, courses, ribs: ribsOn(len), ribV: hi ? [-0.015, 0.5, 1] : [-0.015, 1], ends: hi, fascia: o.fascia, soffit: o.soffit && !lo ? pal.soffit : null, pal, step }, extra || {}));
    const res = { ridgeY: yR, eaveY: yE, W, D };
    const hipRidges = (corners, tops) => {
      // hip ridges along the slope edges from each eave corner to the ridge end, an upturned tip and a small beast
      M.color = pal.ridge;
      corners.forEach((c, i) => {
        const t = tops[i], mid = sagged(V.lerp(c, t, 0.5), 0.5), a = V.add(c, [0, 0.09, 0]), m = V.add(mid, [0, 0.09, 0]), b = V.add(t, [0, 0.09, 0]);
        const wR = (lo ? 0.12 : 0.16) * Math.max(0.6, K);
        M.beam(a, m, wR, wR); M.beam(m, b, wR, wR);
        if (!lo && o.ornaments) {
          const dir = V.norm(V.sub(a, m));
          M.spike(V.sub(a, V.mul(dir, 0.05)), V.add(V.mul(dir, 0.55), [0, 0.85, 0]), 0.09 * K, 0.45 * K, 4);
          if (hi) { M.color = P.shade(pal.ridge, 1.15); M.sphere(V.add(V.lerp(a, m, 0.35), [0, 0.12, 0]), [0.07, 0.1, 0.07], 4, 2, false); M.color = pal.ridge; }
        }
      });
    };
    const mainRidge = (x0, x1, y) => {
      M.color = pal.ridge;
      M.box([cx + (x0 + x1) / 2, y + 0.1 * K, cz], [x1 - x0 + 0.3 * K, (lo ? 0.22 : 0.3) * K, (lo ? 0.24 : 0.32) * K], P.shade(pal.ridge, 1.12), 'tfkrl');
      if (!lo) { M.color = P.shade(pal.ridge, 1.06); M.box([cx + (x0 + x1) / 2, y + 0.31 * K, cz], [x1 - x0, 0.12 * K, 0.2 * K], P.shade(pal.ridge, 1.25), 'tfkrl'); }
      if (o.ornaments && !lo) for (const s of [-1, 1]) ridgeCurl(M, cx + (s > 0 ? x1 : x0), y, cz, s, (hi ? 1 : 0.75) * K, pal.ridge);
    };
    if (o.style === 'pyramid') {
      const apex = [cx, yR, cz];
      const R = side => (u, v) => sagged(V.lerp(eave(u, side), apex, v), v);
      for (const sd of ['f', 'b', 'r', 'l']) slope(R(sd), sd, sd === 'f' || sd === 'b' ? 2 * W : 2 * D, sd === 'f' || sd === 'b' ? colsF : colsF, { ribV: [-0.015, 0.6] });
      hipRidges([eave(0, 'f'), eave(1, 'f'), eave(0, 'b'), eave(1, 'b')], [apex, apex, apex, apex]);
      if (o.ornaments) {
        M.color = toC(o.finial) || PAL.gold;
        M.push().translate(cx, 0, cz);
        M.lathe([[0.22, yR - 0.05], [0.26, yR + 0.12], [0.12, yR + 0.3], [0.17, yR + 0.42], [0.08, yR + 0.62], [0, yR + 0.72]].map(([r, y]) => [r * (o.finialK || 1), y]), 6, false, true, false);
        M.pop();
      }
      return res;
    }
    if (o.style === 'gable') {
      const ridge = u => [cx + lerp(-W, W, u), yR, cz];
      for (const sd of ['f', 'b']) slope((u, v) => sagged(V.lerp(eave(u, sd), ridge(u), v), v), sd, 2 * W, colsF);
      // the gables (山墙) under the roof ends and the barge boards along them
      const gc = toC(o.gableColor) || PAL.plaster, g = o.w / 2;
      M.color = gc;
      for (const s of [-1, 1]) M.triangleFacing([cx + s * g, yE - 0.02, cz - o.d / 2], [cx + s * g, yR - 0.15, cz], [cx + s * g, yE - 0.02, cz + o.d / 2], [s, 0, 0]);
      M.color = toC(o.fascia) || pal.ridge;
      for (const s of [-1, 1]) for (const f of [-1, 1]) {
        const a = eave(s > 0 ? 1 : 0, f > 0 ? 'f' : 'b'), m = sagged(V.lerp(a, [cx + s * W, yR, cz], 0.5), 0.5), b = [cx + s * W, yR, cz];
        M.beam(V.add(a, [0, -0.06, 0]), V.add(m, [0, -0.06, 0]), 0.1, 0.16, [s, 1, 0]); M.beam(V.add(m, [0, -0.06, 0]), V.add(b, [0, -0.06, 0]), 0.1, 0.16, [s, 1, 0]);
      }
      mainRidge(-W, W, yR);
      return res;
    }
    if (o.style === 'xieshan') {
      // lower hip slopes up to a break line, then a steeper gable roof (the pilot gate tower's roof)
      const yB = yE + rise * 0.46, Lb = Math.max(0.3, W - 1.45 - (o.over - 0.9)), Db = Math.max(0.25, D - 1.35 - (o.over - 0.9)), yR2 = yB + rise * 0.54 + 0.2;
      const brk = (u, side) => (side === 'f' || side === 'b' ? [cx + lerp(-Lb, Lb, u), yB, cz + (side === 'f' ? Db : -Db)] : [cx + (side === 'r' ? Lb : -Lb), yB, cz + lerp(-Db, Db, u)]);
      const lowS = side => (u, v) => V.add(V.lerp(eave(u, side), brk(u, side), v), [0, -sag * 4 * v * (1 - v), 0]);
      const nr = ribsOn(2 * W);
      slope(lowS('f'), 'f', 2 * W, colsF);
      slope(lowS('b'), 'b', 2 * W, colsF, { ribs: 0, ends: false, soffit: null });
      for (const sd of ['l', 'r']) slope(lowS(sd), sd, 2 * D, colsS);
      const gx = Lb + 0.25;
      const upS = f => (u, v) => V.lerp([cx + lerp(-gx, gx, u), yB - 0.05, cz + f * (Db + 0.15)], [cx + lerp(-gx, gx, u), yR2, cz], v);
      const ribU = nr ? Array.from({ length: nr }, (_, i) => (lerp(-Lb, Lb, (i + 0.5) / nr) + gx) / (2 * gx)).filter(u => u > 0.02 && u < 0.98) : null;
      tiledSlope(M, upS(1), { out: [0, 1, 1], cols: Math.max(2, colsF - 2), courses: lo ? [0, 1] : [0, 0.5, 1], ribU, ribV: [0, 1], pal, step });
      tiledSlope(M, upS(-1), { out: [0, 1, -1], cols: Math.max(2, colsF - 4), courses: lo ? [0, 1] : [0, 0.5, 1], pal, step });
      for (const s of [-1, 1]) {
        M.color = toC(o.fascia) || pal.ridge;
        M.triangleFacing([cx + s * Lb, yB - 0.05, cz - Db], [cx + s * Lb, yR2 - 0.08, cz], [cx + s * Lb, yB - 0.05, cz + Db], [s, 0, 0]);
        if (!lo) { M.color = PAL.gold; M.triangleFacing([cx + s * (Lb + 0.02), yB + 0.25, cz - 0.3], [cx + s * (Lb + 0.02), yB + 0.6, cz], [cx + s * (Lb + 0.02), yB + 0.25, cz + 0.3], [s, 0, 0]); }
        M.color = pal.ridge;
        for (const f of [1, -1]) M.beam([cx + s * gx, yB - 0.02, cz + f * (Db + 0.2)], [cx + s * gx, yR2 + 0.08, cz], 0.16, 0.16, [s, 1, 0]);
      }
      mainRidge(-gx, gx, yR2);
      const corners = [eave(0, 'f'), eave(1, 'f'), eave(0, 'b'), eave(1, 'b')], tops = [brk(0, 'f'), brk(1, 'f'), brk(0, 'b'), brk(1, 'b')];
      M.color = pal.ridge;
      corners.forEach((c, i) => {
        const t = tops[i], sx = i % 2 ? 1 : -1, sz = i < 2 ? 1 : -1, S = lowS(i < 2 ? 'f' : 'b'), u = i % 2;
        const hp = [1, 0.5, 0].map(v => V.add(S(u, v), [0, 0.09, 0]));
        M.beam(hp[0], hp[1], 0.17, 0.16); M.beam(hp[1], hp[2], 0.17, 0.16);
        if (!lo && o.ornaments) { const dir = V.norm(V.sub(hp[2], hp[1])); M.spike(V.sub(hp[2], V.mul(dir, 0.05)), V.add(V.mul(dir, 0.55), [0, 0.85, 0]), 0.095, 0.5, 4); }
        if (hi && o.ornaments) { M.color = P.shade(pal.ridge, 1.15); M.sphere(V.add(S(u, 0.2), [sx * 0.01, 0.22, sz * 0.01]), [0.07, 0.1, 0.07], 4, 2, false); M.color = pal.ridge; }
      });
      res.ridgeY = yR2;
      return res;
    }
    // hip (庑殿)
    const Lr = Math.max(0.05, W - D);
    const ridgeF = u => [cx + lerp(-Lr, Lr, u), yR, cz];
    for (const sd of ['f', 'b']) slope((u, v) => sagged(V.lerp(eave(u, sd), ridgeF(u), v), v), sd, 2 * W, colsF);
    slope((u, v) => sagged(V.lerp(eave(u, 'r'), [cx + Lr, yR, cz], v), v), 'r', 2 * D, colsS, { ribV: [-0.015, 0.55] });
    slope((u, v) => sagged(V.lerp(eave(u, 'l'), [cx - Lr, yR, cz], v), v), 'l', 2 * D, colsS, { ribV: [-0.015, 0.55] });
    hipRidges([eave(0, 'f'), eave(1, 'f'), eave(0, 'b'), eave(1, 'b')], [[cx - Lr, yR, cz], [cx + Lr, yR, cz], [cx - Lr, yR, cz], [cx + Lr, yR, cz]]);
    mainRidge(-Lr, Lr, yR);
    return res;
  }
  /**
   * Thatch roof (Lanternvale farmhouse): thick straw courses with ragged eaves over a w × d wall top at y, gable (ridge
   * along X) or hip; a rounded ridge roll. opts: { w, d, y, over: 0.5, rise, style: 'gable' | 'hip', color, cx, cz }.
   */
  function thatchRoof(M, o) {
    o = Object.assign({ w: 4, d: 3, y: 2.6, over: 0.5, style: 'gable', color: '#C8A868', cx: 0, cz: 0 }, o);
    const straw = toC(o.color), dark = P.shade(straw, 0.72), W = o.w / 2 + o.over, D = o.d / 2 + o.over, rise = o.rise != null ? o.rise : o.d * 0.62, yR = o.y + rise;
    const cx = o.cx, cz = o.cz, TH = 0.22, hip = o.style === 'hip', Lr = hip ? Math.max(0.1, W - D * 0.8) : W;
    const courses = 4;
    // a gently bulging surface (the straw is thick in the middle of a slope) laid in overlapping courses
    const bul = (u, v) => 0.16 * Math.sin(Math.PI * u) * Math.sin(Math.PI * Math.min(1, v));
    for (const f of [1, -1]) {
      for (let k = 0; k < courses; k++) {
        const v0 = k / courses, v1 = (k + 1) / courses + 0.05;
        const P0 = u => [cx + lerp(-W, W, u) * lerp(1, Lr / W, v0), lerp(o.y, yR, v0) - (k ? 0 : 0.05) + bul(u, v0), cz + f * (lerp(D, 0, v0) + bul(u, v0) * 0.6)];
        const P1 = u => { const v = Math.min(1, v1); return [cx + lerp(-W, W, u) * lerp(1, Lr / W, v), lerp(o.y, yR, v) + bul(u, v), cz + f * (lerp(D, 0, v) + bul(u, v) * 0.6)]; };
        const col = P.shade(straw, 0.88 + 0.06 * k + (k % 2 ? 0.05 : 0)), n = 7;
        for (let j = 0; j < n; j++) {
          const u0 = j / n, u1 = (j + 1) / n, ra = 0.07 * hashN(j, k, 3), rb = 0.07 * hashN(j + 1, k, 3);
          const a = V.add(P0(u0), [0, -ra, f * ra]), b = V.add(P0(u1), [0, -rb, f * rb]), c = P1(u1), d = P1(u0);
          M.quad(a, b, c, d, [0, 1, f], P.shade(col, 0.96 + 0.08 * hashN(j, k, 9)));
          // the course's thick ragged foot
          M.quad(V.add(a, [0, -TH, 0]), V.add(b, [0, -TH, 0]), b, a, [0, -0.2, f], P.shade(dark, 0.95 + 0.1 * hashN(j, k, 5)));
        }
      }
    }
    if (hip) {
      for (const s of [-1, 1]) {
        const e0 = [cx + s * W, o.y - 0.05, cz - D], e1 = [cx + s * W, o.y - 0.05, cz + D], top = [cx + s * Lr, yR, cz];
        M.triangleFacing(e0, e1, top, [s, 0.6, 0], P.shade(straw, 0.95));
        M.quad(V.add(e0, [0, -TH, 0]), V.add(e1, [0, -TH, 0]), e1, e0, [s, -0.2, 0], dark);
      }
    } else {
      M.color = toC(o.gableColor) || PAL.plasterWarm;
      for (const s of [-1, 1]) M.triangleFacing([cx + s * o.w / 2, o.y - 0.02, cz - o.d / 2], [cx + s * o.w / 2, yR - 0.25, cz], [cx + s * o.w / 2, o.y - 0.02, cz + o.d / 2], [s, 0, 0]);
      for (const s of [-1, 1]) { M.color = dark; M.quad([cx + s * W, o.y - 0.05 - TH, cz + D], [cx + s * W, yR - TH * 0.5, cz], [cx + s * W, yR + 0.02, cz], [cx + s * W, o.y - 0.05, cz + D], [s, 0, 0]); M.quad([cx + s * W, o.y - 0.05 - TH, cz - D], [cx + s * W, o.y - 0.05, cz - D], [cx + s * W, yR + 0.02, cz], [cx + s * W, yR - TH * 0.5, cz], [s, 0, 0]); }
    }
    // the ridge roll and its ties
    M.color = P.shade(straw, 0.85);
    M.push().translate(cx, yR + 0.02, cz).rotate(0, 0, 90);
    M.lathe([[0.2, -Lr - 0.15], [0.22, -Lr + 0.2], [0.22, Lr - 0.2], [0.2, Lr + 0.15]], 6, false, true, true);
    M.pop();
    M.color = WOOD.dark;
    for (let i = 0; i < 4; i++) { const x = cx + lerp(-Lr * 0.8, Lr * 0.8, i / 3); M.band([x, yR + 0.02, cz], 0.235, 0.235, -0.04, 0.04, 6); }
    return { ridgeY: yR, eaveY: o.y, W, D };
  }
  /** Terracotta roof (Mediterranean): a low-pitched gable or hip of barrel tiles in warm orange, no upturn. */
  function terracottaRoof(M, o) {
    return hanRoof(M, Object.assign({ style: 'hip', over: 0.45, lift: 0, palette: 'terracotta', fascia: H('#E8DCC4'), soffit: false, ornaments: false, detail: 'mid' }, o, { rise: o.rise != null ? o.rise : o.d * 0.32, sag: 0.02 }));
  }
  /**
   * Flat mud roof (desert / Persian / Indian houses): a roof slab with a parapet (rounded top), protruding beam ends (vigas)
   * under it and a pair of drain spouts. opts: { w, d, y, parapet: 0.45, color, beams: true, cx, cz }.
   */
  function flatRoof(M, o) {
    o = Object.assign({ w: 4, d: 3, y: 2.6, parapet: 0.45, color: '#D8B88A', beams: true, cx: 0, cz: 0 }, o);
    const c = toC(o.color), cd = P.shade(c, 0.86), W = o.w / 2, D = o.d / 2, y = o.y, cx = o.cx, cz = o.cz, t = 0.18;
    M.color = cd; M.box([cx, y + 0.06, cz], [o.w - 0.1, 0.12, o.d - 0.1], P.shade(c, 0.9), 't');
    M.color = c;
    M.box([cx, y + o.parapet / 2, cz + D - t / 2], [o.w, o.parapet, t], P.shade(c, 1.06), 'tfk');
    M.box([cx, y + o.parapet / 2, cz - D + t / 2], [o.w, o.parapet, t], P.shade(c, 1.06), 'tfk');
    for (const s of [-1, 1]) M.box([cx + s * (W - t / 2), y + o.parapet / 2, cz], [t, o.parapet, o.d - 2 * t], P.shade(c, 1.06), s > 0 ? 'trl' : 'trl');
    if (o.beams) { M.color = WOOD.dark; const n = Math.max(3, Math.round(o.w / 0.7)); for (let i = 0; i < n; i++) { const x = cx + lerp(-W + 0.35, W - 0.35, i / (n - 1)); M.segment([x, y - 0.12, cz + D - 0.05], [x, y - 0.12, cz + D + 0.28], 0.07, 0.065, 5, false, true); } }
    M.color = WOOD.mid;
    for (const s of [-1, 1]) M.box([cx + s * W * 0.5, y + 0.12, cz + D + 0.2], [0.12, 0.1, 0.4], null, 'tfrl');
    return { y: y + o.parapet };
  }
  /** Dome on a drum: opts { c: [x,y,z] (drum base), r, drumH: 0.5, shape: 'hemi' | 'onion' | 'low', color, drum, finial, sides: 12 }. */
  function dome(M, o) {
    o = Object.assign({ c: [0, 0, 0], r: 2, drumH: 0.5, shape: 'hemi', color: '#E8E0D0', drum: '#D8CBB4', finial: '#E3B34C', sides: 12 }, o);
    const [x, y, z] = o.c, r = o.r, col = toC(o.color);
    M.push().translate(x, y, z);
    if (o.drumH > 0) { M.color = toC(o.drum); M.cylinder([0, 0, 0], r * 1.04, r * 1.04, o.drumH, o.sides, false, false, false); M.color = P.shade(toC(o.drum), 0.85); M.band([0, 0, 0], r * 1.08, r * 1.08, o.drumH - 0.08, o.drumH, o.sides); }
    const prof = [];
    if (o.shape === 'onion') for (const [rr, yy] of [[1.0, 0], [1.14, 0.3], [1.12, 0.62], [0.88, 0.95], [0.5, 1.25], [0.2, 1.48], [0, 1.6]]) prof.push([rr * r, o.drumH + yy * r]);
    else if (o.shape === 'low') for (let i = 0; i <= 4; i++) { const a = i / 4 * Math.PI / 2; prof.push([Math.cos(a) * r, o.drumH + Math.sin(a) * r * 0.6]); }
    else for (let i = 0; i <= 5; i++) { const a = i / 5 * Math.PI / 2; prof.push([Math.cos(a) * r, o.drumH + Math.sin(a) * r]); }
    prof[prof.length - 1][0] = 0;
    M.color = col;
    M.lathe(prof, o.sides, true, false, false, prof.map((p, i) => P.shade(col, 0.92 + 0.1 * i / prof.length)));
    const top = prof[prof.length - 1][1];
    if (o.finial) { M.color = toC(o.finial); M.lathe([[0.12, top - 0.05], [0.1, top + 0.15], [0.16, top + 0.25], [0.04, top + 0.4], [0.07, top + 0.48], [0, top + 0.62]].map(([a, b]) => [a * Math.max(1, r / 2), b]), 6, false, true, false); }
    M.pop();
    return { top: y + top };
  }
  /**
   * A wall run along X (length len, centred), thickness thick, height h, of a kind: 'rammed' (版筑 courses), 'brick',
   * 'stone' (ashlar), 'mud' (adobe, rounded top), 'plaster' (white-wash on a stone footing); batter (收分); merlons (垛口)
   * on top (true / count). opts: { len, h, thick, kind, batter: 0, merlons: false, faces: 'fk' (front / back) }.
   */
  function wall(M, o) {
    o = Object.assign({ len: 6, h: 3, thick: 0.8, kind: 'rammed', batter: 0, merlons: false }, o);
    const L = o.len / 2, T = o.thick / 2, h = o.h, bat = o.batter;
    const z = y => T - bat * y;
    const keepJ = M.jitterAll; M.jitterAll = true;
    const pal = {
      rammed: [PAL.rammed, P.mix(PAL.rammed, PAL.rammedDark, 0.35)], brick: [H('#9C9A94'), H('#8C8A84')], stone: [H('#B8B0A0'), H('#A8A090')],
      mud: [H('#C8A47A'), H('#BC9870')], plaster: [H('#EEE6D6'), H('#E4DACA')],
    }[o.kind] || [PAL.rammed, PAL.rammedDark];
    const rows = o.kind === 'brick' ? Math.round(h / 0.35) : o.kind === 'stone' ? Math.round(h / 0.5) : o.kind === 'rammed' ? Math.round(h / 0.55) : 2;
    const foot = o.kind === 'plaster' || o.kind === 'mud' ? 0.4 : 0;
    if (foot) { M.color = PAL.stoneDark; for (const f of [1, -1]) M.quad([-L, 0, f * (z(0) + 0.04)], [L, 0, f * (z(0) + 0.04)], [L, foot, f * (z(foot) + 0.04)], [-L, foot, f * (z(foot) + 0.04)], [0, bat, f], P.shade(PAL.stone, 0.92)); }
    for (let r = 0; r < rows; r++) {
      const y0 = lerp(foot, h, r / rows), y1 = lerp(foot, h, (r + 1) / rows);
      const segs = o.kind === 'stone' || o.kind === 'brick' ? Math.max(2, Math.round(o.len / (o.kind === 'stone' ? 1.1 : 1.6))) : Math.max(1, Math.round(o.len / 2.4));
      for (let k = 0; k < segs; k++) {
        const off = (r % 2 ? 0.5 : 0) / segs;
        const x0 = lerp(-L, L, clamp01(k / segs + off - (r % 2 ? 0.5 / segs : 0))), x1 = lerp(-L, L, clamp01((k + 1) / segs + off - (r % 2 ? 0.5 / segs : 0)));
        if (x1 - x0 < 0.05) continue;
        const col = P.shade(pal[(r + k) % 2], 0.94 + 0.12 * (hashN(k, r, 7) * 0.5 + 0.5) - (o.kind === 'rammed' && r === 0 ? 0.08 : 0));
        for (const f of [1, -1]) if (!o.faces || o.faces.includes(f > 0 ? 'f' : 'k')) M.quad([x0, y0, f * z(y0)], [x1, y0, f * z(y0)], [x1, y1, f * z(y1)], [x0, y1, f * z(y1)], [0, bat, f], col);
      }
      if (o.kind !== 'mud' && o.kind !== 'plaster' && r > 0) { M.color = P.shade(pal[0], 0.72); for (const f of [1, -1]) if (!o.faces || o.faces.includes(f > 0 ? 'f' : 'k')) M.quad([-L, y0 - 0.015, f * (z(y0) + 0.004)], [L, y0 - 0.015, f * (z(y0) + 0.004)], [L, y0 + 0.015, f * (z(y0) + 0.004)], [-L, y0 + 0.015, f * (z(y0) + 0.004)], [0, bat, f]); }
    }
    // ends and top
    M.color = pal[1];
    for (const s of [-1, 1]) M.quad([s * L, 0, -z(0)], [s * L, 0, z(0)], [s * L, h, z(h)], [s * L, h, -z(h)], [s, 0, 0]);
    if (o.kind === 'mud') { M.color = P.shade(pal[0], 1.05); M.push().translate(0, h, 0).rotate(0, 0, 90); M.lathe([[z(h), -L], [z(h), L]], 6, false, true, true); M.pop(); }
    else { M.color = P.shade(pal[0], 1.04); M.quad([-L, h, -z(h)], [L, h, -z(h)], [L, h, z(h)], [-L, h, z(h)], [0, 1, 0]); }
    if (o.merlons) {
      const n = o.merlons === true ? Math.max(2, Math.round(o.len / 1.2)) : o.merlons, mw = o.len / n * 0.55;
      for (let i = 0; i < n; i++) { const x = lerp(-L + o.len / n / 2, L - o.len / n / 2, n === 1 ? 0.5 : i / (n - 1)); M.color = P.shade(pal[i % 2], 0.96 + 0.08 * hashN(i, 1, 5)); M.box([x, h + 0.25, z(h) - 0.15], [mw, 0.5, 0.3], null, 'tfkrl'); }
    }
    M.jitterAll = keepJ;
  }
  /**
   * Gate / door opening drawn on a wall face at z (facing +Z): 'arch' (券门: brick voussoirs, dark tunnel, two studded
   * leaves) or 'square' (a timber frame, lintel, two leaves). opts: { w, h, z, kind, door: colour, studs: true, frame }.
   */
  function gate(M, o) {
    o = Object.assign({ w: 2, h: 2.6, z: 0.5, kind: 'arch', door: '#A83E2E', studs: true, frame: '#3E2A22' }, o);
    const hw = o.w / 2, z = o.z, red = toC(o.door), frame = toC(o.frame);
    const ys = o.kind === 'arch' ? o.h - hw : o.h;
    M.color = H('#3A2E28');
    M.quad([-hw, 0, z + 0.004], [hw, 0, z + 0.004], [hw, ys, z + 0.004], [-hw, ys, z + 0.004], [0, 0, 1]);
    if (o.kind === 'arch') for (let i = 0; i < 6; i++) { const a0 = i / 6 * Math.PI, a1 = (i + 1) / 6 * Math.PI; M.triangleFacing([0, ys, z + 0.004], [Math.cos(a0) * hw, ys + Math.sin(a0) * hw, z + 0.004], [Math.cos(a1) * hw, ys + Math.sin(a1) * hw, z + 0.004], [0, 0, 1]); }
    // leaves
    for (const s of [-1, 1]) {
      const pts = [[s * 0.02, 0], [s * (hw - 0.1), 0], [s * (hw - 0.1), ys]];
      if (o.kind === 'arch') for (let i = 1; i <= 4; i++) { const a = i / 4 * Math.PI / 2; pts.push([s * Math.max(0.02, Math.cos(a) * (hw - 0.1)), ys + Math.sin(a) * (hw - 0.1)]); }
      else pts.push([s * 0.02, ys]);
      if (s < 0) pts.reverse();
      M.color = red; M.push().translate(0, 0, z + 0.03); M.flat(pts, 0.05); M.pop();
      if (o.studs) { M.color = PAL.gold; const nr = Math.max(3, Math.round(ys / 0.45)), nc = Math.max(2, Math.round(hw / 0.32)); for (let r = 0; r < nr; r++) for (let c = 0; c < nc; c++) { const p = [s * lerp(0.2, hw - 0.3, nc > 1 ? c / (nc - 1) : 0.5), lerp(0.35, ys - 0.15, r / (nr - 1)), z + 0.062]; M.quad([p[0] - 0.03, p[1] - 0.03, p[2]], [p[0] + 0.03, p[1] - 0.03, p[2]], [p[0] + 0.03, p[1] + 0.03, p[2]], [p[0] - 0.03, p[1] + 0.03, p[2]], [0, 0, 1]); } }
    }
    M.color = frame;
    if (o.kind === 'arch') {
      for (let i = 0; i < 7; i++) { const a = (i + 0.5) / 7 * Math.PI, r = hw + 0.17, q = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, 0, a - Math.PI / 2)); M.color = i === 3 ? PAL.stoneLight : i % 2 ? PAL.brick : PAL.brickDark; M.push().translate(Math.cos(a) * r, ys + Math.sin(a) * r, z + 0.03).rotateQ(q); M.box([0, 0, 0], [0.32, 0.34, 0.08], null, 'tbfrl'); M.pop(); }
      M.color = PAL.brickDark; for (const s of [-1, 1]) M.box([s * (hw + 0.16), ys / 2, z + 0.02], [0.3, ys, 0.06], null, 'tfrl');
    } else {
      for (const s of [-1, 1]) M.box([s * (hw + 0.06), o.h / 2, z + 0.06], [0.14, o.h, 0.14], null, 'fkrl');
      M.box([0, o.h + 0.08, z + 0.07], [o.w + 0.5, 0.16, 0.16], null, 'tbfkrl');
    }
    M.color = P.shade(PAL.stone, 0.92); M.box([0, 0.06, z + 0.12], [o.w + 0.2, 0.12, 0.26], null, 'tfkrl');
  }
  /** Lattice window (格窗) on a wall face at z (facing +Z), centred on (x, y): paper glows at night (lit 'night'). */
  function windowLattice(M, o) {
    o = Object.assign({ x: 0, y: 1.6, z: 0.5, w: 0.9, h: 0.8, frame: '#5A3426', paper: '#E9D7AE', lit: 'night', bars: 2, round: false }, o);
    const { x, y, z, w, h } = o;
    M.color = toC(o.paper); M.nightEmission = o.lit === 'night' ? 0.85 : 0; M.emission = o.lit === true ? 0.85 : 0;
    if (o.round) M.disc([x, y, z + 0.01], w / 2, 10); else M.quad([x - w / 2, y - h / 2, z + 0.008], [x + w / 2, y - h / 2, z + 0.008], [x + w / 2, y + h / 2, z + 0.008], [x - w / 2, y + h / 2, z + 0.008], [0, 0, 1]);
    M.nightEmission = 0; M.emission = 0;
    M.color = toC(o.frame);
    M.box([x, y + h / 2 + 0.03, z + 0.03], [w + 0.12, 0.06, 0.06], null, 'tfb'); M.box([x, y - h / 2 - 0.03, z + 0.03], [w + 0.16, 0.07, 0.08], null, 'tfb');
    for (const s of [-1, 1]) M.box([x + s * (w / 2 + 0.03), y, z + 0.03], [0.06, h, 0.06], null, 'frl');
    for (let i = 1; i <= o.bars; i++) M.box([x - w / 2 + w * i / (o.bars + 1), y, z + 0.025], [0.035, h, 0.03], null, 'f');
    M.box([x, y, z + 0.025], [w, 0.035, 0.03], null, 'f');
  }
  /** Plank door with a frame at (x, z), facing +Z. */
  function door(M, o) {
    o = Object.assign({ x: 0, z: 0.5, w: 0.95, h: 1.9, color: '#7A4E36', frame: '#4A3428' }, o);
    const { x, z, w, h } = o, c = toC(o.color);
    M.color = c; M.box([x, h / 2, z + 0.02], [w, h, 0.05], null, 'f');
    M.color = P.shade(c, 0.78); for (let i = 1; i < 3; i++) M.box([x - w / 2 + w * i / 3, h / 2, z + 0.048], [0.025, h * 0.96, 0.01], null, 'f');
    M.color = toC(o.frame);
    for (const s of [-1, 1]) M.box([x + s * (w / 2 + 0.05), h / 2, z + 0.04], [0.1, h + 0.05, 0.08], null, 'frl');
    M.box([x, h + 0.06, z + 0.04], [w + 0.3, 0.12, 0.1], null, 'tbfrl');
    M.color = PAL.stone; M.box([x, 0.05, z + 0.15], [w + 0.3, 0.1, 0.3], null, 'tfkrl');
  }
  /** Stone plinth course (勒脚) of irregular blocks along X from x0 to x1 at front z (+Z), height h. */
  function plinth(M, x0, x1, z, h) {
    let x = x0;
    const greys = [H('#8E877C'), H('#A39C8E'), H('#7E776E')];
    while (x < x1 - 0.08) {
      let w = 0.75 + M.random01() * 0.7;
      if (x1 - (x + w) < 0.5) w = x1 - x;
      const hh = h * (0.9 + M.random01() * 0.14), d = 0.1 + M.random01() * 0.06;
      M.color = P.shade(greys[(M.random01() * 3) | 0], 0.94 + M.random01() * 0.12);
      M.box([x + w / 2, hh / 2, z + d / 2 - 0.03], [w - 0.035, hh, d + 0.06], null, 'tfrl');
      x += w;
    }
  }
  /** Merlons (垛口) at xs along X on a parapet top at y, front z, size [w, h, d], tone base. */
  function merlons(M, xs, y, z, size, base) {
    xs.forEach((x, i) => { M.color = P.shade(toC(base), 1 + (M.random01() * 2 - 1) * 0.08); M.box([x, y + size[1] / 2, z], size, P.shade(PAL.stoneLight, 0.96 + M.random01() * 0.08), 'tfkrl'); });
  }
  /** Round columns (vermilion by default) at the given [x, z] points from y0 to y1, on square stone bases. */
  function columns(M, pts, y0, y1, o) {
    o = Object.assign({ r: 0.16, color: PAL.hanRed, base: PAL.stone, sides: 6 }, o);
    for (const [x, z] of pts) { M.color = toC(o.base); M.box([x, y0 + 0.06, z], [o.r * 2.8, 0.12, o.r * 2.8], null, 'tfkrl'); M.color = toC(o.color); M.cylinder([x, y0 + 0.12, z], o.r, o.r * 0.9, y1 - y0 - 0.12, o.sides, false, false, false); }
  }
  /** Stupa (窣堵坡): square terraces, a drum with a railing, a white hemispherical dome, a harmika and a three-disc parasol. */
  function stupa(M, o) {
    o = Object.assign({ r: 3, color: '#ECE6D8', stone: '#C8BCA4', gold: '#E3B34C' }, o);
    const r = o.r, white = toC(o.color), stone = toC(o.stone), gold = toC(o.gold);
    M.color = stone; M.box([0, 0.3, 0], [r * 2.6, 0.6, r * 2.6], P.shade(stone, 1.06), 'tfkrl');
    M.box([0, 0.85, 0], [r * 2.2, 0.5, r * 2.2], P.shade(stone, 1.06), 'tfkrl');
    M.color = P.shade(white, 0.95); M.cylinder([0, 1.1, 0], r * 1.02, r * 1.02, 0.7, 14, false, false, true);
    M.color = P.shade(stone, 0.85);
    for (let i = 0; i < 18; i++) { const a = i / 18 * Math.PI * 2; M.box([Math.cos(a) * r * 1.06, 1.42, Math.sin(a) * r * 1.06], [0.08, 0.5, 0.08], null, 'tfkrl'); }
    M.band([0, 0, 0], r * 1.06, r * 1.06, 1.58, 1.66, 14);
    M.color = white;
    const prof = []; for (let i = 0; i <= 5; i++) { const a = i / 5 * Math.PI / 2; prof.push([Math.cos(a) * r, 1.8 + Math.sin(a) * r * 0.95]); } prof[5][0] = 0;
    M.lathe(prof, 14, true, false, false, prof.map((p, i) => P.shade(white, 0.92 + 0.1 * i / 5)));
    const top = 1.8 + r * 0.95;
    M.color = stone; M.box([0, top + 0.25, 0], [r * 0.5, 0.5, r * 0.5], P.shade(stone, 1.06), 'tfkrl');
    M.box([0, top + 0.55, 0], [r * 0.62, 0.12, r * 0.62], null, 'tbfkrl');
    M.color = gold; M.segment([0, top + 0.6, 0], [0, top + 2.2, 0], 0.06, 0.05, 5);
    for (let i = 0; i < 3; i++) { const y = top + 1.0 + i * 0.4, rr = r * (0.42 - i * 0.1); M.cylinder([0, y, 0], rr, rr * 0.85, 0.08, 10, false, true, true); }
    return { top: top + 2.2 };
  }
  Props.parts = { hanRoof, thatchRoof, terracottaRoof, flatRoof, dome, wall, gate, window: windowLattice, door, plinth, merlons, columns, stupa, tiledSlope, ridgeCurl, hangingCloth, ROOF_PAL };


  // ============================================================ vehicles ==
  /** A spoked wheel in the YZ plane (axle along X) centred on c: rim, hub, spokes. */
  function wheel(M, c, r, w, spokes, col) {
    M.push().translate(c).rotate(0, 0, 90);
    M.color = col;
    M.lathe([[r * 0.84, -w / 2], [r, -w / 2], [r, w / 2], [r * 0.84, w / 2], [r * 0.84, -w / 2]], 10, false, false, false);
    M.color = P.shade(col, 0.82);
    M.lathe([[0.001, -w * 0.9], [r * 0.17, -w * 0.9], [r * 0.17, w * 0.9], [0.001, w * 0.9]], 6, false, false, false);
    M.pop();
    M.color = P.shade(col, 0.94);
    for (let i = 0; i < spokes; i++) { const a = (i / spokes) * Math.PI * 2; M.beam([c[0], c[1] + Math.cos(a) * r * 0.15, c[2] + Math.sin(a) * r * 0.15], [c[0], c[1] + Math.cos(a) * r * 0.86, c[2] + Math.sin(a) * r * 0.86], w * 0.45, w * 0.45, [1, 0, 0]); }
  }
  Props.helpers.wheel = wheel;
  /** A slumped grain sack (lathe) at c. */
  function sack(M, c, r, h, yaw, tilt, col) {
    M.color = col;
    M.push().translate(c).rotate(tilt, yaw, 0);
    M.lathe([[r * 0.85, 0], [r, h * 0.12], [r * 1.02, h * 0.4], [r * 0.85, h * 0.72], [r * 0.32, h * 0.9], [r * 0.2, h * 0.94], [r * 0.3, h * 1.04], [0, h * 1.06]], 7, true, false, false,
      [P.shade(col, 0.88), col, P.shade(col, 1.05), col, P.shade(col, 0.95), P.shade(col, 0.8), col, col], yaw);
    M.pop();
  }
  // 辎重车: a two-wheeled ox cart — spoked wheels on an axle, a plank bed with low sides, shafts reaching forward, and a
  // load of sacks under a cloth in the faction colour.
  recipe('cart', {
    group: 'vehicle', label: '辎重车', keys: ['color', 'load'], defaults: { color: '#2f7d4a', load: true },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.3, 0.5), wood = P.hsv(WOOD.mid, (b - 1.5) * 3), woodD = P.shade(wood, 0.8);
      for (const s of [-1, 1]) wheel(M, [s * 0.92, 0.6, 0], 0.6, 0.12, 6, woodD);
      M.color = WOOD.dark; M.segment([-1.0, 0.6, 0], [1.0, 0.6, 0], 0.05, 0.05, 5);
      // bed and sides
      M.color = wood; M.box([0, 0.86, 0.1], [1.6, 0.1, 2.0], P.shade(wood, 1.05), 'tbfkrl');
      M.color = P.shade(wood, 0.92);
      for (const s of [-1, 1]) M.box([s * 0.78, 1.06, 0.1], [0.06, 0.3, 2.0], null, 'tfkrl');
      M.box([0, 1.06, -0.88], [1.6, 0.3, 0.06], null, 'tfkrl');
      M.color = woodD; for (const s of [-1, 1]) for (const z of [-0.8, 0.1, 1.0]) M.box([s * 0.8, 1.06, z], [0.08, 0.42, 0.08], null, 'tfkrl');
      // shafts and yoke bar
      M.color = wood; for (const s of [-1, 1]) M.beam([s * 0.45, 0.86, 1.0], [s * 0.35, 0.62, 2.9], 0.08, 0.08);
      M.color = woodD; M.beam([-0.55, 0.66, 2.6], [0.55, 0.66, 2.6], 0.07, 0.07);
      if (o.load) {
        const cloth = H6('#D8C8A0', '#CDBB90', '#E2D4B0');
        sack(M, [-0.36, 0.91, 0.75], 0.27, 0.55, 30, 0, cloth[0]); sack(M, [0.34, 0.91, 0.78], 0.26, 0.52, -20, 0, cloth[1]);
        // the main load under a tarpaulin in the faction colour, roped down
        const col = H(o.color), ramp = y => P.mix(P.shade(col, 0.72), P.shade(col, 1.08), clamp01((y - 0.95) / 0.7));
        M.softLump([0, 1.0, -0.25], [0.74, 0.62, 0.72], 1, 20, ramp, 0.08);
        M.color = WOOD.rope; for (const z of [-0.55, 0.05]) M.beam([-0.76, 1.15, z], [0.76, 1.15, z], 0.03, 0.03);
      }
      return M;
    },
  });
  // 独轮车: a wheelbarrow (the Shu army's supply cart): one big wheel in front, a frame with handles and a sack load.
  recipe('wheelbarrow', {
    group: 'vehicle', label: '独轮车',
    build(o) {
      const M = builder(o, '', 0.07, 0.3, 0.4), wood = WOOD.mid, woodD = P.shade(wood, 0.8);
      // the wheel in front (+Z) under the load, two handles reaching back, two legs under the back
      wheel(M, [0, 0.42, 0.45], 0.42, 0.1, 6, woodD);
      M.color = wood;
      for (const s of [-1, 1]) { M.beam([s * 0.22, 0.45, 0.6], [s * 0.36, 0.7, -1.3], 0.07, 0.07); M.beam([s * 0.3, 0.62, -0.55], [s * 0.32, 0.02, -0.62], 0.06, 0.06); }
      M.box([0, 0.72, -0.15], [0.8, 0.06, 1.05], P.shade(wood, 1.05), 'tbfkrl');
      M.color = woodD; M.box([0, 0.86, 0.38], [0.8, 0.26, 0.06], null, 'tfkrl');
      const cloth = H6('#D8C8A0', '#CDBB90', '#E2D4B0'), sk = y => P.mix(P.shade(cloth[0], 0.8), cloth[0], clamp01((y - 0.75) / 0.4));
      M.softLump([-0.18, 0.92, -0.2], [0.24, 0.2, 0.42], 1, 5, sk, 0.1);
      M.softLump([0.18, 0.92, -0.15], [0.24, 0.2, 0.4], 1, 9, y => P.mix(P.shade(cloth[1], 0.8), cloth[1], clamp01((y - 0.75) / 0.4)), 0.1);
      M.color = WOOD.rope; M.beam([-0.42, 1.05, -0.2], [0.42, 1.05, -0.2], 0.025, 0.025);
      return M;
    },
  });

  // =============================================================== boats ==
  /**
   * A boat hull along X (bow +X) floating on y = 0: closed cross-sections (outer skin, gunwale tops, inner walls, floor)
   * lofted stern → bow. o: { len, beam (half-width), keel (depth below 0), sheer (gunwale height), bowRise, sternRise,
   * transom (stern width factor, 0 = pointed), n, color, stripe (sheer strake colour), inner, floorY, wall }.
   */
  function hull(M, o) {
    o = Object.assign({ len: 6, beam: 0.8, keel: 0.3, sheer: 0.45, bowRise: 0.3, sternRise: 0.2, transom: 0, n: 10, color: '#7A5A40', stripe: null, inner: '#9A7A58', floorY: 0.12, wall: 0.07, bowPow: 0.6 }, o);
    const L = o.len, col = toC(o.color), inner = toC(o.inner), stripe = toC(o.stripe || P.shade(col, 1.2));
    const rings = [], outer = [];
    for (let i = 0; i <= o.n; i++) {
      const t = i / o.n, x = lerp(-L / 2, L / 2, t);
      let w = o.beam * Math.pow(Math.sin(Math.PI * clamp(t, 0.02, 0.98)), o.bowPow);
      if (o.transom > 0 && t < 0.5) w = Math.max(w, o.beam * o.transom * (1 - t * 0.6));
      w = Math.max(0.05, w);
      const yG = o.sheer + o.bowRise * Math.pow(Math.max(0, (t - 0.65) / 0.35), 2) + o.sternRise * Math.pow(Math.max(0, (0.35 - t) / 0.35), 2);
      const yK = -o.keel * Math.pow(Math.sin(Math.PI * clamp(t, 0.03, 0.97)), 0.35);
      const wi = Math.max(0.02, w - o.wall), fy = Math.min(yG - 0.08, o.floorY + Math.max(0, yK) + 0.0);
      const ring = [[w, yG], [w * 0.97, yG * 0.45], [w * 0.72, yK * 0.55], [0, yK], [-w * 0.72, yK * 0.55], [-w * 0.97, yG * 0.45], [-w, yG], [-wi, yG], [-wi * 0.85, fy], [wi * 0.85, fy], [wi, yG]].map(([z, y]) => [x, y, z]);
      rings.push(ring);
      outer.push(ring.slice(0, 7));
    }
    const nr = rings[0].length;
    const colOf = (j, i) => (j <= 6 ? (j === 0 || j === 6 ? stripe : j === 1 || j === 5 ? col : P.shade(col, 0.82)) : j === 7 || j === 10 ? P.shade(stripe, 0.9) : inner);
    for (let i = 0; i < rings.length - 1; i++) {
      const A = rings[i], B = rings[i + 1];
      for (let j = 0; j < nr; j++) {
        const k = (j + 1) % nr, c = M.jitterOf ? colOf(j, i) : colOf(j, i), jk = 1 + (M.random01() * 2 - 1) * M.jitter, cc = [c[0] * jk, c[1] * jk, c[2] * jk];
        if (V.len(V.sub(A[j], A[k])) < 1e-4 && V.len(V.sub(B[j], B[k])) < 1e-4) continue;
        M.triangle(A[j], A[k], B[j], cc);
        M.triangle(A[k], B[k], B[j], cc);
      }
    }
    // end caps: the outer section (convex) closed flat
    M.color = P.shade(col, 0.9);
    M.fan(outer[0], V.mul(outer[0].reduce((a, p) => V.add(a, p), [0, 0, 0]), 1 / 7), [-1, 0, 0]);
    M.fan(outer[outer.length - 1], V.mul(outer[outer.length - 1].reduce((a, p) => V.add(a, p), [0, 0, 0]), 1 / 7), [1, 0, 0]);
    return { rings, gunwale: x => { const t = clamp01((x + L / 2) / L); return o.sheer + o.bowRise * Math.pow(Math.max(0, (t - 0.65) / 0.35), 2) + o.sternRise * Math.pow(Math.max(0, (0.35 - t) / 0.35), 2); } };
  }
  Props.helpers.hull = hull;
  /** A battened junk sail (a slab with dark battens) between a mast at x and a luff, from y0 to y1, width w (towards −X). */
  function junkSail(M, x, y0, y1, w, col, batten, rake = 0.15) {
    const bot = [x - w * 0.15, y0, 0.08], top = [x - w * 0.05 + rake, y1, 0.08];
    const poly = [[x + w * 0.12, y0, 0.08], [x - w * 0.88, y0 - 0.1, 0.08], [x - w * 0.8 + rake, y1 - 0.2, 0.08], [x + w * 0.05 + rake, y1, 0.08]];
    slab(M, poly, [0, 0, 1], 0.04, col);
    const n = Math.max(3, Math.round((y1 - y0) / 1.0));
    M.color = batten;
    for (let i = 0; i <= n; i++) { const t = i / n, a = V.lerp(poly[0], poly[3], t), c = V.lerp(poly[1], poly[2], t); M.beam(V.add(c, [0, 0, 0.04]), V.add(a, [0.08, 0, 0.04]), 0.035, 0.03, [0, 1, 0]); }
  }
  // 乌篷船 (a covered river boat): a narrow hull with raised ends, an arched black mat canopy over the middle, a sculling oar.
  recipe('boat_river', {
    group: 'boat', label: '乌篷船',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0, 1);
      const hl = hull(M, { len: 5.4, beam: 0.62, keel: 0.22, sheer: 0.38, bowRise: 0.28, sternRise: 0.22, n: 10, color: H('#6E5038'), stripe: H('#8E6A48'), inner: H('#A07A56'), floorY: 0.1 });
      // the canopy: arched rings of black matting (closed: an outer and an inner arc) with pale bamboo ribs
      const mat = H('#34303A'), rib = H('#B8A06A');
      const arc = (x, r, y0, inset) => { const pts = []; for (let i = 0; i <= 6; i++) { const a = Math.PI * i / 6; pts.push([x, y0 + Math.sin(a) * r * 0.95, Math.cos(a) * r]); } return pts.map(p => [p[0], p[1] - (inset ? 0.05 : 0), p[2] * (inset ? 0.92 : 1)]); };
      const xs = [-1.0, -0.4, 0.2, 0.8];
      for (let i = 0; i < xs.length - 1; i++) {
        const x0 = xs[i], x1 = xs[i + 1], r0 = 0.62, y0 = hl.gunwale(x0) - 0.02, y1 = hl.gunwale(x1) - 0.02;
        const A = arc(x0 + 0.02, r0, y0), B = arc(x1 - 0.02, r0, y1);
        for (let j = 0; j < A.length - 1; j++) M.quad(A[j], A[j + 1], B[j + 1], B[j], [0, A[j][1] + A[j + 1][1] - 2 * y0, A[j][2] + A[j + 1][2]], P.shade(mat, 0.92 + 0.12 * ((i + j) % 2)));
        const Ai = arc(x0 + 0.02, r0, y0, true), Bi = arc(x1 - 0.02, r0, y1, true);
        for (let j = 0; j < Ai.length - 1; j++) M.quad(Ai[j], Ai[j + 1], Bi[j + 1], Bi[j], [0, -(Ai[j][1] + Ai[j + 1][1] - 2 * y0), -(Ai[j][2] + Ai[j + 1][2])], P.shade(mat, 0.7));
        for (const [P1, P2] of [[A, Ai], [B, Bi]]) for (let j = 0; j < P1.length - 1; j++) M.quad(P1[j], P1[j + 1], P2[j + 1], P2[j], [P1 === A ? -1 : 1, 0, 0], P.shade(mat, 0.8));
      }
      M.color = rib;
      for (const x of xs) { const y0 = hl.gunwale(x) - 0.02, A = arc(x, 0.64, y0); for (let j = 0; j < A.length - 1; j++) M.segment(A[j], A[j + 1], 0.025, 0.025, 3, false, false); }
      // sculling oar (橹) at the stern, a pole on the deck
      M.color = WOOD.light; M.segment([-2.3, 0.62, 0.05], [-3.6, -0.1, 0.3], 0.03, 0.04, 4);
      M.color = WOOD.mid; M.box([-3.5, 0.0, 0.28], [0.35, 0.04, 0.12]);
      M.color = WOOD.light; M.segment([1.2, 0.5, -0.3], [2.6, 0.5, -0.25], 0.025, 0.025, 4);
      return M;
    },
  });
  // 沙船 / 福船 (a sea-going junk): a broad hull with a high square stern, a deck house, three masts with ribbed lug sails
  // in tan-red, a rudder and painted eyes on the bow.
  recipe('junk', {
    group: 'boat', label: '海船',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0, 1);
      const hullC = H('#6A4A34'), stripe = H('#B84A32'), deck = H('#A88660'), sailC = P.hsv(H('#C07048'), (b - 1.5) * 6), batten = P.shade(sailC, 0.62);
      const hl = hull(M, { len: 13, beam: 2.2, keel: 0.7, sheer: 1.1, bowRise: 0.9, sternRise: 1.4, transom: 0.85, n: 12, color: hullC, stripe, inner: deck, floorY: 0.85, wall: 0.12, bowPow: 0.45 });
      // stern castle and its railing, a deck house amidships
      M.color = H('#8A6446'); M.box([-4.7, 2.4, 0], [2.6, 1.4, 3.2], P.shade(deck, 1.05), 'tfkrl');
      M.color = PAL.hanRed; for (const s of [-1, 1]) M.box([-4.7, 3.25, s * 1.55], [2.6, 0.3, 0.08], null, 'tfkrl');
      M.color = H('#7A5A40'); M.box([-1.0, 1.55, 0], [2.4, 1.0, 2.4], null, 'tfkrl');
      hanRoof(M, { style: 'gable', w: 2.4, d: 2.4, y: 2.05, over: 0.25, rise: 0.6, detail: 'low', palette: 'grey', fascia: null, ornaments: false, cx: -1.0 });
      // masts and sails
      const masts = [[2.6, 9.0, 3.6], [-0.2, 10.5, 4.4], [-5.2, 7.0, 2.6]];
      for (const [x, h, w] of masts) {
        M.color = H('#5A4232'); M.segment([x, 0.8, 0], [x, h, 0], 0.12, 0.08, 6);
        junkSail(M, x, 2.6 + (x < -3 ? 1.6 : 0), h - 0.4, w, sailC, batten);
      }
      // rudder, bow eyes
      M.color = H('#5A4232'); M.box([-6.75, 0.6, 0], [0.3, 1.8, 0.15]);
      for (const s of [-1, 1]) {
        const p = [5.7, hl.gunwale(5.7) - 0.5, s * 0.82];
        M.color = H('#F2EEE4'); slab(M, ngon(p, 0.28, 0.28, 8, [1, 0, 0], [0, 1, 0]).map(q => [q[0], q[1], q[2]]), [0.3, 0, s], 0.02, H('#F2EEE4'));
        M.color = H('#222'); slab(M, ngon(V.add(p, [0.06, 0, s * 0.025]), 0.13, 0.13, 6, [1, 0, 0], [0, 1, 0]), [0.3, 0, s], 0.02, H('#26222A'));
      }
      return M;
    },
  });
  // 罗马桨帆船 (a Roman galley): a long low hull with a bronze ram, a curled stern post, a striped square sail and a row of
  // oars on each side, round shields along the rail.
  recipe('galley', {
    group: 'boat', label: '罗马桨帆船', keys: ['color'], defaults: { color: '#B83A2E' },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0, 1), sail = H('#EDE2C8'), stripe = H(o.color);
      const hl = hull(M, { len: 16, beam: 1.5, keel: 0.5, sheer: 0.9, bowRise: 0.5, sternRise: 1.3, n: 14, color: H('#5E4A3A'), stripe: H('#C8A060'), inner: H('#A08060'), floorY: 0.7, wall: 0.1, bowPow: 0.5 });
      // ram and the curled stern post (aplustre)
      M.color = H('#B08A40'); M.spike([7.6, 0.05, 0], [1, 0.05, 0], 0.22, 1.1, 5);
      M.color = H('#5E4A3A'); M.curve([-7.6, 1.9, 0], [-8.8, 3.3, 0], [-7.9, 4.0, 0], 0.18, 0.06, 5, 5);
      M.curve([7.4, 1.3, 0], [8.2, 2.0, 0], [7.9, 2.4, 0], 0.14, 0.05, 3, 5);
      // mast, yard, square sail with stripes
      M.color = H('#6E5038'); M.segment([0.5, 0.7, 0], [0.5, 8.8, 0], 0.11, 0.08, 6);
      M.beam([0.5, 8.4, -3.0], [0.5, 8.4, 3.0], 0.09, 0.09);
      M.windGradient = true; M.windY0 = 8.4; M.windY1 = 3.5; M.wind = 0.5;
      for (let i = 0; i < 5; i++) { const z0 = lerp(-2.8, 2.8, i / 5), z1 = lerp(-2.8, 2.8, (i + 1) / 5); M.color = i % 2 ? stripe : sail; slab(M, [[0.62, 8.3, z0], [0.62, 8.3, z1], [0.82, 3.6, z1 * 1.04], [0.82, 3.6, z0 * 1.04]], [1, 0.05, 0], 0.04, i % 2 ? stripe : sail); }
      M.wind = 0; M.windGradient = false;
      // oars and shields
      const nO = o.far ? 6 : 12;
      for (let i = 0; i < nO; i++) {
        const x = lerp(-5.2, 5.0, i / (nO - 1));
        for (const s of [-1, 1]) {
          const g = hl.gunwale(x);
          M.color = WOOD.light; M.segment([x, g - 0.15, s * 1.45], [x - 0.4, -0.1, s * 3.6], 0.035, 0.035, 4);
          M.color = WOOD.mid; M.box([x - 0.44, -0.12, s * 3.75], [0.1, 0.03, 0.42]);
          if (i % 2 === 0) { M.color = i % 4 ? H('#C8A040') : stripe; slab(M, ngon([x + 0.3, g + 0.18, s * 1.53], 0.28, 0.28, 8, [1, 0, 0], [0, 1, 0]), [0, 0, s], 0.04); }
        }
      }
      return M;
    },
  });
  // 三角帆船 (a dhow): a raked prow, a high stern, one mast raked forward with a big lateen sail on a long yard.
  recipe('dhow', {
    group: 'boat', label: '三角帆船',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0, 1), sail = P.hsv(H('#ECE2C6'), (b - 1.5) * 4);
      const hl = hull(M, { len: 10, beam: 1.35, keel: 0.5, sheer: 0.85, bowRise: 0.9, sternRise: 0.9, n: 12, color: H('#8A6440'), stripe: H('#A87E54'), inner: H('#B08C64'), floorY: 0.6, wall: 0.09, bowPow: 0.55 });
      M.color = H('#8A6440'); M.box([-4.0, 1.85, 0], [1.8, 0.5, 2.2], H('#B08C64'), 'tfkrl');
      M.color = H('#6E5038');
      M.segment([0.8, 0.6, 0], [1.6, 8.0, 0], 0.11, 0.08, 6);
      // the lateen yard: long, slanting from low forward to high aft
      const y0 = [5.0, 2.2, 0.15], y1 = [-3.4, 9.6, 0.15];
      M.segment(y0, y1, 0.07, 0.05, 5);
      M.windGradient = true; M.windY0 = 9; M.windY1 = 2; M.wind = 0.4;
      slab(M, [V.add(y0, [0, -0.05, 0.05]), V.add(y1, [0, -0.08, 0.05]), [-1.6, 1.6, 0.2]], [0, 0, 1], 0.04, sail);
      M.wind = 0; M.windGradient = false;
      M.color = H('#3E3436'); M.segment([-1.6, 1.6, 0.2], [-4.2, 1.9, 0.1], 0.015, 0.015, 3, false, false);
      return M;
    },
  });

  // ============================================================= village ==
  // 井: a stone well curb, a timber windlass frame with a little tiled gable roof, rope and bucket.
  recipe('well', {
    group: 'village', label: '水井',
    build(o) {
      const M = builder(o, '', 0.07, 0.3, 0.6);
      M.jitterAll = true;
      const n = 8, R = 0.75, r = 0.55, hgt = 0.7;
      for (let i = 0; i < n; i++) {
        const a0 = i / n * Math.PI * 2, a1 = (i + 1) / n * Math.PI * 2;
        const o0 = [Math.cos(a0) * R, Math.sin(a0) * R], o1 = [Math.cos(a1) * R, Math.sin(a1) * R], i0 = [Math.cos(a0) * r, Math.sin(a0) * r], i1 = [Math.cos(a1) * r, Math.sin(a1) * r];
        const st = P.shade(PAL.stone, 0.92 + 0.12 * (i % 3) / 2), mid = [(o0[0] + o1[0]) / 2, (o0[1] + o1[1]) / 2];
        M.quad([o0[0], 0, o0[1]], [o1[0], 0, o1[1]], [o1[0], hgt, o1[1]], [o0[0], hgt, o0[1]], [mid[0], 0, mid[1]], st);
        M.quad([i0[0], 0.1, i0[1]], [i1[0], 0.1, i1[1]], [i1[0], hgt, i1[1]], [i0[0], hgt, i0[1]], [-mid[0], 0, -mid[1]], P.shade(st, 0.7));
        M.quad([o0[0], hgt, o0[1]], [o1[0], hgt, o1[1]], [i1[0], hgt, i1[1]], [i0[0], hgt, i0[1]], [0, 1, 0], P.shade(PAL.stoneLight, 0.95 + 0.08 * (i % 2)));
      }
      M.jitterAll = false;
      M.color = H('#34485A'); M.emission = 0.04; M.disc([0, 0.18, 0], r, n); M.emission = 0;
      // frame, roller, roof
      M.color = PAL.timber;
      for (const s of [-1, 1]) M.box([s * 0.95, 1.1, 0], [0.12, 2.2, 0.12], null, 'tfkrl');
      M.color = WOOD.light;
      M.push().translate(0, 1.5, 0).rotate(0, 0, 90); M.lathe([[0.1, -0.95], [0.12, -0.8], [0.12, 0.8], [0.1, 0.95]], 6, false, true, true); M.pop();
      M.color = PAL.timberDark; M.beam([1.0, 1.5, 0], [1.25, 1.5, 0], 0.05, 0.05); M.beam([1.25, 1.5, 0], [1.25, 1.25, 0], 0.04, 0.04);
      M.color = WOOD.rope; M.segment([0.1, 1.42, 0], [0.1, 0.55, 0.05], 0.015, 0.015, 3, false, false);
      M.color = WOOD.mid; M.lathe([[0.11, 0.38], [0.15, 0.58]], 7, false, true, false); M.color = PAL.iron; M.band([0, 0, 0], 0.13, 0.135, 0.44, 0.48, 7);
      M.push().translate(0.1, 0, 0.05); M.pop();
      hanRoof(M, { style: 'gable', w: 1.9, d: 0.9, y: 2.2, over: 0.25, rise: 0.6, detail: 'mid', palette: 'slate', fascia: null, ornaments: false, gableColor: PAL.timber, soffit: false });
      M.color = WOOD.mid; M.box([0.95, 0.75, 0.75], [0.36, 0.08, 0.36], null, 'tfkrl');
      return M;
    },
  });
  // 路牌: a signpost: a post with a little cap and two arrow boards (optional painted names: opts.text = ['洛阳', '长安']).
  recipe('signpost', {
    group: 'village', label: '路牌', keys: ['text'], defaults: { text: null },
    build(o) {
      const M = builder(o, '', 0.07, 0.3, 0.5), wood = WOOD.light, decals = [];
      M.color = PAL.timber; M.box([0, 1.15, 0], [0.13, 2.3, 0.13], null, 'tfkrl');
      M.color = PAL.tileSlate; M.taperedBox([0, 2.3, 0], [0.3, 0.12, 0.3], 0.8);
      const boards = [[1.75, 25, 1], [1.3, -30, -1]];
      boards.forEach(([y, yaw, dir], i) => {
        M.push().translate(0, y, 0).rotate(0, yaw, 0);
        M.color = P.shade(wood, 1 - 0.08 * i);
        const pts = dir > 0 ? [[0.08, -0.15], [0.86, -0.15], [1.02, 0], [0.86, 0.15], [0.08, 0.15]] : [[-0.08, -0.15], [-0.08, 0.15], [-0.86, 0.15], [-1.02, 0], [-0.86, -0.15]];
        M.flat(pts, 0.05);
        M.pop();
        const t = o.text && o.text[i];
        if (t) decals.push({ text: t, board: '#C8A070', ink: '#3A2A24', w: 0.62, h: 0.2, at: [Math.cos(-yaw * D2R) * 0.47 * dir, y, Math.sin(-yaw * D2R) * 0.47 * dir + 0.03], yaw });
      });
      M.color = PAL.stone; M.box([0, 0.08, 0], [0.32, 0.16, 0.32], null, 'tfkrl');
      return { ink: M, decals };
    },
  });
  // 军帐: an army ridge tent of pale canvas, a faction-coloured band along the foot and ridge, the door flaps tied back,
  // guy ropes and pegs.
  recipe('tent_army', {
    group: 'village', label: '军帐', keys: ['color'], defaults: { color: '#2f7d4a' },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.3, 0.6), canvas = P.hsv(H('#E2D6BC'), (b - 1.5) * 3), col = H(o.color);
      const L = 1.6, Wd = 1.35, Ht = 2.0;
      // two slanted panels (closed slabs), ridge along Z (door at +Z)
      for (const s of [-1, 1]) {
        const poly = [[0, Ht, -L], [0, Ht, L], [s * Wd, 0.02, L + 0.05], [s * Wd, 0.02, -L - 0.05]];
        slab(M, poly, [s * Ht, Wd, 0], 0.04, P.shade(canvas, s > 0 ? 0.96 : 1));
        // faction band near the foot
        const band = [[s * Wd * 0.82, Ht * 0.18, -L - 0.03], [s * Wd * 0.82, Ht * 0.18, L + 0.03], [s * (Wd + 0.01), 0.03, L + 0.06], [s * (Wd + 0.01), 0.03, -L - 0.06]];
        slab(M, band.map(p => V.add(p, [s * 0.012, 0.012, 0])), [s * Ht, Wd, 0], 0.01, col);
      }
      // back wall (triangle) and front: two flaps tied back leaving a dark doorway
      M.color = P.shade(canvas, 0.9); M.triangleFacing([-Wd, 0.02, -L], [0, Ht, -L], [Wd, 0.02, -L], [0, 0, -1]);
      M.color = H('#3A3230'); M.triangleFacing([-Wd * 0.55, 0.02, L - 0.3], [0, Ht * 0.92, L - 0.3], [Wd * 0.55, 0.02, L - 0.3], [0, 0, 1]);
      for (const s of [-1, 1]) slab(M, [[0, Ht, L], [s * Wd * 0.3, Ht * 0.7, L + 0.02], [s * Wd * 0.85, 0.05, L + 0.02], [s * Wd, 0.02, L]], [0, 0, 1], 0.03, P.shade(canvas, 0.92));
      // ridge pole ends, a ridge band, guy ropes and pegs
      M.color = WOOD.dark; M.segment([0, 0, L + 0.1], [0, Ht + 0.3, L + 0.1], 0.04, 0.035, 4); M.segment([0, 0, -L - 0.1], [0, Ht + 0.3, -L - 0.1], 0.04, 0.035, 4);
      M.color = col; M.beam([0, Ht + 0.02, -L - 0.05], [0, Ht + 0.02, L + 0.05], 0.12, 0.06);
      M.color = WOOD.rope;
      for (const s of [-1, 1]) for (const z of [-L, L]) { const a = [0, Ht + 0.2, z + Math.sign(z) * 0.1], e = [s * 0.6, 0, z + Math.sign(z) * 0.9]; M.segment(a, e, 0.012, 0.012, 3, false, false); M.color = WOOD.dark; M.segment(e, V.add(e, [0, 0.18, 0]), 0.025, 0.02, 3); M.color = WOOD.rope; }
      return M;
    },
  });
  // 帅帐: the commander's pavilion — six walls in the faction colour with a pale band, a conical roof with scalloped
  // valance, a finial pennant, a front awning on two poles.
  recipe('tent_command', {
    group: 'village', label: '帅帐', keys: ['color'], defaults: { color: '#2f7d4a' },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.3, 0.8), col = H(o.color), cream = H('#ECE2CA'), gold = PAL.gold;
      const R = 2.5, Hw = 2.0, n = 8;
      M.color = col;
      M.lathe([[R, 0], [R, Hw * 0.45], [R, Hw]], n, false, false, false, [P.shade(col, 0.92), cream, col], 180 / n);
      M.color = P.shade(cream, 0.95);
      M.lathe([[R + 0.3, Hw], [1.0, Hw + 1.4], [0.15, Hw + 2.1], [0, Hw + 2.15]], n, false, true, false, [cream, P.shade(cream, 1.03), cream, cream], 180 / n);
      // scalloped valance under the eave
      M.color = col;
      for (let i = 0; i < n; i++) { const a = (i + 0.5) / n * Math.PI * 2, c = [Math.cos(a) * (R + 0.28), Hw - 0.05, Math.sin(a) * (R + 0.28)], t = [-Math.sin(a), 0, Math.cos(a)]; slab(M, [V.madd(c, t, -0.9), V.madd(c, t, 0.9), V.add(V.madd(c, t, 0.45), [0, -0.38, 0]), V.add(V.madd(c, t, -0.45), [0, -0.38, 0])], [Math.cos(a), 0, Math.sin(a)], 0.02, P.shade(col, 0.9)); }
      // doorway and awning
      M.color = H('#3A2E2A'); M.quad([-0.6, 0, R + 0.01], [0.6, 0, R + 0.01], [0.6, 1.7, R + 0.01], [-0.6, 1.7, R + 0.01], [0, 0, 1]);
      M.color = WOOD.dark; for (const s of [-1, 1]) M.segment([s * 1.0, 0, R + 1.6], [s * 1.0, 2.1, R + 1.6], 0.045, 0.04, 4);
      slab(M, [[-1.15, 2.05, R + 1.65], [1.15, 2.05, R + 1.65], [1.0, 1.95, R - 0.05], [-1.0, 1.95, R - 0.05]], [0, 1, 0.1], 0.04, cream);
      M.color = col; M.box([0, 1.98, R + 1.68], [2.3, 0.18, 0.04], null, 'f');
      M.color = gold; M.segment([0, Hw + 2.1, 0], [0, Hw + 3.2, 0], 0.035, 0.03, 4); M.sphere([0, Hw + 2.15, 0], 0.12, 6, 3, false);
      const start = M.vertexCount; M.color = col;
      slab(M, [[0.03, Hw + 3.15, 0], [1.0, Hw + 2.95, 0], [0.03, Hw + 2.7, 0]], [0, 0, 1], 0.02, col);
      M.windBy(start, p => clamp01(p[0]) * 1.4);
      return M;
    },
  });
  // 蒙古包 (yurt): felt walls with a red patterned band, a low conical roof to a crown ring, rope bands, a painted door.
  recipe('yurt', {
    group: 'village', label: '蒙古包',
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.3, 0.6), felt = P.hsv(H('#ECE4D2'), (b - 1.5) * 3), band = H6('#B84A3A', '#3E6A9A', '#B84A3A', '#4E7A4A')[b];
      const R = 2.4, Hw = 1.5, n = 14;
      M.color = felt; M.lathe([[R, 0], [R, Hw * 0.7], [R, Hw]], n, false, false, false, [P.shade(felt, 0.92), felt, felt]);
      M.color = band; M.band([0, 0, 0], R + 0.015, R + 0.015, Hw * 0.62, Hw * 0.82, n);
      M.color = P.shade(felt, 1.02);
      M.lathe([[R + 0.12, Hw - 0.05], [R * 0.6, Hw + 0.75], [0.45, Hw + 1.2], [0.45, Hw + 1.3]], n, false, true, false, [P.shade(felt, 0.95), felt, P.shade(felt, 1.04), felt]);
      M.color = band; M.band([0, 0, 0], R * 0.82, R * 0.62, Hw + 0.28, Hw + 0.5, n);
      M.color = WOOD.mid; M.lathe([[0.45, Hw + 1.3], [0.5, Hw + 1.36], [0.3, Hw + 1.42], [0, Hw + 1.48]], 10, false, true, false);
      M.color = H('#5A4A3E'); for (const y of [0.35, 1.1]) M.band([0, 0, 0], R + 0.025, R + 0.025, y, y + 0.06, n);
      // door
      M.color = H('#D8783A'); M.box([0, 0.75, R + 0.03], [0.85, 1.5, 0.08], null, 'tfrl');
      M.color = H('#B85A2A'); M.box([0, 0.75, R + 0.075], [0.6, 1.2, 0.02], null, 'f');
      M.color = PAL.gold; M.box([0, 0.75, R + 0.09], [0.22, 0.22, 0.02], null, 'f');
      return M;
    },
  });
  // 木桥: a timber bridge on log stringers spanning X (length opts.len), plank deck, posts and rails, piles at the banks.
  recipe('bridge_wood', {
    group: 'village', label: '木桥', keys: ['len'], defaults: { len: 6 },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.08, 0.3, 0.6), L = o.len / 2, Wd = 1.0, arch = 0.3;
      const yAt = x => 0.6 + arch * (1 - (x / L) * (x / L));
      M.color = WOOD.dark;
      for (const s of [-1, 1]) { const pts = []; for (let i = 0; i <= 4; i++) { const x = lerp(-L - 0.3, L + 0.3, i / 4); pts.push([x, yAt(clamp(x, -L, L)) - 0.15, s * 0.7]); } rib(M, pts, 0.14, 0.14, 6); }
      const n = Math.round(o.len / 0.28);
      for (let i = 0; i < n; i++) { const x = lerp(-L, L, (i + 0.5) / n); M.color = P.shade(WOOD.light, 0.88 + 0.16 * (hashN(i, 1, b) * 0.5 + 0.5)); M.push().translate(x, yAt(x), 0).rotate(0, 0, -Math.atan(-2 * arch * x / (L * L)) / D2R); M.box([0, 0, 0], [0.26, 0.07, 2 * Wd + 0.1 * hashN(i, 2, b)], null, 'tfkrl'); M.pop(); }
      M.color = WOOD.mid;
      const posts = 4;
      for (const s of [-1, 1]) {
        for (let i = 0; i <= posts; i++) { const x = lerp(-L + 0.15, L - 0.15, i / posts); M.box([x, yAt(x) + 0.45, s * Wd], [0.1, 0.9, 0.1], null, 'tfkrl'); }
        const pts = []; for (let i = 0; i <= posts; i++) { const x = lerp(-L + 0.15, L - 0.15, i / posts); pts.push([x, yAt(x) + 0.88, s * Wd]); }
        for (let i = 0; i < posts; i++) M.beam(pts[i], pts[i + 1], 0.08, 0.08);
      }
      M.color = WOOD.dark; for (const x of [-L, L]) for (const s of [-1, 1]) M.segment([x, -0.6, s * 0.7], [x, yAt(x) - 0.1, s * 0.7], 0.12, 0.12, 5);
      return M;
    },
  });
  // 石拱桥: a stone arch bridge spanning X: a voussoir arch through a solid spandrel, a ramped deck and a balustrade of
  // posts and panels; lion-head capitals at the four ends.
  recipe('bridge_stone', {
    group: 'village', label: '石拱桥', keys: ['len'], defaults: { len: 9 },
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 0.8), L = o.len / 2, Wd = 1.35, R = L * 0.55, rise = 1.7;
      const st = H('#B8B0A0'), stD = H('#9A9282'), stL = H('#CCC4B4');
      const deckY = x => rise * (1 - Math.pow(Math.abs(x) / L, 1.6)) + 0.35;
      M.jitterAll = true;
      // the two faces: spandrel strips from the deck down to the arch (or the ground beyond it)
      const N = 14;
      for (const f of [1, -1]) for (let i = 0; i < N; i++) {
        const x0 = lerp(-L, L, i / N), x1 = lerp(-L, L, (i + 1) / N);
        const arch = x => (Math.abs(x) < R ? Math.sqrt(R * R - x * x) * (rise * 0.75 / R) - 0.3 : -0.3);
        const yb0 = Math.max(arch(x0), -0.3), yb1 = Math.max(arch(x1), -0.3);
        M.quad([x0, yb0, f * Wd], [x1, yb1, f * Wd], [x1, deckY(x1), f * Wd], [x0, deckY(x0), f * Wd], [0, 0, f], P.shade(i % 2 ? st : stL, 0.95 + 0.08 * hashN(i, f, 3)));
        // arch underside (intrados) between the faces
        if (Math.abs((x0 + x1) / 2) < R) M.quad([x0, arch(x0), -Wd], [x1, arch(x1), -Wd], [x1, arch(x1), Wd], [x0, arch(x0), Wd], [0, -1, 0], P.shade(stD, 0.85));
      }
      // voussoirs round the arch on both faces
      for (const f of [1, -1]) for (let i = 0; i < 11; i++) {
        const a = Math.PI * (i + 0.5) / 11, x = Math.cos(a) * R * 0.97, y = Math.sin(a) * R * (rise * 0.75 / R) - 0.3;
        M.color = i === 5 ? stL : i % 2 ? st : stD;
        M.push().translate(x, y, f * (Wd + 0.03)).rotate(0, 0, (a - Math.PI / 2) / D2R);
        M.box([0, 0.12, 0], [0.38, 0.42, 0.08], null, f > 0 ? 'tbfrl' : 'tbkrl'); M.pop();
      }
      // deck (paved, in strips)
      for (let i = 0; i < N; i++) { const x0 = lerp(-L, L, i / N), x1 = lerp(-L, L, (i + 1) / N); M.quad([x0, deckY(x0), -Wd], [x1, deckY(x1), -Wd], [x1, deckY(x1), Wd], [x0, deckY(x0), Wd], [0, 1, 0], P.shade(i % 2 ? stL : st, 0.98)); }
      // balustrade
      for (const f of [1, -1]) {
        const np = 6;
        for (let i = 0; i <= np; i++) { const x = lerp(-L + 0.2, L - 0.2, i / np), y = deckY(x); M.color = stL; M.box([x, y + 0.45, f * (Wd - 0.08)], [0.16, 0.9, 0.16], null, 'tfkrl'); M.color = i === 0 || i === np ? st : stL; M.sphere([x, y + 0.98, f * (Wd - 0.08)], [0.12, 0.13, 0.12], 6, 3, false); }
        for (let i = 0; i < np; i++) { const xa = lerp(-L + 0.2, L - 0.2, i / np) + 0.1, xb = lerp(-L + 0.2, L - 0.2, (i + 1) / np) - 0.1; M.color = st; M.quad([xa, deckY(xa) + 0.12, f * (Wd - 0.06)], [xb, deckY(xb) + 0.12, f * (Wd - 0.06)], [xb, deckY(xb) + 0.7, f * (Wd - 0.06)], [xa, deckY(xa) + 0.7, f * (Wd - 0.06)], [0, 0, f]); M.quad([xa, deckY(xa) + 0.12, f * (Wd - 0.1)], [xb, deckY(xb) + 0.12, f * (Wd - 0.1)], [xb, deckY(xb) + 0.7, f * (Wd - 0.1)], [xa, deckY(xa) + 0.7, f * (Wd - 0.1)], [0, 0, -f]); M.color = stL; M.beam([xa - 0.1, deckY(xa) + 0.74, f * (Wd - 0.08)], [xb + 0.1, deckY(xb) + 0.74, f * (Wd - 0.08)], 0.1, 0.08); }
      }
      M.jitterAll = false;
      return M;
    },
  });
  // 土地庙: a little earth-god shrine — a brick body with an open front on a stone step, a small tiled hip roof, a seated
  // clay figure, an incense burner and red paper couplets.
  recipe('shrine', {
    group: 'village', label: '土地庙',
    build(o) {
      const M = builder(o, '', 0.06, 0.3, 0.6), brick = H('#A8A49C'), red = PAL.hanRed;
      M.color = PAL.stone; M.box([0, 0.15, 0.2], [2.2, 0.3, 2.0], PAL.stoneLight, 'tfkrl');
      M.color = brick;
      M.box([0, 1.15, -0.65], [1.8, 1.7, 0.2], null, 'tfkrl');
      for (const s of [-1, 1]) M.box([s * 0.8, 1.15, 0.0], [0.2, 1.7, 1.5], null, 'tfkrl');
      M.color = H('#3E3230'); M.quad([-0.7, 0.3, -0.54], [0.7, 0.3, -0.54], [0.7, 2.0, -0.54], [-0.7, 2.0, -0.54], [0, 0, 1]);
      M.color = red; M.box([0, 2.05, 0.05], [1.9, 0.14, 1.6], null, 'tbfkrl');
      hanRoof(M, { style: 'hip', w: 1.9, d: 1.6, y: 2.1, over: 0.45, rise: 0.75, detail: 'mid', palette: 'grey', ornaments: true, lift: 0.3 });
      // couplets on the front piers, the figure, an altar with a burner
      M.color = red; for (const s of [-1, 1]) M.box([s * 0.8, 1.25, 0.76], [0.12, 1.2, 0.02], null, 'f');
      M.color = H('#B8743A'); M.box([0, 0.7, -0.35], [0.5, 0.5, 0.35], null, 'tfrl');
      M.color = H('#C88A50'); M.sphere([0, 1.08, -0.35], [0.16, 0.18, 0.15], 6, 4, false);
      M.color = H('#F2EEE4'); M.softLump([0, 0.98, -0.2], [0.11, 0.12, 0.06], 0, 3, () => H('#F2EEE4'), 0.1);
      M.color = H('#6E3A2A'); M.box([0, 0.55, 0.35], [0.8, 0.5, 0.45], null, 'tfkrl');
      M.color = H('#7A6A3A'); M.lathe([[0.1, 0.8], [0.16, 0.86], [0.16, 0.98], [0.12, 1.0]], 6, false, true, true);
      M.color = H('#E8D8B0'); for (let i = 0; i < 3; i++) M.segment([(i - 1) * 0.05, 0.98, 0], [(i - 1) * 0.06, 1.25, 0.02], 0.008, 0.006, 3, false, false);
      return M;
    },
  });
  // 石碑: a stone stele — on a tortoise (赑屃, variant 'turtle') or a plain pedestal — with a rounded dragon-curl crown and a
  // darker inscription panel (opts.text: a painted inscription, read top to bottom).
  recipe('stele', {
    group: 'village', label: '石碑', keys: ['variant', 'text'], defaults: { variant: 'turtle', text: null },
    build(o) {
      const M = builder(o, o.variant, 0.06, 0.3, 0.5), st = H('#9C9A94'), stD = H('#7A7874'), decals = [];
      let y0 = 0;
      if (o.variant === 'turtle') {
        M.facetBlob([0, 0.42, 0], [0.75, 0.42, 1.0], 1, 0.06, 7, 0.6, LP.byNormal(P.shade(st, 1.05), st, stD, 0.5));
        M.color = st; M.sphere([0, 0.42, 1.12], [0.22, 0.2, 0.3], 7, 4, false);
        for (const s of [-1, 1]) for (const z of [-0.6, 0.6]) M.sphere([s * 0.68, 0.15, z], [0.2, 0.15, 0.24], 6, 3, false);
        M.color = stD; M.box([0, 0.86, 0], [0.86, 0.12, 0.42], null, 'tfkrl');
        y0 = 0.92;
      } else { M.color = stD; M.box([0, 0.25, 0], [1.2, 0.5, 0.7], P.shade(stD, 1.08), 'tfkrl'); y0 = 0.5; }
      const sw = 0.78, sh = 2.1, sd = 0.24;
      M.color = st; M.box([0, y0 + sh / 2, 0], [sw, sh, sd], null, 'tfkrl');
      M.color = P.shade(st, 1.06);
      M.push().translate(0, y0 + sh, 0).scale(1, 1, 1);
      M.flat([[-sw / 2, 0], [sw / 2, 0], [sw / 2 - 0.05, 0.25], [0.18, 0.42], [-0.18, 0.42], [-sw / 2 + 0.05, 0.25]], sd + 0.02);
      M.pop();
      M.color = P.shade(st, 0.95); for (const s of [-1, 1]) M.sphere([s * 0.22, y0 + sh + 0.3, sd / 2 + 0.01], [0.13, 0.12, 0.05], 6, 3, false);
      M.color = P.shade(st, 0.8); M.box([0, y0 + sh * 0.5, sd / 2 + 0.005], [sw * 0.66, sh * 0.72, 0.01], null, 'f');
      if (o.text) decals.push({ text: o.text, board: '#6E6C68', ink: '#E8E2D2', w: sw * 0.6, h: sh * 0.66, at: [0, y0 + sh * 0.5, sd / 2 + 0.014], vertical: true });
      return { ink: M, decals };
    },
  });
  // 亭 (pavilion): a square stone platform with steps, four vermilion columns, a painted architrave, bench railings, a
  // pyramidal tiled roof with upturned corners and a finial.
  recipe('pavilion', {
    group: 'village', label: '亭', keys: ['palette'], defaults: { palette: 'slate' },
    build(o) {
      const M = builder(o, '', 0.06, 0.3, 0.8), S = 1.6, H0 = 0.45, Hc = 2.6;
      M.color = PAL.stoneLight; M.box([0, H0 / 2, 0], [S * 2 + 0.6, H0, S * 2 + 0.6], P.shade(PAL.stoneLight, 1.05), 'tfkrl');
      M.color = PAL.stone; for (let i = 0; i < 2; i++) M.box([0, H0 * (0.25 + 0.5 * i) / 1, S + 0.45 + 0.25 * (1 - i)], [1.2, H0 * (0.5 + 0.5 * i), 0.25], null, 'tfrl');
      columns(M, [[-S, -S], [S, -S], [S, S], [-S, S]], H0, H0 + Hc, { r: 0.13 });
      M.color = PAL.hanRed; for (const [a, c] of [[[-S, 0, -S], [S, 0, -S]], [[S, 0, -S], [S, 0, S]], [[S, 0, S], [-S, 0, S]], [[-S, 0, S], [-S, 0, -S]]]) M.beam(V.add(a, [0, H0 + Hc - 0.12, 0]), V.add(c, [0, H0 + Hc - 0.12, 0]), 0.2, 0.24);
      M.color = PAL.teal; for (const [a, c] of [[[-S, 0, -S], [S, 0, -S]], [[S, 0, -S], [S, 0, S]], [[S, 0, S], [-S, 0, S]], [[-S, 0, S], [-S, 0, -S]]]) M.beam(V.add(a, [0, H0 + Hc + 0.08, 0]), V.add(c, [0, H0 + Hc + 0.08, 0]), 0.22, 0.16);
      // bench railings on three sides
      M.color = PAL.hanRed;
      for (const [a, c] of [[[-S, -S], [S, -S]], [[S, -S], [S, S]], [[-S, S], [-S, -S]]]) { M.beam([a[0], H0 + 0.45, a[1]], [c[0], H0 + 0.45, c[1]], 0.3, 0.07); M.beam([a[0], H0 + 0.75, a[1]], [c[0], H0 + 0.75, c[1]], 0.06, 0.06); }
      hanRoof(M, { style: 'pyramid', w: S * 2, d: S * 2, y: H0 + Hc + 0.18, over: 0.75, rise: 1.9, lift: 0.5, detail: 'mid', palette: o.palette });
      return M;
    },
  });


  // ======================================================= buildings ==
  const PCOL = { gold: H('#D9A441'), goldLight: H('#EDC46A'), barding: H('#2F7D4A'), bardingTrim: H('#EBDDB0'), wood: H('#8C6A48'), steel: H('#C9D2DA'), tassel: H('#D2402E') };
  const MOSS = LP.foliage(1, H('#7D9A55'), 6);
  function merlonsMossy(M, xsList, y, z, size, base, mossEvery = 4, lumps = 2) {
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


  function buildGateTower(o) {
    const C = PCOL;
    o = o || {};
    const lit = o.lit === true, nightLit = o.lit === 'night' || o.lit == null;
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
    for (const s of [-1, 1]) { M.push(); if (s < 0) M.scale(-1, 1, 1); plinth(M, aw + 0.42, XB + 0.02, ZB, PL); M.pop(); }
    for (const s of [-1, 1]) { M.push().translate(s * XB, 0, 0).rotate(0, s * 90, 0); plinth(M, -ZB - 0.16, ZB + 0.16, 0, PL); M.pop(); }
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
    merlonsMossy(M, mx, HB + 0.6, pz, [0.82, 0.44, 0.46], pc, 4, 1);
    for (const s of [-1, 1]) { M.push().translate(s * px, 0, 0).rotate(0, 90, 0); merlonsMossy(M, [-pz + 0.6, 0, pz - 0.6], HB + 0.6, 0, [0.8, 0.44, 0.46], pc, 0); M.pop(); }
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
      M.color = paper; M.emission = lit ? 0.92 : 0; M.nightEmission = nightLit ? 0.92 : 0;
      M.box([xm, (py0 + py1) / 2, wz - 0.02], [bw - 0.1, py1 - py0, 0.1], null, 'f');
      M.emission = 0; M.nightEmission = 0; M.color = lattice;
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
      M.ribLantern(c, r, h, H('#C8402E'), o.lit == null ? 'night' : o.lit, C.gold, C.gold, 6, 0.2);
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


  function buildCityWall(len, o) {
    const C = PCOL;
    o = o || {};
    const variant = (o.variant | 0) & 1;
    {
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
      for (const [a, b] of runs) plinth(M, a, b, Z, PL);
      for (const bx of bxs) {
        plinth(M, bx - BW / 2 - 0.12, bx + BW / 2 + 0.12, Z + BD, PL);
        for (const s of [-1, 1]) { M.push().translate(bx + s * BW / 2, 0, Z + BD / 2).rotate(0, s * 90, 0); plinth(M, -BD / 2, BD / 2, 0, PL); M.pop(); }
      }
      M.color = PAL.stone; M.quad([-X, 0, -Z], [X, 0, -Z], [X, PL, -zs(PL)], [-X, PL, -zs(PL)], [0, bat, -1]);
      M.aoStrength = 0.35;
      // parapet and merlons (the buttresses carry theirs forward)
      const pc = P.mix(PAL.brick, PAL.rammed, 0.3), zt = zs(Hh), pz = zt - 0.22;
      M.color = pc;
      for (const [a, b] of runs) M.box([(a + b) / 2, Hh + 0.28, pz], [b - a, 0.56, 0.44], null, 'tfkrl');
      const mxs = [];
      for (const [a, b] of runs) { const n = Math.max(1, Math.floor((b - a) / 1.3)); for (let i = 0; i < n; i++) mxs.push(a + 0.65 + i * ((b - a) - 1.3) / Math.max(1, n - 1)); }
      merlonsMossy(M, mxs, Hh + 0.56, pz, [0.8, 0.44, 0.46], pc);
      for (const bx of bxs) {
        M.color = pc;
        M.box([bx, Hh + 0.28, pz + BD], [BW, 0.56, 0.44], null, 'tfkrl');
        for (const s of [-1, 1]) M.box([bx + s * (BW / 2 - 0.22), Hh + 0.28, pz + BD / 2], [0.44, 0.56, BD], null, 'tkrl');
        merlonsMossy(M, [bx - 0.65, bx + 0.65], Hh + 0.56, pz + BD, [0.8, 0.44, 0.46], pc, 2);
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
      return M;
    }
  }

  // 城门楼 (the pilot's approved gate tower): a battered rammed-earth gate base, a studded vermilion arch gate, merlons, a
  // timber hall under a slate 歇山 roof with tile ribs, ridge curls and a gold bird, ribbed lanterns (lit by night), banners.
  recipe('gate_tower', {
    group: 'building', label: '城门楼', keys: ['lit', 'color', 'name', 'hallName'], defaults: { lit: 'night', color: '#2f7d4a', name: '荆州', hallName: '宾阳楼' },
    build(o) {
      const r = buildGateTower(Object.assign({}, o, { flagColor: o.color }));
      return { ink: r.M, lamps: r.lamps.map(p => ({ pos: p, color: '#ffb46c', intensity: 1.0, range: 4.6, haloSize: 1.5 })), decals: r.decals, anchors: { parts: r.parts } };
    },
  });
  // 城墙 (the pilot's city wall): stone plinth, rammed-earth courses with formwork lifts and seams, 马面 buttresses, merlons
  // with moss, a paved wall-walk with an inner parapet and props (variant 0: weapon rack and crates; 1: banner, crates, jar).
  recipe('city_wall', {
    group: 'building', label: '城墙', keys: ['len', 'variant', 'color'], defaults: { len: 18, variant: 0, color: '#2f7d4a' }, seeds: 1,
    build(o) { return buildCityWall(o.len, { variant: o.variant, flagColor: o.color }); },
  });
  // 墙段: a plain wall run of any kind (parts.wall): rammed, brick, stone, mud or plaster, optional merlons.
  recipe('wall_segment', {
    group: 'building', label: '墙段', keys: ['kind', 'len', 'h', 'merlons'], defaults: { kind: 'brick', len: 6, h: 3, merlons: true },
    build(o) {
      const M = builder(o, o.kind, 0.06, 0.3, 1.0);
      wall(M, { len: o.len, h: o.h, thick: 1.0, kind: o.kind, batter: o.kind === 'rammed' ? 0.08 : 0.02, merlons: o.merlons });
      return M;
    },
  });
  // 汉式民居 (a Han house): a stone plinth, plastered walls in a dark timber frame, a plank door and lattice windows (lit by
  // night), a grey-tiled roof — gable (悬山) on even seeds, hip (庑殿) on odd ones.
  recipe('house_han', {
    group: 'building', label: '汉式民居', keys: ['roof', 'palette', 'lit'], defaults: { roof: null, palette: null, lit: 'night' }, lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 1.0), far = o.far;
      const W = [6, 5.4, 6.6, 5][b], D = [4, 3.8, 4.4, 3.6][b], Hw = 2.7, PLH = 0.35;
      const wallC = [PAL.plaster, H('#E8D8BC'), PAL.plasterWarm, H('#ECE2D0')][b], timber = H('#5A3E30');
      M.jitterAll = true;
      M.color = PAL.stone; M.box([0, PLH / 2, 0], [W + 0.4, PLH, D + 0.4], PAL.stoneLight, 'tfkrl');
      M.color = wallC; M.box([0, PLH + Hw / 2, 0], [W, Hw, D], null, 'fkrl');
      M.jitterAll = false;
      // timber frame: corner posts, a sill and an eave beam on the front, a middle post
      M.color = timber;
      for (const [x, z] of [[-W / 2, D / 2], [W / 2, D / 2], [-W / 2, -D / 2], [W / 2, -D / 2]]) M.box([x, PLH + Hw / 2, z], [0.2, Hw, 0.2], null, 'fkrl');
      M.box([0, PLH + Hw - 0.1, D / 2 + 0.02], [W + 0.2, 0.2, 0.06], null, 'f'); M.box([0, PLH + 0.1, D / 2 + 0.02], [W + 0.2, 0.18, 0.06], null, 'f');
      if (!far) for (const x of [-W / 4, W / 4]) M.box([x, PLH + Hw / 2, D / 2 + 0.02], [0.14, Hw, 0.05], null, 'f');
      door(M, { x: 0, z: D / 2, w: 1.0, h: 2.0, color: '#6E4632', frame: '#3E2A22' });
      M.push().translate(0, PLH, 0);
      if (!far) for (const s of [-1, 1]) windowLattice(M, { x: s * W * 0.33, y: 1.5, z: D / 2, w: 0.9, h: 0.75, lit: o.lit });
      else { M.color = H('#E9D7AE'); M.nightEmission = 0.85; for (const s of [-1, 1]) M.quad([s * W * 0.33 - 0.45, 1.12, D / 2 + 0.01], [s * W * 0.33 + 0.45, 1.12, D / 2 + 0.01], [s * W * 0.33 + 0.45, 1.88, D / 2 + 0.01], [s * W * 0.33 - 0.45, 1.88, D / 2 + 0.01], [0, 0, 1]); M.nightEmission = 0; }
      if (!far) windowLattice(M, { x: W / 2, y: 1.5, z: 0, w: 0.7, h: 0.6, lit: o.lit, frame: '#5A3426' });
      M.pop();
      const roof = o.roof || (b % 2 ? 'hip' : 'gable');
      hanRoof(M, { style: roof, w: W, d: D, y: PLH + Hw, over: 0.75, rise: D * 0.52, lift: 0.3, detail: far ? 'low' : 'mid', palette: o.palette || (b === 1 ? 'slate' : 'grey'), fascia: H('#5A3E30'), ornaments: !far, gableColor: wallC });
      return M;
    },
  });
  // 茅屋 (a thatched farmhouse): warm mud-plaster walls in a timber frame on a low stone footing, a thick thatch roof, a
  // door and a small lattice window.
  recipe('house_thatch', {
    group: 'building', label: '茅屋', keys: ['lit'], defaults: { lit: 'night' }, lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.35, 1.0), far = o.far;
      const W = [4.6, 4.2, 5.0, 4.4][b], D = [3.4, 3.2, 3.6, 3.3][b], Hw = 2.1;
      const wallC = P.hsv(H('#D8B88E'), (b - 1.5) * 4), timber = H('#6E5038');
      M.jitterAll = true;
      M.color = PAL.stoneDark; M.box([0, 0.15, 0], [W + 0.2, 0.3, D + 0.2], null, 'tfkrl');
      M.color = wallC; M.box([0, 0.3 + Hw / 2, 0], [W, Hw, D], null, 'fkrl');
      M.jitterAll = false;
      M.color = timber;
      for (const [x, z] of [[-W / 2, D / 2], [W / 2, D / 2], [-W / 2, -D / 2], [W / 2, -D / 2]]) M.box([x, 0.3 + Hw / 2, z], [0.18, Hw, 0.18], null, 'fkrl');
      M.box([0, 0.3 + Hw - 0.08, D / 2 + 0.02], [W, 0.16, 0.06], null, 'f');
      door(M, { x: -W * 0.18, z: D / 2, w: 0.9, h: 1.8, color: '#7A5A40', frame: '#4A3428' });
      M.push().translate(0, 0.3, 0);
      if (!far) windowLattice(M, { x: W * 0.25, y: 1.25, z: D / 2, w: 0.7, h: 0.6, lit: o.lit, bars: 1 });
      M.pop();
      thatchRoof(M, { w: W, d: D, y: 0.3 + Hw, over: 0.55, rise: D * 0.62, style: b % 2 ? 'hip' : 'gable', color: P.hsv(H('#C8A868'), (b - 1.5) * 3), gableColor: wallC });
      return M;
    },
  });
  // 地中海民居: a whitewashed house on a stone footing, a low terracotta hip roof, a round-arched blue door, small shuttered
  // windows and a flower pot — Rome, Greece, the Levant coast.
  recipe('house_med', {
    group: 'building', label: '地中海民居', keys: ['lit'], defaults: { lit: 'night' }, lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.3, 1.0), far = o.far;
      const W = [6, 5.2, 6.4, 5.6][b], D = [5, 4.6, 5.2, 4.8][b], Hw = b === 2 ? 5.2 : 3.3, white = H('#F2ECDE'), shutter = H6('#4E7EA8', '#5E8E6E', '#4E7EA8', '#B86A4A')[b];
      M.jitterAll = true;
      M.color = H('#B8AC98'); M.box([0, 0.25, 0], [W + 0.2, 0.5, D + 0.2], null, 'fkrl');
      M.color = white; M.box([0, 0.5 + Hw / 2, 0], [W, Hw, D], null, 'fkrl');
      M.jitterAll = false;
      // arched door, shuttered windows
      M.color = H('#4A3A34'); M.quad([-0.55, 0.5, D / 2 + 0.01], [0.55, 0.5, D / 2 + 0.01], [0.55, 2.2, D / 2 + 0.01], [-0.55, 2.2, D / 2 + 0.01], [0, 0, 1]);
      for (let i = 0; i < 5; i++) { const a0 = i / 5 * Math.PI, a1 = (i + 1) / 5 * Math.PI; M.triangleFacing([0, 2.2, D / 2 + 0.01], [Math.cos(a0) * 0.55, 2.2 + Math.sin(a0) * 0.55, D / 2 + 0.01], [Math.cos(a1) * 0.55, 2.2 + Math.sin(a1) * 0.55, D / 2 + 0.01], [0, 0, 1]); }
      M.color = shutter; M.push().translate(0, 0, D / 2 + 0.03); M.flat([[-0.48, 0.5], [0.48, 0.5], [0.48, 2.2], [0.24, 2.62], [-0.24, 2.62], [-0.48, 2.2]], 0.04); M.pop();
      const wins = Hw > 4 ? [[-W * 0.3, 1.9], [W * 0.3, 1.9], [-W * 0.3, 4.0], [0, 4.0], [W * 0.3, 4.0]] : [[-W * 0.3, 1.9], [W * 0.3, 1.9]];
      for (const [x, y] of wins) {
        M.color = H('#3E3430'); M.nightEmission = o.lit === 'night' ? 0.8 : 0; M.color = o.lit === 'night' ? H('#C8B08A') : H('#3E3430');
        M.quad([x - 0.32, y - 0.42, D / 2 + 0.01], [x + 0.32, y - 0.42, D / 2 + 0.01], [x + 0.32, y + 0.42, D / 2 + 0.01], [x - 0.32, y + 0.42, D / 2 + 0.01], [0, 0, 1]);
        M.nightEmission = 0;
        if (!far) { M.color = shutter; for (const s of [-1, 1]) M.box([x + s * 0.5, y, D / 2 + 0.04], [0.32, 0.88, 0.04], null, 'frl'); M.color = H('#D8CCB4'); M.box([x, y - 0.48, D / 2 + 0.06], [0.8, 0.07, 0.14], null, 'tf'); }
      }
      if (!far) { M.push().translate(W * 0.42, 0.5, D / 2 + 0.35); M.color = H('#B8643E'); M.lathe([[0.14, 0], [0.2, 0.32], [0.22, 0.36]], 7, false, true, false); M.softLump([0, 0.5, 0], [0.26, 0.2, 0.24], 0, 3, LP.canopyRamp(H('#9CC060'), H('#6E9A4E'), H('#3E6A44'), 0.35, 0.7), 0.15); M.color = H('#E05A6A'); for (let i = 0; i < 4; i++) gem(M, [Math.cos(i * 1.6) * 0.16, 0.62, Math.sin(i * 1.6) * 0.14 + 0.06], 0.045); M.pop(); }
      terracottaRoof(M, { w: W, d: D, y: 0.5 + Hw, over: 0.4, detail: far ? 'low' : 'mid' });
      return M;
    },
  });
  // 平顶土屋: an adobe house with a flat roof (parapet, protruding beams), small deep windows, a timber-lintel doorway and
  // an outside stair to the roof — the dry lands from Liang-zhou to Persia and India.
  recipe('house_mud', {
    group: 'building', label: '平顶土屋', keys: ['lit'], defaults: { lit: 'night' }, lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.35, 1.2), far = o.far;
      const W = [5, 4.6, 5.6, 4.8][b], D = [4.4, 4.2, 4.8, 4.4][b], Hw = 3.0, mud = P.hsv(H('#D2AE82'), (b - 1.5) * 3);
      M.jitterAll = true;
      M.color = mud;
      M.taperedBox([0, 0, 0], [W, Hw, D], 0.025);
      M.jitterAll = false;
      flatRoof(M, { w: W * 0.975, d: D * 0.975, y: Hw, parapet: 0.45, color: P.shade(mud, 1.03), beams: !far });
      // doorway with a lintel
      M.color = H('#3E322C'); M.quad([-0.5, 0, D / 2 + 0.01], [0.5, 0, D / 2 + 0.01], [0.5, 1.95, D / 2 + 0.01], [-0.5, 1.95, D / 2 + 0.01], [0, 0, 1]);
      M.color = WOOD.dark; M.box([0, 2.02, D / 2 + 0.05], [1.4, 0.14, 0.12], null, 'tbf');
      M.color = H('#7A5A3E'); M.box([0.08, 0.95, D / 2 - 0.05], [0.84, 1.9, 0.05], null, 'f');
      for (const x of [-W * 0.3, W * 0.3]) {
        M.color = o.lit === 'night' ? H('#C8A070') : H('#3E322C'); M.nightEmission = o.lit === 'night' ? 0.8 : 0;
        M.quad([x - 0.25, 1.5, D / 2 + 0.01], [x + 0.25, 1.5, D / 2 + 0.01], [x + 0.25, 2.0, D / 2 + 0.01], [x - 0.25, 2.0, D / 2 + 0.01], [0, 0, 1]);
        M.nightEmission = 0;
        if (!far) { M.color = WOOD.mid; M.box([x, 1.48, D / 2 + 0.05], [0.66, 0.06, 0.1], null, 'tf'); }
      }
      if (!far) {
        // outside stair along the right wall
        M.color = P.shade(mud, 0.95);
        for (let i = 0; i < 8; i++) M.box([W / 2 + 0.45, 0.19 + i * 0.37, -D / 2 + 0.5 + i * 0.4], [0.8, 0.38 + i * 0.37 * 0 + 0.0, 0.42], P.shade(mud, 1.05), 'tfkr');
        M.box([W / 2 + 0.45, 1.5, 0.2], [0.8, 3.0, 0.4], null, 'fkr');
        M.color = H('#B86A44'); M.lathe([[0.18, Hw + 0.05], [0.26, Hw + 0.35], [0.12, Hw + 0.6], [0.14, Hw + 0.66]], 7, false, true, true);
      }
      return M;
    },
  });
  // 穹顶殿 (a domed hall — Persia, India, the Kushan lands): a plastered cube with a tall pointed-arch portal (iwan) framed in
  // blue tile, a drum and a dome (onion on odd seeds), four corner pinnacles.
  recipe('dome_hall', {
    group: 'building', label: '穹顶殿', keys: ['lit'], defaults: { lit: 'night' }, lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.05, 0.3, 1.2), far = o.far;
      const S = 7, Hc = 4.6, cream = H('#E8DCC4'), tile = H6('#4E7EA8', '#3E8E8A', '#4E7EA8', '#B87A3A')[b], domeC = b % 2 ? H('#4E7EA8') : H('#E8E0D0');
      M.jitterAll = true;
      M.color = H('#C8BCA4'); M.box([0, 0.3, 0], [S + 0.8, 0.6, S + 0.8], H('#D8CCB4'), 'tfkrl');
      M.color = cream; M.box([0, 0.6 + Hc / 2, 0], [S, Hc, S], P.shade(cream, 1.04), 'tfkrl');
      M.jitterAll = false;
      // the iwan: a recessed pointed arch on the front, tile frame
      const aw = 1.5, ah = 3.6, z = S / 2 + 0.01;
      const arch = []; for (let i = 0; i <= 6; i++) { const a = i / 6 * Math.PI; arch.push([Math.cos(a) * aw * (a < Math.PI / 2 ? 1 : 1), 0.6 + ah - aw + Math.sin(a) * aw * 1.25]); }
      M.color = H('#2E3440'); M.quad([-aw, 0.6, z], [aw, 0.6, z], [aw, 0.6 + ah - aw, z], [-aw, 0.6 + ah - aw, z], [0, 0, 1]);
      for (let i = 0; i < 6; i++) M.triangleFacing([0, 0.6 + ah - aw, z], [arch[i][0], arch[i][1], z], [arch[i + 1][0], arch[i + 1][1], z], [0, 0, 1]);
      M.color = tile; M.box([0, 0.6 + Hc - 0.35, z + 0.03], [aw * 2 + 1.0, 0.3, 0.06], null, 'tf');
      for (const s of [-1, 1]) M.box([s * (aw + 0.32), 0.6 + (Hc - 0.5) / 2, z + 0.03], [0.3, Hc - 0.5, 0.06], null, 'frl');
      M.color = P.mix(PAL.paper, H('#F0C878'), 0.3); M.nightEmission = o.lit === 'night' ? 0.85 : 0;
      M.quad([-0.5, 0.6, z + 0.005], [0.5, 0.6, z + 0.005], [0.5, 2.6, z + 0.005], [-0.5, 2.6, z + 0.005], [0, 0, 1]);
      M.nightEmission = 0;
      // drum, dome, corner pinnacles
      dome(M, { c: [0, 0.6 + Hc, 0], r: 2.6, drumH: 0.9, shape: b % 2 ? 'onion' : 'hemi', color: domeC, drum: cream, finial: PAL.gold, sides: far ? 8 : 12 });
      if (!far) for (const [x, zz] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) { M.color = cream; M.cylinder([x * S / 2, 0.6 + Hc, zz * S / 2], 0.32, 0.28, 0.8, 6, false, false, true); M.color = tile; M.cone([x * S / 2, 0.6 + Hc + 0.8, zz * S / 2], 0.34, 0.7, 6); }
      return M;
    },
  });
  // 佛塔 (stupa, as in Gandhara and Taxila): square terraces, a railed drum, a white dome, the harmika and a three-tier parasol.
  recipe('stupa', {
    group: 'building', label: '佛塔', lod: false,
    build(o) {
      const M = builder(o, '', 0.04, 0.3, 1.2);
      stupa(M, { r: 3 });
      M.color = H('#B8AC94');
      for (let i = 0; i < 4; i++) { const a = i * Math.PI / 2; M.box([Math.cos(a) * 4.3, 0.45, Math.sin(a) * 4.3], [i % 2 ? 0.4 : 1.6, 0.9, i % 2 ? 1.6 : 0.4], null, 'tfkrl'); }
      return M;
    },
  });
  // 望楼 (a Han watchtower, after the glazed pottery towers): a stone base, three timber storeys with balconies and pent
  // eaves, a hip roof on top, a banner and a ladder.
  recipe('watchtower', {
    group: 'building', label: '望楼', keys: ['color', 'lit'], defaults: { color: '#2f7d4a', lit: 'night' }, lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.06, 0.3, 1.2), far = o.far, timber = H('#6E4A36'), red = PAL.hanRed, wallC = PAL.plaster;
      M.jitterAll = true;
      M.color = PAL.stone; M.taperedBox([0, 0, 0], [4.2, 1.6, 4.2], 0.08);
      M.jitterAll = false;
      const stories = [[1.6, 3.0, 2.6], [4.4, 2.5, 2.2], [6.9, 2.3, 1.9]];
      stories.forEach(([y0, h, w], i) => {
        // floor, columns, walls with windows, balcony railing, pent eave
        M.color = timber; M.box([0, y0 + 0.1, 0], [w * 2 + 0.6, 0.2, w * 2 + 0.6], null, 'tbfkrl');
        columns(M, [[-w, -w], [w, -w], [w, w], [-w, w]], y0 + 0.2, y0 + h, { r: 0.12, color: red });
        M.color = wallC; M.box([0, y0 + 0.2 + (h - 0.4) / 2, 0], [w * 2 - 0.3, h - 0.4, w * 2 - 0.3], null, 'fkrl');
        if (!far) windowLattice(M, { x: 0, y: y0 + 0.2 + (h - 0.4) * 0.55, z: w - 0.15, w: 1.0, h: 0.8, lit: o.lit });
        if (!far) { M.color = red; M.beam([-w - 0.3, y0 + 0.75, w + 0.3], [w + 0.3, y0 + 0.75, w + 0.3], 0.06, 0.06); for (const s of [-1, 1]) M.beam([s * (w + 0.3), y0 + 0.75, -w - 0.3], [s * (w + 0.3), y0 + 0.75, w + 0.3], 0.06, 0.06); }
        if (i < stories.length - 1) hanRoof(M, { style: 'hip', w: w * 2, d: w * 2, y: y0 + h, over: 0.55, rise: 0.55, lift: 0.25, detail: far ? 'low' : 'mid', palette: 'grey', ornaments: false, fascia: red });
      });
      const top = stories[2];
      hanRoof(M, { style: 'hip', w: top[2] * 2, d: top[2] * 2, y: top[0] + top[1], over: 0.7, rise: 1.4, lift: 0.4, detail: far ? 'low' : 'mid', palette: 'grey', ornaments: !far, fascia: red });
      if (!far) {
        M.color = WOOD.dark; M.segment([1.8, top[0] + top[1], 1.8], [1.8, top[0] + top[1] + 3.0, 1.8], 0.04, 0.035, 4);
        M.windGradient = true; M.windY0 = top[0] + top[1] + 2.0; M.windY1 = top[0] + top[1] + 3.0; M.wind = 1.2;
        M.color = H(o.color); M.strip([1.84, top[0] + top[1] + 2.75, 1.8], [2.8, top[0] + top[1] + 2.7, 1.84], 0.7, 0.62, [0, 0, 1], 0.03);
        M.wind = 0; M.windGradient = false;
        M.color = WOOD.mid; for (const s of [-1, 1]) M.beam([s * 0.3, 0, 2.6], [s * 0.3, 1.65, 2.2], 0.06, 0.06);
        for (let i = 1; i < 6; i++) M.beam([-0.3, i * 0.28, 2.6 - i * 0.07], [0.3, i * 0.28, 2.6 - i * 0.07], 0.04, 0.04);
      }
      return M;
    },
  });
  // 圆塔 (a round stone tower of the western lands): ashlar courses, arrow slits, a crenellated top and, on odd seeds, a
  // conical slate roof; a door at the foot.
  recipe('tower_round', {
    group: 'building', label: '圆塔', lod: true,
    build(o) {
      const b = o.bucket, M = builder(o, '', 0.07, 0.3, 1.2), far = o.far, R = 2.2, Ht = 8, sides = far ? 8 : 12;
      const st = P.hsv(H('#B8AE9C'), (b - 1.5) * 3), stD = P.shade(st, 0.88);
      const prof = [], cols = [];
      const rows = far ? 3 : 8;
      for (let i = 0; i <= rows; i++) { const y = Ht * i / rows; prof.push([R * (1 - 0.04 * i / rows) + (i === 0 ? 0.2 : 0), y]); cols.push(i % 2 ? st : stD); }
      M.color = st; M.lathe(prof, sides, false, false, true, cols, 15);
      M.color = P.shade(st, 1.04); M.cylinder([0, Ht, 0], R * 1.06, R * 1.06, 0.25, sides, false, true, true);
      const nM = far ? 6 : 10;
      for (let i = 0; i < nM; i++) { const a = i / nM * Math.PI * 2; M.color = i % 2 ? st : P.shade(st, 1.05); M.box([Math.cos(a) * R * 0.98, Ht + 0.55, Math.sin(a) * R * 0.98], [0.62, 0.6, 0.4], null, 'tfkrl'); }
      if (!far) { M.color = H('#2E2A2C'); for (const [a, y] of [[90, 3], [50, 5.2], [130, 5.2], [90, 6.6]]) { const r = a * D2R, c = [Math.cos(r) * R * 0.97, y, Math.sin(r) * R * 0.97]; M.push().translate(c).rotate(0, 90 - a, 0); M.quad([-0.07, -0.4, 0.06], [0.07, -0.4, 0.06], [0.07, 0.4, 0.06], [-0.07, 0.4, 0.06], [0, 0, 1]); M.pop(); } }
      gate(M, { w: 1.1, h: 2.2, z: R * 0.96, kind: 'arch', door: '#6E4632', studs: false });
      if (b % 2) { M.color = PAL.tileSlate; M.lathe([[R * 1.15, Ht + 0.9], [R * 0.6, Ht + 2.6], [0, Ht + 4.0]], sides, false, true, false, [PAL.tileSlateDark, PAL.tileSlate, P.shade(PAL.tileSlate, 1.1)]); }
      return M;
    },
  });

  Props.version = '1.0';
})();
