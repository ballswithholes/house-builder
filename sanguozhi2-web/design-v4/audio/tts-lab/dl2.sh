#!/bin/bash
cd "$1"
B=https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models
for m in vits-piper-ja_JP-hi_fi_captain-medium vits-piper-ko_KR-kss-medium vits-piper-hi_IN-rohan-medium vits-piper-he_IL-saspeech-medium vits-piper-hy_AM-gor-medium vits-piper-th_TH-tsync2-medium vits-piper-id_ID-news_tts-medium vits-piper-ur_PK-fasih-medium vits-piper-fa_IR-reza_ibrahim-medium vits-piper-it_IT-serena-medium vits-piper-zh_CN-xiao_ya-medium vits-piper-is_IS-steinn-medium vits-piper-el_GR-joy-medium vits-piper-cy_GB-bu_tts-medium vits-piper-kk_KZ-iseke-x_low vits-piper-te_IN-venkatesh-medium vits-mms-deu; do
  [ -d "$m" ] && continue
  code=$(curl -sSL -m 600 -w "%{http_code}" -o "$m.tar.bz2" "$B/$m.tar.bz2")
  if [ "$code" = "200" ]; then sz=$(stat -c %s "$m.tar.bz2"); tar xjf "$m.tar.bz2" && rm "$m.tar.bz2"; echo "OK $m $sz"; else rm -f "$m.tar.bz2"; echo "FAIL $m $code"; fi
done
A=https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models
for m in sherpa-onnx-whisper-base sherpa-onnx-whisper-tiny; do
  [ -d "$m" ] && continue
  code=$(curl -sSL -m 900 -w "%{http_code}" -o "$m.tar.bz2" "$A/$m.tar.bz2")
  if [ "$code" = "200" ]; then sz=$(stat -c %s "$m.tar.bz2"); tar xjf "$m.tar.bz2" && rm "$m.tar.bz2"; echo "OK $m $sz"; break; else rm -f "$m.tar.bz2"; echo "FAIL $m $code"; fi
done
echo DONE
