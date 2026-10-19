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
# Usage: python3 -I tools/voice-build/qa.py <wav dir> --models <dir>[,<dir2>] [--allow-nc] [--no-asr] → <wav dir>/qa.json, qa.md
import sys, os, json, glob, wave, argparse, unicodedata, statistics
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument('wavdir')
ap.add_argument('--models', required=True)
ap.add_argument('--allow-nc', action='store_true')
ap.add_argument('--no-asr', action='store_true')
ap.add_argument('--threads', type=int, default=3)
A = ap.parse_args()
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
    summary[lg] = dict(n=p['n'], hardFails=p['hard'], flagged=p['soft'], asrMedian=med, asrN=len(p['errs']), asrReliable=lg in RELIABLE, failed=failed)
json.dump(dict(clips=out, linguas=summary, allowNc=A.allow_nc), open(os.path.join(A.wavdir, 'qa.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
# human-readable report: the flagged clips with their audio paths
L = ['# Voice QA report', '', '| lingua | clips | hard fails | flagged | ASR median (n) | reliable | result |', '|---|---|---|---|---|---|---|']
for lg, s in summary.items():
    L.append(f"| {lg} | {s['n']} | {s['hardFails']} | {s['flagged']} | {s['asrMedian'] if s['asrMedian'] is not None else '—'} ({s['asrN']}) | {'yes' if s['asrReliable'] else 'no'} | {'FAILED → fallback chain' if s['failed'] else 'ok'} |")
L += ['', '## Hard fails (dropped from the packs)', '']
L += [f"- `{o['pack']}/{o['id']}` {o['text']} — {'; '.join(o['hard'])}" for o in out if o['hard']] or ['(none)']
L += ['', '## Flagged for a human listen', '']
L += [f"- `{o['pack']}/{o['id']}` {o['text']} — {'; '.join(o['soft'])}" for o in out if o['soft'] and not o['hard']] or ['(none)']
open(os.path.join(A.wavdir, 'qa.md'), 'w', encoding='utf-8').write('\n'.join(L) + '\n')
print(f"QA: {len(out)} clips, hard fails {sum(1 for o in out if o['hard'])}, flagged {sum(1 for o in out if o['soft'] and not o['hard'])}; failed linguas: {[lg for lg, s in summary.items() if s['failed']]}")
for lg, s in summary.items(): print(f"  {lg:4s} n={s['n']:4d} hard={s['hardFails']:3d} flagged={s['flagged']:3d} asr={s['asrMedian']} ({s['asrN']}) {'FAILED' if s['failed'] else ''}")
