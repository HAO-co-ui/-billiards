#!/usr/bin/env python3
"""袋口"窗口"重算探针（Day 6 spec §1.4 / §7 证据）。

**不依赖 Unity**：直接读权威数据 `Docs/Reference/table-metadata-reference.json`（TM），
逐段求"球心沿竖直方向射入侧袋"的最近段距离，复算"窗口"（能穿袋喉、又不碰袋角、
又不满足 captureRadius 的横向偏移区间）。

**本脚本只做计算与落盘，不含任何断言**；其产出的数值用于与 spec §1.4 的
`8.25 mm`（侧袋，逐 1 mm 实测）/ `8.78 mm`（角袋，同式推得）对照。

用法：
    python3 Docs/Tools/d6_pocket_window_probe.py [--out <json>]
"""
import argparse
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
TM = os.path.join(ROOT, "Docs", "Reference", "table-metadata-reference.json")
DEFAULT_OUT = os.path.join(ROOT, "Docs", "Tuning", "d6-pocket-window-probe.json")

R = 0.028575  # PoolConstants.BallRadius


def dist_point_segment(p, a, b):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    l2 = dx * dx + dy * dy
    if l2 <= 1e-12:
        return math.hypot(p[0] - ax, p[1] - ay)
    t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / l2))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=DEFAULT_OUT)
    args = ap.parse_args()

    with open(TM, encoding="utf-8") as f:
        tm = json.load(f)

    segs = [(s["a"]["x"], s["a"]["y"], s["b"]["x"], s["b"]["y"])
            for s in tm["collision"]["segments"]]

    out = {
        "schema": "billiards.d6-pocket-window-probe.v1",
        "source": "Docs/Reference/table-metadata-reference.json",
        "ballRadius": R,
        "method": "球心沿喉法向的竖直轨迹逐 1 mm 采样，对全部 82 段求最近距离的最小值",
        "pockets": [],
    }

    for p in tm["pockets"]:
        tc = (p["runtimeThroatCenter"]["x"], p["runtimeThroatCenter"]["y"])
        tw = p["runtimeThroatWidth"]
        cr = p["captureRadius"]
        half = tw / 2.0
        # 窗口 = (captureRadius, 半宽 - r]
        out["pockets"].append({
            "index": p["index"],
            "kind": p["kind"],
            "throatCenterX": tc[0],
            "throatCenterY": tc[1],
            "throatHalfWidth": half,
            "captureRadius": cr,
            "windowLow": cr,
            "windowHigh": half - R,
            "windowWidth": (half - R) - cr,
            "windowWidthMm": ((half - R) - cr) * 1000.0,
            "numericallyReproduced": False,
        })

    # 侧袋（tn = (0,-1)）逐 ρ 的最近段距离：唯一"逐 1 mm 实测"的那一档
    side = next(p for p in tm["pockets"] if p["kind"] == "side" and p["runtimeThroatNormal"]["y"] < 0)
    tc = (side["runtimeThroatCenter"]["x"], side["runtimeThroatCenter"]["y"])
    samples = []
    for rho_mm in (30, 35, 39, 44, 50):
        rho = rho_mm / 1000.0
        best = float("inf")
        y = -0.55
        while y > -0.78:
            d = min(dist_point_segment((rho, y), (a, b), (c, d2)) for (a, b, c, d2) in segs)
            best = min(best, d)
            y -= 0.001
        samples.append({
            "rhoMm": rho_mm,
            "minDistToSegmentMm": best * 1000.0,
            "contactsJaw": best < R,
            "crossesThroatPlane": rho <= (side["runtimeThroatWidth"] / 2.0),
        })

    out["sidePocketSamples"] = {
        "pocketIndex": side["index"],
        "samples": samples,
    }
    for rec in out["pockets"]:
        if rec["index"] == side["index"]:
            rec["numericallyReproduced"] = True

    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=2)

    print("D6_WINDOW_PROBE=" + args.out)
    for rec in out["pockets"]:
        print("  pocket[%d] %-6s window=%.2f mm  (half=%.6f, cr=%.6f%s)"
              % (rec["index"], rec["kind"], rec["windowWidthMm"], rec["throatHalfWidth"],
                 rec["captureRadius"], "" if rec["numericallyReproduced"] else ", 推得"))


if __name__ == "__main__":
    main()
