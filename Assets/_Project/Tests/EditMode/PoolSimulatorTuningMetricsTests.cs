using System.Collections.Generic;
using System.Globalization;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **调参度量探针（`T114`）** —— 给"手感调参"提供**同一把尺子**：以固定脚本场景测出
    /// 「参数 → 可观测量」的映射，并把**单调性 / 闭式一致性**写成闸门（防"改了参数其实没生效"的静默失效）。
    ///
    /// ── 它是什么、不是什么 ─────────────────────────────────────────────────
    /// · **是**：调参时的客观读数发生器（打印一张度量表）+ 三条防呆闸门；
    /// · **不是**："调参本身"。客观代理量（行程 / 库边速度比 / 停止耗时）**不能**代替"手感好不好"——
    ///   那一半由人评级（见 `Docs/35-A_Day15_手感调优与边界用例_spec.md` §2.2 的记录格式）。
    ///
    /// ── 判据（三条闸门）────────────────────────────────────────────────────
    /// · **(a) 初速 ↑ ⇒ 行程 ↑**（合成空台、两段滚阻相等 ⇒ 闭式 `s = v²/(2a)` 可比）；
    /// · **(b) 库边恢复系数 e ⇒ 撞库后法向速度比 == e**（对 e 扫描，±1e−3）；
    /// · **(c) 滚阻 ↑ ⇒ 同初速行程 ↓**（且实测值与闭式一致 ⇒ 同时证明"滚阻真的接在解算上"）。
    ///
    /// **为什么这三条值得当闸门**：它们各自能打死一类**静默失效**——
    /// (a) 力度没接到球杆初速（`minLaunchSpeed`/`maxLaunchSpeed` 接错通道）；
    /// (b) `cushionRestitution` 被别的量盖住 / 取反；(c) 滚阻接反或压根没生效（`T59` 守的是"门控"，
    /// 这里守的是"量级与方向"）。
    ///
    /// **盲带（如实）**：全部用**合成空台**（无库边、无袋口、无球-球）⇒ 不覆盖真实表的几何干扰；
    /// 也不覆盖"两段滚阻不等"的组合（那由 `T56a`/`T56b`/`T58` 承担）。
    /// </summary>
    public class PoolSimulatorTuningMetricsTests
    {
        private static TableConfig EmptyTable()
        {
            var t = ScriptableObject.CreateInstance<TableConfig>();
            t.segments = new List<CushionSegment>();
            t.pockets = new List<Pocket>();
            return t;
        }

        private static PhysicsParams Params(float e = 1f, float mu = 1f, float slide = 0f, float roll = 0f,
            float transition = 0f)
        {
            return new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = slide,
                rollingDecelerationRoll = roll,
                rollingTransitionSpeed = transition,
            };
        }

        /// <summary>用合成空台量"给定初速的滑行距离"：推进到静止（或上限）后返回累计位移。</summary>
        private static float FreeTravelDistance(float v0, PhysicsParams parameters, out int steps)
        {
            var sim = new PoolSimulator(EmptyTable(), parameters);
            sim.LoadBalls(new[] { new BallState { position = Vector2.zero, velocity = new Vector2(v0, 0f) } });

            float travelled = 0f;
            Vector2 previous = Vector2.zero;
            steps = 0;
            const int cap = 20000;
            while (steps < cap && (sim.Balls[0].velocity.magnitude > PoolSimulator.RestSpeedThreshold
                                   || sim.ConsecutiveRestFrameCount < PoolSimulator.RestStableFrames))
            {
                sim.Step();
                Vector2 now = sim.Balls[0].position;
                travelled += (now - previous).magnitude;
                previous = now;
                steps++;
            }

            return travelled;
        }

        [Test]
        public void T114_TuningMetrics_MonotoneAndClosedForm()
        {
            var failures = new List<string>();
            var table = new System.Text.StringBuilder();
            var culture = CultureInfo.InvariantCulture;

            // ── (a) 初速 ↑ ⇒ 行程 ↑（闭式 s = v²/(2a)，两段滚阻相等 ⇒ 退化为常量减速度）──
            const float a = 0.25f;
            float previousTravel = -1f;
            foreach (float v0 in new[] { 1f, 2f, 3f, 4f })
            {
                float travel = FreeTravelDistance(v0, Params(slide: a, roll: a, transition: 0f), out int steps);
                float analytic = v0 * v0 / (2f * a);
                table.Append($"    [a] v0={v0:F2} ⇒ 行程 {travel:F4} m（闭式 {analytic:F4} m，{steps} 步）\n");

                if (!(travel > previousTravel))
                {
                    failures.Add($"(a) 初速 {v0:F2} m/s 的行程 {travel:F4} m 未大于上一档 {previousTravel:F4} m"
                                 + "（力度没接到球杆初速？）。");
                }

                if (Mathf.Abs(travel - analytic) > 0.02f * analytic + 0.01f)
                {
                    failures.Add($"(a) 初速 {v0:F2} m/s 的行程 {travel:F4} m 与闭式 {analytic:F4} m 差得过多"
                                 + "（滚阻没接在解算上 / 量纲不对）。");
                }

                previousTravel = travel;
            }

            // ── (b) 库边恢复系数 e ⇒ 撞库后法向速度比 == e（扫描 e）──
            var eTable = new System.Text.StringBuilder();
            foreach (float e in new[] { 0.5f, 0.8f, 1.0f })
            {
                // 合成单段：x = 0 处竖直段；球自 −x 侧以 2 m/s 正撞（切向分量为 0 ⇒ 速度比 = 法向恢复系数）
                var t = ScriptableObject.CreateInstance<TableConfig>();
                t.segments = new List<CushionSegment>
                {
                    new CushionSegment { a = new Vector2(0f, -0.30f), b = new Vector2(0f, 0.30f), rail = "synthetic" },
                };
                t.pockets = new List<Pocket>();

                var sim = new PoolSimulator(t, Params(e: e, mu: 1f));
                sim.LoadBalls(new[] { new BallState { position = new Vector2(-0.25f, 0f), velocity = new Vector2(2f, 0f) } });

                float vIn = 2f;
                float vOut = 0f;
                for (int step = 0; step < 60; step++)
                {
                    sim.Step();
                    if (sim.Balls[0].velocity.x < 0f)
                    {
                        vOut = -sim.Balls[0].velocity.x;
                        break;
                    }
                }

                float ratio = vOut / vIn;
                eTable.Append($"    [b] e={e:F2} ⇒ 撞库后法向速度比 {ratio:F6}（应为 {e:F2}）\n");

                if (Mathf.Abs(ratio - e) > 1e-3f)
                {
                    failures.Add($"(b) e = {e:F2} 时实测速度比 {ratio:F6} ≠ e（差 {Mathf.Abs(ratio - e):E3}）。");
                }

                if (e > 0.5f && ratio <= 0.6f)
                {
                    failures.Add($"(b) e 单调性被破坏（e = {e:F2} 但比值只有 {ratio:F6}）。");
                }
            }

            // ── (c) 滚阻 ↑ ⇒ 行程 ↓（同初速 4 m/s）──
            var cTable = new System.Text.StringBuilder();
            float previousTravelC = float.PositiveInfinity;
            foreach (float decel in new[] { 0.10f, 0.25f, 0.50f })
            {
                float travel = FreeTravelDistance(4f, Params(slide: decel, roll: decel, transition: 0f), out int steps);
                cTable.Append($"    [c] 滚阻 a={decel:F2} ⇒ 行程 {travel:F4} m（{steps} 步）\n");
                if (!(travel < previousTravelC))
                {
                    failures.Add($"(c) 滚阻 {decel:F2} 的行程 {travel:F4} m 未小于上一档 {previousTravelC:F4} m（滚阻接反？）。");
                }

                previousTravelC = travel;
            }

            TestContext.WriteLine("T114 调参度量表（合成空台；客观代理量，不等于手感评级）：\n"
                                  + table + eTable + cTable);
            Assert.IsEmpty(failures, "T114 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T114: 度量表 3 组全部单调且闭式一致 ✓（a=0.25 ⇒ 4 m/s 行程 {4f * 4f / (2f * 0.25f):F4} m）"
                                  + $" [{culture.Name}]");
        }
    }
}
