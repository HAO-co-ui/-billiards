using System.Collections.Generic;
using Billiards.Physics;
using Billiards.Rules;
using NUnit.Framework;
using UnityEngine;

namespace Billiards.Tests
{
    /// <summary>
    /// **8 球规则层（35-C P3）**：`RuleFacts` 提取器 + `Judge` 的开球/常规两分支。
    ///
    /// **期望值来源**：规则原文（`Docs/35-C_AI对战与8球规则_spec.md` §3.1/§3.2/§3.3）＋ 纯函数契约（甲）；
    /// 不依赖仿真（事实与状态**手工构造** ⇒ 判据纯、快、可复现）。
    /// **盲带**：不判"施加"（切回合/发自由球/记分由 P5 宿主做）。
    /// </summary>
    public class EightBallRulesTests
    {
        private static RuleFacts Facts(int firstContact, int[] cushion, int[] potted, int[] offTable, int eightPocket = -1)
        {
            return new RuleFacts
            {
                firstContactBall = firstContact,
                cushionTouchedByBall = new List<int>(cushion ?? new int[0]),
                pottedBalls = new List<int>(potted ?? new int[0]),
                offTableBalls = new List<int>(offTable ?? new int[0]),
                cueBallPottedOrOff = System.Array.IndexOf(potted ?? new int[0], 0) >= 0
                                     || System.Array.IndexOf(offTable ?? new int[0], 0) >= 0,
                eightBallPocket = eightPocket,
            };
        }

        private static RuleState State(bool isBreak, GroupState group = GroupState.OpenTable, ShotTurn turn = ShotTurn.Player,
            int solidRemaining = 7, int stripeRemaining = 7, int calledPocket = -1)
        {
            return new RuleState
            {
                isBreak = isBreak,
                groupState = group,
                turn = turn,
                solidRemaining = solidRemaining,
                stripeRemaining = stripeRemaining,
                calledPocket = calledPocket,
            };
        }

        /// <summary>
        /// **`T142`：8 球规则层**（提取器事实 + 开球分支 + 常规分支 + D3 指定袋）。
        ///
        /// **怎么打死它**：把"≥4 颗碰库"改成 1 ⇒ (b①) 红；把"进非指定袋判负"去掉 ⇒ (c⑤)(c⑥) 红；
        /// 把"开球进黑 8 不算赢"改成算赢 ⇒ (b③) 红。
        /// </summary>
        [Test]
        public void T142_EightBallRules_FactsAndVerdicts()
        {
            var failures = new List<string>();

            // ══ P3-a：facts 提取器（单点实现）══════════════════════════════════════════
            // 一杆"白球→1 号球→进 4 号球"：事件流 = BallBall(0,1) 后 Cushion(4) 后 Pocket(4)
            var events = new List<ContactEvent>
            {
                new ContactEvent { kind = ContactKind.BallBall, ballA = 0, ballB = 1, position = Vector2.zero },
                new ContactEvent { kind = ContactKind.Cushion, ballA = 4, ballB = -1, position = Vector2.zero },
                new ContactEvent { kind = ContactKind.Cushion, ballA = 4, ballB = -1, position = Vector2.zero },   // 同一颗再碰一次
                new ContactEvent { kind = ContactKind.Pocket, ballA = 4, ballB = -1, position = Vector2.zero },
            };
            var beforeInPlay = new List<bool> { true, true, true, true, true };
            var afterInPlay = new List<bool> { true, true, true, true, false };
            var beforePocket = new List<int> { -1, -1, -1, -1, -1 };
            var afterPocket = new List<int> { -1, -1, -1, -1, 2 };

            RuleFacts a = EightBallRules.ExtractFacts(beforePocket, afterPocket, beforeInPlay, afterInPlay, events);
            if (a.firstContactBall != 1)
            {
                failures.Add($"(a①) 首碰球号应为 1，实测 {a.firstContactBall}。");
            }

            if (a.pottedBalls.Count != 1 || a.pottedBalls[0] != 4)
            {
                failures.Add($"(a①) `pottedBalls` 应为 {{4}}，实测 {{{string.Join(",", a.pottedBalls)}}}。");
            }

            if (a.cushionTouchedByBall.Count != 1 || a.cushionTouchedByBall[0] != 4)
            {
                failures.Add($"(a②) **同一颗球多次碰库只算 1 颗**：应为 {{4}}，实测 {{{string.Join(",", a.cushionTouchedByBall)}}}。");
            }

            // (a③) **落袋 vs 非入袋离台**必须分开：5 号越界冻结（pocketIndex == -1）
            var before2 = new List<bool> { true, true, true, true, true, true };
            var after2 = new List<bool> { true, true, true, true, true, false };
            var beforeP2 = new List<int> { -1, -1, -1, -1, -1, -1 };
            var afterP2 = new List<int> { -1, -1, -1, -1, -1, -1 };     // 冻结但**非入袋**
            RuleFacts a2 = EightBallRules.ExtractFacts(beforeP2, afterP2, before2, after2, new List<ContactEvent>());
            if (a2.pottedBalls.Count != 0)
            {
                failures.Add($"(a③) 非入袋离台**不得**进 `pottedBalls`，实测 {{{string.Join(",", a2.pottedBalls)}}}。");
            }

            if (a2.offTableBalls.Count != 1 || a2.offTableBalls[0] != 5)
            {
                failures.Add($"(a③) 5 号应进 `offTableBalls`，实测 {{{string.Join(",", a2.offTableBalls)}}}。");
            }

            // (a④) 白球落袋 ⇒ `cueBallPottedOrOff`
            var after3 = new List<bool> { false, true };
            var afterP3 = new List<int> { 1, -1 };
            RuleFacts a3 = EightBallRules.ExtractFacts(new List<int> { -1, -1 }, afterP3,
                new List<bool> { true, true }, after3, new List<ContactEvent>());
            if (!a3.cueBallPottedOrOff)
            {
                failures.Add("(a④) 白球落袋应使 `cueBallPottedOrOff == true`。");
            }

            // ══ P3-b：开球分支 ═══════════════════════════════════════════════════════
            // (b①) **3 颗碰库 ⇒ 无效开球 ⇒ 重摆**
            RuleVerdict b1 = EightBallRules.Judge(State(isBreak: true), Facts(-1, new[] { 1, 2, 3 }, null, null));
            if (!b1.reRack)
            {
                failures.Add("(b①) 只 3 颗碰库 ⇒ **无效开球、必须重摆**（`reRack = true`）。");
            }

            if (b1.nextTurn != ShotTurn.Player)
            {
                failures.Add("(b①) 无效开球重摆后**开球方不变**（`nextTurn` 应为 Player）。");
            }

            // (b②) **4 颗碰库 ⇒ 有效**（不重摆）
            RuleVerdict b2 = EightBallRules.Judge(State(isBreak: true), Facts(-1, new[] { 1, 2, 3, 4 }, null, null));
            if (b2.reRack)
            {
                failures.Add("(b②) 4 颗碰库 ⇒ **有效开球**（不得重摆）。");
            }

            if (b2.reassignGroup)
            {
                failures.Add("(b②) **开球进球/碰库都不定组**（`reassignGroup` 必须为 false）。");
            }

            // (b③) **开球进黑 8 ⇒ 拿回、不算赢、继续**
            RuleVerdict b3 = EightBallRules.Judge(State(isBreak: true), Facts(1, new[] { 1 }, new[] { 8 }, null, eightPocket: 0));
            if (b3.gameOver)
            {
                failures.Add("(b③) **开球进黑 8 不算赢**（`gameOver` 必须为 false）。");
            }

            // (b④) **开球白球落袋 ⇒ kitchen + mustShootForward**
            RuleVerdict b4 = EightBallRules.Judge(State(isBreak: true), Facts(1, new[] { 1 }, new[] { 1, 0 }, null));
            if (!b4.foul || b4.ballInHand != BallInHandKind.Kitchen || !b4.mustShootForward)
            {
                failures.Add($"(b④) 开球白球落袋 ⇒ **kitchen 自由球 + mustShootForward**，实测 foul={b4.foul} "
                             + $"ballInHand={b4.ballInHand} mustShootForward={b4.mustShootForward}。");
            }

            if (b4.nextTurn != ShotTurn.Ai)
            {
                failures.Add("(b④) 开球犯规后应**换手**给对手。");
            }

            // ══ P3-c：常规分支 ═══════════════════════════════════════════════════════
            RuleState solid = State(isBreak: false, group: GroupState.Solid);

            // (c①) 首碰打错组（实色组却先碰 9 号）⇒ 犯规 + 全场自由球
            RuleVerdict c1 = EightBallRules.Judge(solid, Facts(9, new[] { 9 }, null, null));
            if (!c1.foul || c1.ballInHand != BallInHandKind.FullTable)
            {
                failures.Add($"(c①) 首碰对方球 ⇒ **犯规 + 全场自由球**，实测 foul={c1.foul} ballInHand={c1.ballInHand}。");
            }

            if (c1.nextTurn != ShotTurn.Ai)
            {
                failures.Add("(c①) 犯规后应换手。");
            }

            // (c②) 碰球后无球碰库、也无球进袋 ⇒ 犯规
            RuleVerdict c2 = EightBallRules.Judge(solid, Facts(3, null, null, null));
            if (!c2.foul)
            {
                failures.Add("(c②) 碰球后**无球碰库且无球进袋** ⇒ 必须犯规（碰库原则）。");
            }

            // (c③) **未清完就打进黑 8 ⇒ 判负**（对手胜）
            RuleVerdict c3 = EightBallRules.Judge(solid, Facts(3, new[] { 3 }, new[] { 8 }, null, eightPocket: 0));
            if (!c3.gameOver || c3.winner != ShotTurn.Ai)
            {
                failures.Add($"(c③) **未清完打黑 8 ⇒ 判负**（winner 应为 Ai），实测 gameOver={c3.gameOver} winner={c3.winner}。");
            }

            // (c④) **清完 + 合法 + 指定袋 ⇒ 胜**
            RuleState cleared = State(isBreak: false, group: GroupState.Solid, solidRemaining: 0, calledPocket: 0);
            RuleVerdict c4 = EightBallRules.Judge(cleared, Facts(8, new[] { 8 }, new[] { 8 }, null, eightPocket: 0));
            if (!c4.gameOver || c4.winner != ShotTurn.Player || c4.foul)
            {
                failures.Add($"(c④) 清完 + 合法进黑 8（指定 0 号袋）⇒ **本局胜**，实测 gameOver={c4.gameOver} "
                             + $"winner={c4.winner} foul={c4.foul}。");
            }

            // (c⑤) **黑 8 进非指定袋 ⇒ 判负**
            RuleVerdict c5 = EightBallRules.Judge(cleared, Facts(8, new[] { 8 }, new[] { 8 }, null, eightPocket: 3));
            if (!c5.gameOver || c5.winner != ShotTurn.Ai)
            {
                failures.Add($"(c⑤) **黑 8 进非指定袋（指定 0、实进 3）⇒ 判负**，实测 winner={c5.winner}。");
            }

            // (c⑥) **未指定（-1）却进黑 8 ⇒ 也必须判负（不许静默放过）**
            RuleState noCall = State(isBreak: false, group: GroupState.Solid, solidRemaining: 0, calledPocket: -1);
            RuleVerdict c6 = EightBallRules.Judge(noCall, Facts(8, new[] { 8 }, new[] { 8 }, null, eightPocket: 2));
            if (!c6.gameOver || c6.winner != ShotTurn.Ai)
            {
                failures.Add($"(c⑥) **未指定袋却进黑 8 ⇒ 判负并留痕**，实测 winner={c6.winner}。");
            }

            // (c⑦) **对照（由 ④⑤ 两条规则推出，不是"我以为应该不同"）**：
            //   ④ 的期望 = 本队胜（指定袋**一致** ⇒ 合法取胜）；⑤ 的期望 = **对手**胜（进非指定袋 ⇒ 判负）
            //   ⇒ **两者必然给出不同胜者** ⇒ 相等才是异常（D3 未生效）。
            //   ⚠ 本条第一版把判据写反了（ 时才报错）⇒ **恒红**（这正是上一轮那条残留失败）。
            if (c4.winner == c5.winner)
            {
                failures.Add($"(c⑦) ④ 与 ⑤ 应给出**不同**胜者（指定袋一致 ⇒ 本队胜；进非指定袋 ⇒ 对手胜），"
                             + $"实测两次同为 {c4.winner} —— 说明 D3 判负未生效。");
            }

            // ══ 两分支共用：**黑 8 离台 ⇒ 直接判负** ═══════════════════════════════════
            RuleVerdict d1 = EightBallRules.Judge(solid, Facts(3, new[] { 3 }, null, new[] { 8 }));
            if (!d1.gameOver || d1.winner != ShotTurn.Ai)
            {
                failures.Add($"(d①) **黑 8 离台 ⇒ 本局负**，实测 gameOver={d1.gameOver} winner={d1.winner}。");
            }

            Assert.IsEmpty(failures, "T142 失败（**收集全部**，不在首个失败处中止）：\n" + string.Join("\n", failures));
        }
    }
}
