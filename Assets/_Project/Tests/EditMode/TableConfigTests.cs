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
    /// Day 2 验收测试：把"碰撞数据落地 + 美术资源导入"的硬指标变成 EditMode 断言。
    ///
    /// 三条主线：
    ///  1) TableConfig（运行时真源）与参考数据、PoolConstants 自洽；
    ///  2) A/B 两份源文件逐点一致（不自行选一份了事）；
    ///  3) 轴向转换只做一次且可判据 —— 用网格包围盒与最近顶点距离断言。
    ///
    /// 这些断言是 Day 4+ 物理内核的几何基准；一旦数据源或单位制被改动，这里会先报。
    /// </summary>
    public class TableConfigTests
    {
        private const string ModelPath = "Assets/_Project/Art/Models/PoolTable_Normalized.fbx";
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>
        /// 物理数据 ↔ 渲染网格 的“最近顶点”对应容差 = ≤ 0.5 mm。
        /// 与校验项 AXL-04/AXL-05 同源：0.2613 mm 是“82 段直线段逼近曲率袋角”的轮廓近似残差（非误差），
        /// 阈值由该测量性质推导（约 1.9× 余量）。属数据↔网格对应类，**非镜像判据**（镜像由 CHR-01/CHR-02/CMP-03 负责）。
        /// </summary>
        private const float VertexTolerance = 0.0005f;

        private static TableConfig _config;
        private static TableMetaDto _a;
        private static GameplayMarkersDto _b;

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);

            string aPath = Path.Combine(ProjectRoot, "Docs/Reference/table-metadata-reference.json");
            string bPath = Path.Combine(ProjectRoot, "Docs/Reference/gameplay-markers-reference.json");
            Assert.IsTrue(File.Exists(aPath), "缺少入库参考数据 " + aPath);
            Assert.IsTrue(File.Exists(bPath), "缺少入库参考数据 " + bPath);

            _a = JsonUtility.FromJson<TableMetaDto>(File.ReadAllText(aPath, Encoding.UTF8));
            _b = JsonUtility.FromJson<GameplayMarkersDto>(File.ReadAllText(bPath, Encoding.UTF8));
        }

        private static TableConfig Config
        {
            get
            {
                Assert.IsNotNull(_config, "TableConfig.asset 未生成。请先运行 Billiards/Data/Import Table Reference Data。");
                return _config;
            }
        }

        // ── 拓扑与不变式 ───────────────────────────────────────────────────

        [Test]
        public void TableConfig_AssetIsGenerated()
        {
            Assert.IsNotNull(_config, "TableConfig.asset 应存在（先跑导入器）。");
            Assert.AreEqual(82, _config.SegmentCount);
            Assert.AreEqual(6, _config.PocketCount);
        }

        [Test]
        public void SegmentCount_MatchesReference()
        {
            Assert.AreEqual(82, Config.SegmentCount);
            Assert.AreEqual(PoolConstants.CushionSegmentCount, Config.SegmentCount);
            Assert.AreEqual(82, _a.collision.segments.Length);
        }

        [Test]
        public void PocketCount_MatchesReference()
        {
            Assert.AreEqual(6, Config.PocketCount);
            Assert.AreEqual(6, _a.collision.pockets.Length);
        }

        [Test]
        public void PocketKinds_AreFourCornersTwoSides()
        {
            int corners = 0, sides = 0;
            foreach (Pocket p in Config.pockets)
            {
                if (p.IsCorner) corners++;
                else if (p.IsSide) sides++;
            }

            Assert.AreEqual(4, corners);
            Assert.AreEqual(2, sides);
        }

        [Test]
        public void Config_IsConsistentWithPoolConstants()
        {
            List<string> errors = Config.ConsistencyErrors();
            Assert.AreEqual(0, errors.Count, "与 PoolConstants 不一致：\n" + string.Join("\n", errors));
        }

        [Test]
        public void CaptureRadius_FollowsCaptureNoteInvariant()
        {
            // captureNote：推荐捕获圆 = 袋口内投影半径 − 球半径 − 2 mm。
            const float margin = 0.002f;
            foreach (Pocket p in Config.pockets)
            {
                float derived = p.openingRadius - PoolConstants.BallRadius - margin;
                Assert.AreEqual(derived, p.captureRadius, 1e-6f, $"袋 {p.index} 捕获半径不符不变式。");
                Assert.Less(p.captureRadius, p.openingRadius);
            }
        }

        [Test]
        public void PocketGeometry_MouthWiderThanThroat()
        {
            foreach (Pocket p in Config.pockets)
            {
                Assert.Greater(p.mouthWidth, p.throatWidth, $"袋 {p.index} 应为收口形状。");
                Assert.Greater(p.throatWidth, 0f);
                Assert.IsNotEmpty(p.captureNote, $"袋 {p.index} 缺少 captureNote 捕获规则。");
            }
        }

        [Test]
        public void Segments_AreNonDegenerateAndInsideEnvelope()
        {
            float envelope = Mathf.Max(PoolGeometry.OuterLength, PoolGeometry.OuterWidth) * 0.5f + 0.05f;
            foreach (CushionSegment s in Config.segments)
            {
                Assert.Greater(Vector2.Distance(s.a, s.b), 1e-9f, "存在零长段。");
                Assert.LessOrEqual(Mathf.Abs(s.a.x), envelope);
                Assert.LessOrEqual(Mathf.Abs(s.a.y), envelope);
                Assert.LessOrEqual(Mathf.Abs(s.b.x), envelope);
                Assert.LessOrEqual(Mathf.Abs(s.b.y), envelope);
            }
        }

        [Test]
        public void AllCoordinates_AreFinite()
        {
            foreach (CushionSegment s in Config.segments)
            {
                Assert.IsFalse(float.IsNaN(s.a.x) || float.IsNaN(s.a.y), "段端点出现 NaN。");
                Assert.IsFalse(float.IsNaN(s.b.x) || float.IsNaN(s.b.y), "段端点出现 NaN。");
            }

            foreach (Pocket p in Config.pockets)
            {
                Assert.IsFalse(float.IsNaN(p.center.x) || float.IsNaN(p.center.y), "袋心出现 NaN。");
                Assert.IsFalse(float.IsNaN(p.throatNormal.x) || float.IsNaN(p.throatNormal.y), "袋法向出现 NaN。");
            }
        }

        [Test]
        public void Pockets_OccupyFourQuadrantsAndTwoSides()
        {
            var quadrants = new HashSet<(int, int)>();
            foreach (Pocket p in Config.pockets)
            {
                if (p.IsCorner)
                {
                    quadrants.Add((System.Math.Sign(p.center.x), System.Math.Sign(p.center.y)));
                }
                else
                {
                    Assert.Less(Mathf.Abs(p.center.x), 0.01f, "中袋应位于短轴中线附近（x≈0）。");
                    Assert.Greater(Mathf.Abs(p.center.y), PoolConstants.ClothHalfWidth, "中袋应在内库之下。");
                }
            }

            Assert.AreEqual(4, quadrants.Count, "4 角袋应分别占据 4 个象限。");
        }

        [Test]
        public void PocketCenters_LieOutsideInnerRimRectangle()
        {
            // 袋心必然落在内库矩形之外（袋口在库边后方）。
            foreach (Pocket p in Config.pockets)
            {
                float rx = Mathf.Abs(p.center.x) / PoolConstants.ClothHalfLength;
                float ry = Mathf.Abs(p.center.y) / PoolConstants.ClothHalfWidth;
                Assert.Greater(Mathf.Max(rx, ry), 1f, $"袋 {p.index} 中心落在内库矩形内，几何异常。");
            }
        }

        [Test]
        public void CushionEndpointsBoundingBox_MatchesBaseline()
        {
            // 交叉核对轴向/缩放（独立复核 D2-T02A 基线：x[-1.334342835,+1.334342911] / y[-0.699095331,+0.699095331]）。
            // 注意该包围盒 x 左右不对称仅 7.6e-8 m，故它抓轴交换/缩放，**不是镜像判据**（镜像由 CHR-01/CHR-02/CMP-03 负责）。
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (CushionSegment s in Config.segments)
            {
                min = Vector2.Min(min, Vector2.Min(s.a, s.b));
                max = Vector2.Max(max, Vector2.Max(s.a, s.b));
            }

            Assert.AreEqual(PoolGeometry.CushionEndpointsMin.x, min.x, 1e-5f);
            Assert.AreEqual(PoolGeometry.CushionEndpointsMin.y, min.y, 1e-5f);
            Assert.AreEqual(PoolGeometry.CushionEndpointsMax.x, max.x, 1e-5f);
            Assert.AreEqual(PoolGeometry.CushionEndpointsMax.y, max.y, 1e-5f);
        }

        [Test]
        public void OpeningRadius_EqualsRuntimeInscribedRadius()
        {
            // 独立复核给出的第二条派生关系（误差恰为 0）：openingRadius == runtimeInscribedRadius。
            for (int i = 0; i < _a.collision.pockets.Length; i++)
            {
                Assert.AreEqual(_a.collision.pockets[i].runtimeInscribedRadius,
                    Config.pockets[i].openingRadius, 1e-9f, $"袋 {i} openingRadius 不等于源 runtimeInscribedRadius。");
            }
        }

        [Test]
        public void Provenance_RecordsSourceShaAndCounts()
        {
            Assert.IsNotEmpty(Config.sourceSha256, "应记录源 JSON 的 sha256 以便追溯。");
            Assert.AreEqual(82, Config.SegmentCount);
            Assert.AreEqual(19954, Config.sourceTriangleCount);
            Assert.AreEqual(10572, Config.sourceVertexCount);
        }

        [Test]
        public void FootSpot_IsOnLongAxis()
        {
            // 源原型 foot_spot = (0.51, 0)：位于长轴上。
            Assert.AreEqual(0f, Config.footSpot.y, 1e-6f);
            Assert.Greater(Config.footSpot.x, 0f);
            Assert.Less(Config.footSpot.x, PoolConstants.ClothHalfLength);
        }

        // ── A/B 交叉比对 ───────────────────────────────────────────────────

        [Test]
        public void ReferenceFiles_AreMutuallyConsistent()
        {
            ReferenceCrossCheck.Result r = ReferenceCrossCheck.Compare(_a, _b);

            Assert.IsTrue(r.Identical,
                "A/B 两份源文件应逐点一致，但发现：" + string.Join(" | ", r.Mismatches));
            Assert.AreEqual(82, r.SegmentCountA);
            Assert.AreEqual(82, r.SegmentCountB);
            Assert.AreEqual(6, r.PocketCountA);
            Assert.AreEqual(6, r.PocketCountB);
            Assert.LessOrEqual(r.OverallMaxDelta, ReferenceCrossCheck.BitIdenticalTolerance);
        }

        [Test]
        public void ReferenceFiles_AgreeOnRandomAccessPocketFields()
        {
            // 直接按 index 抽查若干袋字段，防止"整体一致"掩盖字段级错配。
            foreach (PocketDto pa in _a.collision.pockets)
            {
                PocketBDto pb = null;
                foreach (PocketBDto cand in _b.pockets)
                {
                    if (cand.index == pa.index)
                    {
                        pb = cand;
                        break;
                    }
                }

                Assert.IsNotNull(pb, $"B 缺少 index={pa.index} 的袋。");
                Assert.AreEqual(pa.kind, pb.kind);
                Assert.AreEqual(pa.openingRadius, pb.physics2d.openingRadius, 1e-9f);
                Assert.AreEqual(pa.captureRadius, pb.physics2d.captureRadius, 1e-9f);
            }
        }

        // ── 生成资产 vs 源文件：索引对齐逐点比对 + 手性自检 ──────────────────

        [Test]
        public void Config_MatchesReference_IndexAligned()
        {
            // 独立复核建议的最强判据：索引对齐 + 逐字段 + 1e-5 m 容差。
            // 可一次性拦下镜像、180° 旋转、双重缩放、细微端点偏移等自洽检查放行的错误。
            const float tol = 1e-5f;
            Assert.AreEqual(_a.collision.segments.Length, Config.SegmentCount);

            for (int i = 0; i < _a.collision.segments.Length; i++)
            {
                SegmentDto s = _a.collision.segments[i];
                CushionSegment c = Config.segments[i];
                Assert.AreEqual(s.rail, c.rail, $"seg[{i}].rail 不一致");
                Assert.AreEqual(s.a.x, c.a.x, tol, $"seg[{i}].a.x");
                Assert.AreEqual(s.a.y, c.a.y, tol, $"seg[{i}].a.y");
                Assert.AreEqual(s.b.x, c.b.x, tol, $"seg[{i}].b.x");
                Assert.AreEqual(s.b.y, c.b.y, tol, $"seg[{i}].b.y");
            }

            for (int i = 0; i < _a.collision.pockets.Length; i++)
            {
                PocketDto p = _a.collision.pockets[i];
                Pocket c = Config.pockets[i];
                Assert.AreEqual(p.index, c.index);
                Assert.AreEqual(p.kind, c.kind);
                Assert.AreEqual(p.runtimeCenter.x, c.center.x, tol);
                Assert.AreEqual(p.runtimeCenter.y, c.center.y, tol);
                Assert.AreEqual(p.runtimeMouth[0].x, c.mouthA.x, tol);
                Assert.AreEqual(p.runtimeMouth[1].y, c.mouthB.y, tol);
                Assert.AreEqual(p.runtimeThroat[0].x, c.throatA.x, tol);
                Assert.AreEqual(p.runtimeThroat[1].y, c.throatB.y, tol);
                Assert.AreEqual(p.openingRadius, c.openingRadius, tol);
                Assert.AreEqual(p.captureRadius, c.captureRadius, tol);
            }
        }

        [Test]
        public void PocketChirality_MatchesExpectedPattern()
        {
            // 自包含镜像判据：proper 旋转下不变，水平面镜像会整列翻转。
            int[] expected = { 1, -1, -1, -1, 1, 1 };
            Assert.AreEqual(expected.Length, Config.PocketCount);

            for (int i = 0; i < expected.Length; i++)
            {
                Pocket p = Config.GetPocket(i);
                Assert.IsNotNull(p, $"缺少 index={i} 的袋。");
                Vector2 mv = p.mouthB - p.mouthA;
                Vector2 mc = (p.mouthA + p.mouthB) * 0.5f;
                Vector2 iv = p.throatCenter - mc;
                float cr = mv.x * iv.y - mv.y * iv.x;
                int sign = cr >= 0f ? 1 : -1;
                Assert.AreEqual(expected[i], sign, $"袋 {i} 手性符号不符（疑似水平面镜像）。");
            }
        }

        [Test]
        public void SegmentPerimeter_IsCounterClockwise_MirrorDetector()
        {
            // 把 82 段按 rail 分组后贪心拼接，shoelace 的**符号**即绕向。
            // 该链并非严格闭合环（首尾留 ~178 mm 缺口），但 shoelace 对"链+首尾连线"闭合多边形有定义；
            // 水平面镜像会翻转其符号（proper 旋转不变），故用符号做镜像自检，并钉住量级。
            List<Vector2> path = StitchPerimeter();
            Assert.Greater(path.Count, 2);

            float area2 = 0f;
            for (int i = 0; i < path.Count - 1; i++)
            {
                area2 += path[i].x * path[i + 1].y - path[i + 1].x * path[i].y;
            }

            float area = area2 * 0.5f;
            Assert.Greater(area, 0f, "绕向应为 CCW（正有向面积）；为负说明发生了水平面镜像。");
            Assert.AreEqual(3.144285f, area, 0.01f, "拼接轮廓有向面积量级不符（可检出整体缩放错误）。");
        }

        private static List<Vector2> StitchPerimeter()
        {
            var groups = new Dictionary<string, List<CushionSegment>>();
            var order = new List<string>();
            foreach (CushionSegment s in Config.segments)
            {
                if (!groups.TryGetValue(s.rail, out List<CushionSegment> list))
                {
                    list = new List<CushionSegment>();
                    groups[s.rail] = list;
                    order.Add(s.rail);
                }

                list.Add(s);
            }

            var chains = new Dictionary<string, List<Vector2>>();
            foreach (KeyValuePair<string, List<CushionSegment>> kv in groups)
            {
                var pts = new List<Vector2> { kv.Value[0].a };
                foreach (CushionSegment s in kv.Value)
                {
                    pts.Add(s.b);
                }

                chains[kv.Key] = pts;
            }

            var path = new List<Vector2>(chains[order[0]]);
            chains.Remove(order[0]);
            while (chains.Count > 0)
            {
                Vector2 tail = path[path.Count - 1];
                string bestRail = null;
                bool reverse = false;
                float best = float.MaxValue;
                foreach (KeyValuePair<string, List<Vector2>> kv in chains)
                {
                    float d1 = Vector2.Distance(kv.Value[0], tail);
                    float d2 = Vector2.Distance(kv.Value[kv.Value.Count - 1], tail);
                    if (d1 < best) { best = d1; bestRail = kv.Key; reverse = false; }
                    if (d2 < best) { best = d2; bestRail = kv.Key; reverse = true; }
                }

                List<Vector2> chain = chains[bestRail];
                chains.Remove(bestRail);
                if (reverse)
                {
                    chain = new List<Vector2>(chain);
                    chain.Reverse();
                }

                path.AddRange(chain);
            }

            return path;
        }

        // ── 轴向转换 ───────────────────────────────────────────────────────

        [Test]
        public void AxisMapping_RoundTrips()
        {
            var samples = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1.27f, 0.635f),
                new Vector2(-1.2914f, -0.6561f),
                new Vector2(0.51f, 0f),
            };

            foreach (Vector2 p in samples)
            {
                Vector3 w = PoolGeometry.PhysicsToUnity(p);
                Vector2 back = PoolGeometry.UnityToPhysics(w);
                Assert.AreEqual(p.x, back.x, 1e-6f);
                Assert.AreEqual(p.y, back.y, 1e-6f);
                Assert.AreEqual(PoolConstants.ClothHeightY, w.y, 1e-6f);
            }
        }

        [Test]
        public void ModelAxisCorrection_MapsModelUpToUnityUp_AndPreservesChirality()
        {
            // 实测：Unity 导入后 FBX 局部顶点 = (−px, py, up)。修正 (−m.x, m.z, m.y) 把"上"放到 Unity +Y。
            Vector3 up = PoolGeometry.ModelVertexToUnity(new Vector3(0f, 0f, 1f));
            Assert.AreEqual(0f, up.x, 1e-6f);
            Assert.AreEqual(1f, up.y, 1e-6f);
            Assert.AreEqual(0f, up.z, 1e-6f);

            // 模型长轴 X 在 Unity 里取反（Unity 导入时已做 X 取反的 handedness 转换，
            // 因此这里再取反一次即"还原"为物理平面的 +X）。
            Vector3 x = PoolGeometry.ModelVertexToUnity(new Vector3(1f, 0f, 0f));
            Assert.AreEqual(-1f, x.x, 1e-6f);

            // 保距（纯旋转，无缩放）
            var probe = new Vector3(0.3f, -0.7f, 1.1f);
            Assert.AreEqual(probe.magnitude, PoolGeometry.ModelVertexToUnity(probe).magnitude, 1e-5f);

            // 分量式函数与四元数式等价（保证两条 API 不会漂移）
            Vector3 q = PoolGeometry.ModelAxisCorrection * probe;
            Vector3 f = PoolGeometry.ModelVertexToUnity(probe);
            Assert.AreEqual(q.x, f.x, 1e-5f);
            Assert.AreEqual(q.y, f.y, 1e-5f);
            Assert.AreEqual(q.z, f.z, 1e-5f);

            // 行列式 = +1：必须是纯旋转而非镜像，否则会翻转三角形绕序/法线。
            Vector3 ex = PoolGeometry.ModelVertexToUnity(Vector3.right);
            Vector3 ey = PoolGeometry.ModelVertexToUnity(Vector3.up);
            Vector3 ez = PoolGeometry.ModelVertexToUnity(Vector3.forward);
            float det = Vector3.Dot(Vector3.Cross(ex, ey), ez);
            Assert.AreEqual(1f, det, 1e-5f, "轴修正必须保手性（det=+1）。");
        }

        [Test]
        public void Mesh_UnderAxisCorrection_MatchesReferenceBounds()
        {
            List<Vector3> verts = LoadMeshVertices();
            Assert.Greater(verts.Count, 0, "无法读取网格顶点（isReadable？）。");

            // LoadMeshVertices 返回的已是应用唯一轴修正后的 Unity 世界坐标。
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Vector3 w in verts)
            {
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }

            // 修正后包围盒 = 源 bounds_runtime（X 的不对称符号被钉住，可判手性未翻转）
            Assert.AreEqual(PoolGeometry.OuterBoundsMin.x, min.x, 1e-4f, "网格长轴 −X 端不符。");
            Assert.AreEqual(PoolGeometry.OuterBoundsMax.x, max.x, 1e-4f, "网格长轴 +X 端不符。");
            Assert.AreEqual(PoolGeometry.OuterBoundsMin.y, min.y, 1e-4f, "网格最小 Y 应为桌腿底。");
            Assert.AreEqual(PoolGeometry.OuterBoundsMax.y, max.y, 1e-4f, "网格最大 Y 应为库顶。");
            Assert.AreEqual(PoolGeometry.OuterBoundsMin.z, min.z, 1e-4f);
            Assert.AreEqual(PoolGeometry.OuterBoundsMax.z, max.z, 1e-4f);

            // 长轴必须是 X
            Assert.Greater(max.x - min.x, max.z - min.z, "长轴应为 Unity X。");
        }

        [Test]
        public void Mesh_SegmentAndPocketEndpointsAlignWithVertices()
        {
            List<Vector3> verts = LoadMeshVertices();
            Assert.Greater(verts.Count, 0, "无法读取网格顶点。");

            float worst = 0f;
            foreach (CushionSegment s in Config.segments)
            {
                worst = Mathf.Max(worst, NearestHorizontal(verts, PoolGeometry.PhysicsToUnity(s.a)));
                worst = Mathf.Max(worst, NearestHorizontal(verts, PoolGeometry.PhysicsToUnity(s.b)));
            }

            foreach (Pocket p in Config.pockets)
            {
                worst = Mathf.Max(worst, NearestHorizontal(verts, PoolGeometry.PhysicsToUnity(p.mouthA)));
                worst = Mathf.Max(worst, NearestHorizontal(verts, PoolGeometry.PhysicsToUnity(p.mouthB)));
                worst = Mathf.Max(worst, NearestHorizontal(verts, PoolGeometry.PhysicsToUnity(p.throatA)));
                worst = Mathf.Max(worst, NearestHorizontal(verts, PoolGeometry.PhysicsToUnity(p.throatB)));
            }

            Assert.Less(worst, VertexTolerance,
                $"物理轮廓端点与网格顶点最大水平偏差 {worst * 1000f:F4} mm 超过 {VertexTolerance * 1000f:F1} mm —— "
                + "说明轴向映射、缩放或几何对齐不正确。");
        }

        // ── helpers ────────────────────────────────────────────────────────

        /// <summary>加载球桌网格顶点，并应用唯一轴修正，返回 Unity 世界坐标。</summary>
        private static List<Vector3> LoadMeshVertices()
        {
            var result = new List<Vector3>();
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            foreach (UnityEngine.Object o in assets)
            {
                if (o is Mesh m)
                {
                    foreach (Vector3 v in m.vertices)
                    {
                        result.Add(PoolGeometry.ModelVertexToUnity(v));
                    }
                }
            }

            return result;
        }

        private static float NearestHorizontal(List<Vector3> verts, Vector3 q)
        {
            // 物理是二维的：只比较水平面 XZ。库鼻高度 44.98 mm 与台呢高度本就不同面，不参与对齐判据。
            float best = float.MaxValue;
            for (int i = 0; i < verts.Count; i++)
            {
                float dx = verts[i].x - q.x;
                float dz = verts[i].z - q.z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }

            return Mathf.Sqrt(best);
        }
    }
}
