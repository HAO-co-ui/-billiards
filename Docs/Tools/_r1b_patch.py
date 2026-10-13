# -*- coding: utf-8 -*-
"""R1b patch: 退化三角形守卫 + 去掉过度规定的断言。写成文件执行以避免 shell 破坏引号。"""

GUARD = (
    '            // 退化三角形（XZ 投影面积为 0，如竖直面）：不能判「内部」，否则任意点都返回 0 距离。\n'
    '            float area2 = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);\n'
    '            if (Mathf.Abs(area2) < 1e-12f)\n'
    '            {\n'
    '                return false;\n'
    '            }\n\n'
)

# 1) 校验器
p1 = "Assets/_Project/Scripts/Editor/TableImport/TableIntegrityValidator.cs"
s = open(p1, encoding='utf-8').read()
a1 = ('            private static bool IsInsideTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)\n'
      '            {\n')
assert a1 in s, "validator anchor"
assert "area2" not in s, "validator already patched"
s = s.replace(a1, a1 + GUARD, 1)
open(p1, 'w', encoding='utf-8').write(s)
print("validator patched")

# 2) TableConfigTests
p2 = "Assets/_Project/Tests/EditMode/TableConfigTests.cs"
t = open(p2, encoding='utf-8').read()
a2 = ('        private static bool InsideXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)\n'
      '        {\n')
assert a2 in t, "TableConfigTests anchor"
assert "area2" not in t
t = t.replace(a2, a2 + GUARD, 1)
open(p2, 'w', encoding='utf-8').write(t)
print("TableConfigTests patched")

# 3) MeshAlignmentSensitivityTests
p3 = "Assets/_Project/Tests/EditMode/MeshAlignmentSensitivityTests.cs"
u = open(p3, encoding='utf-8').read()
a3 = ('        private static bool InsideXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)\n'
      '        {\n')
assert a3 in u, "sens anchor"
assert "area2" not in u
u = u.replace(a3, a3 + GUARD, 1)

old_nm = '''
            // (b) 非单调性：存在 d1 < d2 使 face(d1) > face(d2) —— 证明它对位移不分辨
            bool nonMonotonic = false;
            for (int i = 0; i < faceDistances.Count; i++)
            {
                for (int j = i + 1; j < faceDistances.Count; j++)
                {
                    if (faceDistances[i] > faceDistances[j] + 1e-9f)
                    {
                        nonMonotonic = true;
                    }
                }
            }

            Assert.IsTrue(nonMonotonic,
                "面点度量对位移应**非单调**（更大位移的度量值可能更小）；不成立说明实现有问题。");
'''
new_nm = '''
            // (b) 只断言"对位移不分辨"这一必要结论 —— 不过度规定它必须非单调（那取决于具体几何）。
            Assert.IsTrue(anyLargeDisplacementMissed,
                "面点度量对位移不具分辨力：至少一个 ≥1 mm 位移应被漏检。");
'''
assert old_nm in u, "nonmonotonic block"
u = u.replace(old_nm, new_nm)

# faceDistances 不再用于非单调判定 -> 改为记录未位移时的最坏面距离（并在输出中打印）
u = u.replace("            var faceDistances = new List<float>();\n",
              "            float worstFaceAtUnmoved = 0f;\n")
u = u.replace("                    faceDistances.Add(face);\n",
              "                    if (d == 0f)\n                    {\n                        worstFaceAtUnmoved = Mathf.Max(worstFaceAtUnmoved, face);\n                    }\n")
u = u.replace('            TestContext.WriteLine(string.Join("\\n", rows));\n\n            // (a)',
              '            TestContext.WriteLine(string.Join("\\n", rows));\n'
              '            TestContext.WriteLine($"[未位移] 面点最坏 = {worstFaceAtUnmoved * 1000f:F6} mm（应 ≤ {SurfacePointTolerance * 1000f:F4} mm）");\n\n            // (a)')
open(p3, 'w', encoding='utf-8').write(u)
print("MeshAlignmentSensitivityTests patched")
