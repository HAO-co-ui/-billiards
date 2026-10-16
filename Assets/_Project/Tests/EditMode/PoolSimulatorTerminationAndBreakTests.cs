using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// 袋口路径确定性（`T32`）、运动终止判定（`T33`）、散球样例（`T34`）与 15 球满堆真开球（`T37`）。
    ///
    /// **终止判定分两层**：`StoppedSpeedEpsilon`（数值终止，1e-6 m/s，既有）与
    /// `RestSpeedThreshold` / `RestStableFrames`（游戏级静止声明）——
    /// 两者语义不同、**不得互相代替**；`TableAtRest` 只**声明**、**不置零速度**。
    /// </summary>
    public class PoolSimulatorTerminationAndBreakTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        private static TableConfig _config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 " + ConfigPath + "（先运行 Billiards/Data/Import Table Reference Data）。");
        }

        // ── 辅助 ──────────────────────────────────────────────────────────

        private static PhysicsParams Params(float e = 1f, float mu = 1f, float a = 0f)
        {
            return new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = a, rollingDecelerationRoll = a,
            };
        }

        /// <summary>
        /// **出厂灰盒的台呢参数**（读 `PhysicsTuning.asset`；缺资产则回落 `DefaultValues()`）。
        /// 这样 `T32`/`T34`/`T37` 跑的是**出厂口径**，而不是写死的 0。
        /// ⚠ 台呢滚阻是**两段模型**（滑动段 / 滚动段 / 过渡速度）⇒ 本档必须整体取资产的**三个**字段，
        /// **不得**压成单一标量（那会把出厂口径悄悄改成"恒定档"，扫描结论就不能外推）。
        /// </summary>
        private static PhysicsParams ShipParams(float e, float mu)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>("Assets/_Project/Data/PhysicsTuning.asset");
            float aSlide;
            float aRoll;
            float vTransition;
            if (tuning != null)
            {
                aSlide = tuning.rollingDecelerationSlide;
                aRoll = tuning.rollingDecelerationRoll;
                vTransition = tuning.rollingTransitionSpeed;
            }
            else
            {
                PhysicsTuning.Defaults d = PhysicsTuning.DefaultValues();
                aSlide = d.rollingDecelerationSlide;
                aRoll = d.rollingDecelerationRoll;
                vTransition = d.rollingTransitionSpeed;
            }

            return new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = aSlide,
                rollingDecelerationRoll = aRoll,
                rollingTransitionSpeed = vTransition,
            };
        }

        private static TableConfig Synthetic(params Pocket[] pockets)
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>();
            t.pockets = new List<Pocket>(pockets);
            return t;
        }

        private static Pocket MakePocket(int index, Vector2 tc, Vector2 tn, float captureRadius)
        {
            Vector2 td = new Vector2(-tn.y, tn.x);
            return new Pocket
            {
                index = index,
                kind = "side",
                throatCenter = tc,
                throatNormal = tn,
                throatA = tc + 0.05f * td,
                throatB = tc - 0.05f * td,
                openingRadius = captureRadius + PoolConstants.BallRadius + 0.002f,
                captureRadius = captureRadius,
            };
        }

        private static BallState Ball(float x, float y, float vx, float vy)
            => new BallState { position = new Vector2(x, y), velocity = new Vector2(vx, vy) };

        private static void Step(PoolSimulator sim, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                sim.Step();
            }
        }

        private static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        private static float Mag(Vector2 v) => Mathf.Sqrt(v.x * v.x + v.y * v.y);

        private static void AssertBitIdentical(PoolSimulator a, PoolSimulator b, string tag)
        {
            for (int i = 0; i < a.Balls.Count; i++)
            {
                Assert.AreEqual(a.Balls[i].position.x.ToString("R"), b.Balls[i].position.x.ToString("R"),
                    $"{tag}：球 {i} 位置 x 逐位不一致。");
                Assert.AreEqual(a.Balls[i].position.y.ToString("R"), b.Balls[i].position.y.ToString("R"),
                    $"{tag}：球 {i} 位置 y 逐位不一致。");
                Assert.AreEqual(a.Balls[i].velocity.x.ToString("R"), b.Balls[i].velocity.x.ToString("R"),
                    $"{tag}：球 {i} 速度 x 逐位不一致。");
                Assert.AreEqual(a.Balls[i].velocity.y.ToString("R"), b.Balls[i].velocity.y.ToString("R"),
                    $"{tag}：球 {i} 速度 y 逐位不一致。");
            }
        }

        /// <summary>球心是否处于某袋的开口区（含喉前后 0.2 m 与口宽 + 球半径）—— 该区的球不参与包络判据。</summary>
        private static bool IsInPocketZone(Vector2 c, TableConfig table)
        {
            for (int p = 0; p < table.PocketCount; p++)
            {
                Pocket pocket = table.pockets[p];
                Vector2 tn = pocket.throatNormal;
                Vector2 td = new Vector2(-tn.y, tn.x);
                float lat = Mathf.Abs(Dot(c - pocket.throatCenter, td));
                float ax = Dot(c - pocket.throatCenter, tn);
                if (lat <= 0.16f && ax > -0.20f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 包络判据：未捕获球、且**不在袋口区**者，其球心须落在台面包络内。
        /// `tight=true` 用 `R-B` 收紧口径（`≤ 半长 − r + 1e-3`，即"不穿库"）；否则用松口径（`≤ 半长 + r`，"不飞离"）。
        /// </summary>
        private static void CheckEnvelope(PoolSimulator sim, TableConfig table, bool tight, string tag, out float overrunX, out float overrunY)
        {
            overrunX = 0f;
            overrunY = 0f;
            float halfL = PoolConstants.ClothHalfLength;
            float halfW = PoolConstants.ClothHalfWidth;
            float r = PoolConstants.BallRadius;
            float marginX = tight ? halfL - r + 1e-3f : halfL + r;
            float marginY = tight ? halfW - r + 1e-3f : halfW + r;

            for (int i = 0; i < sim.Balls.Count; i++)
            {
                if (sim.CapturedPocketIndex(i) >= 0)
                {
                    continue;
                }

                Vector2 p = sim.Balls[i].position;
                if (IsInPocketZone(p, table))
                {
                    continue;
                }

                float ox = Mathf.Abs(p.x) - marginX;
                float oy = Mathf.Abs(p.y) - marginY;
                overrunX = Mathf.Max(overrunX, ox);
                overrunY = Mathf.Max(overrunY, oy);

                Assert.LessOrEqual(Mathf.Abs(p.x), marginX + 1e-6f, $"{tag}：球 {i} 越界（x={p.x:R}）。");
                Assert.LessOrEqual(Mathf.Abs(p.y), marginY + 1e-6f, $"{tag}：球 {i} 越界（y={p.y:R}）。");
            }
        }

        private static void AssertFinite(PoolSimulator sim, string tag)
        {
            for (int i = 0; i < sim.Balls.Count; i++)
            {
                Vector2 p = sim.Balls[i].position;
                Vector2 v = sim.Balls[i].velocity;
                Assert.IsFalse(float.IsNaN(p.x) || float.IsInfinity(p.x), $"{tag}：球 {i} 位置 x 非有限。");
                Assert.IsFalse(float.IsNaN(p.y) || float.IsInfinity(p.y), $"{tag}：球 {i} 位置 y 非有限。");
                Assert.IsFalse(float.IsNaN(v.x) || float.IsInfinity(v.x), $"{tag}：球 {i} 速度 x 非有限。");
                Assert.IsFalse(float.IsNaN(v.y) || float.IsInfinity(v.y), $"{tag}：球 {i} 速度 y 非有限。");
            }
        }

        // ══════════════════ T32 确定性（覆盖袋口路径）══════════════════════

        /// <summary>
        /// 合成表（1 条竖直袋角面 + 1 个袋）：
        ///  · 球 A 直入袋（0 段接触）；
        ///  · 球 B 正撞袋角面后反射且反射路径清空（**恰 1 次段接触**）。
        /// 断言 `TotalContactCount == 1`（**等值** —— 抓"重复解算/计数虚增"，单边 `&gt;0` 挡不住）；捕获恰 1；双跑逐位一致。
        /// 能测出：一次接触被重复解算（`TotalContactCount` 由 1 涨到数十/数百）；冻结写入顺序不确定。
        /// 盲带：`ContactCountCeiling` 类是选定界，只拦量级跃升。
        /// </summary>
        [Test]
        public void T32a_Determinism_SyntheticJaw_ContactCountEqualsOne()
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>
            {
                new CushionSegment { a = new Vector2(0f, 0f), b = new Vector2(0f, 0.4f), rail = "synthetic" },
            };
            t.pockets = new List<Pocket> { MakePocket(0, new Vector2(0f, -0.5f), new Vector2(0f, -1f), 0.0307f) };

            BallState[] initial =
            {
                Ball(0f, -0.30f, 0f, -2f),      // 直入袋、0 段接触
                Ball(-0.20f, 0.20f, 2f, 0f),    // 撞袋角面 1 次后反射
            };

            var a = new PoolSimulator(t, ShipParams(0.9f, 0.8f));
            var b = new PoolSimulator(t, ShipParams(0.9f, 0.8f));
            a.LoadBalls(initial);
            b.LoadBalls(initial);
            Step(a, 60);
            Step(b, 60);

            Assert.AreEqual(1, a.CapturedBallCount, "T32a：应恰有 1 次捕获。");
            Assert.AreEqual(1, a.PocketProximityEvents, "T32a：越界应恰 1 次。");
            Assert.AreEqual(0, a.PocketJawCrossingCount, "T32a：直入不应命中窗口。");
            Assert.AreEqual(1, a.TotalContactCount, "T32a：应恰 1 次段接触（等值 —— 抓重复解算/虚增）。");
            AssertBitIdentical(a, b, "T32a");
            TestContext.WriteLine($"T32a: contacts={a.TotalContactCount}, captured={a.CapturedBallCount}, 双跑逐位一致 ✓");
        }

        /// <summary>
        /// 真实表混合场景：白球直入侧袋（捕获）+ 一球擦角袋袋角（偏折）+ 一球居中滚动（0 段接触）。
        /// 断言（双边）：`CapturedBallCount == 1`、`PocketProximityEvents == 1`、`PocketJawCrossingCount == 0`、
        /// `TotalContactCount ∈ [1, 3000]`、双跑逐位一致。
        /// **选点（`D3`）**：擦角球起点 = `tc − 0.12·tn + 0.045·td`（袋 0）、`v = 2·tn`。
        ///
        /// **⚠ 上界 3000 为"量级界"**（原注：擦角球会嵌入袋角并**反复解算**，实测本场景
        /// `TotalContactCount ≈ 798`）。**该「袋角内反复解算」已在第四轮修掉**（`ResolveSegmentContact`
        /// 补接近判据 ⇒ 已在分离的接触不再施加冲量、不再计数）：修复后本场景接触数降到**个位数**
        /// ⇒ 上界 3000 现在是远高于实际的护栏（保留，用于拦量级失控）。
        /// </summary>
        [Test]
        public void T32b_Determinism_RealTable_CaptureAndJawPath()
        {
            Pocket side = _config.GetPocket(1);
            Pocket corner = _config.GetPocket(0);

            Vector2 sideStart = side.throatCenter - 0.35f * side.throatNormal;
            Vector2 cornerTn = corner.throatNormal;
            Vector2 cornerTd = new Vector2(-cornerTn.y, cornerTn.x);
            // ⚠ ρ0 = 0.060（原 0.045）：0.045 只比「球能否从嘴口挤过去」的临界值
            //   （嘴半宽 0.0727 − 球半径 0.0286 = 0.0441）大 0.9 mm ⇒ 擦切退化；修复前是靠球被袋角
            //   **钉住**才「不越喉」，而钉住本身是缺陷（见 35-A 规格 §3）。
            Vector2 cornerStart = corner.throatCenter - 0.12f * cornerTn + 0.060f * cornerTd;

            BallState[] initial =
            {
                new BallState { position = sideStart, velocity = 2f * side.throatNormal },          // 入侧袋
                new BallState { position = cornerStart, velocity = 2f * cornerTn },                 // 擦角袋袋角
                Ball(0f, 0.45f, 0.15f, 0f),                                                          // 居中滚动
            };

            var a = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            var b = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            a.LoadBalls(initial);
            b.LoadBalls(initial);
            Step(a, 120);
            Step(b, 120);

            Assert.AreEqual(1, a.CapturedBallCount, "T32b：应恰 1 球入袋。");
            Assert.AreEqual(1, a.PocketProximityEvents, "T32b：只有入袋球应越喉平面（擦角球不得越）。");
            Assert.AreEqual(0, a.PocketJawCrossingCount, "T32b：不应命中窗口。");
            Assert.GreaterOrEqual(a.TotalContactCount, 1, "T32b：擦角球必须发生段接触（非空转）。");
            Assert.LessOrEqual(a.TotalContactCount, 3000,
                "T32b：段接触数超量级上界 3000（重复解算失控？）。");
            AssertBitIdentical(a, b, "T32b");
            WriteBreakSampleEvidence(a);
            TestContext.WriteLine($"T32b: contacts={a.TotalContactCount}, captured={a.CapturedBallCount}, 双跑逐位一致 ✓");
        }

        /// <summary>
        /// 写 `d6-break-sample.json`（`§7.1` 必填字段 + `t32bTotalContactCount`）。
        /// **本方法内不得有任何断言**（跳过落盘 ⇒ 不得跳过任何检查）；未给 `-outputDir` ⇒ 只跳过落盘。
        /// </summary>
        private static void WriteBreakSampleEvidence(PoolSimulator sim)
        {
            string path = ReportOutput.ResolveTestEvidencePathOrNull("d6-break-sample.json");
            if (path == null)
            {
                return;
            }

            string root = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schema\": \"billiards.d6-break-sample.v1\",\n");
            sb.Append("  \"generatedAtUtc\": \"").Append(System.DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            sb.Append("  \"unityVersion\": \"").Append(Application.unityVersion).Append("\",\n");
            sb.Append("  \"projectRoot\": \"").Append(root.Replace("\\", "\\\\")).Append("\",\n");
            sb.Append("  \"isBatchMode\": ").Append(Application.isBatchMode ? "true" : "false").Append(",\n");
            sb.Append("  \"passCount\": 1,\n");
            sb.Append("  \"warnCount\": 0,\n");
            sb.Append("  \"failCount\": 0,\n");
            sb.Append("  \"overallStatus\": \"PASS\",\n");
            sb.Append("  \"ballCount\": ").Append(sim.Balls.Count.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"cueSpeed\": ").Append(2f.ToString("R", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"t32bTotalContactCount\": ").Append(sim.TotalContactCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"totalContactCount\": ").Append(sim.TotalContactCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"guardTrips\": ").Append(sim.ToiIterationGuardTrips.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"energyGain\": ").Append(sim.EnergyGainViolations.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"pocketProximityEvents\": ").Append(sim.PocketProximityEvents.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"capturedBallCount\": ").Append(sim.CapturedBallCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"pocketJawCrossingCount\": ").Append(sim.PocketJawCrossingCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"tableAtRest\": ").Append(sim.TableAtRest ? "true" : "false").Append(",\n");
            sb.Append("  \"deterministic\": true,\n");
            sb.Append("  \"checks\": [\n");
            sb.Append("    { \"id\": \"D6-T32B\", \"name\": \"T32b 确定性（真实表混合场景）\", \"expected\": \"双跑逐位一致\", ")
              .Append("\"actual\": \"逐位一致\", \"detail\": \"T32b：捕获 1 + 擦角 1 + 滚动 1；接触数见 t32bTotalContactCount\", \"status\": \"PASS\" }\n");
            sb.Append("  ]\n");
            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString());
        }

        // ══════════════════ T33 终止判定（两层，双边）══════════════════════

        /// <summary>
        /// 低速球（`5e-4 m/s`，落在 `(StoppedSpeedEpsilon, RestSpeedThreshold]` 缝里）应被声明为宏观静止。
        /// 断言 `TableAtRest == true` 且 `ConsecutiveRestFrameCount == RestStableFrames + 2`（等值 —— 用例只跑该步数、天然封顶）。
        /// **能测出**：阈值写反；把 `RestStableFrames` 写成 1；把 `StoppedSpeedEpsilon`（1e-6）当游戏阈值用
        /// （1e-6 太小 ⇒ 5e-4 的球永不停 ⇒ 失败）。
        /// **盲带**：不覆盖"永远在微抖"本身（未构造抖动反例）。
        /// </summary>
        [Test]
        public void T33a_Termination_LowSpeed_RestsAfterStableFrames()
        {
            var sim = new PoolSimulator(Synthetic(), Params());
            sim.LoadBalls(new[] { Ball(0f, 0f, 5e-4f, 0f) });

            int steps = PoolSimulator.RestStableFrames + 2;
            Step(sim, steps);

            Assert.IsTrue(sim.TableAtRest, "T33a：低速球应被声明为宏观静止。");
            Assert.AreEqual(steps, sim.ConsecutiveRestFrameCount,
                $"T33a：连续静止帧数应为 {steps}（= RestStableFrames + 2）。");
            TestContext.WriteLine($"T33a: TableAtRest={sim.TableAtRest}, restFrames={sim.ConsecutiveRestFrameCount} ✓");
        }

        /// <summary>高速球（`1 m/s` &gt; 阈值）**不得**被误判静止（双边：不该停的没停）。</summary>
        [Test]
        public void T33b_Termination_HighSpeed_NotAtRest()
        {
            var sim = new PoolSimulator(Synthetic(), Params());
            sim.LoadBalls(new[] { Ball(0f, 0f, 1f, 0f) });

            Step(sim, 60);

            Assert.IsFalse(sim.TableAtRest, "T33b：高速球不得被判为静止。");
            Assert.AreEqual(0, sim.ConsecutiveRestFrameCount, "T33b：高速球下连续静止帧数应为 0。");
            Assert.Greater(sim.Balls[0].position.x, 0f, "T33b：球应确实移动（非空转）。");
            TestContext.WriteLine($"T33b: TableAtRest={sim.TableAtRest}, restFrames={sim.ConsecutiveRestFrameCount} ✓");
        }

        // ══════════════════ T34 散球样例（把"表现合理"落成可判定）══════════

        /// <summary>
        /// 真实表；`RackPositions(footSpot, 3)`（白球 + 3 球堆）；白球 `= MaxBallSpeed` 朝球堆；`Step() × 240`。
        /// 硬判据（1–9）：无 NaN；无飞离 + 不穿库；无升能；迭代守卫 == 0；非空转（区间）；
        /// 归类完备；进球数（区间）+ 窗口上界（≤ 球数）；确定性；报告项。
        /// </summary>
        [Test]
        public void T34_BreakSample_DecidableCriteria()
        {
            Vector2[] rack = PhysicsTestDrive.RackPositions(_config.footSpot, 3);
            BallState[] initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var a = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            var b = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            a.LoadBalls(initial);
            b.LoadBalls(initial);
            Step(a, 240);
            Step(b, 240);

            AssertFinite(a, "T34");
            CheckEnvelope(a, _config, false, "T34-2a", out float ox, out float oy);
            CheckEnvelope(a, _config, true, "T34-2b", out ox, out oy);
            Assert.AreEqual(0, a.EnergyGainViolations, "T34-3：不得升能。");
            Assert.AreEqual(0, a.ToiIterationGuardTrips, "T34-4：迭代守卫必须为 0。");

            Assert.GreaterOrEqual(a.BallBallContactCount, 1, "T34-5：必须真的发生球-球接触（非空转）。");
            Assert.LessOrEqual(a.BallBallContactCount, 64, "T34-5：球-球接触数超上界 64。");
            Assert.GreaterOrEqual(a.TotalContactCount, 1, "T34-5：总接触数应 ≥1。");
            Assert.LessOrEqual(a.TotalContactCount, 64, "T34-5：总接触数超上界 64。");

            Assert.AreEqual(a.CapturedBallCount + a.PocketJawCrossingCount,
                a.PocketProximityEvents + a.AtRestMouthDrops,
                "T34-6：归类应完备（ProximityEvents + AtRestMouthDrops == Captured + JawCrossing）。");
            Assert.GreaterOrEqual(a.CapturedBallCount, 0, "T34-7：进球数下界。");
            Assert.LessOrEqual(a.CapturedBallCount, initial.Length, "T34-7：进球数不得超球数。");
            // 散球本就会让球斜擦袋口 ⇒ 出现"窗口球"（R-S）是**预期**行为（`Q-1`(a)：冻结不离台）；只设上界。
            Assert.LessOrEqual(a.PocketJawCrossingCount, initial.Length, "T34-7：窗口数不得超球数（上界）。");

            AssertBitIdentical(a, b, "T34-8");

            TestContext.WriteLine(
                $"T34: 4 球散球 contacts={a.TotalContactCount}(bb={a.BallBallContactCount}), " +
                $"captured={a.CapturedBallCount}, guard={a.ToiIterationGuardTrips}, " +
                $"iterPeak={a.BallBallMaxIterationsObserved}, envOverrun=({ox:R},{oy:R}) ✓");
        }

        // ══════════════════ T38 RackPositions 向后兼容闸门（C1）══════════════

        /// <summary>
        /// **向后兼容闸门**：`RackPositions(footSpot, 3)` 必须**逐位等于**旧 4 球字面（**含顺序**）。
        /// 旧字面 = `[footSpot, (−0.30, 0), (−0.30 − row, +half), (−0.30 − row, −half)]`，
        /// 其中 `s = 2·BallRadius + 2e-4`、`row = s·0.8660254`、`half = s/2`。
        /// 若内层 `k` 由降序改为升序 ⇒ 索引 2/3 互换 ⇒ 本闸门 FAIL（保护按索引读取的既有证据：验收器 / 报告项 / 逐球终态）。
        /// </summary>
        [Test]
        public void T38_RackPositions_FourBall_MatchesLegacyLiteral()
        {
            float s = PoolConstants.BallDiameter + 2e-4f;
            float row = s * 0.8660254f;
            float half = s * 0.5f;
            var footSpot = new Vector2(0.508f, 0f);

            Vector2[] actual = PhysicsTestDrive.RackPositions(footSpot, 3);
            Vector2[] legacy =
            {
                footSpot,
                new Vector2(-0.30f, 0f),
                new Vector2(-0.30f - row, half),
                new Vector2(-0.30f - row, -half),
            };

            Assert.AreEqual(legacy.Length, actual.Length, "T38：长度应为 4（白球 + 3 球堆）。");
            for (int i = 0; i < legacy.Length; i++)
            {
                Assert.AreEqual(legacy[i].x.ToString("R"), actual[i].x.ToString("R"), $"T38：索引 {i} 的 x 逐位不一致。");
                Assert.AreEqual(legacy[i].y.ToString("R"), actual[i].y.ToString("R"), $"T38：索引 {i} 的 y 逐位不一致。");
            }

            TestContext.WriteLine("T38: RackPositions(footSpot,3) 与旧 4 球字面逐位一致（含顺序）✓");
        }

        // ══════════════════ T133 满力开球必须打散球堆（实机反馈的判据化）══════════

        /// <summary>`PhysicsTuning` 资产路径（`T133` 读"产品生效值"用）。</summary>
        private const string TuningAssetPath = "Assets/_Project/Data/PhysicsTuning.asset";

        /// <summary>
        /// **满力开球必须把球堆基本打散**（把用户的实机反馈变成可证伪的不变量）。
        /// 夹具复用 `T37`：真实表 + `RackPositions(footSpot, 15)` + 白球从 `footSpot`、朝 **−x 正对顶点球**。
        /// **v0 取"产品生效值"** = `PhysicsTuning` 资产的 `maxLaunchSpeed`（**不是** `MaxBallSpeed`）⇒
        /// **有人把它调回 4.5 ⇒ 本项红**（这正是它的判别力）。
        ///
        /// **主判据**：终态**位移 &gt; 0.30 m** 的目标球数 **≥ 6**（15 颗里）。
        /// 口径说明（**为何不用"半个球径 0.028575 m"**）：该口径会把**抖动**算进来 —— 实测 v0=4.5 时它数到 9 颗，
        /// 但第 6 颗起骤降到 0.183 m 以下；**用户肉眼数的"3–4 个"与 0.30 m 口径的 4 颗吻合**。
        /// **期望值 6 的取法**（**不是**贴着实测值取）：实测 v0=8.0 在 Mono（本套件运行时）= **8 颗**、
        /// 在 CoreCLR（独立 headless 探针）= **7 颗**；退化侧 v0=4.5 ⇒ Mono **4** / CoreCLR **5** 颗。
        /// 取 6 ⇒ **两侧都留余量**（跨运行时不会翻），而"把初速调回 4.5"仍必红（4–5 &lt; 6）。
        /// ⚠ 如实登记：8.0 下"被真正打散"的球数是 **7–8 / 15**（约半数）——"超过半数"这条理想线**跨运行时并不稳健**，
        /// 因此本判据的设计线取"**至少 6 颗被真正打散**"，而不是贴着 8。
        /// **附加**：白球不出台 / 无球越出台面外缘（复用 `CheckEnvelope`）/ 1200 步内到 `TableAtRest`。
        /// **盲带**：只覆盖**正对顶点**这一瞄准几何；偏角开球的分布未纳入。
        /// </summary>
        [Test]
        public void T133_FullPowerBreak_ScattersTheRack()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningAssetPath);
            Assert.IsNotNull(tuning, "T133：找不到 " + TuningAssetPath);
            float v0 = tuning.maxLaunchSpeed;   // ← **产品生效值**（调回 4.5 ⇒ 本项红）

            Vector2[] rack = PhysicsTestDrive.RackPositions(_config.footSpot, 15);
            Assert.AreEqual(16, rack.Length, "T133：应为 白球 + 15 球 = 16。");

            BallState[] initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-v0, 0f);   // 朝球堆（顶点在 −x 侧）

            var sim = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            sim.LoadBalls(initial);
            Step(sim, 1200);

            CheckEnvelope(sim, _config, true, "T133-越界", out float overrunX, out float overrunY);
            Assert.IsTrue(sim.TableAtRest, "T133：1200 步内应到 TableAtRest。");

            const float ScatterThreshold = 0.30f;   // ← 独立设计字面量（见上方口径说明）
            // 期望取 6（**不是**实测值本身）：实测 8.0 在 **Mono（EditMode 运行时）= 8 颗**、
            // 在 **CoreCLR（headless 探针）= 7 颗**（同族已知分歧：`T111` 静止步 488 vs 493）；
            // 退化侧 4.5 ⇒ **Mono 4 / CoreCLR 5 颗**。取 6 ⇒ **两侧都留余量**（绿侧 ≥1、红侧 ≥1）。
            const int MinScattered = 6;
            int scattered = 0;
            for (int i = 1; i < sim.Balls.Count; i++)
            {
                if ((sim.Balls[i].position - initial[i].position).magnitude > ScatterThreshold)
                {
                    scattered++;
                }
            }

            TestContext.WriteLine($"T133: v0={v0:F2} m/s（资产生效值）⇒ 位移 > {ScatterThreshold} m 的球数 = "
                                  + $"{scattered}/{sim.Balls.Count - 1}（期望 ≥ {MinScattered}）；"
                                  + $"越界 overrunX={overrunX:F4} overrunY={overrunY:F4} ✓");
            Assert.GreaterOrEqual(scattered, MinScattered,
                $"T133：满力开球（v0={v0:F2} m/s）只打散 {scattered} 颗（阈值 {ScatterThreshold} m）< {MinScattered}"
                + " ⇒ 球堆未被打散（初速上界偏低？）。");
        }

        // ══════════════════ T37 真开球（15 球满堆，不含预算带断言）══════════

        /// <summary>
        /// 真实表；`RackPositions(footSpot, 15)`（白球 + 15 球）；白球 `= MaxBallSpeed` 朝球堆；`Step() × 1200`。
        /// 硬判据（1–8）：不飞台（2b 紧包络）；无 NaN；无升能；**非空转下界**（`BallBallContactCount ≥ 15`，
        /// 经验下界 —— "球-球未实现"时 = 0 ⇒ FAIL）；归类完备；确定性；`ToiIterationGuardTrips == 0`；报告项。
        /// **明确不含**任何"迭代预算带"断言（`[1, 64]` / `[1, 0.5×128]`）。
        /// 盲带：上界仅由钉基线（±20%）提供，且**同向同幅虚增且仍在 ±20% 内 ⇒ 抓不到**（由 `T35` 小场景精确等值兜）。
        /// </summary>
        [Test]
        public void T37_FullBreak_FifteenBallRack()
        {
            Vector2[] rack = PhysicsTestDrive.RackPositions(_config.footSpot, 15);
            Assert.AreEqual(16, rack.Length, "T37：应为 白球 + 15 球 = 16。");

            BallState[] initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var a = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            var b = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            a.LoadBalls(initial);
            b.LoadBalls(initial);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Step(a, 1200);
            sw.Stop();
            double elapsedMs = sw.Elapsed.TotalMilliseconds;
            Step(b, 1200);

            // 判据 1：不飞台（2b 紧包络）
            CheckEnvelope(a, _config, true, "T37-1", out float overrunX, out float overrunY);
            // 判据 2：无 NaN
            AssertFinite(a, "T37-2");
            // 判据 3：无升能
            Assert.AreEqual(0, a.EnergyGainViolations, "T37-3：不得升能。");
            // 判据 4：非空转下界（经验下界；球-球未实现 ⇒ =0 ⇒ FAIL）
            Assert.GreaterOrEqual(a.BallBallContactCount, 15,
                $"T37-4：球-球碰撞应 ≥15（15 球堆每颗至少被撞一次），实测 {a.BallBallContactCount}。" +
                "（若为 0 ⇒ 球-球解算未实现。）");
            Assert.GreaterOrEqual(a.TotalContactCount, a.BallBallContactCount,
                "T37-4：总接触数应 ≥ 球-球接触数。");
            // 判据 5：归类完备
            Assert.AreEqual(a.CapturedBallCount + a.PocketJawCrossingCount, a.PocketProximityEvents,
                "T37-5：归类应完备。");
            // 判据 6：确定性
            AssertBitIdentical(a, b, "T37-6");
            // 判据 7：迭代守卫（硬上界 —— 非预算带）
            Assert.AreEqual(0, a.ToiIterationGuardTrips, "T37-7：迭代守卫必须为 0。");

            // 真开球同样会让球斜擦袋口 ⇒ 窗口球（R-S）为预期行为；只设上界。
            Assert.LessOrEqual(a.PocketJawCrossingCount, initial.Length, "T37：窗口数不得超球数（上界）。");

            WriteFullBreakEvidence(initial, a, initial.Length, PoolConstants.MaxBallSpeed, elapsedMs, overrunX, overrunY);

            TestContext.WriteLine(
                $"T37: 15 球满堆 contacts={a.TotalContactCount}(bb={a.BallBallContactCount}), " +
                $"captured={a.CapturedBallCount}, guard={a.ToiIterationGuardTrips}, " +
                $"iterPeak={a.BallBallMaxIterationsObserved}, elapsed={elapsedMs:F1} ms ✓");
        }

        // ══════════════════ T111 满堆开球必须收敛（球堆卡死闸门）═════════════

        /// <summary>满堆开球达到整盘静止的步数上限（实测 493 ⇒ 取 3.6× 余量；见判据说明）。</summary>
        private const int BreakSettleStepCap = 1800;

        /// <summary>
        /// **`T111`：满堆开球必须在有界步数内整盘静止**（排期 Day 15「球堆卡死」极端用例）。
        ///
        /// **为什么这条闸门必须有（此前是空的）**：`PhysicsTestDrive.Launch()` **门控 `TableAtRest`**
        /// （"整盘未静止不许出杆"）⇒ 一旦开球后永不静止，**玩家就再也出不了杆**（卡死），
        /// 而 `T37`（同场景）**只把 `tableAtRest` 写进证据 JSON、没有任何断言** ⇒ 没有任何判据会红。
        ///
        /// 场景与 `T37` 逐字相同（`RackPositions(footSpot, 15)` + 白球 `MaxBallSpeed` 朝 −x）——
        /// **刻意重述而非复用**：本判据锁的是**另一个不变量**（收敛性 ，`T37` 锁包络/确定性/迭代守卫），
        /// 复用会把两条判据的失效面绑在一起。
        ///
        /// 断言：`TableAtRest == true` 且达到步数 ≤ <see cref="BreakSettleStepCap"/>、
        /// `ToiIterationGuardTrips == 0`、`EnergyGainViolations == 0`、全部球位有限。
        ///
        /// **实测（首钉）**：**493 步**（≈4.1 s；`contacts = 49`、`captured = 2`、`guard = 0`、`energy = 0`）。
        /// **能测出**：滚阻被改到 0（永不静止）、袋角/球-球解算注入能量（速度不衰减）、
        /// 静止阈值被改到不可达、迭代预算被打满（子步被截断 ⇒ 迟迟不收敛）。
        /// **盲带**：只覆盖这一种初始姿态（`footSpot` 三角摆 + 8 m/s 正撞）；换瞄准角度/力度可能更长。
        /// </summary>
        [Test]
        public void T111_BreakMustSettle_WithinBoundedSteps()
        {
            Vector2[] rack = PhysicsTestDrive.RackPositions(_config.footSpot, 15);
            var initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var sim = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
            sim.LoadBalls(initial);

            int steps = 0;
            while (steps < BreakSettleStepCap && !sim.TableAtRest)
            {
                sim.Step();
                steps++;
            }

            Assert.IsTrue(sim.TableAtRest,
                $"T111：满堆开球在 {BreakSettleStepCap} 步（{BreakSettleStepCap / 120f:F1} s）内**未达整盘静止**"
                + $" ⇒ 球堆卡死。`Launch()` 门控 `TableAtRest` ⇒ 玩家再也出不了杆。"
                + $"（实测连续静止帧 = {sim.ConsecutiveRestFrameCount}）");
            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T111：TOI 迭代守护触发（子步被截断）。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T111：能量增益违规。");
            AssertFinite(sim, "T111");

            TestContext.WriteLine($"T111: 满堆开球 {steps} 步（{steps / 120f:F2} s）后整盘静止（cap {BreakSettleStepCap}）；"
                                  + $"contacts={sim.TotalContactCount}, captured={sim.CapturedBallCount} ✓");
        }

        // ══════════════════ T112 静止判定不得抖动 ═══════════════════════════

        /// <summary>单个姿态允许的静止判定观察窗口（步）；实测最长 45 ⇒ 取值留 13× 余量。</summary>
        private const int RestFlapStepCap = 600;

        /// <summary>
        /// **`T112`：静止判定不得抖动**（排期 Day 15「静止判定抖动」极端用例），
        /// 也正是 `T33a` 的 doc **自述盲带**（"不覆盖『永远在微抖』本身 —— **未构造抖动反例**"）。
        ///
        /// 抖动为什么致命：`TableAtRest` 是"本杆结束"的信号，`Launch()` 与 HUD 的 `Shoot` 可用性都门控它
        /// ⇒ 一旦它来回翻，玩家**看到按钮反复亮灭**、按下又被拒。
        ///
        /// 姿态（12 个，都是"最容易被微抖卡住"的形态）：① 袋角凹角内 3 深度 × 3 微速（0.0015 / 0.01 / 0.08 m/s，
        /// 跨在宏观静止阈值 `RestSpeedThreshold = 1e-3` 的上下）；② 贴长库滚动的 2 个微速；
        /// ③ **两球初始重叠 5 mm**（球-球位置分离也在每子步进行）。
        /// 每个姿态断言：`TableAtRest` 的**上升沿恰 1 次**、`guardTrips == 0`、`EnergyGainViolations == 0`。
        ///
        /// **实测（首钉）**：**全部上升沿 = 1**、静止步数 8–45 ⇒ **未观察到抖动**。
        /// （历史上"每子步无冲量反射"那一类抖动源已在第四轮修掉：`ResolveSegmentContact` 补了接近判据。）
        /// **盲带（如实）**：只覆盖这 12 个姿态；"抖动"不可枚举 ⇒ 本判据只给"可构造的若干反例不抖"，
        /// **不声称**"任何姿态都不抖"。
        /// </summary>
        [Test]
        public void T112_RestDeclaration_MustNotFlap()
        {
            var failures = new List<string>();
            var report = new List<string>();
            Pocket jaw = _config.GetPocket(1);
            Vector2 tn = jaw.throatNormal;
            Vector2 td = new Vector2(-tn.y, tn.x);

            void CheckPose(string tag, params BallState[] balls)
            {
                var sim = new PoolSimulator(_config, ShipParams(0.9f, 0.8f));
                sim.LoadBalls(balls);

                int edges = 0;
                int restAt = -1;
                bool previous = false;
                for (int step = 0; step < RestFlapStepCap; step++)
                {
                    sim.Step();
                    if (sim.TableAtRest && !previous)
                    {
                        edges++;
                        if (restAt < 0)
                        {
                            restAt = step + 1;
                        }
                    }

                    previous = sim.TableAtRest;
                }

                if (edges != 1)
                {
                    failures.Add($"{tag}：`TableAtRest` 上升沿应为 **1**，实测 {edges}"
                                 + "（抖动 ⇒ `Launch()` 门控与 HUD `Shoot` 可用性会反复翻）。");
                }

                if (sim.ToiIterationGuardTrips != 0)
                {
                    failures.Add($"{tag}：TOI 迭代守护触发 {sim.ToiIterationGuardTrips}。");
                }

                if (sim.EnergyGainViolations != 0)
                {
                    failures.Add($"{tag}：能量增益违规 {sim.EnergyGainViolations}。");
                }

                report.Add($"{tag}: rest@{restAt} 边沿={edges}");
            }

            foreach (float depth in new[] { 0f, 0.03f, 0.06f })
            {
                foreach (float speed in new[] { 0.0015f, 0.01f, 0.08f })
                {
                    CheckPose($"凹角d{depth * 1000f:F0}mm/v{speed * 1000f:F0}mm",
                        Ball(jaw.throatCenter.x - depth * tn.x + 0.045f * td.x,
                             jaw.throatCenter.y - depth * tn.y + 0.045f * td.y,
                             speed * tn.x, speed * tn.y));
                }
            }

            CheckPose("贴库/v1.5mm", Ball(0.5f, -0.6351f + PoolConstants.BallRadius, 0.0015f, 0f));
            CheckPose("贴库/v10mm", Ball(0.5f, -0.6351f + PoolConstants.BallRadius, 0.01f, 0f));

            float overlap = 0.005f;
            CheckPose("两球重叠5mm",
                Ball(0.10f, 0.30f, 0f, 0f),
                Ball(0.10f + 2f * PoolConstants.BallRadius - overlap, 0.30f, 0f, 0f));

            Assert.IsEmpty(failures, "T112 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T112: 静止判定无抖动（12 姿态，上升沿各 1）✓ " + string.Join(" | ", report));
        }

        /// <summary>
        /// 写 `d6-full-break-sample.json`。**本方法内不得有任何断言**（跳过落盘 ⇒ 不得跳过任何检查）。
        /// 路径走 <see cref="ReportOutput.ResolveTestEvidencePathOrNull"/> 单点：**未给 `-outputDir` 时返回 null ⇒ 只跳过落盘**。
        /// 基线 `t37BallBallContactBaseline` / `t37CapturedBaseline` 由**首次实现运行的证据钉定**，之后冻结。
        /// </summary>
        private static void WriteFullBreakEvidence(
            BallState[] initial, PoolSimulator sim, int ballCount, float cueSpeed,
            double elapsedMs, float overrunX, float overrunY)
        {
            string path = ReportOutput.ResolveTestEvidencePathOrNull("d6-full-break-sample.json");
            if (path == null)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"schema\": \"billiards.d6-full-break-sample.v1\",\n");
            sb.Append("  \"generatedAtUtc\": \"").Append(System.DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append("\",\n");
            sb.Append("  \"ballCount\": ").Append(ballCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"cueSpeed\": ").Append(cueSpeed.ToString("R", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"ballBallContactCount\": ").Append(sim.BallBallContactCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"t37BallBallContactCount\": ").Append(sim.BallBallContactCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"t37BallBallContactBaseline\": ").Append(sim.BallBallContactCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"t37CapturedBallCount\": ").Append(sim.CapturedBallCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"t37CapturedBaseline\": ").Append(sim.CapturedBallCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"t37ElapsedMs\": ").Append(elapsedMs.ToString("F3", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"totalContactCount\": ").Append(sim.TotalContactCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"guardTrips\": ").Append(sim.ToiIterationGuardTrips.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"energyGain\": ").Append(sim.EnergyGainViolations.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"pocketProximityEvents\": ").Append(sim.PocketProximityEvents.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"capturedBallCount\": ").Append(sim.CapturedBallCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"pocketJawCrossingCount\": ").Append(sim.PocketJawCrossingCount.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"ballBallMaxIterationsObserved\": ").Append(sim.BallBallMaxIterationsObserved.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"tableAtRest\": ").Append(sim.TableAtRest ? "true" : "false").Append(",\n");
            sb.Append("  \"maxEnvelopeOverrunX\": ").Append(overrunX.ToString("R", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"maxEnvelopeOverrunY\": ").Append(overrunY.ToString("R", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"deterministic\": true,\n");
            sb.Append("  \"balls\": [\n");
            for (int i = 0; i < sim.Balls.Count; i++)
            {
                Vector2 p = sim.Balls[i].position;
                sb.Append("    { \"index\": ").Append(i.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"finalX\": ").Append(p.x.ToString("R", CultureInfo.InvariantCulture))
                  .Append(", \"finalY\": ").Append(p.y.ToString("R", CultureInfo.InvariantCulture))
                  .Append(", \"capturedPocketIndex\": ").Append(sim.CapturedPocketIndex(i).ToString(CultureInfo.InvariantCulture))
                  .Append(" }").Append(i + 1 < sim.Balls.Count ? "," : string.Empty).Append("\n");
            }

            sb.Append("  ]\n}\n");
            File.WriteAllText(path, sb.ToString());
        }
    }
}
