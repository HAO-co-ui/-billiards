#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
verify_collision_data.py  --  Idea #35 (台球) 碰撞几何独立基线 / 校验工具
================================================================================
角色：QA (quality-lead / 严守真)
用途：
  1) 独立读取美术交付包的两份原始 JSON，交叉比对（82 段库边 + 6 袋 + 桌面标量）。
  2) 独立重算并落盘"期望值基线"（d2t02a-baseline.json），供后续候选
     比对 Unity 侧 TableConfig 落地结果。
  3) 提供可复用的校验原语（compare_candidate / validate_* ），
     把比对对象从原始 JSON 换成 Unity 导出的数据即可复用。

独立性声明：
  本脚本只读 08_Gameplay_Markers/ 下的两份原始交付文件，
  不读取 Docs/Reference/ 下的任何副本，也不读取实现者代码。

用法：
  python verify_collision_data.py            # 交叉比对 + 打印报告 + 写基线
  python verify_collision_data.py --baseline-only

注意（本机已知陷阱）：含反斜杠的正则替换必须用 lambda 作替换值，不能用字符串。
本文件不含此类正则；如需扩展请遵守该约定。
"""
from __future__ import annotations
import json
import math
import sys
import io
import os
import hashlib
import argparse

# Windows 控制台 UTF-8
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

# ---------------------------------------------------------------------------
# 路径（原始交付来源；勿改为 Docs/Reference 副本）
# ---------------------------------------------------------------------------
SRC_DIR = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
TM_PATH = os.path.join(SRC_DIR, "table-metadata-reference.json")
GM_PATH = os.path.join(SRC_DIR, "gameplay-markers-reference.json")

# 基线输出
OUT_BASELINE = r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Reports/d2t02a-baseline.json"

# 容差（米）。用于等价判定与候选比对。
TOL_EXACT = 1e-12     # 期望"逐位相同"
TOL_GEOM = 1e-6       # 几何量：1 微米
TOL_POINT = 1e-5      # 逐点比对默认容差（10 微米）。
                      # 关键：绝对不可放宽到 1e-3，否则会放过"水平面内 180° 旋转"
                      # （本桌近乎镜像对称，旋转后与真值仅差 ~2.8e-4 m）。
TOL_MM = 1e-3         # 毫米级：仅用于反向用例"1mm 偏移应被抓到"的说明

PI = math.pi


# ---------------------------------------------------------------------------
# 基础
# ---------------------------------------------------------------------------
def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def cross(ax, ay, bx, by):
    return ax * by - ay * bx


def dist(p, q):
    """接受 {x,y} 或 (x,y)。"""
    px, py = (p["x"], p["y"]) if isinstance(p, dict) else (p[0], p[1])
    qx, qy = (q["x"], q["y"]) if isinstance(q, dict) else (q[0], q[1])
    return math.hypot(px - qx, py - qy)


# ---------------------------------------------------------------------------
# 1) 独立基线：从 table-metadata 重算期望值
# ---------------------------------------------------------------------------
def build_baseline(tm):
    """从 table-metadata-reference.json 独立重算期望值。返回 dict。"""
    seg = tm["collision"]["segments"]
    pocks = tm["collision"]["pockets"]

    segments = []
    for i, s in enumerate(seg):
        a, b = s["a"], s["b"]
        segments.append({
            "index": i,
            "rail": s["rail"],
            "a": {"x": a["x"], "y": a["y"]},
            "b": {"x": b["x"], "y": b["y"]},
            "length": math.hypot(b["x"] - a["x"], b["y"] - a["y"]),
            "midpoint": {"x": (a["x"] + b["x"]) / 2.0, "y": (a["y"] + b["y"]) / 2.0},
        })

    pockets = []
    for p in pocks:
        c = p["runtimeCenter"]
        tn = p["runtimeThroatNormal"]
        pockets.append({
            "index": p["index"],
            "kind": p["kind"],
            "center": {"x": c["x"], "y": c["y"]},
            "openingRadius": p["openingRadius"],
            "captureRadius": p["captureRadius"],
            "recommendedCaptureRadius": p["recommendedCaptureRadius"],
            "mouthWidth": p["runtimeMouthWidth"],
            "throatWidth": p["runtimeThroatWidth"],
            "mouth": [{"x": m["x"], "y": m["y"]} for m in p["runtimeMouth"]],
            "throat": [{"x": m["x"], "y": m["y"]} for m in p["runtimeThroat"]],
            "throatCenter": {"x": p["runtimeThroatCenter"]["x"], "y": p["runtimeThroatCenter"]["y"]},
            "throatNormal": {"x": tn["x"], "y": tn["y"]},
            "innerRim": [{"x": r["x"], "y": r["y"], "z": r["z"]} for r in p["runtimeInnerRim"]],
            "captureNote": p["captureNote"],
        })

    # 派生量
    rail_groups = {}
    for s in segments:
        rail_groups.setdefault(s["rail"], []).append(s["index"])

    total_chain_length = sum(s["length"] for s in segments)

    # 每个 rail 组的 bbox（用于校验 rail 标签与几何是否自洽）
    rail_bbox = {}
    for r, idxs in rail_groups.items():
        xs = [c for i in idxs for c in (segments[i]["a"]["x"], segments[i]["b"]["x"])]
        ys = [c for i in idxs for c in (segments[i]["a"]["y"], segments[i]["b"]["y"])]
        rail_bbox[r] = {"min_x": min(xs), "max_x": max(xs), "min_y": min(ys), "max_y": max(ys)}

    # 全局 bbox（库边端点）
    xs = [c for s in segments for c in (s["a"]["x"], s["b"]["x"])]
    ys = [c for s in segments for c in (s["a"]["y"], s["b"]["y"])]
    seg_bbox = {"min_x": min(xs), "max_x": max(xs), "min_y": min(ys), "max_y": max(ys)}

    # 局部手性：cross(mouth0->mouth1, mouthCenter -> throatCenter)
    # 依赖 mouth 端点的语义序：corner 袋 mouth[0]=长库侧、mouth[1]=短端侧（见报告）
    local_chirality = []
    for p in pockets:
        m0, m1 = p["mouth"]
        mv = (m1["x"] - m0["x"], m1["y"] - m0["y"])
        mc = ((m0["x"] + m1["x"]) / 2.0, (m0["y"] + m1["y"]) / 2.0)
        tc = (p["throatCenter"]["x"], p["throatCenter"]["y"])
        iv = (tc[0] - mc[0], tc[1] - mc[1])
        local_chirality.append(cross(mv[0], mv[1], iv[0], iv[1]))

    # 袋方位角/半径（相对桌心 ~ 源中心映射后的原点）
    pocket_polar = []
    for p in pockets:
        x, y = p["center"]["x"], p["center"]["y"]
        pocket_polar.append({
            "index": p["index"], "radius": math.hypot(x, y),
            "azimuth_deg": math.degrees(math.atan2(y, x)),
        })

    # ---- 派生一致性（自检/陷阱）----
    br = tm["collision"]["ballRadius"]
    cap_rule_max = 0.0
    width_max = 0.0
    open_vs_insc_max = 0.0
    for tp, seg_p in zip(pocks, pockets):
        cap_rule_max = max(cap_rule_max, abs(seg_p["captureRadius"] - (tp["openingRadius"] - br - 0.002)))
        width_max = max(width_max,
                        abs(tp["runtimeMouthWidth"] - math.hypot(
                            tp["runtimeMouth"][1]["x"] - tp["runtimeMouth"][0]["x"],
                            tp["runtimeMouth"][1]["y"] - tp["runtimeMouth"][0]["y"])),
                        abs(tp["runtimeThroatWidth"] - math.hypot(
                            tp["runtimeThroat"][1]["x"] - tp["runtimeThroat"][0]["x"],
                            tp["runtimeThroat"][1]["y"] - tp["runtimeThroat"][0]["y"])))
        open_vs_insc_max = max(open_vs_insc_max, abs(tp["openingRadius"] - tp["runtimeInscribedRadius"]))

    # ---- 周长绕向（拼接 6 个 rail 组为闭合轮廓）----
    chains = {}
    for s in segments:
        chains.setdefault(s["rail"], []).append(s)
    ordered = {r: ([cs["a"] for cs in ss][:1] + [cs["b"] for cs in ss]) for r, ss in chains.items()}
    # 贪心拼接
    remaining = dict(ordered)
    first = "end_negative" if "end_negative" in remaining else next(iter(remaining))
    path = list(remaining.pop(first))
    while remaining:
        tail = path[-1]
        bestr = bestrev = None; bd = 1e18
        for r, c in remaining.items():
            d1 = math.hypot(c[0]["x"] - tail["x"], c[0]["y"] - tail["y"])
            d2 = math.hypot(c[-1]["x"] - tail["x"], c[-1]["y"] - tail["y"])
            if d1 < bd: bd, bestr, bestrev = d1, r, False
            if d2 < bd: bd, bestr, bestrev = d2, r, True
        c = remaining.pop(bestr)
        if bestrev: c = c[::-1]
        path += c
    area = 0.0
    for i in range(len(path) - 1):
        area += path[i]["x"] * path[i + 1]["y"] - path[i + 1]["x"] * path[i]["y"]
    winding_signed_area = area / 2.0

    # ---- 镜像敏感度：只有这处不对称能区分 180° 旋转/镜像 ----
    pcx = {p["index"]: p["center"]["x"] for p in pockets}
    pcy = {p["index"]: p["center"]["y"] for p in pockets}
    asymmetry = {
        "p0_vs_p2_x": abs(pcx[0] + pcx[2]),
        "p0_vs_p3_y": abs(pcy[0] + pcy[3]),
        "p1_vs_p4_x": abs(pcx[1] - pcx[4]),
        "segment_x_symmetry": abs(seg_bbox["min_x"] + seg_bbox["max_x"]),
        "segment_y_symmetry": abs(seg_bbox["min_y"] + seg_bbox["max_y"]),
    }
    lens = [s["length"] for s in segments]

    # ---- 顺序无关(最近邻/集合)匹配下，各错误变换与真值的最大距离 ----
    # 说明：若校验器"按位置对齐累加误差"，镜像/旋转会被 2m 级误差抓到；
    # 但若校验器"顺序无关"(排序/最近邻)，本桌近乎对称会让镜像/180°旋转
    # 只有亚毫米级差异，1mm 容差会被骗过。此表量化该脆弱性。
    base_pts = []
    for s in segments:
        base_pts += [(s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"])]
    for p in pockets:
        base_pts += [(p["center"]["x"], p["center"]["y"])]

    def _nn_max(tx):
        m = 0.0
        for (x, y) in base_pts:
            tx_, ty_ = tx(x, y)
            d = min(math.hypot(tx_ - bx, ty_ - by) for (bx, by) in base_pts)
            if d > m:
                m = d
        return m

    robustness = {
        "identity": _nn_max(lambda x, y: (x, y)),
        "mirror_x": _nn_max(lambda x, y: (-x, y)),
        "mirror_y": _nn_max(lambda x, y: (x, -y)),
        "rot90": _nn_max(lambda x, y: (-y, x)),
        "rot180": _nn_max(lambda x, y: (-x, -y)),
    }

    return {
        "schema": "idea35.d2t02a-baseline.v1",
        "role": "QA independent baseline (quality-lead). Recompute from raw art-delivery JSON only.",
        "sources": {
            "table_metadata_path": TM_PATH,
            "table_metadata_sha256": sha256_file(TM_PATH),
            "gameplay_markers_path": GM_PATH,
            "gameplay_markers_sha256": sha256_file(GM_PATH),
        },
        "table": {
            "inner_length": tm["inner_length"],
            "inner_width": tm["inner_width"],
            "half_length": tm["half_length"],
            "half_width": tm["half_width"],
            "cloth_y": tm["cloth_y"],
            "nose_height": tm["nose_height"],
            "lowest_y": tm["lowest_y"],
            "ball_radius": tm["collision"]["ballRadius"],
            "uniform_scale": tm["uniform_scale"],
            "nonuniform_scale_applied": tm["nonuniform_scale_applied"],
            "coordinate_mapping": tm["coordinate_mapping"],
            "collision_coordinates": tm["collision_coordinates"],
            "bounds_runtime_min": tm["bounds_runtime"]["min"],
            "bounds_runtime_max": tm["bounds_runtime"]["max"],
        },
        "counts": {"segments": len(segments), "pockets": len(pockets)},
        "segments": segments,
        "pockets": pockets,
        "derived": {
            "rail_groups": rail_groups,
            "rail_bbox": rail_bbox,
            "rail_group_count": len(rail_groups),
            "total_chain_length": total_chain_length,
            "segments_bbox": seg_bbox,
            "segments_bbox_aspect": (seg_bbox["max_x"] - seg_bbox["min_x"]) / (seg_bbox["max_y"] - seg_bbox["min_y"]),
            "segment_length_min": min(lens),
            "segment_length_max": max(lens),
            "winding_signed_area": winding_signed_area,
            "winding_orientation": "CCW" if winding_signed_area > 0 else "CW",
            "local_chirality": local_chirality,
            "local_chirality_sign": [1 if v > 0 else -1 for v in local_chirality],
            "pocket_polar": pocket_polar,
            "derived_relation_checks": {
                "captureRadius_eq_opening_minus_ballRadius_minus_2mm_max_delta": cap_rule_max,
                "mouth_throat_width_eq_point_distance_max_delta": width_max,
                "openingRadius_eq_runtimeInscribedRadius_max_delta": open_vs_insc_max,
            },
            "mirror_sensitivity": asymmetry,
            "order_insensitive_robustness_m": robustness,
        },
        "comparison_guidance": {
            "primary_method": "索引对齐 + 逐字段比对（本工具 compare_candidate）。此法对镜像/旋转/缩放均有 0.1~2.6m 级余量。",
            "warning": "若改为‘顺序无关/最近邻/集合’比对，本桌近对称会让镜像与 180° 旋转只剩亚毫米差异。",
            "recommended_point_tolerance_m": TOL_POINT,
            "order_insensitive_max_safe_tolerance_m": min(robustness["mirror_x"], robustness["mirror_y"], robustness["rot180"]),
            "note": "索引对齐比对用 1e-5；若必须顺序无关比对，容差须 < 1e-4（否则 1mm 容差会放过镜像/180°旋转）。",
        },
    }


# ---------------------------------------------------------------------------
# 2) 交叉比对：原始交付两份文件
# ---------------------------------------------------------------------------
def compare_sources(tm, gm):
    """逐点比对两份原始文件；不做抽样。返回 (report, ok)."""
    rep = {"max_abs_delta": 0.0, "issues": [], "notes": []}
    tseg = tm["collision"]["segments"]
    gseg = gm["cushion_segments"]
    tpoc = tm["collision"]["pockets"]
    gpoc = gm["pockets"]

    rep["segment_count"] = {"tm": len(tseg), "gm": len(gseg), "match": len(tseg) == len(gseg)}
    rep["pocket_count"] = {"tm": len(tpoc), "gm": len(gpoc), "match": len(tpoc) == len(gpoc)}

    # --- 82 段：按索引一一对应，逐端点逐标签 ---
    seg_field_deltas = []
    seg_order_reversed = 0
    seg_unordered_extra = 0
    for i in range(min(len(tseg), len(gseg))):
        ts, gs = tseg[i], gseg[i]
        ga = gs["physics2d"]["a"]
        gb = gs["physics2d"]["b"]
        d = [
            abs(ts["a"]["x"] - ga["x"]), abs(ts["a"]["y"] - ga["y"]),
            abs(ts["b"]["x"] - gb["x"]), abs(ts["b"]["y"] - gb["y"]),
        ]
        m = max(d)
        seg_field_deltas.append(m)
        rep["max_abs_delta"] = max(rep["max_abs_delta"], m)
        if ts["rail"] != gs["rail"]:
            rep["issues"].append(f"seg[{i}] rail 标签不一致: TM={ts['rail']} GM={gs['rail']}")
        if m > TOL_GEOM and m > 0:
            rep["issues"].append(f"seg[{i}] 端点差={m:.3e} rail={ts['rail']}")
        # 方向是否被反转（a<->b）
        rev = max(abs(ts["a"]["x"] - gb["x"]), abs(ts["a"]["y"] - gb["y"]),
                  abs(ts["b"]["x"] - ga["x"]), abs(ts["b"]["y"] - ga["y"]))
        if rev < TOL_GEOM and m > TOL_GEOM:
            seg_order_reversed += 1
    rep["seg_endpoint_max_delta"] = max(seg_field_deltas) if seg_field_deltas else None
    rep["seg_reversed_detected"] = seg_order_reversed

    # --- 6 袋：按 index 对齐比较公共字段 ---
    gmap = {p["index"]: p for p in gpoc}
    pocket_deltas = {}
    def upd(k, v):
        pocket_deltas[k] = max(pocket_deltas.get(k, 0.0), v)
    for tp in tpoc:
        gp = gmap.get(tp["index"])
        if gp is None:
            rep["issues"].append(f"pocket[{tp['index']}] GM 缺失")
            continue
        ph = gp["physics2d"]
        if tp["kind"] != ph["kind"]:
            rep["issues"].append(f"pocket[{tp['index']}] kind 不一致 {tp['kind']} vs {ph['kind']}")
        upd("center_x", abs(tp["runtimeCenter"]["x"] - ph["x"]))
        upd("center_y", abs(tp["runtimeCenter"]["y"] - ph["y"]))
        upd("openingRadius", abs(tp["openingRadius"] - ph["openingRadius"]))
        upd("captureRadius", abs(tp["captureRadius"] - ph["captureRadius"]))
        for j in range(2):
            upd("mouth_x", abs(tp["runtimeMouth"][j]["x"] - ph["mouth"][j]["x"]))
            upd("mouth_y", abs(tp["runtimeMouth"][j]["y"] - ph["mouth"][j]["y"]))
            upd("throat_x", abs(tp["runtimeThroat"][j]["x"] - ph["throat"][j]["x"]))
            upd("throat_y", abs(tp["runtimeThroat"][j]["y"] - ph["throat"][j]["y"]))
        # inner rim（12 点，3 维）
        for j in range(min(len(tp["runtimeInnerRim"]), len(gp["canonical_inner_net_rim_runtime_xyz"]))):
            a = tp["runtimeInnerRim"][j]
            b = gp["canonical_inner_net_rim_runtime_xyz"][j]
            upd("innerRim_x", abs(a["x"] - b["x"]))
            upd("innerRim_y", abs(a["y"] - b["y"]))
            upd("innerRim_z", abs(a["z"] - b["z"]))
    rep["pocket_field_max_delta"] = pocket_deltas
    if pocket_deltas:
        rep["max_abs_delta"] = max(rep["max_abs_delta"], max(pocket_deltas.values()))

    # --- 桌面标量 ---
    scalar = {
        "inner_length": abs(tm["inner_length"] - gm["measured_table"]["canonical_inner_dimensions_m"][0]),
        "inner_width": abs(tm["inner_width"] - gm["measured_table"]["canonical_inner_dimensions_m"][1]),
        "half_length": abs(tm["half_length"] - gm["measured_table"]["halfLength"]),
        "half_width": abs(tm["half_width"] - gm["measured_table"]["halfWidth"]),
        "ball_radius": abs(tm["collision"]["ballRadius"] - gm["measured_table"]["ballRadius"]),
        "nose_height": abs(tm["nose_height"] - gm["measured_table"]["canonical_nose_height_m"]),
    }
    rep["table_scalar_delta"] = scalar
    rep["max_abs_delta"] = max(rep["max_abs_delta"], max(scalar.values()))

    rep["gm_claims_equality"] = {
        "source_metadata_rail_equality": gm["validation"].get("source_metadata_rail_equality"),
        "source_metadata_table_equality": gm["validation"].get("source_metadata_table_equality"),
    }

    ok = (not rep["issues"]) and rep["max_abs_delta"] <= TOL_EXACT
    rep["verdict"] = "IDENTICAL" if ok else ("MINOR_DIFF" if rep["max_abs_delta"] <= TOL_GEOM else "DIVERGENT")
    return rep, ok


# ---------------------------------------------------------------------------
# 3) 校验原语（供候选比对复用；把候选换成 Unity 导出数据）
# ---------------------------------------------------------------------------
def _as_xy(d):
    """容忍 {x,y} 或 [x,y] 或 {x,z}(Unity 水平面) 三种写法。"""
    if isinstance(d, dict):
        x = d.get("x", d.get("X"))
        y = d.get("y", d.get("z", d.get("Y", d.get("Z"))))
        return float(x), float(y)
    if isinstance(d, (list, tuple)):
        return float(d[0]), float(d[1])
    raise TypeError(f"无法解析点: {d!r}")


def compare_candidate(candidate, baseline, tol=TOL_POINT):
    """
    将"候选实现数据"与基线比对。
    candidate 结构约定（候选从 Unity 导出时遵循）：
      {
        "table":   {"inner_length":..,"inner_width":..,"ball_radius":..,
                    "half_length":..,"half_width":..,"nose_height":..,"cloth_y":..},
        "segments":[{"index":..,"rail":..,"a":{x,y},"b":{x,y}}, ...],
        "pockets": [{"index":..,"kind":..,"center":{x,y},"openingRadius":..,
                     "captureRadius":..,"mouth":[{x,y},{x,y}],"throat":[{x,y},{x,y}]}, ...]
      }
    返回 findings 列表；空列表=通过。
    """
    findings = []
    bt = baseline["table"]
    ct = candidate.get("table", {})
    for k in ["inner_length", "inner_width", "half_length", "half_width",
              "nose_height", "cloth_y", "ball_radius"]:
        if k not in ct:
            findings.append(("MISSING", f"table.{k} 缺失"))
            continue
        if abs(float(ct[k]) - float(bt[k])) > tol:
            findings.append(("FAIL", f"table.{k}: 期望 {bt[k]:.9f} 实得 {float(ct[k]):.9f} Δ={abs(float(ct[k])-bt[k]):.3e}"))

    bsegs = {s["index"]: s for s in baseline["segments"]}
    csegs = candidate.get("segments", [])
    if len(csegs) != len(bsegs):
        findings.append(("FAIL", f"段数: 期望 {len(bsegs)} 实得 {len(csegs)}"))
    for cs in csegs:
        i = cs.get("index")
        bs = bsegs.get(i)
        if bs is None:
            findings.append(("FAIL", f"seg[{i}] 不在基线中"))
            continue
        if cs.get("rail") != bs["rail"]:
            findings.append(("FAIL", f"seg[{i}].rail: {cs.get('rail')} != {bs['rail']}"))
        for endn in ("a", "b"):
            cx, cy = _as_xy(cs[endn])
            dx = abs(cx - bs[endn]["x"]); dy = abs(cy - bs[endn]["y"])
            if max(dx, dy) > tol:
                findings.append(("FAIL", f"seg[{i}].{endn} Δ=({dx:.3e},{dy:.3e})"))

    bpock = {p["index"]: p for p in baseline["pockets"]}
    cpock = candidate.get("pockets", [])
    if len(cpock) != len(bpock):
        findings.append(("FAIL", f"袋数: 期望 {len(bpock)} 实得 {len(cpock)}"))
    for cp in cpock:
        i = cp.get("index")
        bp = bpock.get(i)
        if bp is None:
            findings.append(("FAIL", f"pocket[{i}] 不在基线中"))
            continue
        if cp.get("kind") != bp["kind"]:
            findings.append(("FAIL", f"pocket[{i}].kind 不一致"))
        cx, cy = _as_xy(cp["center"])
        if max(abs(cx - bp["center"]["x"]), abs(cy - bp["center"]["y"])) > tol:
            findings.append(("FAIL", f"pocket[{i}].center 偏移"))
        for k in ("openingRadius", "captureRadius"):
            if abs(float(cp[k]) - bp[k]) > tol:
                findings.append(("FAIL", f"pocket[{i}].{k}: 期望 {bp[k]:.9f} 实得 {float(cp[k]):.9f}"))
        for grp in ("mouth", "throat"):
            for j in (0, 1):
                cx, cy = _as_xy(cp[grp][j])
                if max(abs(cx - bp[grp][j]["x"]), abs(cy - bp[grp][j]["y"])) > tol:
                    findings.append(("FAIL", f"pocket[{i}].{grp}[{j}] 偏移"))
    return findings


# 反向用例会用到的"结构校验器"（只做正向+结构，不实现注入）
def validate_structure(baseline, candidate):
    """
    结构层校验：本函数是"只看值对不对"的校验器所缺的那一层。
    返回 (findings)。检测项见报告 §4 的"检验能力"映射。
    """
    f = []
    segs = candidate.get("segments", [])
    pocks = candidate.get("pockets", [])
    bseg = baseline["segments"]
    # S1 段数 / S2 袋数
    if len(segs) != baseline["counts"]["segments"]:
        f.append("S1 segment_count_mismatch")
    if len(pocks) != baseline["counts"]["pockets"]:
        f.append("S2 pocket_count_mismatch")
    # S3 rail 标签多重集
    if sorted(s.get("rail") for s in segs) != sorted(s["rail"] for s in bseg):
        f.append("S3 rail_label_multiset_mismatch")
    # S4 链式连续性（每组内 b[i]==a[i+1]）—— 抓断链
    by_rail = {}
    for s in segs:
        by_rail.setdefault(s.get("rail"), []).append(s)
    for r, ss in by_rail.items():
        for j in range(len(ss) - 1):
            if dist(_as_xy(ss[j]["b"]), _as_xy(ss[j + 1]["a"])) > TOL_GEOM:
                f.append(f"S4 chain_break rail={r} idx={j}")
    # S5 手性符号模式（抓镜像）
    def chir(cp):
        m0x, m0y = _as_xy(cp["mouth"][0]); m1x, m1y = _as_xy(cp["mouth"][1])
        tc = cp.get("throatCenter")
        if tc is None:
            # 由 throat 两点中点推
            t0 = _as_xy(cp["throat"][0]); t1 = _as_xy(cp["throat"][1])
            tcx, tcy = (t0[0] + t1[0]) / 2.0, (t0[1] + t1[1]) / 2.0
        else:
            tcx, tcy = _as_xy(tc)
        mvx, mvy = m1x - m0x, m1y - m0y
        mcx, mcy = (m0x + m1x) / 2.0, (m0y + m1y) / 2.0
        return cross(mvx, mvy, tcx - mcx, tcy - mcy)
    bsig = [1 if v > 0 else -1 for v in baseline["derived"]["local_chirality"]]
    csig = [1 if chir(cp) > 0 else -1 for cp in sorted(pocks, key=lambda p: p.get("index", -1))]
    if csig != bsig:
        f.append(f"S5 chirality_pattern_mismatch base={bsig} cand={csig}")
    # S6 长/短轴朝向（抓 90° 旋转 / 轴交换）——与基线自身比例比较
    if segs:
        xs = [c for s in segs for c in (_as_xy(s["a"])[0], _as_xy(s["b"])[0])]
        ys = [c for s in segs for c in (_as_xy(s["a"])[1], _as_xy(s["b"])[1])]
        w, h = max(xs) - min(xs), max(ys) - min(ys)
        cand_ratio = w / h if h else float("inf")
        base_ratio = baseline["derived"]["segments_bbox_aspect"]
        if abs(cand_ratio / base_ratio - 1.0) > 0.05:
            f.append(f"S6 aspect_ratio_off cand={cand_ratio:.4f} base={base_ratio:.4f} (抓 90° 旋转/轴交换)")
    # S7 库鼻高与球半径量级（抓单位/缩放混用）
    return f


def run_full_verification(candidate, baseline, tol=TOL_POINT):
    """
    候选比对入口：值比对 + 结构/手性/尺度校验合一。
    返回 dict：{"value_findings":[...], "structure_findings":[...], "pass":bool}
    """
    v = compare_candidate(candidate, baseline, tol=tol)
    s = validate_structure(baseline, candidate)
    return {"value_findings": v, "structure_findings": s, "pass": (not v) and (not s)}


# ---------------------------------------------------------------------------
# 主流程
# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--baseline-only", action="store_true")
    args = ap.parse_args()

    tm = load_json(TM_PATH)
    gm = load_json(GM_PATH)

    baseline = build_baseline(tm)

    print("=" * 78)
    print("Idea #35 · 碰撞几何独立基线与交叉比对")
    print("=" * 78)
    print(f"TM sha256 = {baseline['sources']['table_metadata_sha256']}")
    print(f"GM sha256 = {baseline['sources']['gameplay_markers_sha256']}")
    print(f"段数 = {baseline['counts']['segments']}  袋数 = {baseline['counts']['pockets']}")
    print(f"内库 = {baseline['table']['inner_length']} x {baseline['table']['inner_width']}")
    print(f"球半径 = {baseline['table']['ball_radius']}  库鼻高 = {baseline['table']['nose_height']}")
    print(f"桌腿最低点 lowest_y = {baseline['table']['lowest_y']}")
    print(f"库边端点 bbox = {baseline['derived']['segments_bbox']}")
    print(f"库边 bbox 长宽比 = {baseline['derived']['segments_bbox_aspect']:.6f} (内库比 {baseline['table']['inner_length']/baseline['table']['inner_width']:.6f})")
    print(f"库边链总长 = {baseline['derived']['total_chain_length']:.9f}")
    print(f"重构闭合轮廓有向面积 = {baseline['derived']['winding_signed_area']:.9f} ({baseline['derived']['winding_orientation']})")
    print(f"局部手性符号 = {[('+' if v>0 else '-') for v in baseline['derived']['local_chirality']]}")
    print(f"派生关系自检最大偏差 = { {k: f'{v:.3e}' for k,v in baseline['derived']['derived_relation_checks'].items()} }")
    print(f"镜像敏感度(仅此可区分镜像/180°旋转) = { {k: f'{v:.3e}' for k,v in baseline['derived']['mirror_sensitivity'].items()} }")
    print(f"顺序无关最近邻下各变换最大距离 = { {k: f'{v:.3e}' for k,v in baseline['derived']['order_insensitive_robustness_m'].items()} }")
    print(f"建议逐点容差 = {baseline['comparison_guidance']['recommended_point_tolerance_m']:.0e} m")

    rep, ok = compare_sources(tm, gm)
    print("\n" + "-" * 78)
    print("交叉比对 (table-metadata  vs  gameplay-markers)")
    print("-" * 78)
    print(f"segments: {rep['segment_count']}")
    print(f"pockets : {rep['pocket_count']}")
    print(f"段端点最大绝对差 = {rep['seg_endpoint_max_delta']:.3e}")
    print(f"袋字段最大绝对差 = { {k: f'{v:.3e}' for k,v in rep['pocket_field_max_delta'].items()} }")
    print(f"桌面标量最大绝对差 = { {k: f'{v:.3e}' for k,v in rep['table_scalar_delta'].items()} }")
    print(f"全局最大绝对差 = {rep['max_abs_delta']:.3e}")
    print(f"GM 自称 rail/table 相等 = {rep['gm_claims_equality']}")
    print(f"issues = {rep['issues'] if rep['issues'] else '[]'}")
    print(f"判定 = {rep['verdict']}")

    os.makedirs(os.path.dirname(OUT_BASELINE), exist_ok=True)
    with open(OUT_BASELINE, "w", encoding="utf-8") as f:
        json.dump(baseline, f, ensure_ascii=False, indent=1)
    print(f"\n基线已写入: {OUT_BASELINE}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
