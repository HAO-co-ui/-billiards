"""P6-UI 的注入补丁（**只做文本替换**；备份 / 基线比对 / 还原 / sha1 回读由
`Docs/Tools/p6ui_injections.sh` 负责）。

为什么把补丁与流程分开：本项目踩过"注入态留下脏源码"的事故（`local tag="$1"` 那一次），
所以**流程**（备份在仓外 + apply 前比基线 + 跑完立刻还原 + 还原后回读 sha1）必须是**机械的**，
而"改哪几行"是**内容**问题、单独放这里便于逐条审阅。

用法：<managed venv python> Docs/Tools/p6ui_patch.py {apply|restore} <tag>
"""

from __future__ import annotations

import io
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# tag -> (相对路径, [(anchor, replacement), ...])
#
# ⚠ 每条的 anchor 必须**恰好出现一次**（脚本会断言）—— 出现 0 次说明"锚点漂了"，
#   出现多次说明"注入了不止一处"。两种都直接报错退出，**不猜**。
PATCHES = {
    # ① 把两形态**互换**（胜局得到负局形态、负局得到胜局形态）
    "form-swap": (
        r"Assets\_Project\Scripts\Runtime\UI\MatchResultCopy.cs",
        [
            (
                "return winner == ShotTurn.Player ? MatchResultForm.Win : MatchResultForm.Lose;",
                "return winner == ShotTurn.Player ? MatchResultForm.Lose : MatchResultForm.Win;",
            ),
        ],
    ),
    # ② **面板打开时不禁输入**：把结算卡从蓄力让位门里去掉（只剩首页）
    "input-gate": (
        r"Assets\_Project\Scripts\Runtime\Physics\PointerPowerController.cs",
        [
            (
                "if (Billiards.UI.PracticeResultView.IsOpen || Billiards.UI.HomeView.IsOpen)",
                "if (Billiards.UI.HomeView.IsOpen)",
            ),
        ],
    ),
    # ③ **去掉"弹一次"闸门**：视图不再问 Runtime 的显式标志，改成"只要结束了就弹"
    #    （⇒ 关掉卡以后每帧重弹；这就是"UI 自己记状态"那一类缺陷的形态）
    "one-pop": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeResultView.cs",
        [
            (
                "            if (drive.TryConsumeSettlement())\n"
                "            {\n"
                "                ShowMatchResult(MatchResultCopy.FormFor(drive.Winner), drive.EndReason);\n"
                "                return;\n"
                "            }",
                "            if (drive.MatchOver && !IsOpen)\n"
                "            {\n"
                "                ShowMatchResult(MatchResultCopy.FormFor(drive.Winner), drive.EndReason);\n"
                "                return;\n"
                "            }",
            ),
        ],
    ),
    # ④ **去掉"结算边界"的记录**：结算发生了但**不记历史** ⇒ 首页三项统计不涨（AI 对局尤其明显）
    "record-drop": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeResultView.cs",
        [
            (
                "            RecordRackAtSettlement(form);\n",
                "",
            ),
        ],
    ),
    # ⑤ **把记录口改成"从空历史起算"**（复现旧口径的**覆盖**缺陷）
    #    ⇒ 每局都把文件覆盖成单条历史 ⇒ 首页 `RACKS` 永远最多 1（统计静默做死）
    "record-overwrite": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeSessionStore.cs",
        [
            (
                "            string merged = AppendToHistory(ToHistoryJson(ReadHistoryFromDisk()), record);",
                "            string merged = AppendToHistory(string.Empty, record);",
            ),
        ],
    ),
    # ⑥ **删掉 `ai-turn` 那一支**：登记里仍说它可达，但状态求解**产不出它**
    #    ⇒ 对手回合期间状态条回落成 `aim`（假陈述"轮到你了"）
    "ai-turn-drop": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeHudState.cs",
        [
            (
                "            if (aiTurn)\n"
                "            {\n"
                "                return HudVisualState.AiTurn;\n"
                "            }\n"
                "\n",
                "",
            ),
        ],
    ),
    # ⑦ **把"只有打黑 8 才进选袋"改成恒真**：普通杆也叫你选袋（整个游戏卡在选袋上）
    "callpocket-always": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeHudView.cs",
        [
            (
                "bool callPocket = drive != null && drive.IsEightBallShot && !drive.IsAiTurn;",
                "bool callPocket = drive != null;",
            ),
        ],
    ),
    # ⑧ **删掉"被拒上升沿"的消费**：打黑 8 未指定袋被拒 ⇒ 玩家扣了扳机**没有任何反馈**（静默）
    "callpocket-silent": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeHudView.cs",
        [
            (
                "                if (drive.RefusedCallPocketCount > _lastCallPocketRefusedSeen)\n"
                "                {\n"
                "                    _lastCallPocketRefusedSeen = drive.RefusedCallPocketCount;\n"
                "                    _callPocketRefusedUntil = now + FoulBannerSeconds;\n"
                "                }\n"
                "\n",
                "",
            ),
        ],
    ),
    # ⑨ **默认档漂移**（QA 修复轮 A1）：Runtime 侧默认档 `Club` → `Casual`
    #    ⇒ 两条独立字面量（UI 侧 / Runtime 侧各一条）都必须红 —— 这是"两处默认不得漂移"的**机器形态**
    "p7-default-drift": (
        r"Assets\_Project\Scripts\Runtime\Rules\EightBallAi.cs",
        [
            (
                "public const AiDifficulty DefaultDifficulty = AiDifficulty.Club;",
                "public const AiDifficulty DefaultDifficulty = AiDifficulty.Casual;",
            ),
        ],
    ),
    # ⑩ **档位名文案漂移**（QA 修复轮 A2）：Casual 的显示名 `Casual` → `Rookie`
    #    ⇒ 字面量断言必须红（旧写法 `OpponentName ≡ TierLabel + 后缀` 恒真、抓不到）
    "p7-tier-name": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeHudState.cs",
        [
            (
                "case AiDifficulty.Casual: return HomeDifficultyCasual;",
                "case AiDifficulty.Casual: return \"Rookie\";",
            ),
        ],
    ),
    # ⑪ **配色出现第二个真源**（QA 修复轮 A3）：视图顶部色条绕过 accessor、直接引令牌
    #    ⇒ 单一真源断言必须红（负局会拿黄铜条 = 形态只差在文字上）
    "p7-color-single-source": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeResultView.cs",
        [
            (
                "                _topEdge.color = MatchResultCopy.EdgeColor(win);",
                "                _topEdge.color = ClubUiTokens.ResultWinEdge;",
            ),
        ],
    ),
    # ⑫ **胜负映射反了**（QA 修复轮 A4）：`FormFor` 无条件返回 `Win`
    #    ⇒ 胜因表的"只有胜因能落到 Win"必须红（旧写法 `reasons[Legal] != ReasonLegal…` 恒假、永不红）
    "p7-win-cause": (
        r"Assets\_Project\Scripts\Runtime\UI\MatchResultCopy.cs",
        [
            (
                "            return winner == ShotTurn.Player ? MatchResultForm.Win : MatchResultForm.Lose;",
                "            return MatchResultForm.Win;",
            ),
        ],
    ),
    # ⑬ **三档次序被调换**（QA 修复轮 B5：T144 注释里承诺的那条注入，此前**脚本里没有**）
    "p7-tier-order": (
        r"Assets\_Project\Scripts\Runtime\UI\MatchSetup.cs",
        [
            (
                "            AiDifficulty.Casual,\n            AiDifficulty.Club,\n            AiDifficulty.Expert,",
                "            AiDifficulty.Club,\n            AiDifficulty.Casual,\n            AiDifficulty.Expert,",
            ),
        ],
    ),
    # ⑭ **选袋槽位数被改小**（QA 修复轮 B10）：6 → 5
    #    ⇒ "槽位数 == 6"与"== TableConfig.PocketCount"两条独立断言必须红（旧写法是自指的、跟着绿）
    "p7-slot-count": (
        r"Assets\_Project\Scripts\Runtime\UI\PracticeHudLayout.cs",
        [
            (
                "        public const int CallPocketSlots = 6;",
                "        public const int CallPocketSlots = 5;",
            ),
        ],
    ),
}


def backup_path(tag: str) -> str:
    temp = os.environ.get("TEMP") or os.environ.get("TMP") or "/tmp"
    return os.path.join(temp, "homeui_p6", "inj_" + tag, "backup")


def main() -> None:
    if len(sys.argv) != 3 or sys.argv[1] not in ("apply", "restore"):
        raise SystemExit("用法：p6ui_patch.py {apply|restore} <tag>")

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

    text = io.open(target, encoding="utf-8-sig").read()
    for anchor, replacement in edits:
        count = text.count(anchor)
        if count != 1:
            raise SystemExit(f"[{tag}] 锚点在 {relative} 里出现 {count} 次（应为 1）⇒ 拒绝注入")
        text = text.replace(anchor, replacement)

    io.open(target, "w", encoding="utf-8-sig").write(text)
    print(f"[{tag}] applied to {relative}（{len(edits)} 处）")


if __name__ == "__main__":
    main()
