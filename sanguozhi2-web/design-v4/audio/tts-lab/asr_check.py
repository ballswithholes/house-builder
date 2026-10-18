# 可懂度：Whisper-base 回听每条合成语音，计算字符错误率（CER，去标点、去空格、小写）
import sys, json, glob, os, unicodedata, re, wave
import numpy as np, sherpa_onnx
S = sys.argv[1]; W = S + '/models2/sherpa-onnx-whisper-base'
WL = {'zh':'zh','ja':'ja','ko':'ko','vi':'vi','kk':'kk','mn':'mn','fa':'fa','ar':'ar','el':'el','grc':'el','it':'it','la':'la','de':'de','cy':'cy','hi':'hi','sa':'sa','ta':'ta','ur':'ur','id':'id','ka':'ka','sw':'sw','tr':'tr','ne':'ne','ml':'ml','is':'is','th':'th','gez':'am','got':None,'sga':None,'arc':None}
recs = {}
def rec(lang):
    if lang not in recs:
        recs[lang] = sherpa_onnx.OfflineRecognizer.from_whisper(encoder=W + '/base-encoder.int8.onnx', decoder=W + '/base-decoder.int8.onnx', tokens=W + '/base-tokens.txt', language=lang, task='transcribe', num_threads=4)
    return recs[lang]
def norm(s):
    s = unicodedata.normalize('NFKC', s).lower()
    s = ''.join(c for c in s if not unicodedata.category(c).startswith(('P', 'Z', 'S')))
    return s
def lev(a, b):
    d = list(range(len(b) + 1))
    for i in range(1, len(a) + 1):
        p, d[0] = d[0], i
        for j in range(1, len(b) + 1):
            p, d[j] = d[j], min(d[j] + 1, d[j - 1] + 1, p + (a[i - 1] != b[j - 1]))
    return d[len(b)]
out = []
for rf in sys.argv[2:]:
    for r in json.load(open(rf, encoding='utf-8')):
        lang = WL.get(r.get('lang'))
        if not lang: continue
        fn = os.path.join(os.path.dirname(rf), r['id'] + '.wav')
        with wave.open(fn) as w:
            sr = w.getframerate(); x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
        try:
            R = rec(lang); st = R.create_stream(); st.accept_waveform(sr, x); R.decode_stream(st); hyp = st.result.text
        except Exception as e:
            hyp = '<err ' + repr(e)[:80] + '>'
        ref = r['text']
        if ref.startswith('[['): continue
        a, b = norm(ref), norm(hyp)
        cer = lev(a, b) / max(1, len(a))
        o = dict(id=r['id'], lang=r['lang'], ref=ref, hyp=hyp.strip(), cer=round(cer, 2), dur=r.get('dur'))
        out.append(o); print(json.dumps(o, ensure_ascii=False), flush=True)
json.dump(out, open(os.environ.get('ASR_OUT', S + '/out/asr.json'), 'w'), ensure_ascii=False, indent=1)
