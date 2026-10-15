using System.Text;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **界面精灵清单的单一真源**（铁律 16）。18 个槽位：13 个语义图标 + 5 个几何形状。
    ///
    /// ── 为什么会有一组 PNG（而不是矢量）─────────────────────────────────────
    /// 美术包的设计稿（`04_UI/Reference_Screens`）里所有图标都是**线性小图标**（准星 / 手掌 / 旗帜 /
    /// 警告 / 机器人 / 奖杯 / 房子…），原型用内联 SVG 渲染。本工程是 **uGUI**，没有矢量图形管线
    /// （`Image` 只能画位图）⇒ 把同一批图形**一次栅格化**成 64×64 透明 PNG 落成资产，
    /// 由 `Image.color` 上色。
    ///
    /// ── 纯白 + alpha 为什么是硬要求 ───────────────────────────────────────────
    /// 设计契约要求同一个图标在不同状态下换色（准星：`aim` 用桌布绿、`foul` 用酒红；奖杯：胜局黄铜、
    /// 负局酒红）。颜色烘进 PNG 就得为每种配色各出一份 ⇒ 图标数 × 配色数。
    /// 纯白 + alpha 让 **一份图形 × `ClubUiTokens` 的任一色** 都能用。
    ///
    /// ── 形状为什么也要在这里（`Disc` / `Ring` / `Panel1..3`）──────────────────
    /// 设计说明 §一 要求"控件边角保持 1—3 像素，不做现代 SaaS 式圆角卡片"，而 uGUI 的 `Image`
    /// **没有圆角属性**（只能靠九宫格位图）。圆徽章（品牌球、头像、迷你球、力度滑块、结算奖杯环）
    /// 更必须是真圆。这两类形状与图标一样是"界面共用的位图语言"，散在两处必然漂移 ⇒ 收进同一张清单。
    ///
    /// ── 谁生成（可复现）──────────────────────────────────────────────────────
    /// 生成脚本 `Docs/Tools/make_ui_sprites.py`（确定性：同一输入必得逐字节相同的 PNG）。
    /// 本类只登记**序号 ↔ 名称 ↔ 文件名 ↔ 精灵名**的对应，生成器与消费方都从这里读，不各写一份。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// 这些是**程序化线性近似**，不是 Lucide 图标的逐字还原（路径数据不可得）。形态语义一致
    /// （准星是准星、旗帜是旗帜），笔画粗细与端点与原型**不逐像素相同** —— 已登记。
    /// 九宫格圆角位图在**非整数倍缩放**下会有 ≤1 px 的重采样模糊（uGUI 九宫格的固有行为）。
    /// </summary>
    public static class ClubUiSprites
    {
        /// <summary>精灵资产目录（生成器输出目录、导入后处理器与判据都钉这里）。</summary>
        public const string Directory = "Assets/_Project/UI/Icons";

        /// <summary>栅格边长（px）。设计域里图标是 15–27 px，64 足够 4K 下不糊。</summary>
        public const int RasterSize = 64;

        /// <summary>拓展名（生成器输出、导入设置、判据共用）。</summary>
        public const string Extension = ".png";

        /// <summary>
        /// **槽位清单**。序号即 <see cref="UiSprite"/> 的值 —— 视图上的 `uiSprites` 数组按序号对位。
        ///
        /// ⚠ **只允许追加到末尾**：中间插入会让场景里已接好的槽位整体错位
        /// （判据 `T124` 用"槽位精灵名 == 清单文件名"守这条）。
        /// </summary>
        public enum UiSprite
        {
            // ── 语义图标 ─────────────────────────────────────────────────────
            /// <summary>准星 —— `aim`（轮到你了）。</summary>
            Crosshair = 0,

            /// <summary>虚线圆 —— `moving`（球在动）。</summary>
            Orbit = 1,

            /// <summary>手掌 —— `ball-in-hand`（白球归位）。</summary>
            Hand = 2,

            /// <summary>旗帜 —— `call-pocket`（指定球袋）。</summary>
            Flag = 3,

            /// <summary>警告三角 —— `foul`（犯规）。</summary>
            Warning = 4,

            /// <summary>机器人 —— `ai-turn`（对手回合）。</summary>
            Robot = 5,

            /// <summary>暂停双竖条 —— `paused`。</summary>
            Pause = 6,

            /// <summary>奖杯 —— `result` 与结算卡的圆形徽章。</summary>
            Trophy = 7,

            /// <summary>房子 —— 结算卡的次按钮。</summary>
            House = 8,

            /// <summary>右箭头 —— 主按钮的动作指示（`Play again →`）。</summary>
            ArrowRight = 9,

            /// <summary>喇叭 —— SFX 开关。</summary>
            Volume = 10,

            /// <summary>四角星 —— VFX 开关。</summary>
            Sparkle = 11,

            /// <summary>人形 —— 记分牌头像。</summary>
            Player = 12,

            // ── 几何形状 ─────────────────────────────────────────────────────
            /// <summary>实心圆（品牌球徽章、迷你球、力度滑块）。</summary>
            Disc = 13,

            /// <summary>圆环（头像边、结算奖杯环）。</summary>
            Ring = 14,

            /// <summary>九宫格圆角矩形，圆角 **1 px**（对应 <see cref="ClubUiTokens.RadiusMin"/>）。</summary>
            Panel1 = 15,

            /// <summary>九宫格圆角矩形，圆角 **2 px**（面板 / 按钮 / 力度轨道）。</summary>
            Panel2 = 16,

            /// <summary>九宫格圆角矩形，圆角 **3 px**（对话框 / 出杆栏 / 分段外框）。</summary>
            Panel3 = 17,
        }

        /// <summary>清单长度（= 槽位数）。数组长度与它比较，不走运行时反射。</summary>
        public static readonly int Count = System.Enum.GetValues(typeof(UiSprite)).Length;

        /// <summary>
        /// **九宫格边框宽度**（生成器里定的 4 px）。
        /// `Image.type = Sliced` 下它决定圆角是否被拉伸 —— 边框必须 ≥ 圆角，否则圆角会被压扁。
        /// </summary>
        public const float PanelSliceBorder = 4f;

        /// <summary>
        /// **资产基名**（不含扩展名）—— 例 `icon-crosshair` / `ui-panel-3`。
        ///
        /// ⚠ **必须与 `Sprite.name` 的口径一致**：Unity 导入 PNG 后，`Sprite.name` 是
        /// **去掉扩展名**的文件名（`icon-crosshair`，不是 `icon-crosshair.png`）。
        /// 校验若拿带扩展名的 `FileName` 去比，会把**完全正常**的精灵判成错位
        /// （实测：场景构建抛 `槽位 0（Crosshair）的精灵是 "icon-crosshair"，应为 "icon-crosshair.png"`）——
        /// 即"校验口径与引擎口径不一致"造成的假红。故两个口径分开成两个方法，别混用。
        /// </summary>
        public static string BaseName(UiSprite sprite)
        {
            int index = (int)sprite;
            string prefix = index >= (int)UiSprite.Disc ? "ui-" : "icon-";
            return prefix + ToKebab(sprite.ToString());
        }

        /// <summary>文件名（含扩展名），例如 `icon-crosshair.png`。**只用于组磁盘路径**。</summary>
        public static string FileName(UiSprite sprite)
        {
            return BaseName(sprite) + Extension;
        }

        /// <summary>资产路径，例如 `Assets/_Project/UI/Icons/icon-crosshair.png`。</summary>
        public static string AssetPath(UiSprite sprite)
        {
            return Directory + "/" + FileName(sprite);
        }

        /// <summary>该槽位是否用九宫格绘制（只有三个 `Panel*` 是）。</summary>
        public static bool IsSliced(UiSprite sprite)
        {
            return sprite == UiSprite.Panel1 || sprite == UiSprite.Panel2 || sprite == UiSprite.Panel3;
        }

        /// <summary>
        /// **按设计令牌的圆角值取九宫格精灵**。半径先夹到 `[RadiusMin, RadiusMax]` 再四舍五入
        /// ⇒ 即使某处误传 8 px，拿到的也是设计上限而不是"没有圆角"或"大圆角卡片"。
        /// </summary>
        public static UiSprite PanelForRadius(float radius)
        {
            float clamped = Mathf.Clamp(radius, ClubUiTokens.RadiusMin, ClubUiTokens.RadiusMax);
            int rounded = Mathf.RoundToInt(clamped);
            switch (rounded)
            {
                case 1: return UiSprite.Panel1;
                case 3: return UiSprite.Panel3;
                default: return UiSprite.Panel2;
            }
        }

        /// <summary>
        /// `PascalCase` → `kebab-case`（`ArrowRight` → `arrow-right`、`Panel3` → `panel-3`）。
        ///
        /// ⚠ **数字也是"单词边界"**：`Panel3` 必须变成 `panel-3` 而不是 `panel3`。
        /// 生成器（Python）按"字母→数字插连字符"产出 `ui-panel-3.png`，
        /// 若本方法不这么做，两边名字就对不上 —— 实测后果是**场景构建直接抛异常**
        /// （`界面精灵缺失或未按 Sprite 导入：…/ui-panel1.png`），即"生成器与清单各写一套命名规则"的典型死法。
        /// </summary>
        public static string ToKebab(string pascal)
        {
            if (string.IsNullOrEmpty(pascal))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(pascal.Length + 4);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];

                if (c >= '0' && c <= '9')
                {
                    if (i > 0 && IsAsciiLetter(pascal[i - 1]))
                    {
                        builder.Append('-');
                    }

                    builder.Append(c);
                    continue;
                }

                if (c >= 'A' && c <= 'Z')
                {
                    if (i > 0 && pascal[i - 1] != '-')
                    {
                        builder.Append('-');
                    }

                    builder.Append((char)(c - 'A' + 'a'));
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        /// <summary>
        /// 某个视觉状态该用哪个图标 —— **设计契约 §二 的 `data-state` ↔ 图标，一一对应**。
        /// 不可达状态（`ai-turn` / `call-pocket` / `paused`）同样有图标：契约先立、接入时不必再改这里。
        /// </summary>
        public static UiSprite ForState(HudVisualState state)
        {
            switch (state)
            {
                case HudVisualState.Aim: return UiSprite.Crosshair;
                case HudVisualState.AiTurn: return UiSprite.Robot;
                case HudVisualState.Moving: return UiSprite.Orbit;
                case HudVisualState.BallInHand: return UiSprite.Hand;
                case HudVisualState.CallPocket: return UiSprite.Flag;
                case HudVisualState.Foul: return UiSprite.Warning;
                case HudVisualState.Paused: return UiSprite.Pause;
                case HudVisualState.Result: return UiSprite.Trophy;
                default: return UiSprite.Crosshair;
            }
        }

        /// <summary>
        /// **槽位完整性检查**（单点实现；场景构建器与判据共用，不各写一份）。
        /// 返回 <c>true</c> 表示 `sprites` 长度正确，且每一格的精灵名与清单对得上
        /// （对得上就意味着"没有错位" —— 错位是本数组唯一的失效模式）。
        /// </summary>
        public static bool ValidateSlots(Sprite[] sprites, out string error)
        {
            error = null;

            if (sprites == null)
            {
                error = "界面精灵槽位数组为 null（视图未接线）。";
                return false;
            }

            if (sprites.Length != Count)
            {
                error = $"界面精灵槽位 {sprites.Length} 个，应为 {Count} 个（清单长度）。";
                return false;
            }

            for (int i = 0; i < sprites.Length; i++)
            {
                var sprite = (UiSprite)i;
                if (sprites[i] == null)
                {
                    error = $"槽位 {i}（{sprite}）为空。";
                    return false;
                }

                if (sprites[i].name != BaseName(sprite))
                {
                    error = $"槽位 {i}（{sprite}）的精灵是 \"{sprites[i].name}\"，应为 \"{BaseName(sprite)}\""
                            + "（槽位错位会让状态显示成别的图标）。";
                    return false;
                }
            }

            return true;
        }

        /// <summary>按序号取精灵；越界返回 <c>null</c>（**不抛** —— 视图在缺图时只是不画）。</summary>
        public static Sprite Resolve(Sprite[] sprites, UiSprite sprite)
        {
            if (sprites == null)
            {
                return null;
            }

            int index = (int)sprite;
            return index >= 0 && index < sprites.Length ? sprites[index] : null;
        }
    }
}
