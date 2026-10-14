#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
check_generated_artifacts.py -- 生成物一致性守卫
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


# ---------------------------------------------------------------------------
# 语义指纹（G3 用）
# ---------------------------------------------------------------------------
# 为什么不用「源码 sha」：本项目已多次发生「只改注释 / 只改文案」的合法改动，
# 纯源码哈希会**持续假红** ⇒ 假红多了就被无视 ⇒ 橡皮图章。
# 语义指纹 = **去掉注释 + 去掉字符串字面量** 后的规范化源码哈希：
#   · 改注释        ⇒ 指纹不变（不红）
#   · 改字符串字面量 ⇒ 指纹不变（导出文案类改动不红）
#   · 改逻辑/数值字面量 ⇒ 指纹改变（红）
# 注：`$"...{expr}..."` 插值字符串的 `{expr}` 会被当作字符串内容一并去掉，
#     这是**已知的假阴性**（改插值表达式不红）；本轮接受，登记为盲区。

def _strip_c_like(text):
    """去掉 C# 风格注释与字符串字面量（保留字面量占位，保留其余字符）。"""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":              # 行注释（含 ///）
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and nxt == "*":              # 块注释
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == "@" and nxt == "\"":             # verbatim 字符串 @"..."
            out.append("@\"\"")
            i += 2
            while i < n:
                if text[i] == "\"":
                    if i + 1 < n and text[i + 1] == "\"":
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            continue
        if c == "\"" or c == "'":                # 普通字符串 / 字符字面量
            quote = c
            out.append("\"\"" if quote == "\"" else "''")
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == quote:
                    i += 1
                    break
                i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def _strip_python(text):
    """去掉 Python 注释与字符串字面量（含三引号）。"""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        trip = text[i:i + 3]
        if c == "#":                             # 行注释
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if trip in ('"""', "'''"):               # 三引号字符串
            j = text.find(trip, i + 3)
            out.append("\"\"\"\"\"\"")
            i = n if j < 0 else j + 3
            continue
        if c == "\"" or c == "'":                # 普通字符串
            quote = c
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == quote:
                    break
                j += 1
            out.append("\"\"" if quote == "\"" else "''")
            i = j + 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def semantic_fingerprint(path):
    """返回「去注释 + 去字符串字面量 + 去空行/行首尾空白」后的 sha256。"""
    text = read_text(path)
    stripped = _strip_python(text) if path.endswith(".py") else _strip_c_like(text)
    stripped = stripped.replace("\r\n", "\n").replace("\r", "\n")
    norm = "\n".join(ln.strip() for ln in stripped.split("\n") if ln.strip())
    return hashlib.sha256(norm.encode("utf-8")).hexdigest()


def is_placeholder_generator(gen):
    """形如 `<unity test runner>` 的占位生成器：非文件，无法做逻辑指纹。"""
    return gen.startswith("<") and gen.endswith(">")


def manifest_generators(entry):
    """取一条清单的生成器集合（兼容旧的单个 `generator` 字段）。"""
    if "generators" in entry:
        return list(entry["generators"])
    return [entry["generator"]]


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

# 生成器集合（缺陷 1：治「盲点」）。
# 一个产物的内容可能由**多个文件**共同决定；只登记「主入口」会留下盲点——
# 例如 day1-verification.json 的 RUN-01 文案来自 BilliardsProjectSetup.cs（不在旧清单里），
# 于是改了它守卫也不会红。故 A 类产物用 `generators` 列出**所有能改变其内容的文件**。
#
# coverage 的含义：
#   complete  —— 已逐文件读过生产路径，认为集合完备
#   partial   —— 已补上能确认的文件，但仍存在**未核完**的间接依赖（见 COVERAGE_NOTES）
MANIFEST = [
    dict(path="Docs/Reports/d2t02a-baseline.json", klass="A",
         generators=["Docs/Tools/verify_collision_data.py"],
         reads=[f"{SRC}/table-metadata-reference.json", f"{SRC}/gameplay-markers-reference.json"],
         content="baseline_vs_source", coverage="complete"),
    dict(path="Docs/Reports/d2t02b-crosscheck.json", klass="B",
         generator="Docs/Tools/_gen_crosscheck_json.py", reads=[],
         content="crosscheck_fields"),
    dict(path="Assets/_Project/Data/TableConfig.asset", klass="A",
         generators=["Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs"],
         reads=["Docs/Reference/table-metadata-reference.json"],
         content=None, stamp=("generator", "TableDataImporter.v1"), coverage="partial"),
    dict(path=f"{TEX}/table_metallic_roughness.png", klass="A",
         generators=["Assets/_Project/Scripts/Editor/Import/TableTextureChannelPacker.cs"],
         reads=[f"{TEX}/table_metallic.png", f"{TEX}/table_roughness.png"],
         content="packed_mask", coverage="complete"),
    dict(path="Docs/Reports/d2t02b-mesh-audit.json", klass="A",
         generators=["Assets/_Project/Tests/EditMode/MeshAlignmentAuditTests.cs"],
         reads=["Assets/_Project/Data/TableConfig.asset", FBX], content=None, coverage="partial"),
    dict(path="Docs/Reports/day2-axis-probe.json", klass="A",
         generators=["Assets/_Project/Scripts/Editor/TableImport/AxisProbe.cs"],
         reads=[FBX], content=None, coverage="complete"),
    # day1 的生成器集合：所有能改变其内容的文件（本轮逐文件核对后补齐）。
    # 旧清单只列了 BilliardsProjectVerifier.cs —— 那正是「盲点」：RUN-01 文案、产品名、
    # 必需动作表、模板残留清单来自 BilliardsProjectSetup.cs；schema/序列化来自
    # ProjectSetupReport.cs；LYR-01/02 期望值来自 BilliardsLayers.cs；SCN-04 计数来自
    # PhysicsBanAudit.cs；ASM-04 实测值来自 PoolConstants.cs。
    dict(path="Docs/Reports/day1-verification.json", klass="A",
         generators=["Assets/_Project/Scripts/Editor/ProjectSetup/BilliardsProjectVerifier.cs",
                     "Assets/_Project/Scripts/Editor/ProjectSetup/BilliardsProjectSetup.cs",
                     "Assets/_Project/Scripts/Editor/ProjectSetup/ProjectSetupReport.cs",
                     "Assets/_Project/Scripts/Editor/ProjectSetup/BilliardsLayers.cs",
                     "Assets/_Project/Scripts/Diagnostics/PhysicsBanAudit.cs",
                     "Assets/_Project/Scripts/Runtime/Core/PoolConstants.cs"],
         reads=["<Unity project>"], content=None, coverage="complete"),
    dict(path="Docs/Reports/day2-verification.json", klass="A",
         generators=["Assets/_Project/Scripts/Editor/TableImport/TableIntegrityValidator.cs",
                     "Assets/_Project/Scripts/Editor/Import/BilliardsAssetPostprocessor.cs"],
         reads=["<Unity project>"], content=None, coverage="partial"),
    # day3：**写者其实是 MaterialVisualValidator.cs**，旧清单误记为 MaterialVisualSetup.cs
    # （后者只构建材质/场景/Prefab，不写报告；但它改变被校验对象 ⇒ 仍影响内容）。两个都收。
    dict(path="Docs/Reports/day3-verification.json", klass="A",
         generators=["Assets/_Project/Scripts/Editor/Visual/MaterialVisualValidator.cs",
                     "Assets/_Project/Scripts/Editor/Visual/MaterialVisualSetup.cs"],
         reads=["<Unity project>"], content=None, coverage="partial"),
    dict(path="Docs/Reports/day2-import.json", klass="A",
         generators=["Assets/_Project/Scripts/Editor/TableImport/TableDataImporter.cs"],
         reads=["Docs/Reference/table-metadata-reference.json"], content=None, coverage="complete"),
    dict(path="Docs/Reports/d2t02b-editmode-tests.xml", klass="A",
         generator="<unity test runner>", reads=["<Unity test assemblies>"], content=None),
]

# 生成器集合的**未核完声明**（缺陷 1 的诚实边界）。
COVERAGE_NOTES = {
    "Assets/_Project/Data/TableConfig.asset":
        "未核完：TableConfig.cs（Runtime 序列化契约）未纳入集合——它改变字段集会使资产过期，本轮未做自证。",
    "Docs/Reports/d2t02b-mesh-audit.json":
        "未核完：断言口径与 TableIntegrityValidator 共享 MeshAlignmentMetrics；共享实现变动本轮未覆盖。",
    "Docs/Reports/day2-verification.json":
        "未核完：AXL 三层度量经 PoolGeometry.ModelVertexToUnity；PoolGeometry.cs 变动本轮未纳入（其内容重算留 hook）。",
    "Docs/Reports/day3-verification.json":
        "未核完：校验读回的材质/场景由 BilliardsAssetPostprocessor.cs 与 PoolGeometry.cs 共同影响，本轮未纳入。",
    "Docs/Reports/day1-verification.json":
        "已核：6 个生成器逐文件读过生产路径。未纳入 ReportOutput.cs（只决定落盘位置、不决定内容）。",
}


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
                "Assets/_Project/Tests/EditMode/MeshAlignmentAuditTests.cs"]:
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
    """A 类生成器【集合】的语义指纹守卫。

    三层检查（任一命中即 FAIL）：
      ① 覆盖完备性：MANIFEST 声明的生成器集合 必须 == 台账记录的集合。
         —— 这是「缺陷 1（盲点）」的牙：从集合里**删掉一项**也会红。
      ② key 可解析：生成器文件必须存在、且台账里**必须有它的 key**。
         —— 这是「缺陷 2（静默跳过）」的牙：取不到 key 不再 `continue`，而是 FAIL。
      ③ 语义漂移：生成器**语义指纹**（去注释+去字符串字面量）必须等于台账记录。
    """
    findings = []
    led = load_ledger(root)
    if led is None:
        return [Finding("G3(A逻辑指纹)", "SKIP", "无台账（先 --write-ledger）")]
    led_gens = led.get("generators", {})
    coverage = led.get("artifactGenerators", {})
    checked = set()
    for a in MANIFEST:
        if a["klass"] != "A":
            continue
        artifact = a["path"]
        gens = manifest_generators(a)
        real = [g for g in gens if not is_placeholder_generator(g)]
        if not real:
            findings.append(Finding("G3(A逻辑指纹)", "SKIP",
                                    f"{artifact}：生成器为占位符（{gens[0]}），无法做逻辑指纹——已登记盲区"))
            continue

        # ① 覆盖完备性
        man_set = set(gens)
        led_set = set(coverage.get(artifact, []))
        if man_set != led_set:
            f = Finding("G3(A逻辑指纹)", "FAIL",
                        f"生成器集合与台账不一致（生成器集合不完备 / 被削）：{artifact}")
            f.detail = [f"清单独有（台账未覆盖）：{sorted(man_set - led_set)}",
                        f"台账独有（清单已删项）：{sorted(led_set - man_set)}",
                        "⇒ 生成物处于「部分无守卫」状态（缺陷 1）。"]
            findings.append(f)

        # ② key 可解析 + ③ 语义漂移
        for gen in gens:
            if is_placeholder_generator(gen):
                continue
            gp = os.path.join(root, gen)
            if not os.path.exists(gp):
                f = Finding("G3(A逻辑指纹)", "FAIL", f"生成器文件缺失（key 无法解析）：{gen}")
                f.detail = [f"受影响生成物：{artifact}"]
                findings.append(f)
                continue
            entry = led_gens.get(gen)
            if entry is None:
                f = Finding("G3(A逻辑指纹)", "FAIL", f"生成器在台账中无记录（key 缺失）：{gen}")
                f.detail = [f"受影响生成物：{artifact}",
                            "台账无该 key ⇒ 无基线可比较（缺陷 2：旧实现会静默 continue，永不检查）。"]
                findings.append(f)
                continue
            base = entry.get("semantic_sha256") or entry.get("sha256")
            cur = semantic_fingerprint(gp)
            checked.add(gen)
            if cur != base:
                f = Finding("G3(A逻辑指纹)", "FAIL",
                            f"生成器语义已变：{gen}（台账 {base[:16]} → 现 {cur[:16]}）")
                f.detail = [f"受影响生成物：{artifact}",
                            "语义指纹 = 去注释 + 去字符串字面量；改注释/改文案不在此列。"]
                if "stamp" in a:
                    field, _ = a["stamp"]
                    mm = re.search(rf"{field}:\s*(\S+)", read_text(os.path.join(root, artifact)))
                    got = mm.group(1) if mm else None
                    if got == entry.get("stamp"):
                        f.msg += "；且版本戳未变 ⇒ 资产会静默停留旧版（正是目标窗口）"
                findings.append(f)

    if not any(f.status == "FAIL" for f in findings):
        findings.append(Finding("G3(A逻辑指纹)", "PASS",
                                f"所有 A 类生成器集合完备且语义指纹与台账一致（去注释+去字符串字面量；共 {len(checked)} 个生成器）"))
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
        "G3 的语义指纹 = 去注释 + 去字符串字面量 + 规范化空白；仍**不能**证明『生成逻辑语义正确』",
        "假阴性：`$\"...{expr}...\"` 插值字符串里的表达式会被整段当作字符串去掉 ⇒ 改插值表达式不红",
        "假阴性：改『非注释、非字面量』的重命名（局部变量名等）会使指纹变红（对语义无影响的假阳性，但属安全侧）",
        "台账本身若被误更新（含 artifactGenerators 被削），守卫随之失效（台账需人工审查后更新）",
    ],
    "G4(A内容) 未覆盖": [
        "TableConfig.asset 的内容正确性需 Unity 重生成 → 本轮只做文件层（源sha+逻辑指纹），内容重算留 hook",
        "day1/2/3-verification.json、*-tests.xml、day2-axis-probe.json 等内容依赖 Unity 运行 → 未重算",
        "PNG 重算仅覆盖 packed mask；normal/basecolor/net 的『内容正确性』未重算",
    ],
}


def guard_G5_evidence_snapshot(root):
    """证据内嵌指纹 vs 现行仓库。语义：证据是『时刻 T 的快照』，允许随仓库演进变化。

    ★ 本守卫对「现行仓库」的敏感度是**恒红**的：`anchorsActuallyObserved` 是某时刻的快照，
    任何被锚文件（含纯注释改动）变动它就不同 ⇒ 在持续演进的仓库里**永远红**。
    而永远红 = 被无视 = 橡皮图章。故 **降级为 WARN**（不计入 FAIL、不挂闸门）。

    降级后仍要求：刷新证据时记录**刷新时刻 + 对应提交**（见 `Docs/Reports/d2t02b-crosscheck.json`
    的 `anchorsActuallyObserved` 与刷新记录）。本守卫只负责**提醒**「快照已过期，需刷新或注明」。
    """
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
        # WARN（非 FAIL）：快照过期是**常态**，只提示、不阻断。见函数 docstring 的降级理由。
        f.status = "WARN"
        f.msg = (f"证据内嵌指纹已与现行仓库不符 {len(miss)} 处"
                 f"（上游变动 ⇒ 快照过期，需刷新或注明；WARN 不计入 FAIL）")
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
    print("生成物一致性守卫")
    print("=" * 78)
    npass = sum(1 for f in findings if f.status == "PASS")
    nfail = sum(1 for f in findings if f.status == "FAIL")
    nskip = sum(1 for f in findings if f.status == "SKIP")
    nwarn = sum(1 for f in findings if f.status == "WARN")
    for f in findings:
        print(str(f))
        for d in f.detail[:60]:
            print("      " + str(d))
    print(f"\n小结：PASS={npass} FAIL={nfail} SKIP={nskip} WARN={nwarn}")
    print("说明：**WARN 不计入 FAIL**（G5 证据快照为「时刻 T 快照」，在演进仓库中恒变，"
          "故降级为提示）。FAIL 才是需要处置的项。")


# ---------------------------------------------------------------------------
# 反向用例自证非空转
# ---------------------------------------------------------------------------
def self_test():
    print("=" * 78)
    print("反向用例（每条守卫必须能 FAIL）")
    print("=" * 78)
    tmp = tempfile.mkdtemp(prefix="d2t04_")
    # 造一个最小镜像：复制被守卫读取的构件（含**所有清单生成器**与证据内嵌指纹所指文件）。
    # 生成器集合由 MANIFEST 派生，避免手抄漏项导致基线里混入「生成器文件缺失」的假 FAIL。
    to_copy = {
        "Docs/Reports/d2t02b-crosscheck.json", "Docs/Reports/d2t02a-baseline.json",
        "Docs/Reports/d2t02b-editmode-tests.xml",
        "Assets/_Project/Data/TableConfig.asset",
        f"{TEX}/table_metallic_roughness.png", f"{TEX}/table_metallic.png",
        f"{TEX}/table_roughness.png", f"{TEX}/table_normal.png",
        "Docs/Reference/table-metadata-reference.json", LEDGER_REL,
        "Assets/_Project/Scripts/Runtime/Table/TableConfig.cs",
        "Assets/_Project/Scripts/Runtime/Core/PoolGeometry.cs",
        "Assets/_Project/Tests/EditMode/TableConfigTests.cs",
    }
    for _a in MANIFEST:
        to_copy.add(_a["path"])
        for _g in manifest_generators(_a):
            if not is_placeholder_generator(_g):
                to_copy.add(_g)
    for rel in sorted(to_copy):
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

    def run_case(name, tamper, restore_rel, pick, expect="FAIL"):
        """立即执行：篡改 → run_all → 取守卫 → 复原。`expect` 为该用例应观察到的状态。"""
        tamper()
        f, _ = run_all(tmp)
        # 同一守卫可能有多个 finding（如 G3 的占位符 SKIP + 汇总 PASS/FAIL）——
        # 取**最严重**的一条，否则会被 SKIP 抢占。
        rank = {"FAIL": 0, "WARN": 1, "PASS": 2, "SKIP": 3}
        cands = [x for x in f if x.guard.startswith(pick)]
        g = min(cands, key=lambda x: rank.get(x.status, 9))
        results.append((name, g.status, ([g.msg] + g.detail)[:4], expect))
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
        # 语义漂移：追加一行**真实代码**（非注释、非字符串）⇒ 语义指纹必变。
        p = os.path.join(tmp, GEN)
        open(p, "a", encoding="utf-8").write("\nprivate const int TamperProbe = 1;\n")

    def t_a2c():
        # 只改注释 ⇒ 语义指纹**不应**变（修复「只改注释也红」的假阳性）。
        p = os.path.join(tmp, GEN)
        open(p, "a", encoding="utf-8").write("\n// tamper: comment only, semantics unchanged\n")

    def t_a3():
        p = os.path.join(tmp, BL)
        d = json.loads(read_text(p)); d["table"]["ball_radius"] = 0.03
        open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(d, ensure_ascii=False, indent=1))

    run_case("RC-B1 篡改 json 字段 parsedSegments 82→81", t_b1, CJ, "G1(B内容)")
    run_case("RC-A1 伪造 TableConfig.asset 源 sha", t_a1, AS, "G2(")
    run_case("RC-A2 改生成器源码(语义变/戳不变)", t_a2, GEN, "G3(")
    run_case("RC-A2c 生成器只改注释(语义不变⇒不红)", t_a2c, GEN, "G3(", expect="PASS")
    run_case("RC-A3 篡改 baseline 字段 ball_radius", t_a3, BL, "G4a")

    # 阴性对照：与"基线 FAIL 集合"一致 ⇒ 篡改全部被复原，无残留、无假阳性
    f, _ = run_all(tmp)
    cur_fails = sorted(x.guard for x in f if x.status == "FAIL")
    results.append(("CLEAN 未篡改（FAIL 集合应 == 基线）", "PASS" if cur_fails == base_fails else "FAIL",
                    [f"基线={base_fails}", f"现={cur_fails}"], "PASS"))

    print("\n| 反向用例 | 观察到的状态 | 期望 | 明细 |")
    print("| --- | --- | --- | --- |")
    ok = True
    for name, st, det, expect in results:
        print(f"| {name} | {st} | {expect} | {det} |")
        ok &= (st == expect)
    print(f"\n自证{'通过' if ok else '未通过'}：注入漂移时观察 FAIL、只改注释时观察 PASS、"
          f"未篡改时 FAIL 集合 == 基线。")
    shutil.rmtree(tmp, ignore_errors=True)
    return 0 if ok else 1


def write_ledger():
    import datetime
    gens = {}
    coverage = {}
    served = {}          # gen -> [artifact...]（同一生成器可服务多个生成物）
    for a in MANIFEST:
        if a["klass"] != "A":
            continue
        gset = manifest_generators(a)
        real = [g for g in gset if not is_placeholder_generator(g)]
        if not real:
            continue
        coverage[a["path"]] = sorted(gset)
        for gen in gset:
            if is_placeholder_generator(gen):
                continue
            gp = os.path.join(REPO, gen)
            if not os.path.exists(gp):
                print(f"  [警告] 生成器缺失，未入台账：{gen}（受影响生成物 {a['path']}）")
                continue
            stamp = None
            if "stamp" in a:
                field, _ = a["stamp"]
                mm = re.search(rf"{field}:\s*(\S+)", read_text(os.path.join(REPO, a["path"])))
                stamp = mm.group(1) if mm else None
            served.setdefault(gen, [])
            if a["path"] not in served[gen]:
                served[gen].append(a["path"])
            gens[gen] = {"semantic_sha256": semantic_fingerprint(gp),
                         "raw_sha256": sha256_file(gp),
                         "bytes": os.path.getsize(gp),
                         "stamp": stamp, "verifiedWith": sorted(served[gen])}
    led = {"schema": "idea35.generator-fingerprints.v2",
           "recordedAt": datetime.datetime.now(datetime.timezone.utc).isoformat(),
           "fingerprint": "semantic-v1 = sha256( 去注释 + 去字符串字面量 + 去空行/行首尾空白 )",
           "note": ("生成器语义指纹台账：改注释/改字符串字面量**不**算漂移；"
                    "改逻辑/数值字面量即视为『语义漂移』，需人工复核后刷新本台账。"
                    "artifactGenerators 记录每个生成物的**生成器集合**（改集合也会红）。"),
           "generators": gens,
           "artifactGenerators": coverage}
    p = os.path.join(REPO, LEDGER_REL)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, "w", encoding="utf-8", newline="\n").write(json.dumps(led, ensure_ascii=False, indent=1))
    print("台账已写入：" + p)
    for k, v in gens.items():
        print(f"  {v['semantic_sha256'][:16]}  {k}   [{v['verifiedWith']}]")


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
            "warn": sum(1 for f in findings if f.status == "WARN"),
            "note": "WARN 不计入 FAIL",
        },
        "findings": [{"guard": f.guard, "status": f.status, "message": f.msg, "detail": f.detail}
                     for f in findings],
        "blindSpots": BLIND_SPOTS,
        "generatorCoverageNotes": COVERAGE_NOTES,
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
