using Billiards.Physics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **练习结算卡** —— 模态卡片 + 三列真数据 + 两个按钮，按美术包 `04_UI` 的
    /// `#result-dialog` 规则重建（圆形徽章 / 小标 chip / 大标题 / 副标题 / 三列统计 / 主次两键）。
    ///
    /// ── 与设计稿的映射（逐条）────────────────────────────────────────────────
    /// · 遮罩 `::backdrop` `#040c10b8` → 全画布一层 <see cref="ClubUiTokens.Backdrop"/>（不接收射线）。
    /// · `border-top: 3px solid` → 卡顶贴边色条；**胜局黄铜、负局酒红**（`data-outcome`）。
    /// · `.result-icon` 60 px 圆环 + 奖杯 → 环形位图 + `Trophy` 图标，圆环与图标同色（负局换色）。
    /// · `.result-badge` chip → 1 px 描边 + 透明底的小标（`PRACTICE COMPLETE`）。
    /// · `h2` 38 px Barlow / `p` 11 px 静音灰 → 标题与失败原因。
    /// · `.result-stats` 三列 → 上下细线 + （27 px 大数字 / 8 px 标签）。
    /// · `.primary-button` / `.text-button` → 黄铜实底主键 + 描边次键。
    /// · **全场结算（`data-stage: match`）的加重边框不实现** —— 本卡是**练习**卡，没有比赛比分；
    ///   伪造"整场胜负"正是设计说明 §二之15 明令禁止的事（"练习结算不显示虚构比赛比分"）。
    ///
    /// ── 三列数据的真源（全部既有只读出口，本类不新建统计状态）────────────────────
    /// 杆数 / 进球数 / 犯规（= scratch，白球落袋）都由 <see cref="PracticeSessionTracker"/> 提供，
    /// 而 tracker 的输入是 `PhysicsTestDrive.LaunchCount` / `PoolSimulator.IsBallInPlay` /
    /// `PhysicsTestDrive.LastLaunchRespawnedCueBall`。**没有任何一列是占位值。**
    ///
    /// ── 两个按钮（偏离参考稿，已登记）──────────────────────────────────────────
    /// 参考的 `Back to club` 在本工程**无处可去**（没有首页/俱乐部场景）⇒ 改为 **`Export data`**，
    /// 正好承接排期任务 3 的"一键导出"。`Play again` 保留。
    ///
    /// ── 输入让位（关键）─────────────────────────────────────────────────────
    /// 卡片打开期间 <see cref="IsOpen"/> 为真，`PointerAimController` / `PointerPowerController`
    /// 据此整体让位 —— 否则在卡片上右键拖拽会**蓄力并在松手时出杆**，把新一局打乱。
    /// 这是"模态"在本工程的**唯一判据点**。
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

        /// <summary>徽章标签（照参考稿逐字）。</summary>
        public const string BadgeLabel = "PRACTICE COMPLETE";

        /// <summary>第二个按钮的文案（替代参考稿的 `Back to club`，见类注释）。</summary>
        public const string ExportButtonLabel = "Export data";

        /// <summary>第一个按钮的文案（照参考稿逐字）。</summary>
        public const string PlayAgainButtonLabel = "Play again";

        [Tooltip("被驱动的驱动器（读数与重摆都经它）。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        [Tooltip("HUD 视图（借它的画布、字体与精灵槽位；为空 ⇒ 本组件不生效）。")]
        public PracticeHudView hud;

        [Tooltip("重摆键（卡片未显示时 = 主动结束本局；卡片显示时 = 再来一局）。")]
        public KeyCode rackKey = KeyCode.R;

        private readonly PracticeSessionTracker _tracker = new PracticeSessionTracker();
        private bool _built;
        private bool _started;
        private RectTransform _backdrop;
        private RectTransform _card;
        private Image _topEdge;
        private Image _cardInner;
        private Image _trophyRing;
        private Image _trophyGlyph;
        private RectTransform _badgeFrame;
        private TextMeshProUGUI _badgeText;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _reason;
        private TextMeshProUGUI[] _statValues;
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

            // 顶部 3 px 色条（胜 = 黄铜 / 负 = 酒红，由 `Show()` 按结局改写）
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

            _reason = PracticeHudView.CreateText(_card, "Reason", hud.bodyFont, ClubUiTokens.FontResultBody,
                ClubUiTokens.Body, TextAlignmentOptions.Center,
                new Vector2(PracticeResultLayout.ReasonLocal.xMin, PracticeResultLayout.ReasonLocal.yMin),
                new Vector2(PracticeResultLayout.ReasonLocal.xMax, PracticeResultLayout.ReasonLocal.yMax));

            BuildStatsRow();

            BuildButton(PracticeResultLayout.PlayAgainButtonLocal, PlayAgainButtonLabel, true,
                ClubUiSprites.UiSprite.ArrowRight, OnPlayAgain);
            BuildButton(PracticeResultLayout.ExportButtonLocal, ExportButtonLabel, false,
                ClubUiSprites.UiSprite.House, OnExport);

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

        private void BuildButton(Rect local, string label, bool filled, ClubUiSprites.UiSprite glyph,
                                 UnityEngine.Events.UnityAction action)
        {
            Color edge = filled ? ClubUiTokens.ButtonEdge : ClubUiTokens.LineButton;
            Color fill = filled ? ClubUiTokens.ButtonFill : ClubUiTokens.Transparent;
            Color textColor = filled ? ClubUiTokens.ButtonText : ClubUiTokens.Text;

            RectTransform rect = hud.AddFramed(_card, label, new Vector2(local.xMin, local.yMin),
                new Vector2(local.xMax, local.yMax), edge, fill, ClubUiTokens.RadiusButton);

            var hit = rect.GetComponent<Image>();
            hit.raycastTarget = true;   // 本画布唯一需要接收射线的控件族

            TextMeshProUGUI text = PracticeHudView.CreateText(rect, "Label", hud.bodyFont, ClubUiTokens.FontBody,
                textColor, TextAlignmentOptions.Center, Vector2.zero, new Vector2(0.56f, 1f));
            text.text = label;

            hud.AddFixed(rect, "Glyph", glyph, textColor, new Vector2(0.575f, 0.5f),
                new Vector2(ClubUiTokens.ButtonArrowSize, ClubUiTokens.ButtonArrowSize));

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(action);
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

            if (IsOpen)
            {
                if (Input.GetKeyDown(rackKey))
                {
                    OnPlayAgain();   // 卡片已显示 ⇒ `R` 等价于"再来一局"
                }

                return;
            }

            if (!_started)
            {
                // 首帧起局：以**当前**出杆数为基线（不把进场景前的杆数算进本局）。
                _tracker.BeginRack(drive.LaunchCount);
                _started = true;
            }

            if (Input.GetKeyDown(rackKey))
            {
                _tracker.RequestReRack();
            }

            PoolSimulator simulator = drive.Simulator;
            PracticeSession.CountBalls(simulator, out int pocketed, out int total);
            _tracker.Observe(drive.LaunchCount, pocketed, total, drive.LastLaunchRespawnedCueBall, Time.unscaledTime);

            if (_tracker.Outcome != RackOutcome.None)
            {
                Show();
            }
        }

        /// <summary>显示结算卡并把本局冻结（刷新文案 + 按结局换配色 + 置 <see cref="IsOpen"/>）。</summary>
        public void Show()
        {
            EnsureBuilt();
            if (_card == null)
            {
                return;
            }

            PracticeSessionSnapshot snapshot = _tracker.Snapshot();
            RackOutcome outcome = snapshot.outcome;
            bool win = outcome == RackOutcome.TableCleared;

            _title.text = PracticeSession.OutcomeTitle(outcome);
            _reason.text = PracticeSession.OutcomeReason(outcome);
            _statValues[0].text = snapshot.shots.ToString();
            _statValues[1].text = snapshot.pots.ToString();
            _statValues[2].text = snapshot.fouls.ToString();

            ApplyOutcomeStyle(win);

            IsOpen = true;
            _backdrop.gameObject.SetActive(true);
            _card.gameObject.SetActive(true);
            _card.SetAsLastSibling();
        }

        /// <summary>
        /// 按 `data-outcome` 换配色：**酒红只给负局**（设计说明 §一：酒红只用于犯规和失败）。
        /// 胜局则一律黄铜。
        /// </summary>
        private void ApplyOutcomeStyle(bool win)
        {
            Color32 accentEdge = win ? ClubUiTokens.ResultWinEdge : ClubUiTokens.ResultLoseEdge;
            Color32 iconColor = win ? ClubUiTokens.Accent : ClubUiTokens.ResultLoseIcon;
            Color32 iconEdge = win ? ClubUiTokens.ResultWinEdge : ClubUiTokens.ResultLoseIconEdge;
            Color32 badgeEdge = win ? ClubUiTokens.LineBadge : ClubUiTokens.ResultLoseBadgeEdge;

            if (_topEdge != null)
            {
                _topEdge.color = accentEdge;
            }

            if (_trophyRing != null)
            {
                _trophyRing.color = iconEdge;
            }

            if (_trophyGlyph != null)
            {
                _trophyGlyph.color = iconColor;
            }

            if (_badgeFrame != null && _badgeFrame.GetComponent<Image>() != null)
            {
                _badgeFrame.GetComponent<Image>().color = badgeEdge;
            }

            if (_badgeText != null)
            {
                _badgeText.color = iconColor;
            }
        }

        /// <summary>隐藏结算卡（不重置会话；由调用方决定是否 `BeginRack`）。</summary>
        public void Hide()
        {
            IsOpen = false;
            if (_card != null)
            {
                _card.gameObject.SetActive(false);
            }

            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(false);
            }
        }

        /// <summary>`Play again`：记一次"再来一局"并开新局。</summary>
        public void OnPlayAgain()
        {
            _tracker.NotePlayAgain();
            StartNewRack();
        }

        /// <summary>`Export data`：把当前会话并入历史并一键导出（失败不抛，只在卡上提示）。</summary>
        public void OnExport()
        {
            PracticeSessionRecord record = PracticeSessionStore.NewRecord(_tracker.Snapshot());
            string history = PracticeSessionStore.AppendToHistory(string.Empty, record);

            if (PracticeSessionStore.TryWriteExport(history, out string error))
            {
                ShowToast($"Exported to {PracticeSessionStore.ExportPath}");
            }
            else
            {
                ShowToast("Export failed: " + error);
                Debug.LogWarning($"PracticeResultView：导出失败 {error}", this);
            }
        }

        /// <summary>重摆并开新局（`Play again` 与"重摆键"共用）。</summary>
        public void StartNewRack()
        {
            drive?.ReloadRack();
            Hide();
            _tracker.BeginRack(drive != null ? drive.LaunchCount : 0);
            _started = true;
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
