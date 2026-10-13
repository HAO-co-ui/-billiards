#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_generated_artifacts.py -- D2-T04 生成物一致性守卫
================================================================================
铁律 10：**生成物守卫的强度 = 生成器读取的东西。** 按"生成器读什么"分级：

  A 读源型   生成器读取源文件/工程 → 守卫 = 源指纹 + 生成器逻辑指纹 (+ 可做的内容重算)
  B 转写型   生成器不读任何源（纯字面量）→ 指纹检查无意义，**必须重跑真检查器 + 全字段比对**

用法：
  PY check_generated_artifacts.py                 # 对真实仓库跑全部守卫
  PY check_generated_artifacts.py --with-fbx       # 含 FBX 重算（慢）
  PY check_generated_artifacts.py --self-test      # 反向用例：注入漂移，验证守卫会 FAIL
  PY check_generated_artifacts.py --write-ledger   # 刷新生成器逻辑指纹台账

纪律：**只读原物**；任何写入都落到临时路径或显式 --write-ledger。不覆盖已提交证据。
"""
from __future__ import annotations
import argparse
import hashlib
import importlib
import io
import json
import os
import re
import shutil
import struct
import sys
import tempfile
import xml.etree.ElementTree as ET

try:
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
except Exception:
    pass

REPO = r"E:/Unity_Deve/Unity_Project/Billiards game"
SRC = r"F:/工作文件/idea35_台球/08_Gameplay_Markers"
TOOLS = os.path.join(REPO, "Docs/Tools")
LEDGER_REL = "Docs/Reports/generator-fingerprints.json"

# 数值比对相对容差。证据里的测量值记录精度约 4 位有效数字（如 0.2611、3.144285），
# 故用 1e-3：既容忍记录舍入，又能抓住 ≥0.1% 的真实漂移。int/bool/字符串一律精确比。
NUM_REL_TOL = 1e-3


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------
def import_quietly(name, tools_dir=TOOLS):
    """导入工具模块但不让它二次包裹 stdout（否则会 I/O closed）。"""
    if tools_dir not in sys.path:
        sys.path.insert(0, tools_dir)
    saved = sys.stdout
    sys.stdout = io.StringIO()          # 无 .buffer → 模块的包裹被 except 吞掉
    try:
        mod = importlib.import_module(name)
    finally:
        sys.stdout = saved
    return mod


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def f32(x):
    return struct.unpack("<f", struct.pack("<f", float(x)))[0]


def read_text(path):
    return open(path, "r", encoding="utf-8").read()


def deep_diff(a, b, path="$"):
    """逐字段（含嵌套）比对。只比**两边都有的键**（committed 里的作者文字字段天然跳过）。
    数值：int 精确比；float 用相对容差 NUM_REL_TOL。
    返回 [(path, committed=a, recomputed=b)]。"""
    out = []
    if isinstance(a, dict) and isinstance(b, dict):
        for k in sorted(set(a) & set(b)):
            out += deep_diff(a[k], b[k], f"{path}.{k}")
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            out.append((f"{path}[len]", len(a), len(b)))
        else:
            for i in range(len(a)):
                out += deep_diff(a[i], b[i], f"{path}[{i}]")
    else:
        if isinstance(a, bool) or isinstance(b, bool):
            if a != b:
                out.append((path, a, b))
        elif isinstance(a, (int, float)) and isinstance(b, (int, float)):
            if isinstance(a, int) and isinstance(b, int):
                if a != b:
                    out.append((path, a, b))
            elif abs(float(a) - float(b)) > NUM_REL_TOL * max(1.0, abs(float(a)), abs(float(b))):
                out.append((path, a, b))
        elif a != b:
            out.append((path, a, b))
    return out


class Finding:
    def __init__(self, guard, status, msg, detail=None):
        self.guard = guard
        self.status = status      # PASS | FAIL | SKIP
        self.msg = msg
        self.detail = detail or []

    def __str__(self):
        return f"[{self.status}] {self.guard}: {self.msg}"


# ---------------------------------------------------------------------------
# 生成物清单（分级）
# ---------------------------------------------------------------------------
FBX = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx"
TEX = "Assets/_Project/Art/Textures"

MANIFEST = [
    dict(path="Docs/Reports/d2t02a-baseline.json", klass="A",
         generator="Docs/Tools/verify_collision_data.py",
         reads=[f"{SRC}/table-metadata-reference.json", f"{SRC}/gameplay-markers-reference.json"],
         content="baseline_vs_source"),
    dict(path="Docs/Reports/d2t02b-crosscheck.json", klass="B",
         generator="Docs/Tools/_gen_crosscheck_json.py", reads=[],
         content="crosscheck_fields"),
    dict(path="Assets/_Project/Data/TableConfig.asset", klass="A",
         generator="Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs",
         reads=["Docs/Reference/table-metadata-reference.json"],
         content=None, stamp=("generator", "TableDataImporter.v1")),
    dict(path=f"{TEX}/table_metallic_roughness.png", klass="A",
         generator="Assets/_Project/Scripts/Editor/Import/TableTextureChannelPacker.cs",
         reads=[f"{TEX}/table_metallic.png", f"{TEX}/table_roughness.png"],
         content="packed_mask"),
    dict(path="Docs/Reports/d2t02b-mesh-audit.json", klass="A",
         generator="Assets/_Project/Tests/EditMode/D2T02B_IndependentAuditTests.cs",
         reads=["Assets/_Project/Data/TableConfig.asset", FBX], content=None),
    dict(path="Docs/Reports/day2-axis-probe.json", klass="A",
         generator="Assets/_Project/Scripts/Editor/TableImport/AxisProbe.cs",
         reads=[FBX], content=None),
    dict(path="Docs/Reports/day1-verification.json", klass="A",
         generator="Assets/_Project/Scripts/Editor/ProjectSetup/BilliardsProjectVerifier.cs",
         reads=["<Unity project>"], content=None),
    dict(path="Docs/Reports/day2-verification.json", klass="A",
         generator="Assets/_Project/Scripts/Editor/TableImport/TableIntegrityValidator.cs",
         reads=["<Unity project>"], content=None),
    dict(path="Docs/Reports/day3-verification.json", klass="A",
         generator="Assets/_Project/Scripts/Editor/Visual/Day3VisualSetup.cs",
         reads=["<Unity project>"], content=None),
    dict(path="Docs/Reports/day2-import.json", klass="A",
         generator="Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs",
         reads=["Docs/Reference/table-metadata-reference.json"], content=None),
    dict(path="Docs/Reports/d2t02b-editmode-tests.xml", klass="A",
         generator="<unity test runner>", reads=["<Unity test assemblies>"], content=None),
]


# ---------------------------------------------------------------------------
# 守卫
# ---------------------------------------------------------------------------
def guard_G1_B_content(root, mods, with_fbx):
    """B 类：重跑真检查器 + 全字段比对（不许抽样）。"""
    f = Finding("G1(B内容)", "PASS", "d2t02b-crosscheck.json 与重算结果一致")
    cpath = os.path.join(root, "Docs/Reports/d2t02b-crosscheck.json")
    committed = json.loads(read_text(cpath))
    rec = {}

    cross = mods["cross"]
    asset = os.path.join(root, "Assets/_Project/Data/TableConfig.asset")
    cfg = cross.parse_tableconfig(asset)
    baseline = json.loads(read_text(os.path.join(root, "Docs/Reports/d2t02a-baseline.json")))

    # chainOfCustody
    tmsha = sha256_file(f"{SRC}/table-metadata-reference.json")
    refsha = sha256_file(os.path.join(root, "Docs/Reference/table-metadata-reference.json"))
    asset_sha = [ln.split("sourceSha256:", 1)[1].strip() for ln in read_text(asset).splitlines() if ln.strip().startswith("sourceSha256:")][0]
    rec["chainOfCustody"] = {
        "F_origin_table_sha256": tmsha,
        "Docs_Reference_table_sha256": refsha,
        "asset_recorded_sourceSha256": asset_sha,
        "match": tmsha == refsha == asset_sha,
    }

    # anchorsActuallyObserved
    anchors = {}
    for rel in ["Assets/_Project/Scripts/Editor/TableImport/TableIntegrityValidator.cs",
                "Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs",
                "Assets/_Project/Scripts/Editor/Import/BilliardsAssetPostprocessor.cs",
                "Assets/_Project/Scripts/Runtime/Table/TableConfig.cs",
                "Assets/_Project/Scripts/Runtime/Core/PoolGeometry.cs",
                "Assets/_Project/Data/TableConfig.asset",
                "Assets/_Project/Tests/EditMode/TableConfigTests.cs",
                "Assets/_Project/Tests/EditMode/D2T02B_IndependentAuditTests.cs"]:
        p = os.path.join(root, rel)
        if os.path.exists(p):
            anchors[os.path.basename(rel)] = {"sha256_16": sha256_file(p)[:16], "bytes": os.path.getsize(p)}
    rec["anchorsActuallyObserved"] = anchors

    # check1 数据落地
    bl = baseline
    bs = {s["index"]: s for s in bl["segments"]}
    bp = {p["index"]: p for p in bl["pockets"]}
    raw_max = 0.0; loc = None; f32max = 0.0; f32mis = 0
    for k, s in enumerate(cfg["segments"]):
        b = bs[k]
        for e in ("a", "b"):
            for c in ("x", "y"):
                av = s[e][c]; bv = b[e][c]
                d = abs(av - bv)
                if d > raw_max: raw_max, loc = d, f"seg[{k}].{e}.{c}"
                f32max = max(f32max, abs(av - f32(bv)))
                if f32(av) != f32(bv): f32mis += 1
    rec["check1_dataLanding"] = {
        "parsedSegments": len(cfg["segments"]),
        "parsedPockets": len(cfg["pockets"]),
        "rawMaxDelta_m": raw_max,
        "rawMaxDeltaLocation": loc,
        "float32DomainMaxDelta_m": f32max,
        "pointsWhere_f32_asset_ne_f32_baseline": f32mis,
        "findingsAt1e-6Tolerance": 0 if f32max <= 1e-6 else 1,
    }

    # check2 chirality
    signs = cross._chirality(cfg)
    w = cross._winding(cfg)
    # gap
    groups = {}; order = []
    for s in cfg["segments"]:
        if s["rail"] not in groups: groups[s["rail"]] = []; order.append(s["rail"])
        groups[s["rail"]].append(s)
    chains = {r: [(ss[0]["a"]["x"], ss[0]["a"]["y"])] + [(x["b"]["x"], x["b"]["y"]) for x in ss] for r, ss in groups.items()}
    path = list(chains.pop(order[0]))
    import math
    while chains:
        tail = path[-1]; bestr = bestrev = None; bd = 1e18
        for r, c in chains.items():
            d1 = math.hypot(c[0][0]-tail[0], c[0][1]-tail[1]); d2 = math.hypot(c[-1][0]-tail[0], c[-1][1]-tail[1])
            if d1 < bd: bd, bestr, bestrev = d1, r, False
            if d2 < bd: bd, bestr, bestrev = d2, r, True
        c = chains.pop(bestr)
        if bestrev: c = c[::-1]
        path += c
    gap = math.hypot(path[-1][0]-path[0][0], path[-1][1]-path[0][1])
    rec["check2_chirality"] = {
        "CHR01_signPattern": signs, "CHR01_expected": [1,-1,-1,-1,1,1],
        "CHR01_pass": signs == [1,-1,-1,-1,1,1],
        "CHR02_shoelaceOver2": w, "CHR02_orientation": "CCW" if w > 0 else "CW",
        "CHR02_gapMm": round(gap*1000, 1), "CHR02_pass": w > 0,
    }

    # check4 tests（从 XML 重算）
    xp = os.path.join(root, "Docs/Reports/d2t02b-editmode-tests.xml")
    if os.path.exists(xp):
        r = ET.parse(xp).getroot()
        bd = {}
        for tc in r.iter("test-case"):
            bd[tc.get("fullname").split(".")[-2]] = bd.get(tc.get("fullname").split(".")[-2], 0) + 1
        rec["check4_tests_partial"] = {"total": int(r.get("total")), "passed": int(r.get("passed")),
                                       "failed": int(r.get("failed")), "breakdown": bd}

    # check7 textures
    png = mods["png"]
    pk = png.decode_png(os.path.join(root, TEX, "table_metallic_roughness.png"))
    mr = png.decode_png(os.path.join(root, TEX, "table_metallic.png"))
    rr = png.decode_png(os.path.join(root, TEX, "table_roughness.png"))
    pR, pG, pB, pA = png.channels_of(pk)
    mR = png.channels_of(mr)[0]; rR = png.channels_of(rr)[0]
    npx = min(len(pR), len(mR), len(rR))
    g_non = sum(1 for i in range(npx) if pG[i] != 0)
    b_non = sum(1 for i in range(npx) if pB[i] != 0)
    a_mis = sum(1 for i in range(npx) if abs(pA[i] - (255 - rR[i])) > 1)
    r_mis = sum(1 for i in range(npx) if abs(pR[i] - mR[i]) > 1)
    nl = png.decode_png(os.path.join(root, TEX, "table_normal.png"))
    ng = png.channels_of(nl)[1]
    rec["check7_textures"] = {
        "packedMask": {"R_eq_metallic_mismatchPixels": r_mis, "A_eq_255_minus_roughness_mismatchPixels": a_mis,
                       "G_nonzero": g_non, "B_nonzero": b_non},
        "normalGreenMean": round(sum(ng)/len(ng), 2),
        "packedSize": f"{pk['w']}x{pk['h']}",
    }

    # check8 DAT09/10
    xs = [c for s in cfg["segments"] for c in (s["a"]["x"], s["b"]["x"])]
    ys = [c for s in cfg["segments"] for c in (s["a"]["y"], s["b"]["y"])]
    tm = json.loads(read_text(f"{SRC}/table-metadata-reference.json"))
    d10 = 0.0
    for p in tm["collision"]["pockets"]:
        d10 = max(d10, abs(p["openingRadius"] - p["runtimeInscribedRadius"]))
    rec["check8_dat09_dat10"] = {
        "DAT09_bbox": f"x[{min(xs):.9f},{max(xs):.9f}] y[{min(ys):.9f},{max(ys):.9f}]",
        "DAT09_x_asym_m": abs(min(xs) + max(xs)),
        "DAT10_opening_eq_inscribed_maxDelta": d10,
    }

    # 对每个"可重算组"做全字段比对（只比两边都有的键；作者文字字段天然跳过）
    allmis = []
    for ck in ["chainOfCustody", "check1_dataLanding",
               "check2_chirality", "check7_textures", "check8_dat09_dat10"]:
        cm = committed.get(ck)
        rc = rec.get(ck)
        if cm is None or rc is None:
            continue
        cm_sub = {k: cm[k] for k in cm if k in rc}
        for (p, a, b) in deep_diff(cm_sub, {k: rc[k] for k in cm_sub}):
            allmis.append((f"{ck}{p.lstrip('$')}", a, b))
    # check4（键名不同，单独比）
    if "check4_tests" in committed and "check4_tests_partial" in rec:
        c4 = committed["check4_tests"]; r4 = rec["check4_tests_partial"]
        if {k: c4.get(k) for k in ("total", "passed", "failed")} != {k: r4[k] for k in ("total", "passed", "failed")}:
            allmis.append(("check4_tests(总数)", {k: c4.get(k) for k in ("total","passed","failed")},
                           {k: r4[k] for k in ("total","passed","failed")}))
        if c4.get("breakdown") != r4["breakdown"]:
            allmis.append(("check4_tests.breakdown", c4.get("breakdown"), r4["breakdown"]))

    if allmis:
        f.status = "FAIL"
        f.msg = f"B 类内容重算发现 {len(allmis)} 处不符"
        f.detail = [f"{p}: 提交={a!r} 重算={b!r}" for (p, a, b) in allmis]
    f.recomputed = rec
    f.committed = committed
    return f


def guard_G1b_B_fbx(root, mods, committed):
    """B 类中依赖 FBX 的字段（check5 / check9 / check10）—— 复用真检查器 fbx_mesh_probe.recompute_metrics。"""
    f = Finding("G1b(B-FBX内容)", "PASS", "check5/9/10 与 FBX 重算一致")
    fbx = mods["fbx"]
    cross = mods["cross"]
    cfg = cross.parse_tableconfig(os.path.join(root, "Assets/_Project/Data/TableConfig.asset"))
    endpoints = []
    for s in cfg["segments"]:
        endpoints += [(s["a"]["x"], s["a"]["y"]), (s["b"]["x"], s["b"]["y"])]
    for p in cfg["pockets"]:
        for g in ("mouthA", "mouthB", "throatA", "throatB"):
            endpoints.append((p[g]["x"], p[g]["y"]))
    m = fbx.recompute_metrics(endpoints)
    mis = []
    c5 = committed.get("check5_axisTransform", {}).get("independent48Enumeration", {})
    c9 = committed.get("check9_axlFalsifiableTest", {})
    c10 = committed.get("check10_fbxDirectParse", {})
    pairs = [
        ("check5.bestPermutation", c5.get("bestPermutation"), m["bestPermutation"]),
        ("check5.bestWorstErrorMm", c5.get("bestWorstErrorMm"), m["bestWorstErrorMm"]),
        ("check5.secondPermutation", c5.get("secondPermutation"), m["secondPermutation"]),
        ("check5.secondPermutationWorstMm", c5.get("secondPermutationWorstMm"), m["secondPermutationWorstMm"]),
        ("check5.chosenWorstMm", c5.get("chosenTransform_(-m.x,m.z,m.y)_WorstMm"), m["chosenWorstMm"]),
        ("check9.vertexWorstMm", c9.get("endpointToNearestVertex_worst_mm"), m["endpointToNearestVertex_worst_mm"]),
        ("check9.surfaceWorstMm", c9.get("endpointToNearestSurfacePoint_worst_mm"), m["endpointToNearestSurfacePoint_worst_mm"]),
        ("check10.version", c10.get("version"), m["version"]),
        ("check10.vertexCount", c10.get("vertexCount"), m["vertexCount"]),
    ]
    for (name, a, b) in pairs:
        if a is None:
            continue
        d = deep_diff({"v": a}, {"v": b})
        if d:
            mis.append((name, a, b))
    if mis:
        f.status = "FAIL"
        f.msg = f"FBX 派生字段不符 {len(mis)} 处"
        f.detail = [f"{p}: 提交={a!r} 重算={b!r}" for (p, a, b) in mis]
    else:
        f.msg = (f"check5/9/10 与 FBX 重算一致（best={m['bestPermutation']}@{m['bestWorstErrorMm']:.4f}mm, "
                 f"surface={m['endpointToNearestSurfacePoint_worst_mm']:.6f}mm）")
    return f


def load_ledger(root):
    p = os.path.join(root, LEDGER_REL)
    return json.loads(read_text(p)) if os.path.exists(p) else None


def guard_G2_A_sourcefpr(root):
    """A 类源指纹：资产记录的源 sha 必须等于源文件实际 sha。"""
    findings = []
    asset = os.path.join(root, "Assets/_Project/Data/TableConfig.asset")
    txt = read_text(asset)
    m = re.search(r"sourceSha256:\s*([0-9a-f]{64})", txt)
    rec_sha = m.group(1) if m else None
    ref = sha256_file(os.path.join(root, "Docs/Reference/table-metadata-reference.json"))
    org = sha256_file(f"{SRC}/table-metadata-reference.json")
    ok = rec_sha == ref == org
    f = Finding("G2(A源指纹)", "PASS" if ok else "FAIL",
                "TableConfig.asset sourceSha256 == 入库副本 == F: 原始" if ok
                else "源指纹不一致")
    if not ok:
        f.detail = [f"asset记录={rec_sha}", f"入库副本={ref}", f"F:原始={org}"]
    findings.append(f)
    return findings


def guard_G3_A_logicfpr(root):
    """A 类逻辑指纹：生成器源码 sha 必须等于台账记录；戳变/未变分别报。"""
    findings = []
    led = load_ledger(root)
    if led is None:
        return [Finding("G3(A逻辑指纹)", "SKIP", "无台账（先 --write-ledger）")]
    for a in MANIFEST:
        if a["klass"] != "A":
            continue
        gen = a["generator"]
        gp = os.path.join(root, gen)
        if not os.path.exists(gp):
            continue
        cur = sha256_file(gp)
        entry = led.get("generators", {}).get(gen)
        if entry is None:
            continue
        if cur != entry["sha256"]:
            f = Finding("G3(A逻辑指纹)", "FAIL",
                        f"生成器逻辑已变：{gen}（台账 {entry['sha256'][:16]} → 现 {cur[:16]}）")
            f.detail = [f"受影响生成物：{a['path']}"]
            # 若带戳且戳未变 → 正是"逻辑变、戳未变"窗口
            if "stamp" in a:
                field, expect = a["stamp"]
                mm = re.search(rf"{field}:\s*(\S+)", read_text(os.path.join(root, a["path"])))
                got = mm.group(1) if mm else None
                if got == entry.get("stamp"):
                    f.msg += "；且版本戳未变 ⇒ 资产会静默停留旧版（正是目标窗口）"
            findings.append(f)
    if not any(f.status == "FAIL" for f in findings):
        findings.append(Finding("G3(A逻辑指纹)", "PASS", "所有 A 类生成器逻辑指纹与台账一致"))
    return findings


def guard_G4_A_content(root, mods):
    """A 类内容重算（file-only 可得者）：baseline、packed mask。"""
    findings = []
    # baseline
    vc = mods["vc"]
    tm = json.loads(read_text(f"{SRC}/table-metadata-reference.json"))
    recomputed = vc.build_baseline(tm)
    committed = json.loads(read_text(os.path.join(root, "Docs/Reports/d2t02a-baseline.json")))
    # 只比内容段（sources.sha 依赖模块常量，仍比）
    mis = deep_diff(committed, recomputed)
    findings.append(Finding("G4a(A内容-baseline)", "PASS" if not mis else "FAIL",
                            "baseline == 从源重算" if not mis else f"baseline 与重算不符 {len(mis)} 处",
                            [f"{p}: 提交={a!r} 重算={b!r}" for (p, a, b) in mis[:40]]))
    # packed mask
    png = mods["png"]
    pk = png.decode_png(os.path.join(root, TEX, "table_metallic_roughness.png"))
    mr = png.decode_png(os.path.join(root, TEX, "table_metallic.png"))
    rr = png.decode_png(os.path.join(root, TEX, "table_roughness.png"))
    pR, pG, pB, pA = png.channels_of(pk)
    mR = png.channels_of(mr)[0]; rR = png.channels_of(rr)[0]
    n = min(len(pR), len(mR), len(rR))
    bad = sum(1 for i in range(n) if abs(pR[i]-mR[i]) > 1 or abs(pA[i]-(255-rR[i])) > 1 or pG[i] or pB[i])
    findings.append(Finding("G4b(A内容-packedmask)", "PASS" if bad == 0 else "FAIL",
                            "packed == R:metallic/A:1-roughness/G=B=0（逐像素）" if bad == 0
                            else f"packed mask 有 {bad} 像素不符"))
    return findings


# ---------------------------------------------------------------------------
# 覆盖报告 / 盲区
# ---------------------------------------------------------------------------
BLIND_SPOTS = {
    "G1(B内容) 未覆盖字段": [
        "verdict / concerns[].text / 各 *_interpretation / *_note —— 属作者结论文字，非机器可重算",
        "check3_reverseCases 的逐条 verdict/fired —— 需重跑注入harness；本轮未纳入（见 §漏报场景）",
        "check5_axisTransform 的 48 候选数值、check9 AXL 两度量、check10 FBX —— 依赖 FBX 重算，需 --with-fbx",
        "check6_fieldCoverage 的定性结论（rail 保留等）—— 部分可机检（rail=82），其余为分析文字",
    ],
    "G2/G3(A源/逻辑指纹) 未覆盖": [
        "只能证明『生成器源码未变』，不能证明『生成逻辑语义正确』（改注释也会使指纹变 → 假阳性）",
        "台账本身若被误更新，守卫随之失效（台账需人工审查后更新）",
    ],
    "G4(A内容) 未覆盖": [
        "TableConfig.asset 的内容正确性需 Unity 重生成 → 本轮只做文件层（源sha+逻辑指纹），内容重算留 hook",
        "day1/2/3-verification.json、*-tests.xml、day2-axis-probe.json 等内容依赖 Unity 运行 → 未重算",
        "PNG 重算仅覆盖 packed mask；normal/basecolor/net 的『内容正确性』未重算",
    ],
}


def guard_G5_evidence_snapshot(root):
    """证据内嵌指纹 vs 现行仓库。语义：证据是『时刻 T 的快照』，允许随仓库演进变化；
    但一旦变化，必须刷新或注明——否则证据的溯源信息已不再描述当前仓库。"""
    f = Finding("G5(证据快照vs现行)", "PASS", "证据内嵌指纹与现行仓库一致")
    cp = os.path.join(root, "Docs/Reports/d2t02b-crosscheck.json")
    if not os.path.exists(cp):
        return Finding("G5(证据快照vs现行)", "SKIP", "无 crosscheck.json")
    committed = json.loads(read_text(cp))
    anc = committed.get("anchorsActuallyObserved", {})
    miss = []
    for fn, rec in anc.items():
        if not isinstance(rec, dict):
            continue
        # 定位实际文件
        cand = None
        for dp, _, fns in os.walk(os.path.join(root, "Assets")):
            if fn in fns:
                cand = os.path.join(dp, fn); break
        if cand is None:
            miss.append((fn, "记录于证据", "现行缺失")); continue
        cur16 = sha256_file(cand)[:16]; cursz = os.path.getsize(cand)
        if cur16 != rec.get("sha256_16") or cursz != rec.get("bytes"):
            miss.append((fn, f"{rec.get('sha256_16')}/{rec.get('bytes')}", f"{cur16}/{cursz}"))
    if miss:
        f.status = "FAIL"
        f.msg = f"证据内嵌指纹已与现行仓库不符 {len(miss)} 处（上游变动 ⇒ 快照过期，需刷新或注明）"
        f.detail = [f"{a}: 证据={b} 现行={c}" for (a, b, c) in miss]
    return f


def run_all(root, with_fbx=False, quiet=False):
    mods = {"cross": import_quietly("d2t02b_crosscheck"),
            "vc": import_quietly("verify_collision_data"),
            "png": import_quietly("png_probe"),
            "fbx": import_quietly("fbx_mesh_probe")}
    findings = []
    findings.append(guard_G1_B_content(root, mods, with_fbx))
    if with_fbx:
        findings.append(guard_G1b_B_fbx(root, mods, findings[0].committed))
    else:
        findings.append(Finding("G1b(B-FBX内容)", "SKIP", "未启用 --with-fbx：check5/9/10 未比对（盲区）"))
    findings += guard_G2_A_sourcefpr(root)
    findings += guard_G3_A_logicfpr(root)
    findings += guard_G4_A_content(root, mods)
    findings.append(guard_G5_evidence_snapshot(root))
    return findings, mods


def print_report(findings):
    print("=" * 78)
    print("D2-T04 生成物一致性守卫")
    print("=" * 78)
    npass = sum(1 for f in findings if f.status == "PASS")
    nfail = sum(1 for f in findings if f.status == "FAIL")
    nskip = sum(1 for f in findings if f.status == "SKIP")
    for f in findings:
        print(str(f))
        for d in f.detail[:60]:
            print("      " + str(d))
    print(f"\n小结：PASS={npass} FAIL={nfail} SKIP={nskip}")


# ---------------------------------------------------------------------------
# 反向用例自证非空转
# ---------------------------------------------------------------------------
def self_test():
    print("=" * 78)
    print("D2-T04 反向用例（每条守卫必须能 FAIL）")
    print("=" * 78)
    tmp = tempfile.mkdtemp(prefix="d2t04_")
    # 造一个最小镜像：复制被守卫读取的构件（含证据内嵌指纹所指向的文件）
    for rel in ["Docs/Reports/d2t02b-crosscheck.json", "Docs/Reports/d2t02a-baseline.json",
                "Docs/Reports/d2t02b-editmode-tests.xml",
                "Assets/_Project/Data/TableConfig.asset",
                f"{TEX}/table_metallic_roughness.png", f"{TEX}/table_metallic.png",
                f"{TEX}/table_roughness.png", f"{TEX}/table_normal.png",
                "Docs/Reference/table-metadata-reference.json", LEDGER_REL,
                "Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs",
                "Assets/_Project/Scripts/Editor/Import/TableTextureChannelPacker.cs",
                "Assets/_Project/Scripts/Editor/TableImport/TableIntegrityValidator.cs",
                "Assets/_Project/Scripts/Editor/Import/BilliardsAssetPostprocessor.cs",
                "Assets/_Project/Scripts/Runtime/Table/TableConfig.cs",
                "Assets/_Project/Scripts/Runtime/Core/PoolGeometry.cs",
                "Assets/_Project/Tests/EditMode/TableConfigTests.cs",
                "Assets/_Project/Tests/EditMode/D2T02B_IndependentAuditTests.cs"]:
            src = os.path.join(REPO, rel)
            if not os.path.exists(src):
                continue
            dst = os.path.join(tmp, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(src, dst)

    results = []

    # 基线：未篡改时的 FAIL 集合（当前仓库可能本就因上游变动而 FAIL，属正常）。
    base_f, _ = run_all(tmp)
    base_fails = sorted(x.guard for x in base_f if x.status == "FAIL")
    print(f"（基线：未篡改时 FAIL 集合 = {base_fails}；阴性对照将与之比较）")

    def run_case(name, tamper, restore_rel, pick):
        """立即执行：篡改 → run_all → 取守卫 → 复原。"""
        tamper()
        f, _ = run_all(tmp)
        g = next(x for x in f if x.guard.startswith(pick))
        results.append((name, g.status, ([g.msg] + g.detail)[:4]))
        if restore_rel:
            shutil.copy2(os.path.join(REPO, restore_rel), os.path.join(tmp, restore_rel))

    CJ = "Docs/Reports/d2t02b-crosscheck.json"
    AS = "Assets/_Project/Data/TableConfig.asset"
    GEN = "Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs"
    BL = "Docs/Reports/d2t02a-baseline.json"

    def t_b1():
        p = os.path.join(tmp, CJ)
        d = json.loads(read_text(p)); d["check1_dataLanding"]["parsedSegments"] = 81
        open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(d, ensure_ascii=False, indent=1))

    def t_a1():
        p = os.path.join(tmp, AS)
        t = read_text(p).replace("sourceSha256: ba60fc204c155881127b500bcce2f388066151757984659f89e681c2ef364398",
                                 "sourceSha256: " + "0" * 64)
        open(p, "w", encoding="utf-8", newline="\n").write(t)

    def t_a2():
        p = os.path.join(tmp, GEN)
        open(p, "a", encoding="utf-8").write("\n// tamper: logic changed, stamp unchanged\n")

    def t_a3():
        p = os.path.join(tmp, BL)
        d = json.loads(read_text(p)); d["table"]["ball_radius"] = 0.03
        open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(d, ensure_ascii=False, indent=1))

    run_case("RC-B1 篡改 json 字段 parsedSegments 82→81", t_b1, CJ, "G1(B内容)")
    run_case("RC-A1 伪造 TableConfig.asset 源 sha", t_a1, AS, "G2(")
    run_case("RC-A2 改生成器源码(逻辑变/戳不变)", t_a2, GEN, "G3(")
    run_case("RC-A3 篡改 baseline 字段 ball_radius", t_a3, BL, "G4a")

    # 阴性对照：与"基线 FAIL 集合"一致 ⇒ 篡改全部被复原，无残留、无假阳性
    f, _ = run_all(tmp)
    cur_fails = sorted(x.guard for x in f if x.status == "FAIL")
    results.append(("CLEAN 未篡改（FAIL 集合应 == 基线）", "PASS" if cur_fails == base_fails else "FAIL",
                    [f"基线={base_fails}", f"现={cur_fails}"]))

    print("\n| 反向用例 | 观察到的状态 | 明细 |")
    print("| --- | --- | --- |")
    ok = True
    for name, st, det in results:
        print(f"| {name} | {st} | {det} |")
        ok &= (st == "PASS") if name.startswith("CLEAN") else (st == "FAIL")
    print(f"\n自证{'通过' if ok else '未通过'}：每条守卫在注入漂移时都观察到了 FAIL，未篡改时 PASS。")
    shutil.rmtree(tmp, ignore_errors=True)
    return 0 if ok else 1


def write_ledger():
    import datetime
    gens = {}
    for a in MANIFEST:
        if a["klass"] != "A":
            continue
        gp = os.path.join(REPO, a["generator"])
        if not os.path.exists(gp):
            continue
        stamp = None
        if "stamp" in a:
            field, _ = a["stamp"]
            mm = re.search(rf"{field}:\s*(\S+)", read_text(os.path.join(REPO, a["path"])))
            stamp = mm.group(1) if mm else None
        gens[a["generator"]] = {"sha256": sha256_file(gp), "bytes": os.path.getsize(gp),
                                "stamp": stamp, "verifiedWith": a["path"]}
    led = {"schema": "idea35.generator-fingerprints.v1",
           "recordedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
           "note": "生成器逻辑指纹台账：生成器源码 sha 变化即视为『逻辑漂移』，需人工复核后刷新本台账。",
           "generators": gens}
    p = os.path.join(REPO, LEDGER_REL)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(led, ensure_ascii=False, indent=1))
    print("台账已写入：" + p)
    for k, v in gens.items():
        print(f"  {v['sha256'][:16]}  {k}")


def emit_json(findings, path, with_fbx):
    import datetime
    payload = {
        "schema": "idea35.generated-artifact-guard.v1",
        "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "withFbx": with_fbx,
        "summary": {
            "pass": sum(1 for f in findings if f.status == "PASS"),
            "fail": sum(1 for f in findings if f.status == "FAIL"),
            "skip": sum(1 for f in findings if f.status == "SKIP"),
        },
        "findings": [{"guard": f.guard, "status": f.status, "message": f.msg, "detail": f.detail}
                     for f in findings],
        "blindSpots": BLIND_SPOTS,
    }
    d = os.path.dirname(path)
    if d:
        os.makedirs(d, exist_ok=True)
    open(path, "w", encoding="utf-8", newline="\n").write(json.dumps(payload, ensure_ascii=False, indent=1))
    print(f"JSON 证据已写入：{path}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--with-fbx", action="store_true")
    ap.add_argument("--self-test", action="store_true")
    ap.add_argument("--write-ledger", action="store_true")
    ap.add_argument("--root", default=REPO)
    ap.add_argument("--json", default=None, help="把机读结果写到该路径（供现有校验器框架挂接）")
    args = ap.parse_args()
    if args.write_ledger:
        write_ledger(); return 0
    if args.self_test:
        return self_test()
    findings, _ = run_all(args.root, with_fbx=args.with_fbx)
    print_report(findings)
    if args.json:
        emit_json(findings, args.json, args.with_fbx)
    return 1 if any(f.status == "FAIL" for f in findings) else 0


if __name__ == "__main__":
    sys.exit(main())
