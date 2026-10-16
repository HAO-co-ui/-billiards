using Billiards.Core;
using UnityEngine;

namespace Billiards.UI
{
    /// <summary>
    /// **按"首页是否打开"切换相机预设**的薄组件 —— 用户反馈"点开始游戏就切换为上帝视角"的落点。
    ///
    /// ── 它做什么（就这一件事）────────────────────────────────────────────────
    /// 每帧读 <see cref="HomeView.IsOpen"/>（**纯状态，静态属性**）⇒ 经
    /// <see cref="CameraPresets.Resolve"/> 得到该用的预设 ⇒ 把**同一台相机**摆到那套位姿。
    /// 首页开 ⇒ `Home`（看得见球房）；首页关（= 点了 `Practice` 开始打球）⇒ `Play`（上帝视角）。
    ///
    /// ── 转场（用户第二条反馈："视角转场要丝滑"）──────────────────────────────────
    /// 预设**变化**时不再硬切，而是在 <see cref="transitionSeconds"/> 内按
    /// <see cref="CameraTransition"/>（**smoothstep**，两端速度为 0）插值位置 / 注视点 / fov。
    /// · **终态精确**：`t = 1` 时用**与直接施加预设同一行代码**（<see cref="CameraPresets.ApplyPose"/>）
    ///   落到目标值 ⇒ 逐位等于真源常量（`CAM-01..05` 判的是**稳态**）。
    /// · **首帧硬切**、**状态未变不动画**（见 <see cref="CameraTransition.ShouldAnimate"/>）
    ///   ⇒ 该两条合起来保证**冒烟全程不做过渡**（无人操作 + 首页被抑制 ⇒ 预设恒为 play ⇒ `from == to`）。
    /// · 本卡只接"**首页 → 打球**"这一条方向；机制本身对双向对称（`Home` ⟷ `Play` 都能插）。
    ///
    /// ── 依赖方向（单向，不许回环）────────────────────────────────────────────
    /// 本组件在 `Billiards.Runtime` 的 `Runtime/UI`，`HomeView` 在同一个程序集 ⇒ **读它的静态状态是允许的**
    /// （既有先例：`PointerAimController` / `PointerPowerController` / `AimAssistController` 三处都读
    /// `HomeView.IsOpen`）。**反向禁止**：不让 `HomeView` 去拿相机 —— 保持"UI 报状态、相机自己跟随"。
    ///
    /// ── 为什么是"同一台相机换姿态"，不是第二台相机 ────────────────────────────────
    /// `T127` 断言全场景**恰 1 台**相机 / 恰 1 个 `AudioListener`；加第二台相机会直接破判据，
    /// 而且会把 `CameraViewportFitter`（视口适配）与 `AudioListener` 的归属搞乱。
    ///
    /// ── 与 `CameraViewportFitter` 的协同（**必读，否则 fov 会被静默改回**）────────────
    /// 适配器只在 `OnEnable` 与**宽高比变化**时写 `Camera.fieldOfView`，取值由它自己的
    /// `baseVerticalFovDeg` 决定。若切换预设时**只改相机 fov、不改适配器的 base**，
    /// 那么窗口比例一变，适配器就会用**旧基准**把 fov 覆写回去（"看着像生效了、换窗口就弹回"）。
    /// ⇒ 该协同**已收进唯一实现点** <see cref="CameraPresets.ApplyPose"/>（本组件的每一帧都调它），
    ///   本组件自身**只负责"决定用哪套 / 插到哪一刻"**，不重复实现"怎么施加"。
    /// </summary>
    [AddComponentMenu("Billiards/Play View Camera")]
    public sealed class PlayViewCamera : MonoBehaviour
    {
        [Tooltip("目标相机。为空 ⇒ 取同物体上的 Camera（生成器就是这么接的）。")]
        public Camera targetCamera;

        /// <summary>
        /// 转场时长（秒）。默认 <see cref="CameraTransition.DefaultSeconds"/> = 0.45 s。
        /// 备选档 <see cref="CameraTransition.AlternativeSeconds"/> = 0.55 s（更慢镜）。
        /// </summary>
        [Tooltip("转场时长（秒）。0.45 = 默认档；0.55 = 备选（更慢镜）。")]
        public float transitionSeconds = CameraTransition.DefaultSeconds;

        /// <summary>当前是否正在做转场（只读出口；供诊断 / 定点报告）。</summary>
        public bool IsTransitioning { get; private set; }

        /// <summary>
        /// **状态来源接缝**：非 `null` 时用它替代 <see cref="HomeView.IsOpen"/>；`null` = 产品路径（读真实首页状态）。
        ///
        /// 为什么需要它（不是为了测试而开口子，有两处真实用途）：
        /// · **判据要确定性驱动状态机** —— `HomeView.IsOpen` 是 `{ get; private set; }`，**测试改不动**；
        ///   没有这个接缝，`T136` 就只能测纯函数、测不到"转场真的会起、且终态精确"。
        /// · **没有首页的场景**（纯物理诊断态）能显式固定为打法视角。
        /// </summary>
        public System.Func<bool> homeOpenSource;

        private bool _hasApplied;
        private CameraPresets.Preset _applied;

        // 转场起点（= 开始那一帧相机的**实际**位姿）与终点（= 目标预设）
        private Vector3 _fromPosition;
        private Vector3 _fromLookTarget;
        private float _fromFovDeg;
        private Vector3 _toPosition;
        private Vector3 _toLookTarget;
        private float _toFovDeg;
        private float _progressSeconds;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            _hasApplied = false;
            IsTransitioning = false;
        }

        /// <summary>
        /// 在 `Start` 再应用一次：`OnEnable` 的**组件间顺序不确定**，而适配器也在 `OnEnable` 里写 fov
        /// ⇒ 用它兜底，保证"启动时"一定是本组件最后说话（且**首帧是硬切**，不做动画）。
        /// </summary>
        private void Start()
        {
            _hasApplied = false;
            Apply(0f);
        }

        private void LateUpdate()
        {
            Apply(Time.deltaTime);
        }

        /// <summary>
        /// 推进状态机：**首帧 / 状态未变 ⇒ 硬切；状态变了 ⇒ 起一段转场**；转场中则推进插值。
        /// `deltaSeconds` 由调用方给（`LateUpdate` 传 `Time.deltaTime`；`Start` / 测试传 0）。
        /// </summary>
        public void Apply(float deltaSeconds)
        {
            if (targetCamera == null)
            {
                return;
            }

            bool homeOpen = homeOpenSource != null ? homeOpenSource() : HomeView.IsOpen;
            CameraPresets.Preset want = CameraPresets.Resolve(homeOpen);

            if (!_hasApplied)
            {
                // 首帧：**硬切到目标**（当前姿态是场景序列化态，不是某套预设，不能作为插值起点）
                ApplyPreset(want);
                return;
            }

            if (IsTransitioning)
            {
                Tick(deltaSeconds, want);
                return;
            }

            if (want != _applied)
            {
                BeginTransition(want);
            }
        }

        /// <summary>起一段转场：起点 = **当前相机的实际位姿**（不是"上一次的预设" —— 若上一段没走完就被打断，这里才接得上），终点 = 目标预设。</summary>
        private void BeginTransition(CameraPresets.Preset target)
        {
            Transform t = targetCamera.transform;
            _fromPosition = t.position;
            // 以当前朝向反推一个注视点（沿 forward 走 1 m）作为插值起点 —— 保证"起点姿态 == 当前姿态"
            _fromLookTarget = t.position + t.forward;
            _fromFovDeg = _fitterFovDeg();

            CameraPresets.GetPose(target, out _toPosition, out _toLookTarget, out _toFovDeg);
            _progressSeconds = 0f;
            IsTransitioning = true;

            // 立刻按 t≈0 施加一次（与起点一致），避免"起帧仍停在旧姿态"的观感跳变
            CameraPresets.ApplyPose(targetCamera, _fromPosition, _fromLookTarget, _fromFovDeg);
        }

        private void Tick(float deltaSeconds, CameraPresets.Preset want)
        {
            // 若转场途中目标又变了（本卡只有一条方向，但机制上要稳）⇒ 以**当前位姿**为起点重起一段
            if (want != _applied && deltaSeconds > 0f)
            {
                // 目标已不是这段的终点 ⇒ 重取终点（起点保持"当前实际位姿"，由 BeginTransition 负责）
                CameraPresets.GetPose(want, out Vector3 np, out Vector3 nl, out float nf);
                if (np != _toPosition || nl != _toLookTarget || !Mathf.Approximately(nf, _toFovDeg))
                {
                    Transform t = targetCamera.transform;
                    _fromPosition = t.position;
                    _fromLookTarget = t.position + t.forward;
                    _fromFovDeg = _fitterFovDeg();
                    _toPosition = np;
                    _toLookTarget = nl;
                    _toFovDeg = nf;
                    _progressSeconds = 0f;
                }
            }

            _progressSeconds += deltaSeconds;
            float duration = transitionSeconds > 1e-4f ? transitionSeconds : 1e-4f;
            float t01 = _progressSeconds / duration;

            CameraTransition.Interpolate(
                _fromPosition, _fromLookTarget, _fromFovDeg,
                _toPosition, _toLookTarget, _toFovDeg,
                t01,
                out Vector3 pos, out Vector3 look, out float fov);

            if (t01 >= 1f)
            {
                // ⚠ **终态走"直接施加预设"这条路**（而不是插值结果）⇒ 与稳态逐位一致
                IsTransitioning = false;
                ApplyPreset(want);
                return;
            }

            CameraPresets.ApplyPose(targetCamera, pos, look, fov);
        }

        /// <summary>硬切到某套预设（首帧 / 转场终点）。</summary>
        public void ApplyPreset(CameraPresets.Preset preset)
        {
            if (targetCamera == null)
            {
                return;
            }

            CameraPresets.ApplyTo(targetCamera, preset);
            _applied = preset;
            _hasApplied = true;
            IsTransitioning = false;
        }

        /// <summary>取当前**适配器基准**（= 该用的基准 fov；没有适配器就用相机 fov）。</summary>
        private float _fitterFovDeg()
        {
            var fitter = targetCamera.GetComponent<Billiards.Physics.CameraViewportFitter>();
            return fitter != null ? fitter.baseVerticalFovDeg : targetCamera.fieldOfView;
        }
    }
}
