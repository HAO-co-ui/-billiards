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
    /// ⚠ **第二个键换了功能**（规格 §4.3 已登记）：参考的 `Back to club` 在本工程无处可去（没有首页场景）
    /// ⇒ 改为 **`Export data`**，正好承接排期任务 3 的"一键导出"。
    /// </summary>
    public static class PracticeResultLayout
    {
        // ── 卡片（画布设计像素）────────────────────────────────────────────
        public const float CardWidthPx = 583f;
        public const float CardHeightPx = 635f;

        // ── 卡内两键（卡内比例，左下原点）─────────────────────────────────
        public static readonly Rect PlayAgainButtonLocal = new Rect(0.046f, 0.174f, 0.872f, 0.070f);
        public static readonly Rect ExportButtonLocal = new Rect(0.046f, 0.067f, 0.872f, 0.070f);

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
