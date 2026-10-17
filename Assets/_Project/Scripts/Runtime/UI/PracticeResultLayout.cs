using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **练习结算卡的布局单点**（纯函数，判据直接驱动）。
    ///
    /// ── 尺寸来源：从参考稿 `12-practice-result.png` **量**出来的，不是估的 ──────────
    /// 参考截图窗口 1080×675（16:10）。卡片矩形实测 `x∈[378, 706]`、`y∈[158, 515]`（左上原点）
    /// ⇒ **328 × 357 px**。截图是一张 1920 设计宽画布按 `1080/1920 = 0.5625` 缩放的产物
    /// ⇒ 换算回**画布设计像素**：`328 / 0.5625 = 583`、`357 / 0.5625 = 635`
    /// ⇒ **卡 = 583 × 635 设计 px**（竖长）。
    ///
    /// ── 为什么用"画布设计像素"而不是"视口比例"──────────────────────────────
    /// 视口比例会随宽高比拉伸卡片形状；而 `CanvasScaler` 的设计像素口径**与 HUD 字号同一套**
    /// （复用 <see cref="PracticeHudLayout.CanvasScale"/>），且 16:9 各档的画布**恒为 1920×1080**
    /// ⇒ 卡片在 720p 与 1080p 上**逐像素同构**，这正是"两档可读"想要的性质。
    ///
    /// ── 卡内两键（同样从参考稿量）────────────────────────────────────────────
    /// `Play again`：截图 `x∈[393, 679]`、`y∈[428, 453]`；`Back to club`：`y∈[466, 491]`。
    /// 换算成**卡内比例**（卡宽 328 / 卡高 357）得下方四个常量。
    /// ⚠ **第二个键的功能按形态分派**（P6；规格 §4.3 已登记，本句按事实重写过一次）：
    /// 参考稿的 `Back to club` **现在还真的能回首页**（`HomeSeams` 接缝 + `HomeView` 注册）；
    /// **比赛形态**用它（`Back to club`），**练习形态**仍用 `Export data`（历史记录的唯一 UI 导出入口）。
    /// ⇒ 旧文案「参考的 `Back to club` 在本工程无处可去（没有首页场景）」**已为假**，不得再被引用。
    /// 两个槽位（`PlayAgainButtonLocal` / `ExportButtonLocal`）的**几何**仍逐项照参考稿量，与本条无关。
    /// </summary>
    public static class PracticeResultLayout
    {
        // ── 卡片（画布设计像素）────────────────────────────────────────────
        public const float CardWidthPx = 583f;
        public const float CardHeightPx = 635f;

        // ── 卡内两键（卡内比例，左下原点）─────────────────────────────────
        public static readonly Rect PlayAgainButtonLocal = new Rect(0.046f, 0.174f, 0.872f, 0.070f);
        public static readonly Rect ExportButtonLocal = new Rect(0.046f, 0.067f, 0.872f, 0.070f);

        // ── 卡内其余元素（同样从参考稿量，比例口径与上面两键一致）──────────────
        /// <summary>顶部 3 px 主色/酒红条（设计稿 `#result-dialog { border-top: 3px solid }`）。</summary>
        public const float TopEdgeHeightPx = 3f;

        /// <summary>圆形徽章（奖杯环）：直径 = 参考稿 60 设计 px ÷ 卡宽 583。</summary>
        public static readonly Rect IconLocal = new Rect(0.4485f, 0.827f, 0.103f, 0.0945f);

        /// <summary>小标 chip（`PRACTICE COMPLETE`）。</summary>
        public static readonly Rect BadgeLocal = new Rect(0.350f, 0.712f, 0.300f, 0.045f);

        /// <summary>标题（`Rack ended` / `Table cleared`，Barlow 38 px）。</summary>
        public static readonly Rect TitleLocal = new Rect(0.06f, 0.550f, 0.88f, 0.118f);   // 0.550/0.118（=74.9 px）：38 px × FontScale 1.9 = 72.2 ⇒ 带高 ≥；顶边仍 0.668、与徽章(0.712)留 27.9 px、与"为什么"行(顶 0.540)留 6.4 px（T148(e)(f)）

        /// <summary>副标题（失败原因，11 px 静音灰）。</summary>

        /// <summary>三列统计的容器（内部再竖分"大数字 / 8 px 标签"）。</summary>
        public static readonly Rect StatsLocal = new Rect(0.06f, 0.294f, 0.88f, 0.151f);   // 0.300/0.145 → 0.294/0.151（=95.9 px）：① 数值带 0.58 ⇒ 55.6 px ≥ 27×1.9=51.3；② **上下留白**均为 **0.050（31.7 px）** —— 上接"为什么"行(底 0.495)、下接主键(顶 0.244)，用户要求"留白等差"（T148(e)）

        /// <summary>统计区上边线的卡内 y（设计稿 `.result-stats { border-top }`）。</summary>
        public const float StatsTopLineY = 0.465f;

        /// <summary>统计区下边线的卡内 y（设计稿 `.result-stats { border-bottom }`）。</summary>
        public const float StatsBottomLineY = 0.308f;

        /// <summary>出入的细线左右内缩（`0.06` = 卡宽的 6%）。</summary>
        public const float RuleInset = 0.06f;

        /// <summary>导出结果提示（toast）在卡内的矩形 —— 放在圆形徽章之上、顶部色条之下。</summary>
        public static readonly Rect ToastLocal = new Rect(0.06f, 0.936f, 0.88f, 0.040f);

        /// <summary>画布尺寸（**画布单位** = 屏幕像素 / 画布缩放）。</summary>
        public static Vector2 CanvasSize(int screenWidth, int screenHeight)
        {
            float scale = PracticeHudLayout.CanvasScale(screenWidth, screenHeight);
            if (!(scale > 0f))
            {
                return new Vector2(screenWidth, screenHeight);
            }

            return new Vector2(screenWidth / scale, screenHeight / scale);
        }

        /// <summary>卡片在视口中的矩形（居中）。</summary>
        public static Rect CardRect(int screenWidth, int screenHeight)
        {
            Vector2 canvas = CanvasSize(screenWidth, screenHeight);
            float width = CardWidthPx / canvas.x;
            float height = CardHeightPx / canvas.y;
            return new Rect(0.5f - width * 0.5f, 0.5f - height * 0.5f, width, height);
        }

        public static Rect PlayAgainButtonRect(int screenWidth, int screenHeight)
        {
            return LocalToViewport(CardRect(screenWidth, screenHeight), PlayAgainButtonLocal);
        }

        public static Rect ExportButtonRect(int screenWidth, int screenHeight)
        {
            return LocalToViewport(CardRect(screenWidth, screenHeight), ExportButtonLocal);
        }

        /// <summary>卡内比例矩形 → 视口矩形。</summary>
        public static Rect LocalToViewport(Rect card, Rect local)
        {
            return new Rect(
                card.xMin + local.xMin * card.width,
                card.yMin + local.yMin * card.height,
                local.width * card.width,
                local.height * card.height);
        }
    }
}
