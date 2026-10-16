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
    /// ── 依赖方向（单向，不许回环）────────────────────────────────────────────
    /// 本组件在 `Billiards.Runtime` 的 `Runtime/UI`，`HomeView` 在同一个程序集 ⇒ **读它的静态状态是允许的**
    /// （既有先例：`PointerAimController` / `PointerPowerController` / `AimAssistController` 三处都读
    /// `HomeView.IsOpen`）。**反向禁止**：不让 `HomeView` 去拿相机 —— 保持"UI 报状态、相机自己跟随"。
    ///
    /// ── 为什么是"同一台相机换姿态"，不是第二台相机 ────────────────────────────────
    /// `T127` 断言全场景**恰 1 台** `AudioListener`/相机口径；加第二台相机会直接破判据，
    /// 而且会把 `CameraViewportFitter`（视口适配）与 `AudioListener` 的归属搞乱。
    ///
    /// ── 与 `CameraViewportFitter` 的协同（**必读，否则 fov 会被静默改回**）────────────
    /// 适配器只在 `OnEnable` 与**宽高比变化**时写 `Camera.fieldOfView`，取值由它自己的
    /// `baseVerticalFovDeg` 决定。若切换预设时**只改相机 fov、不改适配器的 base**，
    /// 那么窗口比例一变，适配器就会用**旧基准**把 fov 覆写回去（"看着像生效了、换窗口就弹回"）。
    /// ⇒ 该协同**已收进唯一实现点** <see cref="CameraPresets.ApplyTo"/>（本组件调它），
    ///   本组件自身**只负责"决定用哪套"**，不重复实现"怎么施加"。
    /// </summary>
    [AddComponentMenu("Billiards/Play View Camera")]
    public sealed class PlayViewCamera : MonoBehaviour
    {
        [Tooltip("目标相机。为空 ⇒ 取同物体上的 Camera（生成器就是这么接的）。")]
        public Camera targetCamera;

        private bool _hasApplied;
        private CameraPresets.Preset _applied;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
            }

            _hasApplied = false;
        }

        /// <summary>
        /// 在 `Start` 再应用一次：`OnEnable` 的**组件间顺序不确定**，而适配器也在 `OnEnable` 里写 fov
        /// ⇒ 用它兜底，保证"启动时"一定是本组件最后说话。
        /// </summary>
        private void Start()
        {
            _hasApplied = false;
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        /// <summary>按当前 `HomeView.IsOpen` 应用预设（**状态未变则 no-op**，不做无谓的每帧写入）。</summary>
        public void Apply()
        {
            if (targetCamera == null)
            {
                return;
            }

            CameraPresets.Preset want = CameraPresets.Resolve(HomeView.IsOpen);
            if (_hasApplied && want == _applied)
            {
                return;
            }

            ApplyPreset(want);
        }

        /// <summary>强制应用某套预设（供测试 / 首帧使用）。实际动作在
        /// <see cref="CameraPresets.ApplyTo"/>（唯一实现点）⇒ 本组件只负责"决定用哪套"。</summary>
        public void ApplyPreset(CameraPresets.Preset preset)
        {
            if (targetCamera == null)
            {
                return;
            }

            CameraPresets.ApplyTo(targetCamera, preset);
            _applied = preset;
            _hasApplied = true;
        }
    }
}
