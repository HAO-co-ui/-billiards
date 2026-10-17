using System.Collections.Generic;
using Billiards.Physics;

namespace Billiards.Rules
{
    /// <summary>球局分组状态（8 球规则）。</summary>
    public enum GroupState
    {
        /// <summary>**开放球局**（开球后尚未定组）。</summary>
        OpenTable,

        /// <summary>本方已定为**实色**（`1..7`）。</summary>
        Solid,

        /// <summary>本方已定为**花色**（`9..15`）。</summary>
        Stripe,
    }

    /// <summary>轮到谁打。</summary>
    public enum ShotTurn
    {
        Player,
        Ai,
    }

    /// <summary>自由球的**形态**。</summary>
    public enum BallInHandKind
    {
        None,

        /// <summary>**全场自由球**（常规犯规的处罚）。</summary>
        FullTable,

        /// <summary>**头区（kitchen）自由球**（开球犯规的处罚）：只能放在开球线后、且只能往线前击球。</summary>
        Kitchen,
    }

    /// <summary>
    /// **判定的输入状态**（纯数据；**不含** `matchScore`/`rackIndex` —— D4 单局定胜负已砍掉比赛循环）。
    ///
    /// ⚠ **命名**：本类型**故意不复用** `PracticeHudState.IsPlayerTurn` 那个"**相位判断**"的名字
    /// （同名不同义是本工程刚立过的坑）；本处指的是**球局的回合归属**。
    /// </summary>
    public struct RuleState
    {
        /// <summary>分组状态（开放球局 / 实色 / 花色）。</summary>
        public GroupState groupState;

        /// <summary>当前轮到谁。</summary>
        public ShotTurn turn;

        /// <summary>实色组剩余球数（`1..7` 中仍在场的颗数）。</summary>
        public int solidRemaining;

        /// <summary>花色组剩余球数（`9..15` 中仍在场的颗数）。</summary>
        public int stripeRemaining;

        /// <summary>**本杆必须往开球线前方击球**（头区自由球的附加约束）。</summary>
        public bool mustShootForward;

        /// <summary>**指定的袋号**（D3；`-1` = 尚未指定）。仅在打黑 8 那一杆有意义。</summary>
        public int calledPocket;

        /// <summary>本杆是否是**开球那一杆**。</summary>
        public bool isBreak;
    }

    /// <summary>
    /// **本杆事实**（判定输入）—— **只能来自既有只读出口**：
    /// `PoolSimulator.ShotContactEvents`（含球号的接触事件）＋ 逐球 `IsBallInPlay` / `CapturedPocketIndex` 的**前后比对**。
    /// ⚠ **不许** AI/UI 反向耦合（不得由表现层推算事实）。
    ///
    /// ⚠ **"落袋"与"非入袋离台"必须分开**：
    /// · **落袋** = `CapturedPocketIndex(i) &gt;= 0`；
    /// · **非入袋离台** = `CapturedPocketIndex(i) == -1` **且** `!IsBallInPlay(i)`（越界冻结）。
    /// **不要**用 `CapturedBallCount` 做逐号判定（那是总量）。
    /// </summary>
    public struct RuleFacts
    {
        /// <summary>**首碰球号**（母球最先碰到的球；`-1` = 本杆没碰到任何球）。</summary>
        public int firstContactBall;

        /// <summary>**碰过库的球号**（**按球号去重**；含白球时含 `0`）。</summary>
        public List<int> cushionTouchedByBall;

        /// <summary>**落袋**的球号（含白球时含 `0`）。</summary>
        public List<int> pottedBalls;

        /// <summary>**非入袋离台**的球号（越界冻结；`CapturedPocketIndex == -1`）。</summary>
        public List<int> offTableBalls;

        /// <summary>**白球落袋或离台**（`0` 号不在场）。</summary>
        public bool cueBallPottedOrOff;

        /// <summary>
        /// **黑 8 落进了哪个袋**（袋号；`-1` = 本杆黑 8 未落袋）—— **D3「指定袋」判定必需**
        /// （只知"进了黑 8"不够，还要知道**进的是不是指定的那个袋**）。
        /// </summary>
        public int eightBallPocket;

        // ── **下列三项是"时序"事实（P3-d 补）** ─────────────────────────────────────
        // 规则原文对"碰库原则"的表述是「**母球碰球之后**，任意球碰库或有球进袋」——**时序是规则的一部分**；
        // 而 `cushionTouchedByBall` 只是**集合**（时序丢失）⇒ "母球先碰库 → 再碰球 → 此后无球碰库"
        // 会被**误判合法**（旧实现必然漏）。

        /// <summary>**首个"球-球"接触的事件序号**（`ShotContactEvents` 的**时间序下标**；`-1` = 没碰到球）。</summary>
        public int firstBallContactEventIndex;

        /// <summary>
        /// **在首个球-球接触之后**碰过库的球号（**按球号去重**）—— 由**事件序号 > 首碰序号**筛出。
        /// **碰库原则必须用它**（不是用"全杆碰过库"的集合）。
        /// </summary>
        public List<int> cushionsAfterFirstBallContact;

        /// <summary>
        /// **落袋球号，按事件时间序**（同一个球重复落袋事件不会出现；用于"**第一颗合法进球**定组"）。
        /// ⚠ 集合版 `pottedBalls` **判不出"第一颗"** ⇒ 同杆先进对方的球再进自己的球时定组会错（P3-d-③）。
        /// </summary>
        public List<int> pottedOrder;
    }

    /// <summary>
    /// **本杆的结局种类**（P6：让"结算出口"能**与规则层同源**地说明"为什么结束"，而不是由宿主自己猜）。
    ///
    /// ⚠ **这是穷举枚举**（`switch` 必须**列全**；**不得**再写成 `default: 当成"合法进黑 8"` ——
    /// 那正是"袋口落兜底"那轮的教训）。新增分支时必须同时改这里的枚举与 `Judge`。
    /// </summary>
    public enum ShotOutcomeKind
    {
        /// <summary>本杆**没有**达成任何结局（继续打 / 换手 / 犯规罚自由球）。</summary>
        Ongoing = 0,

        /// <summary>**合法进黑 8**（本方组已清完 + 本杆合法 + 进的是**指定袋**）⇒ **胜**。</summary>
        LegalEightBallPotted,

        /// <summary>**黑 8 提前进袋**（本方组未清完 / 本杆犯规 / 开放球局）⇒ **负**。</summary>
        EightBallPottedTooEarly,

        /// <summary>**打黑 8 进错袋**（`calledPocket` 不一致或未指定）⇒ **负**（D3 指定袋判负）。</summary>
        EightBallWrongPocket,

        /// <summary>**黑 8 离台**（非入袋离台）⇒ **负**。</summary>
        EightBallOffTable,

        /// <summary>**开球进黑 8** ⇒ **复位回脚点、继续打、不算赢**（唯一"进袋但没结局"的分支）。</summary>
        EightBallRespot,
    }

    /// <summary>**判定结果**（纯数据；**施加**留给宿主 —— 切回合 / 发自由球 / 记分都在 P5 的薄宿主）。</summary>
    /// <remarks>
    /// ⚠ **它是 ** ⇒ 分支函数**必须用  传**（按值传的修改会**静默丢失** ——
    /// 本层第一版就踩了：症状是所有分支的判定都不生效）。
    /// </remarks>
    public struct RuleVerdict
    {
        /// <summary>本杆是否犯规。</summary>
        public bool foul;

        /// <summary>下一杆轮到谁。</summary>
        public ShotTurn nextTurn;

        /// <summary>自由球形态（`None` = 不给自由球）。</summary>
        public BallInHandKind ballInHand;

        /// <summary>**需要重摆**（开球无效）。</summary>
        public bool reRack;

        /// <summary>本局是否结束。</summary>
        public bool gameOver;

        /// <summary>本局胜者（`gameOver == true` 时有效）。</summary>
        public ShotTurn winner;

        /// <summary>**是否重新定组**（开放球局里第一颗合法进球 ⇒ `true`；开球**不定组** ⇒ `false`）。</summary>
        public bool reassignGroup;

        /// <summary>
        /// **按哪颗球定组**（`reassignGroup == true` 时有效；`-1` = 无）—— 由
        /// <see cref="EightBallRules.FirstLegalPottedBall"/> 单点算出（**不得**由宿主自己猜）。
        /// </summary>
        public int groupBall;

        /// <summary>
        /// **是否要把黑 8 复位回放球点（`footSpot`）** —— **开球黑 8 进袋**那条规则的**显式出口**
        /// （P3-d-① 补：此前只有 `gameOver == false` 表达"不算赢"，**"拿回放球点"没有任何出口可断**）。
        /// **施加**由宿主做（纯层只表达意图）。
        /// </summary>
        public bool respotEightBall;

        /// <summary>
        /// **下一杆必须往开球线前方击球**（头区自由球的附加约束）—— 宿主把它写进下一杆的
        /// <see cref="RuleState.mustShootForward"/>；出杆方向由
        /// <see cref="Billiards.Physics.PoolSimulator.IsBreakShotForwardEnough"/> 判。
        /// </summary>
        public bool mustShootForward;

        /// <summary>
        /// **本杆的结局种类**（P6）：结算出口据此说明"为什么结束"——**由规则层填**，
        /// 宿主**只做映射、不自己猜**（判据⑥守这条）。
        /// </summary>
        public ShotOutcomeKind outcome;
    }

    /// <summary>
    /// **8 球规则：纯函数判定层**（35-C P3）。
    ///
    /// **分层硬要求**：本类**纯函数**（无 `MonoBehaviour`、无 `Simulator` 引用、**不读时间/随机**）；
    /// **施加**（切回合、发自由球、记分）留给薄宿主（P5 接）。
    /// **AI 与规则的唯一接口** = <see cref="Judge"/>；**事实只能来自既有只读出口**（见 <see cref="RuleFacts"/>）。
    /// </summary>
    public static class EightBallRules
    {
        /// <summary>黑 8 的球号。</summary>
        public const int EightBall = 8;

        /// <summary>开球有效所需的最少**碰库目标球数**（`>= 4` 颗，按球号去重）。</summary>
        public const int ValidBreakCushionCount = 4;

        /// <summary>该球号是否属**实色**组（`1..7`）。</summary>
        public static bool IsSolid(int ball) => ball >= 1 && ball <= 7;

        /// <summary>该球号是否属**花色**组（`9..15`）。</summary>
        public static bool IsStripe(int ball) => ball >= 9 && ball <= 15;

        /// <summary>
        /// **facts 提取器（单点实现）** —— 由"逐球前后状态 + 本杆接触事件"算出 <see cref="RuleFacts"/>。
        ///
        /// 参数都是**既有只读出口的原始数据**（调用方直接灌入即可，本函数**不碰内核**）：
        /// · `beforePocketIndex` / `afterPocketIndex` = `CapturedPocketIndex(i)` 的前后快照；
        /// · `beforeInPlay` / `afterInPlay` = `IsBallInPlay(i)` 的前后快照；
        /// · `shotEvents` = `PoolSimulator.ShotContactEvents`（带 `ballA`/`ballB` 的接触事件流）。
        /// </summary>
        public static RuleFacts ExtractFacts(IReadOnlyList<int> beforePocketIndex, IReadOnlyList<int> afterPocketIndex,
            IReadOnlyList<bool> beforeInPlay, IReadOnlyList<bool> afterInPlay, IReadOnlyList<ContactEvent> shotEvents)
        {
            var facts = new RuleFacts
            {
                firstContactBall = -1,
                cushionTouchedByBall = new List<int>(),
                pottedBalls = new List<int>(),
                offTableBalls = new List<int>(),
                firstBallContactEventIndex = -1,
                cushionsAfterFirstBallContact = new List<int>(),
                pottedOrder = new List<int>(),
            };

            if (shotEvents != null)
            {
                for (int i = 0; i < shotEvents.Count; i++)
                {
                    ContactEvent ev = shotEvents[i];

                    if (ev.kind == ContactKind.BallBall && facts.firstContactBall < 0)
                    {
                        // 首个**球-球**接触：取"另一颗"（母球恒为 0）
                        if (ev.ballA == 0)
                        {
                            facts.firstContactBall = ev.ballB;
                        }
                        else if (ev.ballB == 0)
                        {
                            facts.firstContactBall = ev.ballA;
                        }
                        else
                        {
                            facts.firstContactBall = ev.ballA;
                        }
                    }

                    if (ev.kind == ContactKind.Cushion && ev.ballA >= 0 && !facts.cushionTouchedByBall.Contains(ev.ballA))
                    {
                        facts.cushionTouchedByBall.Add(ev.ballA);   // **按球号去重**（同一颗多次碰库只算 1 颗）
                    }

                    if (ev.kind == ContactKind.Pocket && ev.ballA >= 0)
                    {
                        facts.pottedOrder.Add(ev.ballA);            // **按事件时间序**（供"第一颗合法进球"用）
                    }

                    // **首个球-球接触的事件序号**（时序基准；碰库原则要在它"之后"筛）
                    if (ev.kind == ContactKind.BallBall && facts.firstBallContactEventIndex < 0)
                    {
                        facts.firstBallContactEventIndex = i;
                    }
                }

                // **首碰之后**碰过库的球（**按序号 > 首碰序号**筛 ⇒ 时序进入事实）
                if (facts.firstBallContactEventIndex >= 0)
                {
                    for (int i = facts.firstBallContactEventIndex + 1; i < shotEvents.Count; i++)
                    {
                        ContactEvent later = shotEvents[i];
                        if (later.kind == ContactKind.Cushion && later.ballA >= 0
                            && !facts.cushionsAfterFirstBallContact.Contains(later.ballA))
                        {
                            facts.cushionsAfterFirstBallContact.Add(later.ballA);
                        }
                    }
                }
            }

            if (afterInPlay != null && afterPocketIndex != null && beforeInPlay != null && beforePocketIndex != null)
            {
                int count = afterInPlay.Count;
                for (int ball = 0; ball < count && ball < afterPocketIndex.Count; ball++)
                {
                    bool wasInPlay = ball < beforeInPlay.Count && beforeInPlay[ball];
                    if (!wasInPlay || afterInPlay[ball])
                    {
                        continue;   // 本杆前就不在场 / 现在还在场 ⇒ 本杆与它无关
                    }

                    bool potted = afterPocketIndex[ball] >= 0;
                    int wasPotted = ball < beforePocketIndex.Count ? beforePocketIndex[ball] : -1;
                    if (potted && wasPotted < 0)
                    {
                        facts.pottedBalls.Add(ball);
                    }
                    else if (!potted)
                    {
                        facts.offTableBalls.Add(ball);   // 越界冻结、非入袋
                    }
                }
            }

            facts.cueBallPottedOrOff = facts.pottedBalls.Contains(0) || facts.offTableBalls.Contains(0);
            facts.eightBallPocket = (afterPocketIndex != null && afterPocketIndex.Count > EightBall)
                ? (facts.pottedBalls.Contains(EightBall) ? afterPocketIndex[EightBall] : -1)
                : -1;
            return facts;
        }

        /// <summary>**AI 与规则的唯一接口**：给定"本杆前状态 + 本杆事实"⇒ 判定结果（纯函数）。</summary>
        public static RuleVerdict Judge(in RuleState before, in RuleFacts shot)
        {
            var verdict = new RuleVerdict
            {
                nextTurn = before.turn,
                ballInHand = BallInHandKind.None,
                winner = before.turn,
            };

            bool eightPotted = Contains(shot.pottedBalls, EightBall);
            bool eightOffTable = Contains(shot.offTableBalls, EightBall);
            int objectBallsPotted = CountObjectBalls(shot.pottedBalls);
            int cushionObjectBalls = CountObjectBalls(shot.cushionTouchedByBall);

            // ── 直接判负（两条分支共用）：黑 8 离台 ⇒ 本局负 ──
            if (eightOffTable)
            {
                verdict.foul = true;
                verdict.gameOver = true;
                verdict.winner = Other(before.turn);
                verdict.nextTurn = Other(before.turn);
                verdict.outcome = ShotOutcomeKind.EightBallOffTable;
                return verdict;
            }

            if (before.isBreak)
            {
                JudgeBreak(before, shot, ref verdict, eightPotted, objectBallsPotted, cushionObjectBalls);
                return verdict;
            }

            JudgeRegular(before, shot, ref verdict, eightPotted);
            return verdict;
        }

        /// <summary>**开球分支**（§3.3-2/3/4）。</summary>
        private static void JudgeBreak(in RuleState before, in RuleFacts shot, ref RuleVerdict verdict,
            bool eightPotted, int objectBallsPotted, int cushionObjectBalls)
        {
            // **有效开球** = ≥1 颗目标球进袋 **或** ≥4 颗目标球碰库（按球号去重、只算 1..15）
            bool validBreak = objectBallsPotted >= 1 || cushionObjectBalls >= ValidBreakCushionCount;
            if (!validBreak)
            {
                verdict.reRack = true;          // **重摆，开球方不变**（主理人裁定）
                verdict.nextTurn = before.turn;
                return;
            }

            // 开球犯规（白球落袋 / 离台）⇒ **对手 kitchen 自由球 + 必须往线前击球**
            if (shot.cueBallPottedOrOff)
            {
                verdict.foul = true;
                verdict.nextTurn = Other(before.turn);
                verdict.ballInHand = BallInHandKind.Kitchen;
                verdict.mustShootForward = true;
            }

            // **开球黑 8 进袋** ⇒ 拿回放球点（`footSpot`，由宿主施加）、**继续打、不算赢**
            if (eightPotted)
            {
                verdict.gameOver = false;       // ★ 明确：开球进黑 8 **不是胜利**
                verdict.respotEightBall = true; // ★ **显式出口**（P3-d-①）：宿主据此把黑 8 复位到放球点
                verdict.reassignGroup = false;
                verdict.outcome = ShotOutcomeKind.EightBallRespot;
            }

            // **开球进球不定组**（开放球局）
            verdict.reassignGroup = false;

            // **P7：合法开球但一颗目标球都没进 ⇒ 换手**（正规 8 球；旧实现默认续杆 = 用户反馈 6"发球没进球也没换手"）。
            // ⚠ 放在最后：前面"开球犯规 ⇒ nextTurn = 对手"已经处理过的情况不会被它改写（同向）。
            //   开球**进了黑 8**（respot）或**进了任意目标球** ⇒ `objectBallsPotted >= 1` ⇒ 不换手（继续打）。
            if (!eightPotted && objectBallsPotted == 0)
            {
                verdict.nextTurn = Other(before.turn);
            }
        }

        /// <summary>**常规分支**（§3.2：定组 / 首碰 / 碰库原则 / 直接判负 / 胜负 / D3 指定袋）。</summary>
        private static void JudgeRegular(in RuleState before, in RuleFacts shot, ref RuleVerdict verdict, bool eightPotted)
        {
            bool fouled = false;

            // ① 首碰合法性：**必须先碰到本方组的球**（开放球局除外）；
            //    本方组已清完后，首碰黑 8 才合法。
            if (shot.firstContactBall < 0)
            {
                fouled = true;                                  // 空杆（没碰到任何球）
            }
            else if (before.groupState != GroupState.OpenTable)
            {
                bool ownGroupCleared = before.groupState == GroupState.Solid
                    ? before.solidRemaining <= 0
                    : before.stripeRemaining <= 0;
                int first = shot.firstContactBall;
                bool isOwnGroup = before.groupState == GroupState.Solid ? IsSolid(first) : IsStripe(first);
                bool firstIsEight = first == EightBall;
                if (!(isOwnGroup || (ownGroupCleared && firstIsEight)))
                {
                    fouled = true;                              // 先碰到对方球 / 未清完先碰黑 8
                }
            }

            // ② **碰库原则**：**母球碰球之后**，任意球碰库 或 有球进袋 才算合法
            //    ⚠ **必须用"首碰之后"的集合**（P3-d-②）：用全杆集合会把"先碰库→再碰球→此后无碰库"**误判合法**。
            bool anyBallTouchedCushion = shot.cushionsAfterFirstBallContact != null
                                         && shot.cushionsAfterFirstBallContact.Count > 0;
            bool anyBallPotted = (shot.pottedBalls != null && shot.pottedBalls.Count > 0)
                                 || (shot.offTableBalls != null && shot.offTableBalls.Count > 0);
            if (!anyBallTouchedCushion && !anyBallPotted)
            {
                fouled = true;
            }

            // ③ 白球落袋 / 离台 ⇒ 犯规
            if (shot.cueBallPottedOrOff)
            {
                fouled = true;
            }

            verdict.foul = fouled;

            // ④ 黑 8 入袋：分胜 / 负（**未清完就打进 ⇒ 判负**；清完 + 合法 + 指定袋 ⇒ 胜）
            if (eightPotted)
            {
                bool ownGroupCleared = before.groupState == GroupState.Solid
                    ? before.solidRemaining <= 0
                    : before.stripeRemaining <= 0;
                bool openTable = before.groupState == GroupState.OpenTable;

                // **进非指定袋 ⇒ 判负**；**未指定（-1）却进了黑 8 ⇒ 也必须判负并留痕**（不许静默放过）
                // **进非指定袋 ⇒ 判负**；**未指定（-1）却进了黑 8 ⇒ 也必须判负并留痕**（不许静默放过）
                bool wrongPocket = before.calledPocket < 0
                                   || (shot.eightBallPocket >= 0 && shot.eightBallPocket != before.calledPocket);

                verdict.gameOver = true;
                verdict.winner = (!fouled && !openTable && ownGroupCleared && !wrongPocket) ? before.turn : Other(before.turn);
                verdict.nextTurn = verdict.winner;

                // **结局归类**（P6；**穷举**、无兜底）：`wrongPocket` 优先于"提前进袋" ——
                // 进错袋是**更具体**的违规（D3），先按它归类；否则按胜负分"合法 / 提前"。
                verdict.outcome = wrongPocket
                    ? ShotOutcomeKind.EightBallWrongPocket
                    : (verdict.winner == before.turn
                        ? ShotOutcomeKind.LegalEightBallPotted
                        : ShotOutcomeKind.EightBallPottedTooEarly);
                return;
            }

            // ⑤ 定组：**开球后的第一颗合法进球**才定组（开放球局 + 无犯规 + 有目标球进袋）
            int groupBall = FirstLegalPottedBall(before.groupState, shot.pottedOrder);
            if (before.groupState == GroupState.OpenTable && !fouled && groupBall > 0)
            {
                verdict.reassignGroup = true;
                verdict.groupBall = groupBall;     // **第一颗合法进球**（单点实现 ⇒ 宿主不必猜）
            }

            // ⑥ 犯规 ⇒ 对手**全场**自由球；
            //    **P7 正规规则**：不犯规但**没进本方组的球** ⇒ **换手**（旧实现"只要不犯规就续杆"= 用户反馈 6）。
            //    续杆的唯一条件 = 本杆进了**本方组**的球（开放球局 ⇒ 任意目标球，不含黑 8）。
            if (fouled)
            {
                verdict.nextTurn = Other(before.turn);
                verdict.ballInHand = BallInHandKind.FullTable;
            }
            else if (PottedOwnGroupBall(before.groupState, shot.pottedBalls))
            {
                verdict.nextTurn = before.turn;
            }
            else
            {
                verdict.nextTurn = Other(before.turn);
            }
        }

        private static bool Contains(List<int> list, int ball) => list != null && list.Contains(ball);

        private static int CountObjectBalls(List<int> list)
        {
            if (list == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] >= 1 && list[i] <= 15)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// **"第一颗合法进球"的球号**（P3-d-③ 的单点实现）—— 定组就按它判实/花。
        ///
        /// 定义：按**事件时间序**扫描落袋序列，返回**第一个**满足"该球在**当前分组状态下算合法**"的球号：
        /// · **开放球局** ⇒ 任何目标球（`1..15`，**不含**黑 8）都算合法（⇒ 先进哪颗就按哪颗定组）；
        /// · **已定实色** ⇒ 只有实色球（`1..7`）算合法（**对方组的球被跳过**，不算"第一颗合法"）；
        /// · **已定花色** ⇒ 只有花色球（`9..15`）算合法。
        /// ⇒ 无合法球 ⇒ 返回 `-1`。
        /// ⚠ 为什么集合版不行：`pottedBalls` 是无序集合 ⇒ 同杆"先进对方的球、再进自己的球"时**判不出先后**。
        /// </summary>
        public static int FirstLegalPottedBall(GroupState groupState, IReadOnlyList<int> pottedOrder)
        {
            if (pottedOrder == null)
            {
                return -1;
            }

            for (int i = 0; i < pottedOrder.Count; i++)
            {
                int ball = pottedOrder[i];
                if (ball == EightBall || ball < 1 || ball > 15)
                {
                    continue;   // 黑 8 与白球都不参与"定组"
                }

                bool legal = groupState switch
                {
                    GroupState.OpenTable => true,
                    GroupState.Solid => IsSolid(ball),
                    GroupState.Stripe => IsStripe(ball),
                    _ => false,
                };

                if (legal)
                {
                    return ball;
                }
            }

            return -1;
        }

        /// <summary>
        /// **本杆是否进了"本方组的球"** —— **续杆的唯一判据**（P7）。
        ///
        /// · **开放球局** ⇒ 进了任意目标球（`1..15`，**不含**黑 8）就算续杆；
        /// · **已定实色** ⇒ 只有实色球算；
        /// · **已定花色** ⇒ 只有花色球算。
        ///
        /// ⚠ 与 <see cref="FirstLegalPottedBall"/> **不同**：那个答"按哪颗定组"，这个答"要不要换手"；
        /// 两者都用**集合** `pottedBalls`（本杆进了哪些球），不需要先后次序。
        /// </summary>
        public static bool PottedOwnGroupBall(GroupState groupState, List<int> pottedBalls)
        {
            if (pottedBalls == null)
            {
                return false;
            }

            for (int i = 0; i < pottedBalls.Count; i++)
            {
                int ball = pottedBalls[i];
                if (ball < 1 || ball > 15 || ball == EightBall)
                {
                    continue;   // 白球与黑 8 都不参与"续杆判定"（黑 8 走胜负分支）
                }

                switch (groupState)
                {
                    case GroupState.OpenTable:
                        return true;
                    case GroupState.Solid:
                        if (IsSolid(ball))
                        {
                            return true;
                        }

                        break;
                    case GroupState.Stripe:
                        if (IsStripe(ball))
                        {
                            return true;
                        }

                        break;
                }
            }

            return false;
        }

        /// <summary>对手。</summary>
        public static ShotTurn Other(ShotTurn turn) => turn == ShotTurn.Player ? ShotTurn.Ai : ShotTurn.Player;
    }
}
