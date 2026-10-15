using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **记分牌迷你球贴片清单的单一真源**（铁律 16）。16 个槽位（`ball-00` … `ball-15`）。
    ///
    /// ── 为什么要贴片，而不是把球的 1024×512 贴图缩小 ─────────────────────────
    /// 球贴图是**球面 UV 展开**（极点密、赤道疏），缩到 19 px 后号码会糊成一团；
    /// 记分牌要的是"一眼认出是几号球"的图形符号，不是球的真实外观。
    /// 设计稿的 `.mini-ball` 本身就是 CSS 画出来的（纯色圆 / 三段渐变圆），
    /// 本类对应的贴片按同一几何复刻 ⇒ 形态与设计稿一致。
    ///
    /// ── 纯色 / 花色的分界（设计契约）─────────────────────────────────────────
    /// 1–8 纯色、9–15 花色（`linear-gradient(#f2efe4 22%, var(--ball) 22%, var(--ball) 78%, #f2efe4 78%)`）。
    /// 判据 `T124` 用**顶部条带是不是中性灰**来验这条 ——
    /// 花色球的顶部 22% 是暖白（R≈G≈B），纯色球不是 ⇒ 这条断言**不能**靠"配色表对了"碰巧通过。
    ///
    /// ── 谁生成（可复现）──────────────────────────────────────────────────────
    /// `Docs/Tools/make_ball_chips.py`，色表取自美术包
    /// `03_Models_模型/Balls/Editable/README.md` 给的球色。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// · 号码是**白色直接压在球色上**（设计稿 `color: #fff` 即如此）。黄色 1/9 号球上
    ///   白字对比度偏低，19 px 显示时基本只是"有个白点"—— 这是**设计稿的既定取舍**，不是实现缺陷；
    ///   若要改，应作为设计变更提出，而不是在实现里偷偷加描边。
    /// · 贴片里烘了固定方向的明暗（右下内阴影 + 高光边，对应设计稿的 `box-shadow`）⇒
    ///   贴片**不能**再被旋转/翻转使用（uGUI 也不会）。
    /// </summary>
    public static class ClubBallChips
    {
        /// <summary>贴片资产目录。</summary>
        public const string Directory = "Assets/_Project/UI/Balls";

        /// <summary>球号 0（白球）到 15（花色 15 号），共 16 个槽位。</summary>
        public const int Count = 16;

        /// <summary>花色球的下界（含）。</summary>
        public const int FirstStriped = 9;

        /// <summary>花色球的上界（含）。</summary>
        public const int LastStriped = 15;

        /// <summary>
        /// **球色表**（序号 = 球号）。取自美术包 README，供判据与将来的调色板工具读 ——
        /// 视图**不**用它上色（颜色已经烘在贴片里），避免"两处各自决定球是什么颜色"。
        /// </summary>
        public static readonly Color32[] Palette =
        {
            new Color32(0xF5, 0xF3, 0xE7, 0xFF),   // 0 白球（米白）
            new Color32(0xE4, 0xB5, 0x2A, 0xFF),   // 1 黄
            new Color32(0x25, 0x6D, 0xC3, 0xFF),   // 2 蓝
            new Color32(0xB9, 0x32, 0x31, 0xFF),   // 3 红
            new Color32(0x77, 0x43, 0x9C, 0xFF),   // 4 紫
            new Color32(0xE8, 0x79, 0x26, 0xFF),   // 5 橙
            new Color32(0x1F, 0x93, 0x61, 0xFF),   // 6 绿
            new Color32(0x85, 0x3A, 0x35, 0xFF),   // 7 深红棕
            new Color32(0x11, 0x1B, 0x20, 0xFF),   // 8 黑
            new Color32(0xE4, 0xB5, 0x2A, 0xFF),   // 9 黄（花色）
            new Color32(0x25, 0x6D, 0xC3, 0xFF),   // 10 蓝（花色）
            new Color32(0xB9, 0x32, 0x31, 0xFF),   // 11 红（花色）
            new Color32(0x77, 0x43, 0x9C, 0xFF),   // 12 紫（花色）
            new Color32(0xE8, 0x79, 0x26, 0xFF),   // 13 橙（花色）
            new Color32(0x1F, 0x93, 0x61, 0xFF),   // 14 绿（花色）
            new Color32(0x85, 0x3A, 0x35, 0xFF),   // 15 深红棕（花色）
        };

        /// <summary>该球号是否花色球。</summary>
        public static bool IsStriped(int number)
        {
            return number >= FirstStriped && number <= LastStriped;
        }

        /// <summary>球色（越界返回白球色，**不抛**）。</summary>
        public static Color32 PaletteColor(int number)
        {
            return number >= 0 && number < Palette.Length ? Palette[number] : Palette[0];
        }

        /// <summary>
        /// **资产基名**（不含扩展名），例如 `ball-07`。
        /// ⚠ 与 `Sprite.name` 同口径：Unity 的 `Sprite.name` **不带扩展名**
        /// （拿带 `.png` 的名字去比会把正常精灵判成错位，见 `ClubUiSprites.BaseName` 的注释）。
        /// </summary>
        public static string BaseName(int number)
        {
            return "ball-" + number.ToString("00");
        }

        /// <summary>文件名（含扩展名），例如 `ball-07.png`。**只用于组磁盘路径**。</summary>
        public static string FileName(int number)
        {
            return BaseName(number) + ".png";
        }

        /// <summary>资产路径。</summary>
        public static string AssetPath(int number)
        {
            return Directory + "/" + FileName(number);
        }

        /// <summary>
        /// **判据的采样点**（归一化 UV，原点左下）：`(0.5, 0.90)` —— 球的正上方 40% 半径处。
        ///
        /// 为什么是这里：① 在球内（半径 30 / 32）；② 在号码之上（号码在中心 ±24%）；
        /// ③ 花色球的顶部 22% 暖白带覆盖它、纯色球没有 ⇒ **一个采样点就能分开纯色与花色**；
        /// ④ 烘进贴片的右下内阴影**照不到**这里 ⇒ 不受明暗干扰。
        /// </summary>
        public static readonly Vector2 TopBandSampleUv = new Vector2(0.5f, 0.90f);

        /// <summary>中性判据的容差（0–255）：花色球的顶部条带 R/G/B 三通道极差应 ≤ 它。</summary>
        public const int NeutralChannelTolerance = 20;

        /// <summary>槽位完整性检查（单点实现；场景构建器与判据共用）。</summary>
        public static bool ValidateSlots(Sprite[] chips, out string error)
        {
            error = null;

            if (chips == null)
            {
                error = "迷你球贴片数组为 null（视图未接线）。";
                return false;
            }

            if (chips.Length != Count)
            {
                error = $"迷你球贴片 {chips.Length} 个，应为 {Count} 个（白球 + 1–15 号）。";
                return false;
            }

            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i] == null)
                {
                    error = $"球号 {i} 的贴片为空。";
                    return false;
                }

                if (chips[i].name != BaseName(i))
                {
                    error = $"槽位 {i} 的贴片是 \"{chips[i].name}\"，应为 \"{BaseName(i)}\""
                            + "（槽位错位会让记分牌把球号显示成别的号码）。";
                    return false;
                }
            }

            return true;
        }

        /// <summary>按球号取贴片；越界返回 <c>null</c>。</summary>
        public static Sprite Resolve(Sprite[] chips, int number)
        {
            if (chips == null || number < 0 || number >= chips.Length)
            {
                return null;
            }

            return chips[number];
        }
    }
}
