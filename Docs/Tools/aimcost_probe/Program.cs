using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using UnityEngine;

namespace aimcost
{
    /// <summary>
    /// **独立探针**（QA 复核用，位于 Docs/Tools，不进 Unity 工程）：
    /// 直接引用 Unity 上一次编译出的 `Billiards.Runtime.dll`（经 Cecil 改写掉 Unity ECall 的 null 运算符，
    /// 见 rewrite/），在 Unity 之外重建内核，量两件事：
    ///   ① 内核**单步成本**（16 球 + 82 段，静止空转）—— 与既有 `T119` 同口径，交叉验证"0.37 ms"；
    ///   ② 拖动瞄准冒烟里 `ShotPredictor.Predict` 一次重算**实际跑多少步**（`StepsSimulated`）
    ///      与耗时，覆盖 80 帧整圈瞄准角（与 `SmokeProbe.AimSweepDegrees` 同口径），并对多个步数上限重复。
    /// 参数 / 球桌几何 / 初速口径全部取自提交态资产 ⇒ 是"瓶颈=240 步内核重算"这一归因的独立可复算证据。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool eqMode = args.Length > 0 && args[0] == "eq";
            bool probeMode = args.Length > 0 && (args[0] == "predict" || args[0] == "scatter");
            int off = (eqMode || probeMode) ? 1 : 0;
            string json = args.Length > off
                ? args[off]
                : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "table.json");
            json = Path.GetFullPath(json);
            Console.WriteLine("table.json = " + json);

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(json));
            TableConfig table = BuildTable(doc.RootElement);

            if (eqMode)
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(json), "..", "..", ".."));
                return EqBaseline.Run(table, projectRoot);
            }

            // 仪器模式（`PredictProbe`）：纯**新增**分发 —— 对既有 `eq` / `full` 两条路径**零行为改变**。
            if (probeMode)
            {
                return args[0] == "scatter" ? PredictProbe.Scatter(table) : PredictProbe.Run(table);
            }

            // 与 PhysicsTuning.asset 逐字段一致（= 运行时真源）。
            var phys = new PhysicsParams
            {
                cushionRestitution = 0.8f,
                cushionTangentialFriction = 0.9f,
                ballRestitution = 0.95f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = 0.5f,
                rollingDecelerationRoll = 0.25f,
                rollingTransitionSpeed = 2f,
            };
            const float restSpeed = 0.001f;
            const int restFrames = 8;

            Console.WriteLine($"segmentCount={table.SegmentCount} pocketCount={table.PocketCount} footSpot=({table.footSpot.x},{table.footSpot.y})");

            // ── ① 内核单步成本（T119 ② 同口径：满堆开球 → 跑到静止 → 计时 3000 步）──
            Vector2[] rack = PhysicsTestDrive.RackPositions(table.footSpot, 15);
            var initial = new BallState[rack.Length];
            for (int i = 0; i < rack.Length; i++)
            {
                initial[i] = new BallState { position = rack[i], velocity = Vector2.zero };
            }

            initial[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);

            var idle = new PoolSimulator(table, phys, 1f, restSpeed, restFrames);
            idle.LoadBalls(initial);
            int toRest = 0;
            while (toRest < 100000 && !idle.TableAtRest)
            {
                idle.Step();
                toRest++;
            }

            const int idleSteps = 3000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < idleSteps; i++)
            {
                idle.Step();
            }

            sw.Stop();
            double idleUs = sw.Elapsed.TotalMilliseconds * 1000.0 / idleSteps;
            Console.WriteLine($"IDLE_STEP_US={idleUs.ToString("F2", CultureInfo.InvariantCulture)}  (toRest={toRest}, atRest={idle.TableAtRest})");

            // ── ② 拖动瞄准整圈：每次重算跑多少步 / 花多少 ms ──
            var source = new PoolSimulator(table, phys, 1f, restSpeed, restFrames);
            var restBalls = new List<BallState>(16);
            for (int i = 0; i < rack.Length; i++)
            {
                restBalls.Add(new BallState { position = rack[i], velocity = Vector2.zero });
            }

            source.LoadBalls(restBalls);
            int g = 0;
            while (g < 1000 && !source.TableAtRest)
            {
                source.Step();
                g++;
            }

            Console.WriteLine($"sweep source atRest={source.TableAtRest} (guardSteps={g})");

            var ws = new ShotPredictionWorkspace();

            for (int w = 0; w < 5; w++)
            {
                float a = SmokeProbe.AimSweepDegrees(w);
                float r = a * Mathf.Deg2Rad;
                ShotPredictor.Predict(table, source, new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 2.5f, 240, ws);
            }

            // ── ③ 步数上限 ↔ 重算成本 ↔ 截断线长（批次 B 定 cap 用）──
            // 力度三档：low/mid/high（power 0.25/0.5/1.0 ⇒ 初速 1.5/2.5/4.5 m/s，与场景 Lerp(0.5,4.5,p) 同式）。
            foreach (float power in new[] { 0.25f, 0.5f, 1.0f })
            {
                float speed = 0.5f + (4.5f - 0.5f) * power;
                Console.WriteLine($"===== power={power}  cueSpeed={speed:F2} m/s =====");
                foreach (int cap in new[] { 240, 120, 80, 60, 40, 30 })
                {
                    MeasureSweep(table, source, speed, ws, cap);
                }
            }

            // ── ④ 单步成本分段归因（只读 + 反射调用私有方法，绝不改产品代码）──
            Attribute(table, phys, rack, restSpeed, restFrames);

            return 0;
        }

        /// <summary>分段归因：把 Step() 的 377µs 拆到"球-段深重叠扫描 / 球-球宽相 / TOI"等段上。</summary>
        private static void Attribute(TableConfig table, PhysicsParams phys, Vector2[] rack,
            float restSpeed, int restFrames)
        {
            Console.WriteLine("===== 单步成本分段归因（16 球静止）=====");
            var sim = new PoolSimulator(table, phys, 1f, restSpeed, restFrames);
            var restBalls = new List<BallState>(16);
            for (int i = 0; i < rack.Length; i++)
            {
                restBalls.Add(new BallState { position = rack[i], velocity = Vector2.zero });
            }

            sim.LoadBalls(restBalls);
            int g = 0;
            while (g < 1000 && !sim.TableAtRest) { sim.Step(); g++; }
            Console.WriteLine($"  atRest={sim.TableAtRest}  balls={sim.Balls.Count}  SubSteps(DeriveSubSteps(MaxBallSpeed={PoolConstants.MaxBallSpeed}))={PoolSimulator.DeriveSubSteps(PoolConstants.MaxBallSpeed)}");

            var t = typeof(PoolSimulator);
            MethodInfo rdo = t.GetMethod("ResolveDeepOverlaps", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo est = t.GetMethod("EarliestSegmentToi", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo ebb = t.GetMethod("EarliestBallBallToi", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo cpk = t.GetMethod("CheckPocketProximity", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo upt = t.GetMethod("UpdateTermination", BindingFlags.NonPublic | BindingFlags.Instance);
            Console.WriteLine($"  reflection found: RDO={rdo != null} SegTOI={est != null} BBTOI={ebb != null} Pocket={cpk != null} Term={upt != null}");

            const int N = 3000;

            // 总 Step
            for (int i = 0; i < 200; i++) { sim.Step(); }
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) { sim.Step(); }
            sw.Stop();
            double stepUs = sw.Elapsed.TotalMilliseconds * 1000.0 / N;

            double TimeIt(Action a, int n)
            {
                for (int i = 0; i < 50; i++) { a(); }
                var w = Stopwatch.StartNew();
                for (int i = 0; i < n; i++) { a(); }
                w.Stop();
                return w.Elapsed.TotalMilliseconds * 1000.0 / n;
            }

            double rdoUs = rdo != null ? TimeIt(() => rdo.Invoke(sim, null), N) : double.NaN;
            double estUs = est != null ? TimeIt(() => est.Invoke(sim, new object[] { 1f / 120f }), N) : double.NaN;
            double ebbUs = ebb != null ? TimeIt(() => ebb.Invoke(sim, new object[] { 1f / 120f }), N) : double.NaN;
            double cpkUs = cpk != null ? TimeIt(() => cpk.Invoke(sim, new object[] { 0 }), N) : double.NaN;
            double uptUs = upt != null ? TimeIt(() => upt.Invoke(sim, null), N) : double.NaN;

            // CollectCandidatePairs(16) 单次
            var a1 = new List<int>(256); var a2 = new List<int>(256);
            double ccpUs = TimeIt(() => { a1.Clear(); a2.Clear(); PoolSimulator.CollectCandidatePairs(16, a1, a2); }, N);
            Console.WriteLine($"  pairs generated = {a1.Count}");

            // 分配字节/步
            for (int i = 0; i < 200; i++) { sim.Step(); }
            long b0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < N; i++) { sim.Step(); }
            long b1 = GC.GetAllocatedBytesForCurrentThread();
            double allocPerStep = (double)(b1 - b0) / N;

            int sub = PoolSimulator.DeriveSubSteps(PoolConstants.MaxBallSpeed);
            Console.WriteLine($"  TOTAL Step          = {stepUs,8:F2} µs/步   (100%)");
            Console.WriteLine($"  ResolveDeepOverlaps = {rdoUs,8:F2} µs/次   (×{sub}/步 = {rdoUs * sub:F1} µs = {rdoUs * sub / stepUs * 100:F1}%)");
            Console.WriteLine($"  EarliestSegmentToi(静止) = {estUs,8:F2} µs/次 (×{sub} = {estUs * sub:F1} = {estUs * sub / stepUs * 100:F1}%)  ← 静止应≈0（只遍历活跃球）");
            Console.WriteLine($"  EarliestBallBallToi(静止) = {ebbUs,7:F2} µs/次 (×{sub} = {ebbUs * sub:F1} = {ebbUs * sub / stepUs * 100:F1}%)");
            Console.WriteLine($"  CheckPocketProximity        = {cpkUs,8:F2} µs/次 (×16球×{sub} = {cpkUs * 16 * sub:F1} µs = {cpkUs * 16 * sub / stepUs * 100:F1}%)");
            Console.WriteLine($"  UpdateTermination           = {uptUs,8:F2} µs/次 (×1/步 = {uptUs / stepUs * 100:F1}%)");
            Console.WriteLine($"  CollectCandidatePairs(16)   = {ccpUs,8:F3} µs/次 (×{sub} = {ccpUs * sub:F3})");
            Console.WriteLine($"  每步分配 = {allocPerStep:F1} 字节/步  (0 ⇒ 无 GC 白送空间)");
            Console.WriteLine($"  球×段对/步 = 16×82×{sub} = {16 * 82 * sub}");
        }

        private static void MeasureSweep(TableConfig table, PoolSimulator source, float speed,
            ShotPredictionWorkspace ws, int cap)
        {
            const int frames = 80;
            long totalSteps = 0;
            double totalMs = 0.0;
            int noContact = 0;
            int maxStepsSeen = 0;
            var msList = new List<double>(frames);
            var arcList = new List<double>(frames);   // 画出的折线弧长（米）= 用户实际看到的线长
            var reachList = new List<double>(frames); // 白球起点到路径末点的直线距离（米）
            var stepList = new List<int>(frames);

            for (int f = 0; f < frames; f++)
            {
                float a = SmokeProbe.AimSweepDegrees(f);
                float r = a * Mathf.Deg2Rad;
                var v = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * speed;

                var swp = Stopwatch.StartNew();
                ShotPrediction p = ShotPredictor.Predict(table, source, v, cap, ws);
                swp.Stop();

                msList.Add(swp.Elapsed.TotalMilliseconds);
                totalSteps += p.StepsSimulated;
                totalMs += swp.Elapsed.TotalMilliseconds;
                stepList.Add(p.StepsSimulated);
                if (p.StepsSimulated > maxStepsSeen)
                {
                    maxStepsSeen = p.StepsSimulated;
                }

                if (!p.HasFirstContact)
                {
                    noContact++;
                }

                // 弧长 + 直线reach（读工作区路径缓冲；单消费者，立即消费）
                IReadOnlyList<Vector2> path = p.CueWorldPath;
                double arc = 0.0;
                for (int i = 0; i + 1 < path.Count; i++)
                {
                    arc += (path[i + 1] - path[i]).magnitude;
                }

                arcList.Add(arc);
                reachList.Add(path.Count >= 1 ? (path[path.Count - 1] - path[0]).magnitude : 0.0);
            }

            var msSorted = new List<double>(msList);
            msSorted.Sort();
            var arcSorted = new List<double>(arcList);
            arcSorted.Sort();
            var reachSorted = new List<double>(reachList);
            reachSorted.Sort();
            double avgSteps = (double)totalSteps / frames;
            double avgMs = totalMs / frames;
            double usPerStep = totalSteps > 0 ? totalMs / totalSteps * 1000.0 : 0.0;
            int below125 = 0, below250 = 0;
            foreach (double a in arcList)
            {
                if (a < 1.25) below125++;
                if (a < 2.5) below250++;
            }

            Console.WriteLine($"cap={cap,-4} avgSteps={avgSteps,7:F1} maxSteps={maxStepsSeen,4} "
                              + $"probe_ms avg={avgMs,7:F3} p95={msSorted[(int)(0.95 * msSorted.Count)],7:F3} "
                              + $"us/step={usPerStep,6:F1} noContact={noContact,2}/80 "
                              + $"arcLen(m) p05={arcSorted[(int)(0.05 * arcSorted.Count)],6:F2} "
                              + $"med={arcSorted[arcSorted.Count / 2],6:F2} "
                              + $"p95={arcSorted[(int)(0.95 * arcSorted.Count)],6:F2} max={arcSorted[arcSorted.Count - 1],6:F2} "
                              + $"| <1.25m:{below125,2}/80 <2.5m:{below250,2}/80 "
                              + $"| reach(m) p05={reachSorted[(int)(0.05 * reachSorted.Count)],5:F2} "
                              + $"med={reachSorted[reachSorted.Count / 2],5:F2} max={reachSorted[reachSorted.Count - 1],5:F2}");
        }

        private static TableConfig BuildTable(JsonElement root)
        {
            var t = (TableConfig)RuntimeHelpers.GetUninitializedObject(typeof(TableConfig));

            JsonElement sc = root.GetProperty("scalars");
            t.innerLength = sc.GetProperty("innerLength").GetSingle();
            t.innerWidth = sc.GetProperty("innerWidth").GetSingle();
            t.halfLength = sc.GetProperty("halfLength").GetSingle();
            t.halfWidth = sc.GetProperty("halfWidth").GetSingle();
            t.clothHeight = sc.GetProperty("clothHeight").GetSingle();
            t.noseHeight = sc.GetProperty("noseHeight").GetSingle();
            t.ballRadius = sc.GetProperty("ballRadius").GetSingle();
            t.legBottomY = sc.GetProperty("legBottomY").GetSingle();

            JsonElement fs = root.GetProperty("footSpot");
            t.footSpot = new Vector2(fs[0].GetSingle(), fs[1].GetSingle());

            t.segments = new List<CushionSegment>();
            foreach (JsonElement s in root.GetProperty("segments").EnumerateArray())
            {
                t.segments.Add(new CushionSegment
                {
                    a = Vec(s.GetProperty("a")),
                    b = Vec(s.GetProperty("b")),
                    rail = s.GetProperty("rail").GetString(),
                });
            }

            t.pockets = new List<Pocket>();
            foreach (JsonElement p in root.GetProperty("pockets").EnumerateArray())
            {
                t.pockets.Add(new Pocket
                {
                    index = p.GetProperty("index").GetInt32(),
                    kind = GetStr(p, "kind"),
                    center = Vec(p.GetProperty("center")),
                    mouthA = Vec(p.GetProperty("mouthA")),
                    mouthB = Vec(p.GetProperty("mouthB")),
                    throatA = Vec(p.GetProperty("throatA")),
                    throatB = Vec(p.GetProperty("throatB")),
                    throatCenter = Vec(p.GetProperty("throatCenter")),
                    throatNormal = Vec(p.GetProperty("throatNormal")),
                    mouthWidth = p.GetProperty("mouthWidth").GetSingle(),
                    throatWidth = p.GetProperty("throatWidth").GetSingle(),
                    openingRadius = p.GetProperty("openingRadius").GetSingle(),
                    captureRadius = p.GetProperty("captureRadius").GetSingle(),
                    captureNote = GetStr(p, "captureNote"),
                });
            }

            return t;
        }

        private static string GetStr(JsonElement e, string name)
        {
            return e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }

        private static Vector2 Vec(JsonElement e)
        {
            return new Vector2(e[0].GetSingle(), e[1].GetSingle());
        }
    }
}
