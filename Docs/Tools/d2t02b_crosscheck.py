#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d2t02b_crosscheck.py -- D2-T02B 独立复核：TableConfig.asset ↔ D2-T02A 基线
================================================================================
只读解析 Assets/_Project/Data/TableConfig.asset（Unity YAML），与
Docs/Reports/d2t02a-baseline.json 做**索引对齐、逐字段**比对（float32 域）。
另含 CHR-01/CHR-02 手性/绕向自检与反向用例注入实跑。

不依赖任何实现者脚本；不写入 Assets/。
运行： PY Docs/Tools/d2t02b_crosscheck.py
"""
from __future__ import annotations
import json, math, re, os, struct, sys, io

try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"
ASSET = os.path.join(ROOT, "Assets/_Project/Data/TableConfig.asset")
BASELINE = os.path.join(ROOT, "Docs/Reports/d2t02a-baseline.json")


def f32(x):
    """double -> float32 值（往返一次，得到 float32 可表示的最接近值）。"""
    return struct.unpack("<f", struct.pack("<f", float(x)))[0]


# ---------------------------------------------------------------------------
# Unity YAML 解析（针对本资产的受限语法）
# ---------------------------------------------------------------------------
INLINE = re.compile(r"^\s*\{?\s*(.*?)\s*\}?\s*$")

def _parse_inline(s):
    """'x: 1.0, y: 2.0' / '{x: 1.0, y: 2.0}' -> {'x':1.0,'y':2.0}；也接受 'key: value'。"""
    s = s.strip()
    if s.startswith("{"):
        s = s[1:]
    if s.endswith("}"):
        s = s[:-1]
    out = {}
    for part in s.split(","):
        if ":" not in part:
            continue
        k, v = part.split(":", 1)
        out[k.strip().strip("{}").strip()] = _scalar(v.strip())
    return out


def _scalar(v):
    v = v.strip()
    if v == "":
        return ""
    if v in ("true", "True"):
        return True
    if v in ("false", "False"):
        return False
    try:
        if re.match(r"^-?\d+$", v):
            return int(v)
    except Exception:
        pass
    try:
        return float(v)
    except ValueError:
        pass
    return v


def parse_tableconfig(path):
    lines = open(path, "r", encoding="utf-8").read().splitlines()
    cfg = {"segments": [], "pockets": []}
    i = 0
    cur_list = None       # 'segments' | 'pockets' | None
    cur_item = None
    pending_note = None
    while i < len(lines):
        raw = lines[i]
        line = raw.rstrip()
        stripped = line.strip()
        if not stripped or stripped.startswith("%") or stripped.startswith("---"):
            i += 1
            continue
        indent = len(line) - len(line.lstrip(" "))

        # captureNote 折行续行（缩进 > 6 且无 key:）
        if pending_note is not None and indent >= 6 and ":" not in stripped:
            cur_item["captureNote"] = (cur_item["captureNote"] + " " + stripped).strip()
            i += 1
            continue
        else:
            pending_note = None

        if stripped.endswith(":") and indent == 2 and not stripped.startswith("-"):
            key = stripped[:-1]
            if key in ("segments", "pockets"):
                cur_list = key
            else:
                cur_list = None
            i += 1
            continue

        if stripped.startswith("- ") and indent == 2:
            item = {}
            if cur_list == "segments":
                cfg["segments"].append(item)
            elif cur_list == "pockets":
                cfg["pockets"].append(item)
            cur_item = item
            body = stripped[2:]
            if ":" in body:
                k, v = body.split(":", 1)
                item[k.strip()] = _scalar(v.strip()) if "{" not in v else _parse_inline(v)
            i += 1
            continue

        # 嵌套字段（indent >= 4）
        if cur_item is not None and indent >= 4 and ":" in stripped:
            k, v = stripped.split(":", 1)
            k = k.strip()
            if "{" in v:
                cur_item[k] = _parse_inline(v)
            else:
                val = _scalar(v.strip())
                cur_item[k] = val
                if k == "captureNote" and v.strip() == "":
                    cur_item[k] = ""
                    pending_note = k
            i += 1
            continue

        # 顶层标量（indent == 2）
        if indent == 2 and ":" in stripped:
            k, v = stripped.split(":", 1)
            if "{" in v:
                cfg[k.strip()] = _parse_inline(v)
            else:
                cfg[k.strip()] = _scalar(v.strip())
            i += 1
            continue
        i += 1
    return cfg


# ---------------------------------------------------------------------------
# 主比对
# ---------------------------------------------------------------------------
def main():
    bl = json.load(open(BASELINE, encoding="utf-8"))
    cfg = parse_tableconfig(ASSET)

    print("=" * 78)
    print("D2-T02B 独立复核：TableConfig.asset  vs  D2-T02A 基线")
    print("=" * 78)
    print(f"解析段数={len(cfg['segments'])}  袋数={len(cfg['pockets'])}  "
          f"sourceSha256={cfg.get('sourceSha256')}")

    findings = []
    raw_max = 0.0
    f32_max = 0.0
    worst = {"name": None, "raw": -1.0}
    f32_mismatch = []   # asset 与 baseline 在 float32 域**不同**的点（才是真错）

    def _record(name, av, bv):
        nonlocal raw_max, f32_max
        r = abs(av - bv); f = abs(av - f32(bv))
        raw_max = max(raw_max, r); f32_max = max(f32_max, f)
        if r > worst["raw"]:
            worst["name"], worst["raw"] = name, r
        # 关键判据：解析出的十进制值四舍五入到 float32 后，是否等于 baseline 的 float32？
        if f32(av) != f32(bv):
            f32_mismatch.append((name, av, bv, f32(av), f32(bv)))
        return r, f

    def cmp_point(name, ax, ay, bx, by):
        _record(name + ".x", ax, bx)
        _record(name + ".y", ay, by)

    def cmp_scalar(name, av, bv, tol):
        r, f = _record(name, av, bv)
        if f > tol:
            findings.append(f"FAIL {name}: asset={av!r} baseline={bv!r} rawΔ={r:.3e} f32Δ={f:.3e}")

    # 标量
    scalar_map = [
        ("innerLength", "inner_length"), ("innerWidth", "inner_width"),
        ("halfLength", "half_length"), ("halfWidth", "half_width"),
        ("clothHeight", "cloth_y"), ("noseHeight", "nose_height"),
        ("ballRadius", "ball_radius"), ("legBottomY", "lowest_y"),
    ]
    for ak, bk in scalar_map:
        cmp_scalar(f"table.{ak}", cfg[ak], bl["table"][bk], 1e-6)

    # 段
    bs = {s["index"]: s for s in bl["segments"]}
    if len(cfg["segments"]) != 82:
        findings.append(f"FAIL 段数={len(cfg['segments'])} != 82")
    for k, s in enumerate(cfg["segments"]):
        b = bs[k]
        if s.get("rail") != b["rail"]:
            findings.append(f"FAIL seg[{k}].rail '{s.get('rail')}' != '{b['rail']}'")
        cmp_point(f"seg[{k}].a", s["a"]["x"], s["a"]["y"], b["a"]["x"], b["a"]["y"])
        cmp_point(f"seg[{k}].b", s["b"]["x"], s["b"]["y"], b["b"]["x"], b["b"]["y"])

    # 袋
    bp = {p["index"]: p for p in bl["pockets"]}
    if len(cfg["pockets"]) != 6:
        findings.append(f"FAIL 袋数={len(cfg['pockets'])} != 6")
    point_fields = [
        ("center", "center"), ("mouthA", "mouth", 0), ("mouthB", "mouth", 1),
        ("throatA", "throat", 0), ("throatB", "throat", 1),
        ("throatCenter", "throatCenter"), ("throatNormal", "throatNormal"),
    ]
    for p in cfg["pockets"]:
        i = p["index"]; b = bp[i]
        if p["kind"] != b["kind"]:
            findings.append(f"FAIL pocket[{i}].kind '{p['kind']}' != '{b['kind']}'")
        for spec in point_fields:
            fname = spec[0]
            bpt = b[spec[1]] if len(spec) == 2 else b[spec[1]][spec[2]]
            cmp_point(f"pocket[{i}].{fname}", p[fname]["x"], p[fname]["y"], bpt["x"], bpt["y"])
        cmp_scalar(f"pocket[{i}].openingRadius", p["openingRadius"], b["openingRadius"], 1e-6)
        cmp_scalar(f"pocket[{i}].captureRadius", p["captureRadius"], b["captureRadius"], 1e-6)
        cmp_scalar(f"pocket[{i}].mouthWidth", p["mouthWidth"], b["mouthWidth"], 1e-6)
        cmp_scalar(f"pocket[{i}].throatWidth", p["throatWidth"], b["throatWidth"], 1e-6)
        if not p.get("captureNote"):
            findings.append(f"FAIL pocket[{i}].captureNote 为空")

    print(f"\n[1] 索引对齐逐字段比对")
    print(f"    原始 maxDelta (asset_double - baseline_double) = {raw_max:.6e} m")
    print(f"    float32 域 maxDelta (asset - f32(baseline))     = {f32_max:.6e} m")
    print(f"    最大原始偏差处: {worst['name']}  Δ={worst['raw']:.3e} m")
    print(f"    解析值经 float32 舍入后 != baseline(float32) 的点数 = {len(f32_mismatch)}")
    for m in f32_mismatch[:10]:
        print(f"      {m[0]}: asset={m[1]!r} base={m[2]!r} f32(asset)={m[3]!r} f32(base)={m[4]!r}")
    print(f"    在 1e-6 容差下 findings 数 = {len(findings)}")
    for f in findings[:20]:
        print("      " + f)

    # 手性 CHR-01
    signs = []
    for p in sorted(cfg["pockets"], key=lambda x: x["index"]):
        mvx = p["mouthB"]["x"] - p["mouthA"]["x"]; mvy = p["mouthB"]["y"] - p["mouthA"]["y"]
        mcx = (p["mouthA"]["x"] + p["mouthB"]["x"]) / 2.0
        mcy = (p["mouthA"]["y"] + p["mouthB"]["y"]) / 2.0
        ivx = p["throatCenter"]["x"] - mcx; ivy = p["throatCenter"]["y"] - mcy
        cr = mvx * ivy - mvy * ivx
        signs.append(1 if cr >= 0 else -1)
    exp = [1, -1, -1, -1, 1, 1]
    print(f"\n[2] CHR-01 袋口手性符号 = {signs}  期望 {exp}  -> {'PASS' if signs == exp else 'FAIL'}")

    # 绕向 CHR-02
    groups = {}
    order = []
    for s in cfg["segments"]:
        if s["rail"] not in groups:
            groups[s["rail"]] = []
            order.append(s["rail"])
        groups[s["rail"]].append(s)
    chains = {}
    for r, ss in groups.items():
        pts = [ss[0]["a"]] + [x["b"] for x in ss]
        chains[r] = [(pt["x"], pt["y"]) for pt in pts]
    start = order[0]
    path = list(chains.pop(start))
    while chains:
        tail = path[-1]
        bestr = bestrev = None; bd = 1e18
        for r, c in chains.items():
            d1 = math.hypot(c[0][0] - tail[0], c[0][1] - tail[1])
            d2 = math.hypot(c[-1][0] - tail[0], c[-1][1] - tail[1])
            if d1 < bd: bd, bestr, bestrev = d1, r, False
            if d2 < bd: bd, bestr, bestrev = d2, r, True
        c = chains.pop(bestr)
        if bestrev: c = c[::-1]
        path += c
    area = 0.0
    for j in range(len(path) - 1):
        area += path[j][0] * path[j + 1][1] - path[j + 1][0] * path[j][1]
    area /= 2.0
    gap = math.hypot(path[-1][0] - path[0][0], path[-1][1] - path[0][1])
    print(f"[3] CHR-02 库边拼接绕向 shoelace/2 = {area:.6f} (期望 +3.144285) "
          f"gap={gap*1000:.1f}mm -> {'PASS(CCW)' if area > 0 else 'FAIL'}")

    # ── 反向用例注入（独立复刻实现者三项判据）─────────────────────────────
    run_reverse_cases(cfg, bl)

    return cfg, bl, raw_max, f32_max, findings


def _chirality(cfg):
    signs = []
    for p in sorted(cfg["pockets"], key=lambda x: x["index"]):
        mvx = p["mouthB"]["x"] - p["mouthA"]["x"]; mvy = p["mouthB"]["y"] - p["mouthA"]["y"]
        mcx = (p["mouthA"]["x"] + p["mouthB"]["x"]) / 2.0
        mcy = (p["mouthA"]["y"] + p["mouthB"]["y"]) / 2.0
        cr = mvx * (p["throatCenter"]["y"] - mcy) - mvy * (p["throatCenter"]["x"] - mcx)
        signs.append(1 if cr >= 0 else -1)
    return signs


def _winding(cfg):
    groups = {}
    order = []
    for s in cfg["segments"]:
        if s["rail"] not in groups:
            groups[s["rail"]] = []; order.append(s["rail"])
        groups[s["rail"]].append(s)
    chains = {}
    for r, ss in groups.items():
        chains[r] = [(ss[0]["a"]["x"], ss[0]["a"]["y"])] + [(x["b"]["x"], x["b"]["y"]) for x in ss]
    path = list(chains.pop(order[0]))
    while chains:
        tail = path[-1]
        bestr = bestrev = None; bd = 1e18
        for r, c in chains.items():
            d1 = math.hypot(c[0][0]-tail[0], c[0][1]-tail[1])
            d2 = math.hypot(c[-1][0]-tail[0], c[-1][1]-tail[1])
            if d1 < bd: bd, bestr, bestrev = d1, r, False
            if d2 < bd: bd, bestr, bestrev = d2, r, True
        c = chains.pop(bestr)
        if bestrev: c = c[::-1]
        path += c
    a = 0.0
    for j in range(len(path)-1):
        a += path[j][0]*path[j+1][1] - path[j+1][0]*path[j][1]
    return a/2.0


def _maxdelta_vs_baseline(cfg, bl, tol=1e-5):
    """索引对齐逐字段比对，返回 (maxDelta, n_violations)。"""
    bs = {s["index"]: s for s in bl["segments"]}
    bp = {p["index"]: p for p in bl["pockets"]}
    worst = 0.0; vio = 0
    for k, s in enumerate(cfg["segments"]):
        if s.get("rail") != bs[k]["rail"]:
            vio += 1
        for e in ("a", "b"):
            d = math.hypot(s[e]["x"]-bs[k][e]["x"], s[e]["y"]-bs[k][e]["y"])
            worst = max(worst, d)
            if d > tol: vio += 1
    for p in cfg["pockets"]:
        b = bp[p["index"]]
        for spec in [("center","center"),("mouthA","mouth",0),("mouthB","mouth",1),
                     ("throatA","throat",0),("throatB","throat",1),
                     ("throatCenter","throatCenter"),("throatNormal","throatNormal")]:
            bpt = b[spec[1]] if len(spec) == 2 else b[spec[1]][spec[2]]
            d = math.hypot(p[spec[0]]["x"]-bpt["x"], p[spec[0]]["y"]-bpt["y"])
            worst = max(worst, d)
            if d > tol: vio += 1
        for f in ("openingRadius","captureRadius","mouthWidth","throatWidth"):
            d = abs(p[f]-b[f]); worst = max(worst, d)
            if d > tol: vio += 1
    return worst, vio


def _scalar_violations(cfg):
    """复刻实现 DAT-03：公共标量 vs PoolConstants（硬编码常量，独立于资产）。"""
    exp = {"innerLength":2.54,"innerWidth":1.270108866,"halfLength":1.27,
           "halfWidth":0.6350544330872985,"clothHeight":0.0,"noseHeight":0.04498204256884025,
           "ballRadius":0.028575,"legBottomY":-0.8635167479515076}
    vio = 0; worst = 0.0
    for k,v in exp.items():
        d = abs(cfg[k]-v); worst = max(worst,d)
        if d > 1e-6: vio += 1
    return worst, vio


def run_reverse_cases(cfg0, bl):
    import copy
    print("\n[4] 反向用例注入实跑（复刻实现的 CMP-03 / DAT-03 / CHR-01 / CHR-02）")
    print("    判据: CMP-03=索引对齐逐字段(tol 1e-5) | DAT-03=公共标量 vs PoolConstants")
    print("          | CHR-01=手性[+,-,-,-,+,+] | CHR-02=绕向>0 且 ≈3.1443")

    def apply(case):
        c = copy.deepcopy(cfg0)
        if case == "RC-1":
            c["segments"][40]["b"]["x"] += 0.001
        elif case == "RC-2":
            c["ballRadius"] = 0.03
        elif case == "RC-3":
            c["pockets"][0]["captureRadius"] = 1.0
        elif case == "RC-4":
            for s in c["segments"]:
                s["a"]["x"] *= -1; s["b"]["x"] *= -1
            for p in c["pockets"]:
                for f in ("center","mouthA","mouthB","throatA","throatB","throatCenter","throatNormal"):
                    p[f]["x"] *= -1
        elif case == "RC-5":
            for s in c["segments"]:
                for e in ("a","b"):
                    x,y = s[e]["x"], s[e]["y"]; s[e]["x"], s[e]["y"] = -y, x
            for p in c["pockets"]:
                for f in ("center","mouthA","mouthB","throatA","throatB","throatCenter","throatNormal"):
                    x,y = p[f]["x"], p[f]["y"]; p[f]["x"], p[f]["y"] = -y, x
        elif case == "RC-6":
            for s in c["segments"]:
                for e in ("a","b"):
                    s[e]["x"] *= -1; s[e]["y"] *= -1
            for p in c["pockets"]:
                for f in ("center","mouthA","mouthB","throatA","throatB","throatCenter","throatNormal"):
                    p[f]["x"] *= -1; p[f]["y"] *= -1
        elif case == "RC-7":
            for s in c["segments"]:
                for e in ("a","b"):
                    s[e]["x"] *= 1.2; s[e]["y"] *= 1.2
            for p in c["pockets"]:
                for f in ("center","mouthA","mouthB","throatA","throatB","throatCenter"):
                    p[f]["x"] *= 1.2; p[f]["y"] *= 1.2
        return c

    for case in ["clean","RC-1","RC-2","RC-3","RC-4","RC-5","RC-6","RC-7"]:
        c = apply(case) if case != "clean" else copy.deepcopy(cfg0)
        worst, vio = _maxdelta_vs_baseline(c, bl)
        sworst, svio = _scalar_violations(c)
        chir = _chirality(c) == [1,-1,-1,-1,1,1]
        w = _winding(c)
        chr2 = (w > 0) and (abs(w - 3.144285) < 0.01)
        fired = []
        if vio > 0: fired.append(f"CMP-03(vio={vio},maxΔ={worst:.2e})")
        if svio > 0: fired.append(f"DAT-03(scalarΔ={sworst:.2e})")
        if not chir: fired.append("CHR-01")
        if not chr2: fired.append(f"CHR-02(shoelace={w:.4f})")
        if case == "clean":
            verdict = "PASS(应通过)" if not fired else "**误报**"
        else:
            verdict = "拦截" if fired else "**漏检**"
        print(f"    {case:6s} -> {verdict:10s} 触发: {', '.join(fired) if fired else '无'}")



if __name__ == "__main__":
    main()
