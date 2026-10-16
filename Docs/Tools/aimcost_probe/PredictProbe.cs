using System;
using System.Collections.Generic;
using System.Globalization;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using UnityEngine;

namespace aimcost
{
    /// <summary>
    /// **仪器：`ShotPredictor` 首接触步数 / 满力开球"打散球数"的 headless 复现**
    /// （只读产品代码，**不需要 Unity**；稳态、可无限复跑）。
    ///
    /// ── 用途 ──────────────────────────────────────────────────────────────
    /// · 复现并**推翻**"把 `maxLaunchSpeed` 调高会让瞄准预测射空"这一假设：在同一几何上，
    ///   预测**首碰步数随初速单调下降**；
    /// · 量化"满力开球把球堆打散了多少颗"（`T133` 的期望值取法据此确定），含**跨运行时**取值带。
    ///
    /// ── 用法 ──────────────────────────────────────────────────────────────
    ///   在 `Docs/Tools/aimcost_probe/` 下：
    ///   `dotnet build -c Release`
    ///   `dotnet bin/Release/net9.0/aimcost.dll predict`   # 预测首碰步数 + 真跑内核交叉验证
    ///   `dotnet bin/Release/net9.0/aimcost.dll scatter`   # 满力开球：v0 × 位移阈值 → 打散球数
    /// 几何与参数取自**场景序列化**值：`aimDegrees = 180`、`power = 0.5`、`min/max = 0.5/8`、
    /// `RackPositions(footSpot, 15)`（白球 (0.51,0)、堆顶点 (−0.30,0) 同在 y = 0 上）。
    ///
    /// ── 复现哪条结论 ──────────────────────────────────────────────────────
    /// · **预测首碰步数随 v0 单调下降**：`v0 = 1 / 2.5 / 4.25 / 4.5 / 8 (m/s)`
    ///   ⇒ **108 / 38 / 22 / 21 / 12 步**，且每个初速都 `HasFirstContact = true`
    ///   ⇒ **"高初速 ⇒ 预测射空（跑满 240 步无接触）"在本几何上不可能**；
    ///   真跑内核交叉验证同值（`v0 = 2.5 / 4.25 / 8` ⇒ 38 / 22 / 12 步，`guard = 0`）。
    /// · **满力打散数**（`scatter`：位移 &gt; 0.30 m 的堆球数 / 15）：`v0 = 4.5` ⇒ **5**、`v0 = 8` ⇒ **7**；
    ///   与 Mono 侧实测（4 / 8）合并给出**跨运行时带 7–8** ⇒ `T133` 的 `MinScattered` 应**留余量取 6**。
    ///
    /// ── 产物 ──────────────────────────────────────────────────────────────
    /// 只打印到 stdout；`bin/`、`obj/` 是构建产物，**不入库**。
    /// </summary>
    internal static class PredictProbe
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int Run(TableConfig table)
        {            // ── 场景序列化参数（Main.unity PhysicsTestDrive 块，实测行号见报告）──
            var p = new PhysicsParams
            {
                cushionRestitution = 0.8f,
                cushionTangentialFriction = 0.9f,
                ballRestitution = 0.95f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = 0.5f,
                rollingDecelerationRoll = 0.25f,
                rollingTransitionSpeed = 2f,
            };

            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 15);
            Console.WriteLine($"footSpot={V(table.footSpot)}  rackCount={rack.Length - 1}  rackApex={V(rack[1])}  rackLast={V(rack[rack.Length - 1])}");

            var balls = new List<BallState>(rack.Length);
            for (int i = 0; i < rack.Length; i++)
            {
                balls.Add(new BallState { position = rack[i], velocity = Vector2.zero });
            }

            var sim = new PoolSimulator(table, p);
            sim.LoadBalls(balls);
            int settle = 0;
            while (settle < 2000 && !sim.TableAtRest) { sim.Step(); settle++; }
            Console.WriteLine($"settled after {settle} steps; atRest={sim.TableAtRest} balls={sim.Balls.Count}");

            // ── ① 预测：同一几何、不同初速 ──
            Console.WriteLine("=== ShotPredictor.Predict(aim=180° ⇒ (-1,0)*speed) ===");
            foreach (float speed in new[] { 0.5f, 1f, 2.5f, 3f, 4f, 4.25f, 4.5f, 6f, 8f })
            {
                ShotPrediction pred = ShotPredictor.Predict(table, sim, new Vector2(-1f, 0f) * speed, 240);
                Console.WriteLine($"  v={speed,5:F2}  steps={pred.StepsSimulated,4}  hasContact={pred.HasFirstContact,-5} "
                    + $"bounces={pred.CushionBounceCountBeforeContact,3}  t={pred.TimeToFirstContactSeconds:F4}s  "
                    + $"cueStart={V(pred.CueStart)}  ghost={V(pred.GhostBall)}  bbCount={pred.BallBallContactCount}");
            }

            // ── ② 交叉验证：真跑内核（不是预测），看白球何时发生球-球接触 ──
            Console.WriteLine("=== 真跑内核（同几何）: cue = (-1,0)*speed，逐 10 步打印 bb 计数 ===");
            foreach (float speed in new[] { 2.5f, 4.25f, 8f })
            {
                var s = new PoolSimulator(table, p);
                s.LoadBalls(balls);
                int g = 0;
                while (g < 2000 && !s.TableAtRest) { s.Step(); g++; }
                s.ResetDiagnostics();
                s.PlaceBall(0, table.footSpot, new Vector2(-1f, 0f) * speed);
                int bb = -1;
                for (int i = 1; i <= 400; i++)
                {
                    s.Step();
                    if (s.BallBallContactCount > 0) { bb = i; break; }
                }
                Console.WriteLine($"  v={speed,5:F2}  首次球-球接触在第 {(bb < 0 ? ">400" : bb.ToString(Inv))} 步  "
                    + $"（cue 位移≈{speed * (bb < 0 ? 400 : bb) / 120f:F3} m）  guard={s.ToiIterationGuardTrips} frozen0={!s.IsBallInPlay(0)}");
            }

            return 0;
        }

        private static string V(Vector2 v) => $"({v.x.ToString("F4", Inv)},{v.y.ToString("F4", Inv)})";

        /// <summary>
        /// **`T133` 的独立判别力扫描**（headless，不跑 Unity）：同一"满力开球"场景，
        /// 扫 `v0` × 位移阈值，给出"位移 &gt; 阈值 的堆球数"。
        /// 用途：独立验证 `T133` 的期望（**8 颗 @ v0=8 m/s、阈值 0.30 m**）是否成立，
        /// 并量化**余量**（若 v0=8 恰好给 8 颗 ⇒ 阈值/期望无余量 ⇒ 抖动即红）。
        /// </summary>
        public static int Scatter(TableConfig table)
        {
            var p = new PhysicsParams
            {
                cushionRestitution = 0.9f,
                cushionTangentialFriction = 0.8f,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = 0.5f,
                rollingDecelerationRoll = 0.25f,
                rollingTransitionSpeed = 2f,
            };

            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 15);
            float[] thresholds = { 0.05f, 0.10f, 0.30f, 0.50f, 1.00f };
            Console.WriteLine("=== T133 独立判别力扫描（1200 步；数 = 位移 > 阈值 的堆球数，满分 15）===");
            Console.Write("    v0  ");
            foreach (float t in thresholds) Console.Write($"  thr{t:F2}");
            Console.WriteLine("   atRest guard");

            foreach (float v0 in new[] { 4.0f, 4.5f, 5.0f, 6.0f, 7.0f, 7.5f, 8.0f })
            {
                var init = new BallState[rack.Length];
                for (int i = 0; i < rack.Length; i++)
                {
                    init[i] = new BallState { position = rack[i], velocity = Vector2.zero };
                }

                init[0].velocity = new Vector2(-v0, 0f);

                var sim = new PoolSimulator(table, p);
                sim.LoadBalls(init);
                for (int i = 0; i < 1200; i++)
                {
                    sim.Step();
                }

                Console.Write($"  {v0,4:F1}  ");
                foreach (float t in thresholds)
                {
                    int c = 0;
                    for (int i = 1; i < sim.Balls.Count; i++)
                    {
                        if ((sim.Balls[i].position - init[i].position).magnitude > t)
                        {
                            c++;
                        }
                    }

                    Console.Write($"{c,7}");
                }

                Console.WriteLine($"   {(sim.TableAtRest ? "Y" : "N"),-6} {sim.ToiIterationGuardTrips}");
            }

            return 0;
        }
    }
}
