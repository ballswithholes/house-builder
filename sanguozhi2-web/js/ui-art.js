'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版界面 · 程序化材质（UI-V4 §3.3；设计参考 design-v4/mockups/kit.js 的 K.init）
   「漆 · 纸 · 铜 · 灯」：宣纸、漆木、绢、铜绿四种纹理画在 Canvas 里（空闲时分片生成，每片 ≤ 8 ms），连同铜包角（如意云头）、
   毛笔横线、进度条笔触遮罩、锁印写成 :root 的 CSS 变量；SVG 滤镜 #sg-brush-edge（印章蚀刻边）、#sg-brush-ink（大字干笔）。
   纹理未生成之前界面用平涂底色（首帧不会空白）。装饰随机数一律用固定种子的 xorshift，不碰 SG.Random。

   SG.UIArt.init() → Promise（幂等；生成完毕时兑现）     SG.UIArt.ready（布尔）    SG.UIArt.stats → { ms, slices, bytes }
   SG.UIArt.lantern(size, lit) → SVG 字符串（顶梁的挂灯）  SG.UIArt.seal(text, { round, yin, size }) → HTML（朱印）
   SG.UIArt.quiver(n, used) → SVG 字符串（令箭筒：n 支，已用 used 支）
   SG.UIArt.rng(seed) → () => [0, 1)（装饰用）
   CSS 变量：--tx-paper --tx-lacquer --tx-silk --tx-patina --cap-tl --cap-tr --cap-br --cap-bl --brush-line --brush-under --brush-mask --lock-ico
   ========================================================================== */
(function () {
  const SG = window.SG;

  function rng(seed) { let s = (seed >>> 0) || 1; return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return (s & 0xffffff) / 16777216; }; }
  const now = () => (typeof performance !== 'undefined' ? performance.now() : Date.now());

  // ------------------------------------------------------------------ 纹理（生成器：每次 next() 只画一小段，调度器按 6 ms 一片切开）--
  // 斑驳的径向渐变只在越过边缘时才画绕回的副本（平铺无缝）
  function wraps(x, y, rad, w, h) {
    const out = [];
    for (const dx of [-w, 0, w]) { if (x - rad + dx >= w || x + rad + dx <= 0) continue; for (const dy of [-h, 0, h]) { if (y - rad + dy >= h || y + rad + dy <= 0) continue; out.push([dx, dy]); } }
    return out;
  }
  function* paperGen(g, w, h) {
    const r = rng(11);
    g.fillStyle = '#f3e7cc'; g.fillRect(0, 0, w, h);
    yield;
    for (let i = 0; i < 60; i++) {           // 斑驳（越过边缘的部分绕回，平铺无缝）
      const x = r() * w, y = r() * h, rad = 20 + r() * 60;
      const gr = g.createRadialGradient(x, y, 0, x, y, rad);
      const a = 0.035 + r() * 0.04, warm = r() < 0.5;
      gr.addColorStop(0, warm ? `rgba(196,160,100,${a})` : `rgba(255,250,235,${a})`); gr.addColorStop(1, 'rgba(0,0,0,0)');
      g.fillStyle = gr;
      for (const [dx, dy] of wraps(x, y, rad, w, h)) { g.setTransform(1, 0, 0, 1, dx, dy); g.fillRect(x - rad, y - rad, rad * 2, rad * 2); }
      g.setTransform(1, 0, 0, 1, 0, 0);
      if (i % 2 === 1) yield;
    }
    g.lineCap = 'round';
    for (let i = 0; i < 260; i++) {          // 纤维
      const x = r() * w, y = r() * h, len = 4 + r() * 18, a = r() * Math.PI;
      g.strokeStyle = r() < 0.6 ? `rgba(150,120,80,${0.06 + r() * 0.08})` : `rgba(255,255,245,${0.1 + r() * 0.12})`;
      g.lineWidth = 0.4 + r() * 0.7;
      g.beginPath(); g.moveTo(x, y);
      g.quadraticCurveTo(x + Math.cos(a) * len * 0.5 + (r() - 0.5) * 4, y + Math.sin(a) * len * 0.5 + (r() - 0.5) * 4, x + Math.cos(a) * len, y + Math.sin(a) * len);
      g.stroke();
      if (i % 20 === 19) yield;
    }
    for (let i = 0; i < 90; i++) { g.fillStyle = `rgba(110,80,50,${0.08 + r() * 0.12})`; g.fillRect(r() * w, r() * h, 0.8 + r(), 0.8 + r()); }
  }
  function* lacquerGen(g, w, h) {
    const r = rng(23);
    g.fillStyle = '#241816'; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 220; i++) {          // 竖向刷痕
      const x = r() * w, a = 0.02 + r() * 0.05, light = r() < 0.5;
      g.strokeStyle = light ? `rgba(120,70,50,${a})` : `rgba(0,0,0,${a * 1.4})`;
      g.lineWidth = 0.5 + r() * 2.2;
      g.beginPath(); g.moveTo(x, 0); g.bezierCurveTo(x + (r() - 0.5) * 8, h * 0.33, x + (r() - 0.5) * 8, h * 0.66, x, h); g.stroke();
      if (i % 15 === 14) yield;
    }
    const gr = g.createLinearGradient(0, 0, w, h);
    gr.addColorStop(0, 'rgba(255,200,150,.03)'); gr.addColorStop(0.5, 'rgba(255,200,150,0)'); gr.addColorStop(1, 'rgba(255,200,150,.025)');
    g.fillStyle = gr; g.fillRect(0, 0, w, h);
    for (let i = 0; i < 40; i++) { g.fillStyle = `rgba(160,110,70,${0.05 + r() * 0.06})`; g.fillRect(r() * w, r() * h, 1, 1); }
  }
  function* silkGen(g, w, h) {
    g.clearRect(0, 0, w, h);
    g.lineWidth = 1;
    for (let i = -h; i < w; i += 3) { g.strokeStyle = 'rgba(255,230,200,.07)'; g.beginPath(); g.moveTo(i, 0); g.lineTo(i + h, h); g.stroke(); }
    for (let i = 0; i < w + h; i += 3) { g.strokeStyle = 'rgba(0,0,0,.06)'; g.beginPath(); g.moveTo(i, 0); g.lineTo(i - h, h); g.stroke(); }
  }
  function* patinaGen(g, w, h) {
    const r = rng(5);
    g.clearRect(0, 0, w, h);
    for (let i = 0; i < 26; i++) {
      const x = r() * w, y = r() * h, rad = 2 + r() * 7;
      const gr = g.createRadialGradient(x, y, 0, x, y, rad);
      gr.addColorStop(0, 'rgba(78,140,134,.9)'); gr.addColorStop(1, 'rgba(78,140,134,0)');
      g.fillStyle = gr; g.fillRect(x - rad, y - rad, rad * 2, rad * 2);
    }
  }

  // ------------------------------------------------------------------ SVG 小件 --
  const svgUrl = s => `url("data:image/svg+xml,${encodeURIComponent(s)}")`;
  // 铜包角：L 形铜片，两臂末端卷成如意云头
  function cap(rot) {
    const p = 'M1.5 1.5 H17 Q22.5 1.5 21.6 6 Q20.6 10 16.6 9 Q13.6 8 14.4 5.6 Q15.2 4 16.8 4.8 L16.2 6.2 Q15.7 7.1 16.9 7.2 Q18.9 7.3 18.9 5.2 Q18.9 3.9 17 3.9 H6.2 Q3.9 3.9 3.9 6.2 V17 Q3.9 18.9 5.2 18.9 Q7.3 18.9 7.2 16.9 Q7.1 15.7 6.2 16.2 L4.8 16.8 Q4 15.2 5.6 14.4 Q8 13.6 9 16.6 Q10 20.6 6 21.6 Q1.5 22.5 1.5 17 Z';
    return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><linearGradient id="b" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f0c98a"/><stop offset=".45" stop-color="#b8834a"/><stop offset="1" stop-color="#6e4a28"/></linearGradient></defs><g transform="rotate(${rot} 12 12)"><path d="${p}" fill="url(#b)" stroke="#241a21" stroke-width="1.1" stroke-linejoin="round"/><circle cx="3.2" cy="3.2" r="1" fill="#fbe2b0"/></g></svg>`);
  }
  const BRUSH_LINE = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 8" preserveAspectRatio="none"><path d="M2 4.6 C40 2.6 90 2.4 150 3.2 S300 3.6 398 2.8 L397 4.4 C330 5.6 260 5.4 200 5.2 S60 6.4 3 5.6 Z" fill="#241a21"/><path d="M330 3.4 L398 2.9 L396 3.6 Z M2 5 L30 4.3 L4 5.6Z" fill="#241a21" opacity=".5"/></svg>');
  const BRUSH_LINE_LIGHT = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 8" preserveAspectRatio="none"><path d="M2 4.6 C40 2.6 90 2.4 150 3.2 S300 3.6 398 2.8 L397 4.4 C330 5.6 260 5.4 200 5.2 S60 6.4 3 5.6 Z" fill="#e8b54a"/></svg>');
  const BRUSH_UNDER = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 6" preserveAspectRatio="none"><path d="M1 3.5 C20 1.6 60 1.4 99 2.6 L98 4.2 C60 4.6 25 5 2 4.8Z" fill="#e8b54a"/></svg>');
  // 进度条的笔触遮罩：头圆尾飞白
  const BRUSH_MASK = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 10" preserveAspectRatio="none"><path d="M3 1.5 Q0 5 3 8.6 C60 9.6 140 9 197 8 Q199.5 6 198 4 Q196 2.6 194 3.2 Q190 1.4 186 2.2 C130 .8 60 .6 3 1.5Z" fill="#000"/></svg>');
  const LOCK_ICO = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect x="2" y="2" width="12" height="12" rx="2" fill="#8a2418" stroke="#241a21" stroke-width="1.2"/><path d="M5.5 7.5 V6 a2.5 2.5 0 0 1 5 0 V7.5 M4.8 7.5 h6.4 v4 h-6.4z" fill="none" stroke="#f3e7cc" stroke-width="1.2"/></svg>');
  // 「继续」的笔尖（say 卡右下角，取代 ▼）
  const CONT_ICO = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path d="M4 6 Q12 10 20 6 Q14 14 12 21 Q10 14 4 6Z" fill="#8a2418" stroke="#241a21" stroke-width="1.2" stroke-linejoin="round"/></svg>');

  function filters() {
    if (typeof document === 'undefined' || document.getElementById('sg-filters')) return;
    const d = document.createElement('div');
    d.id = 'sg-filters';
    d.setAttribute('aria-hidden', 'true');
    d.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden;pointer-events:none';
    d.innerHTML = '<svg width="0" height="0">' +
      '<filter id="sg-brush-edge" x="-5%" y="-5%" width="110%" height="110%"><feTurbulence type="fractalNoise" baseFrequency=".9" numOctaves="2" seed="3"/><feDisplacementMap in="SourceGraphic" scale="1.6"/></filter>' +
      '<filter id="sg-brush-ink" x="-5%" y="-10%" width="110%" height="120%"><feTurbulence type="fractalNoise" baseFrequency=".65" numOctaves="2" seed="8" result="n"/><feDisplacementMap in="SourceGraphic" in2="n" scale="2.2" result="d"/><feMorphology in="d" operator="dilate" radius=".25"/></filter>' +
      '</svg>';
    (document.body || document.documentElement).appendChild(d);
  }

  // ------------------------------------------------------------------ 挂灯、令箭筒、朱印 --
  function lantern(size, lit) {
    const h = size || 46, w = Math.round(h * 22 / 46);
    return `<svg viewBox="0 0 22 46" width="${w}" height="${h}" aria-hidden="true"><path d="M11 0 V8" stroke="#241a21" stroke-width="1.5"/><rect x="6" y="7" width="10" height="4" rx="1" fill="#e8b54a" stroke="#241a21" stroke-width="1.2"/>` +
      `<ellipse cx="11" cy="22" rx="10" ry="12" fill="${lit ? '#ff8a4a' : '#c8402e'}" stroke="#241a21" stroke-width="1.5"/><path d="M11 10 V34 M5 13 Q2 22 5 31 M17 13 Q20 22 17 31" stroke="#8a2418" stroke-width="1" fill="none"/>` +
      `${lit ? '<ellipse cx="11" cy="22" rx="5" ry="7" fill="#ffd27a" opacity=".55"/>' : ''}<rect x="6" y="33" width="10" height="4" rx="1" fill="#e8b54a" stroke="#241a21" stroke-width="1.2"/><path d="M11 37 V46" stroke="#c8382c" stroke-width="2.5"/></svg>`;
  }
  // 令箭筒：每枚令牌一支箭（至多 10），已用的画成虚线空插槽；筒随支数加宽
  function quiver(n, used) {
    n = Math.max(1, Math.min(10, n | 0)); used = Math.max(0, Math.min(n, used | 0));
    const w = Math.max(40, 16 + n * 8);
    let s = `<svg class="sg-quiver-svg" viewBox="0 0 ${w} 44" width="${w}" height="44" aria-hidden="true"><g stroke="#241a21" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round">`;
    for (let i = 0; i < n; i++) {
      const x = (w - (n - 1) * 8) / 2 + i * 8, spent = i >= n - used;
      s += spent ? `<path class="arrow spent" d="M${x} 20 V15" stroke="#8d7d6a" stroke-dasharray="2 2"/>`
        : `<g class="arrow" data-i="${i}"><path d="M${x} 22 V5" stroke-width="2.6"/><path d="M${x} 22 V5" stroke="#d2a06a" stroke-width="1"/><path d="M${x - 4} 4 L${x} 1 L${x + 4} 4 L${x + 4} 9 L${x} 7 L${x - 4} 9Z" fill="#c8382c"/></g>`;
    }
    s += `<path d="M7 18 H${w - 7} L${w - 9} 42 H9Z" fill="#7a2419"/><path d="M7 18 H${w - 7} L${w - 7.6} 24 H7.6Z" fill="#b23b2a"/><path d="M9 30 H${w - 9}" stroke="#e8b54a" stroke-width="1.6"/></g></svg>`;
    return s;
  }
  function seal(text, opts) {
    opts = opts || {};
    const esc = SG.esc || (x => String(x));
    return `<span class="sg-sealmark${opts.round ? ' round' : ''}${opts.yin ? ' yin' : ''}"${opts.size ? ` style="font-size:${opts.size}"` : ''}>${esc(text)}</span>`;
  }

  // ------------------------------------------------------------------ 初始化（空闲分片）--
  let promise = null;
  const UIArt = {
    ready: false,
    stats: { ms: 0, slices: 0, bytes: 0, maxSlice: 0, list: [] },
    rng, lantern, quiver, seal, svgUrl,
    init() {
      if (promise) return promise;
      if (typeof document === 'undefined') { promise = Promise.resolve(false); return promise; }
      const root = document.documentElement.style;
      // 不需要画的小件立即写入
      root.setProperty('--cap-tl', cap(0)); root.setProperty('--cap-tr', cap(90)); root.setProperty('--cap-br', cap(180)); root.setProperty('--cap-bl', cap(270));
      root.setProperty('--brush-line', BRUSH_LINE); root.setProperty('--brush-line-light', BRUSH_LINE_LIGHT); root.setProperty('--brush-under', BRUSH_UNDER);
      root.setProperty('--brush-mask', BRUSH_MASK); root.setProperty('--lock-ico', LOCK_ICO); root.setProperty('--cont-ico', CONT_ICO);
      try { filters(); } catch (e) { /* 无 DOM */ }
      // 纹理：生成器分片画（每片 ≤ 6 ms），编码用 toBlob（在主线程之外压缩 PNG），blob: 地址写进 CSS 变量
      const jobs = [['--tx-paper', 256, 256, paperGen], ['--tx-lacquer', 256, 256, lacquerGen], ['--tx-silk', 64, 64, silkGen], ['--tx-patina', 64, 64, patinaGen]];
      const idle = typeof requestIdleCallback === 'function' ? fn => requestIdleCallback(fn, { timeout: 400 }) : fn => setTimeout(fn, 16);
      promise = new Promise(resolve => {
        let cur = null, pending = 0, drawn = false;
        const done = () => { if (!drawn || pending > 0 || UIArt.ready) return; UIArt.ready = true; document.documentElement.classList.add('sg-art-ready'); resolve(true); };
        const encode = (name, c) => {
          const set = url => { try { root.setProperty(name, `url(${url})`); } catch (e) { /* 忽略 */ } };
          if (c.toBlob && typeof URL !== 'undefined' && URL.createObjectURL) {
            pending++;
            c.toBlob(b => { try { if (b) { UIArt.stats.bytes += b.size; set(URL.createObjectURL(b)); } } catch (e) { /* 忽略 */ } pending--; done(); }, 'image/png');
          } else { try { const u = c.toDataURL('image/png'); UIArt.stats.bytes += u.length; set(u); } catch (e) { /* 无画布 */ } }
        };
        const step = () => {
          const t0 = now();
          try {
            for (;;) {
              if (!cur) {
                const j = jobs.shift();
                if (!j) { drawn = true; break; }
                const c = document.createElement('canvas'); c.width = j[1]; c.height = j[2];
                // willReadFrequently：软件画布（不走 GPU 合成）——小纹理在 CPU 上画得快，也不会在 GPU 忙时卡住主线程
                const g = c.getContext('2d', { willReadFrequently: true });
                if (!g) continue;
                cur = { name: j[0], c, it: j[3](g, j[1], j[2]) };
              }
              if (cur.it.next().done) { encode(cur.name, cur.c); cur = null; }
              if (now() - t0 > 6) break;
            }
          } catch (e) { cur = null; }
          const dt = now() - t0;
          UIArt.stats.ms += dt; UIArt.stats.slices++; UIArt.stats.maxSlice = Math.max(UIArt.stats.maxSlice, dt); if (UIArt.stats.list.length < 400) UIArt.stats.list.push(Math.round(dt * 10) / 10);
          if (drawn) done(); else idle(step);
        };
        idle(step);
      });
      return promise;
    },
  };
  SG.UIArt = UIArt;
})();
