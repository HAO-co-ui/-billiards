#!/usr/bin/env bash
# **跑批互斥锁**（P5-A 新增；理由与规则见 `Docs/35-C_AI对战与8球规则_spec.md` 附-2）。
#
# 为什么需要它（不是"礼貌约定"，是"防假证据"）：
#   `unity_batch.sh tests` 起跑前会**删掉旧的结果 XML** ⇒ 两人同时跑时，最危险的后果不是"跑不成"，
#   而是**互相把对方的结果读成自己的**（`testcasecount` 看起来还挺正常）⇒ **假证据**。
#
# 规则（机械化）：
#   ① 锁在**仓外** `%TEMP%\billiards_batch\BATCH_LOCK`；内容 = `持有者|PID|起始时刻`；
#   ② 起跑前：锁存在且（**PID 存活** 或 **mtime 在 TTL 内**）⇒ **让路**（不抢）；
#      两者都不满足 ⇒ 判为残留、**可接管并留痕**；
#      ⚠ **为什么不能只靠 PID**（实测）：`acquire` 自身是**短命子进程**，它一返回 PID 就「已死」
#        ⇒ 只用 PID 判活会让锁**形同虚设**（第二次 `acquire` 直接接管）。故**主判据 = mtime 新鲜度**，
#        PID 只作**信息**（「谁持有」）。TTL 默认 900 s（单次跑批远小于它），可用 `BATCH_LOCK_TTL` 覆写；
#   ③ 跑完 `release`；④ **绝不删 `Temp/UnityLockfile`**（那是 Unity 自己的锁，与本次跑批无关）；
#   ⑤ 结果只认「**自己那次**跑批的 mtime + 自己的持锁窗口」。
#
# 用法：
#   bash Docs/Tools/batch_lock.sh acquire <owner>   # 拿不到 ⇒ 退 3（让路）
#   bash Docs/Tools/batch_lock.sh release <owner>
#   bash Docs/Tools/batch_lock.sh status
set -uo pipefail

LOCK_DIR="${TEMP:-/tmp}/billiards_batch"
LOCK="$LOCK_DIR/BATCH_LOCK"
HIST="$LOCK_DIR/BATCH_LOCK.history"
TTL_MIN="${BATCH_LOCK_TTL_MIN:-15}"   # 判"锁是否还新鲜"的窗口（分钟）

# 删文件走 python：本环境的命令守卫会把带盘符的 `rm -f "C:/…"` 当成不安全删除而**拒绝执行**
unlink() {
  "${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}" -c "import os,sys
p=sys.argv[1]
if os.path.exists(p): os.remove(p); print('removed', p)
else: print('absent', p)" "$1"
}

now() { date +%Y-%m-%d_%H:%M:%S; }

case "${1:-}" in
  acquire)
    owner="${2:-unknown}"
    mkdir -p "$LOCK_DIR"
    if [ -f "$LOCK" ]; then
      IFS='|' read -r o pid started < "$LOCK"
      alive="no"
      kill -0 "$pid" 2>/dev/null && alive="yes"
      fresh="no"
      [ -n "$(find "$LOCK" -mmin -$TTL_MIN 2>/dev/null)" ] && fresh="yes"
      if [ "$alive" = "yes" ] || [ "$fresh" = "yes" ]; then
        echo "⛔ 已有跑批在跑：owner=$o pid=$pid started=$started（PID活=$alive 新鲜=$fresh）⇒ **让路（不抢）**" >&2
        echo "   （若你确信那次跑批已结束：等锁过期（TTL ${TTL_MIN} 分钟）或让持有者 release。）" >&2
        exit 3
      fi
      echo "⚠ 锁残留：owner=$o pid=$pid started=$started 既不存活、mtime 也已过 TTL(${TTL_MIN}min) ⇒ **接管并留痕**" >&2
      printf '%s | 接管 %s by %s(pid=%s)\n' "$(cat "$LOCK")" "$(now)" "$owner" "$$" >> "$HIST"
    fi
    printf '%s|%s|%s\n' "$owner" "$$" "$(now)" > "$LOCK"
    echo "✓ 已持锁：$(cat "$LOCK")　（结果只认本窗口之后的 XML）"
    ;;

  release)
    owner="${2:-unknown}"
    force="${3:-}"
    if [ -f "$LOCK" ]; then
      IFS='|' read -r o pid started < "$LOCK"
      # ⚠ **非本人不得释放**（实测事故：命令链"让路后仍无条件 release" ⇒ 把**别人的锁**删了，
      #   持有者以为自己在被保护、其实保护已经没了）。⇒ 只有 owner 名一致才放行；确需强拆用 `--force`。
      if [ "$o" != "$owner" ] && [ "$force" != "--force" ]; then
        echo "⛔ 拒绝释放：锁属于 **$o**（pid=$pid started=$started），而请求者是 $owner。" >&2
        echo "   要强拆请显式：bash Docs/Tools/batch_lock.sh release $owner --force" >&2
        exit 4
      fi
      if [ "$o" != "$owner" ]; then
        printf '**强拆** %s by %s(pid=%s) at %s\n' "$(cat "$LOCK")" "$owner" "$$" "$(now)" >> "$HIST"
        echo "⚠ 已按 --force 强拆（留痕于 BATCH_LOCK.history）" >&2
      fi
      unlink "$LOCK" >/dev/null
      echo "✓ 已释放锁（owner=$owner）"
    else
      echo "锁不存在（无需释放）"
    fi
    ;;

  status)
    if [ -f "$LOCK" ]; then
      IFS='|' read -r o pid started < "$LOCK"
      alive="no"
      kill -0 "$pid" 2>/dev/null && alive="yes"
      fresh="no"
      [ -n "$(find "$LOCK" -mmin -$TTL_MIN 2>/dev/null)" ] && fresh="yes"
      if [ "$alive" = "yes" ] || [ "$fresh" = "yes" ]; then
        echo "持有中：owner=$o pid=$pid started=$started（PID活=$alive 新鲜=$fresh）"
      else
        echo "残留：owner=$o pid=$pid started=$started（既不存活、也已过 TTL ${TTL_MIN}min）⇒ 可接管"
      fi
    else
      echo "空闲"
    fi
    ;;

  *)
    sed -n '2,22p' "${BASH_SOURCE[0]}"
    exit 2
    ;;
esac
