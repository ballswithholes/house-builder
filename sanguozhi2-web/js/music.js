'use strict';
/* ==========================================================================
   背景音乐引擎（第二版）· SG.Music
   全新创作的乐曲，按原作《三国志II 霸王的大陆》的气质（英雄、苍茫、五声调式），
   用程序合成的现代民乐 / 管弦乐音色实时演奏。不使用任何音频文件。

   文件：
     js/music.js        引擎：记谱解析、编曲时间线、前瞻调度器、采样缓存、混音总线
     js/music-inst.js   乐器：拨弦（Karplus-Strong）、打击（模态合成）、弓弦 / 吹管 / 铜管 / 垫音（实时振荡器）
     js/music-han.js    汉地曲目：title map battle battle-defend clash duel victory defeat ending council
     js/music-world.js  各地域变奏：map@<文化> battle@<文化>

   公开接口
     SG.Music.add(key, piece)           注册乐曲（key 如 'map'、'battle@wa'）
     SG.Music.get(key) / keys()         取乐曲 / 全部 key
     SG.Music.resolve(kind, culture)    → 实际播放的 key（有地域变奏用变奏，否则回退汉地）
     SG.Music.defineInst(name, def)     注册乐器（music-inst.js 使用）
     SG.Music.compile(key)              → 编译结果 { sections, form, errors, ... }（带缓存）
     SG.Music.validate(key)             → 记谱错误列表（小节长度不符、未知声部 / 乐器……）
     SG.Music.prepare(key)              → Promise：分片预渲染该曲需要的采样（每片 ≤ 8ms，不卡主线程）
     new SG.Music.Engine(ctx, dest, opts)
        .play(key, { fade })            交叉淡入淡出（约 0.8 秒）切换曲目；返回 Promise
        .stop(fade)  .duck(level, sec)  .setVolume(v)  .tick()（实时模式由定时器自动调用）
        .scheduleUntil(t)               离线渲染时一次排程到 t 秒
        .onEnded = key => {}            非循环曲（胜利 / 战败 / 冲锋乐句）结束时回调
     SG.Music.renderOffline(key, sec, opts) → Promise<{ buffer, ms, peak, rms, nan, ... }>（测试页用）

   ------------------------------------------------------------------------
   记谱法（简谱，按小节书写；每个声部一行字符串，空白分隔记号）
   ------------------------------------------------------------------------
   乐曲  { key:'D'（1=D）, oct:4（“1”所在八度，默认 4 → D4）, bpm, meter:4（每小节拍数）,
           scale:'1 2 3 5 6'（本调式音阶，供装饰音 / 刮奏取音）, cents:{ 'b3': -30 }（可选微分音）,
           voices:{ 声部:{ inst, oct, gain, pan, rev } }, kit:{ 鼓谱轨:{ inst, art, gain, pan, note } },
           pats:{ 节奏型:{ _step:0.25, 轨:'X..x' } }, sections:{ 段:{...} }, form:['intro','A',...],
           loopFrom:'A'（循环起点；loop:false 为一次性短曲）, gain, reverb }
   段    { 声部名:'谱', dr:'节奏型*3 加花', from:'A'（继承另一段的全部声部）, mute:['pad'],
           inst:{ 声部:'乐器' }（换乐器）, oct:{ 声部:1 }（移八度）, bpm, bpmTo（渐快 / 渐慢）, dyn:0.9 }
   音符  [#|b] 级数 [八度] [时值] [装饰]
           级数   1 宫 2 商 3 角 4 清角 5 徵 6 羽 7 变宫；0 = 休止
           八度   ' 高八度（可叠用）  , 低八度
           时值   默认 1 拍；_ 减半（5_ 八分，5__ 十六分）；. 附点；t 三连音（×2/3）；:N 指定 N 拍
                  单独的 - 记号：前一音延长 1 拍（可跨小节线）
           装饰   ~ 揉弦 / 深颤音   * 轮指（拨弦）/ 颤音（弓弦吹管）  ^ 上倚音  v 下倚音
                  / 上滑音（由下滑入）  \ 下滑音（尾音下落）  > 重音  < 音内渐强
                  ) 连线（与下一音连奏，不重新起音）  @ 刮奏（拨弦：一个八度的上行琶音冲入本音；
                  弓弦吹管：由四度下大滑音）  & 下行刮奏
   和弦  用 + 连接：1,+5,+3:4（时值、装饰写在最后一个音上）。拨弦乐器上为扫弦。
   其他  | 小节线（解析时校验每小节拍数）  ; 换气（断开连奏乐句）
         !pp !p !mp !mf !f !ff 力度   !< !> 渐强 / 渐弱（到下一个力度记号为止）
   鼓谱  每个字符一格（默认十六分音符 _step:0.25 拍）：X 重击 x 普通 o 轻 r 滚奏 f 装饰音（flam）
         1–9 自定义力度  . 或 - 空；空格与 | 忽略。段内 dr:'a*3 b _*2'（_ = 空一小节）。
   ========================================================================== */
(function () {
  const SG = window.SG = window.SG || {};

  const LOOKAHEAD = 0.4;          // 前瞻排程时长（秒）：主线程偶尔卡顿时也不断音
  const INTERVAL = 25;            // 调度器周期（毫秒）
  const SLICE_MS = 8;             // 采样预渲染每片最长耗时
  const SAMPLE_BUDGET = 7e6;      // 采样缓存上限（浮点数个数，约 28MB）
  const DEG_SEMI = [0, 0, 2, 4, 5, 7, 9, 11];
  const NOTE_SEMI = { C: 0, D: 2, E: 4, F: 5, G: 7, A: 9, B: 11 };
  const DYN = { ppp: 0.25, pp: 0.34, p: 0.46, mp: 0.6, mf: 0.74, f: 0.88, ff: 1.0, fff: 1.08 };

  // ---------------------------------------------------------------- 工具 --
  function hash(s) {
    let h = 2166136261 >>> 0;
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
    return h | 0;
  }
  // 装饰性随机（人性化、采样噪声）：自带种子，结果可复现，不影响游戏规则随机数
  function rng(seed) {
    let a = (seed | 0) ^ 0x5bd1e995;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function mtof(m) { return 440 * Math.pow(2, (m - 69) / 12); }
  function keyToMidi(key, oct) {
    const m = /^([A-G])([#b]?)$/.exec(key || 'C');
    if (!m) return 60;
    let s = NOTE_SEMI[m[1]] + (m[2] === '#' ? 1 : m[2] === 'b' ? -1 : 0);
    return 12 * ((oct === undefined ? 4 : oct) + 1) + s;
  }
  function nowMs() { return (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now(); }

  // ------------------------------------------------------------ 乐器注册 --
  // def.type：'sample'（预渲染采样，拨弦 / 打击）| 'legato'（单音连奏实时合成）| 'poly'（复音实时合成，垫音）
  const INST = {};
  function defineInst(name, def) { def.name = name; INST[name] = def; return def; }

  // ------------------------------------------------------------ 乐曲注册 --
  const PIECES = {};
  const COMPILED = {};
  function add(key, piece) { piece.key_ = key; PIECES[key] = piece; delete COMPILED[key]; return piece; }

  // 地域 → 变奏 key 的回退表（未覆盖的文化回退到汉地）
  const CULTURE_FALLBACK = {
    han: 'han', nanman: 'han', yi: 'seasia', wa: 'wa', korea: 'korea', steppe: 'steppe', tarim: 'kushan',
    seasia: 'seasia', kushan: 'kushan', persia: 'persia', arab: 'arab', roman: 'roman', celt: 'celt',
    german: 'celt', sarmatian: 'steppe',
  };
  function resolve(kind, culture) {
    if (!kind) return null;
    if (kind.indexOf('@') >= 0) return PIECES[kind] ? kind : kind.split('@')[0];
    const c = CULTURE_FALLBACK[culture] || culture;
    if (c && c !== 'han' && PIECES[kind + '@' + c]) return kind + '@' + c;
    return PIECES[kind] ? kind : null;
  }

  // ================================================================ 解析 ==
  const ORN_CHARS = '~*^v/\\><)@&';

  // 解析单个音（不含和弦）：返回 { acc, deg, oct, rest } 和剩余后缀
  function parseNoteHead(s) {
    const m = /^([#b]?)([0-7])([',]*)/.exec(s);
    if (!m) return null;
    let oct = 0;
    for (const c of m[3]) oct += c === "'" ? 1 : -1;
    return { acc: m[1], deg: +m[2], oct, rest: s.slice(m[0].length) };
  }

  // 解析后缀：时值修饰与装饰
  function parseSuffix(s, err) {
    let mul = 1, explicit = null;
    const orn = {};
    let i = 0;
    while (i < s.length) {
      const c = s[i];
      if (c === '_') { mul *= 0.5; i++; }
      else if (c === '.') { mul *= 1.5; i++; }
      else if (c === 't') { mul *= 2 / 3; i++; }
      else if (c === ':') {
        const m = /^:([0-9]*\.?[0-9]+)/.exec(s.slice(i));
        if (!m) { err('时值格式错误 ' + s); i++; continue; }
        explicit = parseFloat(m[1]); i += m[0].length;
      } else if (ORN_CHARS.indexOf(c) >= 0) {
        if (c === '~') orn.vib = (orn.vib || 0) + 1;
        else if (c === '*') orn.trem = true;
        else if (c === '^') orn.grace = 1;
        else if (c === 'v') orn.grace = -1;
        else if (c === '/') orn.slide = true;
        else if (c === '\\') orn.fall = true;
        else if (c === '>') orn.acc = true;
        else if (c === '<') orn.swell = true;
        else if (c === ')') orn.tie = true;
        else if (c === '@') orn.gliss = (orn.gliss || 0) + 1;
        else if (c === '&') orn.glissDown = true;
        i++;
      } else { err('未知记号 "' + c + '" 于 ' + s); i++; }
    }
    return { mul, explicit, orn };
  }

  // 一个声部字符串 → 事件列表（拍为单位）
  function parseVoice(str, meter, err) {
    const toks = String(str).trim().split(/\s+/).filter(Boolean);
    const evs = [];
    let beat = 0, barStart = 0, bar = 1, vel = DYN.mf, last = null;
    let hairpin = null;            // { from: 事件下标, v0 }
    const marks = [];              // 力度记号位置（用于渐强渐弱插值）
    for (const tk of toks) {
      if (tk === '|' || tk === '||' || tk === '|:' || tk === ':|') {
        const len = beat - barStart;
        if (Math.abs(len - meter) > 1e-6) err('第 ' + bar + ' 小节 ' + (+len.toFixed(3)) + ' 拍（应为 ' + meter + '）');
        barStart = beat; bar++;
        continue;
      }
      if (tk === '-') {
        if (last) { last.dur += 1; beat += 1; } else { beat += 1; }
        continue;
      }
      if (tk === ';') { if (last) last.breath = true; continue; }
      if (tk[0] === '!') {
        const d = tk.slice(1);
        if (d === '<' || d === '>') { hairpin = { from: evs.length, v0: vel, dir: d }; continue; }
        if (DYN[d] === undefined) { err('未知力度 ' + tk); continue; }
        if (hairpin) {
          // 线性插值 hairpin 区间内各音的力度
          const n = evs.length - hairpin.from;
          for (let k = 0; k < n; k++) evs[hairpin.from + k].vel = hairpin.v0 + (DYN[d] - hairpin.v0) * ((k + 0.5) / n);
          hairpin = null;
        }
        vel = DYN[d];
        marks.push(evs.length);
        continue;
      }
      // 音符 / 和弦 / 休止
      const parts = tk.split('+');
      const heads = [];
      let ok = true;
      for (let p = 0; p < parts.length; p++) {
        const h = parseNoteHead(parts[p]);
        if (!h) { err('无法解析 "' + tk + '"'); ok = false; break; }
        if (p < parts.length - 1 && h.rest) { err('和弦内只有最后一个音可带后缀：' + tk); }
        heads.push(h);
      }
      if (!ok) continue;
      const suf = parseSuffix(heads[heads.length - 1].rest, err);
      const dur = suf.explicit !== null ? suf.explicit * suf.mul : suf.mul;
      if (heads.length === 1 && heads[0].deg === 0) {
        last = { rest: true, beat, dur };
        evs.push(last);
        beat += dur;
        continue;
      }
      last = { beat, dur, notes: heads.map(h => ({ acc: h.acc, deg: h.deg, oct: h.oct })), vel, orn: suf.orn };
      if (suf.orn.acc) last.vel = Math.min(1.1, vel * 1.18);
      evs.push(last);
      beat += dur;
    }
    if (beat - barStart > 1e-6) {
      const len = beat - barStart;
      if (Math.abs(len - meter) > 1e-6) err('第 ' + bar + ' 小节 ' + (+len.toFixed(3)) + ' 拍（应为 ' + meter + '）');
    }
    return { all: evs, beats: beat };
  }

  // 鼓谱：一个节奏型 → { beats, hits: [{ lane, beat, vel, kind }] }
  function parsePattern(pat, err, name) {
    const step = pat._step || 0.25;
    const hits = [];
    let beats = -1;
    for (const lane in pat) {
      if (lane[0] === '_') continue;
      const s = String(pat[lane]).replace(/[\s|]/g, '');
      const len = s.length * step;
      if (beats >= 0 && Math.abs(len - beats) > 1e-6) err('节奏型 ' + name + ' 的轨 ' + lane + ' 长 ' + len + ' 拍，与其他轨不符');
      beats = Math.max(beats, len);
      for (let i = 0; i < s.length; i++) {
        const c = s[i];
        if (c === '.' || c === '-') continue;
        const b = i * step;
        if (c === 'X') hits.push({ lane, beat: b, vel: 1.0 });
        else if (c === 'x') hits.push({ lane, beat: b, vel: 0.74 });
        else if (c === 'o') hits.push({ lane, beat: b, vel: 0.44 });
        else if (c === 'f') { hits.push({ lane, beat: b - 0.06, vel: 0.42 }); hits.push({ lane, beat: b, vel: 0.84 }); }
        else if (c === 'r') { for (let k = 0; k < 4; k++) hits.push({ lane, beat: b + k * step / 4, vel: 0.42 + 0.14 * k }); }
        else if (c >= '1' && c <= '9') hits.push({ lane, beat: b, vel: (+c) / 9 });
        else err('节奏型 ' + name + ' 未知字符 ' + c);
      }
    }
    return { beats: Math.max(0, beats), hits, step };
  }

  // ================================================================ 编译 ==
  function compile(key) {
    if (COMPILED[key]) return COMPILED[key];
    const P = PIECES[key];
    if (!P) return null;
    const errors = [];
    const meter = P.meter || 4;
    const keyMidi = keyToMidi(P.key || 'C', P.oct === undefined ? 4 : P.oct);
    P._keyMidi = keyMidi;
    const cents = P.cents || {};
    // 调式音阶（相对“1”的半音 + 音分），供装饰音 / 刮奏取音
    const scale = String(P.scale || '1 2 3 5 6').trim().split(/\s+/).map(s => {
      const h = parseNoteHead(s);
      return h ? { semi: DEG_SEMI[h.deg] + (h.acc === '#' ? 1 : h.acc === 'b' ? -1 : 0), cents: cents[s] || 0 } : null;
    }).filter(Boolean).sort((a, b) => a.semi - b.semi);
    // 音阶内的全部音高（MIDI 浮点），覆盖 0..127
    const scalePitches = [];
    for (let o = -6; o <= 6; o++) for (const s of scale) scalePitches.push(keyMidi + o * 12 + s.semi + s.cents / 100);
    scalePitches.sort((a, b) => a - b);
    function neighbor(p, dir) {
      if (dir > 0) { for (const q of scalePitches) if (q > p + 0.3) return q; return p + 2; }
      for (let i = scalePitches.length - 1; i >= 0; i--) if (scalePitches[i] < p - 0.3) return scalePitches[i]; return p - 2;
    }
    function runBelow(p, span) { return scalePitches.filter(q => q < p - 0.3 && q >= p - span - 0.01); }
    function runAbove(p, span) { return scalePitches.filter(q => q > p + 0.3 && q <= p + span + 0.01).reverse(); }

    function pitchOf(n, voiceOct) {
      const accS = n.acc === '#' ? 1 : n.acc === 'b' ? -1 : 0;
      const name = (n.acc || '') + n.deg;
      const c = cents[name] || 0;
      return keyMidi + DEG_SEMI[n.deg] + accS + 12 * (n.oct + voiceOct) + c / 100;
    }

    // 持续音声部（呼麦等）的低音音高
    for (const v in P.voices) {
      const V = P.voices[v];
      if (V.drone) { const h = parseNoteHead(V.drone); if (h) V.droneMidi = pitchOf(h, 0); else errors.push('声部 ' + v + ' 的 drone 无法解析'); }
      if (!INST[V.inst]) errors.push('声部 ' + v + ' 未知乐器 ' + V.inst);
    }
    for (const l in (P.kit || {})) if (!INST[P.kit[l].inst]) errors.push('鼓轨 ' + l + ' 未知乐器 ' + P.kit[l].inst);

    // 节奏型
    const pats = {};
    for (const name in (P.pats || {})) pats[name] = parsePattern(P.pats[name], m => errors.push('[节奏型 ' + name + '] ' + m), name);

    // 段落（处理继承）
    const RESERVED = { from: 1, mute: 1, inst: 1, oct: 1, bpm: 1, bpmTo: 1, dyn: 1, dr: 1, bars: 1, label: 1 };
    function resolveSection(name, depth) {
      const S = P.sections[name];
      if (!S) { errors.push('未定义的段 ' + name); return null; }
      if (depth > 8) { errors.push('段继承过深 ' + name); return null; }
      let base = { voices: {}, inst: {}, oct: {}, dr: null, bpm: null, bpmTo: null, dyn: 1 };
      if (S.from) {
        const b = resolveSection(S.from, depth + 1);
        if (b) base = { voices: Object.assign({}, b.voices), inst: Object.assign({}, b.inst), oct: Object.assign({}, b.oct), dr: b.dr, bpm: b.bpm, bpmTo: null, dyn: b.dyn };
      }
      for (const k in S) {
        if (RESERVED[k]) continue;
        if (!P.voices[k]) { errors.push('[段 ' + name + '] 未知声部 ' + k); continue; }
        base.voices[k] = S[k];
      }
      if (S.mute) for (const v of S.mute) delete base.voices[v];
      if (S.inst) Object.assign(base.inst, S.inst);
      if (S.oct) Object.assign(base.oct, S.oct);
      if (S.dr !== undefined) base.dr = S.dr;
      if (S.bpm !== undefined) base.bpm = S.bpm;
      if (S.bpmTo !== undefined) base.bpmTo = S.bpmTo;
      if (S.dyn !== undefined) base.dyn = S.dyn;
      return base;
    }

    const sections = {};
    const sampleKeys = new Set();
    for (const name in P.sections) {
      const R = resolveSection(name, 0);
      if (!R) continue;
      const items = [];
      let beats = 0;
      const lens = {};
      for (const v in R.voices) {
        const V = P.voices[v];
        const instName = R.inst[v] || V.inst;
        const def = INST[instName];
        if (!def) { errors.push('[段 ' + name + '] 声部 ' + v + ' 未知乐器 ' + instName); continue; }
        const voiceOct = (V.oct || 0) + (R.oct[v] || 0);
        const pv = parseVoice(R.voices[v], meter, m => errors.push('[段 ' + name + ' · ' + v + '] ' + m));
        lens[v] = pv.beats;
        beats = Math.max(beats, pv.beats);
        const evs = pv.all;
        const dyn = R.dyn;
        if (def.type === 'legato') {
          // 连续的音（无休止、无换气）组成一个乐句
          let ph = null;
          for (const e of evs) {
            if (e.rest) { ph = null; continue; }
            const n = e.notes[e.notes.length - 1];
            const p = pitchOf(n, voiceOct);
            const note = { beat: e.beat, dur: e.dur, p, vel: e.vel * dyn, orn: e.orn };
            if (e.orn.grace) note.gp = neighbor(p, e.orn.grace);
            if (e.orn.trem) note.tp = neighbor(p, 1);
            if (!ph) { ph = { type: 'p', voice: v, inst: instName, beat: e.beat, notes: [] }; items.push(ph); }
            ph.notes.push(note);
            if (e.breath) ph = null;
          }
        } else {
          for (const e of evs) {
            if (e.rest) continue;
            const ps = e.notes.map(n => pitchOf(n, voiceOct));
            const it = { type: def.type === 'sample' ? 's' : 'y', voice: v, inst: instName, beat: e.beat, dur: e.dur, ps, vel: e.vel * dyn, orn: e.orn };
            if (def.type === 'sample') {
              const top = ps[ps.length - 1];
              if (e.orn.grace) it.gp = neighbor(top, e.orn.grace);
              if (e.orn.gliss) it.run = runBelow(top, 12 * e.orn.gliss);
              if (e.orn.glissDown) it.run = runAbove(top, 12);
              if (e.orn.trem && def.trill) it.tp = neighbor(top, 1);
              for (const q of ps) sampleKeys.add(def.name + '|' + sampleKeyPitch(def, q));
              if (it.gp !== undefined) sampleKeys.add(def.name + '|' + sampleKeyPitch(def, it.gp));
              if (it.tp !== undefined) sampleKeys.add(def.name + '|' + sampleKeyPitch(def, it.tp));
              if (it.run) for (const q of it.run) sampleKeys.add(def.name + '|' + sampleKeyPitch(def, q));
            }
            items.push(it);
          }
        }
      }
      // 各声部长度必须一致
      for (const v in lens) if (Math.abs(lens[v] - beats) > 1e-6) errors.push('[段 ' + name + '] 声部 ' + v + ' 长 ' + lens[v] + ' 拍，段长 ' + beats + ' 拍');
      // 鼓谱
      if (R.dr) {
        const seq = [];
        for (const tk of String(R.dr).trim().split(/\s+/)) {
          const m = /^([^*]+)(?:\*(\d+))?$/.exec(tk);
          if (!m) { errors.push('[段 ' + name + '] 鼓谱序列错误 ' + tk); continue; }
          const n = m[2] ? +m[2] : 1;
          for (let k = 0; k < n; k++) seq.push(m[1]);
        }
        let b = 0;
        for (const pn of seq) {
          if (pn === '_') { b += meter; continue; }
          const pt = pats[pn];
          if (!pt) { errors.push('[段 ' + name + '] 未知节奏型 ' + pn); continue; }
          for (const h of pt.hits) {
            const K = P.kit[h.lane];
            if (!K) { errors.push('[节奏型 ' + pn + '] 未知鼓轨 ' + h.lane); continue; }
            const def = INST[K.inst];
            if (!def) { errors.push('鼓轨 ' + h.lane + ' 未知乐器 ' + K.inst); continue; }
            const pitch = K.note ? pitchOf(parseNoteHead(K.note), 0) : null;
            items.push({ type: 'd', lane: h.lane, inst: K.inst, beat: b + h.beat, vel: h.vel * R.dyn, art: K.art || 0, p: pitch });
            const nv = def.variants || 1;
            for (let k = 0; k < nv; k++) sampleKeys.add(def.name + '|' + drumKey(def, K.art || 0, pitch, k));
          }
          b += pt.beats;
        }
        if (beats === 0) beats = b;
        else if (b > beats + 1e-6) errors.push('[段 ' + name + '] 鼓谱 ' + b + ' 拍，超过段长 ' + beats + ' 拍');
        else if (b < beats - 1e-6 && seq.length) {
          // 鼓谱不足段长时循环补齐
          const loopLen = b;
          if (loopLen > 0) {
            const base = items.filter(x => x.type === 'd');
            for (let off = loopLen; off < beats - 1e-6; off += loopLen) {
              for (const h of base) if (h.beat + off < beats - 1e-6) items.push(Object.assign({}, h, { beat: h.beat + off }));
            }
          }
        }
      }
      if (Math.abs(beats / meter - Math.round(beats / meter)) > 1e-6) errors.push('[段 ' + name + '] 段长 ' + beats + ' 拍，不是整小节');
      items.sort((a, b) => a.beat - b.beat);
      const bpm0 = R.bpm || P.bpm || 90;
      const bpm1 = R.bpmTo || bpm0;
      const tf = makeTime(beats, bpm0, bpm1);
      sections[name] = { name, beats, items, time: tf.time, dur: tf.dur, bars: beats / meter };
    }
    const form = (P.form || Object.keys(P.sections)).filter(n => { if (!sections[n]) { errors.push('曲式中未知的段 ' + n); return false; } return true; });
    let loopFrom = 0;
    if (P.loopFrom !== undefined) {
      loopFrom = typeof P.loopFrom === 'number' ? P.loopFrom : form.indexOf(P.loopFrom);
      if (loopFrom < 0) { errors.push('loopFrom 不在曲式中'); loopFrom = 0; }
    }
    let introDur = 0, loopDur = 0;
    form.forEach((n, i) => { if (i < loopFrom) introDur += sections[n].dur; else loopDur += sections[n].dur; });
    const C = { key, piece: P, sections, form, loopFrom, loop: P.loop !== false, errors, sampleKeys: Array.from(sampleKeys), introDur, loopDur, meter };
    COMPILED[key] = C;
    return C;
  }

  function makeTime(beats, b0, b1) {
    if (!b1 || Math.abs(b1 - b0) < 1e-6 || beats <= 0) { const k = 60 / b0; return { time: b => b * k, dur: beats * k }; }
    const r = (b1 - b0) / beats;
    const time = b => 60 / r * Math.log((b0 + r * b) / b0);
    return { time, dur: time(beats) };
  }

  // 采样键：音高量化到 1 音分（同一音高共用采样）
  function sampleKeyPitch(def, p) {
    if (def.pitchless) return 'x';
    return (Math.round(p * 100) / 100).toFixed(2);
  }
  function drumKey(def, art, pitch, variant) {
    return 'a' + art + (pitch !== null && pitch !== undefined && !def.pitchless ? '@' + (Math.round(pitch * 100) / 100).toFixed(2) : '') + '#' + variant;
  }

  // ============================================================ 采样缓存 ==
  const SAMPLES = new Map();     // 'inst|key' → { data: Float32Array, sr, buf: AudioBuffer|null, used }
  let sampleTotal = 0;
  const jobs = [];
  let pumping = false;
  const pending = new Map();     // fullKey → Promise

  function parseFullKey(fk) {
    const i = fk.indexOf('|');
    return { inst: fk.slice(0, i), sub: fk.slice(i + 1) };
  }

  // 创建渲染任务（生成器）
  function makeJob(fk) {
    const { inst, sub } = parseFullKey(fk);
    const def = INST[inst];
    if (!def || !def.render) return null;
    const sr = def.sr || 32000;
    let gen;
    if (sub[0] === 'a') {
      // 打击：a<art>[@pitch]#variant
      const m = /^a([^@#]+)(?:@([0-9.\-]+))?#(\d+)$/.exec(sub);
      const art = isNaN(+m[1]) ? m[1] : +m[1];
      const pitch = m[2] !== undefined ? parseFloat(m[2]) : null;
      gen = def.render({ sr, art, pitch, freq: pitch !== null ? mtof(pitch) : 0, variant: +m[3], seed: hash(fk) });
    } else {
      const p = sub === 'x' ? 60 : parseFloat(sub);
      gen = def.render({ sr, pitch: p, freq: mtof(p), seed: hash(fk) });
    }
    return { fk, gen, sr };
  }

  function requestSample(fk) {
    if (SAMPLES.has(fk)) return Promise.resolve();
    if (pending.has(fk)) return pending.get(fk);
    const job = makeJob(fk);
    if (!job) return Promise.resolve();
    const pr = new Promise(res => { job.resolve = res; });
    pending.set(fk, pr);
    jobs.push(job);
    pump();
    return pr;
  }

  const schedule = (function () {
    if (typeof MessageChannel !== 'undefined') {
      const ch = new MessageChannel();
      const q = [];
      ch.port1.onmessage = () => { const f = q.shift(); if (f) f(); };
      if (ch.port1.unref) ch.port1.unref();   // Node 下不阻止进程退出
      return f => { q.push(f); ch.port2.postMessage(0); };
    }
    return f => setTimeout(f, 0);
  })();

  function finishJob(job, data) {
    if (data) {
      SAMPLES.set(job.fk, { data, sr: job.sr, buf: null, used: nowMs() });
      sampleTotal += data.length;
    }
    pending.delete(job.fk);
    job.resolve();
  }

  function pump() {
    if (pumping) return;
    pumping = true;
    const step = () => {
      const t0 = nowMs();
      try {
        while (jobs.length && nowMs() - t0 < SLICE_MS) {
          const j = jobs[0];
          let r;
          try { r = j.gen.next(); } catch (e) { console.warn('乐器采样渲染失败', j.fk, e); jobs.shift(); finishJob(j, null); continue; }
          if (r.done) { jobs.shift(); finishJob(j, r.value); }
        }
      } catch (e) { console.warn(e); }
      if (jobs.length) schedule(step);
      else { pumping = false; evict(); }
    };
    schedule(step);
  }

  // 同步渲染（离线测试 / 兜底）
  function renderSampleSync(fk) {
    if (SAMPLES.has(fk)) return;
    const job = makeJob(fk);
    if (!job) return;
    let r;
    do { r = job.gen.next(); } while (!r.done);
    SAMPLES.set(fk, { data: r.value, sr: job.sr, buf: null, used: nowMs() });
    sampleTotal += r.value.length;
  }

  const pinned = new Set();     // 正在使用的曲目所需采样，不可淘汰
  function evict() {
    if (sampleTotal <= SAMPLE_BUDGET) return;
    const arr = Array.from(SAMPLES.entries()).filter(e => !pinned.has(e[0])).sort((a, b) => a[1].used - b[1].used);
    for (const [k, s] of arr) {
      if (sampleTotal <= SAMPLE_BUDGET * 0.8) break;
      SAMPLES.delete(k); sampleTotal -= s.data.length;
    }
  }

  function getBuffer(fk, ctx) {
    const s = SAMPLES.get(fk);
    if (!s) return null;
    s.used = nowMs();
    if (!s.buf) {
      try {
        if (typeof AudioBuffer === 'function') {
          try { s.buf = new AudioBuffer({ length: s.data.length, numberOfChannels: 1, sampleRate: s.sr }); }
          catch (e) { s.buf = ctx.createBuffer(1, s.data.length, s.sr); }
        } else s.buf = ctx.createBuffer(1, s.data.length, s.sr);
        if (s.buf.copyToChannel) s.buf.copyToChannel(s.data, 0); else s.buf.getChannelData(0).set(s.data);
      } catch (e) { return null; }
    }
    return s.buf;
  }

  function prepare(key) {
    const C = compile(key);
    if (!C) return Promise.resolve();
    for (const k of C.sampleKeys) pinned.add(k);
    return Promise.all(C.sampleKeys.map(requestSample)).then(() => undefined);
  }
  function prepareSync(key) {
    const C = compile(key);
    if (!C) return;
    for (const k of C.sampleKeys) renderSampleSync(k);
  }
  function unpinAllBut(keys) {
    pinned.clear();
    for (const key of keys) { const C = compile(key); if (C) for (const k of C.sampleKeys) pinned.add(k); }
  }

  // ============================================================ 共享资源 ==
  // 每个 AudioContext 一份：噪声缓冲、混响脉冲、PeriodicWave 缓存
  function ctxRes(ctx) {
    if (ctx._sgMusic) return ctx._sgMusic;
    const sr = ctx.sampleRate;
    const R = { waves: {}, noise: null };
    const n = Math.round(sr * 2);
    const nb = ctx.createBuffer(1, n, sr);
    const d = nb.getChannelData(0);
    const r = rng(12345);
    for (let i = 0; i < n; i++) d[i] = r() * 2 - 1;
    R.noise = nb;
    ctx._sgMusic = R;
    return R;
  }

  // 程序生成的混响脉冲：早期反射 + 指数衰减的去相关噪声，高频衰减更快
  function makeImpulse(ctx, seconds, decay) {
    const sr = ctx.sampleRate;
    const n = Math.round(sr * seconds);
    const buf = ctx.createBuffer(2, n, sr);
    for (let c = 0; c < 2; c++) {
      const d = buf.getChannelData(c);
      const r = rng(777 + c * 31);
      const pre = Math.round(sr * 0.012);
      let lp = 0;
      for (let i = pre; i < n; i++) {
        const t = (i - pre) / sr;
        const e = Math.exp(-t * 6.9 / decay);
        // 随时间变暗的一阶低通
        const k = 0.75 - 0.6 * Math.min(1, t / decay);
        lp += (r() * 2 - 1 - lp) * k;
        d[i] = lp * e * (t < 0.06 ? t / 0.06 * 0.6 + 0.4 : 1);
      }
      // 早期反射
      const taps = [0.017, 0.023, 0.031, 0.041, 0.053, 0.067, 0.079];
      taps.forEach((tt, k) => {
        const i = Math.round(sr * (tt + c * 0.0031 * (k % 3)));
        if (i < n) d[i] += (k % 2 ? -1 : 1) * 0.5 * Math.exp(-k * 0.35);
      });
      // 归一化能量
      let e2 = 0;
      for (let i = 0; i < n; i++) e2 += d[i] * d[i];
      const g = 1 / Math.sqrt(e2);           // 单位能量：湿声功率 ≈ 干声功率 × 发送量²
      for (let i = 0; i < n; i++) d[i] *= g;
    }
    return buf;
  }

  // 软限幅曲线：|x| ≤ 0.75 线性，其上平滑逼近 0.98（输入域 ±2）
  function softClipCurve() {
    const N = 4097, c = new Float32Array(N);
    for (let i = 0; i < N; i++) {
      const x = (i / (N - 1)) * 4 - 2;
      const a = Math.abs(x);
      const y = a <= 0.75 ? a : 0.75 + 0.23 * Math.tanh((a - 0.75) / 0.23);
      c[i] = Math.sign(x) * y * 0.5;  // 前级 ×0.5 → 后级 ×2 还原
    }
    return c;
  }

  // ============================================================== 引擎 ==
  class Engine {
    constructor(ctx, dest, opts) {
      opts = opts || {};
      this.ctx = ctx;
      this.offline = !!opts.offline;
      this.res = ctxRes(ctx);
      this.tracks = [];
      this.onEnded = null;
      this.volume = opts.volume === undefined ? 1 : opts.volume;
      this.stems = opts.stems || null;           // 测试：只保留这些声部
      const c = ctx;
      // 总线：各曲 → mix → 黏合压缩 → 限幅 → 软削波 → 音量 → dest
      this.mix = c.createGain();
      this.revIn = c.createGain();
      const hp = c.createBiquadFilter(); hp.type = 'highpass'; hp.frequency.value = 180; hp.Q.value = 0.6;
      const lp = c.createBiquadFilter(); lp.type = 'lowpass'; lp.frequency.value = 7500; lp.Q.value = 0.5;
      this.conv = c.createConvolver();
      try { this.conv.normalize = false; } catch (e) { /* 忽略 */ }
      this.conv.buffer = makeImpulse(c, 3.2, 2.5);
      this.revOut = c.createGain(); this.revOut.gain.value = 0.8;
      this.revIn.connect(hp); hp.connect(lp); lp.connect(this.conv); this.conv.connect(this.revOut); this.revOut.connect(this.mix);
      this.glue = c.createDynamicsCompressor();
      setP(this.glue.threshold, -20); setP(this.glue.knee, 10); setP(this.glue.ratio, 2.2);
      setP(this.glue.attack, 0.025); setP(this.glue.release, 0.3);
      this.lim = c.createDynamicsCompressor();
      setP(this.lim.threshold, -4); setP(this.lim.knee, 0); setP(this.lim.ratio, 20);
      setP(this.lim.attack, 0.002); setP(this.lim.release, 0.12);
      this.trim = c.createGain(); this.trim.gain.value = 0.62;  // 抵消压缩器的自动补偿增益
      this.pre = c.createGain(); this.pre.gain.value = 0.5;
      this.shaper = c.createWaveShaper(); this.shaper.curve = softClipCurve();
      try { this.shaper.oversample = '2x'; } catch (e) { /* 忽略 */ }
      this.post = c.createGain(); this.post.gain.value = 2;
      this.out = c.createGain(); this.out.gain.value = this.volume;
      this.mix.connect(this.glue); this.glue.connect(this.lim); this.lim.connect(this.trim);
      this.trim.connect(this.pre); this.pre.connect(this.shaper); this.shaper.connect(this.post); this.post.connect(this.out);
      this.out.connect(dest);
      this.timer = null;
      this.duckGain = 1;
    }

    setVolume(v) {
      this.volume = v;
      try { const t = this.ctx.currentTime; this.out.gain.cancelScheduledValues(t); this.out.gain.setTargetAtTime(v, t, 0.05); } catch (e) { /* 忽略 */ }
    }

    get current() {
      const t = this.tracks.find(x => !x.stopping);
      return t ? t.key : null;
    }

    // 播放曲目（交叉淡入淡出）。返回 Promise（采样就绪、开始播放时 resolve）
    play(key, opts) {
      opts = opts || {};
      const C = compile(key);
      if (!C) return Promise.resolve(false);
      const fadeOut = opts.fade === undefined ? 0.8 : opts.fade;
      const token = this._token = (this._token || 0) + 1;
      const go = () => {
        if (token !== this._token) return false;
        const now = this.ctx.currentTime;
        if (opts.overlay) {
          // 叠加短曲（冲锋乐句）：压低主曲，停掉别的叠加曲
          for (const t of this.tracks) if (!t.stopping && t.overlay) t.stop(now, 0.2);
          this.duck(opts.duck === undefined ? 0.22 : opts.duck, 0.25);
        } else {
          for (const t of this.tracks) if (!t.stopping) t.stop(now, fadeOut);
        }
        const t0 = opts.at !== undefined ? opts.at : now + 0.06;
        const tr = new Track(this, C, t0, opts.fadeIn === undefined ? Math.min(0.6, fadeOut) : opts.fadeIn, !!opts.overlay);
        this.tracks.push(tr);
        unpinAllBut(this.tracks.filter(x => !x.stopping).map(x => x.key));
        this._ensureTimer();
        this.tick();
        return true;
      };
      if (this.offline) { prepareSync(key); return Promise.resolve(go()); }
      return prepare(key).then(go);
    }

    stop(fade) {
      this._token = (this._token || 0) + 1;
      const now = this.ctx.currentTime;
      for (const t of this.tracks) if (!t.stopping) t.stop(now, fade === undefined ? 0.8 : fade);
    }

    // 压低（或恢复）当前非叠加曲目的音量：冲锋乐句叠加时使用
    duck(level, sec) {
      const now = this.ctx.currentTime;
      for (const t of this.tracks) if (!t.stopping && !t.overlay) t.setDuck(level, now, sec === undefined ? 0.3 : sec);
    }

    _ensureTimer() {
      if (this.offline || this.timer) return;
      this.timer = setInterval(() => this.tick(), INTERVAL);
    }

    tick() {
      try {
        const now = this.ctx.currentTime;
        const horizon = now + LOOKAHEAD;
        for (const t of this.tracks) t.scheduleUntil(horizon, now);
        // 清理已结束的曲目
        for (let i = this.tracks.length - 1; i >= 0; i--) {
          const t = this.tracks[i];
          if (t.dead(now)) {
            t.dispose();
            this.tracks.splice(i, 1);
            if (t.finished && !t.stopping) {
              if (t.overlay) this.duck(1, 0.6);
              if (this.onEnded) { try { this.onEnded(t.key, t.overlay); } catch (e) { console.warn(e); } }
            }
          }
        }
        if (!this.tracks.length && this.timer) { clearInterval(this.timer); this.timer = null; }
      } catch (e) { console.warn(e); }
    }

    scheduleUntil(t) { for (const tr of this.tracks) tr.scheduleUntil(t, 0); }

    dispose() {
      if (this.timer) { clearInterval(this.timer); this.timer = null; }
      for (const t of this.tracks) t.dispose();
      this.tracks = [];
      try { this.out.disconnect(); } catch (e) { /* 忽略 */ }
    }
  }

  function setP(p, v) { try { p.value = v; } catch (e) { /* 忽略 */ } }

  // ----------------------------------------------------------- 曲目实例 --
  class Track {
    constructor(eng, C, t0, fadeIn, overlay) {
      this.eng = eng; this.C = C; this.key = C.key; this.overlay = overlay;
      const ctx = this.ctx = eng.ctx;
      const P = C.piece;
      this.rnd = rng(hash(C.key) + 99);
      this.gainTarget = P.gain === undefined ? 1 : P.gain;
      this.bus = ctx.createGain();
      this.send = ctx.createGain();
      this.duckNode = ctx.createGain();
      this.bus.connect(this.duckNode); this.send.connect(eng.revIn);
      this.duckNode.connect(eng.mix);
      const g0 = this.gainTarget, s0 = g0 * (P.reverb === undefined ? 1 : P.reverb);
      if (fadeIn > 0.01) {
        this.bus.gain.setValueAtTime(0, t0); this.bus.gain.linearRampToValueAtTime(g0, t0 + fadeIn);
        this.send.gain.setValueAtTime(0, t0); this.send.gain.linearRampToValueAtTime(s0, t0 + fadeIn);
      } else { this.bus.gain.setValueAtTime(g0, t0); this.send.gain.setValueAtTime(s0, t0); }
      this.channels = {};
      this.nodes = new Set();
      this.segIndex = 0;
      this.seg = null;
      this.t0 = t0;
      this.stopping = false;
      this.finished = false;
      this.endTime = Infinity;
      this.stopAt = Infinity;
      this.loopCount = 0;
      this._startSeg(0, t0);
    }

    channel(name, isDrum) {
      let ch = this.channels[name];
      if (ch) return ch;
      const P = this.C.piece, ctx = this.ctx;
      const V = isDrum ? P.kit[name] : P.voices[name];
      const def = INST[V.inst] || {};
      const input = ctx.createGain();
      let gain = (V.gain === undefined ? 1 : V.gain) * (def.gain === undefined ? 1 : def.gain);
      if (this.eng.stems && this.eng.stems.indexOf(name) < 0) gain = 0;
      input.gain.value = gain;
      let node = input;
      let pan = null;
      const pv = V.pan !== undefined ? V.pan : (def.pan || 0);
      if (ctx.createStereoPanner && pv) { pan = ctx.createStereoPanner(); pan.pan.value = Math.max(-1, Math.min(1, pv)); input.connect(pan); node = pan; }
      node.connect(this.bus);
      const sg = ctx.createGain();
      sg.gain.value = V.rev !== undefined ? V.rev : (def.rev === undefined ? 0.25 : def.rev);
      node.connect(sg); sg.connect(this.send);
      ch = this.channels[name] = { input, pan, sg, def, V, name };
      return ch;
    }

    _startSeg(i, t) {
      const C = this.C;
      if (i >= C.form.length) {
        if (!C.loop) { this.seg = null; this.endTime = t; return; }
        i = C.loopFrom; this.loopCount++;
      }
      this.segIndex = i;
      this.seg = { sec: C.sections[C.form[i]], t0: t, i: 0 };
    }

    // 排程直到 horizon 秒
    scheduleUntil(horizon, now) {
      if (this.stopping) return;
      let guard = 0;
      while (this.seg && guard++ < 64) {
        const seg = this.seg, sec = seg.sec, items = sec.items;
        while (seg.i < items.length) {
          const it = items[seg.i];
          const t = seg.t0 + sec.time(it.beat);
          if (t > horizon) return;
          seg.i++;
          // 主线程卡顿导致落后：短音直接跳过，避免一次涌出一堆音
          if (!this.eng.offline && t < now - 0.08 && it.type !== 'p' && it.type !== 'y') continue;
          try { this.fire(it, seg, t); } catch (e) { console.warn('音乐排程出错', e); }
        }
        const tEnd = seg.t0 + sec.dur;
        if (tEnd > horizon) return;
        this._startSeg(this.segIndex + 1, tEnd);
      }
    }

    // 拍 → 绝对时间（考虑段内渐快渐慢）
    tAt(seg, beat) { return seg.t0 + seg.sec.time(beat); }

    fire(it, seg, t) {
      const def = INST[it.inst];
      if (!def) return;
      const P = this.C.piece;
      const ch = this.channel(it.type === 'd' ? it.lane : it.voice, it.type === 'd');
      if (ch.input.gain.value === 0) return;
      const r = this.rnd;
      // 人性化：时值微偏差与力度起伏；强拍略重（可由乐曲 accents 指定每拍的轻重）
      const meter = this.C.meter;
      const human = beat => {
        const inBar = beat - Math.floor(beat / meter + 1e-9) * meter;
        const ib = Math.round(inBar);
        let m;
        if (Math.abs(inBar - ib) > 1e-6) m = 0.95;
        else if (P.accents) m = P.accents[ib % P.accents.length] || 1;
        else m = ib === 0 ? 1.06 : 1.0;
        return m * (1 + (r() + r() - 1) * 0.06);
      };
      const jit = (r() + r() - 1) * (def.humanT === undefined ? 0.007 : def.humanT);
      const floorT = this.eng.offline ? 0 : this.ctx.currentTime;
      const tt = Math.max(floorT, t + jit);
      if (it.type === 'd') {
        const nv = def.variants || 1;
        const variant = Math.floor(r() * nv);
        const fk = def.name + '|' + drumKey(def, it.art, it.p, variant);
        const K = P.kit[it.lane];
        this.playBuffer(ch, fk, tt, Math.max(0.05, it.vel * human(it.beat)), { rate: 1, dur: K.choke || null, release: 0.08, def });
        return;
      }
      if (it.type === 's') { playPluck(this, ch, def, it, seg, tt, Math.max(0.05, it.vel * human(it.beat))); return; }
      if (it.type === 'y') {
        const t1 = this.tAt(seg, it.beat + it.dur);
        def.play(this, ch, it, tt, t1 - t, Math.max(0.05, it.vel * human(it.beat)), r);
        return;
      }
      if (it.type === 'p') {
        const notes = it.notes.map((n, k) => {
          const nt = Math.max(floorT, this.tAt(seg, n.beat) + (k === 0 ? jit : (r() + r() - 1) * 0.004));
          return { t: nt, d: this.tAt(seg, n.beat + n.dur) - this.tAt(seg, n.beat), p: n.p, f: mtof(n.p), v: Math.max(0.05, n.vel * human(n.beat)), orn: n.orn, gp: n.gp, tp: n.tp };
        });
        // 落后时截去已过去的部分
        for (let k = 1; k < notes.length; k++) if (notes[k].t < notes[k - 1].t + 0.01) notes[k].t = notes[k - 1].t + 0.01;
        def.phrase(this, ch, notes, r);
      }
    }

    // 播放一个预渲染采样
    playBuffer(ch, fk, t, vel, o) {
      const ctx = this.ctx;
      let buf = getBuffer(fk, ctx);
      if (!buf) {
        if (this.eng.offline) { renderSampleSync(fk); buf = getBuffer(fk, ctx); }
        else { requestSample(fk); return null; }
        if (!buf) return null;
      }
      const src = ctx.createBufferSource();
      src.buffer = buf;
      const rate = o.rate || 1;
      src.playbackRate.setValueAtTime(rate, t);
      const g = ctx.createGain();
      const curve = o.def && o.def.velCurve !== undefined ? o.def.velCurve : 1.5;
      const amp = Math.pow(Math.min(1.2, vel), curve);
      g.gain.setValueAtTime(amp, t);
      let out = g;
      if (o.lp) {
        const f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.setValueAtTime(o.lp, t); f.Q.value = 0.4;
        g.connect(f); out = f;
      }
      src.connect(g); out.connect(ch.input);
      const natural = buf.duration / rate;
      let end = t + natural;
      if (o.dur && o.dur < natural) {
        const rel = o.release || 0.1;
        g.gain.setValueAtTime(amp, t + o.dur);
        g.gain.setTargetAtTime(0, t + o.dur, rel / 3);
        end = t + o.dur + rel + 0.02;
      }
      if (o.bend) o.bend(src.playbackRate, t, rate);
      src.start(t);
      try { src.stop(end); } catch (e) { /* 忽略 */ }
      this.track(src, [g, out]);
      return { src, g, amp, end };
    }

    // 登记节点：曲目停止时统一停止，结束后断开
    track(src, extra) {
      const nodes = this.nodes;
      const rec = { src, extra };
      nodes.add(rec);
      src.onended = () => {
        nodes.delete(rec);
        try { src.disconnect(); } catch (e) { /* 忽略 */ }
        if (extra) for (const n of extra) { try { n.disconnect(); } catch (e) { /* 忽略 */ } }
      };
    }

    setDuck(level, now, sec) {
      const g = this.duckNode.gain;
      g.cancelScheduledValues(now);
      g.setValueAtTime(g.value, now);
      g.linearRampToValueAtTime(level, now + sec);
    }

    stop(now, fade) {
      if (this.stopping) return;
      this.stopping = true;
      fade = Math.max(0.02, fade);
      for (const g of [this.bus.gain, this.send.gain]) {
        g.cancelScheduledValues(now);
        g.setValueAtTime(g.value, now);
        g.linearRampToValueAtTime(0, now + fade);
      }
      this.stopAt = now + fade + 0.05;
      for (const rec of this.nodes) {
        try { rec.src.stop(this.stopAt); } catch (e) { /* 已停止或未开始 */ }
      }
    }

    dead(now) {
      if (this.stopping) return now > this.stopAt + 0.1;
      if (this.seg === null && this.endTime < Infinity) {
        // 非循环曲：排程完毕且尾音结束
        if (!this.nodes.size || now > this.endTime + 6) { this.finished = true; return now > this.endTime; }
      }
      return false;
    }

    dispose() {
      for (const rec of this.nodes) { try { rec.src.stop(); } catch (e) { /* 忽略 */ } }
      this.nodes.clear();
      try { this.bus.disconnect(); this.send.disconnect(); this.duckNode.disconnect(); } catch (e) { /* 忽略 */ }
      for (const k in this.channels) { const c = this.channels[k]; try { c.input.disconnect(); if (c.pan) c.pan.disconnect(); c.sg.disconnect(); } catch (e) { /* 忽略 */ } }
    }
  }

  // ------------------------------------------------- 拨弦 / 打击类演奏法 --
  // 处理：扫弦（和弦依次拨出）、轮指（快速重复）、倚音、刮奏、按滑 / 揉弦 / 下滑（变速）
  function playPluck(tr, ch, def, it, seg, t, vel) {
    const ps = it.ps;
    const o = it.orn;
    const t1 = tr.tAt(seg, it.beat + it.dur);
    const durS = Math.max(0.05, t1 - tr.tAt(seg, it.beat));
    const ring = def.ring === undefined ? 0.5 : def.ring;
    const fkOf = p => def.name + '|' + sampleKeyPitch(def, p);
    const brightLp = def.velLp ? def.velLp[0] + def.velLp[1] * vel : null;
    const r = tr.rnd;
    const holdFor = d => (def.ringAll ? null : d + ring);
    // 刮奏：在本音之前快速拨过音阶
    if (it.run && it.run.length) {
      const n = it.run.length;
      const span = Math.min(0.42, 0.045 * n);
      it.run.forEach((p, k) => {
        const tk = t - span + (k * span) / n;
        tr.playBuffer(ch, fkOf(p), Math.max(tr.eng.offline ? 0 : tr.ctx.currentTime, tk), vel * (0.35 + 0.45 * k / n), { def, dur: holdFor(0.25), release: 0.4, lp: brightLp });
      });
    }
    if (it.gp !== undefined) {
      tr.playBuffer(ch, fkOf(it.gp), Math.max(0, t - 0.075), vel * 0.7, { def, dur: 0.09, release: 0.05, lp: brightLp });
    }
    // 弯音（按滑、揉弦、下滑）
    let bend = null;
    if (o.slide || o.vib || o.fall || def.attackBend) {
      bend = (param, t0, rate) => {
        if (o.slide) {
          param.setValueAtTime(rate * Math.pow(2, -(def.slideSemi || 1.6) / 12), t0);
          param.setValueAtTime(rate * Math.pow(2, -(def.slideSemi || 1.6) / 12), t0 + 0.05);
          param.exponentialRampToValueAtTime(rate, t0 + 0.2);
        } else if (def.attackBend) {
          // 拨弦瞬间张力略高：音高先略高再回落
          param.setValueAtTime(rate * Math.pow(2, def.attackBend / 1200), t0);
          param.exponentialRampToValueAtTime(rate, t0 + 0.09);
        }
        if (o.vib) {
          // 揉弦：以小段折线近似正弦（playbackRate 不便接 LFO 时也可用）
          const depth = (def.vibCents || 22) * o.vib / 1200;
          const rateHz = def.vibRate || 5.5;
          const start = t0 + Math.min(0.25, durS * 0.3);
          const end = t0 + Math.min(durS + ring, 4);
          let k = 0;
          for (let tt = start; tt < end; tt += 0.5 / rateHz, k++) {
            const sgn = k % 2 ? -1 : 1;
            const amt = Math.min(1, (tt - start) / 0.3);
            param.linearRampToValueAtTime(rate * (1 + sgn * depth * amt * 0.69), tt + 0.25 / rateHz);
          }
          param.linearRampToValueAtTime(rate, end + 0.05);
        }
        if (o.fall) {
          const fs = t0 + durS * 0.55;
          param.setValueAtTime(rate, fs);
          param.exponentialRampToValueAtTime(rate * Math.pow(2, -1.5 / 12), t0 + durS);
        }
      };
    }
    const strum = def.strum === undefined ? 0.016 : def.strum;
    if (o.trem) {
      // 轮指 / 扫轮：在时值内快速重复，前一下被后一下掐断
      const rateHz = def.tremRate || 14;
      const n = Math.max(2, Math.round(durS * rateHz));
      const step = durS / n;
      let prev = [];
      for (let k = 0; k < n; k++) {
        const tk = t + k * step + (r() - 0.5) * 0.006;
        const pk = it.tp !== undefined && def.trill && k % 2 ? [it.tp] : ps;
        const vk = vel * (k === 0 ? 1 : (0.62 + 0.18 * ((k % 4) === 0 ? 1 : (k % 2 ? 0.2 : 0.6)) + 0.1 * r()));
        for (const pv of prev) { try { pv.g.gain.setTargetAtTime(0, tk, 0.006); pv.src.stop(tk + 0.06); } catch (e) { /* 忽略 */ } }
        prev = [];
        pk.forEach((p, j) => {
          const h = tr.playBuffer(ch, fkOf(p), tk + j * strum * 0.5, vk, { def, dur: k === n - 1 ? holdFor(step) : step + 0.08, release: k === n - 1 ? 0.5 : 0.05, lp: brightLp });
          if (h) prev.push(h);
        });
      }
      return;
    }
    ps.forEach((p, j) => {
      tr.playBuffer(ch, fkOf(p), t + j * strum, vel * (j === ps.length - 1 ? 1 : 0.85), { def, dur: holdFor(durS), release: def.release || 0.35, lp: brightLp, bend: j === ps.length - 1 ? bend : null });
    });
  }

  // ====================================================== 离线渲染（测试） ==
  async function renderOffline(key, seconds, opts) {
    opts = opts || {};
    const sr = opts.sampleRate || 44100;
    const OAC = window.OfflineAudioContext || window.webkitOfflineAudioContext;
    const len = Math.round(sr * seconds);
    const ctx = new OAC(2, len, sr);
    const tPrep = nowMs();
    await prepare(key);
    const prepMs = nowMs() - tPrep;
    const t0 = nowMs();
    const eng = new Engine(ctx, ctx.destination, { offline: true, volume: opts.volume === undefined ? 1 : opts.volume, stems: opts.stems });
    if (opts.raw) {
      // 测试：绕过压缩 / 限幅，测量混音本身的电平
      eng.mix.disconnect(); eng.mix.connect(eng.out);
    }
    await eng.play(key, { fade: 0, fadeIn: 0, at: 0 });
    let schedMs;
    let buffer;
    if (opts.incremental === false) {
      eng.scheduleUntil(seconds);
      schedMs = nowMs() - t0;
      buffer = await ctx.startRendering();
    } else {
      // 与实时播放相同：每 0.25 秒暂停一次，只排程前瞻窗口内的音符（图中节点数与实时一致，
      // 已结束的节点也会被断开），渲染耗时 ≈ 实时播放时音频线程的负载
      const STEP = 0.25, q = 128 / sr;
      eng.scheduleUntil(LOOKAHEAD);
      let ts = STEP;
      const hook = () => {
        if (ts >= seconds) return;
        const at = Math.round(ts / q) * q;
        ctx.suspend(at).then(() => {
          eng.scheduleUntil(at + LOOKAHEAD);
          ts += STEP; hook();
          ctx.resume();
        });
      };
      hook();
      schedMs = nowMs() - t0;
      buffer = await ctx.startRendering();
    }
    const ms = nowMs() - t0;
    let peak = 0, sum = 0, nan = 0;
    for (let c = 0; c < buffer.numberOfChannels; c++) {
      const d = buffer.getChannelData(c);
      for (let i = 0; i < d.length; i++) {
        const v = d[i];
        if (v !== v) { nan++; continue; }
        const a = v < 0 ? -v : v;
        if (a > peak) peak = a;
        sum += v * v;
      }
    }
    const rms = Math.sqrt(sum / (len * buffer.numberOfChannels));
    return { key, buffer, ms, prepMs, schedMs, peak, rms, nan, sr, seconds };
  }

  // ============================================================== 导出 ==
  SG.Music = {
    version: 2,
    INST, PIECES,
    defineInst, add, resolve, compile, prepare, prepareSync,
    get(key) { return PIECES[key] || null; },
    keys() { return Object.keys(PIECES); },
    validate(key) { const C = compile(key); return C ? C.errors.slice() : ['未知曲目 ' + key]; },
    duration(key) { const C = compile(key); return C ? { intro: C.introDur, loop: C.loopDur, total: C.introDur + C.loopDur } : null; },
    Engine,
    renderOffline,
    cultureFallback: CULTURE_FALLBACK,
    // 供乐器模块使用的工具
    util: { rng, hash, mtof, keyToMidi, ctxRes, nowMs },
    get sampleStats() { return { count: SAMPLES.size, floats: sampleTotal, jobs: jobs.length }; },
    _samples: SAMPLES,
  };
})();
