"""把界面精灵与球号贴片拼成**联络表**（一眼看清 18 + 16 张图长什么样）。

为什么要有它：精灵是"程序化生成 + 纯白 + alpha"的位图，单看一张 PNG 看不出对错；
判据只能验"数量 / 名字 / 采样点颜色"，**观感**必须靠人眼 —— 联络表就是给人眼看的那一份。
落在 `Docs/Reports/ui-redo-preview/`（证据目录），不进 `Temp/`（那目录会被 Unity 清）。

用法：<managed venv python> Docs/Tools/make_sprite_sheets.py
"""

from __future__ import annotations

import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ICON_DIR = os.path.join(ROOT, "Assets", "_Project", "UI", "Icons")
BALL_DIR = os.path.join(ROOT, "Assets", "_Project", "UI", "Balls")
OUT_DIR = os.path.join(ROOT, "Docs", "Reports", "ui-redo-preview")

ICON_FILES = [
    "icon-crosshair", "icon-orbit", "icon-hand", "icon-flag", "icon-warning", "icon-robot",
    "icon-pause", "icon-trophy", "icon-house", "icon-arrow-right", "icon-volume", "icon-sparkle",
    "icon-player", "ui-disc", "ui-ring", "ui-panel-1", "ui-panel-2", "ui-panel-3",
]

BACKDROP = (16, 32, 37, 255)      # 炭黑 #102025
LABEL = (212, 180, 108, 255)      # 黄铜 #d4b46c


def sheet(names: list[str], src: str, cols: int, path: str, nearest_for: tuple[str, ...] = ()) -> None:
    cell, label_h = 84, 16
    rows = (len(names) + cols - 1) // cols
    img = Image.new("RGBA", (cols * cell, rows * (cell + label_h)), BACKDROP)
    draw = ImageDraw.Draw(img)

    for i, name in enumerate(names):
        tile = Image.open(os.path.join(src, name + ".png")).convert("RGBA")
        resample = Image.NEAREST if name.startswith(nearest_for) else Image.LANCZOS
        tile = tile.resize((64, 64), resample)
        x = (i % cols) * cell + 10
        y = (i // cols) * (cell + label_h) + 8
        img.alpha_composite(tile, (x, y))
        draw.text((x + 6, y + 66), name.replace("icon-", "").replace("ui-", ""), fill=LABEL)

    os.makedirs(OUT_DIR, exist_ok=True)
    out = os.path.join(OUT_DIR, path)
    img.save(out)
    print(f"  {path}  {os.path.getsize(out)} bytes  {img.size}")


def main() -> None:
    print(f"输出目录：{OUT_DIR}")
    sheet(ICON_FILES, ICON_DIR, 6, "01-ui-icons.png", nearest_for=("ui-panel",))
    sheet([f"ball-{i:02d}" for i in range(16)], BALL_DIR, 8, "02-ball-chips.png")


if __name__ == "__main__":
    main()
