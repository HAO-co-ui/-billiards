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

        /// <summary>
        /// 球杆侧（击球流程状态机的唯一驱动者）。松手（力度达标）时若已接线 ⇒ 置**事件锁存**，
        /// 由球杆在前推到位帧击球；**未接线 ⇒ 本类直接出杆**（兜底，输入路径不被阻塞）。
        /// </summary>
        [Tooltip("球杆控制器（可空）。接线后松手经球杆前推动画击球；为空则松手直接出杆。")]
        public CueStickController cueStick;

        /// <summary>是否处于蓄力中（只读诊断出口）。</summary>
        public bool IsCharging { get; private set; }

        private Vector2 _dragStartPhysics;
        private bool _hadPick;

        // ── 事件锁存（供球杆侧消费；消费即清零 ⇒ 不因两组件的 Update 顺序丢事件，至多滞后一帧）──
        private bool _chargeBeginRequested;
        private bool _chargeCancelRequested;
        private bool _strikeRequested;

        /// <summary>消费"请求进入蓄力"事件（读取即清零）。</summary>
        public bool ConsumeChargeBeginRequested()
        {
            bool value = _chargeBeginRequested;
            _chargeBeginRequested = false;
            return value;
        }

        /// <summary>消费"蓄力被取消"事件（读取即清零）。</summary>
        public bool ConsumeChargeCancelRequested()
        {
            bool value = _chargeCancelRequested;
            _chargeCancelRequested = false;
            return value;
        }

        /// <summary>消费"请求击球"事件（读取即清零；由球杆在前推到位帧执行 Launch）。</summary>
        public bool ConsumeStrikeRequested()
        {
            bool value = _strikeRequested;
            _strikeRequested = false;
            return value;
        }

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

            // **模态让位**：结算卡 / 首页打开期间不许蓄力 —— 否则在卡片或首页上右键拖拽会蓄力、
            // 松手时出杆，把刚开的新一局当场打乱。判据点 = `PracticeResultView.IsOpen` 或 `HomeView.IsOpen`。
            if (Billiards.UI.PracticeResultView.IsOpen || Billiards.UI.HomeView.IsOpen
                || Billiards.UI.PracticeHudView.IsSettingsOpen)
            {
                return;
            }

            // **全场自由球（待放球）让位**：白球不在场上 ⇒ 没有可拉杆的对象；
            // 且此时左键被 `PointerAimController` 接管为"单击放球" ⇒ 右键蓄力一并让位，避免"没球也拉杆"。
            if (drive.PendingCueBallPlacement)
            {
                return;
            }

            if (Input.GetMouseButtonDown(1))
            {
                // **对手回合让位 + 可见反馈**（P5-B）：轮到 AI 时玩家输入被拒 —— 必须**登记一次可见提示**
                // （`MatchTurnNotice`），不许静默；判定与登记都在驱动器的**单一实现点**里。
                if (!drive.TryConsumePlayerInput())
                {
                    return;
                }

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
                    _chargeBeginRequested = true;
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
                // 松手：达到阈值 ⇒ 出杆（接线球杆时经前推动画在到位帧击球；否则直接出杆兜底）；否则取消并力度归零。
                IsCharging = false;
                if (_hadPick && drive.power >= launchThreshold)
                {
                    if (cueStick != null)
                    {
                        _strikeRequested = true;   // 球杆侧消费，前推到位帧击球
                    }
                    else
                    {
                        drive.Launch();
                    }
                }
                else
                {
                    _chargeCancelRequested = true;
                    drive.power = 0f;
                }

                _hadPick = false;
            }
        }
    }
}
