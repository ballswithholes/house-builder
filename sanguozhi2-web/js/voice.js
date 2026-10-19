'use strict';
/* ==========================================================================
   人声 · SG.Voice（AUDIO-V4 乙部 §B3–§B10；第四版要求 12：每个主菜单动作与战斗里都有人声，按君主与他说的语言）
   每个人说自己的语言（SG.VoiceData.LINGUA，26 种），声音按性别 / 年龄 / 气质（profile），台词编号走登记表
   （SG.VoiceData.CUES → SG.Reg.voiceCues，123 条，缺的沿 fb 回退到核心台词或程序化喊叫），字幕给原文与中文。
   语音包是离线神经 TTS 生成的 Opus（voice/<lingua>-<voice>.js，tools/voice-build/），按语言懒加载；
   Opus 解码失败（较旧的 Safari）时改用 voice/mp3/ 下的同名包；包未到或未解码完时 ≤ 120 ms 内改放程序化喊叫 + 字幕。

   接口（§B7.3）
     SG.Voice.init()                         设置、清单、编解码探测、钩子；不会抛错（audio.js 的 SG.Sfx.init 会调用）
     SG.Voice.say(gen, cue, opts) → Promise<{ dur, text, zh, lingua } | null>
         gen：武将对象 / 姓名 / 'herald' / 'merchant'；opts：{ pri, pan, side, scene: 'strategy'|'battle'|'duel', force, subtitle = true,
               auto（委任 / 快速 / 电脑回合：只放 pri ≥ 2 且只放玩家一方或敌方主将）, player（说话人属于玩家一方）, commander,
               vars: { city }（模板台词）, onStart(dur) }；片段结束时兑现；被丢弃 / 关闭时为 null
     SG.Voice.cry(gen, kind, opts)            程序化用力声（§B5：light heavy charge hurt knockout shout roar …）
     SG.Voice.crowd(kind, n, opts)           军队群呼（charge cheer dismay）
     SG.Voice.profile(gen | 'herald' | 'merchant', lingua?) → { lingua, voice, pack, sex, age, band, temper, ruler, pitchK, rate,
         presence: { f, q, gain }, body, grit, tremolo, pan, space }   纯函数（测试用）
     SG.Voice.linguaOf(gen) / pickLine(gen, cue, opts) → { lingua, cue, variant, text, zh, cry, pack, clipId }（纯函数）
     SG.Voice.preload(linguas) → Promise     SG.Voice.settings（§B10.4）  SG.Voice.busy  SG.Voice.log（最近 200 条决定）
     SG.Voice.hooks                          把人声接到现有的指令与战斗事件上（SG.Events 'command' / 'month.phase' /
                                             'faction.succession'，BattleController / BattleView / Clash / DuelGame 的公开方法外包一层）；
                                             各拥有者直接调用 SG.Voice.say 之后可用 SG.Voice.hooks.off(name) 关掉对应的一层
   SG.VoicePacks.add(meta, base64)           语音包脚本调用（包可先于本文件载入：先排队）
   ========================================================================== */
(function () {
  const W = typeof window !== 'undefined' ? window : globalThis;
  const SG = W.SG = W.SG || {};
  const D = () => SG.VoiceData;
  const now = () => (typeof performance !== 'undefined' && performance.now ? performance.now() : Date.now()) / 1000;

  // ---------------------------------------------------------------- small helpers --
  function fnv(s) { let h = 2166136261 >>> 0; s = String(s); for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; } return h >>> 0; }
  const clamp = (x, a, b) => Math.max(a, Math.min(b, x));
  function G() { return SG.G || null; }
  function genOf(x) {
    if (!x) return null;
    if (typeof x === 'object') return x;
    const g = G();
    if (g && g.generals) { const o = g.generals.find(y => y.name === x); if (o) return o; }
    return scenGen(x) || { name: String(x) };
  }
  // scenario rows: name → { war, intel, pol, factionKey, city, culture, born }
  let scenIdx = null;
  function scenIndex() {
    if (scenIdx) return scenIdx;
    scenIdx = { byScen: {}, cityFac: {}, facCult: {}, facKeys: new Set() };
    const S = SG.Scenarios || {};
    for (const k of Object.keys(S)) {
      const sc = S[k]; if (!sc || !sc.Generals) continue;
      const m = scenIdx.byScen[k] = {};
      const cf = scenIdx.cityFac[k] = {};
      for (const l of sc.Factions || []) { const p = l.split('|'); scenIdx.facCult[p[0]] = p[6] || (SG.WorldData ? SG.WorldData.cultureOfFaction(p[0]) : 'han'); scenIdx.facKeys.add(p[0]); }
      for (const l of sc.Generals) {
        const p = l.split('|');
        m[p[0]] = { name: p[0], war: +p[1], intel: +p[2], pol: +p[3], factionKey: p[4] === '-' ? null : p[4], city: p[5], culture: p[9] || null, born: p[10] ? +p[10] : null };
        if (p[4] && p[4] !== '-') (cf[p[5]] = cf[p[5]] || new Set()).add(p[4]);
      }
    }
    return scenIdx;
  }
  function scenName() { const g = G(); return (g && g.scenario) || 'world'; }
  function scenGen(name) {
    const I = scenIndex();
    return (I.byScen[scenName()] || {})[name] || (I.byScen.world || {})[name] || (I.byScen.classic || {})[name] || null;
  }
  function cultureOf(gen) {
    if (gen.culture) return gen.culture;
    const s = scenGen(gen.name); if (s && s.culture) return s.culture;
    try { if (SG.WorldData) return SG.WorldData.cultureOfGeneral(gen.name) || 'han'; } catch (e) { /* 忽略 */ }
    return 'han';
  }
  function facCulture(key) {
    const I = scenIndex();
    if (I.facCult[key]) return I.facCult[key];
    try { if (SG.WorldData) return SG.WorldData.cultureOfFaction(key); } catch (e) { /* 忽略 */ }
    return 'han';
  }
  function sexOf(gen) {
    if (gen.sex) return gen.sex === 'f' ? 'f' : 'm';
    if (gen.female) return 'f';
    try { if (SG.WorldData) return SG.WorldData.sexOf(gen.name) === 'f' ? 'f' : 'm'; } catch (e) { /* 忽略 */ }
    return 'm';
  }
  function bornOf(gen) {
    if (gen.born) return gen.born;
    const s = scenGen(gen.name); if (s && s.born) return s.born;
    try { if (SG.WorldData) return SG.WorldData.bornOf(gen.name); } catch (e) { /* 忽略 */ }
    return null;
  }
  function isRuler(gen) {
    const g = G();
    try { if (g && gen && gen.faction >= 0 && g.factions[gen.faction] && g.factions[gen.faction].ruler === gen.id) return true; } catch (e) { /* 忽略 */ }
    if (gen && gen._ruler) return true;
    // outside a game: the starting rulers of both scenarios
    const S = SG.Scenarios || {};
    for (const k of Object.keys(S)) for (const l of (S[k] && S[k].Factions) || []) if (l.split('|')[2] === gen.name) return true;
    return false;
  }
  function temperOf(gen) {
    const s = (gen.war === undefined && scenGen(gen.name)) || gen;
    const w = s.war | 0, i = s.intel | 0, p = s.pol | 0;
    const m = Math.max(w, i, p);
    if (w === m && w >= i + 6) return 'martial';
    if (i === m && i >= p) return 'scholar';
    if (p === m) return 'statesman';
    return 'martial';
  }

  // ---------------------------------------------------------------- settings (§B10.4; defaults in SG.VoiceData.CONFIG) --
  const SET_IDS = { language: 'audio.voiceLang', frequency: 'audio.voiceFreq', subtitles: 'audio.subtitles', voice: 'audio.voice', readAloud: 'audio.readAloud' };
  const settings = {
    get language() { return getSet('language', 'native'); },
    get frequency() { return getSet('frequency', 'essential'); },
    get subtitles() { return getSet('subtitles', 'both'); },
    get volume() { const v = getSet('voice', 100); return clamp(+v / 100, 0, 1.5); },
    get readAloud() { return !!getSet('readAloud', false); },
    set(k, v) { const id = SET_IDS[k]; if (SG.Settings && SG.Settings.has && SG.Settings.has(id)) return SG.Settings.set(id, v); mem[k] = v; return true; },
  };
  const mem = {};
  function getSet(k, dflt) {
    const id = SET_IDS[k];
    try { if (SG.Settings && SG.Settings.has && SG.Settings.has(id)) { const v = SG.Settings.get(id); if (v !== undefined) return v; } } catch (e) { /* 忽略 */ }
    if (mem[k] !== undefined) return mem[k];
    const C = (D() && D().CONFIG) || {};
    if (k === 'voice') return C.volumes ? C.volumes.voice : 100;
    return C[k] !== undefined ? C[k] : dflt;
  }

  // ---------------------------------------------------------------- lingua (§B3) --
  function linguaRaw(gen) {
    const VD = D();
    if (!VD) return 'zh';
    if (gen === 'herald' || gen === 'merchant') return null;
    const cul = cultureOf(gen);
    const s = scenGen(gen.name);
    const g = G();
    // (1) the starting faction's lingua when the general's culture is that faction's culture
    let fk = s && s.factionKey;
    const C = VD.CONFIG || {};
    if (!C.hiredKeepLanguage && g && gen.faction >= 0 && g.factions[gen.faction]) fk = g.factions[gen.faction].key;
    if (fk && VD.FACTION_LINGUA[fk] && facCulture(fk) === cul) return VD.FACTION_LINGUA[fk];
    // (3) unaffiliated: refined by the starting city (a faction of his culture starting there speaks his city's tongue)
    if (s && !s.factionKey && s.city) {
      const I = scenIndex();
      for (const sc of [scenName(), 'world', 'classic']) {
        const fs = (I.cityFac[sc] || {})[s.city];
        if (fs) for (const f of fs) if (VD.FACTION_LINGUA[f] && facCulture(f) === cul) return VD.FACTION_LINGUA[f];
      }
    }
    // (2) the culture's lingua (a Han officer serving 鲜卑 speaks Chinese; a Xianbei serving 曹操 speaks Mongolic)
    return VD.CULTURE_LINGUA[cul] || VD.CULTURE_LINGUA[SG.cultureBase ? SG.cultureBase(cul) : cul] || 'zh';
  }
  function linguaOf(gen, opts) {
    gen = genOf(gen);
    if (settings.language === 'zh' && !(opts && opts.native)) return 'zh';
    return linguaRaw(gen) || 'zh';
  }
  // the lingua whose pack exists (QA-failed or missing packs walk LINGUA.fb; [] / '~' ends in procedural cries)
  function usableLingua(lg) {
    const VD = D(), M = SG.VoiceManifest;
    const seen = new Set();
    let cur = lg;
    while (cur && !seen.has(cur)) {
      seen.add(cur);
      const L = VD.LINGUA[cur];
      if (L && (!M || ((M.failed || []).indexOf(cur) < 0 && L.packs.some(p => M.packs[p])))) return cur;
      cur = L && L.fb && L.fb.find(x => !seen.has(x));
    }
    return null;
  }

  // ---------------------------------------------------------------- profile (§B4) --
  const BAND_K = { young: 1.05, prime: 1.0, old: 0.95, elder: 0.91 };
  const BODY = { young: -2, prime: 0, old: 1.5, elder: 2.5 };
  function profile(x, lingua) {
    const VD = D();
    if (x === 'herald' || x === 'merchant') {
      const P = VD.PROFILES[x];
      const lg = lingua || (G() && G().playerFaction ? linguaOf(G().ruler(G().player)) : 'zh');
      const L = VD.LINGUA[lg] || VD.LINGUA.zh;
      const vk = P.voice.find(k => L.voices[k] && packOf(lg, k)) || P.voice.find(k => L.voices[k]) || 'm';
      const pk = Math.pow(2, P.pitch / 12) * P.rate;
      return { lingua: lg, voice: vk, pack: packOf(lg, vk) || packOf(lg, 'm') || packOf(lg, 'f'), sex: vk === 'f' ? 'f' : 'm', age: 40, band: 'prime', temper: 'statesman', ruler: false,
        pitchK: clamp(pk, 0.86, 1.12), rate: P.rate, pitchSt: P.pitch, presence: { f: 2000, q: 1.2, gain: 0 }, body: 0, grit: 0, tremolo: 0, pan: 0, space: 0, gain: P.gain, who: x };
    }
    const gen = genOf(x);
    const g = G();
    const ruler = isRuler(gen);
    const year = (g && g.year) || 190;
    const born = bornOf(gen);
    const age = born ? year - born : (ruler ? 45 : 35);
    const band = age < 25 ? 'young' : age < 55 ? 'prime' : age < 70 ? 'old' : 'elder';
    const T = temperOf(gen);
    const h = fnv(gen.name);
    const pitchK = clamp(BAND_K[band] * (1 + ((h & 255) / 255 - 0.5) * 0.08), 0.86, 1.12);
    const lg = lingua || linguaOf(gen);
    const L = VD.LINGUA[lg] || VD.LINGUA.zh;
    const sex = sexOf(gen);
    // base voice: the speaker's sex where the lingua has it; else the other with a mild shift only (§B4.1)
    let voice = sex, shift = 1, presenceAdd = 0, shelfAdd = 0;
    if (!L.voices[voice]) {
      voice = sex === 'f' ? 'm' : 'f';
      if (sex === 'f') { shift = 1.12; presenceAdd = 2; } else { shift = 0.88; shelfAdd = 3; }
    }
    // Saka-accented Prakrit for Khotan and Kashgar (§B3)
    const s = scenGen(gen.name);
    if (lg === 'pra' && s && s.factionKey && VD.SAKA_VOICE[s.factionKey] && L.voices.saka) voice = 'saka';
    const grit = Math.min(0.6, (T === 'martial' ? 0.25 : 0) + (band === 'old' || band === 'elder' ? 0.10 : 0));
    return {
      lingua: lg, voice, pack: packOf(lg, voice), sex, age, band, temper: T, ruler, name: gen.name,
      pitchK, rate: +(pitchK * shift).toFixed(4),
      presence: { f: Math.round(1400 + ((h >>> 8) & 255) / 255 * 1400), q: 1.2, gain: +(-3 + ((h >>> 16) & 255) / 255 * 6 + presenceAdd).toFixed(2) },
      body: BODY[band] + (T === 'martial' ? 1.5 : 0) + shelfAdd, grit, tremolo: band === 'elder' ? 0.025 : 0, pan: 0, space: 0,
    };
  }
  // §B4.2 uniqueness: two speakers with the same base voice in one scene (duel pair, diplomacy, adjacent units within 3 s)
  // and |ΔpitchK| < 0.05 → the second gets × 1.07 (or × 0.93 if that would exceed 1.12). Pure; returns the adjusted rate.
  // (direction-aware: the second moves AWAY from the first — up by 7 % if it is already the higher one, else down — and the
  // other way only when that would leave 0.86 … 1.12; so the two always end ≥ 0.05 apart)
  function distinctRate(prof, others) {
    let rate = prof.rate;
    for (const o of others || []) {
      if (!o || o === prof || o.pack !== prof.pack) continue;
      const ro = o.rateEff || o.rate;
      if (Math.abs(ro - rate) >= 0.05) continue;
      const k = rate / (prof.pitchK || 1);           // the sex-shift factor (1 for most voices)
      const upOk = prof.pitchK * 1.07 <= 1.12, downOk = prof.pitchK * 0.93 >= 0.86;
      const up = rate >= ro ? upOk || !downOk : !downOk;
      rate = +(rate * (up ? 1.07 : 0.93)).toFixed(4);
      if (Math.abs(ro - rate) < 0.05) rate = +((ro + (up ? 0.05 : -0.05) * k)).toFixed(4);
    }
    return rate;
  }
  const recent = [];        // { name, pack, rateEff, t } of the speakers of the last 3 s
  function packOf(lg, voice) { const id = lg + '-' + voice; const L = D().LINGUA[lg]; return L && L.packs.indexOf(id) >= 0 ? id : null; }
  const nameOf = x => (x && typeof x === 'object' ? x.name : x) || '';
  // the base pack a speaker uses for this cue (the §B4.2 comparison); cries compare by the rendered voice (sex) and pitchK
  function packFor(prof, cue) { const lg = usableLingua(prof.lingua); return lg ? voicePack(lg, prof, cue) : null; }
  const cryVoiceKey = prof => 'cry:' + (prof.sex === 'f' ? 'f' : 'm');
  // §B4.2 in one scene: opts.pair = [first, second] (duel: challenger / defender; diplomacy: the player's ruler / the partner).
  // The second of the pair moves away from the first whatever the time between their lines; the first keeps his voice.
  // null = not one of a pair (battle units: the 3 s window of adjacent speakers).  cue null = a cry.
  function sceneOthers(prof, cue, opts) {
    const pr = opts && opts.pair;
    if (!pr || pr.length !== 2 || !pr[0] || !pr[1]) return null;
    const n0 = nameOf(pr[0]), n1 = nameOf(pr[1]);
    if (!n0 || !n1 || n0 === n1) return null;
    if (prof.name === n0) return [];
    if (prof.name !== n1) return null;
    const p0 = profile(genOf(pr[0]));
    return cue === null ? [{ name: n0, pack: cryVoiceKey(p0), rateEff: p0.pitchK }] : [{ name: n0, pack: packFor(p0, cue), rateEff: p0.rate }];
  }

  // ---------------------------------------------------------------- line choice --
  const lastVar = new Map();        // speaker|cue → last variant index (never the same twice in a row)
  const varCount = new Map();
  function resolveCue(lg, cue) {
    const VD = D();
    const lines = VD.LINES[lg] || {};
    let c = cue, guard = 0;
    while (c && guard++ < 8) {
      if (c[0] === '~') return { cue: c, cry: c.slice(1), lines: null };
      if (lines[c] && lines[c].length) return { cue: c, lines: lines[c] };
      const def = VD.CUES[c];
      c = def && def.fb;
    }
    return null;
  }
  function zhOf(cue, variant) { const c = D().CUES[cue]; if (!c) return ''; return c.zh[variant] || c.zh[0] || ''; }
  // pure: which line would this speaker say?  opts: { vars, sig, lingua, variant }
  function pickLine(x, cue, opts) {
    opts = opts || {};
    const VD = D();
    const who = x === 'herald' || x === 'merchant' ? x : genOf(x);
    const prof = profile(who, opts.lingua);
    let lg = usableLingua(prof.lingua) || null;
    // signature line (§B9): the ruler's own lingua and text
    if (cue === 'sig') {
      const s = VD.SIGNATURE[typeof who === 'object' ? who.name : who];
      if (!s) return pickLine(x, 'victory', opts);
      const sl = settings.language === 'zh' ? 'zh' : s.lingua;
      const text = settings.language === 'zh' && s.lingua !== 'zh' ? VD.CUES.victory.zh[0] : s.text;
      return { lingua: sl, cue: 'sig', variant: who.name, text, zh: s.lingua === 'zh' || sl === 'zh' ? '' : (s.zh || ''), cry: null, pack: usableLingua(sl) === sl ? (packOf(sl, prof.sex === 'f' ? 'f' : 'm') || packOf(sl, 'm') || packOf(sl, 'f')) : null, clipId: 'sig#' + who.name, prof, sig: true };
    }
    if (!lg) {   // no usable lingua: the cue's category cry with the Chinese subtitle
      const def = VD.CUES[cue] || {};
      return { lingua: prof.lingua, cue, variant: 0, text: '', zh: zhOf(cue, 0), cry: def.del === 'shout' ? 'shout' : def.del === 'sigh' ? 'sigh' : 'hm', pack: null, clipId: null, prof };
    }
    // ruler interjection (RULER_TAG) for the court languages (Parthian / Persian kings, Sabaean rulers, Kroraina)
    if (prof.ruler && VD.RULER_TAG[lg] && VD.RULER_TAG[lg][cue] && rulerTagApplies(lg, who)) {
      return { lingua: lg, cue: 'rtag.' + cue, variant: 0, text: VD.RULER_TAG[lg][cue], zh: zhOf(cue, 0), cry: null, pack: voicePack(lg, prof, cue), clipId: 'rtag.' + cue + '#0', prof };
    }
    const r = resolveCue(lg, cue);
    if (!r) return null;
    if (r.cry) return { lingua: lg, cue: r.cue, variant: 0, text: '', zh: zhOf(cue, 0), cry: r.cry, pack: null, clipId: null, prof };
    const key = (typeof who === 'object' ? who.name : who) + '|' + r.cue;
    let v = opts.variant;
    if (v === undefined) {
      const n = r.lines.length;
      const k = (varCount.get(key) || 0); varCount.set(key, k + 1);
      v = n > 1 ? (fnv(key + '#' + k) % n) : 0;
      if (n > 1 && lastVar.get(key) === v) v = (v + 1) % n;
      lastVar.set(key, v);
    }
    let text = r.lines[v];
    let cry = null;
    const m = /~([a-z.]+)/.exec(text);
    if (m) { cry = m[1]; text = text.replace(/~[a-z.]+/g, '').trim(); }
    if (opts.vars && VD.CUES[r.cue] && VD.CUES[r.cue].tpl && lg === 'zh') {
      for (const [k, tpl] of Object.entries(VD.CUES[r.cue].tpl)) if (opts.vars[k] !== undefined) text = tpl.replace('{' + k + '}', opts.vars[k]);
    }
    const zh = lg === 'zh' ? '' : zhOf(r.cue, v);
    return { lingua: lg, cue: r.cue, variant: v, text, zh, cry: text ? cry : (cry || 'hm'), cryAfter: !!(text && cry), pack: text ? voicePack(lg, prof, r.cue) : null, clipId: text ? r.cue + '#' + v : null, prof };
  }
  function rulerTagApplies(lg, gen) {
    const s = scenGen(gen.name);
    const fk = s && s.factionKey;
    if (lg === 'ar') return fk === 'saba' || fk === 'himyar' || fk === 'hadramawt';
    if (lg === 'pra') return fk === 'kroraina';
    if (lg === 'fa') return fk === 'parthia' || fk === 'persis';
    return true;
  }
  function voicePack(lg, prof, cue) {
    const def = D().CUES[cue] || {};
    // Gothic battle lines use the angry Thorsten speaker (§B4.3)
    if (lg === 'got' && (def.del === 'shout' || def.scene === 'battle') && packOf('got', 'mBattle')) return 'got-mBattle';
    return packOf(lg, prof.voice) || packOf(lg, 'm') || packOf(lg, 'f');
  }

  // ---------------------------------------------------------------- packs (§B7.1) --
  const Packs = SG.VoicePacks && SG.VoicePacks._q ? SG.VoicePacks : { _q: [] };
  const packs = {};               // id → { meta, b64, bytes (Uint8Array, lazily) }
  const loading = {};             // id → Promise
  Packs.add = function (meta, b64) { packs[meta.id] = { meta, b64, bytes: null }; if (loading[meta.id] && loading[meta.id]._res) loading[meta.id]._res(true); };
  Packs.get = id => packs[id] || null;
  Packs.loaded = id => !!packs[id];
  Packs.ids = () => Object.keys(packs);
  for (const [m, d] of Packs._q.splice(0)) Packs.add(m, d);
  SG.VoicePacks = Packs;
  const base = (function () {
    try { const s = typeof document !== 'undefined' && document.currentScript && document.currentScript.src; if (s) return s.replace(/js\/voice\.js(\?.*)?$/, ''); } catch (e) { /* 忽略 */ }
    return '';
  })();
  let useMp3 = false;
  function loadScript(src) {
    return new Promise(res => {
      if (typeof document === 'undefined') { res(false); return; }
      const el = document.createElement('script');
      el.src = src; el.async = true;
      el.onload = () => res(true); el.onerror = () => res(false);
      (document.head || document.documentElement).appendChild(el);
    });
  }
  function loadPack(id) {
    if (!id) return Promise.resolve(false);
    if (packs[id] && (packs[id].meta.codec === 'mp3') === useMp3) return Promise.resolve(true);
    const lk = id + (useMp3 ? '|mp3' : '|opus');
    if (loading[lk]) return loading[lk];
    const M = SG.VoiceManifest;
    const e = M && M.packs[id];
    if (!e || (useMp3 && !e.mp3)) return Promise.resolve(false);
    let res;
    const p = new Promise(r => { res = r; });
    loading[lk] = p;
    loading[id] = { _res: v => res(v) };          // Packs.add resolves the request of the pack's id
    loadScript(base + (useMp3 ? e.mp3 : e.file)).then(ok => { if (!ok) { delete loading[lk]; res(false); } else if (packs[id]) res(true); });
    return p;
  }
  function preload(linguas) {
    const ids = [];
    for (const lg of linguas || []) { const u = usableLingua(lg); const L = u && D().LINGUA[u]; if (L) for (const p of L.packs) if (SG.VoiceManifest && SG.VoiceManifest.packs[p]) ids.push(p); }
    return Promise.all(ids.map(loadPack));
  }
  function b64ToBytes(b64) {
    if (typeof atob === 'function') { const s = atob(b64); const u = new Uint8Array(s.length); for (let i = 0; i < s.length; i++) u[i] = s.charCodeAt(i); return u; }
    return new Uint8Array(Buffer.from(b64, 'base64'));
  }
  function clipEntry(packId, clipId) {
    const P = packs[packId]; if (!P) return null;
    const i = clipId.lastIndexOf('#');
    const cue = clipId.slice(0, i), v = clipId.slice(i + 1);
    if (cue === 'sig') return P.meta.sig ? P.meta.sig[v] || null : null;
    const arr = P.meta.clips[cue];
    return arr ? arr[+v] || null : null;
  }

  // ---------------------------------------------------------------- decode cache (LRU ≤ 6 MB) --
  const decoded = new Map();     // packId|clipId → AudioBuffer
  const decoding = new Map();    // → Promise<AudioBuffer|null>
  let cacheBytes = 0;
  function ctxOf() { try { return SG.Sfx && SG.Sfx.context; } catch (e) { return null; } }
  function decodeClip(packId, clipId) {
    const key = packId + '|' + clipId;
    if (decoded.has(key)) { const b = decoded.get(key); decoded.delete(key); decoded.set(key, b); return Promise.resolve(b); }
    if (decoding.has(key)) return decoding.get(key);
    const ctx = ctxOf();
    const P = packs[packId];
    const ent = clipEntry(packId, clipId);
    if (!ctx || !P || !ent) return Promise.resolve(null);
    if (!P.bytes) P.bytes = b64ToBytes(P.b64);
    const ab = P.bytes.slice(ent[0], ent[0] + ent[1]).buffer;
    const pr = new Promise(res => {
      let done = false;
      const fin = b => { if (done) return; done = true; decoding.delete(key); if (b) { try { b = trimMp3(ctx, b, ent, P.meta, key); } catch (e) { /* 原样 */ } store(key, b); } res(b || null); };
      try {
        const r = ctx.decodeAudioData(ab, b => fin(b), () => fin(null));
        if (r && r.then) r.then(fin, () => fin(null));
      } catch (e) { fin(null); }
    });
    decoding.set(key, pr);
    return pr;
  }
  // MP3 fallback (§B7.1): the packs keep the Xing / LAME gapless header and each clip entry carries [.., lead, pad] (samples at
  // 22.05 kHz). A decoder that read the header returns the clip at its length; one that did not returns the encoder + decoder
  // delay (and, if it decoded the Xing frame as audio, 576 samples more) before the voice and the padding after it: cut those
  // so the voice starts on time and the clip has its header length (±20 ms, §3.2)
  const mp3Trim = new Map();     // key → { raw, off } (tests/voice.html)
  function trimMp3(ctx, b, ent, meta, key) {
    const G = meta && meta.codec === 'mp3' && meta.gapless;
    if (!G || !ent || ent[G.lead] === undefined) return b;
    // the header length (ms) is the source's and may differ from the encoded sample count by a few ms (resampling), so the
    // three cases are told apart by thresholds far from that error: trimmed (extra ≈ 0), lead + pad, lead + Xing frame + pad
    const sr0 = meta.sr || 22050, lead = ent[G.lead] / sr0, pad = (ent[G.pad] || 0) / sr0, xf = (G.xingFrame || 576) / sr0;
    const extra = b.duration - ent[2] / 1000;
    if (extra < (lead + pad) / 2) { mp3Trim.set(key, { raw: +b.duration.toFixed(4), off: 0 }); return b; }
    const off = extra < lead + pad + xf / 2 ? lead : lead + xf;
    const R = b.sampleRate, s0 = Math.round(off * R), n = Math.max(1, Math.min(b.length - s0, b.length - s0 - Math.round(pad * R)));
    mp3Trim.set(key, { raw: +b.duration.toFixed(4), off: +off.toFixed(4) });
    const t = ctx.createBuffer(b.numberOfChannels, n, R);
    for (let c = 0; c < b.numberOfChannels; c++) { const d = b.getChannelData(c).subarray(s0, s0 + n); if (t.copyToChannel) t.copyToChannel(d, c); else t.getChannelData(c).set(d); }
    return t;
  }
  function store(key, b) {
    const limit = (D() && D().RULES.cacheBytes) || 6 * 1024 * 1024;
    decoded.set(key, b); cacheBytes += b.length * b.numberOfChannels * 4;
    while (cacheBytes > limit && decoded.size > 1) {
      const [k, v] = decoded.entries().next().value;
      decoded.delete(k); cacheBytes -= v.length * v.numberOfChannels * 4;
    }
  }
  // ---------------------------------------------------------------- procedural cries: own LRU ≤ RULES.cryCacheBytes (0.5 MB, §B10.6) --
  // One rendering per (speaker, kind); the variety between repeats is a small per-play detune (CRY_DETUNE), not more buffers.
  // Rendered ahead in idle slices for the participants (warm*, §B11); a cry missing at play time is rendered then (≤ 6 ms warm).
  const cries = new Map();       // 'cry|<speaker>|<kind>' / 'crowd|<n>:<kind>:<variant>' → AudioBuffer
  let cryBytes = 0, cryRenders = 0;
  const CRY_DETUNE = [1, 1.03, 0.97];
  const bufBytes = b => b.length * b.numberOfChannels * 4;
  function cryGet(key) { const b = cries.get(key); if (b) { cries.delete(key); cries.set(key, b); } return b || null; }
  function cryStore(key, b) {
    const limit = (D() && D().RULES.cryCacheBytes) || 512 * 1024;
    if (cries.has(key)) cryBytes -= bufBytes(cries.get(key));
    cries.delete(key); cries.set(key, b); cryBytes += bufBytes(b);
    while (cryBytes > limit && cries.size > 1) {
      const [k, v] = cries.entries().next().value;
      cries.delete(k); cryBytes -= bufBytes(v);
    }
  }
  const cryKeyOf = (prof, kind) => 'cry|' + (prof.name || prof.who || '') + '|' + kind;
  function cryBuffer(prof, kind) {
    const ctx = ctxOf(); const C = SG.VoiceCry;
    if (!ctx || !C) return null;
    const key = cryKeyOf(prof, kind);
    const hit = cryGet(key);
    if (hit) return hit;
    const d = C.render(kind, C.voiceOf(prof), fnv(key));
    cryRenders++;
    try {
      const b = ctx.createBuffer(1, d.length, C.SR);
      if (b.copyToChannel) b.copyToChannel(d, 0); else b.getChannelData(0).set(d);
      cryStore(key, b); return b;
    } catch (e) { return null; }
  }
  const crowdKey = (kind, n, variant) => 'crowd|' + (n | 0) + ':' + kind + ':' + ((variant | 0) % 2);
  function crowdBuffer(d) {
    const ctx = ctxOf(), C = SG.VoiceCry;
    const b = ctx.createBuffer(2, d.L.length, C.SR);
    b.getChannelData(0).set(d.L); b.getChannelData(1).set(d.R);
    return b;
  }

  // ---------------------------------------------------------------- idle-slice warm-up (§B11: cries ahead of the duel / battle) --
  // A queue of small jobs (one cry, or one voice of a crowd) run in requestIdleCallback slices (timeout 400 ms; setTimeout without
  // it), several per slice while the slice has ≥ 6 ms left; nothing here touches the game state or SG.Random.
  const warmQ = [];
  let warmBusy = false, warmJobs = 0;
  function idle(fn) {
    if (typeof W.requestIdleCallback === 'function') W.requestIdleCallback(fn, { timeout: 400 });
    else setTimeout(() => fn(null), 16);
  }
  function pump() {
    if (!warmQ.length) { warmBusy = false; return; }
    warmBusy = true;
    idle(dl => {
      const t0 = now();
      do {
        const job = warmQ.shift();
        try { job(); warmJobs++; } catch (e) { /* 忽略 */ }
      } while (warmQ.length && dl && typeof dl.timeRemaining === 'function' && dl.timeRemaining() > 6 && now() - t0 < 0.012);
      pump();
    });
  }
  function enqueue(jobs) { for (const j of jobs) warmQ.push(j); if (!warmBusy) pump(); }
  function warmCrowd(kind, n, variant) {
    const C = SG.VoiceCry;
    if (!ctxOf() || !C || !C.crowdJob) return;
    const key = crowdKey(kind, n, variant);
    if (cries.has(key)) { cryGet(key); return; }
    let job = null;
    const jobs = [];
    const steps = Math.max(6, Math.min(10, n | 0 || 8)) + 1;
    for (let i = 0; i < steps; i++) jobs.push(() => {
      if (cries.has(key)) return;                    // played (and built) in the meantime
      if (!job) job = C.crowdJob(kind, n || 8, fnv(crowdKey(kind, n, variant).slice(6)));
      if (job.step() || !job.done) return;
      try { cryStore(key, crowdBuffer(job.result())); cryRenders++; } catch (e) { /* 忽略 */ }
    });
    enqueue(jobs);
  }
  function warmCries(who, kinds) {
    if (!who || !ctxOf() || !SG.VoiceCry) return;
    let prof;
    try { prof = profile(who === 'herald' || who === 'merchant' ? who : genOf(who)); } catch (e) { return; }
    enqueue(kinds.map(k => () => { if (!cries.has(cryKeyOf(prof, k))) cryBuffer(prof, k); }));
  }
  const DUEL_CRIES = ['heavy', 'hurt', 'light', 'charge', 'knockout'];
  // the battle: both crowd variants of the clash (one per attacking side) and each side's lead officer's battle cry
  function warmBattle(s) {
    warmCrowd('charge', 8, 0); warmCrowd('charge', 8, 1);
    if (s) for (const g of [s.atk && s.atk[0], s.def && s.def[0]]) if (g) warmCries(g, ['shout']);
  }
  // the duel: the five effort sounds of both fighters (≈ 2 × 245 KB, inside the 0.5 MB cache)
  function warmDuel(a, b) { for (const g of [a, b]) if (g) warmCries(g, DUEL_CRIES); }

  // ---------------------------------------------------------------- scheduler (§B10.3) --
  const active = [];           // { pri, side, scene, src, nodes, end, cue, stop() }
  const lastCue = new Map();   // cue → time
  let lastPri1 = -1e9, lastShell = -1e9;
  const log = [];
  function note(o) { o.t = +now().toFixed(3); log.push(o); if (log.length > 200) log.shift(); }
  function freqAllows(def, cue, opts) {
    const f = settings.frequency;
    const pri = opts.pri || def.pri || 1;
    if (opts.force) return true;
    if (f === 'critical' || f === 'crit') return pri >= 3;
    if (f === 'essential') {
      if (pri >= 2) return true;
      const C = D().CONFIG || {};
      // 精简（仅重要）= priority ≥ 2 (§B10.4); CONFIG.essentialKeepsCommands (false by default) would also keep the player's
      // routine pri-1 command lines (one per command) and still drop the chatter
      return !!(C.essentialKeepsCommands && opts.command && !def.shell);
    }
    return true;
  }
  function maxVoices(scene) { const R = D().RULES.maxVoices; return R[scene] || 1; }
  function admit(def, cue, opts) {
    const t = now();
    const pri = opts.pri || def.pri || 1;
    const scene = opts.scene || def.scene || 'strategy';
    const R = D().RULES;
    for (let i = active.length - 1; i >= 0; i--) if (active[i].end <= t) active.splice(i, 1);
    if (opts.auto && !opts.force && pri < 2) return { action: 'drop', why: 'auto' };
    if (opts.auto && !opts.force && !(opts.player || opts.commander)) return { action: 'drop', why: 'auto-side' };
    // strategy: one voice; battle / duel: two, one per side
    const cap = maxVoices(scene);
    const rivals = scene === 'strategy' ? active.slice() : active.filter(a => a.side === undefined || opts.side === undefined || a.side === opts.side);
    const full = active.length >= cap || rivals.length >= 1;
    const waitAll = () => Math.max(0, ...rivals.map(a => a.end - t));
    if (pri >= 3) {
      if (!full) return { action: 'play', preempt: [] };
      const lower = rivals.filter(a => a.pri < 3);
      if (lower.length === rivals.length && rivals.length) return { action: 'play', preempt: lower };
      if (opts._queued) return { action: 'play', preempt: rivals };          // a pri 3 never waits twice: it always plays
      return { action: 'queue', wait: Math.min(2.0, waitAll()) };
    }
    if (pri === 2) {
      if (!full) return { action: 'play', preempt: [] };
      if (rivals.length && rivals.every(a => a.pri < 2)) return { action: 'play', preempt: rivals };
      const wait = waitAll();
      return !opts._queued && wait <= R.pri2QueueMax ? { action: 'queue', wait } : { action: 'drop', why: 'busy' };
    }
    // pri 1: only when nothing plays, its cooldown passed, all pri 1 ≥ 1.2 s apart; shell cues ≥ 20 s and ≤ 1 per 6 s
    if (active.length) return { action: 'drop', why: 'busy' };
    if (t - lastPri1 < R.pri1Gap) return { action: 'drop', why: 'gap' };
    const cd = def.cd || 0;
    if (cd && t - (lastCue.get(cue) || -1e9) < cd) return { action: 'drop', why: 'cooldown' };
    if (def.shell && t - lastShell < R.shellGap) return { action: 'drop', why: 'shell' };
    return { action: 'play', preempt: [] };
  }

  // ---------------------------------------------------------------- the voice chain (§B7.3) --
  let hallIR = null;
  function graph(ctx, buf, prof, opts, def) {
    const bus = SG.Sfx && SG.Sfx.voiceBus ? SG.Sfx.voiceBus() : null;
    const dest = bus ? bus.input : ctx.destination;
    const src = ctx.createBufferSource(); src.buffer = buf;
    try { src.playbackRate.value = opts.cryRate ? (opts.cryRateK || 1) : prof.rate; } catch (e) { /* 忽略 */ }
    const nodes = [src];
    let last = src;
    const link = n => { last.connect(n); last = n; nodes.push(n); return n; };
    try {
      const sh = ctx.createBiquadFilter(); sh.type = 'lowshelf'; sh.frequency.value = 200; sh.gain.value = prof.body || 0; link(sh);
      const pk = ctx.createBiquadFilter(); pk.type = 'peaking'; pk.frequency.value = prof.presence.f; pk.Q.value = prof.presence.q; pk.gain.value = prof.presence.gain; link(pk);
      const shout = def && def.del === 'shout' && (def.pri || 1) >= 2 ? 0.35 : 0;
      const grit = Math.min(0.6, (prof.grit || 0) + shout);
      if (grit > 0.01) {
        const ws = ctx.createWaveShaper(); const n = 1024, c = new Float32Array(n), k = 1 + grit * 4;
        for (let i = 0; i < n; i++) { const x = i / (n - 1) * 2 - 1; c[i] = Math.tanh(k * x) / Math.tanh(k); }
        ws.curve = c; try { ws.oversample = '2x'; } catch (e) { /* 忽略 */ }
        link(ws);
      }
      const gn = ctx.createGain(); gn.gain.value = (prof.gain || 1) * (opts.gain || 1); link(gn);
      if (prof.tremolo > 0) {
        const lfo = ctx.createOscillator(), lg = ctx.createGain();
        lfo.frequency.value = 5.5; lg.gain.value = prof.tremolo; lfo.connect(lg); lg.connect(gn.gain); lfo.start(); nodes.push(lfo, lg);
      }
      const pan = opts.pan !== undefined ? opts.pan : prof.pan || 0;
      if (ctx.createStereoPanner) { const p = ctx.createStereoPanner(); p.pan.value = clamp(pan, -1, 1); link(p); }
      last.connect(dest);
      // ruler court lines: send 0.18 to the shared 1.2 s hall
      if ((opts.space || (prof.ruler && def && def.court)) && bus && bus.hall) { const s = ctx.createGain(); s.gain.value = D().RULES.hallSend; last.connect(s); s.connect(bus.hall); nodes.push(s); }
    } catch (e) { try { src.connect(dest); } catch (e2) { /* 忽略 */ } }
    return { src, nodes };
  }
  function stopVoice(a, fade) {
    const ctx = ctxOf(); if (!ctx || !a) return;
    try {
      const t = ctx.currentTime, g = a.nodes.find(n => n.gain && n !== a.src && n.gain.value !== undefined && !n.frequency);
      if (g) { g.gain.cancelScheduledValues(t); g.gain.setValueAtTime(g.gain.value, t); g.gain.linearRampToValueAtTime(0, t + fade); }
      a.src.stop(t + fade + 0.01);
    } catch (e) { /* 忽略 */ }
    a.end = now();
    const i = active.indexOf(a); if (i >= 0) active.splice(i, 1);
  }
  function playBuffer(buf, prof, opts, def, meta) {
    const ctx = ctxOf();
    if (!ctx || ctx.state !== 'running' || !buf) return null;
    const { src, nodes } = graph(ctx, buf, prof, opts, def);
    const dur = buf.duration / (opts.cryRate ? (opts.cryRateK || 1) : prof.rate || 1);
    const a = { pri: opts.pri || def.pri || 1, side: opts.side, scene: opts.scene || def.scene || 'strategy', src, nodes, end: now() + dur, cue: meta.cue, start: now() };
    active.push(a);
    src.onended = () => { const i = active.indexOf(a); if (i >= 0) active.splice(i, 1); setTimeout(() => { for (const n of nodes) { try { n.disconnect(); } catch (e) { /* 忽略 */ } } }, 50); };
    try { src.start(); } catch (e) { return null; }
    try { if (SG.Sfx && SG.Sfx.duckForVoice) SG.Sfx.duckForVoice(dur); } catch (e) { /* 忽略 */ }
    return { a, dur };
  }

  // ---------------------------------------------------------------- subtitles (§B10.5) --
  const FONT = {
    cjk: '"PingFang SC","Hiragino Sans","Noto Sans CJK SC","Microsoft YaHei",sans-serif',
    kore: '"Apple SD Gothic Neo","Noto Sans CJK KR","Noto Sans KR","Malgun Gothic",sans-serif',
    deva: '"Kohinoor Devanagari","Noto Sans Devanagari","Nirmala UI",Mangal,sans-serif',
    taml: '"Tamil Sangam MN","Noto Sans Tamil","Nirmala UI",Latha,sans-serif',
    sinh: '"Sinhala Sangam MN","Noto Sans Sinhala","Nirmala UI","Iskoola Pota",sans-serif',
    geor: '"Noto Sans Georgian",Sylfaen,sans-serif',
    ethi: 'Kefa,"Noto Sans Ethiopic",Nyala,Ebrima,sans-serif',
    arab: '"Geeza Pro","Noto Naskh Arabic","Segoe UI",Tahoma,sans-serif',
    grek: 'system-ui,"Segoe UI","Noto Sans",sans-serif', cyrl: 'system-ui,"Segoe UI","Noto Sans",sans-serif', latn: 'system-ui,"Segoe UI","Noto Sans",sans-serif',
  };
  let subEl = null, styled = false;
  function injectStyle() {
    if (styled || typeof document === 'undefined') return;
    styled = true;
    const s = document.createElement('style');
    s.textContent = `
.sgv-subs{position:fixed;left:max(16px,env(safe-area-inset-left));bottom:max(18px,env(safe-area-inset-bottom));z-index:60;display:flex;flex-direction:column;gap:6px;pointer-events:none;max-width:min(440px,calc(100vw - 424px))}
@media (max-width:900px){.sgv-subs{max-width:min(420px,calc(100vw - 32px))}}
@media (max-height:500px){.sgv-subs{bottom:auto;top:calc(60px + env(safe-area-inset-top));left:max(56px,env(safe-area-inset-left));max-width:min(380px,calc(100vw - 112px))}}
.sgv-sub{display:flex;gap:8px;align-items:flex-start;background:var(--paper,#f3ead6);color:var(--ink,#2a2018);border:1.5px solid var(--ink-line,#3b2b1e);border-left:4px solid #b0362a;border-radius:6px;padding:7px 11px 7px 8px;box-shadow:0 3px 10px rgba(20,12,4,.28);opacity:.96;overflow:hidden;animation:sgv-in .16s ease-out both}
.sgv-sub.off{animation:sgv-out .2s ease-in both}
@keyframes sgv-in{from{opacity:0;transform:translateY(6px)}to{opacity:.96;transform:none}}
@keyframes sgv-out{from{opacity:.96}to{opacity:0;transform:translateY(-4px)}}
@media (prefers-reduced-motion:reduce){.sgv-sub,.sgv-sub.off{animation:none}}
.sgv-sub .sgv-face{flex:0 0 auto;width:30px;height:30px;border-radius:50%;overflow:hidden}
.sgv-sub .sgv-face img,.sgv-sub .sgv-face canvas{width:30px;height:30px}
.sgv-sub .sgv-who{font-size:11px;line-height:14px;color:var(--ink-soft,#6b5640);white-space:nowrap}
.sgv-sub .sgv-orig{font-size:15px;line-height:20px;word-break:break-word;overflow-wrap:anywhere}
.sgv-sub .sgv-zh{font-size:12px;line-height:16px;color:var(--ink-soft,#5a4632);overflow-wrap:anywhere}
.sgv-sub[data-side="1"]{border-color:#8a3a2c}
.sgv-sub.sgv-at{position:fixed}`;
    document.head.appendChild(s);
  }
  // ---- where the subtitle stack goes (§B10.5: never over the command grid; here also never over the HUD, the agenda slips or the
  // units' name labels when a free slot exists). Candidate slots in order of preference (desktop: bottom-left first; phone:
  // top-left first); the first that overlaps nothing wins, else the one that overlaps least (HUD weighs 3, unit labels 2).
  const AVOID_HUD = '.sg-battle-card,.sg-battle-actions,.sg-battle-top,.sg-agenda .sg-slip,.sg-citypanel,.sg-endseal,.sg-beam,.sg-topbar,.sg-toast,.sg-dialog,.sg-cmdgrid,.sg-dock';
  const AVOID_LABEL = '.sg-uinfo';
  function isShort() {
    try { return (W.innerHeight || 720) <= 500 || document.documentElement.classList.contains('sg-short'); } catch (e) { return false; }
  }
  function boxesOf(sel, weight, out) {
    for (const el of document.querySelectorAll(sel)) {
      if (el.closest && el.closest('.sgv-subs')) continue;
      const r = el.getBoundingClientRect();
      if (!(r.width > 1 && r.height > 1)) continue;
      out.push({ x0: r.left, y0: r.top, x1: r.right, y1: r.bottom, w: weight });
    }
    return out;
  }
  let subSlot = null;
  function placeSubs(short) {
    if (!subEl || !subEl.isConnected || typeof document === 'undefined') return;
    const vw = W.innerWidth || 1280, vh = W.innerHeight || 720;
    const st = subEl.style;
    // measure at the top-left corner (the full width available: a stack near the right edge would measure shrunk), then place;
    // both happen before the next paint
    st.right = 'auto'; st.bottom = 'auto'; st.left = '0px'; st.top = '0px';
    const r0 = subEl.getBoundingClientRect();
    const w = r0.width, h = r0.height;
    if (!(w > 0 && h > 0)) return;
    const side = short ? 56 : 16, top = short ? 60 : 72, bot = 18;
    const slots = [
      ['bl', side, vh - bot - h], ['tl', side, top], ['tc', (vw - w) / 2, top], ['tr', vw - 16 - w, top],
      ['ml', side, (vh - h) / 2], ['mr', vw - 16 - w, (vh - h) / 2], ['bc', (vw - w) / 2, vh - bot - h], ['br', vw - 16 - w, vh - 84 - h],
    ];
    if (short) { const tl = slots.splice(1, 1)[0]; slots.unshift(tl); }
    const boxes = boxesOf(AVOID_LABEL, 2, boxesOf(AVOID_HUD, 3, []));
    const cost = (x, y) => {
      let c = 0;
      for (const b of boxes) { const ox = Math.min(x + w, b.x1) - Math.max(x, b.x0), oy = Math.min(y + h, b.y1) - Math.max(y, b.y0); if (ox > 0 && oy > 0) c += ox * oy * b.w; }
      return c;
    };
    const cands = slots.map(([id, x0, y0]) => { const x = clamp(x0, 8, Math.max(8, vw - w - 8)), y = clamp(y0, 8, Math.max(8, vh - h - 8)); return { id, x, y, c: cost(x, y) }; });
    // keep the current slot while it is free (no jumping cards), else the first free slot, else the least covering one
    const best = cands.find(c => c.id === subSlot && c.c === 0) || cands.find(c => c.c === 0) || cands.reduce((a, b) => (b.c < a.c ? b : a));
    subSlot = best.id;
    st.left = Math.round(best.x) + 'px'; st.top = Math.round(best.y) + 'px';
    subEl.dataset.slot = best.id;
  }
  function subtitle(prof, line, dur, opts) {
    if (opts.subtitle === false || typeof document === 'undefined') return;
    const mode = settings.subtitles;
    if (mode === 'off') return;
    injectStyle();
    if (!subEl || !subEl.isConnected) { subEl = document.createElement('div'); subEl.className = 'sgv-subs'; (document.getElementById('ui') || document.body).appendChild(subEl); }
    const VD = D();
    const L = VD.LINGUA[line.lingua] || VD.LINGUA.zh;
    const card = document.createElement('div');
    card.className = 'sgv-sub';
    if (opts.side !== undefined) card.dataset.side = opts.side;
    const esc = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    let face = '';
    try { if (prof.name && SG.UI && SG.UI.face) face = SG.UI.face(genOf(prof.name)) || ''; } catch (e) { face = ''; }
    const name = prof.name || (prof.who === 'herald' ? '传令官' : prof.who === 'merchant' ? '商人' : '');
    const orig = line.text, zh = line.zh || (line.lingua === 'zh' ? '' : zhOf(line.cue, line.variant));
    let html = (face ? `<div class="sgv-face">${face}</div>` : '') + '<div class="sgv-body">' + (name ? `<div class="sgv-who">${esc(name)}</div>` : '');
    if (!orig) html += `<div class="sgv-zh">（${esc(zh || zhOf(line.cue, 0))}）</div>`;         // C-language cries: only the Chinese, in parentheses
    else if (mode === 'zh' && line.lingua !== 'zh') html += `<div class="sgv-orig" style="font-family:${FONT.cjk}">${esc(zh || orig)}</div>`;
    else {
      html += `<div class="sgv-orig" lang="${esc(line.lingua)}" style="font-family:${FONT[L.script] || FONT.latn}"${L.script === 'arab' ? ' dir="rtl"' : ''}>${esc(orig)}</div>`;
      if (zh && line.lingua !== 'zh') html += `<div class="sgv-zh">${esc(zh)}</div>`;
    }
    card.innerHTML = html + '</div>';
    if (opts.at && isFinite(opts.at.x) && isFinite(opts.at.y)) {      // battle bubbles above the unit
      card.classList.add('sgv-at');
      card.style.left = Math.round(clamp(opts.at.x - 120, 8, (W.innerWidth || 1280) - 260)) + 'px';
      card.style.top = Math.round(clamp(opts.at.y - 86, 60, (W.innerHeight || 720) - 80)) + 'px';
      (document.getElementById('ui') || document.body).appendChild(card);
    } else {
      subEl.appendChild(card);
      // a phone in battle keeps one card (the board is small); otherwise two
      const short = isShort();
      const keep = short && (opts.scene === 'battle' || opts.scene === 'duel') ? 1 : 2;
      while (subEl.children.length > keep) subEl.removeChild(subEl.firstChild);
      try { placeSubs(short); } catch (e) { /* CSS 的默认位置 */ }
    }
    const life = (dur + D().RULES.bubbleExtra) * 1000;
    setTimeout(() => { card.classList.add('off'); setTimeout(() => card.remove(), 220); }, life);
    return card;
  }

  // ---------------------------------------------------------------- say --
  function say(x, cue, opts) {
    opts = opts || {};
    return new Promise(resolve => {
      try { sayImpl(x, cue, opts, resolve); } catch (e) { if (typeof console !== 'undefined') console.warn('SG.Voice.say', e); resolve(null); }
    });
  }
  function sayImpl(x, cue, opts, resolve) {
    const VD = D();
    if (!VD || !x) { resolve(null); return; }
    init();
    if (settings.language === 'off' || settings.volume <= 0) { note({ cue, action: 'drop', why: 'off' }); resolve(null); return; }
    const def = cue === 'sig' ? { who: 'ruler', pri: 3, del: 'shout', court: 1 } : VD.CUES[cue];
    if (!def) { note({ cue, action: 'drop', why: 'unknown cue' }); resolve(null); return; }
    if (!freqAllows(def, cue, opts)) { note({ cue, action: 'drop', why: 'frequency' }); resolve(null); return; }
    const adm = admit(def, cue, opts);
    const speaker = typeof x === 'object' ? x.name : x;
    if (adm.action === 'drop') { note({ cue, speaker, pri: opts.pri || def.pri, action: 'drop', why: adm.why }); resolve(null); return; }
    if (adm.action === 'queue') {
      note({ cue, speaker, pri: opts.pri || def.pri, action: 'queue', wait: +adm.wait.toFixed(2) });
      setTimeout(() => sayImpl(x, cue, Object.assign({}, opts, { _queued: true }), resolve), Math.max(0, adm.wait * 1000 + 30));
      return;
    }
    for (const a of adm.preempt || []) { note({ cue: a.cue, action: 'preempt', by: cue }); stopVoice(a, VD.RULES.preemptFade); }
    const line = pickLine(x, cue, opts);
    if (!line) { note({ cue, speaker, action: 'drop', why: 'no line' }); resolve(null); return; }
    const prof = line.prof;
    const t = now();
    // the second of two similar voices in one scene sounds a little different (§B4.2): a duel / diplomacy pair (opts.pair)
    // at any distance in time, otherwise the speakers of the last 3 s (adjacent battle units)
    for (let i = recent.length - 1; i >= 0; i--) if (t - recent[i].t > 3) recent.splice(i, 1);
    const others = sceneOthers(prof, line.cue, opts) || recent.filter(r => r.name !== prof.name);
    const rateEff = distinctRate(Object.assign({}, prof, { pack: line.pack }), others);
    if (rateEff !== prof.rate) line.prof = Object.assign({}, prof, { rate: rateEff });
    recent.push({ name: prof.name, pack: line.pack, rateEff, t });
    lastCue.set(cue, t);
    if ((opts.pri || def.pri) < 2) lastPri1 = t;
    if (def.shell) lastShell = t;
    const finish = (buf, isCry) => {
      const r = playBuffer(buf, line.prof, Object.assign({}, opts, isCry ? { cryRate: true, cryRateK: rateEff / (prof.rate || 1) } : {}), def, line);
      if (!r) { note({ cue, speaker, action: 'drop', why: 'no audio', rate: rateEff }); subtitle(prof, line, 1.2, opts); resolve(null); return; }
      note({ cue: line.cue, speaker, pri: opts.pri || def.pri, action: isCry ? 'cry' : 'play', lingua: line.lingua, text: line.text, pack: line.pack, dur: +r.dur.toFixed(2), rate: rateEff });
      subtitle(prof, line, r.dur, opts);
      if (opts.onStart) { try { opts.onStart(r.dur); } catch (e) { /* 忽略 */ } }
      // a line with a trailing '~kind' (Tocharian 'Walo! ~roar', Gothic '~roar'): the cry follows the words
      if (line.cryAfter && line.cry) setTimeout(() => { const b = cryBuffer(prof, line.cry, 0); if (b) playBuffer(b, prof, Object.assign({}, opts, { cryRate: true }), def, { cue: '~' + line.cry }); }, Math.max(0, r.dur * 1000 - 60));
      setTimeout(() => resolve({ dur: r.dur, text: line.text, zh: line.zh, lingua: line.lingua, cue: line.cue, variant: line.variant }), r.dur * 1000);
    };
    // procedural cry lines, or no pack: the category cry right away
    if (!line.clipId || !line.pack) { finish(cryBuffer(prof, line.cry || 'hm', line.variant), true); return; }
    const key = line.pack + '|' + line.clipId;
    if (decoded.has(key)) { finish(decoded.get(key), false); return; }
    // not decoded yet: wait ≤ 120 ms (pack load + decode), else the cry plays and the subtitle shows (§B7.3)
    let settled = false;
    const timer = setTimeout(() => { if (settled) return; settled = true; note({ cue, action: 'late', pack: line.pack }); finish(cryBuffer(prof, line.cry || catCry(def), line.variant), true); }, VD.RULES.decodeWaitMs);
    loadPack(line.pack).then(ok => { if (!ok) return null; fixClip(line); return line.clipId ? decodeClip(line.pack, line.clipId) : null; }).then(buf => {
      if (settled) return;
      settled = true; clearTimeout(timer);
      if (buf) finish(buf, false); else finish(cryBuffer(prof, line.cry || catCry(def), line.variant), true);
    });
  }
  // a clip the QA gate dropped (tools/voice-build/qa.py): another variant of the cue, else the cue's fallback chain in the same pack
  function fixClip(line) {
    if (!line.clipId || clipEntry(line.pack, line.clipId)) return;
    const VD = D(), P = packs[line.pack];
    let c = line.cue, guard = 0;
    while (c && c[0] !== '~' && guard++ < 8) {
      const arr = P && P.meta.clips[c];
      if (arr) { const i = arr.findIndex(e => e); if (i >= 0) { line.cue = c; line.variant = i; line.clipId = c + '#' + i; line.text = String((VD.LINES[line.lingua][c] || [])[i] || line.text).replace(/~[a-z.]+/g, '').trim(); if (line.lingua !== 'zh') line.zh = zhOf(c, i); return; } }
      c = VD.CUES[c] && VD.CUES[c].fb;
    }
    line.clipId = null;
    if (c && c[0] === '~') line.cry = c.slice(1);
  }
  function catCry(def) { return def.del === 'shout' ? 'shout' : def.del === 'sigh' ? 'sigh' : 'hm'; }
  function cry(x, kind, opts) {
    opts = opts || {};
    try {
      init();
      if (settings.language === 'off' || settings.volume <= 0) return null;
      const prof = profile(x === 'herald' || x === 'merchant' ? x : genOf(x));
      const n = (varCount.get('cry|' + prof.name + '|' + kind) || 0) + 1; varCount.set('cry|' + prof.name + '|' + kind, n);
      // §B4.2: the second of a duel pair whose rendered voice (sex, pitchK) is within 0.05 of the first's moves ×1.07 / ×0.93
      let k = 1;
      const so = sceneOthers(prof, null, opts);
      if (so && so.length) k = distinctRate({ rate: prof.pitchK, pitchK: prof.pitchK, pack: cryVoiceKey(prof) }, so) / prof.pitchK;
      const uk = +k.toFixed(4);
      k = +(k * CRY_DETUNE[n % 3]).toFixed(4);
      const ctx = ctxOf(); if (!ctx || ctx.state !== 'running') { note({ cue: '~' + kind, speaker: prof.name, action: 'drop', why: 'no audio', rate: k, uk }); return null; }
      const b = cryBuffer(prof, kind);
      if (!b) return null;
      const { src, nodes } = graph(ctx, b, prof, Object.assign({ cryRate: true, cryRateK: k }, opts), { del: kind === 'heavy' || kind === 'shout' || kind === 'roar' ? 'shout' : 'command', pri: 1 });
      src.onended = () => setTimeout(() => { for (const nd of nodes) { try { nd.disconnect(); } catch (e) { /* 忽略 */ } } }, 50);
      src.start();
      note({ cue: '~' + kind, speaker: prof.name, action: 'cry', rate: k, uk });
      return b.duration / k;
    } catch (e) { return null; }
  }
  let lastCrowd = -1e9;
  function crowd(kind, n, opts) {
    opts = opts || {};
    try {
      init();
      if (settings.language === 'off' || settings.volume <= 0) return null;
      const ctx = ctxOf(); const C = SG.VoiceCry;
      if (!ctx || ctx.state !== 'running' || !C) return null;
      if (now() - lastCrowd < 0.8) return null;              // at most 1 crowd layer
      lastCrowd = now();
      const key = crowdKey(kind, n, opts.variant);
      let b = cryGet(key);
      if (!b) { b = crowdBuffer(C.crowd(kind, n || 8, fnv(key.slice(6)))); cryStore(key, b); cryRenders++; }
      const bus = SG.Sfx && SG.Sfx.voiceBus ? SG.Sfx.voiceBus() : null;
      const src = ctx.createBufferSource(); src.buffer = b;
      const g = ctx.createGain(); g.gain.value = (opts.gain || 0.55);
      src.connect(g); g.connect(bus ? bus.input : ctx.destination);
      src.onended = () => { try { src.disconnect(); g.disconnect(); } catch (e) { /* 忽略 */ } };
      src.start();
      note({ cue: 'crowd.' + kind, action: 'crowd' });
      return b.duration;
    } catch (e) { return null; }
  }

  // ---------------------------------------------------------------- init: manifest, codec probe, cue registry, hooks --
  let inited = false, probed = false;
  function init() {
    if (inited) return;
    inited = true;
    try {
      const VD = D();
      if (VD && SG.Reg && SG.Reg.voiceCues) for (const [id, c] of Object.entries(VD.CUES)) if (!SG.Reg.voiceCues.has(id)) SG.Reg.voiceCues.def(id, c);
      if (!SG.VoiceManifest && typeof document !== 'undefined') loadScript(base + 'voice/manifest.js').then(() => probe());
      else probe();
      setTimeout(() => { try { Hooks.install(); } catch (e) { if (typeof console !== 'undefined') console.warn('SG.Voice hooks', e); } }, 0);
    } catch (e) { /* 忽略 */ }
  }
  // decode the embedded 0.12 s Opus clip; on failure use the MP3 packs (§B7.1)
  function probe() {
    if (probed) return;
    const M = SG.VoiceManifest, ctx = ctxOf();
    if (!M || !M.probe) return;
    if (!ctx) { if (typeof document !== 'undefined' && (probe.tries = (probe.tries || 0) + 1) < 400) setTimeout(probe, 1500); return; }   // browser: wait for the first gesture (≤ 10 min)
    probed = true;
    try {
      const u = b64ToBytes(M.probe.opus);
      const ok = b => { Voice.codec = 'opus'; warmCore(); };
      const bad = () => { Voice._forceMp3(true); warmCore(); };
      const r = ctx.decodeAudioData(u.buffer.slice(0), b => (b && b.duration > 0.05 ? ok(b) : bad()), bad);
      if (r && r.then) r.catch(() => {});
    } catch (e) { useMp3 = true; Voice.codec = 'mp3'; }
  }
  // on idle after load the 20 core cues of the player's lingua are pre-decoded
  function warmCore() {
    try {
      const g = G();
      const ruler = g && g.player >= 0 ? g.ruler(g.player) : null;
      const lg = usableLingua(ruler ? linguaOf(ruler) : 'zh');
      if (!lg) return;
      const prof = ruler ? profile(ruler) : { voice: 'm' };
      const pid = packOf(lg, prof.voice) || packOf(lg, 'm') || packOf(lg, 'f');
      loadPack(pid).then(ok => {
        if (!ok) return;
        const ids = [];
        for (const c of D().CORE) { const arr = packs[pid] && packs[pid].meta.clips[c]; if (arr) arr.forEach((e, i) => { if (e) ids.push(c + '#' + i); }); }
        let i = 0;
        const step = () => { if (i >= ids.length) return; decodeClip(pid, ids[i++]).then(() => setTimeout(step, 30)); };
        step();
      });
    } catch (e) { /* 忽略 */ }
  }

  // ---------------------------------------------------------------- hooks: voices on the existing commands and battles --
  // These wrap PUBLIC methods (never change arguments, return values or timing; every voice call is fire-and-forget).
  // Each owner can replace a layer by calling SG.Voice.say at the exact beat and then SG.Voice.hooks.off('<name>').
  const Hooks = {
    on: { commands: true, month: true, succession: true, battle: true, clash: true, duel: true, ui: true, ending: true },
    installed: {},
    off(name) { Hooks.on[name] = false; },
    install() {
      const E = SG.Events;
      if (E && !Hooks.installed.events) {
        Hooks.installed.events = true;
        E.on('command', (cmd, res) => { if (Hooks.on.commands) onCommand(cmd, res); });
        E.on('month.phase', (name) => { if (Hooks.on.month) onMonthPhase(name); });
        E.on('faction.succession', (fid, heir) => { if (Hooks.on.succession) onSuccession(fid, heir); });
        E.on('captive.taken', (s, gen) => { if (Hooks.on.battle) onCaptive(s, gen); });
        E.on('conquest.prepare', (s) => { try { onPrepare(s); } catch (e) { /* 忽略 */ } });
        E.on('conquest.apply', (s, r) => { if (Hooks.on.battle) onConquest(s, r); });
      }
      wrapBattle(); wrapClash(); wrapDuel(); wrapEnding();
      if (!Hooks.installed.retry && typeof document !== 'undefined') { Hooks.installed.retry = true; setTimeout(() => { wrapBattle(); wrapClash(); wrapDuel(); wrapEnding(); }, 1500); }
    },
  };
  function playerF() { const g = G(); return g ? g.player : -1; }
  function byId(id) { const g = G(); return g && id !== null && id !== undefined && id >= 0 ? g.generals[id] : null; }
  function rulerOfF(f) { const g = G(); try { return g && f >= 0 ? g.ruler(f) : null; } catch (e) { return null; } }
  const CMD_CUE = { recruit: 'cmd.recruit', train: 'cmd.train', move: 'cmd.move', transport: 'cmd.transport', trade: 'cmd.trade' };
  // ---- §B9 signature lines: ruler selection (audio.js previewRuler), a won decisive battle, unification, the first diplomatic meeting ----
  const hasSig = x => { const VD = D(); const n = nameOf(x); return !!(VD && n && VD.SIGNATURE[n]); };
  const SIG_GAP = 0.25;            // seconds between the line before and the signature
  function sayAfter(p, x, cue, opts) { return Promise.resolve(p).then(() => new Promise(r => setTimeout(() => r(say(x, cue, opts)), SIG_GAP * 1000))); }
  const metRulers = new WeakMap();  // game → Set of rulers the player has met in diplomacy (this session)
  function firstMeeting(r) {
    const g = G(); if (!g || !r) return false;
    let set = metRulers.get(g); if (!set) { set = new Set(); metRulers.set(g, set); }
    if (set.has(r.name)) return false;
    set.add(r.name); return true;
  }
  function onCommand(cmd, res) {
    if (!res || res.f !== playerF() || res.f < 0) return;
    const g = G();
    const gen = byId(res.gen) || (g && g.ruler(res.f));
    const tier = res.tier && typeof res.tier === 'object' ? res.tier.tier : res.tier;
    const great = tier === 'great', miss = tier === 'miss' || res.ok === false;
    const o = { command: true, scene: 'strategy', player: true };
    switch (cmd) {
      case 'develop': {
        const base = res.kind === 'industry' ? 'cmd.industry' : res.kind === 'town' ? 'cmd.town' : 'cmd.develop';
        if (res.ok === false) return;
        say(gen, great ? base + '.great' : tier === 'miss' ? 'miss' : base + '.start', o); return;
      }
      case 'search':
        if (res.outcome === 'talent') { const t = byId(res.found); say(t || gen, t ? 'cmd.search.talent' : 'great', o); return; }
        say(gen, res.outcome === 'none' ? 'cmd.search.none' : great ? 'great' : 'cmd.search.start', o); return;
      case 'hire': { const t = byId(res.target); say(t || gen, res.ok ? 'cmd.hire.yes' : 'cmd.hire.no', o); return; }
      case 'ally': {
        const tr = rulerOfF(res.target !== undefined ? res.target : res.to);
        const who = tr || gen;
        const me = g && g.ruler(res.f);
        const op = Object.assign({}, o, { pair: me && tr ? [me, tr] : undefined });
        try { if (tr && SG.Sfx && SG.Sfx.fanfare) SG.Sfx.fanfare(tr.name, { duck: 0.35 }); } catch (e) { /* 忽略 */ }
        // the first time the player meets a hand-suite ruler in diplomacy he introduces himself with his signature line (§B9)
        const first = tr && hasSig(tr) && firstMeeting(tr);
        setTimeout(() => {
          const intro = first ? say(tr, 'sig', Object.assign({}, op, { pri: 3 })) : null;
          if (intro) sayAfter(intro, who, res.ok ? 'cmd.ally.yes' : 'cmd.ally.no', op); else say(who, res.ok ? 'cmd.ally.yes' : 'cmd.ally.no', op);
        }, 900);
        return;
      }
      case 'discord': if (res.ok) say(gen, 'cmd.discord.ok', o); else say(gen, 'miss', o); return;
      case 'persuade': { const t = byId(res.target); if (res.ok) say(t || gen, 'cmd.persuade.yes', o); else say(gen, 'miss', o); return; }
      case 'reward': if (res.ok) say(gen, 'cmd.reward.thanks', o); return;
      case 'appoint': if (res.ok) say(gen, 'cmd.appoint.yes', o); return;
      case 'attack': say(rulerOfF(res.f) || gen, 'cmd.attack.start', o); return;
      default: {
        const c = CMD_CUE[cmd];
        if (!c || res.ok === false) { if (miss && gen) say(gen, 'miss', o); return; }
        say(gen, great ? c + '.great' : tier === 'miss' ? 'miss' : c + '.start', o);
      }
    }
  }
  let monthSaid = -1;
  function onMonthPhase() {
    const g = G(); if (!g || g.player < 0) return;
    if (monthSaid === g.monthIndex) return;
    monthSaid = g.monthIndex;
    say(g.ruler(g.player), 'cmd.endMonth', { scene: 'strategy', command: true, player: true });
  }
  function onSuccession(fid, heir) {
    const g = G();
    if (!g || !heir) return;
    if (fid === g.player) {
      // the map switches to the heir's suite by itself (audio.js follows the player's current ruler); his fanfare and line now
      try { if (SG.Sfx && SG.Sfx.suite && SG.Sfx.setSuite) SG.Sfx.setSuite(null); } catch (e) { /* 忽略 */ }
      try { if (SG.Sfx && SG.Sfx.fanfare) SG.Sfx.fanfare(heir.name, { duck: 0.35 }); } catch (e) { /* 忽略 */ }
      setTimeout(() => say(heir, 'news.succession', { scene: 'strategy', player: true }), 1200);
    }
  }
  // conquest results (also for 委任 / auto-resolved battles, which have no battle screen): the city taken, a captive
  function onCaptive(s, gen) {
    const p = playerF();
    if (!s || !gen || p < 0 || (s.attacker !== p && s.defender !== p)) return;
    say(gen, 'bat.captured', { scene: 'strategy', pri: 3, player: true });
  }
  // who ruled each side when the army set out (succession may already have run when conquest.apply fires)
  const prepRulers = new WeakMap();
  function onPrepare(s) {
    const g = G(); if (!g || !s) return;
    const rid = f => (f >= 0 && g.factions[f] ? g.factions[f].ruler : -1);
    prepRulers.set(s, { a: rid(s.attacker), d: rid(s.defender) });
  }
  // §B9 "a won decisive battle": the player won, and the foe's faction fell, or the foe's ruler fought in it (led the attack /
  // defended the city)
  function decisiveWin(s, r, p) {
    const g = G(); if (!g) return false;
    const won = (r.won && r.attacker === p) || (!r.won && r.defender === p && r.attacker >= 0);
    if (!won) return false;
    const foe = r.attacker === p ? r.defender : r.attacker;
    if (foe < 0) return false;
    if (g.cities && g.cities.every(c => c.owner === p)) return false;    // the last city: unification says it (wrapEnding)
    if (g.factions[foe] && g.factions[foe].alive === false) return true;
    const pr = prepRulers.get(s);
    const rid = pr ? (r.attacker === p ? pr.d : pr.a) : (g.factions[foe] ? g.factions[foe].ruler : -1);
    const list = r.attacker === p ? s.def : s.atk;
    return rid >= 0 && !!(list && list.some(x => x && x.id === rid));
  }
  const sigSaid = new WeakSet();
  function sigAfterWin(s, before) {
    const g = G(), p = playerF();
    if (!g || p < 0 || sigSaid.has(s)) return;
    sigSaid.add(s);
    const ru = g.ruler(p);
    if (ru && hasSig(ru)) sayAfter(before, ru, 'sig', { scene: 'strategy', pri: 3, player: true });
  }
  function onConquest(s, r) {
    const p = playerF();
    if (!s || !r || p < 0 || (r.attacker !== p && r.defender !== p)) return;
    const decisive = decisiveWin(s, r, p);
    if (battle.resultSaid) {
      battle.resultSaid = false;
      if (!(r.won && r.attacker === p)) { if (decisive) sigAfterWin(s, battle.resultP); return; }
    }
    const lead = (s.atk && s.atk[0]) || rulerOfF(r.attacker);
    if (r.won && r.attacker === p) { const q = lead ? say(lead, 'bat.cityTaken', { scene: 'strategy', pri: 3, player: true }) : null; if (decisive) sigAfterWin(s, q); return; }
    const mine = r.attacker === p ? lead : rulerOfF(p);
    const q = mine ? say(mine, r.attacker === p ? 'defeat' : 'victory', { scene: 'strategy', pri: 3, player: true }) : null;
    if (decisive) sigAfterWin(s, q);
  }
  // ---- unification (strategy-screen gameOver(true)): the unifier's ending suite (§A3) and his signature line (§B9) ----
  function wrapEnding() {
    const SS = SG.StrategyScreen;
    if (!SS || !SS.prototype || Hooks.installed.ending) return;
    Hooks.installed.ending = true;
    wrap(SS.prototype, 'gameOver', function (a) {
      if (!Hooks.on.ending || !a[0]) return;
      const g = G(); const ru = g && g.player >= 0 ? g.ruler(g.player) : null;
      try { if (SG.Sfx && SG.Sfx.music) SG.Sfx.music('ending'); } catch (e) { /* 忽略 */ }
      if (ru) setTimeout(() => say(ru, 'sig', { scene: 'strategy', pri: 3, player: true, force: true }), 2600);
    });
  }
  // ---- battle (BattleController / BattleView) ----
    function autoOf(bc) { return !!(bc && (bc.autoPlayer || bc.playerSide < 0 || (bc.M && bc.M.side !== bc.playerSide) || (typeof bc.watching === 'function' && bc.watching()))); }
  function unitOpts(bc, u, extra) {
    const watching = autoOf(bc);
    const player = bc && u && u.side === bc.playerSide;
    let at = null;
    try { if (bc && bc.V && bc.V.screenPos) at = bc.V.screenPos(u); } catch (e) { at = null; }
    const panX = at && W.innerWidth ? clamp((at.x / W.innerWidth) * 1.2 - 0.6, -0.6, 0.6) : (u && u.side === 0 ? -0.3 : 0.3);
    return Object.assign({ scene: 'battle', side: u ? u.side : undefined, auto: watching || !!(bc && bc.autoPlayer), player, commander: !!(u && u.commander), pan: panX, at: at || undefined }, extra || {});
  }
  function wrap(obj, name, fn) {
    if (!obj || typeof obj[name] !== 'function' || obj[name].__sgVoice) return false;
    const orig = obj[name];
    const w = function () { try { fn.call(this, arguments); } catch (e) { /* 人声出错不影响游戏 */ } return orig.apply(this, arguments); };
    w.__sgVoice = true; w.__orig = orig;
    obj[name] = w;
    return true;
  }
  function wrapBoth(obj, name, before, after) {
    if (!obj || typeof obj[name] !== 'function' || obj[name].__sgVoice) return false;
    const orig = obj[name];
    const w = function () {
      try { before.call(this, arguments); } catch (e) { /* 忽略 */ }
      const r = orig.apply(this, arguments);
      const args = arguments, self = this;
      if (r && typeof r.then === 'function') r.then(v => { try { after.call(self, args, v); } catch (e) { /* 忽略 */ } }, () => {});
      else { try { after.call(self, args, r); } catch (e) { /* 忽略 */ } }
      return r;
    };
    w.__sgVoice = true; w.__orig = orig;
    obj[name] = w;
    return true;
  }
  function wrapAfter(obj, name, fn) {
    if (!obj || typeof obj[name] !== 'function' || obj[name].__sgVoice) return false;
    const orig = obj[name];
    const w = function () {
      const r = orig.apply(this, arguments);
      const args = arguments, self = this;
      if (r && typeof r.then === 'function') r.then(v => { try { fn.call(self, args, v); } catch (e) { /* 忽略 */ } }, () => {});
      else { try { fn.call(self, args, r); } catch (e) { /* 忽略 */ } }
      return r;
    };
    w.__sgVoice = true; w.__orig = orig;
    obj[name] = w;
    return true;
  }
  const battle = { enemyPhases: 0, bc: null, lastSel: null, tacRes: new WeakMap() };   // tacRes: model → its last useTactic result (no property on the model)
  function wrapBattle() {
    const BC = SG.BattleController;
    if (!BC || !BC.prototype || Hooks.installed.battle) return;
    Hooks.installed.battle = true;
    const P = BC.prototype;
    wrapBoth(P, 'main', function (a) {
      if (!Hooks.on.battle) return;
      const s = a[0]; battle.bc = this; battle.enemyPhases = 0; battle.lastSel = null;
      const g = G();
      try {
        const ra = g && g.ruler(s.attacker), rd = s.defender >= 0 && g ? g.ruler(s.defender) : null;
        if (SG.MusicSuites) { if (ra) SG.MusicSuites.ensure(ra.name); if (rd) SG.MusicSuites.ensure(rd.name); }
        preload([ra, rd].filter(Boolean).map(r => linguaOf(r)));
        warmBattle(s);                // §B11: the clash crowd and the lead officers' cries in idle slices before the first clash
      } catch (e) { /* 忽略 */ }
    }, function (a) {
      // result: the player's commander says victory / defeat (pri 3)
      if (!Hooks.on.battle) return;
      const s = a[0];
      if (this.playerSide < 0 || !s || !this.M) return;
      const cu = typeof this.M.commander === 'function' ? this.M.commander(this.playerSide) : null;
      const gen = (cu && cu.gen) || (G() && G().ruler(this.playerSide === 0 ? s.attacker : s.defender));
      const won = (s.attackerWon === true) === (this.playerSide === 0);
      battle.resultSaid = true;
      // a won attack ends in 城池已破 (conquest.apply), so the victory line is left to it
      battle.resultP = gen && !(won && this.playerSide === 0) ? say(gen, won ? 'victory' : 'defeat', { scene: 'strategy', pri: 3, player: true }) : null;
      if (battle.bc === this) battle.bc = null;
    });
    wrap(P, 'aiPhase', function () {
      if (!Hooks.on.battle) return;
      if (this.playerSide < 0) return;
      const g = G(); const s = this.M && this.M.setup;
      if (!g || !s) return;
      // enemy phase start → the enemy ruler's fanfare, at most once every 3 enemy phases (§A3)
      if (battle.enemyPhases++ % (D().RULES.fanfareGap || 3) === 0) {
        const foe = this.playerSide === 0 ? s.defender : s.attacker;
        const r = foe >= 0 ? g.ruler(foe) : null;
        try { if (r && SG.Sfx && SG.Sfx.fanfare) SG.Sfx.fanfare(r.name, { duck: 0.35 }); } catch (e) { /* 忽略 */ }
      }
    });
    wrap(P, 'showCard', function (a) {
      if (!Hooks.on.battle) return;
      const u = a[0];
      if (!u || !u.gen || this.watching && this.watching() || u.side !== this.playerSide || !this.awaitingInput) return;
      if (battle.lastSel === u) return;
      battle.lastSel = u;
      say(u.gen, 'bat.select', unitOpts(this, u));
    });
    wrap(P, 'doAttack', function (a) {
      if (!Hooks.on.battle) return;
      const u = a[0];
      if (u && u.gen && !(SG.Clash && SG.Clash.enabled)) say(u.gen, 'bat.attack', unitOpts(this, u));
    });
    // the model's tactic result (success false = 识破) is kept on the model so the doTactic wrapper can say bat.tacticFail
    const BM = SG.BattleModel;
    if (BM && BM.prototype) wrapAfter(BM.prototype, 'useTactic', function (a, r) { battle.tacRes.set(this, r || null); });
    wrapBoth(P, 'doTactic', function (a) {
      if (this.M) battle.tacRes.delete(this.M);
      if (!Hooks.on.battle) return;
      const u = a[0], tac = a[1];
      if (!u || !u.gen) return;
      const fire = tac && (tac.kind === (SG.TacticKind && SG.TacticKind.Fire) || /火/.test(tac.name || ''));
      say(u.gen, fire ? 'bat.fire' : 'bat.tactic', unitOpts(this, u));
    }, function (a, v) {
      if (!Hooks.on.battle) return;
      const u = a[0];
      const r = v && typeof v === 'object' && 'success' in v ? v : (this.M && battle.tacRes.get(this.M));
      if (u && u.gen && r && r.success === false) setTimeout(() => say(u.gen, 'bat.tacticFail', unitOpts(this, u)), 300);
    });
    wrap(P, 'specialCutIn', function (a) {
      if (!Hooks.on.battle) return;
      const u = a[0];
      if (u && u.gen) say(u.gen, 'special', unitOpts(this, u, { pri: 3 }));
    });
    // (wrapBoth: after the duel — DuelGame.perform calls its own play(), not the wrapped SG.DuelGame.play — the battle's crowd is
    // re-warmed in case the duel's effort sounds pushed it out of the 0.5 MB cry cache)
    wrapBoth(P, 'doDuel', function (a) {
      if (!Hooks.on.battle) return;
      const x = a[0], y = a[1];
      if (!x || !x.gen) return;
      const pair = y && y.gen ? [x.gen, y.gen] : undefined;
      if (pair && SG.DuelGame && SG.DuelGame.enabled !== false) warmDuel(x.gen, y.gen);
      say(x.gen, 'duel', unitOpts(this, x, { pri: 3, pair }));
      try {
        const acc = this.M && typeof this.M.duelAccepts === 'function' ? this.M.duelAccepts(x, y) : true;
        setTimeout(() => say(y.gen, acc ? 'duel.accept' : 'duel.refuse', unitOpts(this, y, { pri: acc ? 3 : 2, pair })), 1500);
      } catch (e) { /* 忽略 */ }
    }, function () { if (Hooks.on.battle && battle.bc) warmBattle(null); });
    const BV = SG.BattleView;
    if (BV && BV.prototype) {
      wrap(BV.prototype, 'moveUnit', function (a) {
        if (!Hooks.on.battle) return;
        const u = a[0], bc = battle.bc;
        if (!u || !u.gen || !bc || u.side !== bc.playerSide || (bc.watching && bc.watching())) return;
        say(u.gen, 'bat.move', unitOpts(bc, u));
      });
      wrap(BV.prototype, 'rout', function (a) {
        if (!Hooks.on.battle) return;
        const u = a[0], bc = battle.bc;
        if (!u || !u.gen || !bc) return;
        say(u.gen, 'bat.rout', unitOpts(bc, u));
      });
    }
  }
  // ---- clash: the attacker's charge and the crowd at contact (audio.js stinger reads SG.Voice.clashCtx) ----
  function wrapClash() {
    const C = SG.Clash;
    if (!C || Hooks.installed.clash || typeof C.play !== 'function') return;
    Hooks.installed.clash = true;
    const orig = C.play;
    C.play = function (opts) {
      Voice.clashCtx = opts || null;
      let r;
      try { r = orig.apply(this, arguments); } catch (e) { Voice.clashCtx = null; throw e; }
      if (r && r.then) r.then(() => { if (Voice.clashCtx === opts) Voice.clashCtx = null; }, () => { if (Voice.clashCtx === opts) Voice.clashCtx = null; });
      else Voice.clashCtx = null;
      return r;
    };
    C.play.__sgVoice = true;
  }
  // called by audio.js at clash contact (the stinger beat)
  function onClashContact(ctx) {
    if (!Hooks.on.clash || !ctx || !ctx.attacker) return;
    const bc = battle.bc;
    // the same rule as unitOpts: 委任, a watched battle and the enemy's phase are automatic (§B10.3: only pri ≥ 2, only the
    // player's side or the enemy commander)
    const a = ctx.attacker, auto = autoOf(bc);
    const side = a.side;
    const player = bc ? side === bc.playerSide : ctx.playerSide === side;
    let commander = false;
    try { const u = bc && bc.M && bc.M.units.find(x => x.gen === a.gen); commander = !!(u && u.commander); } catch (e) { commander = false; }
    crowd('charge', 8, { variant: side });
    if (a.gen) say(a.gen, ctx.crit ? 'clash.crit' : 'charge', { scene: 'battle', side, auto, player, commander, pan: side === 0 ? -0.35 : 0.35 });
  }
  // ---- duel (the displayed DuelGame screen's Sim events) ----
  function wrapDuel() {
    const DG = SG.DuelGame;
    const Sim = DG && DG._Sim;
    if (!Sim || !Sim.prototype || Hooks.installed.duel) return;
    Hooks.installed.duel = true;
    // DuelGame.play (battle duels and the tournament): warm both fighters' cries; afterwards re-warm the battle's crowd
    // (the 0.5 MB cry cache may have dropped it for the duel's effort sounds)
    if (typeof DG.play === 'function' && !DG.play.__sgVoice) {
      const orig = DG.play;
      const w = function (opts) {
        try { if (Hooks.on.duel && opts) warmDuel(opts.a && opts.a.gen, opts.b && opts.b.gen); } catch (e) { /* 忽略 */ }
        const r = orig.apply(this, arguments);
        const again = () => { try { if (battle.bc) warmBattle(null); } catch (e) { /* 忽略 */ } };
        if (r && typeof r.then === 'function') r.then(again, again);
        return r;
      };
      w.__sgVoice = true; w.__orig = orig;
      DG.play = w;
    }
    wrapAfter(Sim.prototype, 'emit', function (a) {
      if (!Hooks.on.duel) return;
      const act = DG.active;
      if (!act || act.sim !== this) return;     // only the duel on screen, never the off-screen simulations
      onDuelEvent(this, a[0]);
    });
  }
  function onDuelEvent(sim, e) {
    if (!e || !sim.f) return;
    const fg = i => sim.f[i] && sim.f[i].gen;
    const pair = [fg(0), fg(1)];        // §B4.2: the defender's voice moves away from the challenger's if they are too close
    const o = i => ({ scene: 'duel', side: i, pan: i === 0 ? -0.35 : 0.35, player: true, commander: true, pair });
    switch (e.type) {
      case 'swing': if (e.move === 'heavy' || e.kind === 'H') cry(fg(e.who), 'heavy', o(e.who)); else if (Math.random() < 0.35) cry(fg(e.who), 'light', o(e.who)); break;
      case 'chargeFull': cry(fg(e.who), 'charge', o(e.who)); break;
      case 'hit':
        if (e.crit) say(fg(e.att), 'duel.crit', o(e.att));
        if (!e.guarded) cry(fg(e.def), 'hurt', o(e.def));
        break;
      case 'parry': say(fg(e.def), 'duel.parry', o(e.def)); break;
      case 'guardcrush': say(fg(e.att), 'duel.guardcrush', o(e.att)); break;
      case 'finisher': say(fg(e.who), 'duel.finisher', o(e.who)); break;
      case 'ko':
        cry(fg(e.who), 'knockout', o(e.who));
        setTimeout(() => say(fg(e.winner), 'duel.win', Object.assign(o(e.winner), { pri: 3 })), 700);
        if (e.who !== e.winner && fg(e.who)) setTimeout(() => say(fg(e.who), 'duel.lose', Object.assign(o(e.who), { pri: 2 })), 2300);
        break;
      case 'special': say(fg(e.who), 'special', o(e.who)); break;
      default: break;
    }
  }

  // ---------------------------------------------------------------- 朗读（辅助，§B6.2）: Web Speech, local zh voices only, off by default --
  function readAloud(text) {
    try {
      if (!settings.readAloud || !text || typeof W.speechSynthesis === 'undefined') return false;
      const synth = W.speechSynthesis;
      const vs = (synth.getVoices() || []).filter(v => v.localService && /^zh/i.test(v.lang));
      if (!vs.length) return false;
      const u = new W.SpeechSynthesisUtterance(String(text).replace(/<[^>]+>/g, ''));
      u.voice = vs[0]; u.lang = vs[0].lang; u.rate = 1;
      try { if (SG.Sfx && SG.Sfx.duckForVoice) SG.Sfx.duckForVoice(Math.min(20, String(text).length / 4.5)); } catch (e) { /* 忽略 */ }
      synth.cancel(); synth.speak(u);
      return true;
    } catch (e) { return false; }
  }

  // ---------------------------------------------------------------- export --
  const Voice = {
    init, say, cry, crowd, readAloud, profile, linguaOf, linguaRaw, usableLingua, pickLine, preload, settings, log, hooks: Hooks, distinctRate,
    loadPack, decodeClip, onClashContact, codec: null, clashCtx: null, warmBattle, warmDuel, warmCrowd, warmCries,
    get busy() { const t = now(); return active.some(a => a.end > t); },
    get active() { const t = now(); return active.filter(a => a.end > t).map(a => ({ cue: a.cue, pri: a.pri, side: a.side, scene: a.scene, left: +(a.end - t).toFixed(2) })); },
    _mp3Trim: mp3Trim, _trimMp3: trimMp3,
    _stats() { return { decoded: decoded.size, cacheBytes, packs: Object.keys(packs), useMp3, cries: cries.size, cryBytes, cryRenders, warmJobs, warmQueued: warmQ.length, cryKeys: Array.from(cries.keys()) }; },
    _forceMp3(on) { useMp3 = !!on; Voice.codec = on ? 'mp3' : 'opus'; for (const k of Object.keys(packs)) if ((packs[k].meta.codec === 'mp3') !== useMp3) delete packs[k]; for (const k of Object.keys(loading)) delete loading[k]; decoded.clear(); decoding.clear(); cacheBytes = 0; },
    _reset() { active.splice(0); lastCue.clear(); lastPri1 = -1e9; lastShell = -1e9; log.splice(0); recent.splice(0); },
    _clearCries() { cries.clear(); cryBytes = 0; cryRenders = 0; warmJobs = 0; warmQ.splice(0); },
    _fnv: fnv,
    _subtitle(x, line, dur, opts) { const prof = x === 'herald' || x === 'merchant' ? profile(x) : profile(genOf(x)); return subtitle(prof, line, dur || 1, opts || {}); },
  };
  SG.Voice = Voice;
})();
