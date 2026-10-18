'use strict';
/* ==========================================================================
   三国志-霸王的世界 · 第四版界面 · 设计样张共用脚本（UI-V4 §3–§4）
   K.init()                → 生成纹理 / 包角 / 笔触 / 滤镜，写入 :root 的 CSS 变量
   K.icon(id, size?)       → SVG 字符串（64×64 视窗，墨线 2.5，平涂分面）——图标语言见 UI-V4 §3.6
   K.face(gen, size, cls)  → 立绘框 HTML（用游戏本身的 SG.Portrait 生成头像）
   K.map(canvas, opts)     → 画一张 Lanternvale 风格的战略地图背景（低多边形山丘、拼布田、软团树、河、城）
   K.cutout(src)           → Promise<dataURL>：去掉美术试点渲染图的棋盘格地面（边缘泛洪），留下人物
   K.rng(seed)             → 可复现的随机数
   这些只为样张服务；正式实现的模块划分见 UI-V4.md §17。
   ========================================================================== */
(function () {
  const K = (window.K = {});
  const C = {
    ink: '#241a21', paper: '#f3e7cc', paper2: '#e3d0a8', zhu: '#c8382c', zhuHi: '#e0604a', zhuLo: '#8a2418',
    gold: '#e8b54a', goldHi: '#f8dc8e', goldLo: '#a8742e', bronze: '#a0703f', bronzeHi: '#d2a06a', bronzeLo: '#5e3f22',
    jade: '#4f9a76', jadeHi: '#8fd4a8', ochre: '#c99a2e', ochreHi: '#e2bd5c', indigo: '#3b4f7a', indigoHi: '#5f78a8',
    teal: '#3aa5a0', plum: '#8a5fc0', plumHi: '#b48ae0', ember: '#e07a3a', amber: '#d9933a', tile: '#808da6', tileHi: '#a6b2c8',
    wood: '#8c6a48', woodHi: '#b08860', woodLo: '#5a3e2c', steel: '#c9d2da', steelHi: '#eef2f5', steelLo: '#8b98a6',
    lq: '#2a2430', lqHi: '#4a3a44', cream: '#eadfc4', green: '#3f7f4a', greenHi: '#6aa65e', sky: '#8fb6d0',
  };
  K.C = C;
  K.rng = function (seed) { let s = (seed >>> 0) || 1; return () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return (s & 0xffffff) / 16777216; }; };

  // ------------------------------------------------------------ 纹理 --
  function tex(w, h, draw) { const c = document.createElement('canvas'); c.width = w; c.height = h; draw(c.getContext('2d'), w, h); return c.toDataURL('image/png'); }
  function paperTex() {
    return tex(256, 256, (g, w, h) => {
      const r = K.rng(11);
      g.fillStyle = '#f3e7cc'; g.fillRect(0, 0, w, h);
      for (let i = 0; i < 60; i++) { // 斑驳
        const x = r() * w, y = r() * h, rad = 20 + r() * 60;
        const gr = g.createRadialGradient(x, y, 0, x, y, rad);
        const a = .035 + r() * .04; const warm = r() < .5;
        gr.addColorStop(0, warm ? `rgba(196,160,100,${a})` : `rgba(255,250,235,${a})`); gr.addColorStop(1, 'rgba(0,0,0,0)');
        g.fillStyle = gr; for (const dx of [-w, 0, w]) for (const dy of [-h, 0, h]) { g.save(); g.translate(dx, dy); g.fillRect(x - rad, y - rad, rad * 2, rad * 2); g.restore(); }
      }
      g.lineCap = 'round';
      for (let i = 0; i < 260; i++) { // 纤维
        const x = r() * w, y = r() * h, len = 4 + r() * 18, a = r() * Math.PI;
        g.strokeStyle = r() < .6 ? `rgba(150,120,80,${.06 + r() * .08})` : `rgba(255,255,245,${.1 + r() * .12})`;
        g.lineWidth = .4 + r() * .7; g.beginPath(); g.moveTo(x, y); g.quadraticCurveTo(x + Math.cos(a) * len * .5 + (r() - .5) * 4, y + Math.sin(a) * len * .5 + (r() - .5) * 4, x + Math.cos(a) * len, y + Math.sin(a) * len); g.stroke();
      }
      for (let i = 0; i < 90; i++) { g.fillStyle = `rgba(110,80,50,${.08 + r() * .12})`; g.fillRect(r() * w, r() * h, .8 + r(), .8 + r()); }
    });
  }
  function lacquerTex() {
    return tex(256, 256, (g, w, h) => {
      const r = K.rng(23);
      g.fillStyle = '#241816'; g.fillRect(0, 0, w, h);
      for (let i = 0; i < 220; i++) { // 竖向刷痕
        const x = r() * w; const a = .02 + r() * .05; const light = r() < .5;
        g.strokeStyle = light ? `rgba(120,70,50,${a})` : `rgba(0,0,0,${a * 1.4})`; g.lineWidth = .5 + r() * 2.2;
        g.beginPath(); g.moveTo(x, 0); g.bezierCurveTo(x + (r() - .5) * 8, h * .33, x + (r() - .5) * 8, h * .66, x, h); g.stroke();
      }
      const gr = g.createLinearGradient(0, 0, w, h); gr.addColorStop(0, 'rgba(255,200,150,.03)'); gr.addColorStop(.5, 'rgba(255,200,150,0)'); gr.addColorStop(1, 'rgba(255,200,150,.025)');
      g.fillStyle = gr; g.fillRect(0, 0, w, h);
      for (let i = 0; i < 40; i++) { g.fillStyle = `rgba(160,110,70,${.05 + r() * .06})`; g.fillRect(r() * w, r() * h, 1, 1); }
    });
  }
  function silkTex() {
    return tex(64, 64, (g, w, h) => {
      g.fillStyle = 'rgba(0,0,0,0)'; g.clearRect(0, 0, w, h);
      for (let i = -h; i < w; i += 3) { g.strokeStyle = 'rgba(255,230,200,.07)'; g.lineWidth = 1; g.beginPath(); g.moveTo(i, 0); g.lineTo(i + h, h); g.stroke(); }
      for (let i = 0; i < w + h; i += 3) { g.strokeStyle = 'rgba(0,0,0,.06)'; g.beginPath(); g.moveTo(i, 0); g.lineTo(i - h, h); g.stroke(); }
    });
  }
  function patinaTex() {
    return tex(64, 64, (g, w, h) => {
      const r = K.rng(5); g.clearRect(0, 0, w, h);
      for (let i = 0; i < 26; i++) { const x = r() * w, y = r() * h, rad = 2 + r() * 7; const gr = g.createRadialGradient(x, y, 0, x, y, rad); gr.addColorStop(0, 'rgba(78,140,134,.9)'); gr.addColorStop(1, 'rgba(78,140,134,0)'); g.fillStyle = gr; g.fillRect(x - rad, y - rad, rad * 2, rad * 2); }
    });
  }
  const svgUrl = s => `url("data:image/svg+xml,${encodeURIComponent(s)}")`;
  // 铜包角：L 形铜片，两臂末端卷成如意云头
  function cap(rot) {
    const p = 'M1.5 1.5 H17 Q22.5 1.5 21.6 6 Q20.6 10 16.6 9 Q13.6 8 14.4 5.6 Q15.2 4 16.8 4.8 L16.2 6.2 Q15.7 7.1 16.9 7.2 Q18.9 7.3 18.9 5.2 Q18.9 3.9 17 3.9 H6.2 Q3.9 3.9 3.9 6.2 V17 Q3.9 18.9 5.2 18.9 Q7.3 18.9 7.2 16.9 Q7.1 15.7 6.2 16.2 L4.8 16.8 Q4 15.2 5.6 14.4 Q8 13.6 9 16.6 Q10 20.6 6 21.6 Q1.5 22.5 1.5 17 Z';
    return svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><linearGradient id="b" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#f0c98a"/><stop offset=".45" stop-color="#b8834a"/><stop offset="1" stop-color="#6e4a28"/></linearGradient></defs><g transform="rotate(${rot} 12 12)"><path d="${p}" fill="url(#b)" stroke="#241a21" stroke-width="1.1" stroke-linejoin="round"/><circle cx="3.2" cy="3.2" r="1" fill="#fbe2b0"/></g></svg>`);
  }
  // 毛笔横线（两端飞白）
  const brushLine = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 400 8" preserveAspectRatio="none"><path d="M2 4.6 C40 2.6 90 2.4 150 3.2 S300 3.6 398 2.8 L397 4.4 C330 5.6 260 5.4 200 5.2 S60 6.4 3 5.6 Z" fill="#241a21"/><path d="M330 3.4 L398 2.9 L396 3.6 Z M2 5 L30 4.3 L4 5.6Z" fill="#241a21" opacity=".5"/></svg>');
  const brushUnder = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 6" preserveAspectRatio="none"><path d="M1 3.5 C20 1.6 60 1.4 99 2.6 L98 4.2 C60 4.6 25 5 2 4.8Z" fill="#e8b54a"/></svg>');
  // 进度条的笔触遮罩：头圆尾飞白
  const brushMask = svgUrl('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 10" preserveAspectRatio="none"><path d="M3 1.5 Q0 5 3 8.6 C60 9.6 140 9 197 8 Q199.5 6 198 4 Q196 2.6 194 3.2 Q190 1.4 186 2.2 C130 .8 60 .6 3 1.5Z" fill="#000"/></svg>');
  const lockIco = svgUrl(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><rect x="2" y="2" width="12" height="12" rx="2" fill="#8a2418" stroke="#241a21" stroke-width="1.2"/><path d="M5.5 7.5 V6 a2.5 2.5 0 0 1 5 0 V7.5 M4.8 7.5 h6.4 v4 h-6.4z" fill="none" stroke="#f3e7cc" stroke-width="1.2"/></svg>`);

  K.init = function () {
    const st = document.documentElement.style;
    st.setProperty('--tx-paper', `url(${paperTex()})`);
    st.setProperty('--tx-lacquer', `url(${lacquerTex()})`);
    st.setProperty('--tx-silk', `url(${silkTex()})`);
    st.setProperty('--tx-patina', `url(${patinaTex()})`);
    st.setProperty('--cap-tl', cap(0)); st.setProperty('--cap-tr', cap(90)); st.setProperty('--cap-br', cap(180)); st.setProperty('--cap-bl', cap(270));
    st.setProperty('--brush-line', brushLine); st.setProperty('--brush-under', brushUnder); st.setProperty('--brush-mask', brushMask); st.setProperty('--lock-ico', lockIco);
    // SVG 滤镜：brush-edge（印章边缘的蚀刻感）、brush-ink（大字的干笔毛边）
    if (!document.getElementById('v4-filters')) {
      const d = document.createElement('div');
      d.id = 'v4-filters'; d.style.cssText = 'position:absolute;width:0;height:0;overflow:hidden';
      d.innerHTML = `<svg width="0" height="0"><filter id="brush-edge" x="-5%" y="-5%" width="110%" height="110%"><feTurbulence type="fractalNoise" baseFrequency=".9" numOctaves="2" seed="3"/><feDisplacementMap in="SourceGraphic" scale="1.6"/></filter>
        <filter id="brush-ink" x="-5%" y="-10%" width="110%" height="120%"><feTurbulence type="fractalNoise" baseFrequency=".65" numOctaves="2" seed="8" result="n"/><feDisplacementMap in="SourceGraphic" in2="n" scale="2.2" result="d"/><feMorphology in="d" operator="dilate" radius=".25"/></filter></svg>`;
      document.body.appendChild(d);
    }
    if (/[?&]phone\b/.test(location.search)) document.documentElement.classList.add('is-phone');
    if (/[?&]nonotes\b/.test(location.search)) document.body.classList.add('no-notes');
  };

  // ------------------------------------------------------------ 图标 --
  // 每个图标：64×64，墨线 #241a21 2.5px 圆角连接，平涂 + 左上亮面 / 右下暗面（Lanternvale 分面），一个主色 + 至多一个辅色。
  const S = 'stroke="#241a21" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round"';
  const I = {
    // —— 指令（DOMESTIC-FX §6.2 的母题，简化） ——
    develop: `<path d="M6 52 Q32 42 58 52 L58 58 Q32 48 6 58Z" fill="${C.ochre}" ${S}/><path d="M10 44 Q32 36 54 44 L54 49 Q32 41 10 49Z" fill="${C.ochreHi}" ${S}/><path d="M32 42 V22" fill="none" ${S} stroke="#2f6b45"/><path d="M32 30 Q18 28 15 16 Q29 15 32 28Z" fill="${C.jade}" ${S}/><path d="M32 25 Q44 20 48 8 Q35 9 32 23Z" fill="${C.jadeHi}" ${S}/>`,
    build: `<path d="M6 26 L32 10 L58 26 L52 26 L32 15 L12 26Z" fill="${C.tile}" ${S}/><path d="M12 26 H52 V31 H12Z" fill="#4e8c86" ${S}/><path d="M17 31 V56 M47 31 V56" ${S} stroke-width="6" stroke="#241a21"/><path d="M17 31 V56 M47 31 V56" stroke="${C.zhu}" stroke-width="3"/><path d="M8 56 H56" ${S}/><path d="M24 40 L40 52 M24 52 L40 40" stroke="${C.woodHi}" stroke-width="3" stroke-linecap="round"/><path d="M24 40 L40 52 M24 52 L40 40" fill="none" ${S} stroke-width="1"/>`,
    recruit: `<ellipse cx="30" cy="22" rx="18" ry="7" fill="${C.paper}" ${S}/><path d="M12 22 V44 Q30 54 48 44 V22 Q30 30 12 22Z" fill="${C.zhu}" ${S}/><path d="M12 30 Q30 38 48 30" fill="none" stroke="${C.gold}" stroke-width="2"/><path d="M40 6 L58 26 M56 8 L38 26" ${S} stroke-width="3.5"/><circle cx="58" cy="26" r="3" fill="${C.wood}" ${S} stroke-width="1.5"/><circle cx="38" cy="26" r="3" fill="${C.wood}" ${S} stroke-width="1.5"/><path d="M14 46 L10 58 M46 46 L50 58" ${S}/>`,
    train: `<circle cx="28" cy="34" r="20" fill="${C.paper}" ${S}/><circle cx="28" cy="34" r="13" fill="${C.zhu}" ${S}/><circle cx="28" cy="34" r="6" fill="${C.paper}" ${S}/><path d="M28 34 L56 8" ${S} stroke-width="3"/><path d="M50 6 L58 6 L58 14 M52 12 L58 6" fill="${C.paper}" ${S} stroke-width="2"/>`,
    search: `<path d="M4 34 Q32 6 60 34 Z" fill="${C.ochreHi}" ${S}/><path d="M14 34 Q32 18 50 34" fill="none" stroke="${C.ochre}" stroke-width="2"/><path d="M4 34 H60" ${S}/><ellipse cx="22" cy="46" rx="3.5" ry="5" fill="${C.ink}"/><ellipse cx="34" cy="52" rx="3.5" ry="5" fill="${C.ink}"/><ellipse cx="46" cy="47" rx="3" ry="4.5" fill="${C.ink}" opacity=".7"/><path d="M52 14 l2 -5 l2 5 l5 2 l-5 2 l-2 5 l-2 -5 l-5 -2z" fill="${C.goldHi}" ${S} stroke-width="1.5"/>`,
    hire: `<rect x="10" y="20" width="40" height="22" rx="4" fill="${C.paper}" ${S}/><ellipse cx="10" cy="31" rx="5" ry="11" fill="${C.woodHi}" ${S}/><ellipse cx="50" cy="31" rx="5" ry="11" fill="${C.wood}" ${S}/><path d="M28 20 V42" stroke="${C.zhu}" stroke-width="5"/><path d="M28 42 L22 56 M28 42 L34 56" stroke="${C.zhu}" stroke-width="3.5" stroke-linecap="round"/><path d="M16 28 H24 M16 34 H22 M36 28 H44 M36 34 H42" stroke="${C.ink}" stroke-width="1.6" stroke-linecap="round"/>`,
    reward: `<ellipse cx="32" cy="46" rx="26" ry="9" fill="${C.zhu}" ${S}/><ellipse cx="32" cy="43" rx="22" ry="6" fill="${C.zhuLo}"/><rect x="14" y="24" width="20" height="16" rx="3" fill="${C.plumHi}" ${S}/><path d="M14 30 H34" stroke="${C.gold}" stroke-width="2"/><circle cx="44" cy="34" r="7" fill="${C.goldHi}" ${S}/><rect x="42" y="32" width="4" height="4" fill="${C.ink}"/><circle cx="46" cy="22" r="6" fill="${C.gold}" ${S}/><rect x="44.3" y="20.3" width="3.4" height="3.4" fill="${C.ink}"/>`,
    appoint: `<rect x="14" y="36" width="36" height="14" rx="3" fill="${C.teal}" ${S}/><path d="M20 36 Q20 20 32 18 Q44 20 44 36Z" fill="#2f7f7a" ${S}/><circle cx="32" cy="16" r="5" fill="${C.teal}" ${S}/><path d="M40 50 Q48 58 58 54" fill="none" stroke="${C.zhu}" stroke-width="3.5" stroke-linecap="round"/><rect x="6" y="52" width="14" height="10" fill="${C.zhu}" ${S} stroke-width="2"/>`,
    move: `<path d="M16 58 L22 6" ${S} stroke-width="3.5"/><path d="M21 8 Q38 4 52 12 Q44 18 50 26 Q36 22 19 26Z" fill="var(--fc, ${C.green})" ${S}/><path d="M30 14 Q38 13 44 16" stroke="${C.paper}" stroke-width="2" fill="none" opacity=".7"/><path d="M28 58 Q40 44 60 44" fill="none" stroke="${C.ochre}" stroke-width="5" stroke-linecap="round"/><path d="M28 58 Q40 44 60 44" fill="none" ${S} stroke-width="1.2" stroke-dasharray="3 4"/>`,
    transport: `<rect x="10" y="22" width="38" height="18" rx="3" fill="${C.wood}" ${S}/><path d="M14 22 Q20 10 28 22 M28 22 Q36 8 44 22" fill="${C.ochreHi}" ${S}/><circle cx="22" cy="46" r="10" fill="${C.woodHi}" ${S}/><circle cx="22" cy="46" r="3" fill="${C.ink}"/><path d="M22 36 V56 M12 46 H32" stroke="${C.ink}" stroke-width="1.8"/><path d="M48 32 H60" ${S} stroke-width="3"/>`,
    trade: `<path d="M6 18 H58" ${S} stroke-width="3.5"/><path d="M32 8 V18" ${S}/><path d="M14 18 V30" ${S} stroke-width="1.6"/><path d="M6 30 H22 L18 44 H10Z" fill="${C.ochreHi}" ${S}/><path d="M50 18 V36" ${S} stroke-width="1.6"/><path d="M44 36 H56 L54 48 H46Z" fill="${C.bronze}" ${S}/><circle cx="32" cy="8" r="3.5" fill="${C.gold}" ${S} stroke-width="1.5"/>`,
    market: `<path d="M6 20 L32 8 L58 20 Z" fill="${C.teal}" ${S}/><path d="M6 20 H58 V26 Q52 30 45.6 26 Q39 30 32 26 Q25 30 18.4 26 Q12 30 6 26Z" fill="${C.paper}" ${S}/><path d="M18.4 20 V26 M32 20 V26 M45.6 20 V26" stroke="${C.zhu}" stroke-width="4"/><path d="M10 26 V58 M54 26 V58" ${S} stroke-width="3"/><path d="M26 30 V52" stroke="${C.steelHi}" stroke-width="4" stroke-linecap="round"/><path d="M26 30 V52" fill="none" ${S} stroke-width="1"/><path d="M21 34 H31" ${S} stroke-width="2.5"/><circle cx="42" cy="42" r="8" fill="${C.bronze}" ${S}/><circle cx="42" cy="42" r="3" fill="${C.goldHi}" ${S} stroke-width="1.2"/>`,
    caravan: `<path d="M8 50 L12 36 Q12 26 20 26 Q24 14 30 26 Q34 16 40 26 L48 26 Q50 16 56 16 Q60 18 58 22 L54 24 L52 36 L54 50" fill="${C.ochreHi}" ${S}/><path d="M14 50 V58 M24 40 V58 M42 40 V58 M52 50 V58" ${S} stroke-width="3"/><rect x="22" y="22" width="14" height="10" rx="2" fill="${C.zhu}" ${S} stroke-width="1.8"/>`,
    explore: `<path d="M2 56 L20 24 L30 38 L40 18 L62 56Z" fill="${C.indigoHi}" ${S}/><path d="M20 24 L25 32 L17 30Z M40 18 L46 30 L36 27Z" fill="${C.paper}"/><path d="M40 18 L40 6" ${S} stroke-width="2"/><path d="M40 6 L52 10 L40 14" fill="${C.zhu}" ${S} stroke-width="1.8"/><circle cx="14" cy="50" r="5" fill="${C.amber}" ${S} stroke-width="1.8"/>`,
    diplomacy: `<path d="M32 4 V60" ${S} stroke-width="3.5"/><path d="M32 4 V60" stroke="${C.woodHi}" stroke-width="1.5"/><path d="M24 14 Q32 22 40 14 M22 24 Q32 34 42 24 M20 34 Q32 46 44 34" fill="none" stroke="${C.plum}" stroke-width="5" stroke-linecap="round"/><circle cx="32" cy="50" r="8" fill="${C.jade}" ${S}/><circle cx="32" cy="50" r="2.5" fill="${C.paper}" ${S} stroke-width="1.2"/>`,
    attack: `<path d="M10 58 L50 10 M54 58 L14 10" ${S} stroke-width="3.5"/><path d="M44 8 L56 4 L52 16 Z M20 8 L8 4 L12 16Z" fill="${C.steelHi}" ${S} stroke-width="1.8"/><path d="M18 28 Q32 16 46 28 Q50 42 32 46 Q14 42 18 28Z" fill="${C.gold}" ${S}/><path d="M24 30 L28 36 M32 27 L32 37 M40 30 L36 36" stroke="${C.ink}" stroke-width="2" stroke-linecap="round"/><path d="M32 24 V48" stroke="${C.goldLo}" stroke-width="1.5" stroke-dasharray="2 2"/>`,
    // —— 外壳 ——
    generals: `<path d="M44 6 L22 52" stroke="${C.wood}" stroke-width="4" stroke-linecap="round"/><path d="M44 6 L22 52" ${S} stroke-width="1" fill="none"/><path d="M20 50 L18 58 L25 54Z" fill="${C.ink}"/><path d="M14 40 Q14 18 32 16 Q50 18 50 40Z" fill="${C.steel}" ${S}/><path d="M14 40 Q14 22 28 18 Q22 28 22 40Z" fill="${C.steelHi}" opacity=".8"/><path d="M10 40 H54 L50 46 H14Z" fill="${C.bronze}" ${S}/><path d="M32 16 Q30 6 38 4 Q34 10 40 14Z" fill="${C.zhu}" ${S} stroke-width="1.8"/>`,
    titles: `<rect x="10" y="44" width="44" height="12" rx="5" fill="${C.zhu}" ${S}/><path d="M18 44 L16 26 Q32 16 48 22 L46 44Z" fill="${C.lq}" ${S}/><path d="M20 30 Q32 24 44 27" fill="none" stroke="${C.gold}" stroke-width="2"/><path d="M48 22 L56 14" ${S}/><circle cx="14" cy="12" r="3" fill="${C.goldHi}" ${S} stroke-width="1.2"/><circle cx="24" cy="8" r="3" fill="${C.goldHi}" ${S} stroke-width="1.2"/><circle cx="34" cy="10" r="3" fill="${C.goldHi}" ${S} stroke-width="1.2"/>`,
    factions: `<rect x="8" y="12" width="48" height="40" rx="3" fill="${C.paper}" ${S}/><path d="M8 26 Q20 20 30 30 T56 28 M18 52 Q26 40 40 44" fill="none" stroke="${C.ochre}" stroke-width="2"/><path d="M8 12 V52 M56 12 V52" ${S} stroke-width="5"/><path d="M20 30 V18 L28 21 L20 24" fill="${C.zhu}" ${S} stroke-width="1.6"/><path d="M38 40 V28 L46 31 L38 34" fill="${C.indigo}" ${S} stroke-width="1.6"/><path d="M44 22 V12 L51 15 L44 18" fill="${C.jade}" ${S} stroke-width="1.6"/>`,
    chronicle: `<path d="M10 14 H54 V50 H10Z" fill="${C.ochreHi}" ${S}/><path d="M17 14 V50 M24 14 V50 M31 14 V50 M38 14 V50 M45 14 V50" stroke="${C.ink}" stroke-width="1.5"/><path d="M6 22 H58 M6 42 H58" stroke="${C.zhu}" stroke-width="2.6"/><path d="M13 20 V28 M20 20 V34 M27 20 V30 M34 20 V36" stroke="${C.ink}" stroke-width="1.6" stroke-linecap="round" opacity=".7"/>`,
    settings: `<path d="M8 10 H22 V40 H52 V54 H8Z" fill="${C.bronze}" ${S}/><path d="M12 14 H18 V44 H48 V50 H12Z" fill="${C.bronzeHi}" opacity=".55"/><path d="M42 6 L32 30 M42 6 L56 30" ${S} stroke-width="3"/><circle cx="42" cy="8" r="4" fill="${C.goldHi}" ${S} stroke-width="1.8"/><path d="M34 26 Q44 34 54 26" fill="none" ${S} stroke-width="1.6"/>`,
    menu: `<rect x="8" y="10" width="48" height="11" rx="3" fill="${C.ochreHi}" ${S}/><rect x="8" y="27" width="48" height="11" rx="3" fill="${C.ochre}" ${S}/><rect x="8" y="44" width="48" height="11" rx="3" fill="${C.ochreHi}" ${S}/><path d="M20 10 V55 M44 10 V55" stroke="${C.zhu}" stroke-width="2.4"/>`,
    endMonth: `<circle cx="32" cy="34" r="16" fill="${C.amber}" ${S}/><path d="M4 40 H60 V58 H4Z" fill="${C.indigo}" ${S}/><path d="M18 40 V30 H46 V40" fill="${C.zhu}" ${S}/><path d="M14 30 L32 20 L50 30Z" fill="${C.tile}" ${S}/><path d="M28 58 V48 Q32 44 36 48 V58" fill="${C.ink}"/><path d="M4 18 Q10 16 14 18 M52 12 Q56 10 60 12" stroke="${C.paper}" stroke-width="2" stroke-linecap="round" fill="none" opacity=".8"/>`,
    gold: `<path d="M8 40 Q8 24 20 26 Q24 18 32 18 Q40 18 44 26 Q56 24 56 40 Q56 50 32 50 Q8 50 8 40Z" fill="${C.gold}" ${S}/><path d="M20 30 Q32 40 44 30 Q42 38 32 38 Q22 38 20 30Z" fill="${C.goldLo}"/><path d="M14 36 Q16 30 20 30" stroke="${C.goldHi}" stroke-width="2.4" fill="none" stroke-linecap="round"/>`,
    food: `<path d="M16 22 Q12 40 16 54 H48 Q52 40 48 22Z" fill="${C.ochreHi}" ${S}/><path d="M16 22 Q32 28 48 22 L44 16 Q32 20 20 16Z" fill="${C.ochre}" ${S}/><path d="M32 16 Q28 6 22 4 M32 16 Q36 6 44 4 M32 16 V2" fill="none" ${S} stroke-width="2"/><circle cx="22" cy="4" r="2.6" fill="${C.goldHi}" ${S} stroke-width="1.2"/><circle cx="44" cy="4" r="2.6" fill="${C.goldHi}" ${S} stroke-width="1.2"/><circle cx="32" cy="2.5" r="2.6" fill="${C.goldHi}" ${S} stroke-width="1.2"/><path d="M24 36 H40" stroke="${C.zhu}" stroke-width="3"/>`,
    token: `<path d="M12 56 L48 12" ${S} stroke-width="3.5"/><path d="M12 56 L48 12" stroke="${C.woodHi}" stroke-width="1.5"/><path d="M48 12 L56 6 L52 16Z" fill="${C.steelHi}" ${S} stroke-width="1.8"/><path d="M20 46 L34 46 L28 34 Z" fill="${C.zhu}" ${S} stroke-width="1.8"/><path d="M12 56 L8 52 M12 56 L16 60" ${S} stroke-width="2"/>`,
    city: `<path d="M6 30 H58 V56 H6Z" fill="${C.cream}" ${S}/><path d="M6 30 V24 H12 V30 M18 30 V24 H24 V30 M40 30 V24 H46 V30 M52 30 V24 H58 V30" fill="${C.cream}" ${S}/><path d="M24 56 V44 Q32 36 40 44 V56" fill="${C.zhu}" ${S}/><path d="M20 24 L32 12 L44 24Z" fill="${C.tile}" ${S}/>`,
    prestige: `<circle cx="32" cy="32" r="26" fill="${C.zhu}" ${S}/><circle cx="32" cy="32" r="21" fill="none" stroke="${C.paper}" stroke-width="2"/><text x="32" y="42" text-anchor="middle" font-size="27" font-family="STKaiti,KaiTi,LXGW WenKai,serif" fill="${C.paper}">望</text>`,
    missions: `<rect x="10" y="8" width="12" height="48" rx="3" fill="${C.ochreHi}" ${S}/><rect x="26" y="8" width="12" height="48" rx="3" fill="${C.ochreHi}" ${S}/><rect x="42" y="8" width="12" height="48" rx="3" fill="${C.paper2}" ${S}/><path d="M6 30 H58" stroke="${C.zhu}" stroke-width="3"/>`,
    calendar: `<path d="M44 8 A16 16 0 1 0 56 34 A13 13 0 1 1 44 8Z" fill="${C.goldHi}" ${S}/><path d="M18 30 Q18 22 26 22 Q34 22 34 30 V44 H18Z" fill="${C.bronze}" ${S}/><path d="M14 44 H38" ${S}/><circle cx="26" cy="48" r="3" fill="${C.bronze}" ${S} stroke-width="1.5"/>`,
    // —— 能力与属性 ——
    war: `<path d="M32 4 L38 12 L36 44 H28 L26 12Z" fill="${C.steel}" ${S}/><path d="M32 6 V42" stroke="${C.steelHi}" stroke-width="2"/><path d="M18 44 H46 L44 50 H20Z" fill="${C.gold}" ${S}/><path d="M29 50 H35 V58 H29Z" fill="${C.zhu}" ${S}/><circle cx="32" cy="60" r="3" fill="${C.gold}" ${S} stroke-width="1.5"/>`,
    intel: `<path d="M32 58 V36" ${S} stroke-width="4"/><path d="M32 36 Q8 30 10 10 Q22 4 32 12 Q42 4 54 10 Q56 30 32 36Z" fill="${C.paper}" ${S}/><path d="M32 36 L20 10 M32 36 V8 M32 36 L44 10" stroke="${C.indigoHi}" stroke-width="1.8"/><path d="M32 36 Q20 32 16 14" fill="none" stroke="${C.ink}" stroke-width="1" opacity=".4"/>`,
    pol: `<path d="M38 6 L44 10 L22 48 L16 46Z" fill="${C.wood}" ${S}/><path d="M16 46 L22 48 L18 58 Q12 60 12 56Z" fill="${C.ink}" ${S} stroke-width="1.5"/><path d="M36 8 L42 12" stroke="${C.gold}" stroke-width="3"/><rect x="34" y="40" width="22" height="14" rx="3" fill="${C.jade}" ${S}/><path d="M38 47 H52" stroke="${C.paper}" stroke-width="2"/>`,
    troops: `<path d="M16 60 V12 M32 60 V6 M48 60 V12" ${S} stroke-width="3"/><path d="M16 4 L20 14 H12Z M32 -2 L36 8 H28Z M48 4 L52 14 H44Z" fill="${C.steelHi}" ${S} stroke-width="1.6"/><path d="M12 16 Q16 22 20 16 M28 10 Q32 16 36 10 M44 16 Q48 22 52 16" fill="${C.zhu}" ${S} stroke-width="1.3"/><path d="M8 44 H56 V56 H8Z" fill="${C.zhu}" ${S}/>`,
    training: `<circle cx="32" cy="34" r="22" fill="${C.paper}" ${S}/><circle cx="32" cy="34" r="14" fill="${C.ochre}" ${S}/><circle cx="32" cy="34" r="6" fill="${C.zhu}" ${S}/><path d="M32 34 L10 8" ${S} stroke-width="3"/>`,
    loyalty: `<path d="M32 56 Q10 40 10 24 Q10 12 22 12 Q30 12 32 22 Q34 12 42 12 Q54 12 54 24 Q54 40 32 56Z" fill="${C.zhu}" ${S}/><path d="M18 22 Q20 16 26 16" stroke="${C.zhuHi}" stroke-width="3" fill="none" stroke-linecap="round"/><path d="M24 30 Q32 38 40 30 Q32 22 24 30Z" fill="none" stroke="${C.gold}" stroke-width="2"/>`,
    level: `<path d="M32 4 L39 22 L58 24 L44 37 L48 56 L32 46 L16 56 L20 37 L6 24 L25 22Z" fill="${C.goldHi}" ${S}/><path d="M32 12 L36 24 L32 40 L28 24Z" fill="${C.gold}"/>`,
    combo: `<circle cx="14" cy="32" r="8" fill="${C.zhu}" ${S}/><path d="M32 24 L40 32 L32 40 L24 32Z" fill="${C.indigoHi}" ${S}/><path d="M50 22 L58 38 H42Z" fill="${C.gold}" ${S}/><path d="M22 32 H24 M40 32 H42" ${S} stroke-width="2"/>`,
    tactic: `<path d="M10 12 H54 V52 H10Z" fill="${C.paper}" ${S}/><path d="M10 12 V52" stroke="${C.wood}" stroke-width="6"/><path d="M20 20 L44 44 M44 20 L20 44" stroke="${C.indigo}" stroke-width="3" stroke-linecap="round"/><circle cx="32" cy="32" r="7" fill="${C.zhu}" ${S} stroke-width="1.8"/>`,
    special: `<path d="M32 4 L38 26 L60 32 L38 38 L32 60 L26 38 L4 32 L26 26Z" fill="${C.zhu}" ${S}/><path d="M32 14 L35 29 L50 32 L35 35 L32 50 L29 35 L14 32 L29 29Z" fill="${C.goldHi}"/>`,
    formation: `<rect x="6" y="20" width="12" height="12" rx="2" fill="${C.indigoHi}" ${S} stroke-width="2"/><rect x="26" y="10" width="12" height="12" rx="2" fill="${C.zhu}" ${S} stroke-width="2"/><rect x="46" y="20" width="12" height="12" rx="2" fill="${C.indigoHi}" ${S} stroke-width="2"/><rect x="16" y="40" width="12" height="12" rx="2" fill="${C.indigoHi}" ${S} stroke-width="2"/><rect x="36" y="40" width="12" height="12" rx="2" fill="${C.indigoHi}" ${S} stroke-width="2"/>`,
    mount: `<path d="M12 56 L20 34 Q14 26 20 18 L26 8 L30 16 Q44 16 52 30 L56 42 Q54 48 48 44 L42 36 L36 40 L40 56Z" fill="${C.zhu}" ${S}/><path d="M26 8 Q36 10 40 18 Q32 18 30 16" fill="${C.ink}"/><circle cx="34" cy="24" r="2" fill="${C.ink}"/>`,
    bonds: `<path d="M20 20 Q32 8 44 20 Q56 32 44 44 Q32 56 20 44 Q8 32 20 20Z" fill="none" stroke="${C.zhu}" stroke-width="5"/><path d="M20 20 Q32 8 44 20 Q56 32 44 44 Q32 56 20 44 Q8 32 20 20Z" fill="none" ${S} stroke-width="1.2"/><path d="M26 32 L38 32 M32 26 V38" stroke="${C.zhu}" stroke-width="4" stroke-linecap="round"/><path d="M30 46 L24 60 M34 46 L40 60" stroke="${C.zhu}" stroke-width="3" stroke-linecap="round"/>`,
    history: `<rect x="8" y="12" width="48" height="40" rx="2" fill="${C.paper}" ${S}/><path d="M16 22 V44 M24 22 V40 M32 22 V44 M40 22 V36 M48 22 V42" stroke="${C.ink}" stroke-width="2" stroke-linecap="round" opacity=".75"/><rect x="40" y="40" width="10" height="10" fill="${C.zhu}"/>`,
    // —— 装备 ——
    guandao: `<path d="M50 60 L22 22" ${S} stroke-width="4"/><path d="M50 60 L22 22" stroke="${C.green}" stroke-width="2"/><path d="M22 22 Q8 18 6 4 Q20 2 30 12 L26 20Z" fill="${C.steelHi}" ${S}/><path d="M20 18 Q12 14 10 6" stroke="${C.steelLo}" stroke-width="1.5" fill="none"/><path d="M22 22 L28 18 L30 26Z" fill="${C.jade}" ${S} stroke-width="1.6"/><path d="M28 28 Q34 30 30 36" fill="none" stroke="${C.zhu}" stroke-width="3" stroke-linecap="round"/>`,
    armour: `<path d="M18 10 L46 10 L52 20 L50 54 L14 54 L12 20Z" fill="${C.ochre}" ${S}/><path d="M14 22 H50 M14 30 H50 M14 38 H50 M14 46 H50" stroke="${C.goldLo}" stroke-width="2"/><path d="M24 10 Q32 18 40 10" fill="${C.green}" ${S} stroke-width="1.8"/><path d="M12 20 L4 28 L8 40 L14 34 M52 20 L60 28 L56 40 L50 34" fill="${C.greenHi}" ${S} stroke-width="1.8"/><circle cx="32" cy="30" r="5" fill="${C.goldHi}" ${S} stroke-width="1.5"/>`,
    helmet: `<path d="M12 42 Q12 16 32 14 Q52 16 52 42Z" fill="${C.green}" ${S}/><path d="M16 38 Q16 22 28 18" stroke="${C.greenHi}" stroke-width="3" fill="none" stroke-linecap="round"/><path d="M8 42 H56 L52 50 H12Z" fill="${C.gold}" ${S}/><circle cx="32" cy="12" r="4" fill="${C.green}" ${S}/><path d="M28 30 L32 24 L36 30 L32 34Z" fill="${C.goldHi}" ${S} stroke-width="1.5"/>`,
    shield: `<path d="M32 4 L56 12 Q56 44 32 60 Q8 44 8 12Z" fill="${C.wood}" ${S}/><path d="M32 10 L50 16 Q50 42 32 54Z" fill="${C.woodLo}" opacity=".45"/><circle cx="32" cy="28" r="9" fill="${C.goldHi}" ${S}/><path d="M26 28 Q32 20 38 28 Q32 36 26 28Z" fill="${C.zhu}"/>`,
    horse: `<path d="M14 58 L18 36 Q10 30 14 20 L22 8 L26 16 Q44 14 52 30 L56 44 Q54 50 48 46 L42 38 L36 42 L38 58Z" fill="${C.zhu}" ${S}/><path d="M22 8 Q32 6 40 16 Q30 18 26 16Z" fill="#5a1a12"/><path d="M26 16 Q20 26 22 36" stroke="#5a1a12" stroke-width="5" fill="none" stroke-linecap="round"/><circle cx="34" cy="22" r="2.2" fill="${C.ink}"/>`,
    book: `<rect x="8" y="14" width="48" height="36" rx="2" fill="${C.ochreHi}" ${S}/><path d="M16 14 V50 M24 14 V50 M32 14 V50 M40 14 V50 M48 14 V50" stroke="${C.ink}" stroke-width="1.5"/><path d="M4 24 H60 M4 40 H60" stroke="${C.indigo}" stroke-width="2.6"/>`,
    bow: `<path d="M18 4 Q50 32 18 60" fill="none" ${S} stroke-width="5"/><path d="M18 4 Q50 32 18 60" fill="none" stroke="${C.woodHi}" stroke-width="2.5"/><path d="M18 4 V60" ${S} stroke-width="1.2"/><path d="M8 32 H54" ${S} stroke-width="2"/><path d="M54 32 L46 28 L46 36Z" fill="${C.steelHi}" ${S} stroke-width="1.3"/>`,
    seal: `<rect x="12" y="30" width="40" height="24" rx="3" fill="${C.jade}" ${S}/><path d="M22 30 Q22 12 32 10 Q42 12 42 30Z" fill="${C.jadeHi}" ${S}/><path d="M20 42 H44" stroke="${C.paper}" stroke-width="2.5"/>`,
    // —— 武林大会 ——
    tourney: `<circle cx="32" cy="34" r="24" fill="none" stroke="${C.jade}" stroke-width="6"/><circle cx="32" cy="34" r="24" fill="none" ${S} stroke-width="1.2"/><path d="M14 54 L46 12 M50 54 L18 12" ${S} stroke-width="3.5"/><path d="M42 10 L50 6 L48 14Z M22 10 L14 6 L16 14Z" fill="${C.steelHi}" ${S} stroke-width="1.5"/><path d="M24 50 L18 56 M40 50 L46 56" stroke="${C.gold}" stroke-width="4" stroke-linecap="round"/>`,
    trophy: `<path d="M18 8 H46 V22 Q46 38 32 40 Q18 38 18 22Z" fill="${C.gold}" ${S}/><path d="M18 14 Q6 14 8 24 Q10 30 18 28 M46 14 Q58 14 56 24 Q54 30 46 28" fill="none" ${S}/><path d="M28 40 H36 V48 H28Z" fill="${C.goldLo}" ${S}/><path d="M18 48 H46 V56 H18Z" fill="${C.lq}" ${S}/><path d="M24 14 Q24 26 30 32" stroke="${C.goldHi}" stroke-width="3" fill="none" stroke-linecap="round"/>`,
    // —— 菜单与设置 ——
    save: `<rect x="8" y="40" width="34" height="16" rx="4" fill="${C.lq}" ${S}/><ellipse cx="25" cy="46" rx="10" ry="4" fill="#3b3440"/><path d="M48 6 L56 12 L30 44 L24 42Z" fill="${C.wood}" ${S}/><path d="M24 42 L30 44 L22 52Z" fill="${C.ink}"/>`,
    load: `<rect x="10" y="18" width="44" height="30" rx="2" fill="${C.paper}" ${S}/><path d="M10 18 V48" stroke="${C.wood}" stroke-width="6"/><path d="M54 18 V48" stroke="${C.wood}" stroke-width="6"/><path d="M20 26 H44 M20 33 H40 M20 40 H36" stroke="${C.ink}" stroke-width="2" stroke-linecap="round"/>`,
    help: `<circle cx="32" cy="32" r="26" fill="${C.paper}" ${S}/><path d="M24 24 Q24 14 32 14 Q42 14 42 23 Q42 30 34 33 V40" fill="none" ${S} stroke-width="4"/><circle cx="34" cy="49" r="3.5" fill="${C.ink}"/>`,
    gate: `<path d="M6 26 L32 8 L58 26Z" fill="${C.tile}" ${S}/><path d="M10 26 H54 V58 H10Z" fill="${C.cream}" ${S}/><path d="M24 58 V40 Q32 32 40 40 V58" fill="${C.zhu}" ${S}/>`,
    music: `<rect x="6" y="22" width="52" height="18" rx="6" fill="${C.wood}" ${S}/><path d="M10 27 H54 M10 31 H54 M10 35 H54" stroke="${C.paper}" stroke-width="1.2"/><circle cx="16" cy="31" r="2" fill="${C.goldHi}"/><path d="M44 14 V26 M44 14 L52 12" ${S}/><circle cx="41" cy="46" r="4" fill="${C.ink}"/><path d="M45 46 V34" ${S}/>`,
    voice: `<path d="M10 24 Q10 12 26 12 H40 Q54 12 54 24 Q54 36 40 36 H30 L18 48 L20 36 Q10 34 10 24Z" fill="${C.paper}" ${S}/><path d="M22 24 H42 M22 18 H36 M22 30 H34" stroke="${C.zhu}" stroke-width="2.4" stroke-linecap="round"/>`,
    eye: `<path d="M4 32 Q32 6 60 32 Q32 58 4 32Z" fill="${C.paper}" ${S}/><circle cx="32" cy="32" r="11" fill="${C.patina || '#4e8c86'}" ${S}/><circle cx="32" cy="32" r="4.5" fill="${C.ink}"/><circle cx="28" cy="28" r="2.2" fill="#fff"/>`,
    hand: `<path d="M20 58 Q12 44 12 34 L14 22 Q16 18 19 22 L20 30 V12 Q22 8 25 12 V28 V8 Q28 4 31 8 V28 V10 Q34 6 37 10 V30 V16 Q40 12 43 16 V40 Q42 52 36 58Z" fill="${C.cream}" ${S}/>`,
    access: `<circle cx="32" cy="12" r="6" fill="${C.jade}" ${S}/><path d="M12 22 H52 M32 22 V40 L22 58 M32 40 L42 58" fill="none" ${S} stroke-width="4.5"/><path d="M12 22 H52 M32 22 V40 L22 58 M32 40 L42 58" fill="none" stroke="${C.jade}" stroke-width="2"/>`,
    camera: `<rect x="6" y="20" width="40" height="28" rx="5" fill="${C.bronze}" ${S}/><path d="M46 28 L58 20 V48 L46 40Z" fill="${C.bronzeHi}" ${S}/><circle cx="26" cy="34" r="7" fill="${C.lq}" ${S}/>`,
    play: `<path d="M18 10 L52 32 L18 54Z" fill="${C.zhu}" ${S}/>`,
    gift: `<rect x="10" y="26" width="44" height="30" rx="3" fill="${C.zhu}" ${S}/><path d="M32 26 V56 M10 36 H54" stroke="${C.gold}" stroke-width="4"/><path d="M32 26 Q20 10 16 18 Q14 26 32 26 Q44 10 48 18 Q50 26 32 26Z" fill="${C.goldHi}" ${S} stroke-width="1.8"/>`,
    x: `<path d="M16 16 Q32 30 48 48" fill="none" stroke="${C.ink}" stroke-width="7" stroke-linecap="round"/><path d="M48 14 Q34 30 16 50" fill="none" stroke="${C.ink}" stroke-width="7" stroke-linecap="round"/>`,
    check: `<path d="M10 34 L26 50 L56 14" fill="none" stroke="${C.ink}" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>`,
    filter: `<path d="M8 10 H56 L38 32 V52 L26 58 V32Z" fill="${C.ochreHi}" ${S}/>`,
    sort: `<path d="M16 8 V56 M16 56 L8 46 M16 56 L24 46 M34 14 H58 M34 28 H52 M34 42 H46" fill="none" ${S} stroke-width="3.5"/>`,
    grid: `<rect x="8" y="8" width="20" height="20" rx="3" fill="${C.ochreHi}" ${S}/><rect x="36" y="8" width="20" height="20" rx="3" fill="${C.ochreHi}" ${S}/><rect x="8" y="36" width="20" height="20" rx="3" fill="${C.ochreHi}" ${S}/><rect x="36" y="36" width="20" height="20" rx="3" fill="${C.ochreHi}" ${S}/>`,
    list: `<rect x="8" y="10" width="48" height="10" rx="3" fill="${C.ochreHi}" ${S}/><rect x="8" y="27" width="48" height="10" rx="3" fill="${C.ochreHi}" ${S}/><rect x="8" y="44" width="48" height="10" rx="3" fill="${C.ochreHi}" ${S}/>`,
  };
  K.ICONS = I;
  K.icon = function (id, size, extra) {
    const body = I[id] || I.help;
    const sz = size ? ` width="${size}" height="${size}"` : '';
    return `<svg class="ico ico-${id}" viewBox="0 0 64 64"${sz} ${extra || ''} xmlns="http://www.w3.org/2000/svg" overflow="visible">${body}</svg>`;
  };

  // ------------------------------------------------------------ 立绘 --
  K.portrait = function (gen, size, mood) {
    try {
      if (window.SG && SG.Portrait) return SG.Portrait.url(gen, { size: size || 96, frame: false, mood: mood || 'neutral', color: gen.color || '#8c6a48', raster: false });
    } catch (e) { console.warn(e); }
    return '';
  };
  K.face = function (gen, size, cls, mood) {
    return `<span class="v4-face ${cls || 'r1'}" style="--s:${size}px"><img alt="" src="${K.portrait(gen, size * 2, mood)}"></span>`;
  };

  // ------------------------------------------------------------ 地图 --
  // Lanternvale 风格的战略地图（样张背景）：拼布田、低多边形山丘（分面明暗、不描线）、软团树（上暖下青、描墨线）、
  // 河流、道路、城池（米色城墙 + 石板蓝瓦 + 朱门、描墨线）。黄昏暖光 + 暗角。
  K.map = function (cv, opts) {
    opts = opts || {};
    const dpr = Math.min(2, window.devicePixelRatio || 1);
    const W = cv.clientWidth || window.innerWidth, H = cv.clientHeight || window.innerHeight;
    cv.width = W * dpr; cv.height = H * dpr;
    const g = cv.getContext('2d'); g.scale(dpr, dpr);
    const r = K.rng(opts.seed || 7);
    const sx = W / 1280, sy = H / 720, s = Math.min(sx, sy);
    g.fillStyle = '#a3b964'; g.fillRect(0, 0, W, H);
    // 拼布田
    const fieldCols = ['#b9c574', '#a6bd68', '#cbbf72', '#98ae5c', '#d6c682', '#8fae58', '#c2c97c', '#b3b46a'];
    for (let i = 0; i < 260; i++) {
      const x = r() * W, y = r() * H, w = (30 + r() * 70) * s, h = (18 + r() * 40) * s, a = (r() - .5) * .5;
      g.save(); g.translate(x, y); g.rotate(a); g.fillStyle = fieldCols[(r() * fieldCols.length) | 0]; g.globalAlpha = .85; g.fillRect(-w / 2, -h / 2, w, h);
      g.globalAlpha = .18; g.strokeStyle = '#6e7a3c'; g.lineWidth = 1; for (let k = -h / 2 + 4; k < h / 2; k += 5 * s) { g.beginPath(); g.moveTo(-w / 2, k); g.lineTo(w / 2, k); g.stroke(); }
      g.restore();
    }
    g.globalAlpha = 1;
    // 河
    const river = [[-20, 420], [180, 400], [330, 470], [520, 450], [700, 520], [900, 500], [1060, 560], [1300, 540]].map(([x, y]) => [x * sx, y * sy]);
    const path = (pts) => { g.beginPath(); g.moveTo(pts[0][0], pts[0][1]); for (let i = 1; i < pts.length - 1; i++) { const mx = (pts[i][0] + pts[i + 1][0]) / 2, my = (pts[i][1] + pts[i + 1][1]) / 2; g.quadraticCurveTo(pts[i][0], pts[i][1], mx, my); } g.lineTo(pts[pts.length - 1][0], pts[pts.length - 1][1]); };
    g.lineCap = 'round'; g.lineJoin = 'round';
    path(river); g.strokeStyle = '#d9c08a'; g.lineWidth = 46 * s; g.stroke();
    path(river); g.strokeStyle = '#5f97a8'; g.lineWidth = 34 * s; g.stroke();
    path(river); g.strokeStyle = '#7fb3bf'; g.lineWidth = 20 * s; g.stroke();
    path(river.map(([x, y]) => [x, y - 4 * s])); g.strokeStyle = 'rgba(220,240,240,.5)'; g.lineWidth = 3 * s; g.setLineDash([18 * s, 26 * s]); g.stroke(); g.setLineDash([]);
    // 道路
    const roads = [[[120, 140], [300, 260], [520, 300], [700, 250], [900, 300], [1100, 260]], [[520, 300], [560, 460], [600, 640]], [[900, 300], [960, 480], [1040, 700]]];
    for (const rd of roads) { const p = rd.map(([x, y]) => [x * sx, y * sy]); path(p); g.strokeStyle = '#a07a50'; g.lineWidth = 9 * s; g.stroke(); path(p); g.strokeStyle = '#d2ac78'; g.lineWidth = 6 * s; g.stroke(); }
    // 山丘（低多边形，分面）
    const hill = (cx, cy, rad, hgt, base) => {
      const n = 7 + ((r() * 4) | 0); const pts = [];
      for (let i = 0; i < n; i++) { const a = Math.PI + (i / (n - 1)) * Math.PI; pts.push([cx + Math.cos(a) * rad * (0.8 + r() * .3), cy + Math.sin(a) * hgt * (0.6 + r() * .5)]); }
      const peak = [cx + (r() - .5) * rad * .3, cy - hgt];
      for (let i = 0; i < pts.length - 1; i++) {
        const a = pts[i], b = pts[i + 1]; const lit = (a[0] + b[0]) / 2 < peak[0];
        g.beginPath(); g.moveTo(a[0], a[1]); g.lineTo(b[0], b[1]); g.lineTo(peak[0], peak[1]); g.closePath();
        g.fillStyle = shadeHex(base, lit ? 1.12 - i * .01 : .82 + i * .01); g.fill();
      }
      g.beginPath(); g.moveTo(cx - rad, cy); g.lineTo(cx + rad, cy); g.lineTo(peak[0], peak[1]); g.closePath(); g.globalAlpha = 0; g.fill(); g.globalAlpha = 1;
      if (hgt > 80 * s) { g.beginPath(); g.moveTo(peak[0], peak[1]); g.lineTo(peak[0] - hgt * .22, peak[1] + hgt * .28); g.lineTo(peak[0] + hgt * .05, peak[1] + hgt * .22); g.lineTo(peak[0] + hgt * .2, peak[1] + hgt * .3); g.closePath(); g.fillStyle = '#f1efe6'; g.fill(); }
    };
    for (let i = 0; i < 9; i++) hill((40 + r() * 420) * sx, (90 + r() * 140) * sy, (70 + r() * 70) * s, (60 + r() * 90) * s, i % 3 ? '#8ea35a' : '#9a9c78');
    for (let i = 0; i < 5; i++) hill((800 + r() * 460) * sx, (60 + r() * 80) * sy, (60 + r() * 60) * s, (50 + r() * 60) * s, '#93a65e');
    // 软团树
    const tree = (x, y, rr) => {
      g.fillStyle = 'rgba(40,50,30,.25)'; g.beginPath(); g.ellipse(x + rr * .3, y + rr * .7, rr * .9, rr * .35, 0, 0, Math.PI * 2); g.fill();
      g.fillStyle = '#6b4c3b'; g.fillRect(x - rr * .12, y, rr * .24, rr * .6);
      const gr = g.createLinearGradient(0, y - rr, 0, y + rr * .6); gr.addColorStop(0, '#8fb35a'); gr.addColorStop(.55, '#5f8f4a'); gr.addColorStop(1, '#3f6b5a');
      g.fillStyle = gr; g.strokeStyle = '#241a21'; g.lineWidth = 1.4;
      g.beginPath(); g.arc(x - rr * .35, y - rr * .05, rr * .55, 0, Math.PI * 2); g.arc(x + rr * .35, y - rr * .1, rr * .6, 0, Math.PI * 2); g.fill();
      g.beginPath(); g.arc(x, y - rr * .45, rr * .62, 0, Math.PI * 2); g.fill(); g.stroke();
      g.beginPath(); g.arc(x - rr * .35, y - rr * .05, rr * .55, Math.PI * .5, Math.PI * 1.35); g.stroke();
      g.beginPath(); g.arc(x + rr * .35, y - rr * .1, rr * .6, -Math.PI * .3, Math.PI * .55); g.stroke();
      g.fillStyle = 'rgba(255,240,180,.25)'; g.beginPath(); g.arc(x - rr * .2, y - rr * .7, rr * .25, 0, Math.PI * 2); g.fill();
    };
    const trees = [];
    for (let i = 0; i < 140; i++) trees.push([r() * W, (240 + r() * 480) * sy, (7 + r() * 8) * s]);
    for (let i = 0; i < 40; i++) { const cx = (80 + r() * 300) * sx, cy = (560 + r() * 140) * sy; trees.push([cx + (r() - .5) * 120 * s, cy, (8 + r() * 6) * s]); }
    trees.sort((a, b) => a[1] - b[1]);
    for (const t of trees) { if (Math.abs(t[1] - riverY(t[0])) < 32 * s) continue; tree(t[0], t[1], t[2]); }
    function riverY(x) { let best = 1e9; for (const p of river) if (Math.abs(p[0] - x) < 120 * sx) best = Math.min(best, p[1]); return best; }
    // 城池
    const cities = opts.cities || [];
    for (const c of cities) city(c.x * sx, c.y * sy, (c.size || 1) * s, c.color || '#2f7d4a', c.lit);
    function city(x, y, k, col, lit) {
      const w = 64 * k, h = 26 * k;
      g.fillStyle = 'rgba(30,30,20,.28)'; g.beginPath(); g.ellipse(x + 8 * k, y + h * .6, w * .62, h * .5, 0, 0, Math.PI * 2); g.fill();
      g.strokeStyle = '#241a21'; g.lineWidth = 1.6;
      g.fillStyle = '#d9c39a'; g.fillRect(x - w / 2, y - h / 2, w, h); g.strokeRect(x - w / 2, y - h / 2, w, h);
      g.fillStyle = '#c4ab80'; g.fillRect(x - w / 2, y + h / 4, w, h / 4);
      for (let i = 0; i < 8; i++) { g.fillStyle = '#e6d3ad'; g.fillRect(x - w / 2 + i * w / 8 + 1, y - h / 2 - 4 * k, w / 16, 4 * k); g.strokeRect(x - w / 2 + i * w / 8 + 1, y - h / 2 - 4 * k, w / 16, 4 * k); }
      g.fillStyle = '#a83e2e'; g.beginPath(); g.moveTo(x - 7 * k, y + h / 2); g.lineTo(x - 7 * k, y); g.quadraticCurveTo(x, y - 8 * k, x + 7 * k, y); g.lineTo(x + 7 * k, y + h / 2); g.closePath(); g.fill(); g.stroke();
      // 门楼
      g.fillStyle = '#b8402e'; g.fillRect(x - 14 * k, y - h / 2 - 14 * k, 28 * k, 10 * k); g.strokeRect(x - 14 * k, y - h / 2 - 14 * k, 28 * k, 10 * k);
      g.fillStyle = lit ? '#ffe2a6' : '#eadfc4'; for (let i = 0; i < 3; i++) g.fillRect(x - 11 * k + i * 8 * k, y - h / 2 - 12 * k, 5 * k, 6 * k);
      g.fillStyle = '#808da6'; g.beginPath(); g.moveTo(x - 22 * k, y - h / 2 - 13 * k); g.quadraticCurveTo(x, y - h / 2 - 30 * k, x + 22 * k, y - h / 2 - 13 * k); g.closePath(); g.fill(); g.stroke();
      // 旗
      g.strokeStyle = '#5a3e2c'; g.lineWidth = 1.6; g.beginPath(); g.moveTo(x + w / 2 - 6 * k, y - h / 2); g.lineTo(x + w / 2 - 6 * k, y - h / 2 - 34 * k); g.stroke();
      g.fillStyle = col; g.strokeStyle = '#241a21'; g.lineWidth = 1.2; g.beginPath(); g.moveTo(x + w / 2 - 6 * k, y - h / 2 - 34 * k); g.quadraticCurveTo(x + w / 2 + 8 * k, y - h / 2 - 36 * k, x + w / 2 + 16 * k, y - h / 2 - 30 * k); g.quadraticCurveTo(x + w / 2 + 6 * k, y - h / 2 - 26 * k, x + w / 2 - 6 * k, y - h / 2 - 22 * k); g.closePath(); g.fill(); g.stroke();
      if (lit) { const gl = g.createRadialGradient(x, y, 0, x, y, 70 * k); gl.addColorStop(0, 'rgba(255,200,120,.35)'); gl.addColorStop(1, 'rgba(255,200,120,0)'); g.fillStyle = gl; g.fillRect(x - 80 * k, y - 80 * k, 160 * k, 160 * k); }
    }
    // 暖光 + 暗角
    const sun = g.createRadialGradient(W * .2, -H * .1, 0, W * .2, -H * .1, W * .9); sun.addColorStop(0, 'rgba(255,214,150,.30)'); sun.addColorStop(1, 'rgba(255,214,150,0)'); g.fillStyle = sun; g.fillRect(0, 0, W, H);
    const vig = g.createRadialGradient(W / 2, H / 2, Math.min(W, H) * .35, W / 2, H / 2, Math.max(W, H) * .75); vig.addColorStop(0, 'rgba(40,30,20,0)'); vig.addColorStop(1, 'rgba(40,24,12,.38)'); g.fillStyle = vig; g.fillRect(0, 0, W, H);
    if (opts.night) {
      g.fillStyle = 'rgba(22,30,74,.58)'; g.fillRect(0, 0, W, H);
      g.globalCompositeOperation = 'screen';
      for (const c of cities) { if (!c.lit) continue; const x = c.x * sx, y = c.y * sy, rad = 95 * (c.size || 1) * s; const gl = g.createRadialGradient(x, y, 0, x, y, rad); gl.addColorStop(0, 'rgba(255,190,110,.75)'); gl.addColorStop(.5, 'rgba(255,160,80,.25)'); gl.addColorStop(1, 'rgba(255,160,80,0)'); g.fillStyle = gl; g.fillRect(x - rad, y - rad, rad * 2, rad * 2); }
      g.globalCompositeOperation = 'source-over';
    }
  };
  function shadeHex(hex, k) { const n = parseInt(hex.slice(1), 16); const c = [n >> 16, (n >> 8) & 255, n & 255].map(v => Math.max(0, Math.min(255, Math.round(v * k)))); return `rgb(${c[0]},${c[1]},${c[2]})`; }

  // ------------------------------------------------------------ 去背 --
  K.cutout = function (src, opts) {
    opts = opts || {};
    return new Promise((res) => {
      const img = new Image();
      img.onload = () => {
        const w = img.naturalWidth, h = img.naturalHeight; const c = document.createElement('canvas'); c.width = w; c.height = h;
        const g = c.getContext('2d'); g.drawImage(img, 0, 0); const d = g.getImageData(0, 0, w, h); const p = d.data;
        const isBg = (i) => { const R = p[i], G = p[i + 1], B = p[i + 2]; const mx = Math.max(R, G, B), mn = Math.min(R, G, B); const sat = mx ? (mx - mn) / mx : 0; return (sat < .34 && G >= R * .93 && G > B * 1.02 && mx > 70) || (B > R && B > G * .95 && mx > 150 && sat < .3); };
        const seen = new Uint8Array(w * h); const q = [];
        for (let x = 0; x < w; x++) { q.push(x, (h - 1) * w + x); } for (let y = 0; y < h; y++) { q.push(y * w, y * w + w - 1); }
        while (q.length) { const k = q.pop(); if (seen[k]) continue; seen[k] = 1; const i = k * 4; if (!isBg(i)) continue; p[i + 3] = 0; const x = k % w, y = (k / w) | 0; if (x > 0) q.push(k - 1); if (x < w - 1) q.push(k + 1); if (y > 0) q.push(k - w); if (y < h - 1) q.push(k + w); }
        g.putImageData(d, 0, 0); res(c.toDataURL('image/png'));
      };
      img.onerror = () => res(src);
      img.src = src;
    });
  };

  // 小工具
  K.$ = (s, r) => (r || document).querySelector(s);
  K.h = (html) => { const t = document.createElement('template'); t.innerHTML = html.trim(); return t.content.firstElementChild; };
  K.note = (n, x, y, text) => `<div class="mk-note" data-n="${n}" style="left:${x};top:${y}">${text}</div>`;
  K.ready = (fn) => { if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => { K.init(); fn(); }); else { K.init(); fn(); } };
  // 武将样本（能力取自经典剧本）
  K.GENS = {
    liubei: { name: '刘备', war: 76, intel: 76, pol: 78, culture: 'han', color: '#2f9a55' },
    guanyu: { name: '关羽', war: 98, intel: 78, pol: 62, culture: 'han', color: '#2f9a55' },
    zhangfei: { name: '张飞', war: 99, intel: 34, pol: 26, culture: 'han', color: '#2f9a55' },
    zhaoyun: { name: '赵云', war: 96, intel: 74, pol: 64, culture: 'han', color: '#2f9a55' },
    jianyong: { name: '简雍', war: 34, intel: 68, pol: 72, culture: 'han', color: '#2f9a55' },
    caocao: { name: '曹操', war: 72, intel: 95, pol: 96, culture: 'han', color: '#2b4b9a' },
    xiahoudun: { name: '夏侯惇', war: 90, intel: 58, pol: 70, culture: 'han', color: '#2b4b9a' },
    dianwei: { name: '典韦', war: 95, intel: 35, pol: 24, culture: 'han', color: '#2b4b9a' },
    guojia: { name: '郭嘉', war: 15, intel: 97, pol: 82, culture: 'han', color: '#2b4b9a' },
    lvbu: { name: '吕布', war: 100, intel: 26, pol: 13, culture: 'han', color: '#7a1e2c' },
    sunjian: { name: '孙坚', war: 92, intel: 74, pol: 78, culture: 'han', color: '#c0392b' },
    huangzhong: { name: '黄忠', war: 93, intel: 60, pol: 52, culture: 'han', color: '#b0702a' },
    machao: { name: '马超', war: 97, intel: 44, pol: 26, culture: 'han', color: '#d08a2a' },
    yuanshao: { name: '袁绍', war: 70, intel: 72, pol: 74, culture: 'han', color: '#2a6fd1' },
    dongzhuo: { name: '董卓', war: 86, intel: 64, pol: 18, culture: 'han', color: '#7a1e2c' },
    commodus: { name: '康茂德', war: 82, intel: 30, pol: 18, culture: 'roman', color: '#8a2a6a' },
    himiko: { name: '卑弥呼', war: 20, intel: 92, pol: 88, culture: 'wa', color: '#c85a8a', sex: 'f' },
  };
})();
