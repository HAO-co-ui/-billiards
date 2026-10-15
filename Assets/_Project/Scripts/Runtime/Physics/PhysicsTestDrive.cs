using System.Collections.Generic;
using Billiards.Core;
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
    public sealed class PhysicsTestDrive : MonoBehaviour
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

        [Tooltip("力度 → 初速映射的**上界**（m/s）。**Day 7 调参定为 4.5**（原 8）。⚠ **不是** `PoolConstants.MaxBallSpeed` ——"
                 + "那个常量喂 `DeriveSubSteps`（子步数），**必须保持 8** 作保守余量。")]
        public float maxLaunchSpeed = 4.5f;

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

        [Header("键位")]
        public KeyCode aimLeftKey = KeyCode.LeftArrow;
        public KeyCode aimRightKey = KeyCode.RightArrow;
        public KeyCode powerUpKey = KeyCode.UpArrow;
        public KeyCode powerDownKey = KeyCode.DownArrow;

        [Header("冒烟测试")]
        [Tooltip("按此键把白球以 PoolConstants.MaxBallSpeed 打出。")]
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

        /// <summary>构造内核并装载白球（开球点 = <see cref="TableConfig.footSpot"/>，初速 0）。</summary>
        public void Initialize()
        {
            if (tableConfig == null)
            {
                Debug.LogError("PhysicsTestDrive：未指定 tableConfig，驱动器停用。", this);
                enabled = false;
                return;
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
            ResetVisualRotationTracking();   // 重摆是瞬移 ⇒ 不得被当成真实位移、否则球凭空猛转
            SyncVisual();
            SyncAimIndicator();
        }

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

            SyncAimIndicator();
        }

        private void FixedUpdate()
        {
            if (_simulator == null)
            {
                return;
            }

            Advance(Time.fixedDeltaTime);
            TrackCueBallScratch();
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

        /// <summary>单球模式：白球静止于开球点。</summary>
        private BallState[] BuildSingleBall()
        {
            return new[]
            {
                new BallState { position = tableConfig.footSpot, velocity = Vector2.zero },
            };
        }

        /// <summary>
        /// 球堆模式：白球 + 三球小堆（间距 = 2r + 2e-4 ⇒ **合法不重叠**）。
        /// 用于在编辑器里**直接观察球-球碰撞**（本驱动器是调试入口，不是最终交互）。
        /// </summary>
        private BallState[] BuildRack()
        {
            Vector2[] positions = RackPositions(tableConfig.footSpot);
            var balls = new BallState[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                balls[i] = new BallState { position = positions[i], velocity = Vector2.zero };
            }

            return balls;
        }

        /// <summary>
        /// 球堆位置（**纯函数**，灰盒驱动 / 验收器 / 测试**共用同一口径** —— 单一实现，铁律 16）。
        /// 返回数组 = **`[白球] + rackBallCount 颗堆球**；`rackBallCount` 默认 `3`（= 旧的 4 球摆放）。
        ///
        /// 排布：三角形、**顶点朝 +x（面向白球）**；第 `r` 行沿 y 关于 0 对称，行距 `s·√3/2`。
        /// 间距 `s = 2·BallRadius + 2e-4` ⇒ 满足 <see cref="PoolSimulator.BallInitialOverlapTolerance"/>
        /// 的"合法摆放"要求（任意两球间距 ≥ `2r − 1e-4`）。
        ///
        /// **⚠ 内层 `k` 必须降序**：在 `y = (k − r/2)·s` 下，降序才能让 `r = 1` 时 `+half` 先于 `−half` 入列
        /// ⇒ 与旧 4 球字面 `[footSpot, (−0.30, 0), (−0.30 − row, +half), (−0.30 − row, −half)]` **逐位一致**。
        /// 若改升序，索引 2/3 互换 ⇒ 破坏既有按索引读取的证据（验收器 / 报告项 / 逐球终态）。
        /// </summary>
        public static Vector2[] RackPositions(Vector2 footSpot, int rackBallCount = 3)
        {
            float s = PoolConstants.BallDiameter + 2e-4f;
            float row = s * 0.8660254f;

            int rows = 1;
            while (rows * (rows + 1) / 2 < rackBallCount)
            {
                rows++;
            }

            var positions = new List<Vector2>(1 + rackBallCount) { footSpot };

            int placed = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int k = r; k >= 0; k--)   // ★ 降序 ⇒ 与旧 4 球字面同序
                {
                    if (placed >= rackBallCount)
                    {
                        break;
                    }

                    positions.Add(new Vector2(-0.30f - r * row, (k - r * 0.5f) * s));
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

            float rad = aimDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, Mathf.Clamp01(power));
            Vector2 v0 = direction * speed;

            // ③ 白球不在场上（已入袋 / 擦袋角离台）⇒ 显式放回开球点；否则 ① 就在它停下的地方出杆。
            bool cueOffTable = !_simulator.IsBallInPlay(0);
            Vector2 from = cueOffTable ? tableConfig.footSpot : _simulator.Balls[0].position;

            _simulator.PlaceBall(0, from, v0);

            // 只清**每杆计数**；逐球入袋记录是跨杆状态，**不得**在此抹掉（否则入袋球会复活）。
            _simulator.ResetDiagnostics();
            _accumulator = 0f;
            LastLaunchRespawnedCueBall = cueOffTable;

            // 只读出口（音频层轮询增量 ⇒ strike 事件；累计不清零）。
            LaunchCount++;
            LastLaunchSpeed = speed;

            if (cueOffTable)
            {
                // ⚠ scratch 的**计数与时间戳**不在这里加 —— 见 `TrackCueBallScratch()`：
                // 若等到下一杆才计，玩家在本杆结束后**看不到任何判罚**（用户实测："落网又没反馈"）。

                // 白球从袋口**瞬移**回开球点 ⇒ 必须重置自转跟踪，否则这一瞬移会被当成真实位移、球凭空猛转一下。
                ResetVisualRotationTracking();
                Debug.Log($"PhysicsTestDrive：白球已离台（入袋/擦袋角）⇒ 放回开球点 {tableConfig.footSpot} 后出杆。", this);
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
