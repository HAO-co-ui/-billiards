# -*- coding: utf-8 -*-
"""Temp: 独立复现 ① L_local 跨度 与 ③ 面点度量完全盲。"""
import sys, io, math
sys.path.insert(0, r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Tools")
_r = sys.stdout; sys.stdout = io.StringIO()
import fbx_mesh_probe as F
import d2t02b_crosscheck as C
sys.stdout = _r
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

nv, allv, tris, ver = F.load_mesh()
# 2D 顶点 + 三角形（水平 x,z）
HV = [(-v[0], v[1]) for v in allv]
TRI = []
for verts, polys in tris:
    for (i0, i1, i2) in polys:
        TRI.append((i0, i1, i2))
print(f"FBX 顶点={nv}（去重位置）  三角形={len(TRI)}")


def segd2(px, py, ax, ay, bx, by):
    dx, dy = bx-ax, by-ay; L = dx*dx+dy*dy
    if L == 0: return (px-ax)**2+(py-ay)**2
    t = max(0.0, min(1.0, ((px-ax)*dx+(py-ay)*dy)/L))
    return (px-(ax+t*dx))**2+(py-(ay+t*dy))**2


def tri_face_and_local(px, py, i0, i1, i2):
    """返回 (面距^2, 该三角形最长边)"""
    ax, az = HV[i0]; bx, bz = HV[i1]; cx, cz = HV[i2]
    # 退化守卫：XZ 投影面积
    proj2 = (bx-ax)*(cz-az) - (bz-az)*(cx-ax)
    if abs(proj2) >= 1e-12:
        d1 = (ax-px)*(bz-py)-(az-py)*(bx-px)
        d2 = (bx-px)*(cz-py)-(bz-py)*(cx-px)
        d3 = (cx-px)*(az-py)-(cz-py)*(ax-px)
        neg = d1 < 0 or d2 < 0 or d3 < 0
        pos = d1 > 0 or d2 > 0 or d3 > 0
        if not (neg and pos):
            face = 0.0
        else:
            face = min(segd2(px, py, ax, az, bx, bz), segd2(px, py, bx, bz, cx, cz), segd2(px, py, cx, cz, ax, az))
    else:
        face = min(segd2(px, py, ax, az, bx, bz), segd2(px, py, bx, bz, cx, cz), segd2(px, py, cx, cz, ax, az))
    longest = max(math.hypot(ax-bx, az-bz), math.hypot(bx-cx, bz-cz), math.hypot(cx-ax, cz-az))
    return face, longest


cfg = C.parse_tableconfig(r"E:/Unity_Deve/Unity_Project/Billiards game/Assets/_Project/Data/TableConfig.asset")
EP = []
for s in cfg["segments"]:
    EP += [(s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"])]
for p in cfg["pockets"]:
    for g in ("mouthA", "mouthB", "throatA", "throatB"):
        EP.append((p[g]["x"], p[g]["y"]))

# ① 每个端点的「最近三角形最长边」= L_local
locals_ = []
for (px, py) in EP:
    best = 1e18; lc = 0.0
    for (i0, i1, i2) in TRI:
        f, l = tri_face_and_local(px, py, i0, i1, i2)
        if f < best:
            best = f; lc = l
    locals_.append(lc)
ls = sorted(locals_)
print(f"\n① L_local（最近三角形最长边）分布，n={len(ls)}")
print(f"   min={ls[0]*1000:.2f} mm  中位={ls[len(ls)//2]*1000:.2f} mm  max={ls[-1]*1000:.2f} mm")
print(f"   >100mm 的端点数 = {sum(1 for x in ls if x>0.1)}")
print("   代表值(mm): " + ", ".join(f"{x*1000:.1f}" for x in ls[::max(1,len(ls)//8)]))

# ③ 面点度量对位移是否完全盲（0/0.2/1/2/20 mm）
print("\n③ 面点度量 · 位移响应（逐端点取最大面距）")
dirs = {"+X": lambda x, y: (1.0, 0.0), "朝桌心": lambda x, y: (-x/math.hypot(x, y), -y/math.hypot(x, y))}
for dn, dfn in dirs.items():
    row = []
    for mm in [0, 0.2, 1, 2, 20]:
        d = mm/1000.0; worst = 0.0
        for (px, py) in EP:
            ux, uy = dfn(px, py)
            qx, qy = px+ux*d, py+uy*d
            best = 1e18
            for (i0, i1, i2) in TRI:
                f, _ = tri_face_and_local(qx, qy, i0, i1, i2)
                if f < best: best = f
            if best > worst: worst = best
        row.append(f"{mm}mm→{math.sqrt(worst)*1000:.6f}mm")
    print(f"   {dn}: " + "  ".join(row))
