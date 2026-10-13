# -*- coding: utf-8 -*-
"""Temp: 端点→最近顶点 距离分布 + 与段长/曲率的相关性（判 (a)采样 vs (b)轮廓近似）。"""
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
cfg = C.parse_tableconfig(r"E:/Unity_Deve/Unity_Project/Billiards game/Assets/_Project/Data/TableConfig.asset")


def nv_dist(px, py):
    b = 1e18
    for (cx, cz) in HV:
        d = (cx-px)**2 + (cz-py)**2
        if d < b: b = d
    return math.sqrt(b)


rows = []
for i, s in enumerate(cfg["segments"]):
    L = math.hypot(s["b"]["x"]-s["a"]["x"], s["b"]["y"]-s["a"]["y"])
    for e in ("a", "b"):
        p = s[e]
        rows.append((nv_dist(p["x"], p["y"]), i, e, L, s["rail"]))

ds = sorted(r[0] for r in rows)
print(f"端点→最近顶点 距离分布（188 点，对源 FBX 10572 顶点）:")
print(f"  min={ds[0]*1000:.5f} mm  median={ds[len(ds)//2]*1000:.5f} mm  max={ds[-1]*1000:.5f} mm")
for th in [0.001, 0.01, 0.05, 0.1, 0.2, 0.26]:
    print(f"  > {th:>5} mm 的端点数 = {sum(1 for d in ds if d > th/1000):>3}")

print("\nTop8 最坏端点（含所属段长/rail）:")
for d, i, e, L, rail in sorted(rows, reverse=True)[:8]:
    print(f"  {d*1000:8.4f} mm  seg[{i}].{e}  段长={L*1000:7.3f} mm  rail={rail}")

# 相关性：短段(弧) vs 长段(直)
short = [d for d, i, e, L, r in rows if L < 0.02]     # <20mm 视为弧/短段
long_ = [d for d, i, e, L, r in rows if L >= 0.5]     # >=0.5m 视为长直段
def stat(x):
    return (len(x), (sum(x)/len(x)*1000 if x else 0), (max(x)*1000 if x else 0))
print(f"\n短段(<20mm, 多为袋角弧) 端点数={stat(short)[0]} 均值={stat(short)[1]:.4f} mm 最大={stat(short)[2]:.4f} mm")
print(f"长直段(>=0.5m)      端点数={stat(long_)[0]} 均值={stat(long_)[1]:.4f} mm 最大={stat(long_)[2]:.4f} mm")


# ── 决定性补充：最坏端点是否落在网格**边**上（→在网格上=采样；(b)→离网格=近似残差）──
EDGES = []
for verts, polys in tris:
    for (i0, i1, i2) in polys:
        for (u, v_) in ((i0, i1), (i1, i2), (i2, i0)):
            EDGES.append(((-verts[3*u], verts[3*u+1]), (-verts[3*v_], verts[3*v_+1])))
print(f"\n网格 2D 边数={len(EDGES)}")


def seg_d2b(px, py, ax, ay, bx, by):
    dx, dy = bx-ax, by-ay
    L = dx*dx + dy*dy
    if L == 0:
        return (px-ax)**2 + (py-ay)**2
    t = max(0.0, min(1.0, ((px-ax)*dx + (py-ay)*dy)/L))
    return (px-(ax+t*dx))**2 + (py-(ay+t*dy))**2


def edge_dist(px, py):
    b = 1e18
    for (a, c) in EDGES:
        d = seg_d2b(px, py, a[0], a[1], c[0], c[1])
        if d < b: b = d
    return math.sqrt(b)


print("Top8 最坏端点：到最近顶点 / 到最近边 / 到最近面")
for d, i, e, L, rail in sorted(rows, reverse=True)[:8]:
    p = cfg["segments"][i][e]
    print(f"  seg[{i}].{e}: 顶点={d*1000:.4f}mm  边={edge_dist(p['x'],p['y'])*1000:.4f}mm  rail={rail}")
