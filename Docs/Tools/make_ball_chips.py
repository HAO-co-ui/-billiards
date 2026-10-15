"""生成记分牌用的迷你球贴片（`ball-00.png` … `ball-15.png`）。

为什么单独出贴片，而不是把球的 1024×512 贴图缩小：
  · 球贴图是**球面 UV 展开**（极点密、赤道疏），缩到 19 px 后号码会糊成一团；
  · 记分牌要的是**"一眼认出是几号球"的图形符号**，不是球的真实外观；
  · 设计稿的 `.mini-ball` 本身就是 CSS 画出来的（纯色圆 / 三段渐变圆），不是贴图 ——
    这里按同一几何复刻，形态与设计稿一致。

与设计稿 `style.css` `.mini-ball` 的对应：
  · 纯色球（1–8）：整圆填球色 + 白色号码（设计稿 `background: var(--ball); color: #fff`）；
  · 花色球（9–15）：`linear-gradient(#f2efe4 22%, var(--ball) 22%, var(--ball) 78%, #f2efe4 78%)`
    ⇒ 上下各 22% 暖白、中间 56% 球色；号码落在中段；
  · 内阴影 `inset -2px -2px 3px #0005` 与 1px 高光边 `#ffffff40` 用同比例的明暗近似
    （uGUI 无 box-shadow ⇒ 明暗必须烘进贴片，这点与图标不同）。

号码字体：用工程里已有的 `BarlowCondensed-Bold.ttf`（= 设计稿的标题族）⇒ 与界面同族，不引新字体。

用法：
  <managed venv python> Docs/Tools/make_ball_chips.py
"""

from __future__ import annotations

import os

from PIL import Image, ImageChops, ImageDraw, ImageFont

SS = 4
SIZE = 64
CANVAS = SIZE * SS

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "UI", "Balls")
FONT_PATH = os.path.join(ROOT, "Assets", "_Project", "UI", "Fonts", "BarlowCondensed-Bold.ttf")

WARM_WHITE = (242, 239, 228, 255)
NUMBER_WHITE = (255, 255, 255, 255)

# 球号 → RGB。与美术包 `03_Models_模型/Balls/Editable/README.md` 给的球色一致
# （0 米白 / 1·9 黄 / 2·10 蓝 / 3·11 红 / 4·12 紫 / 5·13 橙 / 6·14 绿 / 7·15 深红棕 / 8 黑）。
PALETTE: dict[int, tuple[int, int, int]] = {
    0: (0xF5, 0xF3, 0xE7),
    1: (0xE4, 0xB5, 0x2A),
    2: (0x25, 0x6D, 0xC3),
    3: (0xB9, 0x32, 0x31),
    4: (0x77, 0x43, 0x9C),
    5: (0xE8, 0x79, 0x26),
    6: (0x1F, 0x93, 0x61),
    7: (0x85, 0x3A, 0x35),
    8: (0x11, 0x1B, 0x20),
    9: (0xE4, 0xB5, 0x2A),
    10: (0x25, 0x6D, 0xC3),
    11: (0xB9, 0x32, 0x31),
    12: (0x77, 0x43, 0x9C),
    13: (0xE8, 0x79, 0x26),
    14: (0x1F, 0x93, 0x61),
    15: (0x85, 0x3A, 0x35),
}

RIM = (255, 255, 255, 0x40)   # 设计稿 `.mini-ball { border: 1px solid #ffffff40 }`


def is_striped(number: int) -> bool:
    return 9 <= number <= 15


def circle_mask(radius_px: float) -> Image.Image:
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    d = ImageDraw.Draw(mask)
    c = CANVAS / 2.0
    d.ellipse([c - radius_px, c - radius_px, c + radius_px, c + radius_px], fill=255)
    return mask


def inner_shade(mask: Image.Image) -> Image.Image:
    """右下内阴影：设计稿 `inset -2px -2px 3px #0005` 的等比例近似。"""
    shade = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    d = ImageDraw.Draw(shade)
    offset = 3 * SS
    for i in range(6 * SS, 0, -1):
        alpha = int(0x50 * (1.0 - i / (6.0 * SS)) ** 2)
        if alpha <= 0:
            continue
        c = CANVAS / 2.0 + offset
        r = CANVAS / 2.0 - 2 * SS + i
        d.ellipse([c - r, c - r, c + r, c + r], fill=(0, 0, 0, alpha))
    out = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    out.paste(shade, (0, 0), mask)
    return out


def number_font(size_px: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(FONT_PATH, size_px * SS)


def draw_number(img: Image.Image, number: int, color: tuple[int, int, int, int]) -> None:
    if number <= 0:
        return
    d = ImageDraw.Draw(img)
    font = number_font(30)
    text = str(number)
    box = d.textbbox((0, 0), text, font=font)
    w = box[2] - box[0]
    h = box[3] - box[1]
    c = CANVAS / 2.0
    d.text((c - w / 2.0 - box[0], c - h / 2.0 - box[1] - 1.5 * SS), text, font=font, fill=color)


def build(number: int) -> Image.Image:
    rgb = PALETTE[number]
    colour = (rgb[0], rgb[1], rgb[2], 255)
    radius = CANVAS / 2.0 - 2 * SS
    mask = circle_mask(radius)

    base = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    d = ImageDraw.Draw(base)
    c = CANVAS / 2.0

    if is_striped(number):
        d.ellipse([c - radius, c - radius, c + radius, c + radius], fill=WARM_WHITE)

        # ⚠ **必须是 `alpha_composite`（合成），不能是 `paste`（替换）**。
        # `paste(src, box, mask)` 的语义是"在 mask 非零处用 src 的像素**替换**目标像素" ——
        # 而色带图在矩形之外是**全透明**的，于是圆内、色带之外的部分会被**换成一整片透明**，
        # 白底被抹掉 ⇒ 花色球在记分牌上只剩中间一条色带（实测：判据 `T124 ⑥` 采样到
        # alpha=74 的透明黑，而不是暖白）。`alpha_composite` 才是"把色带盖到白底上"。
        band = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        ImageDraw.Draw(band).rectangle([0, CANVAS * 0.22, CANVAS, CANVAS * 0.78], fill=colour)
        band.putalpha(ImageChops.multiply(band.getchannel("A"), mask))   # 裁到圆内
        base = Image.alpha_composite(base, band)
    else:
        d.ellipse([c - radius, c - radius, c + radius, c + radius], fill=colour)

    base = Image.alpha_composite(base, inner_shade(mask))

    # 1 px 高光边（设计稿的 `border: 1px solid #ffffff40`）
    rim = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rim)
    rd.ellipse([c - radius, c - radius, c + radius, c + radius], outline=RIM, width=int(1.2 * SS))
    base = Image.alpha_composite(base, rim)

    draw_number(base, number, NUMBER_WHITE)
    return base.resize((SIZE, SIZE), Image.LANCZOS)


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for number in range(0, 16):
        img = build(number)
        path = os.path.join(OUT_DIR, f"ball-{number:02d}.png")
        img.save(path)
        print(f"  ball-{number:02d}.png  {os.path.getsize(path)} bytes")
    print("完成。")


if __name__ == "__main__":
    main()
