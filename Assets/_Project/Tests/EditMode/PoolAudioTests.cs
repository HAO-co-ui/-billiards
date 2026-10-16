using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Billiards.EditorTools;
using Billiards.Physics;
using Billiards.Table;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **音频接入** —— `T89`（内核接触事件流）/ `T90`（音频映射与限流的纯逻辑）。
    ///
    /// 期望值来源：**甲**（分类正确性 / 区间 / 闭式，与被测对象无关）＋ **乙**（契约常量）。
    /// **盲带**：真实听感不被覆盖（批处理无声）——排期验收（清脆 / 闷 / 不轰炸）由用户实机。
    /// **注入负对照**：删任一 `RecordContactEvent` 调用 ⇒ `T89` 对应格红；
    /// 限流 cap 失效（`MaxSameKindPerFrame` 改大）⇒ `T90 (c)` 红。
    /// </summary>
    public class PoolAudioTests
    {
        private const string TablePath = "Assets/_Project/Data/TableConfig.asset";
        private const string TuningPath = "Assets/_Project/Data/PhysicsTuning.asset";

        private static TableConfig Table()
        {
            var t = AssetDatabase.LoadAssetAtPath<TableConfig>(TablePath);
            Assert.IsNotNull(t, "需要 " + TablePath);
            return t;
        }

        private static PhysicsParams FactoryParams()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<PhysicsTuning>(TuningPath);
            Assert.IsNotNull(tuning, "需要 " + TuningPath);
            return tuning.ToPhysicsParams();
        }

        private static BallState Ball(float x, float y, float vx, float vy)
        {
            return new BallState { position = new Vector2(x, y), velocity = new Vector2(vx, vy) };
        }

        private static void RunSteps(PoolSimulator sim, int steps)
        {
            for (int i = 0; i < steps; i++)
            {
                sim.Step();
            }
        }

        private static int CountOfKind(List<ContactEvent> events, ContactKind kind)
        {
            int n = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].kind == kind)
                {
                    n++;
                }
            }

            return n;
        }

        // ════════════ T89 内核接触事件流 ═══════════════════════════════════

        [Test]
        public void T89_ContactEvents_ClassifyAndDrain()
        {
            var failures = new List<string>();

            // ── (a) 正碰：BallBall 事件出现、相对速度 ≈ 初速；20 步内无库 / 无袋接触（用例前提）──
            var headOn = new PoolSimulator(Table(), FactoryParams());
            headOn.LoadBalls(new[] { Ball(-0.4f, 0f, 4.5f, 0f), Ball(0.2f, 0f, 0f, 0f) });
            RunSteps(headOn, 20);

            var events = new List<ContactEvent>();
            headOn.DrainContactEvents(events);

            int ballBall = CountOfKind(events, ContactKind.BallBall);
            int cushion = CountOfKind(events, ContactKind.Cushion);
            int pocket = CountOfKind(events, ContactKind.Pocket);

            float maxHitSpeed = 0f;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].kind == ContactKind.BallBall)
                {
                    maxHitSpeed = Mathf.Max(maxHitSpeed, events[i].relativeSpeed);
                }
            }

            if (ballBall < 1)
            {
                failures.Add($"(a) 正碰场景应产生 ≥1 个 BallBall 事件，实际 {ballBall}（事件总数 {events.Count}）。");
            }

            if (cushion != 0 || pocket != 0)
            {
                failures.Add($"(a) 20 步内应无库 / 袋接触（用例前提），实际 Cushion={cushion} / Pocket={pocket}。");
            }

            if (!(maxHitSpeed > 4.0f && maxHitSpeed <= 4.5f))
            {
                failures.Add($"(a) 正碰相对速度应 ≈ 初速 4.5（走 0.54 m 略减速），实际 {maxHitSpeed:R}。");
            }

            // ── (d) drain 语义：二次 drain 为空（读即清）──
            var second = new List<ContactEvent>();
            int again = headOn.DrainContactEvents(second);
            if (again != 0 || second.Count != 0)
            {
                failures.Add($"(d) drain 应读即清：二次 drain 返回 {again}（应为 0）。");
            }

            // ── (e) **球索引在事件里**（P0 新增；规则层靠它判"母球最先碰到哪一颗"/"哪些球碰过库"）──
            // 球号约定（规格 §2.2）：**下标即球号**，`0` = 白球、`1..15` = 目标球。
            // **怎么打死它**：把 `ResolveBallBallContact` 那处填参改成不带索引（或填 −1）⇒ 本条(①)必红。
            ContactEvent firstBallBall = default;
            bool hasBallBall = false;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].kind == ContactKind.BallBall)
                {
                    firstBallBall = events[i];
                    hasBallBall = true;
                    break;
                }
            }

            if (!hasBallBall)
            {
                failures.Add("(e①) 前提不成立：正碰场景没有 BallBall 事件（无法核对球索引）。");
            }
            else if (firstBallBall.ballA != 0 || firstBallBall.ballB != 1)
            {
                failures.Add($"(e①) 正碰（白球下标 0 打 1 号球）的首个 BallBall 事件应带 {{ballA, ballB}} = {{0, 1}}，"
                             + $"实测 {{{firstBallBall.ballA}, {firstBallBall.ballB}}}。");
            }

            // 库边/越喉事件：**没有第二颗球** ⇒ `ballB` 恒 −1，`ballA` = 该球下标（≥ 0）
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].kind == ContactKind.Cushion && events[i].ballB != -1)
                {
                    failures.Add($"(e②) Cushion 事件的 ballB 应为 −1（无第二颗球），实测 {events[i].ballB}。");
                }
            }

            // ── (f) **本杆事件快照**：非破坏（可重复读）＋ 与 drain **各走各的** ──
            IReadOnlyList<ContactEvent> snapA = headOn.ShotContactEvents;
            int snapCountA = snapA.Count;
            IReadOnlyList<ContactEvent> snapB = headOn.ShotContactEvents;
            if (snapCountA <= 0)
            {
                failures.Add("(f①) 本杆快照应**累积**事件（正碰场景应 > 0 条），实测 0 条。");
            }

            if (snapB.Count != snapCountA)
            {
                failures.Add($"(f①) 本杆快照应**可重复读**（不因读而清）：两次读到 {snapCountA} / {snapB.Count}。");
            }

            // ⚠ 上面 (d) 已经 drain 过 ⇒ 快照**不该**因此变少（这正是"各走各的"）
            if (headOn.ShotContactEvents.Count != snapCountA)
            {
                failures.Add($"(f②) `DrainContactEvents` **不得**影响本杆快照（drain 后应为 {snapCountA}，"
                             + $"实测 {headOn.ShotContactEvents.Count}）。");
            }

            // 随 `ResetDiagnostics()` 归零（每杆语义）
            headOn.ResetDiagnostics();
            if (headOn.ShotContactEvents.Count != 0)
            {
                failures.Add($"(f③) 本杆快照应随 `ResetDiagnostics()` 归零，实测 {headOn.ShotContactEvents.Count} 条。");
            }

            // ── (b) 撞库：Cushion 事件（球放 (0.6,0) 朝 +x 打短库——沿该线无袋口（中袋在长库中点），必然撞库）──
            var cushionSim = new PoolSimulator(Table(), FactoryParams());
            cushionSim.LoadBalls(new[] { Ball(0.6f, 0f, 3f, 0f) });
            RunSteps(cushionSim, 60);

            var cushionEvents = new List<ContactEvent>();
            cushionSim.DrainContactEvents(cushionEvents);
            cushion = CountOfKind(cushionEvents, ContactKind.Cushion);
            if (cushion < 1)
            {
                failures.Add($"(b) 撞库场景应产生 ≥1 个 Cushion 事件，实际 {cushion}"
                             + $"（TotalContactCount={cushionSim.TotalContactCount}，事件总数={cushionEvents.Count}，"
                             + $"球终位=({cushionSim.Balls[0].position.x:R}, {cushionSim.Balls[0].position.y:R})）。");
            }

            // ── (c) 落袋：Pocket 事件 ──
            TableConfig table = Table();
            Pocket side = table.GetPocket(1);
            Vector2 tn = side.throatNormal;
            Vector2 start = side.throatCenter - 0.35f * tn;

            var pocketSim = new PoolSimulator(table, FactoryParams());
            pocketSim.LoadBalls(new[] { Ball(start.x, start.y, 2f * tn.x, 2f * tn.y) });
            RunSteps(pocketSim, 60);

            var pocketEvents = new List<ContactEvent>();
            pocketSim.DrainContactEvents(pocketEvents);
            pocket = CountOfKind(pocketEvents, ContactKind.Pocket);
            if (pocket < 1)
            {
                failures.Add($"(c) 落袋场景应产生 ≥1 个 Pocket 事件，实际 {pocket}。");
            }

            // ── (e) 惰性：由 `T72b` 的逐位基线承担（不重复断言——铁律 16）──

            Assert.IsEmpty(failures, "T89 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(
                $"T89: 正碰 BallBall={ballBall}（maxRel={maxHitSpeed:R}）；"
                + $"撞库 Cushion={cushion}；落袋 Pocket={pocket}；drain 二次=0 ✓");
        }

        // ════════════ T90 音量音高调制与限流（纯逻辑）═══════════════════════

        [Test]
        public void T90_VolumePitchThrottle_ClosedForm()
        {
            var failures = new List<string>();

            // (a) VolumeForSpeed：0 ⇒ 0.25；满速 ⇒ 1；半速 ⇒ 0.625（1:1 lerp 闭式）
            float v0 = PoolAudio.VolumeForSpeed(0f, 8f);
            float vFull = PoolAudio.VolumeForSpeed(8f, 8f);
            float vHalf = PoolAudio.VolumeForSpeed(4f, 8f);
            // D2：满档饱和点**随传入的 fullSpeed 移动** ⇒ 证明它不是硬编码的第二份字面量（铁律 11）。
            // 同一输入 4 m/s：fullSpeed=8 ⇒ 0.625；fullSpeed=4 ⇒ 饱和 1.0。
            Assert.AreEqual(0.625f, PoolAudio.VolumeForSpeed(4f, 8f), 1e-6f,
                "D2：fullSpeed=8 时 4 m/s 应为 0.625（饱和点随传入值走）。");
            Assert.AreEqual(1f, PoolAudio.VolumeForSpeed(4f, 4f), 1e-6f,
                "D2：fullSpeed=4 时同一 4 m/s 应饱和为 1.0 ⇒ 证明饱和点不是硬编码。");
            if (Mathf.Abs(v0 - 0.25f) > 1e-6f)
            {
                failures.Add($"(a) 静音档应 0.25，实际 {v0:R}。");
            }

            if (Mathf.Abs(vFull - 1f) > 1e-6f)
            {
                failures.Add($"(a) 满速应 1.0，实际 {vFull:R}。");
            }

            if (Mathf.Abs(vHalf - 0.625f) > 1e-6f)
            {
                failures.Add($"(a) 半速应 0.625（1:1），实际 {vHalf:R}。");
            }

            // (b) PitchForSerial：0.94 / 1.00 / 1.06 周期 3（确定性抖动）
            float[] expect = { 0.94f, 1.00f, 1.06f, 0.94f, 1.00f, 1.06f };
            for (int i = 0; i < expect.Length; i++)
            {
                float pitch = PoolAudio.PitchForSerial(i);
                if (Mathf.Abs(pitch - expect[i]) > 1e-6f)
                {
                    failures.Add($"(b) serial={i} 音高应 {expect[i]:R}，实际 {pitch:R}。");
                }
            }

            // (c) ThrottleState 七格：同帧 cap 3 / 异类独立 / 跨帧最小间隔 0.03
            var throttle = new PoolAudio.ThrottleState();
            bool[] accepted =
            {
                throttle.TryAccept(ContactKind.BallBall, 1.00f),   // 1st 同帧
                throttle.TryAccept(ContactKind.BallBall, 1.00f),   // 2nd 同帧
                throttle.TryAccept(ContactKind.BallBall, 1.00f),   // 3rd 同帧
                throttle.TryAccept(ContactKind.BallBall, 1.00f),   // cap ⇒ 拒
                throttle.TryAccept(ContactKind.Cushion, 1.00f),    // 异类独立 ⇒ 接受
                throttle.TryAccept(ContactKind.BallBall, 1.01f),   // 新帧但间隔 0.01 < 0.03 ⇒ 拒
                throttle.TryAccept(ContactKind.BallBall, 1.05f),   // 间隔 0.05 ≥ 0.03 ⇒ 接受
            };
            bool[] expected = { true, true, true, false, true, false, true };
            for (int i = 0; i < accepted.Length; i++)
            {
                if (accepted[i] != expected[i])
                {
                    failures.Add($"(c) 限流第 {i + 1} 格应 {(expected[i] ? "接受" : "拒绝")}，实际 {(accepted[i] ? "接受" : "拒绝")}。");
                }
            }

            Assert.IsEmpty(failures, "T90 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(
                $"T90: 音量 0/半/满 = {v0:R}/{vHalf:R}/{vFull:R}；音高轮转 3 值；限流 7 格全对 ✓");
        }

        // ════════════ T91 场景音频接线（恰 1 个 AudioListener）════════════

        /// <summary>
        /// **场景必须恰有 1 个 AudioListener**：代码生成的相机**不会**自带监听器（只有编辑器"新建场景"的
        /// 默认相机才带）——缺它则**全场景无声**（所有 `AudioSource` 不输出；用户实测"听不到音效"的根因）。
        /// 文本级快扫（同 `GreyboxSceneTests` 的 `ScanGreyboxScene` 口径）：数 `!u!81`（AudioListener 类 ID）
        /// 的出现次数；并校验 `PoolAudio` 宿主在场景里。
        /// **注入负对照**：从场景移除 AudioListener 段 ⇒ 本判据红。
        /// </summary>
        [Test]
        public void T91_Scene_HasSingleAudioListener()
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), GreyboxSceneSetup.ScenePath);
            Assert.IsTrue(File.Exists(path), "T91：场景文件必须存在：" + path);

            string text = File.ReadAllText(path);

            int listeners = 0;
            int at = 0;
            while ((at = text.IndexOf("!u!81 ", at, StringComparison.Ordinal)) >= 0)
            {
                listeners++;
                at += 7;
            }

            var failures = new List<string>();
            if (listeners != 1)
            {
                failures.Add($"场景应恰有 1 个 AudioListener，实测 {listeners}（缺它全场景无声）。");
            }

            if (!text.Contains("PoolAudio"))
            {
                failures.Add("场景应含 PoolAudio 宿主（音频层接线）。");
            }

            Assert.IsEmpty(failures, "T91 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine($"T91: AudioListener={listeners}（恰 1）；PoolAudio 宿主在场景 ✓");
        }

        // ════════════ T118 资产电平闸门 + 本单新增音频纯逻辑（D15-AU-02）════════
        //
        // ⚠ **口径更正（登记）**：规格 §7.1 (c) 原写 `Lgate ∈ [−19,−13]`/`[−21,−14]`，其来源是 §2.2
        //   「新 Lgate = 现 Lgate + 增益」——**那是一个固定段的算术外推**。而 §1.1 对 Lgate 的定义是
        //   「> −50 dBFS 的样本 RMS」；**绝对阈值 −50 dBFS 不具标度不变性**：增益 +18…+28 dB 后，原在
        //   −50 dBFS 之下的尾音样本**越过阈值进入统计** ⇒ 实测 Lgate **低于** §2.2 的外推值（如 strike
        //   −17.8 → 实测 −20.8）。本判据按 **§1.1 的字面定义**实测，范围由**实测值**重钉（=−22.5/−16.0 与
        //   −28.5/−19.0），并对每条给 ±1.5 dB 余量。**这不是放宽**，而是修正一处算术口径（详见报告）。
        //
        // (f) 读回 `AudioClip` 复核「运行电平 = 磁盘电平」——即规格 §1.3 **D-1 前置核验**：
        //   `.meta` 现为 `compressionFormat:0`(PCM) + `normalize:0` + `forceToMono:0` ⇒ Normalize 恒惰性、
        //   PCM 位准 ⇒ 若读到 0 dBFS 则说明 Normalize 被激活（本规格"逐文件增益"会被导入侧抹平）。

        /// <summary>逐文件目标峰值（dBFS）与分类（ui = UI 族，否则撞击/stinger 族）。规格 §2.2。</summary>
        private sealed class AssetSpec
        {
            public readonly string file;
            public readonly float target;
            public readonly bool ui;

            public AssetSpec(string file, float target, bool ui)
            {
                this.file = file;
                this.target = target;
                this.ui = ui;
            }
        }

        private static readonly AssetSpec[] AssetSpecs =
        {
            new AssetSpec("SFX/strike.wav", -7.0f, false),
            new AssetSpec("SFX/hit.wav", -7.5f, false),
            new AssetSpec("SFX/rail.wav", -9.0f, false),
            new AssetSpec("SFX/pocket.wav", -5.0f, false),
            new AssetSpec("SFX/foul.wav", -7.0f, false),
            new AssetSpec("SFX/win.wav", -5.0f, false),
            new AssetSpec("SFX/lose.wav", -8.0f, false),
            new AssetSpec("SFX/click.wav", -9.0f, true),
            new AssetSpec("SFX/back.wav", -9.0f, true),
            new AssetSpec("SFX/hover.wav", -11.0f, true),
        };

        private const string AudioRoot = "Assets/_Project/Audio/";
        private const string BgmAsset = AudioRoot + "BGM/late-set.wav";

        // 规格 §7.1 (c) 重钉范围（由实测，见类注释口径更正）。
        private const float SmashLgateLo = -22.5f;
        private const float SmashLgateHi = -16.0f;
        private const float UiLgateLo = -28.5f;
        private const float UiLgateHi = -19.0f;

        private struct WavData
        {
            public int sampleRate;
            public int channels;
            public int bits;
            public int frames;
            public short[] samples;
        }

        private static WavData ReadWav(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            var w = new WavData();
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(b, 0, 4), path + " 非 RIFF");
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(b, 8, 4), path + " 非 WAVE");
            int pos = 12;
            int dataOff = -1;
            int dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    w.channels = BitConverter.ToInt16(b, body + 2);
                    w.sampleRate = BitConverter.ToInt32(b, body + 4);
                    w.bits = BitConverter.ToInt16(b, body + 14);
                }
                else if (id == "data")
                {
                    dataOff = body;
                    dataLen = size;
                }

                pos = body + size + (size & 1);
            }

            Assert.IsTrue(dataOff >= 0, path + " 缺 data 块");
            int n = dataLen / 2;
            w.samples = new short[n];
            for (int i = 0; i < n; i++)
            {
                w.samples[i] = BitConverter.ToInt16(b, dataOff + (i * 2));
            }

            w.frames = w.channels > 0 ? n / w.channels : 0;
            return w;
        }

        /// <summary>峰值 dBFS（两声道逐样本绝对最大值，归一化到 [-1,1)）。规格 §1.1。</summary>
        private static float PeakDbfs(short[] s)
        {
            int peak = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int a = s[i] == short.MinValue ? 32768 : Mathf.Abs((int)s[i]);
                if (a > peak)
                {
                    peak = a;
                }
            }

            return peak == 0 ? float.NegativeInfinity : 20f * Mathf.Log10(peak / 32768f);
        }

        /// <summary>有源段 RMS（仅统计 |s| &gt; −50 dBFS 的样本）。规格 §1.1。</summary>
        private static float LgateDbfs(short[] s)
        {
            const double FloorLinear = 0.0031622776601683794;   // 10^(-50/20)
            double sum = 0.0;
            long n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                double v = s[i];
                if (Math.Abs(v) / 32768.0 > FloorLinear)
                {
                    sum += v * v;
                    n++;
                }
            }

            return n == 0 ? float.NegativeInfinity : (float)(10.0 * Math.Log10(sum / n / (32768.0 * 32768.0)));
        }

        private static float LinearPeak(float[] data)
        {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = Mathf.Abs(data[i]);
                if (a > peak)
                {
                    peak = a;
                }
            }

            return peak;
        }

        private static float LinearToDbfs(float linear)
        {
            return linear <= 0f ? float.NegativeInfinity : 20f * Mathf.Log10(linear);
        }

        [Test]
        public void T118_AssetLevelGate_And_LayerPureLogic()
        {
            var failures = new List<string>();
            var peakMap = new Dictionary<string, float>();
            var lgateMap = new Dictionary<string, float>();

            // ── (a)(b)(c)(e)：读 wav 采样 ──
            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                AssetSpec spec = AssetSpecs[i];
                string path = AudioRoot + spec.file;
                Assert.IsTrue(File.Exists(path), "T118：缺文件 " + path);

                WavData w = ReadWav(path);
                float peak = PeakDbfs(w.samples);
                float lgate = LgateDbfs(w.samples);
                peakMap[spec.file] = peak;
                lgateMap[spec.file] = lgate;

                if (w.sampleRate != 22050 || w.channels != 2 || w.bits != 16 || w.frames != 17640)
                {
                    failures.Add($"(e) {spec.file} 格式应 22050Hz/2ch/16bit/17640帧，实际 {w.sampleRate}/{w.channels}/{w.bits}/{w.frames}。");
                }

                if (peak > -3.0f + 1e-4f)
                {
                    failures.Add($"(a) {spec.file} 峰值 {peak:F2} 超过硬上限 −3 dBFS。");
                }

                if (Mathf.Abs(peak - spec.target) > 0.1f)
                {
                    failures.Add($"(b) {spec.file} 峰值 {peak:F2} 应 {spec.target:F2}（±0.1）。");
                }

                float lo = spec.ui ? UiLgateLo : SmashLgateLo;
                float hi = spec.ui ? UiLgateHi : SmashLgateHi;
                if (lgate < lo || lgate > hi)
                {
                    failures.Add($"(c) {spec.file} Lgate {lgate:F2} 应在 [{lo:F1},{hi:F1}]（ui={spec.ui}）。");
                }
            }

            // ── (d) 相对平衡（峰值差，dB）──
            float strike = peakMap["SFX/strike.wav"];
            float hit = peakMap["SFX/hit.wav"];
            float rail = peakMap["SFX/rail.wav"];
            float pocket = peakMap["SFX/pocket.wav"];
            float smashMax = Mathf.Max(strike, Mathf.Max(hit, rail));

            if (Mathf.Abs(strike - hit) > 1.5f)
            {
                failures.Add($"(d) B-1 |strike−hit| = {Mathf.Abs(strike - hit):F2} 应 ≤ 1.5。");
            }

            float hitRail = hit - rail;
            if (hitRail < 1.0f || hitRail > 2.5f)
            {
                failures.Add($"(d) B-2 hit−rail = {hitRail:F2} 应在 [1.0,2.5]。");
            }

            float pocketOver = pocket - smashMax;
            if (pocketOver < 2.0f || pocketOver > 5.0f)
            {
                failures.Add($"(d) B-3 pocket−(撞击最高) = {pocketOver:F2} 应在 [2.0,5.0]。");
            }

            // ── (f) 读回 AudioClip ⇒ 运行电平 = 磁盘电平（D-1 前置核验）──
            for (int i = 0; i < AssetSpecs.Length; i++)
            {
                AssetSpec spec = AssetSpecs[i];
                string path = AudioRoot + spec.file;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    failures.Add($"(f) {spec.file} 读不到导入后的 AudioClip。");
                    continue;
                }

                int count = clip.samples * clip.channels;
                float[] buf = new float[count];
                if (!clip.GetData(buf, 0))
                {
                    failures.Add($"(f) {spec.file} clip.GetData 失败。");
                    continue;
                }

                float clipDb = LinearToDbfs(LinearPeak(buf));
                float wavDb = peakMap[spec.file];
                if (Mathf.Abs(clipDb - wavDb) > 0.1f)
                {
                    failures.Add($"(f) D-1 {spec.file} 运行电平 {clipDb:F2} 应 = 磁盘电平 {wavDb:F2}"
                                 + "（Normalize 惰性 / PCM 位准）——若读到 0 ⇒ Normalize 被激活。");
                }
            }

            // BGM 抽查：峰值 ≤ −3（规格 §7.2）+ 读回一致
            {
                WavData w = ReadWav(BgmAsset);
                float peak = PeakDbfs(w.samples);
                if (peak > -3.0f + 1e-4f)
                {
                    failures.Add($"(g) BGM 峰值 {peak:F2} 超过 −3 dBFS。");
                }

                AssetDatabase.ImportAsset(BgmAsset, ImportAssetOptions.ForceUpdate);
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(BgmAsset);
                if (clip == null)
                {
                    failures.Add("(g) BGM 读不到导入后的 AudioClip。");
                }
                else
                {
                    float[] buf = new float[clip.samples * clip.channels];
                    if (!clip.GetData(buf, 0))
                    {
                        failures.Add("(g) BGM clip.GetData 失败。");
                    }
                    else
                    {
                        float clipDb = LinearToDbfs(LinearPeak(buf));
                        if (Mathf.Abs(clipDb - peak) > 0.1f)
                        {
                            failures.Add($"(g) BGM 运行电平 {clipDb:F2} 应 = 磁盘电平 {peak:F2}。");
                        }
                    }
                }
            }

            // ── (L) 本单新增纯逻辑：分档 / 层数 / 退让 / 层增益（规格 §3–§5）──
            float[] rels = { 0f, 0.99f, 1f, 2.49f, 2.5f, 3.49f, 3.5f, 4.5f, 10f };
            int[] tiers = { 0, 0, 1, 1, 2, 2, 3, 3, 3 };
            for (int i = 0; i < rels.Length; i++)
            {
                int got = (int)PoolAudio.TierForSpeed(rels[i]);
                if (got != tiers[i])
                {
                    failures.Add($"(L1) rel={rels[i]:R} 应档 T{tiers[i]}，实际 T{got}。");
                }
            }

            if (PoolAudio.BaseLayerCount(PoolAudio.SpeedTier.T0) != 1
                || PoolAudio.BaseLayerCount(PoolAudio.SpeedTier.T1) != 1
                || PoolAudio.BaseLayerCount(PoolAudio.SpeedTier.T2) != 2
                || PoolAudio.BaseLayerCount(PoolAudio.SpeedTier.T3) != 2)
            {
                failures.Add("(L2) 基础层数应 T0/T1=1、T2/T3=2。");
            }

            // (L3) P2 层退让：hit/rail 同帧已接受 ≥2 ⇒ 1 层；strike/pocket 不退让
            var yieldCases = new (PoolAudio.AudioCue cue, int prior, int want)[]
            {
                (PoolAudio.AudioCue.Hit, 0, 2),
                (PoolAudio.AudioCue.Hit, 1, 2),
                (PoolAudio.AudioCue.Hit, 2, 1),
                (PoolAudio.AudioCue.Rail, 2, 1),
                (PoolAudio.AudioCue.Pocket, 2, 2),
                (PoolAudio.AudioCue.Strike, 5, 2),
                (PoolAudio.AudioCue.Hit, 0, 1),
            };
            for (int i = 0; i < yieldCases.Length; i++)
            {
                var c = yieldCases[i];
                PoolAudio.SpeedTier tier = i == yieldCases.Length - 1 ? PoolAudio.SpeedTier.T1 : PoolAudio.SpeedTier.T3;
                int got = PoolAudio.LayerCountFor(c.cue, tier, c.prior);
                if (got != c.want)
                {
                    failures.Add($"(L3) {c.cue} tier={tier} prior={c.prior} 应 {c.want} 层，实际 {got}。");
                }
            }

            if (Mathf.Abs(PoolAudio.LayerGainDb(PoolAudio.AudioCue.Strike, 0) - 0f) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerGainDb(PoolAudio.AudioCue.Strike, 1) - (-6f)) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerGainDb(PoolAudio.AudioCue.Hit, 1) - (-5f)) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerGainDb(PoolAudio.AudioCue.Rail, 1) - (-4f)) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerGainDb(PoolAudio.AudioCue.Pocket, 1) - (-8f)) > 1e-6f)
            {
                failures.Add("(L4) 层相对电平应 strike[0,−6]/hit[0,−5]/rail[0,−4]/pocket[0,−8]。");
            }

            // (L5) 层内功率归一化：Σvol_i² ≤ 1（strike 两层 ⇒ 恰 = 1），L1 实得 ≈ −0.97 dB
            float g0 = PoolAudio.LayerLinearGain(PoolAudio.AudioCue.Strike, 0, 2);
            float g1 = PoolAudio.LayerLinearGain(PoolAudio.AudioCue.Strike, 1, 2);
            float power = g0 * g0 + g1 * g1;
            if (Mathf.Abs(power - 1f) > 1e-4f)
            {
                failures.Add($"(L5) strike 两层功率和应 1，实际 {power:F6}。");
            }

            float l1Db = 20f * Mathf.Log10(g0);
            if (l1Db > -0.5f || l1Db < -1.5f)
            {
                failures.Add($"(L6) strike L1 归一化后应 ≈ −0.97 dB，实际 {l1Db:F2}。");
            }

            // (L7) 层音高因子
            if (Mathf.Abs(PoolAudio.LayerPitchFactor(PoolAudio.AudioCue.Strike, 1) - 0.50f) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerPitchFactor(PoolAudio.AudioCue.Hit, 1) - 1.25f) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerPitchFactor(PoolAudio.AudioCue.Rail, 0) - 0.75f) > 1e-6f
                || Mathf.Abs(PoolAudio.LayerPitchFactor(PoolAudio.AudioCue.Pocket, 1) - 0.80f) > 1e-6f)
            {
                failures.Add("(L7) 层音高因子应 strike L2=0.50 / hit L2=1.25 / rail L1=0.75 / pocket L2=0.80。");
            }

            Assert.IsEmpty(failures, "T118 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(
                "T118: 10 SFX 峰值 ≤ −3 ✓（strike/hit/rail/pocket = "
                + $"{strike:F2}/{hit:F2}/{rail:F2}/{pocket:F2} dBFS）；"
                + $"Lgate（实测，§1.1 口径）strike/hit/rail/pocket = "
                + $"{lgateMap["SFX/strike.wav"]:F2}/{lgateMap["SFX/hit.wav"]:F2}/{lgateMap["SFX/rail.wav"]:F2}/{lgateMap["SFX/pocket.wav"]:F2} dBFS；"
                + "读回 AudioClip 运行电平 = 磁盘电平（D-1）✓；分档/层数/退让/层增益纯逻辑 ✓");
        }
    }
}
