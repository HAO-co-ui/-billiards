namespace Billiards.UI
{
    /// <summary>
    /// **"回到首页"的接缝**（P6：结算卡上的 `Back to club` 真的能回到首页）。
    ///
    /// ── 为什么需要它（既有登记项被关闭）────────────────────────────────────────
    /// `PracticeResultView` 的类注释与 `HomeView` 的盲带登记里都写着：
    /// 「参考稿的 `Back to club` 在本工程**无处可去（没有首页/俱乐部场景）**⇒ 改为 `Export data`」
    /// 与「**"回到首页"没有实现**……属 35-B」。**首页已经落地** ⇒ 那两条**都已为假陈述**，
    /// 本接缝就是把它接上的落点（派单第 6 条：至少 `Back to club` + `Play again`）。
    ///
    /// ── 为什么是静态注册点、而不是在场景里拖引用（照 `MatchSeams.Host` 的既有范式）──
    /// 结算卡与首页**同挂 HUD 画布、但互不知情**（结算卡只需要"回到首页"这一个意图）。
    /// 拖引用要**改场景生成器并重建场景**（`PhysicsGreybox.unity` / `Main.unity` 都会动），
    /// 而静态注册点让两侧都不知道对方的存在，接线失败也**不 panic**（见 <see cref="TryShow"/>）。
    ///
    /// ── 未注册时的行为（与 `MatchSeams.TryStartMatch` 同口径）────────────────────
    /// 返回 <c>false</c> 且**什么都不做** ⇒ 调用方（结算卡）据此**保持卡片打开并打一条提示**，
    /// 而**不是**关掉卡片假装回去了 —— 界面承诺了"回到首页"就必须真的回去（本工程铁律）。
    /// </summary>
    public static class HomeSeams
    {
        /// <summary>由 <see cref="HomeView"/> 在 `Awake` 注册、在 `OnDisable` 注销（**只注销自己**）。</summary>
        public static System.Action Show { get; set; }

        /// <summary>是否已接线（判据/日志用）。</summary>
        public static bool HasHost => Show != null;

        /// <summary>
        /// 请求显示首页。**未接线时返回 <c>false</c> 且什么都不做**（调用方不得假装已回到首页）。
        /// </summary>
        public static bool TryShow()
        {
            if (Show == null)
            {
                return false;
            }

            Show();
            return true;
        }

        /// <summary>清空（**判据专用**）：静态状态会跨用例残留，用例开头调一次保证起点干净。</summary>
        public static void Reset()
        {
            Show = null;
        }
    }
}
