"""「美术接入 + UI 重做」判据族的**注入式反例**补丁（铁律 11：诊断与处方分别验证）。

为什么补丁单独成文件、不内联进 shell：
补丁片段里有中文与引号，`bash` 的 heredoc 会吃掉反斜杠、同名分隔符还会让外层 heredoc 提前结束
（本工程已踩过）⇒ 一律写成独立 `.py`，由 `artui_injections.sh` 调用。

每档只做**一处最小改动**，且必须让**指定的那条判据实测红**；跑完立刻用 `cp` 还原并核对 sha1。
用法（由 shell 调用）：python artui_patch.py <v1|v2|v3|v4|v5|v6>
"""

from __future__ import annotations

import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

DRIVE = os.path.join(ROOT, "Assets", "_Project", "Scripts", "Runtime", "Physics", "PhysicsTestDrive.cs")
SETUP = os.path.join(ROOT, "Assets", "_Project", "Scripts", "Editor", "Visual", "GreyboxSceneSetup.cs")
TOKENS = os.path.join(ROOT, "Assets", "_Project", "Scripts", "Runtime", "UI", "ClubUiTokens.cs")
HUD = os.path.join(ROOT, "Assets", "_Project", "Scripts", "Runtime", "UI", "PracticeHudView.cs")
STATE = os.path.join(ROOT, "Assets", "_Project", "Scripts", "Runtime", "UI", "PracticeHudState.cs")
VFX = os.path.join(ROOT, "Assets", "_Project", "Scripts", "Runtime", "Physics", "PoolVfxView.cs")


def patch(path: str, old: str, new: str) -> None:
    text = io.open(path, encoding="utf-8").read()
    if old not in text:
        raise SystemExit(f"注入失败：在 {os.path.basename(path)} 里找不到锚点 -> {old[:70]!r}")
    text = text.replace(old, new, 1)
    io.open(path, "w", encoding="utf-8", newline="").write(text)
    print(f"  已注入 {os.path.basename(path)}")


def v1() -> None:
    """球数退回 3 ⇒ `T122 ①` 应红（"没弄全"的原始缺陷）。"""
    patch(DRIVE, "public const int RackBallCount = 15;", "public const int RackBallCount = 3;")


def v2() -> None:
    """逐号材质退回"全员套白球材质" ⇒ `T122 ③` 应红（"全白"的原始缺陷）。"""
    patch(SETUP, "BallMaterialPathFormat, ballNumber));", "BallMaterialPathFormat, 0));")


def v3() -> None:
    """主色黄铜改成取色器读数 `#d2b46c`（与设计文档 `#d4b46c` 差 1 个最低位）⇒ `T124 ①` 应红。"""
    patch(TOKENS, "public static readonly Color32 Accent = Rgb(0xD4B46C);",
          "public static readonly Color32 Accent = Rgb(0xD2B46C);")


def v4() -> None:
    """在视图里写一处颜色字面量 ⇒ `T124 ④` 应红（"单一真源"退化）。"""
    anchor = "        private bool _built;"
    injected = ("        private static readonly Color InjectedBadColour = new Color(1f, 0f, 0f, 1f);\n\n"
                + anchor)
    patch(HUD, anchor, injected)


def v5() -> None:
    """指定球袋的指示色借成黄铜 ⇒ `T125 ④` 应红（设计说明：亮青只用于指定球袋）。"""
    patch(STATE, "case HudVisualState.CallPocket: return ClubUiTokens.CallPocketSymbol;",
          "case HudVisualState.CallPocket: return ClubUiTokens.Accent;")


def v6() -> None:
    """犯规环时长改成 0.6 s ⇒ `T126 ①` 应红（设计说明：0.38 秒）。"""
    patch(VFX, "public const float FoulRingDurationSeconds = 0.38f;",
          "public const float FoulRingDurationSeconds = 0.6f;")


CASES = {"v1": v1, "v2": v2, "v3": v3, "v4": v4, "v5": v5, "v6": v6}


def main() -> None:
    if len(sys.argv) != 2 or sys.argv[1] not in CASES:
        raise SystemExit("用法：python artui_patch.py <v1|v2|v3|v4|v5|v6>")
    CASES[sys.argv[1]]()


if __name__ == "__main__":
    main()
