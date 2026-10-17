using System.Collections.Generic;
using Billiards.Core;
using Billiards.Physics;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **击球流程状态机与杆尖位置** —— `T87` / `T88`。
    ///
    /// 期望值来源：**甲**（排期转移表的逐格语义与杆尖闭式，均与被测对象无关）。
    ///
    /// 排期验收「连续击球 50 次无卡死、无重复出杆、无状态机死锁」的**可判定形式**：
    /// 转移表全格 + 全链闭环 + 「非法事件忽略」格（其中 **Aim 态的 released 被拒绝** = 无重复出杆的集中守卫；
    /// **Moving 态的 started 被拒绝** = 球动中不可蓄力，与拉杆侧的 TableAtRest 门控互为双保险）。
    ///
    /// **注入负对照**：
    /// · `T87`：删掉 `chargeReleased` 的 Charging 守卫 ⇒ 「Aim + released ⇒ Aim」格红；
    /// · `T88`：`StickTipPosition` 漏掉 pullback 项 ⇒ (b)(c) 红（1:1 被打死）。
    /// </summary>
    public class ShotFlowTests
    {
        private static ShotPhase Step(ShotFlowState s, bool started, bool cancelled, bool released, bool strike, bool atRest)
        {
            return s.Advance(started, cancelled, released, strike, atRest);
        }

        // ════════════ T87 击球流程状态机转移表 ═══════════════════════════

        [Test]
        public void T87_ShotFlow_TransitionTable()
        {
            var failures = new List<string>();

            // ── 合法转移（每格全新状态机，互不污染）────────────────────
            Check(failures, "Aim + chargeStarted ⇒ Charging",
                Step(new ShotFlowState(), true, false, false, false, false), ShotPhase.Charging);

            var charging = new ShotFlowState();
            charging.Advance(true, false, false, false, false);
            Check(failures, "Charging + chargeCancelled ⇒ Aim",
                Step(charging, false, true, false, false, false), ShotPhase.Aim);

            charging = new ShotFlowState();
            charging.Advance(true, false, false, false, false);
            Check(failures, "Charging + chargeReleased ⇒ Shooting",
                Step(charging, false, false, true, false, false), ShotPhase.Shooting);

            var shooting = new ShotFlowState();
            shooting.Advance(true, false, false, false, false);
            shooting.Advance(false, false, true, false, false);
            Check(failures, "Shooting + strikeArrived ⇒ Moving",
                Step(shooting, false, false, false, true, false), ShotPhase.Moving);

            var moving = new ShotFlowState();
            moving.Advance(true, false, false, false, false);
            moving.Advance(false, false, true, false, false);
            moving.Advance(false, false, false, true, false);
            Check(failures, "Moving + tableAtRest ⇒ Settle",
                Step(moving, false, false, false, false, true), ShotPhase.Settle);

            Check(failures, "Settle + tableAtRest ⇒ Aim",
                Step(moving, false, false, false, false, true), ShotPhase.Aim);

            // ── 非法事件一律忽略（保持原态）＝ 无死锁 / 无重复出杆的守卫 ──
            Check(failures, "Aim + chargeReleased ⇒ 保持 Aim（无重复出杆：只有蓄力中的松手才出杆）",
                Step(new ShotFlowState(), false, false, true, false, false), ShotPhase.Aim);

            var charging2 = new ShotFlowState();
            charging2.Advance(true, false, false, false, false);
            Check(failures, "Charging + strikeArrived ⇒ 保持 Charging（前推事件只在出杆阶段有效）",
                Step(charging2, false, false, false, true, false), ShotPhase.Charging);

            var shooting2 = new ShotFlowState();
            shooting2.Advance(true, false, false, false, false);
            shooting2.Advance(false, false, true, false, false);
            Check(failures, "Shooting + chargeStarted ⇒ 保持 Shooting（前推中按键无效）",
                Step(shooting2, true, false, false, false, false), ShotPhase.Shooting);

            var moving2 = new ShotFlowState();
            moving2.Advance(true, false, false, false, false);
            moving2.Advance(false, false, true, false, false);
            moving2.Advance(false, false, false, true, false);
            Check(failures, "Moving + chargeStarted ⇒ 保持 Moving（球动中不能蓄力——与拉杆门控互为双保险）",
                Step(moving2, true, false, false, false, false), ShotPhase.Moving);

            // ── 全链闭环（排期验收「无状态机死锁」）──────────────────
            var full = new ShotFlowState();
            full.Advance(true, false, false, false, false);   // Aim → Charging
            full.Advance(false, false, true, false, false);   // Charging → Shooting
            full.Advance(false, false, false, true, false);   // Shooting → Moving（击球帧）
            full.Advance(false, false, false, false, true);  // Moving → Settle
            Check(failures, "全链第 4 步 ⇒ Settle", full.Phase, ShotPhase.Settle);
            full.Advance(false, false, false, false, true);   // Settle → Aim
            Check(failures, "全链第 5 步 ⇒ 回 Aim（闭环不死锁）", full.Phase, ShotPhase.Aim);

            // ── 出口语义 ─────────────────────────────────────────────
            var fresh = new ShotFlowState();
            if (!fresh.CanBeginCharge)
            {
                failures.Add("出口：初始态 CanBeginCharge 应为 true。");
            }

            if (!fresh.IsStickVisible)
            {
                failures.Add("出口：初始态 IsStickVisible 应为 true。");
            }

            if (moving2.CanBeginCharge)
            {
                failures.Add("出口：Moving 态 CanBeginCharge 应为 false。");
            }

            if (moving2.IsStickVisible)
            {
                failures.Add("出口：Moving 态 IsStickVisible 应为 false。");
            }

            Assert.IsEmpty(failures, "T87 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine("T87: 合法转移 6 格 + 非法忽略 5 格 + 全链闭环 + 出口语义 ✓");
        }

        private static void Check(List<string> failures, string label, ShotPhase actual, ShotPhase expected)
        {
            if (actual != expected)
            {
                failures.Add($"{label}（期望 {expected}，实际 {actual}）");
            }
        }

        // ════════════ T88 杆尖位置（贴球间隙 + 后拉 1:1 线性）═════════════

        /// <summary>
        /// 杆尖沿**瞄准反方向**后拉 `TipGap + pullback`，1:1 线性。
        ///
        /// **口径**：本判据用**抬升角 = 0**（`StickTipPosition` 的默认实参）⇒ 退化为"水平后拉"，
        /// 是生产路径在"杆身不出台面、抬升角取最小值"时的极限形态；**生产路径的实际后拉方向是
        /// 抬升后的杆身轴线**（`SyncStick` 传入 `elevation`）——"杆尖必须落在过球心的轴线上"这条
        /// 由 `T104 (e) ⑤` 钉住（它才是"蓄力不得穿模"的结构性前提）。
        /// </summary>
        [Test]
        public void T88_StickTipPosition_PullbackOneToOne()
        {
            var cue = new Vector3(0.3f, 0.028575f, -0.2f);
            Vector3 aimDir = new Vector3(-0.6f, 0f, -0.8f).normalized;
            var failures = new List<string>();

            // (a) 贴球：pullback = 0 ⇒ 杆尖到白球距离恰为 TipGap，且在瞄准**反**方向一侧
            Vector3 tip0 = CueStickController.StickTipPosition(cue, aimDir, 0f);
            float gap0 = Vector3.Distance(tip0, cue);
            if (Mathf.Abs(gap0 - (PoolConstants.BallRadius + CueStickController.StickTipGapMeters)) > 1e-6f)
            {
                failures.Add($"(a) 静止时杆尖到球心应为 R+StickTipGapMeters（{PoolConstants.BallRadius + CueStickController.StickTipGapMeters:R}），实际 {gap0:R}。");
            }

            if (Vector3.Dot(tip0 - cue, aimDir) >= 0f)
            {
                failures.Add("(a) 杆尖应在瞄准反方向一侧（杆在白球后面）。");
            }

            // (b) 后拉 1:1：pullback = 0.075 ⇒ 距离恰为 R + StickTipGapMeters + 0.075（闭式，与被测实现无关）
            const float pullback = 0.075f;
            Vector3 tip1 = CueStickController.StickTipPosition(cue, aimDir, pullback);
            float gap1 = Vector3.Distance(tip1, cue);
            if (Mathf.Abs(gap1 - (PoolConstants.BallRadius + CueStickController.StickTipGapMeters + pullback)) > 1e-6f)
            {
                failures.Add($"(b) 后拉 1:1：距离应为 {PoolConstants.BallRadius + CueStickController.StickTipGapMeters + pullback:R}，实际 {gap1:R}。");
            }

            // (c) 线性：两档后拉的位移差恰等于后拉差（无惯性 / 无平方项）
            Vector3 tip2 = CueStickController.StickTipPosition(cue, aimDir, pullback * 2f);
            float delta = Vector3.Distance(tip2, tip1);
            if (Mathf.Abs(delta - pullback) > 1e-6f)
            {
                failures.Add($"(c) 线性 1:1：两档位移差应恰为 {pullback:R}，实际 {delta:R}。");
            }

            Assert.IsEmpty(failures, "T88 失败：\n" + string.Join("\n", failures));
            TestContext.WriteLine(
                $"T88: 贴球间隙 {gap0:R} == TipGap；后拉 {pullback:R} ⇒ {gap1:R}（1:1）；线性差分 {delta:R} == {pullback:R} ✓");
        }
    }
}
