using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 4 物理内核 I 的**接触关系与子步不变量**测试（spec 切片 A：T1 / T2 / T3 / T6）。
    ///
    /// 判别力声明（每条测试都能失败，见工程标准 R3-1/R3-2）：
    ///  · T1 分量分解守恒 —— 期望值由**解析式**给出（<c>v_out·n = −e·(v_in·n)</c>、
    ///    <c>v_out·t = μ·(v_in·t)</c>），不读模拟器内部量。能测出：法向取错/符号反、
    ///    n/t 分解错、e 未应用、μ 未应用、**μ 污染法向**、**切向方向反转**。
    ///  · T2 e 扰动判别 —— 同发射跑 e=1.0 与 e=0.8，断言法向分量之差 = 0.2·|v_in·n|。
    ///    能测出：把 e 写死为 1 的实现（两次结果相同 → 失败）。
    ///  · T3 镜面角守恒 —— **本测试测不出 e 或 μ 的任何错误**（e=μ=1 时角度本应守恒），
    ///    那是 T1/T2 的职责；T3 只负责角度。
    ///  · T6 子步数不变量 —— 自守卫；含"比值恰为整数取整"的 ceil 语义判别。
    ///
    /// 坐标系：规范二维物理平面，x 长轴、y 短轴，米。库段索引按资源顺序（= JSON 顺序）。
    /// </summary>
    public class PoolSimulatorTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>6 条直库在 segments 中的索引（spec §1.1；其余为袋角/弧段）。</summary>
        private static readonly int[] StraightSegments = { 6, 20, 33, 47, 61, 74 };

        private static TableConfig _config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 " + ConfigPath + "（先运行 Billiards/Data/Import Table Reference Data）。");
        }

        // ── T1 ────────────────────────────────────────────────────────────

        [Test]
        public void T1_ContactComponentDecomposition_IsConserved()
        {
            // e、μ 都**不是** 1 —— 否则该测试对两者都不敏感。
            const float e = 0.8f;
            const float mu = 0.9f;
            var n = new Vector2(0f, 1f);
            var t = new Vector2(1f, 0f);

            AssertComponentCase(e, mu, n, t,
                new Vector2(0.859808f, -0.485056f), new Vector2(-1.732051f, -1.000000f), "30°");
            AssertComponentCase(e, mu, n, t,
                new Vector2(0.750000f, -0.375248f), new Vector2(-1.000000f, -1.732051f), "60°");
        }

        private static void AssertComponentCase(float e, float mu, Vector2 n, Vector2 t, Vector2 start, Vector2 vIn, string label)
        {
            PoolSimulator sim = Sim(e, mu, start, vIn);
            bool reflected = StepUntilReflected(sim, n, 400, out Vector2 vOut);
            Assert.IsTrue(reflected, $"T1 {label}：未在 400 步内发生反射（测试为空转）。");
            Assert.AreEqual(0, sim.PocketProximityEvents, $"T1 {label}：触发了袋喉事件，污染了接触结果。");

            // 期望值 = 解析式（不读模拟器内部量）。
            float expectedNormal = -e * Vector2.Dot(vIn, n);
            float expectedTangent = mu * Vector2.Dot(vIn, t);

            float actualNormal = Vector2.Dot(vOut, n);
            float actualTangent = Vector2.Dot(vOut, t);

            Assert.Less(RelativeError(expectedNormal, actualNormal), 1e-4f,
                $"T1 {label}：法向分量 {actualNormal:R} != −e·(v_in·n) = {expectedNormal:R}");
            Assert.Less(RelativeError(expectedTangent, actualTangent), 1e-4f,
                $"T1 {label}：切向分量 {actualTangent:R} != μ·(v_in·t) = {expectedTangent:R}");

            TestContext.WriteLine(
                $"T1 {label}: v_out·n={actualNormal:R} (exp {expectedNormal:R}, relerr {RelativeError(expectedNormal, actualNormal):E3}); " +
                $"v_out·t={actualTangent:R} (exp {expectedTangent:R}, relerr {RelativeError(expectedTangent, actualTangent):E3})");
        }

        // ── T2 ────────────────────────────────────────────────────────────

        [Test]
        public void T2_RestitutionPerturbation_ChangesNormalComponent_ByAnalyticDelta()
        {
            var start = new Vector2(0.859808f, -0.485056f);
            var vIn = new Vector2(-1.732051f, -1.000000f);
            var n = new Vector2(0f, 1f);

            Vector2 outE1 = CapturePostBounce(Params(1.0f, 0.9f), start, vIn, n);
            Vector2 outE08 = CapturePostBounce(Params(0.8f, 0.9f), start, vIn, n);

            // 解析式：Δ(v·n) = (1 − e_lower/e_upper 的组合) ⇒ e 差 0.2 使法向分量差 0.2·|v_in·n|。
            float expectedDelta = 0.2f * Mathf.Abs(Vector2.Dot(vIn, n));
            float actualDelta = Vector2.Dot(outE1, n) - Vector2.Dot(outE08, n);

            Assert.AreNotEqual(Vector2.Dot(outE1, n), Vector2.Dot(outE08, n),
                "T2：e=1.0 与 e=0.8 的法向分量相同 —— 说明 e 被写死（测试确实能失败）。");
            Assert.AreEqual(expectedDelta, actualDelta, 1e-4f,
                $"T2：法向分量差 {actualDelta:R} != 0.2·|v_in·n| = {expectedDelta:R}");
            TestContext.WriteLine(
                $"T2: v_out·n(e=1.0)={Vector2.Dot(outE1, n):R}, v_out·n(e=0.8)={Vector2.Dot(outE08, n):R}, " +
                $"Δ={actualDelta:R} (解析期望 {expectedDelta:R})");
        }

        // ── T3 ────────────────────────────────────────────────────────────

        [Test]
        public void T3_SpecularAngle_IsConserved_WhenRestitutionAndFrictionAreOne()
        {
            // 本测试 e = μ = 1：此时反射角**本应**守恒。
            // ⇒ **测不出 e 或 μ 的任何错误**（那是 T1/T2 的职责），T3 只保证角度与法向方向。
            float[] angles = { 15f, 30f, 45f, 60f, 75f };
            const float speed = 2f;
            float maxDeviation = 0f;
            int caseCount = 0;

            foreach (int seg in StraightSegments)
            {
                (Vector2 mid, Vector2 tan, Vector2 nrm) = SegmentFrame(seg);

                foreach (float deg in angles)
                {
                    float th = deg * Mathf.Deg2Rad;
                    // 起点：库面内侧，沿内法向 r + 20 mm；速度：与库边切向成 θ（法向朝向库面）。
                    Vector2 start = mid + nrm * (PoolConstants.BallRadius + 0.02f);
                    Vector2 dir = Mathf.Cos(th) * tan - Mathf.Sin(th) * nrm;
                    Vector2 vIn = dir * speed;

                    PoolSimulator sim = Sim(1f, 1f, start, vIn);
                    bool reflected = StepUntilReflected(sim, nrm, 200, out Vector2 vOut);
                    Assert.IsTrue(reflected, $"T3 seg[{seg}] θ={deg}°：未发生反射。");
                    Assert.AreEqual(0, sim.PocketProximityEvents, $"T3 seg[{seg}] θ={deg}°：触发了袋喉事件。");

                    float thetaIn = AngleToTangent(vIn, tan, nrm);
                    float thetaOut = AngleToTangent(vOut, tan, nrm);
                    float deviation = Mathf.Abs(thetaOut - thetaIn);
                    if (deviation > maxDeviation)
                    {
                        maxDeviation = deviation;
                    }

                    caseCount++;
                    Assert.Less(deviation, 1.0f,
                        $"T3 seg[{seg}] θ={deg}°：反射角 {thetaOut:F4}° 与入射角 {thetaIn:F4}° 相差超过 1°。");
                }
            }

            TestContext.WriteLine($"T3: {caseCount} 个组合（6 库 × 5 角），最大角度偏差 = {maxDeviation:E3}°（判据 < 1.0°）。");
        }

        // ── T6 ────────────────────────────────────────────────────────────

        [Test]
        public void T6_SubStepCount_IsDerived_NotHardCoded()
        {
            PoolSimulator sim = new PoolSimulator(_config, Params(0.9f, 0.9f));
            float dt = PoolSimulator.FixedDeltaTime;
            float dtSub = dt / sim.SubSteps;

            // 1) 契约：SubSteps 是 DeriveSubSteps(MaxBallSpeed) 的推导值。
            Assert.AreEqual(PoolSimulator.DeriveSubSteps(PoolConstants.MaxBallSpeed), sim.SubSteps,
                "T6-1：SubSteps 与 DeriveSubSteps(MaxBallSpeed) 不一致。");

            // 2) 离散回退界：v_max·dt_sub ≤ ½·直径。
            Assert.LessOrEqual(PoolConstants.MaxBallSpeed * dtSub,
                0.5f * PoolConstants.BallDiameter + 1e-9f,
                "T6-2：v_max·dt_sub 超过半个球直径。");

            // 3) 接触弦-弧误差界：½·a_max·dt_sub² ≤ 5e-5 m（a_max = 5）。
            Assert.LessOrEqual(0.5f * 5f * dtSub * dtSub, 5e-5f + 1e-12f,
                "T6-3：半个子步的弦高误差超过 5e-5 m。");

            // 4) ceil 语义：比值**恰为整数**时取该整数（不以 < 取代，也不截断）。
            float halfDiameter = PoolConstants.BallDiameter * 0.5f;
            float vExact3 = 3f * halfDiameter / dt;          // 判据 A 的比值恰好 = 3.0
            Assert.AreEqual(3, PoolSimulator.DeriveSubSteps(vExact3),
                "T6-4：比值恰为 3 时应取 3（ceil 语义），取 4 说明用了 (int)r+1 或 < 取代。");
            float vJustAbove3 = 3.0005f * halfDiameter / dt; // 比值略大于 3
            Assert.AreEqual(4, PoolSimulator.DeriveSubSteps(vJustAbove3),
                "T6-4：比值略大于 3 应取 4（不得截断为 3）。");

            // 5) 半个子步位移严格小于一个子步位移且为正。
            float halfStepTravel = 0.5f * PoolConstants.MaxBallSpeed * dtSub;
            float fullStepTravel = PoolConstants.MaxBallSpeed * dtSub;
            Assert.Greater(halfStepTravel, 0f);
            Assert.Less(halfStepTravel, fullStepTravel);

            // 6) 当前 MaxBallSpeed = 8 下推导值应为 3（A 得 3、B 得 2）。
            Assert.AreEqual(3, sim.SubSteps, "T6-6：MaxBallSpeed=8 时推导子步数应为 3。");

            TestContext.WriteLine(
                $"T6: MaxBallSpeed={PoolConstants.MaxBallSpeed}, SubSteps={sim.SubSteps}, dt={dt:R}, dtSub={dtSub:R}, " +
                $"判据A比值={(PoolConstants.MaxBallSpeed * dt) / halfDiameter:R}, 判据B比值={(dt / PoolMath.Sqrt((2f * 5e-5f) / 5f)):R}");
        }

        // ── 测试辅助（独立于被测代码的期望值/度量）──────────────────────────

        private static PhysicsParams Params(float e, float mu, float a = 0f)
        {
            return new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                rollingDeceleration = a
            };
        }

        private static PoolSimulator Sim(float e, float mu, Vector2 start, Vector2 v)
        {
            var sim = new PoolSimulator(_config, Params(e, mu));
            sim.LoadBalls(new[] { new BallState { position = start, velocity = v } });
            return sim;
        }

        private static Vector2 CapturePostBounce(PhysicsParams p, Vector2 start, Vector2 v, Vector2 normal)
        {
            var sim = new PoolSimulator(_config, p);
            sim.LoadBalls(new[] { new BallState { position = start, velocity = v } });
            bool reflected = StepUntilReflected(sim, normal, 400, out Vector2 vOut);
            Assert.IsTrue(reflected, "CapturePostBounce：未发生反射。");
            return vOut;
        }

        /// <summary>推进直到法向速度符号翻转（证明已发生反射），返回反射后速度。</summary>
        private static bool StepUntilReflected(PoolSimulator sim, Vector2 normal, int maxSteps, out Vector2 vOut)
        {
            float vn0 = Vector2.Dot(sim.Balls[0].velocity, normal);
            for (int s = 0; s < maxSteps; s++)
            {
                sim.Step();
                Vector2 v = sim.Balls[0].velocity;
                if (Vector2.Dot(v, normal) * vn0 < 0f)
                {
                    vOut = v;
                    return true;
                }
            }

            vOut = sim.Balls[0].velocity;
            return false;
        }

        /// <summary>取段的（中点、单位切向、指向台面内的单位法向）。</summary>
        private (Vector2 mid, Vector2 tan, Vector2 nrm) SegmentFrame(int index)
        {
            CushionSegment seg = _config.segments[index];
            Vector2 a = seg.a;
            Vector2 b = seg.b;
            Vector2 mid = (a + b) * 0.5f;
            Vector2 tan = (b - a).normalized;
            Vector2 nrm = new Vector2(-tan.y, tan.x);
            // 内法向 = 指向台面中心（原点）一侧。
            if (Vector2.Dot(nrm, Vector2.zero - mid) < 0f)
            {
                nrm = -nrm;
            }

            return (mid, tan, nrm);
        }

        /// <summary>速度相对库边切向的夹角（度），用 |法向分量| 与 |切向分量| 的 atan2。</summary>
        private static float AngleToTangent(Vector2 v, Vector2 tan, Vector2 nrm)
        {
            float nComp = Mathf.Abs(Vector2.Dot(v, nrm));
            float tComp = Mathf.Abs(Vector2.Dot(v, tan));
            return PoolMath.Atan2(nComp, tComp) * Mathf.Rad2Deg;
        }

        private static float RelativeError(float expected, float actual)
        {
            float denom = Mathf.Max(1e-6f, Mathf.Abs(expected));
            return Mathf.Abs(actual - expected) / denom;
        }
    }
}
