using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **对局界面设计令牌的单一真源**（铁律 16）。
    ///
    /// ── 数值从哪来（不是审美发挥）────────────────────────────────────────────
    /// 全部逐条抄自美术包 `04_UI` 的**设计说明 + 可交互原型**：
    /// · 文档：`04_UI/UI全流程设计说明_参考.txt` §一（画风：主色 / 专用色 / 字体 / 字距 0 / 边角 1–3 px）
    /// · 令牌：`04_UI/Web_Implementation_Reference/style.css` 的 `:root` 与各组件规则（逐字核对）
    ///
    /// 文档与 CSS 只在黄铜值上差 1 个最低位（文档写 `#d4b46c`、取色器读到 `#d2b46c`）——
    /// **以文档为准取 `#d4b46c`**（文档是设计意图，取色是显示器上的实现结果）。
    ///
    /// ── 为什么必须集中（本类存在的理由）──────────────────────────────────────
    /// 重做前，HUD 与结算卡各自内联自己的 `new Color32(...)`，令牌散落在两处视图里 ⇒
    /// 改一处色值必然漏改另一处（"半新半旧"的界面正是"丑"的机制性来源）。
    /// 现在**所有**色值/尺寸/字号只在本类出现，视图只引用、不写字面量；判据 `T124` 守这一点。
    ///
    /// ── 语义边界（设计说明 §一，**必须守**）──────────────────────────────────
    /// · **酒红（claret）只用于犯规与失败**；
    /// · **亮青（cyan）只用于指定球袋**；
    /// · 其余状态一律走黄铜 / 绿 / 静音灰三族，**不得**借色。
    /// 三族各自的成员在 <see cref="ClaretFamily"/> / <see cref="CyanFamily"/> 里显式登记，
    /// 判据 `T125` 据此断言"没有别的状态偷用这两族颜色"。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// · 原型 CSS 的 `inset` 内阴影、`backdrop-filter: blur`、`text-shadow` 在 uGUI **无对应能力**
    ///   ⇒ 本类**不**伪造同名字段（登记为表现差异，见 `Docs/35-A_美术接入与UI重做_spec.md` §6）。
    /// · 字体族名不是"随便的字符串"：标题族/正文族分别对应两个已建好的 TMP 资产，
    ///   由 `HudAssetBuilder.TitleFontAssetPath` / `BodyFontAssetPath` 解析（本类不重复钉路径）。
    /// </summary>
    public static class ClubUiTokens
    {
        // ════════════════ 主色板（`style.css` `:root` 逐条）════════════════════

        /// <summary>`--accent` 黄铜金 `#d4b46c`。主按钮底、强调文字、细边线来源。</summary>
        public static readonly Color32 Accent = Rgb(0xD4B46C);

        /// <summary>`--green` 桌布绿描绿 `#6dbda9`。"你可以操作了"的指示色。</summary>
        public static readonly Color32 Green = Rgb(0x6DBDA9);

        /// <summary>`--ink` 炭黑 `#102025`。所有面板底、深色文字。</summary>
        public static readonly Color32 Ink = Rgb(0x102025);

        /// <summary>`--cloth` 桌布绿 `#174e46`。徽章底、`Continue` 类按钮底。</summary>
        public static readonly Color32 Cloth = Rgb(0x174E46);

        /// <summary>`--muted` 静音灰 `#a9b9b4`。次级文字、说明文字。</summary>
        public static readonly Color32 Muted = Rgb(0xA9B9B4);

        /// <summary>正文暖白 `#f2efe4`（`body { color }`）。</summary>
        public static readonly Color32 Text = Rgb(0xF2EFE4);

        /// <summary>`#e8cd89` 键盘焦点环色。uGUI 无焦点环 ⇒ 仅登记，供将来使用。</summary>
        public static readonly Color32 Focus = Rgb(0xE8CD89);

        /// <summary>次级说明文字 `#aebeb5`（对话框正文、结算统计标签）。</summary>
        public static readonly Color32 Body = Rgb(0xAEBEB5);

        /// <summary>力度条的数值文字 `#efdcaa`。</summary>
        public static readonly Color32 PowerValue = Rgb(0xEFDCAA);

        /// <summary>力度条轨道底 `#3f615a`（未填充部分）。</summary>
        public static readonly Color32 PowerTrack = Rgb(0x3F615A);

        /// <summary>力度条滑块底 `#e9d096`。</summary>
        public static readonly Color32 PowerThumb = Rgb(0xE9D096);

        /// <summary>力度条滑块 3 px 亮边 `#f7e6b9`。</summary>
        public static readonly Color32 PowerThumbRing = Rgb(0xF7E6B9);

        /// <summary>主按钮底 `#d4b46c`。</summary>
        public static readonly Color32 ButtonFill = Accent;

        /// <summary>主按钮 hover 底 `#e5ca87`。</summary>
        public static readonly Color32 ButtonFillHover = Rgb(0xE5CA87);

        /// <summary>主按钮**按下**态（`ColorTint`；比常态略暗 ⇒ 有"按下去"的手感）。</summary>
        public static readonly Color32 ButtonFillPressed = Rgb(0xC9A961);

        /// <summary>主按钮**禁用**态（`ColorTint`；半透明，保留形状但明显不可点）。</summary>
        public static readonly Color32 ButtonFillDisabled = Alpha(Rgb(0xD4B46C), 0x66);

        /// <summary>主按钮 1 px 亮边 `#ebd397`。</summary>
        public static readonly Color32 ButtonEdge = Rgb(0xEBD397);

        /// <summary>主按钮文字（深炭黑 `#102025`）。</summary>
        public static readonly Color32 ButtonText = Ink;

        /// <summary>次按钮描边 `#d4b46c50`。</summary>
        public static readonly Color32 ButtonEdgeSecondary = Alpha(Accent, 0x50);

        /// <summary>次按钮 hover 底 `#1b3732`。</summary>
        public static readonly Color32 ButtonFillSecondaryHover = Rgb(0x1B3732);

        /// <summary>头像底 `#14312d`。</summary>
        public static readonly Color32 AvatarFill = Rgb(0x14312D);

        /// <summary>头像细边 `#d4b46c50`。</summary>
        public static readonly Color32 AvatarEdge = Alpha(Accent, 0x50);

        /// <summary>头像里的人形图标色 `#ddd6bb`（`style.css` `.avatar svg`）。</summary>
        public static readonly Color32 AvatarIcon = Rgb(0xDDD6BB);

        /// <summary>品牌球徽章底（暖白 `#f2efe4`）。</summary>
        public static readonly Color32 BrandBallFill = Text;

        /// <summary>品牌球徽章数字色 `#111c20`。</summary>
        public static readonly Color32 BrandBallText = Rgb(0x111C20);

        /// <summary>品牌球徽章外环 `#d4b46c88`。</summary>
        public static readonly Color32 BrandBallRing = Alpha(Accent, 0x88);

        /// <summary>图标处于"关"状态时的颜色（同 <see cref="DisabledOpacity"/> 压暗）。</summary>
        public static readonly Color32 DisabledIcon = Alpha(Rgb(0xE9E6D9), 0x61);

        /// <summary>模式切换未选中项文字 `#bdc9c3`。</summary>
        public static readonly Color32 SegmentIdleText = Rgb(0xBDC9C3);

        /// <summary>模式切换选中项文字 `#12262a`。</summary>
        public static readonly Color32 SegmentActiveText = Rgb(0x12262A);

        /// <summary>图标按钮的图标/文字色 `#e9e6d9`。</summary>
        public static readonly Color32 IconIdle = Rgb(0xE9E6D9);

        /// <summary>图标按钮 hover 色 `#edd596`。</summary>
        public static readonly Color32 IconHover = Rgb(0xEDD596);

        /// <summary>比分中"／"分隔符的暗色 `#7e9690`。</summary>
        public static readonly Color32 ScoreDivider = Rgb(0x7E9690);

        /// <summary>小标签文字 `#acbbb4`。</summary>
        public static readonly Color32 Label = Rgb(0xACBBB4);

        /// <summary>提示条（toast）文字 `#efd89b`。</summary>
        public static readonly Color32 ToastText = Rgb(0xEFD89B);

        /// <summary>迷你球白边 `#ffffff40`。</summary>
        public static readonly Color32 MiniBallEdge = Alpha(Text, 0x40);

        /// <summary>迷你球数字色（白）。</summary>
        public static readonly Color32 MiniBallText = Rgb(0xFFFFFF);

        /// <summary>
        /// 全不透明（`#ffffff`）。**存在理由是"视图里不得出现颜色字面量"**：
        /// 贴片自带球色，挂上去时要用一个"不改变贴片颜色"的中性值 ——
        /// 直接写 `Color.white` 会让"视图里搜不到颜色字面量"这条守卫失效
        /// （一处漏网就会让守卫从"零字面量"退化成"差不多为零"，守卫强度 = 守卫读的东西，铁律 10）。
        /// </summary>
        public static readonly Color32 Opaque = Rgb(0xFFFFFF);

        /// <summary>全透明（用于"只描边不填底"的按钮与徽章）。</summary>
        public static readonly Color32 Transparent = new Color32(0, 0, 0, 0);

        // ════════════════ 面板底（同一炭黑的三级不透明）════════════════════════

        /// <summary>计分牌底 `#102025d9`（85%）。</summary>
        public static readonly Color32 PanelScoreboard = Alpha(Ink, 0xD9);

        /// <summary>状态条底 `#102025ed`（93%）。</summary>
        public static readonly Color32 PanelStatus = Alpha(Ink, 0xED);

        /// <summary>出杆栏 / 提示条底 `#102025f2`（95%）。</summary>
        public static readonly Color32 PanelShotbar = Alpha(Ink, 0xF2);

        /// <summary>模式切换外框底 `#102025ae`（68%）—— 比面板更透，看得出底下是球桌。</summary>
        public static readonly Color32 PanelSegment = Alpha(Ink, 0xAE);

        /// <summary>图标按钮底 `#102025e8`（91%）。</summary>
        public static readonly Color32 PanelIconButton = Alpha(Ink, 0xE8);

        /// <summary>结算卡底（不透明炭黑 `#102025`）。</summary>
        public static readonly Color32 PanelCard = Ink;

        /// <summary>模态遮罩 `#040c10b8`（72%）。</summary>
        public static readonly Color32 Backdrop = Alpha(Rgb(0x040C10), 0xB8);

        // ════════════════ 细边线（黄铜的各种不透明度）══════════════════════════

        /// <summary>最轻的分隔线 `#d4b46c25`（15%）。</summary>
        public static readonly Color32 LineFaint = Alpha(Accent, 0x25);

        /// <summary>统计区上下边线 `#d4b46c35`（21%）。</summary>
        public static readonly Color32 LineDivider = Alpha(Accent, 0x35);

        /// <summary>`--line` 默认边线 `#d4b46c40`（25%）。</summary>
        public static readonly Color32 Line = Alpha(Accent, 0x40);

        /// <summary>状态条 / 图标按钮边线 `#d4b46c45`（27%）。</summary>
        public static readonly Color32 LineMid = Alpha(Accent, 0x45);

        /// <summary>次按钮描边 `#d4b46c50`（31%）。</summary>
        public static readonly Color32 LineButton = Alpha(Accent, 0x50);

        /// <summary>出杆栏边线 `#d4b46c60`（38%）。</summary>
        public static readonly Color32 LineStrong = Alpha(Accent, 0x60);

        /// <summary>结算卡 chip 边线 `#d4b46c65`（40%）。</summary>
        public static readonly Color32 LineBadge = Alpha(Accent, 0x65);

        /// <summary>计分牌上边线 `#d4b46c70`（44%）。</summary>
        public static readonly Color32 LineScoreTop = Alpha(Accent, 0x70);

        /// <summary>结算卡圆徽章边线 `#d4b46c80`（50%）。</summary>
        public static readonly Color32 LineEmphasis = Alpha(Accent, 0x80);

        /// <summary>结算卡外框 `#d4b46c85`（52%）。</summary>
        public static readonly Color32 LineCardEdge = Alpha(Accent, 0x85);

        // ════════════════ 状态条八态（`data-state` 逐一对应）════════════════════
        // 顺序与设计说明 §4 的 `data-state` 列表一致：aim / ai-turn / moving /
        // ball-in-hand / call-pocket / foul / paused / result。

        /// <summary>`aim`：准星图标 + 绿色（"轮到你了，可以打"）。</summary>
        public static readonly Color32 AimEdge = LineMid;
        public static readonly Color32 AimSymbol = Green;

        /// <summary>`ai-turn`：机器人图标 + 浅青边线。**P6 起可达**（真值源 `PhysicsTestDrive.IsAiTurn`）。</summary>
        public static readonly Color32 AiTurnEdge = Alpha(Rgb(0x73B6BB), 0x70);
        public static readonly Color32 AiTurnSymbol = Rgb(0x8AC8CD);

        /// <summary>`moving`：虚线圈图标 + 静音灰（球在动，禁止操作）。</summary>
        public static readonly Color32 MovingEdge = LineMid;
        public static readonly Color32 MovingSymbol = Muted;

        /// <summary>`ball-in-hand`：手掌图标 + 黄铜色边线。</summary>
        public static readonly Color32 BallInHandEdge = Alpha(Accent, 0xA0);
        public static readonly Color32 BallInHandSymbol = Rgb(0xE5CD8E);

        /// <summary>`call-pocket`：旗帜图标 + 亮青边线。**仍不可达**：运行期出口已存在，缺 UI 选袋交互。</summary>
        public static readonly Color32 CallPocketEdge = Alpha(Rgb(0x7FCFD2), 0xA0);
        public static readonly Color32 CallPocketSymbol = Rgb(0x97DEDD);

        /// <summary>`foul`：警告图标 + **酒红**边线与底（酒红的第一处、也是界面里唯一的常态用法）。</summary>
        public static readonly Color32 FoulEdge = Alpha(Rgb(0xB66370), 0xA0);
        public static readonly Color32 FoulSymbol = Rgb(0xE59BA5);
        public static readonly Color32 FoulFill = Alpha(Rgb(0x30252B), 0xF2);

        /// <summary>`paused`：暂停图标 + 静音灰。**仍不可达**（本卡没有暂停系统）。</summary>
        public static readonly Color32 PausedEdge = LineMid;
        public static readonly Color32 PausedSymbol = Rgb(0xA8B7B1);

        /// <summary>`result`：奖杯图标 + 黄铜色。</summary>
        public static readonly Color32 ResultEdge = LineMid;
        public static readonly Color32 ResultSymbol = Accent;

        // ════════════════ 结算卡（`#result-dialog` 系列逐条）════════════════════

        /// <summary>胜局：顶部 3 px 黄铜条 + 圆徽章黄铜边。</summary>
        public static readonly Color32 ResultWinEdge = Accent;

        /// <summary>负局：顶部 3 px 条改为酒红 `#9f535b`（酒红的第二处用法）。</summary>
        public static readonly Color32 ResultLoseEdge = Rgb(0x9F535B);

        /// <summary>负局：圆徽章图标/边线 `#d8888f` / `#9f535b99`。</summary>
        public static readonly Color32 ResultLoseIcon = Rgb(0xD8888F);
        public static readonly Color32 ResultLoseIconEdge = Alpha(Rgb(0x9F535B), 0x99);

        /// <summary>负局：chip 边线 `#9f535b80`。</summary>
        public static readonly Color32 ResultLoseBadgeEdge = Alpha(Rgb(0x9F535B), 0x80);

        /// <summary>全场结算：圆徽章底 `#174e4666`（布绿 40%）。</summary>
        public static readonly Color32 ResultMatchIconFill = Alpha(Cloth, 0x66);

        /// <summary>全场结算 + 负局：圆徽章底 `#592f3e33`。</summary>
        public static readonly Color32 ResultMatchLoseIconFill = Alpha(Rgb(0x592F3E), 0x33);

        /// <summary>结算比分大字 `#e8d3a0`。</summary>
        public static readonly Color32 ResultScore = Rgb(0xE8D3A0);

        /// <summary>"eyebrow" 小标（腔调标）色 = 黄铜。</summary>
        public static readonly Color32 ResultEyebrow = Accent;

        // ════════════════ 开始界面（首页，`04_UI` 的 `.home-*` 段逐条）═══════════════
        // 真源 = `Web_Implementation_Reference/style.css` 的 `.home-club-mark` / `.home-menu`
        // / `.home-records` / `.home-table-label` / `.home-track` 各规则。视图侧不得再写字面量。

        /// <summary>首页俱乐部名 `#e8d4a1`（`.home-club-mark > span`）。</summary>
        public static readonly Color32 HomeClubName = Rgb(0xE8D4A1);

        /// <summary>首页徽章副标 `#aabab3`（`.home-club-mark small`；右下曲目行的文字同值）。</summary>
        public static readonly Color32 HomeClubSub = Rgb(0xAABAB3);

        /// <summary>首页提示小字 `#afbbb3`（`.home-table-number` 与 `.home-difficulty > span` 同值）。</summary>
        public static readonly Color32 HomeHint = Rgb(0xAFBBB3);

        /// <summary>首页标题行细线 `#d4b46c70`（`.home-heading-rule > span:not(.home-table-number)`）。</summary>
        public static readonly Color32 HomeRule = Alpha(Accent, 0x70);

        /// <summary>首页列表按钮底 `#102025b8`（`.home-secondary{background}`）。</summary>
        public static readonly Color32 HomeActionFill = Alpha(Ink, 0xB8);

        /// <summary>首页列表按钮描边 `#d4b46c75`（`.home-secondary{border-color}`）。</summary>
        public static readonly Color32 HomeActionEdge = Alpha(Accent, 0x75);

        /// <summary>首页统计标签 `#aebdb5`（`.home-records span`）。</summary>
        public static readonly Color32 HomeStatLabel = Rgb(0xAEBDB5);

        /// <summary>首页右下 `HOUSE TABLE 01` 的色 `#d4b46cb0`（`.home-table-label{color}`）。</summary>
        public static readonly Color32 HomeHouseTint = Alpha(Accent, 0xB0);

        // ════════════════ 语义色族登记（供 `T125` 守"不得借色"）═════════════════

        /// <summary>酒红族：**只允许**出现在犯规与失败两处。</summary>
        public static readonly Color32[] ClaretFamily =
        {
            FoulEdge, FoulSymbol, FoulFill,
            ResultLoseEdge, ResultLoseIcon, ResultLoseIconEdge, ResultLoseBadgeEdge,
            ResultMatchLoseIconFill,
        };

        /// <summary>亮青族：**只允许**出现在 `call-pocket`（指定球袋）一处。</summary>
        public static readonly Color32[] CyanFamily =
        {
            CallPocketEdge, CallPocketSymbol,
        };

        // ════════════════ 圆角与尺寸（设计说明 §一：边角 1–3 px，禁大圆角）══════

        /// <summary>圆角下限（设计说明 §一）。</summary>
        public const float RadiusMin = 1f;

        /// <summary>圆角上限（设计说明 §一）—— **超出即违反设计契约**。</summary>
        public const float RadiusMax = 3f;

        /// <summary>卡片/对话框圆角 `3px`。</summary>
        public const float RadiusDialog = 3f;

        /// <summary>面板圆角 `2px`（状态条）。</summary>
        public const float RadiusPanel = 2f;

        /// <summary>按钮圆角 `2px`。</summary>
        public const float RadiusButton = 2f;

        /// <summary>分段控件外框圆角 `3px`。</summary>
        public const float RadiusSegment = 3f;

        /// <summary>分段控件内钮圆角 `1px`。</summary>
        public const float RadiusSegmentInner = 1f;

        /// <summary>力度条轨道圆角 `1px`。</summary>
        public const float RadiusTrack = 1f;

        /// <summary>细线宽度 `1px`。</summary>
        public const float Hairline = 1f;

        /// <summary>主按钮尺寸 `112 × 43`（`style.css` `.shoot-button`）。</summary>
        public const float ButtonWidth = 112f;
        public const float ButtonHeight = 43f;

        /// <summary>图标按钮 `38 × 38`、圆角 `3`。</summary>
        public const float IconButtonSize = 38f;
        public const float IconButtonIconSize = 18f;

        /// <summary>图标按钮之间的水平间距 `7`（`style.css` `.tools { gap: 7px }`）。</summary>
        public const float IconButtonGap = 7f;

        /// <summary>品牌球徽章直径 `35`（`style.css` `.brand-ball`）。</summary>
        public const float BrandBallSize = 35f;

        /// <summary>品牌球徽章上的号码字号 `23`。</summary>
        public const float FontBrandBall = 23f;

        /// <summary>
        /// 品牌球徽章外环直径 `47`。
        /// 设计稿的 `box-shadow: 0 0 0 5px #102025, 0 0 0 6px #d4b46c88` 是"5 px 底色外扩 + 6 px 黄铜环"
        /// ⇒ 环直径 = 35 + 12 = 47（uGUI 没有 box-shadow，用一圈独立的环形位图等价呈现）。
        /// </summary>
        public const float BrandBallRingSize = 47f;

        /// <summary>分段控件（模式切换）高度 `38`、内距 `3`（`style.css` `.mode-switch`）。</summary>
        public const float SegmentHeight = 38f;
        public const float SegmentPadding = 3f;

        /// <summary>主按钮里的动作箭头 `16`。</summary>
        public const float ButtonArrowSize = 16f;

        /// <summary>
        /// 已进袋的迷你球不透明度 `0.2`（`style.css` `.mini-ball.potted { opacity: .2 }`）。
        /// 这是"还剩哪些球"的**唯一**视觉编码 —— 不用额外的文字计数重复表达同一件事。
        /// </summary>
        public const float PottedBallOpacity = 0.2f;

        /// <summary>头像直径 `46`，内部图标 `23`。</summary>
        public const float AvatarSize = 46f;
        public const float AvatarIconSize = 23f;

        /// <summary>迷你球（记分牌里的剩余/已进球小圆）直径 `15`。</summary>
        public const float MiniBallSize = 15f;

        /// <summary>
        /// 迷你球上的号码字号 `8`（`style.css` `.mini-ball` 的设计值）。
        ///
        /// ⚠ **实测：这个常量目前没有任何使用者** —— 记分牌上的 15 个迷你球是**贴片精灵**（`ClubBallChips`），
        /// 号码**烘在贴图里**、不走 TMP 文字（见 `PracticeHudView.BuildBallRow`）。
        /// ⇒ 它**不参与** `PracticeHudLayout.FontScale` 的放大，也**不是**"字号例外"：
        ///   没有文字，就没有可放大的东西。
        /// ⚠ **将来若要给迷你球加文字**：字号**必须**走 `PracticeHudView.CreateText`（那里乘 `FontScale`）
        ///   或 `PracticeHudLayout.RenderedFontPixels`，**不许**把这个常量直接写进 `text.fontSize` ——
        ///   那会造出"全 UI 都放大了、只有这块没放大"的半新半旧界面（判据 `T148(d)` 守单点）。
        /// </summary>
        public const float MiniBallFontSize = 8f;

        /// <summary>回合指示菱形 `5 × 5`。</summary>
        public const float TurnDotSize = 5f;

        /// <summary>力度条：轨道高 `5`、滑块 `14 × 14`、滑块亮边 `3`。</summary>
        public const float PowerTrackHeight = 5f;
        public const float PowerThumbSize = 14f;
        public const float PowerThumbRingWidth = 3f;

        /// <summary>状态条内图标框 `16`、图标 `15`。</summary>
        public const float StatusSymbolBox = 16f;
        public const float StatusSymbolSize = 15f;

        /// <summary>提示条左侧 3 px 黄铜竖条。</summary>
        public const float ToastEdgeWidth = 3f;

        /// <summary>结算卡顶部 3 px 主色条。</summary>
        public const float ResultTopEdgeWidth = 3f;

        /// <summary>结算卡圆徽章直径（普通 `60`／全场 `72`）。</summary>
        public const float ResultIconSize = 60f;
        public const float ResultIconSizeMatch = 72f;

        /// <summary>结算卡圆徽章内图标 `27`。</summary>
        public const float ResultIconGlyphSize = 27f;

        /// <summary>出杆栏设计高 `77`（`style.css` `.shotbar`），内距 `16 × 22`。</summary>
        public const float ShotbarHeight = 77f;
        public const float ShotbarPaddingY = 16f;
        public const float ShotbarPaddingX = 22f;

        // ════════════════ 字号阶梯（`style.css` 逐条）══════════════════════════

        /// <summary>微标签 `8px`（统计标签、eyebrow 之外的最次级文字）。</summary>
        public const float FontMicro = 8f;

        /// <summary>小号 `9px`（`POWER` 标签、品牌副标题、`score-center > span`）。</summary>
        public const float FontCaption = 9f;

        /// <summary>正文小号 `10px`（球员副信息、`practice-meter span`）。</summary>
        public const float FontSmall = 10f;

        /// <summary>正文小号 `11px`（对话框段落）。</summary>
        public const float FontBodySmall = 11f;

        /// <summary>正文 `12px`（状态条、分段控件、按钮）。</summary>
        public const float FontBody = 12f;

        /// <summary>标签 `13px`（球员名、toast）。</summary>
        public const float FontLabel = 13f;

        /// <summary>品牌字 `24px` Barlow Condensed。</summary>
        public const float FontBrand = 24f;

        /// <summary>比分大字 `29px` Barlow Condensed。</summary>
        public const float FontScore = 29f;

        /// <summary>比分分隔符 `18px`。</summary>
        public const float FontScoreDivider = 18f;

        /// <summary>结算统计数字 `27px` Barlow Condensed。</summary>
        public const float FontStatValue = 27f;

        /// <summary>结算副标题下的说明 `11px`。</summary>
        public const float FontResultBody = 11f;

        /// <summary>结算总比分 `34px` Barlow Condensed。</summary>
        public const float FontResultScore = 34f;

        /// <summary>结算标题 `38px` Barlow Condensed。</summary>
        public const float FontResultTitle = 38f;

        // ── 开始界面（首页）字号（`style.css` 的 `.home-*` 段逐条）──────────────
        // ⚠ 首页里 8 / 10 / 11 / 12 px 四档**不另立常量**：与既有 `FontMicro` / `FontSmall` /
        //   `FontBodySmall` / `FontBody` 逐值相同 ⇒ 复用既有令牌，避免"同一数值两个名字"。

        /// <summary>首页俱乐部名 `18px` Barlow Condensed（`.home-club-mark > span`）。</summary>
        public const float FontHomeClubName = 18f;

        /// <summary>首页大标题 `76px` Barlow Condensed（`.home-menu h1`）。</summary>
        public const float FontHomeTitle = 76f;

        /// <summary>首页列表按钮 `23px` Barlow Condensed（`.home-action`）。</summary>
        public const float FontHomeAction = 23f;

        /// <summary>首页统计数字 `25px` Barlow Condensed（`.home-records strong`）。</summary>
        public const float FontHomeStatValue = 25f;

        /// <summary>首页右下台号 `28px` Barlow Condensed（`.home-table-label strong`）。</summary>
        public const float FontHomeHouseNumber = 28f;

        /// <summary>主按钮字重 `700`、次按钮 `400`。</summary>
        public const int WeightBold = 700;
        public const int WeightSemiBold = 600;

        /// <summary>禁用态不透明度 `button:disabled { opacity: .38 }`。</summary>
        public const float DisabledOpacity = 0.38f;

        // ════════════════ 具名清册（供 `T124` 逐条核对，防"令牌被悄悄改动"）═══

        /// <summary>令牌名 → 色值的**完整清册**（判据读它，不读实现细节）。</summary>
        public static readonly (string Name, Color32 Value)[] ColorLedger =
        {
            (nameof(Accent), Accent),
            (nameof(Green), Green),
            (nameof(Ink), Ink),
            (nameof(Cloth), Cloth),
            (nameof(Muted), Muted),
            (nameof(Text), Text),
            (nameof(Focus), Focus),
            (nameof(Body), Body),
            (nameof(PowerValue), PowerValue),
            (nameof(PowerTrack), PowerTrack),
            (nameof(PowerThumb), PowerThumb),
            (nameof(PowerThumbRing), PowerThumbRing),
            (nameof(ButtonFill), ButtonFill),
            (nameof(ButtonFillHover), ButtonFillHover),
            (nameof(ButtonFillPressed), ButtonFillPressed),
            (nameof(ButtonFillDisabled), ButtonFillDisabled),
            (nameof(ButtonEdge), ButtonEdge),
            (nameof(ButtonText), ButtonText),
            (nameof(ButtonEdgeSecondary), ButtonEdgeSecondary),
            (nameof(AvatarFill), AvatarFill),
            (nameof(AvatarEdge), AvatarEdge),
            (nameof(AvatarIcon), AvatarIcon),
            (nameof(BrandBallFill), BrandBallFill),
            (nameof(BrandBallText), BrandBallText),
            (nameof(BrandBallRing), BrandBallRing),
            (nameof(SegmentIdleText), SegmentIdleText),
            (nameof(SegmentActiveText), SegmentActiveText),
            (nameof(IconIdle), IconIdle),
            (nameof(IconHover), IconHover),
            (nameof(DisabledIcon), DisabledIcon),
            (nameof(ScoreDivider), ScoreDivider),
            (nameof(Label), Label),
            (nameof(ToastText), ToastText),
            (nameof(MiniBallEdge), MiniBallEdge),
            (nameof(MiniBallText), MiniBallText),
            (nameof(Opaque), Opaque),
            (nameof(Transparent), Transparent),
            (nameof(PanelScoreboard), PanelScoreboard),
            (nameof(PanelStatus), PanelStatus),
            (nameof(PanelShotbar), PanelShotbar),
            (nameof(PanelSegment), PanelSegment),
            (nameof(PanelIconButton), PanelIconButton),
            (nameof(PanelCard), PanelCard),
            (nameof(Backdrop), Backdrop),
            (nameof(LineFaint), LineFaint),
            (nameof(LineDivider), LineDivider),
            (nameof(Line), Line),
            (nameof(LineMid), LineMid),
            (nameof(LineButton), LineButton),
            (nameof(LineStrong), LineStrong),
            (nameof(LineBadge), LineBadge),
            (nameof(LineScoreTop), LineScoreTop),
            (nameof(LineEmphasis), LineEmphasis),
            (nameof(LineCardEdge), LineCardEdge),
            (nameof(AiTurnEdge), AiTurnEdge),
            (nameof(AiTurnSymbol), AiTurnSymbol),
            (nameof(BallInHandEdge), BallInHandEdge),
            (nameof(BallInHandSymbol), BallInHandSymbol),
            (nameof(CallPocketEdge), CallPocketEdge),
            (nameof(CallPocketSymbol), CallPocketSymbol),
            (nameof(FoulEdge), FoulEdge),
            (nameof(FoulSymbol), FoulSymbol),
            (nameof(FoulFill), FoulFill),
            (nameof(PausedSymbol), PausedSymbol),
            (nameof(ResultLoseEdge), ResultLoseEdge),
            (nameof(ResultLoseIcon), ResultLoseIcon),
            (nameof(ResultLoseIconEdge), ResultLoseIconEdge),
            (nameof(ResultLoseBadgeEdge), ResultLoseBadgeEdge),
            (nameof(ResultMatchIconFill), ResultMatchIconFill),
            (nameof(ResultMatchLoseIconFill), ResultMatchLoseIconFill),
            (nameof(ResultScore), ResultScore),
            (nameof(HomeClubName), HomeClubName),
            (nameof(HomeClubSub), HomeClubSub),
            (nameof(HomeHint), HomeHint),
            (nameof(HomeRule), HomeRule),
            (nameof(HomeActionFill), HomeActionFill),
            (nameof(HomeActionEdge), HomeActionEdge),
            (nameof(HomeStatLabel), HomeStatLabel),
            (nameof(HomeHouseTint), HomeHouseTint),
        };

        // ════════════════ 小工具 ═══════════════════════════════════════════════

        /// <summary>`0xRRGGBB` → 不透明 <see cref="Color32"/>（alpha = 255）。</summary>
        public static Color32 Rgb(int rgb)
        {
            return new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 0xFF);
        }

        /// <summary>
        /// 只换 alpha、**保留三通道原值**。
        /// 存在理由是"视图里不得出现颜色构造字面量"：需要"把某个已有色变半透明"时若就地
        /// 写 `new Color(c.r, c.g, c.b, a)`，视图里就又多了一处颜色构造点，
        /// "零字面量"这条守卫会被迫放宽成"差不多为零"（守卫强度 = 守卫读的东西，铁律 10）。
        /// </summary>
        public static Color WithAlpha(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, alpha);
        }

        /// <summary>同一 RGB、替换 alpha 的派生色（边线族全部由主色降透明度得到）。</summary>
        public static Color32 Alpha(Color32 color, byte alpha)
        {
            return new Color32(color.r, color.g, color.b, alpha);
        }

        /// <summary>`#rrggbb`（可带 `#rrggbbaa`）文本 → 色值；非法输入返回 <c>false</c>（**不抛**，供判据稳妥使用）。</summary>
        public static bool TryParseHex(string text, out Color32 color)
        {
            color = default;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string hex = text.Trim();
            if (hex.StartsWith("#"))
            {
                hex = hex.Substring(1);
            }

            if (hex.Length != 6 && hex.Length != 8)
            {
                return false;
            }

            if (!TryParseByte(hex, 0, out byte r) || !TryParseByte(hex, 2, out byte g) || !TryParseByte(hex, 4, out byte b))
            {
                return false;
            }

            byte a = 0xFF;
            if (hex.Length == 8 && !TryParseByte(hex, 6, out a))
            {
                return false;
            }

            color = new Color32(r, g, b, a);
            return true;
        }

        /// <summary>`#rrggbb` 文本（**不含 alpha**）—— 判据报错信息与文档对照用。</summary>
        public static string ToHexRgb(Color32 color)
        {
            return "#" + color.r.ToString("x2") + color.g.ToString("x2") + color.b.ToString("x2");
        }

        /// <summary>`#rrggbbaa` 文本（含 alpha）。</summary>
        public static string ToHexRgba(Color32 color)
        {
            return ToHexRgb(color) + color.a.ToString("x2");
        }

        private static bool TryParseByte(string hex, int offset, out byte value)
        {
            value = 0;
            int high = HexDigit(hex[offset]);
            int low = HexDigit(hex[offset + 1]);
            if (high < 0 || low < 0)
            {
                return false;
            }

            value = (byte)((high << 4) | low);
            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            if (c >= 'A' && c <= 'F')
            {
                return c - 'A' + 10;
            }

            return -1;
        }
    }
}
