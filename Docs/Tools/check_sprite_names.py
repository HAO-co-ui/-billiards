"""核对**磁盘上的精灵文件名**与 C# 清单通过 `ToKebab` 推出的期望名是否逐字一致。

为什么需要这一步（实测教训）：生成器是 Python、清单在 C#，两边各有一套"名字怎么拼"的规则。
`Panel3` 在 Python 侧是 `ui-panel-3.png`，而 C# 侧第一版 `ToKebab` 漏了「字母→数字插连字符」
⇒ 期望名变成 `ui-panel3.png` ⇒ **场景构建直接抛异常**（构建期拦截生效，这本身是对的，
但它意味着"生成器与清单已经分叉"，而这本该在跑 Unity **之前**就被发现）。
本脚本 = 把这条一致性检查前移到离线，避免为一次拼写错误重跑几分钟的 Unity。

用法：<managed venv python> Docs/Tools/check_sprite_names.py
"""

from __future__ import annotations

import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 与 ClubUiSprites.UiSprite 的清单**同序**（0..17）
ICON_NAMES = [
    "Crosshair", "Orbit", "Hand", "Flag", "Warning", "Robot", "Pause", "Trophy",
    "House", "ArrowRight", "Volume", "Sparkle", "Player",
]
SHAPE_NAMES = ["Disc", "Ring", "Panel1", "Panel2", "Panel3"]

ICON_DIR = os.path.join(ROOT, "Assets", "_Project", "UI", "Icons")
BALL_DIR = os.path.join(ROOT, "Assets", "_Project", "UI", "Balls")


def to_kebab(pascal: str) -> str:
    """与 `ClubUiSprites.ToKebab` 逐行等价的 Python 复刻（含"字母→数字插连字符"）。"""
    out = []
    for i, c in enumerate(pascal):
        if c.isdigit():
            if i > 0 and pascal[i - 1].isalpha():
                out.append("-")
            out.append(c)
        elif c.isupper():
            if i > 0 and pascal[i - 1] != "-":
                out.append("-")
            out.append(c.lower())
        else:
            out.append(c)
    return "".join(out)


def expected() -> list[str]:
    names = [f"icon-{to_kebab(n)}.png" for n in ICON_NAMES]
    names += [f"ui-{to_kebab(n)}.png" for n in SHAPE_NAMES]
    return names


def main() -> None:
    failures: list[str] = []

    want_icons = expected()
    have_icons = {f for f in os.listdir(ICON_DIR) if f.endswith(".png")} if os.path.isdir(ICON_DIR) else set()

    for name in want_icons:
        if name not in have_icons:
            failures.append(f"缺文件：{os.path.join('UI/Icons', name)}")
    for name in sorted(have_icons - set(want_icons)):
        failures.append(f"多出的文件（清单里没有）：{os.path.join('UI/Icons', name)}")

    want_balls = [f"ball-{i:02d}.png" for i in range(16)]
    have_balls = {f for f in os.listdir(BALL_DIR) if f.endswith(".png")} if os.path.isdir(BALL_DIR) else set()
    for name in want_balls:
        if name not in have_balls:
            failures.append(f"缺文件：{os.path.join('UI/Balls', name)}")
    for name in sorted(have_balls - set(want_balls)):
        failures.append(f"多出的文件（清单里没有）：{os.path.join('UI/Balls', name)}")

    if failures:
        print("FAIL：")
        for item in failures:
            print("  " + item)
        raise SystemExit(1)

    print(f"OK  界面精灵 {len(want_icons)} 个 + 球贴片 {len(want_balls)} 个，文件名与清单逐字一致")
    print(f"    样例：{want_icons[0]} / {want_icons[13]} / {want_icons[-1]} / {want_balls[0]} / {want_balls[-1]}")


if __name__ == "__main__":
    main()
