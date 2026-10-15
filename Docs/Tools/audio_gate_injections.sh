#!/usr/bin/env bash
# T118 注入负对照：把 strike.wav 相对**现值** −3 dB ⇒ `T118` 的 (b) 与 (d) 应**实测红**；
# 实测后**逐字节还原**（sha1 前后一致）。用法：bash Docs/Tools/audio_gate_injections.sh
#
# ⚠ 两条既有教训（照做）：
#  ① 还原**一律用 `cp` 备份**，**绝不用 `git checkout`**（工作区改动未提交时它会连修复一起丢掉）。
#  ② 跑批前确认编辑器已关、无 `Temp/UnityLockfile`（有实例占用 ⇒ 批处理退 21）。
set -u
UNITY="E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe"
PROJ="E:/Unity_Deve/Unity_Project/Billiards game"
OUT="E:/Unity_Deve/_d9r_out/aud_v1"
STRIKE="$PROJ/Assets/_Project/Audio/SFX/strike.wav"
mkdir -p "$OUT"

BK=$(mktemp -d)
cp "$STRIKE" "$BK/strike.wav"
S1=$(sha1sum "$STRIKE" | cut -d' ' -f1)
echo "BASE strike sha1=$S1"

echo "== 注入：strike −3 dB（只碰 data 块，头不变）=="
python - "$STRIKE" <<'PY'
import sys, struct
p = sys.argv[1]
raw = bytearray(open(p, 'rb').read())
pos = 12
off = ln = None
while pos + 8 <= len(raw):
    cid = bytes(raw[pos:pos + 4]); cs = struct.unpack_from('<I', raw, pos + 4)[0]; body = pos + 8
    if cid == b'data':
        off, ln = body, cs
    pos = body + cs + (cs & 1)
n = ln // 2
s = list(struct.unpack_from('<%dh' % n, raw, off))
f = 10.0 ** (-3.0 / 20.0)
s = [max(-32768, min(32767, int(round(x * f)))) for x in s]
struct.pack_into('<%dh' % n, raw, off, *s)
open(p, 'wb').write(raw)
print("injected -3 dB ->", p)
PY

"$UNITY" -batchmode -projectPath "$PROJ" -runTests -testPlatform EditMode \
  -testFilter T118_AssetLevelGate_And_LayerPureLogic \
  -testResults "$OUT/inj_strike.xml" -logFile "$OUT/inj_strike.log" >/dev/null 2>&1
echo "exit=$?"
python -c "
import re
d=open(r'$OUT/inj_strike.xml',encoding='utf-8',errors='replace').read()
g=re.search(r'<test-run[^>]*total=\"(\d+)\" passed=\"(\d+)\" failed=\"(\d+)\"', d)
print('total/passed/failed =', g.groups() if g else 'NO XML')
for m in re.finditer(r'<test-case[^>]*?name=\"([^\"]+)\"[^>]*?result=\"Failed\"[^>]*>(.*?)</test-case>', d, re.S):
    msg=re.search(r'<message><!\[CDATA\[(.*?)\]\]></message>', m.group(2), re.S)
    t=(msg.group(1).strip() if msg else '').split(chr(10))
    print('RED:', m.group(1))
    for x in t[:8]: print('    ', x.strip())
"

cp "$BK/strike.wav" "$STRIKE"
S2=$(sha1sum "$STRIKE" | cut -d' ' -f1)
if [ "$S2" = "$S1" ]; then echo "RESTORED ok sha1=$S2"; else echo "!! RESTORE MISMATCH now=$S2 want=$S1"; fi
rm -rf "$BK" 2>/dev/null || true
echo "DONE"
