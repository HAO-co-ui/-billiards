# -*- coding: utf-8 -*-
"""Temp: 各"度量 × 阈值"组合的**检出下限**（对最坏底噪端点扫描位移）。"""
import sys, io, math
sys.path.insert(0, r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Tools")
_real = sys.stdout; sys.stdout = io.StringIO()
import fbx_mesh_probe as F
import d2t02b_crosscheck as C
sys.stdout = _real
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

nv, allv, tris, ver = F.load_mesh()
HV = [(-v[0], v[1]) for v in allv]
EDGES = []
for verts, polys in tris:
    for (i0, i1, i2) in polys:
        for (u, w) in ((i0, i1), (i1, i2), (i2, i0)):
            EDGES.append(((-verts[3*u], verts[3*u+1]), (-verts[3*w], verts[3*w+1])))
cfg = C.parse_tableconfig(r"E:/Unity_Deve/Unity_Project/Billiards game/Assets/_Project/Data/TableConfig.asset")


def segd(px, py, ax, ay, bx, by):
    dx, dy = bx-ax, by-ay
    L = dx*dx+dy*dy
    if L == 0:
        return (px-ax)**2+(py-ay)**2
    t = max(0.0, min(1.0, ((px-ax)*dx+(py-ay)*dy)/L))
    return (px-(ax+t*dx))**2+(py-(ay+t*dy))**2


def ptri(px, py, ax, ay, bx, by, cx, cy):
    def sg(x1, y1, x2, y2, x3, y3):
        return (x1-x3)*(y2-y3)-(x2-x3)*(y1-y3)
    d1 = sg(px, py, ax, ay, bx, by); d2 = sg(px, py, bx, by, cx, cy); d3 = sg(px, py, cx, cy, ax, ay)
    neg = (d1 < 0) or (d2 < 0) or (d3 < 0); pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    if not (neg and pos):
        return 0.0
    return min(segd(px, py, ax, ay, bx, by), segd(px, py, bx, by, cx, cy), segd(px, py, cx, cy, ax, ay))


def mv(px, py):
    b = 1e18
    for (cx, cz) in HV:
        d = (cx-px)**2+(cz-py)**2
        if d < b: b = d
    return math.sqrt(b)


def me(px, py):
    b = 1e18
    for a, c in EDGES:
        d = segd(px, py, a[0], a[1], c[0], c[1])
        if d < b: b = d
    return math.sqrt(b)


def ms(px, py):
    b = 1e18
    for verts, polys in tris:
        for (i0, i1, i2) in polys:
            d = ptri(px, py, -verts[3*i0], verts[3*i0+1], -verts[3*i1], verts[3*i1+1], -verts[3*i2], verts[3*i2+1])
            if d < b: b = d
    return math.sqrt(b)


# 最坏底噪端点（0.2613mm）。找出它。
best = None
for i, s in enumerate(cfg["segments"]):
    for e in ("a", "b"):
        d = mv(s[e]["x"], s[e]["y"])
        if best is None or d > best[0]:
            best = (d, i, e, s[e]["x"], s[e]["y"])
d0, i0, e0, px, py = best
L = math.hypot(cfg["segments"][i0]["b"]["x"]-cfg["segments"][i0]["a"]["x"],
               cfg["segments"][i0]["b"]["y"]-cfg["segments"][i0]["a"]["y"])
print(f"最坏底噪端点 seg[{i0}].{e0}  floor_vertex={d0*1000:.4f} mm  段长={L*1000:.3f} mm")

dirs = {"朝桌心": (-px/math.hypot(px, py), -py/math.hypot(px, py)),
        "沿+段切向": ((cfg['segments'][i0]['b']['x']-cfg['segments'][i0]['a']['x'])/L,
                   (cfg['segments'][i0]['b']['y']-cfg['segments'][i0]['a']['y'])/L)}
for dn, (dx, dy) in dirs.items():
    print(f"\n方向={dn}   （位移 mm -> 顶点 / 边 / 面，单位 mm）")
    for mm in [0, 0.05, 0.1, 0.2, 0.3, 0.5, 1.0, 2.0, 5.0, 20.0]:
        d = mm/1000.0
        qx, qy = px+dx*d, py+dy*d
        print(f"  δ={mm:>5} mm -> 顶点 {mv(qx,qy)*1000:9.4f} | 边 {me(qx,qy)*1000:9.5f} | 面 {ms(qx,qy)*1000:9.5f}")

# 检出下限：value(δ) = floor + slope*δ；由上面扫描线性拟小段
print("\n[检出下限估算] floor + slope·δ > T  ⇒  δ_min = (T - floor)/slope")
print("  顶点: floor≈0.2613mm, slope≈(1.0001-0.2613)/1.0 ≈ 0.739 mm/mm (朝桌心)")
for T in [0.5, 0.5455, 2.727]:
    print(f"    顶点 T={T} mm -> δ_min ≈ {(T-0.2613)/0.739:.4f} mm")
print("  面点: value 对沿面位移恒 0 -> 任何 T 都 δ_min=∞（盲）")
