using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **拉杆力度的交互胶水**：按住鼠标右键在台面上往后拖 = 蓄力（拖距与力度**严格 1:1**、无延迟、无惯性），
    /// 松手 = 出杆；拖距过小 ⇒ 取消不出杆（防误触）。
    ///
    /// ── 分层（与 <see cref="PointerAimController"/> 同范式）──────────────────
    /// · **纯逻辑**：<see cref="PowerFromDrag"/>（投影 → [0,1] 力度）——判据 `T86` 直接驱动；
    /// · **本类 <c>Update</c>**：薄胶水——只做"读输入 / 拾取 / 调纯函数 / 写 <see cref="PhysicsTestDrive.power"/>（或出杆）"。
    ///
    /// ── 手势约定（8 Ball Pool 式拉杆）──────────────────────────────────────
    /// · 左键拖拽 = 瞄准（<see cref="PointerAimController"/>，不受本类影响）；
    /// · **右键按住拖 = 蓄力**，松手 = 出杆 —— 右键原"取消瞄准"职责移交 Esc（唯一键）；
    /// · 球在运动（!TableAtRest）期间不进入蓄力（与 <see cref="PhysicsTestDrive.Launch"/> 的出杆门控同口径）。
    ///
    /// ── "跟手"的可判定形式（排期硬要求）────────────────────────────────────
    /// 力度 = <see cref="PowerFromDrag"/> 的**无状态纯函数**结果，每帧**直接写入**——
    /// 无平滑、无插值、无累积 ⇒ 拖拽位移与力条值严格 1:1。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────
    /// · <c>Update</c> 的输入读取与拾取不被 EditMode 判据覆盖（同 <see cref="PointerAimController"/> 的胶水盲带）；
    ///   松手出杆 / 取消的端到端行为属 PlayMode / 手动验收。
    /// </summary>
    [AddComponentMenu("Billiards/Pointer Power Controller")]
    public sealed class PointerPowerController : MonoBehaviour
    {
        /// <summary>拉满力度所需的拖拽长度（**规范物理平面**，米）。</summary>
        [Tooltip("拉满力度所需的拖拽长度（规范物理平面，米）。")]
        public float maxDragLength = 0.5f;

        /// <summary>松手出杆的力度下限（低于则取消防误触，力度归零）。</summary>
        [Tooltip("松手出杆的力度下限（低于则取消防误触）。")]
        public float launchThreshold = 0.05f;

        /// <summary>被驱动的驱动器（写它的 power；出杆走它的 Launch）。</summary>
        [Tooltip("被驱动的 PhysicsTestDrive。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        /// <summary>瞄准相机（拾取射线用）。</summary>
        [Tooltip("瞄准相机（拾取射线用）。")]
        public Camera aimCamera;

        /// <summary>可视野缩放根（PlayfieldRoot）——拾取反缩放的唯一入口。</summary>
        [Tooltip("可视野缩放根（PlayfieldRoot）。")]
        public Transform playfieldRoot;

        /// <summary>是否处于蓄力中（只读诊断出口）。</summary>
        public bool IsCharging { get; private set; }

        private Vector2 _dragStartPhysics;
        private bool _hadPick;

        /// <summary>
        /// **拖向量 → 力度 ∈ [0,1]**（纯函数，判据 `T86` 直接驱动）：
        /// 力度 = 拖向量在「瞄准<b>反</b>方向」上的投影 ÷ 拉满长度，再夹到 [0,1]。
        /// · 往后拖（与出球方向相反）⇒ 正投影 ⇒ 蓄力；
        /// · 往前推或正交移动 ⇒ ≤ 0 ⇒ 0（不蓄力）；
        /// · 拖距超过拉满长度 ⇒ 1（上限截断）。
        /// **1:1 语义**：无状态、无平滑 ⇒ 调用方每帧直接写入即"无延迟、无惯性"。
        /// 退化防御：拉程 ≤ 0 或零瞄准方向 ⇒ 返回 0（不产生 NaN / 除零）。
        /// </summary>
        public static float PowerFromDrag(Vector2 dragPhysics, Vector2 aimDirectionPhysics, float maxDragLength)
        {
            if (!(maxDragLength > 0f) || aimDirectionPhysics == Vector2.zero)
            {
                return 0f;
            }

            float backwardProjection = -Vector2.Dot(dragPhysics, aimDirectionPhysics);
            return Mathf.Clamp01(backwardProjection / maxDragLength);
        }

        private void Update()
        {
            if (drive == null || aimCamera == null || playfieldRoot == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                // 进入蓄力：球必须静止（与出杆门控同口径）；起点拾取失败（台面外）则不进入。
                if (drive.Simulator == null || !drive.Simulator.TableAtRest)
                {
                    return;
                }

                if (PointerAimSolver.TryPickPhysicsPoint(aimCamera, playfieldRoot, Input.mousePosition, out Vector2 start))
                {
                    _dragStartPhysics = start;
                    _hadPick = true;
                    IsCharging = true;
                }
            }
            else if (IsCharging && Input.GetMouseButton(1))
            {
                // 蓄力中：每帧以「当前」瞄准方向重投影（蓄力期间允许微调方向），直接写入（1:1 无惯性）。
                if (PointerAimSolver.TryPickPhysicsPoint(aimCamera, playfieldRoot, Input.mousePosition, out Vector2 current))
                {
                    _hadPick = true;
                    float rad = drive.aimDegrees * Mathf.Deg2Rad;
                    Vector2 aimDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    drive.power = PowerFromDrag(current - _dragStartPhysics, aimDir, maxDragLength);
                }

                // 拾取失败（拖出台面 / 近平行）⇒ 保持上一帧力度（不产生 NaN）。
            }
            else if (IsCharging && Input.GetMouseButtonUp(1))
            {
                // 松手：达到阈值 ⇒ 出杆；否则取消并把力度归零。
                IsCharging = false;
                if (_hadPick && drive.power >= launchThreshold)
                {
                    drive.Launch();
                }
                else
                {
                    drive.power = 0f;
                }

                _hadPick = false;
            }
        }
    }
}
