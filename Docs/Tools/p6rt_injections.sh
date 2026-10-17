#!/usr/bin/env bash
# **P6-Runtime 的注入负对照（7 条）** —— 逐条独立命令、每条一个独立备份目录（**在仓外**）、
# apply 前比对基线 sha1、跑完**立即**还原并回读 sha1 断言。
#
# ⚠ 四条纪律都是本项目用事故换来的，不是风格偏好：
#  ① **备份必须在仓外**：仓内 `Temp/` 会被 Unity/跑批清空 —— 已出过一次真事故。
#  ② **apply 之前必须比对基线 sha1**：若目标文件已被别人改过，拿过期备份还原会把他的改动**静默回滚**。
#     基线不符 ⇒ **让路（退 2）**，绝不动手。
#  ③ **还原后回读 sha1**：还原是"复制粘贴"，不校验就等于没还原（注入态残留是最坏的收尾）。
#     ⇒ 本脚本另加 `trap ... EXIT/INT/TERM` 兜底：**任何非正常退出都会尝试还原**。
#  ④ **注入态的结果 XML 必须当轮就留档到仓外**：`unity_batch.sh` 每次都先删旧 XML ⇒
#     不留档就没有"注入那一轮"的独立证据。
#
# ⚠ **基线不写死字面量**（这是本脚本与 `p6ui_injections.sh` 的唯一结构差异）：
#   本轮改完源码基线就变了 ⇒ 用「**首次运行写入 `baseline.sha1` / 之后运行时读取**」：
#     · 缺条目 ⇒ 把**当前** sha1 记为基线（打印 `首次记录`）；
#     · 有条目且不符 ⇒ **退 2 让路**（若确属预期的基线更新，删掉 `baseline.sha1` 重新记录）。
#
# ⚠ **锁 owner 参数化**（不硬编码名字）：第 2 个参数，或环境变量 `P6RT_LOCK_OWNER`。
#   本脚本**只断言**（不 acquire/release）—— 由调用方先 `batch_lock.sh acquire <owner>`。
#
# 用法：
#   bash Docs/Tools/batch_lock.sh acquire <owner>
#   bash Docs/Tools/p6rt_injections.sh <tag> <owner>
#
# tag（6 条 + 1 条 QA Major-2 专属）：
#   settle-unwire          ① 删 `Advance` 里的 `SettleShotIfAtRest();`          ⇒ T145 ⑨ 红
#   rest-gate-drop         ② 去掉静止门                                         ⇒ T145 ⑨ 红
#   settlement-gate-break  ③ 结算闸门改坏（消费结算卡时清 MatchOver）          ⇒ UI T146(d③) 红（+ T145 ⑤ 连带）
#   callpocket-gate-drop   ④ 删「未指定袋 ⇒ 拒杆」那道门                        ⇒ T145 ③ 红（+ T147 连带）
#   eightpocket-freeze     ⑤ `ExtractFacts` 里把 `eightBallPocket` 写死 -1      ⇒ T145 ② 红
#   ai-callpocket-drop     ⑥ 删「AI 打黑 8 自己指定袋」那一步                    ⇒ T145 (AI①) 红
#   ai-never-launch        ⑦ `TryLaunchAiShot` 永不启动                         ⇒ T145 (AI②)「前提不成立」红
#   ai-turn-player-guard-drop ⑧ 去掉「玩家回合不出手」守卫                      ⇒ T145 (AI驱动③) 红
#   ai-turn-rest-gate-drop    ⑨ 去掉「台面静止」守卫                            ⇒ T145 (AI驱动②b) 红
#   ai-turn-yield-drop        ⑩ 删掉「AI 无法出杆 ⇒ 让位」的出路                ⇒ T145 (AI驱动⑤) 红（= 会卡死）
set -uo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PY="${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}"
TAG="${1:?用法：p6rt_injections.sh <tag> <lock-owner>}"
LOCK_OWNER="${2:-${P6RT_LOCK_OWNER:-}}"
if [ -z "$LOCK_OWNER" ]; then
  echo "⛔ 必须给锁 owner（第 2 个参数，或环境变量 P6RT_LOCK_OWNER）—— 本脚本不硬编码持有者名字。" >&2
  exit 2
fi

OUT_ROOT="${TEMP:-/tmp}/p6rt_inj"
BASE_FILE="$OUT_ROOT/baseline.sha1"
OUT="$OUT_ROOT/inj_$TAG"

# ── tag → 目标文件（多个 tag 可指向同一文件；基线按"文件"记，不按 tag）──────────
case "$TAG" in
  settle-unwire|rest-gate-drop|settlement-gate-break|callpocket-gate-drop|ai-callpocket-drop|ai-never-launch|ai-turn-player-guard-drop|ai-turn-rest-gate-drop|ai-turn-yield-drop|p7-group-drop|p7-ballinhand-drop|p7-ai-one-shot|p7-isbreak-never-clear|p7-ai-autoplace-drop)
                    REL="Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs" ;;
  eightpocket-freeze|p7-no-pot-turn)
                    REL="Assets/_Project/Scripts/Runtime/Rules/EightBallRules.cs" ;;
  p7-stick-follow)
                    REL="Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs" ;;
  *) echo "未登记的 tag：$TAG（见本文件头部的 tag 列表）" >&2; exit 2 ;;
esac

TARGET="$PROJECT/$REL"
sha1_of() { sha1sum "$1" | cut -d' ' -f1; }

# **源码树签名**：`Assets/**/*.cs` 的 sha1 集合再取 sha1。
# ⚠ 为什么需要它：跑批**只**受锁保护，而别的成员可以**不持锁就改源码**（本项目实测发生过）
#   ⇒ 若跑批前后树签名不同，说明这一轮编的是"移动靶"，**结果不得被引用**。
tree_sig() {
  ( cd "$PROJECT" && find Assets -type f -name '*.cs' -print0 | sort -z | xargs -0 sha1sum ) \
    | sha1sum | cut -d' ' -f1
}

# ── **调用方自己的待提交文件**（交给共享预检）────────────────────────────────
# ⚠ **噪声白名单只在 `Docs/Tools/batch_precheck.sh` 里维护一份**（`p6ui_*` 也调它）——
#   这里只声明"**我自己的**改动"，别把噪声也抄进来。
EXPECT_ARGS=()
for _p in \
  "Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs" \
  "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs" \
  "Assets/_Project/Scripts/Runtime/Rules/EightBallRules.cs" \
  "Assets/_Project/Scripts/Runtime/Rules/EightBallAi.cs" \
  "Assets/_Project/Tests/EditMode/PhysicsTestDriveLaunchStateTests.cs" \
  "Assets/_Project/Tests/EditMode/EightBallAiTests.cs" \
  "Docs/Tools/p6rt_*.sh" \
  "Docs/Tools/p6rt_*.py" \
  "Docs/35-C_AI对战与8球规则_spec.md" ; do
  EXPECT_ARGS+=(--expect "$_p")
done

# ── **已知的、对方在途的**文件（`--accept-foreign`）──────────────────────────
# ⚠ 语义与 `--expect` **不同**（别混）：
#   · `--expect`        = 「**这些是我要提交的**」
#   · `--accept-foreign`= 「**这些是别人的、我知道且在途**」⇒ 放行但**留痕**
# 默认**空**（严格）。两路并行时用 `BATCH_ACCEPT_FOREIGN="<glob> <glob>"` 显式放行，
# 预检会打「⚠ 已接受外来在途改动：…」一行，被本脚本落进 `run.log` ⇒ 可审计。
ACCEPT_ARGS=()
if [ -n "${BATCH_ACCEPT_FOREIGN:-}" ]; then
  for _g in ${BATCH_ACCEPT_FOREIGN}; do
    ACCEPT_ARGS+=(--accept-foreign "$_g")
  done
fi

mkdir -p "$OUT_ROOT"

# ── ⓪ **持锁断言**：结果只认"自己持锁窗口内的那次" ─────────────────────────
LOCK="${TEMP:-/tmp}/billiards_batch/BATCH_LOCK"
now() { date +%H:%M:%S; }
if [ ! -f "$LOCK" ] || [ "$(cut -d'|' -f1 < "$LOCK")" != "$LOCK_OWNER" ]; then
  echo "⛔ [$TAG] 锁不在 $LOCK_OWNER 手上（当前：$(cat "$LOCK" 2>/dev/null || echo '无锁')）⇒ **让路，不跑批**" >&2
  exit 4
fi

# **持锁窗口的"起点"由锁自己记着**（`owner|pid|起始时刻`）——本脚本**不 acquire/release**（由调用方做），
# 所以只能"读起点 + 打自己的跑批起止"；`release` 时刻由调用方在脚本返回后打印。
LOCK_STARTED="$(cut -d'|' -f3 < "$LOCK" 2>/dev/null || echo '?')"
echo "[$TAG] 持锁窗口：acquire=${LOCK_STARTED}（取自 BATCH_LOCK）　脚本启动=$(now)"

# ── ① 基线：**首跑记录 / 之后读取**（不写字面量）──────────────────────────────
[ -f "$BASE_FILE" ] || : > "$BASE_FILE"
CURRENT="$(sha1_of "$TARGET")"
RECORDED="$(awk -v r="$REL" '$2==r {print $1; exit}' "$BASE_FILE")"
if [ -z "$RECORDED" ]; then
  printf '%s %s\n' "$CURRENT" "$REL" >> "$BASE_FILE"
  BASE="$CURRENT"
  echo "[$TAG] 基线**首次记录**：$BASE  $REL（存 $BASE_FILE）"
elif [ "$CURRENT" != "$RECORDED" ]; then
  echo "⛔ [$TAG] 基线不符：$REL 当前 $CURRENT ≠ 记录 $RECORDED" >&2
  echo "   ⇒ 目标文件已被改过（可能是我这一轮自己的改动，也可能是别人）—— **让路，不注入**。" >&2
  echo "   ⇒ 若这是**预期的基线更新**：删掉 $BASE_FILE 让脚本重新记录，再跑。" >&2
  exit 2
else
  BASE="$RECORDED"
  echo "[$TAG] 基线（读自 $BASE_FILE）：$BASE  $REL"
fi

# ── ①.5 备份（仓外）────────────────────────────────────────────────────────
mkdir -p "$OUT"
cp "$TARGET" "$OUT/backup" || { echo "⛔ 备份失败" >&2; exit 2; }
echo "[$TAG] 备份 -> $OUT/backup"

# ── ② 兜底还原（任何非正常退出都不许停在注入态）──────────────────────────────
APPLIED=0
cleanup() {
  if [ "$APPLIED" = "1" ]; then
    echo "⚠ [$TAG] 非正常退出 ⇒ 兜底还原" >&2
    "$PY" "$PROJECT/Docs/Tools/p6rt_patch.py" restore "$TAG" >/dev/null 2>&1 || true
    APPLIED=0
  fi
}
trap cleanup EXIT INT TERM

# ── ③ 注入 ────────────────────────────────────────────────────────────────
"$PY" "$PROJECT/Docs/Tools/p6rt_patch.py" apply "$TAG" || { echo "⛔ 注入失败" >&2; exit 2; }
APPLIED=1
INJECTED="$(sha1_of "$TARGET")"
if [ "$INJECTED" = "$BASE" ]; then
  echo "⛔ [$TAG] 注入后 sha1 未变 ⇒ 注入是**空转**（锚点漂了？）" >&2
  exit 2
fi
echo "[$TAG] 注入态 sha1 = $INJECTED（基线 $BASE）"

# ── ③.5 **共享前置检查（紧贴起跑）** ────────────────────────────────────────
# ⚠ 「查过一次」不算干净 —— 别人的未提交改动可能在**你上一次检查之后**才落盘
#   （实测：我 11:04:13 查过，另一位 11:06:47 才保存，我 11:06:50 起跑被污染，只差 2 分 34 秒）。
#   ⇒ 检查放在**紧贴跑批**这里，且**机械化**（白名单只有一份，见 `batch_precheck.sh`）。
if ! bash "$PROJECT/Docs/Tools/batch_precheck.sh" "${EXPECT_ARGS[@]}" ${ACCEPT_ARGS[@]+"${ACCEPT_ARGS[@]}"}; then
  echo "⛔ [$TAG] 前置检查未通过（白名单外有**别人的未提交改动**）⇒ **不跑批**；" >&2
  echo "   已注入的内容会由 trap 立即还原。确认可放行：BATCH_ACCEPT_DIRTY=1（会留痕）。" >&2
  exit 7
fi

# ── ④ 跑全量（结果只认本次；跑批脚本自己先删旧 XML 并打印 mtime）───────────
SIG_BEFORE="$(tree_sig)"
echo "[$TAG] 跑批前源码树签名 = $SIG_BEFORE"
echo "[$TAG] 跑批开始 = $(now)"
bash "$PROJECT/Docs/Tools/unity_batch.sh" tests > "$OUT/run.log" 2>&1
RC=$?
SIG_AFTER="$(tree_sig)"
TREE_CHANGED=0
if [ "$SIG_AFTER" != "$SIG_BEFORE" ]; then
  echo "⛔ [$TAG] **跑批期间源码树被改过**（$SIG_BEFORE → $SIG_AFTER）⇒ 这一轮编的是移动靶，结果**不得引用**" >&2
  TREE_CHANGED=1
fi

# 跑批结束后**再断言一次锁**：TTL 内没人能合法接管，但**别人可以显式 release**
LOCK_OWNER_AFTER="$(cut -d'|' -f1 < "$LOCK" 2>/dev/null || echo missing)"
echo "[$TAG] 跑批窗口内锁持有者（跑完时）= $LOCK_OWNER_AFTER" | tee -a "$OUT/run.log"
LOCK_LOST=0
if [ "$LOCK_OWNER_AFTER" != "$LOCK_OWNER" ]; then
  echo "⛔ [$TAG] 跑批期间锁被改过（现在：$LOCK_OWNER_AFTER）⇒ **本轮结果作废**，需重跑" >&2
  LOCK_LOST=1
fi

# 纪律④：**先把注入态产物拷到仓外留档**，再还原（否则会被下一轮跑批删掉）
XML="${TEMP:-/tmp}/billiards_batch/editmode_results.xml"
if [ -f "$XML" ]; then
  cp "$XML" "$OUT/injected_results.xml"
  echo "[$TAG] 注入态结果已留档 -> $OUT/injected_results.xml"
else
  echo "⛔ [$TAG] 注入态没有结果 XML（跑批没落盘）—— 不得引用任何\"红名单\"" >&2
fi

# ── ⑤ **立即还原** + 回读 sha1 断言 ────────────────────────────────────────
"$PY" "$PROJECT/Docs/Tools/p6rt_patch.py" restore "$TAG"
APPLIED=0
RESTORED="$(sha1_of "$TARGET")"
echo "[$TAG] unity rc=$RC　注入态 $INJECTED　还原后 $RESTORED"
if [ "$RESTORED" != "$BASE" ]; then
  echo "⛔ [$TAG] **还原失败**：$RESTORED ≠ $BASE —— 处置：从 $OUT/backup 手工覆盖并复查" >&2
  exit 3
fi
if grep -q "INJ-P6RT" "$TARGET"; then
  echo "⛔ [$TAG] 还原后仍有 INJ-P6RT 残留" >&2
  exit 3
fi
echo "[$TAG] ✓ 还原后 sha1 与基线**逐字节一致**，且 INJ 残留 = 0"
echo "[$TAG] 脚本结束 = $(now)（`release` 由调用方在脚本返回后立即执行）"

# ── ⑥ 汇总（**判成败只认 XML 的 failed 与 mtime**，不看 unity rc）──────────────
grep -E "results mtime" "$OUT/run.log" || true
if [ -f "$OUT/injected_results.xml" ]; then
  "$PY" - "$OUT/injected_results.xml" <<'PYEOF'
import re, sys
path = sys.argv[1]
text = open(path, encoding="utf-8", errors="replace").read()
run = re.search(r"<test-run[^>]*>", text)
print("[xml]", run.group(0) if run else "(no <test-run>)")
PYEOF
fi

if [ "$LOCK_LOST" = "1" ]; then
  echo "⛔ [$TAG] 结果作废（跑批期间锁被改过）—— 请重跑" >&2
  exit 5
fi
if [ "$TREE_CHANGED" = "1" ]; then
  echo "⛔ [$TAG] 结果作废（跑批期间源码树被改过）—— 请重跑" >&2
  exit 6
fi
exit 0
