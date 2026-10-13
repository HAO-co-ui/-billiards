#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
D4 探针 #2：验证「82 段 = 6 条开链」的结构假设，为 Day 4 物理内核 spec 定法向求解规则。

探针 #1 已测出：
  · 段长中位数 13.958 mm，76/82 段短于球直径 —— 82 段不是 82 条直库边；
  · 台心相对 a->b 的侧向不一致（39 左 / 43 右）—— 不能由绕序直接推内法向；
  · 端点度数直方图 {1:12, 2:76}，88 端点 = 82 段 + 6 —— 段成 6 条开链；
  · 包围盒比内库外扩约 64 mm。

本探针要判定五件事（每条都直接决定 spec 的写法）：
  A 6 条最长段是否落在内库内沿（±1.27 / ±0.635）—— 决定「库边线段」的物理含义；
  B 链重建：是否恰好 6 条链，链端点在哪（是否贴着袋喉 throat）；
  C 链内绕序是否统一（b_i 是否接 a_{i+1}）—— 决定能否「解一次侧、全链继承」；
  D 「以链内最长段定内法向、全链继承」这条规则能否覆盖全部 82 段；
  E 包围盒外扩 64 mm 的来源段 —— 是否是袋角段，还是长段本身就在外侧。

只读：不修改任何产物。
"""

import argparse
import json
import math
import sys


def sub(p, q):
    return (p[0] - q[0], p[1] - q[1])


def hypot(v):
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

    raw = tm["cushion_segments"]
    segs = []
    for i, s in enumerate(raw):
        a = (float(s["a"]["x"]), float(s["a"]["y"]))
        b = (float(s["b"]["x"]), float(s["b"]["y"]))
        segs.append({"i": i, "a": a, "b": b, "rail": s.get("rail", ""), "L": hypot(sub(b, a))})

    tol = 1e-6
    pockets = []
    for p in tm["pockets"]:
        pockets.append({
            "index": p["index"],
            "kind": p["kind"],
            "center": (float(p["runtimeCenter"]["x"]), float(p["runtimeCenter"]["y"])),
            "throatA": (float(p["runtimeThroat"][0]["x"]), float(p["runtimeThroat"][0]["y"])),
            "throatB": (float(p["runtimeThroat"][1]["x"]), float(p["runtimeThroat"][1]["y"])),
            "normal": (float(p["runtimeThroatNormal"]["x"]), float(p["runtimeThroatNormal"]["y"])),
        })

    # ── A 最长段是否落在内库内沿 ────────────────────────────────────
    by_len = sorted(segs, key=lambda s: -s["L"])
    longs = by_len[:6]
    half_l = float(tm["collision"]["halfLength"])
    half_w = float(tm["collision"]["halfWidth"])

    def axis_report(s):
        """报告该段到最近的内库线的距离（x=±half_l 或 y=±half_w）。"""
        if abs(s["a"][1] - s["b"][1]) <= tol:      # 水平段 -> 贴 y = ±half_w
            line = half_w if abs(s["a"][1]) > 0 else -half_w
            sign = 1 if s["a"][1] > 0 else -1
            target = sign * half_w
            return {
                "orientation": "horizontal",
                "aY": s["a"][1], "bY": s["b"][1],
                "distanceToNearestInnerLine": abs(s["a"][1] - target),
                "nearestInnerLine": target,
            }
        if abs(s["a"][0] - s["b"][0]) <= tol:      # 垂直段 -> 贴 x = ±half_l
            sign = 1 if s["a"][0] > 0 else -1
            target = sign * half_l
            return {
                "orientation": "vertical",
                "aX": s["a"][0], "bX": s["b"][0],
                "distanceToNearestInnerLine": abs(s["a"][0] - target),
                "nearestInnerLine": target,
            }
        return {"orientation": "oblique", "distanceToNearestInnerLine": None}

    long_report = []
    for s in longs:
        r = axis_report(s)
        r.update({
            "index": s["i"], "rail": s["rail"],
            "a": s["a"], "b": s["b"], "length": s["L"],
        })
        long_report.append(r)

    # ── B 链重建 ───────────────────────────────────────────────────
    def key(pt, table):
        for k in table:
            if hypot(sub(pt, k)) <= tol:
                return k
        return None

    nodes = []
    for s in segs:
        for tag in ("a", "b"):
            if key(s[tag], nodes) is None:
                nodes.append(s[tag])

    def node_id(pt):
        return nodes.index(key(pt, nodes))

    inc = {n: [] for n in range(len(nodes))}
    for s in segs:
        ia, ib = node_id(s["a"]), node_id(s["b"])
        inc[ia].append((s["i"], "a"))
        inc[ib].append((s["i"], "b"))

    degree = {n: len(v) for n, v in inc.items()}
    ends = [n for n, d in degree.items() if d == 1]

    # 从每个悬空端点走链
    visited = set()
    chains = []
    for start in ends:
        if any(start in c["nodeIds"] for c in chains):
            continue
        path_nodes = [start]
        path_segs = []
        cur = start
        prev_seg = None
        while True:
            nxt = None
            for sid, tag in inc[cur]:
                if sid != prev_seg:
                    nxt = (sid, tag)
                    break
            if nxt is None:
                break
            sid, tag = nxt
            other = node_id(segs[sid]["b"] if tag == "a" else segs[sid]["a"])
            path_segs.append((sid, tag, "a->b" if tag == "a" else "b->a"))
            visited.add(sid)
            prev_seg = sid
            cur = other
            path_nodes.append(cur)
            if degree[cur] == 1:
                break
        chains.append({"nodeIds": path_nodes, "segments": path_segs})

    chain_report = []
    for ci, c in enumerate(chains):
        sids = [s[0] for s in c["segments"]]
        total = sum(segs[s]["L"] for s in sids)
        # 链端点附近的袋
        endA, endB = nodes[c["nodeIds"][0]], nodes[c["nodeIds"][-1]]
        def pocket_near(pt, radius=0.12):
            best, bd = None, 1e9
            for p in pockets:
                for tp in (p["throatA"], p["throatB"], p["center"]):
                    dd = hypot(sub(pt, tp))
                    if dd < bd:
                        bd, best = dd, p["index"]
            return {"pocket": best, "distance": round(bd, 9)} if bd < radius else None
        chain_report.append({
            "chainIndex": ci,
            "segmentCount": len(sids),
            "segmentIndices": sids,
            "totalLength": round(total, 9),
            "railtagSet": sorted(set(segs[s]["rail"] for s in sids)),
            "start": {"node": c["nodeIds"][0], "point": endA, "nearPocket": pocket_near(endA)},
            "end": {"node": c["nodeIds"][-1], "point": endB, "nearPocket": pocket_near(endB)},
        })

    # ── C 链内绕序一致性 ───────────────────────────────────────────
    # 沿链行走时，每段被以 a->b 还是 b->a 方向经过；若全链同向，则绕序统一。
    winding_report = []
    for ci, c in enumerate(chains):
        dirs = [s[2] for s in c["segments"]]
        forward = dirs.count("a->b")
        backward = dirs.count("b->a")
        winding_report.append({
            "chainIndex": ci,
            "forward": forward, "backward": backward,
            "uniform": forward == 0 or backward == 0,
            "pattern": "".join("F" if d == "a->b" else "B" for d in dirs),
        })

    # ── D「最长段定侧、全链继承」是否自洽 ──────────────────────────
    inherit_report = []
    for ci, c in enumerate(chains):
        sids = [s[0] for s in c["segments"]]
        anchor = max(sids, key=lambda i: segs[i]["L"])
        # 锚段（最长段，即直库边）的内法向：台心在它的哪一侧
        sa = segs[anchor]
        d = (sa["b"][0] - sa["a"][0], sa["b"][1] - sa["a"][1])
        u = (d[0] / sa["L"], d[1] / sa["L"])
        sign_anchor = 1 if cross2(u, sub((0.0, 0.0), sa["a"])) > 0 else -1
        # 全链是否都在同一侧
        mismatch = []
        dists = []
        for i in sids:
            s = segs[i]
            if s["L"] <= 0:
                continue
            dd = (s["b"][0] - s["a"][0], s["b"][1] - s["a"][1])
            uu = (dd[0] / s["L"], dd[1] / s["L"])
            sg = 1 if cross2(uu, sub((0.0, 0.0), s["a"])) > 0 else -1
            if sg != sign_anchor:
                # 记录该段到台心的最近距离，判断"翻侧"是否只因离台心太近
                mid = ((s["a"][0] + s["b"][0]) / 2.0, (s["a"][1] + s["b"][1]) / 2.0)
                dists.append({"index": i, "length": s["L"], "midRadius": round(hypot(mid), 9)})
                mismatch.append(i)
        inherit_report.append({
            "chainIndex": ci,
            "anchorSegment": anchor,
            "anchorLength": round(segs[anchor]["L"], 9),
            "anchorInteriorSign": sign_anchor,
            "mismatchCount": len(mismatch),
            "mismatchIndices": mismatch,
            "mismatchDetail": dists,
        })

    # ── E 包围盒极端点来源 ─────────────────────────────────────────
    extreme = {"maxX": None, "minX": None, "maxY": None, "minY": None}
    for s in segs:
        for tag in ("a", "b"):
            pt = s[tag]
            for axis, cmp in (("maxX", lambda p: p[0]), ("minX", lambda p: -p[0]),
                              ("maxY", lambda p: p[1]), ("minY", lambda p: -p[1])):
                if extreme[axis] is None or cmp(pt) > cmp(extreme[axis]["point"]):
                    extreme[axis] = {"point": pt, "segmentIndex": s["i"], "tag": tag,
                                     "segmentLength": s["L"], "rail": s["rail"]}

    # ── 附加：短段的等弦特征（是否在逼近圆）─────────────────────────
    shorts = [s for s in segs if s["L"] < 2.0 * 0.028575]
    short_lens = sorted(round(s["L"], 9) for s in shorts)

    report = {
        "source": args.source,
        "A_longestSegments": long_report,
        "B_chains": {
            "chainCount": len(chains),
            "nodesTotal": len(nodes),
            "degreeHistogram": {str(k): sum(1 for v in degree.values() if v == k)
                                for k in sorted(set(degree.values()))},
            "endsCount": len(ends),
            "chains": chain_report,
        },
        "C_windingUniformity": winding_report,
        "D_anchorInheritance": inherit_report,
        "E_boundsExtremes": extreme,
        "F_shortSegmentLengths": {
            "count": len(shorts),
            "distinctLengths": sorted(set(short_lens)),
            "minLength": short_lens[0] if short_lens else None,
            "maxLength": short_lens[-1] if short_lens else None,
        },
    }

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    # stdout 摘要
    print("=== A 最长 6 段 ===")
    for r in long_report:
        dist = r["distanceToNearestInnerLine"]
        ds = f"{dist*1000:.6f} mm" if dist is not None else "n/a"
        print(f"  seg[{r['index']:2d}] {r['rail']:24s} L={r['length']:.6f} m  {r['orientation']:10s} 距内库线 {ds}")
    print("=== B 链 ===")
    print(f"  链数 {len(chains)}   节点 {len(nodes)}   悬空端 {len(ends)}")
    for c in chain_report:
        pa = c["start"]["nearPocket"]
        pb = c["end"]["nearPocket"]
        print(f"  chain{c['chainIndex']}: {c['segmentCount']:2d} 段  总长 {c['totalLength']:.6f} m  "
              f"rail={c['railtagSet']}")
        print(f"      起 {c['start']['point']} -> 袋 {pa}")
        print(f"      终 {c['end']['point']} -> 袋 {pb}")
    print("=== C 绕序一致性 ===")
    for w in winding_report:
        print(f"  chain{w['chainIndex']}: F={w['forward']:2d} B={w['backward']:2d} 统一={w['uniform']}  {w['pattern']}")
    print("=== D 锚段继承 ===")
    for r in inherit_report:
        print(f"  chain{r['chainIndex']}: 锚段 seg[{r['anchorSegment']}] L={r['anchorLength']:.6f} m "
              f"侧={r['anchorInteriorSign']:+d}  翻侧段数={r['mismatchCount']}")
        for m in r["mismatchDetail"][:6]:
            print(f"        seg[{m['index']:2d}] L={m['length']*1000:8.3f} mm 中点距台心 {m['midRadius']:.6f} m")
    print("=== E 包围盒极端点 ===")
    for k, v in extreme.items():
        print(f"  {k}: {v['point']}  seg[{v['segmentIndex']}] L={v['segmentLength']*1000:.3f} mm rail={v['rail']}")
    print("=== F 短段长度谱 ===")
    f = report["F_shortSegmentLengths"]
    print(f"  短于球直径的段 {f['count']} 个；不同长度 {len(f['distinctLengths'])} 种")
    print(f"  范围 {f['minLength']*1000:.3f} ~ {f['maxLength']*1000:.3f} mm")
    print(f"  前 12 种: {[round(x*1000,3) for x in f['distinctLengths'][:12]]}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
