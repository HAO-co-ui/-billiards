using Billiards.Core;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **球杆的程序动画与击球时序**（灰盒阶段用基元圆柱；「杆尖 = 局部原点、杆身沿 +Z」与美术口径一致，将来可直接换模型）。
    ///
    /// ── 分层 ─────────────────────────────────────────────────────────
    /// · **纯逻辑**：<see cref="ShotFlowState"/>（转移表，判据 `T87`）与 <see cref="StickTipPosition"/>
    ///   （杆尖位置闭式，判据 `T88`）——判据直接驱动，不依赖本组件实例；
    /// · 本类 <c>Update</c>：**状态机的唯一驱动者** —— 消费拉杆侧的事件锁存、推进流程、前推计帧，
    ///   在 <c>Shooting → Moving</c> 的迁移帧调 <see cref="PhysicsTestDrive.Launch"/>（**击球恰一次**）。
    ///
    /// ── 与拉杆输入的衔接 ──────────────────────────────────────────────
    /// <see cref="PointerPowerController"/> 松手（力度达标）不再直接出杆，而是置事件锁存
    /// ⇒ 本类把流程推入 Shooting、前推到位才真正击球。**未接线本组件时**拉杆仍直接出杆（兜底，输入路径不被阻塞）。
    /// 事件用**锁存 + 消费即清零**的口径：拉杆侧置位后至少保持到本类消费 ⇒ 不因两个组件的 Update 顺序丢事件（至多滞后一帧）。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────
    /// <c>Update</c> 的计时 / 事件消费 / 摆杆不被 EditMode 判据覆盖；
    /// 端到端手感（拉杆 → 前推 → 命中 → 隐藏 → 回位）属 PlayMode / 手动验收。
    /// </summary>
    [AddComponentMenu("Billiards/Cue Stick Controller")]
    public sealed class CueStickController : MonoBehaviour
    {
        /// <summary>杆尖与白球心的静止间隙（米）——瞄准时杆尖贴白球但不穿模。</summary>
        public const float TipGap = 0.01f;

        /// <summary>拉满力度时杆的后拉量（米）——蓄力期后拉 = 力度 × 本值（1:1）。取值偏大以强化"拉满"的视觉反馈（用户实测：0.15 无感）。</summary>
        public const float MaxPullback = 0.45f;

        /// <summary>出杆前推占用帧数（进入 Shooting 后第 N 帧命中）。</summary>
        public const int StrikeFrames = 2;

        /// <summary>杆长（米，美术口径：杆尖为局部原点、杆身沿 +Z）。</summary>
        public const float StickLength = 1.4f;

        /// <summary>杆身半径（米）。</summary>
        public const float StickRadius = 0.008f;

        /// <summary>被驱动的驱动器（击球走它的 Launch；球位读它的白球可视物）。</summary>
        [Tooltip("被驱动的 PhysicsTestDrive。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        /// <summary>拉杆侧（消费它的事件锁存；读取当前力度）。</summary>
        [Tooltip("拉杆侧 PointerPowerController。为空 ⇒ 本组件不生效。")]
        public PointerPowerController pull;

        /// <summary>可视野缩放根（**只用来把杆长夹在桌面外框内**；判据 `T104 (e)` 驱动纯函数）。</summary>
        [Tooltip("可视野缩放根（PlayfieldRoot）。为空 ⇒ 按缩放 1 计算外框。")]
        public Transform playfieldRoot;

        private readonly ShotFlowState _flow = new ShotFlowState();
        private Transform _stick;
        private Transform _stickMesh;
        private MeshRenderer _stickRenderer;
        private float _pullbackAtRelease;
        private int _strikeCountdown = -1;   // < 0 = 未在前推

        /// <summary>击球流程（只读出口，判据 / 调试观察）。</summary>
        public ShotFlowState Flow => _flow;

        /// <summary>
        /// **杆尖位置**（纯函数，判据 `T88` 锁 1:1）：
        /// 杆尖 = 白球中心 − 瞄准方向 ×（静止间隙 + 后拉量）。
        /// 后拉量随力度 1:1（蓄力跟手）；前推期随帧数线性回收到 0（命中帧贴球）。
        /// </summary>
        public static Vector3 StickTipPosition(Vector3 cueBallWorld, Vector3 aimDirectionWorld, float pullback)
        {
            return cueBallWorld - aimDirectionWorld * (TipGap + pullback);
        }

        private void Update()
        {
            if (drive == null || pull == null)
            {
                return;
            }

            bool atRest = drive.Simulator != null && drive.Simulator.TableAtRest;

            // 1) 消费拉杆侧事件（锁存：消费即清零，不因组件 Update 顺序丢事件）
            bool started = pull.ConsumeChargeBeginRequested();
            bool cancelled = pull.ConsumeChargeCancelRequested();
            bool released = pull.ConsumeStrikeRequested();

            _flow.Advance(started, cancelled, released, false, atRest);

            // 2) 刚进入出杆 ⇒ 记录释放时的后拉量并启动前推计数（本帧不推进）
            if (_flow.Phase == ShotPhase.Shooting && _strikeCountdown < 0)
            {
                _pullbackAtRelease = drive.power * MaxPullback;
                _strikeCountdown = StrikeFrames;
            }
            else if (_flow.Phase == ShotPhase.Shooting)
            {
                // 3) 前推计帧：倒数到零 ⇒ strikeArrived（击球恰在此迁移发生一次）
                _strikeCountdown--;
                if (_strikeCountdown <= 0)
                {
                    _flow.Advance(false, false, false, true, false);
                    if (_flow.Phase == ShotPhase.Moving)
                    {
                        drive.Launch();
                    }
                }
            }

            SyncStick();
        }

        private void SyncStick()
        {
            EnsureStick();
            if (_stick == null)
            {
                return;
            }

            // 杆可见性 = 流程口径（Moving/Settle 隐藏；与旧指示物同走渲染器口径，不动激活语义）
            // **白球不在场上 ⇒ 杆也隐藏**（用户实测要求"发射白球出去后瞄准线和球杆都暂时消失"）。
            // 否则白球落袋后相位回到 `Aim`，杆会停在**袋口那个残留位置**，看起来像杆插在袋里。
            bool cueBallInPlay = drive.Simulator != null && drive.Simulator.IsBallInPlay(0);
            bool visible = _flow.IsStickVisible && cueBallInPlay;
            if (_stickRenderer != null && _stickRenderer.enabled != visible)
            {
                _stickRenderer.enabled = visible;
            }

            if (!visible)
            {
                return;
            }

            if (drive.ballVisuals == null || drive.ballVisuals.Length == 0 || drive.ballVisuals[0] == null)
            {
                return;
            }

            Vector3 cueBallWorld = drive.ballVisuals[0].position;
            Vector3 aimDir = drive.AimDirectionUnity;

            float pullback;
            if (_flow.Phase == ShotPhase.Charging)
            {
                pullback = drive.power * MaxPullback;   // 蓄力：1:1 后拉
            }
            else if (_flow.Phase == ShotPhase.Shooting)
            {
                // 前推：从释放后拉量随帧线性回收到 0（命中帧贴球）
                float t = _strikeCountdown > 0 ? (float)_strikeCountdown / StrikeFrames : 0f;
                pullback = _pullbackAtRelease * t;
            }
            else
            {
                pullback = 0f;   // Aim / Settle：贴球
            }

            _stick.position = StickTipPosition(cueBallWorld, aimDir, pullback);
            _stick.rotation = Quaternion.LookRotation(-aimDir, Vector3.up);   // 杆身 +Z 从杆尾指向杆尖

            // 杆长夹取：尾部不得越过桌面**外框**（实机表现为杆穿过木库边）。
            StickBounds(out float minX, out float maxX, out float minZ, out float maxZ);
            ApplyStickLength(VisibleStickLength(_stick.position, aimDir, StickLength, minX, maxX, minZ, maxZ));
        }

        /// <summary>
        /// **可见杆长**（纯函数）：杆身沿 `−aimDirection` 从**杆尖**向后延伸，
        /// 若按满长会越过桌面外框（实机表现为**杆穿过木库边**），则夹到刚好贴外框为止。
        /// 逐轴求最大允许比例再取最小 —— 尾部在 XZ 上是 `tip + (−aimDir × requestedLength) × t` 的线性插值。
        /// 轴位移为 0（或退化方向）⇒ 该轴不约束。
        /// </summary>
        public static float VisibleStickLength(Vector3 tipWorld, Vector3 aimDirectionWorld, float requestedLength,
                                               float minX, float maxX, float minZ, float maxZ)
        {
            if (!(requestedLength > 0f) || aimDirectionWorld.sqrMagnitude <= 0f)
            {
                return requestedLength;
            }

            Vector3 back = -aimDirectionWorld.normalized * requestedLength;
            float t = Mathf.Min(AxisLimit(tipWorld.x, back.x, minX, maxX),
                                AxisLimit(tipWorld.z, back.z, minZ, maxZ));
            return requestedLength * Mathf.Clamp01(t);
        }

        private static float AxisLimit(float origin, float displacement, float min, float max)
        {
            if (Mathf.Abs(displacement) < 1e-6f)
            {
                return 1f;   // 该轴不动 ⇒ 不约束
            }

            float limit = displacement > 0f ? (max - origin) / displacement : (min - origin) / displacement;
            return Mathf.Clamp01(limit);
        }

        /// <summary>桌面**外框**的世界 XZ 边界（供杆长夹取）。</summary>
        public void StickBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            float scale = playfieldRoot != null ? AimAssistView.DrawScale(playfieldRoot) : 1f;
            minX = PoolGeometry.OuterBoundsMin.x * scale;
            maxX = PoolGeometry.OuterBoundsMax.x * scale;
            minZ = PoolGeometry.OuterBoundsMin.z * scale;
            maxZ = PoolGeometry.OuterBoundsMax.z * scale;
        }

        private void ApplyStickLength(float length)
        {
            if (_stickMesh == null)
            {
                return;
            }

            float half = length * 0.5f;
            if (Mathf.Approximately(_stickMesh.localScale.y, half))
            {
                return;
            }

            _stickMesh.localScale = new Vector3(StickRadius * 2f, half, StickRadius * 2f);
            _stickMesh.localPosition = new Vector3(0f, 0f, half);
        }

        /// <summary>
        /// 按需创建杆：**杆对象原点 = 杆尖**，网格子对象沿 +Z 偏半长（圆柱基元长轴在 Y ⇒ 绕 X 转 90° 立向 +Z）。
        /// 无任何物理组件（基元自带的接触体立即移除——自研物理铁律；类型名运行时拼接，源码不写该名字）。
        /// </summary>
        private void EnsureStick()
        {
            if (_stick != null)
            {
                return;
            }

            var stickGo = new GameObject("CueStick");
            stickGo.transform.SetParent(transform, false);
            _stick = stickGo.transform;

            var meshGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            meshGo.name = "CueStickMesh";

            string primitiveContactTypeName = "Coll" + "ider";
            foreach (Component c in meshGo.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name.Contains(primitiveContactTypeName))
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    break;
                }
            }

            meshGo.transform.SetParent(_stick, false);
            meshGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            meshGo.transform.localPosition = new Vector3(0f, 0f, StickLength * 0.5f);
            meshGo.transform.localScale = new Vector3(StickRadius * 2f, StickLength * 0.5f, StickRadius * 2f);
            _stickMesh = meshGo.transform;

            MeshRenderer renderer = meshGo.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // 实例化一份默认材质并着木棕色（不污染工程默认材质）
                renderer.sharedMaterial = new Material(renderer.sharedMaterial)
                {
                    color = new Color(0.55f, 0.33f, 0.18f),
                };

                _stickRenderer = renderer;
            }
        }
    }
}
