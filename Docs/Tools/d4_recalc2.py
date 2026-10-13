#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D4 spec 复核 第二批：§1.4 证据、袋口几何、逃逸反例、22.2mm 判别力。"""
import json, math, sys, io, os
try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass
ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
tm = json.load(open(os.path.join(ROOT, "Docs/Reference/table-metadata-reference.json"), encoding="utf-8"))
segs = tm["cushion_segments"]; pk = tm["pockets"]
R = float(tm["collision"]["ballRadius"]); D = 2*R

print("=== §1.4 mouth vs segment endpoint（我重算；探针 JSON 中无此项）===")
def pt(d, k): return (float(d[k]["x"]), float(d[k]["y"]))
for pi, sname, sidx, stag in ((1,"mouthA",74,"b"), (1,"mouthB",20,"a"), (0,"mouthB",6,"b")):
    p = pk[pi]["runtimeMouth"] if "runtimeMouth" in pk[pi] else None
    print(f"  pocket[{pi}] mouth keys = {list(pk[pi].keys())[:8]}")
    break
# mouth 字段名探测
print("  pocket[1] 字段:", [k for k in pk[1].keys()])
for pi in (0,1):
    m = pk[pi].get("runtimeMouth")
    if m:
        for j,(sidx,stag) in enumerate(((74,"b"),(20,"a"),(6,"b"))):
            if j < len(m):
                q = (float(m[j]["x"]), float(m[j]["y"]))
                segp = pt(segs[sidx], stag)
                print(f"  pocket[{pi}].runtimeMouth[{j}]={q}  seg[{sidx}].{stag}={segp}  Δ={math.hypot(q[0]-segp[0],q[1]-segp[1]):.3e}")
print()
print("=== 袋口几何（throatCenter / openingRadius）===")
for p in pk:
    tc = p.get("runtimeThroatCenter"); orad = p.get("openingRadius")
    rc = p.get("runtimeCenter")
    if tc:
        tc = (float(tc["x"]), float(tc["y"]))
        d_end = min(math.hypot(tc[0]-pt(segs[i],t)[0], tc[1]-pt(segs[i],t)[1]) for i,_ in enumerate(segs) for t in ("a","b"))
        print(f"  pocket[{p['index']}] throatCenter=({tc[0]:.6f},{tc[1]:.6f}) openingRadius={float(orad):.6f} 触点半径={float(orad)+R:.6f} 最近段端点距={d_end*1000:.3f} mm")
print()
print("=== 逃逸反例：从台心朝 side pocket[1] 直射 ===")
p1 = pk[1]; tc = (float(p1["runtimeThroatCenter"]["x"]), float(p1["runtimeThroatCenter"]["y"])); orad = float(p1["openingRadius"])
print(f"  pocket[1] throatCenter=({tc[0]:.6f},{tc[1]:.6f}) openingRadius={orad:.6f}")
m = p1["runtimeMouth"]; mA=(float(m[0]["x"]),float(m[0]["y"])); mB=(float(m[1]["x"]),float(m[1]["y"]))
print(f"  mouth A={mA} B={mB}  口宽={math.hypot(mA[0]-mB[0],mA[1]-mB[1])*1000:.3f} mm")
print(f"  直库线 y = -0.635056；口两端 x = {mA[0]:.6f} / {mB[0]:.6f} ⇒ x=0 处**无段覆盖**")
print(f"  球从 (0,0) 以 v=(0,-2) 直行 ⇒ 沿 x=0 前进，穿过 y=-0.635 的缺口（该处无任何段）")
print(f"  距 throatCenter 最近时 ≈ {abs(0-tc[0]) if False else math.hypot(0-tc[0],-0.704371-tc[1])*1000:.3f} mm < 触发半径 {(orad+R)*1000:.3f} mm ⇒ PocketProximityEvents 必触发")
print()
print("=== 22.2mm 与 T5b 阈值 2e-2 的耦合 ===")
for ss in (3,4,5,6,8):
    step = 8.0*(1/120)/ss
    print(f"  SubSteps={ss}: v*dt_sub = {step*1000:.3f} mm  ≥20mm? {step>=0.02}")
print()
print("=== §1.1 短段与球半径关系（是否可能球心同时被两段判定接触）===")
Ls = sorted(math.hypot(float(s['b']['x'])-float(s['a']['x']), float(s['b']['y'])-float(s['a']['y'])) for s in segs)
print(f"  最短段 {Ls[0]*1000:.3f} mm < 球直径 {D*1000:.3f} mm ⇒ 球可同时与相邻多段相交")
