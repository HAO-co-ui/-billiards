#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D4 spec 复核：独立重算（不读探针结论，直接从原始 JSON 重算）。"""
import json, math, hashlib, sys, io, os
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
SRC = os.path.join(ROOT, "Docs/Reference/table-metadata-reference.json")
b = open(SRC, "rb").read()
print("FILE sha256 =", hashlib.sha256(b).hexdigest())
tm = json.loads(b.decode("utf-8"))
print("JSON 顶层键 =", sorted(tm.keys())[:12], "...")
print("JSON source        =", tm.get("source"))
print("JSON source_sha256 =", tm.get("source_sha256"))
print("JSON source_unmodified =", tm.get("source_unmodified"))
print()

segs = tm["cushion_segments"]
R = float(tm["collision"]["ballRadius"]); D = 2*R
print(f"ballRadius={R}  diameter={D}")
L = []
for i, s in enumerate(segs):
    a = (float(s["a"]["x"]), float(s["a"]["y"])); c = (float(s["b"]["x"]), float(s["b"]["y"]))
    L.append(math.hypot(c[0]-a[0], c[1]-a[1]))
Ls = sorted(L)
print("\n=== §1.1 段长 ===")
print(f"  n={len(L)}  min={Ls[0]*1000:.4f}mm  中位={Ls[len(Ls)//2]*1000:.4f}mm  max={Ls[-1]*1000:.4f}mm")
print(f"  <直径({D*1000:.2f}mm) 的段数 = {sum(1 for x in L if x < D)}")
print(f"  <半径({R*1000:.3f}mm) 的段数 = {sum(1 for x in L if x < R)}")

print("\n=== §1.1 六条长段 ===")
idx_sorted = sorted(range(len(L)), key=lambda i: -L[i])[:6]
for i in sorted(idx_sorted):
    s = segs[i]
    print(f"  seg[{i}] rail={s.get('rail')} len={L[i]:.9f} a=({float(s['a']['x']):.9f},{float(s['a']['y']):.9f}) b=({float(s['b']['x']):.9f},{float(s['b']['y']):.9f})")

print("\n=== §1.5 包围盒极端（我重算）===")
pts = []
for i, s in enumerate(segs):
    for tag in ("a", "b"):
        pts.append((float(s[tag]["x"]), float(s[tag]["y"]), i, tag))
for name, key in (("maxX", lambda p: p[0]), ("minX", lambda p: -p[0]), ("maxY", lambda p: p[1]), ("minY", lambda p: -p[1])):
    best = max(pts, key=key)
    print(f"  {name} = {best[0] if 'X' in name else best[1]:.9f}  seg[{best[2]}].{best[3]}  len={L[best[2]]*1000:.4f}mm rail={segs[best[2]].get('rail')}")
xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
print(f"  bbox x[{min(xs):.9f},{max(xs):.9f}] y[{min(ys):.9f},{max(ys):.9f}] extX={max(xs)-min(xs):.6f} extY={max(ys)-min(ys):.6f}")

print("\n=== §1.2 绕序（台心侧）===")
pos = neg = zero = 0
for i, s in enumerate(segs):
    a = (float(s["a"]["x"]), float(s["a"]["y"])); c = (float(s["b"]["x"]), float(s["b"]["y"]))
    d = (c[0]-a[0], c[1]-a[1]); n = math.hypot(*d)
    if n == 0: zero += 1; continue
    u = (d[0]/n, d[1]/n); w = (-a[0], -a[1])
    cr = u[0]*w[1]-u[1]*w[0]
    if cr > 0: pos += 1
    elif cr < 0: neg += 1
    else: zero += 1
print(f"  左侧(+)={pos}  右侧(-)={neg}  共线={zero}")

print("\n=== §1.3 拓扑 ===")
TOL = 1e-6
uniq = []
for i, s in enumerate(segs):
    for tag in ("a", "b"):
        p = (float(s[tag]["x"]), float(s[tag]["y"]))
        for k in uniq:
            if math.hypot(p[0]-k[0], p[1]-k[1]) <= TOL:
                k[2].append((i, tag)); break
        else:
            uniq.append([p[0], p[1], [(i, tag)]])
deg = {}
for k in uniq: deg[len(k[2])] = deg.get(len(k[2]), 0) + 1
print(f"  唯一端点={len(uniq)}  度数直方图={deg}  悬空(deg1)={deg.get(1,0)}")

print("\n=== §2.1 推导重算 ===")
dt = 1/120; vmax = 8.0; amax = 5.0; eps = 5e-5
print(f"  判据A: vmax*dt/(0.5*D) = {vmax*dt/(0.5*D):.6f}  => SubSteps >= {math.ceil(vmax*dt/(0.5*D))}")
print(f"  判据B: dt/sqrt(2*eps/amax) = {dt/math.sqrt(2*eps/amax):.6f}  => SubSteps >= {math.ceil(dt/math.sqrt(2*eps/amax))}")
dt_sub = dt/3
print(f"  SubSteps=3 => dt_sub={dt_sub:.9f}")
print(f"    子步位移@8m/s = {vmax*dt_sub*1000:.4f} mm  (界 0.5D={0.5*D*1000:.3f} mm)  合规={vmax*dt_sub <= 0.5*D}")
print(f"    ½*a*dt_sub^2 = {0.5*amax*dt_sub**2*1e6:.4f} µm  (界 50 µm)  合规={0.5*amax*dt_sub**2 <= 5e-5}")
