"""QA 复核工具：**渲染证据非退化检查**（只读文件 + 离线解码，不需要 Unity）。

动机（本工程实测事故，2026-10-16）：`day3` 的四张渲染证据图在某次"同批刷新证据"后
**全部退化成逐字节相同的纯灰图**（单一颜色覆盖 100% 像素），而
  · `RND-01` 只查"4 张落盘"（存在即通过）；
  · `NAB-01` 硬编码 `Ok`（永不失败）；
⇒ **两条判据在"空证据"上照样报绿**（"绿而空"：红灯有人查，空证据上的绿灯没人查）。

本工具把"证据不能为空"变成可判定：
  1. 四张图**存在**且**可解码**；
  2. 每张图**非退化**（`distinctColors > 1` —— 不是单一底色）；
  3. `ab-asis.png` 与 `ab-flipgreen.png` **必须有差异**（差异像素占比 > 0；
     两版相同 ⇒ A/B 证据失效 ⇒ FAIL）。

用法：
  python Docs/Tools/qa_render_evidence_check.py                 # 查 Docs/Reports（默认）
  python Docs/Tools/qa_render_evidence_check.py --dir <path>
  python Docs/Tools/qa_render_evidence_check.py --selftest      # 自证本工具"抓得住空图"

退出码：0 = 全部通过；1 = 有失败（含"证据为空/退化"）。
"""

from __future__ import annotations

import argparse
import hashlib
import io
import os
import sys
import tempfile
from collections import Counter

try:
    from PIL import Image
except Exception as exc:  # pragma: no cover
    print(f"需要 Pillow 才能解码 PNG：{exc}", file=sys.stderr)
    raise SystemExit(2)

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

EXPECTED = [
    "day3-top-view.png",
    "day3-perspective.png",
    "day3-normal-ab/ab-asis.png",
    "day3-normal-ab/ab-flipgreen.png",
]
AB_PAIR = ("day3-normal-ab/ab-asis.png", "day3-normal-ab/ab-flipgreen.png")


def load(path: str):
    with open(path, "rb") as f:
        raw = f.read()
    img = Image.open(io.BytesIO(raw)).convert("RGB")
    return img, raw


def distinct_colors(img) -> int:
    """不同颜色数（**口径 = (r,g,b) 三元组**，与 Unity 侧 `RND-01` **同判**）。

    ⚠ **硬要求**：**禁止**在 Pillow 14 **真正移除** `getdata` 之前改用 `get_flattened_data`；若升级，**必须同时**
    给出「**新旧计数逐位一致**」的验证。**不要**改用 `get_flattened_data`（已试过并**回退**，2026-10-16 批 B）：它返回的是**按通道扁平**的
    序列，直接 `Counter` 会把计数口径从"三元组"变成别的粒度 ⇒ 计数**虚高数倍**
    （实测：`perspective` **同一份字节**从 **34,610 → 122,394**），并与 Unity 侧 `RND-01`（12,484 等）
    **不再同判** ⇒ 两边会"一个绿一个红/数字对不上"。
    ⇒ 若将来 Pillow 14 真移除 `getdata`，升级时**必须同时**做"新旧计数逐位一致"的等价性验证再改；
       改前先接受 Pillow 的 `DeprecationWarning`（它不影响结果）。
    """
    return len(Counter(img.getdata()))


def diff_fraction(a: "Image.Image", b: "Image.Image") -> float:
    """逐像素差异占比（口径同上：三元组比较）。"""
    pa, pb = list(a.getdata()), list(b.getdata())
    if len(pa) != len(pb):
        return 1.0
    d = sum(1 for x, y in zip(pa, pb) if x != y)
    return d / len(pa)


def resolve(base: str, rel: str) -> str:
    """容忍两种布局：`day3-normal-ab/ab-*.png`（现行）与平铺 `ab-*.png`（归档）。"""
    for cand in (rel, os.path.basename(rel)):
        p = os.path.join(base, cand)
        if os.path.exists(p):
            return p
    return os.path.join(base, rel)


def check_dir(base: str) -> int:
    print(f"[render-evidence] dir = {base}")
    failures: list[str] = []
    imgs: dict[str, "Image.Image"] = {}

    for rel in EXPECTED:
        path = resolve(base, rel)
        if not os.path.exists(path):
            failures.append(f"{rel}: 不存在")
            print(f"  MISS  {rel}")
            continue
        if os.path.getsize(path) < 100:
            failures.append(f"{rel}: 文件过小（疑似 Git-LFS 指针或空文件）")
            print(f"  FAIL  {rel}: 文件过小 ({os.path.getsize(path)} B) —— 指针文件不能当证据")
            continue
        try:
            img, raw = load(path)
        except Exception as exc:
            failures.append(f"{rel}: 无法解码（{exc}）")
            print(f"  FAIL  {rel}: 无法解码 {exc}")
            continue
        imgs[rel] = img
        ncol = distinct_colors(img)
        sha = hashlib.sha256(raw).hexdigest()[:12]
        ok = ncol > 1
        print(f"  {'PASS' if ok else 'FAIL'}  {rel}: {img.size} distinctColors={ncol} sha={sha}")
        if not ok:
            failures.append(f"{rel}: 退化图（distinctColors == 1 ⇒ 单一底色 = 什么都没渲染）")

    if all(r in imgs for r in AB_PAIR):
        a, b = AB_PAIR
        frac = diff_fraction(imgs[a], imgs[b])
        ok = frac > 0.0
        print(f"  {'PASS' if ok else 'FAIL'}  A/B 差异: {frac * 100:.2f}% 像素不同"
              + ("" if ok else " ⇒ 两版相同 = A/B 证据失效"))
        if not ok:
            failures.append("A/B 两版逐像素完全相同 ⇒ 该证据无信息")

    print(f"[render-evidence] {'PASS' if not failures else 'FAIL'}（{len(failures)} 项）")
    for f in failures:
        print("   - " + f)
    return 1 if failures else 0


def selftest() -> int:
    """自证：本工具在'空图'上必 FAIL、在'两版有差异'上必 PASS。"""
    print("[selftest] 构造合成证据……")
    with tempfile.TemporaryDirectory() as td:
        os.makedirs(os.path.join(td, "day3-normal-ab"), exist_ok=True)
        blank = Image.new("RGB", (64, 48), (205, 205, 205))
        for rel in EXPECTED:
            blank.save(os.path.join(td, rel))

        rc_blank = check_dir(td)
        if rc_blank == 0:
            print("[selftest] **FAIL**：空图竟然通过了 —— 工具无判别力！")
            return 1
        print("[selftest] 空图 ⇒ FAIL ✓")

        a = Image.new("RGB", (64, 48), (10, 10, 10))
        px = a.load()
        for x in range(64):          # 造一点真实内容
            for y in range(48):
                px[x, y] = (x * 4 % 256, y * 5 % 256, (x + y) % 256)
        a.save(os.path.join(td, AB_PAIR[0]))
        b = a.copy()
        pb = b.load()
        for x in range(64):
            for y in range(48):
                r, g, bl = pb[x, y]
                pb[x, y] = (r, 255 - g, bl)     # 模拟"绿通道翻转"
        b.save(os.path.join(td, AB_PAIR[1]))
        a.save(os.path.join(td, "day3-top-view.png"))
        a.save(os.path.join(td, "day3-perspective.png"))

        rc_ok = check_dir(td)
        if rc_ok != 0:
            print("[selftest] **FAIL**：有内容的证据竟未通过。")
            return 1
        print("[selftest] 有内容 + A/B 有差异 ⇒ PASS ✓")
    print("[selftest] OK（工具能抓空图、能放真图）")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=os.path.join(ROOT, "Docs", "Reports"))
    ap.add_argument("--selftest", action="store_true")
    args = ap.parse_args()
    if args.selftest:
        return selftest()
    return check_dir(args.dir)


if __name__ == "__main__":
    raise SystemExit(main())
