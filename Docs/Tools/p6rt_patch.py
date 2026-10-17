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
    # ④ 删掉「**未指定袋 ⇒ 拒杆**」这道门 ⇒ 打黑 8 不选袋也能出杆
    "callpocket-gate-drop": (
        PTD,
        [
            (
                "            if (IsEightBallShot && ruleState.calledPocket < 0)\n"
                "            {\n"
                "                RefusedCallPocketCount++;\n"
                "                return Vector2.zero;\n"
                "            }\n",
                "            // INJ-P6RT-4：**删掉「未指定袋 ⇒ 拒杆」这道门**（预期指定袋判据 ③ 必红）\n",
            ),
        ],
    ),
    # ⑤ 把「黑 8 进了哪个袋」写死为 -1 ⇒ 规则层判不出"进错袋"
    "eightpocket-freeze": (
        RULES,
        [
            (
                "            facts.eightBallPocket = (afterPocketIndex != null && afterPocketIndex.Count > EightBall)\n"
                "                ? (facts.pottedBalls.Contains(EightBall) ? afterPocketIndex[EightBall] : -1)\n"
                "                : -1;\n",
                "            // INJ-P6RT-5：**把「黑 8 进了哪个袋」写死为 -1**（预期指定袋判据 ② 必红）\n"
                "            facts.eightBallPocket = -1;\n",
            ),
        ],
    ),
    # ⑥ 删掉「**AI 打黑 8 自己指定袋**」那一步 ⇒ AI 撞上"未指定 ⇒ 拒杆"闸门 ⇒ 对局卡死
    "ai-callpocket-drop": (
        PTD,
        [
            (
                "            if (IsEightBallShot)\n"
                "            {\n"
                "                if (intent.pocketIndex < 0 || !TryCallPocket(intent.pocketIndex))\n"
                "                {\n"
                "                    return Vector2.zero;   // 袋号非法（不该发生）⇒ 不出杆，绝不带着错的指定袋开打\n"
                "                }\n"
                "            }\n",
                "            // INJ-P6RT-6：**删掉\"AI 打黑 8 自己指定袋\"这一步**（预期 AI 判据 ① 必红 = AI 被闸门卡死）\n",
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
