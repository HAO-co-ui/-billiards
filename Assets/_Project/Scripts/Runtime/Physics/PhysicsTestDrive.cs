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

        /// <summary>可选：与该球位置同步的视觉 Transform（白球）。</summary>
        [Tooltip("可选：白球视觉 Transform，每步后同步到内核球位。")]
        public Transform ballVisual;

        [Header("物理参数（占位值；正式值待手感标定）")]
        [Tooltip("库边法向恢复系数 e。占位值，未经策划确认。")]
        [Range(0f, 1f)]
        public float cushionRestitution = 0.8f;

        [Tooltip("库边切向摩擦保留系数 μ。占位值，未经策划确认。")]
        [Range(0f, 1f)]
        public float cushionTangentialFriction = 0.9f;

        [Tooltip("台呢滚阻减速度 a（m/s²）。物理内核阶段 = 0（spec §2.1 单一口径）。")]
        public float rollingDeceleration = 0f;

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

            var parameters = new PhysicsParams
            {
                cushionRestitution = cushionRestitution,
                cushionTangentialFriction = cushionTangentialFriction,
                rollingDeceleration = rollingDeceleration,
            };

            _simulator = new PoolSimulator(tableConfig, parameters);
            _simulator.LoadBalls(new[]
            {
                new BallState { position = tableConfig.footSpot, velocity = Vector2.zero },
            });
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

        /// <summary>
        /// 冒烟测试发射：把白球以 <see cref="PoolConstants.MaxBallSpeed"/> 沿物理平面 +y 打出。
        /// 方向固定为正对长库（垂直入射），便于肉眼观察往返；**这不是最终交互**。
        /// </summary>
        public void Launch()
        {
            if (_simulator == null)
            {
                return;
            }

            _simulator.LoadBalls(new[]
            {
                new BallState
                {
                    position = tableConfig.footSpot,
                    velocity = new Vector2(0f, PoolConstants.MaxBallSpeed),
                },
            });
            _simulator.ResetDiagnostics();
            _accumulator = 0f;

            SyncVisual();
        }

        private void SyncVisual()
        {
            if (ballVisual == null || _simulator == null)
            {
                return;
            }

            IReadOnlyList<BallState> balls = _simulator.Balls;
            if (balls == null || balls.Count == 0)
            {
                return;
            }

            ballVisual.position = PoolGeometry.PhysicsBallToUnity(balls[0].position);
        }

        private void OnGUI()
        {
            const int x = 10;
            const int y = 10;
            var rect = new Rect(x, y, 760f, 76f);

            if (_simulator == null)
            {
                GUI.Label(rect, "PoolSimulator 未初始化（tableConfig 缺失？）");
                return;
            }

            IReadOnlyList<BallState> balls = _simulator.Balls;
            float speed = (balls != null && balls.Count > 0) ? balls[0].velocity.magnitude : 0f;

            string text =
                $"contacts={_simulator.TotalContactCount}   guardTrips={_simulator.ToiIterationGuardTrips}   "
                + $"energyGain={_simulator.EnergyGainViolations}   pocketEvents={_simulator.PocketProximityEvents}\n"
                + $"subSteps={_simulator.SubSteps}   |v|={speed:F3} m/s   [{launchKey}] 发射 {PoolConstants.MaxBallSpeed} m/s";

            GUI.Label(rect, text);
        }
    }
}
