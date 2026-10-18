# 中文 piper（拼音音素型，如 chaowen CC0）：用 pypinyin（或台词表里手写的拼音）代替 g2pW
import sys, json, os, time, wave, glob, re, subprocess
import numpy as np
from pypinyin import pinyin, Style
from piper import PiperVoice, SynthesisConfig
from piper.phonemize_chinese import phonemes_to_ids, _normalize_g2pw_syllable, _split_initial_final_tone, PHONEME_TO_ID
S, outdir = sys.argv[1], sys.argv[2]; os.makedirs(outdir, exist_ok=True)
def phon(text, py=None):
    sy = py.split() if py else [p[0] for p in pinyin(text, style=Style.TONE3, neutral_tone_with_five=True, errors=lambda x: list(x))]
    out = []
    for s in sy:
        s = _normalize_g2pw_syllable(s)
        i, f, t = _split_initial_final_tone(s)
        if not f:
            if s in PHONEME_TO_ID: out.append(s)
            elif s in '！!': out.append('!')
            elif s in '，,': out.append(',')
            elif s in '。.': out.append('.')
            continue
        out += [i or 'Ø', f, t]
    return out
res = []
for (id_, model, text, py, ls) in json.loads(sys.argv[3]):
    onnx = glob.glob(f'{S}/models*/{model}/*.onnx')[0]
    v = PiperVoice.load(onnx)
    ph = phon(text, py)
    ids = phonemes_to_ids(ph, v.config.phoneme_id_map)
    t0 = time.time()
    a = v.phoneme_ids_to_audio(ids, SynthesisConfig(length_scale=ls))
    a = np.asarray(a).reshape(-1); el = time.time() - t0
    a = a / max(1e-6, np.abs(a).max()) * 0.89; sr = v.config.sample_rate
    fn = f'{outdir}/{id_}.wav'
    with wave.open(fn, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr); w.writeframes((a * 32767).astype(np.int16).tobytes())
    r = dict(id=id_, lang='zh', model=model, text=text, ph=' '.join(ph), dur=round(len(a) / sr, 3), synth_s=round(el, 3)); res.append(r); print(json.dumps(r, ensure_ascii=False))
json.dump(res, open(outdir + '/results.json', 'w'), ensure_ascii=False, indent=1)
