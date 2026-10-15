using System.Collections.Generic;
using System.IO;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// 球-球碰撞的判据族（编号续物理内核 I，自 T15 起）。
    ///
    /// **几何一律用"合成球桌"**（`ScriptableObject.CreateInstance&lt;TableConfig&gt;` + 指定段）：
    /// ① 期望值可用**解析闭式**写出（不受真实网格的离散袋角干扰）；
    /// ② 无球袋 ⇒ `PocketProximityEvents` 恒为 0（本族要求）；
    /// ③ 不读资产、可离线重现。
    ///
    /// 每条判据都声明"能测出什么 / 测不出什么（盲带）"。
    /// </summary>
    public class PoolSimulatorBallBallTests
    {
        private const float R = 0.028575f;          // PoolConstants.BallRadius
        private const float D = 2f * R;             // 球直径

        private static readonly Vector2 Nx = new Vector2(-1f, 0f);   // "法向沿 x"的约定法向（由 B 指向 A）

        private static TableConfig Table(params Vector2[] flat)
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>();
            for (int i = 0; i + 1 < flat.Length; i += 2)
            {
                t.segments.Add(new CushionSegment { a = flat[i], b = flat[i + 1], rail = "synthetic" });
            }

            t.pockets = new List<Pocket>();
            return t;
        }

        private static TableConfig EmptyTable() => Table();

        private static PoolSimulator Sim(TableConfig table, float eBall, float eCushion, float muCushion,
            params BallState[] balls)
        {
            var p = new PhysicsParams
            {
                cushionRestitution = eCushion,
                cushionTangentialFriction = muCushion,
                ballRestitution = eBall,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = 0f, rollingDecelerationRoll = 0f,
            };

            var sim = new PoolSimulator(table, p);
            sim.LoadBalls(balls);
            return sim;
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

        private static float AngleDeg(Vector2 a, Vector2 b)
        {
            float c = Dot(a, b) / Mathf.Max(Mag(a) * Mag(b), 1e-12f);
            return Mathf.Acos(Mathf.Clamp(c, -1f, 1f)) * Mathf.Rad2Deg;
        }

        // ══════════════════ T15 对心碰撞：速度交换（排期三项之一）══════════════

        [Test]
        public void T15_HeadOn_CarriesVelocityExchange()
        {
            // 期望（甲类）：一维弹性等质量 ⇒ 完全交换。
            PoolSimulator sim = Sim(EmptyTable(), 1.0f, 1.0f, 1.0f,
                Ball(-0.20f, 0f, 1f, 0f), Ball(0.20f, 0f, 0f, 0f));

            Step(sim, 60);

            Vector2 vA = sim.Balls[0].velocity;
            Vector2 vB = sim.Balls[1].velocity;

            TestContext.WriteLine($"T15: vA=({vA.x:F6},{vA.y:F6}) vB=({vB.x:F6},{vB.y:F6}) "
                                  + $"ballBallContacts={sim.BallBallContactCount}");

            Assert.Greater(sim.BallBallContactCount, 0, "T15：必须真的发生球-球接触（非空转）。");
            Assert.Less(Mag(vA - Vector2.zero), 1e-4f, "T15：对心弹性等质量 ⇒ A 应静止（相对误差 ≤1e-4）。");
            Assert.Less(Mag(vB - new Vector2(1f, 0f)), 1e-4f, "T15：B 应取走 A 的全部速度。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T15：不得升能。");
            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T15：迭代守卫必须为 0。");
        }

        // ══════════════════ T16 斜碰分离角（两档参数化）══════════════════════

        /// <summary>T16 的发射几何：A 自 (−0.20, y0) 沿 (1,−1) 入射，B 静止于 (0.20, 0)。</summary>
        /// <summary>
        /// 斜碰几何。**⚠ 首触法向不是精确的 (−1,0)** —— 45° 入射时轨迹先穿过"距离 = 2r"的圆，
        /// 首触点比"两球心同 y"的那一点略早（两者是同一二次方程的两个根，首触取**小根**）。
        /// 故本函数用**解析几何**求出首触法向，再据此分解入射速度 —— 这才是 T16b/T17a 的闭式期望值来源。
        /// </summary>
        private static PoolSimulator SlantedImpact(float eBall, out float vNormal, out float vTangent)
        {
            Vector2 a0 = new Vector2(-0.20f, 0.34285f);
            Vector2 b0 = new Vector2(0.20f, 0f);
            Vector2 v = new Vector2(1f, -1f);

            Vector2 n = FirstContactNormal(a0, v, b0, R);

            vNormal = Dot(v, n);                                        // 法向分量（带号）
            vTangent = Mag(v - vNormal * n);                            // 切向分量（大小）

            return Sim(EmptyTable(), eBall, 1.0f, 1.0f,
                Ball(a0.x, a0.y, v.x, v.y), Ball(b0.x, b0.y, 0f, 0f));
        }

        /// <summary>
        /// **首触法向（解析几何，不读模拟器内部量）**：解 `|A0 + v·s − B0|² = (2r)²` 的**小根**，
        /// 得首触点 ⇒ `n̂ = (A_contact − B0) / (2r)`。
        /// </summary>
        private static Vector2 FirstContactNormal(Vector2 a0, Vector2 v, Vector2 b0, float r)
        {
            Vector2 d = a0 - b0;
            float A = Dot(v, v);
            float B = 2f * Dot(d, v);
            float C = Dot(d, d) - 4f * r * r;
            float disc = B * B - 4f * A * C;
            Assert.Greater(disc, 0f, "首触判据：轨迹必须与该圆相交。");

            float s = (-B - Mathf.Sqrt(disc)) / (2f * A);   // 小根 = 首次进入
            Vector2 contact = a0 + v * s;
            return (contact - b0) / (2f * r);
        }

        [Test]
        public void T16a_SlantedImpact_Elastic_IsExactlyNinetyDegrees()
        {
            PoolSimulator sim = SlantedImpact(1.0f, out float vn, out float vt);
            Step(sim, 60);

            Vector2 vA = sim.Balls[0].velocity;
            Vector2 vB = sim.Balls[1].velocity;
            float theta = AngleDeg(vA, vB);

            // 期望（甲类）：e=1 ⇒ 严格 90°。容差 0.01°（见 spec §5 T16a 的决定规则）。
            TestContext.WriteLine($"T16a: vn={vn:F6} vt={vt:F6} vA=({vA.x:F6},{vA.y:F6}) "
                                  + $"vB=({vB.x:F6},{vB.y:F6}) theta={theta:F6}°");

            Assert.Greater(sim.BallBallContactCount, 0, "T16a：非空转。");
            Assert.Less(Mathf.Abs(theta - 90f), 0.01f, $"T16a：e=1 ⇒ 分离角应为 90°（实测 {theta:F6}°）。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T16a：不得升能。");
        }

        [Test]
        public void T16b_SlantedImpact_WithRestitution_MatchesClosedForm()
        {
            const float e = 0.95f;
            PoolSimulator sim = SlantedImpact(e, out float vn, out float vt);
            Step(sim, 60);

            Vector2 vA = sim.Balls[0].velocity;
            Vector2 vB = sim.Balls[1].velocity;
            float theta = AngleDeg(vA, vB);

            // 期望（甲类闭式）：θ(e) = atan2( |v_t| , |v_n|·(1−e)/2 )
            float expected = Mathf.Atan2(Mathf.Abs(vt), Mathf.Abs(vn) * (1f - e) * 0.5f) * Mathf.Rad2Deg;

            TestContext.WriteLine($"T16b: vn={vn:F6} vt={vt:F6} theta={theta:F6}° expected={expected:F6}°");

            Assert.Greater(sim.BallBallContactCount, 0, "T16b：非空转。");
            Assert.Less(Mathf.Abs(theta - expected), 0.05f,
                $"T16b：分离角应等于闭式 θ(e)（实测 {theta:F6}°，期望 {expected:F6}°）。");
        }

        // ══════════════════ T17 e_ball 的敏感判据（补 T16 的"对 e 不具信息"）══════

        [Test]
        public void T17a_NormalRelativeVelocity_RatioEqualsBallRestitution()
        {
            const float e = 0.95f;
            PoolSimulator sim = SlantedImpact(e, out float vn, out float vt);

            // 接触前：沿法向的相对速度（A 的初速 − B 的初速，投影到 n̂）
            Vector2 v0 = sim.Balls[0].velocity - sim.Balls[1].velocity;
            float relBefore = Dot(v0, Nx);

            Step(sim, 60);

            Vector2 v1 = sim.Balls[0].velocity - sim.Balls[1].velocity;
            float relAfter = Dot(v1, Nx);

            TestContext.WriteLine($"T17a: relBefore={relBefore:F6} relAfter={relAfter:F6} e={e}");

            Assert.Greater(sim.BallBallContactCount, 0, "T17a：非空转。");
            Assert.Less(Mathf.Abs(relAfter - (-e * relBefore)), 1e-4f,
                "T17a：法向相对速度比必须等于 e_ball（忽略 e ⇒ 会取 −relBefore，偏 5% ⇒ FAIL）。");
        }

        [Test]
        public void T17b_KineticEnergyLoss_MatchesClosedForm()
        {
            // 等质量对心：ΔKE/KE = −(1−e²)/2
            const float e = 0.95f;
            PoolSimulator sim = Sim(EmptyTable(), e, 1.0f, 1.0f,
                Ball(-0.20f, 0f, 1f, 0f), Ball(0.20f, 0f, 0f, 0f));

            float keBefore = 0.5f * Mag(sim.Balls[0].velocity) * Mag(sim.Balls[0].velocity);

            Step(sim, 60);

            float keAfter = 0.5f * Mag(sim.Balls[0].velocity) * Mag(sim.Balls[0].velocity)
                            + 0.5f * Mag(sim.Balls[1].velocity) * Mag(sim.Balls[1].velocity);

            float ratio = (keAfter - keBefore) / keBefore;
            float expected = -(1f - e * e) * 0.5f;

            TestContext.WriteLine($"T17b: KE_before={keBefore:F9} KE_after={keAfter:F9} "
                                  + $"ratio={ratio:F9} expected={expected:F9}");

            Assert.Greater(sim.BallBallContactCount, 0, "T17b：非空转。");
            Assert.Less(Mathf.Abs(ratio - expected), 1e-4f,
                "T17b：动能耗散比必须等于 −(1−e²)/2（该量对 e 一阶敏感 ⇒ 承担 e 的检出）。");
        }

        // ══════════════════ T18 无穿透 / 无嵌入（含负对照）══════════════════

        private static float MaxOverlapDuringHeadOn(bool disableBallBallToi, out int guardTrips, out int energyGain)
        {
            // 两球对开，各自 8 m/s（相对 16 m/s），初始间距 0.5 m。
            PoolSimulator sim = Sim(EmptyTable(), 1.0f, 1.0f, 1.0f,
                Ball(-0.25f, 0f, 8f, 0f), Ball(0.25f, 0f, -8f, 0f));
            sim.DisableBallBallToiForDiagnosticsOnly = disableBallBallToi;

            float worst = 0f;
            for (int i = 0; i < 40; i++)
            {
                sim.Step();
                float dist = Mag(sim.Balls[0].position - sim.Balls[1].position);
                float overlap = D - dist;
                if (overlap > worst)
                {
                    worst = overlap;
                }
            }

            guardTrips = sim.ToiIterationGuardTrips;
            energyGain = sim.EnergyGainViolations;
            return worst;
        }

        [Test]
        public void T18a_HighSpeedHeadOn_NeverPenetrates()
        {
            float worst = MaxOverlapDuringHeadOn(false, out int guard, out int gain);

            TestContext.WriteLine($"T18a: maxOverlap={worst:E3} m guardTrips={guard} energyGain={gain}");

            Assert.LessOrEqual(worst, 1e-5f, "T18a：球-球最大重叠必须 ≤ 1e-5 m（抗穿透标准）。");
            Assert.AreEqual(0, guard, "T18a：迭代守卫必须为 0。");
            Assert.AreEqual(0, gain, "T18a：不得升能。");
        }

        [Test]
        public void T18b_WithoutBallBallToi_PenetrationIsObservable()
        {
            float worst = MaxOverlapDuringHeadOn(true, out int _, out int _);

            // 判据公式化：≥ 0.5 · v_rel · dt_sub（当前 SubSteps=3 ⇒ 22.222 mm）
            float vRel = 16f;
            float dtSub = PoolSimulator.FixedDeltaTime / new PoolSimulator(EmptyTable(), default(PhysicsParams)).SubSteps;
            float threshold = 0.5f * vRel * dtSub;

            TestContext.WriteLine($"T18b: maxOverlap={worst:E3} m threshold={threshold:E3} m "
                                  + $"(vRel={vRel} dtSub={dtSub:E6})");

            Assert.GreaterOrEqual(worst, threshold,
                "T18b：关掉球-球 TOI 后必须能观测到显著穿透（否则 T18a 是在测空气）。");
        }

        // ══════════════════ T19 动量守恒（两档参数化）════════════════════════

        [Test]
        public void T19a_MomentumConserved_AndContactNonVacuous()
        {
            foreach (float e in new[] { 1.0f, 0.95f })
            {
                PoolSimulator sim = SlantedImpact(e, out float _, out float _);

                Vector2 pBefore = sim.Balls[0].velocity + sim.Balls[1].velocity;
                Step(sim, 60);
                Vector2 pAfter = sim.Balls[0].velocity + sim.Balls[1].velocity;

                TestContext.WriteLine($"T19a: e={e} pBefore=({pBefore.x:F6},{pBefore.y:F6}) "
                                      + $"pAfter=({pAfter.x:F6},{pAfter.y:F6}) contacts={sim.BallBallContactCount}");

                Assert.Less(Mag(pAfter - pBefore), 1e-6f, $"T19a：等质量动量守恒（e={e}）。");
                Assert.Greater(sim.BallBallContactCount, 0,
                    $"T19a：必须真的发生球-球接触（e={e}）—— 否则「两球互穿、各自匀速」也精确守恒。");
            }
        }

        // ══════════════════ T20 确定性（多球）════════════════════════════════

        [Test]
        public void T20_MultiBallRun_IsBitIdentical()
        {
            var table = Table(new Vector2(-1f, -0.635f), new Vector2(1f, -0.635f));

            PoolSimulator Build()
            {
                return Sim(table, 0.95f, 0.8f, 0.9f,
                    Ball(-0.30f, 0.10f, 3f, -1f),
                    Ball(0.00f, 0.00f, 0f, 0f),
                    Ball(0.05f, 0.03f, 0f, 0f));
            }

            PoolSimulator a = Build();
            PoolSimulator b = Build();
            Step(a, 300);
            Step(b, 300);

            TestContext.WriteLine($"T20: contacts={a.TotalContactCount} ballBall={a.BallBallContactCount} "
                                  + $"guardTrips={a.ToiIterationGuardTrips}");

            Assert.Greater(a.BallBallContactCount, 0, "T20：必须真的发生球-球接触（非空转）。");
            Assert.LessOrEqual(a.BallBallContactCount, 30,
                "T20：球-球解算次数须有上界（计数与动量交换解耦时会虚增到千级）。");

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(a.Balls[i].position.x.ToString("R"), b.Balls[i].position.x.ToString("R"), $"T20：球 {i} x 逐位不一致。");
                Assert.AreEqual(a.Balls[i].position.y.ToString("R"), b.Balls[i].position.y.ToString("R"), $"T20：球 {i} y 逐位不一致。");
                Assert.AreEqual(a.Balls[i].velocity.x.ToString("R"), b.Balls[i].velocity.x.ToString("R"), $"T20：球 {i} vx 逐位不一致。");
                Assert.AreEqual(a.Balls[i].velocity.y.ToString("R"), b.Balls[i].velocity.y.ToString("R"), $"T20：球 {i} vy 逐位不一致。");
            }
        }

        // ══════════════════ T22 深层初始重叠的确定处置 ═══════════════════════

        [Test]
        public void T22_DeepOverlap_IsSeparated_NotSilentPassThrough()
        {
            // 初始 |Δp| = 2r − 5e-4（越过校验期阈值 1e-4 的 5 倍）
            float gap = D - 5e-4f;
            PoolSimulator sim = Sim(EmptyTable(), 1.0f, 1.0f, 1.0f,
                Ball(0f, 0f, 0f, 0f), Ball(gap, 0f, 0f, 0f));

            Step(sim, 1);

            Vector2 a = sim.Balls[0].position;
            Vector2 b = sim.Balls[1].position;
            float dist = Mag(a - b);

            TestContext.WriteLine($"T22: dist={dist:F9} (2r={D:F9}) ballBall={sim.BallBallContactCount} "
                                  + $"NaN={(float.IsNaN(a.x) || float.IsNaN(b.x))}");

            Assert.IsFalse(float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(b.x) || float.IsNaN(b.y), "T22：不得产生 NaN。");
            Assert.GreaterOrEqual(dist, D - 1e-6f, "T22：首次子步后应被分离到 ≈ 2r。");
            Assert.GreaterOrEqual(sim.BallBallContactCount, 1, "T22：深层重叠必须被计一次接触（不得静默）。");
            Assert.GreaterOrEqual(sim.BallBallSeparationCount, 1,
                "T22：**位置修正必须被走到**（可观测量）—— 零覆盖时这条会 FAIL。");
        }

        [Test]
        public void T22b_CoincidentCentres_NoNaN_DeterministicSeparation()
        {
            // 退化最危险的一端：两球心几乎重合（|Δp| < 1e-9）
            PoolSimulator sim = Sim(EmptyTable(), 1.0f, 1.0f, 1.0f,
                Ball(0f, 0f, 0f, 0f), Ball(1e-10f, 0f, 0f, 0f));

            Step(sim, 1);

            Vector2 a = sim.Balls[0].position;
            Vector2 b = sim.Balls[1].position;

            TestContext.WriteLine($"T22b: |Δp|={Mag(a - b):E3} ballBall={sim.BallBallContactCount}");

            Assert.IsFalse(float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsNaN(b.x) || float.IsNaN(b.y), "T22b：不得产生 NaN。");

            // ★ 标题写的是 `DeterministicSeparation` ⇒ **必须真的锁住"分离"**（原版只断言了"无 NaN"，
            //   于是"两球同位且静止 ⇒ 永不分离"这个真实缺口从判据底下漏了过去，直到 Day 15 的
            //   边界用例探针才把它抓出来）。两条：① 必须分离到相切 `2r`；② 双跑逐位一致。
            float gap = Mag(a - b);
            Assert.AreEqual(D, gap, 2e-5f,
                $"T22b：球心几乎重合的一对必须被**分离到相切**（2r = {D:R}），实测间距 {gap:R}"
                + "（同位静止的两球若永不分离，就会叠在原位 ⇒ `footSpot` 被占住时白球会与它重合）。");

            PoolSimulator again = Sim(EmptyTable(), 1.0f, 1.0f, 1.0f,
                Ball(0f, 0f, 0f, 0f), Ball(1e-10f, 0f, 0f, 0f));
            Step(again, 1);
            Assert.AreEqual(0f, (again.Balls[0].position - a).magnitude, "T22b：分离必须确定性（球 0 逐位一致）。");
            Assert.AreEqual(0f, (again.Balls[1].position - b).magnitude, "T22b：分离必须确定性（球 1 逐位一致）。");
        }

        // ══════════════════ T23 三球链式接触（先后链式，非"同刻"）════════════

        [Test]
        public void T23_ThreeBallChain_ConservesMomentum_AndCountsTwoContacts()
        {
            // A 撞 B，B 紧邻 C（间距 2r + 1e-4）。**全程无库边 ⇒ 总动量守恒合法。**
            PoolSimulator sim = Sim(EmptyTable(), 1.0f, 1.0f, 1.0f,
                Ball(-0.20f, 0f, 2f, 0f),
                Ball(0.20f, 0f, 0f, 0f),
                Ball(0.20f + D + 1e-4f, 0f, 0f, 0f));

            Vector2 before = Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                before += sim.Balls[i].velocity;
            }

            Step(sim, 120);

            Vector2 after = Vector2.zero;
            for (int i = 0; i < 3; i++)
            {
                after += sim.Balls[i].velocity;
            }

            TestContext.WriteLine($"T23: before=({before.x:F6},{before.y:F6}) after=({after.x:F6},{after.y:F6}) "
                                  + $"ballBall={sim.BallBallContactCount} guardTrips={sim.ToiIterationGuardTrips}");

            Assert.Less(Mag(after - before), 1e-6f, "T23：三球链全程无库边接触 ⇒ 总动量守恒成立。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T23：不得升能。");
            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T23：迭代守卫必须为 0。");
            Assert.GreaterOrEqual(sim.BallBallContactCount, 2, "T23：三球链至少两次球-球接触（否则空实现也能过）。");
            Assert.LessOrEqual(sim.BallBallContactCount, 8,
                "T23：球-球解算次数必须有**上界**（解析事件数级）—— 抖动/重复解算会把计数放大数十倍。");
            Assert.LessOrEqual(sim.TotalContactCount, 16, "T23：总解算次数同样须有上界。");
        }

        // ══════════════════ T24 库边夹心（零时间推进 + 顺序解算）══════════════

        /// <summary>库边夹心：段 S 为直线 y=0（内侧 +y），B 贴库静止，A 沿法向撞来。</summary>
        private static PoolSimulator Sandwich(float eCushion, out TableConfig table)
        {
            table = Table(new Vector2(-1f, 0f), new Vector2(1f, 0f));
            return Sim(table, 1.0f, eCushion, 1.0f,
                Ball(0f, R + 0.10f, 0f, -2f),          // A：沿 −y 撞向 B
                Ball(0f, R, 0f, 0f));                  // B：贴库静止（距段恰为 r）
        }

        [Test]
        public void T24a_Sandwich_ElasticCushion_NoEnergyGain_NoPenetration()
        {
            PoolSimulator sim = Sandwich(1.0f, out TableConfig _);
            Step(sim, 60);

            Vector2 bPos = sim.Balls[1].position;
            float distToSeg = Mathf.Abs(bPos.y);   // 段为 y=0

            TestContext.WriteLine($"T24a: ballBall={sim.BallBallContactCount} total={sim.TotalContactCount} "
                                  + $"peakIters={sim.BallBallMaxIterationsObserved} guardTrips={sim.ToiIterationGuardTrips} "
                                  + $"energyGain={sim.EnergyGainViolations} B.distToSeg={distToSeg:E3}");

            Assert.AreEqual(0, sim.EnergyGainViolations, "T24a：同刻顺序解算不得升能。");
            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T24a：迭代守卫必须为 0。");
            Assert.GreaterOrEqual(sim.BallBallContactCount, 1, "T24a：非空转（必须发生球-球接触）。");
            Assert.LessOrEqual(sim.BallBallContactCount, 8, "T24a：球-球解算次数须有上界。");
            Assert.LessOrEqual(sim.TotalContactCount, 16, "T24a：总解算次数须有上界。");
            Assert.GreaterOrEqual(sim.BallBallMaxIterationsObserved, 2,
                "T24a：B 的段接触以 t=0 紧跟球-球 ⇒ 至少两轮迭代（顺序路径的可观测签名）。");
            Assert.GreaterOrEqual(distToSeg, R - 1e-6f, "T24a：B 不得钻入库边。");
            Assert.LessOrEqual(sim.BallBallSeparationCount, 8,
                "T24a：位置修正次数须有上界（双边约束）。");
        }

        [Test]
        public void T24b_Sandwich_AbsorbingCushion_TransfersMomentum()
        {
            // e_cushion = 0：墙把 B 的动量全部吸收 ⇒ A 把动量交给 B、B 交给墙 ⇒ A 应静止。
            PoolSimulator sim = Sandwich(0.0f, out TableConfig _);
            Step(sim, 60);

            float speedA = Mag(sim.Balls[0].velocity);
            TestContext.WriteLine($"T24b: |vA|={speedA:E3} ballBall={sim.BallBallContactCount}");

            Assert.GreaterOrEqual(sim.BallBallContactCount, 1, "T24b：非空转。");
            Assert.LessOrEqual(speedA, 1e-4f,
                "T24b：球-球冲量等大反向的可观测推论 —— A 应把全部动量交出（墙吸收）。");
            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T24b：迭代守卫必须为 0。");
        }

        // ══════════════════ F2 参数安全默认（实机档不得跑在 e=0/μ=0 上）══════════

        [Test]
        public void F2a_SafeDefault_HasSafePhysicalValues()
        {
            PhysicsParams d = PhysicsParams.SafeDefault;

            TestContext.WriteLine($"F2a: cushion(e={d.cushionRestitution},μ={d.cushionTangentialFriction}) "
                                  + $"ball(e={d.ballRestitution},μ={d.ballTangentialFriction}) "
                                  + $"a_slide={d.rollingDecelerationSlide},a_roll={d.rollingDecelerationRoll},v_t={d.rollingTransitionSpeed}");

            Assert.AreEqual(1f, d.ballRestitution, "F2a：SafeDefault 的 e_ball 必须为 1.0（不得为 0）。");
            Assert.AreEqual(1f, d.ballTangentialFriction, "F2a：SafeDefault 的 μ_ball 必须为 1.0（0 会删除切向分量）。");
            Assert.AreEqual(1f, d.cushionRestitution, "F2a：SafeDefault 的库边 e 必须为 1.0。");
            Assert.AreEqual(1f, d.cushionTangentialFriction, "F2a：SafeDefault 的库边 μ 必须为 1.0。");
            // ⚠ 旧单值滚阻字段**已删**（退化档由"两段取同值"完整表达）⇒ 本句改为断言**两个新字段都为 0**。
            //   两段都为 0 ⇒ 内核门控为假 ⇒ 与历史"无滚阻"逐位一致（`T59` 钉）。
            Assert.AreEqual(0f, d.rollingDecelerationSlide, "F2a：SafeDefault 的滑动段滚阻必须为 0（历史“无滚阻”口径）。");
            Assert.AreEqual(0f, d.rollingDecelerationRoll, "F2a：SafeDefault 的滚动段滚阻必须为 0（历史“无滚阻”口径）。");
        }

        [Test]
        public void F2b_RuntimeCallSite_PassesEffectiveSafeParamsToKernel()
        {
            // ⚠ **本判据在 F2 修复前必然 FAIL**：`PhysicsTestDrive.Initialize()` 当时用
            // `new PhysicsParams{…只填 3 个字段…}` ⇒ 球-球两字段为 0 ⇒ 实机档 e_ball=0（完全非弹性）、
            // μ_ball=0（命中 μ<1 分支、切向分量被整个删除 = 非物理耗散）。
            // 断言的是**内核实际生效的参数**，而不是调用点源码 —— 因为 `default(PhysicsParams)` 恒为全零
            // 且能一路静默传到内核里。
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<TableConfig>(
                "Assets/_Project/Data/TableConfig.asset");
            Assert.IsNotNull(config, "F2b：需要真实 TableConfig.asset。");

            var go = new GameObject("F2bDrive");
            try
            {
                var drive = go.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = config;
                drive.Initialize();

                Assert.IsNotNull(drive.Simulator, "F2b：Initialize 后应已构造内核。");
                PhysicsParams effective = drive.Simulator.Params;

                TestContext.WriteLine($"F2b: 生效 e_ball={effective.ballRestitution} "
                                      + $"μ_ball={effective.ballTangentialFriction} "
                                      + $"cushion(e={effective.cushionRestitution},μ={effective.cushionTangentialFriction})");

                // ⚠ **期望值是「调参后的目标口径」，不是「安全兜底」** ——
                // 因此它**随 Day 7 的调参结论而变**；变更须记录（本条在 Day 7 由 `1.0` 改为 `0.95`）。
                // 对照：`PhysicsParams.SafeDefault` 的 `e_ball` **仍是 `1.0`**（"字段全零"的解药，由 `F2a` 钉住）——
                // **两个数字不是矛盾**：一个是"安全兜底"，一个是"调参目标口径"。
                Assert.AreEqual(0.95f, effective.ballRestitution,
                    "F2b：实机档 e_ball 必须为调参口径 0.95（0 = 球-球完全非弹性）。");
                Assert.AreEqual(1f, effective.ballTangentialFriction,
                    "F2b：实机档 μ_ball 必须为 1.0（0 = 切向分量被整个删除）。");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void F2c_ScriptsCode_NeverUsesDefaultOrFieldIncompletePhysicsParams()
        {
            // 本判据**替换**原先那条恒真断言：`default(PhysicsParams).ballRestitution == 0` 是 **C# 语言语义**，
            // 与产品代码无关 ⇒ **打不死**，违反工程标准 R3-2「禁止恒真断言」。改成**源码级守卫**（能被打死）。
            //
            // 断言：扫代码（剔除 `//` 行注释），
            //   ① 不出现 `default(PhysicsParams)`（= 字段全零 = 危险值）；
            //   ② 每个 `new PhysicsParams` 的**对象初始化器**必须显式含 `ballRestitution` 与
            //      `ballTangentialFriction`（否则会**静默漏设**球-球两字段 ⇒ 回到 e_ball=μ_ball=0 的老缺陷）。
            //
            // ── 扫描范围与排除（D5-FIX-04）──────────────────────────────────────
            //   · 扫 `Assets/_Project/Scripts/**` = **Runtime(9) + Editor(16) + Diagnostics(4) = 29 文件**（实测）。
            //   · **Editor 区 16 文件已逐个核**：`new PhysicsParams` / `default(PhysicsParams)` **各 0 处** ⇒ 无假阳性。
            //   · **`Tests/**` 明确排除**：测试侧构造是**诊断/白盒用、不喂给出厂内核**。
            //     例：`PoolSimulatorBallBallTests.cs:281`（T18b）的
            //     `new PoolSimulator(EmptyTable(), default(PhysicsParams)).SubSteps` —— **只读 `SubSteps`**
            //     （一个与入口参数无关的推导量），**不参与任何物理解算** ⇒ 属良性用法；
            //     纳入会**假 FAIL** 并迫使改掉良性测试代码。**产线风险由 F2b（唯一运行时调用点）承担。**
            //
            // ── ⚠ 三条**模式级盲带**（本守卫是**文本检查**，**非语义证明**；已逐条核实）──────
            //   ① **只匹配字面量**：`default (PhysicsParams)`（带空格）、`PhysicsParams p = default;`（目标类型推断）、
            //      `new()`、`Activator.CreateInstance<PhysicsParams>()` **均可绕过** check①（不检出）。
            //   ② **只剔 `//` 行注释**（未做词法分析）：**块注释 `/* … */` 与字符串字面量**里出现同名文本会**假 FAIL**。
            //   ③ 初始化器按**花括号配平**切取（D5-FIX-04 改进，取代"首个 `{` 到首个 `}`"，消除嵌套对象/lambda 假 FAIL）；
            //      残余：字符串字面量里的花括号仍可致错位（② 同族）。另，"**无初始化器**的 `new PhysicsParams()` +
            //      其后恰好有含字段名的无关 `{…}`"曾是**假 PASS**（漏报，soundness）——
            //      **已修**：要求 `{` 必须**紧接构造、其间无 `;`**，否则判"未用初始化器" ⇒ FAIL。
            //
            // 今天：唯一运行时构造点 `PhysicsParams.SafeDefault` 初始化器含全 5 字段 ⇒ PASS。
            // **可被打死**：调用点插 `default(PhysicsParams)`（注入 I6）⇒ FAIL；`SafeDefault` 删一 ball 字段（注入 I6b）⇒ FAIL。
            string scriptsDir = Path.Combine(Path.GetDirectoryName(Application.dataPath),
                "Assets/_Project/Scripts");
            Assert.IsTrue(Directory.Exists(scriptsDir), "F2c：找不到 Scripts 源码目录 " + scriptsDir);

            string[] files = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            Assert.IsTrue(files.Length > 0, "F2c：扫描空间为空（守卫会空转）。");

            int constructions = 0;
            foreach (string file in files)
            {
                string code = StripLineComments(File.ReadAllText(file));

                Assert.IsFalse(code.Contains("default(PhysicsParams)"),
                    $"F2c：`{Path.GetFileName(file)}` 代码出现 `default(PhysicsParams)`（字段全零危险值）"
                    + " —— 必须用 `PhysicsParams.SafeDefault` 或逐字段赋值。");

                int at = code.IndexOf("new PhysicsParams", System.StringComparison.Ordinal);
                while (at >= 0)
                {
                    int open = code.IndexOf('{', at);
                    int semi = code.IndexOf(';', at);

                    // ③ 反"假 PASS"：初始化器的 `{` 必须紧接构造（其间无 `;`）——否则是"无初始化器的 new PhysicsParams()"
                    Assert.IsTrue(open >= 0 && (semi < 0 || open < semi),
                        $"F2c：`{Path.GetFileName(file)}` 的 `new PhysicsParams` 未用对象初始化器"
                        + "（无法确认逐字段赋值）。");

                    // ③ 反"假 FAIL"：花括号配平取初始化器（取代"首个 { 到首个 }"）
                    int depth = 0;
                    int close = -1;
                    for (int i = open; i < code.Length; i++)
                    {
                        if (code[i] == '{')
                        {
                            depth++;
                        }
                        else if (code[i] == '}')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                close = i;
                                break;
                            }
                        }
                    }

                    Assert.IsTrue(close > open,
                        $"F2c：`{Path.GetFileName(file)}` 的 `new PhysicsParams` 初始化器花括号不配平。");

                    string init = code.Substring(open, close - open);
                    Assert.IsTrue(init.Contains("ballRestitution") && init.Contains("ballTangentialFriction"),
                        $"F2c：`{Path.GetFileName(file)}` 的 `new PhysicsParams` 初始化器**未显式含**球-球两字段"
                        + " —— 会静默落到 0（非物理耗散）。");

                    constructions++;
                    at = code.IndexOf("new PhysicsParams", at + 1, System.StringComparison.Ordinal);
                }
            }

            TestContext.WriteLine($"F2c: 扫 {files.Length} 个 Scripts 源文件（Runtime+Editor+Diagnostics，Tests 除外）；"
                                  + $"`new PhysicsParams` 构造 {constructions} 处（均含球-球两字段）；`default(PhysicsParams)` 0 处。");

            Assert.Greater(constructions, 0,
                "F2c：应至少有 1 处 `new PhysicsParams`（SafeDefault）—— 若为 0，说明扫描空间/写法变了，守卫会空转。");
        }

        /// <summary>剔除 `//` 行注释（与 D-3 源码守卫同法），避免注释里的 `default(PhysicsParams)` 假阳性。</summary>
        private static string StripLineComments(string src)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string line in src.Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                sb.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            return sb.ToString();
        }

        // ══════════════════ F1 初始摆放校验（含负路径）════════════════════════

        [Test]
        public void F1a_ValidateInitialPlacement_AcceptsLegalRack_RejectsOverlap()
        {
            Vector2[] rack = PhysicsTestDrive.RackPositions(new Vector2(0.5f, 0f));
            Assert.IsNull(PoolSimulator.ValidateInitialPlacement(rack),
                "F1a：合法球堆（间距 2r+2e-4）不得被判违规。");

            // 负路径：把某球挪到与另一球重叠
            Vector2[] bad = PhysicsTestDrive.RackPositions(new Vector2(0.5f, 0f));
            bad[2] = bad[1] + new Vector2(1e-3f, 0f);

            string violation = PoolSimulator.ValidateInitialPlacement(bad);
            TestContext.WriteLine($"F1a: 合法→{(violation == null ? "通过" : "误报")}；重叠注入→{violation ?? "未检出(错)"}");

            Assert.IsNotNull(violation, "F1a：重叠摆放必须被拒绝（否则本校验项无判别力）。");
        }

        // ══════════════════ T25 候选对完备性 ═════════════════════════════════

        [Test]
        public void T25_CandidatePairs_EqualsBruteForceEnumeration()
        {
            var first = new List<int>();
            var second = new List<int>();
            PoolSimulator.CollectCandidatePairs(4, first, second);

            int[] expectedFirst = { 0, 0, 0, 1, 1, 2 };
            int[] expectedSecond = { 1, 2, 3, 2, 3, 3 };

            var shown = new List<string>();
            for (int i = 0; i < first.Count; i++)
            {
                shown.Add($"({first[i]},{second[i]})");
            }

            TestContext.WriteLine("T25: " + string.Join(" ", shown));

            Assert.AreEqual(expectedFirst.Length, first.Count, "T25：4 球应产出 n(n−1)/2 = 6 对。");
            for (int i = 0; i < expectedFirst.Length; i++)
            {
                Assert.AreEqual(expectedFirst[i], first[i], $"T25：第 {i} 对的 first 不符（顺序也须一致）。");
                Assert.AreEqual(expectedSecond[i], second[i], $"T25：第 {i} 对的 second 不符。");
            }
        }

        // ══════════════════ T26 group 归并（同刻段 + 球-球）══════════════════

        [Test]
        public void T26_SameInstantSegmentAndBall_ShareOneEventGroup()
        {
            // 段 = 直线 y = 0（内侧 +y）；B = (0.20, r) 静止；A0 = (0.09285, 0.078575)，v 方向 (1,−1)
            // ⇒ Δx = 0.05 时 A = (0.142850, 0.028575)：距段 = r 且 |A−B| = 2r ⇒ 同一 t ⇒ 同一 group。
            var table = Table(new Vector2(-1f, 0f), new Vector2(1f, 0f));
            PoolSimulator sim = Sim(table, 1.0f, 1.0f, 1.0f,
                Ball(0.09285f, 0.078575f, 1f, -1f),
                Ball(0.20f, R, 0f, 0f));

            Step(sim, 60);

            float distB = Mathf.Abs(sim.Balls[1].position.y);

            TestContext.WriteLine($"T26: ballBall={sim.BallBallContactCount} total={sim.TotalContactCount} "
                                  + $"peakIters={sim.BallBallMaxIterationsObserved} guardTrips={sim.ToiIterationGuardTrips} "
                                  + $"energyGain={sim.EnergyGainViolations} B.distToSeg={distB:E3}");

            Assert.AreEqual(0, sim.EnergyGainViolations, "T26：group 内顺序解算不得升能。");
            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T26：迭代守卫必须为 0。");
            Assert.GreaterOrEqual(sim.BallBallContactCount, 1, "T26：非空转（球-球接触必须发生）。");
            Assert.GreaterOrEqual(sim.TotalContactCount, 2, "T26：段接触与球-球接触都应发生（佐证同 group）。");
            Assert.LessOrEqual(sim.TotalContactCount, 8,
                "T26：总解算次数须有**上界**（删掉球-段接近判据会让它涨到数百）。");
            Assert.LessOrEqual(sim.BallBallContactCount, 4, "T26：球-球解算次数须有上界。");
            Assert.GreaterOrEqual(distB, R - 1e-6f, "T26：B 不得钻入库边。");
        }

        // ══════════════════ T21 全局事件循环自守卫（四条不变量）══════════════

        [Test]
        public void T21_EventLoopInvariants()
        {
            // 构型 = T26（段 + 球-球同刻）—— 本轮声明的"密集"档即此构型。
            var table = Table(new Vector2(-1f, 0f), new Vector2(1f, 0f));
            PoolSimulator sim = Sim(table, 1.0f, 1.0f, 1.0f,
                Ball(0.09285f, 0.078575f, 1f, -1f),
                Ball(0.20f, R, 0f, 0f));

            Step(sim, 60);

            float vRel = 2f * PoolConstants.MaxBallSpeed;
            float dtSub = sim.SubStepDeltaTime;
            bool linked = 0f < 0.5f * vRel * dtSub && 0.5f * vRel * dtSub < vRel * dtSub;

            TestContext.WriteLine($"T21: peakIters={sim.BallBallMaxIterationsObserved} (MaxToiIterations={PoolSimulator.MaxToiIterations}) "
                                  + $"guardTrips={sim.ToiIterationGuardTrips} subSteps={sim.SubSteps} linked={linked}");

            Assert.AreEqual(0, sim.ToiIterationGuardTrips, "T21#1：迭代守卫必须为 0。");
            Assert.GreaterOrEqual(sim.BallBallMaxIterationsObserved, 1,
                "T21#2 下界：事件循环至少跑过一轮。");
            Assert.LessOrEqual(sim.BallBallMaxIterationsObserved, PoolSimulator.MaxToiIterations / 2,
                "T21#2 上界：峰值迭代数必须留出 ≥ 2× 余量（≤ 0.5 × MaxToiIterations）。");
            Assert.IsTrue(linked, "T21#3：0.5·v_rel·dt_sub 必须落在 (0, v_rel·dt_sub) 内。");
            Assert.AreEqual(PoolSimulator.DeriveSubSteps(PoolConstants.MaxBallSpeed), sim.SubSteps,
                "T21#4：SubSteps 必须等于推导值。");
        }
    }
}
