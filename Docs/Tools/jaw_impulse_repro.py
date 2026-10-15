# -*- coding: utf-8 -*-
"""**袋角「撞了不弹、吸在两边」的离线复现工具**（不依赖 Unity，只用权威几何 + 内核同一套公式）。

用途
────
把 `PoolSimulator` 的「TOI 同刻接触组 → 组内逐条解算」这条链按 C# 逐条转写，用于：
 ① 统计「接触解算后仍有库段正在被撞进去」（`min v_n < 0`）的比例 —— 修复前 vs 修复后；
 ② 端到端跑两个代表姿态（TOI 路径 / 深层重叠路径），看球是否**停在袋角**。

为什么必须离线复现
──────────────────
本工程袋口区域是**一串共享端点的短段**（4–25 mm，球半径 28.6 mm），球撞在两段的**公共端点**上时，
`SolveToi` 会对两段各调用同一个端点圆的 `RayCircleToi` ⇒ 同一个接触事件被收成**两条同刻条目**。
第一条把球正常弹出，**第二条拿到的法向速度已经是正值**（球已在分离），而修复前
`ResolveSegmentContact` **没有接近判据**（球-球路径有）⇒ 把刚弹出的球又翻回库内。
本工具就是把这条链量出来。

几何真源
────────
`Docs/Reference/table-metadata-reference.json` 的 `cushion_segments`（82 段）。
**本脚本不做任何坐标变换**（该 JSON 的 x/y 就是规范二维物理平面）。

用法
────
    python Docs/Tools/jaw_impulse_repro.py

输出
────
  · 统计段：全台面 / 中袋袋角 / 角袋袋角 三处的「违反比例」，修复前后对照；
  · 姿态段：两个代表姿态的逐帧轨迹（与判据 `T109` 用的是同一对姿态）。

局限（如实）
────────────
  · 本脚本是**独立转写**，不是内核本身 ⇒ 它证明的是"该机制在该几何下成立"，不是"内核行为逐位如此"；
    判据 `T109` 才是对内核本体的守卫（它由 Unity 跑）。
  · 只做单球、无滚动摩擦、无球-球；袋口判定按内核口径转写（`s > 0 ∧ ρ ≤ openingRadius` ⇒ 入袋，否则越界冻结）。
  · Python 用 float64、内核用 float32 ⇒ 逐位数字会有末位差；姿态挑选时已要求几何**非退化**
    （侵入深度 ≥ 0.6 mm、代表姿态实测 ≥ 16 mm），故结论不依赖末位。
"""

import json
import math
import os
import random

REF = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                   "..", "Reference", "table-metadata-reference.json")

BALL_RADIUS = 0.028575          # PoolConstants.BallRadius
TOI_EPS = 1e-9                  # PoolSimulator.ToiTimeEpsilon
DT = 1.0 / 120.0                # PoolSimulator.FixedDeltaTime
SUB_STEPS = 3                   # PoolSimulator.SubSteps（= DeriveSubSteps(MaxBallSpeed)）
CUSHION_RESTITUTION = 0.8       # PhysicsTuning.cushionRestitution
CUSHION_TANGENTIAL = 0.9        # PhysicsTuning.cushionTangentialFriction
STOPPED_SPEED_EPS = 1e-6        # PoolSimulator.StoppedSpeedEpsilon


def load_geometry():
    with open(os.path.normpath(REF), encoding="utf-8") as handle:
        data = json.load(handle)
    segments = [((s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"]))
                for s in data["cushion_segments"]]
    pockets = [(p["runtimeThroatCenter"]["x"], p["runtimeThroatCenter"]["y"],
                p["runtimeThroatNormal"]["x"], p["runtimeThroatNormal"]["y"],
                p["openingRadius"]) for p in data["pockets"]]
    return segments, pockets


SEGMENTS, POCKETS = load_geometry()


# ── 内核公式的逐条转写 ────────────────────────────────────────────────────

def ray_circle_toi(p, v, center, radius):
    """`PoolSimulator.RayCircleToi`：动点 vs 圆的"进入"时刻（含接近判据），无进入返回 +inf。"""
    a = v[0] * v[0] + v[1] * v[1]
    if a <= 1e-20:
        return math.inf
    mx, my = p[0] - center[0], p[1] - center[1]
    b = 2 * (mx * v[0] + my * v[1])
    c = mx * mx + my * my - radius * radius
    disc = b * b - 4 * a * c
    if disc < 0:
        return math.inf
    t = (-b - math.sqrt(disc)) / (2 * a)
    if t < 0:
        t = 0.0
    cnx, cny = p[0] + t * v[0] - center[0], p[1] + t * v[1] - center[1]
    length = math.hypot(cnx, cny)
    if length > 1e-12 and (v[0] * cnx + v[1] * cny) / length >= 0:
        return math.inf
    return t


def solve_toi(p, v, a, b, radius):
    """`PoolSimulator.SolveToi`：动点 vs 半径 radius 的线段胶囊（直线面 + 两端点圆）。"""
    best = math.inf
    abx, aby = b[0] - a[0], b[1] - a[1]
    len2 = abx * abx + aby * aby
    if len2 > 1e-12:
        length = math.sqrt(len2)
        nx, ny = -aby / length, abx / length
        s0 = (p[0] - a[0]) * nx + (p[1] - a[1]) * ny
        sv = v[0] * nx + v[1] * ny
        if abs(s0) >= radius and sv * s0 < 0:
            target = radius if s0 > 0 else -radius
            t = (target - s0) / sv
            if 0 <= t < best:
                cx, cy = p[0] + t * v[0], p[1] + t * v[1]
                u = ((cx - a[0]) * abx + (cy - a[1]) * aby) / len2
                if 0 <= u <= 1:
                    best = t
    return min(best, ray_circle_toi(p, v, a, radius), ray_circle_toi(p, v, b, radius))


def closest_on_segment(p, a, b):
    abx, aby = b[0] - a[0], b[1] - a[1]
    len2 = abx * abx + aby * aby
    if len2 <= 1e-18:
        return a
    u = max(0.0, min(1.0, ((p[0] - a[0]) * abx + (p[1] - a[1]) * aby) / len2))
    return (a[0] + u * abx, a[1] + u * aby)


def earliest_group(p, v, remaining=math.inf):
    """`PoolSimulator.EarliestSegmentToi`：最早接触时刻 + 全部**同刻**条目（这就是"组"）。"""
    earliest, group = math.inf, []
    for index, (a, b) in enumerate(SEGMENTS):
        t = solve_toi(p, v, a, b, BALL_RADIUS)
        if t < 0 or t > remaining:
            continue
        if t < earliest - TOI_EPS:
            earliest, group = t, [(index, t)]
        elif abs(t - earliest) <= TOI_EPS:
            group.append((index, t))
    return earliest, group


def normal_at(p, index):
    a, b = SEGMENTS[index]
    q = closest_on_segment(p, a, b)
    dx, dy = p[0] - q[0], p[1] - q[1]
    length = math.hypot(dx, dy)
    if length < 1e-18:
        return (0.0, 1.0), q
    return (dx / length, dy / length), q


def apply_impulse(v, n, guard):
    """`ResolveSegmentContact` 的冲量部分；`guard=True` 时复现**修复后**的接近判据。"""
    vn = v[0] * n[0] + v[1] * n[1]
    if guard and vn >= 0:
        return v, False
    vt = (v[0] - vn * n[0], v[1] - vn * n[1])
    v = [v[0] - (1 + CUSHION_RESTITUTION) * vn * n[0],
         v[1] - (1 + CUSHION_RESTITUTION) * vn * n[1]]
    if vt[0] * vt[0] + vt[1] * vt[1] > 0:
        v = [v[0] - (1 - CUSHION_TANGENTIAL) * vt[0],
             v[1] - (1 - CUSHION_TANGENTIAL) * vt[1]]
    return v, True


def resolve_group(p, v, group, guard):
    """`ResolveContactGroup` 的段部分（**组内逐条、顺序固定**）。"""
    applied = []
    for index, _t in group:
        n, _q = normal_at(p, index)
        v, ok = apply_impulse(v, n, guard)
        if ok:
            applied.append(index)
    return v, applied


def touching_segments(p, tolerance=1e-3):
    out = []
    for index, (a, b) in enumerate(SEGMENTS):
        q = closest_on_segment(p, a, b)
        if math.hypot(p[0] - q[0], p[1] - q[1]) <= BALL_RADIUS + tolerance:
            out.append(index)
    return out


def min_normal_speed(p, v):
    """拟立不变量：**对所有仍相触的库段**，球的法向速度必须 >= 0（<0 = 仍有库段在被撞进去）。"""
    worst = math.inf
    for index in touching_segments(p):
        n, _q = normal_at(p, index)
        worst = min(worst, v[0] * n[0] + v[1] * n[1])
    return worst


# ── 单事件（两组路径）────────────────────────────────────────────────────

def first_event(p0, v0, guard):
    """返回 (路径, 事件后位置, 事件后速度, 施加冲量的段索引)。"""
    p, v = list(p0), list(v0)
    applied, deep = [], False
    for index in range(len(SEGMENTS)):                 # A) 深层重叠：先位置修正、再解算（按段索引升序）
        n, q = normal_at(p, index)
        length = math.hypot(p[0] - q[0], p[1] - q[1])
        if BALL_RADIUS * 0 < length < BALL_RADIUS:
            deep = True
            p = [q[0] + BALL_RADIUS * (p[0] - q[0]) / length,
                 q[1] + BALL_RADIUS * (p[1] - q[1]) / length]
            n, _q = normal_at(p, index)
            v, ok = apply_impulse(v, n, guard)
            if ok:
                applied.append(index)
    if deep:
        return "DEEP", p, v, applied
    t, group = earliest_group(p0, v0)                   # B) TOI：推进到接触时刻、再解算
    if not group or t > 1e9:
        return "NONE", p, v, []
    p = [p0[0] + t * v0[0], p0[1] + t * v0[1]]
    v, applied = resolve_group(p, v0, group, guard)
    return "TOI", p, v, applied


# ── 全步进（端到端）──────────────────────────────────────────────────────

def pocket_check(p):
    for index, (tx, ty, nx, ny, opening) in enumerate(POCKETS):
        dx, dy = p[0] - tx, p[1] - ty
        s = dx * nx + dy * ny
        if s <= 0:
            continue
        rho = abs(dx * -ny + dy * nx)
        return index, ("CAPTURED" if rho <= opening else "FROZEN_OFFTABLE")
    return None


def simulate(p0, v0, guard, steps=40):
    p, v = list(p0), list(v0)
    frozen = None
    contacts = deep = 0
    trail = []
    for _ in range(steps):
        for _sub in range(SUB_STEPS):
            for index in range(len(SEGMENTS)):          # 子步起点：深层重叠处置
                q = closest_on_segment(p, SEGMENTS[index][0], SEGMENTS[index][1])
                dx, dy = p[0] - q[0], p[1] - q[1]
                d2 = dx * dx + dy * dy
                if 1e-18 < d2 < BALL_RADIUS * BALL_RADIUS:
                    length = math.sqrt(d2)
                    p = [q[0] + BALL_RADIUS * dx / length, q[1] + BALL_RADIUS * dy / length]
                    deep += 1
                    v, _ok = apply_impulse(v, normal_at(p, index)[0], guard)
            remaining, iterations, previous_zero = DT / SUB_STEPS, 0, False
            while remaining > 1e-9:
                iterations += 1
                if iterations > 128:                    # MaxToiIterations
                    break
                earliest, group = earliest_group(p, v, remaining)
                if not group:
                    p = [p[0] + v[0] * remaining, p[1] + v[1] * remaining]
                    break
                p = [p[0] + v[0] * earliest, p[1] + v[1] * earliest]
                remaining -= earliest
                contacts += len(group)
                v, _applied = resolve_group(p, v, group, guard)
                if math.hypot(v[0], v[1]) <= STOPPED_SPEED_EPS:
                    v = [0.0, 0.0]
                    break
                if earliest <= 1e-9 and previous_zero:
                    break
                previous_zero = earliest <= 1e-9
            hit = pocket_check(p)
            if hit:
                frozen = hit
                v = [0.0, 0.0]
                break
        trail.append((tuple(p), tuple(v), frozen))
        if frozen:
            break
    return p, v, trail, frozen, contacts, deep


# ── 判据 T109 用的两个代表姿态（离线挑出，几何非退化）──────────────────

POSE_TOI = ((-0.009509, -0.304816), (0.992530, -2.812731))
POSE_DEEP = ((-0.096211, -0.629963), (1.021980, -1.103941))


def main():
    print("=" * 100)
    print("A) 统计：『接触解算后仍有相触库段正在被撞进去』（min v_n < 0）")
    print("=" * 100)
    regions = {
        "全台面随机": ((-1.20, 1.20), (-0.60, 0.60)),
        "中袋袋角区域": ((-0.24, 0.24), (-0.63, -0.42)),
        "角袋 #0 袋角": ((-1.26, -1.00), (-0.63, -0.38)),
    }
    random.seed(23)
    for name, region in regions.items():
        total = bad_before = bad_after = deep = 0
        for _ in range(6000):
            p = (random.uniform(*region[0]), random.uniform(*region[1]))
            angle = random.uniform(0, 2 * math.pi)
            speed = random.uniform(0.5, 6.0)
            v = (speed * math.cos(angle), speed * math.sin(angle))
            path, p1, v1, _a1 = first_event(p, v, False)
            if path == "NONE":
                continue
            total += 1
            deep += 1 if path == "DEEP" else 0
            _path2, _p2, v2, _a2 = first_event(p, v, True)
            if min_normal_speed(p1, v1) < -1e-9:
                bad_before += 1
            if min_normal_speed(p1, v2) < -1e-9:
                bad_after += 1
        print(f"  {name}: 事件 {total}（深层重叠 {deep}）")
        print(f"     修复前 {bad_before}/{total} = {100 * bad_before / max(1, total):.1f}%"
              f"   |   修复后 {bad_after}/{total} = {100 * bad_after / max(1, total):.1f}%")

    print()
    print("=" * 100)
    print("B) 端到端：判据 `T109` 用的两个代表姿态（修复前 → 修复后）")
    print("=" * 100)
    for tag, (p0, v0), steps in (("(a) TOI 路径：斜入 −y 中袋袋角（段 19/20 公共端点）", POSE_TOI, 40),
                                 ("(b) 深层重叠路径：球卡在袋角凹角（初始重叠 ~16 mm）", POSE_DEEP, 10)):
        print(f"  {tag}")
        for guard in (False, True):
            name = "修复后（接近判据）" if guard else "修复前（无判据）"
            p, v, trail, frozen, contacts, deep = simulate(p0, v0, guard, steps)
            state = f"袋口 {frozen[1]}" if frozen else "仍在场上"
            print(f"    {name}: p=({p[0]:+.5f},{p[1]:+.5f}) |v|={math.hypot(v[0], v[1]):.5f}"
                  f"  接触条目={contacts} 深层重叠修正={deep} {state}")
            if not guard:
                print(f"        中间帧 |v|: "
                      + ", ".join(f"{math.hypot(t[1][0], t[1][1]):.4f}" for t in trail[:5]))
        print()


if __name__ == "__main__":
    main()
