"""把 TableConfig.asset（Unity YAML）解析成 JSON，供独立探针 `Program.cs` 重建 TableConfig。

为什么这么干：`TableConfig` 是 `ScriptableObject`，在 Unity 之外无法 `CreateInstance`，
但可以用 `FormatterServices.GetUninitializedObject` + 反射逐字段赋值 ⇒ 需要一份**数据**。
本脚本直接读**提交态资产**（不是从源 JSON 重导），保证探针喂给内核的几何 = 运行时真源。

用法：python Docs/Tools/aimcost_probe/parse_table.py
输出：Docs/Tools/aimcost_probe/table.json
"""

from __future__ import annotations

import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
ASSET = os.path.join(ROOT, "Assets/_Project/Data/TableConfig.asset")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "table.json")

VEC = re.compile(r"\{\s*x:\s*([-0-9.eE+]+),\s*y:\s*([-0-9.eE+]+)\s*\}")


def parse_vec(s: str):
    m = VEC.search(s)
    if not m:
        raise SystemExit("非向量：" + repr(s))
    return [float(m.group(1)), float(m.group(2))]


def main() -> None:
    lines = open(ASSET, encoding="utf-8").read().splitlines()

    scalars = {}
    segments = []
    pockets = []

    i = 0
    n = len(lines)
    while i < n:
        line = lines[i]
        stripped = line.strip()

        if stripped.startswith("segments:"):
            i += 1
            while i < n and lines[i].lstrip().startswith("- a:"):
                a = parse_vec(lines[i])
                b = parse_vec(lines[i + 1])
                rail = lines[i + 2].split(":", 1)[1].strip()
                segments.append({"a": a, "b": b, "rail": rail})
                i += 3
            continue

        if stripped.startswith("pockets:"):
            i += 1
            while i < n and lines[i].lstrip().startswith("- index:"):
                p = {}
                p["index"] = int(lines[i].split(":", 1)[1])
                i += 1
                while i < n and not lines[i].lstrip().startswith("- index:") \
                        and not lines[i].lstrip().startswith("footSpot:"):
                    s = lines[i].strip()
                    if not s or ":" not in s:
                        i += 1
                        continue
                    key, _, val = s.partition(":")
                    key = key.strip()
                    val = val.strip()
                    if val.startswith("{"):
                        p[key] = parse_vec(val)
                    elif val == "":
                        # 续行字符串（captureNote）—— 拼到下一行
                        nxt = lines[i + 1].strip() if i + 1 < n else ""
                        p[key] = (val + " " + nxt).strip()
                        i += 1
                    else:
                        try:
                            p[key] = float(val) if ("." in val or "e" in val or "E" in val) else int(val)
                        except ValueError:
                            p[key] = val
                    i += 1
                pockets.append(p)
            continue

        m = re.match(r"^\s*([a-zA-Z][a-zA-Z0-9]*):\s*(\S.*)$", line)
        if m and stripped.split(":")[0] in (
            "innerLength", "innerWidth", "halfLength", "halfWidth",
            "clothHeight", "noseHeight", "ballRadius", "legBottomY",
        ):
            scalars[m.group(1)] = float(m.group(2))
        if stripped.startswith("footSpot:"):
            scalars["footSpot"] = parse_vec(line)

        i += 1

    data = {
        "footSpot": scalars.get("footSpot"),
        "segments": segments,
        "pockets": pockets,
        "scalars": {k: v for k, v in scalars.items() if k != "footSpot"},
    }

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
    print(f"segments={len(segments)} pockets={len(pockets)} footSpot={scalars.get('footSpot')} -> {OUT}")


if __name__ == "__main__":
    main()
