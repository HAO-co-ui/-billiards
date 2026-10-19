using Billiards.Physics;
using Billiards.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **结算卡** —— 模态卡片 + 三列真数据 + 两个按钮，按美术包 `04_UI` 的
    /// `#result-dialog` 规则重建（圆形徽章 / 小标 chip / 大标题 / 副标题 / 三列统计 / 主次两键）。
    ///
    /// ── 一张卡、两个来源、两套形态（P6 的裁定）────────────────────────────────
    /// 本卡由**两个来源**驱动，**共用同一棵树**（"同一时刻至多一张结算卡"因此**结构上成立**）：
    /// · **练习局结局**（清台 / 玩家主动重摆）⇒ 形态 <see cref="MatchResultForm.None"/>，
    ///   标题与副标题取 `PracticeSession` 的既有映射；
    /// · **比赛结算**（一局定胜负，35-C D4）⇒ 形态 **`Win` / `Lose`**，
    ///   文案与配色走 <see cref="MatchResultCopy"/> 的单一实现点。
    /// 两者的按钮、几何、输入让位口径**完全相同**。
    ///
    /// ── 与设计稿的映射（逐条）────────────────────────────────────────────────
    /// · 遮罩 `::backdrop` `#040c10b8` → 全画布一层 <see cref="ClubUiTokens.Backdrop"/>（不接收射线）。
    /// · `border-top: 3px solid` → 卡顶贴边色条；**胜局黄铜、负局酒红**（`data-outcome`）。
    /// · `.result-icon` 60 px 圆环 + 奖杯 → 环形位图 + `Trophy` 图标，圆环与图标同色（负局换色）。
    /// · `.result-badge` chip → 1 px 描边 + 透明底的小标。
    /// · `h2` 38 px Barlow / `p` 11 px 静音灰 → 标题与结局原因。
    /// · `.result-stats` 三列 → 上下细线 + （27 px 大数字 / 8 px 标签）。
    /// · `.primary-button` / `.text-button` → 黄铜实底主键 + 描边次键。
    /// · **全场结算（`data-stage: match`）的加重边框不实现** —— 一局定胜负没有比分可显示；
    ///   伪造"整场比分"正是设计说明 §二之15 明令禁止的事（"不显示虚构比赛比分"）。
    ///
    /// ── 三列数据的真源（全部既有只读出口，本类不新建统计状态）────────────────────
    /// 杆数 / 进球数 / 犯规（= scratch，白球落袋）都由 <see cref="PracticeSessionTracker"/> 提供，
    /// 而 tracker 的输入是 `PhysicsTestDrive.LaunchCount` / `PoolSimulator.IsBallInPlay` /
    /// `PhysicsTestDrive.LastLaunchRespawnedCueBall`。**没有任何一列是占位值。**
    /// 会话基线在两种边界处重取（见 <see cref="RebaselineSession"/>）：**新一局开始**与**回到首页**。
    ///
    /// ── 两个按钮（P6 起**两个来源**的键位不同，**文案与行为始终一致**）────────────
    /// · 主键 `Play again`：**练习** ⇒ 重摆开新局；**比赛** ⇒ 走 `MatchSeams.TryStartMatch`
    ///   **真的再开一局**（未接线 ⇒ 保持卡片并提示，**不假装已开局**）。
    /// · 次键：**比赛形态** ⇒ `Back to club`（回首页，走 `HomeSeams.TryShow`）；
    ///   **练习形态** ⇒ `Export data`（把磁盘上已有的历史写出，**幂等、不新增记录**）。
    ///   ⚠ 当年把参考稿的 `Back to club` 换成 `Export data` 的**唯一理由**是"本工程没有首页"
    ///   —— **首页已落地 ⇒ 该理由已为假**；比赛形态因此按参考稿恢复 `Back to club`。
    ///   两种形态各自的标签都**如实描述自己的动作**（不存在"显示 A 实际做 B"）。
    ///
    /// ── "记一局"与"导出文件"是两件事（P6 起拆开）─────────────────────────────────
    /// **记录**（`PracticeSessionStore.TryAppendRecordToDisk`）挂在**结算边界**（`Present()` ⇒
    /// <see cref="RecordRackAtSettlement"/>）：**一次结算恰好一条**，两种来源都记 ⇒ 首页三项统计
    /// 记的是"你打了多少局"。**导出文件**只是把已有历史原样写出（`TryExportHistory`）。
    /// 旧口径把两者绑在 `Export data` 按钮上、且从空历史起算 ⇒ 既**覆盖**累积历史，又让
    /// **不点按钮的局**（含全部 AI 对战）**不被记录** —— 首页统计会静默做死。
    ///
    /// ── 输入让位（关键）─────────────────────────────────────────────────────
    /// 卡片打开期间 <see cref="IsOpen"/> 为真，`PointerAimController` / `PointerPowerController`
    /// 据此整体让位 —— 否则在卡片上右键拖拽会**蓄力并在松手时出杆**，把新一局打乱。
    /// 这是"模态"在本工程的**唯一判据点**（首页是第二个）。
    ///
    /// ── 盲带（如实）────────────────────────────────────────────────────────
    /// · 设计稿的 `backdrop-filter: blur(4px)` 与 `@keyframes` 淡入在 uGUI 无对应能力
    ///   ⇒ 遮罩是**纯色**、卡片**瞬时**出现（不伪造"轻微模糊/淡入"的说明）。
    /// · **真实鼠标点击**的端到端属手动验收；判据只锁 `EventSystem` / 射线器在场与几何矩形。
    /// </summary>
    [AddComponentMenu("Billiards/Practice Result View")]
    public sealed class PracticeResultView : MonoBehaviour
    {
        /// <summary>结算卡是否正显示 —— **交互层让位的唯一判据点**。</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// **E2**：结算卡**被打开过几次**（单调不减；`Present(...)` 每成功执行一次 `+1`；`Hide()` **不减**）。
        ///
        /// ⚠ **为什么不只看 `IsOpen`**：`IsOpen` 只断"**此刻**开着没"；缺陷③ 的现场是"**开过又被关掉**"
        /// （`OnPlayAgain()` / 再按 `R` 都会 `Hide()`）⇒ 现场自愈，只剩"球堆莫名回到开局"。
        /// **只有次数留得下足迹** ⇒ 判据靠"次数没涨"来证明"那张卡从来没弹过"。
        /// 生命周期与 <see cref="IsOpen"/> 一致（进程内静态）。
        /// </summary>
        public static int ShowCount { get; private set; }

        /// <summary>
        /// **卡片当前显示的形态**（`None` = 卡未显示，或显示的是练习局结局）。
        ///
        /// **可判定出口**（判据读它，**不读像素/不读按钮文字**）——与 <see cref="IsOpen"/> 同口径。
        /// 只由两条展示路径写入（<see cref="Show"/> / <see cref="ShowMatchResult"/>），
        /// 在 <see cref="Hide"/> / `OnDisable` 里清回 `None`（卡片没了就没有"当前形态"）。
        /// </summary>
        public static MatchResultForm CurrentForm { get; private set; }

        /// <summary>练习局卡片的徽章标签（与 <see cref="MatchResultCopy.BadgePractice"/> **同值同源**）。</summary>
        public const string BadgeLabel = MatchResultCopy.BadgePractice;

        /// <summary>次键在**练习形态**下的文案（真源在 <see cref="MatchResultCopy.ExportLabel"/>）。</summary>
        public const string ExportButtonLabel = MatchResultCopy.ExportLabel;

        /// <summary>主键文案（两形态同一句；真源在 <see cref="MatchResultCopy.PlayAgainLabel"/>）。</summary>
        public const string PlayAgainButtonLabel = MatchResultCopy.PlayAgainLabel;

        /// <summary>次键在**比赛形态**下的文案（真源在 <see cref="MatchResultCopy.BackToClubLabel"/>）。</summary>
        public const string BackToClubButtonLabel = MatchResultCopy.BackToClubLabel;

        /// <summary>首页未接线时给玩家的可见提示（**不假装已回到首页**）。</summary>
        public const string HomeUnavailableToast = "Home is not available";

        /// <summary>比赛宿主未接线时给玩家的可见提示（**不假装已开局**）。</summary>
        public const string MatchHostUnavailableToast = "Match host is not available";

        [Tooltip("被驱动的驱动器（读数与重摆都经它）。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        [Tooltip("HUD 视图（借它的画布、字体与精灵槽位；为空 ⇒ 本组件不生效）。")]
        public PracticeHudView hud;

        [Tooltip("重摆键（卡片未显示时 = 主动结束本局；卡片显示时 = 再来一局）。")]
        public KeyCode rackKey = KeyCode.R;

        private readonly PracticeSessionTracker _tracker = new PracticeSessionTracker();
        private bool _built;
        private bool _started;
        private bool _wasMatchOver;
        private RectTransform _backdrop;
        private RectTransform _card;
        private Image _topEdge;
        private Image _cardInner;
        private Image _trophyRing;
        private Image _trophyGlyph;
        private RectTransform _badgeFrame;
        private TextMeshProUGUI _badgeText;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI[] _statValues;
        private TextMeshProUGUI _secondaryLabel;
        private TextMeshProUGUI _toast;
        private float _toastUntil;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            EnsureBuilt();
        }

        private void OnDisable()
        {
            IsOpen = false;
            CurrentForm = MatchResultForm.None;
            if (_card != null)
            {
                _card.gameObject.SetActive(false);
            }

            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        /// <summary>**幂等建树**（`Awake` 会调；判据 `T102` 也可直接调 —— EditMode 下 `AddComponent` 不触发 `Awake`）。</summary>
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

            RectTransform root = hud.CanvasRoot;

            // ── 模态遮罩（设计稿 `::backdrop`；不接收射线，否则会挡住卡片上的按钮）──
            _backdrop = PracticeHudView.AddRect(root, "PracticeResultBackdrop", Vector2.zero, Vector2.one);
            hud.SetImage(_backdrop.gameObject.AddComponent<Image>(), null, ClubUiTokens.Backdrop);

            // ── 卡片：外框（细边线，圆角 3）+ 内层（炭黑实底）────────────────────
            _card = PracticeHudView.AddRect(root, "PracticeResultCard", Vector2.zero, Vector2.zero);
            ApplyCardRect(_card);
            hud.SetImage(_card.gameObject.AddComponent<Image>(), ClubUiSprites.PanelForRadius(ClubUiTokens.RadiusDialog),
                ClubUiTokens.LineCardEdge);

            _cardInner = PracticeHudView.AddRect(_card, "Fill", Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            hud.SetImage(_cardInner, ClubUiSprites.PanelForRadius(ClubUiTokens.RadiusDialog), ClubUiTokens.PanelCard);
            _cardInner.rectTransform.offsetMin = new Vector2(ClubUiTokens.Hairline, ClubUiTokens.Hairline);
            _cardInner.rectTransform.offsetMax = new Vector2(-ClubUiTokens.Hairline, -ClubUiTokens.Hairline);

            // 顶部 3 px 色条（胜 = 黄铜 / 负 = 酒红，由 `ApplyOutcomeStyle()` 按形态改写）
            _topEdge = PracticeHudView.AddRect(_card, "TopEdge", new Vector2(0f, 1f), new Vector2(1f, 1f))
                .gameObject.AddComponent<Image>();
            hud.SetImage(_topEdge, null, ClubUiTokens.ResultWinEdge);
            _topEdge.rectTransform.offsetMin = new Vector2(0f, -PracticeResultLayout.TopEdgeHeightPx);
            _topEdge.rectTransform.offsetMax = Vector2.zero;

            BuildIcon();
            BuildBadge();

            _title = PracticeHudView.CreateText(_card, "Title", hud.titleFont, ClubUiTokens.FontResultTitle,
                ClubUiTokens.Text, TextAlignmentOptions.Center,
                new Vector2(PracticeResultLayout.TitleLocal.xMin, PracticeResultLayout.TitleLocal.yMin),
                new Vector2(PracticeResultLayout.TitleLocal.xMax, PracticeResultLayout.TitleLocal.yMax));

            BuildStatsRow();

            BuildButton(PracticeResultLayout.PlayAgainButtonLocal, PlayAgainButtonLabel, true,
                ClubUiSprites.UiSprite.ArrowRight, OnPlayAgain);
            _secondaryLabel = BuildButton(PracticeResultLayout.ExportButtonLocal, ExportButtonLabel, false,
                ClubUiSprites.UiSprite.House, OnSecondaryAction);

            _toast = PracticeHudView.CreateText(_card, "Toast", hud.bodyFont, ClubUiTokens.FontCaption,
                ClubUiTokens.Muted, TextAlignmentOptions.Center,
                new Vector2(PracticeResultLayout.ToastLocal.xMin, PracticeResultLayout.ToastLocal.yMin),
                new Vector2(PracticeResultLayout.ToastLocal.xMax, PracticeResultLayout.ToastLocal.yMax));

            _backdrop.gameObject.SetActive(false);
            _card.gameObject.SetActive(false);
        }

        private void BuildIcon()
        {
            // 圆形徽章：外圈是**环形位图**（uGUI 画不出圆边框）、内层再放奖杯图标。
            var anchor = new Vector2(PracticeResultLayout.IconLocal.xMin + PracticeResultLayout.IconLocal.width * 0.5f,
                PracticeResultLayout.IconLocal.yMin + PracticeResultLayout.IconLocal.height * 0.5f);

            // `IconLocal.width` 是卡宽比例 ⇒ 换算成设计像素（卡宽 583）才是徽章的真实直径。
            float diameter = PracticeResultLayout.IconLocal.width * PracticeResultLayout.CardWidthPx;

            RectTransform ring = hud.AddFixed(_card, "TrophyRing", ClubUiSprites.UiSprite.Ring,
                ClubUiTokens.ResultWinEdge, anchor, new Vector2(diameter, diameter));
            _trophyRing = ring.GetComponent<Image>();

            RectTransform glyph = hud.AddFixed(_card, "TrophyGlyph", ClubUiSprites.UiSprite.Trophy,
                ClubUiTokens.Accent, anchor, new Vector2(ClubUiTokens.ResultIconGlyphSize, ClubUiTokens.ResultIconGlyphSize));
            _trophyGlyph = glyph.GetComponent<Image>();
        }

        private void BuildBadge()
        {
            _badgeFrame = hud.AddFramed(_card, "Badge",
                new Vector2(PracticeResultLayout.BadgeLocal.xMin, PracticeResultLayout.BadgeLocal.yMin),
                new Vector2(PracticeResultLayout.BadgeLocal.xMax, PracticeResultLayout.BadgeLocal.yMax),
                ClubUiTokens.LineBadge, ClubUiTokens.Transparent, ClubUiTokens.RadiusMin);

            _badgeText = PracticeHudView.CreateText(_badgeFrame, "BadgeLabel", hud.bodyFont,
                ClubUiTokens.FontCaption, ClubUiTokens.Accent, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            _badgeText.text = BadgeLabel;
            // 徽标是**短标签**，永远单行：换行会把两行字压到标题上（实机截图）。
            _badgeText.enableWordWrapping = false;
            _badgeText.overflowMode = TextOverflowModes.Overflow;
        }

        private void BuildStatsRow()
        {
            string[] labels = { "SHOTS", "POCKETED", "FOULS" };
            _statValues = new TextMeshProUGUI[labels.Length];

            // 统计区的上下细线（设计稿 `.result-stats { border-top / border-bottom }`）
            AddRule(PracticeResultLayout.StatsTopLineY);
            AddRule(PracticeResultLayout.StatsBottomLineY);

            for (int i = 0; i < labels.Length; i++)
            {
                float share = PracticeResultLayout.StatsLocal.width / labels.Length;
                var column = PracticeHudView.AddRect(_card, "Stat" + labels[i],
                    new Vector2(PracticeResultLayout.StatsLocal.xMin + i * share, PracticeResultLayout.StatsLocal.yMin),
                    new Vector2(PracticeResultLayout.StatsLocal.xMin + (i + 1) * share, PracticeResultLayout.StatsLocal.yMax));

                _statValues[i] = PracticeHudView.CreateText(column, "Value", hud.titleFont, ClubUiTokens.FontStatValue,
                    ClubUiTokens.Text, TextAlignmentOptions.Bottom, new Vector2(0f, 0.42f), new Vector2(1f, 1f));
                PracticeHudView.CreateText(column, "Label", hud.bodyFont, ClubUiTokens.FontMicro,
                    ClubUiTokens.Body, TextAlignmentOptions.Top, new Vector2(0f, 0.10f), new Vector2(1f, 0.42f))
                    .text = labels[i];
            }
        }

        private void AddRule(float y)
        {
            RectTransform rule = PracticeHudView.AddRect(_card, "Rule" + y.ToString("F3"),
                new Vector2(PracticeResultLayout.RuleInset, y), new Vector2(1f - PracticeResultLayout.RuleInset, y));
            rule.offsetMin = new Vector2(0f, -ClubUiTokens.Hairline);
            rule.offsetMax = Vector2.zero;
            hud.SetImage(rule.gameObject.AddComponent<Image>(), null, ClubUiTokens.LineDivider);
        }

        /// <summary>建一个键并返回它的文字控件（次键的文案按**形态**改写 ⇒ 必须留引用）。</summary>
        private TextMeshProUGUI BuildButton(Rect local, string label, bool filled, ClubUiSprites.UiSprite glyph,
                                           UnityEngine.Events.UnityAction action)
        {
            Color edge = filled ? ClubUiTokens.ButtonEdge : ClubUiTokens.LineButton;
            Color fill = filled ? ClubUiTokens.ButtonFill : ClubUiTokens.Transparent;
            Color textColor = filled ? ClubUiTokens.ButtonText : ClubUiTokens.Text;

            RectTransform rect = hud.AddFramed(_card, label, new Vector2(local.xMin, local.yMin),
                new Vector2(local.xMax, local.yMax), edge, fill, ClubUiTokens.RadiusButton);

            var hit = rect.GetComponent<Image>();
            hit.raycastTarget = true;   // 本画布唯一需要接收射线的控件族

            // **两个键同一视觉中轴**（用户反馈"文案偏左"）：标签占**整宽**居中，箭头移到右缘作指示符。
            // 旧版是「标签占左 0..0.56 居中 + 箭头 0.575」⇒ 内容整体压在左侧 2/3，看起来就是偏左。
            TextMeshProUGUI text = PracticeHudView.CreateText(rect, "Label", hud.bodyFont, ClubUiTokens.FontBody,
                textColor, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            text.text = label;

            hud.AddFixed(rect, "Glyph", glyph, textColor, new Vector2(0.92f, 0.5f),
                new Vector2(ClubUiTokens.ButtonArrowSize, ClubUiTokens.ButtonArrowSize));

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            // 结算卡的按钮也出声（与首页/设置同一入口，避免"有的按钮响、有的不响"）。
            button.onClick.AddListener(() =>
            {
                PoolAudio audio = poolAudio != null ? poolAudio : Object.FindObjectOfType<PoolAudio>();
                if (audio != null)
                {
                    audio.PlayUiClick();
                }

                action();
            });
            return text;
        }

        private void ApplyCardRect(RectTransform card)
        {
            // 卡尺寸来自 `PracticeResultLayout`（画布设计像素）；锚点固定居中 ⇒ 与分辨率无关。
            Rect viewport = PracticeResultLayout.CardRect(Screen.width, Screen.height);
            Vector2 canvas = PracticeResultLayout.CanvasSize(Screen.width, Screen.height);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(PracticeResultLayout.CardWidthPx, PracticeResultLayout.CardHeightPx);
            card.anchoredPosition = Vector2.zero;

            // 视口矩形只用于"是否越界"的自检（越界时给一次警告，不静默）。
            if (viewport.xMin < 0f || viewport.yMin < 0f || viewport.xMax > 1f || viewport.yMax > 1f)
            {
                Debug.LogWarning($"PracticeResultView：结算卡越出屏幕（画布 {canvas}）。", this);
            }
        }

        private void Update()
        {
            if (drive == null || hud == null)
            {
                return;
            }

            EnsureBuilt();

            if (Time.unscaledTime > _toastUntil && _toast != null && !string.IsNullOrEmpty(_toast.text))
            {
                _toast.text = string.Empty;
            }

            // **新一局开始**（`MatchOver` 由真变假 —— 唯一能开新局的口就是 `StartMatch`）：
            // 会话基线重取 + 把卡片收掉。少了这一步，上一局的结局会让 `Show()` **每帧重入**
            // （练习局的 `_tracker.Outcome` 一旦非 `None` 就一直是），表现为"卡片关不掉"。
            if (_wasMatchOver && !drive.MatchOver)
            {
                Hide();
                RebaselineSession();
            }

            _wasMatchOver = drive.MatchOver;

            // **比赛结算**：以 Runtime 的"待展示"闸门为准（`TryConsumeSettlement` **取走即清零**）
            // ⇒ "只弹一次"这条性质**不靠 UI 自己记状态**（UI 记的状态正是判据守不住的那类东西）。
            if (drive.TryConsumeSettlement())
            {
                // **练习模式不显示胜负面板**（用户要求）：`TryConsumeSettlement` 取走即清零，
                // 所以这里**照样消费**、只是不展示 —— 否则会一直挂"待展示"。
                if (drive.AiOpponentEnabled)
                {
                    ShowMatchResult(MatchResultCopy.FormFor(drive.Winner));
                    return;
                }
            }

            if (IsOpen)
            {
                if (Input.GetKeyDown(rackKey))
                {
                    OnPlayAgain();   // 卡片已显示 ⇒ `R` 等价于"再来一局"
                }

                return;
            }

            // **一局定胜负**：本局已结束（且结算卡已展示）⇒ 练习局的"清台 / 主动重摆"判定**整体让位**。
            // 这一条 + `StartMatch` 是唯一清 `MatchOver` 的口 ⇒ **关掉卡后不会自动进下一局**；
            // 要再来一局必须显式点 `Play again`（走 `MatchSeams`）。
            if (drive.MatchOver)
            {
                return;
            }

            // ⚠ 首帧起局（`BeginRack`）已随"会话 tick"搬进 `TickSessionForJudge()`（E4，P37）——
            //   这里**不许**再留一份：两份会让基线在**同一帧**被重取两次（顺序一乱就是静默 bug）。
            if (Input.GetKeyDown(rackKey))
            {
                TryRequestReRack();
            }

            TickSessionForJudge();
        }

        // ════════════════ P37：两个接缝出口（名字由验收标准冻结；只读/只驱动，不改产品语义）════════════════

        /// <summary>
        /// **S1**：**"玩家按了重摆键"的唯一入口**（`Update()` 里 `Input.GetKeyDown(rackKey)` 只允许调它）。
        /// 内部先过**模式门**：对战里（`drive.AiOpponentEnabled`）一律 **`false`**、什么也不做。
        ///
        /// ⚠ **为什么必须抽成方法**（P37 缺陷③ 的根因）：门写在 `Update` 里时判据**进不去**
        /// ⇒ 只能退化成"源码子串守卫"，而源码守卫守不住**跨文件后果**（`Show()` 走练习形态、
        /// `StartNewRack()` → `ReloadRack()` 把 `ruleState`/组别/回合清掉而 `MatchOver` 仍 false）。
        /// 抽出来后：对战里 `TryRequestReRack() == false` ⇒ 下一次 `TickSessionForJudge()` **什么都不该发生**
        /// （`ShowCount` 不变、`IsOpen` 仍 false、球心逐位不变）—— 这些都能在 EditMode 里驱动。
        ///
        /// 门用 `drive.AiOpponentEnabled`：与 `Update()` 里**结算分支**同一个判据（P12 定的"对手单一真源"）。
        /// </summary>
        public bool TryRequestReRack()
        {
            if (drive == null || drive.AiOpponentEnabled)
            {
                return false;
            }

            _tracker.RequestReRack();
            return true;
        }

        /// <summary>
        /// **E4**：跑一次 `Update()` 里"会话观察 → 结局判定 → 展示"那一段
        /// （`BeginRack` / `CountBalls` / `Observe` / `if (Outcome != None) Show()`）。
        ///
        /// ⚠ **为什么必须有这个出口**：`Show()` **只**在 `Update()` 里被调用，而 **EditMode 不跑 `Update`**
        /// ⇒ 没有它，`ShowCount` / `IsOpen` 在判据里**永远不变** ⇒ 断言会变成恒真（比不写更糟：
        /// 给人"已覆盖"的错觉）。
        /// ⚠ **抽取纪律**：只搬代码、不改顺序；`Update()` 里调用它的位置必须仍在那两个早退
        /// （`IsOpen` / `MatchOver`）**之后**。
        /// </summary>
        public void TickSessionForJudge()
        {
            if (drive == null)
            {
                return;
            }

            if (!_started)
            {
                // 首帧起局：以**当前**出杆数为基线（不把进场景前的杆数算进本局）。
                _tracker.BeginRack(drive.LaunchCount);
                _started = true;
            }

            PoolSimulator simulator = drive.Simulator;
            PracticeSession.CountBalls(simulator, out int pocketed, out int total);
            _tracker.Observe(drive.LaunchCount, pocketed, total, drive.LastLaunchRespawnedCueBall, Time.unscaledTime);

            if (_tracker.Outcome != RackOutcome.None)
            {
                Show();
            }
        }

        // ════════════════ 两条展示路径（共用同一棵树）════════════════════════════

        /// <summary>显示结算卡（**练习局**：清台 / 主动重摆）并冻结本局。</summary>
        public void Show()
        {
            EnsureBuilt();
            PracticeSessionSnapshot snapshot = _tracker.Snapshot();
            RackOutcome outcome = snapshot.outcome;

            // **用户要求：不管输赢都不显示"为什么"那一行** ⇒ 只剩 标题 / 徽章 / 次键标签。
            Present(MatchResultForm.None, outcome == RackOutcome.TableCleared,
                PracticeSession.OutcomeTitle(outcome), MatchResultCopy.BadgePractice);
            ApplyStats(snapshot);

            // **云服务上报**（仅 WebGL 生效）：一局练习结束时把成绩写进云排行榜。
            // 结算卡是"这局收口"的唯一落点 ⇒ 上报也只在这里发一次（不绑在按钮上，避免重复上报）。
            PoolCloudBridge.SubmitPracticeScore(snapshot.shots, snapshot.pots, snapshot.fouls,
                outcome == RackOutcome.TableCleared);
        }

        /// <summary>
        /// 显示结算卡（**比赛结算**：一局定胜负）并冻结本局。
        /// `form` 由 <see cref="MatchResultCopy.FormFor"/> 从 `PhysicsTestDrive.Winner` 解出；
        /// ⚠ 不再有 `reason` 参数（用户第四轮反馈：「那句解释不要」）——`EndReason` 仍在 Runtime，UI 不再展示它。
        /// </summary>
        public void ShowMatchResult(MatchResultForm form)
        {
            EnsureBuilt();
            Present(form, MatchResultCopy.IsWin(form),
                MatchResultCopy.Title(form), MatchResultCopy.Badge(form));
            ApplyStats(_tracker.Snapshot());
        }

        /// <summary>
        /// **唯一的落笔点**：四样东西（标题 / 徽章 / 说明 / 次键标签）与配色一起写 → 置 <see cref="IsOpen"/>。
        /// 两条展示路径**只能**经它写卡 —— 这样"两形态的差异项算全了吗"只有一个地方要审。
        /// </summary>
        /// <summary>结算音源（场景未接线时 `Present` 会现找一次）。</summary>
        public PoolAudio poolAudio;

        private void Present(MatchResultForm form, bool win, string title, string badge)
        {
            if (_card == null)
            {
                return;
            }

            CurrentForm = form;
            ShowCount++;   // E2：**一次打开记一次**（含练习 / 对战两种形态；`Hide()` 不减）

            // **胜/负结算音**：只在卡片真正弹出时响一次（`Present` 已被一次性闸门守着，不会重复）。
            // 场景未接线时现找一次 `PoolAudio`（仅此处发生，不在每帧路径上）。
            PoolAudio audio = poolAudio != null ? poolAudio : Object.FindObjectOfType<PoolAudio>();
            if (audio != null)
            {
                audio.PlayOutcome(win);
            }

            _title.text = title;
            _badgeText.text = badge;

            // 次键标签**如实描述它此刻的动作**（练习 ⇒ Export data；比赛 ⇒ Back to club）。
            if (_secondaryLabel != null)
            {
                _secondaryLabel.text = form == MatchResultForm.None ? ExportButtonLabel : BackToClubButtonLabel;
            }

            ApplyOutcomeStyle(win);

            // **结算边界**：本局在这里**恰好记一次**（两种来源都记）。
            // ⚠ 一次结算**只**记一条 —— `Present()` 是**唯一落笔点**，而它每次结算只被调一次
            //   （练习：`Show()` 由 `_tracker.Outcome` 触发且紧接着 `IsOpen` 挡住重入；
            //     比赛：`ShowMatchResult()` 由 Runtime 的 `TryConsumeSettlement()` 闸门保证只来一次）。
            RecordRackAtSettlement(form);

            IsOpen = true;
            _backdrop.gameObject.SetActive(true);
            _card.gameObject.SetActive(true);
            _card.SetAsLastSibling();
        }

        private void ApplyStats(PracticeSessionSnapshot snapshot)
        {
            if (_statValues == null)
            {
                return;
            }

            _statValues[0].text = snapshot.shots.ToString();
            _statValues[1].text = snapshot.pots.ToString();
            _statValues[2].text = snapshot.fouls.ToString();
        }

        /// <summary>
        /// 按结局换配色：**酒红只给负局**（设计说明 §一：酒红只用于犯规和失败）。
        /// 四个色值全部取自 <see cref="MatchResultCopy"/> 的单一实现点（本类**不写二选一**）。
        /// </summary>
        private void ApplyOutcomeStyle(bool win)
        {
            if (_topEdge != null)
            {
                _topEdge.color = MatchResultCopy.EdgeColor(win);
            }

            if (_trophyRing != null)
            {
                _trophyRing.color = MatchResultCopy.IconEdgeColor(win);
            }

            if (_trophyGlyph != null)
            {
                _trophyGlyph.color = MatchResultCopy.IconColor(win);
            }

            if (_badgeFrame != null && _badgeFrame.GetComponent<Image>() != null)
            {
                _badgeFrame.GetComponent<Image>().color = MatchResultCopy.BadgeEdgeColor(win);
            }

            if (_badgeText != null)
            {
                _badgeText.color = MatchResultCopy.IconColor(win);
            }
        }

        /// <summary>隐藏结算卡（不重置会话；由调用方决定是否 <see cref="RebaselineSession"/>）。</summary>
        public void Hide()
        {
            IsOpen = false;
            CurrentForm = MatchResultForm.None;
            if (_card != null)
            {
                _card.gameObject.SetActive(false);
            }

            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        /// <summary>会话基线重取（本局统计从"此刻的出杆数"重新起算）。</summary>
        private void RebaselineSession()
        {
            _tracker.BeginRack(drive != null ? drive.LaunchCount : 0);
            _started = true;
        }

        /// <summary>
        /// **结算边界（唯一）：把本局记进历史。**
        ///
        /// ── 为什么在这里、而不是在按钮上（P6 的裁定）────────────────────────────────
        /// 记录必须与"玩家点了哪个键"**无关**：`Play again` / `Back to club` / `Export data` 三个出口里
        /// 只有一个是导出，若把记录挂在它上面，**比赛形态（次键是 `Back to club`）就永远不会记**
        /// ⇒ 打完一局 AI 对战，首页三项统计**不涨**（与"统计恒 0"是同一个病）。
        /// ⇒ 记录挪到**结算边界**：**结算发生就记**，两种来源都记，一次结算**恰好一条**。
        ///
        /// ── `racks` 为什么用 `Max(1, …)` ──────────────────────────────────────────
        /// "一局打完"这个事实是**结算出口**给的，不是 tracker 给的：比赛形态下 `_tracker` **从不 resolve**
        /// （`MatchOver` 之后 `Update()` 提前返回、`Observe` 不再跑）⇒ 它的 `racks` 恒 0，而事实是**打完了 1 局**。
        /// `Max` 同时保住练习形态的既有值（那里的 `racks` 本来就是 1）。
        /// </summary>
        private void RecordRackAtSettlement(MatchResultForm form)
        {
            PracticeSessionSnapshot snapshot = _tracker.Snapshot();
            snapshot.racks = Mathf.Max(1, snapshot.racks);

            string mode = form == MatchResultForm.None
                ? PracticeSessionStore.ModePractice
                : PracticeSessionStore.ModeMatch;

            PracticeSessionRecord record = PracticeSessionStore.NewRecord(snapshot, mode);
            if (!PracticeSessionStore.TryAppendRecordToDisk(record, out string error))
            {
                Debug.LogWarning($"PracticeResultView：本局记录写盘失败 {error}", this);
            }
        }

        // ════════════════ 两个键的回调 ════════════════════════════════════════════

        /// <summary>
        /// 次键：**按形态分派**（练习 ⇒ 导出数据；比赛 ⇒ 回首页）。
        /// 两条分支各自都是**真动作**，且键上的标签与所做的事**始终一致**（`Present` 里改写）。
        /// </summary>
        public void OnSecondaryAction()
        {
            if (CurrentForm == MatchResultForm.None)
            {
                OnExport();
                return;
            }

            OnBackToClub();
        }

        /// <summary>
        /// `Play again`：**真的**再开一局。
        /// · 比赛形态 ⇒ 走 `MatchSeams.TryStartMatch`（未接线 ⇒ 保持卡片 + 可见提示，**不假装已开局**）；
        /// · 练习形态 ⇒ 既有行为（重摆 + 记一次"再来一局"）。
        /// </summary>
        public void OnPlayAgain()
        {
            if (CurrentForm != MatchResultForm.None)
            {
                if (!MatchSeams.TryStartMatch(MatchSetup.Selected))
                {
                    ShowToast(MatchHostUnavailableToast);
                    Debug.LogWarning("PracticeResultView：`Play again` 被点击，但 `MatchSeams.Host` 尚未注册"
                                     + "（Runtime 侧开局入口未接线）⇒ **不进入新一局**（不假装已开局）。", this);
                    return;
                }

                Hide();
                RebaselineSession();
                return;
            }

            _tracker.NotePlayAgain();
            StartNewRack();
        }

        /// <summary>
        /// `Back to club`：回到首页（走 <see cref="HomeSeams.TryShow"/>）。
        /// ⚠ **未接线 ⇒ 保持卡片并提示**（界面显示为可点就必须真的做得到，本工程铁律）。
        /// </summary>
        public void OnBackToClub()
        {
            if (!HomeSeams.TryShow())
            {
                ShowToast(HomeUnavailableToast);
                Debug.LogWarning("PracticeResultView：`Back to club` 被点击，但 `HomeSeams.Show` 尚未注册"
                                 + "（首页未接线）⇒ **保持结算卡**（不假装已回到首页）。", this);
                return;
            }

            Hide();
            RebaselineSession();   // 回首页 = 本局收口 ⇒ 下一局从此刻的出杆数起算
        }

        /// <summary>
        /// `Export data`：把**磁盘上已有的历史原样写出**（幂等）。
        /// ⚠ **不新增记录** —— 记录由 <see cref="RecordRackAtSettlement"/> 在**结算边界**做
        /// ⇒ "记一局"与"导出文件"解耦，不存在"结算记一次 + 导出又记一次"的**双记**
        /// （旧口径从空历史起算，会把累积的历史**覆盖成单条**，那正是首页统计静默做死的根因）。
        /// </summary>
        public void OnExport()
        {
            if (PracticeSessionStore.TryExportHistory(out string error))
            {
                // **WebGL 的 `persistentDataPath` 是 `/idbfs/<hash>`**（虚拟文件系统，用户根本访问不到）
                // ⇒ 网页版只告诉他"存在浏览器里"，不显示无意义路径；桌面版仍显示真实路径（可去文件夹找）。
#if UNITY_WEBGL && !UNITY_EDITOR
                ShowToast("Saved in this browser's site data");
#else
                ShowToast($"Exported to {PracticeSessionStore.ExportPath}");
#endif
            }
            else
            {
                ShowToast("Export failed: " + error);
                Debug.LogWarning($"PracticeResultView：导出失败 {error}", this);
            }
        }

        /// <summary>重摆并开新局（练习形态的 `Play again` 与"重摆键"共用）。</summary>
        public void StartNewRack()
        {
            drive?.ReloadRack();
            Hide();
            RebaselineSession();
        }

        private void ShowToast(string message)
        {
            if (_toast == null)
            {
                return;
            }

            _toast.text = message;
            _toastUntil = Time.unscaledTime + 6f;
        }
    }
}
