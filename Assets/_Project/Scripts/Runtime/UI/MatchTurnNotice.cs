namespace Billiards.UI
{
    /// <summary>
    /// **"对方回合不许静默"的可见提示出口**（P5-B，用户明确要求）。
    ///
    /// ── 解决的问题 ──────────────────────────────────────────────────────────────
    /// AI 回合期间玩家点击台面，**当前会什么都不发生**（输入被让位、界面上没有任何变化）——
    /// 玩家的体验是"游戏卡了/坏了"。用户要求：**必须给可见反馈**。
    ///
    /// ── 为什么是一个"出口"而不是直接在输入处弹提示 ────────────────────────────────
    /// ① **输入侧不是本组件的域**（`PointerAimController` / `PointerPowerController` 由 Runtime 侧维护）：
    ///    那边只做一件事 —— **判定"这次输入因为轮到对手而被拒"**，然后调用本类的
    ///    <see cref="RequestBlock"/>；**显示与计数全在这里**（单一实现点）。
    /// ② **判据不许读像素**（本工程既有铁律）：所以这里同时提供**可判定出口** ——
    ///    <see cref="BlockedCount"/> / <see cref="LastBlockedReason"/>，判据直接读它们。
    ///
    /// ── 边沿语义（为什么不在这里开时间窗）────────────────────────────────────────
    /// `RequestBlock` 只**记账 + 置一个 pending 位**；时间窗由**视图**用既有的
    /// `FoulBannerSeconds` 那套（`now < _xxxUntil`）去开 —— 与 `ScratchCount` / `RefusedLaunchCount` /
    /// `RefusedPlacementCount` 的轮询范式**逐字同形**（视图侧 `ConsumePending()` 取上升沿）。
    /// ⇒ 本类**不持有时间**、不依赖 `Time`，因此可以在 EditMode 里被直接驱动。
    /// </summary>
    public static class MatchTurnNotice
    {
        /// <summary>标准拒绝原因：**轮到对手**（输入的合法拒绝原因之一，逐字常量，便于判据字面量比对）。</summary>
        public const string ReasonOpponentTurn = "opponent-turn";

        /// <summary>累计被拒次数（**可判定出口**：判据读它，不读像素）。</summary>
        public static int BlockedCount { get; private set; }

        /// <summary>最近一次被拒的原因（<c>null</c> = 从未被拒）；不透传玩家输入内容，只透传**原因常量**。</summary>
        public static string LastBlockedReason { get; private set; }

        /// <summary>是否已有过可见反馈（= 至少被拒过一次）。</summary>
        public static bool HasFeedback => BlockedCount > 0;

        private static bool _pending;

        /// <summary>
        /// **Runtime 侧调用口**（逐字签名见 P5-B 报告）：
        /// **仅当**玩家输入被拒的原因是"轮到对手"时才调用；每次调用 = 一次可见反馈。
        /// </summary>
        public static void RequestBlock(string reason)
        {
            BlockedCount++;
            LastBlockedReason = reason;
            _pending = true;
        }

        /// <summary>
        /// 视图侧取**上升沿**（与 `ScratchCount` 那套同形）：返回 <c>true</c> 表示"自上次取走后又发生过一次"。
        /// 取走即清零 ⇒ 视图自己开时间窗，重复帧不会重复触发。
        /// </summary>
        public static bool ConsumePending()
        {
            if (!_pending)
            {
                return false;
            }

            _pending = false;
            return true;
        }

        /// <summary>清空（**判据专用**）：静态状态会跨用例残留，用例开头调一次保证起点干净。</summary>
        public static void Reset()
        {
            BlockedCount = 0;
            LastBlockedReason = null;
            _pending = false;
        }
    }
}
