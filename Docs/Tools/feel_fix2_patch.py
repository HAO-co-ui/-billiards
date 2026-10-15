# -*- coding: utf-8 -*-
"""注入补丁：把某档负对照写进指定文件（原地改写；还原由调用方的 `cp` 备份负责）。

用法：python Docs/Tools/feel_fix2_patch.py <v1|v2> <目标文件>
为什么单独成文件：补丁片段里的缩进/中文若内联进 shell heredoc，容易被同名分隔符或转义吃掉。

档位
────
  v1  去掉 `ResolveSegmentContact` 的**接近判据**（= 复现"袋角撞了不弹"的原状）⇒ `T109` 应红
  v2  把杆尖后拉改回**水平方向**（= 复现"蓄力穿模"的原状）⇒ `T104 (e) ③c/⑤` 应红
"""

import io
import sys


def apply(path, old, new):
    text = io.open(path, encoding="utf-8", newline="").read()
    if old in text:
        text = text.replace(old, new, 1)
    else:
        crlf_old = old.replace("\n", "\r\n")
        if crlf_old not in text:
            raise SystemExit("anchor not found: " + old[:80])
        text = text.replace(crlf_old, new.replace("\n", "\r\n"), 1)
    io.open(path, "w", encoding="utf-8", newline="").write(text)
    print("injected", sys.argv[1], "->", path)


MODE, TARGET = sys.argv[1], sys.argv[2]

if MODE == "v1":
    apply(
        TARGET,
        """            // ★ 接近判据：已在分离 ⇒ 不施加冲量、不计接触（与球-球路径同一不变量）。
            if (vn >= 0f)
            {
                return;
            }
""",
        """            // INJECTED v1：去掉接近判据（负对照）
""")
elif MODE == "v2":
    apply(
        TARGET,
        """            return cueBallWorld + StickDirection(aimDirectionWorld, elevationDegrees) * (TipGap + pullback);""",
        """            return cueBallWorld - aimDirectionWorld * (TipGap + pullback);   // INJECTED v2：水平后拉（负对照）""")
else:
    raise SystemExit("unknown mode " + MODE)
