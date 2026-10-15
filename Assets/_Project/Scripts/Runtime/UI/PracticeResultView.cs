using Billiards.Physics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **练习结算卡**（排期 Day 14 任务 1）：模态卡片 + 三列真数据 + 两个按钮。
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
    /// · 徽章**用菱形色点**代替参考稿的奖杯图标（本工程无矢量图标资源；与 HUD 状态点同一手法）。
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

        [Tooltip("HUD 视图（借它的画布与字体；为空 ⇒ 本组件不生效）。")]
        public PracticeHudView hud;

        [Tooltip("重摆键（卡片未显示时 = 主动结束本局；卡片显示时 = 再来一局）。")]
        public KeyCode rackKey = KeyCode.R;

        private readonly PracticeSessionTracker _tracker = new PracticeSessionTracker();
        private bool _built;
        private bool _started;
        private RectTransform _card;
        private Image _badge;
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

            _card = PracticeHudView.CreateZone(hud.CanvasRoot, new Rect(0f, 0f, 0f, 0f), "PracticeResultCard");
            ApplyCardRect(_card);
            PracticeHudView.AddPanel(_card, PracticeHudView.PanelColor, PracticeHudView.AccentColor);

            // 徽章：45° 菱形色点（代替参考稿的奖杯图标，见类注释的盲带）
            RectTransform badge = PracticeHudView.AddPanel(
                _card, PracticeHudView.AccentColor, PracticeHudView.AccentColor,
                new Vector2(0.485f, 0.875f), new Vector2(0.515f, 0.925f));
            badge.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _badge = badge.GetComponent<Image>();

            RectTransform badgePill = PracticeHudView.AddPanel(
                _card, new Color(0f, 0f, 0f, 0f), PracticeHudView.AccentColor,
                new Vector2(0.300f, 0.800f), new Vector2(0.700f, 0.845f));
            PracticeHudView.CreateText(badgePill, "BadgeLabel", hud.bodyFont, 12f, PracticeHudView.AccentColor,
                TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 1f)).text = BadgeLabel;

            _title = PracticeHudView.CreateText(_card, "Title", hud.titleFont, 26f, PracticeHudView.TextColor,
                TextAlignmentOptions.Center, new Vector2(0.04f, 0.700f), new Vector2(0.96f, 0.785f));

            _reason = PracticeHudView.CreateText(_card, "Reason", hud.bodyFont, 13f, PracticeHudView.MutedColor,
                TextAlignmentOptions.Center, new Vector2(0.04f, 0.640f), new Vector2(0.96f, 0.695f));

            BuildStatsRow();
            BuildButton(_card, PracticeResultLayout.PlayAgainButtonLocal, PlayAgainButtonLabel, true, OnPlayAgain);
            BuildButton(_card, PracticeResultLayout.ExportButtonLocal, ExportButtonLabel, false, OnExport);

            _toast = PracticeHudView.CreateText(_card, "Toast", hud.bodyFont, 12f, PracticeHudView.MutedColor,
                TextAlignmentOptions.Center, new Vector2(0.04f, 0.250f), new Vector2(0.96f, 0.300f));

            _card.gameObject.SetActive(false);
        }

        private void BuildStatsRow()
        {
            string[] labels = { "SHOTS", "POCKETED", "FOULS" };
            _statValues = new TextMeshProUGUI[labels.Length];

            for (int i = 0; i < labels.Length; i++)
            {
                float share = 1f / labels.Length;
                RectTransform column = PracticeHudView.CreateZone(
                    _card, new Rect(0.05f + i * share * 0.9f, 0.430f, share * 0.9f, 0.170f), "Stat" + labels[i]);

                _statValues[i] = PracticeHudView.CreateText(column, "Value", hud.titleFont, 26f, PracticeHudView.TextColor,
                    TextAlignmentOptions.Top, new Vector2(0f, 0.35f), new Vector2(1f, 1f));
                PracticeHudView.CreateText(column, "Label", hud.bodyFont, 11f, PracticeHudView.MutedColor,
                    TextAlignmentOptions.Bottom, new Vector2(0f, 0f), new Vector2(1f, 0.35f)).text = labels[i];
            }
        }

        private void BuildButton(RectTransform parent, Rect local, string label, bool filled, UnityEngine.Events.UnityAction action)
        {
            Color fill = filled ? PracticeHudView.ShootFill : new Color(0f, 0f, 0f, 0f);
            RectTransform rect = PracticeHudView.AddPanel(parent, fill, PracticeHudView.AccentColor);
            rect.anchorMin = new Vector2(local.xMin, local.yMin);
            rect.anchorMax = new Vector2(local.xMax, local.yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = rect.GetComponent<Image>();
            image.raycastTarget = true;   // 本画布唯一需要接收射线的控件

            PracticeHudView.CreateText(rect, "Label", hud.titleFont, 14f,
                filled ? PracticeHudView.ShootText : PracticeHudView.TextColor,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = label;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
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
            card.sizeDelta = new Vector2(
                PracticeResultLayout.CardWidthPx,
                PracticeResultLayout.CardHeightPx);
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

        /// <summary>显示结算卡并把本局冻结（刷新文案 + 置 <see cref="IsOpen"/>）。</summary>
        public void Show()
        {
            EnsureBuilt();
            if (_card == null)
            {
                return;
            }

            PracticeSessionSnapshot snapshot = _tracker.Snapshot();
            RackOutcome outcome = snapshot.outcome;

            _title.text = PracticeSession.OutcomeTitle(outcome);
            _reason.text = PracticeSession.OutcomeReason(outcome);
            _badge.color = outcome == RackOutcome.TableCleared ? PracticeHudView.AccentColor : PracticeHudView.MutedColor;
            _statValues[0].text = snapshot.shots.ToString();
            _statValues[1].text = snapshot.pots.ToString();
            _statValues[2].text = snapshot.fouls.ToString();

            IsOpen = true;
            _card.gameObject.SetActive(true);
            _card.SetAsLastSibling();
        }

        /// <summary>隐藏结算卡（不重置会话；由调用方决定是否 `BeginRack`）。</summary>
        public void Hide()
        {
            IsOpen = false;
            if (_card != null)
            {
                _card.gameObject.SetActive(false);
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
