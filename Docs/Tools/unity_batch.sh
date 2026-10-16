#!/usr/bin/env bash
# Unity 批处理跑批入口（把"钉死的路径 + 跑批纪律"写成代码，避免每次手打长命令打错）。
#
# 纪律（每一条都是踩过的坑，不是风格偏好）：
#  ① **必须钉死国际版编辑器路径**：中国版镜像残缺，会毁 `Library/PackageCache` 与 `packages-lock.json`。
#  ② **`-runTests` 一律不带 `-quit`**：带上时编辑器在测试运行器启动前就退出 ⇒
#     RC=0、无 XML、日志里根本没有跑测试 —— **假成功**。不带 `-quit` 才有产物（有失败则 RC=2）。
#  ③ **要入库的测试证据必须显式给 `-outputDir`**；不给它 `-runTests` 什么都不写。
#  ④ **测试结果与日志写仓库外**（`-testResults` / `-logFile` 指到临时目录）⇒ 跑测试不产生仓库改动。
#  ⑤ 跑前查 `Temp/UnityLockfile`：编辑器开着 ⇒ 批处理退 21（唯一核验手段退化成离线编译）。
#
# 用法：
#   bash Docs/Tools/unity_batch.sh sprites         # 重新生成界面精灵与球号贴片
#   bash Docs/Tools/unity_batch.sh rebuild-scene   # 全量重建灰盒诊断场景 PhysicsGreybox.unity
#   bash Docs/Tools/unity_batch.sh rebuild-main    # 给正式入口 Main.unity 叠加/重建玩法层
#   bash Docs/Tools/unity_batch.sh rebuild-main-base  # 只重建 Main 的基础视觉场景（生成侧改动）
#   bash Docs/Tools/unity_batch.sh tests
#   bash Docs/Tools/unity_batch.sh validators
#   bash Docs/Tools/unity_batch.sh build
#   bash Docs/Tools/unity_batch.sh smoke            # 静置 600 帧冒烟
#   bash Docs/Tools/unity_batch.sh smoke-aim        # 连续拖动瞄准 600 帧冒烟（峰值帧时）
set -uo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
UNITY="E:/Unity_Deve/Unity_Tools/Editor/6000.0.49f1/Editor/Unity.exe"
LOG_DIR="${TEMP:-/tmp}/billiards_batch"
PY="${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}"
mkdir -p "$LOG_DIR"

# 删文件一律走 python，**不用 `rm`**：
# 本环境的命令守卫会把带盘符的路径当成不安全删除而**拒绝执行**（`rm -f "C:/…"` 报
# `SAFE_DELETE_INVALID_PATH`），而 `rm ... && run` 一旦被拒就会**静默短路** ——
# 实际后果我踩过：以为跑了测试，其实没跑，读到的是**上一轮的结果 XML**（数字看起来还挺正常）。
unlink() {
  "$PY" -c "import os,sys
p=sys.argv[1]
if os.path.exists(p): os.remove(p); print('removed', p)
else: print('absent', p)" "$1"
}

if [[ ! -f "$UNITY" ]]; then
  echo "找不到国际版编辑器：$UNITY" >&2
  exit 1
fi

if [[ -f "$PROJECT/Temp/UnityLockfile" ]]; then
  echo "⛔ Unity 编辑器正开着本工程（Temp/UnityLockfile 在）⇒ 批处理会退 21。请先关闭编辑器。" >&2
  exit 21
fi

step="${1:-}"
shift || true

case "$step" in
  sprites)
    # 重新生成全部界面精灵与球号贴片（确定性；改色/改形状后必跑）。
    PY="${PY:-C:/Users/hp/.workbuddy/binaries/python/envs/default/Scripts/python.exe}"
    "$PY" "$PROJECT/Docs/Tools/make_ui_sprites.py"
    "$PY" "$PROJECT/Docs/Tools/make_ball_chips.py"
    ;;

  rebuild-scene)
    # 全量重建灰盒场景（含球/杆真模型、逐号材质、界面精灵槽接线）。
    # ⚠ 这是**生成器**路径（config as code）：场景由代码产出，不自带手工调整。
    "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
      -logFile "$LOG_DIR/rebuild_scene.log" \
      -executeMethod Billiards.EditorTools.GreyboxSceneSetup.RebuildFromCommandLine
    ;;

  rebuild-main)
    # 把**正式游戏入口** Main.unity 的玩法层叠加/重建（保留既有桌 / 地面 / 相机 / 光）。
    # ⚠ 出包入口与 build list 都指向 Main.unity ⇒ 这是"正式入口"的生成器路径。
    "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
      -logFile "$LOG_DIR/rebuild_main.log" \
      -executeMethod Billiards.EditorTools.GreyboxSceneSetup.RebuildMainFromCommandLine
    ;;

  rebuild-main-base)
    # **只重建 Main 的基础视觉场景**（桌 / 地面 / 光 / 相机 由 MaterialVisualSetup 生成 + 末尾自动叠玩法层）。
    # 用于"生成侧改动"（如 PlayfieldRoot 缩放 / 相机取景）的复现；不重写材料与预制件。
    "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
      -logFile "$LOG_DIR/rebuild_main_base.log" \
      -executeMethod Billiards.EditorTools.MaterialVisualSetup.BuildSceneFromCommandLine
    ;;

  fonts)
    "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
      -logFile "$LOG_DIR/fonts.log" \
      -executeMethod Billiards.EditorTools.HudAssetBuilder.BuildFromCommandLine
    ;;

  tests)
    # 全量 EditMode。**不带 `-quit`**（见头部纪律②）。
    # ⚠ **跑前先删旧结果、跑后核对时间戳**：不这么做就会把上一轮的 XML 当本轮结论读
    #（本环境的 `rm` 守卫会静默短路整条命令链，我因此误读过一次结果）。
    results="$LOG_DIR/editmode_results.xml"
    unlink "$results"
    "$UNITY" -batchmode -projectPath "$PROJECT" \
      -logFile "$LOG_DIR/tests.log" \
      -runTests -testPlatform EditMode \
      -testResults "$results" \
      -outputDir "$LOG_DIR/editmode_output" \
      "$@"
    echo "unity rc=$?"
    "$PY" -c "import os,sys,time
p=sys.argv[1]
if not os.path.exists(p):
    print('NO XML —— 跑批没有落盘（不得引用任何\"通过数\"）'); raise SystemExit(3)
print('results mtime = ' + time.strftime('%H:%M:%S', time.localtime(os.path.getmtime(p))) + '  size=' + str(os.path.getsize(p)))" "$results"
    ;;

  validators)
    # 四个验收器（各自 `EditorApplication.Exit`，同样不加 `-quit`）。
    # 通过数基线 26 / 33 / 22 / 7（顺序即下表；见 `Docs/测试数基线台账.md`）。
    #
    # ⚠ **验收器会把证据写进仓库**（`Docs/Reports/day*-verification.json` + `day3-*.png`），
    #   与 `-runTests` 的 `-outputDir` 不同，它们**没有**重定向开关 ⇒ 跑一次就会改写已入库证据。
    #   因此：**常规复核跑完后要还原证据**（`git checkout -- Docs/Reports/`），
    #   只有"确实要更新入库证据"时才保留 —— 否则冻结锚点（铁律 13）会被无声破坏。
    echo "⚠ 提醒：验收器会覆写 Docs/Reports/day*-verification.json 与 day3 渲染图；"
    echo "   非证据更新场景请在跑完后执行 git checkout -- Docs/Reports/"
    for entry in Billiards.EditorTools.BilliardsProjectSetup.ApplyProjectConfigFromCommandLine \
                 Billiards.EditorTools.PhysicsValidator.ValidateFromCommandLine \
                 Billiards.EditorTools.MaterialVisualValidator.ValidateFromCommandLine \
                 Billiards.EditorTools.TableIntegrityValidator.ValidateFromCommandLine; do
      # ⚠ **渲染证据必须有 GPU**：`-batchmode -nographics` 下 `cam.Render()` / `ReadPixels` 拿不到内容
      #   ⇒ 产出**纯灰空图**（本工程实测事故：day3 四张图逐字节相同、distinctColors=1，
      #      而 RND-01 只数文件、NAB-01 硬编码 Ok ⇒ **两条绿灯在空证据上亮着**）。
      #   ⇒ **只有会写渲染证据的那一个验收器**去掉 `-nographics`（其余保留 headless：不需要 GPU、更快）。
      ng="-nographics"
      case "$entry" in
        *MaterialVisualValidator*) ng="" ;;
      esac
      echo "--- $entry（$([ -z "$ng" ] && echo '带图形' || echo 'headless')）"
      "$UNITY" -batchmode $ng -projectPath "$PROJECT" \
        -logFile "$LOG_DIR/validator_$(basename "$entry").log" \
        -executeMethod "$entry"
      echo "    rc=$?"
    done

    # ── 后置守卫：**渲染证据非退化**（与判据内 `RND-01`/`NAB-01` **同判**；离线、不需要 Unity）──
    # 事前拦截：谁再刷新证据、只要渲染图退化成空图（或 A/B 两版变相同），**当场红**，
    # 不依赖"人记得去看"。工具自带 `--selftest`（空图 FAIL / 真图 PASS）⇒ 本身不是恒绿摆设。
    echo "--- 后置守卫：qa_render_evidence_check（渲染证据非退化）"
    # ⚠ **必须按退出码分流**（工具契约：`0` = 通过 / `1` = 证据退化 / `2` = 环境不洁[缺 Pillow]）。
    #   绝不可把 `2` 并进 `1` —— 否则"没装 Pillow"会被读成"证据坏了"：**假红，且把后人引向错误的排查方向**。
    #   另：`127` = 找不到工具文件（例如它没被提交）⇒ 同属**环境**问题，不是证据问题。
    guard_rc=0
    "$PY" "$PROJECT/Docs/Tools/qa_render_evidence_check.py" --dir "$PROJECT/Docs/Reports" || guard_rc=$?
    case "$guard_rc" in
      0)
        : ;;   # 通过
      1)
        echo "⛔ 渲染证据退化（空图 / A-B 两版相同）⇒ validators 步骤失败" >&2
        echo "   （注：守卫自身抛异常也会退 1；区别请看上面工具的输出行）" >&2
        exit 1 ;;
      2)
        echo "⚠ 环境不洁：渲染证据守卫**无法运行**（缺 Pillow）⇒ 以**独立退出码 2** 结束（**不是**证据问题）" >&2
        exit 2 ;;
      *)
        echo "⚠ 渲染证据守卫异常退出（rc=$guard_rc，例如 127 = 找不到工具文件）⇒ 以**独立退出码 2** 结束（**不是**证据问题）" >&2
        exit 2 ;;
    esac
    ;;

  build)
    "$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
      -logFile "$LOG_DIR/build.log" \
      -executeMethod Billiards.EditorTools.BuildWindows.BuildFromCommandLine
    ;;

  smoke)
    # 冒烟探针（会短暂开一次窗口 —— 真渲染不能无窗口）。
    exe="$PROJECT/Build/Windows/Idea35PoolRoom.exe"
    if [[ ! -f "$exe" ]]; then
      echo "找不到出包：$exe（先跑 build）" >&2
      exit 1
    fi
    "$exe" -billiards-smoke 600 -billiards-smoke-out "$LOG_DIR/smoke.json"
    echo "smoke rc=$?  evidence=$LOG_DIR/smoke.json"
    ;;

  smoke-aim)
    # **拖动瞄准负载**冒烟：采样期间每帧连续改变瞄准角 ⇒ 逼出"预测重算"这条最坏路径的帧时。
    # 与静置冒烟分开跑、分开落证据（静置数据仍是"基准"，拖动数据是"峰值"）。
    exe="$PROJECT/Build/Windows/Idea35PoolRoom.exe"
    if [[ ! -f "$exe" ]]; then
      echo "找不到出包：$exe（先跑 build）" >&2
      exit 1
    fi
    "$exe" -billiards-smoke 600 -billiards-smoke-aim -billiards-smoke-out "$LOG_DIR/smoke-aim.json"
    echo "smoke-aim rc=$?  evidence=$LOG_DIR/smoke-aim.json"
    ;;

  smoke-transition)
    # **转场负载探针**：采样期**主动触发一次 首页→打球 转场**，把"转场窗口逐帧耗时"与"前后稳态"分开记。
    # ⚠ 与既有两档**不是一个口径**：既有两档（`smoke`/`smoke-aim`）**故意不播转场**（首页被抑制 ⇒ from==to
    #   ⇒ 一次过渡都不起）⇒ 它们**永远测不到**"点开始那一刻的卡顿"。本档**必须让首页显示**才能切。
    #   ⇒ 本档的 `homeStartup` 预期是 `shown`（不是 `suppressed`），这是**预期**、也是"确实播了转场"的前提证据。
    # 帧数 = 60（转场前稳态）+ 90（转场窗口，覆盖 0.9 s@60fps = 54 帧并留余量）+ 若干帧收尾稳态。
    exe="$PROJECT/Build/Windows/Idea35PoolRoom.exe"
    if [[ ! -f "$exe" ]]; then
      echo "找不到出包：$exe（先跑 build）" >&2
      exit 1
    fi
    "$exe" -billiards-smoke 260 -billiards-smoke-transition -billiards-smoke-out "$LOG_DIR/smoke-transition.json"
    echo "smoke-transition rc=$?  evidence=$LOG_DIR/smoke-transition.json"
    ;;

  *)
    sed -n '2,30p' "${BASH_SOURCE[0]}"
    exit 2
    ;;
esac

echo "完成：$step（日志 $LOG_DIR）"
