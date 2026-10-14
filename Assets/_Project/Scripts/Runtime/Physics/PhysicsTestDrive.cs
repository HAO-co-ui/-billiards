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

        [Tooltip("台呢滚阻减速度 a（m/s²）。物理内核阶段 = 0（spec §2.1 单一口径）。")]
        public float rollingDeceleration = 0f;

        [Header("球-球参数（默认取 SafeDefault = 1.0）")]
        [Tooltip("球-球法向恢复系数 e_ball。1.0 = 完全弹性（默认）；台球实测 0.93–0.96。")]
        [Range(0f, 1f)]
        public float ballRestitution = 1f;

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

        public float maxLaunchSpeed = PoolConstants.MaxBallSpeed;

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

        /// <summary>当前内核（未初始化时为 <c>null</c>）。</summary>
        public PoolSimulator Simulator => _simulator;

        /// <summary>本帧待推进的累计时间余量（秒）。仅用于显示/调试。</summary>
        public float Accumulator => _accumulator;

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
            if (tuning != null)
            {
                parameters = tuning.ToPhysicsParams();
                captureScale = tuning.pocketCaptureRadiusScale;
            }
            else
            {
                parameters = PhysicsParams.SafeDefault;
                parameters.cushionRestitution = cushionRestitution;
                parameters.cushionTangentialFriction = cushionTangentialFriction;
                parameters.rollingDeceleration = rollingDeceleration;
                parameters.ballRestitution = ballRestitution;
                parameters.ballTangentialFriction = ballTangentialFriction;
                captureScale = 1f;   // 袋口半径真源仍是 TableConfig.Pocket（逐袋）
            }

            _simulator = new PoolSimulator(tableConfig, parameters, captureScale);
            _simulator.LoadBalls(loadRackForBallBallDemo ? BuildRack() : BuildSingleBall());
            _accumulator = 0f;

            if (cushionGizmo != null)
            {
                cushionGizmo.SetSimulator(_simulator);
            }

            SyncVisual();
        }

        private void Update()
        {
            if (_simulator == null)
            {
                return;
            }

            bool fine = Input.GetKey(fineKey);
            float aStep = fine ? aimStepDegrees * 0.2f : aimStepDegrees;
            float pStep = fine ? powerStep * 0.25f : powerStep;

            if (Input.GetKey(aimLeftKey)) { aimDegrees += aStep; }
            if (Input.GetKey(aimRightKey)) { aimDegrees -= aStep; }
            if (Input.GetKey(powerUpKey)) { power = Mathf.Clamp01(power + pStep); }
            if (Input.GetKey(powerDownKey)) { power = Mathf.Clamp01(power - pStep); }

            if (Input.GetKeyDown(launchKey))
            {
                Launch();
            }
        }

        private void FixedUpdate()
        {
            if (_simulator == null)
            {
                return;
            }

            Advance(Time.fixedDeltaTime);
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

            SyncVisual();
        }

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
        /// 冒烟测试发射。**球堆模式下**给白球一个朝向球堆（−x）的初速 ⇒ 球堆散开，
        /// 可直接看到球-球碰撞；单球模式下沿 +y 正对长库打出。
        /// **这不是最终交互**（瞄准与力度属交互层）。
        /// </summary>
        /// <summary>
        /// **按 <see cref="aimDegrees"/> / <see cref="power"/> 打出一杆**（键盘最小可玩）。
        /// 初速方向 = `(cos θ, sin θ)`（θ = `aimDegrees`），大小 = `lerp(minLaunchSpeed, maxLaunchSpeed, power)`。
        /// 返回白球初速（供测试 / 日志断言）。
        /// </summary>
        public Vector2 Launch()
        {
            if (_simulator == null)
            {
                return Vector2.zero;
            }

            BallState[] balls = loadRackForBallBallDemo ? BuildRack() : BuildSingleBall();
            float rad = aimDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, Mathf.Clamp01(power));
            Vector2 v0 = direction * speed;
            balls[0].velocity = v0;

            _simulator.LoadBalls(balls);
            _simulator.ResetDiagnostics();
            _accumulator = 0f;

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
                    if (ballVisuals[i] != null)
                    {
                        ballVisuals[i].position = PoolGeometry.PhysicsBallToUnity(balls[i].position);
                    }
                }

                return;
            }

            if (ballVisual != null)
            {
                ballVisual.position = PoolGeometry.PhysicsBallToUnity(balls[0].position);
            }
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
