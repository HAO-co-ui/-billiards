# -*- coding: utf-8 -*-
"""Temporary: per-rail bbox, mouth/throat correspondence to segment endpoints, local chirality."""
import json, sys, io, math
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

BASE = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
t = json.load(open(BASE + "/table-metadata-reference.json", encoding='utf-8'))
g = json.load(open(BASE + "/gameplay-markers-reference.json", encoding='utf-8'))
seg = t['collision']['segments']
pc = t['collision']['pockets']

# per-rail bbox
groups = {}
for s in seg:
    groups.setdefault(s['rail'], []).append(s)
print("per-rail bbox & endpoints:")
for rail, ss in groups.items():
    xs = [p for s in ss for p in (s['a']['x'], s['b']['x'])]
    ys = [p for s in ss for p in (s['a']['y'], s['b']['y'])]
    print(f"  {rail:26s} n={len(ss):2d} x[{min(xs):+.4f},{max(xs):+.4f}] y[{min(ys):+.4f},{max(ys):+.4f}]")
    print(f"       first a=({ss[0]['a']['x']:+.5f},{ss[0]['a']['y']:+.5f})  last b=({ss[-1]['b']['x']:+.5f},{ss[-1]['b']['y']:+.5f})")

# build set of all segment endpoints
eps = []
for i, s in enumerate(seg):
    eps.append((i, 'a', s['a']['x'], s['a']['y']))
    eps.append((i, 'b', s['b']['x'], s['b']['y']))

def nearest(x, y):
    best = None; bd = 1e9
    for (i, w, ex, ey) in eps:
        d = math.hypot(ex-x, ey-y)
        if d < bd:
            bd = d; best = (i, w, ex, ey)
    return best, bd

print("\npocket mouth/throat points -> nearest segment endpoint & to rail planes:")
for p in pc:
    print(f"\n--Pocket {p['index']} ({p['kind']}) center=({p['runtimeCenter']['x']:+.5f},{p['runtimeCenter']['y']:+.5f})")
    for label in ['runtimeMouth', 'runtimeThroat']:
        for j, pt in enumerate(p[label]):
            (i, w, ex, ey), d = nearest(pt['x'], pt['y'])
            on_long = abs(abs(pt['y']) - t['half_width']) < 1e-6
            on_end = abs(abs(pt['x']) - t['half_length']) < 1e-6
            print(f"   {label}[{j}]=({pt['x']:+.6f},{pt['y']:+.6f}) near seg{i}.{w} d={d:.3e} onLong={on_long} onEnd={on_end} rail={seg[i]['rail']}")

# local chirality: cross( mouth[0]->mouth[1] , mouth_center->throat_center )
print("\nLocal chirality cross(mouth01, mouthCenter->throatCenter):")
def cross(ax,ay,bx,by): return ax*by-ay*bx
for p in pc:
    m = p['runtimeMouth']; th = p['runtimeThroat']
    mv = (m[1]['x']-m[0]['x'], m[1]['y']-m[0]['y'])
    mc = ((m[0]['x']+m[1]['x'])/2, (m[0]['y']+m[1]['y'])/2)
    tc = p['runtimeThroatCenter']
    iv = (tc['x']-mc['x'], tc['y']-mc['y'])
    c = cross(mv[0],mv[1],iv[0],iv[1])
    print(f"  idx={p['index']} cross={c:+.9f} sign={'+' if c>0 else '-'}")

# Now same for a mirrored copy (x -> -x) to test discrimination
print("\nAfter synthetic mirror x->-x: local chirality should flip")
for p in pc:
    m = p['runtimeMouth']; th = p['runtimeThroat']
    mv = (-(m[1]['x'])-(-(m[0]['x'])), m[1]['y']-m[0]['y'])  # mirror x then diff
    mc = ((-(m[0]['x'])+-(m[1]['x']))/2, (m[0]['y']+m[1]['y'])/2)
    tc = p['runtimeThroatCenter']
    iv = (tc['x']*(-1)-mc[0], tc['y']-mc[1])
    c = cross(mv[0],mv[1],iv[0],iv[1])
    print(f"  idx={p['index']} cross={c:+.9f} sign={'+' if c>0 else '-'}")
