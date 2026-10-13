using System.Collections.Generic;
using Billiards.Core;
using Billiards.Diagnostics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// 【落盘的盲洞标定测试】—— 三层度量（L1 面点 / L2 边距 / L3 顶点）的**检出下限与盲带**。
    ///
    /// 目的**不是**证明无洞（已确认 L3 有周期性盲带），而是把洞**按威胁类标定出来供审计**：
    ///  · **单点**位移 → 存在上盲带 → 该威胁由 `CMP-03`（索引对齐，容差 1e-5 m）覆盖；
    ///  · **整体**刚性位移 / **局部**区域位移 → 应无上盲带（判据取所有位移点的最大值，个别端点落带会被掩蔽）。
    ///
    /// 三层各自的盲区（禁止声称「全向无漏」）：
    ///  · L1：只验「落在网格面上」，**对足迹内滑动盲**（但对掉出网格/袋孔敏感）；
    ///  · L2：高灵敏（底噪 0.18 µm），但**对沿边方向滑动全盲**；
    ///  · L3：**唯一无方向性盲区**，但有**周期性盲带**，**不保证检出**。
    /// </summary>
    public class MeshAlignmentSensitivityTests
    {
        private const string ModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        // 阈值走单源 `MeshAlignmentTolerances`；不再就地复制（铁律 16）。

        private const float ScanStep = 0.00005f;   // 0.05 mm
        private const float ScanMax = 0.020f;      // 20 mm

        private TableConfig _config;
        private MeshAlignmentMetrics _metrics;
        private Vector3[] _verts;
        private int[] _tris;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 TableConfig.asset");

            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (o is Mesh m)
                {
                    int baseIndex = verts.Count;
                    foreach (Vector3 v in m.vertices)
                    {
                        verts.Add(PoolGeometry.ModelVertexToUnity(v));
                    }

                    int[] t = m.triangles;
                    for (int i = 0; i < t.Length; i++)
                    {
                        tris.Add(baseIndex + t[i]);
                    }
                }
            }

            Assert.Greater(verts.Count, 0, "无法读取网格顶点");
            _verts = verts.ToArray();
            _tris = tris.ToArray();
            _metrics = new MeshAlignmentMetrics(_verts, _tris);
        }

        // ── 采样点 ─────────────────────────────────────────────────────────

        private List<Vector2> AllEndpoints()
        {
            var pts = new List<Vector2>();
            foreach (CushionSegment s in _config.segments)
            {
                pts.Add(s.a);
                pts.Add(s.b);
            }

            foreach (Pocket p in _config.pockets)
            {
                pts.Add(p.mouthA);
                pts.Add(p.mouthB);
                pts.Add(p.throatA);
                pts.Add(p.throatB);
            }

            return pts;
        }

        /// <summary>取一条 rail 的段端点（模拟「某段库边被重做」的局部区域位移）。</summary>
        private List<Vector2> OneRailEndpoints()
        {
            var pts = new List<Vector2>();
            string rail = _config.segments[0].rail;
            foreach (CushionSegment s in _config.segments)
            {
                if (s.rail == rail)
                {
                    pts.Add(s.a);
                    pts.Add(s.b);
                }
            }

            return pts;
        }

        // ── 判据 ───────────────────────────────────────────────────────────

        private readonly struct Verdict
        {
            public readonly bool AnyCaught;

            public Verdict(bool any) { AnyCaught = any; }
        }

        private bool AnyLayerCaught(Vector3 q)
        {
            MeshAlignmentMetrics.Measurement m = _metrics.Measure(q);
            bool l1 = m.Face > MeshAlignmentTolerances.L1SurfacePoint;
            bool l2 = m.Edge > MeshAlignmentTolerances.L2EdgeDistance;
            bool l3 = m.Vertex / Mathf.Max(m.LocalLongestEdge, 1e-9f) > MeshAlignmentTolerances.L3VertexRatio;
            return l1 || l2 || l3;
        }

        /// <summary>一组点刚性同向平移后，该组是否**全部**未被检出。</summary>
        private bool GroupAllPass(List<Vector2> pts, Vector2 delta)
        {
            foreach (Vector2 p in pts)
            {
                if (AnyLayerCaught(PoolGeometry.PhysicsToUnity(p + delta)))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<(float lo, float hi)> BlindBands(System.Func<float, bool> passAt, out float firstCaught)
        {
            var bands = new List<(float, float)>();
            firstCaught = float.NaN;

            float lo = -1f, hi = -1f;
            for (float d = 0f; d <= ScanMax + 1e-9f; d += ScanStep)
            {
                bool pass = passAt(d);
                if (pass && d > 0f)
                {
                    if (lo < 0f)
                    {
                        lo = d;
                    }

                    hi = d;
                }
                else
                {
                    if (lo >= 0f)
                    {
                        bands.Add((lo, hi));
                        lo = -1f;
                    }

                    if (d > 0f && float.IsNaN(firstCaught))
                    {
                        firstCaught = d;
                    }
                }
            }

            if (lo >= 0f)
            {
                bands.Add((lo, hi));
            }

            return bands;
        }

        private static string Fmt(List<(float lo, float hi)> bands)
        {
            if (bands.Count == 0)
            {
                return "（无）";
            }

            var sb = new List<string>();
            foreach ((float lo, float hi) in bands)
            {
                sb.Add($"[{lo * 1000f:F2}, {hi * 1000f:F2}] mm");
            }

            return string.Join("、", sb);
        }

        // ── 1. 未位移：三层都不得报警 ───────────────────────────────────────

        [Test]
        public void Undisplaced_NoLayerFires_AndBaselinesAreRecorded()
        {
            float worstFace = 0f, worstEdge = 0f, worstRatio = 0f;
            foreach (Vector2 p in AllEndpoints())
            {
                MeshAlignmentMetrics.Measurement m = _metrics.Measure(PoolGeometry.PhysicsToUnity(p));
                worstFace = Mathf.Max(worstFace, m.Face);
                worstEdge = Mathf.Max(worstEdge, m.Edge);
                worstRatio = Mathf.Max(worstRatio, m.Vertex / Mathf.Max(m.LocalLongestEdge, 1e-9f));
            }

            TestContext.WriteLine($"[底噪] L1 面点 max = {worstFace * 1e6f:F4} µm；"
                                + $"L2 边距 max = {worstEdge * 1e6f:F4} µm；"
                                + $"L3 最坏比值 = {worstRatio:F4}（阈值 k = {MeshAlignmentTolerances.L3VertexRatio:F4}，余量 {MeshAlignmentTolerances.L3VertexRatio / Mathf.Max(worstRatio, 1e-9f):F2}×）");

            Assert.LessOrEqual(worstFace, MeshAlignmentTolerances.L1SurfacePoint, "未位移 L1 不得报警");
            Assert.LessOrEqual(worstEdge, MeshAlignmentTolerances.L2EdgeDistance, "未位移 L2 不得报警");
            Assert.LessOrEqual(worstRatio, MeshAlignmentTolerances.L3VertexRatio, "未位移 L3 不得报警");
        }

        // ── 2. 单点位移密扫：列出上盲带，并标注兜底 ──────────────────────────

        [Test]
        public void SinglePointScan_ListsUpperBlindBands_CoveredByCmp03()
        {
            Vector2 worst = AllEndpoints()[0];
            float worstRatio = -1f;
            foreach (Vector2 p in AllEndpoints())
            {
                MeshAlignmentMetrics.Measurement m = _metrics.Measure(PoolGeometry.PhysicsToUnity(p));
                float r = m.Vertex / Mathf.Max(m.LocalLongestEdge, 1e-9f);
                if (r > worstRatio)
                {
                    worstRatio = r;
                    worst = p;
                }
            }

            var dir = new Vector2(1f, 1f).normalized;
            List<(float lo, float hi)> bands =
                BlindBands(d => !AnyLayerCaught(PoolGeometry.PhysicsToUnity(worst + dir * d)), out float firstCaught);

            TestContext.WriteLine($"[单点] 端点({worst.x:F4},{worst.y:F4})，方向 (1,1)/√2，步进 0.05 mm：");
            TestContext.WriteLine($"[单点] 首个被检出的 δ = {firstCaught * 1000f:F2} mm");
            TestContext.WriteLine($"[单点] 上盲带（期间判 PASS）= {Fmt(bands)}");
            TestContext.WriteLine("[单点] 该威胁由 CMP-03（索引对齐、容差 1e-5 m）覆盖 —— 单点值错误属威胁 A，不是 AXL 的职责。");

            Assert.IsTrue(bands.Count > 0, "单点位移应存在上盲带（到最近特征类度量的固有性质）；若为 0，请检查实现是否退化。");
        }

        // ── 3. 整体刚性位移：应无上盲带 ─────────────────────────────────────

        [Test]
        public void WholeSetRigidShift_HasNoUpperBlindBand()
        {
            List<Vector2> all = AllEndpoints();
            var dir = new Vector2(1f, 1f).normalized;

            List<(float lo, float hi)> bands = BlindBands(d => GroupAllPass(all, dir * d), out float firstCaught);

            TestContext.WriteLine($"[整体] {all.Count} 点刚性平移，方向 (1,1)/√2：首个检出 δ = {firstCaught * 1000f:F2} mm");
            TestContext.WriteLine($"[整体] 盲带 = {Fmt(bands)}（应仅有底噪带）");

            foreach ((float lo, float hi) in bands)
            {
                Assert.LessOrEqual(hi, 0.0010f,
                    $"整体刚性位移不应存在 >1 mm 的上盲带，但有 [{lo * 1000f:F2},{hi * 1000f:F2}] mm");
            }
        }

        // ── 4. 局部区域位移：应无上盲带 ─────────────────────────────────────

        [Test]
        public void LocalRegionShift_HasNoUpperBlindBand()
        {
            List<Vector2> rail = OneRailEndpoints();
            var dir = new Vector2(0f, 1f);

            List<(float lo, float hi)> bands = BlindBands(d => GroupAllPass(rail, dir * d), out float firstCaught);

            TestContext.WriteLine($"[局部] rail='{_config.segments[0].rail}' 的 {rail.Count} 点平移，方向 (0,1)：首个检出 δ = {firstCaught * 1000f:F2} mm");
            TestContext.WriteLine($"[局部] 盲带 = {Fmt(bands)}（应仅有底噪带）");

            foreach ((float lo, float hi) in bands)
            {
                Assert.LessOrEqual(hi, 0.0010f,
                    $"局部区域位移不应存在 >1 mm 的上盲带，但有 [{lo * 1000f:F2},{hi * 1000f:F2}] mm");
            }
        }

        // ── 5. L2 的方向性盲区：沿边滑动 ⇒ 距离恒 ≈0（必须被钉住）──────────────

        [Test]
        public void L2_IsBlindAlongEdgeDirection_AndThisIsPinned()
        {
            // 取一条**真实网格边**（三角形的 a-b），在其中点起步、沿该边方向滑动。
            // 注意：不能用"段端点 a→b 方向"—— 段端点只是面上的点，其连线不等于网格边方向（首版即此误用）。
            Vector3 a = _verts[_tris[0]];
            Vector3 b = _verts[_tris[1]];
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = (b - a).normalized;
            float halfLen = Vector3.Distance(a, b) * 0.3f;

            float maxL2 = 0f, maxL3Ratio = 0f;
            for (float d = -halfLen; d <= halfLen; d += halfLen / 20f)
            {
                MeshAlignmentMetrics.Measurement m = _metrics.Measure(mid + dir * d);
                maxL2 = Mathf.Max(maxL2, m.Edge);
                maxL3Ratio = Mathf.Max(maxL3Ratio, m.Vertex / Mathf.Max(m.LocalLongestEdge, 1e-9f));
            }

            TestContext.WriteLine($"[L2 盲区] 沿**真实网格边**滑动 ±{halfLen * 1000f:F2} mm："
                                + $"L2 最大 = {maxL2 * 1e6f:F4} µm（≈0 即全盲）；"
                                + $"L3 最大比值 = {maxL3Ratio:F4}（阈值 {MeshAlignmentTolerances.L3VertexRatio:F4}）");

            Assert.LessOrEqual(maxL2, MeshAlignmentTolerances.L2EdgeDistance, "L2 沿边方向应恒 ≈0（已知盲区，必须被测试钉住）");
        }
    }
}
