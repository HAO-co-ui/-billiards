#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
D4 spec 复核探针：验证 quality-lead 复核报告中的两项「中-高」结论。

任务 1 —— 误差累积（对应复核 ③）
  以解析解为真值，对比两种位置更新方案在**等减速运动**下的漂移：
    方案 A = spec §4.1 原样写下：position += velocity * remaining  （一阶/前向欧拉）
    方案 B = 端点精确：position += (velocity - ½·a·Δ·v̂) * remaining （梯形/中点）
  两个场景：
    (a) 纯滚动（无墙）—— 隔离自由飞行积分误差
    (b) 多库多次弹跳  —— 叠加接触线性化误差

  判据：误差若不累积，方案 A 的漂移应随 T 有界；若累积，应呈线性增长。

任务 2 —— 袋口逃逸（对应复核 ⑥）
  球自台心 (0,0) 以 v = (0,−2) m/s 直指 side pocket[1]，逐点求到全部 82 段的最小距离。
  若最小距离恒 > 球半径，则球确实从袋口缺口离开台面 —— 归纳不变量不闭合。

只读。
"""

import argparse
import json
import math
import sys


# ── 解析真值 ────────────────────────────────────────────────────────────

def analytic_roll_distance(v0, a):
    """等减速纯滚动：从 v0 到停止的总位移（沿运动方向）。"""
    return v0 * v0 / (2.0 * a) if a > 0 else float("inf")


def analytic_bounce_run(L, r, v0, a, e, max_contacts=10000):
    """1D 多库弹跳解析解。墙 = 台面边界，接触面位于 |y| = L − r。

    返回：接触序列 [(contact_index, y_at_contact, speed_before, speed_after)]、终态 y。
    """
    wall = L - r
    y = 0.0
    v = v0
    contacts = []
    for k in range(max_contacts):
        if v == 0.0:
            break
        d = wall - y if v > 0 else (-wall) - y     # 到目标墙的带符号位移
        dist = abs(d)
        stop_dist = v * v / (2.0 * a) if a > 0 else float("inf")
        if dist >= stop_dist:
            # 未到墙即停
            y += math.copysign(stop_dist, v)
            v = 0.0
            break
        speed_before = v * v - 2.0 * a * dist
        if speed_before < 0.0:
            speed_before = 0.0
        speed_before = math.sqrt(speed_before)
        y = wall if v > 0 else -wall
        v = -math.copysign(speed_before * e, v)
        contacts.append((k, y, speed_before, abs(v)))
    return contacts, y


# ── 方案 A / B ──────────────────────────────────────────────────────────

def sim_pure_roll(v0, a, dt_sub, total_time, scheme):
    """纯滚动（无墙）。返回位移。"""
    y = 0.0
    v = v0
    steps = int(round(total_time / dt_sub))
    for _ in range(steps):
        if v == 0.0:
            break
        if scheme == "A":
            y += v * dt_sub
            dv = a * dt_sub
            v = 0.0 if v <= dv else v - dv
        else:  # B
            v_avg = v - 0.5 * a * dt_sub
            if v_avg < 0.0:
                v_avg = 0.0
            y += v_avg * dt_sub
            dv = a * dt_sub
            v = 0.0 if v <= dv else v - dv
    return y


def sim_bounce(L, r, v0, a, e, dt_sub, scheme, max_substeps=2_000_000):
    """1D 多库弹跳。返回 (接触次数, 终态 y, 迭代守卫触发次数, 总子步数)。"""
    wall = L - r
    y = 0.0
    v = v0
    n_contacts = 0
    guard_trips = 0
    n_sub = 0

    for _ in range(max_substeps):
        n_sub += 1
        if v == 0.0:
            break

        if scheme == "A":
            v_move = v
        else:
            v_move = v - math.copysign(0.5 * a * dt_sub, v)
            if v_move * v < 0.0:
                v_move = 0.0

        remaining = dt_sub
        it = 0
        while remaining > 0.0:
            it += 1
            if it > 16:
                guard_trips += 1
                break
            if v_move == 0.0:
                y += 0.0
                break
            target = wall if v_move > 0 else -wall
            d = target - y
            # 本剩余时间内能否到达
            t_hit = d / v_move
            if t_hit > remaining or t_hit < 0.0:
                y += v_move * remaining
                remaining = 0.0
            else:
                y = target
                remaining -= t_hit
                v = -e * v
                v_move = -v_move
                n_contacts += 1

        # 子步末的减速（spec §4.1 第 4 步的位置）
        if v != 0.0:
            dv = a * dt_sub
            v = 0.0 if abs(v) <= dv else v - math.copysign(dv, v)

    return n_contacts, y, guard_trips, n_sub


# ── 任务 2：袋口逃逸 ────────────────────────────────────────────────────

def seg_closest_distance(p, a, b):
    abx, aby = b[0] - a[0], b[1] - a[1]
    l2 = abx * abx + aby * aby
    if l2 <= 0.0:
        return math.hypot(p[0] - a[0], p[1] - a[1])
    t = ((p[0] - a[0]) * abx + (p[1] - a[1]) * aby) / l2
    t = max(0.0, min(1.0, t))
    qx, qy = a[0] + t * abx, a[1] + t * aby
    return math.hypot(p[0] - qx, p[1] - qy)


def pocket_escape_scan(tm, v0y, y_far, steps=200000):
    segs = []
    for s in tm["cushion_segments"]:
        segs.append(((s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"])))
    ball_r = float(tm["collision"]["ballRadius"])

    worst = float("inf")
    worst_s = None
    worst_y = None
    first_contact_y = None
    for i in range(steps + 1):
        y = y_far * i / steps
        p = (0.0, y)
        for si, (a, b) in enumerate(segs):
            d = seg_closest_distance(p, a, b)
            if d < worst:
                worst, worst_s, worst_y = d, si, y
            if d < ball_r and first_contact_y is None:
                first_contact_y = y

    pockets = []
    for pk in tm["pockets"]:
        tc = (pk["runtimeThroatCenter"]["x"], pk["runtimeThroatCenter"]["y"])
        trigger = float(pk["openingRadius"]) + ball_r
        # 球沿 x=0 下行的最近距离
        dmin = min(math.hypot(0.0 - tc[0], y - tc[1]) for y in
                   [y_far * i / steps for i in range(steps + 1)])
        pockets.append({
            "index": pk["index"], "kind": pk["kind"],
            "throatCenter": tc, "openingRadius": pk["openingRadius"],
            "triggerRadius": trigger,
            "minDistanceAlongPath": dmin,
            "triggers": dmin <= trigger,
        })

    return {
        "path": {"from": [0.0, 0.0], "to": [0.0, y_far], "velocity": [0.0, v0y], "samples": steps + 1},
        "ballRadius": ball_r,
        "minDistanceToAnySegment": worst,
        "minDistanceSegmentIndex": worst_s,
        "minDistanceAtY": worst_y,
        "firstContactY": first_contact_y,
        "neverContactsAnySegment": first_contact_y is None,
        "pocketProximity": pockets,
    }


# ── main ────────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    with open(args.source, "r", encoding="utf-8") as fh:
        tm = json.load(fh)

    half_w = float(tm["collision"]["halfWidth"])
    ball_r = float(tm["collision"]["ballRadius"])
    dt_sub = 1.0 / 360.0

    report = {"dtSub": dt_sub, "ballRadius": ball_r, "halfWidth": half_w}

    # ── 任务 1(a) 纯滚动 ──
    roll = []
    for a in (0.5, 1.0, 2.0, 5.0):
        v0 = 2.0
        T = v0 / a
        truth = analytic_roll_distance(v0, a)
        for scheme in ("A", "B"):
            got = sim_pure_roll(v0, a, dt_sub, T, scheme)
            roll.append({
                "a": a, "v0": v0, "T": T, "scheme": scheme,
                "truth": truth, "got": got, "error": got - truth,
            })
    report["task1a_pureRoll"] = roll

    # ── 任务 1(b) 多库弹跳 ──
    bounce = []
    for a in (0.5, 5.0):
        v0 = 2.0
        e = 0.95
        truth_contacts, truth_y = analytic_bounce_run(half_w, ball_r, v0, a, e)
        for scheme in ("A", "B"):
            nc, y, guards, nsub = sim_bounce(half_w, ball_r, v0, a, e, dt_sub, scheme)
            bounce.append({
                "a": a, "v0": v0, "e": e, "scheme": scheme,
                "truthContacts": len(truth_contacts), "gotContacts": nc,
                "truthFinalY": truth_y, "gotFinalY": y,
                "finalYError": y - truth_y,
                "guardTrips": guards, "subSteps": nsub,
            })
    report["task1b_bounce"] = bounce

    # ── 任务 1(c) 误差随 T 的标度（判定是否累积）──
    scale = []
    a = 1.0
    v0 = 2.0
    for frac in (0.25, 0.5, 0.75, 1.0):
        T = frac * (v0 / a)
        truth = analytic_roll_distance(v0, a) * (2.0 * frac - frac * frac)
        got = sim_pure_roll(v0, a, dt_sub, T, "A")
        scale.append({"T": T, "T_over_taustop": frac, "truth": truth,
                      "got": got, "error": got - truth,
                      "cumulativeBound_half_a_dt_sub_T": 0.5 * a * dt_sub * T})
    report["task1c_errorScaling"] = scale

    # ── 任务 2 袋口逃逸 ──
    report["task2_pocketEscape"] = pocket_escape_scan(tm, -2.0, -0.80, steps=40000)

    with open(args.out, "w", encoding="utf-8") as fh:
        json.dump(report, fh, ensure_ascii=False, indent=2)
        fh.write("\n")

    print("=" * 68)
    print("任务 1(a) 纯滚动：解析真值 vs 两种位置更新方案")
    print(f"  dt_sub = 1/360 s, v0 = 2 m/s")
    print(f"  {'a':>5} {'真值(m)':>12} {'方案':>5} {'实测(m)':>12} {'误差(mm)':>12}")
    for r in roll:
        print(f"  {r['a']:>5.1f} {r['truth']:>12.6f} {r['scheme']:>5} "
              f"{r['got']:>12.6f} {r['error']*1000:>12.4f}")
    print()
    print("任务 1(b) 多库弹跳（e=0.95）")
    print(f"  {'a':>5} {'方案':>5} {'接触次数 真/实':>16} {'终态y 真值':>14} {'终态y 实测':>14} {'误差(mm)':>12}")
    for r in bounce:
        print(f"  {r['a']:>5.1f} {r['scheme']:>5} "
              f"{str(r['truthContacts'])+'/'+str(r['gotContacts']):>16} "
              f"{r['truthFinalY']:>14.6f} {r['gotFinalY']:>14.6f} {r['finalYError']*1000:>12.4f}")
    print()
    print("任务 1(c) 方案 A 误差随 T 的标度（a=1.0, v0=2.0）")
    print(f"  {'T(s)':>8} {'T/T_stop':>10} {'误差(mm)':>12} {'累积上界 ½aΔT (mm)':>22}")
    for r in scale:
        print(f"  {r['T']:>8.4f} {r['T_over_taustop']:>10.2f} {r['error']*1000:>12.4f} "
              f"{r['cumulativeBound_half_a_dt_sub_T']*1000:>22.4f}")
    print()
    print("任务 2 袋口逃逸扫描")
    t2 = report["task2_pocketEscape"]
    print(f"  路径 {t2['path']['from']} -> {t2['path']['to']}，v = {t2['path']['velocity']}")
    print(f"  球半径 {t2['ballRadius']:.9f} m")
    print(f"  到全部 82 段的最小距离 = {t2['minDistanceToAnySegment']:.9f} m "
          f"(seg[{t2['minDistanceSegmentIndex']}] @ y={t2['minDistanceAtY']:.6f})")
    print(f"  是否全程未接触任何段: {t2['neverContactsAnySegment']}")
    for p in t2["pocketProximity"]:
        print(f"  pocket[{p['index']}] {p['kind']:7s} throatCenter={p['throatCenter']} "
              f"触发半径={p['triggerRadius']:.6f} 路径最近={p['minDistanceAlongPath']:.6f} "
              f"--> 触发={p['triggers']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
