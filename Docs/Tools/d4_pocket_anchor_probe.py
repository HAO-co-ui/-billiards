#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
物理内核 spec 探针 #3：补齐 spec §1.3 / §1.4 的**可复现证据**。

背景：spec v1 的 §1.4 声称「袋口 mouth 端点与直库段端点逐位重合」并标为「探针实测」，
但两份已有探针 JSON 里**并不包含**这项比对（复核方 D4-spec_独立复核 ⑧ 指出）。
本探针把它做成可重跑的产物，消除「声称实测但证据不在所引文件」的问题。

同时补齐 §1.3 所缺的一项：12 个悬空链端点距各袋关键点的精确距离
（工程复核 H-2 指出 T7 的端点射击会落进 PocketProximityEvents 触发半径 —— 需要精确数字支撑）。

产出三节：
  G1 6 条长段端点 → 最近 mouth 顶点的偏差（支撑 §1.4）
  G2 12 个悬空链端点 → 到达袋各关键点的距离（支撑 §1.3）
  G3 全 164 个段端点 → 最近 mouth 顶点的偏差分布（支撑 §1.1「库线由多段拼成」）

只读。
"""

import argparse
import json
import math
import sys


def dist(p, q):
    return math.hypot(p[0] - q[0], p[1] - q[1])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    with open(args.source, "r", encoding="utf-8") as fh:
        tm = json.load(fh)

    segs = []
    for i, s in enumerate(tm["cushion_segments"]):
        segs.append({
            "i": i, "rail": s.get("rail", ""),
            "a": (s["a"]["x"], s["a"]["y"]),
            "b": (s["b"]["x"], s["b"]["y"]),
        })

    pockets = []
    for p in tm["pockets"]:
        pockets.append({
            "index": p["index"], "kind": p["kind"],
            "mouthA": (p["runtimeMouth"][0]["x"], p["runtimeMouth"][0]["y"]),
            "mouthB": (p["runtimeMouth"][1]["x"], p["runtimeMouth"][1]["y"]),
            "throatA": (p["runtimeThroat"][0]["x"], p["runtimeThroat"][0]["y"]),
            "throatB": (p["runtimeThroat"][1]["x"], p["runtimeThroat"][1]["y"]),
            "throatCenter": (p["runtimeThroatCenter"]["x"], p["runtimeThroatCenter"]["y"]),
            "center": (p["runtimeCenter"]["x"], p["runtimeCenter"]["y"]),
            "openingRadius": float(p["openingRadius"]),
            "mouthWidth": float(p["runtimeMouthWidth"]),
        })

    ball_r = float(tm["collision"]["ballRadius"])
    trigger = pockets[0]["openingRadius"] + ball_r

    # ── G1 6 条长段端点 → 最近 mouth 顶点 ──────────────────────────────
    def nearest_mouth(pt):
        best = None
        for pk in pockets:
            for tag in ("mouthA", "mouthB"):
                d = dist(pt, pk[tag])
                if best is None or d < best[0]:
                    best = (d, pk["index"], tag)
        return best

    lengths = []
    for s in segs:
        lengths.append((math.dist(s["a"], s["b"]), s["i"]))
    lengths.sort(reverse=True)
    long_idx = [i for _, i in lengths[:6]]

    g1 = []
    for i in long_idx:
        s = segs[i]
        for tag in ("a", "b"):
            d, pi, ptag = nearest_mouth(s[tag])
            g1.append({
                "segmentIndex": i, "rail": s["rail"], "endpoint": tag,
                "point": s[tag], "length": math.dist(s["a"], s["b"]),
                "nearestPocket": pi, "nearestMouthVertex": ptag,
                "deviation": d,
            })
    g1_max = max(e["deviation"] for e in g1)

    # ── G2 12 个悬空链端点 → 各袋关键点距离 ───────────────────────────
    # 悬空端点：不被两段共用的端点。用 1e-6 m 容差重算度分布。
    TOL = 1e-6
    nodes = []
    for s in segs:
        for tag in ("a", "b"):
            found = None
            for k in nodes:
                if dist(s[tag], k) <= TOL:
                    found = k
                    break
            if found is None:
                nodes.append(s[tag])

    degree = {}
    for s in segs:
        for tag in ("a", "b"):
            for k in nodes:
                if dist(s[tag], k) <= TOL:
                    degree[k] = degree.get(k, 0) + 1
                    break

    dangling = sorted([k for k, d in degree.items() if d == 1])

    g2 = []
    for p in dangling:
        row = {"point": p}
        best = None
        for pk in pockets:
            for key in ("mouthA", "mouthB", "throatA", "throatB", "throatCenter", "center"):
                d = dist(p, pk[key])
                if best is None or d < best[0]:
                    best = (d, pk["index"], key)
            row[f"pocket{pk['index']}_throatCenter"] = dist(p, pk["throatCenter"])
        row["nearestKeyPoint"] = {"distance": best[0], "pocket": best[1], "key": best[2]}
        row["minDistToAnyThroatCenter"] = min(row[f"pocket{i}_throatCenter"] for i in range(6))
        row["triggersPocketProximity"] = row["minDistToAnyThroatCenter"] <= trigger
        g2.append(row)

    # ── G3 全 164 端点 → 最近 mouth 顶点 ─────────────────────────────
    g3 = []
    for s in segs:
        for tag in ("a", "b"):
            d, pi, ptag = nearest_mouth(s[tag])
            g3.append({"segmentIndex": s["i"], "endpoint": tag, "deviation": d,
                       "pocket": pi, "mouthVertex": ptag})
    devs = sorted(e["deviation"] for e in g3)

    report = {
        "source": args.source,
        "ballRadius": ball_r,
        "pocketProximityTriggerRadius": trigger,
        "note": "本文件为 spec §1.3 / §1.4 的证据来源；此前两份探针不含这些比对。",

        "G1_longSegmentEndpointsVsMouthVertex": {
            "count": len(g1),
            "maxDeviation": g1_max,
            "entries": g1,
        },

        "G2_danglingChainEndpointsVsPockets": {
            "count": len(dangling),
            "triggerRadius": trigger,
            "triggerCount": sum(1 for r in g2 if r["triggersPocketProximity"]),
            "minDistToNearestKeyPoint": min(r["nearestKeyPoint"]["distance"] for r in g2),
            "maxDistToNearestKeyPoint": max(r["nearestKeyPoint"]["distance"] for r in g2),
            "minDistToAnyThroatCenter": min(r["minDistToAnyThroatCenter"] for r in g2),
            "maxDistToAnyThroatCenter": max(r["minDistToAnyThroatCenter"] for r in g2),
            "entries": g2,
        },

        "G3_allSegmentEndpointsVsMouthVertex": {
            "count": len(g3),
            "min": devs[0], "median": devs[len(devs) // 2], "max": devs[-1],
            "countWithin1e-5m": sum(1 for d in devs if d <= 1e-5),
            "countBeyond1e-3m": sum(1 for d in devs if d > 1e-3),
            "histogram": {
                "<=1e-5": sum(1 for d in devs if d <= 1e-5),
                "1e-5..1e-4": sum(1 for d in devs if 1e-5 < d <= 1e-4),
                "1e-4..1e-3": sum(1 for d in devs if 1e-4 < d <= 1e-3),
                ">1e-3": sum(1 for d in devs if d > 1e-3),
            },
        },
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    print("=== G1 6 条长段端点 -> 最近 mouth 顶点 ===")
    for e in g1:
        print(f"  seg[{e['segmentIndex']:2d}] {e['rail']:24s} {e['endpoint']} "
              f"-> pocket[{e['nearestPocket']}].{e['nearestMouthVertex']:6s} "
              f"偏差 = {e['deviation']:.9f} m")
    print(f"  最大偏差 = {g1_max:.9f} m  (数据导入容差 1e-5 m)")
    print()
    print("=== G2 12 个悬空链端点 ===")
    print(f"  触发半径 = {trigger:.9f} m")
    print(f"  距最近关键点: {report['G2_danglingChainEndpointsVsPockets']['minDistToNearestKeyPoint']:.6f}"
          f" ~ {report['G2_danglingChainEndpointsVsPockets']['maxDistToNearestKeyPoint']:.6f} m")
    print(f"  距最近 throatCenter: "
          f"{report['G2_danglingChainEndpointsVsPockets']['minDistToAnyThroatCenter']:.6f}"
          f" ~ {report['G2_danglingChainEndpointsVsPockets']['maxDistToAnyThroatCenter']:.6f} m")
    print(f"  落在触发半径内: "
          f"{report['G2_danglingChainEndpointsVsPockets']['triggerCount']} / {len(dangling)}")
    for r in g2:
        nk = r["nearestKeyPoint"]
        print(f"  ({r['point'][0]:+.6f},{r['point'][1]:+.6f})  "
              f"最近 {nk['key']:13s}@pocket[{nk['pocket']}] d={nk['distance']:.6f}  "
              f"距throatCenter_min={r['minDistToAnyThroatCenter']:.6f}  "
              f"{'触发' if r['triggersPocketProximity'] else '不触发'}")
    print()
    print("=== G3 全 164 端点 -> 最近 mouth 顶点 ===")
    q = report["G3_allSegmentEndpointsVsMouthVertex"]
    print(f"  min={q['min']:.9f}  median={q['median']:.9f}  max={q['max']:.9f} m")
    print(f"  直方图 {q['histogram']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
