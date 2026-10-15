#!/usr/bin/env bash
# 第四轮"蓄力穿模 + 袋角吸住"两修的**注入负对照**：每档必须**实测红**，且逐字节还原。
# 用法：bash Docs/Tools/feel_fix2_injections.sh
#
# ⚠ 两条教训（都在前几轮亲身踩过，必须照做）：
#  ① 还原**一律用 `cp` 备份**，**绝不用 `git checkout`** —— 工作区改动尚未提交时
#     `git checkout -- <file>` 会把刚写的修复一起丢掉。
#  ② 含 heredoc/中文缩进 的补丁片段**不要**内联进本脚本 ⇒ 补丁放同目录的 `feel_fix2_patch.py`。
set -u
OUT="E:/Unity_Deve/_d9r_out/v10"
UNITY="E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe"
PROJ="E:/Unity_Deve/Unity_Project/Billiards game"
HERE="$(dirname "$0")"
mkdir -p "$OUT"

run_case () {
  local tag="$1" filter="$2"
  "$UNITY" -batchmode -projectPath "$PROJ" -runTests -testPlatform EditMode \
    -testFilter "$filter" -testResults "$OUT/inj_$tag.xml" -logFile "$OUT/inj_$tag.log" >/dev/null 2>&1
  echo "  exit=$?"
  python -c "
import re
d=open(r'$OUT/inj_$tag.xml',encoding='utf-8',errors='replace').read()
g=re.search(r'<test-run[^>]*total=\"(\d+)\" passed=\"(\d+)\" failed=\"(\d+)\"', d)
print('  total/passed/failed =', g.groups() if g else 'NO XML')
for m in re.finditer(r'<test-case[^>]*?name=\"((?:T|Sixteen|D)[^\"]*)\"[^>]*?result=\"Failed\"[^>]*>(.*?)</test-case>', d, re.S):
    msg=re.search(r'<message><!\[CDATA\[(.*?)\]\]></message>', m.group(2), re.S)
    t=(msg.group(1).strip() if msg else '').split(chr(10))
    print('  RED:', m.group(1), '|', ' / '.join(x.strip() for x in t[:3]))
"
}

restore_and_verify () {
  local file="$1" bak="$2" want="$3"
  cp "$bak" "$file"
  local now
  now=$(sha1sum "$file" | cut -d' ' -f1)
  if [ "$now" = "$want" ]; then echo "  RESTORED ok sha1=$now"; else echo "  RESTORE MISMATCH now=$now want=$want"; fi
}

SIM="Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs"
CUE="Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs"
BK=$(mktemp -d)
cp "$SIM" "$BK/sim"; cp "$CUE" "$BK/cue"
S1=$(sha1sum "$SIM" | cut -d' ' -f1); S2=$(sha1sum "$CUE" | cut -d' ' -f1)
echo "BASE sha1 sim=$S1 cue=$S2"

echo "== V1：去掉袋角接触的接近判据（撞了不弹 原状）⇒ T109 应红 =="
python "$HERE/feel_fix2_patch.py" v1 "$SIM"
run_case v1 "T109_JawImpact_MustNotStickAtJaw"
restore_and_verify "$SIM" "$BK/sim" "$S1"

echo "== V2：杆尖后拉改回水平方向（蓄力穿模 原状）⇒ T104 应红 =="
python "$HERE/feel_fix2_patch.py" v2 "$CUE"
run_case v2 "T104_AimAssistVisibility_AndFoulFeedback"
restore_and_verify "$CUE" "$BK/cue" "$S2"

rm -rf "$BK"
echo "ALL DONE"
