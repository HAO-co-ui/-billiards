using Billiards.Physics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **最小练习 HUD 视图**：程序化建出三块（计数组 / 状态条 / 力度块），每帧从既有只读出口刷值。
    ///
    /// ── 几何唯一来源 ────────────────────────────────────────────────────────
    /// 三块的矩形一律取自 <see cref="PracticeHudLayout.Resolve"/>，并以 **uGUI 锚点比例**落地
    /// （`anchorMin/anchorMax` = 视口分区）⇒ 分区一变，视图自动跟着变，**不存在第二份数字**。
    /// 分区本身由判据 `T95` 在六档宽高比下守住（不越界 / 不压球桌主区 / 互不重叠）。
    ///
    /// ── 数据来源（全部既有只读出口，本类不新建任何状态）────────────────────────
    /// · 状态与 `Shoot` 可用性 ← `CueStickController.Flow.Phase`（`ShotPhase`）
    /// · 力度 ← `PhysicsTestDrive.power`
    /// · 杆数 ← `PhysicsTestDrive.LaunchCount`；球数 ← `PoolSimulator.IsBallInPlay`
    ///
    /// ── 刻意不做（边界，如实登记）────────────────────────────────────────────
    /// · **不接点击**：`Shoot` 只做显示态（可用/禁用随 `ShotPhase`），故**不挂 `GraphicRaycaster`**、
    ///   不依赖 `EventSystem`。出杆通路仍是既有的右键拉杆 + `Space`（`PhysicsTestDrive.launchKey`）。
    /// · **不绘制状态图标**：参考实现的准星 / 虚线圆需要矢量图标资源；本单用**状态色菱形点 + 文案**
    ///   承担同一语义（设计说明 §4/§5 的"绿色指示自方回合 / 球运动"可用颜色区分）。
    /// · 文字在块内的**实际像素溢出**不判（`-nographics` 无渲染）⇒ 由用户实机截图兜。
    /// </summary>
    [AddComponentMenu("Billiards/Practice Hud View")]
    public sealed class PracticeHudView : MonoBehaviour
    {
        // ── 设计 token（逐字抄参考实现 `style.css` 的 `:root`）──────────────────
        public static readonly Color PanelColor = new Color32(0x10, 0x20, 0x25, 0xED);
        public static readonly Color PanelBorder = new Color32(0xD4, 0xB4, 0x6C, 0x45);
        public static readonly Color AccentColor = new Color32(0xD4, 0xB4, 0x6C, 0xFF);
        public static readonly Color TextColor = new Color32(0xF2, 0xEF, 0xE4, 0xFF);
        public static readonly Color MutedColor = new Color32(0xA9, 0xB9, 0xB4, 0xFF);
        public static readonly Color StateGreen = new Color32(0x6D, 0xBD, 0xA9, 0xFF);
        public static readonly Color PowerTrack = new Color32(0x3F, 0x61, 0x5A, 0xFF);
        public static readonly Color ShootFill = new Color32(0xD4, 0xB4, 0x6C, 0xFF);
        public static readonly Color ShootFillDisabled = new Color32(0xD4, 0xB4, 0x6C, 0x61);
        public static readonly Color ShootText = new Color32(0x10, 0x20, 0x25, 0xFF);

        /// <summary>控件圆角（设计说明 §一：1–3 px，不做现代 SaaS 式圆角）。</summary>
        public const float CornerRadius = 2f;

        /// <summary>禁用态不透明度（参考实现 `button:disabled { opacity: .38 }`）。</summary>
        public const float DisabledOpacity = 0.38f;

        [Tooltip("被驱动的驱动器（读计数与力度）。为空 ⇒ 本组件只建结构、不刷值。")]
        public PhysicsTestDrive drive;

        [Tooltip("球杆控制器（读击球流程相位）。为空 ⇒ 状态条按 Aim 显示。")]
        public CueStickController cueStick;

        [Tooltip("标题族字体（数值 / POWER / Shoot）。")]
        public TMP_FontAsset titleFont;

        [Tooltip("正文族字体（小标签 / 状态文案）。")]
        public TMP_FontAsset bodyFont;

        /// <summary>环境光/背景色随场景设置；本组件只画 UI。</summary>
        private Canvas _canvas;

        /// <summary>
        /// HUD 画布的根 `RectTransform`。**结算卡挂这里而不是自建第二个画布** ——
        /// 同一画布 = 同一个 `CanvasScaler` 口径（不存在两份画布缩放），且只有一个 `sortingOrder` 需要维护。
        /// </summary>
        public RectTransform CanvasRoot
        {
            get
            {
                EnsureBuilt();
                return (RectTransform)_canvas.transform;
            }
        }

        private RectTransform _counters;
        private RectTransform _status;
        private RectTransform _power;
        private TextMeshProUGUI[] _counterValues;
        private TextMeshProUGUI _stateText;
        private Image _stateDot;
        private TextMeshProUGUI _powerPercent;
        private Image _powerFill;
        private Image _shootFill;
        private TextMeshProUGUI _shootText;
        private bool _built;

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            EnsureBuilt();
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        /// <summary>
        /// **幂等建树**。`Awake`/`OnEnable` 会调它；判据 `T98` 也直接调它
        /// （EditMode 下 `AddComponent` **不会**触发 `Awake` ⇒ 不给测试留入口就等于结构无判据）。
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            PracticeHudRects zones = PracticeHudLayout.Resolve();

            var canvasGo = new GameObject("PracticeHudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;   // 覆盖 3D 场景

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(PracticeHudLayout.ReferenceWidth, PracticeHudLayout.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = PracticeHudLayout.MatchWidthOrHeight;

            // 射线器：**结算卡的按钮**需要它（本画布是全场景唯一带射线器的画布，`T102 (c)` 守）。
            // HUD 自身的三块仍只显示、不接收射线（各控件的 `raycastTarget` 保持 false）。
            canvasGo.AddComponent<GraphicRaycaster>();

            _counters = CreateZone(canvasGo.transform, zones.counters, "Counters");
            _status = CreateZone(canvasGo.transform, zones.status, "Status");
            _power = CreateZone(canvasGo.transform, zones.power, "Power");

            BuildCounters(_counters);
            BuildStatus(_status);
            BuildPower(_power);
        }

        // ── 建树 ─────────────────────────────────────────────────────────────

        /// <summary>分区落地：**锚点 = 视口分区**（`offsetMin/Max = 0`）⇒ 与分辨率无关，且与判据同源。</summary>
        public static RectTransform CreateZone(Transform parent, Rect zone, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(zone.xMin, zone.yMin);
            rect.anchorMax = new Vector2(zone.xMax, zone.yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void BuildCounters(RectTransform zone)
        {
            AddPanel(zone, PanelColor, PanelBorder);

            _counterValues = new TextMeshProUGUI[PracticeHudState.CounterLabels.Length];
            for (int i = 0; i < PracticeHudState.CounterLabels.Length; i++)
            {
                float share = 1f / PracticeHudState.CounterLabels.Length;
                var column = CreateZone(zone, new Rect(i * share, 0f, share, 1f), "Column" + i);

                CreateText(column, "Label", bodyFont, PracticeHudLayout.LabelFontSize * 0.72f, AccentColor,
                    TextAlignmentOptions.Bottom, new Vector2(0.02f, 0.52f), new Vector2(0.98f, 0.92f))
                    .text = PracticeHudState.CounterLabels[i];

                _counterValues[i] = CreateText(column, "Value", titleFont, PracticeHudLayout.ValueFontSize, TextColor,
                    TextAlignmentOptions.Top, new Vector2(0.02f, 0.10f), new Vector2(0.98f, 0.56f));
            }
        }

        private void BuildStatus(RectTransform zone)
        {
            AddPanel(zone, PanelColor, PanelBorder);

            var dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            var dotRect = (RectTransform)dotGo.transform;
            dotRect.SetParent(zone, false);
            dotRect.anchorMin = new Vector2(0.06f, 0.5f);
            dotRect.anchorMax = new Vector2(0.06f, 0.5f);
            dotRect.sizeDelta = new Vector2(10f, 10f);
            dotRect.localRotation = Quaternion.Euler(0f, 0f, 45f);   // 参考实现的 `#turn-dot` 是 5×5 菱形
            _stateDot = dotGo.GetComponent<Image>();
            _stateDot.color = StateGreen;

            _stateText = CreateText(zone, "State", bodyFont, PracticeHudLayout.LabelFontSize, TextColor,
                TextAlignmentOptions.Left, new Vector2(0.12f, 0.10f), new Vector2(0.98f, 0.90f));

            var hint = CreateText(zone, "ZoneCaption", bodyFont, PracticeHudLayout.LabelFontSize * 0.72f, MutedColor,
                TextAlignmentOptions.Right, new Vector2(0.60f, 0.10f), new Vector2(0.98f, 0.34f));
            hint.text = "PRACTICE";
        }

        private void BuildPower(RectTransform zone)
        {
            AddPanel(zone, PanelColor, PanelBorder);

            CreateText(zone, "PowerLabel", bodyFont, PracticeHudLayout.LabelFontSize * 0.72f, MutedColor,
                TextAlignmentOptions.Left, new Vector2(0.04f, 0.70f), new Vector2(0.55f, 0.96f))
                .text = PracticeHudState.LabelPower;

            _powerPercent = CreateText(zone, "PowerValue", titleFont, PracticeHudLayout.ValueFontSize * 0.85f, AccentColor,
                TextAlignmentOptions.Right, new Vector2(0.50f, 0.70f), new Vector2(0.96f, 0.96f));

            // 力条：轨道 + 填充（填充宽 = 力度比例）
            var track = AddPanel(zone, PowerTrack, PowerTrack, new Vector2(0.04f, 0.56f), new Vector2(0.96f, 0.64f));
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRect = (RectTransform)fillGo.transform;
            fillRect.SetParent(track, false);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _powerFill = fillGo.GetComponent<Image>();
            _powerFill.color = AccentColor;
            _powerFill.type = Image.Type.Filled;
            _powerFill.fillMethod = Image.FillMethod.Horizontal;
            _powerFill.fillOrigin = 0;
            _powerFill.fillAmount = 0f;

            // Shoot（显示态；不接点击）
            var button = AddPanel(zone, ShootFill, ShootFill, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.46f));
            _shootFill = button.GetComponent<Image>();
            _shootText = CreateText(button, "ShootText", titleFont, PracticeHudLayout.LabelFontSize, ShootText,
                TextAlignmentOptions.Center, new Vector2(0f, 0f), new Vector2(1f, 1f));
            _shootText.text = PracticeHudState.ButtonShoot;
        }

        // ── 刷新 ─────────────────────────────────────────────────────────────

        /// <summary>按当前状态刷值。**只写显示层**，不碰任何物理量。</summary>
        public void Refresh()
        {
            if (!_built)
            {
                return;
            }

            ShotPhase phase = cueStick != null && cueStick.Flow != null ? cueStick.Flow.Phase : ShotPhase.Aim;
            _stateText.text = PracticeHudState.StateLabel(phase);
            _stateDot.color = phase == ShotPhase.Aim || phase == ShotPhase.Charging ? StateGreen : MutedColor;

            bool canShoot = PracticeHudState.ShootEnabled(phase);
            _shootFill.color = canShoot ? ShootFill : ShootFillDisabled;
            _shootText.alpha = canShoot ? 1f : DisabledOpacity;

            float power = drive != null ? Mathf.Clamp01(drive.power) : 0f;
            _powerPercent.text = PracticeHudState.PowerLabel(power);
            _powerFill.fillAmount = power;

            if (drive != null && drive.Simulator != null)
            {
                // 逐球清点走 `PracticeSession.CountBalls`（**唯一实现点**，铁律 16）——
                // 结算卡的三列与 HUD 的计数组共用同一份口径，不存在两处各数一遍。
                PracticeSession.CountBalls(drive.Simulator, out int pocketed, out int totalObjects);

                _counterValues[0].text = (totalObjects - pocketed).ToString();
                _counterValues[1].text = drive.LaunchCount.ToString();
                _counterValues[2].text = PracticeHudState.PocketedLabel(pocketed, totalObjects);
            }
            else
            {
                _counterValues[0].text = "0";
                _counterValues[1].text = "0";
                _counterValues[2].text = PracticeHudState.PocketedLabel(0, 0);
            }
        }

        // ── 小工具 ───────────────────────────────────────────────────────────

        public static RectTransform AddPanel(RectTransform parent, Color fill, Color border)
        {
            return AddPanel(parent, fill, border, Vector2.zero, Vector2.one);
        }

        public static RectTransform AddPanel(RectTransform parent, Color fill, Color border,
                                             Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.color = fill;
            return rect;
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset font, float size,
                                                 Color color, TextAlignmentOptions alignment,
                                                 Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = 0f;   // 设计说明 §一：字距 0
            text.raycastTarget = false;
            return text;
        }
    }
}
