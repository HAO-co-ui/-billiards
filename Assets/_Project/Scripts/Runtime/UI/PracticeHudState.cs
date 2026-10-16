using Billiards.Physics;
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
    /// ── 本卡可达 5 态、契约先行 3 态（如实登记）───────────────────────────────
    /// 35-A 是**单机练习卡**：没有 AI、没有规则引擎、没有暂停菜单 ⇒ `ai-turn` / `call-pocket` / `paused`
    /// **无法在本卡被触发**。它们仍然在枚举、配色、图标、文案里**全部定义齐全** ——
    /// 这样 35-B 接入 AI/规则/暂停时**不需要改这里**（也就不会出现"新增状态没人管配色"的半成品）。
    /// 不可达的三个在 <see cref="UnreachableStates"/> 里显式登记，判据 `T125` 据此断言
    /// "不可达集合既没被偷偷扩大、也没被偷偷缩小"。
    /// </summary>
    public enum HudVisualState
    {
        /// <summary>`aim` —— 轮到玩家、可以出杆。</summary>
        Aim = 0,

        /// <summary>`ai-turn` —— 对手回合。**35-A 不可达**。</summary>
        AiTurn = 1,

        /// <summary>`moving` —— 球在动 / 台面未静止（含"出杆被拒"）。</summary>
        Moving = 2,

        /// <summary>`ball-in-hand` —— 白球已归位、等你出杆。</summary>
        BallInHand = 3,

        /// <summary>`call-pocket` —— 指定球袋。**35-A 不可达**。</summary>
        CallPocket = 4,

        /// <summary>`foul` —— 犯规（本工程唯一的可观测犯规 = 白球落袋）。</summary>
        Foul = 5,

        /// <summary>`paused` —— 比赛冻结。**35-A 不可达**。</summary>
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

        /// <summary>`foul` 状态的处置说明 —— 本工程的实际处置是**白球放回开球点**。</summary>
        public const string FoulDetail = "Cue ball back on the spot";

        /// <summary>`ball-in-hand` 状态的说明（本工程是**自动归位**，非手动摆位，故如实这么写）。</summary>
        public const string BallInHandDetail = "Cue ball replaced at the spot";

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

        /// <summary>本卡**无法触发**的状态（显式登记，防"悄悄少做/悄悄多做"）。</summary>
        public static readonly HudVisualState[] UnreachableStates =
        {
            HudVisualState.AiTurn,     // 35-B：AI 回合
            HudVisualState.CallPocket, // 35-B：规则引擎的指定球袋
            HudVisualState.Paused,     // 本卡无暂停菜单
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
        public static HudVisualState ResolveVisualState(ShotPhase phase, bool foulWindow, bool refusedWindow,
                                                        bool ballInHand, bool resultOpen)
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

            if (ballInHand)
            {
                return HudVisualState.BallInHand;
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
                PowerLabel(0f), PowerLabel(0.45f), PowerLabel(1f),
                PocketedLabel(0, 15), PocketedLabel(15, 15),
                "0123456789");
        }
    }
}
