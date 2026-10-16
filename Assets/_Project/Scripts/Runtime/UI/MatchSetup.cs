using Billiards.Rules;

namespace Billiards.UI
{
    /// <summary>
    /// **赛前设置的选择模型**（P5-B）—— 三档难度 `Casual / Club / Expert` 的**单一状态源**。
    ///
    /// ── 为什么是一个纯静态类、而不是视图里的字段 ──────────────────────────────────
    /// 判据必须能**不起场景**就驱动它（本工程既有的纯函数范式：`HomeLayout` 的几何、`CameraTransition`
    /// 的状态机、`HomeView.IsOpen` 的模态位）。视图只负责**渲染** `ActiveIndex` 与把点击转成 `Select(...)`。
    ///
    /// ── 与 Runtime 侧的**唯一真源**对齐（不新造第二份三档枚举）────────────────────
    /// 三档枚举就是 `Billiards.Rules.AiDifficulty`（已入库），**默认档**取
    /// `EightBallAi.DefaultDifficulty`（= `Club`，用户拍板）⇒ 本类**不写死**该值，避免两处漂移。
    /// ⚠ 判据 `P5-B①` 仍然会**字面量**断言"默认 = `Club`"：那是**契约**（用户拍板的那一档），
    /// 不是重复实现 —— 若有人把 `DefaultDifficulty` 改成别的档，该判据会红。
    ///
    /// ── 档位顺序（界面从左到右）────────────────────────────────────────────────
    /// `Tiers` 的**下标即界面次序**（`Casual | Club | Expert`），与参考稿 `index.html:28` 的
    /// `data-home-difficulty="casual"/"club"/"expert"` 逐项同序。
    /// </summary>
    public static class MatchSetup
    {
        /// <summary>界面次序（**下标即从左到右**）；与参考稿三档逐项同序。</summary>
        public static readonly AiDifficulty[] Tiers =
        {
            AiDifficulty.Casual,
            AiDifficulty.Club,
            AiDifficulty.Expert,
        };

        /// <summary>当前选中档（初值 = Runtime 侧的默认档 `EightBallAi.DefaultDifficulty` = `Club`）。</summary>
        public static AiDifficulty Selected { get; private set; } = EightBallAi.DefaultDifficulty;

        /// <summary>当前选中档在 <see cref="Tiers"/> 里的下标（= 分段控件该高亮哪一格）。</summary>
        public static int ActiveIndex => IndexOf(Selected);

        /// <summary>某档在界面次序里的下标；不在表内 ⇒ <c>-1</c>（**不静默回落到 0**，见 `Select` 的注释）。</summary>
        public static int IndexOf(AiDifficulty tier)
        {
            for (int i = 0; i < Tiers.Length; i++)
            {
                if (Tiers[i] == tier)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 选中某档（幂等：重复点同一档不改变任何状态）。
        /// ⚠ 不在 <see cref="Tiers"/> 内的档**直接忽略**（不写进去）—— 宁可保持原选中，
        /// 也不制造一个"选中了界面上没有的档"的不可见状态（那会让"选中态唯一"这条判据失去意义）。
        /// </summary>
        public static void Select(AiDifficulty tier)
        {
            if (IndexOf(tier) < 0)
            {
                return;
            }

            Selected = tier;
        }

        /// <summary>按下标取档；越界 ⇒ 返回 <see cref="Selected"/>（调用方无需自己夹取）。</summary>
        public static AiDifficulty TierAt(int index)
        {
            return index >= 0 && index < Tiers.Length ? Tiers[index] : Selected;
        }

        /// <summary>
        /// 回到**出厂默认**（= `EightBallAi.DefaultDifficulty`）。**判据专用**：静态状态会跨测试残留，
        /// 每条用例开头调它一次，避免"上一条用例把档位留在 Expert"造成假红/假绿。
        /// </summary>
        public static void ResetToDefault()
        {
            Selected = EightBallAi.DefaultDifficulty;
        }
    }
}
