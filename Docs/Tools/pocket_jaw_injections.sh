#!/usr/bin/env bash
# 三档注入负对照 + 逐字节还原（每档必须实测红）。
set -u
SIM="Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs"
OUT="E:/Unity_Deve/_d9r_out/v7"
UNITY="E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe"

BK=$(mktemp)
cp "$SIM" "$BK"
SHA0=$(sha1sum "$SIM" | cut -d' ' -f1)
echo "BASE sha1=$SHA0"

restore_and_verify () {
  cp "$BK" "$SIM"
  local now
  now=$(sha1sum "$SIM" | cut -d' ' -f1)
  if [ "$now" = "$SHA0" ]; then echo "RESTORED ok sha1=$now"; else echo "RESTORE MISMATCH now=$now want=$SHA0"; fi
}

run_case () {
  local tag="$1" testfilter="$2"
  "$UNITY" -batchmode -projectPath "E:/Unity_Deve/Unity_Project/Billiards game" \
    -runTests -testPlatform EditMode -testFilter "$testfilter" \
    -testResults "$OUT/inj_$tag.xml" -logFile "$OUT/inj_$tag.log" >/dev/null 2>&1
  echo "  exit=$?"
  python -c "
import re,sys
d=open(r'$OUT/inj_$tag.xml',encoding='utf-8',errors='replace').read()
g=re.search(r'<test-run[^>]*total=\"(\d+)\" passed=\"(\d+)\" failed=\"(\d+)\"', d)
print('  total/passed/failed =', g.groups() if g else 'NO XML')
for m in re.finditer(r'<test-case[^>]*?name=\"((?:T|Sixteen|D)[^\"]*)\"[^>]*?result=\"Failed\"[^>]*>(.*?)</test-case>', d, re.S):
    msg=re.search(r'<message><!\[CDATA\[(.*?)\]\]></message>', m.group(2), re.S)
    t=(msg.group(1).strip() if msg else '').split(chr(10))
    print('  RED:', m.group(1), '|', ' / '.join(x.strip() for x in t[:2]))
"
}

# ── V1：边界回退到历史值 captureRadius（口径回退）────────────────────
echo "== V1：边界回退到 PoolConstants.PocketCaptureRadius =="
python - "$SIM" <<'PY'
import io,sys
p=sys.argv[1]
s=io.open(p,encoding="utf-8",newline="").read()
a="if (rho > _pocketOpeningRadius[p])"
assert a in s
s=s.replace(a,"if (rho > PoolConstants.PocketCaptureRadius)",1)
io.open(p,"w",encoding="utf-8",newline="").write(s)
PY
run_case v1 "PoolSimulatorPocketCaptureTests"
restore_and_verify

# ── V2：第 2 档不冻结（只 return）⇒ 球离台消失 / 飞台 ────────────────
echo "== V2：第 2 档不冻结（越界球留在场外）=="
python - "$SIM" <<'PY'
import io,sys
p=sys.argv[1]
s=io.open(p,encoding="utf-8",newline="").read()
a="""                    PocketJawCrossingCount++;
                    _balls[index].velocity = Vector2.zero;
                    _frozen[index] = true;
                    return;"""
assert a in s
s=s.replace(a,"""                    PocketJawCrossingCount++;
                    return;""",1)
io.open(p,"w",encoding="utf-8",newline="").write(s)
PY
run_case v2 "PoolSimulatorPocketCaptureTests"
restore_and_verify

# ── V3：去掉静止补判 ⇒ 球挂在袋口 ───────────────────────────────────
echo "== V3：去掉静止补判（TableAtRest 上升沿钩子）=="
python - "$SIM" <<'PY'
import io,sys
p=sys.argv[1]
s=io.open(p,encoding="utf-8",newline="").read()
a="""            if (TableAtRest && !wasAtRest)
            {
                DropBallsRestingInsidePocketMouths();
            }"""
assert a in s
s=s.replace(a,"",1)
io.open(p,"w",encoding="utf-8",newline="").write(s)
PY
run_case v3 "PoolSimulatorPocketCaptureTests"
restore_and_verify

# ── V4：恒等式改成"由和推导" ⇒ T39b 白盒守卫应红 ────────────────────
echo "== V4：PocketProximityEvents 由 Captured+Jaw 推导 =="
python - "$SIM" <<'PY'
import io,sys
p=sys.argv[1]
s=io.open(p,encoding="utf-8",newline="").read()
a="""                // ★ 两计数各自独立累加（不得由另两者相加得到 —— 否则"归类完备"判据恒真）。
                PocketProximityEvents++;"""
assert a in s
s=s.replace(a,"""                PocketProximityEvents = CapturedBallCount + PocketJawCrossingCount + 1;""",1)
io.open(p,"w",encoding="utf-8",newline="").write(s)
PY
run_case v4 "PoolSimulatorPocketIndependenceTests"
restore_and_verify

rm -f "$BK"
echo "ALL DONE"
