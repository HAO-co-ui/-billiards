#!/usr/bin/env bash
# **P6-UI 的注入负对照（三条）** —— 逐条独立命令、每条一个独立备份目录（**在仓外**）、
# apply 前比对基线 sha1、跑完**立即**还原并回读 sha1 断言。
#
# ⚠ 四条纪律都是本项目用事故换来的，不是风格偏好：
#  ① **备份必须在仓外**：仓内 `Temp/` 会被 Unity/跑批清空 —— 上一位 UI 成员就在这里出过一次真事故。
#  ② **apply 之前必须比对基线 sha1**：若目标文件已被别人改过（另一位成员正在改 `Runtime/Physics/**`），
#     拿过期备份还原会把他的改动**静默回滚**。基线不符 ⇒ **让路（退 2）**，绝不动手。
#  ③ **还原后回读 sha1**：还原是"复制粘贴"，不校验就等于没还原（注入态残留是最坏的收尾）。
#  ④ **注入态的结果 XML 必须当轮就留档到仓外**：跑批脚本每次都先删旧 XML ⇒
#     不留档就没有"注入那一轮"的独立证据（上一位 UI 成员正因此只能引用 `.log`）。
#
# 用法：
#   bash Docs/Tools/p6ui_injections.sh form-swap
#   bash Docs/Tools/p6ui_injections.sh input-gate
#   bash Docs/Tools/p6ui_injections.sh one-pop
set -uo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PY="${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}"
TAG="${1:?用法：p6ui_injections.sh <tag> [lock-owner]}"

# ⚠ **锁的 owner 必须参数化**（QA 修复轮 C11）：写死 `home-ui2` 会让"换个人重跑"必须改脚本，
#   而改脚本 = 改证据产生器本身。优先级：第 2 个位置参数 > 环境变量 `P6UI_OWNER` > 默认 `home-ui2`。
OWNER="${2:-${P6UI_OWNER:-home-ui2}}"
OUT="${TEMP:-/tmp}/homeui_p6/inj_$TAG"

# 各条注入的目标文件与**基线 sha1**（跑批前实测记录；apply 前必须逐字节一致）
case "$TAG" in
  form-swap)        REL="Assets/_Project/Scripts/Runtime/UI/MatchResultCopy.cs"
                    BASE="938c94018b1eebcc0c67bb9c5fbf115faecbeec0" ;;
  input-gate)       REL="Assets/_Project/Scripts/Runtime/Physics/PointerPowerController.cs"
                    BASE="423c33b20da101472ab14eaa78d302186650c624" ;;
  one-pop)          REL="Assets/_Project/Scripts/Runtime/UI/PracticeResultView.cs"
                    BASE="c58a90a415bcd66b14ee41184baddc76bf8c128c" ;;
  record-drop)      REL="Assets/_Project/Scripts/Runtime/UI/PracticeResultView.cs"
                    BASE="c58a90a415bcd66b14ee41184baddc76bf8c128c" ;;
  record-overwrite) REL="Assets/_Project/Scripts/Runtime/UI/PracticeSessionStore.cs"
                    BASE="8d702fea291e7008e5b5ab169435486117e8ed8c" ;;
  ai-turn-drop)     REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudState.cs"
                    BASE="58499ac9f0de8539ddd1f56fd2a894fc5d505b08" ;;
  callpocket-always) REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudView.cs"
                    BASE="528208bb3bc7250f3421d01bc468afdd253d8b53" ;;
  callpocket-silent) REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudView.cs"
                    BASE="528208bb3bc7250f3421d01bc468afdd253d8b53" ;;
  # ── QA 修复轮（"有牙化"六条；每条对应一处被改写的判据）────────────────────
  p7-default-drift)  REL="Assets/_Project/Scripts/Runtime/Rules/EightBallAi.cs"
                     BASE="ef54df10e31d1b80f9c20e91a9314eab2c88e950" ;;
  p7-tier-name)      REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudState.cs"
                     BASE="58499ac9f0de8539ddd1f56fd2a894fc5d505b08" ;;
  p7-color-single-source) REL="Assets/_Project/Scripts/Runtime/UI/PracticeResultView.cs"
                     BASE="c58a90a415bcd66b14ee41184baddc76bf8c128c" ;;
  p7-win-cause)      REL="Assets/_Project/Scripts/Runtime/UI/MatchResultCopy.cs"
                     BASE="938c94018b1eebcc0c67bb9c5fbf115faecbeec0" ;;
  p7-tier-order)     REL="Assets/_Project/Scripts/Runtime/UI/MatchSetup.cs"
                     BASE="6def82276f0ab88d1061d855b29f8774f26c0cd8" ;;
  p7-slot-count)     REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudLayout.cs"
                     BASE="b9dad47c28eaa5d44a65559b7b293e67f3481dfe" ;;
  # ── P6 第 4 小段（对手叫袋那一行）五条 ──────────────────────────────────
  p8-ai-called-always)      REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudState.cs"
                            BASE="b861e49a598d9a53f6549acdd03d5b697fb9e3f1" ;;
  p8-ai-called-no-guard)    REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudState.cs"
                            BASE="b861e49a598d9a53f6549acdd03d5b697fb9e3f1" ;;
  p8-ai-called-player-copy) REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudState.cs"
                            BASE="b861e49a598d9a53f6549acdd03d5b697fb9e3f1" ;;
  p8-ai-called-state-gate-drop) REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudState.cs"
                            BASE="b861e49a598d9a53f6549acdd03d5b697fb9e3f1" ;;
  p8-ai-called-view-feed)   REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudView.cs"
                            BASE="7e55e457ab09fde9d970390301533bbaa9a712ec" ;;
  # ── 字号整体放大（FontScale）一条 ──────────────────────────────────────────
  p9-font-scale-twice)      REL="Assets/_Project/Scripts/Runtime/UI/PracticeHudView.cs"
                            BASE="c23f0aeccd1ee602ed1ad1a1fe5f43d01d3573e4" ;;
  *) echo "未登记的 tag：$TAG" >&2; exit 2 ;;
esac

TARGET="$PROJECT/$REL"
sha1_of() { sha1sum "$1" | cut -d' ' -f1; }

# ── ⓪ **持锁断言**：结果只认"自己持锁窗口内的那次" ──────────────────────────
# ⚠ 本轮实测过一次真事故：跑批期间**锁被别人 `release` 掉**（`%TEMP%/billiards_batch/BATCH_LOCK.history`
#   里有 `释放 home-ui2|… by billiards-runtime`）⇒ 两个 Unity 会争同一个结果 XML = **假证据**。
#   故每次跑批前**机械断言**锁还在自己手上；不在 ⇒ 退 4（**让路**，不产出任何数字）。
LOCK="${TEMP:-/tmp}/billiards_batch/BATCH_LOCK"
if [ ! -f "$LOCK" ] || [ "$(cut -d'|' -f1 < "$LOCK")" != "$OWNER" ]; then
  echo "⛔ [$TAG] 锁不在 $OWNER 手上（当前：$(cat "$LOCK" 2>/dev/null || echo '无锁')）⇒ **让路，不跑批**" >&2
  exit 4
fi

mkdir -p "$OUT"

# ── ⓪' **共享预检（紧贴起跑）**：白名单外有"既非 --expect 也非 --accept-foreign"的改动 ⇒ **退 7**
# ⚠ **位置必须在 `apply` 之前**：本脚本**没有 `trap`** —— 若先注入再被拦下，就必须自己保证还原。
#   放在这里，窗口只多约 1 秒。
# ⚠ **白名单只有一份**（在 `batch_precheck.sh` 里），本脚本**不另写一套**，只调用。
# ⚠ 起因（真实事故）：`billiards-runtime` 11:04 查过 `git status` 是干净的，但 UI 侧 11:06:47
#   把一个**未跑过**的判据存进工作区，它 11:06:50 起跑 ⇒ 全量把半成品编了进去、红名单里多一条无关红。
#   ⇒ **「查过一次」不算干净 —— 检查会过期**。
EXPECT_ARGS=(--expect "Assets/_Project/Scripts/Runtime/UI/*.cs"
             --expect "Assets/_Project/Tests/EditMode/ClubUiTests.cs"
             --expect "Docs/Tools/p6ui_*"
             --expect "Docs/Tools/check_verify_dll.py")
ACCEPT_ARGS=(--accept-foreign "Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs"
             --accept-foreign "Assets/_Project/Scripts/Runtime/Rules/EightBall*.cs"
             --accept-foreign "Assets/_Project/Tests/EditMode/PhysicsTestDriveLaunchStateTests.cs"
             --accept-foreign "Assets/_Project/Tests/EditMode/EightBallAiTests.cs"
             --accept-foreign "Docs/Tools/p6rt_*"
             --accept-foreign "Docs/35-C_*.md")
if ! bash "$PROJECT/Docs/Tools/batch_precheck.sh" "${EXPECT_ARGS[@]}" "${ACCEPT_ARGS[@]}"; then
  echo "⛔ [$TAG] 跑批前置检查未通过（白名单外有既非 --expect 也非 --accept-foreign 的改动）⇒ **不跑批**" >&2
  exit 7
fi

# ── ① 备份（仓外）──────────────────────────────────────────────────────────
cp "$TARGET" "$OUT/backup"
echo "[$TAG] 备份 -> $OUT/backup"

# ── ② apply 前比对基线（不符 ⇒ 让路，绝不动别人正在改的文件）────────────────
CURRENT="$(sha1_of "$TARGET")"
if [ "$CURRENT" != "$BASE" ]; then
  echo "⛔ [$TAG] 基线不符：当前 $CURRENT ≠ 记录 $BASE" >&2
  echo "   ⇒ 目标文件已被改过（可能是另一位成员）—— **让路，不注入**。" >&2
  exit 2
fi

# ── ③ 注入 ────────────────────────────────────────────────────────────────
"$PY" "$PROJECT/Docs/Tools/p6ui_patch.py" apply "$TAG" || { echo "⛔ 注入失败" >&2; exit 2; }
INJECTED="$(sha1_of "$TARGET")"
if [ "$INJECTED" = "$BASE" ]; then
  echo "⛔ [$TAG] 注入后 sha1 未变 ⇒ 注入是**空转**（锚点漂了？）" >&2
  "$PY" "$PROJECT/Docs/Tools/p6ui_patch.py" restore "$TAG"
  exit 2
fi
echo "[$TAG] 注入态 sha1 = $INJECTED（基线 $BASE）"

# ── ④ 跑全量（结果只认本次；跑批脚本自己先删旧 XML 并打印 mtime）───────────
bash "$PROJECT/Docs/Tools/unity_batch.sh" tests > "$OUT/run.log" 2>&1
RC=$?

# ⚠ 跑批结束后**再断言一次锁**：TTL 内没人能合法接管，但**别人可以显式 release**
#   （本轮实测过）⇒ 锁若已不在我手上，这次的结果**不得被引用为有效证据**。
LOCK_OWNER_AFTER="$(cut -d'|' -f1 < "$LOCK" 2>/dev/null || echo missing)"
if [ "$LOCK_OWNER_AFTER" != "$OWNER" ]; then
  echo "⛔ [$TAG] 跑批期间锁被改过（现在：$LOCK_OWNER_AFTER）⇒ **本轮结果作废**，需重跑" >&2
  LOCK_LOST=1
fi
echo "[$TAG] 跑批窗口内锁持有者（跑完时）= $LOCK_OWNER_AFTER" | tee -a "$OUT/run.log"

# ⚠ 纪律④：**先把注入态产物拷到仓外留档**，再还原（否则会被下一轮跑批删掉）
XML="${TEMP:-/tmp}/billiards_batch/editmode_results.xml"
if [ -f "$XML" ]; then
  cp "$XML" "$OUT/injected_results.xml"
  echo "[$TAG] 注入态结果已留档 -> $OUT/injected_results.xml"
else
  echo "⛔ [$TAG] 注入态没有结果 XML（跑批没落盘）—— 不得引用任何\"红名单\"" >&2
fi

# ── ⑤ **立即还原** + 回读 sha1 断言 ────────────────────────────────────────
"$PY" "$PROJECT/Docs/Tools/p6ui_patch.py" restore "$TAG"
RESTORED="$(sha1_of "$TARGET")"
echo "[$TAG] unity rc=$RC　注入态 $INJECTED　还原后 $RESTORED"
if [ "$RESTORED" != "$BASE" ]; then
  echo "⛔ [$TAG] **还原失败**：$RESTORED ≠ $BASE　—— 处置：从 $OUT/backup 手工覆盖并复查" >&2
  exit 3
fi
echo "[$TAG] ✓ 还原后 sha1 与基线**逐字节一致**"
grep -E "results mtime" "$OUT/run.log" || true
if [ "${LOCK_LOST:-0}" = "1" ]; then
  echo "⛔ [$TAG] 结果作废（跑批期间锁被改过）—— 请重跑" >&2
  exit 5
fi
exit 0
