using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **视口适配**：`16:9` 基准 + 窄窗回落，**保持水平 FOV 恒定**。
    ///
    /// ── 规则（唯一落点）──────────────────────────────────────────────────────
    /// <code>
    /// tanHalfH = tan(baseVerticalFovDeg/2) · baseAspect      // 16:9 基准的水平半角正切
    /// aspect ≥ baseAspect ⇒ vFov = baseVerticalFovDeg        // 更宽 ⇒ 不动（显示更多，不裁切）
    /// aspect &lt; baseAspect ⇒ vFov = 2·atan(tanHalfH/aspect)   // 更窄 ⇒ 抬垂直 FOV，保住水平覆盖
    /// vFov = min(vFov, maxVerticalFovDeg)                    // 兜底，防逼近 180°
    /// </code>
    /// **为什么这样就"窄窗不裁切"**：水平覆盖由 `tanHalfH` **恒定**决定 ⇒ 只要 16:9 基准放得下，
    /// 任何**更窄**的窗口水平方向必定仍放得下 ⇒ 不裁切是**结构保证**，不是调参调出来的。
    ///
    /// ── 与 `PhysicsTestDrive` 的边界 ──────────────────────────────────────────
    /// 本类只写 `Camera.fieldOfView`（**视觉**），**不碰**任何物理量；`PoolSimulator` 无任何 `Camera/Screen` 依赖。
    ///
    /// ── 盲带 ─────────────────────────────────────────────────────────────────
    /// · **不判**"球桌是否太小看不清"（排期只要求"不裁切"）。
    /// · 低于 <see cref="minSupportedAspect"/> 的比例**超出支持域**（`tanHalfH` 仍要保住 ⇒ 需要更大的垂直 FOV；
    ///   一旦被 <see cref="maxVerticalFovDeg"/> 截断，水平覆盖就会缩小）⇒ **声明降级**，本单不做 letterbox。
    /// · 触发只覆盖 `OnEnable` + `aspect` 变化；**不覆盖**运行时相机位姿被改动的情形。
    /// </summary>
    [AddComponentMenu("Billiards/Camera Viewport Fitter")]
    public sealed class CameraViewportFitter : MonoBehaviour
    {
        /// <summary>目标相机。为空 ⇒ <see cref="Apply"/> 退化为 no-op。</summary>
        [Tooltip("目标相机。为空 ⇒ 本组件不生效。")]
        public Camera targetCamera;

        /// <summary>基准宽高比（16:9）。本条即"基准档"。 </summary>
        [Tooltip("基准宽高比（16:9）。此比例下适配器为 no-op。")]
        public float baseAspect = 16f / 9f;

        /// <summary>
        /// 基准垂直 FOV（度）。**必须等于场景序列化 FOV**（`T53` 守）。
        ///
        /// ⚠ **实测定值（本单）**：spec §4.1 假定"= 场景现值 `40`"，但**实测 40 装不下内库四角**：
        /// 灰盒内库四角（物理 `(±1.27, ±0.635)` m）经 `×1.2` 显示缩放后投到
        /// `viewport.x ∈ [−0.103, 1.103]`、`viewport.y_min = −0.044`（要 `[0.02, 0.98]`）。
        /// 根因：**切片 A 的 1.2 显示缩放**把桌子推出 Day 3 相机的水平视锥（scale 1.0 时该角约 `32.4°`，
        /// 基准水平半角 `32.9°` —— 本就只剩丝毫余量；×1.2 后 `38.0°` > `32.9°` ⇒ 溢出）。
        /// **实测最小可行基准 FOV ≈ 50°**（`dev = max|vport − 0.5| ≤ 0.48` 的门限）；
        /// 本处取 **52°**（留约 0.03 的余量）。⇒ 场景相机 FOV 同步 `40 → 52`（由 `GreyboxSceneSetup` 生成）。
        /// </summary>
        [Tooltip("基准垂直 FOV（度），= 场景序列化 FOV。")]
        public float baseVerticalFovDeg = 52f;

        /// <summary>
        /// **支持域下界**（宽高比）。低于此值 ⇒ 超域降级（**不保证不裁切**）。
        /// 取 spec §5 的宽高比集合中最窄的一档 `3/4`（= 0.75）。
        /// </summary>
        [Tooltip("支持域下界（宽高比）。低于此值超出支持域，不保证不裁切。")]
        public float minSupportedAspect = 3f / 4f;

        /// <summary>
        /// 垂直 FOV 兜底上界（度）。**防窄窗把垂直 FOV 抬到逼近 180°**（畸变 / 近裁）。
        ///
        /// ⚠ **实测定值（本单）**：spec §4.1 的"乙-系数"假定 `60`，但**实测 60 与最窄档 `3/4` 不相容**：
        /// 在"保持水平 FOV"规则下，`aspect = 3/4` 要保住同样的水平覆盖，**本质需要 ≈ 98° 垂直 FOV**
        /// （`2·atan(tan(半个水平角)/0.75)`）—— 这是几何决定的，与实现无关。
        /// 实测：`cap = 90` ⇒ `3/4` 档 `dev = 0.520 > 0.48`（裁切）；`cap = 96` ⇒ 全 6 档 `dev ≤ 0.471`（通过）。
        /// ⇒ 本处取 **100°**（> 所需的 ≈98.2°，留余量）。
        /// **代价（如实记录）**：`3/4` 档成广角/鱼眼观感；若不可接受，替代方案是**把相机后撤**
        /// （实测：维持 `40°/60°` 需后撤约 `1.88×`）—— 那属**相机取景**变更，不在本单范围，留待裁定。
        /// </summary>
        [Tooltip("垂直 FOV 兜底上界（度）。")]
        public float maxVerticalFovDeg = 100f;

        private float _lastAspect = -1f;

        /// <summary>
        /// **纯函数**：给定宽高比与基准参数，求该应用的垂直 FOV（度）。
        /// 判据直接驱动本函数，不必真的渲染。
        /// </summary>
        public static float ResolveVerticalFovDeg(float aspect, float baseAspect, float baseVerticalFovDeg, float maxVerticalFovDeg)
        {
            if (!(aspect > 0f) || !(baseAspect > 0f) || !(maxVerticalFovDeg > 0f))
            {
                return baseVerticalFovDeg;   // 退化：给不出比例 ⇒ 用基准（不抛异常）
            }

            if (aspect >= baseAspect)
            {
                return Mathf.Min(baseVerticalFovDeg, maxVerticalFovDeg);   // 更宽 ⇒ 不动
            }

            float tanHalfH = Mathf.Tan(baseVerticalFovDeg * 0.5f * Mathf.Deg2Rad) * baseAspect;
            float narrow = 2f * Mathf.Atan(tanHalfH / aspect) * Mathf.Rad2Deg;
            return Mathf.Min(narrow, maxVerticalFovDeg);                   // 兜底
        }

        private void OnEnable()
        {
            _lastAspect = -1f;
            Apply();
        }

        private void Update()
        {
            // 只在宽高比变化时重算（避免每帧写 fieldOfView 造成无谓的脏标记）。
            float aspect = CurrentAspect();
            if (!Mathf.Approximately(aspect, _lastAspect))
            {
                Apply();
            }
        }

        /// <summary>按当前宽高比应用适配（幂等）。</summary>
        public void Apply()
        {
            if (targetCamera == null)
            {
                return;
            }

            float aspect = CurrentAspect();
            _lastAspect = aspect;
            targetCamera.fieldOfView = ResolveVerticalFovDeg(aspect, baseAspect, baseVerticalFovDeg, maxVerticalFovDeg);
        }

        private float CurrentAspect()
        {
            // 相机自身 aspect 已由引擎按渲染目标维护；退化（为 0）时用 Screen 兜底。
            float aspect = targetCamera != null ? targetCamera.aspect : 0f;
            if (!(aspect > 0f))
            {
                aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : baseAspect;
            }

            return aspect;
        }
    }
}
