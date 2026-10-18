using System.Collections.Generic;
using Billiards.Physics;
using Billiards.Rules;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **状态条的视觉状态 —— 与设计契约 §二 的 `data-state` 一一对应**。
    ///
    /// 八个取值就是美术包 `04_UI/UI全流程设计说明_参考.txt` §四写死的那八个 token：
    /// `aim / ai-turn / moving / ball-in-hand / call-pocket / foul / paused / result`。
    /// 每个状态在设计里都同时规定了三件事：**图标**（<see cref="ClubUiIcons.ForState"/>）、
    /// **边线色与指示色**（<see cref="PracticeHudState.EdgeColor"/> / <see cref="PracticeHudState.SymbolColor"/>）、
    /// **文案**（<see cref="PracticeHudState.StateTitle"/> / <see cref="PracticeHudState.StateDetail"/>）。
    ///
    /// ── 本卡可达 7 态、契约先行 1 态（P6 按事实重钉；如实登记）──────────────────
    /// `ai-turn` 与 `call-pocket` 在 P6 **都已接线可达**；仍不可触发的只剩 `paused`（**本卡无暂停系统**）。
    /// 它仍然在枚举、配色、图标、文案里**定义齐全** —— 接入暂停时**不需要改这里**。
    /// 不可达的那一条在 <see cref="UnreachableStates"/> 里显式登记，判据 `T125②` / `②'` 据此断言
    /// "不可达集合既没被偷偷扩大、也没被偷偷缩小"，并且**用参数穷举反证**"登记为可达的态真的能被产出"。
    /// </summary>
    public enum HudVisualState
    {
        /// <summary>`aim` —— 轮到玩家、可以出杆。</summary>
        Aim = 0,

        /// <summary>`ai-turn` —— 对手回合。**P6 起可达**（真值源 `PhysicsTestDrive.IsAiTurn`）。</summary>
        AiTurn = 1,

        /// <summary>`moving` —— 球在动 / 台面未静止（含"出杆被拒"）。</summary>
        Moving = 2,

        /// <summary>`ball-in-hand` —— 白球已归位、等你出杆。</summary>
        BallInHand = 3,

        /// <summary>
        /// `call-pocket` —— 指定球袋。**契约先行（当前不可达）**：D3 已按用户要求取消
        /// （黑 8 进任意袋都有效），产品侧出口已全部删除 ⇒ 见 <see cref="UnreachableStates"/>。
        /// </summary>
        CallPocket = 4,

        /// <summary>`foul` —— 犯规（本工程唯一的可观测犯规 = 白球落袋）。</summary>
        Foul = 5,

        /// <summary>`paused` —— 比赛冻结。**仍不可达**（本卡没有暂停系统）。</summary>
        Paused = 6,

        /// <summary>`result` —— 结算卡已弹出。</summary>
        Result = 7,
    }

    /// <summary>
    /// **HUD 的显示映射单点**：视觉状态 → 文案 / 配色 / 图标；力度 → 百分比文案；计数 → `n / m`。
    ///
    /// ── 文案口径（**全英文**，本阶段不得含中文）───────────────────────────────
    /// 美术包的两套字体（Barlow Condensed / Inter）只覆盖 **Latin 子集**，且设计说明明写
    /// "不覆盖中文 ⇒ 改中文需团队确认字体" ⇒ 35-A 的 HUD 文案全部英文是**按设计说明的规避手段**，
    /// 不是偷懒。中文方案属正式版决策项。
    ///
    /// ── 与排期「状态条（`aim` / `moving`）」的对应 ────────────────────────────
    /// 流程状态机有五个相位（`Aim`/`Charging`/`Shooting`/`Moving`/`Settle`），但**视觉状态只有
    /// `Aim` 与 `Moving` 两个**可映射（其余是设计契约里本卡不可达的三态 + `ball-in-hand` / `foul` / `result`）。
    /// `Aim`/`Charging` → `aim`；`Shooting`/`Moving`/`Settle` → `moving`
    /// —— 与设计说明 §5「球运动：击球与力条在球停止前不可用」同口径。
    ///
    /// ── 优先级（唯一实现点，铁律 16）────────────────────────────────────────
    /// `result` &gt; **被拒** &gt; 犯规 &gt; 自由球 &gt; 相位。
    /// 「被拒」排在犯规之前：犯规是**已经发生的事**，被拒是**你正想做的事没做成**，
    /// 后者更需要当场的解释。
    /// </summary>
    public static class PracticeHudState
    {
        // ════════════════ 文案常量（单点；视图与判据 `T97` 的字符覆盖检查共用同一组字面量）════

        /// <summary>`aim` 状态标题。</summary>
        public const string StateAim = "Aim";

        /// <summary>`moving` 状态标题。**同时是"出杆被拒"的标题** —— 被拒的成因就是台面没停，
        /// 视觉状态因此就是 `moving`（虚线圈 + 静音灰 + 力条与 `Shoot` 不可用），
        /// 两处用**同一个**常量 ⇒ 不存在"被拒文案与运动文案各写一份、改一处忘一处"。</summary>
        public const string StateBallsMoving = "Balls moving";

        /// <summary>`moving` 状态标题的旧名（与本类的 <see cref="StateBallsMoving"/> 同值，保留以兼容既有引用）。</summary>
        public const string StateMoving = StateBallsMoving;

        /// <summary>`foul` 状态标题 —— 设计说明 §9 要求犯规"不以颜色作为唯一识别手段" ⇒ 颜色与文字同时给。</summary>
        public const string StateFoul = "Foul";

        /// <summary>`ball-in-hand` 状态标题（设计说明 §7 逐字：手掌图标 + 黄铜边线）。</summary>
        public const string StateBallInHand = "Ball in hand";

        /// <summary>`paused` 状态标题（35-A 不可达，契约先行）。</summary>
        public const string StatePaused = "Paused";

        /// <summary>`result` 状态标题（35-A 不可达，契约先行 —— 真正出结果时走结算卡）。</summary>
        public const string StateResult = "Result";

        /// <summary>`ai-turn` 状态标题（35-A 不可达，契约先行）。</summary>
        public const string StateAiTurn = "Opponent";

        /// <summary>`call-pocket` 状态标题（35-A 不可达，契约先行）。</summary>
        public const string StateCallPocket = "Call a pocket";

        /// <summary>`aim` 状态的常驻说明 —— **设计稿逐字**：练习模式的目标提示。</summary>
        public const string CaptionIdle = "Clear the colors, then the 8 ball";

        /// <summary>`moving` 状态的说明。</summary>
        public const string RefusedDetail = "Wait for the table";

        /// <summary>
        /// `foul` 状态的说明行 —— **P32 文案口径变更：不再含任何位置主张**（主理人裁定）。
        ///
        /// ── 为什么从 `"Cue ball back on the spot"` 改成 `"Foul"` ────────────────────────
        /// 旧句描述的是**更早那套规则**（白球落袋 ⇒ 自动放回开球点）。现在它在**两种模式下都不总成立**：
        /// · **对战**：犯规给的是**自由球**，白球由玩家自己放到台面任意合法位置
        ///   （那一档的文案是 <see cref="BallInHandPendingDetail"/>）；
        /// · **练习**：`ApplyVerdict` 根本不置 `PendingCueBallPlacement`（`practiceNoHandBall`）
        ///   ⇒ 空白杆那种犯规白球**原地不动**，而旧句会让玩家以为它被搬回了开球点。
        /// ⇒ 说明行只说"发生了什么"（犯规）；**位置与后续动作由后续状态承担**
        ///   （待放球档给"怎么放"、`Aim` 档给 `CaptionIdle`）—— 一句话不承诺它保证不了的事。
        ///
        /// ⚠ 与 <see cref="StateFoul"/> 同值（标题与说明都读 `Foul`）是**有意的**：
        ///   这一档没有更细的信息可给，宁可重复一个词，也不写一句可能为假的解释。
        /// </summary>
        public const string FoulDetail = "Foul";

        /// <summary>
        /// `ball-in-hand` 且**白球已回到台上**时的说明。
        ///
        /// ⚠ 旧文案是 `Cue ball replaced at the spot`，描述的是**旧规则**（白球入袋后自动放回开球点）。
        /// 新规则是"**全场自由球**"（白球入袋后玩家可把它放在台面任意合法位置，Esc 才放回开球点）
        /// ⇒ 那句已经**不成立**了（玩家可能把它放在别处），故改为**不含位置主张**的陈述。
        /// </summary>
        public const string BallInHandDetail = "Cue ball is back on the table";

        /// <summary>
        /// `ball-in-hand` 且**白球还在手上（尚未放下）**时的说明 —— 即"**待放球**"这一瞬态。
        ///
        /// 三条信息缺一不可：① 动作（点台面放下）；② 范围（台面任意合法位置）；③ **兜底键**（`Esc`）。
        /// ③ 是**可发现性**的硬要求：兜底若不在界面上出现，就等于不存在（玩家不会去猜）。
        ///
        /// ── 两处措辞是**被裁定修正过的**（都不是风格问题）─────────────────────────────
        /// · `Tap` → **`Click`**：本工程的放下动作是鼠标 `Input.GetMouseButtonDown(0)`，**不是触摸屏**
        ///   （全库只有这一处曾用 `Tap`）⇒ 写 `Tap` 是**指向不存在的输入方式**；
        /// · **`Esc` 那句的落点措辞**：最终写作 **`returns it to its starting position`**。
        ///   为什么**不用** `spot` 这个词（这是本条最值得留下的理由）：`Esc` 的落点是
        ///   `PhysicsTestDrive.ReturnCueBallToHome()` → `CueBallHomePosition(tableConfig.footSpot)`
        ///   = **本局白球起始位**（球堆顶点 `RackApexX = -0.30` **后方 1.27 m**，见该字段注释），
        ///   而 **`spot` 在台球标准术语里专指 `foot spot`**（摆球那一点），本工程的 `footSpot` 也正是那个点
        ///   ⇒ 一旦用 `spot`，玩家会把它读成"放在脚点"——**与"离它 1.27 m"的事实相反**。
        ///   换句话说：不是"换个说法"，而是**这个位置在标准术语里就没有对应的那个词**，故措辞必须回避它。
        ///
        /// ℹ **一处回报（引用常量名的价值）**：`PointerAimController.HandleCueBallPlacement()` 的注释
        ///   引用的是本常量的**名字**（不是抄句子）⇒ 本次措辞改动**它一行都不用改**。
        ///   这就是"让它引常量名而不是抄句子"的直接回报（见那条注释自身写明的一致性要求）。
        ///
        /// 字符口径：本工程字体只覆盖 Latin 子集，中间的分隔符用 `·`（U+00B7，5 个源字体实测全覆盖）。
        /// </summary>
        public const string BallInHandPendingDetail =
            "Click to place the cue ball anywhere · Esc returns it to its starting position";

        /// <summary>
        /// `ball-in-hand` 且**落点被拒**（`PhysicsTestDrive.RefusedPlacementCount` 上升沿）时的说明。
        ///
        /// ⚠ **不得复用** `RefusedDetail`（`"Wait for the table"`）：落点被拒那一刻台面**是静止的**
        /// （正因为静止才允许放球）⇒ 那句话在此场景是**假陈述**。故单开这一条。
        /// </summary>
        public const string PlacementRefusedDetail = "Cue ball can't go there";

        /// <summary>`paused` 状态的说明（契约先行）。</summary>
        public const string PausedDetail = "Match frozen";

        /// <summary>`result` 状态的说明（契约先行）。</summary>
        public const string ResultDetail = "Play again or export the data";

        /// <summary>`ai-turn` 状态的说明（契约先行）。</summary>
        public const string AiTurnDetail = "Opponent is taking its shot";

        /// <summary>`call-pocket` 状态的说明（契约先行）。</summary>
        public const string CallPocketDetail = "Pick the pocket for the 8 ball";

        // ── 标签与按钮（记分牌 / 力度块）───────────────────────────────────────
        /// <summary>记分牌右侧球行的标签（未进的球亮、已进球的 20% 透明 ⇒ 字面就是"还剩多少"）。</summary>
        /// <summary>对战 HUD 每一侧最多几个球号 chip（= 一组球上限 7）。</summary>
        public const int MatchGroupChipSlots = 7;

        /// <summary>
        /// **剩余球号 → 固定槽位的 chip 号**（纯函数；`-1` = 该槽**不显示**）。
        ///
        /// ⚠ **空列表 ⇒ 全 `-1`** = 用户要的「一开始是空的」：开放球局两块**零 chip、零字形**，
        ///   **不写 "Open table" 之类占位**（用户原话："也不要'待定'字样"）。
        /// ⚠ 黑 8 不在输入里（`*RemainingNumbers` 天然不含）⇒ 无需特判。
        /// </summary>
        public static int[] GroupChipNumbers(IReadOnlyList<int> numbers, int slots)
        {
            var shown = new int[slots];
            for (int i = 0; i < slots; i++)
            {
                shown[i] = numbers != null && i < numbers.Count ? numbers[i] : -1;
            }

            return shown;
        }

        /// <summary>对战 HUD 中栏：单局编号（**恒为 1**，不写 `of 3` —— 本工程是一局定胜负）。</summary>
        public const string RackLabel = "RACK 1";

        /// <summary>对战 HUD 中栏大字：轮到玩家。</summary>
        public const string YourShotLabel = "YOUR SHOT";

        /// <summary>对战 HUD 中栏大字：轮到对手。</summary>
        public const string OpponentTurnLabel = "AI";

        /// <summary>组别小标签：全色球（玩家的 `Solids`）。</summary>
        public const string SolidGroupTag = "Solids";

        /// <summary>组别小标签：花色球（`Stripes`）。</summary>
        public const string StripeGroupTag = "Stripes";

        /// <summary>对手名（对战 HUD 右栏；与 `OpponentName(MatchSetup)` 方法区分）。</summary>
        public const string OpponentDisplayName = "Expert AI";

        /// <summary>`GroupState` → 组别小标签；**开放球局返回空串**（零字形，用户原话"一开始是空的"）。</summary>
        public static string GroupTag(GroupState group)
        {
            switch (group)
            {
                case GroupState.Solid: return SolidGroupTag;
                case GroupState.Stripe: return StripeGroupTag;
                default: return string.Empty;
            }
        }

        public const string LabelBallsLeft = "BALLS LEFT";

        /// <summary>玩家名下的一行实时统计标签（与 `FOULS` 同处一行）。</summary>
        public const string LabelShots = "SHOTS";

        /// <summary>记分牌中央大数字上方的标签（`pocketed / total` 的语义）。</summary>
        public const string LabelPocketed = "POCKETED";

        /// <summary>犯规统计标签（结算卡与记分牌同一用词）。</summary>
        public const string LabelFouls = "FOULS";

        /// <summary>力度块上方的标签。</summary>
        public const string LabelPower = "POWER";

        /// <summary>玩家名（设计稿记分牌左栏）。</summary>
        public const string LabelYou = "You";

        /// <summary>主按钮文案（设计稿 `Shoot`）。</summary>
        public const string ButtonShoot = "Shoot";

        // ── 设置面板（返回俱乐部 / 退出 / 音量）──────────────────────────────
        /// <summary>设置按钮文案。⚠ 工程无齿轮图标（16 个图标槽里没有），故用文本；补图标后只换 glyph。</summary>
        public const string ButtonSettings = "SET";
        /// <summary>设置面板标题。</summary>
        public const string TitleSettings = "SETTINGS";
        /// <summary>回首页。</summary>
        public const string ButtonBackToClub = "Back to club";
        /// <summary>退出游戏（出包后生效；编辑器内只记日志）。</summary>
        public const string ButtonQuit = "Quit";
        /// <summary>关闭设置面板。</summary>
        public const string ButtonClose = "Close";
        /// <summary>音量行标签。</summary>
        public const string LabelVolume = "Volume";

        // ── 操作引导（首次进入时的一次性引导）──────────────────────────────
        /// <summary>引导步数（文案与图标都由 <see cref="OnboardingTitle"/> / <see cref="OnboardingBody"/> 单点给出）。</summary>
        public const int OnboardingStepCount = 4;

        /// <summary>引导结束按钮。</summary>
        public const string OnboardingDone = "Got it";

        /// <summary>引导下一步按钮。</summary>
        public const string OnboardingNext = "Next";

        /// <summary>第 <paramref name="index"/> 步标题（越界 ⇒ 空串，**不抛**）。</summary>
        public static string OnboardingTitle(int index)
        {
            switch (index)
            {
                case 0: return "AIM";
                case 1: return "SHOOT";
                case 2: return "BALL IN HAND";
                case 3: return "SETTINGS";
                default: return string.Empty;
            }
        }

        /// <summary>第 <paramref name="index"/> 步正文。**只描述真实存在的操作**（界面不许有假陈述）。</summary>
        public static string OnboardingBody(int index)
        {
            switch (index)
            {
                case 0: return "Drag with the left mouse button to aim.\nThe white line shows where the cue ball will go.";
                case 1: return "Hold the right mouse button to build power.\nRelease to shoot.";
                case 2: return "If the cue ball is potted, click the table\nto place it back.";
                case 3: return "Use SET at the top right to return to the club,\nquit, or change the volume.";
                default: return string.Empty;
            }
        }

        /// <summary>"已看过引导"的存档键（**带版本号**：改引导内容时升版，老玩家可再看一次）。</summary>
        public const string OnboardingSeenKey = "billiards.onboarding.seen.v1";

        /// <summary>品牌副标题（设计稿 `brand small` 的位置）。</summary>
        public const string BrandEyebrow = "PRACTICE";

        /// <summary>品牌主标题（设计稿 wordmark）。</summary>
        public const string BrandTitle = "POOL ROOM";

        /// <summary>品牌球徽章上的号码（八号球徽章，设计稿 §一「搪瓷八号球徽章」）。</summary>
        public const string BrandBallNumber = "8";

        /// <summary>模式切换的两项（设计稿 `mode-switch`）。</summary>
        public const string ModePractice = "Practice";
        public const string ModeAiMatch = "AI Match";

        /// <summary>出杆栏右侧的信息位标题（设计稿 `shot-info span`）。</summary>
        public const string LabelYourShot = "YOUR SHOT";

        /// <summary>图标按钮的提示文案（`T97` 字符覆盖检查需要它们出现在字符集里）。</summary>
        public const string TooltipVfx = "VFX";
        public const string TooltipSfx = "SFX";

        // ── 开始界面（首页，参考稿 `04_UI/Reference_Screens/home-1920x1080.png`）────
        // ⚠ **全英文、且只用 ASCII + `·`（U+00B7）**：美术包两套字体只覆盖 Latin 子集。
        //   实测（读 5 个源 TTF 的 cmap）：`·` 五个字体都有；`♫`（U+266B）**五个字体都没有**
        //   ⇒ 参考稿里的曲目行图标**不能**写成 `♫` 字符，只能用已有的 `Volume` 图标精灵
        //   （见 `HomeView` 的类注释"与参考稿的偏离"）。

        /// <summary>首页左上俱乐部名。</summary>
        public const string HomeClubName = "THE EIGHT BALL CLUB";

        /// <summary>首页左上俱乐部副标（中间的 `·` 是 U+00B7，5 个源字体全覆盖）。</summary>
        public const string HomeClubSub = "PRIVATE TABLE · NO. 35";

        /// <summary>首页标题上方的小标（黄铜 eyebrow）。</summary>
        public const string HomeEyebrow = "8-BALL CLUB";

        /// <summary>首页标题行里的台号。</summary>
        public const string HomeTableNumber = "TABLE 01";

        /// <summary>首页难度区的小标签。</summary>
        public const string HomeDifficultyLabel = "OPPONENT";

        /// <summary>首页难度分段的三项（`Club` 为参考稿的选中项）。</summary>
        public const string HomeDifficultyCasual = "Casual";
        public const string HomeDifficultyClub = "Club";
        public const string HomeDifficultyExpert = "Expert";

        /// <summary>首页底部统计第三项的标签（前两项复用 `SHOTS` / `POCKETED`）。</summary>
        public const string HomeStatRacks = "RACKS";

        /// <summary>首页右下角台号牌（标签 + 大号号码）。</summary>
        public const string HomeHouseLabel = "HOUSE TABLE";
        public const string HomeHouseNumber = "01";

        /// <summary>首页右下角曲目名（图标另走 `Volume` 精灵，**不是** `♫` 字符）。</summary>
        public const string HomeTrackTitle = "Late Set";

        // ── P5-B：赛事设置屏（AI 对战）的文案 ───────────────────────────────────
        //
        // 全部英文（字体只有 Latin 子集 ⇒ 否则 `T97 (c)` 红），分隔符一律 `·`（U+00B7，5 个源字体实测全覆盖）。
        // 文案来源：`04_UI/Web_Implementation_Reference/index.html:72-79`（`#match-intro-dialog`）——
        // 参考稿的 `<h2>Tonight's match</h2>`、`You`、`VS`、`Start match`、`Back to club` 逐字采用。
        // ⚠ **赛制那两句刻意偏离参考稿**（`BEST OF THREE` / `First to two racks` = 三局两胜）：
        //   本工程的赛制是**一局定胜负**（35-C D4 已砍掉比赛循环）⇒ 照抄参考稿就是**假陈述**。

        /// <summary>赛前屏大标题（参考稿 `#match-intro-dialog h2` 逐字）。</summary>
        public const string MatchIntroTitle = "Tonight's match";

        /// <summary>己方名（参考稿逐字）。</summary>
        public const string MatchIntroYou = "You";

        /// <summary>对阵分隔（参考稿逐字）。</summary>
        public const string MatchIntroVersus = "VS";

        /// <summary>
        /// 赛制标题。⚠ **不是**参考稿的 `BEST OF THREE`（原文照抄会是**假陈述**）：
        /// 本工程的赛制是**一局定胜负**（35-C D4 已砍掉比赛循环 / 比分）——
        /// 参考稿的 `Best of three` + `First to two racks` 描述的是**三局两胜**，
        /// 界面上写着"三局两胜"而实际打一局就结算 = 界面在撒谎（本工程铁律）。
        /// </summary>
        public const string MatchIntroFormatTitle = "ONE RACK";

        /// <summary>赛制说明（与 <see cref="MatchIntroFormatTitle"/> 同一口径：一局定胜负）。</summary>
        public const string MatchIntroFormatDetail = "Win this rack to win the match";

        /// <summary>开始按钮（参考稿逐字）。</summary>
        public const string MatchIntroStart = "Start match";

        /// <summary>返回按钮（参考稿逐字）。</summary>
        public const string MatchIntroBack = "Back to club";

        /// <summary>对手名后缀（参考稿 `Club AI` 的形状 = `<difficulty> AI`）。</summary>
        public const string OpponentNameSuffix = " AI";

        /// <summary>
        /// **对手回合点击被拒**时的可见提示（P5-B 用户要求"不许静默"）。
        /// 与 `AiTurnDetail`（"Opponent is taking its shot"，常驻态说明）**刻意不同**：
        /// 那句描述**状态**，这句回应**玩家刚刚的那次点击**（"你点了，但现在不是你的回合"）。
        /// </summary>
        public const string OpponentBlockedDetail = "Opponent is shooting · you can aim when it's your turn";

        /// <summary>
        /// **结算面板打开期间，"对手回合被拒"的可见提示是否允许触发**（P6 第 2 条的**显式优先级**）。
        ///
        /// 答案：**不允许**。理由（写成一句话，免得下一个人重排优先级）：
        ///  · 结算面板打开时**出杆输入已被整体让位**（`PracticeResultView.IsOpen` 是那四个输入判据点的既有单点）
        ///    ⇒ "因为轮到对手而被拒"这件事**在此刻不可能发生**；
        ///  · 若仍有 pending（例如面板恰在那一帧弹出），**一律丢弃**、**不排队补弹** ——
        ///    否则面板关掉后会冒出一条"过期提示"，两套反馈互相打架。
        ///
        /// 抽成纯函数是为了让这条**优先级**可被判据直接驱动（不必起场景、不必跑帧）。
        /// </summary>
        public static bool OpponentNoticeAllowed(bool settlementOpen)
        {
            return !settlementOpen;
        }

        /// <summary>三档难度的显示名 —— **复用首页分段的既有常量**（同一档位在界面上只有一个名字）。</summary>
        public static string TierLabel(AiDifficulty tier)
        {
            switch (tier)
            {
                case AiDifficulty.Casual: return HomeDifficultyCasual;
                case AiDifficulty.Expert: return HomeDifficultyExpert;
                default: return HomeDifficultyClub;
            }
        }

        /// <summary>对手名 = `<档位> AI`（参考稿 `Club AI` 的形状）。</summary>
        public static string OpponentName(AiDifficulty tier)
        {
            return TierLabel(tier) + OpponentNameSuffix;
        }

        // ════════════════ 状态 → 文案 / 配色 / 图标（唯一实现点）═════════════════

        /// <summary>`data-state` 契约 token（判据 `T125` 用它把枚举钉在设计契约上）。</summary>
        public static string StateToken(HudVisualState state)
        {
            switch (state)
            {
                case HudVisualState.Aim: return "aim";
                case HudVisualState.AiTurn: return "ai-turn";
                case HudVisualState.Moving: return "moving";
                case HudVisualState.BallInHand: return "ball-in-hand";
                case HudVisualState.CallPocket: return "call-pocket";
                case HudVisualState.Foul: return "foul";
                case HudVisualState.Paused: return "paused";
                case HudVisualState.Result: return "result";
                default: return "aim";
            }
        }

        /// <summary>状态标题（设计说明 §四把每个 token 的语义写死在这里）。</summary>
        public static string StateTitle(HudVisualState state)
        {
            switch (state)
            {
                case HudVisualState.Aim: return StateAim;
                case HudVisualState.AiTurn: return StateAiTurn;
                case HudVisualState.Moving: return StateBallsMoving;
                case HudVisualState.BallInHand: return StateBallInHand;
                case HudVisualState.CallPocket: return StateCallPocket;
                case HudVisualState.Foul: return StateFoul;
                case HudVisualState.Paused: return StatePaused;
                case HudVisualState.Result: return StateResult;
                default: return StateAim;
            }
        }

        /// <summary>状态说明（长句，跟在标题后面）。</summary>
        public static string StateDetail(HudVisualState state)
        {
            switch (state)
            {
                case HudVisualState.Aim: return CaptionIdle;
                case HudVisualState.AiTurn: return AiTurnDetail;
                case HudVisualState.Moving: return RefusedDetail;
                case HudVisualState.BallInHand: return BallInHandDetail;
                case HudVisualState.CallPocket: return CallPocketDetail;
                case HudVisualState.Foul: return FoulDetail;
                case HudVisualState.Paused: return PausedDetail;
                case HudVisualState.Result: return ResultDetail;
                default: return CaptionIdle;
            }
        }

        /// <summary>
        /// **状态条"说明"行的最终文案**（纯函数 ⇒ 判据可直接驱动，不必起视图）。
        ///
        /// 为什么要有这一层：`StateDetail(state)` 只按状态取文案，但自由球**有三个子情形**
        /// （待放球 / 已回到台上 / **落点被拒**），它们的说明各不相同却共用同一档状态与配色
        /// ⇒ 选择逻辑必须有个**可被判据钉住的单一实现点**，否则"哪句优先"只活在视图里、没人守。
        ///
        /// ── 优先级（定死在这里，逐条有理由）────────────────────────────────────────
        /// ① **落点被拒**最优先：它是玩家**刚刚做了动作、没成功**的即时反馈，而默认文案会给出
        ///    **假陈述**（落点时台面是静止的，`"Wait for the table"` 不成立）⇒ 必须压过其它。
        /// ② **待放球**次之：白球在手上时最该说的是"怎么把它放下"（含 `Esc` 兜底键）。
        /// ③ 其余回落到 `StateDetail(state)`（既有行为**逐字不变**）。
        /// </summary>
        public static string CaptionLine(HudVisualState state, bool awaitingPlacement, bool placementRefused,
                                         bool opponentBlocked = false)
        {
            // ⚠ 优先级：**对手回合被拒**排在最前 —— 它是玩家**刚刚做了动作、被拒**的即时反馈，
            //   而其余两句描述的是"当前是什么状态"（状态说明随时都能看到，这一次点击的解释错过就没了）。
            if (opponentBlocked)
            {
                return OpponentBlockedDetail;
            }

            if (placementRefused)
            {
                return PlacementRefusedDetail;
            }

            // **犯规 + 待放球**（P31 修 4；**P32 只更新理由，分支与行为一字未改**）：
            // 白球落袋那一刻，"犯规窗口（1.6 s）"与"待放球"**同时成立**，而 `ResolveVisualState`
            // 的优先级让犯规档压过自由球档 ⇒ 说明行默认会落到 `StateDetail(Foul)` = `FoulDetail`。
            // `FoulDetail` 在 P32 已按裁定改成不含位置主张的 `"Foul"`（假陈述已消失），但它**不可操作** ——
            // 而这一刻玩家最需要的是"怎么把球放下"（含 `Esc` 兜底键）。
            // ⇒ 这一档回落成 `BallInHandPendingDetail`：**可操作性优先于复述状态**。
            if (state == HudVisualState.Foul && awaitingPlacement)
            {
                return BallInHandPendingDetail;
            }

            if (state == HudVisualState.BallInHand && awaitingPlacement)
            {
                return BallInHandPendingDetail;
            }

            return StateDetail(state);
        }

        /// <summary>状态条边线色（设计契约逐条取自 `style.css` 的 `.status-bar[data-state=…]`）。</summary>
        public static Color32 EdgeColor(HudVisualState state)
        {
            switch (state)
            {
                case HudVisualState.Aim: return ClubUiTokens.AimEdge;
                case HudVisualState.AiTurn: return ClubUiTokens.AiTurnEdge;
                case HudVisualState.Moving: return ClubUiTokens.MovingEdge;
                case HudVisualState.BallInHand: return ClubUiTokens.BallInHandEdge;
                case HudVisualState.CallPocket: return ClubUiTokens.CallPocketEdge;
                case HudVisualState.Foul: return ClubUiTokens.FoulEdge;
                case HudVisualState.Paused: return ClubUiTokens.PausedEdge;
                case HudVisualState.Result: return ClubUiTokens.ResultEdge;
                default: return ClubUiTokens.AimEdge;
            }
        }

        /// <summary>状态图标（与边线）的着色 —— 同一状态只有一个指示色。</summary>
        public static Color32 SymbolColor(HudVisualState state)
        {
            switch (state)
            {
                case HudVisualState.Aim: return ClubUiTokens.AimSymbol;
                case HudVisualState.AiTurn: return ClubUiTokens.AiTurnSymbol;
                case HudVisualState.Moving: return ClubUiTokens.MovingSymbol;
                case HudVisualState.BallInHand: return ClubUiTokens.BallInHandSymbol;
                case HudVisualState.CallPocket: return ClubUiTokens.CallPocketSymbol;
                case HudVisualState.Foul: return ClubUiTokens.FoulSymbol;
                case HudVisualState.Paused: return ClubUiTokens.PausedSymbol;
                case HudVisualState.Result: return ClubUiTokens.ResultSymbol;
                default: return ClubUiTokens.AimSymbol;
            }
        }

        /// <summary>状态条底色（只有 `foul` 在设计里换了底色，其余一律 <see cref="ClubUiTokens.PanelStatus"/>）。</summary>
        public static Color32 FillColor(HudVisualState state)
        {
            return state == HudVisualState.Foul ? ClubUiTokens.FoulFill : ClubUiTokens.PanelStatus;
        }

        /// <summary>
        /// 本卡**无法触发**的状态（显式登记，防"悄悄少做/悄悄多做"）。
        ///
        /// ── ⚠ **P6 两小步重钉（按事实，不是删掉）**──────────────────────────────────
        /// · **`ai-turn` 已移出**（P6 第 2 小段）：真值源 = `Billiards.Physics.PhysicsTestDrive.IsAiTurn`
        ///   （`= CurrentTurn == ShotTurn.Ai`），施加口 = `SetTurn(ShotTurn)` / `ApplyVerdict(in RuleVerdict)`，
        ///   状态条出口 = <see cref="ResolveVisualState"/> 的 `aiTurn` 入参（由 `PracticeHudView` 喂）。
        ///   旧理由「本卡无 AI」**已是假陈述**。
        /// · **`call-pocket` 重新变为不可达**（**用户拍板取消 D3 指定袋**：黑 8 进任意袋都有效）——
        ///   产品侧出口已**全部删除**（`IsEightBallShot` / `TryCallPocket` / `CalledPocket` /
        ///   `RefusedCallPocketCount` / `ShotOutcomeKind.EightBallWrongPocket`）⇒ 没有任何东西能产出它。
        ///   **契约先行**：状态、配色、图标、标题与说明仍定义齐全（将来若要恢复"指定袋"不必改这里）。
        /// · **`paused` 仍不可达**：本卡**确实没有**暂停系统（没有任何暂停入口或状态真源）。
        /// </summary>
        public static readonly HudVisualState[] UnreachableStates =
        {
            HudVisualState.CallPocket, // D3 已按用户要求取消（黑 8 任意袋有效）⇒ 该状态不可达
            HudVisualState.Paused,     // 本卡没有暂停系统
        };

        /// <summary>
        /// 犯规边线的**脉冲时长 0.8 s**（设计说明 §9 逐字："0.8 秒克制脉冲"）。
        ///
        /// 与犯规**提示窗口**（`PracticeHudView.FoulBannerSeconds` = 1.6 s）刻意分开：
        /// 脉冲是"让你注意到"，停留是"让你读得完"。前 0.8 s 做脉冲、余下 0.8 s 停在稳定态。
        /// 也和 VFX 侧的犯规环时长（0.38 s，在球上）是三件不同的事。
        /// </summary>
        public const float FoulPulseSeconds = 0.8f;

        /// <summary>
        /// **脉冲混合量（纯函数）**：`elapsed ∈ [0, FoulPulseSeconds]` → `1 → 0` 的 ease-out 曲线。
        /// 0 = 最亮的酒红（`#d8888f`）、1 = 稳定态边线（`#b66370a0`）。
        /// 超过脉冲窗口一律返回 1（稳定态），**不循环** —— 设计稿的 `@keyframes … 100%` 是一次性的。
        /// </summary>
        public static float FoulPulseBlend(float elapsedSeconds)
        {
            if (!(elapsedSeconds > 0f))
            {
                return 0f;
            }

            if (elapsedSeconds >= FoulPulseSeconds)
            {
                return 1f;
            }

            float p = elapsedSeconds / FoulPulseSeconds;
            return Mathf.SmoothStep(0f, 1f, p);
        }

        /// <summary>某状态在本卡是否可达（判据与视图都读它，不各写一份 switch）。</summary>
        public static bool IsReachable(HudVisualState state)
        {
            for (int i = 0; i < UnreachableStates.Length; i++)
            {
                if (UnreachableStates[i] == state)
                {
                    return false;
                }
            }

            return true;
        }

        // ════════════════ 状态求解（纯函数，唯一实现点）════════════════════════

        /// <summary>
        /// **把可观测的运行时事实解析成唯一的视觉状态**（纯函数 ⇒ 判据可复现）。
        ///
        /// 优先级：`result` &gt; 被拒 &gt; 犯规 &gt; 自由球 &gt; 相位。
        /// </summary>
        /// <param name="phase">击球流程相位（<see cref="ShotPhase"/>）。</param>
        /// <param name="foulWindow">犯规提示窗口内（由 `ScratchCount` 上升沿驱动）。</param>
        /// <param name="refusedWindow">出杆被拒窗口内（由 `RefusedLaunchCount` 上升沿驱动）。</param>
        /// <param name="ballInHand">白球刚被自动归位、玩家尚未出杆（`LastLaunchRespawnedCueBall` 驱动）。</param>
        /// <param name="resultOpen">结算卡已弹出。</param>
        /// <param name="awaitingPlacement">
        /// **待放球**（"全场自由球"新规则）：白球**不在台上**、轮到玩家、台面已停 ⇒ 它等着被**放下**。
        /// 由既有只读出口 `PoolSimulator.IsBallInPlay(0)` 直接给出，**不需要物理侧新增任何状态**。
        /// 默认 `false` ⇒ 既有调用点（含判据 `T96` / `T125` 的优先级表）**行为逐位不变**。
        /// </param>
        /// <param name="aiTurn">
        /// **轮到对手**（P6 新接线）：真值源 = `Billiards.Physics.PhysicsTestDrive.IsAiTurn`
        /// （`= CurrentTurn == ShotTurn.Ai`；施加口是 `SetTurn` / `ApplyVerdict`）。
        /// 默认 `false` ⇒ 既有调用点与判据**行为逐位不变**。
        ///
        /// ── 优先级为什么排在**残余窗口之后、相位回落之前**（唯一一处，别重排）──────────
        /// `被拒` / `犯规` / `自由球` 三族是**残余窗口**：它们解释的是"玩家刚做的那一下"
        /// （犯规窗口 1.6 s，**经常正好跨过换手那一刻**）。若把 `ai-turn` 排到它们前面，
        /// 玩家犯规后换手 ⇒ 犯规反馈会被**吞掉**（那是既有判据 `T126` / `T104` 守住的行为）。
        /// 而"相位回落"那一层（`Aim` / `Moving`）才是**"现在轮到谁"的常态显示** ——
        /// `ai-turn` 只替换这一层：**对手在打的时候，状态条不许再说 `Aim`**（那是假陈述）。
        /// </param>
        public static HudVisualState ResolveVisualState(ShotPhase phase, bool foulWindow, bool refusedWindow,
                                                        bool ballInHand, bool resultOpen,
                                                        bool awaitingPlacement = false, bool aiTurn = false)
        {
            if (resultOpen)
            {
                return HudVisualState.Result;
            }

            if (refusedWindow)
            {
                return HudVisualState.Moving;
            }

            if (foulWindow)
            {
                return HudVisualState.Foul;
            }

            if (ballInHand || awaitingPlacement)
            {
                return HudVisualState.BallInHand;
            }

            if (aiTurn)
            {
                return HudVisualState.AiTurn;
            }

            return IsPlayerTurn(phase) ? HudVisualState.Aim : HudVisualState.Moving;
        }

        /// <summary>该相位是否属于"玩家可操作"（`Aim` / `Charging`）。</summary>
        public static bool IsPlayerTurn(ShotPhase phase)
        {
            return phase == ShotPhase.Aim || phase == ShotPhase.Charging;
        }

        /// <summary>
        /// **状态条该显示什么**（保留的旧入口）：犯规窗口内优先显示犯规，否则按流程相位。
        /// 内部就是 <see cref="ResolveVisualState"/> + <see cref="StateTitle"/>（单点实现，铁律 16）。
        /// </summary>
        public static string StatusLabel(ShotPhase phase, bool foulWindow)
        {
            return StatusLabel(phase, foulWindow, false);
        }

        /// <summary>旧入口（含"被拒"参数），语义与 <see cref="ResolveVisualState"/> 完全一致。</summary>
        public static string StatusLabel(ShotPhase phase, bool foulWindow, bool refusedWindow)
        {
            return StateTitle(ResolveVisualState(phase, foulWindow, refusedWindow, false, false));
        }

        /// <summary>与 <see cref="StatusLabel(ShotPhase, bool, bool)"/> 配套的副标题（同一解析路径）。</summary>
        public static string StatusDetail(bool foulWindow, bool refusedWindow)
        {
            return StateDetail(ResolveVisualState(ShotPhase.Aim, foulWindow, refusedWindow, false, false));
        }

        /// <summary>旧入口：仅按相位的状态条文案。</summary>
        public static string StateLabel(ShotPhase phase)
        {
            return StateTitle(ResolveVisualState(phase, false, false, false, false));
        }

        /// <summary>`Shoot` 是否可用 —— **只在 `Aim` 态可用**（蓄力中/出杆中/球运动中/静止过渡都不可用）。</summary>
        public static bool ShootEnabled(ShotPhase phase)
        {
            return phase == ShotPhase.Aim;
        }

        /// <summary>力度（0–1，越界先夹紧）→ 整数百分比文案。</summary>
        public static string PowerLabel(float normalizedPower)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(normalizedPower) * 100f) + "%";
        }

        /// <summary>已进球 / 目标球总数。</summary>
        public static string PocketedLabel(int pocketed, int total)
        {
            return pocketed + " / " + total;
        }

        /// <summary>
        /// HUD 会出现的**全部字符**（字面量 + 动态片段），供 `T97 (c)` 的字符覆盖检查使用。
        /// 动态片段取边界值（`0%` / `100%` / 两位数计数）以覆盖数字与符号。
        ///
        /// ⚠ **新增任何界面文案都必须加到这里**：字体只覆盖 Latin 子集，
        /// 漏加会让"文案里混进了字体没有的字符"在实机上变成方框，而判据仍绿。
        /// </summary>
        public static string AllDisplayCopy()
        {
            string states = string.Empty;
            foreach (HudVisualState state in System.Enum.GetValues(typeof(HudVisualState)))
            {
                states = string.Concat(states, StateTitle(state), StateDetail(state), StateToken(state));
            }

            return string.Concat(
                states,
                StateAiTurn, StateCallPocket, StatePaused, StateResult,
                LabelBallsLeft, LabelShots, LabelPocketed, LabelFouls, LabelPower, LabelYou, ButtonShoot,
                BrandEyebrow, BrandTitle, BrandBallNumber,
                ModePractice, ModeAiMatch, LabelYourShot, TooltipVfx, TooltipSfx,
                // 首页：`POOL ROOM` / `AI Match` / `Practice` / `SHOTS` / `POCKETED` 与上面**同值复用**
                // （`BrandTitle` / `ModeAiMatch` / `ModePractice` / `LabelShots` / `LabelPocketed`）
                // ⇒ 这里只补首页独有的那些串，不重复列举（重复列举会让"同一句话有两处字面量"）。
                HomeClubName, HomeClubSub, HomeEyebrow, HomeTableNumber,
                HomeDifficultyLabel, HomeDifficultyCasual, HomeDifficultyClub, HomeDifficultyExpert,
                HomeStatRacks, HomeHouseLabel, HomeHouseNumber, HomeTrackTitle,
                // P5-B 赛前屏（AI 对战）文案 + 对手回合被拒的提示
                MatchIntroTitle, MatchIntroYou, MatchIntroVersus, MatchIntroFormatTitle,
                MatchIntroFormatDetail, MatchIntroStart, MatchIntroBack, OpponentNameSuffix,
                OpponentBlockedDetail,
                // 三档的对手名（`<档位> AI`）—— 由纯函数生成，故逐档列出（否则字符覆盖判据看不到它们）
                OpponentName(AiDifficulty.Casual),
                OpponentName(AiDifficulty.Club),
                OpponentName(AiDifficulty.Expert),
                // 自由球"待放球"与"落点被拒"两个子情形的说明
                //（`BallInHandDetail` 已由上面的 `states` 循环覆盖，不重复列举）
                BallInHandPendingDetail, PlacementRefusedDetail,
                // P6：结算卡**两形态**的标题 / 徽章 / 结局原因 + 两个键的文案
                // （全部由 `MatchResultCopy` 单点持有；`Back to club` 与上面的 `MatchIntroBack` **同值**、
                //   故不重复列举 —— 重复列举会让"同一句话有两个字面量"）
                MatchResultCopy.TitleWin, MatchResultCopy.TitleLose,
                MatchResultCopy.BadgePractice, MatchResultCopy.BadgeWin, MatchResultCopy.BadgeLose,
                MatchResultCopy.PlayAgainLabel, MatchResultCopy.ExportLabel,
                PowerLabel(0f), PowerLabel(0.45f), PowerLabel(1f),
                PocketedLabel(0, 15), PocketedLabel(15, 15),
                "0123456789");
        }
    }
}
