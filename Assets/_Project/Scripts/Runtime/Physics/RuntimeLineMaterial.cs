using UnityEngine;

namespace Billiards.Physics
{
    /// <summary>
    /// **全场景唯一的运行时线条材质**（`Sprites/Default` 实例）—— 瞄准线与特效线的**共享材质单点**。
    ///
    /// ── 为什么必须单点 ──────────────────────────────────────────────────────────
    /// `SmokeProbe.MaxDistinctMaterialCount = 30` 是**零余量预算**（冒烟实测恰 30/30）：
    /// `SmokeProbeRunner.SampleScenePeaks` 对每个非空 `sharedMaterial` 计一个 distinct 项。
    /// `AimAssistView`（瞄准线）与 `PoolVfxView`（特效线）原先**各自** `new Material(...)`
    /// ⇒ 白占一个额度；而球杆模型接入 `Cue_Pool.mat` 正需要这个额度
    /// ⇒ 两份合并成这一个共享实例后，材质计数**净变化为零**，预算不被破坏。
    ///
    /// ── 边界（改动前先读）────────────────────────────────────────────────────
    /// · 只服务 **LineRenderer**：颜色由各渲染器的顶点色（`startColor/endColor`）承载，
    ///   本材质不承担逐视图参数差异；
    /// · **不要**在任一处对 <see cref="Shared"/> 调 `SetFloat/SetColor/SetTexture` —— 会串到另一处视图；
    ///   若将来确有参数分档需求，应在**本类**开口子（按档缓存实例）而不是在调用方直接改；
    /// · 预算真源是 `SmokeProbe.MaxDistinctMaterialCount`；若将来预算上调，本类仍应保留（省一份材质实例）。
    /// </summary>
    public static class RuntimeLineMaterial
    {
        private static Material _shared;

        /// <summary>共享材质实例（首次访问时创建；shader 缺失时返回 <c>null</c>，调用方各自跳过赋值）。</summary>
        public static Material Shared
        {
            get
            {
                if (_shared != null)
                {
                    return _shared;
                }

                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    return null;
                }

                _shared = new Material(shader)
                {
                    name = "RuntimeLine (Sprites/Default)",
                };
                return _shared;
            }
        }

        /// <summary>
        /// 进入播放模式 / 域重载后清掉静态引用（静态状态不得跨会话残留；见工程纪律「静态状态重置」）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _shared = null;
        }
    }
}
