using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **积分格式（端点精确）与子步推导（判据 B）** 验证。
    ///
    /// 补 `Docs/Reviews/D4-Day4独立复核.md` §三 / §7.3 指出的三处零覆盖：
    ///  1. spec §5 T11 自由飞行位移精确性（a=0 与 a≠0 合成档）；
    ///  2. spec §5 T12 终点精确性有限差分自检（位移法 == 速度法）；
    ///  3. `MaxRollingDeceleration` / 判据 B 的子步数推导从未被执行过。
    ///
    /// ## 判别力预算（为什么容差是这么定的）
    ///
    /// 前向欧拉 `p += v·Δ` 相对端点精确（梯形）在 **恒定减速度** 下的单子步位移差为 `½·a·Δ²·v̂`，
    /// 累积 `N` 子步 ⇒ 位移误差 `½·a·Δ·T·v̂`。**实测（注入永久前向欧拉）**：
    ///
    /// | 用例 | 前向欧拉实测误差 | 本条容差（SubSteps=3） | 倍数 |
    /// | --- | --- | --- | --- |
    /// | T11b | **6.946e-4 m** | 6.44e-6 m | **~108×** |
    /// | T12  | 速度差 **1.391e-3 m/s** | 1e-5 m/s | **~139×** |
    /// | T12  | 位移残差（同上派生） | 1e-7 m | — |
    ///
    /// ⚠ **表里 `1.391e-3` 随 `SubSteps` 变**（它**不是**常量）：前向欧拉的速度差闭式为
    /// `a·dt/(2n)`（推导见 `T12_EndpointPrecision_…` 的注释）。`a = 1.0`（本测试的合成减速度）、
    /// `dt = 1/120 s` ⇒ `n=3` 时 `1/720 = 1.389e-3`、`n=4` 时 `1/960 = 1.042e-3`。
    /// ⇒ **断言消息里不得写死这个数**，必须现算（见该测试；历史教训：曾写死 `1.042e-3`，
    /// 它来自 `a_max=20`（SubSteps=4）那一轮的配置，子步数回落到 3 后该字面量即陈旧）。
    ///
    /// 而「子步数取错 / 漏子步 / 误用 dt」这类**纯位移 bug** 的误差 = `|v|·(dt − dt_sub) ≈ 8.2e-3 m`
    /// ⇒ 比 T11 容差大 **~900 倍**。⇒ **容差在 1e-6…1e-5 之间判别力等价**，无需卡到 1e-6。
    ///
    /// ## 容差为什么必须由 `SubSteps` 派生（统一修法）
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
        /// **自由飞行位移容差模型（由 `SubSteps` 派生，不硬编码）** —— 统一修法。
        ///
        /// 模型：跑 <paramref name="steps"/> 步、每步 <paramref name="subSteps"/> 个子步 ⇒ `N = steps·subSteps`
        /// 次位置加法；本用例量级 `|p| ≈ 0.8 m` 处 float32 ULP ≈ `9.6e-8 m`；舍入按随机游走累积 ⇒ 量级 `√N·ULP`。
        /// 取安全系数 5（覆盖实测：SubSteps=3 → 6.563e-7 m；SubSteps=4 → 1.462e-6 m）。
        ///
        /// **为什么容差可以放宽**：本用例要抓的失效模式是「子步数取错 / 漏子步 / 误用 dt」，
        /// 其位移误差 = `|v|·(dt − dt_sub) ≈ 1.4765 × 5.556e-3 ≈ 8.2e-3 m`
        /// ⇒ **比容差大 ~900 倍**（SubSteps=3）。**1e-6 与 1e-5 之间的任何值判别力等价**，
        /// 而 1e-6 会与 `SubSteps` 耦合（实测：SubSteps=4 时误差 1.462e-6 > 1e-6 假失败）。
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
        /// 同初态，**合成** `a = 1.0 m/s²`（不依赖滚阻标定），跑 T = 0.5 s。
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
        ///
        /// **前向欧拉下的解析预估（`a·dt/(2n)`）的推导** —— 供断言消息**现算**用，**不要写死数值**：
        /// 设一个定长步内有 `n` 个子步、每子步 `Δ = dt/n`、恒减速 `a`、初速 `v₀`、方向 `v̂`。
        ///  · **前向欧拉**（`p += v_k·Δ`，`v_k = v₀ − k·a·Δ·v̂`）累积位移
        ///    `Δp = n·v₀·Δ − a·Δ²·n(n−1)/2·v̂` ⇒ **位移法** `Δp/dt = v₀ − a·Δ·(n−1)/2·v̂`；
        ///  · **速度法** `(v₀ + v_n)/2 = v₀ − n·a·Δ/2·v̂`；
        ///  ⇒ 两者之差 = `a·Δ/2` = **`a·dt/(2n)`**（正是本用例在 bug 下会测到的 `velocityDiff`）。
        ///  · 数值核对：`a=1`、`dt=1/120` ⇒ `n=3` 时 `1/720 = 1.389e-3`、`n=4` 时 `1/960 = 1.042e-3`。
        ///    ⇒ **该值随 `SubSteps` 变**；历史事故：曾把 `1.042e-3` 写死进消息，而它是 `a_max=20`
        ///    （SubSteps=4）那一轮的配置值，子步数回落 3 后即**陈旧**（自证矛盾被冻结在
        ///    `Docs/Reports/d4-sliceC-editmode-INJECT2.xml`）。
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

            // 前向欧拉下的解析预估速度差 = a·dt/(2n)（**现算，不写死** —— 该值随 SubSteps 变；推导见本测试 doc 注释）。
            float forwardEulerVelocityDiff = a * PoolSimulator.FixedDeltaTime / (2f * sim.SubSteps);

            Assert.Less(velocityDiff, tolV,
                $"T12：位移法 {displacementMethod:R} 与速度法 {velocityMethod:R} 相差 {velocityDiff:E3} m/s，" +
                $"超过 {tolV:E3}（SubSteps={sim.SubSteps} 派生；前向欧拉预估 a·dt/(2n)={forwardEulerVelocityDiff:E3} m/s）。");
            Assert.Less(displacementResidual, tolD,
                $"T12：位移域残差 {displacementResidual:E3} m 超过 {tolD:E3} m。");

            TestContext.WriteLine(
                $"T12(a=1.0, Δ=dt): 位移法={displacementMethod:R}, 速度法={velocityMethod:R}, " +
                $"速度差={velocityDiff:E3} m/s (tol {tolV:E3}), 位移残差={displacementResidual:E3} m (tol {tolD:E3}) [SubSteps={sim.SubSteps}]" +
                $"；前向欧拉预估 a·dt/(2n)={forwardEulerVelocityDiff:E3} m/s（= 本容差的 {forwardEulerVelocityDiff / tolV:F0} 倍）");
        }

        // ── 判据 B 的子步数推导 ──────────────────────────────────────

        /// <summary>
        /// `MaxRollingDeceleration` 与判据 B 的推导**从未被执行过**（此前 a 恒 0）。
        ///
        /// 断言两件事：
        ///  1. **生产不等式** `½·a_max·dt_sub² ≤ ContactArcErrorBound`（spec §6 判据 3 的式子）；
        ///  2. **判据 B 分支确实参与 `DeriveSubSteps`**：取一速度使判据 A 的比值 = 0.9（⇒ nA = 1），
        ///     则结果只可能来自判据 B ⇒ 断言 `DeriveSubSteps(vA1) == max(1, nB)`，
        ///     其中 `nB` 由 `dt`、`ContactArcErrorBound`、`MaxRollingDeceleration` 在本测试内算出。
        ///
        /// ## ⚠ 本用例的覆盖面必须准确表述
        ///
        /// 上面的"在测试内独立算出"**只对"独立于常量的取值"成立，对"独立于公式"不成立** —— 两者不是一回事：
        /// 本用例把生产 `DeriveSubSteps` 的判据 B 式子**逐字重抄**进测试，故它
        /// **对"判据 B 的公式本身写错"完全盲**（把公式改错、测试跟着抄错，两边同错 ⇒ 照样 PASS）。
        /// ⇒ **本用例验证的是"判据 B 分支是活的"（没被删掉、没被短路），不验证该公式本身正确。**
        /// 公式无关的那一半由
        /// <see cref="D2b_JudgementB_SufficiencyAndMinimality_AreFormulaAgnostic"/> 承担：
        /// 两者**两向互补**（本用例对"任何公式改动"是 fail-closed；性质断言抓"公式错但自洽"），
        /// 与 `D4-PHY-01` ↔ 汇编级 PhysX 检查是**同一个模式**。**两者都不得删**（铁律 4）。
        ///
        /// **判别力实测**：临时删掉 `DeriveSubSteps` 里的判据 B 分支 ⇒ 本断言 FAIL
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

            // (2) 判据 B 分支活体：期望值在本测试内算出（**独立于常量的取值**，但**不**独立于公式 ——
            //     见本测试 doc：本断言对"公式写错"是盲的，那由性质断言覆盖）。
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

        // ── 判据 B 的"充分性 + 最小性"性质断言（**公式无关**）────

        /// <summary>
        /// **判据 B 的"必要性 + 最小性"性质断言（公式无关）**。
        ///
        /// 设 `n = DeriveSubSteps(vA1)`。因 `vA1` 使判据 A 的比值 = 0.9 ⇒ `nA = 1`，
        /// 故 `n` 只可能来自判据 B ⇒ 下面两条性质**与判据 B 的写法无关**，只与
        /// 「`n` 是使弧误差不等式成立的**最小**整数」这一**语义**有关：
        ///  · **充分性**：`½·a_max·(dt/n)² ≤ ContactArcErrorBound`；
        ///  · **最小性**（`n ≥ 2` 时）：`½·a_max·(dt/(n−1))² > ContactArcErrorBound`。
        ///
        /// **为什么它能抓公式错误**（两种错误实现都验算过）：
        /// | 错误实现 | 观察 |
        /// | --- | --- |
        /// | **删掉判据 B 分支** ⇒ `n = 1` | 充分性：`½·5·(1/120)² = 1.736e-4 > 5e-5` ⇒ **违反 ⇒ FAIL** |
        /// | **公式漏掉因子 2**（`dtForArc = sqrt(bound/a_max)`）⇒ `n = 3` | 充分性满足；但最小性：`½·5·(1/240)² = 4.34e-5 ≤ 5e-5` ⇒ `n=3` **不是最小 ⇒ FAIL** |
        ///
        /// ⇒ 本条**严格强于** <see cref="D2_JudgementB_SubStepDerivation_IsLive"/> 的转写断言
        /// （后者只需换成另一种**自洽的**错公式即可通过）。**但转写断言不删** —— 两者**两向互补**：
        /// 转写断言抓"任何公式改动"（fail-closed），本条抓"公式错但自洽"。与
        /// `D4-PHY-01` ↔ 汇编级 PhysX 检查是**同一个模式**。
        ///
        /// 数值核对（正确实现）：`nB = ceil(dt / sqrt(2·bound/a_max)) = ceil(1.8634) = 2` ⇒ `n = max(nA=1, nB=2) = 2`；
        /// 充分性 `½·5·(1/240)² = 4.340e-5 ≤ 5e-5` ✓；最小性 `½·5·(1/120)² = 1.736e-4 > 5e-5` ✓。
        /// **注意 `n = 2` 而非全局 `SubSteps = 3`**：后者由判据 A 主导（见 `D4GAP02_3_…` 的锚定）。
        /// </summary>
        [Test]
        public void D2b_JudgementB_SufficiencyAndMinimality_AreFormulaAgnostic()
        {
            float aMax = PoolSimulator.MaxRollingDeceleration;
            float bound = PoolSimulator.ContactArcErrorBound;
            float dt = PoolSimulator.FixedDeltaTime;
            float halfDiameter = PoolConstants.BallDiameter * 0.5f;

            float vA1 = 0.9f * halfDiameter / dt;                 // 判据 A 的比值 = 0.9 ⇒ nA = 1

            // 前提自证：nA 确为 1。若该前提不成立，本用例就不再"只测判据 B"，性质断言随之失去意义。
            float ratioA = (vA1 * dt) / halfDiameter;
            Assert.LessOrEqual(ratioA, 1f - 1e-4f,
                $"D2b：前提不成立 —— 判据 A 的比值 {ratioA:R} 未 < 1，nA 可能 > 1，本用例不再只测判据 B。");

            int n = PoolSimulator.DeriveSubSteps(vA1);

            float suffLhs = 0.5f * aMax * (dt / n) * (dt / n);
            Assert.LessOrEqual(suffLhs, bound + 1e-12f,
                $"D2b：**充分性**违反 —— ½·a_max·(dt/n)² = {suffLhs:E3} m > 弧误差界 {bound:E3} m（n={n}）。" +
                " n=1 通常意味着判据 B 分支被删/被短路。");

            if (n >= 2)
            {
                float minLhs = 0.5f * aMax * (dt / (n - 1)) * (dt / (n - 1));
                Assert.Greater(minLhs, bound,
                    $"D2b：**最小性**违反 —— ½·a_max·(dt/(n−1))² = {minLhs:E3} m ≤ 弧误差界 {bound:E3} m，" +
                    $"故 n={n} 不是最小整数（n−1 也满足不等式）⇒ 判据 B 的公式有问题（例如漏掉因子 2）。");
            }

            float minLhsReport = 0.5f * aMax * (dt / Mathf.Max(1, n - 1)) * (dt / Mathf.Max(1, n - 1));
            TestContext.WriteLine(
                $"D2b: a_max={aMax:R}, bound={bound:E1} m, dt={dt:R} s, vA1={vA1:R} m/s, ratioA={ratioA:F4}（<1 ⇒ nA=1）; " +
                $"DeriveSubSteps(vA1)={n}; 充分性 {suffLhs:E3} ≤ {bound:E3} ✓; " +
                $"最小性 {minLhsReport:E3} > {bound:E3}（n≥2 时检查 n−1）✓");
        }

        // ── 定长步与子步数的**字面量**钉住 ──────────────────────

        /// <summary>
        /// **用独立字面量钉住两个契约常量** —— 它们此前只被"间接"约束，改动后**全套件照过**：
        ///  · `FixedDeltaTime`：此前唯一的间接钉住是 `GreyboxSceneTests` 的 `WholeSteps(0.02f, Step) == 2`，
        ///    它只把 dt 约束到区间 **`(1/150, 1/100]`**（能抓 1/60，**抓不住精确值**）⇒ 改成 1/110 全套件仍绿；
        ///  · `SubSteps`（= `DeriveSubSteps(PoolConstants.MaxBallSpeed)`）：此前**完全没有**字面量锚
        ///    （源码与注释里只有 "SubSteps=3" 这类**散文**），改 `MaxBallSpeed` / 判据系数后无人报警。
        ///
        /// 判据刻意是**字面量**（`1f/120f` 与 `3`），**不引用被测常量自身** —— 否则就退化成
        /// `DeriveSubSteps(X) == DeriveSubSteps(X)` 这类**恒真式**（铁律 8）。
        /// `1/120 s` 是 spec §2.1 的定长；`3` 的来历：判据 A `ceil(8·(1/120)/0.028575) = ceil(2.3334) = 3` 主导，
        /// 判据 B `ceil(1.8634) = 2` ⇒ `max(3, 2) = 3`（判据 A 主导**仅因** `MaxBallSpeed = 8`）。
        /// </summary>
        [Test]
        public void D4GAP02_3_FixedDeltaTimeAndSubSteps_ArePinnedByIndependentLiterals()
        {
            Assert.AreEqual(1f / 120f, PoolSimulator.FixedDeltaTime, 0f,
                $"D4-GAP-02-3：定长步应为字面量 1/120 s（spec §2.1），实测 {PoolSimulator.FixedDeltaTime:R}。");

            int subSteps = PoolSimulator.DeriveSubSteps(PoolConstants.MaxBallSpeed);
            Assert.AreEqual(3, subSteps,
                $"D4-GAP-02-3：DeriveSubSteps(MaxBallSpeed={PoolConstants.MaxBallSpeed:R}) 应等于字面量 3，实测 {subSteps}。");

            // 交叉印证：运行期实例的 SubSteps / SubStepDeltaTime 必须与上面同源（防"两处各算一份"分叉）。
            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f, 0f));
            Assert.AreEqual(subSteps, sim.SubSteps,
                "D4-GAP-02-3：实例 SubSteps 与 DeriveSubSteps(MaxBallSpeed) 不一致（两处推导已分叉）。");

            // ⚠ 必须落在 **float32 域**比较，用 `"R"` 串（本工程既有惯例，见逐位比较）：
            //   `Assert.AreEqual(float, float, 0f)` 会选中 **double** 重载；而 C# 允许浮点运算**以高于结果
            //   类型的精度**进行 ⇒ 左侧 `FixedDeltaTime / subSteps` 可能带着"多出来的中间位"直接当 double 传入，
            //   右侧 `SubStepDeltaTime` 则是 **float 字段存储后**再拓宽 ⇒ 二者差 1 ULP 以下也判失败。
            //   **实测**（基线跑）：Expected=0.0027777779226501784d（double 中间精度） vs
            //   Actual=0.0027777778450399637d（float32 存储后拓宽）；差值 7.8e-11 **< 1 ULP(float32)=2.3e-10**
            //   ⇒ 两者**是同一个 float32**。故换域比较是**修正比较域**，**不是**放宽判据（仍是 0 容差的逐位相等）。
            float dtSubExpected = PoolSimulator.FixedDeltaTime / subSteps;
            Assert.AreEqual(dtSubExpected.ToString("R"), sim.SubStepDeltaTime.ToString("R"),
                $"D4-GAP-02-3：SubStepDeltaTime({sim.SubStepDeltaTime:R}) != FixedDeltaTime/SubSteps({dtSubExpected:R})。");

            TestContext.WriteLine(
                $"D4-GAP-02-3: FixedDeltaTime={PoolSimulator.FixedDeltaTime:R} s（= 字面量 1/120 ✓）, " +
                $"SubSteps={subSteps}（= 字面量 3 ✓）, dt_sub={sim.SubStepDeltaTime:R} s");
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
