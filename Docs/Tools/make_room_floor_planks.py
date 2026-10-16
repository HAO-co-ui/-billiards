"""生成球房**木地板贴图**（`room_floor_planks.png`）—— 由美术包内的真实木纹合成一张**可无缝平铺**的木板图。

━━ 输入 → 输出 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  输入①（**纹理来源**）：`00_交付总览/Playable_Prototype_Reference/dist/assets/wood.jpg`
       —— 美术包自述即 "A flat wood face cropped directly from the real source UV atlas,
          for runtime table frame material."（从真实 UV 图集里裁出的一块**平整木面**）。
          注意：本脚本**不直接读** `.../Runtime_Reference/asset-metadata.json`；该 JSON 的
          `wood.source_crop_uv = {x:[0.22,0.26], y:[0.875,0.904]}` 记录了它是从
          `wooden_floor_tile_3d_model_basecolor` 图集上的哪一小块裁出来的 —— 即 `wood.jpg`
          本身就是"图集派生物"，用它不引入工程外的新美术。
  输入②（**同目录留档的原始图集**）：`Assets/_Project/Art/Textures/Environment/source_floor_atlas_1024.jpg`
       —— 只用于生成**对照预览**（证明"为什么不能直接平铺它"），**不参与**产物纹理的合成。

  输出①（**入库产物**）：`Assets/_Project/Art/Textures/Environment/room_floor_planks.png`（1024²，LFS）
  输出②（**可追溯对照图**）：`Docs/Reference/room_floor_texture_choice.png`
        = 左「原图集 3×3 平铺」/ 右「本产物 3×3 平铺」并排，供后人复核"当初为什么不用原图集"。

━━ 为什么不用原图集直接平铺 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  `source_floor_atlas_1024.jpg` 是**24 块地板 tile 的 UV 打包图集**（板条以各种角度散落、
  带烘焙的接缝与透视），它不是一张"可平铺材质"。实测把整张图 3×3 平铺（见输出②左图）会得到
  **明显重复的杂乱花纹 + 平铺接缝**。参考图要的是**成片的木板地面**，因此按美术包自己的做法
  （`wood.jpg` 就是"从图集裁一块平整木面"）把平整木面**合成为规则木板**。

━━ 为什么是「1024² = 1.5 m 见方」这个尺度 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  本工程地面是一个 **30×30 规范米的 Plane**（`TableVisualSetup.AttachRoom`：默认 10×10 × localScale 3），
  挂在 ×1.2 视觉根下 ⇒ 世界 36 m 见方；材质 `_BaseMap_ST` 取 `36 / 1.5 = 24` 次平铺
  （见 `RoomEnvironmentSetup.ConfigureFloorMaterial` 的 `FloorTileMetres`）。
  1.5 m/次 下：板条长 = 512/1024 × 1.5 = **0.75 m**、宽 = 128/1024 × 1.5 = **0.19 m**
  —— 与真实实木地板的常见规格（板宽 0.1–0.2 m）同量级，肉眼不"巨人化"也不"缩小成瓷砖"。
  贴图 1024² 在 1.5 m 上 ⇒ 约 **683 px/m**，在本工程取景（相机离桌约 2–4 m）下不糊。

━━ 确定性 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  `random.seed(35)` 固定 ⇒ 同一 Pillow 版本下**逐字节可复现**（本脚本产出与入库 PNG 的 sha256 一致）。
  换 Pillow 大版本可能改变 JPEG/PNG 编码字节 ⇒ 若 sha 变了但视觉一致，属**编码差异**，不是逻辑漂移。

用法：
  <managed venv python> Docs/Tools/make_room_floor_planks.py
"""

from __future__ import annotations

import hashlib
import os
import random

from PIL import Image, ImageDraw, ImageEnhance

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# 纹理来源：美术包自述"从真实 UV 图集裁出的平整木面"（**包外**路径，需人工拷进来；本工程已拷 1 份到 Art/Textures/Environment）
GRAIN_SRC = r"F:/工作文件/idea35_台球/00_交付总览/Playable_Prototype_Reference/dist/assets/wood.jpg"
# 工程内留档的原始图集（只用于对照预览）
ATLAS_SRC = os.path.join(ROOT, "Assets", "_Project", "Art", "Textures", "Environment",
                         "source_floor_atlas_1024.jpg")

OUT_TEX = os.path.join(ROOT, "Assets", "_Project", "Art", "Textures", "Environment", "room_floor_planks.png")
OUT_PREVIEW = os.path.join(ROOT, "Docs", "Reference", "room_floor_texture_choice.png")

SIZE = 1024          # 输出边长
ROW_H = 128          # 板条行高（8 行）—— 必须整除 SIZE，否则底边会留未填充黑带（初版踩过这个坑）
PLANK_W = 512        # 板条长度周期（行间按 211 取模错缝）
SEAM = (36, 25, 17)  # 板缝颜色（比木面暗一档）
SEED = 35


def build_planks(grain: Image.Image) -> Image.Image:
    rnd = random.Random(SEED)
    gw, gh = grain.size
    out = Image.new("RGB", (SIZE, SIZE))
    rows = SIZE // ROW_H
    assert SIZE % ROW_H == 0, "ROW_H 必须整除 SIZE，否则底边留黑带"

    for r in range(rows):
        y0 = r * ROW_H
        off = -((r * 211) % PLANK_W)
        x = off
        while x < SIZE:
            cw = rnd.randint(gw // 2, gw)
            ch = rnd.randint(gh // 2, gh)
            cx = rnd.randint(0, gw - cw)
            cy = rnd.randint(0, gh - ch)
            patch = grain.crop((cx, cy, cx + cw, cy + ch)).resize((PLANK_W, ROW_H))
            patch = ImageEnhance.Brightness(patch).enhance(0.90 + rnd.random() * 0.20)
            out.paste(patch, (x, y0))
            x += PLANK_W

    # 板缝：水平缝每行一条；竖直缝按行错开；另外补首行/首列，保证平铺时接缝连续
    d = ImageDraw.Draw(out)
    for r in range(rows):
        y0 = r * ROW_H
        if y0 > 0:
            d.line([(0, y0), (SIZE, y0)], fill=SEAM, width=2)
        off = -((r * 211) % PLANK_W)
        for x in range(off, SIZE + PLANK_W, PLANK_W):
            if 0 < x < SIZE:
                d.line([(x, y0), (x, y0 + ROW_H)], fill=SEAM, width=2)
    d.line([(0, 0), (SIZE, 0)], fill=SEAM, width=2)
    d.line([(0, 0), (0, SIZE)], fill=SEAM, width=2)
    return out


def tiled(img: Image.Image, n: int = 3, cell: int = 256) -> Image.Image:
    t = img.resize((cell, cell))
    out = Image.new("RGB", (cell * n, cell * n))
    for i in range(n):
        for j in range(n):
            out.paste(t, (i * cell, j * cell))
    return out


def main() -> None:
    print(f"木纹来源：{GRAIN_SRC}")
    if not os.path.exists(GRAIN_SRC):
        raise SystemExit("找不到木纹来源 wood.jpg（美术包路径）：" + GRAIN_SRC)
    grain = Image.open(GRAIN_SRC).convert("RGB")

    planks = build_planks(grain)
    os.makedirs(os.path.dirname(OUT_TEX), exist_ok=True)
    planks.save(OUT_TEX)
    sha = hashlib.sha256(open(OUT_TEX, "rb").read()).hexdigest()
    print(f"  {os.path.basename(OUT_TEX)}  {os.path.getsize(OUT_TEX)} bytes  sha256={sha}")

    # 对照预览：左=原图集平铺（发花），右=本产物平铺（木地板）
    if os.path.exists(ATLAS_SRC):
        atlas = Image.open(ATLAS_SRC).convert("RGB")
        left, right = tiled(atlas), tiled(planks)
        gap = 16
        sheet = Image.new("RGB", (left.width + gap + right.width, left.height), (16, 16, 16))
        sheet.paste(left, (0, 0))
        sheet.paste(right, (left.width + gap, 0))
        d = ImageDraw.Draw(sheet)
        d.text((8, 8), "source_floor_atlas_1024.jpg tiled 3x3 (BAD)", fill=(255, 240, 120))
        d.text((left.width + gap + 8, 8), "room_floor_planks.png tiled 3x3 (USED)", fill=(120, 255, 140))
        os.makedirs(os.path.dirname(OUT_PREVIEW), exist_ok=True)
        sheet.save(OUT_PREVIEW)
        print(f"  {os.path.relpath(OUT_PREVIEW, ROOT)}  {os.path.getsize(OUT_PREVIEW)} bytes（对照图）")
    else:
        print("  ⚠ 缺 source_floor_atlas_1024.jpg，跳过对照预览")

    print("完成。")


if __name__ == "__main__":
    main()
