# 粗测每个合成样本的中位基频（自相关），用来判断声库的性别 / 音域
import sys, glob, os, wave, json
import numpy as np
def f0(fn):
    w = wave.open(fn); sr = w.getframerate(); x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    hop, win = int(sr * 0.01), int(sr * 0.04); out = []
    for i in range(0, len(x) - win, hop):
        f = x[i:i + win] * np.hanning(win)
        if np.sqrt((f ** 2).mean()) < 0.03: continue
        ac = np.correlate(f, f, 'full')[win - 1:]
        lo, hi = int(sr / 400), int(sr / 70)
        k = lo + np.argmax(ac[lo:hi])
        if ac[k] > 0.35 * ac[0]: out.append(sr / k)
    return float(np.median(out)) if out else None
rows = {}
for fn in sorted(glob.glob(sys.argv[1] + '/out/t*/*.wav') + glob.glob(sys.argv[1] + '/out/ja/*.wav') + glob.glob(sys.argv[1] + '/out/zh2/*.wav')):
    b = os.path.basename(fn)[:-4]
    v = f0(fn)
    if v: rows[b] = round(v)
for k, v in rows.items(): print(f'{k:34s} {v} Hz')
json.dump(rows, open(sys.argv[1] + '/out/f0.json', 'w'), indent=1)
