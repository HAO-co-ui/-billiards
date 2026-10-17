using Billiards.Rules;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **结算卡的形态** —— 一局定胜负（35-C D4：比赛循环已砍掉）的两种结局。
    ///
    /// ── 为什么是一个枚举、而不是两个视图类（主理人裁定）──────────────────────────
    /// 结算卡是**同一张卡的两个形态**：形态只决定 **标题 / 徽章 / 配色 / 说明** 四处，
    /// 卡的**结构、几何、按钮与输入让位口径完全相同**。单卡让"同一时刻至多一张结算卡"
    /// 这条性质**结构上成立**（不可能画出来两张）；两个类只能靠"约定"维持它。
    ///
    /// ── `None` 的语义（练习局，**不是**"输了"）─────────────────────────────────
    /// `None` = 卡上显示的不是一场比赛的胜负（练习局的**清台** / **主动重摆**）——
    /// 标题与配色沿用 `RackOutcome` 的既有映射。⚠ **不得**把练习局的 `Rack ended` 说成 `Lose`：
    /// "玩家自己按了重摆"与"输掉一局"是两件事；把它算成负局是**假陈述**。
    ///
    /// ⚠ **本文本的字符口径**：全英文 + `·`（U+00B7）—— 美术包两套字体只覆盖 Latin 子集。
    /// 新增文案**必须**同时进 `PracticeHudState.AllDisplayCopy()`（判据 `T97 (c)` 据此查 5 个源 TTF 的 `cmap`）。
    /// </summary>
    public enum MatchResultForm
    {
        /// <summary>不是比赛结算（练习局的清台 / 主动重摆）。</summary>
        None = 0,

        /// <summary>玩家赢下本局（黄铜系）。</summary>
        Win = 1,

        /// <summary>玩家输掉本局（酒红系 —— 设计说明 §一：酒红只用于犯规与失败）。</summary>
        Lose = 2,
    }

    /// <summary>
    /// **结算卡两形态的单一实现点**（纯函数 / 纯数据，**不依赖场景、不读像素**）。
    ///
    /// ── 为什么文案与配色都要在这里、而不是散在视图里 ─────────────────────────────
    /// 派单要求"两形态必须在 **标题 + 配色令牌 + 文案** 三处都不同（不是只差一个词）"。
    /// 若把这四处写在视图的 `ApplyXxx()` 里，判据就只能起场景去读控件（本项目**明令禁止读像素**）；
    /// 收成纯函数后，判据可以直接驱动它们（与 `MatchSetup` / `PracticeHudState.CaptionLine`
    /// 的既有范式同形），而视图只做**渲染**。
    ///
    /// ── "为什么"那一行从哪来（**不许从球位反推**）─────────────────────────────
    /// 取 `Billiards.Physics.PhysicsTestDrive.EndReason`（= 规则层 `RuleVerdict.outcome` 的**原样映射**，
    /// 穷举枚举 `ShotOutcomeKind`）。UI **不做任何几何/球位推断** —— 那是规则层的唯一职责，
    /// 在 UI 里再推一遍必然与规则层分叉（铁律 16）。
    /// </summary>
    public static class MatchResultCopy
    {
        // ── 标题（两形态**不同**）───────────────────────────────────────────────

        /// <summary>胜局标题。</summary>
        public const string TitleWin = "Rack won";

        /// <summary>负局标题（与 <see cref="TitleWin"/> **不同**，不是同一个词换大小写）。</summary>
        public const string TitleLose = "Rack lost";

        // ── 徽章 chip（两形态也不同；`None` 用练习局的既有那枚）──────────────────

        /// <summary>练习局的徽章（照参考稿逐字，既有值）。</summary>
        public const string BadgePractice = "PRACTICE COMPLETE";

        /// <summary>胜局徽章。</summary>
        public const string BadgeWin = "MATCH WON";

        /// <summary>负局徽章。</summary>
        public const string BadgeLose = "MATCH LOST";

        // ── 按钮文案（与首页赛前屏**复用同一常量**，同一句话只有一处字面量）────────

        /// <summary>`Back to club`（复用赛前屏的既有常量 —— 同一个动作在界面上只有一个名字）。</summary>
        public const string BackToClubLabel = PracticeHudState.MatchIntroBack;

        /// <summary>`Play again`（与练习形态**同一个键**、同一句文案）。</summary>
        public const string PlayAgainLabel = "Play again";

        /// <summary>`Export data`（**练习形态**的次键：本工程既有的"一键导出"）。</summary>
        public const string ExportLabel = "Export data";

        // ════════════════ 形态 ↔ 胜者（判据的**主靶**）═══════════════════════════

        /// <summary>
        /// **胜者 → 形态**：玩家 ⇒ <see cref="MatchResultForm.Win"/>，对手 ⇒ <see cref="MatchResultForm.Lose"/>。
        /// 入参就是规则层的 <see cref="ShotTurn"/>（**不新增第二份"谁赢"的表示**）。
        /// </summary>
        public static MatchResultForm FormFor(ShotTurn winner)
        {
            return winner == ShotTurn.Player ? MatchResultForm.Win : MatchResultForm.Lose;
        }

        /// <summary>该形态是否"胜"（= 用黄铜系；否则酒红系）。`None` ⇒ <c>false</c>（练习局另走既有映射）。</summary>
        public static bool IsWin(MatchResultForm form)
        {
            return form == MatchResultForm.Win;
        }

        /// <summary>该 `ShotOutcomeKind` 是否**能**成为结算原因（穷举，无兜底）：只有 4 种结局杆。 </summary>
        public static bool IsEndingOutcome(ShotOutcomeKind kind)
        {
            switch (kind)
            {
                case ShotOutcomeKind.LegalEightBallPotted:
                case ShotOutcomeKind.EightBallPottedTooEarly:
                case ShotOutcomeKind.EightBallOffTable:
                    return true;   // D3 已取消：不再有"进错袋"这一种结局（集合由 4 → 3）

                case ShotOutcomeKind.EightBallRespot:   // 复位 ⇒ gameOver == false
                case ShotOutcomeKind.Ongoing:            // 未达成结局
                    return false;

                default:
                    // ⚠ 新增枚举值落到这里 ⇒ `T146 (c)` 会红（它逐值断言"非空原因 + 结局归类"），
                    //   这里的 `false` 只是**保守的假**（不会把未知结局谎称成结局），不是兜底实现。
                    return false;
            }
        }

        // ════════════════ 形态 → 文案 ═══════════════════════════════════════════

        /// <summary>
        /// 形态标题。⚠ `None` ⇒ **空串**（练习局走 `PracticeSession.OutcomeTitle`）——
        /// 刻意**不**回落到 `TitleLose`：那会把"玩家主动重摆"说成"输"，是假陈述。
        /// </summary>
        public static string Title(MatchResultForm form)
        {
            switch (form)
            {
                case MatchResultForm.Win: return TitleWin;
                case MatchResultForm.Lose: return TitleLose;
                default: return string.Empty;
            }
        }

        /// <summary>形态徽章（`None` ⇒ 练习局那枚，见 <see cref="BadgePractice"/>）。</summary>
        public static string Badge(MatchResultForm form)
        {
            switch (form)
            {
                case MatchResultForm.Win: return BadgeWin;
                case MatchResultForm.Lose: return BadgeLose;
                default: return BadgePractice;
            }
        }

        // ════════════════ 胜 / 负 → 配色令牌（**唯一实现点**）════════════════════
        //
        // 视图侧只准读这四个函数，不自己写二选一 —— 否则"两形态配色必须不同"这条
        // 只活在视图里（判据读不到），而 `T124 ④` 的"视图零颜色字面量"守卫也守不住
        // "从令牌里挑错了一个"。四个返回值的 win/lose 分岔**必须真的不同**（`T146 (b)` 逐对断言）。

        /// <summary>顶部 3 px 色条：胜 = 黄铜、负 = 酒红。</summary>
        public static Color32 EdgeColor(bool win)
        {
            return win ? ClubUiTokens.ResultWinEdge : ClubUiTokens.ResultLoseEdge;
        }

        /// <summary>圆徽章内图标：胜 = 黄铜、负 = 浅酒红。</summary>
        public static Color32 IconColor(bool win)
        {
            return win ? ClubUiTokens.Accent : ClubUiTokens.ResultLoseIcon;
        }

        /// <summary>圆徽章边线（环形位图）：胜 = 黄铜、负 = 半透明酒红。</summary>
        public static Color32 IconEdgeColor(bool win)
        {
            return win ? ClubUiTokens.ResultWinEdge : ClubUiTokens.ResultLoseIconEdge;
        }

        /// <summary>徽章 chip 边线：胜 = 黄铜浅描边、负 = 半透明酒红。</summary>
        public static Color32 BadgeEdgeColor(bool win)
        {
            return win ? ClubUiTokens.LineBadge : ClubUiTokens.ResultLoseBadgeEdge;
        }
    }
}
