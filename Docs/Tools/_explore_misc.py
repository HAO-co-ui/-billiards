# -*- coding: utf-8 -*-
"""Temporary: GM sources, layouts, engine params, TM scalar fields."""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
BASE = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
t = json.load(open(BASE + "/table-metadata-reference.json", encoding='utf-8'))
g = json.load(open(BASE + "/gameplay-markers-reference.json", encoding='utf-8'))

print("GM sources:")
for s in g['sources']:
    print(f"  {s.get('sha256','')[:12]} {s.get('bytes'):>9} acc={s.get('accessible')} {s.get('path')}")

print("\nGM validation:", json.dumps(g['validation'], ensure_ascii=False))
print("\nGM initial_ball_layouts:")
for bl in g['initial_ball_layouts']:
    print(f"  mode={bl['mode']} n={len(bl['balls'])}")
    for b in bl['balls']:
        print(f"     id={b['id']} node={b['node']} phys=({b['physics2d']['x']},{b['physics2d']['y']}) canon={b['canonical_center_runtime_xyz']}")
print("\nGM cue_anchor:", json.dumps(g['cue_anchor'], ensure_ascii=False))
print("\nGM existing_engine_reference_parameters:", json.dumps(g['existing_engine_reference_parameters'], ensure_ascii=False, indent=1))

print("\nTM source_center_xy:", t['source_center_xy'])
print("TM source_nose_x:", t['source_nose_x'])
print("TM source_nose_y:", t['source_nose_y'])
print("TM source_cloth_z:", t['source_cloth_z'])
print("TM uniform_scale:", t['uniform_scale'])
print("TM nonuniform_scale_applied:", t['nonuniform_scale_applied'])
print("TM bounds_runtime:", t['bounds_runtime'])
print("TM lowest_y:", t['lowest_y'])
print("TM collision.halfLength/halfWidth/clothHeight/noseHeight/ballRadius:",
      t['collision']['halfLength'], t['collision']['halfWidth'], t['collision']['clothHeight'],
      t['collision']['noseHeight'], t['collision']['ballRadius'])
print("TM inner_length/inner_width/half_length/half_width/nose_height:", 
      t['inner_length'], t['inner_width'], t['half_length'], t['half_width'], t['nose_height'])
print("\nGM transforms full:", json.dumps(g['transforms'], ensure_ascii=False, indent=1))

# check pocket recommendedCaptureRadius vs openingRadius - ballRadius - margin
print("\ncaptureRadius derivation check (openingRadius - ballRadius - 0.002):")
br = t['collision']['ballRadius']
for p in t['collision']['pockets']:
    deriv = p['openingRadius'] - br - 0.002
    print(f"  idx={p['index']} capR={p['captureRadius']:.15f} deriv={deriv:.15f} diff={p['captureRadius']-deriv:.3e} recMatch={p['recommendedCaptureRadius']==p['captureRadius']}")

# captureNote full
print("\ncaptureNote (all):")
for p in t['collision']['pockets']:
    print(f"  idx={p['index']}: {p['captureNote']}")
