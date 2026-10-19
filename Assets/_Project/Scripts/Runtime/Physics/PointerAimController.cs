using Billiards.Core;
using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **指针瞄准的交互胶水**：读输入 → 驱动 <see cref="PointerAimState"/>（纯状态机）→ 写
    /// <see cref="PhysicsTestDrive.aimDegrees"/>。
    ///
    /// ── 分层（本类的存在理由）──────────────────────────────────────────────────
    /// · **状态机**（<see cref="PointerAimState"/>）与**粒度算术**（本类的两个 <c>static</c> 纯函数）是
    ///   **纯逻辑**，判据可直接驱动（不碰输入设备）。
    /// · 本类的 <c>Update</c> 是**薄胶水**：只做"读输入 / 调纯核 / 写一个 public 字段"。
    ///
    /// ── 三条硬约束（照 ADR-0001 §2.5 与工程标准 R5）─────────────────────────────
    ///  1. **不新开方向/坐标转换**：拾取走 <see cref="PointerAimSolver.TryPickPhysicsPoint"/>，
    ///     方向走 <see cref="PointerAimSolver.AimDirectionPhysics"/>；世界方向仍归
    ///     <see cref="PoolGeometry.PhysicsDirectionToUnity"/>（本类**不**手写任何 `(−x,−y)` 映射）。
    ///  2. **只写 `aimDegrees` 一个字段**：不改 <see cref="PhysicsTestDrive"/> 的其它状态
    ///     （出杆门控 <c>TableAtRest</c> / 不重建球堆等语义仍归驱动器，见 `Launch()`）。
    ///  3. **不引 PhysX**：本类无任何碰撞体 / 刚体名。
    ///
    /// ── ⚠ 与 spec §3.3 的偏差（如实声明）────────────────────────────────────────
    /// spec §3.3 写"读 **Input System** 的 `Gameplay` 映射"。本实现改用**旧版 `Input`**（`Input.GetMouseButton` 等）：
    ///  · `ProjectSettings.activeInputHandler = 2`（**Both**）⇒ 新旧输入共存（`PhysicsTestDrive` 走的就是旧版）；
    ///  · `.inputactions` **没有生成 C# 包装类**（无 `*.inputactions.cs`），走 Input System 需在场景里序列化
    ///    接线 `InputActionAsset` 或 `InputAction` 字段 —— 那会新增一处**未受本单判据保护**的场景接线；
    ///  · 交互**状态机**才是被判据覆盖的对象，它与输入设备**正交**（见类首"分层"）。
    ///  ⇒ 该偏差**不影响任何判据**，留待后续"输入方案统一"单处理。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// <c>Update</c> 的输入读取**不被 EditMode 判据覆盖**（EditMode 不跑 <c>Update</c>，且不碰输入设备）。
    /// </summary>
    [AddComponentMenu("Billiards/Pointer Aim Controller")]
    public sealed class PointerAimController : MonoBehaviour
    {
        /// <summary>
        /// **细调系数**：按住 <see cref="fineKey"/> 时步长 = 基础步长 × 本系数。
        /// `0.2f` 的依据 = 排期"方向键微调（粒度约 **0.2°**）"（基础步长 `aimStepDegrees = 1.0°`，
        /// `1.0 × 0.2 = 0.2` ⇒ 0.2° 是**细调**步长，见 spec §3.4 / `Q-9`）。
        /// **唯一真源**：`PhysicsTestDrive` 的键盘路径也引用本常量（避免两处各写一个 0.2）。
        /// </summary>
        public const float FineAimStepFactor = 0.2f;

        /// <summary>瞄准相机（拾取射线用）。</summary>
        [Tooltip("瞄准相机（指针拾取射线用）。为空 ⇒ 不更新方向。")]
        public Camera aimCamera;

        /// <summary>可视野缩放根（`PlayfieldRoot`）——拾取反缩放的唯一入口。</summary>
        [Tooltip("可视野缩放根（PlayfieldRoot）。为空 ⇒ 不更新方向。")]
        public Transform playfieldRoot;

        /// <summary>被驱动的驱动器（**只写**它的 <see cref="PhysicsTestDrive.aimDegrees"/>）。</summary>
        [Tooltip("被驱动的 PhysicsTestDrive（只写入其 aimDegrees）。为空 ⇒ 本组件不生效。")]
        public PhysicsTestDrive drive;

        /// <summary>按住此键 ⇒ 方向键走细粒度（0.2°）。</summary>
        [Tooltip("按住此键 ⇒ 方向键走细粒度（× FineAimStepFactor）。")]
        public KeyCode fineKey = KeyCode.LeftShift;

        /// <summary>降低瞄准角（`←`，与输入的 `AimNudge` 反号一致）。</summary>
        [Tooltip("瞄准角 +键（`←`）。")]
        public KeyCode aimLeftKey = KeyCode.LeftArrow;

        /// <summary>升高瞄准角（`→`）。</summary>
        [Tooltip("瞄准角 −键（`→`）。")]
        public KeyCode aimRightKey = KeyCode.RightArrow;

        /// <summary>
        /// **全场自由球**：左键"按下→抬起"的位移超过本值（像素）就**不算放球**（视为"想拖拽瞄准"）。
        /// 目的：放球与既有"左键拖拽瞄准"**不抢手势**（轻点放球、拖动瞄准）。
        /// </summary>
        [Tooltip("放球判定：左键按下→抬起的最大位移（像素）。超过它视为拖拽瞄准。")]
        public float placementClickMaxPixels = 12f;

        private readonly PointerAimState _state = new PointerAimState();

        /// <summary>放球手势的按下位置（像素）。</summary>
        private Vector2 _placementDownScreen;

        /// <summary>放球手势是否已按下。</summary>
        private bool _placementDown;

        /// <summary>当前交互状态机（只读出口，供调试 / 判据观察）。</summary>
        public PointerAimState State => _state;

        // ── 纯函数（判据直接驱动；不依赖任何 MonoBehaviour 实例 / 输入设备）──────────

        /// <summary>
        /// **按轴向与粒度求新瞄准角**（度）。
        /// 步长 = <paramref name="fine"/> ? `baseStepDegrees × `<see cref="FineAimStepFactor"/>` : baseStepDegrees`。
        /// </summary>
        /// <param name="currentDegrees">当前瞄准角（度）。</param>
        /// <param name="axisSign">轴向符号（`+1` / `-1` / `0`）。</param>
        /// <param name="fine">是否按住细调键。</param>
        /// <param name="baseStepDegrees">基础步长（度）—— 现场从 <see cref="PhysicsTestDrive.aimStepDegrees"/> 取。</param>
        public static float ApplyNudgeDegrees(float currentDegrees, float axisSign, bool fine, float baseStepDegrees)
        {
            float step = fine ? baseStepDegrees * FineAimStepFactor : baseStepDegrees;
            return currentDegrees + axisSign * step;
        }

        /// <summary>
        /// **物理平面方向 → 瞄准角**（度）：`atan2(dy, dx)`。
        /// 退化方向（零向量）⇒ 返回 <paramref name="fallbackDegrees"/>（保留原角，不产生 NaN）。
        /// </summary>
        public static float AimDegreesFromPhysicsDirection(Vector2 physicsDirection, float fallbackDegrees)
        {
            if (physicsDirection == Vector2.zero)
            {
                return fallbackDegrees;
            }

            return Mathf.Atan2(physicsDirection.y, physicsDirection.x) * Mathf.Rad2Deg;
        }

        // ── 胶水（薄；输入读取不被判据覆盖）────────────────────────────────────────

        private void Update()
        {
            if (drive == null)
            {
                return;
            }

            // **模态让位**：结算卡 / 首页打开期间不作瞄准 —— 判据点 = `PracticeResultView.IsOpen`
            // 或 `HomeView.IsOpen`。否则在卡片或首页上左键拖拽会同时驱动瞄准锁，交互会串。
            // **取景转场让位**（第三个判据点，同类的"相机在动"根因）：转场期间相机在扫，
            // 拖拽瞄准既看不见（瞄准线已被 `AimAssistController` 门控）又容易串味 ⇒ 一并让位。
            // **对手回合不许动瞄准线**：AI 在思考/出杆时，玩家拖动会改掉瞄准角，
            //   轮回来时方向已经被改过 ⇒ 观感是"AI 用了我没设的方向"。
            // ⚠ **只有"真的有 AI 对手"时才让位**：练习模式里规则同样会把回合判给"对方"，
            //   `IsAiTurn` 会变真；若直接拿它当门，练习模式的**放球点击也会被挡** ⇒
            //   表现为"白球卡住 / 球杆不见"（用户实机反馈）。⇒ 加 `AiOpponentEnabled` 这一层。
            bool aiTurn = drive != null && drive.IsAiTurn && drive.AiOpponentEnabled;
            if (Billiards.UI.PracticeResultView.IsOpen || Billiards.UI.HomeView.IsOpen
                || Billiards.UI.PracticeHudView.IsSettingsOpen
                || Billiards.UI.PracticeHudView.IsOnboardingOpen
                || Billiards.Core.CameraTransition.IsActive
                || aiTurn)
            {
                return;
            }

            // **全场自由球（待放球）让位**（第四个判据点）：白球不在场上时，"瞄准"没有对象
            // ⇒ 本组件**整体让位**，并**接管左键单击 = 放球**（见 `HandleCueBallPlacement`）。
            // 这样放球与右键蓄力（`PointerPowerController`）也不抢手势（那个组件在待放球期间同样让位）。
            // **开球摆球期（用户规则）**：白球可直接拖动到开球线后方任意位置。
            // 与"待放球"不同：**不挡出杆**，所以必须在瞄准分支**之前**单独接管"按住球拖"的手势；
            // 只有按下点落在白球附近才接管，其余左键仍是正常瞄准（不抢手势）。
            if (HandleBreakCueBallDrag())
            {
                return;
            }

            if (drive.PendingCueBallPlacement)
            {
                HandleCueBallPlacement();
                return;
            }

            bool fine = Input.GetKey(fineKey);

            // 1) 方向键微调（与"指针拖拽"正交，锁定时也允许微调 —— 那是"锁定后微调"的正常用途）。
            float axis = 0f;
            if (Input.GetKey(aimLeftKey))
            {
                axis += 1f;
            }

            if (Input.GetKey(aimRightKey))
            {
                axis -= 1f;
            }

            if (axis != 0f)
            {
                drive.aimDegrees = ApplyNudgeDegrees(drive.aimDegrees, axis, fine, drive.aimStepDegrees);
            }

            // 2) 拖拽瞄准 / 松开锁定（状态机决定"指针是否应改方向"）。
            bool lockHeld = Input.GetMouseButton(0);                                  // 输入资产 AimLock
            // 取消瞄准只保留 Esc：右键已移交「拉杆蓄力」（PointerPowerController，8 Ball Pool 式拖拽出杆）。
            bool cancelAim = Input.GetKeyDown(KeyCode.Escape);                        // 输入资产 CancelAim

            if (!_state.Advance(lockHeld, cancelAim))
            {
                return;
            }

            if (aimCamera == null || playfieldRoot == null)
            {
                return;
            }

            if (!PointerAimSolver.TryPickPhysicsPoint(aimCamera, playfieldRoot, Input.mousePosition, out Vector2 targetPhysics))
            {
                // 拾取失败（近平行 / 相机或根缺失）⇒ **不得**改方向（否则 NaN 会传播到 aimDegrees）。
                return;
            }

            Vector2 cuePhysics = (drive.Simulator != null && drive.Simulator.Balls.Count > 0)
                ? drive.Simulator.Balls[0].position
                : Vector2.zero;

            Vector2 direction = PointerAimSolver.AimDirectionPhysics(cuePhysics, targetPhysics);
            drive.aimDegrees = AimDegreesFromPhysicsDirection(direction, drive.aimDegrees);
        }

        /// <summary>开球摆球期：鼠标与白球的**屏幕距离**在这个像素内，按下即视为"抓住球"。</summary>
        private const float breakGrabPixels = 48f;

        private bool _breakDragging;

        /// <summary>
        /// **开球摆球期：按住白球拖动 ⇒ 摆到开球线后方任意位置**（用户规则）。
        /// 返回 true 表示本帧输入已被本分支消费（`Update` 不再走瞄准）。
        ///
        /// 设计要点：
        /// · **只有按下点落在白球附近**才接管 ⇒ 点别处仍是正常瞄准，不抢手势；
        /// · 拖动过程中**逐帧**调 <see cref="PhysicsTestDrive.TryPlaceCueBallAtBreak"/>：非法点被拒（球停在上一个合法位置），
        ///   与"放球窗口"的判据**同源**（`IsPlacementLegalInKitchen`），不另写一份；
        /// · 松开左键即结束；**不消耗任何出杆输入**（出杆仍走 `PointerPowerController`）。
        /// </summary>
        private bool HandleBreakCueBallDrag()
        {
            if (drive == null || !drive.BreakPlacementActive || aimCamera == null || playfieldRoot == null
                || drive.Simulator == null)
            {
                _breakDragging = false;
                return false;
            }

            if (!_breakDragging)
            {
                if (!Input.GetMouseButtonDown(0))
                {
                    return false;
                }

                Vector2 cue = drive.Simulator.Balls[0].position;
                Vector3 cueWorld = playfieldRoot.TransformPoint(new Vector3(cue.x, 0f, cue.y));
                Vector3 cueScreen = aimCamera.WorldToScreenPoint(cueWorld);
                if (cueScreen.z <= 0f)
                {
                    return false;
                }

                if (((Vector2)Input.mousePosition - (Vector2)cueScreen).magnitude > breakGrabPixels)
                {
                    return false;   // 没按在球上 ⇒ 交给瞄准
                }

                _breakDragging = true;
            }

            if (PointerAimSolver.TryPickPhysicsPoint(aimCamera, playfieldRoot, Input.mousePosition,
                    out Vector2 physicsPoint))
            {
                drive.TryPlaceCueBallAtBreak(physicsPoint);
            }

            if (Input.GetMouseButtonUp(0))
            {
                _breakDragging = false;
            }

            return true;
        }

        /// <summary>
        /// **全场自由球：左键单击落点 ⇒ 放白球**（`Update` 在待放球期间调它、并让位瞄准）。
        ///
        /// · **轻点**（位移 ≤ <see cref="placementClickMaxPixels"/>）= 放球；**拖动** = 不是放球（避免抢"拖拽瞄准"的手势）；
        /// · 落点 = **屏幕点投到台面平面**（复用唯一实现点 <see cref="PointerAimSolver.TryPickPhysicsPoint"/>）；
        ///   拾取失败（近平行 / 相机或根缺失）⇒ **不放**（不得用 NaN 落点）；
        /// · **合法性由 <see cref="PhysicsTestDrive.TryPlaceCueBall"/> 判**（台面内 / 不压球 / 不在袋口）；
        ///   **非法 ⇒ 不放、不改状态**（白球仍离台，玩家可再点）+ `RefusedPlacementCount++`
        ///   ⇒ **不静默**（HUD 侧可读该计数给文案，属 `home-ui` 的域）。
        /// </summary>
        private void HandleCueBallPlacement()
        {
            // **Esc = 兜底"放回开球点"** —— ⚠ 必须在**本分支自己读**：`Update` 里那处
            // `cancelAim`（`KeyCode.Escape`）在本分支**之后**、而本分支会 `return` ⇒ 靠它什么都不会发生。
            // 语义与 HUD 文案常量 `PracticeHudState.BallInHandPendingDetail`（"… Esc puts it back on the break spot"）
            // **必须一致**（否则界面在承诺一个不存在的操作）。
            // ⚠ **引文案别抄句子、要引常量名**：抄句子会随文案漂移而失配（本合同批次 B"引数字的文档必须排在
            //   产数字的动作之后"是同一类毛病）—— 本行就是这么被主理人抓到的。
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                drive.ReturnCueBallToHome();
                return;
            }

            // ⚠ **不要在这里 `return`**：快速点一下时"按下"和"抬起"会落在**同一帧**，
            //   按下就 return 会把当帧的 `GetMouseButtonUp` 吃掉 ⇒ 这次点击被吞、`_placementDown`
            //   还留在 true ⇒ 玩家感觉"点放球没反应/卡住"（用户实机反馈）。
            if (Input.GetMouseButtonDown(0))
            {
                _placementDownScreen = Input.mousePosition;
                _placementDown = true;
            }

            if (!_placementDown || !Input.GetMouseButtonUp(0))
            {
                return;
            }

            _placementDown = false;

            if (((Vector2)Input.mousePosition - _placementDownScreen).magnitude > placementClickMaxPixels)
            {
                return;   // 拖动手势 ⇒ 不是放球
            }

            if (aimCamera == null || playfieldRoot == null)
            {
                return;
            }

            if (!PointerAimSolver.TryPickPhysicsPoint(aimCamera, playfieldRoot, Input.mousePosition,
                    out Vector2 placementPhysics))
            {
                return;
            }

            drive.TryPlaceCueBall(placementPhysics);
        }
    }
}
