using System;
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
    /// **台呢滚阻两段模型**的判据族（`T54`–`T59`）。
    ///
    /// 模型：<c>a(v) = a_roll + (a_slide − a_roll)·clamp01(v / v_transition)</c>，
    /// 由 <see cref="PhysicsParams.rollingDecelerationSlide"/> /
    /// <see cref="PhysicsParams.rollingDecelerationRoll"/> /
    /// <see cref="PhysicsParams.rollingTransitionSpeed"/> 三个字段驱动，
    /// 内核侧唯一实现点是 <c>PoolSimulator.DecelerationAt</c>。
    ///
    /// ⚠ **本内核没有角速度状态 ω**：`a(v)` 是"按速度阈值分两段"的近似，
    /// **不是**在积分转动/滑动摩擦。故 `v_roll = (5/7)v0`、`t_roll`、`d_slide` 等解析值
    /// **不得**被写成断言（它们只解释"为什么高速段该扣得多"）。
    /// 本文件只断言**形状 / 单调性 / 退化等价 / 不造能 / 确定性 / 门控**。
    ///
    /// ⚠ 本工程 NUnit（`com.unity.ext.nunit` 3.5 分支）**无 `Assert.Multiple`**（实测 `CS0117`）
    /// ⇒ 需要多条一起验的地方一律**手工汇总失败项**后一次报告。
    /// </summary>
    public class PoolSimulatorTwoStageRollingTests
    {
        private const string SimulatorSourcePath = "Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs";
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";

        // ── 公共辅助 ──────────────────────────────────────────────────────

        /// <summary>空台（无段、无袋）：把滚阻从碰撞耗散里**隔离**出来。</summary>
        private static TableConfig EmptyTable()
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>();
            t.pockets = new List<Pocket>();
            return t;
        }

        /// <summary>两段档（`F2c`：`new PhysicsParams` 的对象初始化器字段齐全）。</summary>
        private static PhysicsParams Rolling(float aSlide, float aRoll, float vTransition)
        {
            return new PhysicsParams
            {
                cushionRestitution = 1f,
                cushionTangentialFriction = 1f,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = aSlide,
                rollingDecelerationRoll = aRoll,
                rollingTransitionSpeed = vTransition,
            };
        }

        private static PoolSimulator SimOf(PhysicsParams p, TableConfig table, Vector2 start, Vector2 v)
        {
            var sim = new PoolSimulator(table, p);
            sim.LoadBalls(new[] { new BallState { position = start, velocity = v } });
            return sim;
        }

        private static PoolSimulator EmptySim(PhysicsParams p, Vector2 start, Vector2 v)
            => SimOf(p, EmptyTable(), start, v);

        private static float Speed(Vector2 v) => PoolMath.Sqrt(v.x * v.x + v.y * v.y);

        /// <summary>逐位可比的串（`R` = 往返格式）。</summary>
        private static string RVec(Vector2 v)
            => v.x.ToString("R", CultureInfo.InvariantCulture) + ";"
             + v.y.ToString("R", CultureInfo.InvariantCulture);

        private static string RState(PoolSimulator sim, int i)
            => RVec(sim.Balls[i].position) + "|" + RVec(sim.Balls[i].velocity);

        /// <summary>推进到"整桌静止"或耗尽步数上限；返回实际步数。</summary>
        private static int RunToRest(PoolSimulator sim, int cap)
        {
            int steps = 0;
            while (steps < cap && !sim.TableAtRest)
            {
                sim.Step();
                steps++;
            }

            return steps;
        }

        // ══════════════════ T54 退化等价（硬要求）══════════════════════════════

        /// <summary>
        /// **历史恒定减速度模型**的独立重述。
        ///
        /// ⚠ **为什么需要它**：旧单值滚阻字段已按 `Q-14` (a) **全删** ⇒ 仓库里已**不存在**
        /// "恒定模型"这条代码路径，无法再"双跑对比"。故在此**独立重述**其算术
        /// （`v_avg = v − ½·a·Δt·v̂`，与 `v` 反向取零 ⇒ `p += v_avg·Δt`；子步末 `keep = (speed − a·Δt)/speed`，
        /// `speed ≤ a·Δt` ⇒ 置零），**不调用 `DecelerationAt`、不读任何 `_params` 字段**。
        /// 期望值来源 = **甲**（同一算术式的等价性），判定式 = 逐位（`ToString("R")`）比较。
        /// </summary>
        private static void HistoricalConstantSubStep(ref Vector2 p, ref Vector2 v, float a, float dtSub)
        {
            Vector2 vAvg;
            if (a <= 0f)
            {
                vAvg = v;
            }
            else
            {
                Vector2 vHat = Normalize(v);
                Vector2 candidate = v - (0.5f * a * dtSub) * vHat;
                float dot = candidate.x * v.x + candidate.y * v.y;
                vAvg = dot < 0f ? Vector2.zero : candidate;
            }

            p += vAvg * dtSub;

            float speed = Speed(v);
            if (speed <= 0f)
            {
                return;
            }

            float dv = a * dtSub;
            if (speed <= dv)
            {
                v = Vector2.zero;
                return;
            }

            float keep = (speed - dv) / speed;
            v = v * keep;
        }

        private static Vector2 Normalize(Vector2 v)
        {
            float len2 = v.x * v.x + v.y * v.y;
            if (len2 <= 1e-24f)
            {
                return Vector2.zero;
            }

            float inv = 1f / PoolMath.Sqrt(len2);
            return new Vector2(v.x * inv, v.y * inv);
        }

        /// <summary>
        /// **`a_slide == a_roll` ⇒ 与历史恒定模型逐位一致**。
        ///
        /// 做法：空台 + 单球（`v0 = MaxBallSpeed`，朝 `+x`），`N` 步；
        /// ① 退化档（`a_slide == a_roll == 0.9`，`v_transition` 取两个不同的正值与 0 共三档）
        ///    跑 `N` 步；② 用 `HistoricalConstantSubStep`（钉字面量 `0.9`，`甲`）算同一轨迹；
        /// ③ **逐步逐位**比较位置 / 速度（`R` 往返串）。
        ///
        /// **判别力**：`DecelerationAt` 里引入任何非等价算术（查表 / 缩放 / 硬切换用了别的常量）
        /// ⇒ 退化档不再逐位一致 ⇒ FAIL。
        /// **盲带**：只覆盖**退化档**；**漏改某个调用点**在退化档下不可见（三处同值）⇒ 由 `T55` 承担。
        /// **注入负对照（实测）**：把 `DecelerationAt` 的返回改成 `a_roll * 0.999f` ⇒ 本判据 FAIL。
        /// </summary>
        [Test]
        public void T54_DegenerateRolling_MatchesHistoricalConstantModel_BitIdentical()
        {
            const float aConst = 0.9f;          // = 历史出厂值（钉字面量，**不读资产**）
            const int steps = 30;               // 0.25 s：速度仍 ≈ 7.78 m/s，远高于任何静止阈值
            var p0 = new Vector2(0.123f, -0.456f);
            var v0 = new Vector2(PoolConstants.MaxBallSpeed, 0f);

            var failures = new List<string>();

            // 三档 v_transition：0（不分区）/ 1（落在 v0 之下）/ 7（落在 v0 之下更远处）—— 退化档下必须同值
            float[] transitions = { 0f, 1f, 7f };
            foreach (float vt in transitions)
            {
                PoolSimulator sim = EmptySim(Rolling(aConst, aConst, vt), p0, v0);

                Vector2 rp = p0;
                Vector2 rv = v0;
                float dtSub = sim.SubStepDeltaTime;
                int subSteps = sim.SubSteps;

                for (int s = 0; s < steps; s++)
                {
                    for (int k = 0; k < subSteps; k++)
                    {
                        HistoricalConstantSubStep(ref rp, ref rv, aConst, dtSub);
                    }

                    sim.Step();

                    if (RVec(sim.Balls[0].position) != RVec(rp) || RVec(sim.Balls[0].velocity) != RVec(rv))
                    {
                        failures.Add($"v_transition={vt}：第 {s + 1} 步起逐位不一致 —— "
                                     + $"内核 pos={RVec(sim.Balls[0].position)} vel={RVec(sim.Balls[0].velocity)}；"
                                     + $"历史模型 pos={RVec(rp)} vel={RVec(rv)}。");
                        break;
                    }
                }
            }

            Assert.IsEmpty(failures,
                "T54 退化逐位等价失败（`a_slide == a_roll` 必须与历史恒定减速度模型**逐位**一致）：\n"
                + string.Join("\n", failures));
            TestContext.WriteLine($"T54: 退化档（a={aConst} × v_transition∈{{0,1,7}}）× {steps} 步 ⇒ 与历史恒定模型逐位一致 ✓");
        }

        // ══════════════════ T55 单点实现守卫（白盒）════════════════════════════

        /// <summary>
        /// **`DecelerationAt` 是唯一的 `a(v)` 实现点**，且**没有任何调用点绕过它**。
        ///
        /// 断言（扫 <c>PoolSimulator.cs</c>，剔 `//` 注释后）：
        /// ① 存在 `DecelerationAt(`；
        /// ② ★ **反向穷举**：`_params.rolling*` 的读取点**只能**是本模型的三个字段
        ///    （`rollingDecelerationSlide` / `rollingDecelerationRoll` / `rollingTransitionSpeed`）
        ///    —— 任何别的名字都是"旧单值字段的残留"或"新造的重复存储"（铁律 16：同一量不得有两种写法）；
        /// ③ ★ **每个 `AverageVelocity(…)` / `ApplyRollingDeceleration(…)` 调用点都必须传 `DecelerationAt(…)`**
        ///    —— 这才是"漏改调用点"的**可判定**形式；
        /// ④ 三个字段必须真的从 `_params` 读出来（否则实现根本没接线）。
        ///
        /// ⚠ **② 为什么是"穷举前缀"而不是"grep 旧字段名"（两点都要说清）**：
        ///  · **语义上更强**：旧字段名只是"别的名字"的一个特例；穷举 `_params.rolling*` 能抓住
        ///    **任何**新造的重复存储，而不只是那一个名字；
        ///  · **机械上必需**：本题若要正则在源码里**字面点名**旧字段，那条正则自身就会让
        ///    "旧字段名在 `Assets/` 下零残留"的 `grep`（词边界匹配）**多出一条命中** ⇒ 与零残留自相矛盾。
        ///    故本判据**不在源码里出现旧字段名**（连注释也不写）。
        ///
        /// ⚠ **与 spec 字面注入的差异（诚实记录）**：spec 的注入是"把 TOI 后推进那处改回读旧单值字段"，
        /// 但 `Q-14` (a) 已把该字段**删掉** ⇒ 那种写法**无法编译**、注入不可执行。
        /// ⇒ 本判据用 ③ 取代它：「把调用点换成只传 `a_slide` 常量」这类**可编译**的漏改会被 ③ 抓住（已实测）。
        ///
        /// **盲带**：文本检查、**非语义证明**（别名 / 局部变量转发可绕过）。
        /// </summary>
        [Test]
        public void T55_DecelerationAt_IsTheOnlyReadPoint_AndNoCallSiteBypassesIt()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string full = Path.Combine(root, SimulatorSourcePath);
            Assert.IsTrue(File.Exists(full), "T55：找不到内核源码 " + full);

            var codeOnly = new StringBuilder();
            foreach (string line in File.ReadAllText(full).Split('\n'))
            {
                int cut = line.IndexOf("//", StringComparison.Ordinal);
                codeOnly.Append(cut >= 0 ? line.Substring(0, cut) : line).Append('\n');
            }

            string code = codeOnly.ToString();
            var failures = new List<string>();

            if (!Regex.IsMatch(code, @"\bDecelerationAt\s*\("))
            {
                failures.Add("① 未找到 `DecelerationAt(` —— 两段模型的 `a(v)` 实现点缺失。");
            }

            // ② 反向穷举：`_params.rolling*` 只允许是本模型的三个字段
            string[] allowed = { "rollingDecelerationSlide", "rollingDecelerationRoll", "rollingTransitionSpeed" };
            var seen = new List<string>();
            foreach (Match m in Regex.Matches(code, @"_params\.rolling[A-Za-z]*"))
            {
                string name = m.Value.Substring("_params.".Length);
                if (Array.IndexOf(allowed, name) < 0)
                {
                    failures.Add($"② `_params.{name}` 不是两段模型的三字段之一 ⇒ 旧单值字段的残留 / 新造的重复存储。");
                }

                if (!seen.Contains(name))
                {
                    seen.Add(name);
                }
            }

            foreach (string name in allowed)
            {
                if (!seen.Contains(name))
                {
                    failures.Add($"④ `{name}` 未从 `_params` 读取 ⇒ 参数没接进内核。");
                }
            }

            // ③ 调用点单点性：call 行（排除声明行）必须传 `DecelerationAt(`
            int avgCalls = 0;
            int rollCalls = 0;
            foreach (string line in code.Split('\n'))
            {
                bool isDeclaration = Regex.IsMatch(line, @"\bstatic\b.*\b(AverageVelocity|ApplyRollingDeceleration)\s*\(");
                if (Regex.IsMatch(line, @"\bAverageVelocity\s*\(") && !isDeclaration)
                {
                    avgCalls++;
                    if (!Regex.IsMatch(line, @"\bDecelerationAt\s*\("))
                    {
                        failures.Add("③ `AverageVelocity` 有调用点**未传** `DecelerationAt(...)`（漏改 ⇒ 非退化档行为不一致，退化档看不见）。");
                    }
                }

                if (Regex.IsMatch(line, @"\bApplyRollingDeceleration\s*\(") && !isDeclaration)
                {
                    rollCalls++;
                    if (!Regex.IsMatch(line, @"\bDecelerationAt\s*\("))
                    {
                        failures.Add("③ `ApplyRollingDeceleration` 的调用点**未传** `DecelerationAt(...)`。");
                    }
                }
            }

            if (avgCalls != 2)
            {
                failures.Add($"③ `AverageVelocity` 调用点应为 **2** 处（子步内位置推进 / TOI 后剩余时间推进），实测 {avgCalls}。");
            }

            if (rollCalls != 1)
            {
                failures.Add($"③ `ApplyRollingDeceleration` 调用点应为 **1** 处（子步末应用），实测 {rollCalls}。");
            }

            Assert.IsEmpty(failures, "T55 单点实现守卫失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T55: `DecelerationAt(` 单点存在；`_params.rolling*` 读取点 = {{{string.Join(", ", seen)}}}（恰为三字段）；"
                                  + $"AverageVelocity 调用 {avgCalls} 处 / ApplyRollingDeceleration 调用 {rollCalls} 处，全部走 `DecelerationAt` ✓");
        }

        // ══════════════════ T56a 两段形状（快-慢）══════════════════════════════

        /// <summary>
        /// **两段形状**：高速段（`v &gt; v_transition`）的平均减速度显著大于低速段（`v ≤ v_transition`）。
        ///
        /// 做法：空台 + 单球 `v0 = MaxBallSpeed`，非退化参数（`2.0 / 0.1 / 2.0`），逐帧记录速度；
        /// `d_hi = (v0 − v_t) / t(v_t)`；`d_lo = v_t / (t_rest − t(v_t))`；断言 `d_hi &gt; 2·d_lo`。
        /// 期望值来源 = **甲**（模型定义式的直接推论：`v &gt; v_t ⇒ a = a_slide = 20·a_roll`）。
        ///
        /// **盲带**：只证"两段有区别"，**不证**"像真球"（那要扫描 + 用户判）；`v_transition` 的绝对值不在其中。
        /// **注入负对照（实测）**：① 把 `DecelerationAt` 的 `clamp01(v/v_transition)` 换成常量 `1f`
        /// （⇒ 全程 `a_slide`）⇒ 本判据 FAIL；② 把两段系数**对调**
        /// （`a = a_slide + (a_roll − a_slide)·t` ⇒ 高速段拿 `a_roll`）⇒ 本判据 FAIL
        /// （实测 `d_hi` 由 2.0 掉到 0.1、`d_lo` 升到 0.63 ⇒ 比值反转）。
        /// </summary>
        [Test]
        public void T56a_TwoStage_HighSpeedSegmentDeceleratesMuchFaster()
        {
            const float aSlide = 2.0f;
            const float aRoll = 0.1f;
            const float vTransition = 2.0f;
            const float v0 = PoolConstants.MaxBallSpeed;
            const int cap = 8000;

            PoolSimulator sim = EmptySim(Rolling(aSlide, aRoll, vTransition), Vector2.zero, new Vector2(v0, 0f));

            int stepCross = -1;
            int stepRest = -1;
            for (int s = 1; s <= cap; s++)
            {
                sim.Step();
                float sp = Speed(sim.Balls[0].velocity);

                if (stepCross < 0 && sp <= vTransition)
                {
                    stepCross = s;
                }

                if (sp <= 0f)
                {
                    stepRest = s;
                    break;
                }
            }

            Assert.Greater(stepCross, 0, "T56a：前提失败 —— 观测窗口内速度未降到 v_transition 以下（用例应保证跨过阈值）。");
            Assert.Greater(stepRest, stepCross, "T56a：前提失败 —— 观测窗口内球未静止（低速段耗时无法测量）。");

            float tHi = stepCross / 120f;
            float tLo = (stepRest - stepCross) / 120f;
            float dHi = (v0 - vTransition) / tHi;
            float dLo = vTransition / tLo;

            TestContext.WriteLine($"T56a: t(v_t)={tHi:F4} s ⇒ d_hi={dHi:F4} m/s²；静止 t={stepRest / 120f:F4} s ⇒ d_lo={dLo:F4} m/s²；"
                                  + $"比值={dHi / dLo:F3}（门槛 > 2）");

            Assert.Greater(dHi, 2f * dLo,
                $"T56a：高速段平均减速度 d_hi={dHi:F4} 未显著大于低速段 d_lo={dLo:F4}（门槛 d_hi > 2·d_lo）"
                + "⇒ 两段形状不成立（退化成恒定 / 权重写反 / clamp 常量）。");
        }

        // ══════════════════ T56b 单调性 ═══════════════════════════════════════

        /// <summary>
        /// **单调性与"哪一段拿大值"**。
        ///
        /// ① **单调性**：`a_roll = 0.1`、`v_t = 2.0` 固定；`a_slide ∈ {1.0, 2.0, 4.0}` ⇒ 断言静止耗时**严格递减**。
        /// ② **轴敏感性**（★ 本次实测后补强，见下）：**同一个大小值放高段 vs 放低段必须不同** ——
        ///    断言 `t(a_slide=4, a_roll=0.1) &lt; t(a_slide=0.1, a_roll=4)`（大值给高速段 ⇒ 更早停）。
        ///
        /// 期望值来源 = **甲**（"高速段扣得更多 ⇒ 更早停" + "大值给高段比给低段停得早"）。
        ///
        /// ⚠ **为什么必须加 ②（诚实记录一次实测教训）**：只测 ① 时，把两段系数**对调**
        /// （`a = a_slide + (a_roll − a_slide)·t`）**不会**被抓住 —— 实测该注入下
        /// `a_slide ∈ {1,2,4}` 的静止耗时仍严格递减（更大的 `a_slide` 仍经由**低速段**更快停）。
        /// ⇒ ① 对"两段取值对调"是**免疫**的；② 才是这条注入的可判定形式（② 在正确模型下余量很大：
        /// 实测约 3.4 s vs 62 s）。
        /// **盲带**：不覆盖"是否像真球"，也不覆盖用户要的"**滚得更久**"（那是**绝对值**问题，靠扫描选值）。
        /// </summary>
        [Test]
        public void T56b_Monotonic_LargerSlideDeceleration_StopsSooner()
        {
            const float aRoll = 0.1f;
            const float vTransition = 2.0f;
            const int cap = 20000;

            float[] slides = { 1.0f, 2.0f, 4.0f };
            var times = new float[slides.Length];
            var failures = new List<string>();

            for (int i = 0; i < slides.Length; i++)
            {
                bool rested;
                times[i] = TimeToRest(slides[i], aRoll, vTransition, cap, out rested);
                if (!rested)
                {
                    failures.Add($"前提：a_slide={slides[i]} 在 {cap} 步内未静止（用例前提失败）。");
                }
            }

            for (int i = 1; i < slides.Length; i++)
            {
                if (!(times[i] < times[i - 1]))
                {
                    failures.Add($"① a_slide {slides[i - 1]} → {slides[i]}：静止耗时 {times[i - 1]:F4} s → {times[i]:F4} s **未严格递减**。");
                }
            }

            // ② 轴敏感性：同一个"大值"给高速段（slide=4 / roll=0.1）必须比给低速段（slide=0.1 / roll=4）**更早停**
            bool restedA;
            bool restedB;
            float tBigHi = TimeToRest(4.0f, 0.1f, vTransition, cap, out restedA);
            float tBigLo = TimeToRest(0.1f, 4.0f, vTransition, cap, out restedB);
            if (!restedA || !restedB)
            {
                failures.Add($"前提：② 的两个用例未在 {cap} 步内静止（restedA={restedA}, restedB={restedB}）。");
            }
            else if (!(tBigHi < tBigLo))
            {
                failures.Add($"② \"大值给高速段\"（4/0.1 ⇒ {tBigHi:F4} s）未早于\"大值给低速段\"（0.1/4 ⇒ {tBigLo:F4} s）"
                             + " ⇒ 两段取值被**对调**（只测单调性① 对这类错误免疫）。");
            }

            Assert.IsEmpty(failures,
                "T56b 单调性 / 轴敏感性失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T56b: a_slide∈{{1,2,4}} ⇒ 静止耗时 {times[0]:F4} / {times[1]:F4} / {times[2]:F4} s，严格递减 ✓；"
                                  + $"轴敏感性 4/0.1={tBigHi:F4} s < 0.1/4={tBigLo:F4} s ✓");
        }

        /// <summary>空台单球（满力、朝 `+x`）从 `(0,0)` 起跑到宏观静止的耗时（秒）。</summary>
        private static float TimeToRest(float aSlide, float aRoll, float vTransition, int cap, out bool rested)
        {
            PoolSimulator sim = EmptySim(Rolling(aSlide, aRoll, vTransition), Vector2.zero,
                new Vector2(PoolConstants.MaxBallSpeed, 0f));
            int steps = RunToRest(sim, cap);
            rested = sim.TableAtRest;
            return steps / 120f;
        }

        // ══════════════════ T57 不造能 ════════════════════════════════════════

        /// <summary>
        /// **不造能**：`a(v) ≥ 0` 恒成立 ⇒ 台呢滚阻**永不加速**球。
        ///
        /// 做法（**可观测形式**：`a(v)` 是私有的，故用"速度是否反增"作为等价观测）：
        /// ① **参数域（含负值）**：`a_slide ∈ {−1, −0.5, 0, 0.5, 3} × a_roll ∈ {−1, −0.5, 0, 0.1, 3}
        ///    × v_transition ∈ {−1, 0, 1, 2}`，低速起跑（`0.5 m/s`，落在低速段），逐帧断言速度**不增**；
        /// ② **实跑**：非退化档满力一杆，断言速度单调不增且 `EnergyGainViolations == 0`。
        /// 期望值来源 = **甲**（`a ≥ 0` 是最小能量约束）。
        ///
        /// **判别力**：去掉 `Mathf.Max(0f, a)` 且 `a_roll` 为负 ⇒ `ApplyRollingDeceleration` 的
        /// `keep = (speed − a·Δt)/speed &gt; 1` ⇒ 速度**反增** ⇒ FAIL。
        /// **盲带**：只挡"造能"，不挡"耗散过大"（那是参数选择问题）。
        /// **注入负对照（实测）**：去掉 `Mathf.Max(0f, a)` ⇒ 本判据 ① FAIL。
        /// </summary>
        [Test]
        public void T57_NoEnergyGain_NeverAccelerates()
        {
            float[] slides = { -1f, -0.5f, 0f, 0.5f, 3f };
            float[] rolls = { -1f, -0.5f, 0f, 0.1f, 3f };
            float[] transitions = { -1f, 0f, 1f, 2f };
            const float v0 = 0.5f;
            const int steps = 240;

            var failures = new List<string>();
            int cells = 0;

            foreach (float aSlide in slides)
            {
                foreach (float aRoll in rolls)
                {
                    foreach (float vt in transitions)
                    {
                        cells++;
                        PoolSimulator sim = EmptySim(Rolling(aSlide, aRoll, vt), Vector2.zero, new Vector2(v0, 0f));
                        float prev = v0;
                        for (int s = 0; s < steps; s++)
                        {
                            sim.Step();
                            float sp = Speed(sim.Balls[0].velocity);
                            if (sp > prev + 1e-9f)
                            {
                                failures.Add($"① a_slide={aSlide}, a_roll={aRoll}, v_t={vt}：第 {s + 1} 步速度由 {prev:R} 反增到 {sp:R} ⇒ **造能**。");
                                break;
                            }

                            prev = sp;
                        }
                    }
                }
            }

            // ② 非退化档实跑：满力，速度单调不增 + 诊断计数为 0
            PoolSimulator full = EmptySim(Rolling(2.0f, 0.1f, 2.0f), Vector2.zero,
                new Vector2(PoolConstants.MaxBallSpeed, 0f));
            float prevFull = PoolConstants.MaxBallSpeed;
            for (int s = 0; s < 1200; s++)
            {
                full.Step();
                float sp = Speed(full.Balls[0].velocity);
                if (sp > prevFull + 1e-9f)
                {
                    failures.Add($"② 非退化档：第 {s + 1} 步速度由 {prevFull:R} 反增到 {sp:R} ⇒ **造能**。");
                    break;
                }

                prevFull = sp;
            }

            if (full.EnergyGainViolations != 0)
            {
                failures.Add($"② EnergyGainViolations = {full.EnergyGainViolations}（应为 0）。");
            }

            Assert.IsEmpty(failures,
                "T57 不造能失败（台呢滚阻必须 `a(v) ≥ 0`，永不加能）：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T57: 参数域 {cells} 格（含 a_slide/a_roll/v_transition 负值）速度均不增；"
                                  + $"非退化档满力 1200 步不增、EnergyGainViolations={full.EnergyGainViolations} ✓");
        }

        // ══════════════════ T58 确定性（非退化档）══════════════════════════════

        /// <summary>
        /// **确定性（非退化档）**：4 球开球场景（真实台，`RackPositions(footSpot, 3)`）双跑逐位一致
        /// （全部球的位置 + 速度）。
        /// 期望值来源 = **乙-结构**（逐位一致是非解析判据）。
        /// **判别力**：`a(v)` 依赖历史 / 引入 `HashSet`·`Dictionary` 迭代序 / `Time` / `Random` ⇒ FAIL。
        /// **盲带**：不覆盖"禁用清单不充分"（源码词表是结构性补充，非证明）。
        /// **注入负对照（实测）**：在 `DecelerationAt` 里加 `* (1f + Time.deltaTime * 0f)`（读 `Time`）⇒ FAIL。
        /// </summary>
        [Test]
        public void T58_Determinism_TwoStage_RealTableBreak()
        {
            var table = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(table, "T58：需要 " + TablePath);

            const int steps = 240;
            PhysicsParams p = Rolling(2.0f, 0.1f, 2.0f);

            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 3);
            var initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var a = new PoolSimulator(table, p);
            var b = new PoolSimulator(table, p);
            a.LoadBalls(initial);
            b.LoadBalls(initial);

            for (int s = 0; s < steps; s++)
            {
                a.Step();
                b.Step();
            }

            var failures = new List<string>();
            Assert.AreEqual(a.Balls.Count, b.Balls.Count, "T58：球数不一致。");
            for (int i = 0; i < a.Balls.Count; i++)
            {
                if (RState(a, i) != RState(b, i))
                {
                    failures.Add($"球 {i}：A={RState(a, i)}；B={RState(b, i)}。");
                }
            }

            Assert.IsEmpty(failures,
                "T58 确定性失败（非退化档双跑必须逐位一致）：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T58: 非退化档 4 球开球 × {steps} 步（{a.Balls.Count} 球）双跑逐位一致 ✓");
        }

        // ══════════════════ T59 门控 ══════════════════════════════════════════

        /// <summary>
        /// **门控**：`a_slide = a_roll = 0` ⇒ 与历史"无滚阻"逐位一致；**只有一段非零** ⇒ 仍然**生效**。
        ///
        /// ① 两段都 0：速度必须**逐位不变**（历史上门控为假 ⇒ 整条滚阻分支被跳过），且 240 步内不静止。
        /// ② 只有**滚动段**非零（`slide = 0, roll = 0.1`）⇒ 低速段有恒定保底减速 ⇒ 应能在上限内静止。
        /// ③ 只有**滑动段**非零（`slide = 0.1, roll = 0`）⇒ 高速段有减速 ⇒ 速度必须严格下降
        ///    （⚠ 此档 `a(v) = a_slide·v/v_t → 0`，是**指数衰减**、永不为精确 0 ⇒ 只断言"下降"，不断言"静止"）。
        ///
        /// 期望值来源 = **甲**（① 同一行为；②③ "非零即应生效"）。
        /// **判别力**：门控误写成 `&&` ⇒ ②③ 永不生效 ⇒ FAIL。**这正是 `B4` 同族的"门控反了"缺陷**。
        /// **注入负对照（实测）**：把门控的 `||` 改成 `&&` ⇒ ②③ FAIL。
        /// </summary>
        [Test]
        public void T59_Gate_BothZeroIsInert_AnySingleNonZeroIsEffective()
        {
            var failures = new List<string>();

            // ① 两段都 0 ⇒ 逐位同历史"无滚阻"：速度原封不动
            var v0 = new Vector2(0.5f, 0f);
            PoolSimulator inert = EmptySim(Rolling(0f, 0f, 1f), Vector2.zero, v0);
            for (int s = 0; s < 240; s++)
            {
                inert.Step();
            }

            if (RVec(inert.Balls[0].velocity) != RVec(v0))
            {
                failures.Add($"① a_slide=a_roll=0：速度被改动 —— 期望 {RVec(v0)}，实测 {RVec(inert.Balls[0].velocity)}（应与历史“整段跳过”逐位一致）。");
            }

            if (inert.TableAtRest)
            {
                failures.Add("① a_slide=a_roll=0：240 步内不应静止（无任何耗散）。");
            }

            // ② 只有滚动段非零 ⇒ 必须生效（能停下来）。`&&` 注入下此处永不静止。
            PoolSimulator rollOnly = EmptySim(Rolling(0f, 0.1f, 1f), Vector2.zero, new Vector2(0.5f, 0f));
            int rollSteps = RunToRest(rollOnly, 20000);
            if (!rollOnly.TableAtRest)
            {
                failures.Add($"② a_slide=0, a_roll=0.1：{rollSteps} 步内未静止 ⇒ 门控不是 `||`（只有一段非零时滚阻未生效）。");
            }

            // ③ 只有滑动段非零 ⇒ 必须生效（速度严格下降）。`&&` 注入下此处速度不变。
            PoolSimulator slideOnly = EmptySim(Rolling(0.1f, 0f, 1f), Vector2.zero, new Vector2(0.5f, 0f));
            var slideFailures = new List<string>();
            float prev = 0.5f;
            for (int s = 0; s < 240; s++)
            {
                slideOnly.Step();
                float sp = Speed(slideOnly.Balls[0].velocity);
                if (sp > prev + 1e-9f)
                {
                    slideFailures.Add($"第 {s + 1} 步反增（{prev:R} → {sp:R}）");
                    break;
                }

                prev = sp;
            }

            if (slideFailures.Count > 0)
            {
                failures.Add("③ a_slide=0.1, a_roll=0：速度反增 —— " + string.Join("；", slideFailures));
            }

            if (!(Speed(slideOnly.Balls[0].velocity) < 0.5f))
            {
                failures.Add($"③ a_slide=0.1, a_roll=0：240 步后速度仍为 {Speed(slideOnly.Balls[0].velocity):R}（未下降）⇒ 门控不是 `||`。");
            }

            Assert.IsEmpty(failures,
                "T59 门控失败（两段都 0 必须惰性；**任一段**非零必须生效 —— 门控是 `||` 不是 `&&`）：\n"
                + string.Join("\n", failures));
            TestContext.WriteLine($"T59: 两段 0 ⇒ 速度逐位不变且不静止；仅滚动段非零 ⇒ {rollSteps} 步静止；"
                                  + $"仅滑动段非零 ⇒ 240 步降到 {Speed(slideOnly.Balls[0].velocity):R} ✓");
        }
    }
}
