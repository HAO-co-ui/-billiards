#!/usr/bin/env bash
# Day 15「稳定性边界用例」两档注入负对照：每档必须**实测红**，且逐字节还原。
# 用法：bash Docs/Tools/day15_injections.sh
#
# ⚠ 两条既有教训（照做）：
#  ① 还原**一律用 `cp` 备份**，**绝不用 `git checkout`**（工作区改动未提交时它会连修复一起丢掉）。
#  ② 含 heredoc/中文的补丁片段**不要**内联进本脚本 ⇒ 补丁放同目录的 `day15_patch.py`。
set -u
OUT="E:/Unity_Deve/_d9r_out/v11"
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
for m in re.finditer(r'<test-case[^>]*?name=\"([^\"]+)\"[^>]*?result=\"Failed\"[^>]*>(.*?)</test-case>', d, re.S):
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
BK=$(mktemp -d)
cp "$SIM" "$BK/sim"
S1=$(sha1sum "$SIM" | cut -d' ' -f1)
echo "BASE sha1 sim=$S1"

echo "== V1：球-球重合即早退（同位静止两球永不分离 原状）⇒ T22b 应红 =="
python "$HERE/day15_patch.py" v1 "$SIM"
run_case v1 "T22b_CoincidentCentres_NoNaN_DeterministicSeparation"
restore_and_verify "$SIM" "$BK/sim" "$S1"

echo "== V2：滚阻清零（参数不生效）⇒ T114 应红 =="
python "$HERE/day15_patch.py" v2 "$SIM"
run_case v2 "T114_TuningMetrics_MonotoneAndClosedForm"
restore_and_verify "$SIM" "$BK/sim" "$S1"

rm -rf "$BK" 2>/dev/null || true
echo "ALL DONE"
