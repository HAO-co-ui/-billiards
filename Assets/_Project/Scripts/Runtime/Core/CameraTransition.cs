using UnityEngine;

namespace Billiards.Core
{
    /// <summary>
    /// **取景切换的过渡**（位置 / 注视点 / fov 同步插值，**smoothstep** 缓动）—— 全为**纯函数**。
    ///
    /// 用户实机反馈：「开始游戏的视角转场要"丝滑"」——此前 `PlayViewCamera` 是**硬切**（一帧跳变）。
    ///
    /// ── 为什么插值"位置 + 注视点"而不是"位置 + 四元数" ──────────────────────────────
    /// `CameraPresets.ApplyPose` 的终态是 `rotation = LookRotation(lookTarget − position)`。
    /// 若插值四元数再还原朝向，**终态会有浮点残差**；而插值 `(position, lookTarget, fov)` 后
    /// **用同一个 `LookRotation` 求朝向** ⇒ 终态**逐位等于**真源预设（构造上成立，不靠容差）。
    /// 这正是硬要求「终态必须精确落在预设值上」的最省事实现（`CAM-01..05` 判的是**稳态**）。
    ///
    /// ── 缓动为什么选 smoothstep ────────────────────────────────────────────────
    /// `w(t) = t²(3 − 2t)`：`w(0)=0`、`w(1)=1`、`w'(0)=w'(1)=0` ⇒ **两端速度为 0**，
    /// 起步与收尾都不突兀（不会"猛地一窜 / 猛地一顿"）；且在 `[0,1]` 上**单调**⇒ 不回头、不越界。
    /// </summary>
    public static class CameraTransition
    {
        /// <summary>过渡时长（秒）—— **默认档**。0.9 s：约 54 帧 @60 fps。</summary>
        /// <remarks>
        /// ⚠ **用户实机反馈"太快"⇒ 0.45 → 0.9**（原 0.45 s 只是我按"不突兀"选的经验值，用户实机觉得太仓促）。
        /// 键帧表已按新时长重算（见 `Docs/35-A_球房环境_spec.md` §13.1）。
        /// </remarks>
        public const float DefaultSeconds = 0.9f;

        /// <summary>过渡时长（秒）—— **备选档**。0.55 s：更利落一点，约 33 帧 @60 fps。</summary>
        public const float AlternativeSeconds = 0.55f;

        /// <summary>
        /// **"转场正在进行"的中性状态** —— 由**相机侧发布**（`Billiards.UI.PlayViewCamera` 是唯一写入者）、
        /// 由**瞄准/球杆侧读取**（照 <see cref="Billiards.UI.HomeView.IsOpen"/> 的既有范式：**同程序集、单向**）。
        ///
        /// **为什么需要它**（用户实机反馈："转场过程中**瞄准线会出来乱转**"）：
        /// 瞄准线/幽灵球/球杆都是**相机相关的投影**，相机在转场中会扫过一段路径 ⇒
        /// 这些表现若不门控，就会跟着扫（用户看到的"乱转"）。根因是**同一个**（相机在动），
        /// 所以读它的地方要一起门控（`AimAssistController` / `PointerAimController` / `CueStickController`）。
        ///
        /// ⚠ **门必须在 `t ≥ 1` 才放开**（`PlayViewCamera` 在**落终态**那一刻才置 `false`），
        /// 否则最后几帧又会闪一下（"提前恢复"是这类门最典型的漏法）。
        /// </summary>
        public static bool IsActive { get; private set; }

        /// <summary>发布"转场是否进行中"。**唯一写入者 = <see cref="Billiards.UI.PlayViewCamera"/>**（不要在别处写）。</summary>
        public static void Publish(bool active)
        {
            IsActive = active;
        }

        /// <summary>**纯函数**：进度 `t`（0..1，越界会被 clamp）⇒ 插值权重。smoothstep：两端速度为 0。</summary>
        public static float Weight(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// **纯函数**：给定起止位姿与进度 `t` ⇒ 当前位姿。
        ///
        /// ⚠ **端点精确**：`t ≤ 0` 原样返回起点、`t ≥ 1` **原样返回终点**（不走插值）⇒
        /// 终态**逐位等于**目标预设，不会有"插值到 99% 就停"的残差。
        /// </summary>
        public static void Interpolate(
            Vector3 fromPosition, Vector3 fromLookTarget, float fromFovDeg,
            Vector3 toPosition, Vector3 toLookTarget, float toFovDeg,
            float t,
            out Vector3 position, out Vector3 lookTarget, out float fovDeg)
        {
            if (t >= 1f)
            {
                position = toPosition;
                lookTarget = toLookTarget;
                fovDeg = toFovDeg;
                return;
            }

            if (t <= 0f)
            {
                position = fromPosition;
                lookTarget = fromLookTarget;
                fovDeg = fromFovDeg;
                return;
            }

            float w = Weight(t);
            position = Vector3.LerpUnclamped(fromPosition, toPosition, w);
            lookTarget = Vector3.LerpUnclamped(fromLookTarget, toLookTarget, w);
            fovDeg = Mathf.LerpUnclamped(fromFovDeg, toFovDeg, w);
        }

        /// <summary>
        /// **纯函数**：这一帧**该不该做过渡**（而不是直接硬切）。
        ///
        /// 返回 `true` 当且仅当：**已经施放过至少一次**（`hasApplied`）**且**预设**真的变了**（`from != to`）。
        ///
        /// 两条都为假的情形**都必须硬切**，各有其原因：
        /// · `hasApplied == false` ⇒ **首帧**：相机当前姿态是"场景序列化态"，不是某套预设，
        ///   从它插值到目标会让**开机第一帧**看起来像"相机从别处飞过来"；且开机应当**立刻**是稳态。
        /// · `from == to` ⇒ **状态没变**：没必要动画。
        /// ⇒ ⚠ **这两条合起来就是"冒烟不做过渡"的证明**：冒烟全程**无人操作**、且首页被抑制
        ///   （`homeStartup=suppressed`）⇒ `HomeView.IsOpen` 恒为 `false` ⇒ 解出的预设**恒为 `play`**
        ///   ⇒ `from == to` 成立 ⇒ **一次过渡都不会发生**（那两档冒烟测的是"无人操作跑 600 帧的帧成本"，
        ///   起始若干帧若被过渡污染，历史基线就不可比）。
        /// </summary>
        public static bool ShouldAnimate(bool hasApplied, CameraPresets.Preset from, CameraPresets.Preset to)
        {
            return hasApplied && from != to;
        }

        /// <summary>
        /// **纯函数**：进度 `t` ⇒ 插值权重随时间的**单调性**自检用的采样序列（供 `T136` 直接断言）。
        /// 返回 `samples` 个等距采样点（含 0 与 1）。
        /// </summary>
        public static float[] SampleWeights(int samples)
        {
            if (samples < 2)
            {
                samples = 2;
            }

            var result = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                result[i] = Weight((float)i / (samples - 1));
            }

            return result;
        }
    }
}
