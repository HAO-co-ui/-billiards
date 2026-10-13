# -*- coding: utf-8 -*-
"""Temporary: empirically exercise the checkers against reverse cases & failure modes."""
import json, sys, io, math, copy
sys.path.insert(0, r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Tools")
import verify_collision_data as V
# 注意：verify_collision_data 在 import 时已把 sys.stdout 包成 utf-8，勿再次包裹。

BASE = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
tm = json.load(open(BASE + "/table-metadata-reference.json", encoding='utf-8'))
baseline = V.build_baseline(tm)

def candidate_from_baseline(bl):
    return {
        "table": {
            "inner_length": bl["table"]["inner_length"],
            "inner_width": bl["table"]["inner_width"],
            "half_length": bl["table"]["half_length"],
            "half_width": bl["table"]["half_width"],
            "nose_height": bl["table"]["nose_height"],
            "cloth_y": bl["table"]["cloth_y"],
            "ball_radius": bl["table"]["ball_radius"],
        },
        "segments": [{"index": s["index"], "rail": s["rail"],
                      "a": dict(s["a"]), "b": dict(s["b"])} for s in bl["segments"]],
        "pockets": [{"index": p["index"], "kind": p["kind"],
                     "center": dict(p["center"]),
                     "openingRadius": p["openingRadius"], "captureRadius": p["captureRadius"],
                     "mouth": [dict(p["mouth"][0]), dict(p["mouth"][1])],
                     "throat": [dict(p["throat"][0]), dict(p["throat"][1])],
                     "throatCenter": dict(p["throatCenter"])} for p in bl["pockets"]],
    }

def show(name, cand):
    r = V.run_full_verification(cand, baseline)
    v = r["value_findings"]
    s = r["structure_findings"]
    print(f"\n=== {name} ===")
    print(f"  pass={r['pass']}  value_findings={len(v)}  structure_findings={s}")
    for f in v[:4]:
        print(f"    V: {f}")
    if len(v) > 4:
        print(f"    ... 共 {len(v)} 条")

print("### 0. 未注入（应为 PASS）")
show("clean", candidate_from_baseline(baseline))

print("\n### RC-1: seg[40].b.x += 1mm")
c = candidate_from_baseline(baseline); c["segments"][40]["b"]["x"] += 0.001
show("RC-1 端点偏移 1mm", c)

print("\n### RC-2: ballRadius = 0.03")
c = candidate_from_baseline(baseline); c["table"]["ball_radius"] = 0.03
show("RC-2 球半径 0.03", c)

print("\n### RC-3: pocket[0].captureRadius = 1.0")
c = candidate_from_baseline(baseline); c["pockets"][0]["captureRadius"] = 1.0
show("RC-3 captureRadius=1.0", c)

print("\n### RC-3b: pocket[0].captureRadius = 0.030 (细微错，量级合理)")
c = candidate_from_baseline(baseline); c["pockets"][0]["captureRadius"] = 0.030
show("RC-3b captureRadius=0.030", c)

print("\n### RC-4: 全量镜像 x -> -x")
c = candidate_from_baseline(baseline)
for s in c["segments"]:
    s["a"]["x"] *= -1; s["b"]["x"] *= -1
    s["rail"] = s["rail"]  # rail 语义也应变，但故意不改以观察
for p in c["pockets"]:
    p["center"]["x"] *= -1
    for g in ("mouth", "throat"):
        for pt in p[g]: pt["x"] *= -1
    p["throatCenter"]["x"] *= -1
show("RC-4 镜像 x->-x", c)

print("\n### RC-4b: 镜像 x->-x 但 pocket index 也按镜像重排（更隐蔽）")
c2 = candidate_from_baseline(baseline)
for s in c2["segments"]:
    s["a"]["x"] *= -1; s["b"]["x"] *= -1
for p in c2["pockets"]:
    p["center"]["x"] *= -1
    for g in ("mouth", "throat"):
        for pt in p[g]: pt["x"] *= -1
    p["throatCenter"]["x"] *= -1
show("RC-4b 镜像(不重排)", c2)

print("\n### RC-5: 90° 旋转 (x,y)->(-y,x)")
c = candidate_from_baseline(baseline)
for s in c["segments"]:
    for e in ("a", "b"):
        x, y = s[e]["x"], s[e]["y"]; s[e]["x"], s[e]["y"] = -y, x
for p in c["pockets"]:
    x, y = p["center"]["x"], p["center"]["y"]; p["center"]["x"], p["center"]["y"] = -y, x
    for g in ("mouth", "throat"):
        for pt in p[g]:
            x, y = pt["x"], pt["y"]; pt["x"], pt["y"] = -y, x
    x, y = p["throatCenter"]["x"], p["throatCenter"]["y"]; p["throatCenter"]["x"], p["throatCenter"]["y"] = -y, x
show("RC-5 90°旋转", c)

print("\n### RC-6: 180° 旋转 (x,y)->(-x,-y)")
c = candidate_from_baseline(baseline)
for s in c["segments"]:
    for e in ("a", "b"):
        s[e]["x"] *= -1; s[e]["y"] *= -1
for p in c["pockets"]:
    p["center"]["x"] *= -1; p["center"]["y"] *= -1
    for g in ("mouth", "throat"):
        for pt in p[g]: pt["x"] *= -1; pt["y"] *= -1
    p["throatCenter"]["x"] *= -1; p["throatCenter"]["y"] *= -1
show("RC-6 180°旋转", c)

print("\n### RC-7: 整体 ×1.2（双重缩放）")
c = candidate_from_baseline(baseline)
for s in c["segments"]:
    for e in ("a", "b"):
        s[e]["x"] *= 1.2; s[e]["y"] *= 1.2
for p in c["pockets"]:
    p["center"]["x"] *= 1.2; p["center"]["y"] *= 1.2
    for g in ("mouth", "throat"):
        for pt in p[g]: pt["x"] *= 1.2; pt["y"] *= 1.2
    p["throatCenter"]["x"] *= 1.2; p["throatCenter"]["y"] *= 1.2
show("RC-7 整体×1.2", c)

# 单独观察自包含判据对 RC-6 的分辨（用 1mm 容差，看是否被放过）
print("\n### RC-6 在 1mm 容差下的值比对结果（模拟宽松校验器）")
c = candidate_from_baseline(baseline)
for s in c["segments"]:
    for e in ("a", "b"):
        s[e]["x"] *= -1; s[e]["y"] *= -1
for p in c["pockets"]:
    p["center"]["x"] *= -1; p["center"]["y"] *= -1
r = V.compare_candidate(c, baseline, tol=1e-3)
print(f"  1e-3 容差下 value_findings 数量 = {len(r)}  (0 表示被放过)")

# 关键演示：若校验器是"顺序无关/最近邻匹配"，180° 旋转只有 0.28mm 差异
print("\n### 顺序无关最近邻匹配下，各旋转/镜像的最大匹配距离（暴露 0.28mm 脆弱性）")
def set_nn_max(cand_pts, base_pts):
    m = 0.0
    for cp in cand_pts:
        d = min(math.hypot(cp[0]-bp[0], cp[1]-bp[1]) for bp in base_pts)
        m = max(m, d)
    return m

base_pts = []
for s in baseline["segments"]:
    base_pts += [(s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"])]
for p in baseline["pockets"]:
    base_pts += [(p["center"]["x"], p["center"]["y"])]

def transform(tx):
    pts = []
    for s in baseline["segments"]:
        for e in ("a", "b"):
            pts.append(tx(s[e]["x"], s[e]["y"]))
    for p in baseline["pockets"]:
        pts.append(tx(p["center"]["x"], p["center"]["y"]))
    return pts

for name, tx in [("identity", lambda x,y:(x,y)),
                 ("mirror_x", lambda x,y:(-x,y)),
                 ("mirror_y", lambda x,y:(x,-y)),
                 ("rot90", lambda x,y:(-y,x)),
                 ("rot180", lambda x,y:(-x,-y))]:
    print(f"  {name:9s} 顺序无关最近邻最大距离 = {set_nn_max(transform(tx), base_pts):.3e} m")

