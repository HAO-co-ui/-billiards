# -*- coding: utf-8 -*-
"""
axl_blindband_scans.py -- AXL 三类位移扫描的**盲带标定**（单点 / 整体 / 局部）
================================================================================
目的（team-lead 裁断）：到最近特征类度量天生非单调；**不要求无洞，要求把洞标定出来**。
输出：每组扫描的"盲带区间 + 其间最坏度量值"。
加速：均匀网格（顶点直接桶；边按 AABB 桶）。
只读。
"""
import sys, io, math, json, os
sys.path.insert(0, r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Tools")
_real = sys.stdout; sys.stdout = io.StringIO()
import fbx_mesh_probe as F
import d2t02b_crosscheck as C
sys.stdout = _real
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
nv, allv, tris, ver = F.load_mesh()
HV = [(-v[0], v[1]) for v in allv]          # 顶点(水平面)
# 边（去重，索引对）
EIDX = set()
for verts, polys in tris:
    for (i0, i1, i2) in polys:
        for (u, w) in ((i0, i1), (i1, i2), (i2, i0)):
            EIDX.add((min(u, w), max(u, w)))
EIDX = list(EIDX)

CELL = 0.008  # 8 mm
VB = {}
for i, (x, y) in enumerate(HV):
    VB.setdefault((int(math.floor(x/CELL)), int(math.floor(y/CELL))), []).append(i)
EB = {}
for (u, w) in EIDX:
    x1, y1 = HV[u]; x2, y2 = HV[w]
    cx0, cx1 = int(math.floor(min(x1, x2)/CELL)), int(math.floor(max(x1, x2)/CELL))
    cy0, cy1 = int(math.floor(min(y1, y2)/CELL)), int(math.floor(max(y1, y2)/CELL))
    for cx in range(cx0, cx1+1):
        for cy in range(cy0, cy1+1):
            EB.setdefault((cx, cy), []).append((u, w))


def close_pt(px, py, qx, qy):
    return (px-qx)**2 + (py-qy)**2


def segd2(px, py, ax, ay, bx, by):
    dx, dy = bx-ax, by-ay
    L = dx*dx+dy*dy
    if L == 0:
        return (px-ax)**2+(py-ay)**2
    t = max(0.0, min(1.0, ((px-ax)*dx+(py-ay)*dy)/L))
    return (px-(ax+t*dx))**2+(py-(ay+t*dy))**2


def nearest_vertex(px, py):
    cx, cy = int(math.floor(px/CELL)), int(math.floor(py/CELL))
    best = 1e18
    for r in range(0, 12):
        found = False
        for i in range(cx-r, cx+r+1):
            for j in range(cy-r, cy+r+1):
                if max(abs(i-cx), abs(j-cy)) != r:
                    continue
                for vi in VB.get((i, j), ()):  # noqa
                    d = close_pt(px, py, HV[vi][0], HV[vi][1])
                    if d < best:
                        best = d
                found = True
        if best <= (r*CELL)**2 and found:
            break
    return math.sqrt(best)


def nearest_edge(px, py):
    cx, cy = int(math.floor(px/CELL)), int(math.floor(py/CELL))
    best = 1e18
    seen = set()
    for r in range(0, 8):
        if best <= ((r-1)*CELL)**2 and r > 0:
            break
        for i in range(cx-r, cx+r+1):
            for j in range(cy-r, cy+r+1):
                if max(abs(i-cx), abs(j-cy)) != r:
                    continue
                for (u, w) in EB.get((i, j), ()):  # noqa
                    if (u, w) in seen:
                        continue
                    seen.add((u, w))
                    d = segd2(px, py, HV[u][0], HV[u][1], HV[w][0], HV[w][1])
                    if d < best:
                        best = d
    return math.sqrt(best)


cfg = C.parse_tableconfig(os.path.join(ROOT, "Assets/_Project/Data/TableConfig.asset"))
EP = []      # (label, x, y, rail)
for i, s in enumerate(cfg["segments"]):
    for e in ("a", "b"):
        EP.append((f"seg[{i}].{e}", s[e]["x"], s[e]["y"], s["rail"]))

# 阈值（按 team-lead 裁断）
T_VERT = 0.522e-3     # k=2×0.0479=0.0958 × L_local 5.455mm ≈ 0.522mm
T_EDGE = 5e-6         # 5 µm
DIRV = (1.0, 0.0)     # 刚性平移方向 +X
STEP = 0.00005        # 0.05 mm
DMAX = 0.020


def scan(moved_indices, dirfn):
    """返回 (顶点盲带, 边盲带)"""
    vb_band = []
    eb_band = []
    for k in range(int(DMAX/STEP)+1):
        d = k*STEP
        wv = 0.0
        we = 0.0
        for idx in moved_indices:
            _, x, y, _ = EP[idx]
            ux, uy = dirfn(x, y)
            qx, qy = x+ux*d, y+uy*d
            v = nearest_vertex(qx, qy)
            e = nearest_edge(qx, qy)
            if v > wv:
                wv = v
            if e > we:
                we = e
        if wv <= T_VERT:
            vb_band.append((d, wv))
        if we <= T_EDGE:
            eb_band.append((d, we))
    return _merge(vb_band), _merge(eb_band)


def _merge(pts):
    """把 (δ, 值) 序列合并成连续盲带区间 + 区间内最坏值。"""
    if not pts:
        return []
    out = []
    s, worst = pts[0][0], pts[0][1]
    prev = pts[0][0]
    for (d, v) in pts[1:]:
        if d-prev > STEP*1.5:
            out.append((s*1000, prev*1000, worst*1000))
            s, worst = d, v
        else:
            worst = max(worst, v)
        prev = d
    out.append((s*1000, prev*1000, worst*1000))
    return out


allidx = list(range(len(EP)))

# 单点：最坏底噪端点
worst_i = max(range(len(EP)), key=lambda i: nearest_vertex(EP[i][1], EP[i][2]))
single = [worst_i]
# 局部：取一个 rail 组的端点
rail = EP[worst_i][3]
local = [i for i in allidx if EP[i][3] == rail]

res = {}
DIRS = {
    "+X": lambda x, y: (1.0, 0.0),
    "+Y": lambda x, y: (0.0, 1.0),
    "朝桌心": lambda x, y: (-x/math.hypot(x, y), -y/math.hypot(x, y)),
}
for name, idxs in (("单点", single), ("整体", allidx), ("局部(单组rail)", local)):
    res[name] = {"movedPoints": len(idxs)}
    print(f"\n### {name}（移动 {len(idxs)} 点，阈值 顶点{T_VERT*1000:.3f}mm / 边{T_EDGE*1e6:.0f}µm）")
    for dname, dfn in DIRS.items():
        vb, eb = scan(idxs, dfn)
        res[name][dname] = {"vertexBlindBandsMm": vb, "edgeBlindBandsMm": eb}
        print(f"  方向 {dname}")
        print("    顶点盲带: " + ("；".join(f"[{a:.2f},{b:.2f}]mm 最坏{w:.4f}mm" for a, b, w in vb) if vb else "无"))
        print("    边点盲带: " + ("；".join(f"[{a:.2f},{b:.2f}]mm 最坏{w:.4f}mm" for a, b, w in eb) if eb else "无"))

out = {"schema": "idea35.axl-blindband-scans.v1", "direction": "+X", "stepMm": STEP*1000,
       "thresholds": {"vertexMm": T_VERT*1000, "edgeUm": T_EDGE*1e6}, "result": res}
p = os.path.join(ROOT, "Docs/Reports/axl-blindband-scans.json")
open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(out, ensure_ascii=False, indent=1))
print("\n已写入：" + p)
