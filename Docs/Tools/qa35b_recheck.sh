#!/usr/bin/env bash
# QA 复核（35-B）串行跑批：幂等 + 四验收器 + 出包 + 两冒烟。
# 纪律与 unity_batch.sh 一致：钉死国际版编辑器；跑前查锁；结果写仓库外；跑完还原 Docs/Reports。
# ⚠ **用途 = 复核工具**（[3] 会 `git checkout -- Docs/Reports/` 保住仓库证据）⇒ **不是"刷新证据"的入口**：
#    拿它刷新会与"同批刷新（不要还原）"的裁定冲突 ⇒ 要更新证据请用 `unity_batch.sh validators`。
# ⚠ **两套退出码不得互套**：**冒烟** = 0 达标 / 1 未达标 / 2 写盘失败 / 3 环境不洁；
#    **`qa_render_evidence_check.py`** = 0 通过 / 1 证据退化 / 2 缺 Pillow（环境）。**数字含义不同**。
set -uo pipefail

PROJECT="E:/Unity_Deve/Unity_Project/Billiards game"
UNITY="E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe"
LOG_DIR="${TEMP:-/tmp}/billiards_qa35b"
PY="${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}"
MAIN="$PROJECT/Assets/_Project/Scenes/Main.unity"
mkdir -p "$LOG_DIR"

if [[ -f "$PROJECT/Temp/UnityLockfile" ]]; then
  echo "LOCK PRESENT - abort" >&2; exit 21
fi

sha() { sha1sum "$1" | cut -d' ' -f1; }

echo "=== [0] Main.unity sha1 BEFORE = $(sha "$MAIN")"
stat -c '  mtime=%y size=%s' "$MAIN"

echo "=== [1] idempotence: ApplyMainScene (idempotent entry) x2 ==="
for i in 1 2; do
  "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
    -logFile "$LOG_DIR/apply_main_$i.log" \
    -executeMethod Billiards.EditorTools.GreyboxSceneSetup.ApplyMainFromCommandLine
  echo "  run $i rc=$?  sha1=$(sha "$MAIN")"
done
echo "  (idempotent ⇒ 两次 Apply 后 sha1 应等于 BEFORE)"

echo "=== [2] four validators ==="
for entry in Billiards.EditorTools.BilliardsProjectSetup.ApplyProjectConfigFromCommandLine \
             Billiards.EditorTools.PhysicsValidator.ValidateFromCommandLine \
             Billiards.EditorTools.MaterialVisualValidator.ValidateFromCommandLine \
             Billiards.EditorTools.TableIntegrityValidator.ValidateFromCommandLine; do
  # ⚠ **与 `unity_batch.sh` 同口径**：**只**给会写渲染证据的 `MaterialVisualValidator` 去掉 `-nographics`
  #   （`-batchmode` 保留 ⇒ 短暂开窗，与本工程冒烟同模式），其余三个仍 headless（不需要 GPU、更快）。
  #   旧版把四个**都**放在 `-nographics` 下跑、再靠 [3] 的 `git checkout` 把后果"还原"掉 ⇒
  #   **等于把"渲染证据退化成纯灰空图"这个真问题藏起来**（本工程实测事故：四张逐字节相同、1 色）。
  ng="-nographics"
  case "$entry" in
    *MaterialVisualValidator*) ng="" ;;
  esac
  "$UNITY" -batchmode $ng -projectPath "$PROJECT" \
    -logFile "$LOG_DIR/v_$(basename "$entry").log" \
    -executeMethod "$entry"
  echo "  $entry rc=$?（$([ -z "$ng" ] && echo '带图形' || echo 'headless')）"
done
grep -h 'DAY[0-9]_RESULT' "$LOG_DIR"/v_*.log || true

echo "=== [2b] post-guard: 渲染证据非退化（与判据内 RND-01/NAB-01 同判）==="
# 按退出码分流（工具契约：0 通过 / 1 证据退化 / 2 环境不洁[缺 Pillow]）—— **绝不把 2 并进 1**，
# 否则"没装 Pillow"会被读成"证据坏了"（假红 + 引错排查方向）。`*`（如 127 找不到工具）归**环境**。
# ⚠ 必须放在 [3] "还原证据"之前：否则空图会被还原掉、这一步就抓不到了。
guard_rc=0
"$PY" "$PROJECT/Docs/Tools/qa_render_evidence_check.py" --dir "$PROJECT/Docs/Reports" || guard_rc=$?
case "$guard_rc" in
  0) echo "  render-evidence OK" ;;
  1) echo "⛔ 渲染证据退化（空图 / A-B 两版相同）⇒ 复核失败" >&2; exit 1 ;;
  2) echo "⚠ 环境不洁：守卫无法运行（缺 Pillow）⇒ 独立退出码 2" >&2; exit 2 ;;
  *) echo "⚠ 守卫异常退出（rc=$guard_rc，如 127 = 找不到工具）⇒ 独立退出码 2" >&2; exit 2 ;;
esac

echo "=== [3] restore validator-overwritten evidence ==="
# 注意：本脚本是**复核**（不改动入库证据）⇒ 这里仍还原。但**空图问题已在 [2]/[2b] 当场拦住**，
# 不再依赖"还原"来掩盖。若将来要**更新**证据，请用 `unity_batch.sh validators`（那里**不**还原）。
( cd "$PROJECT" && git checkout -- Docs/Reports/ && echo "  restored Docs/Reports" && git status --short Docs/Reports/ )

echo "=== [4] build ==="
"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -logFile "$LOG_DIR/build.log" \
  -executeMethod Billiards.EditorTools.BuildWindows.BuildFromCommandLine
echo "  build rc=$?"
cat "$PROJECT/Build/Windows/build-report.json" 2>/dev/null

echo "=== [5] smoke (static) ==="
"$PROJECT/Build/Windows/Idea35PoolRoom.exe" -billiards-smoke 600 -billiards-smoke-out "$LOG_DIR/smoke.json"
echo "  smoke rc=$?"
cat "$LOG_DIR/smoke.json"

echo "=== [6] smoke-aim (drag) ==="
"$PROJECT/Build/Windows/Idea35PoolRoom.exe" -billiards-smoke 600 -billiards-smoke-aim -billiards-smoke-out "$LOG_DIR/smoke-aim.json"
echo "  smoke-aim rc=$?"
cat "$LOG_DIR/smoke-aim.json"

echo "=== [7] noise restore (ProjectSettings if rewritten) ==="
( cd "$PROJECT" && git checkout -- ProjectSettings/QualitySettings.asset ProjectSettings/GraphicsSettings.asset 2>/dev/null; git status --short ProjectSettings/ | head )

echo "=== DONE (logs: $LOG_DIR) ==="
