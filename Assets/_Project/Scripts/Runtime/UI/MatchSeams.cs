using Billiards.Rules;

namespace Billiards.UI
{
    /// <summary>
    /// **UI → Runtime 的开局入口**（P5-B 的接缝，形状由 UI 侧定义、由 Runtime 侧实现）。
    ///
    /// ── 为什么需要它（而不是让 UI 直接去 `new` 一个比赛）──────────────────────────
    /// "开始一局 AI 对战"要动的东西（摆 15 颗球、定先手、进入 AI 回合、记分）**全在 Runtime 侧**，
    /// 而赛前屏（本文件的实现方）只负责**收集用户意图**（哪一档）。
    /// ⇒ 两者之间只留一个**最小接缝**：UI 说"用 Club 档开始"，怎么开始由 Runtime 决定。
    ///
    /// ── 依赖方向的**唯一规则**（⚠ 别以为库里有两套方向）─────────────────────────
    /// **规则：接缝由调用方定义 —— 谁不能依赖谁，谁就定义接口。**
    /// 本工程已在两处实例上用到同一条规则，**两例不是相反的两套约定**：
    ///  · `Billiards.Core.ISmokeAimSweepTarget`：**调用方住在 `Core`**（冒烟执行体 `SmokeProbeRunner`），
    ///    它不能依赖驱动层 ⇒ 由 `Core` 定义、由 `Billiards.Physics` 实现；
    ///  · `Billiards.UI.IMatchHost`（本文件）：**调用方住在 `UI`**（赛前屏 `HomeView` 的 `Start match`），
    ///    它不能依赖比赛宿主住在哪一层 ⇒ 由 `UI` 定义、由 Runtime 侧实现。
    /// 补充事实（team-lead 查证）：本项目 Runtime 只有三个 asmdef（`Billiards.{Runtime, Editor, Diagnostics}`）
    /// ⇒ `Core` / `Physics` / `Rules` / `UI` **同属一个程序集**，这条"方向"是**命名空间约定**、不是编译期约束；
    /// 且 `Billiards.Physics` 早已在读 `Billiards.UI` 的状态（`AimAssistController.cs:131`、
    /// `CueStickController.cs:173`、`PointerAimController.cs:130`、`PointerPowerController.cs:120`、
    /// `PoolVfxView.cs:4`）⇒ UI 侧定义接缝并不会开一个"反向依赖"的先例。
    ///
    /// ── 未注册时的行为（重要）──────────────────────────────────────────────────
    /// 见 <see cref="MatchSeams.TryStartMatch"/>：**不假装开局**。
    /// </summary>
    public interface IMatchHost
    {
        /// <summary>
        /// **开始一局 AI 对战**（摆球、定先手、进入流程）。
        /// 由 Runtime 侧实现；调用方保证 <paramref name="tier"/> 是 <see cref="MatchSetup.Tiers"/> 内的档。
        /// </summary>
        void StartMatch(AiDifficulty tier);
    }

    /// <summary>
    /// <see cref="IMatchHost"/> 的**注册点**（照 `PlayViewCamera.homeOpenSource` / `SmokeProbe` 那类
    /// "宿主由上层注册、调用方只问接缝"的既有范式）。
    ///
    /// 为什么用一个静态注册点而不是场景里拖引用：赛前屏与比赛宿主**不同层级**（前者在 HUD 画布上，
    /// 后者可能挂在驱动器上），静态注册点让两侧**都不知道对方的存在**，接线失败也不 panic。
    /// </summary>
    public static class MatchSeams
    {
        /// <summary>Runtime 侧在 `Awake` 里注册（或置 `null` 注销）。</summary>
        public static IMatchHost Host { get; set; }

        /// <summary>是否已接线（判据/日志用）。</summary>
        public static bool HasHost => Host != null;

        /// <summary>
        /// 请求开局。**未接线时返回 <c>false</c> 且什么都不做** —— 调用方（赛前屏）据此
        /// **保持赛前屏打开并打一条告警**，而**不是**关掉首页假装已开局：
        /// 界面承诺了"开始一局"就必须真的开始，否则就是**假陈述**（本工程铁律）。
        /// </summary>
        public static bool TryStartMatch(AiDifficulty tier)
        {
            if (Host == null)
            {
                return false;
            }

            Host.StartMatch(tier);
            return true;
        }
    }
}
