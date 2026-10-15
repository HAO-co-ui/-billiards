#!/usr/bin/env bash
# 本轮"贴库/滑墙/穿模"三修的四档注入负对照 + 逐字节还原（每档必须实测红）。
# 用法：bash Docs/Tools/feel_fix_injections.sh
#
# ⚠ 两条教训（都在本轮亲身踩过）：
#  ① 还原**一律用 `cp` 备份**，**绝不用 `git checkout`** —— 工作区改动尚未提交时
#     `git checkout -- <file>` 会把未提交的编辑一起丢掉（本轮三处修复曾被自己的脚本回滚）。
#  ② 含 heredoc 字面量的补丁片段**不要**内联进本脚本（同名分隔符会让外层提前结束）
#     ⇒ 补丁放在同目录的 `feel_fix_patch.py` 里。
set -u
OUT="E:/Unity_Deve/_d9r_out/v8"
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
    print('  RED:', m.group(1), '|', ' / '.join(x.strip() for x in t[:2]))
"
}

restore_and_verify () {
  local file="$1" bak="$2" want="$3"
  cp "$bak" "$file"
  local now
  now=$(sha1sum "$file" | cut -d' ' -f1)
  if [ "$now" = "$want" ]; then echo "  RESTORED ok sha1=$now"; else echo "  RESTORE MISMATCH now=$now want=$want"; fi
}

CUE="Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs"
VFX="Assets/_Project/Scripts/Runtime/Physics/PoolVfxView.cs"
SIM="Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs"
BK=$(mktemp -d)
cp "$CUE" "$BK/cue"; cp "$VFX" "$BK/vfx"; cp "$SIM" "$BK/sim"
S1=$(sha1sum "$CUE" | cut -d' ' -f1); S2=$(sha1sum "$VFX" | cut -d' ' -f1); S3=$(sha1sum "$SIM" | cut -d' ' -f1)
echo "BASE sha1 cue=$S1 vfx=$S2 sim=$S3"

echo "== V1：抬升角退回固定 4 度（穿模原状）=="
python "$HERE/feel_fix_patch.py" v1 "$CUE"
run_case v1 "T104_AimAssistVisibility_AndFoulFeedback"
restore_and_verify "$CUE" "$BK/cue" "$S1"

echo "== V2：入袋动画退回原地垂直下沉（滑墙原状）=="
python "$HERE/feel_fix_patch.py" v2 "$VFX"
run_case v2 "T92_VfxPose_ClosedForm"
restore_and_verify "$VFX" "$BK/vfx" "$S2"

echo "== V3：静止补判半径放宽 2 倍（边界守卫）=="
python "$HERE/feel_fix_patch.py" v3 "$SIM"
run_case v3 "T108_ClassificationIdentity_AndAtRestMouthDrop"
restore_and_verify "$SIM" "$BK/sim" "$S3"

echo "== V4：洞口中心直接用喉心（表现层落点守卫）=="
python "$HERE/feel_fix_patch.py" v4 "$SIM"
run_case v4 "T108_ClassificationIdentity_AndAtRestMouthDrop"
restore_and_verify "$SIM" "$BK/sim" "$S3"

echo "ALL DONE"
