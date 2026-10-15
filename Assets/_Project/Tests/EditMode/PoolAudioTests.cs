using System;
using System.Collections.Generic;
using System.IO;
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
            float v0 = PoolAudio.VolumeForSpeed(0f);
            float vFull = PoolAudio.VolumeForSpeed(PoolAudio.FullSpeedForVolume);
            float vHalf = PoolAudio.VolumeForSpeed(PoolAudio.FullSpeedForVolume * 0.5f);
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
    }
}
