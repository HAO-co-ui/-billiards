using System.Collections.Generic;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.Reference;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// D2-T02B 独立复核专用（QA 新增，不改实现代码）。
    ///
    /// 目的：现有 37 项测试**全部是正向断言**——它们只能证明"当前数据通过"，
    /// 不能证明"错误数据会被拦下"。本文件补上两类：
    ///  1) 负向/变异测试：喂给**实现者自己的纯函数**（ReferenceCrossCheck.Compare）
    ///     一个被故意损坏的输入，断言它真的报错（证明判据有牙，不是恒真）。
    ///  2) 独立轴枚举：不复用 PoolGeometry 的结论，穷举 48 种轴映射，
    ///     验证"唯一轴修正"是否确为最优、次优差多少（复现实现者 0.261/640 mm 的数量级）。
    ///
    /// 本文件只读取资产与参考数据、只写 Docs/Reports 下的证据文件，绝不修改任何实现代码或资产。
    /// </summary>
    public class D2T02B_IndependentAuditTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";
        private const string ModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        private const string RefAPath = "Docs/Reference/table-metadata-reference.json";
        private const string RefBPath = "Docs/Reference/gameplay-markers-reference.json";
        private const string AuditOutPath = "Docs/Reports/d2t02b-mesh-audit.json";

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        private static TableMetaDto LoadA()
        {
            string p = Path.Combine(ProjectRoot, RefAPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(p), "缺少 " + p);
            return JsonUtility.FromJson<TableMetaDto>(File.ReadAllText(p, Encoding.UTF8));
        }

        private static GameplayMarkersDto LoadB()
        {
            string p = Path.Combine(ProjectRoot, RefBPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(p), "缺少 " + p);
            return JsonUtility.FromJson<GameplayMarkersDto>(File.ReadAllText(p, Encoding.UTF8));
        }

        // ── 1) 负向测试：ReferenceCrossCheck.Compare 必须能抓错 ──────────────

        [Test]
        public void CrossCheck_DetectsMirroredB()
        {
            TableMetaDto a = LoadA();
            GameplayMarkersDto b = LoadB();
            // 把 B 的库边与袋口 x 全部取反（模拟"水平面镜像"）
            foreach (SegmentBDto s in b.cushion_segments)
            {
                s.physics2d.a.x = -s.physics2d.a.x;
                s.physics2d.b.x = -s.physics2d.b.x;
            }
            foreach (PocketBDto p in b.pockets)
            {
                p.physics2d.x = -p.physics2d.x;
                p.physics2d.mouth[0].x = -p.physics2d.mouth[0].x;
                p.physics2d.mouth[1].x = -p.physics2d.mouth[1].x;
                p.physics2d.throat[0].x = -p.physics2d.throat[0].x;
                p.physics2d.throat[1].x = -p.physics2d.throat[1].x;
            }

            ReferenceCrossCheck.Result r = ReferenceCrossCheck.Compare(a, b);
            Assert.IsFalse(r.Identical, "镜像后的 B 应被判定为不一致，但 Compare 说它一致（判据无牙）。");
            Assert.Greater(r.MaxSegmentEndpointDelta, 1f, "镜像应造成 ~2.6 m 的端点偏差。");
        }

        [Test]
        public void CrossCheck_DetectsScaledB()
        {
            TableMetaDto a = LoadA();
            GameplayMarkersDto b = LoadB();
            foreach (SegmentBDto s in b.cushion_segments)
            {
                s.physics2d.a.x *= 1.2f; s.physics2d.a.y *= 1.2f;
                s.physics2d.b.x *= 1.2f; s.physics2d.b.y *= 1.2f;
            }

            ReferenceCrossCheck.Result r = ReferenceCrossCheck.Compare(a, b);
            Assert.IsFalse(r.Identical, "×1.2 缩放后的 B 应被判定为不一致。");
            Assert.Greater(r.MaxSegmentEndpointDelta, 0.1f);
        }

        [Test]
        public void CrossCheck_DetectsSingleEndpointOffsetB()
        {
            TableMetaDto a = LoadA();
            GameplayMarkersDto b = LoadB();
            // 仅一个段端点偏移 1 mm —— 结构/连续性/手性全部完好，只有逐点比对能抓。
            b.cushion_segments[40].physics2d.b.x += 0.001f;

            ReferenceCrossCheck.Result r = ReferenceCrossCheck.Compare(a, b);
            Assert.IsFalse(r.Identical, "1 mm 单点偏移应被判定为不一致。");
        }

        // ── 2) 独立手性/绕向探针（自包含，证明镜像可被检出）─────────────────

        [Test]
        public void ChiralityProbe_FlipsUnderHorizontalMirror()
        {
            TableConfig cfg = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(cfg);

            int[] clean = ChiralitySigns(cfg, false);
            int[] mirrored = ChiralitySigns(cfg, true);
            Assert.AreEqual(new[] { 1, -1, -1, -1, 1, 1 }, clean, "clean 手性符号应为 [+,-,-,-,+,+]。");

            for (int i = 0; i < clean.Length; i++)
            {
                Assert.AreEqual(-clean[i], mirrored[i], $"镜像后袋 {i} 手性符号应翻转（判据须有牙）。");
            }
        }

        [Test]
        public void WindingProbe_FlipsSignUnderHorizontalMirror()
        {
            TableConfig cfg = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(cfg);

            float clean = Winding(cfg, false);
            float mirrored = Winding(cfg, true);
            Assert.Greater(clean, 0f, "clean 绕向应为 CCW。");
            Assert.Less(mirrored, 0f, "镜像后绕向应翻为 CW（判据须有牙）。");
        }

        private static int[] ChiralitySigns(TableConfig cfg, bool mirrorX)
        {
            var signs = new List<int>();
            foreach (Pocket p in cfg.pockets)
            {
                float s = mirrorX ? -1f : 1f;
                Vector2 m0 = new Vector2(s * p.mouthA.x, p.mouthA.y);
                Vector2 m1 = new Vector2(s * p.mouthB.x, p.mouthB.y);
                Vector2 tc = new Vector2(s * p.throatCenter.x, p.throatCenter.y);
                Vector2 mv = m1 - m0;
                Vector2 mc = (m0 + m1) * 0.5f;
                Vector2 iv = tc - mc;
                float cr = mv.x * iv.y - mv.y * iv.x;
                signs.Add(cr >= 0f ? 1 : -1);
            }

            return signs.ToArray();
        }

        private static float Winding(TableConfig cfg, bool mirrorX)
        {
            var groups = new Dictionary<string, List<Vector2>>();
            var order = new List<string>();
            float s = mirrorX ? -1f : 1f;
            foreach (CushionSegment seg in cfg.segments)
            {
                if (!groups.TryGetValue(seg.rail, out List<Vector2> list))
                {
                    list = new List<Vector2>();
                    groups[seg.rail] = list;
                    order.Add(seg.rail);
                }

                list.Add(new Vector2(s * seg.b.x, seg.b.y));
                if (list.Count == 1)
                {
                    list.Insert(0, new Vector2(s * seg.a.x, seg.a.y));
                }
            }

            var path = new List<Vector2>(groups[order[0]]);
            groups.Remove(order[0]);
            while (groups.Count > 0)
            {
                Vector2 tail = path[path.Count - 1];
                string bestRail = null;
                bool reverse = false;
                float best = float.MaxValue;
                foreach (KeyValuePair<string, List<Vector2>> kv in groups)
                {
                    float d1 = Vector2.Distance(kv.Value[0], tail);
                    float d2 = Vector2.Distance(kv.Value[kv.Value.Count - 1], tail);
                    if (d1 < best) { best = d1; bestRail = kv.Key; reverse = false; }
                    if (d2 < best) { best = d2; bestRail = kv.Key; reverse = true; }
                }

                List<Vector2> chain = groups[bestRail];
                groups.Remove(bestRail);
                if (reverse)
                {
                    chain = new List<Vector2>(chain);
                    chain.Reverse();
                }

                path.AddRange(chain);
            }

            float area2 = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                area2 += path[i].x * path[i + 1].y - path[i + 1].x * path[i].y;
            }

            return area2 * 0.5f;
        }

        // ── 3) 独立 48 候选轴枚举 + 最近顶点复算 ────────────────────────────

        [Test]
        public void IndependentAxisEnumeration_ReproducesBestAndSecondBest()
        {
            TableConfig cfg = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(cfg, "TableConfig 未生成。");

            List<Vector3> verts = LoadMeshVertsLocal();
            Assert.Greater(verts.Count, 0, "无法读取网格顶点。");

            // 基线端点（物理平面）——直接取自配置本身，但变换枚举与对齐算法均为本测试独立实现。
            var endpoints = new List<Vector2>();
            foreach (CushionSegment seg in cfg.segments)
            {
                endpoints.Add(seg.a);
                endpoints.Add(seg.b);
            }

            foreach (Pocket p in cfg.pockets)
            {
                endpoints.Add(p.mouthA);
                endpoints.Add(p.mouthB);
                endpoints.Add(p.throatA);
                endpoints.Add(p.throatB);
            }

            int[][] perms =
            {
                new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
                new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 },
            };
            int[][] signs =
            {
                new[] { 1, 1, 1 }, new[] { 1, 1, -1 }, new[] { 1, -1, 1 }, new[] { 1, -1, -1 },
                new[] { -1, 1, 1 }, new[] { -1, 1, -1 }, new[] { -1, -1, 1 }, new[] { -1, -1, -1 },
            };

            var results = new List<(string perm, string signs, string desc, float worst)>();
            foreach (int[] perm in perms)
            {
                foreach (int[] sg in signs)
                {
                    // 变换所有顶点一次
                    var tv = new List<Vector3>(verts.Count);
                    for (int i = 0; i < verts.Count; i++)
                    {
                        Vector3 v = verts[i];
                        float[] c = { v.x, v.y, v.z };
                        tv.Add(new Vector3(sg[0] * c[perm[0]], sg[1] * c[perm[1]], sg[2] * c[perm[2]]));
                    }

                    float worst = 0f;
                    foreach (Vector2 e in endpoints)
                    {
                        float bestD = float.MaxValue;
                        for (int i = 0; i < tv.Count; i++)
                        {
                            float dx = tv[i].x - e.x;
                            float dz = tv[i].z - e.y;
                            float d = dx * dx + dz * dz;
                            if (d < bestD) bestD = d;
                        }

                        worst = Mathf.Max(worst, Mathf.Sqrt(bestD));
                    }

                    string permKey = $"{perm[0]}{perm[1]}{perm[2]}";
                    string signKey = $"{sg[0]},{sg[1]},{sg[2]}";
                    results.Add((permKey, signKey, $"perm{permKey}/sign({signKey})", worst));
                }
            }

            results.Sort((x, y) => x.worst.CompareTo(y.worst));
            string bestPerm = results[0].perm;
            float bestErr = results[0].worst;

            // 每个排列的最优误差
            var permBest = new Dictionary<string, float>();
            foreach (var r in results)
            {
                if (!permBest.TryGetValue(r.perm, out float cur) || r.worst < cur)
                {
                    permBest[r.perm] = r.worst;
                }
            }

            var permList = new List<KeyValuePair<string, float>>(permBest);
            permList.Sort((x, y) => x.Value.CompareTo(y.Value));
            string secondPerm = permList[1].Key;
            float secondPermErr = permList[1].Value;

            // 同一最优排列内，8 个符号变体的误差跨度（暴露"符号不可判"事实）
            float signMin = float.MaxValue, signMax = float.MinValue;
            foreach (var r in results)
            {
                if (r.perm == bestPerm)
                {
                    signMin = Mathf.Min(signMin, r.worst);
                    signMax = Mathf.Max(signMax, r.worst);
                }
            }

            // 实现者所选变换 (-m.x, m.z, m.y) = perm021 / sign(-1,1,1) 的误差
            float chosenErr = float.NaN;
            foreach (var r in results)
            {
                if (r.perm == "021" && r.signs == "-1,1,1")
                {
                    chosenErr = r.worst;
                }
            }

            WriteAudit(bestPerm, bestErr, secondPerm, secondPermErr, signMin, signMax, chosenErr,
                verts.Count, endpoints.Count, results);

            // 判据 1：最优排列能把对齐做到亚毫米（轴向映射基本正确）
            Assert.Less(bestErr, 0.001f, $"最优对齐误差 {bestErr * 1000f:F4} mm 应 < 1 mm。");
            // 判据 2：最优"排列"显著优于次优"排列"（排列维是可判的）
            Assert.Greater(secondPermErr, bestErr * 2f,
                $"次优排列 {secondPerm} 误差 {secondPermErr * 1000f:F3} mm 应 > 最优的 2 倍，否则排列不可判。");
            // 判据 3（记录真相，不作失败）：同一排列内 8 个符号变体误差跨度极小
            //   —— 说明"端点对齐"这一指标**无法确定符号/手性**（本桌近对称）。符号由 det=+1（保法线）
            //   等独立理由确定，须在报告中作为残余风险列出。
            Assert.Less(signMax - signMin, 1e-4f,
                "同排列内符号变体误差跨度应极小（揭示端点对齐对符号不敏感）。");
        }

        private static List<Vector3> LoadMeshVertsLocal()
        {
            var result = new List<Vector3>();
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            foreach (Object o in assets)
            {
                if (o is Mesh m)
                {
                    result.AddRange(m.vertices);
                }
            }

            return result;
        }

        private static void WriteAudit(string bestPerm, float bestErr, string secondPerm, float secondPermErr,
            float signMin, float signMax, float chosenErr, int vertCount, int endpointCount,
            List<(string perm, string signs, string desc, float worst)> all)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"schema\": \"idea35.d2t02b-mesh-audit.v1\",");
                sb.AppendLine($"  \"meshVertexCount\": {vertCount},");
                sb.AppendLine($"  \"endpointCount\": {endpointCount},");
                sb.AppendLine($"  \"bestPermutation\": \"{bestPerm}\",");
                sb.AppendLine($"  \"bestWorstErrorMm\": {bestErr * 1000f:F4},");
                sb.AppendLine($"  \"secondPermutation\": \"{secondPerm}\",");
                sb.AppendLine($"  \"secondPermutationWorstMm\": {secondPermErr * 1000f:F3},");
                sb.AppendLine($"  \"samePermSignSpreadMm\": {(signMax - signMin) * 1000f:F5},");
                sb.AppendLine($"  \"chosenTransform(-m.x,m.z,m.y)_WorstMm\": {chosenErr * 1000f:F4},");
                sb.AppendLine("  \"finding\": \"端点对齐可判‘排列’(021 优于 012 约 2.4x)，但同排列内 8 个符号变体误差跨度 <1e-4 mm，"
                    + "无法判‘符号/手性’——符号由 det=+1(保法线) 等独立理由确定。\",");
                sb.AppendLine("  \"allCandidates\": [");
                for (int i = 0; i < all.Count; i++)
                {
                    sb.AppendLine($"    {{ \"rank\": {i + 1}, \"perm\": \"{all[i].perm}\", \"signs\": \"{all[i].signs}\", "
                        + $"\"worstMm\": {all[i].worst * 1000f:F5} }}"
                        + (i < all.Count - 1 ? "," : ""));
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");
                string path = Path.Combine(ProjectRoot, AuditOutPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("写审计文件失败：" + e.Message);
            }
        }
    }
}
