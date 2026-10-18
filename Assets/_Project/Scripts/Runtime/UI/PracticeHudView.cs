using System.Collections.Generic;
using Billiards.Physics;
using Billiards.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Billiards.UI
{
    /// <summary>
    /// **对局 HUD 视图** —— 按美术包 `04_UI` 的设计稿重建（记分牌 / 状态条 / 力度块）。
    ///
    /// ── 几何唯一来源 ────────────────────────────────────────────────────────
    /// 三块的矩形一律取自 <see cref="PracticeHudLayout.Resolve"/>，并以 **uGUI 锚点比例**落地
    /// （`anchorMin/anchorMax` = 视口分区）⇒ 分区一变，视图自动跟着变，**不存在第二份数字**。
    /// 分区本身由判据 `T95` 在六档宽高比下守住（不越界 / 不压球桌主区 / 互不重叠）。
    ///
    /// ── 视觉唯一来源 ────────────────────────────────────────────────────────
    /// 颜色 / 圆角 / 字号 / 尺寸**全部**取自 <see cref="ClubUiTokens"/>，
    /// 图标与形状取自 <see cref="ClubUiSprites"/>，球号贴片取自 <see cref="ClubBallChips"/>。
    /// 本类里**不出现任何颜色字面量或字号字面量**（判据 `T124` 守这条）——
    /// 这正是重做前"两处视图各写一套色值 ⇒ 界面半新半旧"的机制性修法。
    ///
    /// ── 精灵为什么必须从场景数组来（实现约束，不是风格问题）──────────────────
    /// 运行期**不能**用 `Resources`/路径加载资产（`Assets/` 目录在出包后不存在）。
    /// 故所有精灵都挂在 <see cref="uiSprites"/> / <see cref="ballChips"/> 上、由场景序列化；
    /// 本类只按 <see cref="ClubUiSprites"/> 的**清单序号**取，并做"取不到就不挂"的判空
    /// （挂一个 `null` 会让 uGUI 画出**白色方块**，比不画更糟）。
    ///
    /// ── 数据来源（全部既有只读出口，本类不新建任何状态）────────────────────────
    /// · 状态 ← `CueStickController.Flow.Phase`（`ShotPhase`）+ `ScratchCount` 上升沿（犯规）
    ///   + `RefusedLaunchCount` 上升沿（出杆被拒）+ **`RefusedPlacementCount` 上升沿（落点被拒）**
    ///   + `LastLaunchRespawnedCueBall`（白球归位）
    ///   + **`PendingCueBallPlacement`（"待放球"＝白球离台且玩家尚未放置，全场自由球新规则）**
    ///   + `PracticeResultView.IsOpen`（结算）
    /// · 力度 ← `PhysicsTestDrive.power`；杆数/犯规 ← `LaunchCount` / `ScratchCount`；
    ///   进球 / 总数 ← `PoolSimulator.IsBallInPlay`（经 `PracticeSession.CountBalls` 单点口径）
    ///
    /// ── 刻意不做（边界，如实登记）────────────────────────────────────────────
    /// · **HUD 三块不接收射线**（各控件 `raycastTarget = false`）；只有右上两个 **VFX / SFX 开关**
    ///   是真按钮（改的是 `PoolVfxView.vfxEnabled` / `PoolAudio.audioEnabled`）。
    ///   出杆通路仍是既有的右键拉杆 + `Space`（`PhysicsTestDrive.launchKey`）。
    /// · **模式切换里的 `AI Match` 是禁用态**（`opacity .38`，不挂按钮）：35-A 没有 AI 对手，
    ///   做一个点得动的假入口等于欺骗 —— 设计稿里它就是"另一个模式"，这里如实呈现为"尚未开放"。
    /// · 原型 CSS 的 `inset` 内阴影 / `backdrop-filter: blur` / `text-shadow` 在 uGUI 无对应能力
    ///   ⇒ 不伪造，已登记为表现差异。
    /// · 文字在块内的**实际像素溢出**不判（`-nographics` 无渲染）⇒ 由用户实机截图兜。
    /// · **首页（`HomeView`）打开期间，本 HUD 的三块分区被临时隐藏**
    ///   （参考稿 `body.home-active` 的 `display:none`，见 <see cref="SetChromeVisible"/>）
    ///   ⇒ 这段时间本类的状态翻译仍在跑、但**看不见**（首页挡住一切交互，状态条的口径不适用）。
    /// </summary>
    [AddComponentMenu("Billiards/Practice Hud View")]
    public sealed class PracticeHudView : MonoBehaviour
    {
        /// <summary>
        /// 犯规提示的停留时长（秒）。设计说明给的边线脉冲是 **0.8 s**（那是"看一眼就够"的动效）；
        /// 本处要让人**读完文字**再判断"刚才那杆犯没犯错"，故取 **1.6 s**（两倍）。
        /// </summary>
        public const float FoulBannerSeconds = 1.6f;

        /// <summary>记分牌球行的列数（第一行 8 个，第二行放剩下的 7 个）。</summary>
        public const int BallRowColumns = 8;

        /// <summary>滑块在轨道两端的**锚点内缩**（半个滑块 ≈ 7 px ÷ 轨道宽 ≈ 0.014）。</summary>
        private const float ThumbInsetFraction = 0.014f;

        [Tooltip("被驱动的驱动器（读计数、力度、犯规与出杆数）。为空 ⇒ 本组件只建结构、不刷值。")]
        public PhysicsTestDrive drive;

        [Tooltip("球杆控制器（读击球流程相位）。为空 ⇒ 状态条按 Aim 显示。")]
        public CueStickController cueStick;

        [Tooltip("标题族字体（品牌字 / 大数字 / POWER / Shoot）。")]
        public TMP_FontAsset titleFont;

        [Tooltip("正文族字体（小标签 / 状态文案）。")]
        public TMP_FontAsset bodyFont;

        [Tooltip("界面精灵槽位（长度与顺序必须等于 ClubUiSprites 的清单）。")]
        public Sprite[] uiSprites;

        [Tooltip("球号贴片槽位（16 个，索引 = 球号）。")]
        public Sprite[] ballChips;

        [Tooltip("特效宿主（右上开关写它的 vfxEnabled）。为空 ⇒ 开关只显示、不接线。")]
        public PoolVfxView vfxView;

        [Tooltip("音频宿主（右上开关写它的 audioEnabled）。为空 ⇒ 开关只显示、不接线。")]
        public PoolAudio poolAudio;

        private Canvas _canvas;
        private RectTransform _counters;
        private RectTransform _status;
        private RectTransform _power;

        // ── 记分牌 ───────────────────────────────────────────────────────────
        private TextMeshProUGUI _scoreValue;
        private TextMeshProUGUI _playerStats;
        private Image[] _ballChips;
        // 对战 HUD：左右两侧的剩余球号 chip（`-1` = 隐藏；映射走 `PracticeHudState.GroupChipNumbers`）。
        private Image[] _yourGroupChips;
        private Image[] _oppGroupChips;
        private int[] _yourShownNumbers;
        private int[] _oppShownNumbers;
        private TextMeshProUGUI _ballsLabel;
        private TextMeshProUGUI _scoreLabel;
        private TextMeshProUGUI _goalText;
        private TextMeshProUGUI _yourGroupLabel;
        private TextMeshProUGUI _oppGroupLabel;
        private TextMeshProUGUI _oppName;
        private RectTransform _oppAvatar;
        private RectTransform _scoreboard;
        private RectTransform _settingsToggle;
        private RectTransform _settingsPanel;
        private TextMeshProUGUI _volumeLabel;

        // ── 状态条 ───────────────────────────────────────────────────────────
        private Image _statusFrame;
        private Image _statusFill;
        private Image _stateSymbol;
        private Image _turnDot;
        private TextMeshProUGUI _stateText;
        private TextMeshProUGUI _stateCaption;

        // ── 力度块 ───────────────────────────────────────────────────────────
        private TextMeshProUGUI _powerPercent;
        private Image _powerFill;
        private RectTransform _thumbGlow;
        private RectTransform _thumb;
        private Image _shootFrame;
        private Image _shootFill;
        private TextMeshProUGUI _shootText;
        private Image _shootArrow;
        private TextMeshProUGUI _shotInfoValue;

        /// <summary>`Shoot` 下方的 `YOUR SHOT` 小标（对战形态隐藏 —— 中栏已有轮次信息，重复会叠在按钮上）。</summary>
        private TextMeshProUGUI _shotInfoLabel;

        /// <summary>品牌小标（`PRACTICE` / `AI Match`）：**随模式切换**（用户实机截图：对战里仍写 `PRACTICE`）。</summary>
        private TextMeshProUGUI _brandEyebrow;

        /// <summary>对战形态的状态行（`Foul` / `Ball in hand` / `Opponent` …）—— 状态条整块隐藏后的唯一出口。</summary>
        private TextMeshProUGUI _matchStatus;

        // ── 开关 ─────────────────────────────────────────────────────────────
        private Image _vfxIcon;
        private Image _sfxIcon;

        // ── 轮询基线 ─────────────────────────────────────────────────────────
        private int _lastScratchSeen;
        private int _lastRefusedSeen;

        /// <summary>落点被拒的轮询基线（与 <see cref="_lastRefusedSeen"/> **刻意分开**：两条拒绝链各有各的窗口）。</summary>
        private int _lastPlacementRefusedSeen;

        private int _lastLaunchSeen;
        private bool _ballInHandLatch;
        private float _foulUntil;
        private float _foulStart;
        private float _refusedUntil;

        /// <summary>落点被拒的提示窗口截止时刻（与 <see cref="_refusedUntil"/> 分开，见 `PlacementRefusedDetail`）。</summary>
        private float _placementRefusedUntil;

        /// <summary>**对手回合点击被拒**的提示窗口截止时刻（P5-B："不许静默"；与上面两个窗口同形）。</summary>
        private float _opponentBlockedUntil;

        private bool _built;

        /// <summary>
        /// 首页期"不可见让位"用的画布组（三块分区各一个；首次 <see cref="SetChromeVisible"/> 时建）。
        /// **刻意不序列化**（无 `[SerializeField]`）⇒ 不改场景、不需要重生成场景。
        /// </summary>
        private CanvasGroup[] _chromeGroups;

        /// <summary>
        /// **设置面板是否打开**（`Back to club` / `Quit` / 音量 三件）。
        /// ⚠ 这是**模态判据点**：`PointerAimController` / `PointerPowerController` / `AimAssistController`
        /// 与结算卡、首页并列读它 —— 否则面板开着时点击会穿透去瞄准或出杆。
        /// </summary>
        public static bool IsSettingsOpen { get; private set; }

        /// <summary>
        /// **操作引导是否打开**（首次进入时的一次性引导）。
        /// ⚠ 与设置面板同属**模态判据点**：三个输入控制器都读它，否则引导开着还能瞄准/出杆。
        /// </summary>
        public static bool IsOnboardingOpen { get; private set; }

        private RectTransform _onboardingPanel;
        private TextMeshProUGUI _onboardingTitle;
        private TextMeshProUGUI _onboardingBody;
        private TextMeshProUGUI _onboardingButtonLabel;
        private int _onboardingStep;

        /// <summary>设置按钮（文本 `SET`；工程无齿轮图标）。</summary>
        private TextMeshProUGUI _settingsButtonText;

        /// <summary>设置面板根（懒建一次；未打开时 `SetActive(false)`）。</summary>

        /// <summary>面板里的音量百分比文本。</summary>
        private TextMeshProUGUI _volumeValueText;

        /// <summary>音量轨道里的亮色填充条（随音量伸缩；只做视觉，不接收射线）。</summary>
        private RectTransform _volumeFill;

        /// <summary>判据入口：设置按钮节点。</summary>
        public RectTransform SettingsToggleForJudge => _settingsToggle;

        /// <summary>重置球桌按钮（仅练习模式可见；判据入口）。</summary>
        public Image ResetRackForJudge => _resetIcon;

        private Image _resetIcon;

        /// <summary>判据入口：音量百分比文本。</summary>
        public string VolumeTextForJudge => _volumeValueText != null ? _volumeValueText.text : null;

        /// <summary>判据收尾：把静态开关复位（EditMode 里对象销毁不会自动复位静态量）。</summary>
        public static void CloseSettingsForJudge() => IsSettingsOpen = false;

        /// <summary>首页视图（`Back to club` 用；场景未接线时运行时 `FindObjectOfType` 兜底）。</summary>
        public HomeView home;

        /// <summary>设置面板的三处文案（**只用拉丁字母**：字体只覆盖 Latin 子集）。</summary>
        public const string SettingsTitleText = "SETTINGS";
        public const string SettingsBackText = "Back to club";
        public const string SettingsQuitText = "Quit";
        public const string SettingsVolumeText = "Volume";
        public const string SettingsToggleText = "SET";
        public const string SettingsCloseText = "Close";

        /// <summary>记分牌专用分区（全宽，见 `EnsureBuilt`）。**必须进 `_chromeGroups`** —— 否则首页盖上来时它不会让位。</summary>
        private RectTransform _boardZone;

        /// <summary>
        /// HUD 画布的根 `RectTransform`。**结算卡挂这里而不是自建第二个画布** ——
        /// 同一画布 = 同一个 `CanvasScaler` 口径（不存在两份画布缩放），且只有一个 `sortingOrder` 要维护。
        /// </summary>
        public RectTransform CanvasRoot
        {
            get
            {
                EnsureBuilt();
                return (RectTransform)_canvas.transform;
            }
        }

        /// <summary>
        /// **显示 / 隐藏三块 HUD 分区**（首页盖在上面时用）。
        ///
        /// 为什么需要：参考稿的首页是**整屏覆盖**在 3D 画面上的（`.home-screen{inset:0;z-index:5}`），
        /// 且明确写了 `body.home-active .topbar /.scoreboard /.status-bar /.shotbar { display:none }`。
        /// 本工程 HUD 三块落在顶带 `y ∈ [0.780, 0.982]`，而首页的俱乐部徽章在 `y ≈ 0.896…0.978`
        /// ⇒ **不隐藏就必然叠字**（徽章与文字压住品牌行与记分牌）。
        ///
        /// ⚠ **只切三块分区**，**不动 `PracticeHudCanvas`、更不动 `GraphicRaycaster`**：
        /// 首页正是建在这台画布上的（`HomeView` 走 <see cref="CanvasRoot"/>），
        /// 把画布 `SetActive(false)` 会连首页一起停掉；而射线器是全场景唯一的那一个（`T102 (c)` 守）。
        /// </summary>
        public void SetChromeVisible(bool visible)
        {
            EnsureBuilt();

            CanvasGroup[] groups = EnsureChromeGroups();
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] == null)
                {
                    continue;
                }

                // 不可见 = `alpha 0`（视觉上等价于设计稿 `body.home-active` 的 `display:none`）；
                // ⚠ **必须同时断射线与交互**：否则首页期那两个 38×38 的 VFX / SFX 开关会变成
                //   **隐形但可点** —— 那是"为了修卡顿换来一个看不见的可点区域"，不能接受。
                groups[i].alpha = visible ? 1f : 0f;
                groups[i].blocksRaycasts = visible;
                groups[i].interactable = visible;
            }

            if (!visible)
            {
                // 让位的同时把"首次出图"的开销**当场付掉**（动机与实测见 `WarmUpChromeText` 的注释）。
                WarmUpChromeText();
            }
        }

        /// <summary>
        /// 首页期"不可见让位"用的画布组（三块分区各一个；**首次调用时建**，不进序列化 ⇒ 不改场景）。
        ///
        /// ── 为什么用 `CanvasGroup.alpha = 0` 而不是 `SetActive(false)` ──────────────────
        /// 两者**视觉上等价**（都看不见），但**开销时机不同**：`SetActive(false)` 会把三块的首次
        /// 网格/材质/绘制开销**推迟到"点开始那一刻"**（转场探针实测单帧 **20.834 ms**，基线 ≈6.94 —— 见
        /// `WarmUpChromeText` 的注释）；保持 `active` + `alpha 0` 则让这部分开销落在**首页期**（静态菜单，
        /// 无人察觉）。两条路径**并行保留**（`WarmUpChromeText` 覆盖 TMP 网格/图集；
        /// 真正画出一次覆盖"材质/着色器首次使用"）—— 后者**只有真的绘制才可能被预热**，
        /// 故不能只留 `ForceMeshUpdate`。
        /// </summary>
        private CanvasGroup[] EnsureChromeGroups()
        {
            if (_chromeGroups != null)
            {
                return _chromeGroups;
            }

            RectTransform[] zones = { _counters, _status, _power, _boardZone };
            _chromeGroups = new CanvasGroup[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                _chromeGroups[i] = zones[i] == null ? null : zones[i].gameObject.AddComponent<CanvasGroup>();
            }

            return _chromeGroups;
        }

        /// <summary>
        /// **不可见预热**：把三个分区的 TMP **网格 / 字体图集**在**分区物体保持 `inactive`（= 不可见）**的前提下
        /// 先出一次，把"首次激活时的一次性开销"从"点开始那一刻"挪到**没人看的时候**。
        ///
        /// ── 为什么需要它（实测依据，不是猜测）────────────────────────────────────────
        /// 用户实机反馈"转场有点卡顿"。**转场负载探针**实测（`-billiards-smoke-transition`）：
        /// · 转场窗口 p95 **6.9515 ms** vs 前后稳态 **6.9508 / 6.9510 ms** ⇒ **转场期间每帧都慢不成立**；
        /// · 但**触发后第 1 帧单帧 20.834 ms**（基线 ≈6.94，**≈3×**、超 16.67 ms 预算）
        /// ⇒ 结论 = **"点开始那一刻的一次性开销"**（`SetChromeVisible(true)` 首次激活三个分区时
        /// TMP 首次出网格/图集），**不是转场本身的问题** ⇒ 正确修法是**把这次性开销预热掉**。
        ///
        /// ── 为什么**不能**"先显示一帧再隐藏" ────────────────────────────────────────
        /// 那会在首页上**闪一下界面** —— 把"卡顿"换成"可见瑕疵"，等于没修（**主理人硬约束**）。
        /// 故本方法**全程不改变激活状态**（分区始终 `inactive`），只借
        /// <c>ForceMeshUpdate(ignoreActiveState: true, …)</c> 在**不可见状态下**把网格/图集先算出来。
        ///
        /// ⚠ **失败方向**：若某版 TMP 在 `inactive` 下拒绝出图（`ForceMeshUpdate` 变空操作），
        /// 本方法**不报错**、只是白做 —— 届时**尖峰不会消失**，由探针的"最慢帧"数字**如实暴露**
        /// （见规格 §13.5：验收标准就是"最慢帧回落到基线量级"）。
        /// </summary>
        /// <param name="forceTextReparsing">
        /// 是否强制重新解析文本。**首次预热传 `true`**（保证走完整条解析/布局/出图路径，不留半成品状态）。
        /// </param>
        public void WarmUpChromeText(bool forceTextReparsing = true)
        {
            EnsureBuilt();

            WarmUpZone(_counters, forceTextReparsing);
            WarmUpZone(_status, forceTextReparsing);
            WarmUpZone(_power, forceTextReparsing);
        }

        /// <summary>对一个分区下的**全部** TMP 文本做一次不可见出图（含 `inactive` 子树）。</summary>
        private static void WarmUpZone(RectTransform zone, bool forceTextReparsing)
        {
            if (zone == null)
            {
                return;
            }

            // ⚠ `includeInactive: true` —— 预热时分区**本来就是 inactive**（这正是"不可见"的来源）。
            TextMeshProUGUI[] texts = zone.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                // `ignoreActiveState: true` = 即使物体 inactive 也照样出图（这是"不可见预热"能成立的关键）。
                texts[i].ForceMeshUpdate(true, forceTextReparsing);
            }
        }

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

            // 射线器：**结算卡的按钮**与右上两个开关需要它
            // （本画布是全场景唯一带射线器的画布，`T102 (c)` 守）。
            canvasGo.AddComponent<GraphicRaycaster>();

            _counters = CreateZone(canvasGo.transform, zones.counters, "Counters");
            _status = CreateZone(canvasGo.transform, zones.status, "Status");
            _power = CreateZone(canvasGo.transform, zones.power, "Power");

            BuildScoreboard(_counters,
                // **记分牌独占一条分区**：宽度到 0.690 为止 —— 右侧 `0.700–0.980` 是力度块。
                // ⚠ 之前给到 0.960 ⇒ **右块被力度块整片盖住**（用户实机截图：`Expert AI`/`Stripes` 压在 `Shoot` 上）。
                _boardZone = CreateZone(canvasGo.transform, new Rect(0.020f, 0.780f, 0.670f, 0.202f), "Board"));
            BuildStatusBar(_status);
            BuildShotbar(_power);

            // **一行启动日志**（P7-1）：把"用户实际看到的字号"一次性钉成可读数据 ——
            // 起因：用户两次反馈"看不清"，而**窗口模式**下 `Screen.width/height` 可能不是 1920×1080
            // （若是 1280×720，`CanvasScale` 只有 0.667 ⇒ 正文在 1.9 下也只有 15.2 px，那才是真凶）。
            // ⚠ `Application.isPlaying` 守卫：EditMode 判据也会调 `EnsureBuilt`，不守卫会刷屏（且本工程刚修过"假告警"）。
            // ⚠ `Screen.dpi` 在**窗口模式**可能返回 0 ⇒ 标 `0(未知)`，**不拿它做任何判断**。
            if (Application.isPlaying)
            {
                int w = Screen.width;
                int h = Screen.height;
                string dpi = Screen.dpi > 0f ? Screen.dpi.ToString("F0") : "0(未知)";

                // ⚠ 这两个值**必须走 `RenderedFontPixels`**（与判据同一个函数、同一个真源）——
                //   若在这里自己写 `字号 * canvasScale * FontScale`，就等于在视图里**又乘了一次倍率**
                //   ⇒ `T148(d)`「视图里该乘积只准出现 1 次」会立刻红（第一版就是这么红的，实测 `实测 3`）。
                Debug.Log(
                    $"[UI/字号] screen={w}x{h} dpi={dpi} "
                    + $"canvasScale={PracticeHudLayout.CanvasScale(w, h):F4} "
                    // ⚠ **故意不打印 `FontScale` 常量本身**：那会让视图里多出一处该标识符的出现，
                    //   而 `T148(d)` 守的正是"视图里只有 `CreateText` 那一处提到它"（bodyPx 已隐含倍率）。
                    + $"bodyPx={PracticeHudLayout.RenderedFontPixels(ClubUiTokens.FontBody, w, h):F1} "
                    + $"captionPx={PracticeHudLayout.RenderedFontPixels(ClubUiTokens.FontCaption, w, h):F1} "
                    + "homeScale=1.00(首页按用户要求保持原字号)", this);
            }
        }

        // ════════════════ 分区 A：品牌 + 记分牌 ═══════════════════════════════

        private void BuildScoreboard(RectTransform zone, RectTransform boardZone)
        {
            // ── 品牌行（设计稿 `.brand`：白球徽章 + wordmark + 模式小标）──────
            RectTransform brand = AddRect(zone, "Brand", new Vector2(0f, 0.78f), new Vector2(1f, 1f));
            var brandAnchor = new Vector2(0f, 0.5f);
            var brandCenter = new Vector2(ClubUiTokens.BrandBallSize * 0.5f + ClubUiTokens.Hairline, 0f);

            RectTransform ring = AddFixed(brand, "BrandBallRing", ClubUiSprites.UiSprite.Ring,
                ClubUiTokens.BrandBallRing, brandAnchor,
                new Vector2(ClubUiTokens.BrandBallRingSize, ClubUiTokens.BrandBallRingSize));
            ring.anchoredPosition = brandCenter;

            RectTransform ball = AddFixed(brand, "BrandBall", ClubUiSprites.UiSprite.Disc,
                ClubUiTokens.BrandBallFill, brandAnchor,
                new Vector2(ClubUiTokens.BrandBallSize, ClubUiTokens.BrandBallSize));
            ball.anchoredPosition = brandCenter;

            CreateText(ball, "BrandBallText", titleFont, ClubUiTokens.FontBrandBall, ClubUiTokens.BrandBallText,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = PracticeHudState.BrandBallNumber;

            CreateText(brand, "BrandTitle", titleFont, ClubUiTokens.FontBrand, ClubUiTokens.Text,
                TextAlignmentOptions.BottomLeft, new Vector2(0.092f, 0.44f), new Vector2(0.70f, 0.98f))
                .text = PracticeHudState.BrandTitle;

            _brandEyebrow = CreateText(brand, "BrandEyebrow", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Accent,
                TextAlignmentOptions.TopLeft, new Vector2(0.092f, 0.04f), new Vector2(0.70f, 0.44f));
            _brandEyebrow.text = PracticeHudState.BrandEyebrow;

            BuildToolButtons(brand);

            // ── 记分牌（设计稿 `.scoreboard`：上下细边线 + 85% 炭黑底）────────
            RectTransform board = AddRect(boardZone, "Scoreboard", new Vector2(0f, 0.06f), new Vector2(1f, 0.72f));
            _scoreboard = board;
            SetImage(board.gameObject.AddComponent<Image>(), null, ClubUiTokens.PanelScoreboard);
            AddEdgeStrip(board, "ScoreTopLine", true, ClubUiTokens.LineScoreTop);
            AddEdgeStrip(board, "ScoreBottomLine", false, ClubUiTokens.Line);

            // 左栏：头像 + 玩家名 + 实时统计
            var avatarAnchor = new Vector2(0.045f, 0.5f);
            RectTransform avatar = AddFixed(board, "Avatar", ClubUiSprites.UiSprite.Disc, ClubUiTokens.AvatarFill,
                avatarAnchor, new Vector2(ClubUiTokens.AvatarSize, ClubUiTokens.AvatarSize));
            AddFixed(board, "AvatarRing", ClubUiSprites.UiSprite.Ring, ClubUiTokens.AvatarEdge, avatarAnchor,
                new Vector2(ClubUiTokens.AvatarSize, ClubUiTokens.AvatarSize));
            AddFixed(avatar, "AvatarGlyph", ClubUiSprites.UiSprite.Player, ClubUiTokens.AvatarIcon,
                new Vector2(0.5f, 0.5f),
                new Vector2(ClubUiTokens.AvatarIconSize, ClubUiTokens.AvatarIconSize));

            TextMeshProUGUI youName = CreateText(board, "PlayerName", bodyFont, ClubUiTokens.FontLabel,
                ClubUiTokens.Text, TextAlignmentOptions.BottomLeft,
                new Vector2(0.100f, 0.64f), new Vector2(0.340f, 1f));
            youName.text = PracticeHudState.LabelYou;
            // **单行**：名字不许折行（用户实机截图里 `Expert AI` 被折成 `Expe` / `rt AI`）。
            youName.enableWordWrapping = false;
            youName.overflowMode = TextOverflowModes.Overflow;

            // 组别小标签（`Solids`/`Stripes`）：**开放球局是空串（零字形）**，定组后才出现。
            _yourGroupLabel = CreateText(board, "PlayerGroupTag", bodyFont, ClubUiTokens.FontSmall,
                ClubUiTokens.Muted, TextAlignmentOptions.Left,
                new Vector2(0.100f, 0.46f), new Vector2(0.340f, 0.62f));
            _yourGroupLabel.text = string.Empty;

            _playerStats = CreateText(board, "PlayerStats", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Label,
                TextAlignmentOptions.TopLeft, new Vector2(0.100f, 0.32f), new Vector2(0.340f, 0.44f));

            // 中栏：`POCKETED` + 大数字 + 目标说明（设计稿 `.score-center`）
            _scoreLabel = CreateText(board, "ScoreLabel", bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.Accent,
                TextAlignmentOptions.Bottom, new Vector2(0.36f, 0.80f), new Vector2(0.66f, 1f));
            // 练习形态：小字 = `POCKETED`（对战形态由 `ApplyMatchHud` 换成 `RACK 1`）。
            _scoreLabel.text = PracticeHudState.LabelPocketed;

            _scoreValue = CreateText(board, "ScoreValue", titleFont, ClubUiTokens.FontScore, ClubUiTokens.Text,
                TextAlignmentOptions.Center, new Vector2(0.36f, 0.38f), new Vector2(0.66f, 0.80f));

            // **对战状态行**：状态条整块隐藏后，`Foul` / `Ball in hand` / `Opponent` 这些提示必须有出口
            //   （用户问："犯规提示…AI 对战里现在没有地方设置了"）。
            // 只在**非 Aim** 时显示 —— 常态不占版面。
            _matchStatus = CreateText(board, "MatchStatus", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Accent,
                TextAlignmentOptions.Center, new Vector2(0.36f, 0.08f), new Vector2(0.66f, 0.34f));
            _matchStatus.text = string.Empty;
            _matchStatus.gameObject.SetActive(false);
            // 大字 = **当前该谁打**（不是比分 —— 本工程是一局定胜负，0—0 是假信息）。
            _scoreValue.text = PracticeHudState.YourShotLabel;

            _playerStats.text = PracticeHudState.LabelShots + " 0   " + PracticeHudState.LabelFouls + " 0";

            _goalText = CreateText(board, "GoalText", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Muted,
                TextAlignmentOptions.Top, new Vector2(0.34f, 0.04f), new Vector2(0.68f, 0.34f));
            _goalText.text = PracticeHudState.CaptionIdle;

            // 右栏：球行（未进亮、已进 20% 透明 ⇒ 一眼看出还剩哪些）
            _ballsLabel = CreateText(board, "BallsLabel", bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.Accent,
                TextAlignmentOptions.BottomRight, new Vector2(0.70f, 0.80f), new Vector2(0.955f, 1f));
            _ballsLabel.text = PracticeHudState.LabelBallsLeft;

            BuildBallRow(board);
            // **对手头像**（对战模式才显示）：复用既有 `Disc`+`Ring`+`Robot` 精灵 ⇒ **零新增材质**
            // （美术包里没有任何人物/头像资源：`02_Characters_人物` 只有"不适用说明.md"）。
            _oppAvatar = AddFixed(board, "OppAvatar", ClubUiSprites.UiSprite.Disc, ClubUiTokens.AvatarFill,
                new Vector2(0.955f, 0.5f), new Vector2(ClubUiTokens.AvatarSize, ClubUiTokens.AvatarSize));
            AddFixed(_oppAvatar, "OppAvatarRing", ClubUiSprites.UiSprite.Ring, ClubUiTokens.AvatarEdge,
                new Vector2(0.5f, 0.5f), new Vector2(ClubUiTokens.AvatarSize, ClubUiTokens.AvatarSize));
            AddFixed(_oppAvatar, "OppAvatarGlyph", ClubUiSprites.UiSprite.Robot, ClubUiTokens.AvatarIcon,
                new Vector2(0.5f, 0.5f), new Vector2(ClubUiTokens.AvatarIconSize, ClubUiTokens.AvatarIconSize));
            _oppAvatar.gameObject.SetActive(false);

            // 右栏文字（镜像左栏）：`Expert AI` + 组别小标签；开放球局时组别为空。
            _oppName = CreateText(board, "OppName", bodyFont, ClubUiTokens.FontLabel, ClubUiTokens.Text,
                TextAlignmentOptions.BottomRight, new Vector2(0.660f, 0.64f), new Vector2(0.900f, 1f));
            _oppName.text = PracticeHudState.OpponentDisplayName;
            _oppName.enableWordWrapping = false;
            _oppName.overflowMode = TextOverflowModes.Overflow;
            _oppGroupLabel = CreateText(board, "OppGroupTag", bodyFont, ClubUiTokens.FontSmall,
                ClubUiTokens.Muted, TextAlignmentOptions.Right, new Vector2(0.660f, 0.46f), new Vector2(0.900f, 0.62f));
            _oppGroupLabel.text = string.Empty;
            BuildGroupChipRow(board, "YourGroupChips", 0.02f, 0.34f, 0.08f, out _yourGroupChips);
            BuildGroupChipRow(board, "OppGroupChips", 0.66f, 0.98f, 0.08f, out _oppGroupChips);
            // 判据缓存：**必须在建树时初始化**（否则第一次 `ApplyGroupChips` 收到 null ⇒ 判据永远读空）。
            _yourShownNumbers = new int[PracticeHudState.MatchGroupChipSlots];
            _oppShownNumbers = new int[PracticeHudState.MatchGroupChipSlots];
            for (int c = 0; c < _yourShownNumbers.Length; c++)
            {
                _yourShownNumbers[c] = -1;
                _oppShownNumbers[c] = -1;
            }
        }

        /// <summary>
        /// 球行：15 个目标球，两行排布。**贴片自带球色与号码** ⇒ 这里只负责摆位与"已进袋"的压暗。
        /// 贴片缺失时退回"纯色圆 + 色表颜色"（判据 `T124` 会把缺贴片钉红）。
        /// </summary>
        private void BuildBallRow(RectTransform board)
        {
            const int numbers = ClubBallChips.Count - 1;      // 15 个目标球（不含白球）
            _ballChips = new Image[numbers];

            for (int i = 0; i < numbers; i++)
            {
                int row = i / BallRowColumns;
                int column = i % BallRowColumns;
                int columnsInRow = Mathf.Min(BallRowColumns, numbers - row * BallRowColumns);
                int number = i + 1;                           // 球号 1..15

                // 两行各占 0.30 的记分牌高度（记分牌在本工程是 0.66 × 218 ≈ 144 px ⇒ 每行 ≈ 43 px，
                // 15 px 的球在其中垂直居中；`BallsLabel` 在其上方的 0.80–1.00 带内）。
                var rowRect = AddRect(board, "BallRow" + row,
                    new Vector2(0.70f, 0.36f - row * 0.30f),
                    new Vector2(0.955f, 0.66f - row * 0.30f));

                float x = (column + 0.5f) / columnsInRow;
                RectTransform chip = AddFixed(rowRect, "Ball" + number.ToString("00"),
                    ClubUiSprites.UiSprite.Disc, ClubBallChips.PaletteColor(number),
                    new Vector2(x, 0.5f), new Vector2(ClubUiTokens.MiniBallSize, ClubUiTokens.MiniBallSize));

                var image = chip.GetComponent<Image>();
                Sprite face = ClubBallChips.Resolve(ballChips, number);
                if (face != null)
                {
                    image.sprite = face;
                    image.color = ClubUiTokens.Opaque;        // 贴片自带颜色 ⇒ 不叠色
                }

                _ballChips[i] = image;
            }
        }

        private void BuildToolButtons(RectTransform brand)
        {
            // 设计稿 `.tools`：右上角一组 38×38 图标按钮。
            // 本工程只放**真实生效**的按钮 —— 做个点不动的齿轮是最典型的假 UI。
            _vfxIcon = AddIconButton(brand, "VfxToggle", ClubUiSprites.UiSprite.Sparkle, 0, OnToggleVfx);
            _sfxIcon = AddIconButton(brand, "SfxToggle", ClubUiSprites.UiSprite.Volume, 1, OnToggleSfx);


            // **设置**（第三个位置）。⚠ 工程现有 16 个图标槽里**没有齿轮/菜单图标**，
            //   而 `Pause` 已被"暂停"语义占用（拿来当设置会误导）⇒ 先用**文本 `SET`** 的同尺寸按钮，
            //   待美术补齿轮图标后只换 glyph、不动布局。
            //   不用 `⚙`：字体只有 Latin 子集，缺字会变空白。
            AddSettingsButton(brand, "SettingsToggle", 2, OnOpenSettings);

            // **重置球桌**（仅练习模式）：圆形箭头 = 重摆；对战中隐藏（会把对局清掉）。
            _resetIcon = AddIconButton(brand, "ResetRack", ClubUiSprites.UiSprite.Orbit, 3, OnResetRack);
            _settingsToggle = (RectTransform)brand.Find("SettingsToggle");
        }

        /// <summary>
        /// **设置按钮**：优先用程序化生成的**齿轮位图**（`Resources/UiGear.png`，与图标同一套导入规则）；
        /// 取不到时退回文本 `SET` —— 保证任何构建里都不会出现"看得见、点不动"或空白按钮。
        /// </summary>
        private void AddSettingsButton(RectTransform parent, string name, int slot,
                                       UnityEngine.Events.UnityAction action)
        {
            float step = ClubUiTokens.IconButtonSize + ClubUiTokens.IconButtonGap;
            RectTransform frame = AddFramed(parent, name,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusSegment);
            frame.pivot = new Vector2(1f, 0.5f);
            frame.sizeDelta = new Vector2(ClubUiTokens.IconButtonSize, ClubUiTokens.IconButtonSize);
            frame.anchoredPosition = new Vector2(-slot * step, 0f);

            var hit = frame.GetComponent<Image>();
            hit.raycastTarget = true;   // 可点控件必须显式开射线（`SetImage` 默认关）

            Sprite gear = Resources.Load<Sprite>("UiGear");
            if (gear != null)
            {
                RectTransform glyph = AddRect(frame, "Glyph", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                glyph.sizeDelta = new Vector2(ClubUiTokens.IconButtonIconSize, ClubUiTokens.IconButtonIconSize);
                var glyphImage = glyph.gameObject.AddComponent<Image>();
                glyphImage.sprite = gear;
                glyphImage.color = ClubUiTokens.IconIdle;
                glyphImage.raycastTarget = false;
            }
            else
            {
                TextMeshProUGUI text = CreateText(frame, "Label", bodyFont, ClubUiTokens.FontMicro,
                    ClubUiTokens.IconIdle, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
                text.text = PracticeHudState.ButtonSettings;
            }

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(action);
        }

        /// <summary>与 <see cref="AddIconButton"/> 同尺寸同边框，但内容是**文本**（用于缺图标的场合）。</summary>
        private TextMeshProUGUI AddTextButton(RectTransform parent, string name, string label, int slot,
                                              UnityEngine.Events.UnityAction action)
        {
            float step = ClubUiTokens.IconButtonSize + ClubUiTokens.IconButtonGap;
            RectTransform frame = AddFramed(parent, name,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusSegment);
            frame.pivot = new Vector2(1f, 0.5f);
            frame.sizeDelta = new Vector2(ClubUiTokens.IconButtonSize, ClubUiTokens.IconButtonSize);
            frame.anchoredPosition = new Vector2(-slot * step, 0f);

            var hit = frame.GetComponent<Image>();
            hit.raycastTarget = true;

            TextMeshProUGUI text = CreateText(frame, "Label", bodyFont, ClubUiTokens.FontMicro,
                ClubUiTokens.IconIdle, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            text.text = label;

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(action);
            return text;
        }

        private Image AddIconButton(RectTransform parent, string name, ClubUiSprites.UiSprite sprite, int slot,
                                    UnityEngine.Events.UnityAction action)
        {
            float step = ClubUiTokens.IconButtonSize + ClubUiTokens.IconButtonGap;
            RectTransform frame = AddFramed(parent, name,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusSegment);
            frame.pivot = new Vector2(1f, 0.5f);
            frame.sizeDelta = new Vector2(ClubUiTokens.IconButtonSize, ClubUiTokens.IconButtonSize);
            frame.anchoredPosition = new Vector2(-slot * step, 0f);

            var hit = frame.GetComponent<Image>();
            hit.raycastTarget = true;   // 本画布上少数几个真的可点的控件之一

            RectTransform glyph = AddFixed(frame, "Glyph", sprite, ClubUiTokens.IconIdle,
                new Vector2(0.5f, 0.5f),
                new Vector2(ClubUiTokens.IconButtonIconSize, ClubUiTokens.IconButtonIconSize));

            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(action);
            return glyph.GetComponent<Image>();
        }


        // ════════════════ 分区 B：模式切换 + 状态条 ═══════════════════════════

        private void BuildStatusBar(RectTransform zone)
        {
            // ── 模式切换（`.mode-switch`）**已按用户要求整块移除** ──────────────
            // 用户实机截图圈出顶部 `Practice | AI Match` 并明确要求去掉。
            // ⇒ 不再建这个控件（它也只是"看起来能切"的静态段：本工程没有局中切模式的接线，
            //   留着就是假可点态）。模式改由**首页入口**决定，HUD 只用文案表达当前模式。

            // ── 状态条（设计稿 `.status-bar`：12 px 正文 + 圆点 + 图标 + 可换边线色）──
            RectTransform bar = AddFramed(zone, "StatusBar",
                new Vector2(0f, 0.04f), new Vector2(1f, 0.64f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelStatus, ClubUiTokens.RadiusPanel);
            _statusFrame = bar.GetComponent<Image>();
            _statusFill = bar.Find("Fill") != null ? bar.Find("Fill").GetComponent<Image>() : null;

            RectTransform dot = AddFixed(bar, "TurnDot", ClubUiSprites.UiSprite.Panel1, ClubUiTokens.Green,
                new Vector2(0.030f, 0.70f), new Vector2(ClubUiTokens.TurnDotSize, ClubUiTokens.TurnDotSize));
            dot.localRotation = Quaternion.Euler(0f, 0f, 45f);   // 设计稿 `#turn-dot` 是 5×5 菱形
            _turnDot = dot.GetComponent<Image>();

            RectTransform symbol = AddFixed(bar, "StateSymbol", ClubUiSprites.UiSprite.Crosshair,
                ClubUiTokens.Green, new Vector2(0.068f, 0.70f),
                new Vector2(ClubUiTokens.StatusSymbolSize, ClubUiTokens.StatusSymbolSize));
            _stateSymbol = symbol.GetComponent<Image>();

            _stateText = CreateText(bar, "StateText", bodyFont, ClubUiTokens.FontBody, ClubUiTokens.Text,
                TextAlignmentOptions.Left, new Vector2(0.110f, 0.48f), new Vector2(0.96f, 0.94f));

            _stateCaption = CreateText(bar, "StateCaption", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Muted,
                TextAlignmentOptions.TopLeft, new Vector2(0.110f, 0.06f), new Vector2(0.96f, 0.48f));
        }

        // ════════════════ 分区 C：力度块（出杆栏）══════════════════════════════

        private void BuildShotbar(RectTransform zone)
        {
            // **构建期自检**：力度块的设计域宽度必须容得下设计稿给按钮的**最小**宽度。
            // 分区是视口比例常量 ⇒ 这一步在构建时就能确定地算出来，不必等实机才发现按钮被压扁。
            float powerZoneWidthPx = PracticeHudLayout.PowerZone.width * PracticeHudLayout.ReferenceWidth;
            if (PracticeHudLayout.ShootButtonWidth > powerZoneWidthPx)
            {
                Debug.LogWarning(
                    $"力度块设计宽 {powerZoneWidthPx} px 放不下设计稿的按钮最小宽 "
                    + $"{PracticeHudLayout.ShootButtonWidth} px —— 应调分区或改按钮尺寸口径。", this);
            }

            AddFramed(zone, "Shotbar", Vector2.zero, Vector2.one,
                ClubUiTokens.LineStrong, ClubUiTokens.PanelShotbar, ClubUiTokens.RadiusDialog);

            RectTransform bar = (RectTransform)zone.Find("Shotbar");

            CreateText(bar, "PowerLabel", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Label,
                TextAlignmentOptions.BottomLeft, new Vector2(0.045f, 0.80f), new Vector2(0.55f, 0.95f))
                .text = PracticeHudState.LabelPower;

            _powerPercent = CreateText(bar, "PowerValue", titleFont, ClubUiTokens.FontBody, ClubUiTokens.PowerValue,
                TextAlignmentOptions.BottomRight, new Vector2(0.50f, 0.80f), new Vector2(0.955f, 0.95f));

            // 力度条：轨道（5 px）+ 填充 + 滑块（14 px 亮球 + 外光）
            // ⚠ 填充**必须有精灵**：uGUI 的 `Image` 在 `sprite == null` 时会跳过 Filled 分支、
            //    直接画一整块实心矩形 ⇒ `fillAmount` 完全失效（实测踩过）。
            RectTransform track = AddRect(bar, "PowerTrack", new Vector2(0.045f, 0.63f), new Vector2(0.955f, 0.73f));
            ClubUiSprites.UiSprite railSprite = ClubUiSprites.PanelForRadius(ClubUiTokens.RadiusTrack);

            RectTransform rail = AddRect(track, "Rail", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            rail.offsetMin = new Vector2(0f, -ClubUiTokens.PowerTrackHeight * 0.5f);
            rail.offsetMax = new Vector2(0f, ClubUiTokens.PowerTrackHeight * 0.5f);
            SetImage(rail.gameObject.AddComponent<Image>(), railSprite, ClubUiTokens.PowerTrack);

            RectTransform fillRect = AddRect(track, "Fill", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillRect.offsetMin = new Vector2(0f, -ClubUiTokens.PowerTrackHeight * 0.5f);
            fillRect.offsetMax = new Vector2(0f, ClubUiTokens.PowerTrackHeight * 0.5f);
            _powerFill = fillRect.gameObject.AddComponent<Image>();
            SetImage(_powerFill, railSprite, ClubUiTokens.Accent);
            _powerFill.type = Image.Type.Filled;
            _powerFill.fillMethod = Image.FillMethod.Horizontal;
            _powerFill.fillOrigin = 0;
            _powerFill.fillAmount = 0f;

            _thumbGlow = AddFixed(track, "ThumbGlow", ClubUiSprites.UiSprite.Disc,
                ClubUiTokens.Alpha(ClubUiTokens.Accent, 0x22), new Vector2(0f, 0.5f),
                new Vector2(ClubUiTokens.PowerThumbSize + 6f, ClubUiTokens.PowerThumbSize + 6f));
            _thumb = AddFixed(track, "Thumb", ClubUiSprites.UiSprite.Disc, ClubUiTokens.PowerThumb,
                new Vector2(0f, 0.5f), new Vector2(ClubUiTokens.PowerThumbSize, ClubUiTokens.PowerThumbSize));

            // 主按钮（设计稿 `.shoot-button`：黄铜底 + 深字 + 1 px 亮边 + 右侧箭头）
            RectTransform button = AddFramed(bar, "Shoot",
                new Vector2(0.045f, 0.40f), new Vector2(0.955f, 0.40f),
                ClubUiTokens.ButtonEdge, ClubUiTokens.ButtonFill, ClubUiTokens.RadiusButton);
            button.offsetMin = new Vector2(0f, -ClubUiTokens.ButtonHeight * 0.5f);
            button.offsetMax = new Vector2(0f, ClubUiTokens.ButtonHeight * 0.5f);
            _shootFrame = button.GetComponent<Image>();
            _shootFill = button.Find("Fill") != null ? button.Find("Fill").GetComponent<Image>() : null;

            // **文字 + 箭头作为整体居中**（用户实机反馈：原来文字偏左、箭头孤零零贴在右侧）。
            _shootText = CreateText(button, "ShootText", titleFont, ClubUiTokens.FontBody, ClubUiTokens.ButtonText,
                TextAlignmentOptions.Center, new Vector2(0.26f, 0f), new Vector2(0.72f, 1f));
            _shootText.text = PracticeHudState.ButtonShoot;

            RectTransform arrow = AddFixed(button, "Arrow", ClubUiSprites.UiSprite.ArrowRight,
                ClubUiTokens.ButtonText, new Vector2(0.745f, 0.5f),
                new Vector2(ClubUiTokens.ButtonArrowSize, ClubUiTokens.ButtonArrowSize));
            _shootArrow = arrow.GetComponent<Image>();

            // 出杆信息（设计稿 `.shot-info`）
            _shotInfoLabel = CreateText(bar, "ShotInfoLabel", bodyFont, ClubUiTokens.FontMicro, ClubUiTokens.Accent,
                TextAlignmentOptions.Bottom, new Vector2(0.045f, 0.19f), new Vector2(0.955f, 0.28f));
            _shotInfoLabel.text = PracticeHudState.LabelYourShot;

            _shotInfoValue = CreateText(bar, "ShotInfoValue", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Muted,
                TextAlignmentOptions.Top, new Vector2(0.045f, 0.06f), new Vector2(0.955f, 0.19f));
        }

        // ════════════════ 刷新 ═══════════════════════════════════════════════

        // ════════════════ 指定袋选袋条（P6 第 3 小段：D3 的 UI 半边）══════════════

        /// <summary>按当前状态刷值。**只写显示层**，不碰任何物理量。</summary>
        public void Refresh()
        {
            if (!_built)
            {
                return;
            }

            ShotPhase phase = cueStick != null && cueStick.Flow != null ? cueStick.Flow.Phase : ShotPhase.Aim;
            float now = Time.unscaledTime;

            // 犯规 / 自由球：轮询 `ScratchCount` 与 `LaunchCount` 的**增量**（与 `PoolAudio` /
            // `PoolVfxView` 同一口径：计数只认上升，下降静默更新基准）。
            //
            // ⚠ **顺序是硬约束**：先看 `LaunchCount`、再看 `ScratchCount`。
            //    白球落袋发生在那一次出杆里 ⇒ 两个计数**同一帧**都上升；
            //    若先处理犯规、后处理出杆，`_ballInHandLatch` 会被同一帧的出杆立刻清掉
            //    ⇒ 自由球状态永远不会显示（这正是"写完看不见效果"的典型死法）。
            if (drive != null)
            {
                if (drive.LaunchCount > _lastLaunchSeen)
                {
                    _lastLaunchSeen = drive.LaunchCount;
                    _ballInHandLatch = false;
                }

                if (drive.ScratchCount > _lastScratchSeen)
                {
                    _lastScratchSeen = drive.ScratchCount;
                    _foulUntil = now + FoulBannerSeconds;
                    _foulStart = now;
                    _ballInHandLatch = true;
                }

                // 出杆被拒：`Launch()` 在未静止时**静默返回零向量**，玩家实测"这个角度为什么发不出去"
                // —— 按了完全没反应、也看不到原因。这里把"没反应"变成"看得见的原因"。
                if (drive.RefusedLaunchCount > _lastRefusedSeen)
                {
                    _lastRefusedSeen = drive.RefusedLaunchCount;
                    _refusedUntil = now + FoulBannerSeconds;
                }

                // **落点被拒**（"全场自由球"）：`PlacePendingCueBall` 在非法落点上**不改任何状态**、
                // 只把 `RefusedPlacementCount` 加一 ⇒ 与"出杆被拒"同一种"静默失败"，同样要变成
                // "看得见的原因"。**另开一个窗口、另用一条文案**（详见 `PracticeHudState.PlacementRefusedDetail`）：
                // 那一刻台面**是静止的**，所以出杆被拒那句 `"Wait for the table"` 在这里是**假陈述**。
                if (drive.RefusedPlacementCount > _lastPlacementRefusedSeen)
                {
                    _lastPlacementRefusedSeen = drive.RefusedPlacementCount;
                    _placementRefusedUntil = now + FoulBannerSeconds;
                }

                // **对手回合点击被拒**（P5-B：用户要求"不许静默"）：
                // `MatchTurnNotice` 只记账 + 置一个 pending 位，**时间窗在这里开** ——
                // 与 `ScratchCount` / `RefusedLaunchCount` / `RefusedPlacementCount` 的轮询范式逐字同形。
                // ⚠ P6：**结算面板期间消费并丢弃**（不排队补弹）——
                // 优先级见 `PracticeHudState.OpponentNoticeAllowed`（结算 > 对手回合提示）。
                bool opponentBlockedPending = MatchTurnNotice.ConsumePending();
                if (opponentBlockedPending
                    && PracticeHudState.OpponentNoticeAllowed(PracticeResultView.IsOpen))
                {
                    _opponentBlockedUntil = now + FoulBannerSeconds;
                }
            }

            bool foulWindow = now < _foulUntil;
            bool refusedWindow = now < _refusedUntil;
            bool placementRefusedWindow = now < _placementRefusedUntil;
            bool opponentBlockedWindow = now < _opponentBlockedUntil;
            bool ballInHand = _ballInHandLatch && phase == ShotPhase.Aim;

            // **待放球**（"全场自由球"新规则）：白球**不在台上**、玩家尚未放置、轮到玩家、台面已停
            // ⇒ 它不是"等着被击打"，而是**等着被放下**（点台面任意合法位置放下；`Esc` 放回**开球位**）。
            // 判据用**语义量** `PhysicsTestDrive.PendingCueBallPlacement`（物理侧新增的只读出口，
            // 语义 = "白球离台 + 玩家尚未放置"），**不再**由更底层的 `IsBallInPlay(0)` 反推。
            // `TableAtRest` **刻意留在这一侧**（不烘进字段）：它是另一个子系统的状态、且会随运动抖动，
            // 烘进去会让字段的语义变成"两条链的合取"；放在这里既能挡住"球还在滚就叫玩家点"的假提示，
            // 又不动字段本身的球生命周期语义。
            PoolSimulator sim = drive != null ? drive.Simulator : null;
            bool awaitingPlacement = phase == ShotPhase.Aim && sim != null
                                     && sim.TableAtRest && drive.PendingCueBallPlacement;

            // **轮到对手**（P6）：真值源 = `PhysicsTestDrive`；⚠ 与 `AiOpponentEnabled` 相与 ——
            // 练习模式规则同样会换手，但场上没有对手，界面必须保持"轮到你"。
            bool aiTurn = drive != null && drive.IsAiTurn;
            // ⚠ **练习模式没有对手**：规则换手会让上面为真，但场上没有对手 ⇒ 对外显示必须相与
            //   （否则 HUD 写"对手正在击球"、玩家还发不了球）。**保留上面整行字面**：`T146(h)` 按它守接线。
            aiTurn = aiTurn && drive != null && drive.AiOpponentEnabled;

            HudVisualState state = PracticeHudState.ResolveVisualState(
                phase, foulWindow, refusedWindow, ballInHand, PracticeResultView.IsOpen, awaitingPlacement, aiTurn);

            RefreshStatusBar(state, awaitingPlacement, placementRefusedWindow, opponentBlockedWindow);
            RefreshPower(phase);
            RefreshCounters();
            RefreshToolButtons();
        }

        private void RefreshStatusBar(HudVisualState state, bool awaitingPlacement, bool placementRefused,
                                      bool opponentBlocked)
        {
            Color32 symbol = PracticeHudState.SymbolColor(state);
            Color32 edge = PracticeHudState.EdgeColor(state);

            if (state == HudVisualState.Foul)
            {
                // 设计说明 §9：犯规是**0.8 秒克制脉冲**（从亮酒红衰减到稳定酒红），不是恒定色。
                // 脉冲混量是纯函数（`T125` 直接驱动），这里只负责喂时间。
                float blend = PracticeHudState.FoulPulseBlend(Time.unscaledTime - _foulStart);
                edge = Color32.Lerp(ClubUiTokens.FoulSymbol, ClubUiTokens.FoulEdge, blend);
            }

            if (_statusFrame != null)
            {
                _statusFrame.color = edge;
            }

            if (_statusFill != null)
            {
                _statusFill.color = PracticeHudState.FillColor(state);
            }

            if (_stateSymbol != null)
            {
                Sprite face = ClubUiSprites.Resolve(uiSprites, ClubUiSprites.ForState(state));
                if (face != null)
                {
                    _stateSymbol.sprite = face;
                }

                _stateSymbol.color = symbol;
            }

            if (_turnDot != null)
            {
                _turnDot.color = ClubUiTokens.Green;   // 设计稿 `#turn-dot` 恒为桌布绿（"你"的指示）
            }

            if (_stateText != null)
            {
                _stateText.text = PracticeHudState.StateTitle(state);
                _stateText.color = symbol;
            }

            if (_stateCaption != null)
            {
                // 说明行的**文案选择**一律走纯函数 `PracticeHudState.CaptionLine`（判据可直接驱动它）——
                // 本方法只负责"喂事实"，不自己判"哪句优先"：自由球有三个子情形（待放球 / 已回到台上 /
                // **落点被拒**）共用同一档状态与配色，优先级必须有个**可被判据钉住**的单一实现点。
                _stateCaption.text = PracticeHudState.CaptionLine(state, awaitingPlacement, placementRefused,
                    opponentBlocked);
            }

            if (_shotInfoValue != null)
            {
                _shotInfoValue.text = PracticeHudState.StateTitle(state);
            }
        }

        private void RefreshPower(ShotPhase phase)
        {
            float power = drive != null ? Mathf.Clamp01(drive.power) : 0f;

            if (_powerPercent != null)
            {
                _powerPercent.text = PracticeHudState.PowerLabel(power);
            }

            if (_powerFill != null)
            {
                _powerFill.fillAmount = power;
            }

            // 两端各留半个滑块，避免夹在轨道外侧（设计稿的 range thumb 同此行为）。
            float x = Mathf.Lerp(ThumbInsetFraction, 1f - ThumbInsetFraction, power);
            if (_thumb != null)
            {
                _thumb.anchorMin = new Vector2(x, 0.5f);
                _thumb.anchorMax = new Vector2(x, 0.5f);
            }

            if (_thumbGlow != null)
            {
                _thumbGlow.anchorMin = new Vector2(x, 0.5f);
                _thumbGlow.anchorMax = new Vector2(x, 0.5f);
            }

            bool canShoot = PracticeHudState.ShootEnabled(phase);

            if (_shootFrame != null)
            {
                _shootFrame.color = canShoot
                    ? ClubUiTokens.ButtonEdge
                    : ClubUiTokens.Alpha(ClubUiTokens.ButtonEdge, 0x61);
            }

            if (_shootFill != null)
            {
                _shootFill.color = canShoot
                    ? ClubUiTokens.ButtonFill
                    : ClubUiTokens.Alpha(ClubUiTokens.ButtonFill, 0x61);
            }

            if (_shootText != null)
            {
                _shootText.alpha = canShoot ? 1f : ClubUiTokens.DisabledOpacity;
            }

            if (_shootArrow != null)
            {
                _shootArrow.color = canShoot
                    ? ClubUiTokens.ButtonText
                    : ClubUiTokens.Alpha(ClubUiTokens.ButtonText, 0x61);
            }
        }

        private void RefreshCounters()
        {
            int shots = drive != null ? drive.LaunchCount : 0;
            int fouls = drive != null ? drive.ScratchCount : 0;

            if (_playerStats != null)
            {
                _playerStats.text = PracticeHudState.LabelShots + " " + shots
                                    + "   " + PracticeHudState.LabelFouls + " " + fouls;
            }

            if (drive == null || drive.Simulator == null)
            {
                return;
            }

            // 逐球清点走 `PracticeSession.CountBalls`（**唯一实现点**，铁律 16）——
            // 结算卡的三列与 HUD 的记分牌共用同一份口径，不存在两处各数一遍。
            PracticeSession.CountBalls(drive.Simulator, out int pocketed, out int totalObjects);

            if (_scoreValue != null && !HomeView.IsAiMatchSession)
            {
                _scoreValue.text = PracticeHudState.PocketedLabel(pocketed, totalObjects);
            }

            RefreshBallRow();
            RefreshGroupChips();
        }

        /// <summary>
        /// **对战 HUD 的球号行**：一侧一行，最多 `MatchGroupChipSlots` 个 chip，**默认全部隐藏**
        /// （开放球局 = 零 chip、零字形 —— 用户原话"一开始是空的"）。
        /// ⚠ 头像没有美术资源（`02_Characters_人物` 只有"不适用说明.md"）⇒ 复用既有
        ///   `UiSprite.Player` / `UiSprite.Robot` 精灵，**不新增材质**（材质 30/30 零余量）。
        /// </summary>
        private void BuildGroupChipRow(RectTransform board, string name, float left, float right, float bottom,
                                       out Image[] chips)
        {
            RectTransform row = AddRect(board, name, new Vector2(left, bottom), new Vector2(right, bottom + 0.14f));
            chips = new Image[PracticeHudState.MatchGroupChipSlots];
            for (int i = 0; i < chips.Length; i++)
            {
                float x = (i + 0.5f) / chips.Length;
                RectTransform chip = AddFixed(row, "Chip" + (i + 1), ClubUiSprites.UiSprite.Disc,
                    ClubBallChips.PaletteColor(1), new Vector2(x, 0.5f),
                    new Vector2(ClubUiTokens.MiniBallSize, ClubUiTokens.MiniBallSize));
                chips[i] = chip.GetComponent<Image>();
                chips[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 按**模式**与**两侧剩余球号**刷两行 chip；练习形态则还原原来的 15 颗球行。
        /// 单一真源：模式只读 `HomeView.IsAiMatchSession`；球号只来自
        /// `PhysicsTestDrive.PlayerRemainingNumbers` / `OpponentRemainingNumbers`（**不在 UI 里再记一份**）。
        /// </summary>
        /// <summary>
        /// **对战 HUD 的应用口**（判据 `T144(g)` 直接调用它 —— 与运行时刷新走**同一条路径**，
        /// 否则"进一颗消一颗"只能被纯函数层断言，注入打不中）。
        /// `aiMatch=false` ⇒ 还原练习形态（15 颗球行 + `BALLS LEFT` 可见、两侧球号全隐藏）。
        /// </summary>
        /// <summary>**判据用**：某一侧"当前可见"的球号（隐藏槽位不入列）。</summary>
        public int[] VisibleGroupChips(bool yours)
        {
            int[] cache = yours ? _yourShownNumbers : _oppShownNumbers;
            if (cache == null)
            {
                return new int[0];
            }

            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < cache.Length; i++)
            {
                // ⚠ **必须同时看 `activeSelf`**：只看缓存会让"注入了不刷新"照样读到新号码
                //   （判据就变成"测缓存"而不是"测显示"）。
                Image[] chips = yours ? _yourGroupChips : _oppGroupChips;
                if (cache[i] >= 0 && chips != null && chips[i] != null && chips[i].gameObject.activeSelf)
                {
                    list.Add(cache[i]);
                }
            }

            return list.ToArray();
        }

        /// <summary>
        /// **判据用**：某一侧**当前真正可见**的 chip 个数 —— **只数 `activeSelf`，不读任何缓存**。
        ///
        /// ⚠ 这是 P11 注入 `p11-hud-stale` 暴露出的教训：原读取器同时看"末次写入的号码（cache）"
        ///   与 `activeSelf`，而两者**由同一条刷新路径更新** ⇒ 注入"不刷新"时**两者一起失效**，
        ///   判据看不到脏状态（"载体被同源污染"的变体）。⇒ 判据必须读**视图上真实的东西**，
        ///   并且**不许为此再存第二份状态**。
        /// </summary>
        public int VisibleChipCount(bool yours)
        {
            Image[] chips = yours ? _yourGroupChips : _oppGroupChips;
            if (chips == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i] != null && chips[i].gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>**判据用**：记分牌（顶部条）根节点 —— 判据按子节点名读各块 `rect` 做重叠检测。</summary>
        public RectTransform ScoreboardForJudge => _scoreboard;

        /// <summary>**判据用**：玩家侧组别小标签的当前文本（`Solids`/`Stripes`/空串）。</summary>
        public string YourGroupTagText => _yourGroupLabel != null ? _yourGroupLabel.text : null;

        /// <summary>**判据用**：对手侧组别小标签的当前文本。</summary>
        public string OppGroupTagText => _oppGroupLabel != null ? _oppGroupLabel.text : null;

        /// <summary>**判据用**：中栏大字（`YOUR SHOT` / `AI`）。</summary>
        public string CenterTurnText => _scoreValue != null ? _scoreValue.text : null;

        /// <summary>**判据用**：中栏小字（`RACK 1`）。</summary>
        public string CenterRackText => _scoreLabel != null ? _scoreLabel.text : null;

        /// <summary>**判据用**：练习形态那排（`BALLS LEFT` + 15 颗球）此刻是否可见。</summary>
        public bool PracticeBallRowVisible => _ballsLabel != null && _ballsLabel.gameObject.activeSelf;

        public void ApplyMatchHud(bool aiMatch, GroupState yourGroup, GroupState oppGroup, bool aiTurn,
                                  IReadOnlyList<int> yourNumbers, IReadOnlyList<int> oppNumbers)
        {
            // 组别小标签：**开放球局是空串**（零字形，不写 "Open"/"?"/占位）。
            if (_yourGroupLabel != null)
            {
                _yourGroupLabel.text = aiMatch ? PracticeHudState.GroupTag(yourGroup) : string.Empty;
            }

            if (_oppGroupLabel != null)
            {
                _oppGroupLabel.text = aiMatch ? PracticeHudState.GroupTag(oppGroup) : string.Empty;
            }

            if (_oppName != null)
            {
                _oppName.gameObject.SetActive(aiMatch);
            }

            // 中栏：小字 `RACK 1`（单局，不写 of 3）、大字 = **当前该谁打**；退回练习形态时恢复 `POCKETED`。
            if (_scoreLabel != null)
            {
                _scoreLabel.text = aiMatch ? PracticeHudState.RackLabel : PracticeHudState.LabelPocketed;
            }

            if (_scoreValue != null && aiMatch)
            {
                _scoreValue.text = aiTurn ? PracticeHudState.OpponentTurnLabel : PracticeHudState.YourShotLabel;
            }

            // 品牌小标随模式切换（对战写 `AI Match`、练习写 `PRACTICE`）。
            if (_brandEyebrow != null)
            {
                _brandEyebrow.text = aiMatch ? PracticeHudState.ModeAiMatch : PracticeHudState.BrandEyebrow;
            }

            // 对战状态行：状态条整块隐藏后，`Foul` / `Ball in hand` / `Opponent` 的**唯一出口**。
            // `Aim` 是常态，不占版面（否则又会变成"中栏挂一行没用的字"）。
            // 重置按钮**只在练习模式出现**：对战中重摆等于把对局清掉。
            if (_resetIcon != null)
            {
                _resetIcon.gameObject.SetActive(!aiMatch);
            }

            if (_matchStatus != null)
            {
                string status = _shotInfoValue != null ? _shotInfoValue.text : string.Empty;
                bool showStatus = aiMatch && !string.IsNullOrEmpty(status) && status != PracticeHudState.StateAim;
                _matchStatus.gameObject.SetActive(showStatus);
                _matchStatus.text = showStatus ? status : string.Empty;
            }

            // 中栏在**对战形态要显示**：`RACK 1`（小字）+ `YOUR SHOT`/`AI`（大字）。
            // ⚠ 记分牌宽度**按模式切换**：对战 0.690（三段横排），练习 0.280（原三栏布局）。
            //   练习态若也用宽版，中栏会**正好压在状态条上**（用户实机截图：`POCKETED 5/15` 与
            //   `Clear the colors, then the 8 ball` 叠字）。
            if (_boardZone != null)
            {
                _boardZone.anchorMax = new Vector2(aiMatch ? 0.690f : 0.300f, _boardZone.anchorMax.y);
            }            // ⚠ 早前"对战隐藏中栏、把宽度让给两侧"的口径已被参考稿覆盖 —— 用户实机截图要求中栏有轮次信息，
            //   且实测"中栏空着 + 状态条还在"会被当成排版错误。
            if (_scoreLabel != null)
            {
                _scoreLabel.gameObject.SetActive(true);
            }

            if (_scoreValue != null)
            {
                _scoreValue.gameObject.SetActive(true);
            }

            if (_goalText != null)
            {
                _goalText.gameObject.SetActive(!aiMatch);
            }

            if (_playerStats != null)
            {
                // 练习统计（SHOTS/FOULS）在对战形态**必须整行隐藏** —— 否则会和组别标签、
                // 球号 chip 叠在同一块（用户实机截图：`You` 下面挤着 `Str…` + 一串圆点 + `Solids`）。
                _playerStats.gameObject.SetActive(!aiMatch);
            }

            // 对手头像只在対戦出现（左侧玩家头像一直在）。
            if (_oppAvatar != null)
            {
                _oppAvatar.gameObject.SetActive(aiMatch);
            }

            // **对战形态隐藏整条状态条**（`Aim` + `Clear the colors…` 那两行）——
            //   它属练习语义（`PracticeHudState.CaptionLine`），对战里显示就是假陈述。
            //   ⚠ 中栏的 `RACK 1` / `YOUR SHOT` **不动**（它们是对的）。
            if (_stateText != null)
            {
                _stateText.gameObject.SetActive(!aiMatch);
            }

            if (_stateCaption != null)
            {
                _stateCaption.gameObject.SetActive(!aiMatch);
            }

            // ⚠ 只藏文字会留下**一个空黑条**（底板 + 菱形点 + 交叉准星仍在）⇒ 必须整块隐藏。
            if (_statusFrame != null)
            {
                _statusFrame.gameObject.SetActive(!aiMatch);
            }

            if (_ballsLabel != null)
            {
                _ballsLabel.gameObject.SetActive(!aiMatch);
            }

            // `Shoot` 下方那两行（`YOUR SHOT` / 状态）在对战形态隐藏：中栏已经表达轮次，
            // 重复一遍会压在 `Shoot` 按钮上（用户实机截图：`Ball in hand` 叠着按钮）。
            if (_shotInfoLabel != null)
            {
                _shotInfoLabel.gameObject.SetActive(!aiMatch);
            }

            if (_shotInfoValue != null)
            {
                _shotInfoValue.gameObject.SetActive(!aiMatch);
            }

            if (_ballChips != null)
            {
                for (int i = 0; i < _ballChips.Length; i++)
                {
                    if (_ballChips[i] != null)
                    {
                        _ballChips[i].gameObject.SetActive(!aiMatch);
                    }
                }
            }

            ApplyGroupChips(_yourGroupChips, _yourShownNumbers, aiMatch ? yourNumbers : null);
            ApplyGroupChips(_oppGroupChips, _oppShownNumbers, aiMatch ? oppNumbers : null);
        }

        private void RefreshGroupChips()
        {
            bool ai = HomeView.IsAiMatchSession;
            ApplyMatchHud(ai,
                drive != null ? drive.PlayerGroup : GroupState.OpenTable,
                drive != null ? drive.OpponentGroup : GroupState.OpenTable,
                // **练习模式没有对手**：`IsAiTurn` 会因规则换手变真，但界面上不能写"对手正在击球"。
                drive != null && drive.IsAiTurn && drive.AiOpponentEnabled,
                drive != null ? drive.PlayerRemainingNumbers : null,
                drive != null ? drive.OpponentRemainingNumbers : null);
        }

        private void ApplyGroupChips(Image[] chips, int[] cache, IReadOnlyList<int> numbers)
        {
            if (chips == null)
            {
                return;
            }

            int[] shown = PracticeHudState.GroupChipNumbers(numbers, chips.Length);
            for (int i = 0; i < chips.Length; i++)
            {
                if (chips[i] == null)
                {
                    continue;
                }

                int number = shown[i];
                chips[i].gameObject.SetActive(number >= 0);
                if (number < 0)
                {
                    continue;
                }

                Sprite face = ClubBallChips.Resolve(ballChips, number);
                chips[i].sprite = face != null ? face : ClubUiSprites.Resolve(uiSprites, ClubUiSprites.UiSprite.Disc);
                chips[i].color = face != null ? ClubUiTokens.Opaque : ClubBallChips.PaletteColor(number);
            }
            if (cache != null)
            {
                for (int c = 0; c < cache.Length; c++)
                {
                    cache[c] = c < shown.Length ? shown[c] : -1;
                }
            }

        }

        /// <summary>
        /// 球行：把**已进**的球压到设计稿的 `opacity .2`，未进的保持原样。
        /// 这是"还剩哪些球"的**唯一**视觉编码 —— 不再另写一份文字计数重复表达同一件事。
        /// </summary>
        private void RefreshBallRow()
        {
            if (_ballChips == null || drive == null || drive.Simulator == null)
            {
                return;
            }

            for (int i = 0; i < _ballChips.Length; i++)
            {
                if (_ballChips[i] == null)
                {
                    continue;
                }

                bool inPlay = drive.Simulator.IsBallInPlay(i + 1);   // 索引 0 是白球 ⇒ 目标球从 1 起
                float alpha = inPlay ? 1f : ClubUiTokens.PottedBallOpacity;
                Color current = _ballChips[i].color;
                _ballChips[i].color = ClubUiTokens.WithAlpha(_ballChips[i].color, alpha);   // 只改 alpha，保留贴片本色
            }
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

        // ════════════════ 开关回调 ═══════════════════════════════════════════

        /// <summary>VFX 开关：写宿主上的既有 bool，不新建偏好状态（偏好持久化属 35-B 的设置页）。</summary>
        public void OnToggleVfx()
        {
            if (vfxView != null)
            {
                vfxView.vfxEnabled = !vfxView.vfxEnabled;
            }
        }

        /// <summary>SFX 开关：同上。</summary>
        public void OnToggleSfx()
        {
            if (poolAudio != null)
            {
                poolAudio.audioEnabled = !poolAudio.audioEnabled;
            }
        }

        // ════════════════ 设置面板（返回俱乐部 / 退出 / 音量）═══════════════════

        /// <summary>打开设置面板。**两个模式共用**（不挂在"仅对战"分支）。</summary>
        public void OnOpenSettings()
        {
            EnsureBuilt();
            EnsureSettingsPanel();
            if (_settingsPanel != null)
            {
                _settingsPanel.gameObject.SetActive(true);
            }

            IsSettingsOpen = true;
            RefreshVolumeText();
        }

        /// <summary>关闭设置面板（点背板、`Close`、或做完动作后都走这里）。</summary>
        public void CloseSettings()
        {
            if (_settingsPanel != null)
            {
                _settingsPanel.gameObject.SetActive(false);
            }

            IsSettingsOpen = false;
        }

        // ════════════════ 操作引导（首次进入弹一次）═══════════════════════════════

        /// <summary>若从未看过引导则弹一次（`PlayerPrefs` 存档键带版本号）。</summary>
        public void ShowOnboardingIfFirstTime()
        {
            if (PlayerPrefs.GetInt(PracticeHudState.OnboardingSeenKey, 0) != 0)
            {
                return;
            }

            ShowOnboarding();
        }

        /// <summary>打开引导（首页的 `How to play` 也走这里）。</summary>
        public void ShowOnboarding()
        {
            EnsureBuilt();
            EnsureOnboardingPanel();
            if (_onboardingPanel == null)
            {
                return;
            }

            _onboardingStep = 0;
            RefreshOnboardingStep();
            _onboardingPanel.gameObject.SetActive(true);
            IsOnboardingOpen = true;
        }

        /// <summary>`Next` / `Got it`：最后一步则关闭并记档。</summary>
        public void AdvanceOnboarding()
        {
            PlayUiClick();
            _onboardingStep++;
            if (_onboardingStep >= PracticeHudState.OnboardingStepCount)
            {
                CloseOnboarding(markSeen: true);
                return;
            }

            RefreshOnboardingStep();
        }

        /// <summary>关闭引导（`markSeen` = 是否记"已看过"；点背板中途关掉也记，避免每次进来都烦）。</summary>
        public void CloseOnboarding(bool markSeen)
        {
            if (_onboardingPanel != null)
            {
                _onboardingPanel.gameObject.SetActive(false);
            }

            IsOnboardingOpen = false;
            if (markSeen)
            {
                PlayerPrefs.SetInt(PracticeHudState.OnboardingSeenKey, 1);
                PlayerPrefs.Save();
            }
        }

        /// <summary>把当前步的文案/按钮标签刷到界面上（文案来自纯函数，判据同源）。</summary>
        public void RefreshOnboardingStep()
        {
            if (_onboardingTitle != null)
            {
                _onboardingTitle.text = PracticeHudState.OnboardingTitle(_onboardingStep);
            }

            if (_onboardingBody != null)
            {
                _onboardingBody.text = PracticeHudState.OnboardingBody(_onboardingStep);
            }

            if (_onboardingButtonLabel != null)
            {
                bool last = _onboardingStep >= PracticeHudState.OnboardingStepCount - 1;
                _onboardingButtonLabel.text = last
                    ? PracticeHudState.OnboardingDone
                    : PracticeHudState.OnboardingNext;
            }
        }

        /// <summary>懒建引导卡（背板 + 卡片 + 标题 + 正文 + 下一步）。复用既有面板与令牌，不新增材质。</summary>
        private void EnsureOnboardingPanel()
        {
            if (_onboardingPanel != null || _canvas == null)
            {
                return;
            }

            RectTransform root = AddRect(_canvas.transform, "OnboardingPanel", Vector2.zero, Vector2.one);
            _onboardingPanel = root;

            var backdrop = root.gameObject.AddComponent<Image>();
            SetImage(backdrop, null, ClubUiTokens.Alpha(ClubUiTokens.PanelShotbar, 0xC8));
            backdrop.raycastTarget = true;

            RectTransform card = AddFramed(root, "Card", new Vector2(0.28f, 0.30f), new Vector2(0.72f, 0.70f),
                ClubUiTokens.LineStrong, ClubUiTokens.PanelStatus, ClubUiTokens.RadiusDialog);

            _onboardingTitle = CreateText(card, "Title", titleFont, ClubUiTokens.FontBody, ClubUiTokens.Accent,
                TextAlignmentOptions.Center, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.94f));

            _onboardingBody = CreateText(card, "Body", bodyFont, ClubUiTokens.FontLabel, ClubUiTokens.Text,
                TextAlignmentOptions.Center, new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.76f));

            RectTransform next = AddFramed(card, "Next", new Vector2(0.30f, 0.12f), new Vector2(0.70f, 0.26f),
                ClubUiTokens.ButtonEdgeSecondary, ClubUiTokens.ButtonFill, ClubUiTokens.RadiusDialog);
            _onboardingButtonLabel = CreateText(next, "Label", bodyFont, ClubUiTokens.FontBody,
                ClubUiTokens.ButtonText, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);

            var nextImage = next.GetComponent<Image>();
            nextImage.raycastTarget = true;
            var nextButton = next.gameObject.AddComponent<Button>();
            nextButton.targetGraphic = nextImage;
            nextButton.onClick.AddListener(AdvanceOnboarding);

            _onboardingPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// **重置球桌**（练习模式）：重摆 15 颗、清自由球/待放置/组别/指定袋，回到开球前。
        /// 复用 `PhysicsTestDrive.ReloadRack()`（**唯一重置入口**，不在这里另写一套状态清理）。
        /// </summary>
        public void OnResetRack()
        {
            PlayUiClick();
            if (drive != null)
            {
                drive.ReloadRack();
            }
        }

        /// <summary>点击按钮的短音（唯一入口；`poolAudio` 为空时静默，不抛）。</summary>
        private void PlayUiClick()
        {
            // 场景里 `poolAudio` 可能没接线（本工程的 HUD 由生成器建树，历史上只接了 `drive`）
            // ⇒ 为空就现找一次并缓存；找不到就静默（不抛）。
            if (poolAudio == null)
            {
                poolAudio = Object.FindObjectOfType<PoolAudio>();
            }

            if (poolAudio != null)
            {
                poolAudio.PlayUiClick();
            }
        }

        /// <summary>`Back to club`：关面板 → **关掉 AI 对手** → 回首页。两个模式都能回。</summary>
        public void OnBackToClub()
        {
            CloseSettings();

            if (drive != null)
            {
                drive.EndAiMatchSession();
            }

            // 首页视图：优选用场景里接好的引用；没接就现找一次（只在点按钮时发生，不在每帧路径上）。
            HomeView target = home != null ? home : Object.FindObjectOfType<HomeView>();
            if (target != null)
            {
                target.Show();
            }
        }

        /// <summary>`Quit`：出包后生效。**编辑器内 `Application.Quit()` 是空操作** ⇒ 明确记一条日志，不留"点了没反应"的假象。</summary>
        public void OnQuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("设置 → Quit：编辑器内不生效（Application.Quit 在编辑器是空操作）；出包后可用。");
#else
            Application.Quit();
#endif
        }

        /// <summary>音量增减（每次 10%，夹到 [0,1]）—— 写**全局主音量**（SFX 与 BGM 一起受影响）。</summary>
        public void AdjustVolume(float delta)
        {
            AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
            RefreshVolumeText();
        }

        /// <summary>把音量数值同步到百分比文本（`NN%`）。</summary>
        public void RefreshVolumeText()
        {
            if (_volumeValueText != null)
            {
                _volumeValueText.text = Mathf.RoundToInt(AudioListener.volume * 100f) + "%";
            }

            if (_volumeFill != null)
            {
                Vector2 max = _volumeFill.anchorMax;
                max.x = Mathf.Clamp01(AudioListener.volume);
                _volumeFill.anchorMax = max;
            }
        }

        /// <summary>懒建：全屏背板（点击关闭）+ 居中卡片 + 标题 + 三行 + `Close`。</summary>
        private void EnsureSettingsPanel()
        {
            if (_settingsPanel != null || _canvas == null)
            {
                return;
            }

            RectTransform root = AddRect(_canvas.transform, "SettingsPanel", Vector2.zero, Vector2.one);
            _settingsPanel = root;

            // 背板：半透明黑（复用既有面板色，不新增材质）
            var backdrop = root.gameObject.AddComponent<Image>();
            // 背板压暗（比卡片更深）⇒ 焦点落在设置卡上。
            SetImage(backdrop, null, ClubUiTokens.Alpha(ClubUiTokens.PanelShotbar, 0xC8));
            backdrop.raycastTarget = true;
            var backdropButton = root.gameObject.AddComponent<Button>();
            backdropButton.targetGraphic = backdrop;
            backdropButton.onClick.AddListener(CloseSettings);

            RectTransform card = AddFramed(root, "Card", new Vector2(0.345f, 0.27f), new Vector2(0.655f, 0.73f),
                ClubUiTokens.LineStrong, ClubUiTokens.PanelStatus, ClubUiTokens.RadiusDialog);

            CreateText(card, "Title", titleFont, ClubUiTokens.FontBody, ClubUiTokens.Accent,
                TextAlignmentOptions.Center, new Vector2(0.06f, 0.84f), new Vector2(0.94f, 0.97f))
                .text = PracticeHudState.TitleSettings;

            RectTransform divider = AddRect(card, "Divider", new Vector2(0.06f, 0.815f), new Vector2(0.94f, 0.815f));
            divider.offsetMin = new Vector2(0f, -ClubUiTokens.Hairline);
            divider.offsetMax = Vector2.zero;
            SetImage(divider.gameObject.AddComponent<Image>(), null, ClubUiTokens.LineMid);

            SettingsButtonRow(card, "BackToClub", PracticeHudState.ButtonBackToClub, 0.655f, 0.765f, OnBackToClub);
            SettingsButtonRow(card, "Quit", PracticeHudState.ButtonQuit, 0.525f, 0.635f, OnQuitGame);

            // ── 音量：标签 + 可视轨道 + 百分比 + 两个圆角步进键 ────────────────
            CreateText(card, "VolumeLabel", bodyFont, ClubUiTokens.FontCaption, ClubUiTokens.Muted,
                TextAlignmentOptions.Left, new Vector2(0.06f, 0.16f), new Vector2(0.34f, 0.30f))
                .text = PracticeHudState.LabelVolume;

            RectTransform minus = AddFramed(card, "VolumeDown", new Vector2(0.395f, 0.175f), new Vector2(0.475f, 0.285f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusDialog);
            CreateText(minus, "Label", titleFont, ClubUiTokens.FontBody, ClubUiTokens.Text,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = "-";
            var minusImage = minus.GetComponent<Image>();
            minusImage.raycastTarget = true;
            var minusButton = minus.gameObject.AddComponent<Button>();
            minusButton.targetGraphic = minusImage;
            minusButton.onClick.AddListener(() => { PlayUiClick(); AdjustVolume(-0.1f); });
            ApplyButtonTint(minusButton);

            // 轨道（只做视觉，不接收射线）：底色 + 按当前音量伸长的亮色条。
            RectTransform rail = AddRect(card, "VolumeRail", new Vector2(0.50f, 0.205f), new Vector2(0.71f, 0.255f));
            SetImage(rail.gameObject.AddComponent<Image>(), null, ClubUiTokens.LineMid);
            _volumeFill = AddRect(rail, "Fill", Vector2.zero, Vector2.one);
            SetImage(_volumeFill.gameObject.AddComponent<Image>(), null, ClubUiTokens.Accent);

            _volumeValueText = CreateText(card, "VolumeValue", titleFont, ClubUiTokens.FontBody, ClubUiTokens.Text,
                TextAlignmentOptions.Center, new Vector2(0.725f, 0.17f), new Vector2(0.845f, 0.29f));

            RectTransform plus = AddFramed(card, "VolumeUp", new Vector2(0.855f, 0.175f), new Vector2(0.935f, 0.285f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusDialog);
            CreateText(plus, "Label", titleFont, ClubUiTokens.FontBody, ClubUiTokens.Text,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = "+";
            var plusImage = plus.GetComponent<Image>();
            plusImage.raycastTarget = true;
            var plusButton = plus.gameObject.AddComponent<Button>();
            plusButton.targetGraphic = plusImage;
            plusButton.onClick.AddListener(() => { PlayUiClick(); AdjustVolume(0.1f); });
            ApplyButtonTint(plusButton);

            // 右上 `Close`
            RectTransform close = AddFramed(card, "Close", new Vector2(0.895f, 0.875f), new Vector2(0.955f, 0.935f),
                ClubUiTokens.LineMid, ClubUiTokens.PanelIconButton, ClubUiTokens.RadiusSegment);
            CreateText(close, "Label", titleFont, ClubUiTokens.FontBody, ClubUiTokens.Muted,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = "X";
            var closeImage = close.GetComponent<Image>();
            closeImage.raycastTarget = true;
            var closeButton = close.gameObject.AddComponent<Button>();
            closeButton.targetGraphic = closeImage;
            closeButton.onClick.AddListener(() => { PlayUiClick(); CloseSettings(); });
            ApplyButtonTint(closeButton);

            RefreshVolumeText();
            _settingsPanel.gameObject.SetActive(false);
        }

        /// <summary>
        /// 卡片里的一行整宽按钮：**描边 + 大圆角 + 左对齐文案 + 右侧箭头 + 悬停/按下变色**（只用既有令牌）。
        /// ⚠ `SetImage` 统一把 `raycastTarget` 置 false ⇒ 可点控件的图片必须**显式打开**射线。
        /// </summary>
        private void SettingsButtonRow(RectTransform card, string name, string label, float yMin, float yMax,
                                       UnityEngine.Events.UnityAction action)
        {
            // 尺寸收窄收矮（用户："按键小一点"），文案**居中**（"字体居中"）。
            RectTransform row = AddFramed(card, name, new Vector2(0.16f, yMin), new Vector2(0.84f, yMax),
                ClubUiTokens.ButtonEdgeSecondary, ClubUiTokens.ButtonFill, ClubUiTokens.RadiusDialog);

            CreateText(row, "Label", bodyFont, ClubUiTokens.FontBody, ClubUiTokens.ButtonText,
                TextAlignmentOptions.Center, Vector2.zero, Vector2.one).text = label;

            var rowImage = row.GetComponent<Image>();
            rowImage.raycastTarget = true;

            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = rowImage;
            // 点击先出声，再执行动作（声音与动作同一处接线，不会出现"有的按钮有声音、有的没有"）。
            button.onClick.AddListener(() =>
            {
                PlayUiClick();
                action();
            });
            ApplyButtonTint(button);
        }

        /// <summary>
        /// 统一的按键反馈：**悬停变亮、按下变暗**（`ColorTint`）。
        /// 没有这一层，鼠标移上去毫无变化 —— 用户会以为按钮是死的。
        /// </summary>
        private static void ApplyButtonTint(Button button)
        {
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = ClubUiTokens.ButtonFillHover;
            colors.pressedColor = ClubUiTokens.ButtonFillPressed;
            colors.selectedColor = Color.white;
            colors.disabledColor = ClubUiTokens.ButtonFillDisabled;
            colors.fadeDuration = 0.08f;
            colors.colorMultiplier = 1f;
            button.colors = colors;
        }

        // ════════════════ 建树小工具（同一套几何口径，供结算卡复用）═══════════

        /// <summary>分区落地：**锚点 = 视口分区**（`offsetMin/Max = 0`）⇒ 与分辨率无关，且与判据同源。</summary>
        public static RectTransform CreateZone(Transform parent, Rect zone, string name)
        {
            RectTransform rect = AddRect(parent, name, new Vector2(zone.xMin, zone.yMin),
                new Vector2(zone.xMax, zone.yMax));
            return rect;
        }

        /// <summary>按**父级归一化锚点**建一个空的 `RectTransform` 容器。</summary>
        public static RectTransform AddRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>建一个**固定尺寸**的元素（头像 / 徽章 / 图标 / 迷你球）；`anchor` 即元素中心。</summary>
        public RectTransform AddFixed(Transform parent, string name, ClubUiSprites.UiSprite sprite,
                                      Color color, Vector2 anchor, Vector2 size)
        {
            RectTransform rect = AddRect(parent, name, anchor, anchor);
            rect.sizeDelta = size;
            SetImage(rect.gameObject.AddComponent<Image>(), sprite, color);
            return rect;
        }

        /// <summary>
        /// 建一块**带 1 px 细边线的圆角面板**：外层描边色 + 内层填充色
        /// （uGUI 的 `Image` 没有边框属性，双层是唯一能画出真细线的做法）。
        /// 圆角由 `radius` 经 <see cref="ClubUiSprites.PanelForRadius"/> 取九宫格位图
        /// ⇒ 设计令牌里的圆角数值与画出来的圆角**只有一个来源**。
        ///
        /// 内层命名为 `Fill` 并**置为不接收射线**：这样 `Button.targetGraphic` 用外层时
        /// 整块面板都是可点区域，且需要动态换底色时可以按名取到内层。
        /// </summary>
        public RectTransform AddFramed(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                                       Color edge, Color fill, float radius)
        {
            ClubUiSprites.UiSprite panel = ClubUiSprites.PanelForRadius(radius);

            RectTransform rect = AddRect(parent, name, anchorMin, anchorMax);
            SetImage(rect.gameObject.AddComponent<Image>(), panel, edge);

            RectTransform inner = AddRect(rect, "Fill", Vector2.zero, Vector2.one);
            inner.offsetMin = new Vector2(ClubUiTokens.Hairline, ClubUiTokens.Hairline);
            inner.offsetMax = new Vector2(-ClubUiTokens.Hairline, -ClubUiTokens.Hairline);
            SetImage(inner.gameObject.AddComponent<Image>(), panel, fill);
            return rect;
        }

        /// <summary>建一条**贴边的 1 px 横线**（设计稿只给上/下边线时用它，避免四边框的观感误差）。</summary>
        public RectTransform AddEdgeStrip(RectTransform parent, string name, bool top, Color color)
        {
            RectTransform rect = AddRect(parent, name,
                top ? new Vector2(0f, 1f) : new Vector2(0f, 0f),
                top ? new Vector2(1f, 1f) : new Vector2(1f, 0f));
            rect.offsetMin = top ? new Vector2(0f, -ClubUiTokens.Hairline) : new Vector2(0f, 0f);
            rect.offsetMax = top ? new Vector2(0f, 0f) : new Vector2(0f, ClubUiTokens.Hairline);

            SetImage(rect.gameObject.AddComponent<Image>(), null, color);
            return rect;
        }

        /// <summary>
        /// 把一个 `Image` 按槽位口径配置好。
        /// **精灵取不到时保持 `null`**（uGUI 会画一块白方块，而白方块比缺图更糟 ⇒ 调用方必须自己判）。
        /// </summary>
        public void SetImage(Image image, ClubUiSprites.UiSprite? sprite, Color color)
        {
            image.color = color;
            image.raycastTarget = false;

            if (sprite == null)
            {
                return;
            }

            Sprite face = ClubUiSprites.Resolve(uiSprites, sprite.Value);
            if (face == null)
            {
                return;
            }

            image.sprite = face;
            image.type = ClubUiSprites.IsSliced(sprite.Value) ? Image.Type.Sliced : Image.Type.Simple;
        }

        /// <summary>
        /// 建文本。`font` 为空时（例如未接线的测试探针）**仍然建出对象** ——
        /// 结构判据要能在没有字体资产的情况下跑，不能因为"字体没接"就整棵树建不出来。
        ///
        /// ⚠ **字号在这里乘全局倍率**（`PracticeHudLayout.FontScale`）—— 本方法是**全 UI 唯一**写
        /// `text.fontSize` 的地方（HUD / 首页 / 赛前屏 / 结算卡 / 选袋条全部经它）⇒ 一个乘号 = 整体放大。
        /// ⚠ **只准乘这一次**（判据 `T148(d)` 数 `* PracticeHudLayout.FontScale` 的出现次数）：
        /// 乘两次会变成 1.9² ≈ 3.61（1.6 时代是 2.56），界面会当场崩掉版式。
        /// ⚠ 注释里出现这个乘积**不算**（判据读的是 `StripComments` 之后的文本）；启动日志那两处也**必须**走
        /// `RenderedFontPixels`，不许在视图里自己再乘一遍倍率（实测：那样 `T148(d)` 会数到 3）。
        /// </summary>
        /// <param name="applyGlobalScale">
        /// **是否吃全局字号倍率**（默认 `true`）。**首页（开始屏）传 `false`** ——
        /// 用户原话：「**我没说改开始主页字体，现在字体排版都乱了**」⇒ 首页保持**原字号**，
        /// 只是**不再被 `FontScale` 带歪**（首页的 rect 本来就一个都没动过，乱的唯一原因是字被放大了）。
        /// ⚠ 赛前屏（`AddIntroText` / `BuildIntroTierSegment`）**仍然吃倍率** —— 那是打球流程里的屏，不是"开始主页"。
        /// </param>
        public static TextMeshProUGUI CreateText(Transform parent, string name, TMP_FontAsset font, float size,
                                                Color color, TextAlignmentOptions alignment,
                                                Vector2 anchorMin, Vector2 anchorMax,
                                                bool applyGlobalScale = true)
        {
            RectTransform rect = AddRect(parent, name, anchorMin, anchorMax);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size * (applyGlobalScale ? PracticeHudLayout.FontScale : 1f);
            text.color = color;
            text.alignment = alignment;
            text.characterSpacing = 0f;   // 设计说明 §一：字距 0
            text.raycastTarget = false;
            return text;
        }
    }
}
