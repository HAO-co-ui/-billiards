# -*- coding: utf-8 -*-
"""Temporary: local chirality + perimeter winding reconstruction."""
import json, sys, io, math
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

BASE = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
t = json.load(open(BASE + "/table-metadata-reference.json", encoding='utf-8'))
pc = t['collision']['pockets']
seg = t['collision']['segments']

def cross(ax, ay, bx, by):
    return ax*by - ay*bx

def local_chirality(mirror_x=False, mirror_y=False):
    out = []
    for p in pc:
        m = p['runtimeMouth']
        tc = p['runtimeThroatCenter']
        sx = -1 if mirror_x else 1
        sy = -1 if mirror_y else 1
        m0 = (sx*m[0]['x'], sy*m[0]['y'])
        m1 = (sx*m[1]['x'], sy*m[1]['y'])
        tcc = (sx*tc['x'], sy*tc['y'])
        mv = (m1[0]-m0[0], m1[1]-m0[1])
        mc = ((m0[0]+m1[0])/2, (m0[1]+m1[1])/2)
        iv = (tcc[0]-mc[0], tcc[1]-mc[1])
        out.append(cross(mv[0], mv[1], iv[0], iv[1]))
    return out

base = local_chirality()
mx = local_chirality(mirror_x=True)
my = local_chirality(mirror_y=True)
print("local chirality cross(mouth01, mouthCenter->throatCenter):")
for i in range(6):
    print(f"  idx={i} base={base[i]:+.6e}  mirrorX={mx[i]:+.6e}  mirrorY={my[i]:+.6e}")

# also: cross(throat01, throatCenter->mouthCenter) maybe more stable sign
print("\nalternate: cross(mouth01, center->mouthCenter) [outward sense]")
for p in pc:
    m=p['runtimeMouth']
    mv=(m[1]['x']-m[0]['x'], m[1]['y']-m[0]['y'])
    mc=((m[0]['x']+m[1]['x'])/2,(m[0]['y']+m[1]['y'])/2)
    c=cross(mv[0],mv[1],mc[0],mc[1])
    print(f"  idx={p['index']} cross={c:+.6e} sign={'+' if c>0 else '-'}")

# reconstruct perimeter by stitching groups: nearest free endpoints
print("\nperimeter stitch:")
pts = []
groups = {}
for s in seg:
    groups.setdefault(s['rail'], []).append(s)

def order_group(ss):
    # assume already connected internally; return ordered point list a..b
    return [(ss[0]['a']['x'],ss[0]['a']['y'])] + [(s['b']['x'],s['b']['y']) for s in ss]

chains = {r: order_group(ss) for r, ss in groups.items()}
for r, c in chains.items():
    print(f"  {r:26s} start={c[0]}  end={c[-1]}")

# brute stitch: pick a start, then always attach nearest chain end
remaining = dict(chains)
start_rail = 'end_negative'
path = list(remaining.pop(start_rail))
while remaining:
    tail = path[-1]
    bestr=None; bestrev=False; bd=1e18
    for r, c in remaining.items():
        d1 = math.hypot(c[0][0]-tail[0], c[0][1]-tail[1])
        d2 = math.hypot(c[-1][0]-tail[0], c[-1][1]-tail[1])
        if d1<bd: bd=d1; bestr=r; bestrev=False
        if d2<bd: bd=d2; bestr=r; bestrev=True
    c = remaining.pop(bestr)
    if bestrev: c = c[::-1]
    print(f"  attach {bestr:26s} rev={bestrev} gap={bd:.6f}")
    path += c

area=0.0
for i in range(len(path)-1):
    x1,y1=path[i]; x2,y2=path[i+1]
    area += x1*y2-x2*y1
print(f"\nreconstructed closed-perimeter shoelace/2 = {area/2:.9f} sign={'CCW' if area>0 else 'CW'}")
print(f"closing gap last->first = {math.hypot(path[-1][0]-path[0][0], path[-1][1]-path[0][1]):.9f}")
