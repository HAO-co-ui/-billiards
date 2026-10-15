# -*- coding: utf-8 -*-
"""Day 15 注入补丁：把某档负对照写进指定文件（原地改写；还原由调用方的 `cp` 备份负责）。

用法：python Docs/Tools/day15_patch.py <v1|v2> <目标文件>

档位
────
  v1  球-球深层重叠**跳回"球心完全重合即早退"**（= 复现"同位静止两球永不分离"）⇒ `T22b` 应红
  v2  把两段滚阻**清零**（= 复现"滚阻参数不生效"）⇒ `T114 (a)/(c)` 应红
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
        """                float dist = Length(delta);
                if (dist >= 2f * r)
                {
                    continue;
                }""",
        """                float dist = Length(delta);
                if (dist >= 2f * r || dist < 1e-9f)
                {
                    continue;   // INJECTED v1：跳回"重合即早退"（负对照）
                }""")
elif MODE == "v2":
    apply(
        TARGET,
        """            float vTransition = _params.rollingTransitionSpeed;
""",
        """            float vTransition = _params.rollingTransitionSpeed;
            aSlide = 0f;   // INJECTED v2：滚阻清零（负对照）
            aRoll = 0f;
""")
else:
    raise SystemExit("unknown mode " + MODE)
