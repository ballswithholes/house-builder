# tools/voice-build/qa.py — step 4 of the offline voice pipeline (AUDIO-V4 §B7.2, §3.3)
# Gates for every clip of synth.json:
#   (i)   length sanity: 0.3 s ≤ length ≤ max(0.6 s, 2.5 × expected), expected = 0.25 s + characters ÷ the lingua's rate
#         (catches hallucinating models such as el_GR-rapunzelina on Greek text)                         → hard fail
#   (ii)  ASR round trip with Whisper (sherpa-onnx whisper-base): zh on toneless pinyin syllables, ja on OpenJTalk phonemes,
#         the others on characters; error > 0.5 (zh > 0.35, whisper-base needs a human for zh) → listed for a human (soft flag)
#   (iii) clipping (peak ≥ −0.1 dBFS), NaN, silence (RMS < −40 dBFS), loudness window RMS −16 ± 1, peak ≤ −1 dBFS  → hard / soft
#   (iv)  licence: a model whose licence is non-commercial or unknown fails a release build unless --allow-nc (dev builds)
# A lingua FAILS QA when more than half of its clips hard-fail, or when Whisper is reliable for it and its median error
# exceeds 0.75; a failed lingua ships no pack and the runtime walks its fallback chain (SG.VoiceData.LINGUA[lg].fb).
# A lingua that ships is labelled against the §3.3 targets, not just "ok": A grade with an ASR median > 0.5 → "needs review";
# zh needs whisper-small ≤ 0.35 or a human listen (whisper-base alone → "needs review"); B / C grade or no ASR → "specialist
# review" (AUDIO-V4 §4 Q3). The report also records whether --allow-nc was used and every model's licence (§B7.2 iv).
# Usage: python3 -I tools/voice-build/qa.py <wav dir> --models <dir>[,<dir2>] [--allow-nc] [--no-asr] → <wav dir>/qa.json, qa.md
#        python3 -I tools/voice-build/qa.py <wav dir> --report-only   → rewrite qa.md from an existing qa.json + synth.json
import sys, os, json, glob, wave, argparse, unicodedata, statistics

ap = argparse.ArgumentParser()
ap.add_argument('wavdir')
ap.add_argument('--models', default='')
ap.add_argument('--report-only', action='store_true')
ap.add_argument('--allow-nc', action='store_true')
ap.add_argument('--no-asr', action='store_true')
ap.add_argument('--threads', type=int, default=3)
A = ap.parse_args()
if not A.report_only and not A.models: ap.error('--models is required (unless --report-only)')
recs = json.load(open(os.path.join(A.wavdir, 'synth.json'), encoding='utf-8'))

# characters per second at bark delivery (design-v4/audio/voice-lines.js SPEED, measured on the tts-eval clips)
SPEED = { 'zh': 4.6, 'ja': 7.5, 'ko': 5.5, 'vi': 4.4, 'ms': 5.5, 'mn': 11, 'kk': 11, 'pra': 11, 'sa': 11, 'ta': 11, 'si': 11, 'fa': 11, 'syc': 12, 'ka': 12, 'ar': 11, 'gez': 6, 'la': 13, 'grc': 12, 'got': 12, 'cy': 13, 'ga': 13, 'txb': 10, 'xbc': 10, 'xmr': 10, 'ber': 10, 'os': 10 }
# Whisper language per lingua (None = no ASR: specialist review list); RELIABLE = where whisper-base proved meaningful (§B6.1)
WL = { 'zh': 'zh', 'ja': 'ja', 'ko': 'ko', 'vi': 'vi', 'ms': 'ms', 'mn': 'mn', 'kk': 'kk', 'ta': 'ta', 'si': 'si', 'fa': 'fa', 'ka': 'ka', 'ar': 'ar', 'la': 'la', 'cy': 'cy', 'sa': 'sa', 'gez': 'am' }
RELIABLE = { 'zh', 'ja', 'ko', 'ar', 'la', 'ms', 'fa', 'kk' }
# model licences (MODEL_CARD / tts-eval.json); nc = non-commercial, unknown = card says only "see URL"
LIC = {
    'kokoro-multi-lang-v1_0': ('Apache-2.0', 'ok'), 'vits-piper-zh_CN-chaowen-medium': ('CC0', 'ok'),
    'vits-mimic3-ko_KO-kss_low': ('KSS dataset CC BY-NC-SA 4.0', 'nc'), 'vits-piper-vi_VN-vais1000-medium': ('see model card', 'unknown'),
    'vits-piper-id_ID-news_tts-medium': ('see URL', 'unknown'), 'vits-piper-kk_KZ-issai-high': ('CC-BY-4.0', 'ok'),
    'vits-piper-kk_KZ-iseke-x_low': ('CC-BY-4.0', 'ok'), 'vits-piper-ur_PK-fasih-medium': ('MIT', 'ok'),
    'vits-piper-fa_IR-amir-medium': ('CC0', 'ok'), 'vits-piper-fa_IR-reza_ibrahim-medium': ('CC0', 'ok'),
    'vits-piper-ml_IN-arjun-medium': ('see URL', 'unknown'), 'vits-piper-ml_IN-meera-medium': ('see URL', 'unknown'),
    'vits-piper-ar_JO-kareem-medium': ('see URL', 'unknown'), 'vits-piper-ka_GE-natia-medium': ('see LICENSE file', 'unknown'),
    'vits-piper-sw_CD-lanfrica-medium': ('see URL', 'unknown'), 'vits-piper-it_IT-riccardo-x_low': ('see URL', 'unknown'),
    'vits-piper-it_IT-paola-medium': ('see URL', 'unknown'), 'vits-piper-de_DE-thorsten_emotional-medium': ('CC0', 'ok'),
    'vits-piper-cy_GB-bu_tts-medium': ('CC-BY', 'ok'),
}

def model_dir(name):
    for d in A.models.split(','):
        p = os.path.join(d, name)
        if os.path.isdir(p): return p
    return None
def norm(s):
    s = unicodedata.normalize('NFKC', s).lower()
    return ''.join(c for c in s if not unicodedata.category(c).startswith(('P', 'Z', 'S')))
def lev(a, b):
    d = list(range(len(b) + 1))
    for i in range(1, len(a) + 1):
        p, d[0] = d[0], i
        for j in range(1, len(b) + 1):
            p, d[j] = d[j], min(d[j] + 1, d[j - 1] + 1, p + (a[i - 1] != b[j - 1]))
    return d[len(b)]
def read_wav(fn):
    with wave.open(fn) as w:
        sr = w.getframerate(); x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    return x, sr

ASR_TARGET = 0.5        # §3.3: A languages, error ≤ 0.5
ZH_TARGET = 0.35        # §3.3: zh with whisper-small ≤ 0.35 on syllables, or a human review

def review_of(lg, s, asr_model):
    """the §3.3 label of a lingua that ships (s = its summary row)"""
    if s['failed']: return 'FAILED → fallback chain'
    med = s['asrMedian']
    if lg == 'zh':
        if asr_model != 'whisper-small': return f"needs review (zh: {asr_model or 'no ASR'} only; §3.3 wants whisper-small ≤ {ZH_TARGET} or a human listen)"
        return 'ok' if med is not None and med <= ZH_TARGET else f'needs review (ASR median {med} > {ZH_TARGET})'
    if s.get('conf') != 'A':
        return 'specialist review (grade ' + str(s.get('conf')) + (f'; ASR median {med}' if med is not None else '; no ASR') + ')'
    if med is None: return 'specialist review (A grade, no ASR)'
    if med > ASR_TARGET: return f"needs review (ASR median {med} > {ASR_TARGET}{'' if s['asrReliable'] else '; Whisper unreliable here'})"
    return 'ok'

def write_report(out, summary, allow_nc, asr_model):
    for lg, s in summary.items(): s['review'] = review_of(lg, s, asr_model)
    L = ['# Voice QA report', '',
         f"ASR: {asr_model or 'none'} · targets (AUDIO-V4 §3.3): A languages ≤ {ASR_TARGET}, zh ≤ {ZH_TARGET} with whisper-small or a human listen · "
         f"a lingua FAILS (ships no pack) when > ½ of its clips hard-fail, or Whisper is reliable for it and its median > 0.75", '',
         '| lingua | grade | clips | hard fails | flagged | ASR median (n) | reliable | result |', '|---|---|---|---|---|---|---|---|']
    for lg, s in summary.items():
        L.append(f"| {lg} | {s.get('conf', '?')} | {s['n']} | {s['hardFails']} | {s['flagged']} | {s['asrMedian'] if s['asrMedian'] is not None else '—'} ({s['asrN']}) | {'yes' if s['asrReliable'] else 'no'} | {s['review']} |")
    ok_n = sum(1 for s in summary.values() if s['review'] == 'ok')
    L += ['', f"{ok_n} of {len(summary)} linguas meet the §3.3 target outright; the rest ship but are listed for a listen above.", '']
    # licences (§B7.2 iv): every model used, its licence, the packs and clips it voices
    models = {}
    for r in recs:
        m = models.setdefault(r['model'], dict(packs=set(), clips=0))
        m['packs'].add(r['pack']); m['clips'] += 1
    blocked = [k for k in models if LIC.get(k, ('unknown', 'unknown'))[1] != 'ok']
    L += ['## Licences (§B7.2 iv)', '',
          f"Built with `--allow-nc`: **{'yes' if allow_nc else 'no'}**." + (
              f" These packs are a DEV build: a release build (without `--allow-nc`) fails the licence gate for {len(blocked)} of {len(models)} models "
              f"({sum(models[k]['clips'] for k in blocked)} clips). Korean (KSS) is non-commercial; the 'see URL' / 'see model card' voices must be checked before any distribution (AUDIO-V4 §4 Q1)."
              if allow_nc and blocked else ''), '',
          '| model | licence | kind | packs | clips |', '|---|---|---|---|---|']
    for k in sorted(models):
        lic = LIC.get(k, ('unknown', 'unknown'))
        L.append(f"| `{k}` | {lic[0]} | {'**' + lic[1] + '**' if lic[1] != 'ok' else 'ok'} | {', '.join(sorted(models[k]['packs']))} | {models[k]['clips']} |")
    L += ['', '## Hard fails (dropped from the packs)', '']
    L += [f"- `{o['pack']}/{o['id']}` {o['text']} — {'; '.join(o['hard'])}" for o in out if o['hard']] or ['(none)']
    L += ['', '## Flagged for a human listen', '']
    L += [f"- `{o['pack']}/{o['id']}` {o['text']} — {'; '.join(o['soft'])}" for o in out if o['soft'] and not o['hard']] or ['(none)']
    open(os.path.join(A.wavdir, 'qa.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')

def conf_of(lg):
    for r in recs:
        if r['lingua'] == lg: return r.get('conf')
    return None

if A.report_only:
    Q = json.load(open(os.path.join(A.wavdir, 'qa.json'), encoding='utf-8'))
    for lg, s in Q['linguas'].items(): s.setdefault('conf', conf_of(lg))
    write_report(Q['clips'], Q['linguas'], Q.get('allowNc', False), Q.get('asrModel', 'whisper-base' if any('asr' in o for o in Q['clips']) else None))
    print('qa.md rewritten from qa.json:', ', '.join(f"{lg} {s['review']}" for lg, s in Q['linguas'].items()))
    sys.exit(0)

import numpy as np      # the gates need it; --report-only does not
recs_by_lg = {}
for r in recs: recs_by_lg.setdefault(r['lingua'], []).append(r)
asr = {}
W = model_dir('sherpa-onnx-whisper-base')
if not A.no_asr and W:
    import sherpa_onnx
    from pypinyin import lazy_pinyin
    import pyopenjtalk
    for lg, rs in recs_by_lg.items():
        wl = WL.get(lg)
        if not wl: continue
        try:
            R = sherpa_onnx.OfflineRecognizer.from_whisper(encoder=W + '/base-encoder.int8.onnx', decoder=W + '/base-decoder.int8.onnx', tokens=W + '/base-tokens.txt', language=wl, task='transcribe', num_threads=A.threads)
        except Exception as e:
            print('ASR init failed', lg, e); continue
        for r in rs:
            if r.get('error'): continue
            x, sr = read_wav(os.path.join(A.wavdir, r['wav']))
            # pad 0.3 s of silence on both sides: whisper hears isolated barks better with context
            pad = np.zeros(int(0.3 * sr), dtype=np.float32)
            st = R.create_stream(); st.accept_waveform(sr, np.concatenate([pad, x, pad])); R.decode_stream(st)
            hyp = st.result.text.strip()
            ref = r['text']
            if lg == 'zh':
                a, b = lazy_pinyin(norm(ref)), lazy_pinyin(norm(hyp)); unit = 'syllable'
            elif lg == 'ja':
                a = pyopenjtalk.g2p(norm(ref)).split(); b = pyopenjtalk.g2p(norm(hyp)).split() if norm(hyp) else []; unit = 'phoneme'
            else:
                a, b = list(norm(ref)), list(norm(hyp)); unit = 'char'
            asr[r['pack'] + '/' + r['id']] = dict(hyp=hyp, err=round(lev(a, b) / max(1, len(a)), 2), unit=unit)
        print('ASR', lg, len(rs), flush=True)

out, per = [], {}
for r in recs:
    key = r['pack'] + '/' + r['id']
    hard, soft = [], []
    if r.get('error'): hard.append('synth error: ' + r['error'])
    else:
        n = len([c for c in r['text'] if unicodedata.category(c)[0] in 'LN'])
        exp = 0.25 + n / SPEED.get(r['lingua'], 10)
        if r['del_'] == 'sigh': exp += 0.28
        if r['dur'] < 0.3: hard.append(f"too short {r['dur']} s")
        if r['dur'] > max(0.6, 2.5 * exp): hard.append(f"too long {r['dur']} s (expected {exp:.2f})")
        if r.get('nan'): hard.append('NaN')
        if r['peak_db'] >= -0.1: hard.append('clipping')
        if r['rms_db'] < -40: hard.append('silence')
        if abs(r['rms_db'] + 16) > 1: soft.append(f"RMS {r['rms_db']} dBFS")
        if r['peak_db'] > -1: soft.append(f"peak {r['peak_db']} dBFS")
        a = asr.get(key)
        if a and a['err'] > (0.35 if r['lingua'] == 'zh' else 0.5): soft.append(f"ASR {a['err']} ({a['unit']}): {a['hyp'][:40]}")
    lic = LIC.get(r['model'], ('unknown', 'unknown'))
    if lic[1] != 'ok' and not A.allow_nc: hard.append('licence ' + lic[0])
    o = dict(pack=r['pack'], id=r['id'], lingua=r['lingua'], conf=r['conf'], text=r['text'], dur=r.get('dur'), ok=not hard, hard=hard, soft=soft, licence=lic[0], licenceKind=lic[1])
    if key in asr: o['asr'] = asr[key]
    out.append(o)
    p = per.setdefault(r['lingua'], dict(n=0, hard=0, soft=0, errs=[]))
    p['n'] += 1; p['hard'] += bool(hard); p['soft'] += bool(soft)
    if key in asr: p['errs'].append(asr[key]['err'])
summary = {}
for lg, p in per.items():
    med = statistics.median(p['errs']) if p['errs'] else None
    failed = p['hard'] > p['n'] / 2 or (lg in RELIABLE and med is not None and med > 0.75)
    summary[lg] = dict(n=p['n'], conf=conf_of(lg), hardFails=p['hard'], flagged=p['soft'], asrMedian=med, asrN=len(p['errs']), asrReliable=lg in RELIABLE, failed=failed)
asr_model = 'whisper-base' if asr else None
# human-readable report (labels against §3.3, licences, dropped and flagged clips), then the machine-readable one with the labels
write_report(out, summary, A.allow_nc, asr_model)
json.dump(dict(clips=out, linguas=summary, allowNc=A.allow_nc, asrModel=asr_model), open(os.path.join(A.wavdir, 'qa.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
print(f"QA: {len(out)} clips, hard fails {sum(1 for o in out if o['hard'])}, flagged {sum(1 for o in out if o['soft'] and not o['hard'])}; failed linguas: {[lg for lg, s in summary.items() if s['failed']]}")
for lg, s in summary.items(): print(f"  {lg:4s} n={s['n']:4d} hard={s['hardFails']:3d} flagged={s['flagged']:3d} asr={s['asrMedian']} ({s['asrN']}) {'FAILED' if s['failed'] else ''}")
