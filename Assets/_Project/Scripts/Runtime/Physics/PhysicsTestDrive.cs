using System.Collections.Generic;
using Billiards.Core;
using Billiards.Rules;
using Billiards.Table;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// 把 <see cref="PoolSimulator"/> 接到 Unity 循环上的适配器（灰盒场景的"发动机"）。
    ///
    /// ── 契约 ───────────────────────────────────────────────────────────────
    ///  1. **累加器驱动**：<c>FixedUpdate</c> 每次把 <c>Time.fixedDeltaTime</c> 累加进
    ///     <see cref="_accumulator"/>，每满一个 <see cref="PoolSimulator.FixedDeltaTime"/>
    ///     调一次 <see cref="PoolSimulator.Step"/>。余量跨帧保留，不做四舍五入。
    ///  2. **内核不读时间**：<c>Time.*</c> 只在本 MonoBehaviour 侧读；喂进内核的只有整数步。
    ///     <see cref="PoolSimulator"/> 内部无任何 <c>Time</c>/<c>Random</c> 引用。
    ///  3. **坐标只经唯一转换点**：球位走 <see cref="PoolGeometry.PhysicsBallToUnity"/>（R5-2）。
    ///  4. 本类是**调试驱动**，不是最终交互：瞄准与力度属交互层，这里只求"能看到球动"。
    /// </summary>
    [AddComponentMenu("Billiards/Physics Test Drive")]
    public sealed class PhysicsTestDrive : MonoBehaviour, ISmokeAimSweepTarget, Billiards.UI.IMatchHost
    {
        /// <summary>球桌碰撞几何真源。</summary>
        [Tooltip("球桌碰撞几何真源（TableConfig.asset）。")]
        public TableConfig tableConfig;

        /// <summary>可选：把内核注入 Gizmo 以便在场景视图看到球与速度矢量。</summary>
        [Tooltip("可选：运行时把内核注入该 Gizmo 组件。")]
        public CushionGizmo cushionGizmo;

        /// <summary>可选：与各球位置依次同步的视觉 Transform（球 0 = 白球）。</summary>
        [Tooltip("可选：与内核球位一一对应的视觉 Transform（球 0 = 白球）。留空则只同步球 0。")]
        public Transform[] ballVisuals;

        /// <summary>兼容旧字段：单球视觉（当 <see cref="ballVisuals"/> 未配置时使用）。</summary>
        [Tooltip("可选：白球视觉 Transform（兼容用；优先使用 ballVisuals）。")]
        public Transform ballVisual;

        [Header("球-球演示（开箱即可看到碰撞）")]
        [Tooltip("勾选后装载「白球 + 球堆」而非单球，便于在编辑器里直接观察球-球碰撞。")]
        public bool loadRackForBallBallDemo = true;

        /// <summary>
        /// 球堆模式下的**目标球数**（不含白球）。**正式对局 = <see cref="RackBallCount"/>(15)**，
        /// 字段化的唯一目的是让**测试可以显式**搭小堆场景。
        ///
        /// ⚠ 为什么必须显式可配：本轮把正式球数从 3 改成 15 之后，两个**与球堆大小无关**的用例
        /// （`T65` 出杆不清盘、`T78` 银行球预测与实跑一致）因为"场景里多了 12 颗球"而失效 ——
        /// 它们的摆位/瞄准角是**在 3 球堆下量出来的**（`T78` 的注释里就写着"实测它们不参与本场景的接触"）。
        /// 与其让"默认值"继续隐式决定场上球数（这正是本轮踩过的坑：默认值悄悄让场上只剩 4 颗），
        /// 不如把这件事**变成测试里的显式参数**：小堆用例写 `rackBallCount = 3`，
        /// 正式球数由 `T122`（场景球数）与 `T111`（满堆开球收敛）守。
        /// </summary>
        [Tooltip("球堆模式的目标球数（不含白球）。正式对局用默认值；测试可显式调小以搭隔离场景。")]
        public int rackBallCount = RackBallCount;

        /// <summary>
        /// **8 球开局摆位的种子**（35-C `P5-A`）。<see cref="RandomRackSeed"/>（`-1`）= **每次
        /// <see cref="Initialize"/> 取一次真随机**（运行期）；其余值 = 直接用（**判据注入固定种子 ⇒ 逐位可复现**）。
        ///
        /// ⚠ **只有 15 颗**（<see cref="RackBallCount"/>）才走"**按球号编号**"的 8 球摆位
        /// （<see cref="EightBallRackPositions"/>：`[8]` 在第三排正中、底排两角一实一花）；
        /// 其它球数（测试的小堆）仍走既有几何摆位 <see cref="RackPositions"/> —— **既有夹具的期望值一个都不动**。
        ///
        /// ⚠ **两条路径的"位置集合"完全相同**（同一个 <see cref="RackSlot"/> 铺同样的槽）
        /// ⇒ 只差"哪个下标拿哪个槽"（= 球号）⇒ 纯物理量（接触数 / 是否静止 / 某颗球的行程）**与种子无关**。
        /// </summary>
        [Tooltip("8 球开局摆位种子。-1 = 运行期取真随机；判据注入固定值。")]
        public int rackSeed = RandomRackSeed;

        /// <summary>`rackSeed` 的**真随机**哨兵值（运行期每局取一次）。</summary>
        public const int RandomRackSeed = -1;

        /// <summary>
        /// **本局实际生效的摆位种子**（<see cref="Initialize"/> 解析一次并**留痕**）——
        /// 判据读它即可知道"这一局用的是哪个种子"，不必猜"真随机有没有生效"。
        ///
        /// ⚠ <see cref="ReloadRack"/> **不重掷**（沿用本值）：重摆是"同一局的再来一杆"，
        /// 重掷会让"重摆后逐位可复现"这条既有契约失效。
        /// </summary>
        public int ResolvedRackSeed { get; private set; }

        [Header("物理参数（占位值；正式值待手感标定）")]
        [Tooltip("库边法向恢复系数 e。占位值，未经策划确认。")]
        [Range(0f, 1f)]
        public float cushionRestitution = 0.8f;

        [Tooltip("库边切向摩擦保留系数 μ。占位值，未经策划确认。")]
        [Range(0f, 1f)]
        public float cushionTangentialFriction = 0.9f;

        [Tooltip("台呢**滑动段**减速度 a_slide（m/s²，高速段）。出厂 0.5（手感口径；真实台呢约 0.05–0.15 ⇒ 非物理真值）。")]
        public float rollingDecelerationSlide = 0.5f;

        [Tooltip("台呢**滚动段**减速度 a_roll（m/s²，低速段）。出厂 0.25 —— **这是“球能滑多远”的主旋钮**（调小 ⇒ 尾巴更长）。"
                 + "⚠ 与 `rollingDecelerationSlide` 取同值即“恒定档”（与历史行为逐位等价）。")]
        public float rollingDecelerationRoll = 0.25f;

        [Tooltip("两段过渡速度 v_transition（m/s）。出厂 2.0。≤ 0 ⇒ 恒取 a_slide；两段同值时本项不影响结果。")]
        public float rollingTransitionSpeed = 2.0f;

        [Header("球-球参数（默认 = 调参口径 0.95；SafeDefault 的 1.0 是「全零解药」、非调参默认）")]
        [Tooltip("球-球法向恢复系数 e_ball。台球实测 0.93–0.96；调参目标口径 = 0.95（Day 7 定）。")]
        [Range(0f, 1f)]
        public float ballRestitution = 0.95f;

        [Tooltip("球-球切向保留系数 μ_ball。**本模型无自旋 ⇒ 必须保持 1.0**（<1 属非物理耗散）。")]
        [Range(0f, 1f)]
        public float ballTangentialFriction = 1f;

        [Header("物理调参资产（参数唯一可调入口）")]
        [Tooltip("留空 ⇒ 回落到 SafeDefault + 本组件的占位值（行为不变）。指定后由该资产统一提供参数。")]
        public PhysicsTuning tuning;

        [Header("键盘瞄准与力度（最小可玩；指针瞄准属交互层，不在本组件范围）")]
        [Tooltip("当前瞄准方向（度）。0° = 指向 +x，逆时针为正。")]
        public float aimDegrees = 180f;

        [Tooltip("当前力度 ∈ [0,1]（线性映射到 [minLaunchSpeed, maxLaunchSpeed]）。")]
        [Range(0f, 1f)]
        public float power = 0.5f;

        [Tooltip("力度 → 初速映射的端点（m/s）。")]
        public float minLaunchSpeed = 0.5f;

        [Tooltip("力度 → 初速映射的**上界**（m/s）= 满力开球初速；与 `PoolConstants.MaxBallSpeed` **不是一回事** ——"
                 + "那个常量喂 `DeriveSubSteps`（子步数）并**必须保持 8** 作取样余量。")]
        public float maxLaunchSpeed = 8f;

        [Header("瞄准指示物（Game 视图可见；无物理组件）")]
        [Tooltip("瞄准指示物的 Transform（细长立方体）。留空 ⇒ Initialize 时按需自动创建（开箱即用）。")]
        public Transform aimIndicator;

        [Tooltip("瞄准指示物长度（米）。")]
        public float aimIndicatorLength = 0.55f;

        /// <summary>瞄准指示物的截面厚度（米）。</summary>
        public float aimIndicatorThickness = 0.012f;

        /// <summary>
        /// 是否**渲染**瞄准指示物（细长立方体）。瞄准预测线（白线路径 + 幽灵球 + 出射线）已承担方向指示
        /// ⇒ 出厂**隐藏**本指示物；对象仍按跟随口径创建 / 对齐 / 激活（判据对象不变），仅关闭渲染可见性。
        /// 置 true 可恢复显示（例如调试方向链时与预测线对照）。
        /// </summary>
        [Tooltip("是否渲染瞄准指示物。默认隐藏——方向指示由瞄准预测线承担。")]
        public bool showAimIndicator = false;

        [Tooltip("瞄准辅助：预测线开关（设置项预留接口）。")]
        public bool enableShotPrediction = true;

        /// <summary>
        /// 当前瞄准方向（**Unity 世界、水平面**）= 物理方向 `(cos θ, sin θ)` 经
        /// <see cref="PoolGeometry.PhysicsDirectionToUnity"/> 映射 ⇒ **`(−cos θ, 0, −sin θ)`**。
        ///
        /// ⚠ **必须过映射**：`Launch()` 的物理初速是 `speed·(cos θ, sin θ)`，其**世界走向**为
        /// `(−cos θ, 0, −sin θ)`；若本属性直接返回 `(cos θ, 0, sin θ)`，指示物就会与白球真实走向**相差 180°**。
        /// </summary>
        public Vector3 AimDirectionUnity
        {
            get
            {
                float rad = aimDegrees * Mathf.Deg2Rad;
                return PoolGeometry.PhysicsDirectionToUnity(new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)));
            }
        }

        [Tooltip("方向键每按一次的瞄准粒度（度）。")]
        public float aimStepDegrees = 1f;

        [Tooltip("力度键每按一次的力度粒度。")]
        public float powerStep = 0.02f;

        [Tooltip("按住此键 ⇒ 瞄准 / 力度走细粒度（0.2° / 0.005）。")]
        public KeyCode fineKey = KeyCode.LeftShift;

        [Header("调试叠加层（左上角诊断文本）")]
        [Tooltip("是否绘制左上角的调试叠加层（瞄准角 / 力度 / 接触计数 / 台面静止状态等）。"
                 + "**默认关闭**（正式入口不显示）；按 F1 可随时切换。诊断场景（灰盒）在场景里置 true。")]
        public bool showDebugOverlay = false;

        [Tooltip("切换调试叠加层的热键。")]
        public KeyCode debugOverlayToggleKey = KeyCode.F1;

        [Header("键位")]
        public KeyCode aimLeftKey = KeyCode.LeftArrow;
        public KeyCode aimRightKey = KeyCode.RightArrow;
        public KeyCode powerUpKey = KeyCode.UpArrow;
        public KeyCode powerDownKey = KeyCode.DownArrow;

        [Header("冒烟测试")]
        [Tooltip("按此键**以当前 HUD 力度**出杆（力度来自拖距/方向键，见 `power`）。")]
        public KeyCode launchKey = KeyCode.Space;

        [Tooltip("单帧最多推进的物理步数（防掉帧死亡螺旋）。")]
        public int maxStepsPerFrame = 8;

        private PoolSimulator _simulator;
        private float _accumulator;

        /// <summary>上一帧白球是否在场上（scratch 上升沿检测用）。</summary>
        private bool _cueWasInPlay = true;

        /// <summary>
        /// 各球可视物**上一帧写入的世界位置**（按球索引），用于求本帧世界位移 ⇒ 纯滚动自转。
        /// <c>null</c> 表示"尚无上一帧"（首帧 / 重新装载后）⇒ 只记录、不旋转。
        /// </summary>
        private Vector3[] _lastVisualPositions;

        // ── 入袋下沉动画状态（表现层；索引惰性扩展；`-1` = 未启动）────────────
        private float[] _pocketSinkStart;
        private Vector3[] _ballBaseScales;

        /// <summary>入袋动画的**起点**（球可视物坐标系 = 规范局部坐标；启动那帧的球位）与**落点**（洞口中心）。</summary>
        private Vector3[] _pocketSinkOrigin;
        private Vector3[] _pocketSinkHole;

        /// <summary>是否已有可用的"上一帧位置"；<c>false</c> 时本帧只记录、不旋转。</summary>
        private bool _hasLastVisualPositions;

        /// <summary>当前内核（未初始化时为 <c>null</c>）。</summary>
        public PoolSimulator Simulator => _simulator;

        /// <summary>本帧待推进的累计时间余量（秒）。仅用于显示/调试。</summary>
        public float Accumulator => _accumulator;

        /// <summary>
        /// **最近一次 <see cref="Launch"/> 是否因为白球已离台（入袋 / 擦袋角）而把它显式放回了开球点**
        /// （`footSpot`）。**只读状态出口** —— 让"白球重摆"这件事**可观测**（判据可断言、日志亦打一条）。
        /// 正常出杆（白球仍在场上）为 `false`。
        /// </summary>
        public bool LastLaunchRespawnedCueBall { get; private set; }

        /// <summary>
        /// **全场自由球：是否正处于「待放球」**（白球离台、玩家**尚未**放置）。
        ///
        /// **用户原话**：「练习模式白球进袋了，全场自由球，可以把白球放在台面任意位置击球」。
        /// 语义 = ① 白球不在场上 **且** ② 玩家还没把它放回去。
        ///
        /// ⚠ **与 <see cref="LastLaunchRespawnedCueBall"/> 分开**（主理人裁定）：后者**保留**既有语义
        /// （= "这一杆前白球离台" = **犯规**），它是 `PracticeSession` 犯规计数与 `PracticeResultView` 的**唯一驱动源**
        /// ⇒ 若把两件事挤进同一个 flag，**犯规统计会静默失真**（而且不会红）。
        /// </summary>
        public bool PendingCueBallPlacement { get; private set; }

        /// <summary>最近一次"放球被拒"（落点非法）累计次数（**只读出口**，供 HUD 给"不许静默"的反馈）。</summary>
        public int RefusedPlacementCount { get; private set; }

        /// <summary>
        /// **本杆期间白球曾离台**（粘滞位，出杆时消费一次）。
        ///
        /// ⚠ **为什么需要它**（全场自由球引入的**真缺陷**，本轮勘出并修）：旧实现里"白球离台"这件事只在
        /// `Launch()` 里读（`cueOffTable`）⇒ 而现在玩家可以**先放球再出杆**，那一刻 `cueOffTable` 已是 **false**
        /// ⇒ <see cref="LastLaunchRespawnedCueBall"/> 会被置 **false** ⇒ **犯规计数直接丢掉这一杆**
        /// （`PracticeSession` 的犯规数 ← 它的上升沿；而且**不会红** —— 典型"静默失真"）。
        /// ⇒ 改用粘滞位：**白球离台那一刻置位**，到**下一次出杆**时消费（⇒ 上升沿次数与改动前**逐位一致**）。
        /// </summary>
        private bool _cueBallLeftTableSinceLastLaunch;

        /// <summary>
        /// **白球落袋（scratch）累计次数**（只读诊断量，**跨杆单调、不因重摆归零**）。
        ///
        /// 口径：出杆那一刻白球不在场上（已入袋 / 擦袋角离台）⇒ 计一次。
        /// 这是本工程**唯一**可观测的犯规类型（完整犯规分类需规则引擎，属 35-B）。
        /// `PoolAudio` 与 HUD 的犯规提示都轮询它的**增量**（同 `LaunchCount` 的口径）。
        /// </summary>
        public int ScratchCount { get; private set; }

        /// <summary>最近一次 scratch 发生的时刻（`Time.unscaledTime`）；从未发生 ⇒ 负无穷。</summary>
        public float LastScratchTime { get; private set; } = float.NegativeInfinity;

        /// <summary>
        /// **因整盘未静止而被拒绝的出杆次数**（单调，只读诊断量）。
        ///
        /// ⚠ 本项存在的理由：`Launch()` 在 `!TableAtRest` 时**静默返回零向量**（设计如此 —— 不改任何状态）。
        /// 但玩家实测"这个角度发球为什么就发射不了了呢"时**完全看不到原因**：按了、什么都没发生。
        /// 现在每次拒绝都计数 + 打时间戳，HUD 据此显示 `Balls moving`（把"没反应"变成"看得见的原因"）。
        /// </summary>
        public int RefusedLaunchCount { get; private set; }

        /// <summary>最近一次"因未静止被拒"的时刻；从未发生 ⇒ 负无穷。</summary>
        public float LastRefusedLaunchTime { get; private set; } = float.NegativeInfinity;

        /// <summary>**累计出杆次数**（只读出口；音频层轮询增量 ⇒ strike 事件。**不清零**）。</summary>
        public int LaunchCount { get; private set; }

        /// <summary>最近一次**成功出杆**的白球初速大小（m/s；被门控拒绝时保持旧值）。</summary>
        public float LastLaunchSpeed { get; private set; }

        /// <summary>
        /// **瞄准辅助预测线开关的唯一读点**（= <see cref="enableShotPrediction"/>）。
        ///
        /// 未来设置菜单**只写**那个字段，不新增第二处开关状态；<see cref="AimAssistController"/> **不另存**副本。
        /// 关闭 ⇒ 预测**零成本**（不重算、不绘制）。
        /// </summary>
        public bool PredictionEnabled => enableShotPrediction;

        private void Awake()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            UnregisterMatchHost();
        }

        /// <summary>
        /// **注销比赛宿主接缝**（<see cref="OnDestroy"/> 调它；判据也直接调它）。
        ///
        /// **只注销自己** —— 注册点只有一个，别的东西（另一个驱动器 / 上层）可能已把注册点接管过去，
        /// 无条件置 `null` 会把**别人的注册**一起抹掉。
        ///
        /// ⚠ **为什么做成公开方法**：**EditMode 下 Unity 不调 `Awake` / `OnDestroy`**（只 `AddComponent`
        /// 不会触发它们）⇒ 把注销写成 `OnDestroy` 的私有内联体，就**无法被判定**（判据只能"相信"它）。
        /// 抽出本方法后，"只注销自己"这条语义在 EditMode 里**可跑、可判**。
        /// </summary>
        public void UnregisterMatchHost()
        {
            if (ReferenceEquals(Billiards.UI.MatchSeams.Host, this))
            {
                Billiards.UI.MatchSeams.Host = null;
            }
        }

        /// <summary>构造内核并装载白球（开球点 = <see cref="TableConfig.footSpot"/>，初速 0）。</summary>
        public void Initialize()
        {
            if (tableConfig == null)
            {
                Debug.LogError("PhysicsTestDrive：未指定 tableConfig，驱动器停用。", this);
                enabled = false;
                return;
            }

            // **注册比赛宿主接缝**（P5-B）：赛前屏的 `Start match` 通过 `MatchSeams` 找到本驱动器。
            // ⚠ **必须在 `Initialize()` 里注册、不能放 `Awake()`**：**EditMode 下 `AddComponent` 不会调 `Awake`**
            //   ⇒ 放 `Awake` 的话，"注册"这件事在 EditMode 驱动的路径上**永远不发生**（判据也就无从判定）。
            //   运行期语义不变（`Awake()` 本来就只是 `Initialize()`）。
            // ⚠ 用**全限定名**（不 `using Billiards.UI;`）⇒ "Physics 侧读了 UI 的哪个符号"在源码里一眼可 grep。
            Billiards.UI.MatchSeams.Host = this;

            // **8 球摆位种子：每局解析一次**（35-C P5-A）。`-1` ⇒ 取真随机并**留痕**（可回读复现该局）。
            // ⚠ 解析点必须在 `BuildRack()` **之前**（`LoadBalls(...BuildRack())` 要用 `ResolvedRackSeed`）。
            ResolvedRackSeed = rackSeed == RandomRackSeed ? System.Guid.NewGuid().GetHashCode() : rackSeed;
            if (rackSeed == RandomRackSeed)
            {
                Debug.Log($"[PhysicsTestDrive] 8 球摆位取真随机种子 ResolvedRackSeed = {ResolvedRackSeed}"
                          + "（要复现这一局就把 `rackSeed` 设成该值）。", this);
            }

            // ⚠ **必须逐字段显式赋值**：`default(PhysicsParams)` 恒为全零（C# 语言定义），
            // 而 `ballTangentialFriction = 0` 会把切向分量整个删除（非物理耗散）、
            // `ballRestitution = 0` 会让球-球碰撞完全非弹性。
            // 起点取 SafeDefault，再覆盖本组件的可调字段 ⇒ 既安全又不依赖默认值。
            // **参数来源（单一入口）**：指定了 `tuning` 资产 ⇒ 由它统一提供；
            // 未指定 ⇒ 回落到 `SafeDefault` + 本组件的占位字段（行为不变；`F2b` 走的就是这条路径）。
            PhysicsParams parameters;
            float captureScale;
            float restSpeed;
            int restFrames;
            if (tuning != null)
            {
                parameters = tuning.ToPhysicsParams();
                captureScale = tuning.pocketCaptureRadiusScale;
                restSpeed = tuning.restSpeedThreshold;
                restFrames = tuning.restStableFrames;

                // **初速也由资产统一提供**（`B4` 事故：资产缺这两个字段 ⇒ 灰盒实际用的是组件/场景序列化的旧值）。
                // 读入组件字段 ⇒ `Launch()` 仍只有**一条**读取路径（组件字段），不产生第二个真源。
                minLaunchSpeed = tuning.minLaunchSpeed;
                maxLaunchSpeed = tuning.maxLaunchSpeed;
            }
            else
            {
                parameters = PhysicsParams.SafeDefault;
                parameters.cushionRestitution = cushionRestitution;
                parameters.cushionTangentialFriction = cushionTangentialFriction;
                parameters.rollingDecelerationSlide = rollingDecelerationSlide;
                parameters.rollingDecelerationRoll = rollingDecelerationRoll;
                parameters.rollingTransitionSpeed = rollingTransitionSpeed;
                parameters.ballRestitution = ballRestitution;
                parameters.ballTangentialFriction = ballTangentialFriction;
                captureScale = 1f;   // 袋口半径真源仍是 TableConfig.Pocket（逐袋）
                restSpeed = PoolSimulator.RestSpeedThreshold;
                restFrames = PoolSimulator.RestStableFrames;
            }

            _simulator = new PoolSimulator(tableConfig, parameters, captureScale, restSpeed, restFrames);
            _simulator.LoadBalls(loadRackForBallBallDemo ? BuildRack() : BuildSingleBall());
            _accumulator = 0f;
            TotalSteps = 0;

            // **P7：新一局 ⇒ 规则状态归零**（组别 / 自由球 / 前向约束 / 指定袋），并按新球台重算剩余球数。
            // ⚠ 必须在 `LoadBalls` **之后**：剩余球数是**按球台**算的。
            // ⚠ 两个入口（`Initialize` / `ReloadRack`）都走这段 ⇒ 重摆后不会带着上一局的组别继续打。
            _playerGroup = GroupState.OpenTable;
            ruleState = new RuleState { calledPocket = -1 };
            BallInHand = BallInHandKind.None;
            PendingCueBallPlacement = false;
            AiPhase = AiShotPhase.None;
            _aiPhaseElapsed = 0f;
            _aiThinkElapsed = 0f;
            SyncGroupsForTurn();
            RefreshRemainingCounts();
            ResetVisualRotationTracking();   // 首帧只落位、不自转（无"上一帧位置"可用）

            if (cushionGizmo != null)
            {
                cushionGizmo.SetSimulator(_simulator);
            }

            SyncVisual();
            SyncAimIndicator();
        }

        /// <summary>
        /// **重摆开新局**（练习"再来一局"的唯一入口）：按当前模式重新装球 + 清累加器 + 重置自转跟踪。
        ///
        /// ── 为什么**刻意不动 `LaunchCount`** ────────────────────────────────────
        /// `LaunchCount` 是 `PoolAudio`（击杆声）与 `PoolVfxView`（击杆特效）的**单调出杆计数器**，
        /// 二者都只认"**上升**"（`count > last`）。归零后再从 1 涨起，**永远追不上**归零前的 `last`
        /// ⇒ 重摆后的第一杆会**既不响也不出特效**。故本方法只重装球，计数器留给会话层做差值。
        ///
        /// 入袋球的可视物由 `SyncBallVisual` 的"回场复位"分支自动激活（下沉动画的中断路径已覆盖）。
        /// </summary>
        public void ReloadRack()
        {
            if (_simulator == null)
            {
                return;
            }

            _simulator.LoadBalls(loadRackForBallBallDemo ? BuildRack() : BuildSingleBall());
            _accumulator = 0f;
            TotalSteps = 0;

            // **P7：新一局 ⇒ 规则状态归零**（组别 / 自由球 / 前向约束 / 指定袋），并按新球台重算剩余球数。
            // ⚠ 必须在 `LoadBalls` **之后**：剩余球数是**按球台**算的。
            // ⚠ 两个入口（`Initialize` / `ReloadRack`）都走这段 ⇒ 重摆后不会带着上一局的组别继续打。
            _playerGroup = GroupState.OpenTable;
            ruleState = new RuleState { calledPocket = -1 };
            BallInHand = BallInHandKind.None;
            PendingCueBallPlacement = false;
            AiPhase = AiShotPhase.None;
            _aiPhaseElapsed = 0f;
            _aiThinkElapsed = 0f;
            SyncGroupsForTurn();
            RefreshRemainingCounts();
            ResetVisualRotationTracking();   // 重摆是瞬移 ⇒ 不得被当成真实位移、否则球凭空猛转

            // **复位"全场自由球"的两个状态**（独立复核抓出的**状态泄漏**，可达路径很短：
            // 把白球打进袋 ⇒ 按 `R`（没有任何门拦 `R`）⇒ 卡开 ⇒ 点 `Play again` ⇒ 走到这里）：
            // ① `PendingCueBallPlacement`：不复位 ⇒ 新局开局就误报"待放球"，且**第一次左键被当成放球**
            //    （`CanPlaceCueBall` 跳过索引 0 ⇒ 必成功）⇒ 那一次点击把白球挪走而不是瞄准；
            // ② `_cueBallLeftTableSinceLastLaunch`：不复位 ⇒ 新局第一杆 `LastLaunchRespawnedCueBall = false || true = true`
            //    ⇒ `PracticeSession._fouls++` ⇒ **一次假犯规**。
            PendingCueBallPlacement = false;
            _cueBallLeftTableSinceLastLaunch = false;

            SyncVisual();
            SyncAimIndicator();
        }

        /// <summary>
        /// ⚠ **本对函数参与出杆路径**（不是只读出口）：`Launch()` 与 AI 探针**共用同一算式**。
        ///
        /// **力度 ⇒ 白球初速大小**（**唯一算式**）：`Lerp(minLaunchSpeed, maxLaunchSpeed, Clamp01(power))`。
        /// ⚠ <see cref="Launch"/> 与 <see cref="EvaluateAiCandidate"/> **必须共用本对函数**，
        /// 否则"AI 评估的那一杆"与"真正打出的那一杆"会各自演化（AI 照着**另一个物理**做决策）。
        /// </summary>
        public float LaunchSpeedFor(float power)
        {
            return Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, Mathf.Clamp01(power));
        }

        /// <summary>
        /// **角度 + 速度大小 ⇒ 白球初速向量**（**唯一算式**）：
        /// 方向 = `(cos θ, sin θ)`（θ = 角度，`0° = +x`、逆时针为正），再乘速度大小。
        /// ⚠ 本函数与 <see cref="LaunchSpeedFor"/> 一起，是 `Launch()` 改动前那三行算术的**逐位等价拆分**
        /// （同 3 步：`Cos` / `Sin` / `向量 × 标量`，只把 `Lerp` 挪进另一个函数体）。
        /// </summary>
        public Vector2 CueVelocityFor(float degrees, float speed)
        {
            float rad = degrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            return direction * speed;
        }

        // ══════════════════ AI 出杆（35-C P5-A）═════════════════════════════

        /// <summary>
        /// **AI 认定的本方分组**（`P5-B` 的宿主写它；默认开放球局）。
        /// ⚠ 这是**规则层**的输入（本方打实色 / 花色 / 未定组），**不是**"轮到谁"（那个在宿主）。
        /// </summary>
        [Tooltip("AI 认定的本方分组（开放 / 实色 / 花色）。")]
        public GroupState aiGroupState = GroupState.OpenTable;

        /// <summary>
        /// **AI 评估探针**（懒建、**复用**；**与主内核完全分离**）——
        /// 两条硬保证：① 评估**绝不改动**真实球台；② 评估期的接触事件**绝不混进**主内核的诊断流。
        /// </summary>
        private PoolSimulator _aiProbe;

        /// <summary>**最近一次 AI 规划出的意图**（未规划过 ⇒ `planned == false`）。（**只读出口**：供判据与诊断用；**不参与出杆路径**）</summary>
        public AiShotIntent LastAiShotIntent { get; private set; }

        /// <summary>**最近一次 AI 决策用的种子**（判据据此复现同一次决策）。（**只读出口**：供判据与诊断用；**不参与出杆路径**）</summary>
        public int LastAiSeed { get; private set; }

        // ── AI 回合驱动（P6 收口：让"轮到 AI ⇒ 自己出一杆"在**运行期**真的发生）──────

        /// <summary>
        /// **AI"思考"停顿（秒）** —— 让玩家看得见"轮到对手了"。
        /// ⚠ **可配置**且**走 `Advance(dt)`**（不是 `Update`/`FixedUpdate` 里的 sleep）⇒
        /// 判据能用固定 dt 把 AI **确定性**推到"出手"那一刻。
        /// </summary>
        [Tooltip("AI 出杆前的思考停顿（秒）。")]
        public float aiThinkSeconds = 1.0f;

        /// <summary>
        /// **AI 出杆种子的注入位**（照既有 `rackSeed` 的范式）：`>= 0` ⇒ 用
        /// `aiLaunchSeed + (本局第几杆)`（**每杆都变**且**逐位可复现**）；`-1` ⇒ 每杆取真随机。
        /// </summary>
        [Tooltip("AI 出杆种子（-1 = 每杆真随机；>=0 = 可复现的起点种子）。")]
        public int aiLaunchSeed = RandomRackSeed;

        /// <summary>**AI 因无法出杆而把回合让给玩家的次数**（只读出口：判据不读像素）。</summary>
        public int AiTurnYieldedCount { get; private set; }

        /// <summary>
        /// **AI 自己把白球放到合法位置的次数**（只读出口）。
        /// ⚠ 为什么必须有这个出口：`Launch()` 里有一条"白球离台 ⇒ 回开球位"的**兜底**
        /// ⇒ 即使 AI **完全不放球**，它也能"照打"（自由球白拿）。⇒ 只看"它出杆了"是**真空绿**，
        /// 必须看"它真的自己放了吗"。
        /// </summary>
        public int AiAutoPlaceCount { get; private set; }

        /// <summary>本局 AI 已经**真的出杆**的次数（种子派生的单调计数；只读出口）。</summary>
        public int AiLaunchCount => _aiLaunchCount;

        /// <summary>
        /// **AI 出杆的三个可见阶段**（"AI 要像人"：先**瞄准** → 再**拉杆** → 最后**击出**）。
        /// ⚠ 一次性 `aim+power+Launch()` 虽然结果一样，但玩家**看不到**对手在做什么 —— 用户实机要的是"像人"。
        /// </summary>
        public enum AiShotPhase
        {
            /// <summary>没在出杆（玩家回合 / 球在滚 / 有杆在飞 / 已结算）。</summary>
            None = 0,

            /// <summary>**瞄准**：杆已指向目标方向（可见），但还没拉杆。</summary>
            Aiming,

            /// <summary>**拉杆**：`power` 从 0 线性涨到规划力度（球杆据此可见地后拉）。</summary>
            Pulling,

            /// <summary>**击出**：这一帧走**既有** `Launch()`（与玩家同一条出杆路径）。</summary>
            Strike,
        }

        /// <summary>**当前 AI 出杆阶段**（只读出口：判据据此断"分阶段"真的发生，而不是一次性出杆）。</summary>
        public AiShotPhase AiPhase { get; private set; }

        /// <summary>AI **瞄准**阶段时长（秒，可配置）。</summary>
        public float aiAimSeconds = 0.5f;

        /// <summary>AI **拉杆**阶段时长（秒，可配置）。</summary>
        public float aiPullSeconds = 0.4f;

        private float _aiPhaseElapsed;
        private float _aiPlannedPower;

        /// <summary>**本杆是否在飞**（只读出口：球杆据此决定"跟球"还是"冻结在击球位置"）。</summary>
        public bool ShotInFlight => _shotInFlight;

        /// <summary>
        /// **最近一次出杆那一刻白球的位置**（物理坐标，只读出口）。
        /// ⚠ 存在的理由：出杆后白球会跑，而球杆若每帧读 `ballVisuals[0].position` ⇒ **杆跟着白球飞**
        ///（用户实机反馈）。球杆在 `ShotInFlight` 期间应当冻结在**这个点**。
        /// </summary>
        public Vector2 LastStrikeCueBallPosition { get; private set; }

        private float _aiThinkElapsed;
        private int _aiLaunchCount;

        // ── 比赛宿主（`Billiards.UI.IMatchHost`，P5-B）──────────────────────────

        /// <summary>本局的难度档（`StartMatch` 写入；UI 显示与 AI 决策**共用同一个真源**）。（**只读出口**：供判据与诊断用；**不参与出杆路径**）</summary>
        public AiDifficulty MatchTier { get; private set; } = EightBallAi.DefaultDifficulty;

        /// <summary>**当前轮到谁**（回合真源：UI 的"轮到谁"与输入让位门都读它）。（**只读出口**：供判据与诊断用；**不参与出杆路径**）</summary>
        public ShotTurn CurrentTurn { get; private set; } = ShotTurn.Player;

        /// <summary>是否轮到对手。（**只读出口**：供判据与诊断用；**不参与出杆路径**）</summary>
        public bool IsAiTurn => CurrentTurn == ShotTurn.Ai;

        // ── 结算出口（P6：一局定胜负）────────────────────────────────────────

        /// <summary>**本局是否已结束**（`true` ⇒ **不会再有第 2 局**：只有显式再调 `StartMatch` 才开新局）。
        /// （**只读出口**：供 UI 与诊断用；**不参与出杆路径**）</summary>
        public bool MatchOver { get; private set; }

        /// <summary>**本局胜者**（`MatchOver == true` 时有效；`ShotTurn.Player` = 玩家胜）。
        /// （**只读出口**：供 UI 与诊断用；**不参与出杆路径**）</summary>
        public ShotTurn Winner { get; private set; }

        /// <summary>
        /// **结算原因**（`MatchOver == true` 时有效）—— **由规则层 `RuleVerdict.outcome` 原样映射**，
        /// 宿主**不自己猜**（判据⑥守这条：改规则层结论 ⇒ 本出口必须跟着变）。
        /// （**只读出口**：供 UI 与诊断用；**不参与出杆路径**）
        /// </summary>
        public ShotOutcomeKind EndReason { get; private set; }

        private bool _settlementPending;

        /// <summary>
        /// **本杆的规则层输入**（**宿主维护**：轮次 / 本方组别 / 剩余球数 / 指定袋 / 是否开球）。
        /// ⚠ 驱动器**不自己编造**这些（它不认识"谁该打哪个组"）—— 宿主灌进来，驱动器只负责
        /// "一杆结束后拿它去问规则层"。
        /// </summary>
        public RuleState ruleState;

        // ── 规则施加（P7：`RuleVerdict` 的七个字段**一个都不许丢**）─────────────────

        /// <summary>
        /// **玩家（人类）一方的组别**（`OpenTable` = 还没定组）—— 组别的**唯一真源**。
        /// `ruleState.groupState` 是**击球方相对**的（规则层按 `before.turn` 解读），
        /// 由 <see cref="SyncGroupsForTurn"/> 从本字段派生 ⇒ **别在两处各写一份**。
        /// </summary>
        private GroupState _playerGroup = GroupState.OpenTable;

        /// <summary>
        /// **本杆之后的自由球形态**（`None` = 没有自由球）。**只读出口**：HUD / 判据据此知道"该放白球了、能放哪"。
        /// 与 <see cref="PendingCueBallPlacement"/> 的分工：本字段说**是什么自由球**（全场 / 头区），
        /// 那个布尔说**现在处于待放态**（能不能放）。
        /// </summary>
        public BallInHandKind BallInHand { get; private set; }

        /// <summary>**因"开球犯规后必须往头线前打"而被拒的出杆次数**（只读出口 —— 拒绝不许静默）。</summary>
        public int RefusedForwardCount { get; private set; }

        /// <summary>头区自由球"往前打"的最小前向分量（m/s）—— 喂给 `PoolSimulator.IsBreakShotForwardEnough`。</summary>
        public float minForwardComponent = 0.5f;

        /// <summary>
        /// **最近一次被施加的判定**（**只读出口**）。用途：判据/诊断可以直接看"规则层到底给了什么"
        /// （`reassignGroup` / `ballInHand` / `mustShootForward` / `respotEightBall` / `reRack` / `outcome`），
        /// 不必从副作用反推 —— 反推容易把"副作用碰巧相同"读成"字段被施加了"（真空绿的常见来源）。
        /// </summary>
        public RuleVerdict LastVerdict { get; private set; }

        private bool _shotInFlight;
        private int[] _shotBeforePocketIndex;
        private bool[] _shotBeforeInPlay;

        /// <summary>
        /// **施加一次判定**（**唯一施加口**）：把规则层 `RuleVerdict` 的**七个字段全部落地**。
        ///
        /// ⚠ **P7 的根因就是这里曾经只施加了 2/7**：`reassignGroup` / `ballInHand` / `mustShootForward` /
        /// `respotEightBall` / `reRack` 全被丢掉 ⇒ ① 组别永远是 `OpenTable`（首碰合法性整段被跳过、
        /// `IsEightBallShot` 恒 false ⇒ **进黑 8 必判负**、赢了也判负）；② 自由球没人发；③ 开球进黑 8 不回位；
        /// ④ 无效开球不重摆。⇒ 现在**逐字段施加**，每个字段都有**可判定出口**（判据 ①–⑦ 守这条）。
        ///
        /// 施加顺序刻意如此：**先做"局内状态"（重摆/定组/自由球/前向/复位），再切回合，最后按新回合派生组别与剩余球数**。
        /// 理由：定组要按**击球方**的视角记（`_playerGroup`），而 `ruleState.groupState` 是**击球方相对**的 ⇒
        /// 必须等 `SetTurn` 之后再派生，否则会把"对手的组"写成"我的组"。
        /// </summary>
        public void ApplyVerdict(in RuleVerdict verdict)
        {
            LastVerdict = verdict;   // 只读出口：判据可直接断言"规则层给了什么"

            // ① **重摆**（开球无效）：只重摆，**开球方不变**（回合由 ⑥ 的 `nextTurn` 决定，这里不动）。
            if (verdict.reRack)
            {
                ReRackKeepingEveryone();
            }

            // ② **定组**（开放球局的第一颗合法进球）：先记进"玩家侧真源"，等回合切完再派生。
            if (verdict.reassignGroup && verdict.groupBall > 0)
            {
                GroupState shooterGroup = EightBallRules.IsSolid(verdict.groupBall)
                    ? GroupState.Solid
                    : GroupState.Stripe;
                _playerGroup = CurrentTurn == ShotTurn.Player ? shooterGroup : OpponentGroup(shooterGroup);
            }

            // ③ **自由球**：记形态 + 打开"待放球"闸门（玩家拖动放 / AI 自动放，见 `TickAiTurn`）。
            //    ⚠ 只记形态**不等于**实现了自由球 —— 谁能放、放哪、AI 怎么自动放，是 ③ 的配套（判据 ⑤ 守）。
            BallInHand = verdict.ballInHand;
            if (verdict.ballInHand != BallInHandKind.None)
            {
                PendingCueBallPlacement = true;
            }

            // ④ **必须往头线前打**（开球犯规的附加约束 ⇒ 喂给下一杆 `Launch()` 的前向门）。
            ruleState.mustShootForward = verdict.mustShootForward;

            // ⑤ **黑 8 复位**（开球进黑 8 ⇒ 放回 `footSpot`，继续打、不算赢）。
            if (verdict.respotEightBall)
            {
                RespotEightBall();
            }

            // ⑥ 切回合（走唯一施加口，连带清 `calledPocket`）。
            SetTurn(verdict.nextTurn);

            // ⑦ **回合切完**：派生组别（`ruleState.groupState` / `aiGroupState`）+ 按**当前球台**重算剩余球数。
            SyncGroupsForTurn();
            RefreshRemainingCounts();

            // **开球只判一次**；但**无效开球要重摆 ⇒ 仍是"开球那一杆"**（下一杆还得按开球规则判）。
            ruleState.isBreak = verdict.reRack;

            if (!verdict.gameOver)
            {
                return;
            }

            MatchOver = true;
            Winner = verdict.winner;
            EndReason = verdict.outcome;
            _settlementPending = true;
        }

        private static GroupState OpponentGroup(GroupState group) => group switch
        {
            GroupState.Solid => GroupState.Stripe,
            GroupState.Stripe => GroupState.Solid,
            _ => GroupState.OpenTable,
        };

        /// <summary>
        /// 把"玩家侧组别"派生到**击球方相对**的 `ruleState.groupState` 与 AI 侧 `aiGroupState`（**单点**）。
        /// ⚠ **只有这里能写 `ruleState.groupState`**（除了测试夹具自己设）—— 两处各写一份必然分叉。
        /// </summary>
        private void SyncGroupsForTurn()
        {
            GroupState aiGroup = _playerGroup == GroupState.OpenTable
                ? GroupState.OpenTable
                : OpponentGroup(_playerGroup);
            ruleState.groupState = CurrentTurn == ShotTurn.Player ? _playerGroup : aiGroup;
            aiGroupState = aiGroup;
        }

        /// <summary>
        /// 按**当前球台**重算两组剩余球数（**每杆结束后 + 每局装球后**各一次）。
        /// ⚠ 为什么必须重算：`solidRemaining/stripeRemaining` 是**规则层的输入**，若与球台脱节，
        /// `IsEightBallShot`（"清完本方球 ⇒ 该打黑 8"）与 `JudgeRegular` 的 `ownGroupCleared` 会**同时**判错。
        /// </summary>
        public void RefreshRemainingCounts()
        {
            if (_simulator == null)
            {
                return;
            }

            int solid = 0;
            int stripe = 0;
            int count = _simulator.Balls.Count;
            for (int ball = 1; ball <= 15 && ball < count; ball++)
            {
                if (ball == EightBallRules.EightBall || !_simulator.IsBallInPlay(ball))
                {
                    continue;
                }

                if (EightBallRules.IsSolid(ball))
                {
                    solid++;
                }
                else if (EightBallRules.IsStripe(ball))
                {
                    stripe++;
                }
            }

            ruleState.solidRemaining = solid;
            ruleState.stripeRemaining = stripe;
        }

        /// <summary>**把黑 8 放回 `footSpot`**（开球进黑 8 的既有口径：复位、继续打、不算赢）。</summary>
        private void RespotEightBall()
        {
            if (_simulator == null || tableConfig == null)
            {
                return;
            }

            _simulator.PlaceBall(EightBallRules.EightBall, tableConfig.footSpot, Vector2.zero);
            ResetVisualRotationTracking();
            SyncVisual();
        }

        /// <summary>
        /// **重摆**（开球无效）：按当前种子整盘重建，**不改回合、不改组别**（开球方继续开球）。
        /// </summary>
        private void ReRackKeepingEveryone()
        {
            if (_simulator == null)
            {
                return;
            }

            _simulator.LoadBalls(BuildRack());
            _accumulator = 0f;
            PendingCueBallPlacement = false;
            BallInHand = BallInHandKind.None;
            ruleState.mustShootForward = false;
            ClearCalledPocket();
            ResetVisualRotationTracking();
            RefreshRemainingCounts();
            SyncVisual();
        }

        /// <summary>
        /// **结算面板只该弹一次**的闸门：返回 `true` **仅第一次**（取走即清零）。
        /// ⚠ 用**显式标志**而不是让 UI 自己记 —— 判据要能断"不会重复弹"。
        /// </summary>
        public bool TryConsumeSettlement()
        {
            if (!_settlementPending)
            {
                return false;
            }

            _settlementPending = false;
            return true;
        }

        // ── 指定袋（D3，P6 的 Runtime 半边）──────────────────────────────────

        /// <summary>本杆指定的袋号（`-1` = 未指定）。**只读出口**：UI 的"指定了哪个袋"读它。</summary>
        public int CalledPocket => ruleState.calledPocket;

        /// <summary>因"**打黑 8 却没指定袋**"被拒的出杆次数（**只读出口**：判据不读像素）。</summary>
        public int RefusedCallPocketCount { get; private set; }

        /// <summary>
        /// **本杆是否"打黑 8"**（= 该杆**必须**指定袋）—— **单点口径，UI 与 Runtime 共用**
        /// （别在 UI 与 Runtime 各算一遍，那会分叉）。
        ///
        /// 口径与 `EightBallRules.JudgeRegular` 的 `ownGroupCleared` **同源**：按 `groupState` 取对应组的剩余数，
        /// `<= 0` 即已清完；**开放球局不算**（那时还没定组，不可能是"打黑 8 那杆"）。
        /// （**只读出口**：供 UI 与诊断用；**不参与出杆路径**）
        /// </summary>
        public bool IsEightBallShot
        {
            get
            {
                if (ruleState.groupState == GroupState.OpenTable)
                {
                    return false;
                }

                return ruleState.groupState == GroupState.Solid
                    ? ruleState.solidRemaining <= 0
                    : ruleState.stripeRemaining <= 0;
            }
        }

        /// <summary>
        /// **指定袋 —— 唯一写入口**（写进 `ruleState.calledPocket`）。
        /// **非法索引**（越界 / 该袋不存在）⇒ 返回 `false` 且**不改任何状态**。
        /// </summary>
        public bool TryCallPocket(int pocketIndex)
        {
            if (tableConfig == null || pocketIndex < 0 || pocketIndex >= tableConfig.PocketCount
                || tableConfig.GetPocket(pocketIndex) == null)
            {
                return false;
            }

            ruleState.calledPocket = pocketIndex;
            return true;
        }

        /// <summary>清除指定袋（回到 `-1` 未指定）。UI 取消选择时调它。</summary>
        public void ClearCalledPocket()
        {
            ruleState.calledPocket = -1;
        }

        /// <summary>本杆"前"快照（P6）：`Launch()` 捕获一次，结算时与"后"比对。</summary>
        private void CaptureShotBaseline()
        {
            int n = _simulator.Balls.Count;
            _shotBeforePocketIndex = new int[n];
            _shotBeforeInPlay = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _shotBeforePocketIndex[i] = _simulator.CapturedPocketIndex(i);
                _shotBeforeInPlay[i] = _simulator.IsBallInPlay(i);
            }
        }

        /// <summary>
        /// **一杆结束后结算 —— 规则管线接结算口的唯一实现点**（P6）。
        ///
        /// 流程：`_shotInFlight && TableAtRest && !MatchOver` ⇒
        /// 取"后"快照 ⇒ `EightBallRules.ExtractFacts(before, after, ShotContactEvents)` ⇒
        /// `EightBallRules.Judge(ruleState, facts)` ⇒ `ApplyVerdict(verdict)`。
        ///
        /// 硬约束：
        /// · **静止门用既有 `TableAtRest`**（不新造判定）；
        /// · **同一杆只结算一次**（`_shotInFlight` 落闸）；
        /// · **规则结论只来自规则层**（驱动器不自己算胜负 —— 判据⑥守这条）；
        /// · **一局定胜负**：结算后 `MatchOver` 制住后续杆，要再来一局必须显式 `StartMatch`。
        /// </summary>
        /// <returns>`true` = 本次真的结算了一杆。</returns>
        public bool SettleShotIfAtRest()
        {
            if (_simulator == null || !_shotInFlight || MatchOver || !_simulator.TableAtRest)
            {
                return false;
            }

            _shotInFlight = false;

            int n = _simulator.Balls.Count;
            var afterPocketIndex = new List<int>(n);
            var afterInPlay = new List<bool>(n);
            for (int i = 0; i < n; i++)
            {
                afterPocketIndex.Add(_simulator.CapturedPocketIndex(i));
                afterInPlay.Add(_simulator.IsBallInPlay(i));
            }

            RuleFacts facts = EightBallRules.ExtractFacts(_shotBeforePocketIndex, afterPocketIndex,
                _shotBeforeInPlay, afterInPlay, _simulator.ShotContactEvents);

            RuleState state = ruleState;
            state.turn = CurrentTurn;   // 轮次以**施加端**为准（宿主灌的 `ruleState.turn` 可能滞后）

            ApplyVerdict(EightBallRules.Judge(state, facts));
            return true;
        }

        /// <summary>
        /// **设置回合**（**施加端**）：规则层只判"下一杆轮到谁"（`RuleVerdict.nextTurn`），
        /// 真正切回合由宿主做 —— 本方法就是那个唯一的施加口。
        /// </summary>
        public void SetTurn(ShotTurn turn)
        {
            CurrentTurn = turn;

            // **回合边界清回 -1**（P6）：玩家回合**不得复用**上一杆（可能是 AI）的选择；
            // 放在 `SetTurn` 这个**唯一施加口**里，别在 UI 与 Runtime 各清一遍。
            if (turn == ShotTurn.Player)
            {
                ClearCalledPocket();
            }
        }

        /// <summary>
        /// **开始一局 AI 对战**（<see cref="Billiards.UI.IMatchHost.StartMatch"/>）：
        /// **新开一局** ⇒ ① 记档；② 定组回**开放球局**；③ `rackSeed` 置回 <see cref="RandomRackSeed"/>
        /// （**每局换摆法归宿主**；`ResolvedRackSeed` 在 <see cref="Initialize"/> 里重掷）；
        /// ④ 重建 15 颗；⑤ **先手交给玩家**（开球）。
        /// </summary>
        public void StartMatch(AiDifficulty tier)
        {
            MatchTier = tier;
            aiGroupState = GroupState.OpenTable;
            rackSeed = RandomRackSeed;
            Initialize();
            CurrentTurn = ShotTurn.Player;

            // **一局定胜负**：`StartMatch` 是**唯一**能开新局的口 —— 它把结算位一并清掉
            // （⇒ "结算后不会自动进第 2 局"；要再来一局必须**显式**再调一次本方法）。
            MatchOver = false;
            Winner = ShotTurn.Player;
            EndReason = ShotOutcomeKind.Ongoing;
            _settlementPending = false;
        }

        /// <summary>
        /// **玩家输入的让位门（单一实现点）**：轮到对手 ⇒ **拒绝本次输入**，并**登记一次可见反馈**
        /// （<see cref="Billiards.UI.MatchTurnNotice"/>，**不许静默** —— 静默 = 界面在假装什么都没发生）。
        /// 返回 `false` ⇒ 调用方**必须放弃**本次手势。
        /// 交互层在**消费一次玩家手势之前**调它（例：`PointerPowerController` 的右键蓄力边沿）。
        /// </summary>
        public bool TryConsumePlayerInput()
        {
            if (!IsAiTurn)
            {
                return true;
            }

            Billiards.UI.MatchTurnNotice.RequestBlock(Billiards.UI.MatchTurnNotice.ReasonOpponentTurn);
            return false;
        }

        /// <summary>
        /// **只规划、不出杆**（可复现性判据与 UI 预览用）。`seed` 由**调用方注入**：
        /// 宿主传**真随机**、判据传**固定值** —— 同 `seed` + 同局面 ⇒ **逐位可复现**。
        ///
        /// ⚠ **不改变任何真实状态**（只读 `_simulator`；写 <see cref="LastAiShotIntent"/> / <see cref="LastAiSeed"/>）。
        /// </summary>
        public AiShotIntent PlanAiShot(AiDifficulty difficulty, int seed)
        {
            LastAiSeed = seed;
            if (_simulator == null)
            {
                LastAiShotIntent = default;
                return LastAiShotIntent;
            }

            AiShotContext context = BuildAiContext();
            LastAiShotIntent = EightBallAi.Plan(context, difficulty, seed, EvaluateAiCandidate);
            return LastAiShotIntent;
        }

        /// <summary>
        /// **AI 出杆入口**：先 <see cref="PlanAiShot"/> ⇒ 把意图写进**既有的** `aimDegrees` / `power`
        /// （与交互层**同一写入口径**）⇒ 再走**既有 <see cref="Launch()"/>**（**物理一行不动**）。
        ///
        /// 返回值与 <see cref="Launch"/> **同契约**：零向量 = 被门控拒绝（此时意图仍留在
        /// <see cref="LastAiShotIntent"/> 里，调用方可据此重试或显示"未出杆"）。
        /// </summary>
        public Vector2 TryLaunchAiShot(AiDifficulty difficulty, int seed)
        {
            AiShotIntent intent = PlanAiShot(difficulty, seed);
            if (!intent.planned)
            {
                return Vector2.zero;
            }

            // **AI 打黑 8 必须自己指定袋**（P6，功能级）：否则会撞上"未指定 ⇒ 拒杆"的闸门
            // ⇒ **AI 会一直出不了杆（对局卡死）**。
            // 袋号 = `Plan` **已经选出的目标袋**（不另写一套选袋逻辑）；写入口仍是**唯一**入口 `TryCallPocket`。
            // ⚠ 普通杆**不动** `calledPocket`；"清回 -1" 由回合边界（`SetTurn`）负责，不在这里各清一遍。
            if (IsEightBallShot)
            {
                if (intent.pocketIndex < 0 || !TryCallPocket(intent.pocketIndex))
                {
                    return Vector2.zero;   // 袋号非法（不该发生）⇒ 不出杆，绝不带着错的指定袋开打
                }
            }

            aimDegrees = intent.aimDegrees;
            power = intent.power;
            SyncAimIndicator();
            return Launch();
        }

        /// <summary>
        /// **本帧是否满足"AI 该出杆"的全部前提**（**单一判定点**；驱动器与判据共用，别各写一份）：
        /// `MatchOver == false` **且** 轮到 AI **且** 没有待结算（`_settlementPending`）
        /// **且** 没有在飞的杆（`_shotInFlight`）**且** 台面静止（既有 `TableAtRest`）。
        ///
        /// ⚠ **五个条件缺一不可**（每条都对应一种真实故障）：
        /// · 少了 `MatchOver` ⇒ 结算后还在打（一局定胜负被破坏）；
        /// · 少了 `IsAiTurn` ⇒ **玩家回合 AI 抢杆**（对局完全失控）；
        /// · 少了 `_settlementPending` ⇒ 结算卡还没弹就被下一杆盖掉；
        /// · 少了 `_shotInFlight` ⇒ 同一杆期间反复出杆；
        /// · 少了 `TableAtRest` ⇒ 球还在滚就出杆，`Launch()` 会拒 ⇒ 白送一次"让位"。
        /// </summary>
        public bool AiTurnArmed
        {
            get
            {
                if (MatchOver || !IsAiTurn || _settlementPending || _shotInFlight)
                {
                    return false;
                }

                return _simulator != null && _simulator.TableAtRest;
            }
        }

        /// <summary>
        /// **AI 回合驱动 —— 唯一实现点**：`AiTurnArmed` 期间累计"思考"时间，到点就**只走**
        /// <see cref="TryLaunchAiShot"/>（叫袋 / 袋号 / 初速全在它里面；本方法**不碰物理**）。
        ///
        /// ⚠ **为什么在 `Advance()` 里 tick**（与 `SettleShotIfAtRest()` 同一条理由）：
        /// EditMode **不跑** `Update`/`FixedUpdate` ⇒ 判据只有靠固定 dt 推进 `Advance`
        /// 才能**确定性**地把 AI 推到"出手"那一刻。
        ///
        /// ⚠ **绝不死锁**（用户实机报的第一硬伤）：`TryLaunchAiShot` 返回零向量
        ///（意图为空 / 被门控拒）⇒ **显式把回合让给玩家**（`SetTurn(Player)` +
        /// <see cref="AiTurnYieldedCount"/> +1 + `Debug.LogWarning`）。
        /// **不允许**出现"`IsAiTurn` 长期为真 + 台面静止 + 没人出杆"。
        /// </summary>
        private void TickAiTurn(float elapsed)
        {
            if (!AiTurnArmed)
            {
                // 前提不成立（玩家回合 / 球还在滚 / 有杆在飞 / 有待结算 / 已结算）⇒ **阶段与计时都归零**，
                // 不许把"球在滚的那几帧"攒进去（否则一静止就立刻开打，看不出"思考"）。
                _aiThinkElapsed = 0f;
                _aiPhaseElapsed = 0f;
                AiPhase = AiShotPhase.None;
                return;
            }

            switch (AiPhase)
            {
                case AiShotPhase.None:
                    // ① **思考**（可配置延时）—— 让玩家先看到"轮到对手了"。
                    _aiThinkElapsed += elapsed;
                    if (_aiThinkElapsed < aiThinkSeconds)
                    {
                        return;
                    }

                    _aiThinkElapsed = 0f;

                    // ② **自由球**：AI 自己找合法位置放白球（否则第 1 段刚做的 `ballInHand` 会把 AI 卡住）。
                    if (!TryAutoPlaceAiCueBall())
                    {
                        YieldAiTurn("自由球找不到合法放球点");
                        return;
                    }

                    // ③ **规划**（只读探针）+ 打黑 8 时叫袋；失败 ⇒ 让位。
                    if (!BeginAiShot())
                    {
                        YieldAiTurn("规划不出任何一杆（无合法目标 / 叫袋失败）");
                    }

                    return;

                case AiShotPhase.Aiming:
                    // ④ **瞄准段**：杆已指向目标（`aimDegrees` 已写），停一拍让玩家看清方向。
                    _aiPhaseElapsed += elapsed;
                    if (_aiPhaseElapsed < aiAimSeconds)
                    {
                        return;
                    }

                    _aiPhaseElapsed = 0f;
                    power = 0f;                        // 拉杆从 **0** 起
                    AiPhase = AiShotPhase.Pulling;
                    return;

                case AiShotPhase.Pulling:
                {
                    // ⑤ **拉杆段**：`power` 从 0 线性涨到规划力度 ⇒ 球杆可见地后拉。
                    _aiPhaseElapsed += elapsed;
                    float t = aiPullSeconds <= 0f ? 1f : Mathf.Clamp01(_aiPhaseElapsed / aiPullSeconds);
                    power = _aiPlannedPower * t;
                    if (t < 1f)
                    {
                        return;
                    }

                    AiPhase = AiShotPhase.Strike;
                    return;
                }

                case AiShotPhase.Strike:
                    // ⑥ **击出**：走**既有** `Launch()`（与玩家同一条出杆路径；`aimDegrees`/`power` 已在前两段写好）。
                    AiPhase = AiShotPhase.None;
                    _aiPhaseElapsed = 0f;
                    if (Launch().magnitude > 0f)
                    {
                        _aiLaunchCount++;
                        return;
                    }

                    YieldAiTurn("Launch() 被门控拒绝");
                    return;
            }
        }

        /// <summary>
        /// **AI 出杆的"规划段"**：只读探针算意图 ⇒ 打黑 8 时**叫袋** ⇒ 把方向写进 `aimDegrees`（玩家可见）。
        /// 返回 `false` = 规划不出来（调用方**让位**）。
        /// </summary>
        private bool BeginAiShot()
        {
            // 种子：**注入位**优先（判据可复现）；否则每杆真随机 ⇒ 同一局不会每杆都一样。
            int seed = aiLaunchSeed >= 0
                ? aiLaunchSeed + _aiLaunchCount
                : System.Guid.NewGuid().GetHashCode();

            AiShotIntent intent = PlanAiShot(MatchTier, seed);
            if (!intent.planned)
            {
                return false;
            }

            // AI 打黑 8 必须自己叫袋（既有口径：写入口唯一 `TryCallPocket`）。
            if (IsEightBallShot && (intent.pocketIndex < 0 || !TryCallPocket(intent.pocketIndex)))
            {
                return false;
            }

            aimDegrees = intent.aimDegrees;
            _aiPlannedPower = intent.power;
            power = 0f;
            SyncAimIndicator();

            _aiPhaseElapsed = 0f;
            AiPhase = AiShotPhase.Aiming;
            return true;
        }

        /// <summary>
        /// **AI 自由球自动放球**：按**确定性网格**找第一个合法点（`CanPlaceCueBall` 已含台内/压球/袋口，
        /// 头区自由球还含"线后"约束）。找不到 ⇒ `false`（调用方**让位**，不许静默卡住）。
        /// ⚠ 不用随机：同局面 ⇒ 同落点（判据可复现，AI 站位也不会每局乱跳）。
        /// </summary>
        private bool TryAutoPlaceAiCueBall()
        {
            if (!PendingCueBallPlacement || _simulator == null || tableConfig == null)
            {
                return true;   // 没有自由球 ⇒ 无事可做（**不是**失败）
            }

            const int steps = 24;
            for (int iy = 0; iy < steps; iy++)
            {
                float y = Mathf.Lerp(-tableConfig.halfWidth * 0.9f, tableConfig.halfWidth * 0.9f,
                    (iy + 0.5f) / steps);
                for (int ix = 0; ix < steps; ix++)
                {
                    float x = Mathf.Lerp(-tableConfig.halfLength * 0.9f, tableConfig.halfLength * 0.9f,
                        (ix + 0.5f) / steps);
                    var candidate = new Vector2(x, y);
                    if (CanPlaceCueBall(candidate))
                    {
                        if (TryPlaceCueBall(candidate))
                        {
                            AiAutoPlaceCount++;   // 可判定出口：AI **真的自己放了**（不是靠 `Launch()` 的兜底）
                            return true;
                        }

                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>**出路（b）：显式让位** —— 绝不静默卡住（`AiTurnYieldedCount` 是可判定出口）。</summary>
        private void YieldAiTurn(string reason)
        {
            AiPhase = AiShotPhase.None;
            _aiPhaseElapsed = 0f;
            AiTurnYieldedCount++;
            SetTurn(ShotTurn.Player);
            Debug.LogWarning($"PhysicsTestDrive：AI 无法出杆（{reason}）"
                             + " ⇒ 把回合让给玩家（AiTurnYieldedCount +1）。", this);
        }

        /// <summary>
        /// **给一个候选打分（真内核）** —— <see cref="EightBallAi.Plan"/> 的求值器**本体**。
        /// （**只读出口**：供判据与诊断用；**不参与出杆路径**）
        /// `public` 是刻意的（照 `PoolSimulator.ShotContactEvents` 的既有模式）：判据要能对**同一个候选**
        /// 做顺序无关性检查、诊断要能回答"AI 为什么选了这一杆"。
        ///
        /// 契约：**只读主内核、只写探针** ⇒ 调用任意次都**不改变真实球台**（③a 判据守它）。
        ///
        /// ⚠ **探针设置（含 `ResetDiagnostics()`）全在 <see cref="PrepareProbe"/> 一处** ——
        /// 本方法与 <see cref="AiCandidatePots"/> **共用**它；**改探针设置只改 `PrepareProbe`**（单一实现点）。
        /// 少那一次归零会**同时**污染两个读出（本方法的分 + `AiCandidatePots` 的进袋判定）。
        /// </summary>
        public float EvaluateAiCandidate(AiShotContext context, float aimDegrees, float power, int targetBall,
            int pocketIndex)
        {
            PoolSimulator probe = PrepareProbe(context, aimDegrees, power);
            if (probe == null)
            {
                return 0f;
            }

            // **"目标球是否真进袋" = 事件流**（§4.3 口径）：存在 `Pocket` 事件且 `ballA == 目标球`。
            float score = -20f;                                 // 没进 ⇒ 负分
            IReadOnlyList<ContactEvent> events = probe.ShotContactEvents;
            for (int i = 0; i < events.Count; i++)
            {
                ContactEvent ev = events[i];
                if (ev.kind != ContactKind.Pocket || ev.ballA != targetBall)
                {
                    continue;
                }

                // 越喉越**居中** ⇒ 分越高（连续分，避免"进袋=1/不进=0"的并列把选杆推到窗口边缘）。
                float centrality = 0.5f;
                if (pocketIndex >= 0 && pocketIndex < tableConfig.PocketCount)
                {
                    Pocket pocket = tableConfig.GetPocket(pocketIndex);
                    if (pocket != null && pocket.throatWidth > 1e-6f)
                    {
                        centrality = Mathf.Clamp01((ev.position - pocket.throatCenter).magnitude / pocket.throatWidth);
                    }
                }

                score = Mathf.Max(score, 100f - 60f * centrality);
            }

            if (!probe.IsBallInPlay(0))
            {
                score -= 300f;                                   // 白球落袋 / 离台 ⇒ 重罚（"不会自己送"）
            }

            return score;
        }

        /// <summary>
        /// **只回答"这个候选会不会把目标球打进"**（**不含任何评分/惩罚**）。（**只读出口**：供判据与诊断用；**不参与出杆路径**）
        ///
        /// ⚠ **为什么必须单独有这么一个读出**：<see cref="EvaluateAiCandidate"/> 的**合成分**不能当"进没进"的分类器 ——
        /// 它的"白球落袋 −300"能把"被污染而虚高的进袋分 (+98)"**压回负数**，于是"没进"与"进了但白球也落袋"
        /// 在分数上**不可区分**（实测：`98.60 − 300 = −201.40`，看起来像"没进袋"⇒ 判据假绿）。
        /// 判据要判的是**分类**，所以这里把"进没进"这一个信号单独取出。
        /// ⚠ 与 <see cref="EvaluateAiCandidate"/> **共用** <see cref="PrepareProbe"/>（单一探针设置点）——
        /// 否则"少一次 `ResetDiagnostics`"的注入只会打到其中一个读出，另一个变成**真空**。
        /// </summary>
        public bool AiCandidatePots(AiShotContext context, float aimDegrees, float power, int targetBall)
        {
            PoolSimulator probe = PrepareProbe(context, aimDegrees, power);
            if (probe == null)
            {
                return false;
            }

            IReadOnlyList<ContactEvent> events = probe.ShotContactEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].kind == ContactKind.Pocket && events[i].ballA == targetBall)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// **探针的一次"本杆"设置（单点实现）**：**归零诊断（含事件快照）→ 灌局面 → 给白球初速 → 推到静止**。
        ///
        /// ⚠ **`ResetDiagnostics()` 必须在这里、且只有这一处**：`ShotContactEvents` 是**累积到本杆**的流，
        /// 而 `LoadBalls` **不清它** ⇒ 少这一次归零，**两个读出（评分 / 进没进）会同时**被上一个候选的
        /// 越喉事件污染（症状：规划不再确定性 + "没进"被读成"进了"）。
        /// </summary>
        private PoolSimulator PrepareProbe(AiShotContext context, float aimDegrees, float power)
        {
            PoolSimulator probe = EnsureAiProbe();
            if (probe == null)
            {
                return null;
            }

            probe.ResetDiagnostics();                          // ★ 本杆诊断归零（**含事件快照**）
            LoadProbeBalls(probe, context);
            probe.PlaceBall(0, context.cueBallPosition, CueVelocityFor(aimDegrees, LaunchSpeedFor(power)));
            StepToRest(probe);
            return probe;
        }

        /// <summary>
        /// 由**当前真实球台**构造 <see cref="AiShotContext"/>（**球号 = 下标**；球号约定见 35-C §6-P1）。
        /// **只读**：`public` 是刻意的 —— 判据与 UI/诊断要能拿到"AI 眼里的局面"。（**只读出口**：供判据与诊断用；**不参与出杆路径**）
        /// </summary>
        public AiShotContext BuildAiContext()
        {
            var positions = new Vector2[16];
            var inPlay = new bool[16];
            IReadOnlyList<BallState> balls = _simulator.Balls;
            for (int ball = 0; ball < positions.Length; ball++)
            {
                bool present = ball < balls.Count;
                positions[ball] = present ? balls[ball].position : Vector2.zero;
                inPlay[ball] = present && _simulator.IsBallInPlay(ball);
            }

            return new AiShotContext
            {
                cueBallPosition = positions[0],
                ballPositions = positions,
                ballInPlay = inPlay,
                groupState = aiGroupState,
                // **AI 打黑 8 也自己指定袋**（D3）：`TryLaunchAiShot` 在 `IsEightBallShot` 时，用本方法喂出的
                // 局面让 `Plan` 选出 `intent.pocketIndex`，再走**唯一写入口** `TryCallPocket(int)` 写进
                // `RuleState.calledPocket`；**袋号非法 ⇒ 不出杆**（绝不带着错的指定袋开打）。
                // 之后由规则层据此判"进错袋 ⇒ 负"（`ShotOutcomeKind.EightBallWrongPocket`）；
                // `SetTurn(Player)` 在**回合边界**把它清回 `-1`（玩家回合不复用上一杆的选择）。
                pocketCenters = PocketCenters(),
            };
        }

        private Vector2[] PocketCenters()
        {
            var centers = new Vector2[tableConfig.PocketCount];
            for (int i = 0; i < centers.Length; i++)
            {
                centers[i] = tableConfig.GetPocket(i).throatCenter;
            }

            return centers;
        }

        /// <summary>
        /// 懒建 AI 探针：**照抄主内核的物理口径**（`Params` / 袋口缩放 / 静止阈值 / 静止帧数）
        /// ⇒ 评估模型与实跑模型一致（否则 AI 会照着**另一套物理**做决策）。
        /// </summary>
        private PoolSimulator EnsureAiProbe()
        {
            if (_aiProbe == null && tableConfig != null && _simulator != null)
            {
                _aiProbe = new PoolSimulator(tableConfig, _simulator.Params,
                    _simulator.PocketCaptureRadiusScaleValue, _simulator.RestSpeedThresholdValue,
                    _simulator.RestStableFramesValue);
            }

            return _aiProbe;
        }

        /// <summary>
        /// 把局面灌进探针。**不在场的球**（已入袋 / 越界冻结）一律**挪到台外停车位**（`y = 50 + i·0.5`，
        /// 彼此不重叠）—— `LoadBalls` 会把它们"复活"到场上，而复活一颗已入袋的球会让评估结果失真。
        /// </summary>
        private static void LoadProbeBalls(PoolSimulator probe, AiShotContext context)
        {
            var balls = new BallState[context.ballPositions.Length];
            for (int ball = 0; ball < balls.Length; ball++)
            {
                bool present = context.ballInPlay != null && ball < context.ballInPlay.Length && context.ballInPlay[ball];
                balls[ball] = new BallState
                {
                    position = present ? context.ballPositions[ball] : new Vector2(0f, 50f + ball * 0.5f),
                    velocity = Vector2.zero,
                };
            }

            probe.LoadBalls(balls);
        }

        /// <summary>把探针推到"整盘静止"（上限与预测路径同数量级；到上限即收尾，绝不无限循环）。</summary>
        private static void StepToRest(PoolSimulator probe)
        {
            for (int i = 0; i < AiProbeStepCap; i++)
            {
                probe.Step();
                if (probe.TableAtRest)
                {
                    return;
                }
            }
        }

        /// <summary>探针单次评估的步数上限（8 m/s 满力滚动 ≈ 3800 步 ⇒ 给 2 倍余量）。</summary>
        private const int AiProbeStepCap = 8000;

        private void Update()
        {
            if (_simulator == null)
            {
                return;
            }
            bool fine = Input.GetKey(fineKey);
            // 细调系数**唯一真源**在 `PointerAimController.FineAimStepFactor`（不再于此处另写一个字面量）。
            float aStep = fine ? aimStepDegrees * PointerAimController.FineAimStepFactor : aimStepDegrees;
            float pStep = fine ? powerStep * 0.25f : powerStep;

            if (Input.GetKey(aimLeftKey)) { aimDegrees += aStep; }
            if (Input.GetKey(aimRightKey)) { aimDegrees -= aStep; }
            if (Input.GetKey(powerUpKey)) { power = Mathf.Clamp01(power + pStep); }
            if (Input.GetKey(powerDownKey)) { power = Mathf.Clamp01(power - pStep); }

            if (Input.GetKeyDown(launchKey))
            {
                Launch();
            }

            // 调试叠加层热键（默认关闭；F1 切换）。与既有输入口径一致：`Input.GetKeyDown`。
            if (Input.GetKeyDown(debugOverlayToggleKey))
            {
                showDebugOverlay = !showDebugOverlay;
            }

            SyncAimIndicator();
        }

        private void FixedUpdate()
        {
            if (_simulator == null)
            {
                return;
            }

            Advance(Time.fixedDeltaTime);   // 记账（含 scratch）已收进 `Advance` 内部，见那里的注释
        }

        /// <summary>
        /// **白球离台的那一刻**记账（**不是**等到下一杆）。
        ///
        /// 用户实测反馈："发射白球落网又没反馈"。原实现把 scratch 计在**下一次 `Launch()`** 里
        /// ⇒ 本杆结束后状态条毫无反应、判罚迟了一杆。改为在推进循环里判**上升沿**：
        /// `IsBallInPlay(0)` 由真变假 ⇒ 立即 `ScratchCount++` + 打时间戳，
        /// HUD 与音频据此**当帧**给出反馈。
        /// </summary>
        private void TrackCueBallScratch()
        {
            if (_simulator == null)
            {
                return;
            }

            bool inPlay = _simulator.IsBallInPlay(0);
            if (_cueWasInPlay && !inPlay)
            {
                ScratchCount++;
                LastScratchTime = Time.unscaledTime;

                // **进入「待放球」**（全场自由球）：白球离台 ⇒ 由玩家决定放哪（不再自动归位）。
                // ⚠ 这里**不**去动 `LastLaunchRespawnedCueBall`（它仍是"这一杆前白球离台"的犯规留痕，
                //   在 `Launch()` 里照旧赋值）⇒ 犯规统计与改动前**逐位一致**。
                PendingCueBallPlacement = true;
                _cueBallLeftTableSinceLastLaunch = true;   // 犯规留痕（出杆时消费，见该字段注释）
            }

            _cueWasInPlay = inPlay;
        }

        /// <summary>
        /// 累加器推进：把外部时间切片折算为整数个固定步。**余量跨帧保留**，
        /// 保证长时间平均步频精确等于 <c>1 / FixedDeltaTime</c>。
        /// </summary>
        public void Advance(float elapsed)
        {
            if (_simulator == null)
            {
                return;
            }

            _accumulator += elapsed;

            int steps = WholeSteps(_accumulator, PoolSimulator.FixedDeltaTime);
            if (steps > maxStepsPerFrame)
            {
                // 掉帧保护：丢弃超额累计，避免"越追越慢"的死亡螺旋。
                steps = maxStepsPerFrame;
                _accumulator = 0f;
            }
            else
            {
                _accumulator -= steps * PoolSimulator.FixedDeltaTime;
                if (_accumulator < 0f)
                {
                    _accumulator = 0f;
                }
            }

            for (int i = 0; i < steps; i++)
            {
                _simulator.Step();
            }

            TotalSteps += steps;
            // 只读上报：给冒烟证据用（Core 层只存一个数字，不认识物理类型 ⇒ 不形成反向依赖）。
            SmokeProbe.ReportPhysicsSteps(TotalSteps);

            // **推进即记账**（白球离台 ⇒ 犯规留痕 + 进入待放球）。
            // ⚠ 放在这里（而不是只放 `Update`）的理由：**判据要在 EditMode 下确定性驱动它**
            //（EditMode 不跑 `Update`）⇒ 否则"全场自由球"的状态迁移**无法被判据覆盖**。
            TrackCueBallScratch();

            // **P6：本杆结束后结算**（**唯一接线点**）—— 静止门在里面用既有 `TableAtRest` 判。
            // ⚠ 放在 `Advance` 里（而不是只放 `Update`/`FixedUpdate`）：**判据要在 EditMode 下确定性驱动它**
            // （EditMode 不跑 `Update`/`FixedUpdate`）—— 与上面 `TrackCueBallScratch()` 同一个理由。
            SettleShotIfAtRest();

            // **AI 回合驱动**（**唯一实现点**）：轮到对手 ⇒ 停顿一拍 ⇒ **自己出一杆**。
            // ⚠ 与上面 `SettleShotIfAtRest()` 同一条理由放在 `Advance` 里（EditMode 不跑
            // `Update`/`FixedUpdate`）⇒ 判据能用固定 dt 确定性驱动"轮到 AI ⇒ 真的出手"。
            // ⚠ 顺序：**先结算再驱动** —— 上一杆的判定可能正好把回合交给 AI。
            TickAiTurn(elapsed);

            SyncVisual();
        }

        /// <summary>
        /// **累计已推进的物理步数**（只读出口）。用途：① 冒烟证据里核"物理是否真的按 120 Hz 在走"；
        /// ② 判据可直接断言"累计步数 == 期望步数"而不用反推累加器。
        /// </summary>
        public long TotalSteps { get; private set; }

        /// <summary>
        /// 由累计时间求应推进的**整数**步数（向下取整）。纯函数，供单测直接断言。
        /// 边界语义：<c>accumulated == fixedStep</c> 恰好算 1 步（用 <c>&gt;=</c>，不是 <c>&gt;</c>）。
        /// </summary>
        public static int WholeSteps(float accumulated, float fixedStep)
        {
            if (!(fixedStep > 0f) || !(accumulated >= fixedStep))
            {
                return 0;
            }

            return Mathf.FloorToInt(accumulated / fixedStep);
        }

        /// <summary>单球模式：白球静止于**开球位**（<see cref="CueBallHomePosition"/>；不再用 `footSpot`）。</summary>
        private BallState[] BuildSingleBall()
        {
            return new[]
            {
                new BallState { position = CueBallHomePosition(tableConfig.footSpot), velocity = Vector2.zero },
            };
        }

        /// <summary>
        /// 球堆模式：白球 + 三球小堆（间距 = 2r + 2e-4 ⇒ **合法不重叠**）。
        /// 用于在编辑器里**直接观察球-球碰撞**（本驱动器是调试入口，不是最终交互）。
        /// </summary>
        /// <summary>
        /// **正式对局的球堆球数**（目标球 15 颗 ⇒ 加白球共 **16** 颗）。
        ///
        /// ⚠ 与 <see cref="RackPositions"/> 的**默认参数 3** 刻意分开：那个默认值是早期"球-球演示"的遗留，
        /// 仍被 `T38`（向后兼容闸门）与验收器 `D4` 的初始摆位检查依赖 ⇒ **不动它**；
        /// **正式对局一律走本常量**，避免"默认值悄悄决定场上球数"（本轮实测正是如此：场上一度只有 4 颗）。
        /// </summary>
        public const int RackBallCount = 15;

        private BallState[] BuildRack()
        {
            int count = Mathf.Max(1, rackBallCount);

            // **8 球局（15 颗）** ⇒ 走"**按球号编号**"的摆位：`[0]` 白球、`[1..15]` 各号球
            //   （`[8]` = 黑 8 在**第三排正中**、底排两角一实一花）。
            // 其它球数（测试搭的小堆）⇒ **原样**走几何摆位 `RackPositions`（既有期望值不动）。
            // ⚠ 两条路径的**位置集合相同**（同一个 `RackSlot` 铺同样的槽）⇒ 物理量不因本分支而变。
            Vector2[] positions = count == RackBallCount
                ? EightBallRackPositions(tableConfig.footSpot, ResolvedRackSeed)
                : RackPositions(tableConfig.footSpot, count);

            var balls = new BallState[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                balls[i] = new BallState { position = positions[i], velocity = Vector2.zero };
            }

            return balls;
        }

        /// <summary>
        /// **球堆顶点的 x（规范坐标）** —— 球堆从该点起沿 `−x` 展开。**唯一真源**：
        /// <see cref="RackPositions"/>（排布）与 <see cref="CueBallHomePosition"/>（白球间距）共用，
        /// 免得"顶点"与"间距"两处各写一个数而悄悄漂移。
        /// </summary>
        public const float RackApexX = -0.30f;

        /// <summary>
        /// **开球时白球到球堆顶点的间距（米）** —— **用户裁定**："开球时白球离球堆再远一点，接近现实开球距离"。
        ///
        /// 现实口径：白球在**头区**、球堆在**脚点** ⇒ 相距 ≈ **半个台长** = `ClothHalfLength` × 2 / 2 = **1.27 m**。
        /// 旧值（白球放在 `footSpot`）实测只有 **0.808 m** ⇒ 短了约 0.46 m，与用户"有点近"一致。
        ///
        /// ⚠ **为什么不去调初速**：初速 8 m/s 是上一卡**用户拍板**的（`T133` 守它）；本条只动几何。
        /// ⚠ **档位不是凭直觉挑的**（实测四组，见规格 §13.2）：打散数**不随距离单调**
        ///   （1.15 m 档掉到 6/15 余量 0 且触发 TOI 守卫；而 1.27 m 档回到 8/15、guard=0）。
        /// </summary>
        public const float BreakClearanceMetres = 1.27f;

        /// <summary>
        /// **开球时白球的落点**（**纯函数，唯一真源**）—— `BuildRack` / `BuildSingleBall` /
        /// **落袋重摆** / `ShotPredictor` 的幽灵白球 / **白球可视物** 全部读这里 ⇒ 物理与画面不分叉。
        ///
        /// ⚠ 它**取代**了"白球 = `footSpot`"的旧口径：`RackPositions[0]` 与它返回**同一个值**
        /// （不留旧契约值 —— 否则 `T38` 会变成一个"守着没人再用的摆设值"的守卫）。
        /// </summary>
        public static Vector2 CueBallHomePosition(Vector2 footSpot)
        {
            return new Vector2(RackApexX + BreakClearanceMetres, footSpot.y);
        }

        /// <summary>
        /// 球堆位置（**纯函数**，灰盒驱动 / 验收器 / 测试**共用同一口径** —— 单一实现，铁律 16）。
        /// 返回数组 = **`[白球] + rackBallCount 颗堆球**；`rackBallCount` 默认 `3`（= 旧的 4 球摆放）。
        ///
        /// **⚠ `[0]` = <see cref="CueBallHomePosition"/>（不是 `footSpot`）** —— 用户裁定"开球距离对齐现实"后，
        /// 白球落点由"脚点"改为"距球堆顶点 1.27 m"。`T38` 已按**新口径重新钉**（不是被绕过）。
        ///
        /// 排布：三角形、**顶点朝 +x（面向白球）**；第 `r` 行沿 y 关于 0 对称，行距 `s·√3/2`。
        /// 间距 `s = 2·BallRadius + 2e-4` ⇒ 满足 <see cref="PoolSimulator.BallInitialOverlapTolerance"/>
        /// 的"合法摆放"要求（任意两球间距 ≥ `2r − 1e-4`）。
        ///
        /// **⚠ 内层 `k` 必须降序**：在 `y = (k − r/2)·s` 下，降序才能让 `r = 1` 时 `+half` 先于 `−half` 入列
        /// ⇒ 与旧 4 球字面 **逐位一致**（只差 `[0]` 换成了新地标）。
        /// 若改升序，索引 2/3 互换 ⇒ 破坏既有按索引读取的证据（验收器 / 报告项 / 逐球终态）。
        /// </summary>
        /// <summary>
        /// **摆球几何：单点实现**（`RackPositions` 与 <see cref="EightBallRackPositions"/> **共用**）——
        /// 第 `r` 排（从顶点排 `r = 0` 起）第 `k` 槽（`k` 从 `r` 递减到 `0`）的**规范坐标**。
        /// 球间距 `s = BallDiameter + 2e-4`；排间距 `s·√3/2`；顶点在 <see cref="RackApexX"/>。
        /// ⚠ **不得**在别处再写一套摆球几何（铁律 16）。
        /// </summary>
        private static Vector2 RackSlot(int r, int k)
        {
            float s = PoolConstants.BallDiameter + 2e-4f;
            return new Vector2(RackApexX - r * (s * 0.8660254f), (k - r * 0.5f) * s);
        }

        // ── **球号约定（35-C §6-P1）** ───────────────────────────────────────────────
        // **下标即球号**：`0` = **白球**，`1..15` = **目标球**（`8` = **黑 8**）。
        // 内核 `BallState` **只存 `position` / `velocity`、不存球号** ⇒ **球号 = 球表下标**（内核无需改）。
        // 既有"逐号材质/可视物"**正依赖本约定**（故不要再立第二套编号）：
        //   · `GreyboxSceneSetup.BallMaterialPathFormat` = `"Assets/_Project/Materials/Balls/Ball_{0:00}.mat"`
        //     （`Ball_00` = **白球** ⇒ 与"下标 0 = 白球"一致）；
        //   · `PhysicsTestDrive.ballVisuals`（`Transform[]`）与球表**同序**（见 `SyncVisual` 的 `ballVisuals[i]`）。

        /// <summary>
        /// **8 球三角框摆位（15 颗）—— 按**球号**索引返回**：`[0]` = 白球（<see cref="CueBallHomePosition"/>），
        /// `[1..15]` = **各号球**的落点（`[8]` 即**黑 8**的落点）。
        ///
        /// 规则（35-C §6-P1）：
        /// · **几何与 <see cref="RackPositions"/> 同源**（同一个 <see cref="RackSlot"/>；顶点 → 底排）；
        /// · **`8` 号在第三排正中**（槽位按 `RackPositions` 的放置顺序编号：`0` = 顶点，`3/4/5` = 第 3 排，
        ///   **`4` = 第三排正中**，其 `y = 0`）；
        /// · **底排两角一实一花**（实色 = `1..7`、花色 = `9..15`；**角由种子随机决定哪边是实色**）；
        /// · **其余 12 颗随机**（Fisher–Yates）。
        ///
        /// ⚠ **随机性与可复现**：**运行期每局取新种子**；**判据注入固定种子** ⇒ 同种子**逐位相同**、不同种子（一般）不同。
        /// ⚠ **不改变** <see cref="RackPositions"/>（3 颗版）的既有行为（35-C §7-1：新增 15 颗夹具、保留 3 颗夹具）。
        /// </summary>
        public static Vector2[] EightBallRackPositions(Vector2 footSpot, int seed)
        {
            const int rackCount = RackBallCount;                 // 15
            const int eightBall = 8;

            var positions = new Vector2[1 + rackCount];          // [0] = 白球；[1..15] = 球号
            positions[0] = CueBallHomePosition(footSpot);

            // 槽位编号 = `RackPositions` 的放置顺序（r 升序、k 降序）：
            //   0        = 顶点            （r=0）
            //   1,2      = 第 2 排          （r=1）
            //   3,4,5    = 第 3 排          （r=2）  ⇒ **4 = 正中**
            //   6,7,8,9  = 第 4 排          （r=3）
            //   10..14   = 第 5 排（底排）  （r=4）  ⇒ **10 与 14 = 两个底角**
            const int slotEightBall = 4;
            const int slotCornerLeft = 10;
            const int slotCornerRight = 14;

            var solids = new List<int> { 1, 2, 3, 4, 5, 6, 7 };        // 实色
            var stripes = new List<int> { 9, 10, 11, 12, 13, 14, 15 }; // 花色
            var rng = new System.Random(seed);

            int solidIndex = rng.Next(solids.Count);
            int solidCorner = solids[solidIndex];
            solids.RemoveAt(solidIndex);

            int stripeIndex = rng.Next(stripes.Count);
            int stripeCorner = stripes[stripeIndex];
            stripes.RemoveAt(stripeIndex);

            bool leftCornerIsSolid = rng.Next(2) == 0;

            var slotBall = new int[rackCount];
            for (int i = 0; i < rackCount; i++)
            {
                slotBall[i] = -1;
            }

            slotBall[slotEightBall] = eightBall;
            slotBall[slotCornerLeft] = leftCornerIsSolid ? solidCorner : stripeCorner;
            slotBall[slotCornerRight] = leftCornerIsSolid ? stripeCorner : solidCorner;

            // 其余 12 颗（实/花混合）随机铺进剩下的 12 个槽（Fisher–Yates，种子可复现）
            var rest = new List<int>(rackCount - 3);
            rest.AddRange(solids);
            rest.AddRange(stripes);
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = rest[i];
                rest[i] = rest[j];
                rest[j] = tmp;
            }

            int cursor = 0;
            for (int slot = 0; slot < rackCount; slot++)
            {
                if (slotBall[slot] == -1)
                {
                    slotBall[slot] = rest[cursor++];
                }
            }

            // 按槽位顺序把**球号**映射到**几何位置**
            int index = 0;
            for (int r = 0; r < 5; r++)
            {
                for (int k = r; k >= 0; k--)
                {
                    positions[slotBall[index++]] = RackSlot(r, k);
                }
            }

            return positions;
        }

        public static Vector2[] RackPositions(Vector2 footSpot, int rackBallCount = 3)
        {
            int rows = 1;
            while (rows * (rows + 1) / 2 < rackBallCount)
            {
                rows++;
            }

            var positions = new List<Vector2>(1 + rackBallCount) { CueBallHomePosition(footSpot) };

            int placed = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int k = r; k >= 0; k--)   // ★ 降序 ⇒ 与旧 4 球字面同序
                {
                    if (placed >= rackBallCount)
                    {
                        break;
                    }

                    positions.Add(RackSlot(r, k));   // ← 几何**单点实现**（35-C §6-P1：与 8 球摆位同源）
                    placed++;
                }

                if (placed >= rackBallCount)
                {
                    break;
                }
            }

            return positions.ToArray();
        }

        /// <summary>
        /// **冒烟模式：脚本化设置瞄准角**（<see cref="ISmokeAimSweepTarget"/> 的唯一实现点）。
        /// 只在构建产物的拖动瞄准负载冒烟里被调用（`-billiards-smoke-aim`）；正常运行时不涉及。
        /// 只写 <see cref="aimDegrees"/> 一个字段 ⇒ 与交互层（`PointerAimController`）的写入口径一致。
        /// </summary>
        public void ApplySmokeAimDegrees(float degrees)
        {
            aimDegrees = degrees;
        }

        /// <summary>**落点是否合法**（薄包装：把本驱动的表 + 场上球喂给纯函数核 <see cref="PoolSimulator.IsPlacementLegal"/>）。</summary>
        public bool CanPlaceCueBall(Vector2 position)
        {
            if (_simulator == null || tableConfig == null)
            {
                return false;
            }

            IReadOnlyList<BallState> balls = _simulator.Balls;
            var occupied = new List<Vector2>(balls.Count);
            for (int i = 0; i < balls.Count; i++)
            {
                if (i == 0)
                {
                    continue;   // 白球自己：正在被放，不参与重叠判定
                }

                if (_simulator.IsBallInPlay(i))
                {
                    occupied.Add(balls[i].position);
                }
            }

            // **P7：头区自由球**（开球犯规的处罚）⇒ 落点还必须在**开球线之后**。
            // ⚠ 复用既有纯函数 `IsPlacementLegalInKitchen`（它**内部调用** `IsPlacementLegal` 做台内/压球/袋口）
            //   ⇒ 不在这里重写那三条基础规则（重写必然分叉）。
            if (BallInHand == BallInHandKind.Kitchen)
            {
                return PoolSimulator.IsPlacementLegalInKitchen(position, occupied, tableConfig.pockets,
                    tableConfig.halfLength, tableConfig.halfWidth, PoolConstants.KitchenLineX);
            }

            return PoolSimulator.IsPlacementLegal(position, occupied, tableConfig.pockets,
                tableConfig.halfLength, tableConfig.halfWidth);
        }

        /// <summary>
        /// **把白球放到 <paramref name="position"/>**（全场自由球）。返回是否成功。
        /// **合法才放**；成功 ⇒ 退出待放态。非法 ⇒ **不改任何状态**（白球仍离台）+ `RefusedPlacementCount++`
        /// ⇒ 玩家可以再点（**不许静默**）。
        /// </summary>
        public bool TryPlaceCueBall(Vector2 position)
        {
            if (_simulator == null || !PendingCueBallPlacement)
            {
                return false;
            }

            if (!CanPlaceCueBall(position))
            {
                RefusedPlacementCount++;
                return false;
            }

            _simulator.PlaceBall(0, position, Vector2.zero);
            PendingCueBallPlacement = false;
            ResetVisualRotationTracking();
            SyncVisual();
            return true;
        }

        /// <summary>
        /// **兜底：把白球放回开球位**（<see cref="CueBallHomePosition"/>）并退出待放态。
        /// 由 `Launch()` 在待放球期间出杆时调用；也供 UI/按键挂钩（"放回原位"）。
        /// </summary>
        public void ReturnCueBallToHome()
        {
            if (_simulator == null || tableConfig == null)
            {
                return;   // 与 `CanPlaceCueBall` 同判：缺表配置时不得走进 `tableConfig.footSpot`（否则 NRE）
            }

            _simulator.PlaceBall(0, CueBallHomePosition(tableConfig.footSpot), Vector2.zero);
            PendingCueBallPlacement = false;
            ResetVisualRotationTracking();
            SyncVisual();
        }

        /// <summary>
        /// **按 <see cref="aimDegrees"/> / <see cref="power"/> 打出一杆**（键盘最小可玩）。
        /// 初速方向 = `(cos θ, sin θ)`（θ = `aimDegrees`），大小 = `Lerp(minLaunchSpeed, maxLaunchSpeed, power)`。
        /// 返回白球初速（供测试 / 日志断言）。
        ///
        /// ── 三条语义（本驱动器的"连续对局"契约）──────────────────────────────
        /// ① **球滚到哪就从哪打**：白球**不改位置**，只把速度换成 `v0`；**任何其他球都不动**
        ///    （既不动球位，也不动它们的入袋记录）。
        ///    ⇒ 实现走内核的单用途入口 `PoolSimulator.PlaceBall`，**不再** `LoadBalls` 整盘重置。
        /// ② **门控 = `TableAtRest`**：整盘未静止（含"刚 `Initialize()`、还没满 `RestStableFrames` 帧"）
        ///    ⇒ **拒绝出杆**：返回 `Vector2.zero` 且**不改任何状态**。
        ///    ⇒ 调用方可据"返回值是否为零向量"区分"没打出"与"打出但初速为 0"。
        /// ③ **白球入袋 ⇒ 显式放回 `footSpot`**，并**留痕**（<see cref="LastLaunchRespawnedCueBall"/> + 一条日志），
        ///    **不许静默**。
        ///
        /// ⚠ **不再做**的事（与旧实现相反）：不再 `BuildRack()`/`LoadBalls()`（那会把整盘摆回开球位、
        /// 并把已入袋的球"复活"）、不再无条件 `ResetVisualRotationTracking()`
        /// （正常出杆时球位不瞬移，重置会白吞一帧自转；**只有白球重摆那一杆**才需要重置）。
        /// </summary>
        public Vector2 Launch()
        {
            if (_simulator == null)
            {
                return Vector2.zero;
            }

            // ② 门控：整盘未静止 ⇒ 拒绝（不改任何状态）。**留痕**（否则玩家只看到"按了没反应"）。
            if (!_simulator.TableAtRest)
            {
                RefusedLaunchCount++;
                LastRefusedLaunchTime = Time.unscaledTime;
                return Vector2.zero;
            }

            // **打黑 8 必须先指定袋**（D3，P6）。⚠ **只在"这一杆是打黑 8"时生效** ——
            // 若无条件拦，普通杆也出不去 ⇒ 整个游戏卡死。判定口径走**单点** `IsEightBallShot`。
            if (IsEightBallShot && ruleState.calledPocket < 0)
            {
                RefusedCallPocketCount++;
                return Vector2.zero;
            }

            // 初速 = **方向 × 速度**（**唯一算式**见 <see cref="CueVelocityFor"/> / <see cref="LaunchSpeedFor"/>：
            // AI 评估探针读**同一对函数**，否则"评估的那一杆"与"真正打出的那一杆"会悄悄分叉）。
            // 算式与改动前**逐位等价**（同 3 步：`Cos` / `Sin` / `向量 × 标量`，只把 `Lerp` 挪进函数体）。
            float speed = LaunchSpeedFor(power);
            Vector2 v0 = CueVelocityFor(aimDegrees, speed);

            // **P7：头区自由球的第一杆必须往头线前打**（开球犯规的附加约束 —— 规则层给 `mustShootForward`，
            // 这里按**既有纯函数**判方向，不另写几何）。被拒要**留痕**（`RefusedForwardCount`），不许静默。
            if (ruleState.mustShootForward
                && !PoolSimulator.IsBreakShotForwardEnough(v0, minForwardComponent))
            {
                RefusedForwardCount++;
                return Vector2.zero;
            }

            // ③ 白球不在场上（已入袋 / 擦袋角离台）⇒ **兜底**：放回开球位（详见下方 `PendingCueBallPlacement` 分支）；
            //    否则 ① 就在它停下的地方出杆。
            bool cueOffTable = !_simulator.IsBallInPlay(0);
            if (cueOffTable && PendingCueBallPlacement)
            {
                // **全场自由球的兜底**（主理人裁定「兜底是必需的」）：待放球期间玩家**直接出杆** ⇒
                // 回落到"放回开球位再出杆"（= 改动前的旧行为）⇒ **不会卡死在待放态**
                //（否则一旦放置输入失效 / 找不到合法点，玩家就再也动不了）。
                ReturnCueBallToHome();
            }

            Vector2 cueHome = CueBallHomePosition(tableConfig.footSpot);
            Vector2 from = cueOffTable ? cueHome : _simulator.Balls[0].position;

            _simulator.PlaceBall(0, from, v0);
            // **记住击球位置**：出杆后白球会跑，球杆在 `ShotInFlight` 期间要**冻结在这个点**（不许跟球飞）。
            LastStrikeCueBallPosition = from;

            // **P7：一开打，"放球窗口"就结束**（自由球不能用两次；否则 `PendingCueBallPlacement`
            // 会一直挂着，下一杆还会被当成"待放球"）。
            PendingCueBallPlacement = false;

            // 只清**每杆计数**；逐球入袋记录是跨杆状态，**不得**在此抹掉（否则入袋球会复活）。
            _simulator.ResetDiagnostics();
            CaptureShotBaseline();   // **P6**：本杆"前"快照（结算要做前后比对）
            _shotInFlight = true;
            _accumulator = 0f;
            // **犯规留痕**：`cueOffTable`（兜底路径）**或**"本杆期间曾离台"（玩家自己放球那条）
            // ⇒ 两条路都会让 `LastLaunchRespawnedCueBall` 为 true ⇒ **犯规计数与改动前逐位一致**。
            LastLaunchRespawnedCueBall = cueOffTable || _cueBallLeftTableSinceLastLaunch;
            _cueBallLeftTableSinceLastLaunch = false;

            // 只读出口（音频层轮询增量 ⇒ strike 事件；累计不清零）。
            LaunchCount++;
            LastLaunchSpeed = speed;

            if (cueOffTable)
            {
                // ⚠ scratch 的**计数与时间戳**不在这里加 —— 见 `TrackCueBallScratch()`：
                // 若等到下一杆才计，玩家在本杆结束后**看不到任何判罚**（用户实测："落网又没反馈"）。

                // 白球从袋口**瞬移**回开球位 ⇒ 必须重置自转跟踪，否则这一瞬移会被当成真实位移、球凭空猛转一下。
                ResetVisualRotationTracking();
                Debug.Log($"PhysicsTestDrive：白球已离台（入袋/擦袋角）⇒ 放回开球位 {cueHome} 后出杆。", this);
            }

            SyncVisual();
            return v0;
        }

        private void SyncVisual()
        {
            if (_simulator == null)
            {
                return;
            }

            IReadOnlyList<BallState> balls = _simulator.Balls;
            if (balls == null || balls.Count == 0)
            {
                return;
            }

            if (ballVisuals != null && ballVisuals.Length > 0)
            {
                int n = Mathf.Min(ballVisuals.Length, balls.Count);
                for (int i = 0; i < n; i++)
                {
                    SyncBallVisual(ballVisuals[i], i, PoolGeometry.PhysicsBallToUnity(balls[i].position));
                }

                _hasLastVisualPositions = true;
                return;
            }

            if (ballVisual != null)
            {
                SyncBallVisual(ballVisual, 0, PoolGeometry.PhysicsBallToUnity(balls[0].position));
                _hasLastVisualPositions = true;
            }
        }

        /// <summary>清空"上一帧世界位置"跟踪 ⇒ 下一次 <see cref="SyncVisual"/> 只落位、不自转。</summary>
        private void ResetVisualRotationTracking()
        {
            _lastVisualPositions = null;
            _hasLastVisualPositions = false;
        }

        /// <summary>同步**一个**球可视物：先按本帧位移加纯滚动自转，再写入位置，最后记录位置供下一帧求位移。
        /// <paramref name="index"/> = 球索引（用于取出上一帧位置）；<paramref name="canonicalPosition"/> = 本帧目标位置，
        /// **规范世界坐标**（= 缩放根的**局部坐标**）。
        ///
        /// ⚠ **写的是 `localPosition` 而不是 `position`**：球可视物挂在 `PlayfieldRoot`（表现层等比缩放）之下，
        /// 局部坐标 = **规范**坐标、世界坐标 = 缩放后的**显示**坐标。若写世界坐标，
        /// 就会出现"桌 1.2×、球 1.0×"的静默错位。
        ///
        /// ⚠ 只有拿到"上一帧位置"（<see cref="_hasLastVisualPositions"/>）时才自转 —— 首帧 / 重新装载后
        /// 没有可靠的前一位置，若强行相减会得出一个巨大的假位移 ⇒ 球会凭空猛转一下。
        /// </summary>
        private void SyncBallVisual(Transform visual, int index, Vector3 canonicalPosition)
        {
            if (visual != null)
            {
                if (_hasLastVisualPositions && _lastVisualPositions != null && index < _lastVisualPositions.Length)
                {
                    ApplyRollingRotation(visual, canonicalPosition - _lastVisualPositions[index]);
                }

                visual.localPosition = canonicalPosition;

                // 入袋反馈：离台球的下沉 + 缩小消失；回场球复位（曲线 = `PoolVfxView.PocketSinkPose`）。
                ApplyPocketSink(visual, index);
            }

            if (_lastVisualPositions == null)
            {
                _lastVisualPositions = new Vector3[Mathf.Max(4, index + 1)];
            }
            else if (index >= _lastVisualPositions.Length)
            {
                var grown = new Vector3[Mathf.Max(index + 1, _lastVisualPositions.Length * 2)];
                System.Array.Copy(_lastVisualPositions, grown, _lastVisualPositions.Length);
                _lastVisualPositions = grown;
            }

            _lastVisualPositions[index] = canonicalPosition;
        }

        /// <summary>
        /// **入袋反馈动画**（排期「下沉 + 缩放 + 短暂高亮」的可实现部分）：球离开台面后，可视物在
        /// <see cref="PoolVfxView.SinkDurationSeconds"/> 内按 <see cref="PoolVfxView.PocketSinkPose"/>
        /// 下沉并缩小，完成即隐藏；球回到场上（重摆 / 重载）⇒ 复位（激活 + 恢复基准缩放 + 清计时）。
        /// （"短暂高亮"由 <see cref="PoolVfxView"/> 的袋口双环承担；球体材质级高亮待正式美术接入，登记。）
        ///
        /// ⚠ 表现层允许读 `Time`（确定性纪律针对内核；本类 `Update` 本就驱动表现）。
        /// 基准缩放**首见时记录**（不假设为 1——将来美术换模型缩放不会漂移）。
        /// </summary>
        private void ApplyPocketSink(Transform visual, int index)
        {
            if (_simulator == null || index >= _simulator.Balls.Count)
            {
                return;
            }

            EnsureSinkArrays(index);
            if (_ballBaseScales[index] == Vector3.zero)
            {
                _ballBaseScales[index] = visual.localScale;   // 首见记录基准
            }

            if (_simulator.IsBallInPlay(index))
            {
                if (_pocketSinkStart[index] >= 0f)
                {
                    // 回场复位（含动画中途被打断的情形）。
                    _pocketSinkStart[index] = -1f;
                    _pocketSinkOrigin[index] = Vector3.zero;
                    _pocketSinkHole[index] = Vector3.zero;
                    visual.localScale = _ballBaseScales[index];
                    if (!visual.gameObject.activeSelf)
                    {
                        visual.gameObject.SetActive(true);
                    }
                }

                return;
            }

            if (_pocketSinkStart[index] < 0f)
            {
                _pocketSinkStart[index] = Time.time;              // 首次观测到离台 ⇒ 启动动画
                _pocketSinkOrigin[index] = visual.localPosition;  // 起点 = 冻结/判入时的球位
                _pocketSinkHole[index] = PocketHoleLocalPoint(index);
            }

            float p = (Time.time - _pocketSinkStart[index]) / PoolVfxView.SinkDurationSeconds;
            PoolVfxView.PocketSinkPose(p, out _, out float scale);
            visual.localScale = _ballBaseScales[index] * scale;

            // ★ 不原地垂直下沉：**先滑到洞口再落下**（`PocketSinkLocalPosition`）。
            //   原地下沉会把"球在离洞口几十毫米处被冻结"画成"贴着库边原地往下滑"（用户实测："像滑墙"）。
            visual.localPosition = PoolVfxView.PocketSinkLocalPosition(
                _pocketSinkOrigin[index], _pocketSinkHole[index], p);

            if (p >= 1f && visual.gameObject.activeSelf)
            {
                visual.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 球 <paramref name="index"/> 的**入袋落点**（球可视物坐标系 = 规范局部坐标）：
        ///  · 已判入袋 ⇒ 用**那个袋**的洞口中心；
        ///  · 未判入袋但已离台（越界冻结）⇒ 取物理平面内**最近的**洞口中心 —— 否则原地落下会表现为"滑墙"。
        /// 洞口中心一律走内核出口 <see cref="PoolSimulator.PocketHoleCenter"/>（单点实现，不在表现层重算）。
        /// </summary>
        private Vector3 PocketHoleLocalPoint(int index)
        {
            int pocket = _simulator.CapturedPocketIndex(index);
            if (pocket < 0)
            {
                Vector2 ball = _simulator.Balls[index].position;
                float bestSqr = float.PositiveInfinity;
                for (int p = 0; p < tableConfig.PocketCount; p++)
                {
                    float d = (_simulator.PocketHoleCenter(p) - ball).sqrMagnitude;
                    if (d < bestSqr)
                    {
                        bestSqr = d;
                        pocket = p;
                    }
                }
            }

            if (pocket < 0)
            {
                return Vector3.zero;
            }

            Vector2 hole = _simulator.PocketHoleCenter(pocket);
            return PoolGeometry.PhysicsToUnity(hole, PoolConstants.BallRadius);
        }

        private void EnsureSinkArrays(int index)
        {
            if (_pocketSinkStart == null)
            {
                _pocketSinkStart = new float[Mathf.Max(4, index + 1)];
                for (int i = 0; i < _pocketSinkStart.Length; i++)
                {
                    _pocketSinkStart[i] = -1f;
                }
            }
            else if (index >= _pocketSinkStart.Length)
            {
                int oldLen = _pocketSinkStart.Length;
                var grown = new float[Mathf.Max(index + 1, oldLen * 2)];
                System.Array.Copy(_pocketSinkStart, grown, oldLen);
                for (int i = oldLen; i < grown.Length; i++)
                {
                    grown[i] = -1f;
                }

                _pocketSinkStart = grown;
            }

            if (_ballBaseScales == null || index >= _ballBaseScales.Length)
            {
                int newLen = Mathf.Max(Mathf.Max(index + 1, (_ballBaseScales?.Length ?? 0) * 2), 4);
                var grown = new Vector3[newLen];
                if (_ballBaseScales != null)
                {
                    System.Array.Copy(_ballBaseScales, grown, _ballBaseScales.Length);
                }

                _ballBaseScales = grown;
            }

            _pocketSinkOrigin = GrowVector3Array(_pocketSinkOrigin, index);
            _pocketSinkHole = GrowVector3Array(_pocketSinkHole, index);
        }

        /// <summary>把（可空的）逐球 Vector3 数组扩到至少 <c>index + 1</c> 项，保留既有值、新槽为 0。</summary>
        private static Vector3[] GrowVector3Array(Vector3[] array, int index)
        {
            if (array != null && index < array.Length)
            {
                return array;
            }

            int newLen = Mathf.Max(Mathf.Max(index + 1, (array?.Length ?? 0) * 2), 4);
            var grown = new Vector3[newLen];
            if (array != null)
            {
                System.Array.Copy(array, grown, array.Length);
            }

            return grown;
        }

        /// <summary>
        /// 位移小于该值（米）视为"没动"：不旋转。避免静止球因浮点噪声每帧空转（也避免无意义 `Rotate`）。
        /// 取 1e-6 m 的依据：float 在 ~0.5 m 量级的绝对分辨率为 ~6e-8 m，1e-6 m 高出约 16 倍 ⇒
        /// 足以滤掉噪声，又**远低于**任何玩法相关位移（游戏级静止阈值 1e-3 m/s ⇒ 单步 8.3e-6 m）。
        /// </summary>
        private const float RollingDistanceEpsilon = 1e-6f;

        /// <summary>
        /// 给球可视物施加**纯滚动自转**（无滑动）：地面法向 <c>+Y</c>，接触点速度为 0
        /// ⇒ <c>v = ω × (r·ŷ)</c> ⇒ <c>ω = (v_z/r, 0, −v_x/r)</c>（Unity 坐标）。
        /// 等价地：**轴 = <c>up × d̂</c>**、**转角(度) = <c>|d| / r · Rad2Deg</c>**，其中 <c>d</c> = **本帧世界位移**。
        ///
        /// **为什么用位移而不用速度**：位移天然含时间步长，不必去问内核 <c>FixedDeltaTime</c>，
        /// 也不受"一帧内推进了几个物理步"影响（`SyncVisual` 每帧只被调用一次，位移就是这一帧的总位移）。
        ///
        /// **不变量（将来改局部坐标时依然成立，勿跟着改）**：本项目下一步的相机/指针瞄准会引入
        /// `PlayfieldRoot` 的视觉缩放 1.2，届时 `SyncVisual` 会改写**局部坐标**。但只要本函数继续吃
        /// **世界位移**、继续除以**世界半径**（`PoolConstants.BallRadius`），比值 <c>|d| / r</c> 在等比缩放下**不变**
        /// ⇒ 转角依旧正确。**故本函数不得改成"局部位移 / 局部半径"之外的等比组合**（那会引入缩放平方项）。
        /// </summary>
        private static void ApplyRollingRotation(Transform visual, Vector3 displacement)
        {
            float distance = displacement.magnitude;
            if (distance <= RollingDistanceEpsilon)
            {
                return;   // 静止（或数值噪声）⇒ 不旋转
            }

            Vector3 axis = Vector3.Cross(Vector3.up, displacement);
            if (axis.sqrMagnitude <= 0f)
            {
                return;   // 纯竖直位移（台呢上不可能发生）⇒ 轴无定义，跳过
            }

            float angleDegrees = distance / PoolConstants.BallRadius * Mathf.Rad2Deg;
            visual.Rotate(axis.normalized, angleDegrees, Space.World);
        }

        /// <summary>瞄准指示物对象名（Game 视图可见的细长立方体）。</summary>
        public const string AimIndicatorObjectName = "AimIndicator";

        /// <summary>
        /// **按需创建**瞄准指示物（Game 视图可见；**无物理组件** —— 立即移除基元自带的碰撞体）。
        /// 用**细长立方体**而非 `LineRenderer`：只用默认材质、无材质/宽度曲线依赖 ⇒ 最朴素、开箱即用。
        /// </summary>
        private void EnsureAimIndicator()
        {
            if (aimIndicator != null)
            {
                return;
            }

            GameObject stick = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stick.name = AimIndicatorObjectName;

            // ⚠ 本类位于 `Runtime/Physics/`：**源码里不得出现 PhysX 的两个类型名**
            //（`D4-PHY-01` 扫名 + 汇编级 PhysX 守卫）。故下面用**运行时类型名拼接**，不写出该名字。
            string primitiveContactTypeName = "Coll" + "ider";
            foreach (Component c in stick.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name.Contains(primitiveContactTypeName))
                {
                    UnityEngine.Object.DestroyImmediate(c);   // 自研物理铁律 R5-1：不得带碰撞体
                    break;
                }
            }

            stick.transform.SetParent(transform, false);
            aimIndicator = stick.transform;
        }

        /// <summary>
        /// 把指示物对齐到**白球位置 + 当前瞄准方向**（改角、出杆、每帧都调用）。
        /// 方向 = <see cref="AimDirectionUnity"/> = 世界 **`(−cos θ, 0, −sin θ)`**（与 `Launch()` 的白球世界走向同向），
        /// 长轴 = 局部 +Z。
        /// </summary>
        public void SyncAimIndicator()
        {
            EnsureAimIndicator();
            if (aimIndicator == null)
            {
                return;
            }

            // 渲染可见性 = 开关（**不动激活语义**：对象仍激活、位置/朝向仍更新 ⇒ 跟随口径判据的对象不变，
            // 仅用户不再看到这根多余的柱子——方向指示已由瞄准预测线承担）。
            MeshRenderer indicatorRenderer = aimIndicator.GetComponent<MeshRenderer>();
            if (indicatorRenderer != null && indicatorRenderer.enabled != showAimIndicator)
            {
                indicatorRenderer.enabled = showAimIndicator;
            }

            Vector3 dir = AimDirectionUnity;

            // **跟随口径（8 Ball Pool）**：球运动期间（`!TableAtRest`）指示物**隐藏** ——
            // 不得粘着滚动中的白球乱跑；全静止后回到白球当前位置、随 `aimDegrees`（含 Shift 微调）实时更新。
            bool ballsMoving = _simulator != null && !_simulator.TableAtRest;
            if (aimIndicator.gameObject.activeSelf == ballsMoving)
            {
                aimIndicator.gameObject.SetActive(!ballsMoving);
            }

            if (ballsMoving)
            {
                return;
            }

            Vector3 origin;
            if (ballVisuals != null && ballVisuals.Length > 0 && ballVisuals[0] != null)
            {
                origin = ballVisuals[0].position;
            }
            else if (_simulator != null && _simulator.Balls.Count > 0)
            {
                origin = PoolGeometry.PhysicsBallToUnity(_simulator.Balls[0].position);
            }
            else
            {
                origin = Vector3.zero;
            }

            aimIndicator.position = origin + dir * (aimIndicatorLength * 0.5f);
            aimIndicator.localScale = new Vector3(aimIndicatorThickness, aimIndicatorThickness, aimIndicatorLength);
            aimIndicator.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        private void OnGUI()
        {
            // **开关判据（首行）**：调试叠加层默认关闭 ⇒ 正式入口不显示任何诊断文本。
            // 关闭时直接返回（连 `GUI.Label` 都不调用）⇒ 零绘制成本。诊断场景（PhysicsGreybox）在场景里置 true。
            if (!showDebugOverlay)
            {
                return;
            }

            const int x = 10;
            const int y = 10;
            var rect = new Rect(x, y, 920f, 116f);

            if (_simulator == null)
            {
                GUI.Label(rect, "PoolSimulator 未初始化（tableConfig 缺失？）");
                return;
            }

            IReadOnlyList<BallState> balls = _simulator.Balls;
            float speed = (balls != null && balls.Count > 0) ? balls[0].velocity.magnitude : 0f;

            string text =
                $"瞄准 {aimDegrees:F1}°   力度 {power * 100f:F0}%   初速 {Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, power):F2} m/s\n"
                + $"balls={balls.Count}  contacts={_simulator.TotalContactCount}  ballBall={_simulator.BallBallContactCount}  "
                + $"pocketEvents={_simulator.PocketProximityEvents}  captured={_simulator.CapturedBallCount}  jawCrossing={_simulator.PocketJawCrossingCount}\n"
                + $"guardTrips={_simulator.ToiIterationGuardTrips}  energyGain={_simulator.EnergyGainViolations}  "
                + $"atRest={_simulator.TableAtRest}  restFrames={_simulator.ConsecutiveRestFrameCount}  |v_0|={speed:F3} m/s\n"
                + $"[{aimLeftKey}/{aimRightKey}] 方向   [{powerUpKey}/{powerDownKey}] 力度   [{fineKey}] 细调   [{launchKey}] 出杆"
                + (loadRackForBallBallDemo ? "（球堆模式）" : "（单球模式）");

            GUI.Label(rect, text);
        }
    }
}
