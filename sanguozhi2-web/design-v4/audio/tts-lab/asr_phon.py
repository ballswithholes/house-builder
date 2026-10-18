# 按音而非字比较：中文 → 无调拼音，日文 → OpenJTalk 音素，其余语言用原字 CER
import json, sys, unicodedata
from pypinyin import lazy_pinyin
import pyopenjtalk
S = sys.argv[1]
d = json.load(open(S + '/out/asr.json', encoding='utf-8'))
def lev(a, b):
    dd = list(range(len(b) + 1))
    for i in range(1, len(a) + 1):
        p, dd[0] = dd[0], i
        for j in range(1, len(b) + 1):
            p, dd[j] = dd[j], min(dd[j] + 1, dd[j - 1] + 1, p + (a[i - 1] != b[j - 1]))
    return dd[len(b)]
def keep(s): return ''.join(c for c in unicodedata.normalize('NFKC', s) if not unicodedata.category(c).startswith(('P','Z','S')))
for r in d:
    if r['lang'] == 'zh':
        a = lazy_pinyin(keep(r['ref'])); b = lazy_pinyin(keep(r['hyp']))
        r['per'] = round(lev(a, b) / max(1, len(a)), 2); r['unit'] = 'syllable'
    elif r['lang'] == 'ja':
        a = pyopenjtalk.g2p(keep(r['ref'])).split(); b = pyopenjtalk.g2p(keep(r['hyp'])).split() if keep(r['hyp']) else []
        r['per'] = round(lev(a, b) / max(1, len(a)), 2); r['unit'] = 'phoneme'
    else:
        r['per'] = r['cer']; r['unit'] = 'char'
    print(f"{r['id']:32s} {r['lang']:4s} {r['unit']:8s} {r['per']:5}  {r['hyp'][:40]}")
json.dump(d, open(S + '/out/asr2.json', 'w'), ensure_ascii=False, indent=1)
