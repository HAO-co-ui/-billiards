# -*- coding: utf-8 -*-
"""Temp: 独立复现 AXL "到面度量对沿面位移盲" 的注入表。"""
import sys, io, math, os, json
sys.path.insert(0, r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Tools")
# 导入期间把 stdout 换成无 .buffer 的对象，吞掉各模块 import 时的包裹；之后自行包一次
_real = sys.stdout
sys.stdout = io.StringIO()
import fbx_mesh_probe as F
import d2t02b_crosscheck as C
sys.stdout = _real
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

nv, allv, tris, ver = F.load_mesh()
print(f"FBX version={ver} 顶点={nv} 三角网格块={len(tris)}")

# 唯一轴修正 ModelVertexToUnity = (-m.x, m.z, m.y) → 水平 (x,z) = (-v0, v1)
HV = [(-v[0], v[1]) for v in allv]


def seg_d2(px, py, ax, ay, bx, by):
    dx, dy = bx-ax, by-ay
    L = dx*dx + dy*dy
    if L == 0:
        return (px-ax)**2 + (py-ay)**2
    t = max(0.0, min(1.0, ((px-ax)*dx + (py-ay)*dy)/L))
    return (px-(ax+t*dx))**2 + (py-(ay+t*dy))**2


def ptri2(px, py, ax, ay, bx, by, cx, cy):
    def sg(x1, y1, x2, y2, x3, y3):
        return (x1-x3)*(y2-y3) - (x2-x3)*(y1-y3)
    d1 = sg(px, py, ax, ay, bx, by); d2 = sg(px, py, bx, by, cx, cy); d3 = sg(px, py, cx, cy, ax, ay)
    neg = (d1 < 0) or (d2 < 0) or (d3 < 0); pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
    if not (neg and pos):
        return 0.0
    return min(seg_d2(px, py, ax, ay, bx, by), seg_d2(px, py, bx, by, cx, cy), seg_d2(px, py, cx, cy, ax, ay))


def nearest_vertex(px, py):
    b = 1e18
    for (cx, cz) in HV:
        d = (cx-px)**2 + (cz-py)**2
        if d < b: b = d
    return math.sqrt(b)


def nearest_surface(px, py):
    b = 1e18
    for verts, polys in tris:
        for (i0, i1, i2) in polys:
            ax = -verts[3*i0]; az = verts[3*i0+1]
            bx = -verts[3*i1]; bz = verts[3*i1+1]
            cx = -verts[3*i2]; cz = verts[3*i2+1]
            d = ptri2(px, py, ax, az, bx, bz, cx, cz)
            if d < b: b = d
    return math.sqrt(b)


# 载入资产端点（用 cfg 解析）
import d2t02b_crosscheck as C
cfg = C.parse_tableconfig(r"E:/Unity_Deve/Unity_Project/Billiards game/Assets/_Project/Data/TableConfig.asset")
p0 = cfg["segments"][0]["b"]            # seg[0].b
px, py = p0["x"], p0["y"]
print(f"\n注入点 = seg[0].b(physical) = ({px:.6f}, {py:.6f})")

# 沿面位移方向：取"朝桌心"方向（-p 归一化），以及纯 +x
r = math.hypot(px, py)
dirs = {"朝桌心": (-px/r, -py/r), "+x": (1.0, 0.0), "+y(短轴)": (0.0, 1.0)}

print("\n| 方向 | 注入位移 | 顶点度量 | 面点度量 | 面点判定(tol 1e-4) |")
print("| --- | --- | --- | --- | --- |")
for dname, (dx, dy) in dirs.items():
    for mm in [0, 1, 2, 20, 200]:
        d = mm/1000.0
        qx, qy = px + dx*d, py + dy*d
        nv_ = nearest_vertex(qx, qy)
        ns_ = nearest_surface(qx, qy)
        verdict = "PASS" if ns_ <= 1e-4 else "FAIL"
        print(f"| {dname} | {mm} mm | {nv_*1000:.4f} mm | {ns_:.3e} m | {verdict} |")

# 机制核验：台面正中 (0,0) 的到面距离
print(f"\n台面正中 (0,0) -> 面点距离 = {nearest_surface(0,0):.3e} m  (顶点距离={nearest_vertex(0,0)*1000:.4f} mm)")

# 到 y=0 平面的距离对沿面点恒为 0（解析事实）
print("（解析）任何落在 y=0 台呢平面内的点，其到该平面距离恒为 0.0 m —— 与阈值无关。")
