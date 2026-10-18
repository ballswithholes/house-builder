#!/bin/bash
cd "$1"
B=https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models
for m in vits-piper-zh_CN-huayan-medium vits-zh-hf-fanchen-C sherpa-onnx-vits-zh-ll vits-mimic3-ko_KO-kss_low kokoro-multi-lang-v1_0 kokoro-int8-multi-lang-v1_0 vits-piper-vi_VN-vais1000-medium vits-piper-kk_KZ-issai-high vits-piper-fa_IR-amir-medium vits-piper-ar_JO-kareem-medium vits-piper-el_GR-rapunzelina-low vits-piper-it_IT-riccardo-x_low vits-piper-de_DE-thorsten-medium vits-piper-cy_GB-gwryw_gogleddol-medium vits-piper-hi_IN-pratham-medium vits-piper-ml_IN-arjun-medium vits-piper-ka_GE-natia-medium vits-piper-sw_CD-lanfrica-medium vits-piper-tr_TR-dfki-medium vits-piper-ne_NP-google-medium vits-mms-tha vits-piper-is_IS-bui-medium vits-piper-fa_IR-gyro-medium vits-piper-ar_JO-SA_miro-high vits-piper-de_DE-thorsten_emotional-medium vits-piper-zh_CN-chaowen-medium vits-piper-it_IT-paola-medium vits-piper-el_GR-chreece-high vits-piper-te_IN-maya-medium vits-piper-hi_IN-priyamvada-medium vits-piper-ml_IN-meera-medium vits-piper-ta_IN-? ; do
  [ -d "$m" ] && continue
  code=$(curl -sSL -m 600 -w "%{http_code}" -o "$m.tar.bz2" "$B/$m.tar.bz2")
  if [ "$code" = "200" ]; then sz=$(stat -c %s "$m.tar.bz2"); tar xjf "$m.tar.bz2" && rm "$m.tar.bz2"; echo "OK $m $sz"; else rm -f "$m.tar.bz2"; echo "FAIL $m $code"; fi
done
echo DONE
