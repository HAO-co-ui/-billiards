using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// 物理内核的**穿透性不变量**测试（spec：T5a / T5b）。
    ///
    /// T5a（正）断言高速正撞 6 条直库时**全轨迹零穿透**；
    /// T5b（负对照）把 TOI 换成"子步末采样"（<see cref="PoolSimulator.DisableToiForDiagnosticsOnly"/>，
    /// 复用同一条生产推进路径，仅替换接触检测），断言最大嵌入量 ≥ **0.9·v·dt_sub**，
    /// 从而证明 T5a 的 1e-5 m 上界**确有判别力**（不是恒真）。
    ///
    /// **起点与阈值均由 `v` / `dt_sub` 派生**（统一修法）：旧版硬编码"球心距库面 30 mm"
    /// + 阈值 `0.5·v·dt_sub` 在 `SubSteps=4` 时失效（实测嵌入量 7.046 mm &lt; 8.333 mm）。
    /// 现由 <see cref="StartClearance"/> 把接触采样相位钉在 Step 末的最坏相位 ⇒ 阈值可稳写为 `0.9·v·dt_sub`。
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

        /// <summary>
        /// 起点球心距库面的距离，**由 `v` 与 `dt_sub` 派生出"最坏相位"**（统一修法，不硬编码）。
        ///
        /// 动机：禁用 TOI（T5b 负对照）时，接触**只在子步末**被检测 ⇒
        /// 「一个子步内越过的量」= `v·dt_sub` 的**相位函数**：
        /// 旧硬编码起点（球心距库面 30 mm）在 SubSteps=3 时实测只占 `0.5·v·dt_sub` 阈值的 84.6%，
        /// **SubSteps=4 时掉到 42.3% ⇒ 负对照"无判别力"而失败**。
        ///
        /// 构造：令球在第 `(SubSteps−1)` 个子步末仍**恰在接触带外**（球心距 = `r + 余量`），
        /// 第 `SubSteps` 个子步末越过带面 ⇒ 该 **Step 末**（= 第 `SubSteps` 个子步末，正是外部测量的采样点）
        /// 的嵌入量 ≈ `v·dt_sub`。**该构造对任意 `SubSteps` 成立** ⇒ 阈值可稳写为 `0.9·v·dt_sub`。
        /// </summary>
        private static float StartClearance(float v, float dtSub, int subSteps)
        {
            const float phaseMargin = 0.02f;   // 第 (subSteps−1) 个采样点恰落在带外 r + 0.02·v·dt_sub 处
            return PoolConstants.BallRadius
                 + (subSteps - 1) * v * dtSub
                 + phaseMargin * v * dtSub;
        }

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
                var sim = new PoolSimulator(_config, Params());
                float dtSub = sim.SubStepDeltaTime;
                Vector2 start = mid + nrm * StartClearance(Speed, dtSub, sim.SubSteps); // 由 v/dt_sub 派生
                Vector2 v0 = -nrm * Speed; // 正撞：沿内法向反向朝库面

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

                var sim = new PoolSimulator(_config, Params());
                sim.DisableToiForDiagnosticsOnly = true;
                float dtSub = sim.SubStepDeltaTime;
                // 与 T5a 完全同一发射（起点由 v/dt_sub 派生的"最坏相位"，见 StartClearance）。
                Vector2 start = mid + nrm * StartClearance(Speed, dtSub, sim.SubSteps);
                Vector2 v0 = -nrm * Speed;
                sim.LoadBalls(new[] { new BallState { position = start, velocity = v0 } });

                // 阈值由 v 与 dt_sub 派生（不硬编码）：最坏相位下嵌入量 ≈ v·dt_sub ⇒ 取 0.9·v·dt_sub 仍留余量。
                float threshold = 0.9f * Speed * dtSub;

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
                    $"仍小于 0.9·v·dt_sub = {threshold * 1000f:F6} mm —— 负对照无判别力。");

                perSegment.Append($"seg[{seg}]={maxPenetration * 1000f:F2}mm ");
                thresholdLogged = threshold;
                if (maxPenetration > worst)
                {
                    worst = maxPenetration;
                }
            }

            TestContext.WriteLine(
                $"T5b: 阈值 0.9·v·dt_sub = {thresholdLogged * 1000f:F4} mm；各库最大嵌入量 {perSegment}；worst={worst * 1000f:F4} mm。");

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
