'use strict';
/* ==========================================================================
   三国志II 霸王的大陆 · 网页版 · 共享工具
   全局命名空间 SG。数学函数与 Unity 的 Mathf / Random 语义一致，
   以便从 C# 逐行移植。
   ========================================================================== */
(function () {
  const SG = (window.SG = window.SG || {});

  // ---------------------------------------------------------------- Mathf --
  const M = {
    PI: Math.PI,
    clamp(v, a, b) { return v < a ? a : v > b ? b : v; },
    clamp01(v) { return v < 0 ? 0 : v > 1 ? 1 : v; },
    lerp(a, b, t) { return a + (b - a) * M.clamp01(t); },
    lerpUnclamped(a, b, t) { return a + (b - a) * t; },
    inverseLerp(a, b, v) { return a === b ? 0 : M.clamp01((v - a) / (b - a)); },
    // Unity Mathf.SmoothStep(from, to, t)
    smoothStep(from, to, t) { t = M.clamp01(t); t = -2 * t * t * t + 3 * t * t; return to * t + from * (1 - t); },
    // Unity Mathf.RoundToInt 使用银行家舍入；这里与之一致
    roundToInt(f) {
      const r = Math.round(f);
      if (Math.abs(f % 1) === 0.5) return 2 * Math.round(f / 2);
      return r;
    },
    ceilToInt(f) { return Math.ceil(f); },
    floorToInt(f) { return Math.floor(f); },
    // C# 整数除法（向零截断）。移植 C# 的 int / int 时必须使用它。
    idiv(a, b) { return Math.trunc(a / b); },
    repeat(t, len) { return t - Math.floor(t / len) * len; },
    lerpAngle(a, b, t) {
      let d = M.repeat(b - a, 360);
      if (d > 180) d -= 360;
      return a + d * M.clamp01(t);
    },
    deg2rad: Math.PI / 180,
    rad2deg: 180 / Math.PI,
  };
  SG.M = M;

  // ---------------------------------------------------------- Perlin 噪声 --
  // 与 Unity Mathf.PerlinNoise 相同的接口与输出尺度：原始噪声按 Unity 的
  // (n + 0.69) / 1.483 归一化（整数坐标处为 0.4652731，均值约 0.465），
  // 不做截断——与 Unity 一样，返回值可能略小于 0 或略大于 1。
  // C# 中的阈值（如 BattleModel 的 0.72 / 0.6 / 0.62）都基于这一尺度。
  const perm = new Uint8Array(512);
  (function () {
    const p = [];
    for (let i = 0; i < 256; i++) p.push(i);
    let s = 1337;
    for (let i = 255; i > 0; i--) {
      s = (s * 16807) % 2147483647;
      const j = s % (i + 1);
      const t = p[i]; p[i] = p[j]; p[j] = t;
    }
    for (let i = 0; i < 512; i++) perm[i] = p[i & 255];
  })();
  function fade(t) { return t * t * t * (t * (t * 6 - 15) + 10); }
  function grad(h, x, y) {
    switch (h & 7) {
      case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
      case 4: return x; case 5: return -x; case 6: return y; default: return -y;
    }
  }
  M.perlinNoise = function (x, y) {
    const xi = Math.floor(x), yi = Math.floor(y);
    const X = xi & 255, Y = yi & 255;
    const xf = x - xi, yf = y - yi;
    const u = fade(xf), v = fade(yf);
    const aa = perm[perm[X] + Y], ab = perm[perm[X] + Y + 1], ba = perm[perm[X + 1] + Y], bb = perm[perm[X + 1] + Y + 1];
    const x1 = grad(aa, xf, yf) + (grad(ba, xf - 1, yf) - grad(aa, xf, yf)) * u;
    const x2 = grad(ab, xf, yf - 1) + (grad(bb, xf - 1, yf - 1) - grad(ab, xf, yf - 1)) * u;
    const n = x1 + (x2 - x1) * v; // 约 [-1, 1]
    return (n + 0.69) / 1.483;
  };

  // --------------------------------------------------------------- Random --
  // UnityEngine.Random 的语义：rangeInt(min, max) 不含 max；rangeFloat 含两端
  let rng = Math.random;
  SG.Random = {
    value() { return rng(); },
    rangeInt(min, maxExclusive) { if (maxExclusive <= min) return min; return min + Math.floor(rng() * (maxExclusive - min)); },
    rangeFloat(min, max) { return min + rng() * (max - min); },
    // 测试用：可复现的随机数
    seed(s) {
      let a = s >>> 0;
      rng = function () { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
    },
    reset() { rng = Math.random; },
  };
  // System.Random(seed) 的替代：返回 nextDouble() / next(maxExclusive) / next(min, maxExclusive)
  SG.SeededRandom = function (seed) {
    let a = (seed | 0) ^ 0x9e3779b9;
    const f = function () { a |= 0; a = (a + 0x6D2B79F5) | 0; let t = Math.imul(a ^ (a >>> 15), 1 | a); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
    return {
      nextDouble: f,
      next(a1, b1) { if (b1 === undefined) return Math.floor(f() * a1); return a1 + Math.floor(f() * (b1 - a1)); },
    };
  };

  // ------------------------------------------------------------- 异步计时 --
  // 协程 → async/await
  SG.sleep = function (ms) { return new Promise(r => setTimeout(r, ms)); };
  SG.wait = function (seconds) { return SG.sleep(seconds * 1000); };
  SG.frame = function () { return new Promise(r => requestAnimationFrame(() => r())); };
  // 按帧推进的动画：fn(t) 其中 t 从 0 到 1，持续 seconds 秒
  SG.animate = async function (seconds, fn) {
    const start = performance.now();
    for (;;) {
      const t = Math.min(1, (performance.now() - start) / 1000 / seconds);
      fn(t);
      if (t >= 1) return;
      await SG.frame();
    }
  };

  // ------------------------------------------------------------- 颜色工具 --
  SG.hexToRgb = function (hex) {
    const h = hex.replace('#', '');
    const n = parseInt(h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6), 16);
    return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
  };
  SG.rgbToHex = function (r, g, b) {
    const c = v => Math.round(M.clamp01(v) * 255).toString(16).padStart(2, '0');
    return '#' + c(r) + c(g) + c(b);
  };
  SG.esc = function (s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); };

  // ------------------------------------------------------------- 坐标约定 --
  // Unity 为左手系（z 向北）。three.js 为右手系。
  // 约定：世界坐标 three = (unityX, unityY, -unityZ)。地图坐标 (mapX, mapY) 对应 unity (x, *, z=mapY)。
  SG.U = function (x, y, z) { return new THREE.Vector3(x, y, -z); };
  // Unity 绕 y 轴旋转角（度）→ three.js rotation.y（弧度）
  SG.yawToThree = function (unityDegrees) { return -unityDegrees * M.deg2rad; };

  SG.isTouch = (typeof window !== 'undefined') && (('ontouchstart' in window) || (window.matchMedia && matchMedia('(pointer: coarse)').matches));
})();
