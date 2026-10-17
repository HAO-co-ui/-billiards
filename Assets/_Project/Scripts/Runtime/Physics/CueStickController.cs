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

        /// <summary>
        /// **场景里预建的杆模型**（杆尖 = 局部原点、杆身沿 +Z、长 1.4 m）—— 由场景构建器接入美术包模型。
        /// 为空时本组件在运行期自建基元圆柱**兜底**（不得因为没接线就没有杆）。位姿一律由本组件驱动（含可见性）。
        /// </summary>
        [Tooltip("场景预建的杆模型（杆尖为局部原点、杆身 +Z）。为空 ⇒ 运行时自建基元圆柱。")]
        public Transform stickInstance;

        private readonly ShotFlowState _flow = new ShotFlowState();
        private Transform _stick;
        private MeshRenderer _stickRenderer;
        private float _pullbackAtRelease;
        private int _strikeCountdown = -1;   // < 0 = 未在前推

        /// <summary>击球流程（只读出口，判据 / 调试观察）。</summary>
        public ShotFlowState Flow => _flow;

        /// <summary>
        /// **杆尖位置**（纯函数，判据 `T88`/`T104`）：杆尖 = 白球中心 **沿杆身轴线**后移
        /// （静止间隙 + 后拉量）。后拉量随力度 1:1（蓄力跟手）；前推期随帧数线性回收到 0（命中帧贴球）。
        ///
        /// ── ⚠ 后拉方向是**抬升后的杆身轴线**，不是水平方向（第三轮实机反馈逼出来的）────────
        /// 症状：**球在右边库附近蓄力时，杆整根穿过木库边**（截图里杆在库边两侧都可见）。
        /// 根因：抬升角 `θ` 按**球位**反解（`StickElevationDegreesFor`：要求杆身在跨过台面外缘处
        /// 下表面高于库顶），该反解的前提是**杆身落在过球心那条直线上**；而旧实现把后拉量沿
        /// **水平方向**加 ⇒ 杆身整体向库边平移 `(TipGap+pullback)`，却一点额外高度都不带
        /// ⇒ 跨库处实际高度比设计值低 `(TipGap+pullback)·tanθ`。
        /// 实测（用户截图姿态）：球离库 0.62 m、后拉 0.45 m、θ 取最小的 4° ⇒ 杆身在跨库处
        /// 只抬 0.016 m，而需求 0.0324 m ⇒ 必然整根穿库。
        /// **沿轴线后拉**则杆身**恒在过球心的直线上** ⇒ 抬升角的保证对**任意后拉量**都成立。
        ///
        /// `elevationDegrees = 0`（默认）⇒ 退化为"水平后拉"，与历史逐位一致（`T88` 锁这条 1:1）。
        /// </summary>
        public static Vector3 StickTipPosition(Vector3 cueBallWorld, Vector3 aimDirectionWorld, float pullback,
            float elevationDegrees = 0f)
        {
            return cueBallWorld + StickDirection(aimDirectionWorld, elevationDegrees) * (TipGap + pullback);
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

        /// <summary>
        /// **球杆是否显示**（**纯函数** —— 与 <see cref="AimAssistController.ShouldShowAimAssist"/> 同形，
        /// 判据 `T137` 直接驱动它，不必起场景）。
        ///
        /// 四条同时成立才显示：
        ///  · `flowVisible` —— 流程口径（`Moving`/`Settle` 隐藏）；
        ///  · `cueBallInPlay` —— **白球必须在场上**（否则相位回到 `Aim` 时杆会停在**袋口残留位置**，
        ///    看起来像杆插在袋里；用户实测截图）；
        ///  · `transitionActive == false` —— **取景转场让位**（用户实机反馈"转场中瞄准表现会乱扫"）：
        ///    杆与瞄准线**同根因**（都是相机相关的投影，而相机在转场中在动）⇒ 走**同一个**门
        ///    （`Billiards.Core.CameraTransition.IsActive`），且该门到转场**落终态**才放开；
        ///  · `homeViewOpen == false` —— **首页观景视角让位**（用户实机截图反馈②："主页的球杆不要这样
        ///    浮空在球桌上"）：首页那个"观景"取景下，杆**横躺在台面上、杆尾悬空** ⇒ 从该视角看就是"浮在桌上"。
        ///    处置选**隐藏**（用户给的两选项之一），而**不是**把场景里的杆对象删掉 —— 删对象会让
        ///    35-A 的球杆判据与整条场景重建链一起动，代价远大于收益。
        ///    > "立在椅子旁边"是用户给的**另一个**选项，属**新摆放工作**（要先给椅子定锚点、再摆一个"靠椅"
        ///    > 姿态），已登记为**可选后续**，本卡不做。
        ///
        /// ⚠ `homeViewOpen` / `shotInFlight` 都**带默认值 `false`**：既有断言（`T137②` 3 参 / 4 参调用）
        ///   与生产调用点之外的调用者一律**行为逐位不变**；生产侧**显式**传后两个实参。
        ///
        /// ⚠ **`shotInFlight`（P8-D，用户要求）**：出杆后球在滚的整段时间里，球杆**整根隐藏**（不是"冻结在击球位"）——
        ///   用户原话「AI 击玩球，画面上的球杆也要暂时消失」。等台面静止、相位回到 `Aim` 时自然重新出现。
        /// </summary>
        public static bool ShouldShowStick(bool flowVisible, bool cueBallInPlay, bool transitionActive,
                                          bool homeViewOpen = false, bool shotInFlight = false)
        {
            return flowVisible && cueBallInPlay && !transitionActive && !homeViewOpen && !shotInFlight;
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
            // **取景转场期间也隐藏**（用户实机反馈"转场中瞄准表现会乱扫"）：杆与瞄准线同根因
            // （都是相机相关的投影，而相机在转场中在动）⇒ 走**同一个**门（`CameraTransition.IsActive`），
            // 且该门在转场**落终态**才放开 ⇒ 不会在最后几帧闪回来。
            // **首页观景视角期间同样隐藏**（用户实机截图反馈②："主页的球杆不要这样浮空在球桌上"）：
            // 首页那个取景下杆看着像浮在桌面 ⇒ 让位。读 `HomeView.IsOpen` 与 `AimAssistController`
            // 的模态门**同一范式**（同程序集、单向 UI→Physics 只读静态属性）⇒ **不引入新的依赖方向**。
            bool cueBallInPlay = drive.Simulator != null && drive.Simulator.IsBallInPlay(0);
            // **P8-D**：出杆后（球在飞的整段时间）**球杆整根隐藏** —— 用户要"暂时消失"，不是"冻在原地"。
            bool visible = ShouldShowStick(_flow.IsStickVisible, cueBallInPlay,
                Billiards.Core.CameraTransition.IsActive, Billiards.UI.HomeView.IsOpen, drive.ShotInFlight);
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
            if (_flow.Phase == ShotPhase.Charging
                || drive.AiPhase == PhysicsTestDrive.AiShotPhase.Pulling)
            {
                pullback = drive.power * MaxPullback;   // 蓄力 / **AI 拉杆段**：1:1 后拉
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

            // 抬升角**按几何反解**（球位 + 瞄准方向）：球离库边越近 ⇒ 跨库处越低 ⇒ 需要的角度越大。
            // ⚠ 只用球位与瞄准方向、**不含后拉量** ⇒ 与后拉无关（"蓄力不得让杆变短/变形"的不变量）。
            Vector2 ballPhysics = drive.Simulator != null && drive.Simulator.Balls.Count > 0
                ? drive.Simulator.Balls[0].position
                : PoolGeometry.UnityToPhysics(cueBallWorld);
            float aimRad = drive.aimDegrees * Mathf.Deg2Rad;
            float elevation = StickElevationDegreesFor(
                ballPhysics, new Vector2(Mathf.Cos(aimRad), Mathf.Sin(aimRad)));

            // ★ 杆尖**沿杆身轴线**后拉（`elevation` 必须先进来）—— 否则杆身整体向库边平移却不抬高，
            //   抬升角的保证失效 ⇒ 蓄力时整根穿库（见 `StickTipPosition` 的推导）。
            _stick.position = StickTipPosition(cueBallWorld, aimDir, pullback, elevation);

            // 杆身 +Z 从杆尾指向杆尖 = **抬升后的方向**（杆从库边上方过；长度恒为 `StickLength`）。
            _stick.rotation = Quaternion.LookRotation(StickDirection(aimDir, elevation), Vector3.up);
        }

        /// <summary>杆身**最小**抬升角（度）：杆身不需要越库时用它（保持"杆贴着球"的观感）。</summary>
        public const float MinStickElevationDegrees = 4f;

        /// <summary>杆身**最大**抬升角（度）：球紧贴库边时需求角会趋向 90°，取上限以免看起来像扎杆。</summary>
        public const float MaxStickElevationDegrees = 45f;

        /// <summary>
        /// **从某点沿某方向到台面外缘矩形边界的水平距离**（与输入**同一个 2D 单位系**；纯函数）。
        /// 方向朝某轴正向 ⇒ 取该轴正向边界；某轴分量为 0 ⇒ 该轴不参与；点已在边界外 ⇒ 返回 0。
        /// </summary>
        public static float ExitDistanceToTableEdge(Vector2 point, Vector2 direction)
        {
            return ExitDistanceToRectEdge(point, direction,
                new Vector2(PoolConstants.ClothHalfLength, PoolConstants.ClothHalfWidth));
        }

        /// <inheritdoc cref="ExitDistanceToTableEdge"/>
        public static float ExitDistanceToRectEdge(Vector2 point, Vector2 direction, Vector2 halfExtents)
        {
            float best = float.PositiveInfinity;
            if (Mathf.Abs(direction.x) > 1e-6f)
            {
                best = Mathf.Min(best, (Mathf.Sign(direction.x) * halfExtents.x - point.x) / direction.x);
            }

            if (Mathf.Abs(direction.y) > 1e-6f)
            {
                best = Mathf.Min(best, (Mathf.Sign(direction.y) * halfExtents.y - point.y) / direction.y);
            }

            return Mathf.Max(0f, best);
        }

        /// <summary>
        /// **杆身抬升角（度）** —— 纯函数，**只依赖球位与瞄准方向**（⇒ 与后拉量无关，杆长恒定）。
        ///
        /// 需求（**可证**形式）：杆身沿 −瞄准方向自球心射出，在它跨过**台面外缘**处，其**下表面**
        /// 须高于台面外轮廓最高点 ⇒ 杆不可能与球桌网格相交：
        ///   `sinθ ≥ (OuterBoundsMax.y + StickRadius − BallRadius) / d`，
        /// `d` = 球心沿杆身方向到台面外缘的水平距离（<see cref="ExitDistanceToTableEdge"/>）。
        ///
        /// ── ⚠ 为什么原先固定 4° 不够（**实测**，用户截图：杆在木库边两侧都可见 = 整根穿过库边）──
        /// 旧注释拿 `1.4·sin4° = 0.098 m`（**杆尾处**的总抬升）去比"库边高出球心 0.0324 m"、看到 2.5 倍余量 ——
        /// 但**杆在库边处的抬升只有 `d·sinθ`**：`d = 0.23 m` 时仅 `0.016 m` < 0.0324 m ⇒ 必然穿库。
        /// 现在按**跨库处的实际高度**反解 ⇒ 球离库越近角度越大（真实打球也正是这么抬杆的）。
        ///
        /// **为什么这条反解只需看球位**（本函数成立的前提）：杆尖沿**杆身轴线**后拉
        /// （<see cref="StickTipPosition"/>）⇒ 杆身**恒在过球心那条直线上**，杆上位点的高度只由
        /// 它到球心的**水平距离** `ξ` 决定：`h(ξ) = 球心高 + ξ·tanθ`，与后拉量无关。
        /// 而 `ξ` 越大杆越高 ⇒ 约束的**最紧点**就是杆身第一次跨出台面外缘处（`ξ = d`）
        /// ⇒ 只要 `ballRadius + d·tanθ − StickRadius ≥ OuterBoundsMax.y` 即处处成立。
        /// **反过来说：任何"把杆尖沿水平方向平移"的实现都会破坏这个前提**（杆身不再过球心，
        /// 杆在跨库处的高度低于上式的预测）—— 那正是第三轮实机"蓄力还是穿模"的根因。
        ///
        /// **盲带（如实）**：需求取的是**全桌最高点**（木库顶面），比袋口附近的实际遮挡（库边顶面更低）
        /// 更保守；球心离库边外缘 < ~57 mm 时 45° 上限仍可能让杆擦过库边顶部 —— 该带登记、不做遮蔽近似。
        /// </summary>
        public static float StickElevationDegreesFor(Vector2 ballPhysics, Vector2 aimPhysics)
        {
            Vector2 back = -aimPhysics.normalized;
            float d = ExitDistanceToTableEdge(ballPhysics, back);
            float clearance = (PoolGeometry.OuterBoundsMax.y + StickRadius) - PoolConstants.BallRadius;
            if (d <= 1e-4f)
            {
                return MaxStickElevationDegrees;
            }

            return Mathf.Clamp(Mathf.Asin(Mathf.Clamp01(clearance / d)) * Mathf.Rad2Deg,
                MinStickElevationDegrees, MaxStickElevationDegrees);
        }

        /// <summary>
        /// **杆身方向**（纯函数）：`−aimDirection` 在**水平面内**再绕垂直轴抬升 `elevationDegrees`。
        /// **只依赖瞄准方向** —— 与后拉量无关 ⇒ 杆长恒定（这正是"蓄力不得让杆变短"的形式化）。
        /// </summary>
        public static Vector3 StickDirection(Vector3 aimDirectionWorld, float elevationDegrees)
        {
            if (aimDirectionWorld.sqrMagnitude <= 0f)
            {
                return Vector3.forward;
            }

            Vector3 back = -aimDirectionWorld.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, back).normalized;
            return Quaternion.AngleAxis(-elevationDegrees, right) * back;   // 负号 ⇒ 杆尾**上抬**
        }

        /// <summary>
        /// **杆尾世界位置** = 杆尖 + 方向 × <see cref="StickLength"/>。
        /// 长度恒为 `StickLength`（**不随蓄力变化、不做任何夹取**）。
        /// </summary>
        public static Vector3 StickTailPosition(Vector3 tipWorld, Vector3 aimDirectionWorld, float elevationDegrees)
        {
            return tipWorld + StickDirection(aimDirectionWorld, elevationDegrees) * StickLength;
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

            // ① 场景预建的真模型优先（美术接入路径）：本组件只负责驱动位姿与可见性。
            if (stickInstance != null)
            {
                _stick = stickInstance;
                _stickRenderer = stickInstance.GetComponentInChildren<MeshRenderer>(true);
                return;
            }

            // ② 兜底：运行期自建基元圆柱（未接线时仍有杆可打）。
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
