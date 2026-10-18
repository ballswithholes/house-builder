# 离线 TTS 试验：按 JSON 列表合成短句，记录合成耗时、时长、峰值 / RMS，并编码为 Opus 测体积
import json, sys, time, wave, os, subprocess, glob
import numpy as np
S = sys.argv[1]; tests = json.load(open(sys.argv[2], encoding='utf-8')); outdir = sys.argv[3]
os.makedirs(outdir, exist_ok=True)
from piper import PiperVoice, SynthesisConfig
from piper.phonemize_espeak import EspeakPhonemizer
from piper.voice import ESPEAK_DATA_DIR
import sherpa_onnx
cache = {}
espk = None
def piper_voice(model):
    if model not in cache:
        onnx = [p for p in glob.glob(os.path.join(S, 'models*', model, '*.onnx'))][0]
        t0 = time.time(); v = PiperVoice.load(onnx); cache[model] = (v, time.time() - t0)
    return cache[model]
def sherpa_tts(model):
    if model in cache: return cache[model]
    d = glob.glob(os.path.join(S, 'models*', model))[0]
    t0 = time.time()
    if model.startswith('kokoro'):
        mf = glob.glob(d + '/model*.onnx')[0]
        cfg = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(kokoro=sherpa_onnx.OfflineTtsKokoroModelConfig(model=mf, voices=d + '/voices.bin', tokens=d + '/tokens.txt', data_dir=d + '/espeak-ng-data', dict_dir=d + '/dict', lexicon=d + '/lexicon-us-en.txt,' + d + '/lexicon-zh.txt'), num_threads=2), rule_fsts=d + '/date-zh.fst,' + d + '/phone-zh.fst,' + d + '/number-zh.fst')
    else:
        mf = [p for p in glob.glob(d + '/*.onnx')][0]
        kw = dict(model=mf, tokens=d + '/tokens.txt')
        if os.path.exists(d + '/lexicon.txt'): kw['lexicon'] = d + '/lexicon.txt'
        if os.path.exists(d + '/dict'): kw['dict_dir'] = d + '/dict'
        if os.path.exists(d + '/espeak-ng-data'): kw['data_dir'] = d + '/espeak-ng-data'
        fst = ','.join(p for p in [d + '/phone.fst', d + '/date.fst', d + '/number.fst'] if os.path.exists(p))
        cfg = sherpa_onnx.OfflineTtsConfig(model=sherpa_onnx.OfflineTtsModelConfig(vits=sherpa_onnx.OfflineTtsVitsModelConfig(**kw), num_threads=2), rule_fsts=fst)
    tts = sherpa_onnx.OfflineTts(cfg)
    cache[model] = (tts, time.time() - t0); return cache[model]
res = []
for t in tests:
    try:
        t0 = time.time()
        if t['engine'] == 'piper':
            v, lt = piper_voice(t['model'])
            text = t['text']
            if t.get('phon'):          # 跨语言：用另一种 espeak 语言注音，再交给本模型
                global_esp = EspeakPhonemizer(ESPEAK_DATA_DIR)
                ph = global_esp.phonemize(t['phon'], text)
                phs = ''.join(''.join(s) for s in ph)
                text = '[[ ' + phs + ' ]]'
                t['ipa'] = phs
            cfg = SynthesisConfig(length_scale=t.get('ls'), noise_scale=t.get('ns'), noise_w_scale=t.get('nw'), speaker_id=t.get('sid'))
            chunks = list(v.synthesize(text, syn_config=cfg))
            audio = np.concatenate([c.audio_float_array for c in chunks]); sr = chunks[0].sample_rate
        else:
            tts, lt = sherpa_tts(t['model'])
            g = tts.generate(t['text'], sid=t.get('sid', 0), speed=t.get('speed', 1.0))
            audio = np.array(g.samples, dtype=np.float32); sr = g.sample_rate
        el = time.time() - t0
        # 去掉首尾静音（-45 dB）
        a = np.abs(audio); thr = a.max() * 0.0056 if a.max() > 0 else 0
        idx = np.where(a > thr)[0]
        if len(idx): audio = audio[max(0, idx[0] - int(0.02 * sr)): idx[-1] + int(0.05 * sr)]
        audio = audio / max(1e-6, np.abs(audio).max()) * 0.89
        fn = os.path.join(outdir, t['id'] + '.wav')
        with wave.open(fn, 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr); w.writeframes((audio * 32767).astype(np.int16).tobytes())
        sizes = {}
        for br in (24, 32):
            of = fn[:-4] + f'.{br}k.opus'
            subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', fn, '-c:a', 'libopus', '-b:a', f'{br}k', '-ac', '1', '-application', 'voip', of], check=True)
            sizes[br] = os.path.getsize(of)
        dur = len(audio) / sr
        rms = float(np.sqrt(np.mean(audio ** 2)))
        r = dict(id=t['id'], lang=t.get('lang'), model=t['model'], text=t['text'], sr=sr, dur=round(dur, 3), synth_s=round(el, 3), rtf=round(el / max(dur, 1e-3), 3), load_s=round(lt, 2), rms_db=round(20 * np.log10(rms + 1e-9), 1), opus24=sizes[24], opus32=sizes[32])
        if t.get('ipa'): r['ipa'] = t['ipa']
        res.append(r); print(json.dumps(r, ensure_ascii=False), flush=True)
    except Exception as e:
        print(json.dumps(dict(id=t['id'], error=repr(e)[:300]), ensure_ascii=False), flush=True)
json.dump(res, open(os.path.join(outdir, 'results.json'), 'w'), ensure_ascii=False, indent=1)
