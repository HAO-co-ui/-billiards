using System.Collections.Generic;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// 袋口捕获与袋角行为判据族（编号续内核 II，自 `T27` 起）。
    ///
    /// 判据（规范二维物理平面，米）：
    ///  · 越界量   `s  = dot(C − tc, tn)`（`tn` 指向网内）；
    ///  · 横向偏移 `ρ  = |dot(C − tc, td)|`，`td = (−tn.y, tn.x)` —— **面内**偏移、**不含法向分量**。
    ///  · `Captured` = `s > 0 ∧ ρ ≤ 该袋 openingRadius`（球心已在洞口上方）⇒ **冻结（离台）**；
    ///    `JawCrossing` = `s > 0 ∧ ρ > openingRadius`（球心在喉口之外，本工程只能靠**隧穿**达到）
    ///    ⇒ **送回台内**（不冻结）；两者各自独立累加 ⇒
    ///    `PocketProximityEvents + AtRestMouthDrops == Captured + JawCrossing`。
    ///
    /// ⚠ **边界量是 `openingRadius`（不再是 `captureRadius`）** —— 这是「袋角归属裁定 A」的口径，
    /// 依据与四种候选动作的实测见 `Docs/35-A_袋角归属裁定_spec.md` §4c/§4d。
    /// 历史口径（`ρ ≤ captureRadius ⇒ 捕获`，否则越界冻结）**已作废** ⇒ 原以 `captureRadius` 为边界的
    /// `T31a/T31b/T31c` 随之**口径改写**（`T31b` 改为"超出喉口 ⇒ 不捕获"、新增 `T31d` 补边界双侧判别）。
    ///
    /// **几何两路**：真实表（6 袋逐一、擦角）与合成表（解析期望值、无真实网格干扰）。
    /// 每条判据都声明"能测出什么 / 测不出什么（盲带）"。
    /// </summary>
    public class PoolSimulatorPocketCaptureTests
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

        private static float Axial(Vector2 c, Pocket p) => Dot(c - p.throatCenter, p.throatNormal);

        private static float Lateral(Vector2 c, Pocket p)
        {
            Vector2 td = new Vector2(-p.throatNormal.y, p.throatNormal.x);
            return Mathf.Abs(Dot(c - p.throatCenter, td));
        }

        // ══════════════════ T27 6 袋逐一捕获 ══════════════════════════════

        /// <summary>
        /// 真实表：对每个袋沿其**喉法向**从台侧直入，断言逐袋被捕获且入对袋。
        ///
        /// 能测出：捕获路径未实现、法向取反（`s` 符号反 ⇒ 从台侧就冻结）、袋索引错。
        /// 盲带：直入时 `ρ ≈ 0` ⇒ **对 `captureRadius` 的取值不敏感**（阈值判别由 `T31` 承担）。
        /// </summary>
        [Test]
        public void T27a_PocketCapture_SweepAllSixPockets()
        {
            int capturedTotal = 0;
            for (int p = 0; p < _config.PocketCount; p++)
            {
                Pocket pocket = _config.pockets[p];
                Vector2 tn = pocket.throatNormal;
                Vector2 start = pocket.throatCenter - 0.35f * tn;

                var sim = new PoolSimulator(_config, Params());
                sim.LoadBalls(new[] { new BallState { position = start, velocity = 2f * tn } });

                for (int s = 0; s < 200 && sim.CapturedBallCount == 0; s++)
                {
                    sim.Step();
                }

                Assert.AreEqual(1, sim.CapturedBallCount, $"T27a：袋 {p} 未被捕获（应沿喉法向直入）。");
                Assert.AreEqual(p, sim.CapturedPocketIndex(0), $"T27a：球入袋索引应为 {p}。");

                Vector2 c = sim.Balls[0].position;
                Assert.Greater(Axial(c, pocket), 0f, $"T27a：袋 {p} 捕获位置应在喉平面进网侧，实测 s={Axial(c, pocket):R}。");
                Assert.LessOrEqual(Lateral(c, pocket), pocket.captureRadius + 1e-6f,
                    $"T27a：袋 {p} 捕获位置横向偏移 {Lateral(c, pocket):R} 超 captureRadius {pocket.captureRadius:R}。");

                Assert.AreEqual(0, sim.PocketJawCrossingCount, $"T27a：袋 {p} 直入不应命中窗口。");
                Assert.AreEqual(0, sim.EnergyGainViolations, $"T27a：袋 {p} 不应升能。");
                capturedTotal++;
            }

            Assert.AreEqual(_config.PocketCount, capturedTotal, "T27a：应覆盖全部 6 袋。");
            TestContext.WriteLine($"T27a: 6 袋逐一捕获 ✓（pocketCount={_config.PocketCount}）");
        }

        /// <summary>
        /// 合成单袋（无段）：`tc=(0,0)`、`tn=(0,−1)`、`captureRadius=0.0307`；球自 `(0, 0.20)` 沿 `−y` 直入。
        /// 期望（甲类，几何）：约 `t = 0.10 s` 捕获、捕获位置 `y &lt; 0`、`ρ = 0`。
        /// </summary>
        [Test]
        public void T27b_PocketCapture_AnalyticStraightIn()
        {
            Pocket pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), 0.0307f);
            var sim = new PoolSimulator(Synthetic(pocket), Params());
            sim.LoadBalls(new[] { Ball(0f, 0.20f, 0f, -2f) });

            Step(sim, 120);

            Assert.AreEqual(1, sim.CapturedBallCount, "T27b：合成袋直入应捕获。");
            Assert.AreEqual(0, sim.CapturedPocketIndex(0), "T27b：应落入袋 0。");
            Assert.AreEqual(1, sim.PocketProximityEvents, "T27b：越界应恰 1 次。");
            Assert.AreEqual(0, sim.PocketJawCrossingCount, "T27b：直入不应命中窗口。");

            Vector2 c = sim.Balls[0].position;
            Assert.Less(c.y, 0f, $"T27b：捕获位置应在进网侧（y<0），实测 y={c.y:R}。");
            Assert.Less(Mathf.Abs(c.x), 1e-4f, $"T27b：沿轴直入 ⇒ ρ≈0，实测 x={c.x:R}。");
            TestContext.WriteLine($"T27b: 捕获位 c={c:R}, events={sim.PocketProximityEvents}");
        }

        // ══════════════════ T28 擦袋口不落（反例式）════════════════════════

        /// <summary>
        /// 真实表：球心在 −y 长库内侧、**擦过侧袋口**（`x` 横穿缺口）—— 不越喉平面 ⇒ 不捕获。
        /// 能测出：把冻结写成"恒冻结"、把判据写成"任何球都捕获"。
        /// 盲带：不覆盖"越喉平面但偏离开口"的窗口球（那是 `R-S`，见 `T31b`/`T35`）。
        /// </summary>
        [Test]
        public void T28a_GrazingAlongCushionPastSidePocket_IsNotCaptured()
        {
            float y0 = -(PoolConstants.ClothHalfWidth - PoolConstants.BallRadius) + 3e-3f;
            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f));
            sim.LoadBalls(new[] { Ball(-0.40f, y0, 2f, 0f) });

            Step(sim, 60);

            Assert.AreEqual(0, sim.PocketProximityEvents, "T28a：擦过袋口不应越喉平面。");
            Assert.AreEqual(0, sim.CapturedBallCount, "T28a：擦过袋口不应被捕获。");
            Assert.AreEqual(0, sim.PocketJawCrossingCount, "T28a：不应命中窗口。");
            Assert.LessOrEqual(Mathf.Abs(sim.Balls[0].position.y), PoolConstants.ClothHalfWidth,
                "T28a：球应仍在台面内。");
            Assert.Greater(Mag(sim.Balls[0].velocity), 0f, "T28a：不应被置零（恒冻结）。");
            TestContext.WriteLine($"T28a: 擦过袋口未捕获 ✓ 末位={sim.Balls[0].position:R}");
        }

        /// <summary>
        /// 真实表：球沿喉法向平行、横向偏移 `ρ0 = 0.045 m` 射向角袋 —— **先撞袋角被封堵**、
        /// 弹回台面、**永不越喉平面**（若无袋角几何，该球会穿过袋喉越界 ⇒ `ProximityEvents &gt; 0` ⇒ 失败）。
        /// </summary>
        [Test]
        public void T28b_ShotAtCornerJaw_IsBlockedAndStaysOnTable()
        {
            Pocket pocket = _config.GetPocket(0);
            Vector2 tn = pocket.throatNormal;
            Vector2 td = new Vector2(-tn.y, tn.x);
            Vector2 start = pocket.throatCenter - 0.12f * tn + 0.045f * td;

            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f));
            sim.LoadBalls(new[] { new BallState { position = start, velocity = 2f * tn } });

            Step(sim, 200);

            Assert.AreEqual(0, sim.CapturedBallCount, "T28b：擦袋角不应被捕获。");
            Assert.AreEqual(0, sim.PocketProximityEvents, "T28b：被封堵 ⇒ 不应越喉平面（否则袋角几何未参与碰撞）。");
            Assert.Greater(sim.TotalContactCount, 0, "T28b：必须真的撞到袋角（非空转）。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T28b：袋角解算不得升能。");
            Assert.LessOrEqual(Mathf.Abs(sim.Balls[0].position.x), PoolConstants.ClothHalfLength, "T28b：球应仍在台面内（x）。");
            Assert.LessOrEqual(Mathf.Abs(sim.Balls[0].position.y), PoolConstants.ClothHalfWidth, "T28b：球应仍在台面内（y）。");
            TestContext.WriteLine($"T28b: 擦袋角被封堵 ✓ contacts={sim.TotalContactCount}, 末位={sim.Balls[0].position:R}");
        }

        // ══════════════════ T29 库边附近擦过不误判 ══════════════════════════

        /// <summary>
        /// 真实表：斜射袋口**邻近**的库边（`s &lt; 0`）→ 撞库反弹，**不越任何喉平面**。
        /// 能测出：把判据写成"球心到袋心距离 &lt; 某半径即捕获"（球掠过时可能落入开口半径 ⇒ 假捕获）。
        /// 盲带：不覆盖窗口球（`R-S`）。
        /// </summary>
        [Test]
        public void T29_SlantedShotNearPocket_NoProximityEvent()
        {
            var sim = new PoolSimulator(_config, Params(0.9f, 0.8f));
            sim.LoadBalls(new[] { Ball(0.30f, -0.55f, -1.5f, -1.0f) });

            Step(sim, 120);

            Assert.AreEqual(0, sim.PocketProximityEvents, "T29：擦库不应越喉平面。");
            Assert.AreEqual(0, sim.PocketJawCrossingCount, "T29：不应命中窗口。");
            Assert.Greater(sim.TotalContactCount, 0, "T29：应撞到库边（非空转）。");
            TestContext.WriteLine($"T29: 库边附近擦过未误判 ✓ contacts={sim.TotalContactCount}");
        }

        // ══════════════════ T30 袋角偏折 ══════════════════════════════════

        /// <summary>
        /// 合成单段（竖直"袋角面"）：球正撞 ⇒ 镜面反射（法向独立乘 e、切向独立乘 μ）。
        /// 期望（甲类）：`v_out·n̂ = −e·(v_in·n̂) = 1.8`、切向不变（v_in 切向 = 0）。
        /// </summary>
        [Test]
        public void T30a_JawFacet_MirrorReflection()
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>
            {
                new CushionSegment { a = new Vector2(0f, 0f), b = new Vector2(0f, 0.4f), rail = "synthetic" },
            };
            t.pockets = new List<Pocket>();

            var sim = new PoolSimulator(t, Params(0.9f, 0.8f));
            sim.LoadBalls(new[] { Ball(-0.20f, 0.20f, 2f, 0f) });

            Step(sim, 60);

            Vector2 v = sim.Balls[0].velocity;
            Assert.Greater(sim.TotalContactCount, 0, "T30a：必须真的撞段。");
            Assert.Less(Mathf.Abs(v.x - (-1.8f)), 1e-4f, $"T30a：法向出射应为 −e·2 = −1.8（球被弹回 −x），实测 vx={v.x:R}。");
            Assert.Less(Mathf.Abs(v.y), 1e-4f, $"T30a：切向应保持 0，实测 vy={v.y:R}。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T30a：不得升能。");
            TestContext.WriteLine($"T30a: 镜面反射 v_out=({v.x:F6},{v.y:F6})（期望 (1.8,0)）");
        }

        /// <summary>
        /// 合成**斜撞**单段（水平"袋角面"）：法向独立乘 `e`、切向独立乘 `μ`（镜面律）。
        /// `v_in = (1, −2)`、`e = 0.9`、`μ = 0.8`、`n̂ = (0, 1)` ⇒ `v_n = −2`、`v_t = (1, 0)` ⇒
        /// `v_out = (μ·1, −e·v_n) = (0.8, 1.8)`。
        /// （真实角袋的袋角偏折由 `T28b` 覆盖；本档用解析几何把 `e` 与 `μ` **同时**钉住。）
        /// </summary>
        [Test]
        public void T30b_JawFacet_ObliqueMirrorReflection()
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>
            {
                new CushionSegment { a = new Vector2(-0.5f, 0f), b = new Vector2(0.5f, 0f), rail = "synthetic" },
            };
            t.pockets = new List<Pocket>();

            var sim = new PoolSimulator(t, Params(0.9f, 0.8f));
            sim.LoadBalls(new[] { Ball(-0.30f, 0.20f, 1f, -2f) });

            Step(sim, 60);

            Vector2 v = sim.Balls[0].velocity;
            Assert.Greater(sim.TotalContactCount, 0, "T30b：必须真的撞段。");
            Assert.Less(Mathf.Abs(v.x - 0.8f), 1e-4f, $"T30b：切向出射应为 μ·1 = 0.8，实测 vx={v.x:R}。");
            Assert.Less(Mathf.Abs(v.y - 1.8f), 1e-4f, $"T30b：法向出射应为 e·2 = 1.8，实测 vy={v.y:R}。");
            Assert.AreEqual(0, sim.EnergyGainViolations, "T30b：不得升能。");
            TestContext.WriteLine($"T30b: 斜撞镜面 v_out=({v.x:F6},{v.y:F6})（期望 (0.8,1.8)）");
        }

        // ══════════════════ T31 喉口边界双边判别（判别力来源）════════════

        /// <summary>探针用的合成袋参数：`captureRadius = 0.0307`、`openingRadius = cr + r + 2 mm`
        /// （与 <see cref="MakePocket"/> 的构造口径逐字一致）。</summary>
        private const float ProbeCaptureRadius = 0.0307f;

        private static float ProbeOpeningRadius => ProbeCaptureRadius + PoolConstants.BallRadius + 0.002f;

        private static PoolSimulator RadiusProbe(float rho, out Pocket pocket)
        {
            pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), ProbeCaptureRadius);
            var sim = new PoolSimulator(Synthetic(pocket), Params());
            sim.LoadBalls(new[] { Ball(rho, 0.20f, 0f, -PoolConstants.MaxBallSpeed) });
            Step(sim, 30);
            return sim;
        }

        /// <summary>
        /// `ρ = captureRadius − 1e-3`（远在喉口内）⇒ **必须捕获**。
        /// 用 `8 m/s`（单子步位移 22.2 mm）使 `ρ` 与 `|C − tc|` **可分**：
        /// 若实现误用 `|C − tc|`（含法向分量），越界那一子步 `|C−tc| ≈ √(0.0297²+0.0222²) = 0.037` ⇒ 本档失败。
        ///
        /// ⚠ **判别力登记**：本档的 ρ 同时满足"≤ captureRadius"与"≤ openingRadius" ⇒
        /// 在裁定 A 的口径下它**不再区分这两个量**（边界判别力已由 `T31b`/`T31d` 承担，见下）。
        /// </summary>
        [Test]
        public void T31a_InsideThroatOpening_FarInside_IsCaptured()
        {
            PoolSimulator sim = RadiusProbe(ProbeCaptureRadius - 1e-3f, out Pocket pocket);
            Assert.AreEqual(1, sim.CapturedBallCount, "T31a：喉口内（远中心）的球必须被捕获。");
            Assert.AreEqual(0, sim.PocketJawCrossingCount, "T31a：喉口内不应计擦袋角。");
            TestContext.WriteLine($"T31a: ρ={ProbeCaptureRadius - 1e-3f:F4} ⇒ 捕获 ✓");
        }

        /// <summary>
        /// `ρ = openingRadius + 1e-3`（**超出喉口**）⇒ **不得捕获**，计擦袋角、**冻结离台**
        /// （归属 = "离台但不计入袋"）。
        ///
        /// **这是本轮唯一能"打死"`openingRadius` 上界的负对照**：若实现把边界放宽成
        /// "越喉即捕获"或误用 `ρ &lt; 某更大值`，本档失败。它是旧 `T31b`
        /// （`ρ = captureRadius + 1e-3 ⇒ 不捕获`）在裁定 A 下的**等价替换** ——
        /// 旧断言已作废（该 ρ 落在喉口内，按新口径必须捕获）。
        /// </summary>
        [Test]
        public void T31b_OutsideThroatOpening_IsNotCaptured()
        {
            float rho = ProbeOpeningRadius + 1e-3f;
            PoolSimulator sim = RadiusProbe(rho, out Pocket pocket);
            Assert.AreEqual(0, sim.CapturedBallCount, "T31b：超出喉口的球不得被捕获（否则 openingRadius 上界未被使用）。");
            Assert.AreEqual(1, sim.PocketJawCrossingCount, "T31b：超出喉口应计为擦袋角越界。");
            Assert.AreEqual(1, sim.PocketProximityEvents, "T31b：仍应越喉平面（越喉事件）。");
            Assert.IsFalse(sim.IsBallInPlay(0), "T31b：擦袋角越界应**离台**（不得静默留在台面外）。");
            Assert.AreEqual(-1, sim.CapturedPocketIndex(0), "T31b：越界冻结**不计入袋**（入袋索引 −1）。");
            TestContext.WriteLine($"T31b: ρ={rho:F4} > opening={ProbeOpeningRadius:F4} ⇒ 不捕获、计擦袋角、越界冻结 ✓");
        }

        /// <summary>`ρ = captureRadius` 恰等（仍在喉口内）⇒ 捕获（`≤` 语义、边界闭合），
        /// 且**不在**擦袋角计数里。</summary>
        [Test]
        public void T31c_CaptureRadius_ExactBoundary_IsCaptured()
        {
            PoolSimulator sim = RadiusProbe(ProbeCaptureRadius, out Pocket pocket);
            Assert.AreEqual(1, sim.CapturedBallCount, "T31c：captureRadius 处（喉口内）应捕获。");
            Assert.AreEqual(0, sim.PocketJawCrossingCount, "T31c：不应判为擦袋角。");
            TestContext.WriteLine($"T31c: ρ=cr={ProbeCaptureRadius:F4} ⇒ 捕获 ✓");
        }

        /// <summary>
        /// **喉口边界（`openingRadius`）双侧判别** —— 裁定 A 的口径下"哪个量是边界"的守卫。
        /// 内侧（`openingRadius − 1e-3`）⇒ 捕获；外侧（`+1e-3`）⇒ 不捕获且计擦袋角。
        /// 能测出：把边界写回 `captureRadius`（内侧档仍过，外侧档失败）、或写成"无上界"。
        /// </summary>
        [Test]
        public void T31d_ThroatOpeningBoundary_BothSides()
        {
            PoolSimulator inside = RadiusProbe(ProbeOpeningRadius - 1e-3f, out Pocket pocketIn);
            Assert.AreEqual(1, inside.CapturedBallCount, "T31d：喉口内侧 1 mm 必须捕获。");
            Assert.AreEqual(0, inside.PocketJawCrossingCount, "T31d：喉口内侧不应计擦袋角。");

            PoolSimulator outside = RadiusProbe(ProbeOpeningRadius + 1e-3f, out Pocket pocketOut);
            Assert.AreEqual(0, outside.CapturedBallCount, "T31d：喉口外侧 1 mm 不得捕获。");
            Assert.AreEqual(1, outside.PocketJawCrossingCount, "T31d：喉口外侧应计擦袋角。");

            TestContext.WriteLine($"T31d: opening={ProbeOpeningRadius:F6} 双侧判别 ✓（内 1 mm 捕获 / 外 1 mm 送回）");
        }

        // ══════════════════ T35 捕获归类的一致性（等值）════════════════════

        /// <summary>
        /// 同一场景内**同时**发生 1 次捕获与 1 次窗口越界 ⇒ 断言三计数**等值**且各恰 1。
        ///
        /// 能测出：计数虚增（抖动/重复冻结/重复分类，两侧会同时虚增 ⇒ 等值仍成立，故用**等值**而非 ≥1）；
        ///         漏计；把两类混算。
        /// **盲带**：① 两侧同向同幅漂移时本判据可能仍过（由 `T31` 的绝对界补）；
        ///          ② 若实现以"相加"求 `ProximityEvents`，本判据**恒真**（前提见 §3.2 R-A：三计数须独立累加）。
        /// </summary>
        [Test]
        public void T35_CaptureClassification_EqualityHolds()
        {
            Pocket pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), 0.0307f);
            var sim = new PoolSimulator(Synthetic(pocket), Params());
            sim.LoadBalls(new[]
            {
                Ball(0.00f, 0.20f, 0f, -PoolConstants.MaxBallSpeed),   // ρ=0    ⇒ 捕获
                Ball(0.08f, 0.60f, 0f, -PoolConstants.MaxBallSpeed),   // ρ=0.08 ⇒ 窗口
            });

            Step(sim, 40);

            Assert.AreEqual(1, sim.CapturedBallCount, "T35：应恰有 1 次捕获。");
            Assert.AreEqual(1, sim.PocketJawCrossingCount, "T35：应恰有 1 次窗口越界。");
            Assert.AreEqual(2, sim.PocketProximityEvents, "T35：越界事件应恰 2 次。");
            Assert.AreEqual(sim.CapturedBallCount + sim.PocketJawCrossingCount,
                sim.PocketProximityEvents + sim.AtRestMouthDrops,
                "T35：归类应完备（ProximityEvents + AtRestMouthDrops == Captured + JawCrossing）。");
            TestContext.WriteLine(
                $"T35: proxy={sim.PocketProximityEvents}, captured={sim.CapturedBallCount}, jaw={sim.PocketJawCrossingCount} ✓");
        }

        // ══════════════════ T36 多球同一步越界 ⇒ 冻结顺序确定 ══════════════

        /// <summary>
        /// 合成双袋：两球在**同一步**分别越两个袋的平面 ⇒ 两球都被捕获、入对袋；
        /// 两个**独立构造**的模拟器逐位一致（冻结写入按球索引升序 ⇒ 顺序确定）。
        /// 能测出：多球同时越界的冻结顺序不确定（例如用无序容器收集"本子步要冻结的球"）。
        /// 盲带：仅覆盖"同一步、两个袋"。
        /// </summary>
        [Test]
        public void T36_MultiBallSameStepThroatCrossing_FreezeOrderDeterministic()
        {
            // 两袋法向沿 ∓x，使**各球只越各自袋的平面**（否则一球会先被另一袋的"无限平面"按索引序截获）。
            Pocket left = MakePocket(0, new Vector2(-0.20f, 0f), new Vector2(-1f, 0f), 0.0307f);
            Pocket right = MakePocket(1, new Vector2(0.20f, 0f), new Vector2(1f, 0f), 0.0307f);
            TableConfig table = Synthetic(left, right);

            BallState[] initial =
            {
                Ball(-0.10f, 0f, -PoolConstants.MaxBallSpeed, 0f),
                Ball(0.10f, 0f, PoolConstants.MaxBallSpeed, 0f),
            };

            var a = new PoolSimulator(table, Params());
            var b = new PoolSimulator(table, Params());
            a.LoadBalls(initial);
            b.LoadBalls(initial);

            Step(a, 40);
            Step(b, 40);

            Assert.AreEqual(2, a.CapturedBallCount, "T36：两球都应被捕获。");
            Assert.AreEqual(0, a.PocketJawCrossingCount, "T36：直入不应命中窗口。");
            Assert.AreEqual(0, a.CapturedPocketIndex(0), "T36：球 0 应入袋 0。");
            Assert.AreEqual(1, a.CapturedPocketIndex(1), "T36：球 1 应入袋 1。");

            for (int i = 0; i < initial.Length; i++)
            {
                Assert.AreEqual(a.Balls[i].position.x.ToString("R"), b.Balls[i].position.x.ToString("R"),
                    $"T36：球 {i} 位置 x 逐位不一致（冻结顺序不确定）。");
                Assert.AreEqual(a.Balls[i].position.y.ToString("R"), b.Balls[i].position.y.ToString("R"),
                    $"T36：球 {i} 位置 y 逐位不一致。");
            }

            TestContext.WriteLine($"T36: 同一步两球越两袋，入袋 {a.CapturedPocketIndex(0)}/{a.CapturedPocketIndex(1)}，双跑逐位一致 ✓");
        }

        // ══════════════════ T106 喉口内越喉 ⇒ 入袋（裁定 A）════════════════

        /// <summary>
        /// **T106：喉口内越喉 ⇒ 入袋** —— `ρ` 取**旧"擦袋角带"的中值**
        /// `(captureRadius + openingRadius)/2`。该带在历史口径下"越界冻结"（用户实测：球像被粘在网框上）、
        /// 在规格初稿里"弹回"；四种候选动作的实测见 `Docs/35-A_袋角归属裁定_spec.md` §4c
        /// ⇒ 裁定 A 归入**入袋**（球心已在洞口上方）。
        ///
        /// 断言：捕获 1、擦袋角 0、入对袋、离台、捕获位在喉平面**进网侧**、横向偏移 ≤ `openingRadius`。
        /// 盲带：只覆盖正对喉口的一条直线；边界判别由 `T31b`/`T31d` 承担。
        /// </summary>
        [Test]
        public void T106_InsideThroatOpening_IsCaptured()
        {
            float rho = 0.5f * (ProbeCaptureRadius + ProbeOpeningRadius);
            PoolSimulator sim = RadiusProbe(rho, out Pocket pocket);

            Assert.AreEqual(1, sim.CapturedBallCount, $"T106：ρ={rho:F4} 落在喉口内 ⇒ 必须入袋。");
            Assert.AreEqual(0, sim.PocketJawCrossingCount, "T106：喉口内不应计擦袋角。");
            Assert.AreEqual(1, sim.PocketProximityEvents, "T106：应恰 1 次越喉事件。");
            Assert.AreEqual(0, sim.CapturedPocketIndex(0), "T106：应入袋 0。");
            Assert.IsFalse(sim.IsBallInPlay(0), "T106：入袋球应离台（冻结）。");

            Vector2 c = sim.Balls[0].position;
            Assert.Greater(Axial(c, pocket), 0f, $"T106：捕获位应在喉平面进网侧，实测 s={Axial(c, pocket):R}。");
            Assert.LessOrEqual(Lateral(c, pocket), pocket.openingRadius + 1e-6f,
                $"T106：捕获位横向偏移 {Lateral(c, pocket):R} 超 openingRadius {pocket.openingRadius:R}。");
            TestContext.WriteLine($"T106: ρ={rho:F4}∈(cap,open) ⇒ 入袋 ✓ 捕获位 c={c:R}");
        }

        // ══════════════════ T107 超出喉口 ⇒ 越界冻结（不静默飞台）════════════

        /// <summary>
        /// **T107：`ρ &gt; openingRadius`（只能靠隧穿达到）⇒ 计擦袋角 + 冻结离台**。
        /// 三条候选动作的实测（见规格 §4c）：什么都不做 ⇒ 球**静默飞出台面**（x = −7.077 m）；
        /// 送回台内 + 反射 ⇒ 球被放进袋角环空腔、TOI 同刻接触组每子步解算 ~15 轮（总接触 199）
        /// ⇒ 冻结是这里唯一**可证安全**的动作。
        ///
        /// 断言：不计入袋（入袋索引 −1）、计擦袋角恰 1、**离台**、速度置零 —— 即"离台但不入袋"。
        /// </summary>
        [Test]
        public void T107_OutsideThroatOpening_IsFrozenOffTable()
        {
            float rho = ProbeOpeningRadius + 1e-3f;
            PoolSimulator sim = RadiusProbe(rho, out Pocket pocket);

            Assert.AreEqual(0, sim.CapturedBallCount, "T107：超出喉口不得计入袋。");
            Assert.AreEqual(1, sim.PocketJawCrossingCount, "T107：应计擦袋角越界。");
            Assert.AreEqual(1, sim.PocketProximityEvents, "T107：应恰 1 次越喉事件。");
            Assert.AreEqual(-1, sim.CapturedPocketIndex(0), "T107：越界冻结**不计入袋**（入袋索引为 −1）。");
            Assert.IsFalse(sim.IsBallInPlay(0), "T107：越界球必须离台（冻结）—— 不得静默留在台面外。");
            Assert.AreEqual(0f, sim.Balls[0].velocity.x, "T107：越界球速度应置零（x）。");
            Assert.AreEqual(0f, sim.Balls[0].velocity.y, "T107：越界球速度应置零（y）。");
            TestContext.WriteLine($"T107: ρ={rho:F4} > opening={ProbeOpeningRadius:F4} ⇒ 越界冻结（离台不入袋）✓");
        }

        // ══════════════════ T108 归类恒等式 + 静止补判 ═════════════════════

        /// <summary>
        /// **T108：归类恒等式 + 静止补判入袋**。
        ///
        /// (a) `PocketProximityEvents + AtRestMouthDrops == CapturedBallCount + PocketJawCrossingCount`
        ///     在**四类**场景各成立（正中入袋 / 喉口内 / 超出喉口 / 静止补判）。
        ///     ⚠ `AtRestMouthDrops` **不进** `PocketProximityEvents`（它不是越喉事件）
        ///     ⇒ 恒等式左边必须显式带上它，否则判据恒假。
        /// (b) **正例**：停在开口内（未越喉）的球 ⇒ 补判入袋 + 入袋索引指向该袋 + 离台；
        ///     **负例**：仍在移动的球**不得**被补判（飞行球不误伤）。
        /// </summary>
        [Test]
        public void T108_ClassificationIdentity_AndAtRestMouthDrop()
        {
            string report = string.Empty;

            // (a) 场景 1–3：合成袋（无段）
            foreach (float rho in new[] { 0f, 0.5f * (ProbeCaptureRadius + ProbeOpeningRadius), ProbeOpeningRadius + 1e-3f })
            {
                PoolSimulator s = RadiusProbe(rho, out Pocket p);
                int left = s.PocketProximityEvents + s.AtRestMouthDrops;
                int right = s.CapturedBallCount + s.PocketJawCrossingCount;
                Assert.AreEqual(right, left, $"T108：ρ={rho:F4} 场景恒等式不成立（左 {left} / 右 {right}）。");
                report += $"[ρ={rho:F4}: {s.PocketProximityEvents}+{s.AtRestMouthDrops}=={s.CapturedBallCount}+{s.PocketJawCrossingCount}] ";
            }

            // (b) 正例：静止补判
            Pocket pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), ProbeCaptureRadius);
            var rest = new PoolSimulator(Synthetic(pocket), Params());
            rest.LoadBalls(new[] { Ball(0f, 0.03f, 0f, 0f) });   // s = −0.03（未越喉）、|C−tc| = 0.03 ≤ openingRadius
            Step(rest, PoolSimulator.RestStableFrames + 2);

            Assert.IsTrue(rest.TableAtRest, "T108：前提不成立 —— 该场景应达成整盘静止。");
            Assert.AreEqual(1, rest.AtRestMouthDrops, "T108：停在开口内的球应被补判入袋。");
            Assert.AreEqual(1, rest.CapturedBallCount, "T108：补判应计入 CapturedBallCount（走与正常入袋相同的路径）。");
            Assert.AreEqual(0, rest.PocketProximityEvents, "T108：补判不是越喉事件 ⇒ 不得计入 PocketProximityEvents。");
            Assert.AreEqual(0, rest.PocketJawCrossingCount, "T108：补判不得计入擦袋角。");
            Assert.AreEqual(0, rest.CapturedPocketIndex(0), "T108：补判应指向该袋（索引 0）。");
            Assert.IsFalse(rest.IsBallInPlay(0), "T108：补判后球应离台。");
            Assert.AreEqual(rest.CapturedBallCount + rest.PocketJawCrossingCount,
                rest.PocketProximityEvents + rest.AtRestMouthDrops, "T108：补判场景恒等式不成立。");
            report += $"[补判: {rest.PocketProximityEvents}+{rest.AtRestMouthDrops}=={rest.CapturedBallCount}+{rest.PocketJawCrossingCount}] ";

            // (b) 负例：飞行中的球不得被补判。
            var flying = new PoolSimulator(Synthetic(pocket), Params());
            flying.LoadBalls(new[] { Ball(0f, 0.05f, 0.02f, 0f) });   // 移动中、仍属 s<0、仍在开口内
            Step(flying, PoolSimulator.RestStableFrames + 2);

            Assert.IsFalse(flying.TableAtRest, "T108：负例前提不成立 —— 移动中的球不应达成整盘静止。");
            Assert.AreEqual(0, flying.AtRestMouthDrops, "T108：未静止的球**不得**被静止支补判（飞行球误伤）。");
            Assert.IsTrue(flying.IsBallInPlay(0), "T108：未静止的球必须仍在场。");

            TestContext.WriteLine("T108: 恒等式四场景成立 ✓ " + report);
        }
    }
}
