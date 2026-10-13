#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D4-AUDIT-02：验证若干"是否在复述实现/循环论证"的怀疑（可重跑）。"""
import sys, io
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

L = 2.54            # PoolConstants.ClothInnerLength
W = 1.270108866     # PoolConstants.ClothInnerWidth
NOMINAL_W = 1.27    # 测试里硬编码的标称值

def test_body(L, W, tol=1e-6):
    """复刻 PoolConstantsTests.TableAspectRatio_... 的算法。"""
    widthRelativeError = (W - NOMINAL_W) / NOMINAL_W
    expectedRatio = 2.0 * (1.0 - widthRelativeError)
    actualRatio = L / W
    return expectedRatio, actualRatio, abs(expectedRatio - actualRatio) <= tol

print("=== 复刻 TableAspectRatio_... 的判别力 ===")
e, a, ok = test_body(L, W)
print(f"  基线:   expected={e:.9f} actual={a:.9f} 通过={ok}")
for dW in (-1e-3, -1e-4, 0.0, +1e-4, +1e-3):
    e2, a2, ok2 = test_body(L, W + dW)
    print(f"  扰动 W {dW:+.0e} -> expected={e2:.9f} actual={a2:.9f} |Δ|={abs(e2-a2):.2e} 仍通过={ok2}")
# 若 W 改了而 L 不动，它能否发现？
print("\n  只改 W（L 不变）时，expected 与 actual 是否**同步移动**（⇒ 测不出 W 被改）：")
for dW in (-1e-3, +1e-3):
    e2, a2, ok2 = test_body(L, W + dW)
    print(f"    W{dW:+.0e}: |Δ|={abs(e2-a2):.2e} ⇒ {'测不出' if ok2 else '能测出'}")
print("\n  结论：expected 由 W 自身导出，故 W 被改时两侧同步 ⇒ 对'W 被改'不敏感；")
print("        它只能发现'L 相对 (2.54−W)/1.27 的偏离'，即一个近乎代数恒等式。")

print("\n=== 「面点≠完全盲」在同一测试里的体现（对照 spec 结论）===")
print("  TableConfigTests 自带 InsideXZ/SegXZ/NearestHorizontal 三份 helper，")
print("  与 Runtime/Core/MeshAlignmentMetrics.cs 的算法**重复实现**（非引用）。")
