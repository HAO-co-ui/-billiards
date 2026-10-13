# -*- coding: utf-8 -*-
"""
axl_directional_matrix.py -- AXL 三度量的**方向性盲区矩阵**
================================================================================
多点 × 多方向 × 多量级，测 面点/边点/顶点 三度量的响应，给出检出下限矩阵与盲区。
注入点含**段中点**；覆盖 ≥3 个不同朝向的段；4 个方向；5 个量级。
只读；结果落 Docs/Reports/axl-directional-matrix.json。
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
HV = [(-v[0], v[1]) for v in allv]
EDGES = []
TRI2 = []
for verts, polys in tris:
    for (i0, i1, i2) in polys:
        for (u, w) in ((i0, i1), (i1, i2), (i2, i0)):
            EDGES.append(((-verts[3*u], verts[3*u+1]), (-verts[3*w], verts[3*w+1])))
        TRI2.append((-verts[3*i0], verts[3*i0+1], -verts[3*i1], verts[3*i1+1], -verts[3*i2], verts[3*i2+1]))

cfg = C.parse_tableconfig(os.path.join(ROOT, "Assets/_Project/Data/TableConfig.asset"))


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


def m_vertex(px, py):
    b = 1e18
    for (cx, cz) in HV:
        d = (cx-px)**2+(cz-py)**2
        if d < b: b = d
    return math.sqrt(b)


def m_edge(px, py):
    b = 1e18
    for (a, c) in EDGES:
        d = segd(px, py, a[0], a[1], c[0], c[1])
        if d < b: b = d
    return math.sqrt(b)


def m_face(px, py):
    b = 1e18
    for (ax, ay, bx, by, cx, cy) in TRI2:
        d = ptri(px, py, ax, ay, bx, by, cx, cy)
        if d < b: b = d
    return math.sqrt(b)


METRICS = {"面点": m_face, "边点": m_edge, "顶点": m_vertex}
THRESH = {"面点": 1e-4, "边点": 5e-6, "顶点": 5e-4}    # 米（L1/L2/L3 拟定阈值）
MAGS = [0.1, 0.5, 1.0, 2.0, 20.0]                       # mm

# 选 ≥3 个不同朝向的段：端库直段 / 长库直段 / 袋角弧段
SEG_IDS = [6, 19, 79]
points = []
for i in SEG_IDS:
    s = cfg["segments"][i]
    a = (s["a"]["x"], s["a"]["y"]); b = (s["b"]["x"], s["b"]["y"])
    mid = ((a[0]+b[0])/2, (a[1]+b[1])/2)
    for tag, p in (("a", a), ("mid", mid), ("b", b)):
        points.append((f"seg[{i}].{tag}", p, a, b))

DIRS = ["沿边", "垂直边", "朝桌心", "朝桌外"]


def dirvec(kind, p, a, b):
    ex, ey = b[0]-a[0], b[1]-a[1]
    L = math.hypot(ex, ey) or 1.0
    ux, uy = ex/L, ey/L
    px, py = p
    r = math.hypot(px, py) or 1.0
    if kind == "沿边":
        return ux, uy
    if kind == "垂直边":
        return -uy, ux
    if kind == "朝桌心":
        return -px/r, -py/r
    return px/r, py/r


# 计算（记录每点布尔，便于算并集）
detail = {}
flags = {}
for mname, fn in METRICS.items():
    detail[mname] = {}
    flags[mname] = {}
    for d in DIRS:
        detail[mname][d] = {}
        flags[mname][d] = {}
        for mag in MAGS:
            hits = 0
            valmax = 0.0
            fl = []
            for (ptag, p, a, b) in points:
                dx, dy = dirvec(d, p, a, b)
                qx, qy = p[0]+dx*mag/1000.0, p[1]+dy*mag/1000.0
                v = fn(qx, qy)
                valmax = max(valmax, v)
                ok = v > THRESH[mname]
                fl.append(ok)
                if ok:
                    hits += 1
            detail[mname][d][mag] = {"hits": hits, "n": len(points), "worstVal_mm": valmax*1000}
            flags[mname][d][mag] = fl

# 并集：任一层 FAIL 即"拦下"
union = {}
for d in DIRS:
    union[d] = {}
    for mag in MAGS:
        c = 0
        for i in range(len(points)):
            if any(flags[m][d][mag][i] for m in METRICS):
                c += 1
        union[d][mag] = c

# 汇总检出下限（要求全部点都命中）
summary = {}
for mname in METRICS:
    summary[mname] = {}
    for d in DIRS:
        floor = None
        holes = []
        for mag in MAGS:
            h = detail[mname][d][mag]["hits"]
            if h == detail[mname][d][mag]["n"]:
                if floor is None:
                    floor = mag
            else:
                if floor is not None:
                    holes.append(mag)
        summary[mname][d] = {"floorMm": floor, "holesAboveFloorMm": holes,
                             "detectedAt": {str(m): detail[mname][d][m]["hits"] for m in MAGS}}

print("=" * 90)
print("AXL 三度量 · 方向性盲区矩阵（注入点 9 个：3 段 × {a, mid, b}；阈值 面点=1e-4m, 边点=5µm, 顶点=0.5mm）")
print("=" * 90)
print("单元 = 命中点数/9（10 个点里有多少被判 FAIL）")
hdr = "%-6s %-8s" % ("度量", "方向") + "".join(f"{str(m)+'mm':>10}" for m in MAGS) + "  检出下限"
print(hdr)
for mname in METRICS:
    for d in DIRS:
        row = f"{mname:<6} {d:<8}"
        for m in MAGS:
            h = detail[mname][d][m]["hits"]
            row += f"{str(h)+'/9':>10}"
        f = summary[mname][d]["floorMm"]
        row += f"  {'≤'+str(f) if f else '未检出(全盲/部分盲)'}"
        print(row)
    print("-" * 90)
print("%-6s %-8s" % ("并集", "方向") + "".join(f"{str(m)+'mm':>10}" for m in MAGS))
for d in DIRS:
    row = f"{'三层':<6} {d:<8}"
    for m in MAGS:
        row += f"{str(union[d][m])+'/9':>10}"
    print(row)

out = {"schema": "idea35.axl-directional-matrix.v1",
       "points": [pt[0] for pt in points], "dirs": DIRS, "magnitudesMm": MAGS,
       "thresholds": {"面点_m": 1e-4, "边点_m": 5e-6, "顶点_m": 5e-4},
       "detail": detail, "summary": summary,
       "unionHits": {d: {str(m): union[d][m] for m in MAGS} for d in DIRS},
       "note": "unit = 命中点数/9；『检出下限』要求 9 点全命中。'并集'=任一层 FAIL 即拦下。"}
p = os.path.join(ROOT, "Docs/Reports/axl-directional-matrix.json")
open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(out, ensure_ascii=False, indent=1))
print("\n已写入：" + p)
