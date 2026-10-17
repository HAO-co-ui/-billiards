"""P6-Runtime 的注入补丁（**只做文本替换**；备份 / 基线比对 / 还原 / sha1 回读由
`Docs/Tools/p6rt_injections.sh` 负责）。

为什么把补丁与流程分开：本项目踩过"注入态留下脏源码"的事故，所以**流程**
（备份在仓外 + apply 前比基线 + 跑完立刻还原 + 还原后回读 sha1）必须是**机械的**，
而"改哪几行"是**内容**问题、单独放这里便于逐条审阅。

⚠ **基线不在本文件里**：本轮改完源码基线就变了 ⇒ 基线由 `p6rt_injections.sh`
用「首次运行写入 `baseline.sha1` / 之后运行时读取」的机制管理（**不写死字面量**）。

用法：
    <managed venv python> Docs/Tools/p6rt_patch.py {apply|restore} <tag>
"""

from __future__ import annotations

import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

PTD = "Assets/_Project/Scripts/Runtime/Physics/PhysicsTestDrive.cs"
RULES = "Assets/_Project/Scripts/Runtime/Rules/EightBallRules.cs"
AI = "Assets/_Project/Scripts/Runtime/Rules/EightBallAi.cs"

# tag -> (相对路径, [(anchor, replacement), ...])
#
# ⚠ 每条的 anchor 必须**恰好出现一次**（脚本会断言）—— 出现 0 次说明"锚点漂了"，
#   出现多次说明"注入了不止一处"。两种都直接报错退出，**不猜**。
PATCHES = {
    # ① 删掉 `Advance()` 尾部的**唯一结算接线** ⇒ 规则管线没有人调用 ⇒ 端到端不结算
    "settle-unwire": (
        PTD,
        [
            (
                "            // （EditMode 不跑 `Update`/`FixedUpdate`）—— 与上面 `TrackCueBallScratch()` 同一个理由。\n"
                "            SettleShotIfAtRest();\n",
                "            // （EditMode 不跑 `Update`/`FixedUpdate`）—— 与上面 `TrackCueBallScratch()` 同一个理由。\n"
                "            // INJ-P6RT-1：**删掉结算接线**（预期 T145 ⑨ 必红）\n",
            ),
        ],
    ),
    # ② 去掉**静止门** ⇒ 出杆后第一次推进就"结算"，事实全空 ⇒ 端到端不成立
    "rest-gate-drop": (
        PTD,
        [
            (
                "            if (_simulator == null || !_shotInFlight || MatchOver || !_simulator.TableAtRest)\n",
                "            // INJ-P6RT-2：**去掉静止门**（预期 T145 ⑨ 必红）\n"
                "            if (_simulator == null || !_shotInFlight || MatchOver)\n",
            ),
        ],
    ),
    # ③ 把**结算闸门改坏**：消费结算卡时顺手把 `MatchOver` 清掉（"关掉卡就自动开新局"）
    "settlement-gate-break": (
        PTD,
        [
            (
                "            if (!_settlementPending)\n"
                "            {\n"
                "                return false;\n"
                "            }\n"
                "\n"
                "            _settlementPending = false;\n"
                "            return true;\n",
                "            if (!_settlementPending)\n"
                "            {\n"
                "                return false;\n"
                "            }\n"
                "\n"
                "            // INJ-P6RT-3：**结算闸门改坏** —— 消费结算卡时把 MatchOver 一并清掉（\"关掉卡就自动开新局\"）\n"
                "            MatchOver = false;\n"
                "            _settlementPending = false;\n"
                "            return true;\n",
            ),
        ],
    ),
    # ⑦ **QA Major-2 的专属注入**：`TryLaunchAiShot` **永不启动那一杆**
    #    ⇒ AI 判据② 若没补"前提成立"，就会**真空绿**；补了之后必红在「前提不成立」那条。
    "ai-never-launch": (
        PTD,
        [
            (
                "            aimDegrees = intent.aimDegrees;\n"
                "            power = intent.power;\n"
                "            SyncAimIndicator();\n"
                "            return Launch();\n",
                "            aimDegrees = intent.aimDegrees;\n"
                "            power = intent.power;\n"
                "            SyncAimIndicator();\n"
                "            // INJ-P6RT-7：**AI 永不启动那一杆**（预期 T145 (AI②) 的「前提不成立」必红）\n"
                "            return Vector2.zero;\n",
            ),
        ],
    ),
    # ⑧ **去掉「玩家回合不出手」守卫** ⇒ AI 在**玩家回合**也抢杆（对局失控）
    "ai-turn-player-guard-drop": (
        PTD,
        [
            (
                "                if (MatchOver || !IsAiTurn || _settlementPending || _shotInFlight)\n",
                "                // INJ-P6RT-8：**去掉「玩家回合不出手」守卫**（预期 T145 (AI驱动③) 必红）\n"
                "                if (MatchOver || _settlementPending || _shotInFlight)\n",
            ),
        ],
    ),
    # ⑨ **去掉「台面静止」守卫** ⇒ 球还在滚就出杆（`Launch()` 拒 ⇒ 白送一次「让位」）
    "ai-turn-rest-gate-drop": (
        PTD,
        [
            (
                "                return _simulator != null && _simulator.TableAtRest;\n",
                "                // INJ-P6RT-9：**去掉「台面静止」守卫**（预期 T145 (AI驱动②b) 必红）\n"
                "                return _simulator != null;\n",
            ),
        ],
    ),
    # ⑩ **删掉「AI 无法出杆 ⇒ 让位」的出路** ⇒ `IsAiTurn` 长期为真、没人出杆 = **僵局**
    "ai-turn-yield-drop": (
        PTD,
        [
            (
                "        private void YieldAiTurn(string reason)\n"
                "        {\n"
                "            AiPhase = AiShotPhase.None;\n"
                "            _aiPhaseElapsed = 0f;\n"
                "            AiTurnYieldedCount++;\n"
                "            SetTurn(ShotTurn.Player);\n",
                "        private void YieldAiTurn(string reason)\n"
                "        {\n"
                "            // INJ-P6RT-10：**删掉「AI 无法出杆 ⇒ 让位」的出路**"
                "（预期 T145 (AI驱动⑤) 必红 = 会卡死）\n"
                "            AiPhase = AiShotPhase.None;\n"
                "            _aiPhaseElapsed = 0f;\n",
            ),
        ],
    ),
    # ⑪ **回到"合法就续杆"的旧行为** ⇒ 合法但没进本方球也续杆（P7 判据③ 必红）
    "p7-no-pot-turn": (
        RULES,
        [
            (
                "            else if (PottedOwnGroupBall(before.groupState, shot.pottedBalls))\n"
                "            {\n"
                "                verdict.nextTurn = before.turn;\n"
                "            }\n"
                "            else\n"
                "            {\n"
                "                verdict.nextTurn = Other(before.turn);\n"
                "            }\n",
                "            else if (PottedOwnGroupBall(before.groupState, shot.pottedBalls))\n"
                "            {\n"
                "                verdict.nextTurn = before.turn;\n"
                "            }\n"
                "            else\n"
                "            {\n"
                "                // INJ-P7-1：**删掉「合法但没进本方球 ⇒ 换手」**（回到旧行为：合法就续杆）\n"
                "                verdict.nextTurn = before.turn;\n"
                "            }\n",
            ),
        ],
    ),
    # ⑫ **删掉 `reassignGroup` 的施加** ⇒ 组别永远停在 OpenTable（P7 判据①⑦ 必红）
    "p7-group-drop": (
        PTD,
        [
            (
                "            if (verdict.reassignGroup && verdict.groupBall > 0)\n"
                "            {\n"
                "                GroupState shooterGroup = EightBallRules.IsSolid(verdict.groupBall)\n"
                "                    ? GroupState.Solid\n"
                "                    : GroupState.Stripe;\n"
                "                _playerGroup = CurrentTurn == ShotTurn.Player ? shooterGroup : ComplementGroup(shooterGroup);\n"
                "            }\n",
                "            // INJ-P7-2：**删掉 `reassignGroup` 的施加**（组别永远停在 OpenTable）\n",
            ),
        ],
    ),
    # ⑬ **删掉 `ballInHand` 的施加** ⇒ 自由球永远不发（P7 判据⑤ 必红）
    "p7-ballinhand-drop": (
        PTD,
        [
            (
                "            BallInHand = verdict.ballInHand;\n"
                "            if (verdict.ballInHand != BallInHandKind.None)\n"
                "            {\n"
                "                PendingCueBallPlacement = true;\n"
                "            }\n",
                "            // INJ-P7-3：**删掉 `ballInHand` 的施加**（自由球永远不发）\n",
            ),
        ],
    ),
    # ⑭ **让球杆在飞行期间仍跟着白球** ⇒ P7 判据⑧ 必红（用户实机反馈"杆跟着球飞"）
    # ⚠ **`p7-stick-follow` 已退役（P8-D）**：它打的是 `StickAnchorPosition`（P7 的"冻结"方案），
    #   而该函数已按用户要求**整块删除**（改为"出杆期间整根隐藏"）⇒ 旧注入没有锚点了。
    #   替代者是下面的 `p8-cue-visible-in-flight`（同一条用户反馈的新形态）。
    # ⑯ **出杆期间球杆仍可见**（删 `!shotInFlight`）⇒ P8 判据⑧ 必红（用户要"暂时消失"）
    "p8-cue-visible-in-flight": (
        "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs",
        [
            (
                "            return flowVisible && cueBallInPlay && !transitionActive && !homeViewOpen && !shotInFlight\n"
                "                   && !awaitingPlacement;\n",
                "            // INJ-P8-1：**出杆期间仍可见**（删掉 `!shotInFlight`）\n"
                "            return flowVisible && cueBallInPlay && !transitionActive && !homeViewOpen\n"
                "                   && !awaitingPlacement;\n",
            ),
        ],
    ),
    # ⑰ **删掉「待放置 ⇒ 不许出杆」那道门** ⇒ T70b 必红（白球会"自动代打"回去）
    "p8-pending-launch-gate-drop": (
        PTD,
        [
            (
                "            if (cueOffTable)\n"
                "            {\n"
                "                RefusedPendingPlacementCount++;\n"
                "                return Vector2.zero;\n"
                "            }\n",
                "            // INJ-P8-2：**删掉「待放置 ⇒ 不许出杆」那道门**（预期 T70b 必红）\n",
            ),
        ],
    ),
    # ⑮ **AI 恢复"一次性出杆"**（规划完立刻击出，跳过瞄准/拉杆） ⇒ P7 判据⑨ 必红
    "p7-ai-one-shot": (
        PTD,
        [
            (
                "                    if (!BeginAiShot())\n"
                "                    {\n"
                "                        YieldAiTurn(\"规划不出任何一杆（无合法目标 / 叫袋失败）\");\n"
                "                    }\n"
                "\n"
                "                    return;\n",
                "                    if (!BeginAiShot())\n"
                "                    {\n"
                "                        YieldAiTurn(\"规划不出任何一杆（无合法目标 / 叫袋失败）\");\n"
                "                        return;\n"
                "                    }\n"
                "\n"
                "                    // INJ-P7-5：**AI 恢复「一次性出杆」**（跳过瞄准 / 拉杆两段）\n"
                "                    power = _aiPlannedPower;\n"
                "                    AiPhase = AiShotPhase.Strike;\n"
                "                    return;\n",
            ),
        ],
    ),
    # ⑯ **`isBreak` 从不清**（回到旧 bug）⇒ 第一杆之后**每一杆都被 `JudgeBreak` 判**（P7 判据⑩ 必红）
    "p7-isbreak-never-clear": (
        PTD,
        [
            (
                "            ruleState.isBreak = verdict.reRack;\n",
                "            // INJ-P7-7：**`isBreak` 从不清**（回到旧 bug ⇒ 之后每一杆都被 `JudgeBreak` 判）\n",
            ),
        ],
    ),
    # ⑰ **删掉 AI 的自由球自动放置** ⇒ AI 只能吃 `Launch()` 的"回开球位"兜底（P7 判据⑪ 必红）
    "p7-ai-autoplace-drop": (
        PTD,
        [
            (
                "                    if (!TryAutoPlaceAiCueBall())\n"
                "                    {\n"
                "                        YieldAiTurn(\"自由球找不到合法放球点\");\n"
                "                        return;\n"
                "                    }\n",
                "                    // INJ-P7-6：**删掉 AI 的自由球自动放置**（预期 P7⑪ 必红）\n",
            ),
        ],
    ),
    # ⑱ **剩余球号列表不随进球刷新** ⇒ 定组后仍为空（P9-B 判据② 必红）
    "p9-roster-stale": (
        PTD,
        [
            (
                "            RebuildRemainingNumbers();\n",
                "            // INJ-P9-1：**删掉剩余球号的重建**（列表不随进球更新）\n",
            ),
        ],
    ),
    # ⑲ **三档参数全改成同一组**（都走 Club）⇒ 差距塌成 0（P9-A③ 必红）
    "p9-tiers-flatten": (
        AI,
        [
            (
                "        public static AiDifficultyParameters Parameters(AiDifficulty difficulty)\n"
                "        {\n"
                "            switch (difficulty)\n",
                "        public static AiDifficultyParameters Parameters(AiDifficulty difficulty)\n"
                "        {\n"
                "            // INJ-P9-2：**三档参数全改成同一组**（都走 Club）⇒ 难度无差别\n"
                "            difficulty = AiDifficulty.Club;\n"
                "            switch (difficulty)\n",
            ),
        ],
    ),
    # ⑳ **删掉「待放态把白球抬到悬浮位」** ⇒ 白球仍躺在台布上（P9-D 判据必红）
    "p9-cue-float-drop": (
        PTD,
        [
            (
                # ⚠ 只锚**代码行**（不锚注释：注释里带引号，锚点一碰就碎）
                "if (i == 0 && _cueFloatHeight > PoolConstants.BallRadius + 1e-4f)\n",
                "if (i == 0 && false && _cueFloatHeight > PoolConstants.BallRadius + 1e-4f)   "
                "// INJ-P9-3：**永不抬球**（预期 P9-D / P10-C 必红）\n",
            ),
        ],
    ),
    # ㉑ **删掉球杆的「待放置」门** ⇒ 白球还在台上的那种待放会照常画出球杆（P10-A 必红）
    "p10-stick-awaiting-drop": (
        "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs",
        [
            (
                "                   && !awaitingPlacement;\n",
                "                   ;\n",
            ),
        ],
    ),
    # ㉒ **删掉瞄准线的「待放置」门** ⇒ 瞄准线残留（P10-B 必红）
    "p10-aim-awaiting-drop": (
        "Assets/_Project/Scripts/Runtime/Physics/AimAssistController.cs",
        [
            (
                "                   && !awaitingPlacement;\n",
                "                   ;\n",
            ),
        ],
    ),
    # ㉓ **重钉（P10 甲方案后）**：`awaiting` 改成"只看待放"（= 回到"两种待放都抬"）
    #     ⇒ **只有 ② 红**（犯规+球在台上+位置合法时不该抬）
    "p10-float-any-pending": (
        PTD,
        [
            (
                "            bool awaiting = MustPlaceCueBallBeforeShooting;   // P10：单点真源（球在台上且合法 ⇒ 不抬）\n"
                "            float target = awaiting ? CueBallFloatHeightMeters : PoolConstants.BallRadius;\n",
                "            bool awaiting = PendingCueBallPlacement;   // INJ：**任何待放都抬**\n"
                "            float target = awaiting ? CueBallFloatHeightMeters : PoolConstants.BallRadius;\n",
            ),
        ],
    ),
    # ㉓b **INJ-A2-b**：横向无条件用「拿出来」的固定落点 ⇒ **只有 P10-A2② 红**
    # ㉕ **删掉「待放期一律拒杆」那道门** ⇒ P10-拒杆③ 必红
    "p10-refuse-gate-drop": (
        PTD,
        [
            (
                "            if (MustPlaceCueBallBeforeShooting)\n"
                "            {\n"
                "                RefusedLaunchWhileAwaitingCount++;\n"
                "                return Vector2.zero;\n"
                "            }\n",
                "            // INJ-P10-5：**删掉「待放期一律拒杆」**（预期 P10-拒杆③ 必红）\n",
            ),
        ],
    ),
    # ㉖ **把门写成恒真** ⇒ 连开球也被拒 ⇒ P10-拒杆① 必红
    "p10-refuse-gate-always": (
        PTD,
        [
            (
                "            if (MustPlaceCueBallBeforeShooting)\n",
                "            if (true)   // INJ-P10-6：**恒真 ⇒ 连开球也拒**（预期 P10-拒杆① 必红）\n",
            ),
        ],
    ),
    "p10-float-xy-fixed": (
        PTD,
        [
            (
                "                        Vector2 xy = awaiting\n"
                "                            ? (offTable ? CueBallFloatPhysicsPosition : balls[0].position)\n"
                "                            : balls[0].position;\n",
                "                        Vector2 xy = awaiting ? CueBallFloatPhysicsPosition : balls[0].position;\n",
            ),
        ],
    ),
    # ㉕ **`MustPlaceCueBallBeforeShooting` 恒等于 `IsWaitingForPlacement`**（回到"两种待放都强制"）
    #     ⇒ **只有 ② 红**（该直接出杆的被拒）
    "p10-must-place-stuck": (
        PTD,
        [
            (
                "        public bool MustPlaceCueBallBeforeShooting =>\n",
                "        public bool MustPlaceCueBallBeforeShooting => IsWaitingForPlacement;   // INJ：回到\"两种待放都强制\"\n"
                "        private bool Unused_MustPlace() =>\n",
            ),
        ],
    ),
    # ㉖ **`MustPlaceCueBallBeforeShooting` 恒 false**（永不要求放球）⇒ **只有 ① 红**
    "p10-must-place-off": (
        PTD,
        [
            (
                "        public bool MustPlaceCueBallBeforeShooting =>\n",
                "        public bool MustPlaceCueBallBeforeShooting => false;   // INJ：永不要求放球\n"
                "        private bool Unused_MustPlace() =>\n",
            ),
        ],
    ),
    # ㉔ **把悬浮过渡时长设为 0**（瞬移）⇒ P10-C 的"中途夹在两端之间"必红
    # ㉗ **`StickTipGapMeters = 0`** ⇒ 静止时杆尖贴到球面（P11 判据① 必红）
    "p11-tipgap-zero": (
        "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs",
        [
            (
                "        public const float StickTipGapMeters = 0.002f;\n",
                "        public const float StickTipGapMeters = 0f;   // INJ：静止间隙归零\n",
            ),
        ],
    ),
    # ㉘ **删掉击球前冲**（Shooting 相位不再前进）⇒ 击球帧离球面仍 2 mm（判据② 必红）
    "p11-no-strike-advance": (
        "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs",
        [
            (
                "? StickTipGapMeters : 0f;\n",
                "? 0f : 0f;   // INJ：删掉击球前冲\n",
            ),
        ],
    ),
    # ㉙ **击球前冲过量（2×）** ⇒ 杆尖戳进球里（判据③ 必红）
    "p11-strike-overshoot": (
        "Assets/_Project/Scripts/Runtime/Physics/CueStickController.cs",
        [
            (
                "? StickTipGapMeters : 0f;\n",
                "? StickTipGapMeters * 2f : 0f;   // INJ：前冲过量\n",
            ),
        ],
    ),
    "p10-float-instant": (
        PTD,
        [
            (
                "        public const float CueFloatTransitionSeconds = 0.25f;\n",
                "        // INJ-P10-4：**过渡时长设为 0 ⇒ 瞬移**（预期 P10-C 中途断言必红）\n"
                "        public const float CueFloatTransitionSeconds = 0f;\n",
            ),
        ],
    ),
}


def work_root() -> str:
    temp = os.environ.get("TEMP") or os.environ.get("TMP") or "/tmp"
    return os.path.join(temp, "p6rt_inj")


def backup_path(tag: str) -> str:
    return os.path.join(work_root(), "inj_" + tag, "backup")


def _read_text_preserve_bom(path: str) -> tuple[str, bool]:
    raw = open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    return raw.decode("utf-8-sig"), bom


def _write_text_preserve_bom(path: str, text: str, bom: bool) -> None:
    data = text.encode("utf-8")
    if bom:
        data = b"\xef\xbb\xbf" + data
    # **二进制写**：不碰行尾（本仓库是 LF；用文本模式会被平台翻译成 CRLF，破坏逐位比对）
    with open(path, "wb") as handle:
        handle.write(data)


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in ("apply", "restore"):
        raise SystemExit("用法：p6rt_patch.py {apply|restore} <tag>")

    action, tag = sys.argv[1], sys.argv[2]
    if tag not in PATCHES:
        raise SystemExit("未登记的注入 tag：" + tag)

    relative, edits = PATCHES[tag]
    target = os.path.join(ROOT, relative)
    store = backup_path(tag)

    if action == "restore":
        if not os.path.exists(store):
            raise SystemExit("找不到备份：" + store + "（拒绝在无备份时还原）")
        shutil.copyfile(store, target)
        print("restored", relative, "<-", store)
        return

    if not os.path.exists(target):
        raise SystemExit("找不到目标文件：" + target)

    text, bom = _read_text_preserve_bom(target)
    for anchor, replacement in edits:
        count = text.count(anchor)
        if count != 1:
            raise SystemExit(f"[{tag}] 锚点在 {relative} 里出现 {count} 次（应为 1）⇒ 拒绝注入")
        text = text.replace(anchor, replacement, 1)

    _write_text_preserve_bom(target, text, bom)
    print(f"[{tag}] applied to {relative}（{len(edits)} 处）")


if __name__ == "__main__":
    main()
