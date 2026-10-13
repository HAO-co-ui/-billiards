# -*- coding: utf-8 -*-
"""Temporary: dump JSON structure of the two source files."""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

BASE = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
TM = BASE + "/table-metadata-reference.json"
GM = BASE + "/gameplay-markers-reference.json"

def describe(obj, indent=0, maxdepth=3, path=""):
    pad = "  " * indent
    if indent > maxdepth:
        return
    if isinstance(obj, dict):
        for k, v in obj.items():
            t = type(v).__name__
            extra = ""
            if isinstance(v, list):
                extra = f" len={len(v)}"
                if v and isinstance(v[0], dict):
                    extra += f" elem0keys={list(v[0].keys())}"
                elif v:
                    extra += f" elem0={v[0]!r}"
            elif isinstance(v, (str, int, float, bool)) or v is None:
                s = repr(v)
                if len(s) > 120:
                    s = s[:120] + "..."
                extra = " = " + s
            print(f"{pad}{k}: {t}{extra}")
            if isinstance(v, dict):
                describe(v, indent+1, maxdepth, path+"."+k)
            elif isinstance(v, list) and v and isinstance(v[0], (dict, list)):
                describe(v[0], indent+1, maxdepth, path+"[0]")
    elif isinstance(obj, list):
        print(f"{pad}[list len={len(obj)}]")

print("="*80)
print("TABLE-METADATA-REFERENCE.JSON")
print("="*80)
d = json.load(open(TM, encoding='utf-8'))
for key in ['source', 'source_sha256', 'source_unmodified', 'source_table_object',
            'runtime_node', 'uniform_scale', 'nonuniform_scale_applied',
            'source_center_xy', 'source_cloth_z', 'source_nose_x', 'source_nose_y',
            'coordinate_mapping', 'cloth_y', 'inner_length', 'inner_width',
            'half_length', 'half_width', 'nose_height', 'bounds_runtime', 'lowest_y',
            'bytes', 'collision_geometry_status', 'collision_coordinates',
            'collision_source', 'verification', 'notes']:
    v = d.get(key, "<MISSING>")
    if isinstance(v, (dict, list)):
        print(f"\n### {key} ({type(v).__name__}, len={len(v)})")
        describe(v, 1, 2)
    else:
        print(f"\n### {key} = {v!r}")

print("\n\n### coordinate_mapping detail")
print(json.dumps(d.get('coordinate_mapping'), ensure_ascii=False, indent=1)[:1500])
print("\n### vertices / triangles")
print("vertices type:", type(d.get('vertices')), "len:", len(d.get('vertices', [])) if isinstance(d.get('vertices'), list) else "n/a")
print("triangles type:", type(d.get('triangles')), "len:", len(d.get('triangles', [])) if isinstance(d.get('triangles'), list) else "n/a")

print("\n\n" + "="*80)
print("GAMEPLAY-MARKERS-REFERENCE.JSON")
print("="*80)
g = json.load(open(GM, encoding='utf-8'))
describe(g, 0, 3)
