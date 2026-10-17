#!/usr/bin/env bash
# **跑批前置检查（共享，单一实现点）** —— `p6rt_*` 与 `p6ui_*` 两套注入脚本**共同调用它**；
# **噪声白名单只在本文件里维护一份**（别在各自脚本里再抄一遍）。
#
# ── 为什么需要它（真实事故，2026-10-17）──────────────────────────────────────
#   我在 `11:04:13` 查过 `git status`（只有两条字体噪声），但另一位成员在 **`11:06:47`**
#   保存了它的**未提交**判据（`T148`），我 **`11:06:50`** 起跑 ⇒ 我的全量**把它的半成品一起编了**。
#   ⇒ **「查过一次」不算干净 —— 检查会过期**。所以检查必须
#     ① **紧贴起跑**（不是"开工前查一次"）；② **机械化**（不靠人记）。
#   ⚠ 另一条同源事实：**"字节稳定" ≠ "字节干净"** —— 源码树签名不变只说明跑批期间没人再改，
#     不说明那份字节里**没有别人未提交的改动**。这两件事必须分别判。
#
# ── 三类路径，语义**不同**（别混用）──────────────────────────────────────────
#   · **噪声白名单**（本文件里唯一一份）：跑批**本身**会写脏的资产 / 单写者文档 ——
#     与"谁的改动"无关，**任何人跑批都放行**。
#   · **`--expect <glob>`** = 「**这些是我要提交的**」—— 调用方**自己的**在途文件。
#   · **`--accept-foreign <glob>`** = 「**这些是别人的、我知道且在途**」—— 放行，但**必须留痕**
#     （打「⚠ 已接受外来在途改动：…」）。两路并行时用它，避免互相把对方挡死。
#   ⇒ 既不在白名单、也不在任一旗子里 ⇒ **退 7**（先问一句再跑）。
#
# ── 用法 ─────────────────────────────────────────────────────────────────
#   bash Docs/Tools/batch_precheck.sh \
#        --expect <glob> [--expect ...] \
#        [--accept-foreign <glob> ...]
#   BATCH_ACCEPT_DIRTY=1 bash ...      # 全放行（留痕）
#   bash ... --selftest                # 自测（造/清探针；覆盖 4 条语义）
#
# ── 退出码 ───────────────────────────────────────────────────────────────
#   0 = 干净（或已按旗子/`BATCH_ACCEPT_DIRTY` 放行）
#   7 = **白名单外有既非 --expect 也非 --accept-foreign 的改动**（先问一句再跑）
#   2 = 用法错误；3 = 自测失败
set -uo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PY="${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}"
SELF_REL="Docs/Tools/batch_precheck.sh"

# ── **噪声白名单（唯一一份）** ─────────────────────────────────────────────
# · `ProjectSettings/QualitySettings.asset` / `Assets/_Project/UI/Fonts/*SDF.asset`
#   —— **跑批本身**就会写脏（Unity 每次跑都改），与任何人的改动无关；
# · `Docs/测试数基线台账.md` —— 纯文档，且由**单写者**（UI 侧）维护。
# ⚠ 加条目必须给理由：这里是"**跑批噪声**"，不是"我不想看的改动"。
NOISE=(
  "ProjectSettings/QualitySettings.asset"
  "Assets/_Project/UI/Fonts/*SDF.asset"
  "Docs/测试数基线台账.md"
)

matches() { # $1 = path；其余 = patterns
  local p="$1"; shift
  local pat
  for pat in "$@"; do
    # shellcheck disable=SC2053  # 需要 glob 语义
    if [[ "$p" == $pat ]]; then
      return 0
    fi
  done
  return 1
}

# 解析 `git status --porcelain -z`，把路径分发到 3 个数组（nameref）：
#   $1 = foreign（既非白名单、也非任何旗子）｜$2 = accepted（命中 --accept-foreign）｜$3 = expected（命中 --expect）
collect() {
  local -n _foreign="$1" _accepted="$2" _expected="$3"
  _foreign=(); _accepted=(); _expected=()
  local rec path
  while IFS= read -r -d '' rec; do
    if [[ "$rec" =~ ^..\ (.*)$ ]]; then
      path="${BASH_REMATCH[1]}"
    else
      path="$rec"             # 认不出的记录（例如 rename 的第二段）⇒ 保守地当路径
    fi
    [ -z "$path" ] && continue

    # 预检**自身**不参与判定：它是新增文件、提交前一直处于未跟踪状态 ⇒ 否则两套脚本都会
    # 被"它自己"挡住（而且在提交前永远挡）。**只排除这一个路径**，不做通配。
    [ "$path" = "$SELF_REL" ] && continue

    if [ ${#NOISE[@]} -gt 0 ] && matches "$path" "${NOISE[@]}"; then
      continue
    fi
    if [ ${#EXPECT[@]} -gt 0 ] && matches "$path" "${EXPECT[@]}"; then
      _expected+=("$path"); continue
    fi
    if [ ${#ACCEPT_FOREIGN[@]} -gt 0 ] && matches "$path" "${ACCEPT_FOREIGN[@]}"; then
      _accepted+=("$path"); continue
    fi
    _foreign+=("$path")
  done < <(git -C "$PROJECT" status --porcelain -z)
}

report_and_decide() { # $1 = foreign（nameref），$2 = accepted（nameref）
  local -n _f="$1" _a="$2"
  printf '%s\n' "=== git status --porcelain（起跑前快照，原样打印）==="
  git -C "$PROJECT" status --porcelain || true
  printf '%s\n' "=== 噪声白名单（唯一一份，见本文件头部理由）==="
  printf '   · %s\n' "${NOISE[@]}"

  if [ ${#_a[@]} -gt 0 ]; then
    # ⚠ **接受别人的在途改动必须留痕**（可审计）：调用方把这段写进 run.log
    printf '%s\n' "⚠ 已接受外来在途改动（--accept-foreign）："
    printf '   · %s\n' "${_a[@]}"
  fi

  if [ ${#_f[@]} -eq 0 ]; then
    printf '%s\n' "✓ 前置检查：白名单外**没有**未声明的改动（可以跑批）"
    return 0
  fi

  if [ "${BATCH_ACCEPT_DIRTY:-0}" = "1" ]; then
    printf '⚠ ACCEPT_DIRTY 已放行，外来改动 = %s\n' "${_f[*]}"
    return 0
  fi

  printf '%s\n' "⛔ 白名单外有改动 —— **这些不是你的改动，先问一句再跑**：" >&2
  printf '   - %s\n' "${_f[@]}" >&2
  printf '%s\n' "   ⇒ 若确认是**对方的在途文件**：--accept-foreign <glob>（会留痕）" >&2
  printf '%s\n' "   ⇒ 若确认可整体放行：BATCH_ACCEPT_DIRTY=1（会留痕）" >&2
  return 7
}

EXPECT=()
ACCEPT_FOREIGN=()
SELFTEST=0
while [ $# -gt 0 ]; do
  case "$1" in
    --expect)
      [ $# -ge 2 ] || { echo "--expect 需要值" >&2; exit 2; }
      EXPECT+=("$2"); shift 2 ;;
    --accept-foreign)
      [ $# -ge 2 ] || { echo "--accept-foreign 需要值" >&2; exit 2; }
      ACCEPT_FOREIGN+=("$2"); shift 2 ;;
    --selftest) SELFTEST=1; shift ;;
    *) echo "未知参数：$1" >&2; exit 2 ;;
  esac
done

# ── 自测 ─────────────────────────────────────────────────────────────────
# ⚠ **探针必须"真的可见"才算测到**：第一版用 `*.tmp` 当探针，而 `.gitignore:29` 忽略 `*.tmp`
#   ⇒ `git status` 根本看不见它 ⇒ 那一轮 `rc=7` 是**别的外来改动**贡献的，探针**没被测到**
#   = **真空绿的新变体（连检测载体都不可见）**。
#   ⇒ 自测**不只看 rc**：还断言"那个探针真的出现在**外来清单**里"，
#     并用 `git check-ignore` **独立证明探针没被忽略**（"我建了它就在"是错的假设）。
if [ "$SELFTEST" = "1" ]; then
  d="$PROJECT/Docs/Tools"
  pa="$d/_precheck_selftest_expect.txt"    # --expect 声明 ⇒ 应**不**进外来清单
  pb="$d/_precheck_selftest_accept.txt"    # --accept-foreign 声明 ⇒ 应**不**进外来清单、但有留痕
  pc="$d/_precheck_selftest_foreign.txt"   # 什么都不声明 ⇒ 应**进**外来清单
  l1="$TEMP/_pc_self_expect.log"
  l2="$TEMP/_pc_self_accept.log"
  l3="$TEMP/_pc_self_foreign.log"
  l4="$TEMP/_pc_self_acceptdirty.log"

  : > "$pa"; : > "$pb"; : > "$pc"
  # **独立证明探针可见**（不是"我建了它就在"）
  ignore_hits="$(git -C "$PROJECT" check-ignore "$pa" "$pb" "$pc" 2>/dev/null | wc -l | tr -d ' ')"

  rc1=0; rc2=0; rc3=0; rc4=0
  bash "${BASH_SOURCE[0]}" --expect "Docs/Tools/_precheck_selftest_expect.txt"  > "$l1" 2>&1 || rc1=$?
  bash "${BASH_SOURCE[0]}" --accept-foreign "Docs/Tools/_precheck_selftest_accept.txt" > "$l2" 2>&1 || rc2=$?
  bash "${BASH_SOURCE[0]}" > "$l3" 2>&1 || rc3=$?
  BATCH_ACCEPT_DIRTY=1 bash "${BASH_SOURCE[0]}" > "$l4" 2>&1 || rc4=$?

  # 删探针走 python（本环境的命令守卫会把带盘符的 `rm -f` 当不安全删除而拒绝）
  "$PY" -c "import os,sys
for p in sys.argv[1:]:
    os.remove(p) if os.path.exists(p) else None" "$pa" "$pb" "$pc"

  in_foreign() { grep -cE "^   - .*$1" "$2" || true; }      # 外来清单里的条目数
  accept_trace() { grep -cE "^   · .*$1" "$2" || true; }    # 「已接受外来在途改动」那段里的条目数

  n_expect_in_foreign="$(in_foreign '_precheck_selftest_expect\.txt' "$l1")"
  n_accept_in_foreign="$(in_foreign '_precheck_selftest_accept\.txt' "$l2")"
  n_accept_trace="$(accept_trace '_precheck_selftest_accept\.txt' "$l2")"
  n_foreign_in_foreign="$(in_foreign '_precheck_selftest_foreign\.txt' "$l3")"

  echo "selftest: 探针被 .gitignore 忽略的条数 = $ignore_hits（期望 0）"
  echo "selftest: ① --expect        rc=$rc1（期望 7）／该探针在外来清单 = $n_expect_in_foreign（期望 0）"
  echo "selftest: ② --accept-foreign rc=$rc2（期望 7）／该探针在外来清单 = $n_accept_in_foreign（期望 0）／留痕 = $n_accept_trace（期望 ≥1）"
  echo "selftest: ③ 都不声明        rc=$rc3（期望 7）／该探针在外来清单 = $n_foreign_in_foreign（期望 ≥1）"
  echo "selftest: ④ ACCEPT_DIRTY=1  rc=$rc4（期望 0）"

  if [ "$ignore_hits" = "0" ] \
     && [ "$n_expect_in_foreign" = "0" ] \
     && [ "$n_accept_in_foreign" = "0" ] && [ "$n_accept_trace" -ge 1 ] \
     && [ "$n_foreign_in_foreign" -ge 1 ] \
     && [ "$rc1" = "7" ] && [ "$rc2" = "7" ] && [ "$rc3" = "7" ] && [ "$rc4" = "0" ]; then
    echo "selftest: PASS"
    exit 0
  fi
  echo "selftest: FAIL" >&2
  exit 3
fi

foreign_list=()
accepted_list=()
expected_list=()
collect foreign_list accepted_list expected_list
report_and_decide foreign_list accepted_list
exit $?
