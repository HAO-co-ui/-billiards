# -*- coding: utf-8 -*-
"""Temporary: DAT-09 / DAT-10 independent verification."""
import json, sys, io, os, math
sys.path.insert(0, r"E:/Unity_Deve/Unity_Project/Billiards game/Docs/Tools")
import d2t02b_crosscheck as C
# 注意：d2t02b_crosscheck 在 import 时已把 sys.stdout 包成 utf-8，勿再次包裹。

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
B = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
t = json.load(open(B + "/table-metadata-reference.json", encoding='utf-8'))
cfg = C.parse_tableconfig(os.path.join(ROOT, "Assets/_Project/Data/TableConfig.asset"))

print("### DAT-09 库边端点包围盒（来自 TableConfig.asset，float32）")
xs = [p for s in cfg["segments"] for p in (s["a"]["x"], s["b"]["x"])]
ys = [p for s in cfg["segments"] for p in (s["a"]["y"], s["b"]["y"])]
print(f"  x[{min(xs):.9f}, {max(xs):.9f}]")
print(f"  y[{min(ys):.9f}, {max(ys):.9f}]")
expX = (-1.334342835, 1.334342911); expY = (-0.699095331, 0.699095331)
err = max(abs(min(xs)-expX[0]), abs(max(xs)-expX[1]), abs(min(ys)-expY[0]), abs(max(ys)-expY[1]))
print(f"  期望 x[{expX[0]:.9f},{expX[1]:.9f}] y[{expY[0]:.9f},{expY[1]:.9f}]")
print(f"  最大误差 = {err:.3e} m  -> {'PASS' if err < 1e-5 else 'FAIL'}")

# DAT-09 自我限定：x 不对称量
asymX = abs(min(xs) + max(xs))
asymY = abs(min(ys) + max(ys))
print(f"  自我限定核验: x 左右不对称 = {asymX:.3e} m ; y 上下不对称 = {asymY:.3e} m")
print(f"    → 该 bbox 能否抓镜像? {'否（不对称量 ' + f'{asymX:.2e}' + ' m 远小于任何可用容差）' if asymX < 1e-4 else '可能'}")
print(f"    → 但它能抓轴交换吗? 长宽比 = {(max(xs)-min(xs))/(max(ys)-min(ys)):.4f}（内库比 {2.54/1.270108866174597:.4f}）")

print("\n### DAT-10 openingRadius == runtimeInscribedRadius（源 JSON，double）")
worst = 0.0
for p in t['collision']['pockets']:
    d = abs(p['openingRadius'] - p['runtimeInscribedRadius'])
    worst = max(worst, d)
print(f"  6 袋最大偏差 = {worst:.3e}  -> {'PASS(精确相等)' if worst == 0 else ('PASS' if worst < 1e-9 else 'FAIL')}")

print("\n### 补充：资产 openingRadius 与源 runtimeInscribedRadius 的关系")
bl = json.load(open(os.path.join(ROOT, "Docs/Reports/d2t02a-baseline.json"), encoding="utf-8"))
for p, bp in zip(cfg["pockets"], bl["pockets"]):
    d = abs(p["openingRadius"] - bp["openingRadius"])
    print(f"  pocket[{p['index']}] asset.openingRadius={p['openingRadius']:.9f} baseline={bp['openingRadius']:.9f} Δ={d:.2e}")

print("\n### 补充：DAT-09 端点数与 rail 分组的二法核验（否定性结论纪律）")
print(f"  方法1(解析资产) rail 出现次数 = {sum(1 for s in cfg['segments'] if s.get('rail'))} / {len(cfg['segments'])}")
import subprocess
txt = open(os.path.join(ROOT, "Assets/_Project/Data/TableConfig.asset"), encoding='utf-8').read()
print(f"  方法2(纯文本计数) 'rail:' 出现次数 = {txt.count('rail:')}")
print(f"  方法3(源 JSON) rail 字段数 = {sum(1 for s in t['collision']['segments'] if 'rail' in s)}")
