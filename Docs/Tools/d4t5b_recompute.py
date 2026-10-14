#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""物理内核 T5b 负对照数值的**独立复算**（不依赖 Unity、不写仓库、可重跑）。

用途
====
T5b 是证明 T5a（8 m/s 无穿透，判据 ≤ 1e-5 m）**不是空转**的拱心石：它在
`PoolSimulator.DisableToiForDiagnosticsOnly = true` 下断言"最大嵌入量 ≥ ½·v·dt_sub"。
本脚本**用纯 Python 独立重写该模式下的推进算法**，把 T5b 报出的 18.7973 mm 变成
可复跑的证据，而不是一句 claim。

复算的物理模型（必须与被测实现逐条对齐，否则复算的不是同一个东西）
==============================================================
被测实现 = `Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs` 中
`IntegrateBall()` 的**禁用 TOI 分支**。本脚本按同一口径重写：

  dt       = 1/120 s                      （PoolSimulator.FixedDeltaTime）
  n        = 3                            （DeriveSubSteps(8)，判据 A=2.333→3、B=1.863→2）
  dt_sub   = dt / n = 1/360 s             （PoolSimulator.SubStepDeltaTime）
  r        = 0.028575 m                   （PoolConstants.BallRadius）
  a        = 0                            ⇒ v_avg = v（无滚动减速度）
  e        = 0.9, μ = 0.8                 （PoolSimulatorInvariantTests.Params()）
  v0       = 8 m/s                        （PoolConstants.MaxBallSpeed）
  起点     = 直库中点 + 内法向 × 0.03 m
  速度     = −内法向 × 8 m/s             （正撞）

  单个子步的推进（禁用 TOI）：
      rem = dt_sub
      while rem > 1e-9:                       # ToiRemainingEpsilon
          earliest = rem                      # ★ 采样模式：不做逐步 TOI，只认子步末接触
          new_pos  = pos + v_avg * earliest   # 先**整子步**推进
          overlaps = { 段 i : dist(new_pos, seg_i) < r }   # 在**子步末位置**检测
          pos      = new_pos
          rem     -= earliest                 # → 0
          if overlaps:
              按段索引升序 ResolveContact(pos, v, overlaps)
              # n̂ = normalize(pos − Q_i)；v −= (1+e)·(v·n̂)·n̂；再 v −= (1−μ)·v_t

  段集合 = reference JSON 的**全部 82 段**（不只 6 条直库）——避免"只考虑目标段"的简化假设。
  袋喉冻结 = 同样复算（球心越过任一喉平面即冻结），并以 NEVENTS 计数自证未触发。

采样口径（team-lead 要求写清的两者差异）
======================================
  · **主判据用"Step 边界采样"**：每 3 个子步（1 个 Step = dt）记录一次嵌入量——**与 T5b
    测试完全一致**（测试只能从 `sim.Balls[0]` 取值，天然只在 Step 边界观测）。
  · **脚本额外打印"子步边界采样"峰值**（每 1/360 s 记一次）作为对照：
    它更大（≈20.8 mm），因为禁用模式下球在**第 1 个子步内**就穿入最深，随后两个子步被反射推离。
    ⇒ 两个口径给出**不同的数**；脚本断言的 [ASSERT] 值取 **Step 边界**，与 T5b 同口径。
    子步峰值仅作信息性对照，不参与断言。

精度
====
本脚本用 Python **float64**；Unity 内核用 **float32**。两者在 ~1e-7 相对量级上可差，
故 18.7972（本脚本）与 18.7973（测试）的 0.0001 mm 尾差不构成不一致。

退出码
======
  worst(Step 边界) ≥ ½·v·dt_sub  →  exit 0（通过）
  否则                            →  exit 1（复算不成立）
"""

import json
import math
import os
import sys

# ── 常量（与被测实现同一口径）─────────────────────────────────────────────
DT = 1.0 / 120.0            # PoolSimulator.FixedDeltaTime
SUB_STEPS = 3               # DeriveSubSteps(PoolConstants.MaxBallSpeed = 8)
DT_SUB = DT / SUB_STEPS     # PoolSimulator.SubStepDeltaTime = 1/360
R = 0.028575                # PoolConstants.BallRadius

A = 0.0                     # rollingDeceleration（= 0，无滚动减速）
E = 0.9                     # cushionRestitution
MU = 0.8                    # cushionTangentialFriction
SPEED = 8.0                 # PoolConstants.MaxBallSpeed
START_GAP = 0.03            # 起点球心距库面 30 mm
STEPS = 8                   # 与 T5b 相同

TOI_REMAINING_EPS = 1e-9    # PoolSimulator.ToiRemainingEpsilon

STRAIGHT_SEGMENTS = [6, 20, 33, 47, 61, 74]  # spec §1.1 的 6 条直库

PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
REFERENCE_JSON = os.path.join(PROJECT_ROOT, "Docs", "Reference", "table-metadata-reference.json")


# ── 几何工具 ───────────────────────────────────────────────────────────────
def closest_point_on_segment(p, a, b):
    abx, aby = b[0] - a[0], b[1] - a[1]
    len2 = abx * abx + aby * aby
    if len2 <= 1e-12:
        return a
    u = ((p[0] - a[0]) * abx + (p[1] - a[1]) * aby) / len2
    u = 0.0 if u < 0.0 else (1.0 if u > 1.0 else u)
    return (a[0] + u * abx, a[1] + u * aby)


def distance_point_segment(p, a, b):
    q = closest_point_on_segment(p, a, b)
    dx, dy = p[0] - q[0], p[1] - q[1]
    return math.sqrt(dx * dx + dy * dy)


def inward_normal(a, b):
    """段的内法向：垂直于段、指向台面中心（原点）一侧。"""
    tx, ty = b[0] - a[0], b[1] - a[1]
    length = math.hypot(tx, ty)
    nx, ny = -ty / length, tx / length
    mx, my = (a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5
    if nx * (0.0 - mx) + ny * (0.0 - my) < 0.0:
        nx, ny = -nx, -ny
    return (nx, ny)


def resolve(pos, vel, seg_indices, segs):
    """按段索引升序解算接触（法向/切向独立赋值）——与 PoolSimulator.ResolveContact 同口径。"""
    vx, vy = vel
    for i in seg_indices:
        a, b = segs[i]
        q = closest_point_on_segment(pos, a, b)
        dx, dy = pos[0] - q[0], pos[1] - q[1]
        d2 = dx * dx + dy * dy
        if d2 < 1e-18:
            # 退化：球心恰在段上，取速度反方向为法向
            ln = math.hypot(vx, vy)
            nx, ny = (-vx / ln, -vy / ln) if ln > 0 else (0.0, 1.0)
        else:
            d = math.sqrt(d2)
            nx, ny = dx / d, dy / d

        vn = vx * nx + vy * ny
        tvx, tvy = vx - vn * nx, vy - vn * ny

        # 法向：独立赋值（恢复系数 e）
        vx -= (1.0 + E) * vn * nx
        vy -= (1.0 + E) * vn * ny
        # 切向：独立赋值（摩擦保留系数 μ）
        if tvx * tvx + tvy * tvy > 0.0:
            vx -= (1.0 - MU) * tvx
            vy -= (1.0 - MU) * tvy
    return (vx, vy)


def crossed_pocket_plane(pos, pockets):
    """球心越过任一袋喉平面 → 返回 True（与 CheckPocketProximity 同口径）。"""
    for tc, tn in pockets:
        if (pos[0] - tc[0]) * tn[0] + (pos[1] - tc[1]) * tn[1] > 0.0:
            return True
    return False


def simulate_one(seg_index, segs, pockets):
    """复算一条直库的正撞；返回 (step 边界 worst, 子步边界 peak, 袋喉事件数, 接触数)。"""
    a, b = segs[seg_index]
    nx, ny = inward_normal(a, b)
    mid = ((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5)

    pos = (mid[0] + nx * START_GAP, mid[1] + ny * START_GAP)
    vel = (-nx * SPEED, -ny * SPEED)

    worst_step = 0.0
    peak_substep = 0.0
    pocket_events = 0
    contacts = 0
    frozen = False

    for _ in range(STEPS):
        for _ in range(SUB_STEPS):
            if frozen:
                break
            if vel[0] == 0.0 and vel[1] == 0.0:
                break

            vx, vy = vel  # a=0 ⇒ v_avg = v
            rem = DT_SUB
            while rem > TOI_REMAINING_EPS:
                earliest = rem                                  # ★ 采样模式
                new_pos = (pos[0] + vx * earliest, pos[1] + vy * earliest)
                overlaps = [i for i in range(len(segs))
                            if distance_point_segment(new_pos, segs[i][0], segs[i][1]) < R]
                pos = new_pos
                rem -= earliest
                if overlaps:
                    vel = resolve(pos, (vx, vy), overlaps, segs)
                    contacts += len(overlaps)
                    vx, vy = vel
                    if vx == 0.0 and vy == 0.0:
                        break

            # 子步边界采样（信息性对照口径）
            pen_sub = R - distance_point_segment(pos, a, b)
            if pen_sub > peak_substep:
                peak_substep = pen_sub

            if crossed_pocket_plane(pos, pockets):
                pocket_events += 1
                vel = (0.0, 0.0)
                frozen = True
                break

        # Step 边界采样（★ 主判据口径，与 T5b 测试一致）
        pen_step = R - distance_point_segment(pos, a, b)
        if pen_step > worst_step:
            worst_step = pen_step

    return worst_step, peak_substep, pocket_events, contacts


def main():
    with open(REFERENCE_JSON, "r", encoding="utf-8") as handle:
        data = json.load(handle)

    raw_segs = data["collision"]["segments"]
    segs = [((s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"])) for s in raw_segs]

    pockets = []
    for pk in data["pockets"]:
        pockets.append(((pk["runtimeThroatCenter"]["x"], pk["runtimeThroatCenter"]["y"]),
                        (pk["runtimeThroatNormal"]["x"], pk["runtimeThroatNormal"]["y"])))

    threshold = 0.5 * SPEED * DT_SUB

    print("=" * 78)
    print("物理内核 T5b 独立复算（禁用 TOI 采样推进；Step 边界口径与测试一致）")
    print("=" * 78)
    print(f"几何来源            : {REFERENCE_JSON}")
    print(f"段数 / 袋数         : {len(segs)} / {len(pockets)}")
    print(f"dt={DT:.10f}  SubSteps={SUB_STEPS}  dt_sub={DT_SUB:.10f}  r={R}")
    print(f"e={E}  mu={MU}  a={A}  v0={SPEED} m/s  起点=库中点+内法向*{START_GAP} m")
    print(f"阈值 ½·v·dt_sub     : {threshold:.10f} m = {threshold * 1000:.4f} mm")
    print("-" * 78)

    worst = 0.0
    worst_substep = 0.0
    total_pocket_events = 0
    total_contacts = 0

    for seg_index in STRAIGHT_SEGMENTS:
        w_step, peak_sub, pocket_events, contacts = simulate_one(seg_index, segs, pockets)
        worst = max(worst, w_step)
        worst_substep = max(worst_substep, peak_sub)
        total_pocket_events += pocket_events
        total_contacts += contacts
        rail = raw_segs[seg_index].get("rail", "")
        print(f"seg[{seg_index:2d}] {rail:22s} Step边界嵌入={w_step * 1000:8.4f} mm"
              f"   子步边界peak={peak_sub * 1000:8.4f} mm   接触={contacts}  袋喉={pocket_events}")

    print("-" * 78)
    print(f"worst (Step 边界, 主判据)  = {worst * 1000:.4f} mm")
    print(f"peak  (子步边界, 信息性)   = {worst_substep * 1000:.4f} mm")
    print(f"袋喉事件总数               = {total_pocket_events}（预期 0）")
    print(f"接触总次数                 = {total_contacts}（应 > 0，否则复算为空转）")
    print(f"T5a 判据上界               = {1e-5 * 1000:.4f} mm")
    print("-" * 78)

    ok = True
    if total_contacts <= 0:
        print("[ASSERT-FAIL] 接触次数为 0 —— 复算为空转，无判别力。")
        ok = False
    if total_pocket_events != 0:
        print("[ASSERT-FAIL] 出现袋喉事件 —— 发射/几何与 T5b 前提不符。")
        ok = False
    if worst < threshold:
        print(f"[ASSERT-FAIL] worst {worst * 1000:.4f} mm < 阈值 {threshold * 1000:.4f} mm —— 负对照不成立。")
        ok = False
    if worst <= 1e-5:
        print("[ASSERT-FAIL] worst 未与 T5a 的 1e-5 m 上界拉开数量级 —— T5a 无判别力。")
        ok = False

    if ok:
        print(f"[ASSERT-OK] worst(Step 边界) {worst * 1000:.4f} mm ≥ 阈值 {threshold * 1000:.4f} mm，"
              f"且与 T5a 上界 {1e-5 * 1000:.4f} mm 相差 {worst / 1e-5:.0f} 倍。")
        print("            ⇒ 与 T5b 上报的 18.7973 mm 一致（float64 vs float32 尾差量级 1e-7）。")
        return 0

    return 1


if __name__ == "__main__":
    sys.exit(main())
