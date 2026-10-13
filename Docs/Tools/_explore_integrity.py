# -*- coding: utf-8 -*-
"""Temporary: integrity / same-source evidence / field gaps / degeneracy."""
import json, sys, io, math, hashlib
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
BASE = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
TM = BASE + "/table-metadata-reference.json"
GM = BASE + "/gameplay-markers-reference.json"
t = json.load(open(TM, encoding='utf-8'))
g = json.load(open(GM, encoding='utf-8'))

print("### 1. 同源证据")
print(f"TM 文件字节数 = {len(open(TM,'rb').read())}")
for s in g['sources']:
    if 'table-metadata' in s['path']:
        print(f"GM.sources 中 table-metadata.json: bytes={s['bytes']} sha256={s['sha256']}")
        print(f"  与 TM 文件字节数相同? {s['bytes']==len(open(TM,'rb').read())}")
print(f"TM source_sha256(源FBX) = {t['source_sha256']}")

print("\n### 2. GM 袋字段 vs TM 袋字段（GM 缺失哪些）")
tm_keys = set(t['collision']['pockets'][0].keys())
gm_keys = set(g['pockets'][0]['physics2d'].keys())
print("TM pocket keys:", sorted(tm_keys))
print("GM physics2d  keys:", sorted(gm_keys))
print("TM 有而 GM 无:", sorted(tm_keys - gm_keys))

print("\n### 3. GM 内部自洽：physics2d {x,y}  vs  canonical_runtime_endpoints_xyz [x,0,z]")
mx = 0.0
for s in g['cushion_segments']:
    a = s['physics2d']['a']; b = s['physics2d']['b']
    ca = s['canonical_runtime_endpoints_xyz'][0]; cb = s['canonical_runtime_endpoints_xyz'][1]
    mx = max(mx, abs(a['x']-ca[0]), abs(a['y']-ca[2]), abs(ca[1]-0), abs(b['x']-cb[0]), abs(b['y']-cb[2]), abs(cb[1]-0))
print(f"  最大偏差 = {mx:.3e}  (期望 x→[0], y→[2], [1]=0)")

print("\n### 4. GM 内部自洽：pocket canonical_center == [physics2d.x, 0, physics2d.y]")
mx = 0.0
for p in g['pockets']:
    ph = p['physics2d']; c = p['canonical_center_runtime_xyz']
    mx = max(mx, abs(c[0]-ph['x']), abs(c[1]-0), abs(c[2]-ph['y']))
print(f"  最大偏差 = {mx:.3e}")

print("\n### 5. GM displayed == 1.2 * canonical ?（双重缩放陷阱）")
mx = 0.0
for p in g['pockets']:
    c = p['canonical_center_runtime_xyz']; d = p['displayed_center_runtime_xyz']
    mx = max(mx, abs(d[0]-1.2*c[0]), abs(d[2]-1.2*c[2]))
for s in g['cushion_segments']:
    ca = s['canonical_runtime_endpoints_xyz'][0]; da = s['displayed_runtime_endpoints_xyz'][0]
    mx = max(mx, abs(da[0]-1.2*ca[0]), abs(da[2]-1.2*ca[2]))
print(f"  displayed vs 1.2*canonical 最大偏差 = {mx:.3e}")
print(f"  measured_table canonical_inner=[{g['measured_table']['canonical_inner_dimensions_m']}]")
print(f"  measured_table displayed_inner=[{g['measured_table']['prototype_displayed_inner_dimensions_m']}]  ratio={g['measured_table']['prototype_displayed_inner_dimensions_m'][0]/2.54}")

print("\n### 6. 退化/重复段检查")
seg = t['collision']['segments']
zero = [(i, s['rail'], s['a'], s['b']) for i, s in enumerate(seg) if math.hypot(s['b']['x']-s['a']['x'], s['b']['y']-s['a']['y']) < 1e-9]
print(f"  零长段数量 = {len(zero)}")
ends = {}
for s in seg:
    for p in (s['a'], s['b']):
        k = (round(p['x'], 9), round(p['y'], 9))
        ends[k] = ends.get(k, 0) + 1
dups = {k: v for k, v in ends.items() if v > 2}
print(f"  出现>2次的端点(非流形点) 数量 = {len(dups)}")
for k, v in list(dups.items())[:10]:
    print(f"    {k} x{v}")

lens = [math.hypot(s['b']['x']-s['a']['x'], s['b']['y']-s['a']['y']) for s in seg]
print(f"  段长 min={min(lens):.6e} max={max(lens):.6e} 总和={sum(lens):.9f}")

print("\n### 7. 6 袋是否严格对称（镜像判据的判别力来源）")
pc = t['collision']['pockets']
for a_idx, b_idx, axis in [(0,2,'x(-)->x(+)'),(0,3,'y(-)->y(+)'),(1,4,'y(-)->y(+) side')]:
    pa = pc[a_idx]['runtimeCenter']; pb = pc[b_idx]['runtimeCenter']
    if axis.startswith('x'):
        print(f"  P{a_idx} vs P{b_idx}: x {pa['x']:.9f} vs {-pb['x']:.9f} diff={abs(pa['x']+pb['x']):.3e}")
    else:
        print(f"  P{a_idx} vs P{b_idx}: y {pa['y']:.9f} vs {-pb['y']:.9f} diff={abs(pa['y']+pb['y']):.3e}")
print(f"  P1.x={pc[1]['runtimeCenter']['x']:.9f} P4.x={pc[4]['runtimeCenter']['x']:.9f} diff={abs(pc[1]['runtimeCenter']['x']-pc[4]['runtimeCenter']['x']):.3e}")

print("\n### 8. 长/短轴比")
xs=[c for s in seg for c in (s['a']['x'],s['b']['x'])]; ys=[c for s in seg for c in (s['a']['y'],s['b']['y'])]
w=max(xs)-min(xs); h=max(ys)-min(ys)
print(f"  库边 bbox {w:.6f} x {h:.6f}  ratio={w/h:.6f}  内库比={2.54/1.270108866174597:.6f}")
