using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Billiards.Core;
using Billiards.Physics;
using Billiards.Table;
using UnityEngine;

namespace aimcost
{
    /// <summary>
    /// **等价性对照基线**（headless CoreCLR；只读产品代码）。
    ///
    /// 目的：在内核（`ResolveDeepOverlaps` 的球-段扫描）做"保守预筛"这类**等价优化**之前，
    /// 把一组**最大化覆盖物理行为**的场景的**逐位结果**固定下来 —— 优化后必须**全等**。
    ///
    /// 逐位形式：float 一律 `ToString("R", Invariant)`（与既有 `AssertBitIdentical` 同口径）；
    /// 整数直取。全部字段按 key 排序后拼串 → **SHA-256**（先比哈希，不等再逐字段 diff）。
    ///
    /// ⚠ 运行时口径：本探针是 **CoreCLR**；EditMode 是 **Mono**。`CoreCLR` 与 `Mono` 的 float
    /// 末位**不可比**（更高精度中间值 / JIT 可能 FMA 收缩）⇒ **采集与比对必须同一运行时**。
    /// 本基线的法定用途 = **headless↔headless** 比对；Mono 侧另由"EditMode 全量 220 + 期望值零改动"守卫。
    /// </summary>
    internal static class EqBaseline
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>(8192);

        private static void Add(string k, float v) => Fields.Add(new KeyValuePair<string, string>(k, v.ToString("R", Inv)));
        private static void AddI(string k, int v) => Fields.Add(new KeyValuePair<string, string>(k, v.ToString(Inv)));
        private static void AddB(string k, bool v) => Fields.Add(new KeyValuePair<string, string>(k, v ? "true" : "false"));

        private const string KernelDir = "Assets/_Project/Scripts";

        // ── 内核文件清单（用于记录"基线覆盖了哪些内核文件 + 各自 sha1"）──
        private static readonly string[] KernelFiles =
        {
            "Assets/_Project/Scripts/Runtime/Physics/PoolSimulator.cs",
            "Assets/_Project/Scripts/Runtime/Physics/PoolMath.cs",
            "Assets/_Project/Scripts/Runtime/Physics/BallState.cs",
            "Assets/_Project/Scripts/Runtime/Physics/PhysicsParams.cs",
            "Assets/_Project/Scripts/Runtime/Physics/PhysicsTuning.cs",
            "Assets/_Project/Scripts/Runtime/Physics/ShotPrediction.cs",
            "Assets/_Project/Scripts/Runtime/Core/PoolGeometry.cs",
            "Assets/_Project/Scripts/Runtime/Core/PoolConstants.cs",
        };

        public static int Run(TableConfig real, string projectRoot)
        {
            string start = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            Fields.Clear();

            // 诊断：打印**实际加载**的物理程序集位置 + 其 sha256（确认跑的到底是哪个 DLL）
            var asm = typeof(PoolSimulator).Assembly;
            string asmPath = asm.Location;
            string asmSha = File.Exists(asmPath) ? Hex(SHA256.Create().ComputeHash(File.ReadAllBytes(asmPath))) : "<n/a>";
            Console.WriteLine($"physics assembly: {asmPath}");
            Console.WriteLine($"physics assembly sha256: {asmSha}");

            // 参数源：PhysicsTuning.asset 的 rolling 三段（ShipParams 用）
            var (aSlide, aRoll, vT) = ReadTuning(projectRoot);
            Console.WriteLine($"tuning rolling: slide={aSlide} roll={aRoll} transition={vT}");

            ShipParams = (e, mu) => new PhysicsParams
            {
                cushionRestitution = e,
                cushionTangentialFriction = mu,
                ballRestitution = 1f,
                ballTangentialFriction = 1f,
                rollingDecelerationSlide = aSlide,
                rollingDecelerationRoll = aRoll,
                rollingTransitionSpeed = vT,
            };

            // ═══ S1 T32b ═══
            {
                Pocket side = real.GetPocket(1), corner = real.GetPocket(0);
                Vector2 sideStart = side.throatCenter - 0.35f * side.throatNormal;
                Vector2 cTn = corner.throatNormal, cTd = new Vector2(-cTn.y, cTn.x);
                Vector2 cornerStart = corner.throatCenter - 0.12f * cTn + 0.060f * cTd;
                var sim = new PoolSimulator(real, ShipParams(0.9f, 0.8f));
                sim.LoadBalls(new[]
                {
                    Ball(sideStart.x, sideStart.y, 2f * side.throatNormal.x, 2f * side.throatNormal.y),
                    Ball(cornerStart.x, cornerStart.y, 2f * cTn.x, 2f * cTn.y),
                    Ball(0f, 0.45f, 0.15f, 0f),
                });
                Step(sim, 120);
                Rec("S1_T32b", sim);
            }

            // ═══ S2 T37 / S3 T111（同场景）═══
            Vector2[] rack = PhysicsTestDrive.RackPositions(real.footSpot, 15);
            BallState[] breakInit()
            {
                var a = new BallState[rack.Length];
                for (int i = 0; i < rack.Length; i++) a[i] = new BallState { position = rack[i], velocity = Vector2.zero };
                a[0].velocity = new Vector2(-PoolConstants.MaxBallSpeed, 0f);
                return a;
            }

            {
                var sim = new PoolSimulator(real, ShipParams(0.9f, 0.8f));
                sim.LoadBalls(breakInit());
                Step(sim, 1200);
                Rec("S2_T37", sim);
            }
            {
                var sim = new PoolSimulator(real, ShipParams(0.9f, 0.8f));
                sim.LoadBalls(breakInit());
                int steps = 0;
                while (steps < 1800 && !sim.TableAtRest) { sim.Step(); steps++; }
                AddI("S3_T111.settleSteps", steps);
                Rec("S3_T111", sim);
            }

            // ═══ S4 T22b ═══
            {
                var sim = new PoolSimulator(Empty(), P(1f, 1f, 0f));
                sim.LoadBalls(new[] { Ball(0f, 0f, 0f, 0f), Ball(1e-10f, 0f, 0f, 0f) });
                Step(sim, 1);
                Rec("S4_T22b", sim);
            }

            // ═══ S5 T110(a) 合成 5mm 段 ═══
            {
                var t = Segs(new Vector2(0f, -0.0025f), new Vector2(0f, 0.0025f));
                var sim = new PoolSimulator(t, P(0.9f, 0.8f, 0f));
                float dtSub = sim.SubStepDeltaTime;
                int sub = sim.SubSteps;
                float clear = PoolConstants.BallRadius + (sub - 1) * 8f * dtSub + 0.02f * 8f * dtSub;
                sim.LoadBalls(new[] { Ball(-clear, 0f, 8f, 0f) });
                bool bounced = false;
                for (int s = 0; s < 8; s++) { sim.Step(); if (sim.Balls[0].velocity.x < 0f) bounced = true; }
                AddB("S5_T110a.bounced", bounced);
                Rec("S5_T110a", sim);
            }

            // ═══ S6 T110(b) 真实表中袋袋角 ═══
            {
                Pocket jaw = real.GetPocket(1);
                Vector2 tn = jaw.throatNormal, td = new Vector2(-tn.y, tn.x);
                Vector2 pos = jaw.throatCenter - 0.45f * tn + 0.055f * td;
                var sim = new PoolSimulator(real, P(0.9f, 0.8f, 0f));
                sim.LoadBalls(new[] { Ball(pos.x, pos.y, PoolConstants.MaxBallSpeed * tn.x, PoolConstants.MaxBallSpeed * tn.y) });
                Step(sim, 120);
                Rec("S6_T110b", sim);
            }

            // ═══ S7 T31d（喉口边界双侧）═══
            { var s = RadiusProbe(ProbeOpening() - 1e-3f); Rec("S7_T31d.inside", s); }
            { var s = RadiusProbe(ProbeOpening() + 1e-3f); Rec("S7_T31d.outside", s); }

            // ═══ S8 T106（喉口内）═══
            { var s = RadiusProbe(0.5f * (ProbeCapture() + ProbeOpening())); Rec("S8_T106", s); }

            // ═══ S9 T107（喉口外）═══
            { var s = RadiusProbe(ProbeOpening() + 1e-3f); Rec("S9_T107", s); }

            // ═══ S10 T108 ═══
            {
                int k = 0;
                foreach (float rho in new[] { 0f, 0.5f * (ProbeCapture() + ProbeOpening()), ProbeOpening() + 1e-3f })
                {
                    var s = RadiusProbe(rho);
                    Rec("S10_T108.a" + k, s);
                    k++;
                }

                var pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), ProbeCapture());
                var rest = new PoolSimulator(Synthetic(pocket), P(1f, 1f, 0f));
                rest.LoadBalls(new[] { Ball(0f, 0.03f, 0f, 0f) });
                Step(rest, PoolSimulator.RestStableFrames + 2);
                Rec("S10_T108.b", rest);

                var near = new PoolSimulator(Synthetic(pocket), P(1f, 1f, 0f));
                near.LoadBalls(new[] { Ball(0f, ProbeOpening() - 1e-3f, 0f, 0f) });
                Step(near, PoolSimulator.RestStableFrames + 2);
                Rec("S10_T108.c_in", near);

                var outb = new PoolSimulator(Synthetic(pocket), P(1f, 1f, 0f));
                outb.LoadBalls(new[] { Ball(0f, ProbeOpening() + 1e-3f, 0f, 0f) });
                Step(outb, PoolSimulator.RestStableFrames + 2);
                Rec("S10_T108.c_out", outb);

                // (d) 真实表洞口中心（表现层落点）
                var realSim = new PoolSimulator(real, P(1f, 1f, 0f));
                for (int p = 0; p < real.PocketCount; p++)
                {
                    Vector2 h = realSim.PocketHoleCenter(p);
                    Add($"S10_T108.d.hole{p}.x", h.x);
                    Add($"S10_T108.d.hole{p}.y", h.y);
                }
            }

            // ═══ S11 T114 ═══
            {
                const float a = 0.25f;
                int idx = 0;
                foreach (float v0 in new[] { 1f, 2f, 3f, 4f })
                {
                    float travel = FreeTravel(Empty(), PT(1f, 1f, a, a, 0f), v0, out int steps);
                    Add($"S11_T114.a{idx}.travel", travel);
                    AddI($"S11_T114.a{idx}.steps", steps);
                    idx++;
                }

                idx = 0;
                foreach (float e in new[] { 0.5f, 0.8f, 1.0f })
                {
                    var t = Segs(new Vector2(0f, -0.30f), new Vector2(0f, 0.30f));
                    var sim = new PoolSimulator(t, PT(e, 1f, 0f, 0f, 0f));
                    sim.LoadBalls(new[] { Ball(-0.25f, 0f, 2f, 0f) });
                    float vOut = 0f;
                    for (int s = 0; s < 60; s++)
                    {
                        sim.Step();
                        if (sim.Balls[0].velocity.x < 0f) { vOut = -sim.Balls[0].velocity.x; break; }
                    }
                    Add($"S11_T114.b{idx}.vOut", vOut);
                    Add($"S11_T114.b{idx}.ratio", vOut / 2f);
                    idx++;
                }

                idx = 0;
                foreach (float decel in new[] { 0.10f, 0.25f, 0.50f })
                {
                    float travel = FreeTravel(Empty(), PT(1f, 1f, decel, decel, 0f), 4f, out int steps);
                    Add($"S11_T114.c{idx}.travel", travel);
                    AddI($"S11_T114.c{idx}.steps", steps);
                    idx++;
                }
            }

            // ═══ S12 T35 / T36 ═══
            {
                var p35 = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), 0.0307f);
                var s35 = new PoolSimulator(Synthetic(p35), P(1f, 1f, 0f));
                s35.LoadBalls(new[] { Ball(0.00f, 0.20f, 0f, -PoolConstants.MaxBallSpeed), Ball(0.08f, 0.60f, 0f, -PoolConstants.MaxBallSpeed) });
                Step(s35, 40);
                Rec("S12_T35", s35);

                var left = MakePocket(0, new Vector2(-0.20f, 0f), new Vector2(-1f, 0f), 0.0307f);
                var right = MakePocket(1, new Vector2(0.20f, 0f), new Vector2(1f, 0f), 0.0307f);
                var t36 = Synthetic(left, right);
                var s36 = new PoolSimulator(t36, P(1f, 1f, 0f));
                s36.LoadBalls(new[] { Ball(-0.10f, 0f, -PoolConstants.MaxBallSpeed, 0f), Ball(0.10f, 0f, PoolConstants.MaxBallSpeed, 0f) });
                Step(s36, 40);
                Rec("S12_T36", s36);
            }

            // ═══ S13 ★ 白球放回被占用的 footSpot（静态重叠分离：RDO 存在的唯一理由）═══
            {
                var sim = new PoolSimulator(real, ShipParams(0.9f, 0.8f));
                sim.LoadBalls(new[]
                {
                    Ball(real.footSpot.x, real.footSpot.y, 0f, 0f),
                    Ball(real.footSpot.x + 0.001f, real.footSpot.y, 0f, 0f),
                });
                sim.Step();
                Rec("S13.footSpotOverlap.step1", sim);
                Step(sim, 59);
                Rec("S13.footSpotOverlap.step60", sim);
            }

            // ═══ S14 ★ 球嵌入库边段（RDO 的球-段分支：98% 成本所在）═══
            {
                CushionSegment seg = real.segments[6];
                Vector2 m = 0.5f * (seg.a + seg.b);
                Vector2 dir = Norm(seg.b - seg.a);
                Vector2 n = new Vector2(-dir.y, dir.x);
                if (n.x * (0f - m.x) + n.y * (0f - m.y) < 0f) n = -n;   // 指向台心
                Vector2 pos = m + n * (PoolConstants.BallRadius - 0.005f);   // 嵌入 5 mm
                var sim = new PoolSimulator(real, ShipParams(0.9f, 0.8f));
                sim.LoadBalls(new[] { Ball(pos.x, pos.y, 0f, 0f) });
                sim.Step();
                Rec("S14.segmentEmbed.step1", sim);
                Step(sim, 29);
                Rec("S14.segmentEmbed.step30", sim);
            }

            string end = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);

            // ── 规范化 + SHA-256 ──
            Fields.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            var canon = new StringBuilder(Fields.Count * 48);
            foreach (var f in Fields) canon.Append(f.Key).Append('=').Append(f.Value).Append('\n');
            byte[] bytes = Encoding.UTF8.GetBytes(canon.ToString());
            string sha;
            using (var h = SHA256.Create()) sha = Hex(h.ComputeHash(bytes));

            // 内核文件 sha1（git blob 口径，与我先前核对的 hash 一致）
            var kernelLines = new List<string>();
            string poolSha = "";
            foreach (string f in KernelFiles)
            {
                string full = Path.Combine(projectRoot, f);
                string s = File.Exists(full) ? GitBlobSha1(File.ReadAllBytes(full)) : "<missing>";
                kernelLines.Add($"{s}  {f}");
                if (f.EndsWith("PoolSimulator.cs")) poolSha = s;
            }

            string outDir = Path.Combine(Path.GetTempPath(), "billiards_eqbase");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, poolSha.Substring(0, 12) + ".json");

            var json = new StringBuilder(canon.Length + 4096);
            json.Append("{\n");
            json.Append("  \"schema\": \"billiards.eqbaseline.v1\",\n");
            json.Append("  \"note\": \"CoreCLR headless; compare headless<->headless only (float bits NOT comparable CoreCLR vs Mono).\",\n");
            json.Append($"  \"windowStart\": \"{start}\",\n");
            json.Append($"  \"windowEnd\": \"{end}\",\n");
            json.Append($"  \"scenarioFieldCount\": {Fields.Count},\n");
            json.Append($"  \"sha256\": \"{sha}\",\n");
            json.Append($"  \"canonicalBytes\": {bytes.Length},\n");
            json.Append("  \"kernelFilesSha1\": [\n");
            for (int i = 0; i < kernelLines.Count; i++)
                json.Append("    \"").Append(kernelLines[i]).Append(i + 1 < kernelLines.Count ? "\",\n" : "\"\n");
            json.Append("  ],\n");
            json.Append("  \"fields\": {\n");
            for (int i = 0; i < Fields.Count; i++)
                json.Append("    \"").Append(Fields[i].Key).Append("\": \"").Append(Fields[i].Value)
                    .Append(i + 1 < Fields.Count ? "\",\n" : "\"\n");
            json.Append("  }\n}\n");
            File.WriteAllText(outPath, json.ToString(), new UTF8Encoding(false));

            Console.WriteLine();
            Console.WriteLine("===== EQ BASELINE =====");
            Console.WriteLine($"window      : {start} → {end}");
            Console.WriteLine($"fields      : {Fields.Count}");
            Console.WriteLine($"canon bytes : {bytes.Length}");
            Console.WriteLine($"SHA-256     : {sha}");
            Console.WriteLine($"written     : {outPath}");
            Console.WriteLine("kernel sha1:");
            foreach (string l in kernelLines) Console.WriteLine("  " + l);
            return 0;
        }

        // ── 记录一个 sim 的全部可观测量 ──
        private static void Rec(string s, PoolSimulator sim)
        {
            AddI(s + ".TotalContactCount", sim.TotalContactCount);
            AddI(s + ".BallBallContactCount", sim.BallBallContactCount);
            AddI(s + ".BallBallSeparationCount", sim.BallBallSeparationCount);
            AddI(s + ".PocketProximityEvents", sim.PocketProximityEvents);
            AddI(s + ".CapturedBallCount", sim.CapturedBallCount);
            AddI(s + ".PocketJawCrossingCount", sim.PocketJawCrossingCount);
            AddI(s + ".AtRestMouthDrops", sim.AtRestMouthDrops);
            AddI(s + ".ToiIterationGuardTrips", sim.ToiIterationGuardTrips);
            AddI(s + ".EnergyGainViolations", sim.EnergyGainViolations);
            AddI(s + ".BallBallMaxIterationsObserved", sim.BallBallMaxIterationsObserved);
            AddI(s + ".ConsecutiveRestFrameCount", sim.ConsecutiveRestFrameCount);
            AddB(s + ".TableAtRest", sim.TableAtRest);
            for (int i = 0; i < sim.Balls.Count; i++)
            {
                BallState b = sim.Balls[i];
                Add($"{s}.b{i}.px", b.position.x);
                Add($"{s}.b{i}.py", b.position.y);
                Add($"{s}.b{i}.vx", b.velocity.x);
                Add($"{s}.b{i}.vy", b.velocity.y);
                AddI($"{s}.b{i}.capPocket", sim.CapturedPocketIndex(i));
                AddB($"{s}.b{i}.inPlay", sim.IsBallInPlay(i));
            }
        }

        // ── helpers ──
        private static Func<float, float, PhysicsParams> ShipParams;

        private static BallState Ball(float x, float y, float vx, float vy)
            => new BallState { position = new Vector2(x, y), velocity = new Vector2(vx, vy) };

        private static void Step(PoolSimulator sim, int n) { for (int i = 0; i < n; i++) sim.Step(); }

        private static Vector2 Norm(Vector2 v)
        {
            float m = (float)Math.Sqrt(v.x * v.x + v.y * v.y);
            return m > 0f ? new Vector2(v.x / m, v.y / m) : Vector2.zero;
        }

        private static TableConfig Empty()
        {
            var t = (TableConfig)RuntimeHelpers.GetUninitializedObject(typeof(TableConfig));
            t.segments = new List<CushionSegment>();
            t.pockets = new List<Pocket>();
            return t;
        }

        private static TableConfig Segs(params Vector2[] flat)
        {
            var t = Empty();
            for (int i = 0; i + 1 < flat.Length; i += 2)
                t.segments.Add(new CushionSegment { a = flat[i], b = flat[i + 1], rail = "synthetic" });
            return t;
        }

        private static TableConfig Synthetic(params Pocket[] pockets)
        {
            var t = Empty();
            t.pockets = new List<Pocket>(pockets);
            return t;
        }

        private static Pocket MakePocket(int index, Vector2 tc, Vector2 tn, float cr)
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
                openingRadius = cr + PoolConstants.BallRadius + 0.002f,
                captureRadius = cr,
            };
        }

        private static float ProbeCapture() => 0.0307f;
        private static float ProbeOpening() => ProbeCapture() + PoolConstants.BallRadius + 0.002f;

        private static PoolSimulator RadiusProbe(float rho)
        {
            Pocket pocket = MakePocket(0, Vector2.zero, new Vector2(0f, -1f), ProbeCapture());
            var sim = new PoolSimulator(Synthetic(pocket), P(1f, 1f, 0f));
            sim.LoadBalls(new[] { Ball(rho, 0.20f, 0f, -PoolConstants.MaxBallSpeed) });
            Step(sim, 30);
            return sim;
        }

        /// <summary>`Params(e,mu,a)`：PocketCaptureTests 口径（rolling slide=roll=a，transition 缺省 0）。</summary>
        private static PhysicsParams P(float e, float mu, float a) => new PhysicsParams
        {
            cushionRestitution = e,
            cushionTangentialFriction = mu,
            ballRestitution = 1f,
            ballTangentialFriction = 1f,
            rollingDecelerationSlide = a,
            rollingDecelerationRoll = a,
        };

        /// <summary>TuningMetrics 口径（四参数显式）。</summary>
        private static PhysicsParams PT(float e, float mu, float slide, float roll, float trans) => new PhysicsParams
        {
            cushionRestitution = e,
            cushionTangentialFriction = mu,
            ballRestitution = 1f,
            ballTangentialFriction = 1f,
            rollingDecelerationSlide = slide,
            rollingDecelerationRoll = roll,
            rollingTransitionSpeed = trans,
        };

        private static float FreeTravel(TableConfig table, PhysicsParams prm, float v0, out int steps)
        {
            var sim = new PoolSimulator(table, prm);
            sim.LoadBalls(new[] { Ball(0f, 0f, v0, 0f) });
            float travelled = 0f;
            Vector2 prev = Vector2.zero;
            steps = 0;
            const int cap = 20000;
            while (steps < cap && (sim.Balls[0].velocity.magnitude > PoolSimulator.RestSpeedThreshold
                                   || sim.ConsecutiveRestFrameCount < PoolSimulator.RestStableFrames))
            {
                sim.Step();
                Vector2 now = sim.Balls[0].position;
                travelled += (now - prev).magnitude;
                prev = now;
                steps++;
            }
            return travelled;
        }

        private static (float, float, float) ReadTuning(string projectRoot)
        {
            string p = Path.Combine(projectRoot, "Assets/_Project/Data/PhysicsTuning.asset");
            string t = File.ReadAllText(p);
            float G(string key) => float.Parse(Regex.Match(t, key + @":\s*([-0-9.eE+]+)").Groups[1].Value, Inv);
            return (G("rollingDecelerationSlide"), G("rollingDecelerationRoll"), G("rollingTransitionSpeed"));
        }

        /// <summary>git blob sha1（与 `git hash-object` 同口径 ⇒ 可直接与 `git ls-tree HEAD` 对表）。</summary>
        private static string GitBlobSha1(byte[] content)
        {
            byte[] header = Encoding.ASCII.GetBytes("blob " + content.Length + "\0");
            using var sha = SHA1.Create();
            sha.TransformBlock(header, 0, header.Length, null, 0);
            sha.TransformFinalBlock(content, 0, content.Length);
            return Hex(sha.Hash);
        }

        private static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2", Inv));
            return sb.ToString();
        }
    }
}
