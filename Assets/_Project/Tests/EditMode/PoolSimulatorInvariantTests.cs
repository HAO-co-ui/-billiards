using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 4 物理内核 I 的**穿透性不变量**测试（spec 切片 A：T5a / T5b）。
    ///
    /// T5a（正）断言高速正撞 6 条直库时**全轨迹零穿透**；
    /// T5b（负对照）把 TOI 换成"子步末采样"（<see cref="PoolSimulator.DisableToiForDiagnosticsOnly"/>，
    /// 复用同一条生产推进路径，仅替换接触检测），断言最大嵌入量 ≥ ½·v·dt_sub = 11.111 mm，
    /// 从而证明 T5a 的 1e-5 m 上界**确有判别力**（不是恒真）。
    ///
    /// 嵌入量度量（<see cref="DistancePointToSegment"/>）在本文件**独立实现**，
    /// 不复用被测内核的任何几何函数。
    /// </summary>
    public class PoolSimulatorInvariantTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>6 条直库在 segments 中的索引（spec §1.1）。</summary>
        private static readonly int[] StraightSegments = { 6, 20, 33, 47, 61, 74 };

        private const float Speed = 8f;
        private const int Steps = 8;
        private const float GapFromCushion = 0.03f; // 起点球心距库面 30 mm

        private static TableConfig _config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 " + ConfigPath + "（先运行 Billiards/Data/Import Table Reference Data）。");
        }

        // ── T5a 正 ────────────────────────────────────────────────────────

        [Test]
        public void T5a_HighSpeedHeadOn_NoPenetration()
        {
            float worstPenetration = 0f;

            foreach (int seg in StraightSegments)
            {
                (Vector2 a, Vector2 b, Vector2 mid, Vector2 nrm) = SegmentFrame(seg);
                Vector2 start = mid + nrm * GapFromCushion;
                Vector2 v0 = -nrm * Speed; // 正撞：沿内法向反向朝库面

                var sim = new PoolSimulator(_config, Params());
                sim.LoadBalls(new[] { new BallState { position = start, velocity = v0 } });

                float maxPenetration = 0f;
                bool bounced = false;

                for (int s = 0; s < Steps; s++)
                {
                    sim.Step();
                    BallState ball = sim.Balls[0];
                    Vector2 c = ball.position;

                    if (Vector2.Dot(ball.velocity, nrm) > 0f)
                    {
                        bounced = true;
                    }

                    // ① 球心到该段的带符号距离符号从不翻转（始终在库面内侧）。
                    float signedDistance = Vector2.Dot(c - a, nrm);
                    Assert.Greater(signedDistance, 0f,
                        $"T5a seg[{seg}]：球心越过库边平面（带符号距离 {signedDistance:R}）。");

                    // ② 全轨迹最大嵌入量。
                    float penetration = PoolConstants.BallRadius - DistancePointToSegment(c, a, b);
                    if (penetration > maxPenetration)
                    {
                        maxPenetration = penetration;
                    }
                }

                // 非空转证据（R4-3）：确实发生了反射与接触。
                Assert.IsTrue(bounced, $"T5a seg[{seg}]：未发生反射（测试为空转）。");
                Assert.Greater(sim.TotalContactCount, 0, $"T5a seg[{seg}]：无接触记录（测试为空转）。");

                // ③ 判据。
                Assert.LessOrEqual(maxPenetration, 1e-5f,
                    $"T5a seg[{seg}]：最大嵌入量 {maxPenetration * 1000f:F6} mm 超过 1e-5 m。");
                Assert.AreEqual(0, sim.ToiIterationGuardTrips, $"T5a seg[{seg}]：TOI 迭代守护触发。");
                Assert.AreEqual(0, sim.EnergyGainViolations, $"T5a seg[{seg}]：能量增益违规。");
                Assert.AreEqual(0, sim.PocketProximityEvents, $"T5a seg[{seg}]：触发了袋喉事件。");

                if (maxPenetration > worstPenetration)
                {
                    worstPenetration = maxPenetration;
                }
            }

            TestContext.WriteLine($"T5a: 6 条直库 8 m/s 正撞，全轨迹最大嵌入量 = {worstPenetration * 1000f:F6} mm（判据 ≤ 0.01 mm）。");
        }

        // ── T5b 负对照 ────────────────────────────────────────────────────

        [Test]
        public void T5b_NegativeControl_ToiDisabled_PenetrationIsLarge()
        {
            float worst = 0f;
            var perSegment = new System.Text.StringBuilder();
            float thresholdLogged = 0f;

            foreach (int seg in StraightSegments)
            {
                (Vector2 a, Vector2 b, Vector2 mid, Vector2 nrm) = SegmentFrame(seg);
                Vector2 start = mid + nrm * GapFromCushion;
                Vector2 v0 = -nrm * Speed; // 与 T5a 完全同一发射

                var sim = new PoolSimulator(_config, Params());
                sim.DisableToiForDiagnosticsOnly = true;
                sim.LoadBalls(new[] { new BallState { position = start, velocity = v0 } });

                float dtSub = sim.SubStepDeltaTime;
                float threshold = 0.5f * Speed * dtSub; // ½·v·dt_sub（当前 = 11.111 mm）

                float maxPenetration = 0f;
                for (int s = 0; s < Steps; s++)
                {
                    sim.Step();
                    Vector2 c = sim.Balls[0].position;
                    float penetration = PoolConstants.BallRadius - DistancePointToSegment(c, a, b);
                    if (penetration > maxPenetration)
                    {
                        maxPenetration = penetration;
                    }
                }

                Assert.GreaterOrEqual(maxPenetration, threshold,
                    $"T5b seg[{seg}]：禁用 TOI 后最大嵌入量 {maxPenetration * 1000f:F6} mm " +
                    $"仍小于 ½·v·dt_sub = {threshold * 1000f:F6} mm —— 负对照无判别力。");

                perSegment.Append($"seg[{seg}]={maxPenetration * 1000f:F2}mm ");
                thresholdLogged = threshold;
                if (maxPenetration > worst)
                {
                    worst = maxPenetration;
                }
            }

            TestContext.WriteLine(
                $"T5b: 阈值 ½·v·dt_sub = {thresholdLogged * 1000f:F4} mm；各库最大嵌入量 {perSegment}；worst={worst * 1000f:F4} mm。");

            // 判别力对比：T5a 上界 1e-5 m 与 T5b 实测差 ≥ 3 个数量级。
            Assert.Greater(worst, 1e-5f * 100f,
                $"T5b：禁用 TOI 的实测最大嵌入量仅 {worst * 1000f:F6} mm，与 T5a 的 1e-5 m 上界区分不足。");
        }

        // ── 测试辅助 ──────────────────────────────────────────────────────

        private static PhysicsParams Params()
        {
            return new PhysicsParams
            {
                cushionRestitution = 0.9f,
                cushionTangentialFriction = 0.8f,
                rollingDeceleration = 0f
            };
        }

        /// <summary>取段的（端点 a、端点 b、中点、指向台面内的单位法向）。</summary>
        private (Vector2 a, Vector2 b, Vector2 mid, Vector2 nrm) SegmentFrame(int index)
        {
            CushionSegment seg = _config.segments[index];
            Vector2 a = seg.a;
            Vector2 b = seg.b;
            Vector2 mid = (a + b) * 0.5f;
            Vector2 tan = (b - a).normalized;
            Vector2 nrm = new Vector2(-tan.y, tan.x);
            if (Vector2.Dot(nrm, Vector2.zero - mid) < 0f)
            {
                nrm = -nrm;
            }

            return (a, b, mid, nrm);
        }

        /// <summary>点到线段的最短距离（本文件独立实现，避免与内核共用一个几何口径）。</summary>
        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.x * ab.x + ab.y * ab.y;
            if (len2 <= 1e-12f)
            {
                return (p - a).magnitude;
            }

            float u = Vector2.Dot(p - a, ab) / len2;
            if (u < 0f)
            {
                u = 0f;
            }
            else if (u > 1f)
            {
                u = 1f;
            }

            Vector2 q = a + u * ab;
            return (p - q).magnitude;
        }
    }
}
