using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day 4 切片 C —— **积分格式（端点精确）与子步推导（判据 B）** 验证。
    ///
    /// 补 `Docs/Reviews/D4-Day4独立复核.md` §三 / §7.3 D-2 指出的三处零覆盖：
    ///  1. spec §5 T11 自由飞行位移精确性（a=0 与 a≠0 合成档）；
    ///  2. spec §5 T12 终点精确性有限差分自检（位移法 == 速度法）；
    ///  3. D-2：`MaxRollingDeceleration` / 判据 B 的子步数推导从未被执行过。
    ///
    /// ## 判别力预算（为什么容差是这么定的）
    ///
    /// 前向欧拉 `p += v·Δ` 相对端点精确（梯形）在 **恒定减速度** 下的单子步位移差为 `½·a·Δ²·v̂`，
    /// 累积 `N` 子步 ⇒ 位移误差 `½·a·Δ·T·v̂`。**实测（注入永久前向欧拉，Run B）**：
    ///
    /// | 用例 | 前向欧拉实测误差 | 本条容差（SubSteps=3） | 倍数 |
    /// | --- | --- | --- | --- |
    /// | T11b | **6.946e-4 m** | 6.44e-6 m | **~108×** |
    /// | T12  | 速度差 **1.391e-3 m/s** | 1e-5 m/s | **~139×** |
    /// | T12  | 位移残差（同上派生） | 1e-7 m | — |
    ///
    /// 而「子步数取错 / 漏子步 / 误用 dt」这类**纯位移 bug** 的误差 = `|v|·(dt − dt_sub) ≈ 8.2e-3 m`
    /// ⇒ 比 T11 容差大 **~900 倍**。⇒ **容差在 1e-6…1e-5 之间判别力等价**，无需卡到 1e-6。
    ///
    /// ## 容差为什么必须由 `SubSteps` 派生（V-2 的统一修法）
    ///
    /// 固定容差 1e-6 会与 `SubSteps` 耦合：**实测把 `MaxRollingDeceleration` 5→20（SubSteps 3→4）后，
    /// T11a（a=0，与前向欧拉无关）误差从 6.563e-7 升到 1.462e-6 ⇒ 假失败**。
    /// ⇒ 容差改由 `FreeFlightTolerance` / `T12*Tolerance(subSteps)` **派生**（模型见各方法注释），
    /// 目标失效模式与容差之间仍保持 2~3 个数量级余量。
    ///
    /// ## spec 的 1e-9（T12）在 float32 下不可达
    ///
    /// 端点精确格式在**解析意义上**自由飞行误差恒为 0（`_sliceC_probe/t12_probe.py` 双精度复算：8e-17）。
    /// float32 下实测：T11a 6.563e-7 / T11b 5.191e-7 m，T12 速度差 1.660e-6 m/s、位移残差 1.401e-8 m。
    /// ⇒ 1e-9 不可达（按铁律 11 先怀疑条款），改用可达且写明理由的容差。
    ///
    /// 坐标系：规范二维物理平面，x 长轴、y 短轴，米。
    /// </summary>
    public class PoolSimulatorIntegralFormatTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";

        /// <summary>
        /// **自由飞行位移容差模型（由 `SubSteps` 派生，不硬编码）** —— V-2 的统一修法。
        ///
        /// 模型：跑 <paramref name="steps"/> 步、每步 <paramref name="subSteps"/> 个子步 ⇒ `N = steps·subSteps`
        /// 次位置加法；本用例量级 `|p| ≈ 0.8 m` 处 float32 ULP ≈ `9.6e-8 m`；舍入按随机游走累积 ⇒ 量级 `√N·ULP`。
        /// 取安全系数 5（覆盖实测：SubSteps=3 → 6.563e-7 m；SubSteps=4 → 1.462e-6 m）。
        ///
        /// **为什么容差可以放宽**：本用例要抓的失效模式是「子步数取错 / 漏子步 / 误用 dt」，
        /// 其位移误差 = `|v|·(dt − dt_sub) ≈ 1.4765 × 5.556e-3 ≈ 8.2e-3 m`
        /// ⇒ **比容差大 ~900 倍**（SubSteps=3）。**1e-6 与 1e-5 之间的任何值判别力等价**，
        /// 而 1e-6 会与 `SubSteps` 耦合（V-2 实测：SubSteps=4 时误差 1.462e-6 > 1e-6 假失败）。
        /// </summary>
        private static float FreeFlightTolerance(int steps, int subSteps)
        {
            const float ulpAtTableScale = 9.6e-8f;
            const float safety = 5f;
            return safety * Mathf.Sqrt(steps * subSteps) * ulpAtTableScale;
        }

        /// <summary>
        /// T12 有限差分的容差。残差随**一个定长步内的子步数**增长 ⇒ 由 `SubSteps` 派生（基线 SubSteps=3 锚定实测）。
        /// 基线实测：速度差 1.66e-6 m/s（容差 1e-5，余量 6×）、位移残差 1.40e-8 m（容差 1e-7，余量 7×）。
        /// </summary>
        private static float T12VelocityTolerance(int subSteps)
        {
            return 1e-5f * subSteps / 3f;
        }

        private static float T12DisplacementTolerance(int subSteps)
        {
            return 1e-7f * subSteps / 3f;
        }

        /// <summary>复刻内核的 ceil 语义（比值恰为整数时取该整数）。</summary>
        private static int CeilWithEpsilon(float ratio)
        {
            return Mathf.CeilToInt(ratio - 1e-5f);
        }

        private static TableConfig _config;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(_config, "缺少 " + ConfigPath + "（先运行 Billiards/Data/Import Table Reference Data）。");
        }

        // ── T11a：a = 0 自由飞行 ───────────────────────────────────────────

        /// <summary>
        /// 球自 `(−0.5, 0.3)`、`v = (1.3, −0.7)`、a=0，跑 120 步（1 s）。
        /// 断言末位移 = `p₀ + v·N·dt`，容差由 <see cref="FreeFlightTolerance"/> 按 `SubSteps` 派生。
        /// 能测出：`remaining` 递减写错、漏子步、误用 dt 而非 dt_sub 等纯位移量级 bug。
        /// </summary>
        [Test]
        public void T11a_FreeFlight_NoDeceleration_DisplacementIsExact()
        {
            var p0 = new Vector2(-0.5f, 0.3f);
            var v0 = new Vector2(1.3f, -0.7f);
            const int steps = 120;

            PoolSimulator sim = Sim(a: 0f, p0, v0);
            for (int s = 0; s < steps; s++)
            {
                sim.Step();
            }

            Vector2 pEnd = sim.Balls[0].position;
            float t = steps * PoolSimulator.FixedDeltaTime;          // 解析时间 = dt·SubSteps·步数
            Vector2 expected = p0 + v0 * t;
            float error = (pEnd - expected).magnitude;
            float tolerance = FreeFlightTolerance(steps, sim.SubSteps);

            // 非空转：本用例必须是纯自由飞行（无接触、无袋喉事件）。
            Assert.AreEqual(0, sim.TotalContactCount, "T11a：应无任何库边接触，却发生接触（用例不再是自由飞行）。");
            Assert.AreEqual(0, sim.PocketProximityEvents, "T11a：触发了袋喉事件，污染自由飞行。");

            Assert.Less(error, tolerance,
                $"T11a：自由飞行位移误差 {error:E3} m 超过容差 {tolerance:E3} m（SubSteps={sim.SubSteps}）。" +
                $"（目标失效模式≈8.2e-3 m，比容差大 ~{8.2e-3f / tolerance:F0} 倍）");

            TestContext.WriteLine(
                $"T11a(a=0): p_end={pEnd:R}, expected={expected:R}, err={error:E3} m, tol={tolerance:E3} m（SubSteps={sim.SubSteps} 派生）, " +
                $"contacts={sim.TotalContactCount}");
        }

        // ── T11b：a = 1.0 合成档（关键：唯一能让积分格式"可观测"的档）──────

        /// <summary>
        /// 同初态，**合成** `a = 1.0 m/s²`（不依赖 Day 7 滚阻），跑 T = 0.5 s。
        /// 断言末位移 = `p₀ + v·T − ½·a·T²·v̂`，容差由 <see cref="FreeFlightTolerance"/> 按 `SubSteps` 派生。
        ///
        /// **本档是唯一对积分格式敏感的用例**：a=0 时 `AverageVelocity` 直接返回 v，
        /// 前向欧拉与端点精确**走同一条代码路径**（复核 §三）。a≠0 时才分叉。
        /// 若退回前向欧拉，**实测位移误差 6.946e-4 m**（与解析式 `½·a·Δ_sub·T` = 6.944e-4 m **一致**）
        /// ⇒ 比容差大 **~108 倍** ⇒ FAIL。
        /// </summary>
        [Test]
        public void T11b_FreeFlight_WithSyntheticDeceleration_DisplacementIsExact()
        {
            var p0 = new Vector2(-0.5f, 0.3f);
            var v0 = new Vector2(1.3f, -0.7f);
            const float a = 1.0f;
            const int steps = 60;                                    // T = 60/120 = 0.5 s

            PoolSimulator sim = Sim(a, p0, v0);
            for (int s = 0; s < steps; s++)
            {
                sim.Step();
            }

            Vector2 pEnd = sim.Balls[0].position;
            float t = steps * PoolSimulator.FixedDeltaTime;
            Vector2 vHat = v0.normalized;
            // 解析解（匀减速，速度未归零）：p(T) = p0 + v0·T − ½·a·T²·v̂
            Vector2 expected = p0 + v0 * t - (0.5f * a * t * t) * vHat;
            float error = (pEnd - expected).magnitude;
            float tolerance = FreeFlightTolerance(steps, sim.SubSteps);

            Assert.AreEqual(0, sim.TotalContactCount, "T11b：应无接触（纯自由飞行）。");
            Assert.AreEqual(0, sim.PocketProximityEvents, "T11b：触发了袋喉事件。");
            Assert.Greater(sim.Balls[0].velocity.magnitude, 0f, "T11b：球在 T 内不应停止（否则解析式不成立）。");

            Assert.Less(error, tolerance,
                $"T11b：a≠0 自由飞行位移误差 {error:E3} m 超过容差 {tolerance:E3} m（SubSteps={sim.SubSteps}）。" +
                $"若退回前向欧拉，此误差实测为 6.946e-4 m（比容差大 ~{6.946e-4f / tolerance:F0} 倍）。");

            TestContext.WriteLine(
                $"T11b(a=1.0, T=0.5): p_end={pEnd:R}, expected={expected:R}, err={error:E3} m, " +
                $"tol={tolerance:E3} m（SubSteps={sim.SubSteps} 派生）, 前向欧拉实测误差=6.946e-4 m（本容差的 {6.946e-4f / tolerance:F0} 倍）");
        }

        // ── T12：终点精确性有限差分自检 ───────────────────────────────────

        /// <summary>
        /// 同初态、`a = 1.0`，推进一个定长步（`Δ = FixedDeltaTime`，即 SubSteps 个子步 ——
        /// 公开 API 只暴露 <see cref="PoolSimulator.Step"/>，故取最小可观测步长；恒减速下速度线性，
        /// 该恒等式在子步粒度同样成立），断言：
        ///  · 「位移法」`(p₁ − p₀)/Δ` 与「速度法」`(v₀ + v₁)/2` 相等（差 ≤ 1e-5 m/s）；
        ///  · 位移域残差 `(p₁ − p₀) − (v₀+v₁)/2·Δ` ≈ 0（≤ 1e-6 m）。
        ///
        /// 能测出「改一处、漏一处」：位置更新加了 `−½aΔ²` 而 TOI 仍用 v_start（或反之）——
        /// 前向欧拉下速度差 **实测 1.391e-3 m/s**（超容差 139 倍）⇒ **FAIL**。
        /// </summary>
        [Test]
        public void T12_EndpointPrecision_DisplacementMatchesMeanVelocity()
        {
            var p0 = new Vector2(-0.5f, 0.3f);
            var v0 = new Vector2(1.3f, -0.7f);
            const float a = 1.0f;

            PoolSimulator sim = Sim(a, p0, v0);
            Vector2 startP = sim.Balls[0].position;
            Vector2 startV = sim.Balls[0].velocity;

            sim.Step();

            Vector2 endP = sim.Balls[0].position;
            Vector2 endV = sim.Balls[0].velocity;
            float delta = PoolSimulator.FixedDeltaTime;

            Vector2 displacementMethod = (endP - startP) / delta;      // 位移法（m/s）
            Vector2 velocityMethod = (startV + endV) * 0.5f;           // 速度法（m/s）
            float velocityDiff = (displacementMethod - velocityMethod).magnitude;
            float displacementResidual = ((endP - startP) - velocityMethod * delta).magnitude;
            float tolV = T12VelocityTolerance(sim.SubSteps);
            float tolD = T12DisplacementTolerance(sim.SubSteps);

            Assert.AreEqual(0, sim.TotalContactCount, "T12：应无接触（自由飞行，排除 TOI 干扰）。");

            Assert.Less(velocityDiff, tolV,
                $"T12：位移法 {displacementMethod:R} 与速度法 {velocityMethod:R} 相差 {velocityDiff:E3} m/s，" +
                $"超过 {tolV:E3}（SubSteps={sim.SubSteps} 派生；前向欧拉实测 1.042e-3）。");
            Assert.Less(displacementResidual, tolD,
                $"T12：位移域残差 {displacementResidual:E3} m 超过 {tolD:E3} m。");

            TestContext.WriteLine(
                $"T12(a=1.0, Δ=dt): 位移法={displacementMethod:R}, 速度法={velocityMethod:R}, " +
                $"速度差={velocityDiff:E3} m/s (tol {tolV:E3}), 位移残差={displacementResidual:E3} m (tol {tolD:E3}) [SubSteps={sim.SubSteps}]");
        }

        // ── D-2：判据 B 的子步数推导 ──────────────────────────────────────

        /// <summary>
        /// 复核 D-2：`MaxRollingDeceleration` 与判据 B 的推导**从未被执行过**（Day 4 a 恒 0）。
        ///
        /// 断言两件事：
        ///  1. **生产不等式** `½·a_max·dt_sub² ≤ ContactArcErrorBound`（spec §6 判据 3 的式子）；
        ///  2. **判据 B 分支确实参与 `DeriveSubSteps`**（**期望值由生产常量在测试内独立算出，不硬编码** ——
        ///     与 V-2/V-3/V-4 同一规则）：取一速度使判据 A 的比值 = 0.9（⇒ nA = 1），
        ///     则结果只可能来自判据 B ⇒ 断言 `DeriveSubSteps(vA1) == max(1, nB)`，
        ///     其中 `nB` 由 `dt`、`ContactArcErrorBound`、`MaxRollingDeceleration` 在本测试内独立算得。
        ///
        /// **判别力实测（Run E）**：临时删掉 `DeriveSubSteps` 里的判据 B 分支 ⇒ 本断言 FAIL
        /// （`DeriveSubSteps(vA1)` 变 1）。这是判据 B 的**活体检测** —— 否则就是"算过、没验过"。
        ///
        /// 说明：不等式 (1) 本身**结构自守卫**（a_max 调大时 `SubSteps` 同步由 `DeriveSubSteps` 重推，
        /// 不等式仍成立，**实测 a_max=20 时 8.573e-6 ≤ 5e-5**）⇒ **真正需要测的是 (2)**。
        /// **因此本用例的期望值也刻意不硬编码 a_max 的效果**：a_max 5→20 时 nB 2→4，断言随之成立，不假失败。
        /// </summary>
        [Test]
        public void D2_JudgementB_SubStepDerivation_IsLive()
        {
            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            float aMax = PoolSimulator.MaxRollingDeceleration;
            float bound = PoolSimulator.ContactArcErrorBound;
            float dt = PoolSimulator.FixedDeltaTime;
            float dtSub = sim.SubStepDeltaTime;

            // (1) 生产不等式（spec 判据 3）。
            float lhs = 0.5f * aMax * dtSub * dtSub;
            Assert.LessOrEqual(lhs, bound + 1e-12f,
                $"D-2：½·a_max·dt_sub² = {lhs:E3} m 超过弧误差界 {bound:E3} m。");

            // (2) 判据 B 分支活体：期望值在本测试内由生产常量独立算出。
            float halfDiameter = PoolConstants.BallDiameter * 0.5f;
            float vA1 = 0.9f * halfDiameter / dt;                       // 判据 A 的比值 = 0.9 ⇒ nA = 1
            float dtForArc = Mathf.Sqrt((2f * bound) / aMax);
            float ratioB = dt / dtForArc;
            int nB = CeilWithEpsilon(ratioB);
            int expected = Mathf.Max(1, nB);

            int actual = PoolSimulator.DeriveSubSteps(vA1);
            Assert.AreEqual(expected, actual,
                $"D-2：DeriveSubSteps({vA1:R} m/s) = {actual}，应为 {expected}（= max(1, nB)；判据 A 在此速度只需 1 子步，" +
                $"故该结果只可能由判据 B 给出）。返回 1 表示判据 B 分支被删/未生效 —— 判据 B 无人守卫。");

            TestContext.WriteLine(
                $"D-2: a_max={aMax:R} m/s², bound={bound:E1} m, dt_sub={dtSub:R} s, " +
                $"½·a_max·dt_sub²={lhs:E3} m（余量 {bound / lhs:F2}×）; 判据B需求比值={ratioB:F4} ⇒ nB={nB}; " +
                $"vA1={vA1:R} m/s ⇒ DeriveSubSteps={actual}（期望 max(1,nB)={expected}）; SubSteps={sim.SubSteps}");
        }

        // ── 测试辅助 ──────────────────────────────────────────────────────

        private static PhysicsParams Params(float e, float mu, float a)
        {
            return new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                rollingDeceleration = a
            };
        }

        private static PoolSimulator Sim(float a, Vector2 p, Vector2 v)
        {
            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f, a));
            sim.LoadBalls(new[] { new BallState { position = p, velocity = v } });
            return sim;
        }
    }
}
