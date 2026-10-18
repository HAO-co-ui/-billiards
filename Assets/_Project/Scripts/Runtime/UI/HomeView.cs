using Billiards.Core;
using Billiards.Physics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **开始界面（首页）** —— 按美术包 `04_UI/Reference_Screens/home-1920x1080.png`
    /// 与 `Web_Implementation_Reference/style.css` 的 `.home-*` 段重建，代码建 uGUI，
    /// **建在既有 HUD 画布上**（与 `PracticeResultView` 同一套做法，不新开第二台画布）。
    ///
    /// ── 逐块映射（参考稿 → 本类）────────────────────────────────────────────────
    /// · 左上 `.home-club-mark` → 俱乐部徽章位图（精灵槽 `ClubCrest`）+ `THE EIGHT BALL CLUB`
    ///   + `PRIVATE TABLE · NO. 35`；
    /// · `.home-menu` 列 → eyebrow `8-BALL CLUB` / 大标题 `POOL ROOM` / 标题行（`TABLE 01` + 两侧细线）
    ///   / 两个列表按钮 / `OPPONENT` + 难度分段；
    /// · `.home-records` → 三项统计（`SHOTS` / `POCKETED` / `RACKS`，数字在上、标签在下）；
    /// · `.home-table-label` → 右下 `HOUSE TABLE` + 大号 `01`；
    /// · `.home-track` → 右下曲目行；
    /// · `.home-tools` → 右上两个 38×38 图标按钮。
    /// 几何**全部**取自 <see cref="HomeLayout"/>（单点）；色/字号取自 <see cref="ClubUiTokens"/>。
    /// 本类里**不出现任何颜色字面量或字号字面量**（判据 `T124 ④` 守这条）。
    ///
    /// ── 输入让位（关键，工程踩过的坑）──────────────────────────────────────────
    /// 首页打开期间 <see cref="IsOpen"/> 为真，`PointerAimController` / `PointerPowerController` /
    /// `AimAssistController` 据此**整体让位** —— 否则在首页上右键拖拽会**蓄力并在松手时出杆**，
    /// 把还没开始的一局当场打乱。这是 `PracticeResultView.IsOpen` 之外**第二个**模态判据点
    /// （结算卡当年就是这么被咬过一次的）。
    /// 同时把 HUD 三块分区隐藏（<see cref="PracticeHudView.SetChromeVisible"/>）：参考稿的
    /// `body.home-active` 明确 `display:none`，且本工程 HUD 的顶带与首页徽章在**同一片区域**
    /// —— 不隐藏就必然叠字。
    ///
    /// ── 冒烟让位（**验收工具必须量的是玩法，不是菜单**）──────────────────────────
    /// `SmokeProbe.IsSmokeMode` 为真（= 命令行带了 `-billiards-smoke`）时，**启动不显示首页**
    /// （见 <see cref="Awake"/>）。理由不只是"帧率数据失真"，更硬的是：预测路径走
    /// `AimAssistController.ShouldShow`，而它被 <see cref="IsOpen"/> 整体让位 ⇒
    /// `-billiards-smoke-aim` 会测到"**没有预测的帧**"，即**测错了对象**。
    /// 抑制**必须可验证** ⇒ 实际走向经 <see cref="SmokeProbe.ReportHomeStartup"/> 落进
    /// `smoke.json` 的 `homeStartup` 字段（`suppressed` / `shown` / `unreported`，语义见规格 §9.3）。
    ///
    /// ⚠ **依赖方向**：本类（UI）读 Core 的 <see cref="SmokeProbe.IsSmokeMode"/> 是**有意**的 ——
    /// 反方向（让 Core 去关首页）会形成 Core → UI 的反向依赖。故 Core 只提供"本次进程是冒烟模式"
    /// 这个**中性事实**，由首页自己决定让位。
    ///
    /// ── 与参考稿的三处**偏离**（全部如实登记，不包装成"照做"）────────────────────
    /// ① **两个列表按钮同款式**（深色半透明底 + 描边 + 左文字 + 右箭头）。
    ///    参考稿把 `AI Match` 画成**黄铜实底的主按钮**（它是设计里的首选动作）；但本工程
    ///    **没有 AI 对手**（35-A 是单机练习卡）⇒ 把一个**不能用的**入口画成整屏最亮的一个点，
    ///    正是"假的可点态"。故两行取同一款式，`Practice` 是**首页唯一真能用的入口**。
    /// ② **难度分段整体禁用**：参考稿的三档难度在本工程**没有难度系统**承接
    ///    （`Casual/Club/Expert` 选谁都不会改变任何东西）⇒ 保留参考稿的几何与"`Club` 选中"
    ///    的配色位置（信息仍在），但整段套一层 `CanvasGroup` 压到 `DisabledOpacity`
    ///    且**不挂任何 `Button`**（点击无效）。压在什么条件下能接上：难度要有真承接方
    ///    （AI 强度 / 让球规则）才有意义 ⇒ 属 **35-B**。
    /// ③ **右上两个按钮是 VFX / SFX**（复用 HUD 上已经在写的两个真开关），
    ///    而参考稿是 `music-2` + `settings-2`：本工程**没有设置页**（做出来就是"点了没反应"
    ///    的假齿轮），曲目开关也**没有独立宿主**（BGM 与音效同在 `PoolAudio.audioEnabled` 下）。
    ///    故只放**真的能改状态**的两个。右下曲目行的音乐图标同理复用 `Volume` 精灵：
    ///    参考稿的 `music-2` 图标本工程没有对应槽位，而 `♫`（U+266B）**5 个源字体全都没有**
    ///    （实测读 `cmap`）⇒ 写成字符会直接打红 `T97 (c)`。
    ///
    /// ── 底部三项统计的**真源**（不是占位 0）──────────────────────────────────
    /// 三项 = <see cref="PracticeSessionStore.ReadHistoryFromDisk"/> 的历史记录**累加**
    /// （`shots` / `pots` / `racks`），与参考实现 `home-ui.js` 的 `sum(history ∪ session)` 同语义。
    /// 没有导出记录时三项**诚实地显示 0**（不是"填个 0 占位"）。
    ///
    /// ── 盲带（如实）──────────────────────────────────────────────────────────
    /// · 参考稿 `.home-screen` 的 `text-shadow` 在 uGUI 无对应能力 ⇒ 首页文字直接压在 3D 画面上，
    ///   对比度属**实机观感**问题（与既有 HUD 的登记口径一致），由用户截图验收。
    /// · 参考稿的三个 `@media` 断点是**重排**；uGUI 无流式布局 ⇒ 非 16:9 档按锚点等比拉伸
    ///   （登记为表现差异，见 <see cref="HomeLayout"/> 的类注释）。
    /// · 按钮的 `hover` / `focus` 视觉态不做（本工程既有控件一律不做，uGUI 侧无统一实现）。
    ///
    /// ⚠ **两条已关闭的登记（P6）**：本类注释与结算卡注释里曾写
    /// 「**"回到首页"没有实现**……首页只在**启动时**出现一次。加"局内返回首页"要有暂停/离开语义，属 35-B」
    /// 与「参考的 `Back to club` **无处可去**（没有首页/俱乐部场景）」。P6 起这两条**已为假**：
    /// 本类在 `Awake` 把 `Show` 注册进 <see cref="HomeSeams.Show"/>，结算卡的 `Back to club`
    /// 因此**真的能回到首页**（未注册时保持卡片并给可见提示，**不假装**）。
    /// </summary>
    [AddComponentMenu("Billiards/Home View")]
    public sealed class HomeView : MonoBehaviour, Billiards.Core.ISmokeTransitionTrigger
    {
        /// <summary>
        /// 首页是否正显示 —— **交互层让位的第二个判据点**（第一个是
        /// <see cref="PracticeResultView.IsOpen"/>）。三个输入控制器都读它。
        /// </summary>
        public static bool IsOpen { get; private set; }

        [Tooltip("HUD 视图（借它的画布、字体与精灵槽位；为空 ⇒ 本组件不生效、也不锁玩法）。")]
        public PracticeHudView hud;

        [Tooltip("特效宿主（右上 VFX 开关写它的 vfxEnabled）。为空 ⇒ 该开关只显示、不接线。")]
        public PoolVfxView vfxView;

        [Tooltip("音频宿主（右上 SFX 开关写它的 audioEnabled）。为空 ⇒ 该开关只显示、不接线。")]
        public PoolAudio poolAudio;

        [Tooltip("是否在启用时（= 启动时）立即显示首页。")]
        public bool shownOnStart = true;

        private bool _built;
        private RectTransform _root;
        private Image _vfxIcon;
        private Image _sfxIcon;
        private TextMeshProUGUI[] _statValues;

        // ── P5-B：赛前屏（模态）────────────────────────────────────────────────
        private RectTransform _introLayer;
        private TextMeshProUGUI _introOpponentName;
        private Image[] _introTierFills;
        private TextMeshProUGUI[] _introTierLabels;

        /// <summary>
        /// **本局是不是 AI 对战**（**UI 侧单一真源**）。`PracticeHudView` 只读它决定 HUD 形态。
        ///
        /// 为什么在 UI 侧：`PhysicsTestDrive` **没有**"本局模式"出口（只有 `MatchOver` / `Difficulty`），
        /// 而"对战 HUD ≠ 练习 HUD"是**纯显示**问题 ⇒ 由**发起方**（首页两个入口）置位。
        /// ⚠ **只许在两处置位**：`OnBeginMatch()` = `true`、`OnStartPractice()` = `false`（两个写入口必然分叉）。
        /// ⚠ 它**只表示"是不是对战"**；球号数据仍只来自
        /// `PhysicsTestDrive.PlayerRemainingNumbers` / `OpponentRemainingNumbers`（不许在此再挂一份球号状态）。
        /// </summary>
        public static bool IsAiMatchSession { get; private set; }

        private void Awake()
        {
            EnsureBuilt();

            // **注册"回到首页"接缝**（P6）：结算卡上的 `Back to club` 经它把首页调回来。
            // 注册放在 `shownOnStart` 的判断**之前** —— "能不能被叫回来"与"启动时要不要显示"是两件事。
            HomeSeams.Show = Show;

            if (!shownOnStart)
            {
                return;
            }

            // **冒烟让位**（`SmokeProbe.IsSmokeMode`，由冒烟入口在**场景加载前**置位 —— 时序见 `ParseEarly`）：
            // 两个冒烟都是**无人操作跑 N 帧**的验收工具，要量的是"玩法负载"而不是"开始界面"。
            // 更硬的一条：`AimAssistController.ShouldShow` 会被 <see cref="IsOpen"/> 整体让位
            // ⇒ 预测路径根本不执行 ⇒ `-billiards-smoke-aim` 会测到"**没有预测的帧**"
            // （那是**测错了对象**，不是"数字偏乐观"）—— 这正是必须抑制、且必须**可验证**的理由。
            // 故把实际走向上报给 <see cref="SmokeProbe"/>，落进 `smoke.json` 的 `homeStartup` 字段。
            // ⚠ **例外：转场负载探针**（`-billiards-smoke-transition`）**必须让首页显示** ——
            //   它要的就是"首页 → 打球"这一次切换；抑制掉首页就**没有转场可测**（探针会给假阴性）。
            //   ⇒ 抑制条件 = `IsSmokeMode && !IsTransitionProbe`；该探针的 `homeStartup` 会是 `shown`（预期）。
            if (SmokeProbe.IsSmokeMode && !SmokeProbe.IsTransitionProbe)
            {
                SmokeProbe.ReportHomeStartup(SmokeProbe.HomeStartupSuppressed);
                Debug.Log("[HOME] 冒烟模式：抑制首页启动显示（本次采样量的是玩法负载）。");
                return;
            }

            Show();
            SmokeProbe.ReportHomeStartup(SmokeProbe.HomeStartupShown);
        }

        private void OnEnable()
        {
            EnsureBuilt();
        }

        private void OnDisable()
        {
            IsOpen = false;

            // **只注销自己**（照 `PhysicsTestDrive.UnregisterMatchHost` 的既有口径）：
            // 注册点只有一个，别的东西可能已把它接管过去，无条件置 `null` 会把别人的注册一起抹掉。
            if (HomeSeams.Show == (System.Action)Show)
            {
                HomeSeams.Show = null;
            }

            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }

            if (hud != null)
            {
                hud.SetChromeVisible(true);
            }
        }

        /// <summary>
        /// **幂等建树**（`Awake` 会调；判据也可直接调 —— EditMode 下 `AddComponent` 不触发 `Awake`）。
        /// 与 <see cref="PracticeResultView.EnsureBuilt"/> 同构：建完**默认隐藏**，由 <see cref="Show"/> 打开。
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;

            if (hud == null)
            {
                return;
            }

            // 建在**既有 HUD 画布**上（同一 `CanvasScaler` 口径、同一个 `GraphicRaycaster`，
            // 不新开第二台画布 —— "全场景唯一带射线器的画布"这条不变量不许破）。
            _root = PracticeHudView.AddRect(hud.CanvasRoot, "Home", Vector2.zero, Vector2.one);

            ReportOutOfBounds();

            BuildCrest();
            BuildMenu();
            BuildRecords();
            BuildHouseLabel();
            BuildTrack();
            BuildTools();
            BuildMatchIntro();   // P5-B：赛前屏（模态；默认隐藏）

            _root.gameObject.SetActive(false);
        }

        /// <summary>构建期越界自检：任何元素出画布都要当场说出来（不能等实机才发现被裁掉）。</summary>
        private void ReportOutOfBounds()
        {
            foreach (Rect rect in HomeLayout.AllRects())
            {
                if (!HomeLayout.WithinViewport(rect))
                {
                    Debug.LogWarning(
                        $"HomeView：首页元素越出画布 rect={rect}（设计画布 "
                        + $"{HomeLayout.ReferenceWidth}×{HomeLayout.ReferenceHeight}）—— 会被裁掉。", this);
                }
            }
        }

        // ════════════════ 左上：俱乐部徽章 + 名字 ═══════════════════════════════

        private void BuildCrest()
        {
            // 徽章是**彩色成品位图**（不是纯白图标）⇒ 上色必须用 `Opaque`，否则会把徽章染坏
            // （与 16 个球号贴片同一处置口径）。
            RectTransform crest = hud.AddFixed(_root, "Crest", ClubUiSprites.UiSprite.ClubCrest,
                ClubUiTokens.Opaque, Center(HomeLayout.Crest),
                new Vector2(HomeLayout.CrestSize, HomeLayout.CrestSize));

            var crestImage = crest.GetComponent<Image>();
            if (crestImage != null && crestImage.sprite == null)
            {
                // 精灵没接上时 `Image` 会画一块**白色方块**（比缺图更糟）⇒ 明确停用该元素。
                crest.gameObject.SetActive(false);
                Debug.LogWarning("HomeView：俱乐部徽章精灵缺失（槽位 ClubCrest）⇒ 已隐藏该元素。", this);
            }

            CreateHomeText("ClubName", hud.titleFont, ClubUiTokens.FontHomeClubName, ClubUiTokens.HomeClubName,
                TextAlignmentOptions.Left, HomeLayout.ClubName).text = PracticeHudState.HomeClubName;

            CreateHomeText("ClubSub", hud.bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.HomeClubSub,
                TextAlignmentOptions.Left, HomeLayout.ClubSub).text = PracticeHudState.HomeClubSub;
        }

        // ════════════════ 左中：标题列（eyebrow / 大标题 / 标题行 / 两个入口 / 难度）═══

        private void BuildMenu()
        {
            CreateHomeText("HomeEyebrow", hud.bodyFont, ClubUiTokens.FontBody, ClubUiTokens.Accent,
                TextAlignmentOptions.Left, HomeLayout.MenuEyebrow).text = PracticeHudState.HomeEyebrow;

            CreateHomeText("HomeTitle", hud.titleFont, ClubUiTokens.FontHomeTitle, ClubUiTokens.Text,
                TextAlignmentOptions.Left, HomeLayout.MenuTitle).text = PracticeHudState.BrandTitle;

            BuildHeadingRule();

            // 第一行：`AI Match` —— **真入口**（P5-B：点开赛前屏）。
            // 参考稿把它画成**黄铜实底的主按钮**（`.home-action`）；本工程曾因"没有 AI 对手"把它压暗并
            // 登记"Ai 落地时必须恢复上亮下暗"（规格 §9.2）⇒ 本轮落地即**恢复**（`primary: true`）。
            // ⚠ 界面一旦显示为可点，就必须真的能点开（既有铁律：界面不得留假陈述）。
            BuildActionRow("ActionAiMatch", HomeLayout.ActionAiMatch, PracticeHudState.ModeAiMatch,
                true, OnStartMatch, primary: true);

            // 第二行：`Practice` —— 次按钮（深底描边），一直是真入口。
            BuildActionRow("ActionPractice", HomeLayout.ActionPractice, PracticeHudState.ModePractice, true, OnStartPractice);


        }

        /// <summary>标题行：左侧细线 + `TABLE 01` + 右侧细线（参考稿 `.home-heading-rule` 的 `flex:1` 两侧线）。</summary>
        private void BuildHeadingRule()
        {
            RectTransform rule = PracticeHudView.AddRect(_root, "HeadingRule",
                Min(HomeLayout.HeadingRule), Max(HomeLayout.HeadingRule));

            // 两侧细线各让出「`TABLE 01` 的半宽 + 间距」——与 CSS 的 `gap:12px` + 文字宽度等价。
            float inset = HomeLayout.TableNumberHalfWidth + HomeLayout.RuleGap;
            float halfText = HomeLayout.TableNumberHalfWidth / HomeLayout.MenuWidth;

            AddRuleLine(rule, "RuleLeft", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), 0f, -inset);
            AddRuleLine(rule, "RuleRight", new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), inset, 0f);

            PracticeHudView.CreateText(rule, "TableNumber", hud.bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.HomeHint,
                TextAlignmentOptions.Center,
                new Vector2(0.5f - halfText, 0f), new Vector2(0.5f + halfText, 1f),
                applyGlobalScale: false)
                .text = PracticeHudState.HomeTableNumber;
        }

        /// <summary>
        /// 建一条 1 px 细线。<paramref name="offsetMinX"/> / <paramref name="offsetMaxX"/> 是
        /// **相对各自锚点**的像素偏移（canvas 单位 = 设计像素）⇒ 与 `AddEdgeStrip` 同一口径。
        /// </summary>
        private void AddRuleLine(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                 float offsetMinX, float offsetMaxX)
        {
            RectTransform rect = PracticeHudView.AddRect(parent, name, anchorMin, anchorMax);
            rect.offsetMin = new Vector2(offsetMinX, -ClubUiTokens.Hairline * 0.5f);
            rect.offsetMax = new Vector2(offsetMaxX, ClubUiTokens.Hairline * 0.5f);
            hud.SetImage(rect.gameObject.AddComponent<Image>(), null, ClubUiTokens.HomeRule);
        }

        /// <summary>
        /// 一个整行宽的列表按钮。
        /// <paramref name="interactive"/> 为 <c>false</c> ⇒ **不挂 `Button`**，整行压到
        /// <see cref="ClubUiTokens.DisabledOpacity"/> 且不接收射线
        /// （"点了没反应"的假可点态是本工程明令禁止的）。
        /// </summary>
        private void BuildActionRow(string name, Rect rect, string label, bool interactive,
                                    UnityEngine.Events.UnityAction action, bool primary = false)
        {
            // **两行不同款式**（参考稿：`AI Match` = `.home-action` 黄铜实底主按钮；`Practice` = `.home-secondary` 深底描边）
            // ⇒ 令牌按 `primary` 二选一，**不再写死一套**（这是"恢复上亮下暗"的落点）。
            Color32 fill = primary ? ClubUiTokens.ButtonFill : ClubUiTokens.HomeActionFill;
            Color32 edge = primary ? ClubUiTokens.ButtonEdge : ClubUiTokens.HomeActionEdge;
            Color32 labelColor = primary ? ClubUiTokens.ButtonText : ClubUiTokens.Text;

            RectTransform frame = hud.AddFramed(_root, name, Min(rect), Max(rect),
                edge, fill, ClubUiTokens.RadiusButton);

            // **文案居中**（用户："字体居中"）：整行等宽居中，不再左对齐 + 右侧留白。
            PracticeHudView.CreateText(frame, "Label", hud.titleFont, ClubUiTokens.FontHomeAction, labelColor,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one,
                applyGlobalScale: false)
                .text = label;

            // 箭头：`x` 取「右边距 20 + 半个箭头」⇒ 与 CSS 的 `padding:13px 20px` + `space-between` 等价。
            float glyphX = 1f - (HomeLayout.ActionPaddingX + HomeLayout.ActionGlyphSize * 0.5f) / HomeLayout.MenuWidth;
            hud.AddFixed(frame, "Glyph", ClubUiSprites.UiSprite.ArrowRight, ClubUiTokens.Text,
                new Vector2(glyphX, 0.5f), new Vector2(HomeLayout.ActionGlyphSize, HomeLayout.ActionGlyphSize));

            if (!interactive)
            {
                var disabled = frame.gameObject.AddComponent<CanvasGroup>();
                disabled.alpha = ClubUiTokens.DisabledOpacity;
                disabled.interactable = false;
                disabled.blocksRaycasts = false;
                return;
            }

            var hit = frame.GetComponent<Image>();
            hit.raycastTarget = true;   // 首页上真的可点的三个控件之一

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                PlayUiClick();
                action();
            });
        }


        // ════════════════ P5-B：赛前屏（模态）════════════════════════════════
        //
        // 参考稿把它做成**居中模态**（`style.css:167` `#match-intro-dialog`）⇒ 本视图**不换页**，
        // 只在首页之上**叠一层**（`_introLayer`）。两个理由：
        //  · 换页要重排整棵首页树（`AllRects()` 自检、`T124`/`T98` 的口径都要动）——代价不成比例；
        //  · 叠加层**顺带保住了输入让位**：首页期间 `HomeView.IsOpen` 恒为 `true`，四个输入控制器读的就是它
        //    ⇒ 赛前屏打开时右键拖拽**不会**去蓄力出杆（这正是本项目吃过的坑，见类注释"输入让位"）。
        // 代价：模态打开时底下的"俱乐部"画面仍在（参考稿的 `dialog` 同样压在 `body.home-active` 上）✓

        private void BuildMatchIntro()
        {
            Rect card = HomeLayout.MatchIntroCard;

            _introLayer = PracticeHudView.AddRect(_root, "MatchIntro", Vector2.zero, Vector2.one);

            hud.SetImage(PracticeHudView.AddRect(_introLayer, "Backdrop", Vector2.zero, Vector2.one)
                .gameObject.AddComponent<Image>(), null, ClubUiTokens.Backdrop);

            RectTransform cardRt = hud.AddFramed(_introLayer, "Card", Min(card), Max(card),
                ClubUiTokens.LineCardEdge, ClubUiTokens.PanelCard, ClubUiTokens.RadiusDialog);

            // 顶部黄铜边（参考稿 `border-top: 3px solid var(--accent)`）
            hud.SetImage(PracticeHudView.AddRect(cardRt, "TopEdge",
                    RelativeMin(card, HomeLayout.MatchIntroCardTopEdge),
                    RelativeMax(card, HomeLayout.MatchIntroCardTopEdge)).gameObject.AddComponent<Image>(),
                null, ClubUiTokens.ButtonFill);

            AddIntroText(cardRt, card, "Eyebrow", hud.bodyFont, ClubUiTokens.FontBody, ClubUiTokens.Accent,
                PracticeHudState.HomeEyebrow, HomeLayout.MatchIntroEyebrow);
            AddIntroText(cardRt, card, "Title", hud.titleFont, ClubUiTokens.FontResultTitle, ClubUiTokens.Text,
                PracticeHudState.MatchIntroTitle, HomeLayout.MatchIntroTitle);

            // 对阵行：图标在上、名字在下（参考稿 `.intro-player` 的两列）
            hud.AddFixed(cardRt, "PlayerIcon", ClubUiSprites.UiSprite.Player, ClubUiTokens.Text,
                RelativeCenter(card, HomeLayout.MatchIntroPlayerIcon),
                new Vector2(HomeLayout.MatchIntroAvatarSize, HomeLayout.MatchIntroAvatarSize));
            AddIntroText(cardRt, card, "PlayerName", hud.bodyFont, ClubUiTokens.FontBody, ClubUiTokens.Text,
                PracticeHudState.MatchIntroYou, HomeLayout.MatchIntroPlayerName);
            AddIntroText(cardRt, card, "Versus", hud.titleFont, ClubUiTokens.FontHomeAction, ClubUiTokens.Accent,
                PracticeHudState.MatchIntroVersus, HomeLayout.MatchIntroVersus);
            hud.AddFixed(cardRt, "OpponentIcon", ClubUiSprites.UiSprite.Robot, ClubUiTokens.Text,
                RelativeCenter(card, HomeLayout.MatchIntroOpponentIcon),
                new Vector2(HomeLayout.MatchIntroAvatarSize, HomeLayout.MatchIntroAvatarSize));

            // 对手名 = `<难度> AI`（**动态**：随档位变，见 `RefreshMatchIntro`）
            _introOpponentName = AddIntroText(cardRt, card, "OpponentName", hud.bodyFont, ClubUiTokens.FontBody,
                ClubUiTokens.Text, PracticeHudState.OpponentName(MatchSetup.Selected),
                HomeLayout.MatchIntroOpponentName);

            // 赛制块（上下各一条 1 px 细线，参考稿 `.intro-format`）
            AddIntroRule(cardRt, card, "FormatRuleTop", HomeLayout.MatchIntroFormatRuleTop);
            AddIntroText(cardRt, card, "FormatTitle", hud.titleFont, ClubUiTokens.FontHomeAction,
                ClubUiTokens.HomeClubName, PracticeHudState.MatchIntroFormatTitle,
                HomeLayout.MatchIntroFormatTitle);
            AddIntroText(cardRt, card, "FormatDetail", hud.bodyFont, ClubUiTokens.FontSmall,
                ClubUiTokens.HomeHint, PracticeHudState.MatchIntroFormatDetail,
                HomeLayout.MatchIntroFormatDetail);
            AddIntroRule(cardRt, card, "FormatRuleBottom", HomeLayout.MatchIntroFormatRuleBottom);

            AddIntroText(cardRt, card, "TierLabel", hud.bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.HomeHint,
                PracticeHudState.HomeDifficultyLabel, HomeLayout.MatchIntroTierLabel);

            BuildIntroTierSegment(cardRt, card);

            AddIntroButton(cardRt, card, "StartButton", PracticeHudState.MatchIntroStart,
                HomeLayout.MatchIntroStartButton, OnBeginMatch, primary: true);
            AddIntroButton(cardRt, card, "BackButton", PracticeHudState.MatchIntroBack,
                HomeLayout.MatchIntroBackButton, OnBackToClub, primary: false);

            _introLayer.gameObject.SetActive(false);
            RefreshMatchIntro();
        }

        /// <summary>三档分段（**可点**：三格各挂 `Button`；选中态由 `MatchSetup.ActiveIndex` 决定、可刷新）。</summary>
        private void BuildIntroTierSegment(RectTransform cardRt, Rect card)
        {
            Rect rect = HomeLayout.MatchIntroTierSegment;
            RectTransform segment = hud.AddFramed(cardRt, "TierSegment", RelativeMin(card, rect),
                RelativeMax(card, rect), ClubUiTokens.Line, ClubUiTokens.PanelSegment, ClubUiTokens.RadiusSegment);

            RectTransform inner = PracticeHudView.AddRect(segment, "Padding", Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(ClubUiTokens.SegmentPadding, ClubUiTokens.SegmentPadding);
            inner.offsetMax = new Vector2(-ClubUiTokens.SegmentPadding, -ClubUiTokens.SegmentPadding);

            int count = MatchSetup.Tiers.Length;
            _introTierFills = new Image[count];
            _introTierLabels = new TextMeshProUGUI[count];

            for (int i = 0; i < count; i++)
            {
                string label = PracticeHudState.TierLabel(MatchSetup.Tiers[i]);
                RectTransform cell = PracticeHudView.AddRect(inner, label,
                    new Vector2(i / (float)count, 0f), new Vector2((i + 1) / (float)count, 1f));

                // 选中底：**每格都建**、且**始终 enabled** ——
                // ① 切换时只改 `color`（不动 `enabled`），因为**禁用的 Graphic 收不到射线**，
                //    若用 `enabled` 表示"未选中"，就只剩选中那格能点（这是最容易踩的坑）；
                // ② 非选中的颜色取 `PanelSegment`（= 分段自己的底色）⇒ 视觉上就是"没有底"，
                //    与参考稿 `.club-segment button{background:none}` 等价，**不必新增透明令牌**。
                var fill = cell.gameObject.AddComponent<Image>();
                hud.SetImage(fill, ClubUiSprites.PanelForRadius(ClubUiTokens.RadiusSegmentInner),
                    ClubUiTokens.PanelSegment);
                fill.raycastTarget = true;   // 三格都要能点
                _introTierFills[i] = fill;

                _introTierLabels[i] = PracticeHudView.CreateText(cell, "Label", hud.bodyFont,
                    ClubUiTokens.FontBodySmall, ClubUiTokens.SegmentIdleText, TextAlignmentOptions.Center,
                    Vector2.zero, Vector2.one);
                _introTierLabels[i].text = label;

                int captured = i;   // 逐格绑定：闭包必须捕获**当次**下标
                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = fill;
                button.onClick.AddListener(() => OnSelectTier(captured));
            }
        }

        /// <summary>按 `MatchSetup` 刷新对手名与三格选中态（**唯一刷新点**）。</summary>
        private void RefreshMatchIntro()
        {
            if (_introOpponentName != null)
            {
                _introOpponentName.text = PracticeHudState.OpponentName(MatchSetup.Selected);
            }

            if (_introTierFills == null)
            {
                return;
            }

            int active = MatchSetup.ActiveIndex;
            for (int i = 0; i < _introTierFills.Length; i++)
            {
                bool on = i == active;
                if (_introTierFills[i] != null)
                {
                    // ⚠ 改 `color` 而不是 `enabled`：禁用的 Graphic **收不到射线**（见 `BuildIntroTierSegment` 的注释）
                    _introTierFills[i].color = on ? ClubUiTokens.ButtonFill : ClubUiTokens.PanelSegment;
                }

                if (_introTierLabels[i] != null)
                {
                    _introTierLabels[i].color = on ? ClubUiTokens.SegmentActiveText : ClubUiTokens.SegmentIdleText;
                }
            }
        }

        private TextMeshProUGUI AddIntroText(RectTransform parent, Rect card, string name, TMP_FontAsset font,
                                            float size, Color32 color, string text, Rect rect)
        {
            TextMeshProUGUI label = PracticeHudView.CreateText(parent, name, font, size, color,
                TextAlignmentOptions.Center, RelativeMin(card, rect), RelativeMax(card, rect));
            label.text = text;
            return label;
        }

        private void AddIntroRule(RectTransform parent, Rect card, string name, Rect rect)
        {
            hud.SetImage(PracticeHudView.AddRect(parent, name, RelativeMin(card, rect), RelativeMax(card, rect))
                .gameObject.AddComponent<Image>(), null, ClubUiTokens.LineCardEdge);
        }

        private void AddIntroButton(RectTransform parent, Rect card, string name, string label, Rect rect,
                                    UnityEngine.Events.UnityAction action, bool primary)
        {
            Color32 fill = primary ? ClubUiTokens.ButtonFill : ClubUiTokens.PanelCard;
            Color32 edge = primary ? ClubUiTokens.ButtonEdge : ClubUiTokens.ButtonEdgeSecondary;
            Color32 textColor = primary ? ClubUiTokens.ButtonText : ClubUiTokens.Text;

            RectTransform frame = hud.AddFramed(parent, name, RelativeMin(card, rect), RelativeMax(card, rect),
                edge, fill, ClubUiTokens.RadiusButton);

            PracticeHudView.CreateText(frame, "Label", hud.titleFont, ClubUiTokens.FontBody, textColor,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = label;

            var hit = frame.GetComponent<Image>();
            hit.raycastTarget = true;

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                PlayUiClick();
                action();
            });
        }

        /// <summary>把一个绝对视口矩形换算成"相对 <paramref name="outer"/>"的锚点（子物体用）。</summary>
        private static Vector2 RelativeMin(Rect outer, Rect inner)
        {
            return new Vector2((inner.xMin - outer.xMin) / outer.width, (inner.yMin - outer.yMin) / outer.height);
        }

        /// <summary>见 <see cref="RelativeMin"/>。</summary>
        private static Vector2 RelativeMax(Rect outer, Rect inner)
        {
            return new Vector2((inner.xMax - outer.xMin) / outer.width, (inner.yMax - outer.yMin) / outer.height);
        }

        /// <summary>把一个绝对视口矩形换算成"相对 <paramref name="outer"/>"的中心锚点（定尺寸子物体用）。</summary>
        private static Vector2 RelativeCenter(Rect outer, Rect inner)
        {
            return new Vector2((inner.center.x - outer.xMin) / outer.width,
                (inner.center.y - outer.yMin) / outer.height);
        }

        // ════════════════ 左下：三项统计（数字在上、标签在下）══════════════════

        private void BuildRecords()
        {
            hud.SetImage(PracticeHudView.AddRect(_root, "RecordsRule",
                    Min(HomeLayout.RecordsRule), Max(HomeLayout.RecordsRule)).gameObject.AddComponent<Image>(),
                null, ClubUiTokens.LineButton);

            string[] labels = { PracticeHudState.LabelShots, PracticeHudState.LabelPocketed, PracticeHudState.HomeStatRacks };
            _statValues = new TextMeshProUGUI[labels.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                _statValues[i] = CreateHomeText("StatValue" + i, hud.titleFont, ClubUiTokens.FontHomeStatValue,
                    ClubUiTokens.Text, TextAlignmentOptions.Left, HomeLayout.RecordsValue(i));

                CreateHomeText("StatLabel" + i, hud.bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.HomeStatLabel,
                    TextAlignmentOptions.Left, HomeLayout.RecordsLabel(i)).text = labels[i];
            }

            // 建完立刻填一次真值 ⇒ 建出来的树本身就是**自洽的**（不必等 `Show()` 才有数字），
            // 也避免在视图里留一个手写的占位常量。
            RefreshRecords();
        }

        /// <summary>把磁盘上的历史累加进三项统计（没有记录 ⇒ 诚实地显示 0）。</summary>
        private void RefreshRecords()
        {
            if (_statValues == null)
            {
                return;
            }

            int shots = 0;
            int pots = 0;
            int racks = 0;

            foreach (PracticeSessionRecord record in PracticeSessionStore.ReadHistoryFromDisk())
            {
                shots += record.shots;
                pots += record.pots;
                racks += record.racks;
            }

            _statValues[0].text = shots.ToString();
            _statValues[1].text = pots.ToString();
            _statValues[2].text = racks.ToString();
        }

        // ════════════════ 右下：台号牌 + 曲目行 ═══════════════════════════════

        private void BuildHouseLabel()
        {
            CreateHomeText("HouseText", hud.bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.HomeHouseTint,
                TextAlignmentOptions.Right, HomeLayout.HouseText).text = PracticeHudState.HomeHouseLabel;

            hud.SetImage(PracticeHudView.AddRect(_root, "HouseRule",
                    Min(HomeLayout.HouseRule), Max(HomeLayout.HouseRule)).gameObject.AddComponent<Image>(),
                null, ClubUiTokens.LineMid);

            CreateHomeText("HouseNumber", hud.titleFont, ClubUiTokens.FontHomeHouseNumber, ClubUiTokens.HomeHouseTint,
                TextAlignmentOptions.Left, HomeLayout.HouseNumber).text = PracticeHudState.HomeHouseNumber;
        }

        private void BuildTrack()
        {
            // 参考稿是 `lucide music-2`；本工程没有该槽位，且 `♫`（U+266B）**5 个源字体全都没有**
            // （实测读 cmap）⇒ 复用已有的 `Volume` 精灵（喇叭），**不写字符**（写了必打红 `T97 (c)`）。
            hud.AddFixed(_root, "TrackGlyph", ClubUiSprites.UiSprite.Volume, ClubUiTokens.Accent,
                Center(HomeLayout.TrackGlyph), new Vector2(HomeLayout.TrackGlyphSize, HomeLayout.TrackGlyphSize));

            CreateHomeText("TrackText", hud.bodyFont, ClubUiTokens.FontSmall, ClubUiTokens.HomeClubSub,
                TextAlignmentOptions.Left, HomeLayout.TrackText).text = PracticeHudState.HomeTrackTitle;
        }

        // ════════════════ 右上：两个图标按钮（VFX / SFX，真实开关）══════════════

        private void BuildTools()
        {
            _vfxIcon = AddToolButton("VfxToggle", 0, ClubUiSprites.UiSprite.Sparkle, OnToggleVfx);
            _sfxIcon = AddToolButton("SfxToggle", 1, ClubUiSprites.UiSprite.Volume, OnToggleSfx);
            RefreshToolButtons();
        }

        private Image AddToolButton(string name, int index, ClubUiSprites.UiSprite sprite,
                                    UnityEngine.Events.UnityAction action)
        {
            Rect rect = HomeLayout.ToolButton(index);
            RectTransform frame = hud.AddFramed(_root, name, Min(rect), Max(rect),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusSegment);

            var hit = frame.GetComponent<Image>();
            hit.raycastTarget = true;   // 首页上真的可点的三个控件之一

            RectTransform glyph = hud.AddFixed(frame, "Glyph", sprite, ClubUiTokens.IconIdle,
                new Vector2(0.5f, 0.5f),
                new Vector2(ClubUiTokens.IconButtonIconSize, ClubUiTokens.IconButtonIconSize));

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() =>
            {
                PlayUiClick();
                action();
            });
            return glyph.GetComponent<Image>();
        }

        private void RefreshToolButtons()
        {
            if (_vfxIcon != null)
            {
                bool on = vfxView == null || vfxView.vfxEnabled;
                _vfxIcon.color = on ? ClubUiTokens.IconIdle : ClubUiTokens.DisabledIcon;
            }

            if (_sfxIcon != null)
            {
                bool on = poolAudio == null || poolAudio.audioEnabled;
                _sfxIcon.color = on ? ClubUiTokens.IconIdle : ClubUiTokens.DisabledIcon;
            }
        }

        // ════════════════ 显隐与回调 ═══════════════════════════════════════════

        /// <summary>
        /// 显示首页：刷新三项统计与两个开关的显示态、置 <see cref="IsOpen"/>、隐藏 HUD 三块分区。
        ///
        /// ⚠ **接线缺失时不置位**（`hud` 为空 ⇒ 建不出首页）：宁可不显示首页，也**不能**把玩法锁死。
        /// 这种"失败方向"是刻意选的 —— 首页画不出来只是少个门面，`IsOpen` 被错误置真则该局玩不了。
        /// </summary>
        public void Show()
        {
            EnsureBuilt();

            if (_root == null)
            {
                Debug.LogWarning("HomeView：hud 未接线（首页建不出来）⇒ 不显示、也不锁玩法。", this);
                return;
            }

            RefreshRecords();
            RefreshToolButtons();
            HideMatchIntro();   // 每次显示首页都从"首页页"开始（赛前屏不留着上次的状态）

            IsOpen = true;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();

            if (hud != null)
            {
                // ⚠ **不在这里单独调预热**：预热（`WarmUpChromeText` + `CanvasGroup.alpha=0` 那条）已收进
                // `PracticeHudView.SetChromeVisible(false)` **内部** ⇒ **单一调用点**（同一动作不留两份）。
                // 本轮实测依据见 `PracticeHudView.WarmUpChromeText` 的注释与规格 §13.5.1。
                hud.SetChromeVisible(false);
            }
        }

        /// <summary>隐藏首页（= 进入玩法）。不重摆球堆：首页只在启动时出现，台面还没被动过。</summary>
        public void Hide()
        {
            IsOpen = false;

            if (_root != null)
            {
                _root.gameObject.SetActive(false);
            }

            if (hud != null)
            {
                hud.SetChromeVisible(true);
            }
        }

        /// <summary>`Practice`：关闭首页、交回玩法（练习入口）。</summary>
        /// <summary>点击任意按钮的短音（唯一入口；音频组件未接线时现找一次并缓存）。</summary>
        private void PlayUiClick()
        {
            if (_poolAudio == null)
            {
                _poolAudio = Object.FindObjectOfType<PoolAudio>();
            }

            if (_poolAudio != null)
            {
                _poolAudio.PlayUiClick();
            }
        }

        private PoolAudio _poolAudio;

        public void OnStartPractice()
        {
            IsAiMatchSession = false;   // **置位点 ①**（练习）

            // **练习模式没有对手**：必须同时把 Runtime 侧的 AI 开关关掉 ——
            // 否则规则层照样会把回合判给对方，AI 真的会上场出杆（用户实机反馈：
            // "练习模式不该有 AI""打着打着球杆没了"，两者是同一个根因）。
            if (hud != null && hud.drive != null)
            {
                hud.drive.EndAiMatchSession();
            }

            // 首次进入打球弹一次操作引导（`PlayerPrefs` 记档，重进不再弹）。
            if (hud != null)
            {
                hud.ShowOnboardingIfFirstTime();
            }

            Hide();
        }

        // ── P5-B：赛前屏的公开动作（三处绑定：`AI Match` 行 / `Start match` / `Back to club`）──

        /// <summary>`AI Match`：**打开赛前屏**（这就是"点亮入口"的落点：真能点开）。</summary>
        public void OnStartMatch()
        {
            if (_introLayer == null)
            {
                Debug.LogWarning("HomeView：赛前屏未建出（`hud` 未接线？）⇒ 无法打开。", this);
                return;
            }

            RefreshMatchIntro();   // 每次打开都按当前档位刷新（对手名/选中态）
            _introLayer.gameObject.SetActive(true);
            _introLayer.SetAsLastSibling();
        }

        /// <summary>`Back to club`：关闭赛前屏、回到首页（不改变 `IsOpen`）。</summary>
        public void OnBackToClub()
        {
            HideMatchIntro();
        }

        /// <summary>
        /// `Start match`：把选中的档位交给 Runtime 开局。
        /// ⚠ **未接线时保持赛前屏并告警、不进入对局** —— 界面承诺了"开始一局"就必须真的开始，
        /// 否则就是**假陈述**（本工程铁律）。`room-env` 侧注册 `MatchSeams.Host` 后本分支自然走通。
        /// </summary>
        public void OnBeginMatch()
        {
            // 首次进入打球弹一次操作引导（与练习同一入口，重进不再弹）。
            if (hud != null)
            {
                hud.ShowOnboardingIfFirstTime();
            }

            if (!MatchSeams.TryStartMatch(MatchSetup.Selected))
            {
                Debug.LogWarning("HomeView：`Start match` 被点击，但 `MatchSeams.Host` 尚未注册"
                                 + "（Runtime 侧开局入口未接线）⇒ **不进入对局**（不假装已开局）。", this);
                return;
            }

            IsAiMatchSession = true;    // **置位点 ②**（对战；只在真的开局成功之后置位）
            HideMatchIntro();
            Hide();
        }

        /// <summary>档位按钮（三格各自绑 `index`）：写进 `MatchSetup` 并刷新表现。</summary>
        public void OnSelectTier(int index)
        {
            PlayUiClick();   // 点击音放在方法内：**不改**上面那行接线文本（`T144④` 按字面守它）
            MatchSetup.Select(MatchSetup.TierAt(index));
            RefreshMatchIntro();
        }

        private void HideMatchIntro()
        {
            if (_introLayer != null)
            {
                _introLayer.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// **转场负载探针**（`-billiards-smoke-transition`）的触发口 —— 走**与"点 `Practice`"完全相同**的路径
        /// （就是上面那个 `OnStartPractice()`），因为探针要抓的正是**真实路径上的**一次性尖峰
        /// （HUD 懒构建 / 首页层 `SetActive(false)` / `SetChromeVisible(true)`）。
        /// ⚠ **不得**在这里另走捷径（例如只改状态）—— 那会让探针给出"转场很便宜"的假阴性。
        /// </summary>
        void Billiards.Core.ISmokeTransitionTrigger.TriggerSmokeTransition()
        {
            OnStartPractice();
        }

        /// <summary>VFX 开关：写宿主上的既有 bool，不新建偏好状态。</summary>
        public void OnToggleVfx()
        {
            if (vfxView != null)
            {
                vfxView.vfxEnabled = !vfxView.vfxEnabled;
            }

            RefreshToolButtons();
        }

        /// <summary>SFX 开关：同上。</summary>
        public void OnToggleSfx()
        {
            if (poolAudio != null)
            {
                poolAudio.audioEnabled = !poolAudio.audioEnabled;
            }

            RefreshToolButtons();
        }

        // ════════════════ 小工具 ═══════════════════════════════════════════════

        private static Vector2 Min(Rect rect)
        {
            return new Vector2(rect.xMin, rect.yMin);
        }

        private static Vector2 Max(Rect rect)
        {
            return new Vector2(rect.xMax, rect.yMax);
        }

        private static Vector2 Center(Rect rect)
        {
            return new Vector2(rect.center.x, rect.center.y);
        }

        private TextMeshProUGUI CreateHomeText(string name, TMP_FontAsset font, float size, Color color,
                                           TextAlignmentOptions alignment, Rect rect)
        {
            // 首页所有散落文本都走这里 ⇒ **统一不吃全局倍率**（首页保持原版式，用户明确要求）。
            return PracticeHudView.CreateText(_root, name, font, size, color, alignment, Min(rect), Max(rect),
                applyGlobalScale: false);
        }
    }
}
