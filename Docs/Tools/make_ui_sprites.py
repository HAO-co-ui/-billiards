"""生成对局界面所需的 UI 精灵（线性图标 + 圆形/圆角形状）。

为什么是"程序化生成"而不是从设计稿切图：
  · 设计稿（`04_UI/Reference_Screens`）是**整屏渲染图**，里面的图标带着背景、字与光照，
    切出来不可复用（半透明边缘会带上桌布绿）。
  · 原型用的是内联 SVG；本工程是 uGUI，没有矢量管线 ⇒ 只能栅格化。
  · 生成是**确定性的**：同样的脚本 + 同样的 Pillow 版本 ⇒ 逐字节相同的 PNG（可复现、可复核）。

约定（消费者 = `ClubUiSprites.cs` 的清单，两边必须一致）：
  · 画布 64×64，透明底；图形**纯白 + alpha**，颜色交给 uGUI 的 `Image.color` 上色。
  · 4× 超采样后 LANCZOS 缩放 ⇒ 边缘抗锯齿，不会出现硬台阶。
  · 圆角矩形另出 1/2/3 px 三个半径（对应设计说明"控件边角保持 1—3 像素"），
    九宫格边框各留 4 px，供 uGUI `Image.type = Sliced` 使用。

用法：
  <managed venv python> Docs/Tools/make_ui_sprites.py
"""

from __future__ import annotations

import os
from PIL import Image, ImageDraw

SS = 4                  # 超采样倍数
SIZE = 64               # 输出边长（像素）
CANVAS = SIZE * SS
STROKE = 5 * SS         # 线性图标笔画宽（输出域 5 px）

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "Assets", "_Project", "UI", "Icons",
)

WHITE = (255, 255, 255, 255)


def new_canvas() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    img = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def finish(img: Image.Image, name: str) -> None:
    out = img.resize((SIZE, SIZE), Image.LANCZOS)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    out.save(path)
    print(f"  {name}.png  {os.path.getsize(path)} bytes")


def P(x: float, y: float) -> tuple[float, float]:
    """把 0—64 的设计坐标换算到超采样画布坐标。"""
    return (x * SS, y * SS)


def circle(draw: ImageDraw.ImageDraw, cx: float, cy: float, r: float, width: float = STROKE) -> None:
    x, y = P(cx, cy)
    rr = r * SS
    draw.ellipse([x - rr, y - rr, x + rr, y + rr], outline=WHITE, width=int(round(width)))


def disc(draw: ImageDraw.ImageDraw, cx: float, cy: float, r: float) -> None:
    x, y = P(cx, cy)
    rr = r * SS
    draw.ellipse([x - rr, y - rr, x + rr, y + rr], fill=WHITE)


def line(draw: ImageDraw.ImageDraw, x1: float, y1: float, x2: float, y2: float,
         width: float = STROKE) -> None:
    draw.line([P(x1, y1), P(x2, y2)], fill=WHITE, width=int(round(width)), joint="curve")


def dashed_circle(draw: ImageDraw.ImageDraw, cx: float, cy: float, r: float,
                  segments: int = 8, arc_fraction: float = 0.62) -> None:
    """虚线圆 —— 设计稿里 `moving` 状态的图标（原型为 CSS 旋转虚线圆）。"""
    step = 360.0 / segments
    span = step * arc_fraction
    for i in range(segments):
        start = i * step
        draw.arc(
            [P(cx - r, cy - r)[0], P(cx - r, cy - r)[1], P(cx + r, cy + r)[0], P(cx + r, cy + r)[1]],
            start=start, end=start + span, fill=WHITE, width=int(round(STROKE)),
        )


def round_rect(draw: ImageDraw.ImageDraw, x0: float, y0: float, x1: float, y1: float,
               radius: float, width: float = STROKE) -> None:
    a = P(x0, y0)
    b = P(x1, y1)
    draw.rounded_rectangle([a[0], a[1], b[0], b[1]], radius=radius * SS, outline=WHITE,
                           width=int(round(width)))


def filled_round_rect(draw: ImageDraw.ImageDraw, x0: float, y0: float, x1: float, y1: float,
                      radius: float) -> None:
    a = P(x0, y0)
    b = P(x1, y1)
    draw.rounded_rectangle([a[0], a[1], b[0], b[1]], radius=radius * SS, fill=WHITE)


# ══════════════════════════ 线性图标 ══════════════════════════

def icon_crosshair() -> None:
    img, d = new_canvas()
    circle(d, 32, 32, 19)
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        line(d, 32 + dx * 23, 32 + dy * 23, 32 + dx * 30, 32 + dy * 30)
    disc(d, 32, 32, 2.5)
    finish(img, "icon-crosshair")


def icon_orbit() -> None:
    img, d = new_canvas()
    dashed_circle(d, 32, 32, 21)
    finish(img, "icon-orbit")


def icon_hand() -> None:
    img, d = new_canvas()
    # 掌
    round_rect(d, 20, 26, 44, 54, 9)
    # 四指
    for x in (23.5, 30.5, 37.5):
        round_rect(d, x, 12, x + 6.5, 30, 3.2, STROKE * 0.86)
    round_rect(d, 43, 16, 49.5, 30, 3.2, STROKE * 0.86)
    # 拇指
    round_rect(d, 10, 32, 20.5, 42, 5, STROKE * 0.86)
    finish(img, "icon-hand")


def icon_flag() -> None:
    img, d = new_canvas()
    line(d, 20, 10, 20, 55)
    d.polygon([P(20, 12), P(50, 22), P(20, 32)], fill=WHITE)
    finish(img, "icon-flag")


def icon_warning() -> None:
    img, d = new_canvas()
    r = 22.0
    top = P(32, 32 - r)
    left = P(32 - r * 0.92, 32 + r * 0.78)
    right = P(32 + r * 0.92, 32 + r * 0.78)
    d.line([top, left, right, top], fill=WHITE, width=int(round(STROKE)), joint="curve")
    line(d, 32, 22, 32, 39)
    disc(d, 32, 46, 3)
    finish(img, "icon-warning")


def icon_robot() -> None:
    img, d = new_canvas()
    round_rect(d, 15, 22, 49, 50, 8)
    line(d, 32, 22, 32, 12)
    disc(d, 32, 9.5, 3.5)
    disc(d, 25, 34, 3.4)
    disc(d, 39, 34, 3.4)
    line(d, 25, 43, 39, 43, STROKE * 0.86)
    finish(img, "icon-robot")


def icon_pause() -> None:
    img, d = new_canvas()
    filled_round_rect(d, 21, 14, 29, 50, 3)
    filled_round_rect(d, 35, 14, 43, 50, 3)
    finish(img, "icon-pause")


def icon_trophy() -> None:
    img, d = new_canvas()
    # 杯身：上宽下收（用多段线近似圆弧）
    pts = [P(19, 16), P(45, 16), P(42, 33), P(36, 39), P(28, 39), P(22, 33), P(19, 16)]
    d.line(pts, fill=WHITE, width=int(round(STROKE)), joint="curve")
    # 两侧耳
    d.arc([P(6, 16)[0], P(16, 16)[1], P(20, 34)[0], P(34, 34)[1]], 90, 270,
          fill=WHITE, width=int(round(STROKE)))
    d.arc([P(44, 16)[0], P(16, 16)[1], P(58, 34)[0], P(34, 34)[1]], 270, 90,
          fill=WHITE, width=int(round(STROKE)))
    # 柱与底座
    line(d, 32, 39, 32, 47)
    line(d, 21, 50, 43, 50, STROKE * 1.06)
    finish(img, "icon-trophy")


def icon_house() -> None:
    img, d = new_canvas()
    d.line([P(11, 31), P(32, 13), P(53, 31)], fill=WHITE, width=int(round(STROKE)), joint="curve")
    line(d, 16, 29, 16, 51)
    line(d, 48, 29, 48, 51)
    line(d, 14, 51, 50, 51)
    round_rect(d, 27, 38, 37, 51, 1.6, STROKE * 0.8)
    finish(img, "icon-house")


def icon_arrow_right() -> None:
    img, d = new_canvas()
    line(d, 14, 32, 48, 32)
    d.line([P(35, 19), P(50, 32), P(35, 45)], fill=WHITE, width=int(round(STROKE)), joint="curve")
    finish(img, "icon-arrow-right")


def icon_volume() -> None:
    img, d = new_canvas()
    d.polygon([P(14, 26), P(24, 26), P(34, 16), P(34, 48), P(24, 38), P(14, 38)], fill=WHITE)
    d.arc([P(28, 18)[0], P(18, 18)[1], P(56, 46)[0], P(46, 46)[1]], -55, 55,
          fill=WHITE, width=int(round(STROKE)))
    d.arc([P(30, 10)[0], P(10, 10)[1], P(62, 54)[0], P(54, 54)[1]], -50, 50,
          fill=WHITE, width=int(round(STROKE * 0.8)))
    finish(img, "icon-volume")


def icon_sparkle() -> None:
    img, d = new_canvas()
    d.polygon([P(32, 8), P(38, 26), P(56, 32), P(38, 38), P(32, 56), P(26, 38), P(8, 32), P(26, 26)],
              fill=WHITE)
    finish(img, "icon-sparkle")


def icon_player() -> None:
    img, d = new_canvas()
    circle(d, 32, 24, 10.5)
    d.arc([P(11, 34)[0], P(34, 34)[1], P(53, 62)[0], P(62, 62)[1]], 180, 360,
          fill=WHITE, width=int(round(STROKE)))
    finish(img, "icon-player")


# ══════════════════════════ 形状（圆 / 圆角） ══════════════════════════

def shape_disc() -> None:
    img, d = new_canvas()
    disc(d, 32, 32, 32)
    finish(img, "ui-disc")


def shape_ring() -> None:
    img, d = new_canvas()
    circle(d, 32, 32, 30.5, 2 * SS)
    finish(img, "ui-ring")


def shape_panel(radius: int) -> None:
    img, d = new_canvas()
    # 九宫格：32×32 输出、边框各 4 px；圆角在三处统一为 radius（1/2/3）。
    # 超采样画布上按同比例放大（8 × SS），保证与输出的 4 px 边框一致。
    size = 32 * SS // 2      # 64 输出像素的一半 -> 32 输出像素
    scale = SS // 2 if SS >= 2 else 1
    # 直接以输出域 32×32 为基准绘制，再放大到超采样画布
    pad = 0
    a = (pad * SS, pad * SS)
    b = (a[0] + 32 * SS - 1, a[1] + 32 * SS - 1)
    d.rounded_rectangle([a[0], a[1], b[0], b[1]], radius=radius * SS, fill=WHITE)
    del size, scale
    out = img.crop((0, 0, 32 * SS, 32 * SS)).resize((32, 32), Image.LANCZOS)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, f"ui-panel-{radius}.png")
    out.save(path)
    print(f"  ui-panel-{radius}.png  {os.path.getsize(path)} bytes")


def main() -> None:
    print(f"输出目录：{OUT_DIR}")
    for fn in (icon_crosshair, icon_orbit, icon_hand, icon_flag, icon_warning, icon_robot,
               icon_pause, icon_trophy, icon_house, icon_arrow_right, icon_volume,
               icon_sparkle, icon_player, shape_disc, shape_ring):
        fn()
    for radius in (1, 2, 3):
        shape_panel(radius)
    print("完成。")


if __name__ == "__main__":
    main()
