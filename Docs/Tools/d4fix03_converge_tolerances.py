#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把测试里的**容差字面量**收敛到单源 `MeshAlignmentTolerances`。

设计判断（**不照搬复核处方**）：20 处字面量**不是同一个容差**，按语义分三类：

  A. `IndexAligned`（1e-5）—— 两份数据/声明必须逐字段一致
  B. `Bounds`（1e-4）—— 世界包围盒 = 转换链推导期望
  C. `TransformIdentity`（1e-5，**本次新增**）—— 同一几何量的两种算法/表示必须一致
     （det=+1 保手性、纯旋转保距、分量式 API 与四元数式 API 等价、判决量复现）
  D. **保持本地**：审计专用的经验上界（`MeshAlignmentAuditTests.cs` 内），无需与别处一致 —— 只加注说明。

⚠ 把 C 硬塞进 A 是**语义错误**（数值巧合不是合并理由）。故新增 C 而非复用 A。

脚本以 **(文件, 行号, 期望子串 → 替换子串)** 为单位，**每处先断言期望子串存在**，
任一处不符即中止且不写盘（避免"改了一部分"的中间态）。
"""

import sys
import os

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
T = os.path.join(ROOT, "Assets/_Project/Tests/EditMode")

INDEX = "MeshAlignmentTolerances.IndexAligned"
BOUNDS = "MeshAlignmentTolerances.Bounds"
XFORM = "MeshAlignmentTolerances.TransformIdentity"

# (相对路径, 行号(1-based), 期望包含, 替换为)
REPL = [
    # ── A. IndexAligned：库端包围盒 vs PoolGeometry 声明常量（跨声明一致性）──
    ("TableConfigTests.cs", 206, "min.x, 1e-5f)", f"min.x, {INDEX})"),
    ("TableConfigTests.cs", 207, "min.y, 1e-5f)", f"min.y, {INDEX})"),
    ("TableConfigTests.cs", 208, "max.x, 1e-5f)", f"max.x, {INDEX})"),
    ("TableConfigTests.cs", 209, "max.y, 1e-5f)", f"max.y, {INDEX})"),
    # ── A. IndexAligned：Config vs 源文件 逐字段比对 ──
    ("TableConfigTests.cs", 287, "const float tol = 1e-5f;", f"const float tol = {INDEX};"),
    # ── C. TransformIdentity：纯旋转保距 ──
    ("TableConfigTests.cs", 455, "probe).magnitude, 1e-5f)", f"probe).magnitude, {XFORM})"),
    # ── C. TransformIdentity：分量式 API ≡ 四元数式 API ──
    ("TableConfigTests.cs", 460, "q.x, f.x, 1e-5f)", f"q.x, f.x, {XFORM})"),
    ("TableConfigTests.cs", 461, "q.y, f.y, 1e-5f)", f"q.y, f.y, {XFORM})"),
    ("TableConfigTests.cs", 462, "q.z, f.z, 1e-5f)", f"q.z, f.z, {XFORM})"),
    # ── C. TransformIdentity：det = +1 ──
    ("TableConfigTests.cs", 469, "Assert.AreEqual(1f, det, 1e-5f, ",
                                  f"Assert.AreEqual(1f, det, {XFORM}, "),
    # ── B. Bounds：世界包围盒 vs 网格实测 ──
    ("TableConfigTests.cs", 488, "1e-4f", BOUNDS),
    ("TableConfigTests.cs", 489, "1e-4f", BOUNDS),
    ("TableConfigTests.cs", 490, "1e-4f", BOUNDS),
    ("TableConfigTests.cs", 491, "1e-4f", BOUNDS),
    ("TableConfigTests.cs", 492, "1e-4f", BOUNDS),
    ("TableConfigTests.cs", 493, "1e-4f", BOUNDS),
    # ── C. TransformIdentity：ModelOrientationTests 的 det / 判决量复现 ──
    ("ModelOrientationTests.cs", 37, "Assert.AreEqual(1f, det, 1e-5f, ",
                                     f"Assert.AreEqual(1f, det, {XFORM}, "),
    ("ModelOrientationTests.cs", 102, "asym, 1e-5f,", f"asym, {XFORM},"),
    ("ModelOrientationTests.cs", 133, "err, 1e-5f,", f"err, {XFORM},"),
]


def main():
    # 按文件分组，先把所有替换在内存里做完并逐处断言，全部通过才写盘
    staged = {}
    failures = []

    for rel, lineno, needle, repl in REPL:
        path = os.path.join(T, rel)
        if path not in staged:
            with open(path, "r", encoding="utf-8") as fh:
                staged[path] = fh.read().split("\n")

        lines = staged[path]
        idx = lineno - 1
        if idx >= len(lines):
            failures.append(f"{rel}:{lineno} 行号越界（文件只有 {len(lines)} 行）")
            continue
        if needle not in lines[idx]:
            failures.append(f"{rel}:{lineno} 期望子串未命中\n      期望含: {needle!r}\n      实际行: {lines[idx]!r}")
            continue
        lines[idx] = lines[idx].replace(needle, repl)

    if failures:
        print("✗ 中止，未写盘。以下处不符合预期：")
        for f in failures:
            print("   " + f)
        return 1

    for path, lines in staged.items():
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            fh.write("\n".join(lines))
        print(f"✓ 已写 {os.path.relpath(path, ROOT)}")

    print(f"\n共替换 {len(REPL)} 处。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
