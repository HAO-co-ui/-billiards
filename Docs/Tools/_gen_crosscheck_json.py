# -*- coding: utf-8 -*-
"""Generate Docs/Reports/d2t02b-crosscheck.json from verified results."""
import json, sys, io, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
ROOT = r"E:/Unity_Deve/Unity_Project/Billiards game"

evidence = {
  "schema": "idea35.d2t02b-crosscheck.v1",
  "task": "D2-T02B TableConfig 落地交叉复核",
  "reviewer": "quality-lead",
  "verdict": "PASS_WITH_CONCERNS",
  "blocksDay3": False,
  "target": "Assets/_Project/Data/TableConfig.asset",
  "baseline": "Docs/Reports/d2t02a-baseline.json",
  "chainOfCustody": {
    "F_origin_table_sha256": "ba60fc204c155881127b500bcce2f388066151757984659f89e681c2ef364398",
    "Docs_Reference_table_sha256": "ba60fc204c155881127b500bcce2f388066151757984659f89e681c2ef364398",
    "asset_recorded_sourceSha256": "ba60fc204c155881127b500bcce2f388066151757984659f89e681c2ef364398",
    "match": True,
    "note": "入库副本与 F: 原始文件逐字节相同；资产记录 hash 与之一致。"
  },
  "check1_dataLanding": {
    "parsedSegments": 82,
    "parsedPockets": 6,
    "rawMaxDelta_m": 1.057670e-07,
    "rawMaxDeltaLocation": "seg[61].b.x",
    "float32DomainMaxDelta_m": 5.381012e-08,
    "pointsWhere_f32_asset_ne_f32_baseline": 0,
    "findingsAt1e-6Tolerance": 0,
    "interpretation": "原始 maxDelta 1.06e-7 m 纯属 float32 打印精度：把资产解析值四舍五入到 float32 后与真值 float32 逐点相同（0 处不同）。真实落地误差 = 0。"
  },
  "check2_chirality": {
    "CHR01_signPattern": [1, -1, -1, -1, 1, 1],
    "CHR01_expected": [1, -1, -1, -1, 1, 1],
    "CHR01_pass": True,
    "CHR02_shoelaceOver2": 3.144285,
    "CHR02_orientation": "CCW",
    "CHR02_gapMm": 177.8,
    "CHR02_pass": True,
    "CHR02_notVacuous": "注入镜像后 shoelace 变 -3.1443（符号翻转），故非恒真。"
  },
  "check3_reverseCases": [
    {"id": "clean", "inject": "无", "result": "PASS(应通过)", "fired": []},
    {"id": "RC-1", "inject": "seg[40].b.x += 1mm", "result": "拦截", "fired": ["CMP-03(vio=1,maxDelta=1.0e-3)"]},
    {"id": "RC-2", "inject": "ballRadius=0.03", "result": "拦截", "fired": ["DAT-03(scalarDelta=1.42e-3)"]},
    {"id": "RC-3", "inject": "pocket[0].captureRadius=1.0", "result": "拦截", "fired": ["CMP-03(vio=1,maxDelta=9.69e-1)"]},
    {"id": "RC-4", "inject": "全量 x->-x (水平面镜像)", "result": "拦截", "fired": ["CMP-03(vio=202,maxDelta=2.67)", "CHR-01", "CHR-02(shoelace=-3.1443)"]},
    {"id": "RC-5", "inject": "全量 90 度旋转", "result": "拦截", "fired": ["CMP-03(vio=206,maxDelta=2.06)"]},
    {"id": "RC-6", "inject": "全量 180 度旋转", "result": "拦截", "fired": ["CMP-03(vio=206,maxDelta=2.91)"], "note": "仅索引对齐逐点比对能抓；CHR-01/CHR-02 对 proper 旋转不响应（符合预期）。"},
    {"id": "RC-7", "inject": "全量 x1.2", "result": "拦截", "fired": ["CMP-03(vio=200,maxDelta=2.91e-1)", "CHR-02(shoelace=4.5278)"]}
  ],
  "reverseCasesMissed": [],
  "check4_tests": {
    "command": "Unity.exe -batchmode -projectPath <proj> -runTests -testPlatform EditMode -testResults Docs/Reports/d2t02b-editmode-tests.xml -logFile Logs/d2t02b-tests.log",
    "total": 43, "passed": 43, "failed": 0,
    "breakdown": {"PoolConstantsTests": 13, "TableConfigTests": 24, "D2T02B_IndependentAuditTests": 6},
    "note": "team-lead 预期 35，实际 37（实现者原有）+ 6（QA 新增）= 43。"
  },
  "check5_axisTransform": {
    "secondTransformSearch": {
      "result": "仅 PoolGeometry.cs 一处",
      "evidence": "全 Assets/_Project/**/*.cs 搜索 new Vector3/localScale/.scale=/Quaternion/Euler/Rotation/Instantiate/SetParent → 全部坐标变换集中在 PoolGeometry.cs；无任何 localScale 或 .scale 赋值；场景 Main.unity 仅 1 相机 + 1 平行光，scale 均 1，无球桌对象。",
      "importerSettings": "bakeAxisConversion=false, globalScale=1, useFileScale=false → 导入器不做二次转换/缩放。"
    },
    "independent48Enumeration": {
      "bestPermutation": "021",
      "bestWorstErrorMm": 0.2611,
      "secondPermutation": "012",
      "secondPermutationWorstMm": 640.722,
      "chosenTransform_(-m.x,m.z,m.y)_WorstMm": 0.2613,
      "reproducesImplementerNumbers": True
    },
    "newFinding_signBlindness": {
      "samePermSignSpreadMm": 0.00027,
      "meaning": "水平面端点对齐（AXL-04/05）对水平符号/手性不敏感：镜像变换 (-m.x,m.z,-m.y) 的对齐误差 0.26114 mm 甚至略优于所选 0.26127 mm。故 AXL-04/05 单独不能证明手性；符号实际由 AXL-02（包围盒不对称：X 端 -1.412686 vs +1.412409；Y 端腿底 -0.8635 vs 顶 +0.0610）+ 修正确为纯旋转(det=+1) 共同钉死。结论：所选变换正确，但 AXL-04 detail 文案‘证明轴向映射正确’属过度声明。"
    }
  },
  "check6_fieldCoverage": {
    "rail": {"status": "保留", "note": "82 段全部带 rail（end_negative / long_positive_* 等）。team-lead 的 grep 得 0 属误报（字段确实存在）。"},
    "legBottomY": {"status": "保留", "note": "= 源 lowest_y -0.8635167479515076；同时存在于 PoolConstants.TableLegBottomY。归属合理。"},
    "carriedViaRename": ["runtimeCenter->center","runtimeMouth[0/1]->mouthA/B","runtimeThroat[0/1]->throatA/B","runtimeThroatCenter->throatCenter","runtimeThroatNormal->throatNormal","runtimeMouthWidth->mouthWidth","runtimeThroatWidth->throatWidth"],
    "notCarried": {
      "source_space_measurement": ["sourceInnerRim","sourceCenter","sourceProjectedMinRadius","sourceProjectedMaxRadius","sourceInscribedRadius","sourceMouth","sourceThroat"],
      "runtime_measurement": ["runtimeInnerRim(12点袋网内沿)","runtimeProjectedMinRadius","runtimeProjectedMaxRadius","runtimeInscribedRadius"],
      "source_modeling_metadata": ["meshComponent","topRimFaceCount"],
      "redundant": ["recommendedCaptureRadius(==captureRadius)"],
      "semantic_strings": ["coordinate_mapping","collision_coordinates","notes"]
    },
    "impact": "无 gameplay 关键字段丢失：captureRadius/openingRadius/throatNormal/captureNote/mouthWidth/throatWidth/rail 均在。runtimeInnerRim 供 Day3 袋网渲染可考虑补；语义字符串仅存于代码注释。"
  },
  "check7_textures": {
    "sizes": {"table_metallic.png": "1024x1024", "table_roughness.png": "1024x1024", "table_metallic_roughness.png": "1024x1024", "table_normal.png": "2048x2048", "table_net_rgba.png": "2048x2048", "table_basecolor.jpg": "JPEG(2048 声明)"},
    "packedMask": {"R_eq_metallic_mismatchPixels": 0, "A_eq_255_minus_roughness_mismatchPixels": 0, "G_nonzero": 0, "B_nonzero": 0, "verdict": "逐像素精确"},
    "normalGreenMean": 125.73,
    "normalGreenMeanIsWeakDiscriminant": "翻转后绿均值约 129.3，同样接近中性；该指标不能区分是否翻转。不翻转的结论主要依据 OpenGL 约定链，非视觉结论 → Day3 A/B 复核。"
  },
  "concerns": [
    {"id": "C1", "severity": "中", "text": "AXL-04/05（水平面最近顶点）对水平符号/手性不敏感（sign spread 0.27 µm），单独不能证明手性；手性实际由 AXL-02 包围盒不对称 + det=+1 钉死。建议修正 AXL-04 detail 文案，勿据此宣称证明轴向。"},
    {"id": "C2", "severity": "低", "text": "法线绿通道‘不翻转’主要由约定链支撑；实测绿均值 125.7 对是否翻转不具判别力。已由实现者自认非视觉定论 → Day3 受光 A/B。"},
    {"id": "C3", "severity": "低", "text": "CMP-03 只逐点比对段与袋，未直接比对全局标量（innerWidth 等）；标量由 DAT-03 对照 PoolConstants 覆盖（间接）。"},
    {"id": "C4", "severity": "信息", "text": "PoolConstants.PlayfieldVisualScale=1.2 已定义但全工程未使用 → 当前无双重缩放风险；Day3 应用时须仅作用于视觉 root（PoolConstantsTests 已断言其独立于物理）。"},
    {"id": "C5", "severity": "信息", "text": "coordinate_mapping / collision_coordinates 等坐标语义字符串未随资产持久化，仅在代码注释；追踪性略弱。"}
  ]
}

out = os.path.join(ROOT, "Docs/Reports/d2t02b-crosscheck.json")
with open(out, "w", encoding="utf-8") as f:
    json.dump(evidence, f, ensure_ascii=False, indent=1)
print("written:", out)
print("verdict:", evidence["verdict"], "| blocksDay3:", evidence["blocksDay3"])
print("tests:", evidence["check4_tests"]["passed"], "/", evidence["check4_tests"]["total"])
