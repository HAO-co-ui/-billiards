# -*- coding: utf-8 -*-
"""Temporary: field coverage audit TM/GM -> TableConfig."""
import json, sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
B = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
t = json.load(open(B + "/table-metadata-reference.json", encoding='utf-8'))
g = json.load(open(B + "/gameplay-markers-reference.json", encoding='utf-8'))

# TableConfig 实际字段（来自 asset）
cfg_top = set("""sourceFileName sourceSha256 runtimeNode generatedAtUtc generator sourceVertexCount
sourceTriangleCount sourceUniformScale sourceNonUniformScaleApplied innerLength innerWidth halfLength
halfWidth clothHeight noseHeight ballRadius legBottomY outerBoundsMin outerBoundsMax segments pockets
footSpot offTableHalfExtents""".split())
cfg_seg = {"a","b","rail"}
cfg_pocket = set("""index kind center mouthA mouthB throatA throatB throatCenter throatNormal
mouthWidth throatWidth openingRadius captureRadius captureNote""".split())

def cov(name, src_keys, cfg_keys):
    missing = sorted(src_keys - cfg_keys)
    print(f"\n### {name}")
    print(f"  源字段 {len(src_keys)}  落地 {len(cfg_keys & src_keys)}  未落地 {len(missing)}")
    for m in missing:
        print(f"    - {m}")

print("=== TM 顶层字段 -> TableConfig（对照 asset 顶层）===")
print("TM top:", len(t.keys()))
# 映射：TM 字段名 -> asset 字段名 的已知等价
mapped = {
 'inner_length':'innerLength','inner_width':'innerWidth','half_length':'halfLength','half_width':'halfWidth',
 'cloth_y':'clothHeight','nose_height':'noseHeight','lowest_y':'legBottomY','runtime_node':'runtimeNode',
 'source_sha256':'sourceSha256','uniform_scale':'sourceUniformScale','nonuniform_scale_applied':'sourceNonUniformScaleApplied',
 'vertices':'sourceVertexCount','triangles':'sourceTriangleCount','bounds_runtime':'outerBoundsMin/Max',
 'collision':'segments+pockets+halfLength/clothHeight/noseHeight/ballRadius',
}
landed = set(mapped.keys())
for k in sorted(t.keys()):
    tag = "落地" if k in landed else "未落地"
    eq = f"  ({mapped[k]})" if k in mapped else ""
    print(f"    [{tag}] {k}{eq}")

print("\n=== TM collision.ballRadius -> ? ===")
print("  collision.ballRadius -> ballRadius :", 'ballRadius' in cfg_top)

cov("TM collision.segments[] 字段", set(['a','b','rail']), cfg_seg)
cov("TM collision.pockets[] 字段", set(t['collision']['pockets'][0].keys()), cfg_pocket)
cov("GM pockets[].physics2d 字段", set(g['pockets'][0]['physics2d'].keys()), cfg_pocket)

print("\n=== GM 顶层字段 ===")
gm_used = {'measured_table','pockets','cushion_segments','existing_engine_reference_parameters','validation'}
for k in sorted(g.keys()):
    print(f"    [{'落地/参考' if k in gm_used else '未落地'}] {k}")
