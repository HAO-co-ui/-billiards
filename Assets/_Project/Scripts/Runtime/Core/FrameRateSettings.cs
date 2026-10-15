using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// **帧率策略的唯一真源**（目标平台的固定件，不是可调项）：
    /// 垂直同步 **开** + 目标帧率 **60**。
    ///
    /// ── 为什么两者都要设 ─────────────────────────────────────────────────
    /// · **垂直同步**（`vSyncCount = 1`）：消除撕裂，并把帧率与显示刷新对齐 —— 这是"**稳定**"的来源；
    /// · **目标帧率**（`targetFrameRate = 60`）：**VSync 生效时它被忽略**，仅作兜底
    ///   （驱动/环境禁用了 VSync 时仍能压住不必要的高帧空转，也保证验收口径可复现）。
    ///
    /// ⚠ **语义边界**：本类承诺的是"**目标 ≥ 60 fps 且不掉帧**"，**不**承诺"锁死 60.00 fps"
    /// （在 120/144 Hz 屏上 VSync 会把帧率抬到该刷新率）。实测口径见 `SmokeProbe`。
    ///
    /// ── 为什么用 `[RuntimeInitializeOnLoadMethod]` 而不是往场景挂组件 ────────
    /// 挂在场景里就要依赖"某个组件被建出来/被接线"，任一环节断了就静默失效（本工程踩过：代码生成场景
    /// 缺编辑器默认组件、接线漏挂）。静态入口**不依赖场景内容**，编辑器 Play 与构建产物**同一条路径**。
    /// </summary>
    public static class FrameRateSettings
    {
        /// <summary>目标帧率（fps）。</summary>
        public const int TargetFrameRate = 60;

        /// <summary>垂直同步计数（1 = 每帧同步一次）。</summary>
        public const int VSyncCount = 1;

        /// <summary>把帧率策略应用到运行环境（幂等）。</summary>
        public static void Apply()
        {
            QualitySettings.vSyncCount = VSyncCount;
            Application.targetFrameRate = TargetFrameRate;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Apply();
        }
    }
}
