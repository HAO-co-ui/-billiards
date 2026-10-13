using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.Table;
using UnityEditor;
using UnityEngine;

namespace Billiards.EditorTools
{
    /// <summary>
    /// 一次性诊断工具：实测 Unity 导入 FBX 后网格顶点所处的轴向约定，
    /// 并穷举 48 种正交轴映射候选，报告哪一种让"82 段端点"与网格顶点对齐最好。
    ///
    /// 存在的理由：本项目**不能靠推断**确定轴向（任务书硬约束：只做一次转换、且要可判据）。
    /// PoolTable_Normalized.fbx 声明 UpAxis=Y 但顶点实际为 Z-up，Unity 的导入结果必须实测。
    /// 产出 Docs/Reports/day2-axis-probe.json，据此把唯一映射写进 PoolGeometry 常量。
    ///
    /// 这不是常规验收项，是"确定映射"的证据生成器；映射定下来后即可弃用。
    /// </summary>
    public static class AxisProbe
    {
        private const string ModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        public static void ProbeFromCommandLine()
        {
            int exit = 0;
            try
            {
                Probe();
            }
            catch (Exception ex)
            {
                Debug.LogError("AxisProbe 失败：" + ex);
                exit = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exit);
            }
        }

        [MenuItem("Billiards/Verify/Probe Model Axis Convention (diagnostic)", false, 250)]
        public static void ProbeInteractive()
        {
            Probe();
        }

        private static void Probe()
        {
            var config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            List<Vector3> raw = LoadVertices();

            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Vector3 v in raw)
            {
                Vector3 r = Truncate(v);
                min = Vector3.Min(min, r);
                max = Vector3.Max(max, r);
            }

            Debug.Log($"RAW local bounds min={Fmt(min)} max={Fmt(max)}  verts={raw.Count}");

            // 查询点：82 段端点（物理平面 → 三维水平点，高度取 0，仅比较水平面 XZ）
            var queries = new List<Vector2>();
            if (config != null)
            {
                foreach (CushionSegment s in config.segments)
                {
                    queries.Add(s.a);
                    queries.Add(s.b);
                }

                foreach (Pocket p in config.pockets)
                {
                    queries.Add(p.mouthA);
                    queries.Add(p.mouthB);
                    queries.Add(p.throatA);
                    queries.Add(p.throatB);
                }
            }

            var results = new List<Candidate>();
            foreach (int[] perm in new[]
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            })
            {
                for (int signMask = 0; signMask < 8; signMask++)
                {
                    int s0 = (signMask & 1) == 0 ? 1 : -1;
                    int s1 = (signMask & 2) == 0 ? 1 : -1;
                    int s2 = (signMask & 4) == 0 ? 1 : -1;
                    var signs = new[] { s0, s1, s2 };

                    var transformed = new List<Vector3>(raw.Count);
                    foreach (Vector3 v in raw)
                    {
                        transformed.Add(new Vector3(
                            signs[0] * v[perm[0]],
                            signs[1] * v[perm[1]],
                            signs[2] * v[perm[2]]));
                    }

                    var bmin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    var bmax = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    foreach (Vector3 v in transformed)
                    {
                        bmin = Vector3.Min(bmin, v);
                        bmax = Vector3.Max(bmax, v);
                    }

                    float worstHz = 0f;
                    foreach (Vector2 q in queries)
                    {
                        // 期望：物理(px,py) 与顶点水平面 (px,py) 对齐 → 在 XZ 平面比较 (px,py)
                        var target = new Vector2(q.x, q.y);
                        float best = float.MaxValue;
                        foreach (Vector3 v in transformed)
                        {
                            float dx = v.x - target.x;
                            float dz = v.z - target.y;
                            float d = dx * dx + dz * dz;
                            if (d < best) best = d;
                        }

                        worstHz = Mathf.Max(worstHz, Mathf.Sqrt(best));
                    }

                    results.Add(new Candidate
                    {
                        perm = $"{perm[0]}{perm[1]}{perm[2]}",
                        signs = $"{s0},{s1},{s2}",
                        bboxMin = Fmt(bmin),
                        bboxMax = Fmt(bmax),
                        worstHorizontal = worstHz,
                    });
                }
            }

            results.Sort((x, y) => x.worstHorizontal.CompareTo(y.worstHorizontal));

            var sb = new StringBuilder();
            sb.AppendLine("RAW local bounds: min=" + Fmt(min) + " max=" + Fmt(max) + "  verts=" + raw.Count);
            sb.AppendLine("Top 10 candidates by worst horizontal (XZ) alignment error:");
            for (int i = 0; i < Mathf.Min(10, results.Count); i++)
            {
                Candidate c = results[i];
                sb.AppendLine($"  perm={c.perm} signs={c.signs}  worstHz={c.worstHorizontal * 1000f:F4} mm  bbox={c.bboxMin} .. {c.bboxMax}");
            }

            Debug.Log(sb.ToString());

            WriteJson(min, max, raw.Count, results);
        }

        private class Candidate
        {
            public string perm;
            public string signs;
            public string bboxMin;
            public string bboxMax;
            public float worstHorizontal;
        }

        [Serializable]
        private class ProbeReport
        {
            public string schema = "billiards.day2-axis-probe.v1";
            public string generatedAtUtc;
            public string rawBoundsMin;
            public string rawBoundsMax;
            public int vertexCount;
            public List<CandidateDto> candidates = new List<CandidateDto>();
        }

        [Serializable]
        private class CandidateDto
        {
            public string perm;
            public string signs;
            public string bboxMin;
            public string bboxMax;
            public float worstHorizontalMetres;
        }

        private static void WriteJson(Vector3 min, Vector3 max, int count, List<Candidate> results)
        {
            var report = new ProbeReport
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                rawBoundsMin = Fmt(min),
                rawBoundsMax = Fmt(max),
                vertexCount = count,
            };

            foreach (Candidate c in results)
            {
                report.candidates.Add(new CandidateDto
                {
                    perm = c.perm,
                    signs = c.signs,
                    bboxMin = c.bboxMin,
                    bboxMax = c.bboxMax,
                    worstHorizontalMetres = c.worstHorizontal,
                });
            }

            Directory.CreateDirectory(SetupReport.ReportsDirectory);
            string path = Path.Combine(SetupReport.ReportsDirectory, "day2-axis-probe.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            Debug.Log("轴约定探针报告已写入：" + path);
        }

        private static List<Vector3> LoadVertices()
        {
            var result = new List<Vector3>();
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (o is Mesh m)
                {
                    result.AddRange(m.vertices);
                }
            }

            return result;
        }

        private static Vector3 Truncate(Vector3 v)
        {
            return v;
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F6},{v.y:F6},{v.z:F6})";
        }
    }
}
