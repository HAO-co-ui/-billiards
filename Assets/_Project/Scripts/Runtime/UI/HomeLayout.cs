using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **开始界面（首页）布局单点**（纯函数，判据可直接驱动）。
    ///
    /// ── 尺寸来源：不是审美发挥，是逐条抄参考稿 ─────────────────────────────────
    /// 真源 = 美术包 `04_UI/Web_Implementation_Reference/style.css` 的 `.home-*` 段
    /// （`.home-club-mark` / `.home-tools` / `.home-menu` / `.home-records` / `.home-table-label`
    /// / `.home-track`），本类把其中的 `px` 值**原样**搬进来并在 1920×1080 设计画布上落位。
    ///
    /// 与 `home-1920x1080.png`（设计稿同尺寸导出图）逐块核对过：菜单列 `x = 60…416`、
    /// `AI Match` 行的中心落在 `y ≈ 584`（本类算得 585）、底部统计的基线落在 `y ≈ 1037`
    /// —— 与参考图目视位置一致（容差 ±5 px）。
    ///
    /// ── 为什么用「设计像素 + 归一化」而不是像 `PracticeResultLayout` 那样写比例 ──────
    /// 参考稿是一张 **1920×1080 的整屏图**，所有元素都是「距离某条边的固定像素」语义
    /// （`left:60` / `top:24` / `right:34` / `bottom:30`），**不是**「卡内比例」。
    /// 若强行写成比例，改一次基准分辨率就要重算一遍且极易抄错。
    /// 故本类的唯一入口是 <see cref="Px"/>：**设计像素（左上原点）→ 视口归一化（左下原点）**，
    /// 全类只此一处做坐标换算 ⇒ 不存在"两处各写一份 y 翻转"（这正是抄错的高发点）。
    ///
    /// ── 与 HUD 同一套画布口径（硬约束，不是巧合）──────────────────────────────
    /// 复用 <see cref="PracticeHudLayout.CanvasScale"/> 的口径（1920×1080 + `match = 0.5`）
    /// ⇒ 首页与 HUD 在同一台 `CanvasScaler` 下**逐像素同构**；16:9 各档的画布恒为 1920×1080，
    /// 而 4:3 / 21:9 档归一化锚点会等比拉伸（参考稿的 `@media` 三档是**重排**，uGUI 无流式布局
    /// ⇒ 登记为**表现差异**：非 16:9 档首页元素按锚点比例拉伸，不做断点重排）。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// · 本类只保证**几何**（元素落在画布内、互相不叠、尺寸与参考稿一致）。
    ///   `text-shadow`（参考稿 `.home-screen` 给全屏文字加了 `0 2px 10px #0009`）在 uGUI
    ///   **无对应能力** ⇒ 不做，也不伪造同名字段：首页文字直接压在 3D 球桌画面上，
    ///   对比度属**实机观感问题**，由用户截图验收（与既有 HUD 的登记口径相同）。
    /// · 参考稿的 `home-action:hover` / `.active` 悬停态在 uGUI 侧没有实现（本工程既有控件
    ///   一律不做 hover 变色）⇒ 不登记为"已做"。
    /// </summary>
    public static class HomeLayout
    {
        // ── 画布基准（与 HUD **同一份常量**，不另立数值）──────────────────────
        /// <summary>设计画布宽（= `PracticeHudLayout.ReferenceWidth`）。</summary>
        public const float ReferenceWidth = PracticeHudLayout.ReferenceWidth;

        /// <summary>设计画布高（= `PracticeHudLayout.ReferenceHeight`）。</summary>
        public const float ReferenceHeight = PracticeHudLayout.ReferenceHeight;

        // ── 边距与间距（`style.css` 逐条）────────────────────────────────────
        /// <summary>菜单列 / 统计行的左边距（`.home-menu{left:60}`、`.home-records{left:60}`）。</summary>
        public const float MarginLeft = 60f;

        /// <summary>菜单列与统计行的宽度：`min(356, 42vw - 60)` 在 1920 下取 **356**。</summary>
        public const float MenuWidth = 356f;

        /// <summary>右上工具条的右边距（`.home-tools{right:32}`）。</summary>
        public const float ToolsRight = 32f;

        /// <summary>右上工具条的顶边距（`.home-tools{top:38}`）。</summary>
        public const float ToolsTop = 38f;

        /// <summary>两个图标按钮的水平间距（`.home-tools{gap:8}`）。</summary>
        public const float ToolsGap = 8f;

        /// <summary>俱乐部徽章的左边距（`.home-club-mark{left:40}`）。</summary>
        public const float CrestLeft = 40f;

        /// <summary>俱乐部徽章的顶边距（`.home-club-mark{top:24}`）。</summary>
        public const float CrestTop = 24f;

        /// <summary>俱乐部徽章边长（`.home-club-mark img{width:88px}`）。</summary>
        public const float CrestSize = 88f;

        /// <summary>徽章与俱乐部名的间距（`.home-club-mark{gap:14}`）。</summary>
        public const float CrestGap = 14f;

        /// <summary>徽章右侧文字块宽度（给 18 px 的 `THE EIGHT BALL CLUB` 留够，不压到球桌）。</summary>
        public const float CrestTextWidth = 260f;

        /// <summary>标题行（`TABLE 01` + 两侧细线）里的文字半宽 —— 两侧细线各让出 `半宽 + <see cref="RuleGap"/>`。</summary>
        public const float TableNumberHalfWidth = 30f;

        /// <summary>标题行细线与 `TABLE 01` 的间距（`.home-heading-rule{gap:12}`）。</summary>
        public const float RuleGap = 12f;

        /// <summary>两个列表按钮之间的竖直间距（`.home-actions{gap:10}`）。</summary>
        public const float ActionGap = 10f;

        /// <summary>列表按钮最小高度（`.home-action{min-height:52px}`）。</summary>
        public const float ActionHeight = 52f;

        /// <summary>列表按钮的左右内距（`.home-action{padding:13px 20px}` 的横向分量）。</summary>
        public const float ActionPaddingX = 20f;

        /// <summary>列表按钮里的箭头边长（`.home-action svg{width:20px}`）。</summary>
        public const float ActionGlyphSize = 20f;

        /// <summary>难度标签与分段控件之间的间距（`.home-difficulty > span{margin-bottom:9}`）。</summary>
        public const float DifficultyLabelGap = 9f;

        /// <summary>列表按钮区与难度区之间的间距（`.home-difficulty{margin-top:22}`）。</summary>
        public const float DifficultyTopGap = 22f;

        /// <summary>底部统计行的下边距（`.home-records{bottom:34}`）。</summary>
        public const float RecordsBottom = 34f;

        /// <summary>统计行上方细线到数字的距离（`.home-records{padding-top:16}`）。</summary>
        public const float RecordsPaddingTop = 16f;

        /// <summary>统计数字与标签之间的间距（`.home-records span{margin-top:5}`）。</summary>
        public const float RecordsLabelGap = 5f;

        /// <summary>右下 `HOUSE TABLE 01` 的右边距 / 下边距（`.home-table-label{right:34;bottom:30}`）。</summary>
        public const float HouseRight = 34f;
        public const float HouseBottom = 30f;

        /// <summary>`HOUSE TABLE` 与数字之间的间距（`.home-table-label{gap:11}` + 左侧 1 px 竖线的 `padding-left:11`）。</summary>
        public const float HouseGap = 11f;

        /// <summary>右下曲目行的右边距 / 下边距（`.home-track{right:34;bottom:78}`）。</summary>
        public const float TrackRight = 34f;
        public const float TrackBottom = 78f;

        /// <summary>曲目行里音乐图标的边长（`.home-track svg{width:13px}`）。</summary>
        public const float TrackGlyphSize = 13f;

        /// <summary>曲目行里图标与文字的间距（`.home-track{gap:8}`）。</summary>
        public const float TrackGap = 8f;

        // ── 竖直堆叠的锚点（自上而下；由 CSS 的 `top:50% + translateY(-43%)` 复算）──
        /// <summary>eyebrow（`8-BALL CLUB`）的顶边。</summary>
        public const float EyebrowTop = 382f;

        /// <summary>eyebrow 行高（12 px 字号）。</summary>
        public const float EyebrowHeight = 15f;

        /// <summary>大标题与 eyebrow 的间距（`.home-menu h1{margin:12px 0 19px}` 的上分量）。</summary>
        public const float TitleTopGap = 12f;

        /// <summary>大标题行高 = `76px × 1.2`（`.home-menu h1{font:600 76px/1.2}`）。</summary>
        public const float TitleHeight = 91f;

        /// <summary>大标题与标题行（`TABLE 01`）的间距（`.home-menu h1{margin:12px 0 19px}` 的下分量）。</summary>
        public const float TitleBottomGap = 19f;

        /// <summary>标题行（细线 + `TABLE 01`）自身高度。</summary>
        public const float HeadingRuleHeight = 12f;

        /// <summary>标题行与按钮区之间的间距（`.home-heading-rule{margin-bottom:30}`）。</summary>
        public const float HeadingRuleBottomGap = 30f;

        // ── 派生矩形（全部经 <see cref="Px"/> 换算成视口归一化，左下原点）──────

        /// <summary>俱乐部徽章（`ui-club-crest`）。</summary>
        public static readonly Rect Crest = Px(CrestLeft, CrestTop, CrestSize, CrestSize);

        /// <summary>俱乐部名 `THE EIGHT BALL CLUB`（18 px Barlow）。</summary>
        public static readonly Rect ClubName = Px(CrestLeft + CrestSize + CrestGap, CrestTop + 23f, CrestTextWidth, 24f);

        /// <summary>俱乐部副标 `PRIVATE TABLE · NO. 35`（8 px Inter）。</summary>
        public static readonly Rect ClubSub = Px(CrestLeft + CrestSize + CrestGap, CrestTop + 51f, CrestTextWidth, 12f);

        /// <summary>eyebrow `8-BALL CLUB`（12 px Inter，黄铜）。</summary>
        public static readonly Rect MenuEyebrow = Px(MarginLeft, EyebrowTop, MenuWidth, EyebrowHeight);

        /// <summary>大标题 `POOL ROOM`（76 px Barlow）。</summary>
        public static readonly Rect MenuTitle = Px(MarginLeft, EyebrowTop + EyebrowHeight + TitleTopGap, MenuWidth, TitleHeight);

        /// <summary>`TABLE 01` + 两侧细线所在的行。</summary>
        public static readonly Rect HeadingRule = Px(
            MarginLeft,
            EyebrowTop + EyebrowHeight + TitleTopGap + TitleHeight + TitleBottomGap,
            MenuWidth, HeadingRuleHeight);

        /// <summary>第一行列表按钮 `AI Match`（**禁用态**，见 <see cref="HomeView"/> 的类注释）。</summary>
        public static readonly Rect ActionAiMatch = Px(
            MarginLeft, HeadingRuleTop + HeadingRuleHeight + HeadingRuleBottomGap, MenuWidth, ActionHeight);

        /// <summary>第二行列表按钮 `Practice`（**首页唯一真能用的入口**）。</summary>
        public static readonly Rect ActionPractice = Px(
            MarginLeft, HeadingRuleTop + HeadingRuleHeight + HeadingRuleBottomGap + ActionHeight + ActionGap,
            MenuWidth, ActionHeight);

        /// <summary>难度小标签 `OPPONENT`（8 px Inter）。</summary>
        public static readonly Rect DifficultyLabel = Px(
            MarginLeft, PracticeActionTop + ActionHeight + DifficultyTopGap, MenuWidth, 10f);

        /// <summary>难度分段控件（`Casual | Club | Expert`）。</summary>
        public static readonly Rect DifficultySegment = Px(
            MarginLeft, DifficultyLabelTop + 10f + DifficultyLabelGap, MenuWidth, ClubUiTokens.SegmentHeight);

        /// <summary>底部统计行（三项）—— 容器（含上细线）的矩形。</summary>
        public static readonly Rect Records = Px(
            MarginLeft, RecordsTop, MenuWidth, RecordsHeight);

        /// <summary>统计行的上细线（`.home-records{border-top:1px}`）。</summary>
        public static readonly Rect RecordsRule = Px(
            MarginLeft, RecordsTop, MenuWidth, ClubUiTokens.Hairline);

        /// <summary>右下 `HOUSE TABLE` + `01` 的容器。</summary>
        public static readonly Rect HouseLabel = Px(
            ReferenceWidth - HouseRight - HouseLabelWidth, ReferenceHeight - HouseBottom - HouseLabelHeight,
            HouseLabelWidth, HouseLabelHeight);

        /// <summary>右下曲目行（音乐图标 + `Late Set`）的容器。</summary>
        public static readonly Rect Track = Px(
            ReferenceWidth - TrackRight - TrackWidth, ReferenceHeight - TrackBottom - TrackHeight,
            TrackWidth, TrackHeight);

        /// <summary>右上工具条容器（两个 38×38 图标按钮）。</summary>
        public static readonly Rect Tools = Px(
            ReferenceWidth - ToolsRight - (ClubUiTokens.IconButtonSize * 2f + ToolsGap), ToolsTop,
            ClubUiTokens.IconButtonSize * 2f + ToolsGap, ClubUiTokens.IconButtonSize);

        // ── 派生标量（供视图内部再分格用）────────────────────────────────────

        /// <summary>标题行上边（= 大标题底边 + 间距）—— 供上面几个 `Rect` 的声明顺序使用。</summary>
        public const float HeadingRuleTop = EyebrowTop + EyebrowHeight + TitleTopGap + TitleHeight + TitleBottomGap;

        /// <summary>`Practice` 按钮的上边。</summary>
        public const float PracticeActionTop = HeadingRuleTop + HeadingRuleHeight + HeadingRuleBottomGap + ActionHeight + ActionGap;

        /// <summary>`OPPONENT` 标签的上边。</summary>
        public const float DifficultyLabelTop = PracticeActionTop + ActionHeight + DifficultyTopGap;

        /// <summary>底部统计行的高度 = 16（内距）+ 数字行 31 + 5（间距）+ 标签行 10。</summary>
        public const float RecordsHeight = RecordsPaddingTop + 31f + RecordsLabelGap + 10f;

        /// <summary>底部统计行的上边（含其上细线）。</summary>
        public const float RecordsTop = ReferenceHeight - RecordsBottom - RecordsHeight;

        /// <summary>统计行的三列宽度。</summary>
        public const float RecordsColumnWidth = MenuWidth / 3f;

        /// <summary>统计数字（25 px Barlow）的行高。</summary>
        public const float RecordsValueHeight = 31f;

        /// <summary>统计标签（8 px Inter）的行高。</summary>
        public const float RecordsLabelHeight = 10f;

        /// <summary>右下 `HOUSE TABLE 01` 容器的宽度（文字 + 间距 + 竖线 + 间距 + 大号数字）。</summary>
        public const float HouseLabelWidth = 160f;

        /// <summary>右下 `HOUSE TABLE 01` 容器的高度（对齐 28 px 的数字行）。</summary>
        public const float HouseLabelHeight = 34f;

        /// <summary>图号 `01` 的宽度（28 px Barlow 的两个数字）。</summary>
        public const float HouseNumberWidth = 42f;

        /// <summary>右下曲目行容器的宽度（图标 + 间距 + `Late Set`）。</summary>
        public const float TrackWidth = 90f;

        /// <summary>右下曲目行容器的高度（10 px 文字行，图标 13 px 在其中垂直居中）。</summary>
        public const float TrackHeight = 15f;

        // ── 子矩形（同样只经 <see cref="Px"/> 换算；视图不再自己算坐标）──────────

        /// <summary>统计行第 <paramref name="index"/> 列（0…2）的**数字**矩形（25 px Barlow）。</summary>
        public static Rect RecordsValue(int index)
        {
            return Px(RecordsColumnLeft(index), RecordsTop + RecordsPaddingTop, RecordsColumnWidth, RecordsValueHeight);
        }

        /// <summary>统计行第 <paramref name="index"/> 列（0…2）的**标签**矩形（8 px Inter）。</summary>
        public static Rect RecordsLabel(int index)
        {
            return Px(RecordsColumnLeft(index),
                RecordsTop + RecordsPaddingTop + RecordsValueHeight + RecordsLabelGap,
                RecordsColumnWidth, RecordsLabelHeight);
        }

        /// <summary>右下 `HOUSE TABLE` 文字的矩形（右对齐；右边让出间距与 1 px 竖线）。</summary>
        public static readonly Rect HouseText = Px(
            ReferenceWidth - HouseRight - HouseLabelWidth, ReferenceHeight - HouseBottom - HouseLabelHeight,
            HouseLabelWidth - HouseGap - ClubUiTokens.Hairline - HouseGap - HouseNumberWidth, HouseLabelHeight);

        /// <summary>右下 `HOUSE TABLE` 与 `01` 之间的 1 px 竖线（`.home-table-label strong{border-left}`）。</summary>
        public static readonly Rect HouseRule = Px(
            ReferenceWidth - HouseRight - HouseNumberWidth - HouseGap, ReferenceHeight - HouseBottom - HouseLabelHeight,
            ClubUiTokens.Hairline, HouseLabelHeight);

        /// <summary>右下大号台号 `01` 的矩形（28 px Barlow）。</summary>
        public static readonly Rect HouseNumber = Px(
            ReferenceWidth - HouseRight - HouseNumberWidth, ReferenceHeight - HouseBottom - HouseLabelHeight,
            HouseNumberWidth, HouseLabelHeight);

        /// <summary>右下曲目行里的音乐图标（13 px，在行内垂直居中）。</summary>
        public static readonly Rect TrackGlyph = Px(
            ReferenceWidth - TrackRight - TrackWidth, ReferenceHeight - TrackBottom - TrackHeight + (TrackHeight - TrackGlyphSize) * 0.5f,
            TrackGlyphSize, TrackGlyphSize);

        /// <summary>右下曲目行里的曲目名 `Late Set`（10 px Inter）。</summary>
        public static readonly Rect TrackText = Px(
            ReferenceWidth - TrackRight - TrackWidth + TrackGlyphSize + TrackGap, ReferenceHeight - TrackBottom - TrackHeight,
            TrackWidth - TrackGlyphSize - TrackGap, TrackHeight);

        /// <summary>右上第 <paramref name="index"/> 个图标按钮（0 = 左，1 = 右）。</summary>
        public static Rect ToolButton(int index)
        {
            int i = Mathf.Clamp(index, 0, 1);
            return Px(ReferenceWidth - ToolsRight - (ClubUiTokens.IconButtonSize * 2f + ToolsGap)
                      + i * (ClubUiTokens.IconButtonSize + ToolsGap),
                ToolsTop, ClubUiTokens.IconButtonSize, ClubUiTokens.IconButtonSize);
        }

        /// <summary>统计行第 <paramref name="index"/> 列的左边（设计像素）。</summary>
        private static float RecordsColumnLeft(int index)
        {
            return MarginLeft + Mathf.Clamp(index, 0, 2) * RecordsColumnWidth;
        }

        /// <summary>
        /// **设计像素（左上原点）→ 视口归一化矩形（左下原点）** —— 全类唯一的坐标换算点。
        /// </summary>
        public static Rect Px(float left, float top, float width, float height)
        {
            return new Rect(
                left / ReferenceWidth,
                1f - (top + height) / ReferenceHeight,
                width / ReferenceWidth,
                height / ReferenceHeight);
        }

        /// <summary>
        /// 首页的**全部元素矩形**（自检用：越界必须当场看得见，不能等实机才发现被裁掉）。
        /// 构建期调它一次，越界就告警 —— 与 `PracticeResultView.ApplyCardRect` 同一处置口径。
        /// </summary>
        public static Rect[] AllRects()
        {
            return new[]
            {
                Crest, ClubName, ClubSub,
                MenuEyebrow, MenuTitle, HeadingRule, ActionAiMatch, ActionPractice,
                DifficultyLabel, DifficultySegment,
                Records, RecordsRule, HouseLabel, Track, Tools,
                HouseText, HouseRule, HouseNumber, TrackGlyph, TrackText,
                RecordsValue(0), RecordsValue(1), RecordsValue(2),
                RecordsLabel(0), RecordsLabel(1), RecordsLabel(2),
                ToolButton(0), ToolButton(1),
            };
        }

        /// <summary><paramref name="inner"/> 是否完整落在视口内（容差 0 —— 越界即真越界）。</summary>
        public static bool WithinViewport(Rect inner)
        {
            return inner.xMin >= 0f && inner.xMax <= 1f && inner.yMin >= 0f && inner.yMax <= 1f;
        }
    }
}
