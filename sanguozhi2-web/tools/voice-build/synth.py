# tools/voice-build/synth.py — step 2 of the offline voice pipeline (AUDIO-V4 §B7.2)
# Renders every row of lines.json with offline neural TTS, then the post chain (§B7.2 step 3):
#   trim to −45 dB (20 ms head, 50 ms tail) · delivery presets (§B4.3: command 1.0 / shout 0.88 + shout chain / sigh 1.15 + breath)
#   · loudness RMS −16 dBFS ± 1, peak ≤ −1 dBFS.
# Engines (the ones proven in design-v4/audio/tts-lab):
#   piper     Piper 1.8 voices (sherpa-onnx tts-models mirrors); cross-lingual routes phonemize with espeak-ng in the SOURCE
#             language and inject the IPA as [[ … ]] (Latin → Italian, Gothic → Icelandic phonemes on Thorsten, …); 'ipa' rows
#             carry authored IPA (export-lines.js)
#   kokoro    Kokoro v1.0 through sherpa-onnx for Chinese (zm_yunjian / zf_xiaoyi)
#   kokoro-ja Kokoro ONNX driven directly with OpenJTalk phonemes (the sherpa front end has no Japanese G2P)
#   sherpa    sherpa-onnx VITS for the Mimic3 Korean voice
# Usage (offline, developer machine; run with the TTS venv of tts-lab):
#   python3 -I tools/voice-build/synth.py lines.json --models <dir>[,<dir2>] --out <wav dir> [--only <pack,...>] [--limit N]
# Writes <out>/<pack>/<cue>#<variant>.wav and <out>/synth.json (one record per clip: dur, rms, peak, synth time, ipa used).
import sys, os, json, time, glob, wave, argparse, re, zlib
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument('lines')
ap.add_argument('--models', required=True)
ap.add_argument('--out', required=True)
ap.add_argument('--only', default='')
ap.add_argument('--limit', type=int, default=0)
ap.add_argument('--threads', type=int, default=2)
ap.add_argument('--force', action='store_true')
A = ap.parse_args()
MODEL_DIRS = A.models.split(',')
rows = json.load(open(A.lines, encoding='utf-8'))
only = set(x for x in A.only.split(',') if x)
if only: rows = [r for r in rows if r['pack'] in only]
if A.limit: rows = rows[:A.limit]
os.makedirs(A.out, exist_ok=True)

from scipy.signal import butter, lfilter, sosfilt, resample_poly

def model_dir(name):
    for d in MODEL_DIRS:
        p = os.path.join(d, name)
        if os.path.isdir(p): return p
    raise FileNotFoundError(name)

# ---------------------------------------------------------------- engines --
KOKORO_SID = { 'af_alloy': 0, 'jf_alpha': 37, 'jf_gongitsune': 38, 'jf_nezumi': 39, 'jf_tebukuro': 40, 'jm_kumo': 41,
               'zf_xiaobei': 45, 'zf_xiaoni': 46, 'zf_xiaoxiao': 47, 'zf_xiaoyi': 48, 'zm_yunjian': 49, 'zm_yunxi': 50, 'zm_yunxia': 51, 'zm_yunyang': 52 }
cache = {}
def piper_voice(model):
    if model not in cache:
        from piper import PiperVoice
        d = model_dir(model)
        onnx = [p for p in glob.glob(d + '/*.onnx')][0]
        v = PiperVoice.load(onnx)
        cache[model] = v
    return cache[model]
_esp = None
def espeak(lang, text):
    global _esp
    from piper.phonemize_espeak import EspeakPhonemizer
    from piper.voice import ESPEAK_DATA_DIR
    if _esp is None: _esp = EspeakPhonemizer(ESPEAK_DATA_DIR)
    ph = _esp.phonemize(lang, text)
    return ' '.join(''.join(s) for s in ph)
def sherpa_kokoro(model):
    key = 'sk:' + model
    if key not in cache:
        import sherpa_onnx
        d = model_dir(model)
        mf = glob.glob(d + '/model*.onnx')[0]
        cfg = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(kokoro=sherpa_onnx.OfflineTtsKokoroModelConfig(
            model=mf, voices=d + '/voices.bin', tokens=d + '/tokens.txt', data_dir=d + '/espeak-ng-data', dict_dir=d + '/dict',
            lexicon=d + '/lexicon-us-en.txt,' + d + '/lexicon-zh.txt'), num_threads=A.threads),
            rule_fsts=d + '/date-zh.fst,' + d + '/phone-zh.fst,' + d + '/number-zh.fst')
        cache[key] = sherpa_onnx.OfflineTts(cfg)
    return cache[key]
def sherpa_vits(model):
    key = 'sv:' + model
    if key not in cache:
        import sherpa_onnx
        d = model_dir(model)
        mf = [p for p in glob.glob(d + '/*.onnx')][0]
        kw = dict(model=mf, tokens=d + '/tokens.txt')
        if os.path.exists(d + '/lexicon.txt'): kw['lexicon'] = d + '/lexicon.txt'
        if os.path.exists(d + '/dict'): kw['dict_dir'] = d + '/dict'
        if os.path.exists(d + '/espeak-ng-data'): kw['data_dir'] = d + '/espeak-ng-data'
        cfg = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(vits=sherpa_onnx.OfflineTtsVitsModelConfig(**kw), num_threads=A.threads))
        cache[key] = sherpa_onnx.OfflineTts(cfg)
    return cache[key]
# Japanese: OpenJTalk phonemes → Kokoro IPA tokens (tts-lab/kokoro_ja.py)
JA_MAP = {'a':'a','i':'i','u':'ɯ','e':'e','o':'o','A':'a','I':'i','U':'ɯ','E':'e','O':'o','N':'ɴ','cl':'ʔ','pau':',','k':'k','g':'ɡ','s':'s','sh':'ɕ','z':'z','j':'ʥ','t':'t','ch':'ʨ','ts':'ʦ','d':'d','n':'n','h':'h','f':'ɸ','b':'b','p':'p','m':'m','y':'j','r':'ɾ','w':'w','ky':'kj','gy':'ɡj','ny':'ɲ','hy':'ç','my':'mj','ry':'ɾj','by':'bj','py':'pj','v':'v','dy':'dj','ty':'tj'}
def kokoro_ja(model, sid, text, speed):
    key = 'kj:' + model
    if key not in cache:
        import onnxruntime as ort
        d = model_dir(model)
        tok = {}
        for l in open(d + '/tokens.txt', encoding='utf-8'):
            l = l.rstrip('\n')
            if not l: continue
            sym, i = l.rsplit(' ', 1); tok[sym if sym else ' '] = int(i)
        voices = np.fromfile(d + '/voices.bin', dtype=np.float32).reshape(-1, 510, 256)
        so = ort.SessionOptions(); so.intra_op_num_threads = A.threads
        cache[key] = (tok, voices, ort.InferenceSession(d + '/model.onnx', so))
    tok, voices, sess = cache[key]
    import pyopenjtalk
    out = []
    for seg in pyopenjtalk.g2p(text).split(' '):
        p = JA_MAP.get(seg, '')
        if out and p and p == out[-1] and p in 'aiɯeo': out.append('ː'); continue
        out.append(p)
    ph = ''.join(out)
    if text.strip()[-1:] in '！!': ph += '!'
    elif text.strip()[-1:] in '？?': ph += '?'
    elif text.strip()[-1:] in '…。.': ph += '.'
    ids = [tok[c] for c in ph if c in tok]
    a = sess.run(None, {'tokens': np.array([[0] + ids + [0]], dtype=np.int64), 'style': voices[sid, min(len(ids), 509)][None, :], 'speed': np.array([speed], dtype=np.float32)})[0].reshape(-1)
    return a.astype(np.float32), 24000, ph

LS = { 'command': 1.0, 'shout': 0.88, 'sigh': 1.15 }
def synth(r):
    ls = LS.get(r['del'], 1.0)
    model, spk = r['model'], r.get('speaker')
    route = r['route']
    info = {}
    if model.startswith('kokoro') and r['lingua'] == 'ja':
        a, sr, ph = kokoro_ja(model, KOKORO_SID[spk], r['text'], 1.0 / ls); info['ipa'] = ph
        return a, sr, info
    if model.startswith('kokoro'):
        tts = sherpa_kokoro(model)
        g = tts.generate(r['text'], sid=KOKORO_SID[spk], speed=1.0 / ls)
        return np.array(g.samples, dtype=np.float32), g.sample_rate, info
    if model.startswith('vits-mimic3') or not model.startswith('vits-piper'):
        tts = sherpa_vits(model)
        g = tts.generate(r['text'], sid=int(spk or 0), speed=1.0 / ls)
        return np.array(g.samples, dtype=np.float32), g.sample_rate, info
    from piper import SynthesisConfig
    v = piper_voice(model)
    native = (v.config.espeak_voice if hasattr(v.config, 'espeak_voice') else None) or ''
    text = r['text']
    if r.get('ipa'):
        text = '[[ ' + r['ipa'] + ' ]]'; info['ipa'] = r['ipa']
    elif route.startswith('espeak:'):
        src = route.split(':', 1)[1]
        if src.split('-')[0] != native.split('-')[0]:
            ph = espeak(src, text); text = '[[ ' + ph + ' ]]'; info['ipa'] = ph
    sid = None
    if spk is not None:
        m = v.config.speaker_id_map if hasattr(v.config, 'speaker_id_map') else {}
        sid = int(spk) if str(spk).isdigit() else (m or {}).get(spk)
    cfg = SynthesisConfig(length_scale=ls, speaker_id=sid)
    chunks = list(v.synthesize(text, syn_config=cfg))
    a = np.concatenate([c.audio_float_array for c in chunks]).astype(np.float32)
    return a, chunks[0].sample_rate, info

# ---------------------------------------------------------------- post chain --
def db(x): return float(20 * np.log10(max(1e-9, float(x))))
def trim(a, sr):
    pk = np.abs(a).max() if len(a) else 0
    if pk <= 0: return a
    idx = np.where(np.abs(a) > pk * 0.0056)[0]          # −45 dB re peak
    if not len(idx): return a
    return a[max(0, idx[0] - int(0.02 * sr)): idx[-1] + int(0.05 * sr)]
def peaking(sr, f0, gain_db, q):
    A_ = 10 ** (gain_db / 40); w = 2 * np.pi * f0 / sr; al = np.sin(w) / (2 * q)
    b = [1 + al * A_, -2 * np.cos(w), 1 - al * A_]; a = [1 + al / A_, -2 * np.cos(w), 1 - al / A_]
    return np.array(b) / a[0], np.array(a) / a[0]
def compress(a, sr, thr_db=-20, ratio=6.0, att=0.005, rel=0.06):
    env = 0.0; out = np.empty_like(a)
    ka, kr = np.exp(-1 / (att * sr)), np.exp(-1 / (rel * sr))
    thr = 10 ** (thr_db / 20)
    # block-wise envelope (1 ms) keeps this fast in numpy
    n = max(1, int(sr * 0.001))
    for i in range(0, len(a), n):
        blk = a[i:i + n]; pk = np.abs(blk).max() if len(blk) else 0
        k = ka if pk > env else kr
        env = k * env + (1 - k) * pk
        g = 1.0 if env <= thr else (thr * (env / thr) ** (1 / ratio)) / env
        out[i:i + n] = blk * g
    return out
def shout_chain(a, sr):
    sos = butter(2, 120, 'highpass', fs=sr, output='sos'); a = sosfilt(sos, a)
    a = a / max(1e-6, np.abs(a).max()) * 0.8
    a = compress(a, sr)
    a = np.tanh(a * 1.8) / np.tanh(1.8)
    b, c = peaking(sr, 2500, 4, 1.0); a = lfilter(b, c, a)
    out = np.zeros(len(a) + int(0.08 * sr), dtype=np.float64)
    out[:len(a)] += a
    for d, g in ((0.035, 0.32), (0.070, 0.16)):     # slap echo 35 / 70 ms
        k = int(d * sr); out[k:k + len(a)] += a * g
    return out.astype(np.float32)
def sigh_tail(a, sr, seed):
    rng = np.random.default_rng(seed)
    n = int(0.28 * sr)
    nz = rng.standard_normal(n).astype(np.float32)
    sos = butter(2, [300, 1600], 'bandpass', fs=sr, output='sos'); nz = sosfilt(sos, nz)
    env = np.exp(-np.linspace(0, 5, n)) * np.minimum(1, np.linspace(0, 8, n))
    rms = np.sqrt(np.mean(a ** 2)) if len(a) else 0.05
    tail = nz / max(1e-6, np.sqrt(np.mean(nz ** 2))) * rms * 0.06 * env
    return np.concatenate([a, tail.astype(np.float32)])
def loudness(a):
    tgt = 10 ** (-16 / 20); ceil = 10 ** (-1 / 20)
    for _ in range(4):
        rms = np.sqrt(np.mean(a ** 2)) if len(a) else 0
        if rms <= 0: return a
        a = a * (tgt / rms)
        pk = np.abs(a).max()
        if pk <= ceil: return a
        t = 0.6 * ceil                                   # soft knee limiter above −5.4 dBFS
        s = np.sign(a); x = np.abs(a)
        over = x > t
        x[over] = t + (ceil - t) * np.tanh((x[over] - t) / (ceil - t))
        a = s * x
    return a
def write_wav(fn, a, sr):
    a = np.clip(a, -1, 1)
    with wave.open(fn, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr); w.writeframes((a * 32767).astype(np.int16).tobytes())

# ---------------------------------------------------------------- main --
res_fn = os.path.join(A.out, 'synth.json')
done = {}
if os.path.exists(res_fn) and not A.force:
    for x in json.load(open(res_fn, encoding='utf-8')): done[x['pack'] + '/' + x['id']] = x
results = dict(done)
t_all = time.time()
for i, r in enumerate(rows):
    key = r['pack'] + '/' + r['id']
    fn = os.path.join(A.out, r['pack'], r['id'].replace('/', '_') + '.wav')
    if key in done and os.path.exists(fn): continue
    os.makedirs(os.path.dirname(fn), exist_ok=True)
    rec = dict(pack=r['pack'], id=r['id'], lingua=r['lingua'], cue=r['cue'], variant=r['variant'], text=r['text'], model=r['model'], speaker=r.get('speaker'), del_=r['del'], conf=r['conf'])
    try:
        t0 = time.time()
        a, sr, info = synth(r)
        rec['synth_s'] = round(time.time() - t0, 3)
        a = np.nan_to_num(a.astype(np.float32))
        a = trim(a, sr)
        if r['del'] == 'shout': a = shout_chain(a, sr)
        if r['del'] == 'sigh': a = sigh_tail(a, sr, zlib.crc32(key.encode('utf-8')))
        a = loudness(a.astype(np.float64)).astype(np.float32)
        write_wav(fn, a, sr)
        rec.update(sr=sr, dur=round(len(a) / sr, 3), rms_db=round(db(np.sqrt(np.mean(a ** 2))), 1), peak_db=round(db(np.abs(a).max()), 1), wav=os.path.relpath(fn, A.out), nan=bool(np.isnan(a).any()))
        rec.update(info)
    except Exception as e:
        rec['error'] = repr(e)[:300]
    results[key] = rec
    if i % 25 == 0:
        print(f'[{i + 1}/{len(rows)}] {key} {rec.get("dur")} s {rec.get("error", "")} ({time.time() - t_all:.0f} s)', flush=True)
        json.dump(list(results.values()), open(res_fn, 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
json.dump(list(results.values()), open(res_fn, 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
errs = [x for x in results.values() if x.get('error')]
print(f'synth: {len(results)} clips, {len(errs)} errors, {time.time() - t_all:.0f} s')
for x in errs[:20]: print('  ERR', x['pack'], x['id'], x['error'])
