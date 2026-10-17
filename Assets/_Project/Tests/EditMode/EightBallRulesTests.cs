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
        private static RuleFacts Facts(int firstContact, int[] cushion, int[] potted, int[] offTable)
        {
            return new RuleFacts
            {
                firstContactBall = firstContact,
                cushionTouchedByBall = new List<int>(cushion ?? new int[0]),
                pottedBalls = new List<int>(potted ?? new int[0]),
                offTableBalls = new List<int>(offTable ?? new int[0]),
                cueBallPottedOrOff = System.Array.IndexOf(potted ?? new int[0], 0) >= 0
                                     || System.Array.IndexOf(offTable ?? new int[0], 0) >= 0,
            };
        }

        private static RuleState State(bool isBreak, GroupState group = GroupState.OpenTable, ShotTurn turn = ShotTurn.Player,
            int solidRemaining = 7, int stripeRemaining = 7)
        {
            return new RuleState
            {
                isBreak = isBreak,
                groupState = group,
                turn = turn,
                solidRemaining = solidRemaining,
                stripeRemaining = stripeRemaining,
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
            RuleVerdict b3 = EightBallRules.Judge(State(isBreak: true), Facts(1, new[] { 1 }, new[] { 8 }, null));
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
            RuleVerdict c3 = EightBallRules.Judge(solid, Facts(3, new[] { 3 }, new[] { 8 }, null));
            if (!c3.gameOver || c3.winner != ShotTurn.Ai)
            {
                failures.Add($"(c③) **未清完打黑 8 ⇒ 判负**（winner 应为 Ai），实测 gameOver={c3.gameOver} winner={c3.winner}。");
            }

            // (c④) **清完 + 合法 ⇒ 胜**（P8 起 D3 取消：黑 8 进**任意**袋都有效）
            RuleState cleared = State(isBreak: false, group: GroupState.Solid, solidRemaining: 0);
            RuleVerdict c4 = EightBallRules.Judge(cleared, Facts(8, new[] { 8 }, new[] { 8 }, null));
            if (!c4.gameOver || c4.winner != ShotTurn.Player || c4.foul)
            {
                failures.Add($"(c④) 清完 + 合法进黑 8 ⇒ **本局胜**，实测 gameOver={c4.gameOver} "
                             + $"winner={c4.winner} foul={c4.foul}。");
            }

            // (c⑤) **结局枚举里不许再有"进错袋"**（P8：D3 已整块摘除）——
            //   这是**结构级反向守卫**：谁把"指定袋 / 进错袋"偷偷加回来，这里立刻红。
            //   ⚠ 不能写成"换一个袋号 ⇒ 结论相同"：`RuleFacts` 里**已经没有"黑 8 进哪个袋"这个事实**了
            //     （它本来就是 D3 的输入）⇒ 那种断言在现在的结构下是**恒真**的（真空绿）。
            var expectedOutcomes = new[]
            {
                nameof(ShotOutcomeKind.Ongoing),
                nameof(ShotOutcomeKind.LegalEightBallPotted),
                nameof(ShotOutcomeKind.EightBallPottedTooEarly),
                nameof(ShotOutcomeKind.EightBallOffTable),
                nameof(ShotOutcomeKind.EightBallRespot),
            };
            var actualOutcomes = new List<string>();
            foreach (ShotOutcomeKind kind in System.Enum.GetValues(typeof(ShotOutcomeKind)))
            {
                actualOutcomes.Add(kind.ToString());
            }

            if (string.Join(",", actualOutcomes) != string.Join(",", expectedOutcomes))
            {
                failures.Add($"(c⑤) `ShotOutcomeKind` 应恰为 [{string.Join(", ", expectedOutcomes)}]，"
                             + $"实测 [{string.Join(", ", actualOutcomes)}] —— 多出来的多半是 `EightBallWrongPocket`"
                             + "（D3 已按用户要求取消：黑 8 进任意袋都算）。");
            }

            // (c⑥) **清完 + 犯规（白球落袋）却进黑 8 ⇒ 判负**（与 c④ 形成正反两面）
            RuleVerdict c6 = EightBallRules.Judge(cleared, Facts(8, new[] { 8 }, new[] { 8, 0 }, null));
            if (!c6.gameOver || c6.winner != ShotTurn.Ai || !c6.foul)
            {
                failures.Add($"(c⑥) 清完但**本杆犯规**（白球落袋）却进黑 8 ⇒ 应判负，实测 "
                             + $"gameOver={c6.gameOver} winner={c6.winner} foul={c6.foul}。");
            }

            // (c⑦) **已随 D3 一起删除**（P8）：它原来断言"指定袋一致 ⇒ 本队胜 / 进非指定袋 ⇒ 对手胜"两向不同，
            //   而 D3 取消后"袋号"不再是判定输入 ⇒ 那条对照**没有意义**（也不该留着误导后人）。
            //   现在"黑 8 的袋"这件事由 (c⑤) 的**枚举级守卫**兜住（不许再有 `EightBallWrongPocket`）。

            // ══ 两分支共用：**黑 8 离台 ⇒ 直接判负** ═══════════════════════════════════
            RuleVerdict d1 = EightBallRules.Judge(solid, Facts(3, new[] { 3 }, null, new[] { 8 }));
            if (!d1.gameOver || d1.winner != ShotTurn.Ai)
            {
                failures.Add($"(d①) **黑 8 离台 ⇒ 本局负**，实测 gameOver={d1.gameOver} winner={d1.winner}。");
            }

            // ══ P3-d：**时序事实**（碰库原则含时序 / 定组看第一颗合法 / 开球黑 8 复位出口）═══════════
            // 期望值来源（逐条写清推导）：
            //  ① 碰库原则原文 =「**母球碰球之后**，任意球碰库或有球进袋」⇒ **碰库必须晚于首碰**（时序）
            //  ② 定组原文 =「开球后的**第一颗合法进球**定组」⇒ 必须按**事件时间序**取第一颗
            //  ③ 开球黑 8 原文 =「拿回放球点、继续打、不算赢」⇒ 需**显式出口**表达"复位"（只有 gameOver=false 不够）

            // (f①) **时序负路径**：母球**先碰库**（事件 0）→ 再碰球（事件 1）→ **此后无球碰库/无球进袋**
            //        ⇒ 旧"全杆集合"实现会看到"Cushion 集合非空"⇒ **误判合法**；新实现必须判**犯规**。
            var timeSeqEvents = new List<ContactEvent>
            {
                new ContactEvent { kind = ContactKind.Cushion, ballA = 0, ballB = -1, position = Vector2.zero },   // 白球先碰库
                new ContactEvent { kind = ContactKind.BallBall, ballA = 0, ballB = 3, position = Vector2.zero },   // 再碰 3 号
            };
            RuleFacts f1 = EightBallRules.ExtractFacts(
                new List<int> { -1, -1, -1, -1 }, new List<int> { -1, -1, -1, -1 },
                new List<bool> { true, true, true, true }, new List<bool> { true, true, true, true },
                timeSeqEvents);
            if (f1.firstBallContactEventIndex != 1)
            {
                failures.Add($"(f①) 首碰事件序号应为 1（先碰库那条在 0），实测 {f1.firstBallContactEventIndex}。");
            }

            if (f1.cushionsAfterFirstBallContact.Count != 0)
            {
                failures.Add($"(f①) **首碰之后**没有碰库 ⇒ `cushionsAfterFirstBallContact` 应为空，"
                             + $"实测 {{{string.Join(",", f1.cushionsAfterFirstBallContact)}}}。");
            }

            RuleVerdict f1v = EightBallRules.Judge(solid, f1);
            if (!f1v.foul || f1v.ballInHand != BallInHandKind.FullTable)
            {
                failures.Add($"(f①) **母球先碰库→再碰球→此后无球碰库** ⇒ 必须**判犯规 + 全场自由球**（碰库原则含时序）；"
                             + $"实测 foul={f1v.foul} ballInHand={f1v.ballInHand}。");
            }

            // (f②) **正路径**：碰球（事件 1）**之后**有球碰库（事件 2）⇒ **合法**（别把新规则变成"一律犯规"）
            var timeSeqOk = new List<ContactEvent>
            {
                new ContactEvent { kind = ContactKind.BallBall, ballA = 0, ballB = 3, position = Vector2.zero },
                new ContactEvent { kind = ContactKind.Cushion, ballA = 3, ballB = -1, position = Vector2.zero },
            };
            RuleFacts f2 = EightBallRules.ExtractFacts(
                new List<int> { -1, -1, -1, -1 }, new List<int> { -1, -1, -1, -1 },
                new List<bool> { true, true, true, true }, new List<bool> { true, true, true, true },
                timeSeqOk);
            RuleVerdict f2v = EightBallRules.Judge(solid, f2);
            if (f2v.foul)
            {
                failures.Add("(f②) 母球碰球**之后**有球碰库 ⇒ 应**合法**（不得把新规则做成一律犯规）。");
            }

            // (f③) **定组看"第一颗合法进球"**：同杆**先进 9（对方组）、再进 3（本方组）**
            //   · **开放球局** ⇒ 9 也算合法 ⇒ 定组依据 = **9**（第一颗）
            //   · **已定实色** ⇒ 9 属对方组 ⇒ **跳过** ⇒ 依据 = **3**
            if (EightBallRules.FirstLegalPottedBall(GroupState.OpenTable, new List<int> { 9, 3 }) != 9)
            {
                failures.Add("(f③) 开放球局的第一颗合法进球应为 **9**（先进哪颗就按哪颗定组）。");
            }

            if (EightBallRules.FirstLegalPottedBall(GroupState.Solid, new List<int> { 9, 3 }) != 3)
            {
                failures.Add("(f③) 已定实色时 **9 属对方组、必须跳过** ⇒ 第一颗合法进球应为 **3**。");
            }

            // ⚠ **集合顺序刻意与时间序不同**（ vs 时间序 ）：
            //   这样**换回集合版**就**必然**得出 3（错），只有用**有序序列**才得 9（对）。
            RuleFacts f3 = Facts(3, new[] { 3 }, new[] { 3, 9 }, null);
            f3.pottedOrder = new List<int> { 9, 3 };      // **按事件时间序**（集合版判不出先后）
            RuleVerdict f3v = EightBallRules.Judge(State(isBreak: false, group: GroupState.OpenTable), f3);
            if (!f3v.reassignGroup || f3v.groupBall != 9)
            {
                failures.Add($"(f③) 开放球局同杆进 {{9,3}} ⇒ 定组依据应为 **9**（`reassignGroup=true` 且 `groupBall=9`），"
                             + $"实测 reassignGroup={f3v.reassignGroup} groupBall={f3v.groupBall}。");
            }

            // (f④) **开球黑 8 复位出口**：`gameOver == false` **且** `respotEightBall == true`
            RuleVerdict f4v = EightBallRules.Judge(State(isBreak: true), Facts(1, new[] { 1 }, new[] { 8 }, null));
            if (f4v.gameOver)
            {
                failures.Add("(f④) 开球进黑 8 **不算赢**（`gameOver` 必须为 false）。");
            }

            if (!f4v.respotEightBall)
            {
                failures.Add("(f④) 开球进黑 8 ⇒ **必须给出 `respotEightBall = true`**（否则「拿回放球点」这条规则无出口表达）。");
            }

            Assert.IsEmpty(failures, "T142 失败（**收集全部**，不在首个失败处中止）：\n" + string.Join("\n", failures));
        }
    }
}
