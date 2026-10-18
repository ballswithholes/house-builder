# 日语：OpenJTalk 注音 → 近似 misaki IPA → 直接驱动 Kokoro ONNX（绕开 sherpa 的中英前端）
import sys, json, time, wave, os, subprocess
import numpy as np, onnxruntime as ort, pyopenjtalk
S, outdir = sys.argv[1], sys.argv[2]; os.makedirs(outdir, exist_ok=True)
D = S + '/models/kokoro-multi-lang-v1_0'
tok = {}
for l in open(D + '/tokens.txt', encoding='utf-8'):
    l = l.rstrip('\n')
    if not l: continue
    sym, i = l.rsplit(' ', 1); tok[sym if sym else ' '] = int(i)
voices = np.fromfile(D + '/voices.bin', dtype=np.float32).reshape(54, 510, 256)
sess = ort.InferenceSession(D + '/model.onnx')
M = {'a':'a','i':'i','u':'ɯ','e':'e','o':'o','A':'a','I':'i','U':'ɯ','E':'e','O':'o','N':'ɴ','cl':'ʔ','pau':',','k':'k','g':'ɡ','s':'s','sh':'ɕ','z':'z','j':'ʥ','t':'t','ch':'ʨ','ts':'ʦ','d':'d','n':'n','h':'h','f':'ɸ','b':'b','p':'p','m':'m','y':'j','r':'ɾ','w':'w','ky':'kj','gy':'ɡj','ny':'ɲ','hy':'ç','my':'mj','ry':'ɾj','by':'bj','py':'pj','v':'v','dy':'dj','ty':'tj'}
def ipa(text):
    out = []
    for seg in pyopenjtalk.g2p(text).split(' '):
        p = M.get(seg, '')
        if out and p and p == out[-1] and p in 'aiɯeo': out.append('ː'); continue
        out.append(p)
    s = ''.join(out)
    if text.strip()[-1] in '！!': s += '!'
    return s
res = []
for (id_, sid, text, speed) in json.loads(sys.argv[3]):
    ph = ipa(text)
    ids = [tok[c] for c in ph if c in tok]
    t0 = time.time()
    audio = sess.run(None, {'tokens': np.array([[0] + ids + [0]], dtype=np.int64), 'style': voices[sid, min(len(ids), 509)][None, :], 'speed': np.array([speed], dtype=np.float32)})[0].reshape(-1)
    el = time.time() - t0
    audio = audio / max(1e-6, np.abs(audio).max()) * 0.89
    fn = f'{outdir}/{id_}.wav'
    with wave.open(fn, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(24000); w.writeframes((audio * 32767).astype(np.int16).tobytes())
    subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', fn, '-c:a', 'libopus', '-b:a', '24k', fn[:-4] + '.24k.opus'], check=True)
    r = dict(id=id_, lang='ja', model='kokoro-direct', text=text, ipa=ph, sr=24000, dur=round(len(audio) / 24000, 3), synth_s=round(el, 3), opus24=os.path.getsize(fn[:-4] + '.24k.opus'))
    res.append(r); print(json.dumps(r, ensure_ascii=False))
json.dump(res, open(outdir + '/results.json', 'w'), ensure_ascii=False, indent=1)
