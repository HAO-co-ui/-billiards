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

        /// <summary>拉满力度时杆的后拉量（米）——蓄力期后拉 = 力度 × 本值（1:1）。</summary>
        public const float MaxPullback = 0.15f;

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

        private readonly ShotFlowState _flow = new ShotFlowState();
        private Transform _stick;
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
            bool visible = _flow.IsStickVisible;
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
