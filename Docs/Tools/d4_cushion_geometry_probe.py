#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
D4 探针：只读分析 82 段库边的几何既定事实，为 Day 4 物理内核 spec 提供事实依据。

回答四个问题（这四条决定了接口签名与 TOI 求解写法，不能靠推断）：
  Q1 段长分布 —— 是否存在短于 2r (57.15 mm) 的段？短段会不会让"最近点求解"退化？
  Q2 法向朝向是否一致 —— 排好序的 a->b 是否让台心恒定在同一侧？
     若一致，则内法向可由 seg 自身唯一确定，无需逐段手工标注；
     若不一致，则必须引入逐段朝向标注（schema 变更）。
  Q3 段是否首尾相接成链 —— 决定能否用"绕边界行走"的算法，以及袋口处是否有缺口。
  Q4 段按 rail 的分布 —— 判断哪些是直库边、哪些是袋角/弧段。

只读：不写任何产物（除本脚本的 stdout / 由调用方重定向的 JSON）。
用法：
  python Docs/Tools/d4_cushion_geometry_probe.py \
      --source Docs/Reference/table-metadata-reference.json \
      --out    Docs/Reports/d4-cushion-geometry-probe.json
"""

import argparse
import json
import math
import sys


def norm(v):
    return math.hypot(v[0], v[1])


def cross2(d, w):
    return d[0] * w[1] - d[1] * w[0]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    with open(args.source, "r", encoding="utf-8") as fh:
        tm = json.load(fh)

    segs_raw = tm["cushion_segments"]
    ball_radius = float(tm["collision"]["ballRadius"])
    diameter = 2.0 * ball_radius
    half_l = float(tm["collision"]["halfLength"])
    half_w = float(tm["collision"]["halfWidth"])

    segments = []
    for i, s in enumerate(segs_raw):
        a = (float(s["a"]["x"]), float(s["a"]["y"]))
        b = (float(s["b"]["x"]), float(s["b"]["y"]))
        d = (b[0] - a[0], b[1] - a[1])
        L = norm(d)
        segments.append({
            "index": i,
            "a": a,
            "b": b,
            "rail": s.get("rail", ""),
            "length": L,
            "dir": d,
        })

    # ── Q1 段长分布 ────────────────────────────────────────────────────
    lengths = sorted(s["length"] for s in segments)
    shorter_than_diameter = [s["index"] for s in segments if s["length"] < diameter]
    shorter_than_radius = [s["index"] for s in segments if s["length"] < ball_radius]

    # ── Q2 法向朝向一致性 ──────────────────────────────────────────────
    # 对每段取左法向 nL = perp(dir)，用台心 (0,0) 判定内法向应取哪一侧。
    # s = cross(dir, center - a)：>0 表示台心在 a->b 的左侧。
    # 若全部段该值同号，则内法向 = 该符号对应的法向，可由 seg 自身唯一确定。
    interior_side = []
    for s in segments:
        d = s["dir"]
        if s["length"] <= 0.0:
            interior_side.append(None)
            continue
        dv = (d[0] / s["length"], d[1] / s["length"])
        w = (0.0 - s["a"][0], 0.0 - s["a"][1])
        c = cross2(dv, w)
        interior_side.append(1 if c > 0 else (-1 if c < 0 else 0))

    pos = [s["index"] for s, t in zip(segments, interior_side) if t == 1]
    neg = [s["index"] for s, t in zip(segments, interior_side) if t == -1]
    zero = [s["index"] for s, t in zip(segments, interior_side) if t == 0]

    # 对异号段，检查其是否属于袋角（靠台角/中袋），以判断"不一致"是否可解释
    def near_pocket(idx):
        a = segments[idx]["a"]
        b = segments[idx]["b"]
        pts = [a, b]
        hits = []
        for p in tm["pockets"]:
            cx = float(p["runtimeCenter"]["x"])
            cy = float(p["runtimeCenter"]["y"])
            for pt in pts:
                if math.hypot(pt[0] - cx, pt[1] - cy) < 0.12:
                    hits.append(p["index"])
                    break
        return sorted(set(hits))

    inconsistent = sorted(set(pos) | set(neg))
    inconsistent_detail = []
    if pos and neg:
        for idx in sorted(set(neg) if len(pos) > len(neg) else set(pos)):
            inconsistent_detail.append({
                "index": idx,
                "rail": segments[idx]["rail"],
                "length": segments[idx]["length"],
                "nearPockets": near_pocket(idx),
            })

    # ── Q3 首尾相接成链 ───────────────────────────────────────────────
    # 统计端点重复度：链式边界里每个端点恰好被 2 段共用（袋口缺口则是 1 段独占）。
    ENDPOINT_TOL = 1e-6
    endpoint_map = {}
    for s in segments:
        for tag, pt in (("a", s["a"]), ("b", s["b"])):
            key = None
            for k in endpoint_map:
                if math.hypot(pt[0] - k[0], pt[1] - k[1]) <= ENDPOINT_TOL:
                    key = k
                    break
            if key is None:
                endpoint_map[(pt[0], pt[1])] = []
                key = (pt[0], pt[1])
            endpoint_map[key].append((s["index"], tag))

    degree_hist = {}
    for k, v in endpoint_map.items():
        degree_hist[len(v)] = degree_hist.get(len(v), 0) + 1
    dangling = [
        {"point": [k[0], k[1]], "users": v}
        for k, v in endpoint_map.items() if len(v) == 1
    ]

    # ── Q4 rail 分布 ─────────────────────────────────────────────────
    rail_stats = {}
    for s in segments:
        r = rail_stats.setdefault(s["rail"], {"count": 0, "minLen": 1e9, "maxLen": 0.0, "indices": []})
        r["count"] += 1
        r["minLen"] = min(r["minLen"], s["length"])
        r["maxLen"] = max(r["maxLen"], s["length"])
        r["indices"].append(s["index"])
    for r in rail_stats.values():
        r["minLen"] = round(r["minLen"], 9)
        r["maxLen"] = round(r["maxLen"], 9)

    # ── 附加：包围盒与硬指标自检 ───────────────────────────────────────
    xs = [s["a"][0] for s in segments] + [s["b"][0] for s in segments]
    ys = [s["a"][1] for s in segments] + [s["b"][1] for s in segments]

    report = {
        "source": args.source,
        "sourceSha256Declared": tm.get("source_sha256", ""),
        "segmentCount": len(segments),
        "ballRadius": ball_radius,
        "ballDiameter": diameter,
        "declaredHalfLength": half_l,
        "declaredHalfWidth": half_w,

        "Q1_segmentLength": {
            "min": round(lengths[0], 9),
            "median": round(lengths[len(lengths) // 2], 9),
            "max": round(lengths[-1], 9),
            "minOverDiameter": round(lengths[0] / diameter, 6),
            "countShorterThanDiameter": len(shorter_than_diameter),
            "indicesShorterThanDiameter": shorter_than_diameter,
            "countShorterThanRadius": len(shorter_than_radius),
            "indicesShorterThanRadius": shorter_than_radius,
        },

        "Q2_normalOrientation": {
            "conventionTest": "s = cross(normalize(b-a), center - a); sign tells which side the table centre lies on",
            "countPositive": len(pos),
            "countNegative": len(neg),
            "countZero": len(zero),
            "zeroIndices": zero,
            "consistent": len(neg) == 0 or len(pos) == 0,
            "minoritySideIndices": inconsistent_detail,
        },

        "Q3_chainTopology": {
            "endpointTolerance": ENDPOINT_TOL,
            "uniqueEndpoints": len(endpoint_map),
            "degreeHistogram": degree_hist,
            "danglingCount": len(dangling),
            "dangling": dangling[:20],
        },

        "Q4_railDistribution": rail_stats,

        "extra_bounds": {
            "minX": round(min(xs), 9), "maxX": round(max(xs), 9),
            "minY": round(min(ys), 9), "maxY": round(max(ys), 9),
            "extentX": round(max(xs) - min(xs), 9),
            "extentY": round(max(ys) - min(ys), 9),
            "expectedExtentX_if_segments_are_inner": 2.0 * half_l,
            "expectedExtentY_if_segments_are_inner": 2.0 * half_w,
        },
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    # stdout 摘要
    q1 = report["Q1_segmentLength"]
    q2 = report["Q2_normalOrientation"]
    q3 = report["Q3_chainTopology"]
    print("=== Q1 段长 ===")
    print(f"  min={q1['min']*1000:.3f} mm  median={q1['median']*1000:.3f} mm  max={q1['max']*1000:.3f} mm")
    print(f"  min / diameter = {q1['minOverDiameter']:.6f}")
    print(f"  短于球直径(57.15mm)的段: {q1['countShorterThanDiameter']} 段")
    print(f"  短于球半径(28.575mm)的段: {q1['countShorterThanRadius']} 段")
    print("=== Q2 法向朝向 ===")
    print(f"  台心在左侧: {q2['countPositive']}   在右侧: {q2['countNegative']}   共线: {q2['countZero']}")
    print(f"  一致（可由段自身唯一确定内法向）: {q2['consistent']}")
    print("=== Q3 链拓扑 ===")
    print(f"  唯一端点 {q3['uniqueEndpoints']} 个；度数直方图 {q3['degreeHistogram']}")
    print(f"  悬空端点 {q3['danglingCount']} 个")
    print("=== Q4 rail 分布 ===")
    for r, v in rail_stats.items():
        print(f"  {r:28s} n={v['count']:2d}  len {v['minLen']*1000:8.3f} ~ {v['maxLen']*1000:8.3f} mm")
    print("=== 包围盒 ===")
    eb = report["extra_bounds"]
    print(f"  x: {eb['minX']:.9f} ~ {eb['maxX']:.9f}  (extent {eb['extentX']:.9f})")
    print(f"  y: {eb['minY']:.9f} ~ {eb['maxY']:.9f}  (extent {eb['extentY']:.9f})")
    print(f"  若段为内库内沿，期望 extent = {2*half_l:.9f} x {2*half_w:.9f}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
