using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **内核新增只读诊断（首次球-球接触）的单点性与惰性** —— `T72b`。
    ///
    /// ── 背景；为什么需要它 ─────────────────────────────────────────────────
    /// 击球预测要在"白球与首个目标球**接触那一瞬**"取幽灵球。内核对外**只在 `Step()` 粒度可见**
    /// ⇒ 若靠"每步轮询 `BallBallContactCount`"，取到的是**该 Step 末端**的位置（擦碰时白球已带撞后速度
    /// 又走了 `1/120 s`，偏差可达 **37.5 mm &gt; 球半径**）。故内核新增**只读诊断**
    /// （<see cref="PoolSimulator.HasBallBallContact"/> / <see cref="PoolSimulator.FirstBallBallContact"/>）。
    ///
    /// ── 本判据钉四条硬约束（K-1…K-4）────────────────────────────────────────
    /// · **(a) K-1 单点**：`BallBallContactCount++` 恰 **2 处**、且**两处都在** `ResolveBallBallContact` 内；
    ///   首接触**记录语句恰一处**、也在该函数内；`EarliestBallBallToi(` 仍恰 2 处（定义 + 唯一调用）
    ///   ⇒ **没有第二个球-球检测/扫描点**。
    /// · **(b) K-2 惰性（逐位）**：按既有场景重跑，`T32b` / `T37` 的**接触数与逐球终态**与**实测基线逐位相同**
    ///   —— 这是"**只读**"这个说法的**唯一证据**（把声明变成实测）。
    ///   ⚠ 基线在第四轮随「袋角冲量」修复**重钉过**（`T32b 74→2`、`T37 6746→49`、T37 10 颗球的终态），
    ///   依据（因果对照 + 机制量测 + 终态位移）写在常量上方的注释里 —— 那是**物理行为真变了**，
    ///   而**不是**本条诊断量造成的。
    /// · **(c) K-3 清零语义**：`ResetDiagnostics()` 后 `HasBallBallContact == false`（与既有计数同处归零）。
    /// · **(d) K-4 `T55` 回归**：**由 `T55` 本身承担**（同一份源码、同一次运行）——
    ///   本判据**不重复**其断言（铁律 16：同一不变量不写两遍）。理由：`T55` 已逐字断言
    ///   `AverageVelocity` 调用点 == 3、`ApplyRollingDeceleration` == 1、每个调用点都传 `DecelerationAt(...)`、
    ///   且 `_params.rolling*` 的名字集合恰为三个字段；本诊断不新增其中任何一项（报告里给出 `T55` 的实测结果）。
    ///
    /// ⚠ **(a) 的盲带（如实声明）**：源码守卫是**文本级**的（改注释/换行风格可规避）⇒ 它只作**单点性的辅助证据**；
    /// 真正的判据是 **(b) 的逐位不变**与 `T75` 的 `|Δ| == 2r` 几何恒等式。
    /// ⚠ **不声称** (a) 能证明"实现者真的没另开检测点"。
    /// </summary>
    public class PoolSimulatorBallBallContactDiagnosticTests
    {
        private const string SimulatorSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";

        // ── 实测基线（口径：`Tests/EditMode/PoolSimulatorTerminationAndBreakTests.cs` 的 T32b / T37 场景与串行化口径）──
        //
        // ⚠ **第四轮重钉（「袋角冲量」修复：`ResolveSegmentContact` 补接近判据）** —— 三条依据：
        //   ① **因果对照**：把该判据去掉（注入）⇒ 本判据的**全部旧基线逐位复原**
        //      （`T32b total=74`、`T37 total=6746`、16 球终态逐位相同，实测）⇒ 这些变化**只**由那一条判据引起。
        //   ② **机制（实测）**：旧计数里 **6668/6746 = 98.8%** 是**已在分离**的接触（球嵌进库段后每子步被重复解算），
        //      并伴随 **6656 次深层重叠位置修正**；补上接近判据后变为 `total=49 / 跳过=1 / 深层重叠=0`
        //      ⇒ 旧基线量的是「球被按在库边反复解算」这一**缺陷**（用户实机「撞库角像没弹力、吸在两边」）。
        //   ③ **终态位移**：变化最大的球 1 由 `(-0.0757,-0.6240)`（贴在 −y 长库上）变为 `(0.0482,+0.6526)`
        //      （弹到台面另一侧）—— 与「不再被库边吸住」一致。
        private const int T32bBaselineTotalContactCount = 2;
        private const int T32bBaselineCapturedBallCount = 1;
        private const int T37BaselineBallBallContactCount = 37;
        private const int T37BaselineCapturedBallCount = 2;   // 袋角归属裁定 A：越喉且在喉口内 ⇒ 入袋（原 0）
        private const int T37BaselineTotalContactCount = 49;   // 第四轮重钉：原 6746 中 98.8% 是「已在分离」的伪接触（见上方注释）
        private const int T37BaselinePocketProximityEvents = 2;
        private const int T37BaselinePocketJawCrossingCount = 0;   // 同上：边界移到 openingRadius 后该场景无越界（原 2）
        private const int T37BaselineGuardTrips = 0;
        private const int T37BaselineEnergyGain = 0;
        private const int T37BaselineBallBallMaxIterationsObserved = 6;

        /// <summary>T37 的 16 颗球终态（`x;y`，逐位串；**第四轮重钉**后的实测基线，依据见上方注释）。
        /// 其中 10 颗变化（0/1/3/6/7/8/10/12/13/14），6 颗逐位不变；变化最大的球 1 由「贴在 −y 长库上」
        /// 变为「弹到台面另一侧」。</summary>
        private static readonly string[] T37BaselineFinalPositions =
        {
            "1.21900022;0.0102524208",
            "0.04816721;0.6526122",
            "-0.3474119;0.0283389147",
            "-0.277584672;-0.219319478",
            "-0.399346828;0.0574025027",
            "-0.399149865;0.0001637311",
            "0.6000458;0.0312398374",
            "-0.09315988;0.5284948",
            "-0.662210941;0.08714859",
            "-0.449126482;-0.0282310452",
            "-0.5803914;-0.3078415",
            "-1.256161;0.6710566",
            "-0.7874716;0.396159828",
            "-0.301134646;0.3414967",
            "-0.1881547;-0.487112969",
            "-1.255292;-0.6730765",
        };

        // ── 辅助 ──────────────────────────────────────────────────────────

        private static string RepoRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        /// <summary>读内核源码并**剔掉 `//` 注释**（含 `///`），供白盒文本断言用。</summary>
        private static string SimulatorCodeWithoutComments()
        {
            string full = Path.Combine(RepoRoot(), SimulatorSourcePath);
            Assert.IsTrue(File.Exists(full), "T72b：找不到内核源码 " + full);

            var codeOnly = new StringBuilder();
            foreach (string line in File.ReadAllText(full).Split('\n'))
            {
                int cut = line.IndexOf("//", System.StringComparison.Ordinal);
                codeOnly.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            return codeOnly.ToString();
        }

        /// <summary>取某个方法**函数体**的文本（按大括号配对；找不到返回 <c>null</c>）。</summary>
        private static string MethodBody(string code, string signatureFragment)
        {
            int sig = code.IndexOf(signatureFragment, System.StringComparison.Ordinal);
            if (sig < 0)
            {
                return null;
            }

            int open = code.IndexOf('{', sig + signatureFragment.Length);
            if (open < 0)
            {
                return null;
            }

            int depth = 0;
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
                        return code.Substring(open, i - open + 1);
                    }
                }
            }

            return null;
        }

        private static int CountOf(string haystack, string needle)
        {
            int n = 0;
            int at = 0;
            while (true)
            {
                int hit = haystack.IndexOf(needle, at, System.StringComparison.Ordinal);
                if (hit < 0)
                {
                    return n;
                }

                n++;
                at = hit + needle.Length;
            }
        }

        private static TableConfig Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(t, "T72b：需要 " + TablePath);
            return t;
        }

        /// <summary>
        /// **出厂口径的台呢参数**（读 `PhysicsTuning.asset`；与 `PoolSimulatorTerminationAndBreakTests.ShipParams` 同源）。
        /// ⚠ 这里**重述**场景胶水（约 25 行）是刻意的：K-2 要求"**再跑一次**并断言不变"，
        /// 而既有辅助函数是**另一个测试类的 private static** ⇒ 复用需要改动那份已入库的测试文件。
        /// 本判据只用**公开 API + 资产**构造，**不复制任何解算逻辑**（解算仍是被测内核本身）。
        /// </summary>
        private static PhysicsParams ShipParams(float cushionRestitution, float cushionTangentialFriction)
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "T72b：需要 " + TuningPath);

            PhysicsParams p = tuning.ToPhysicsParams();
            p.cushionRestitution = cushionRestitution;
            p.cushionTangentialFriction = cushionTangentialFriction;
            p.ballRestitution = 1f;
            p.ballTangentialFriction = 1f;
            return p;
        }

        private static BallState Ball(float x, float y, float vx, float vy)
        {
            return new BallState { position = new Vector2(x, y), velocity = new Vector2(vx, vy) };
        }

        private static void RunSteps(PoolSimulator sim, int steps)
        {
            for (int s = 0; s < steps; s++)
            {
                sim.Step();
            }
        }

        private static string RVec(Vector2 v)
        {
            return v.x.ToString("R", CultureInfo.InvariantCulture) + ";"
                 + v.y.ToString("R", CultureInfo.InvariantCulture);
        }

        // ══════════════════ T72b ══════════════════════════════════════════════

        /// <summary>
        /// `T72b` —— 内核新增只读诊断的**单点**（K-1）、**惰性**（K-2）、**清零语义**（K-3）。
        /// （K-4 由 `T55` 承担，见类型注释。）
        /// </summary>
        [Test]
        public void T72b_BallBallContactDiagnostic_IsSinglePointAndInert()
        {
            var failures = new List<string>();

            // ── (a) K-1 单点 ────────────────────────────────────────────────
            string code = SimulatorCodeWithoutComments();
            string body = MethodBody(code, "private void ResolveBallBallContact(int i, int k)");
            if (body == null)
            {
                failures.Add("(a) 找不到 `ResolveBallBallContact(int i, int k)` 的函数体（签名被改名？）。");
            }
            else
            {
                int totalIncrements = CountOf(code, "BallBallContactCount++;");
                int incrementsInside = CountOf(body, "BallBallContactCount++;");
                if (totalIncrements != 2 || incrementsInside != 2)
                {
                    failures.Add($"(a) `BallBallContactCount++` 应恰 **2** 处且**都在** `ResolveBallBallContact` 内，"
                                 + $"实测 全文件 {totalIncrements} 处 / 函数内 {incrementsInside} 处。");
                }

                int recordSites = CountOf(code, "_firstBallBallContact = new BallBallContactRecord");
                int recordSitesInside = CountOf(body, "_firstBallBallContact = new BallBallContactRecord");
                if (recordSites != 1 || recordSitesInside != 1)
                {
                    failures.Add($"(a) 首接触**记录语句**应恰 **1** 处且在该函数内，实测 全文件 {recordSites} 处 / 函数内 {recordSitesInside} 处。");
                }

                int guardSites = CountOf(code, "_hasFirstBallBallContact = true;");
                int guardSitesInside = CountOf(body, "_hasFirstBallBallContact = true;");
                if (guardSites != 1 || guardSitesInside != 1)
                {
                    failures.Add($"(a) \"只记第一次\"守卫应恰 **1** 处且在该函数内，实测 全文件 {guardSites} 处 / 函数内 {guardSitesInside} 处。");
                }
            }

            // 没有第二个球-球检测/扫描点：球-球 TOI 扫描仍是"定义 + 唯一调用"两处。
            int toiSites = CountOf(code, "EarliestBallBallToi(");
            if (toiSites != 2)
            {
                failures.Add($"(a) `EarliestBallBallToi(` 应恰 **2** 处（定义 + 唯一调用 ⇒ 没有第二个球-球扫描点），实测 {toiSites} 处。");
            }

            // ── (b) K-2 惰性：T32b / T37 既有场景逐位不变 ────────────────────
            TableConfig table = Table();

            // T32b：侧袋直入 + 角袋擦角 + 居中滚动，120 步。
            Pocket side = table.GetPocket(1);
            Pocket corner = table.GetPocket(0);
            Vector2 sideStart = side.throatCenter - 0.35f * side.throatNormal;
            Vector2 cornerTn = corner.throatNormal;
            Vector2 cornerTd = new Vector2(-cornerTn.y, cornerTn.x);
            // 与 T32b 同步（ρ0 0.045 → 0.060，理由见 T28b 的 doc）。
            Vector2 cornerStart = corner.throatCenter - 0.12f * cornerTn + 0.060f * cornerTd;

            BallState[] t32bInitial =
            {
                new BallState { position = sideStart, velocity = 2f * side.throatNormal },
                new BallState { position = cornerStart, velocity = 2f * cornerTn },
                Ball(0f, 0.45f, 0.15f, 0f),
            };

            var t32b = new PoolSimulator(table, ShipParams(0.9f, 0.8f));
            t32b.LoadBalls(t32bInitial);
            RunSteps(t32b, 120);

            if (t32b.TotalContactCount != T32bBaselineTotalContactCount)
            {
                failures.Add($"(b) T32b `TotalContactCount` 基线 {T32bBaselineTotalContactCount} → 实测 {t32b.TotalContactCount}"
                             + "（基线不符：先按类注释的三条依据复核是不是有意的物理改动，"
                             + "确认后重钉并写明原因；**不得**为了本判据变绿而放宽断言）。");
            }

            if (t32b.CapturedBallCount != T32bBaselineCapturedBallCount)
            {
                failures.Add($"(b) T32b `CapturedBallCount` 基线 {T32bBaselineCapturedBallCount} → 实测 {t32b.CapturedBallCount}。");
            }

            // T37：15 球满堆真开球，1200 步。
            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 15);
            var t37Initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                t37Initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            t37Initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var t37 = new PoolSimulator(table, ShipParams(0.9f, 0.8f));
            t37.LoadBalls(t37Initial);
            RunSteps(t37, 1200);

            if (t37.BallBallContactCount != T37BaselineBallBallContactCount)
            {
                failures.Add($"(b) T37 `BallBallContactCount` 基线 {T37BaselineBallBallContactCount} → 实测 {t37.BallBallContactCount}。");
            }

            if (t37.CapturedBallCount != T37BaselineCapturedBallCount)
            {
                failures.Add($"(b) T37 `CapturedBallCount` 基线 {T37BaselineCapturedBallCount} → 实测 {t37.CapturedBallCount}。");
            }

            if (t37.TotalContactCount != T37BaselineTotalContactCount)
            {
                failures.Add($"(b) T37 `TotalContactCount` 基线 {T37BaselineTotalContactCount} → 实测 {t37.TotalContactCount}。");
            }

            if (t37.PocketProximityEvents != T37BaselinePocketProximityEvents
                || t37.PocketJawCrossingCount != T37BaselinePocketJawCrossingCount)
            {
                failures.Add($"(b) T37 越喉/擦角计数 基线 {T37BaselinePocketProximityEvents}/{T37BaselinePocketJawCrossingCount}"
                             + $" → 实测 {t37.PocketProximityEvents}/{t37.PocketJawCrossingCount}。");
            }

            if (t37.ToiIterationGuardTrips != T37BaselineGuardTrips || t37.EnergyGainViolations != T37BaselineEnergyGain
                || t37.BallBallMaxIterationsObserved != T37BaselineBallBallMaxIterationsObserved)
            {
                failures.Add($"(b) T37 诊断量 基线 guard={T37BaselineGuardTrips}/energy={T37BaselineEnergyGain}/maxIter={T37BaselineBallBallMaxIterationsObserved}"
                             + $" → 实测 {t37.ToiIterationGuardTrips}/{t37.EnergyGainViolations}/{t37.BallBallMaxIterationsObserved}。");
            }

            if (t37.Balls.Count != T37BaselineFinalPositions.Length)
            {
                failures.Add($"(b) T37 球数 基线 {T37BaselineFinalPositions.Length} → 实测 {t37.Balls.Count}。");
            }
            else
            {
                for (int i = 0; i < t37.Balls.Count; i++)
                {
                    string now = RVec(t37.Balls[i].position);
                    if (now != T37BaselineFinalPositions[i])
                    {
                        failures.Add($"(b) T37 球 {i} 终态 基线 {T37BaselineFinalPositions[i]} → 实测 {now}");
                    }
                }
            }

            // ── (c) K-3 清零语义 ────────────────────────────────────────────
            Assert.IsTrue(t37.HasBallBallContact,
                "(c) 前提不成立 —— 15 球开球应在 1200 步内发生球-球接触（否则清零语义不可判定）。");

            // ⚠ 先把要比对的数值取出来 —— 下面的 `ResetDiagnostics()` 会把它们清零，
            //   若在那之后再读，日志会把"接触数"印成 0（**误导**）。
            int logT32bTotal = t32b.TotalContactCount;
            int logT37Bb = t37.BallBallContactCount;
            int logT37Total = t37.TotalContactCount;

            t37.ResetDiagnostics();
            if (t37.HasBallBallContact)
            {
                failures.Add("(c) `ResetDiagnostics()` 后 `HasBallBallContact` 仍为 true（诊断未纳入每杆计数语义）。");
            }

            if (t37.BallBallContactCount != 0)
            {
                failures.Add($"(c) `ResetDiagnostics()` 后 `BallBallContactCount` 应为 0，实测 {t37.BallBallContactCount}。");
            }

            Assert.IsEmpty(failures,
                "T72b 失败（K-1 单点 / K-2 惰性 / K-3 清零）：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T72b: (a) 记录点单点 + 球-球 TOI 扫描仍 {toiSites} 处；"
                                  + $"(b) T32b total={logT32bTotal}(基 {T32bBaselineTotalContactCount})、"
                                  + $"T37 bb={logT37Bb}(基 {T37BaselineBallBallContactCount})/total={logT37Total}(基 {T37BaselineTotalContactCount})、"
                                  + $"16 球终态逐位相同；(c) ResetDiagnostics 后 HasBallBallContact=false ✓");
        }
    }
}
