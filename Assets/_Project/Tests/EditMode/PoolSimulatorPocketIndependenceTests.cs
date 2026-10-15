using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// Day-7 追补判据：**`P1` 三计数独立累加的外部守卫** + **灰盒键盘出杆的行为判据**（`T40`）。
    ///
    /// `P1` 背景（`D6-GATE-01` 登记）：spec `R-A` 要求三计数**各自独立累加**，但**无任何判据可守** ——
    /// 把 `PocketProximityEvents` 改成 `= Captured + Jaw`（注入 I5）后 **`T35` 仍绿**（因该等式**恒真**）。
    /// 本条提供**两条**补偿控制：
    ///  · `T39`（黑盒，独立路径）：**只看球末位 + 袋数据**、**不读任何计数**地重算越界与分类，再与三个计数比对。
    ///  · `T39b`（白盒，源码守卫）：断言"独立自增存在"且"**不得**由另两者推导"。
    ///
    /// ⚠ **边界（如实声明）**：`I5` 那种"由和推导"是**观测等价的重构**（数值不变）⇒ **黑盒判据原理上抓不到**；
    /// **唯一能打死它的是 `T39b` 的白盒守卫**（与 `D-3` 的源码守卫同族）。
    /// </summary>
    public class PoolSimulatorPocketIndependenceTests
    {
        private const string ConfigPath = "Assets/_Project/Data/TableConfig.asset";
        private const string SimulatorSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs";

        private static PhysicsParams Params()
        {
            return new PhysicsParams
            {
                cushionRestitution = 1f,
                cushionTangentialFriction = 1f,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = 0f, rollingDecelerationRoll = 0f,
            };
        }

        private static TableConfig Synthetic(params Pocket[] pockets)
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>();
            t.pockets = new List<Pocket>(pockets);
            return t;
        }

        /// <summary>
        /// 全盘球的**位置 + 速度**逐位快照（`R` 往返格式）。用途：断言"被拒绝的出杆**没动任何球**"。
        /// 纯读，无断言。
        /// </summary>
        private static string SnapshotBalls(PhysicsTestDrive drive)
        {
            var sb = new StringBuilder();
            System.Collections.Generic.IReadOnlyList<BallState> balls = drive.Simulator.Balls;
            for (int i = 0; i < balls.Count; i++)
            {
                sb.Append(i).Append(':')
                  .Append(balls[i].position.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(balls[i].position.y.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                  .Append(balls[i].velocity.x.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(balls[i].velocity.y.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }

            return sb.ToString();
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

        private static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;

        // ══════════════════ T39 独立枚举 vs 三计数（黑盒）══════════════════════

        /// <summary>
        /// **外部独立路径**：一次捕获 + 一次窗口越界 + 一颗不越界。
        /// 重算**只依赖球末位与袋数据**（不调用 `CapturedBallCount`/`PocketJawCrossingCount`/`PocketProximityEvents`），
        /// 再与三个计数**逐项比对**。
        ///
        /// **能测出**：**非对称**分类错（该捕获的被判窗口 / 反之，且两类数不等）、漏计、越界判定错（`s` 符号）。
        /// **盲带（两条，必须声明）**：
        ///  ① `I5`（`PocketProximityEvents = Captured + Jaw`）是**观测等价的重构** ⇒ 本判据**抓不到**（由 `T39b` 白盒守卫承担）；
        ///  ② ⚠ **对称分类错误抓不到**：本用例场景是"两类各 1"（1 捕获 + 1 窗口）⇒ 把两类**对调**后两侧计数仍是 `(1,1)`、
        ///     交叉比对**天然相等** ⇒ 本判据**仍绿**。**该错误由 `T27a` / `T27b` / `T31a` / `T31b` / `T31c` / `T36` / `T32a` / `T32b`
        ///     共 8 条兜住** —— 它们**钉死了"哪一类应是几"**，而本判据只做交叉比对。
        /// </summary>
        [Test]
        public void T39_IndependentCrossingEnumeration_MatchesCounters()
        {
            Pocket pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), 0.0307f);
            var sim = new PoolSimulator(Synthetic(pocket), Params());
            sim.LoadBalls(new[]
            {
                Ball(0.00f, 0.20f, 0f, -PoolConstants.MaxBallSpeed),   // ρ=0    ⇒ 捕获
                Ball(0.08f, 0.60f, 0f, -PoolConstants.MaxBallSpeed),   // ρ=0.08 ⇒ 窗口
                Ball(-0.30f, 0.10f, 0.2f, 0f),                          // 不越界
            });

            for (int i = 0; i < 60; i++)
            {
                sim.Step();
            }

            // ★ 独立重算：只看球末位 + 袋数据
            Vector2 tn = pocket.throatNormal;
            Vector2 td = new Vector2(-tn.y, tn.x);
            int indCross = 0;
            int indCaptured = 0;
            int indJaw = 0;
            for (int i = 0; i < sim.Balls.Count; i++)
            {
                Vector2 c = sim.Balls[i].position;
                float s = Dot(c - pocket.throatCenter, tn);
                if (s <= 0f)
                {
                    continue;
                }

                indCross++;
                float rho = Mathf.Abs(Dot(c - pocket.throatCenter, td));
                // ⚠ 分类边界必须与内核同规则 = `openingRadius`（袋角归属裁定 A）：
                //   `ρ ≤ openingRadius` ⇒ 入袋冻结；`ρ > openingRadius` ⇒ 越界冻结（不计入袋）。
                //   历史口径用 `captureRadius`；本场景（ρ=0.08 同时大于两者）两种规则给出同一结果，
                //   故本行是**口径保真**修正、不改变结论。
                if (rho <= pocket.openingRadius)
                {
                    indCaptured++;
                }
                else
                {
                    indJaw++;
                }
            }

            Assert.AreEqual(2, indCross, "T39：独立枚举应见 2 次越界（1 捕获 + 1 窗口）。");
            Assert.AreEqual(1, indCaptured, "T39：独立枚举应见 1 次捕获。");
            Assert.AreEqual(1, indJaw, "T39：独立枚举应见 1 次窗口。");

            Assert.AreEqual(indCaptured, sim.CapturedBallCount,
                "T39：独立枚举捕获数须等于 CapturedBallCount。");
            Assert.AreEqual(indJaw, sim.PocketJawCrossingCount,
                "T39：独立枚举窗口数须等于 PocketJawCrossingCount。");
            Assert.AreEqual(indCross, sim.PocketProximityEvents,
                "T39：独立枚举越界数须等于 PocketProximityEvents。");

            TestContext.WriteLine($"T39: 独立枚举 cross={indCross} cap={indCaptured} jaw={indJaw} "
                                  + $"== 内核 {sim.PocketProximityEvents}/{sim.CapturedBallCount}/{sim.PocketJawCrossingCount} ✓");
        }

        // ══════════════════ T39b 源码守卫（白盒，唯一能打死 I5 的）══════════════

        /// <summary>
        /// **白盒守卫**（与 `D-3` 的源码守卫同族）：`PoolSimulator` 热路径**代码**（剔除 `//` 注释）中
        ///  · ① 必须存在 `PocketProximityEvents++`（独立自增）；
        ///  · ② `ProximityEvents` **不得**被赋值为 `Captured`/`Jaw` 的表达式；
        ///  · ③ `CapturedBallCount` 与 `PocketJawCrossingCount` **不得**互相推导。
        ///
        /// **能测出**：`I5` 那种"把三计数改成由和推导"的重构（**黑盒抓不到**，只有本条能抓）。
        /// **盲带**：文本检查、非语义证明 —— 未列出的等价形态（自定义累加器、别名赋值等）**不会被发现**。
        /// </summary>
        [Test]
        public void T39b_CounterAccumulation_IsIndependent_NotDerivedFromSum()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string full = Path.Combine(root, SimulatorSourcePath);
            Assert.IsTrue(File.Exists(full), "T39b：找不到内核源码 " + full);

            var codeOnly = new StringBuilder();
            foreach (string line in File.ReadAllText(full).Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                codeOnly.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            string code = codeOnly.ToString();

            Assert.IsTrue(code.Contains("PocketProximityEvents++"),
                "T39b：越界分支必须有 PocketProximityEvents 的**独立自增**（R-A）。");

            Assert.IsFalse(Regex.IsMatch(code, @"PocketProximityEvents\s*=\s*[^;]*\b(CapturedBallCount|PocketJawCrossingCount)\b"),
                "T39b：PocketProximityEvents **不得**由 Captured + Jaw 推导（违反 R-A 独立累加）。");
            Assert.IsFalse(Regex.IsMatch(code, @"CapturedBallCount\s*=\s*[^;]*PocketJawCrossingCount"),
                "T39b：CapturedBallCount 不得由 PocketJawCrossingCount 推导。");
            Assert.IsFalse(Regex.IsMatch(code, @"PocketJawCrossingCount\s*=\s*[^;]*CapturedBallCount"),
                "T39b：PocketJawCrossingCount 不得由 CapturedBallCount 推导。");

            TestContext.WriteLine("T39b: 独立自增存在 且 三计数无「由和推导」 ✓");
        }

        // ══════════════════ T40 灰盒键盘出杆（行为判据）══════════════════════

        /// <summary>
        /// **键盘最小可玩的行为判据**：构造 `PhysicsTestDrive`（球堆模式）⇒ 设 `aimDegrees=0°`、`power=1.0`
        /// ⇒ <see cref="PhysicsTestDrive.Launch"/> ⇒ 断言初速 = `(maxLaunchSpeed, 0)`；再推进 1 s，
        /// 断言白球**确实位移**（非空转）且**确实发生碰撞**。
        /// 这条是"③ 灰盒可打"在没有人手点键时的**自动化替身**。
        ///
        /// **⚠ 关于门控（前置条件显式化，不是放宽断言）**：`Launch()` 现在**门控 `<see cref="PoolSimulator.TableAtRest"/>`**
        /// ⇒ 刚 `Initialize()` 时（`RestStableFrames` 尚未满足）出杆**必须被拒绝**。
        /// 本用例因此**先断言"未静止时被拒绝"**，再**推进到静止**后才出杆 ——
        /// 这两步把"什么时候才能出杆"从隐含约定变成**显式前置条件**，判据本身一字未放宽。
        /// </summary>
        [Test]
        public void T40_GreyboxDrive_KeyboardAimAndPower_ProducesShot()
        {
            TableConfig config = AssetDatabase.LoadAssetAtPath<TableConfig>(ConfigPath);
            Assert.IsNotNull(config, "T40：需要真实 TableConfig.asset。");

            var go = new GameObject("T40Drive");
            try
            {
                var drive = go.AddComponent<PhysicsTestDrive>();
                drive.tableConfig = config;
                drive.loadRackForBallBallDemo = true;
                drive.Initialize();
                Assert.IsNotNull(drive.Simulator, "T40：Initialize 后应已构造内核。");

                drive.aimDegrees = 0f;      // +x
                drive.power = 1f;           // 满力

                // ★ 前置条件①：整盘未静止 ⇒ 必须**拒绝出杆**（返回零向量），且不改状态。
                if (drive.Simulator.TableAtRest)
                {
                    Assert.Fail("T40：前提不成立 —— 刚 Initialize 时 TableAtRest 应为 false。");
                }

                string snapshotBefore = SnapshotBalls(drive);
                Vector2 rejected = drive.Launch();
                Assert.AreEqual(Vector2.zero, rejected,
                    "T40：整盘未静止（刚 Initialize、未满足 RestStableFrames）时 `Launch()` 必须被拒绝并返回零向量。");
                Assert.AreEqual(snapshotBefore, SnapshotBalls(drive),
                    "T40：被拒绝的出杆**不得**改任何球的位置/速度。");

                // ★ 前置条件②：推进到静止（`RestStableFrames` + 2 步余量）后才允许出杆。
                for (int i = 0; i < PoolSimulator.RestStableFrames + 2; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                Assert.IsTrue(drive.Simulator.TableAtRest,
                    "T40：前提不成立 —— 推进 RestStableFrames + 2 步后整盘应静止。");

                Vector2 v0 = drive.Launch();

                Assert.AreEqual(drive.maxLaunchSpeed, v0.x, 1e-3f, "T40：0° + 满力 ⇒ vx = maxLaunchSpeed。");
                Assert.AreEqual(0f, v0.y, 1e-3f, "T40：0° ⇒ vy = 0。");

                Vector2 p0 = drive.Simulator.Balls[0].position;
                for (int i = 0; i < 60; i++)
                {
                    drive.Advance(PoolSimulator.FixedDeltaTime);
                }

                float moved = (drive.Simulator.Balls[0].position - p0).magnitude;
                Assert.Greater(moved, 0.10f, "T40：白球应确实移动（非空转）。");
                Assert.Greater(drive.Simulator.TotalContactCount + drive.Simulator.BallBallContactCount, 0,
                    "T40：球堆模式应发生碰撞。");

                TestContext.WriteLine($"T40: v0=({v0.x:F3},{v0.y:F3}) 60 步位移={moved:F3} m, "
                                      + $"contacts={drive.Simulator.TotalContactCount}, bb={drive.Simulator.BallBallContactCount} ✓");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
